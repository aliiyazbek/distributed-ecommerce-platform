require('dotenv').config();
const grpc = require('@grpc/grpc-js');
const protoLoader = require('@grpc/proto-loader');
const { Pool } = require('pg');
const path = require('path');

// 1. Setup Database Connection Pool
// PostgreSQL accepts 100 connections in total, shared with the API, the email worker and pgAdmin, so the
// pool is capped (DB_POOL_MAX, default 20). Under a spike, requests wait at most DB_POOL_WAIT_MS for a free
// connection and then fail cleanly instead of piling up until the caller's 2 s gRPC deadline.
const pool = new Pool({
    user: process.env.DB_USER,
    host: process.env.DB_HOST,
    database: process.env.DB_NAME,
    password: process.env.DB_PASSWORD,
    port: process.env.DB_PORT,
    max: Number(process.env.DB_POOL_MAX || 20),
    connectionTimeoutMillis: Number(process.env.DB_POOL_WAIT_MS || 1500),
    idleTimeoutMillis: 30000,
});

// An idle client can be dropped by the server (e.g. Postgres restart). Without this handler the
// 'error' event would crash the whole process.
pool.on('error', err => console.error('[pg] idle client error:', err.message));

// 2. Auto-initialize Database Tables
async function initDb() {
    const client = await pool.connect();
    try {
        await client.query(`
            CREATE SCHEMA IF NOT EXISTS inventory;
            CREATE TABLE IF NOT EXISTS inventory.stock (
                product_id UUID PRIMARY KEY,
                available_quantity INT NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS inventory.reservations (
                order_id UUID,
                product_id UUID,
                amount INT NOT NULL,
                PRIMARY KEY (order_id, product_id)
            );
        `);
        console.log("Inventory database tables verified.");
    } finally {
        client.release();
    }
}

// 3. Load gRPC Contract
const PROTO_PATH = path.resolve(__dirname, '../Protos/inventory.proto');
const packageDefinition = protoLoader.loadSync(PROTO_PATH, {
    keepCase: true, longs: String, enums: String, defaults: true, oneofs: true,
});
const inventoryProto = grpc.loadPackageDefinition(packageDefinition).inventory;

// 4. Implement Contract Methods
async function checkQuantity(call, callback) {
    const { ProductId } = call.request;
    try {
        const res = await pool.query('SELECT available_quantity FROM inventory.stock WHERE product_id = $1', [ProductId]);
        const qty = res.rows.length > 0 ? res.rows[0].available_quantity : 0;
        callback(null, { AvailableQuantity: qty });
    } catch (err) {
        callback({ code: grpc.status.INTERNAL, details: err.message });
    }
}

// Batched stock lookup: one query for a whole page of products (avoids N+1).
// Products without a stock row are returned with 0.
async function checkQuantities(call, callback) {
    const ids = [...new Set(call.request.ProductIds || [])];
    if (ids.length === 0) return callback(null, { Items: [] });

    try {
        const res = await pool.query(
            'SELECT product_id, available_quantity FROM inventory.stock WHERE product_id = ANY($1::uuid[])',
            [ids]
        );
        const found = new Map(res.rows.map(r => [r.product_id, r.available_quantity]));
        callback(null, {
            Items: ids.map(id => ({ ProductId: id, AvailableQuantity: found.get(id.toLowerCase()) ?? 0 }))
        });
    } catch (err) {
        callback({ code: grpc.status.INTERNAL, details: err.message });
    }
}

