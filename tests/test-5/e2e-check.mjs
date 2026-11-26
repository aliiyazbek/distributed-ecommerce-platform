import { API, listen, login, psql, rabbit, report, status, wait } from './lib.mjs'

const alice = await login('rt.customer@test.local')
const bob = await login('rt.customer2@test.local')
const admin = await login('rt.admin@test.local')
const as = token => ({ Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' })
const productId = psql(`SELECT p."Id" FROM products."Products" p JOIN inventory.stock s ON s.product_id = p."Id"
                        WHERE s.available_quantity > 0 ORDER BY p."Name" LIMIT 1`)

const aliceTab = await listen('orders', alice.token)
const bobTab = await listen('orders', bob.token)
const adminTab = await listen('admin-orders', admin.token)

const placedAt = Date.now()
const created = await fetch(`${API}/api/v1/orders`, { method: 'POST', headers: as(alice.token), body: JSON.stringify({ items: [{ productId, quantity: 2 }] }) })
const order = await created.json()
const pushedOrder = await adminTab.next('OrderCreated', o => o.id === order.id)
report('Alice places an order (real API, gRPC stock, outbox)', [200, 201].includes(created.status), `HTTP ${created.status}`)
report('admin dashboard receives the new order, no refresh', !!pushedOrder, pushedOrder ? `${pushedOrder.at - placedAt} ms after the request` : 'nothing within 5 s')
report('pushed order has the right data', pushedOrder && pushedOrder.data.userId === alice.userId
  && pushedOrder.data.totalPrice === order.totalPrice && pushedOrder.data.status === 'Pending'
  && pushedOrder.data.items[0]?.quantity === 2, pushedOrder ? `total ${pushedOrder.data.totalPrice}, ${pushedOrder.data.items.length} line(s)` : '')

const approvedAt = Date.now()
const patched = await fetch(`${API}/api/v1/orders/${order.id}/status`, { method: 'PATCH', headers: as(admin.token), body: JSON.stringify({ status: 'Approved' }) })
const pushedChange = await aliceTab.next('OrderStatusChanged', c => c.orderId === order.id && c.status === 'Approved')
report('admin approves it', patched.status === 200, `HTTP ${patched.status}`)
report("Alice's page receives the approval, no refresh", !!pushedChange, pushedChange ? `${pushedChange.at - approvedAt} ms after the request` : 'nothing within 5 s')

await wait(1500)
report('Bob (another customer) received nothing', bobTab.pushes.length === 0, `${bobTab.pushes.length} pushes`)
report('customers never receive the admin feed', !aliceTab.pushes.some(p => p.target === 'OrderCreated'))
const reloaded = await (await fetch(`${API}/api/v1/orders/${order.id}`, { headers: as(alice.token) })).json()
report('approval is saved (Alice reloads -> Approved)', reloaded.status === 'Approved', reloaded.status)
report('Bob cannot open the order over REST either', (await status(`${API}/api/v1/orders/${order.id}`, { headers: as(bob.token) })) === 403)
report("Majd's outbox is empty (both events published)", psql('SELECT count(*) FROM orders."OutboxMessages"') === '0')
const queues = rabbit('list_queues', 'name', 'messages_ready', 'messages_unacknowledged')
const ours = queues.find(([name]) => name === 'realtime.order-events')
report('our queue is empty (every event pushed and acked)', ours?.[1] === '0' && ours?.[2] === '0', `ready=${ours?.[1]} unacked=${ours?.[2]}`)
const email = queues.find(([name]) => name === 'email.order-created')
console.log(`      info: email.order-created got its own copy too (waiting for Majd's worker: ${email?.[1] ?? 'queue missing'})`)
console.log(`      order used in this run: ${order.id}`)
;[aliceTab, bobTab, adminTab].forEach(tab => tab.close())
