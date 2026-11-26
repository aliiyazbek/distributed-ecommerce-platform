import { API, login } from './lib.mjs'

const customer = await login('rt.customer@test.local')
const auth = { headers: { Authorization: `Bearer ${customer.token}` } }
const count = (map, key) => { map[key] = (map[key] ?? 0) + 1 }

const sample = await fetch(`${API}/api/v1/orders?page=1&pageSize=1`, auth)
console.log('one response -> X-Instance-Id:', sample.headers.get('x-instance-id'), '| X-Upstream-Server:', sample.headers.get('x-upstream-server'))

const sequential = {}
for (let i = 0; i < 30; i++) {
  const res = await fetch(`${API}/api/v1/orders?page=1&pageSize=5`, auth)
  count(sequential, `${res.headers.get('x-instance-id')} ${res.status}`)
}
console.log('30 sequential GET /api/v1/orders :', sequential)

const parallel = {}
await Promise.all(Array.from({ length: 30 }, async () => {
  const res = await fetch(`${API}/api/v1/orders?page=1&pageSize=5`, auth)
  count(parallel, `${res.headers.get('x-instance-id')} ${res.status}`)
}))
console.log('30 parallel   GET /api/v1/orders :', parallel)
