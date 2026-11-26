import { API, psql, register, waitFor } from './lib.mjs'

await waitFor(async () => (await fetch(`${API}/api/v1/orders`)).status === 401, 'API')
await waitFor(() => psql(`SELECT to_regclass('inventory.stock') IS NOT NULL`) === 't', 'inventory service')

psql(`INSERT INTO inventory.stock (product_id, available_quantity) SELECT "Id", 100 FROM products."Products"
      ON CONFLICT (product_id) DO UPDATE SET available_quantity = 100`)
for (const email of ['rt.customer@test.local', 'rt.customer2@test.local', 'rt.admin@test.local']) await register(email)
psql(`UPDATE users."Users" SET "RoleId" = 1 WHERE "Email" = 'rt.admin@test.local'`)

console.log(psql(`SELECT p."Name" || ' | ' || p."Price" || ' | stock ' || s.available_quantity
                  FROM products."Products" p JOIN inventory.stock s ON s.product_id = p."Id" ORDER BY p."Name"`))
console.log('ready: API, inventory, stock and the 3 test accounts')
