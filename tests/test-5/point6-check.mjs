import { createHmac } from 'node:crypto'
import { API, listen, login, negotiate, report, status, wait, wsStatus } from './lib.mjs'

const DEV_KEY = 'super_secret_key_that_must_be_at_least_32_characters_long_for_hs256!'
const customer = (await login('rt.customer@test.local')).token
const admin = (await login('rt.admin@test.local')).token

const part = value => Buffer.from(JSON.stringify(value)).toString('base64url')
const claims = token => JSON.parse(Buffer.from(token.split('.')[1], 'base64url'))
const sign = data => createHmac('sha256', Buffer.from(DEV_KEY, 'ascii')).update(data).digest('base64url')
const [header, , signature] = customer.split('.')
const keyKnown = sign(customer.split('.').slice(0, 2).join('.')) === signature
const resign = (token, changes) => {
  const body = `${header}.${part({ ...claims(token), ...changes })}`
  return `${body}.${sign(body)}`
}
const forged = `${header}.${part({ ...claims(customer), role: 'Admin' })}.${signature}`
const now = () => Math.floor(Date.now() / 1000)
const expired = keyKnown ? resign(customer, { iat: now() - 7200, nbf: now() - 7200, exp: now() - 60 }) : null

const cases = [
  ['/hubs/orders        no token', await negotiate('orders'), 401],
  ['/hubs/orders        forged token (role edited to Admin)', await negotiate('orders', forged), 401],
  ['/hubs/orders        expired token', expired ? await negotiate('orders', expired) : 'skipped', 401],
  ['/hubs/orders        Admin token', await negotiate('orders', admin), 403],
  ['/hubs/orders        Customer token', await negotiate('orders', customer), 200],
  ['/hubs/admin-orders  no token', await negotiate('admin-orders'), 401],
  ['/hubs/admin-orders  forged token (role edited to Admin)', await negotiate('admin-orders', forged), 401],
  ['/hubs/admin-orders  expired token', expired ? await negotiate('admin-orders', expired) : 'skipped', 401],
  ['/hubs/admin-orders  Customer token', await negotiate('admin-orders', customer), 403],
  ['/hubs/admin-orders  Admin token', await negotiate('admin-orders', admin), 200],
  ['WebSocket directly  /hubs/orders, no token', await wsStatus('orders'), 401],
  ['WebSocket directly  /hubs/orders, Admin token', await wsStatus('orders', admin), 403],
  ['WebSocket directly  /hubs/orders, Customer token', await wsStatus('orders', customer), 101],
  ['WebSocket directly  /hubs/admin-orders, Customer token', await wsStatus('admin-orders', customer), 403],
  ['WebSocket directly  /hubs/admin-orders, Admin token', await wsStatus('admin-orders', admin), 101],
  ['REST /api/v1/orders, token in the URL', await status(`${API}/api/v1/orders?access_token=${customer}`), 401],
  ['REST /api/v1/orders, token in the header', await status(`${API}/api/v1/orders`, { headers: { Authorization: `Bearer ${customer}` } }), 200],
]
for (const [name, got, expected] of cases) report(name, got === expected, `got ${got}, expected ${expected}`)

if (!keyKnown) {
  console.log('SKIP  expiry checks: the API signs with a key other than the dev default')
} else {
  const expiresAt = now() + 5
  const tabs = await Promise.all([
    listen('orders', resign(customer, { exp: expiresAt })),
    listen('admin-orders', resign(admin, { exp: expiresAt })),
  ])
  const closedAt = await Promise.race([Promise.all(tabs.map(t => t.closed)), wait(20000).then(() => [null, null])])
  ;['/hubs/orders      ', '/hubs/admin-orders'].forEach((hub, i) => {
    const after = closedAt[i] && (closedAt[i] - expiresAt * 1000) / 1000
    report(`${hub}  open connection is closed when its token expires`, closedAt[i] && after >= 0, closedAt[i] ? `closed ${after.toFixed(1)} s after expiry` : 'still open after 20 s')
  })
}
