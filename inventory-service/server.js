require('dotenv').config();
const grpc = require('@grpc/grpc-js');
const protoLoader = require('@grpc/proto-loader');
const { Pool } = require('pg');
const path = require('path');

// 1. Setup Database Connection Pool
const pool = new Pool({
    user: process.env.DB_USER,
    host: process.env.DB_HOST,
    database: process.env.DB_NAME,
    password: process.env.DB_PASSWORD,
    port: process.env.DB_PORT,
});

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

// Reserves ALL lines of an order in one transaction: either every line is reserved or none is.
// A constant number of SQL statements, whatever the number of lines.
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

    const client = await pool.connect();
    try {
        await client.query('BEGIN');

        // Idempotency: reservations for an order are all-or-nothing, so any existing row means
        // this order was already reserved (e.g. a retry after a timeout).
        const existing = await client.query('SELECT 1 FROM inventory.reservations WHERE order_id = $1 LIMIT 1', [OrderId]);
        if (existing.rows.length > 0) {
            await client.query('COMMIT');
            return callback(null, { Success: true, Message: 'Already reserved', Shortages: [], MissingProductIds: [] });
        }

        // Lock every stock row of the order in a fixed order (by product_id), so two concurrent
        // orders sharing products always lock in the same sequence and cannot deadlock.
        const stockRes = await client.query(
            `SELECT product_id, available_quantity FROM inventory.stock
             WHERE product_id = ANY($1::uuid[])
             ORDER BY product_id
             FOR UPDATE`,
            [productIds]
        );
        const available = new Map(stockRes.rows.map(r => [r.product_id, r.available_quantity]));

        const missing = productIds.filter(id => !available.has(id));
        const shortages = productIds
            .filter(id => available.has(id) && available.get(id) < totals.get(id))
            .map(id => ({ ProductId: id, Requested: totals.get(id), Available: available.get(id) }));

        if (missing.length > 0 || shortages.length > 0) {
            await client.query('ROLLBACK');
            return callback(null, {
                Success: false,
                Message: missing.length > 0 ? 'Product not found in inventory' : 'Insufficient stock',
                Shortages: shortages,
                MissingProductIds: missing
            });
        }

        // One UPDATE and one INSERT for all lines, using unnest() over parallel arrays.
        await client.query(
            `UPDATE inventory.stock s
             SET available_quantity = s.available_quantity - r.amount
             FROM unnest($1::uuid[], $2::int[]) AS r(product_id, amount)
             WHERE s.product_id = r.product_id`,
            [productIds, amounts]
        );
        await client.query(
            `INSERT INTO inventory.reservations (order_id, product_id, amount)
             SELECT $1, r.product_id, r.amount FROM unnest($2::uuid[], $3::int[]) AS r(product_id, amount)`,
            [OrderId, productIds, amounts]
        );

        await client.query('COMMIT');
        callback(null, { Success: true, Message: 'Reserved successfully', Shortages: [], MissingProductIds: [] });
    } catch (err) {
        await client.query('ROLLBACK');
        callback({ code: grpc.status.INTERNAL, details: err.message || 'Database error' });
    } finally {
        client.release(); // Prevent connection leaks
    }
}

// Releases everything reserved for an order in one transaction.
// Idempotent: calling it again (or for an order with no reservations) changes nothing.
async function releaseOrder(call, callback) {
    const { OrderId } = call.request;
    const client = await pool.connect();

    try {
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
        await client.query('ROLLBACK');
        callback({ code: grpc.status.INTERNAL, details: err.message });
    } finally {
        client.release(); // Prevent connection leaks
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
    const client = await pool.connect();
    
    console.log(`[gRPC] InitializeStock received for Product: ${ProductId} with Qty: ${InitialQuantity}`);

    try {
        await client.query('BEGIN');
        
        await client.query(
            'INSERT INTO inventory.stock (product_id, available_quantity) VALUES ($1, $2)',
            [ProductId, InitialQuantity]
        );
        
        await client.query('COMMIT');
        callback(null, { Success: true, Message: "Stock initialized successfully" });
    } catch (err) {
        await client.query('ROLLBACK');
        console.error(`[gRPC] InitializeStock error:`, err.message);
        callback({ code: grpc.status.INTERNAL, details: err.message });
    } finally {
        client.release();
    }
}