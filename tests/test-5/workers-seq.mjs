import { spawnSync } from 'node:child_process'
import { API, P, login, psql, wait } from './lib.mjs'
const N = Number(process.argv[2] ?? 15)
const since = new Date(Date.now() - 1000).toISOString()
const customer = await login('rt.customer@test.local')
const products = psql(`SELECT p."Id" FROM products."Products" p JOIN inventory.stock s ON s.product_id = p."Id" WHERE s.available_quantity > 10 ORDER BY p."Name"`).split('\n')
const order = []
for (let i = 0; i < N; i++) {
  const res = await fetch(`${API}/api/v1/orders`, { method: 'POST',
    headers: { Authorization: `Bearer ${customer.token}`, 'Content-Type': 'application/json' },
    body: JSON.stringify({ items: [{ productId: products[i % products.length], quantity: 1 }] }) })
  order.push(res.status)
  await wait(600)
}
await wait(4000)
const perWorker = {}
const sequence = []
for (const w of ['worker-1', 'worker-2', 'worker-3']) {
  const out = spawnSync('docker', ['logs', '--timestamps', '--since', since, `${P}-${w}-1`], { encoding: 'utf8' })
  const lines = (out.stdout + out.stderr).split('\n').filter(l => l.includes('Confirmation email for order'))
  perWorker[w] = lines.length
  for (const l of lines) sequence.push([l.slice(0, 30), w])
}
sequence.sort((a, b) => a[0].localeCompare(b[0]))
console.log(`orders created one by one: ${order.filter(s => s === 201).length}/${N}`)
console.log('emails sent per worker   :', perWorker)
console.log('order of handling        :', sequence.map(([, w]) => w.replace('worker-', 'w')).join(' '))