// Reserves ALL lines of an order atomically: either every line is reserved or none is.
//
// One SQL statement (data-modifying CTEs) instead of BEGIN / SELECT ... FOR UPDATE / UPDATE / INSERT / COMMIT:
// the stock rows stay locked only while this single statement runs (about a millisecond) instead of across
// five network round trips. Under a spike on one popular product, that lock time is the bottleneck,
// because every order for that product must wait for the previous one (this is what prevents overselling).
//   req     : the requested lines (duplicates already merged below)
//   already : idempotency - this order was reserved before (retry after a timeout) -> report success
//   locked  : locks the stock rows in product_id order (fixed order = no deadlocks between orders)
//   verdict : all products exist and all have enough stock?
//   upd/ins : apply the reservation only when the verdict is positive (all or nothing)
const RESERVE_SQL = `
WITH req AS (
    SELECT r.product_id, r.amount FROM unnest($2::uuid[], $3::int[]) AS r(product_id, amount)
),
already AS (
    SELECT EXISTS (SELECT 1 FROM inventory.reservations WHERE order_id = $1) AS done
),
locked AS (
    SELECT s.product_id, s.available_quantity
    FROM inventory.stock s
    WHERE s.product_id = ANY($2::uuid[])
    ORDER BY s.product_id
    FOR UPDATE
),
verdict AS (
    SELECT (SELECT done FROM already) AS done,
           COUNT(l.product_id) = (SELECT COUNT(*) FROM req) AS all_found,
           COALESCE(BOOL_AND(l.available_quantity >= req.amount), false) AS all_enough
    FROM req LEFT JOIN locked l USING (product_id)
),
upd AS (
    UPDATE inventory.stock s
    SET available_quantity = s.available_quantity - req.amount
    FROM req, verdict v
    WHERE s.product_id = req.product_id AND NOT v.done AND v.all_found AND v.all_enough
    RETURNING s.product_id
),
ins AS (
    INSERT INTO inventory.reservations (order_id, product_id, amount)
    SELECT $1, req.product_id, req.amount FROM req, verdict v
    WHERE NOT v.done AND v.all_found AND v.all_enough
    RETURNING product_id
)
SELECT v.done, v.all_found, v.all_enough,
       (SELECT COUNT(*) FROM upd)::int AS updated,
       (SELECT COUNT(*) FROM ins)::int AS inserted,
       COALESCE((SELECT json_agg(json_build_object('ProductId', req.product_id, 'Requested', req.amount, 'Available', l.available_quantity))
                 FROM req JOIN locked l USING (product_id) WHERE l.available_quantity < req.amount), '[]'::json) AS shortages,
       COALESCE((SELECT json_agg(req.product_id)
                 FROM req LEFT JOIN locked l USING (product_id) WHERE l.product_id IS NULL), '[]'::json) AS missing
FROM verdict v`;

async function reserveItems(call, callback) {
    const { OrderId } = call.request;

    // Merge duplicate lines for the same product (the reservations PK is (order_id, product_id)).
    const totals = new Map();
    for (const { ProductId, Amount } of call.request.Items || []) {
        if (!(Amount > 0)) {
            return callback({ code: grpc.status.INVALID_ARGUMENT, details: `Invalid amount for product ${ProductId}` });
        }
        const id = ProductId.toLowerCase();
        totals.set(id, (totals.get(id) || 0) + Amount);
    }
    if (totals.size === 0) {
        return callback({ code: grpc.status.INVALID_ARGUMENT, details: 'Order has no items' });
    }
    const productIds = [...totals.keys()];
    const amounts = productIds.map(id => totals.get(id));

    try {
        // pool.query borrows a connection, runs the statement (its own transaction) and returns the connection.
        const { rows: [r] } = await pool.query(RESERVE_SQL, [OrderId, productIds, amounts]);

        if (r.done) {
            return callback(null, { Success: true, Message: 'Already reserved', Shortages: [], MissingProductIds: [] });
        }
        if (r.all_found && r.all_enough) {
            return callback(null, { Success: true, Message: 'Reserved successfully', Shortages: [], MissingProductIds: [] });
        }
        callback(null, {
            Success: false,
            Message: r.missing.length > 0 ? 'Product not found in inventory' : 'Insufficient stock',
            Shortages: r.shortages,
            MissingProductIds: r.missing
        });
    } catch (err) {
        // No free connection in time, Postgres full or down: report it as a gRPC error, never crash the process.
        const busy = /timeout exceeded when trying to connect|too many clients/i.test(err.message || '');
        callback({ code: busy ? grpc.status.UNAVAILABLE : grpc.status.INTERNAL, details: err.message || 'Database error' });
    }
}

