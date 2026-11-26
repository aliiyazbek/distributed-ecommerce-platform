import { createRequire } from 'node:module'
import { API, login, psql, wait } from './lib.mjs'

const require = createRequire(`${process.env.REPO ?? 'C:/Users/ALI/Desktop/IA/distributed-ecommerce-platform'}/frontend/ecommerce-store/package.json`)
const signalR = require('@microsoft/signalr')

const customer = await login('rt.customer@test.local')
const admin = await login('rt.admin@test.local')
const order = psql(`SELECT "Id" || '|' || "Status" FROM orders."Orders" WHERE "UserId" = '${customer.userId}' ORDER BY "CreatedAt" DESC LIMIT 1`)
const [orderId, current] = order.split('|')

const hubInstance = (await fetch(`${API}/hubs/orders/negotiate?negotiateVersion=1`, {
  method: 'POST', headers: { Authorization: `Bearer ${customer.token}` },
})).headers.get('x-instance-id')

const received = []
const connection = new signalR.HubConnectionBuilder()
  .withUrl(`${API}/hubs/orders`, { accessTokenFactory: () => customer.token })
  .configureLogging(signalR.LogLevel.Warning)
  .build()
connection.on('OrderStatusChanged', change => received.push(change.status))
await connection.start()

const cycle = ['Approved', 'Shipped', 'Delivered']
const start = (cycle.indexOf(current) + 1) % cycle.length
const statuses = Array.from({ length: 6 }, (_, i) => cycle[(start + i) % cycle.length])
const patchedOn = []
for (const status of statuses) {
  const res = await fetch(`${API}/api/v1/orders/${orderId}/status`, {
    method: 'PATCH',
    headers: { Authorization: `Bearer ${admin.token}`, 'Content-Type': 'application/json' },
    body: JSON.stringify({ status }),
  })
  patchedOn.push(`${res.headers.get('x-instance-id')}(${res.status})`)
  await wait(700)
}
await wait(2000)
await connection.stop()

console.log(`official SignalR client (negotiate + WebSocket) through nginx: hub connection sticky on ${hubInstance}`)
console.log(`admin status changes served by : ${patchedOn.join(', ')}`)
console.log(`pushes received by the customer: ${received.length}/${statuses.length}  [${received.join(' -> ')}]`)
