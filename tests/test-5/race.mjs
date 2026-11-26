import { randomUUID } from 'node:crypto'
import { wait } from './lib.mjs'

const MQ = 'http://localhost:15672/api'
const AUTH = 'Basic ' + Buffer.from('ecommerce:ecommerce_password').toString('base64')
const MAILPIT = 'http://localhost:8025/api/v1'
const PAIRS = Number(process.argv[2] ?? 10)

const publish = (messageId, body) => fetch(`${MQ}/exchanges/%2F/amq.default/publish`, {
  method: 'POST',
  headers: { Authorization: AUTH, 'Content-Type': 'application/json' },
  body: JSON.stringify({
    routing_key: 'email.order-created', payload: body, payload_encoding: 'string',
    properties: { message_id: messageId, content_type: 'application/json', delivery_mode: 2 },
  }),
}).then(r => r.json())

await fetch(`${MAILPIT}/messages`, { method: 'DELETE' })
const orders = []
for (let i = 0; i < PAIRS; i++) {
  const orderId = randomUUID()
  const body = JSON.stringify({
    orderId, userId: randomUUID(), customerEmail: 'rt.race@test.local', totalPrice: 1,
    items: [{ productId: randomUUID(), productName: 'Duplicate test', unitPrice: 1, quantity: 1 }],
    createdAt: new Date().toISOString(),
  })
  const messageId = randomUUID()
  await Promise.all([publish(messageId, body), publish(messageId, body)])
  orders.push(orderId)
}

await wait(8000)
const messages = (await (await fetch(`${MAILPIT}/messages?limit=1000`)).json()).messages
const emailsFor = id => messages.filter(m => m.Subject === `Order ${id.replace(/-/g, '').slice(0, 8).toUpperCase()} confirmed`).length
const results = orders.map(emailsFor)
console.log(`${PAIRS} messages, each delivered twice with the SAME message_id at the same moment`)
console.log('emails per message   :', results.join(' '))
console.log('messages sent twice  :', results.filter(c => c > 1).length, `of ${PAIRS}`)
