import { spawnSync } from 'node:child_process'
import { API, P, login, psql, wait } from './lib.mjs'

const MAILPIT = 'http://localhost:8025/api/v1'
const N = Number(process.argv[2] ?? 30)
const orderNumber = id => id.replace(/-/g, '').slice(0, 8).toUpperCase()

await fetch(`${MAILPIT}/messages`, { method: 'DELETE' })
const since = new Date(Date.now() - 1000).toISOString()
const processedBefore = Number(psql('SELECT count(*) FROM email."ProcessedMessages"'))
const customer = await login('rt.customer@test.local')
const products = psql(`SELECT p."Id" FROM products."Products" p JOIN inventory.stock s ON s.product_id = p."Id"
                       WHERE s.available_quantity > 10 ORDER BY p."Name"`).split('\n')

const created = []
await Promise.all(Array.from({ length: N }, async (_, i) => {
  const res = await fetch(`${API}/api/v1/orders`, {
    method: 'POST',
    headers: { Authorization: `Bearer ${customer.token}`, 'Content-Type': 'application/json' },
    body: JSON.stringify({ items: [{ productId: products[i % products.length], quantity: 1 }] }),
  })
  if (res.status === 201) created.push((await res.json()).id)
  else console.log('order failed:', res.status, (await res.text()).slice(0, 150))
}))

let total = 0
for (let i = 0; i < 90 && total < created.length; i++) {
  await wait(1000)
  total = (await (await fetch(`${MAILPIT}/messages?limit=1`)).json()).total
}
await wait(4000)

const messages = (await (await fetch(`${MAILPIT}/messages?limit=1000`)).json()).messages
const perOrder = {}
for (const m of messages) {
  const n = m.Subject.split(' ')[1]
  perOrder[n] = (perOrder[n] ?? 0) + 1
}
const missing = created.filter(id => !perOrder[orderNumber(id)])
const duplicated = Object.entries(perOrder).filter(([, c]) => c > 1)

const perWorker = {}
for (const w of ['worker-1', 'worker-2', 'worker-3']) {
  const out = spawnSync('docker', ['logs', '--since', since, `${P}-${w}-1`], { encoding: 'utf8' })
  perWorker[w] = ((out.stdout + out.stderr).match(/Confirmation email for order/g) ?? []).length
}
const processedAfter = Number(psql('SELECT count(*) FROM email."ProcessedMessages"'))

console.log(`orders created          : ${created.length}/${N}`)
console.log(`emails in Mailpit       : ${messages.length}`)
console.log(`orders without an email : ${missing.length}`)
console.log(`orders with >1 email    : ${duplicated.length}`, duplicated.length ? JSON.stringify(duplicated) : '')
console.log(`emails sent per worker  :`, perWorker)
console.log(`ProcessedMessages rows  : +${processedAfter - processedBefore}`)