// Releases everything reserved for an order in one transaction.
// Idempotent: calling it again (or for an order with no reservations) changes nothing.
async function releaseOrder(call, callback) {
    const { OrderId } = call.request;
    let client;

    try {
        client = await pool.connect(); // inside try: no free connection -> gRPC error, not a crash
        await client.query('BEGIN');

        const reserved = await client.query(
            'SELECT product_id, amount FROM inventory.reservations WHERE order_id = $1 FOR UPDATE',
            [OrderId]
        );

        if (reserved.rows.length > 0) {
            const productIds = reserved.rows.map(r => r.product_id);
            const amounts = reserved.rows.map(r => r.amount);

            // Same lock order as reserveItems to avoid deadlocks
            await client.query(
                'SELECT 1 FROM inventory.stock WHERE product_id = ANY($1::uuid[]) ORDER BY product_id FOR UPDATE',
                [productIds]
            );
            await client.query(
                `UPDATE inventory.stock s
                 SET available_quantity = s.available_quantity + r.amount
                 FROM unnest($1::uuid[], $2::int[]) AS r(product_id, amount)
                 WHERE s.product_id = r.product_id`,
                [productIds, amounts]
            );
            await client.query('DELETE FROM inventory.reservations WHERE order_id = $1', [OrderId]);
        }

        await client.query('COMMIT');
        callback(null, { Success: true, Message: reserved.rows.length > 0 ? 'Released successfully' : 'Nothing to release' });
    } catch (err) {
        if (client) await client.query('ROLLBACK').catch(() => {}); // the connection itself may be broken
        callback({ code: grpc.status.INTERNAL, details: err.message });
    } finally {
        client?.release(); // Prevent connection leaks
    }
}

// 5. Start Server & Graceful Shutdown
const server = new grpc.Server();
server.addService(inventoryProto.InventoryService.service, {
    checkQuantity,
    checkQuantities,
    reserveItems,
    releaseOrder,
    InitializeStock: initializeStock
});

initDb().then(() => {
    const bindAddr = `0.0.0.0:${process.env.GRPC_PORT || 50051}`;
    server.bindAsync(bindAddr, grpc.ServerCredentials.createInsecure(), (err, port) => {
        if (err) {
            // Most common cause: another copy of this service is already running on the port.
            console.error(`Could not bind ${bindAddr}: ${err.message}`);
            console.error('Is another inventory-service already running? Check with: netstat -ano | findstr :50051');
            pool.end(() => process.exit(1));
            return;
        }
        console.log(`Inventory gRPC Service running on port ${port}`);
    });
}).catch(err => {
    // Usually Postgres is not running (docker compose up -d) or the .env DB_* values are wrong.
    console.error(`Could not connect to PostgreSQL at ${process.env.DB_HOST}:${process.env.DB_PORT}: ${err.code || err.message}`);
    console.error('Start the database with: docker compose up -d');
    process.exit(1);
});

// Intercept SIGTERM to drain requests and close pool safely[cite: 3]
process.on('SIGINT', gracefulShutdown);
process.on('SIGTERM', gracefulShutdown);

function gracefulShutdown() {
    console.log('Signal received: closing gRPC server and database pool');
    server.tryShutdown(() => {
        pool.end(() => {
            console.log('PostgreSQL pool drained');
            process.exit(0);
        });
    });
}

async function initializeStock(call, callback) {
    const { ProductId, InitialQuantity } = call.request;
    let client;

    console.log(`[gRPC] InitializeStock received for Product: ${ProductId} with Qty: ${InitialQuantity}`);

    try {
        client = await pool.connect(); // inside try: no free connection -> gRPC error, not a crash
        await client.query('BEGIN');
        
        await client.query(
            'INSERT INTO inventory.stock (product_id, available_quantity) VALUES ($1, $2)',
            [ProductId, InitialQuantity]
        );
        
        await client.query('COMMIT');
        callback(null, { Success: true, Message: "Stock initialized successfully" });
    } catch (err) {
        if (client) await client.query('ROLLBACK').catch(() => {});
        console.error(`[gRPC] InitializeStock error:`, err.message);
        callback({ code: grpc.status.INTERNAL, details: err.message });
    } finally {
        client?.release();
    }
}