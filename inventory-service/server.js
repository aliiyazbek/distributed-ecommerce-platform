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

async function reserveQuantity(call, callback) {
    const { ProductId, Amount, OrderId } = call.request;
    const client = await pool.connect();

    try {
        await client.query('BEGIN');

        // Idempotency Check: If order already reserved this product, return success immediately[cite: 3]
        const checkRes = await client.query(
            'SELECT 1 FROM inventory.reservations WHERE order_id = $1 AND product_id = $2', 
            [OrderId, ProductId]
        );
        if (checkRes.rows.length > 0) {
            await client.query('COMMIT');
            return callback(null, { Success: true, Message: "Already reserved" });
        }

        // Row Lock: Prevent race conditions[cite: 3]
        const stockRes = await client.query(
            'SELECT available_quantity FROM inventory.stock WHERE product_id = $1 FOR UPDATE', 
            [ProductId]
        );

        if (stockRes.rows.length === 0) {
            throw { code: grpc.status.NOT_FOUND, message: "Product not found in inventory" }; // Standardized Errors[cite: 3]
        }

        const currentQty = stockRes.rows[0].available_quantity;
        if (currentQty < Amount) {
            throw { code: grpc.status.FAILED_PRECONDITION, message: "Insufficient stock" }; // Standardized Errors[cite: 3]
        }

        // Update stock and log reservation
        await client.query('UPDATE inventory.stock SET available_quantity = available_quantity - $1 WHERE product_id = $2', [Amount, ProductId]);
        await client.query('INSERT INTO inventory.reservations (order_id, product_id, amount) VALUES ($1, $2, $3)', [OrderId, ProductId, Amount]);

        await client.query('COMMIT');
        callback(null, { Success: true, Message: "Reserved successfully" });
    } catch (err) {
        await client.query('ROLLBACK');
        callback({ 
            code: err.code || grpc.status.INTERNAL, 
            details: err.message || "Database error" 
        });
    } finally {
        client.release(); // Prevent connection leaks[cite: 3]
    }
}

async function releaseQuantity(call, callback) {
    const { ProductId, Amount, OrderId } = call.request;
    const client = await pool.connect();

    try {
        await client.query('BEGIN');

        // Check if reservation exists to allow rollback
        const checkRes = await client.query('SELECT 1 FROM inventory.reservations WHERE order_id = $1 AND product_id = $2', [OrderId, ProductId]);
        
        if (checkRes.rows.length > 0) {
            await client.query('UPDATE inventory.stock SET available_quantity = available_quantity + $1 WHERE product_id = $2', [Amount, ProductId]);
            await client.query('DELETE FROM inventory.reservations WHERE order_id = $1 AND product_id = $2', [OrderId, ProductId]);
        }

        await client.query('COMMIT');
        callback(null, { Success: true, Message: "Released successfully" });
    } catch (err) {
        await client.query('ROLLBACK');
        callback({ code: grpc.status.INTERNAL, details: err.message });
    } finally {
        client.release(); // Prevent connection leaks[cite: 3]
    }
}

// 5. Start Server & Graceful Shutdown
const server = new grpc.Server();
server.addService(inventoryProto.InventoryService.service, {
    checkQuantity,
    reserveQuantity,
    releaseQuantity,
});

initDb().then(() => {
    const bindAddr = `0.0.0.0:${process.env.GRPC_PORT || 50051}`;
    server.bindAsync(bindAddr, grpc.ServerCredentials.createInsecure(), (err, port) => {
        if (err) {
            console.error(err);
            return;
        }
        console.log(`Inventory gRPC Service running on port ${port}`);
    });
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