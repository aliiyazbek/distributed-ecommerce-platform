const api = process.env.API_URL ?? 'http://localhost:5169'
const adminEmail = process.env.ADMIN_EMAIL
const adminPassword = process.env.ADMIN_PASSWORD
const orderCount = Number(process.env.ORDERS ?? 20)
const pixel = 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg=='

if (!adminEmail || !adminPassword) {
  console.error('Set ADMIN_EMAIL and ADMIN_PASSWORD first.')
  process.exit(1)
}

async function send(path, { token, json, form } = {}) {
  const response = await fetch(api + path, {
    method: json || form ? 'POST' : 'GET',
    headers: {
      ...(token && { Authorization: `Bearer ${token}` }),
      ...(json && { 'Content-Type': 'application/json' }),
    },
    body: form ?? (json && JSON.stringify(json)),
  })
  const text = await response.text()
  return { status: response.status, body: text ? JSON.parse(text) : null }
}

async function signIn() {
  const { status, body } = await send('/api/users/login', { json: { email: adminEmail, password: adminPassword } })
  if (status !== 200) throw new Error(`Admin sign in failed with ${status}.`)
  return body.token
}

async function createCustomer() {
  const email = `last-piece-${crypto.randomUUID()}@example.com`
  const { status, body } = await send('/api/users/register', { json: { email, password: 'Pass1234' } })
  if (status !== 201) throw new Error(`Creating a customer failed with ${status}.`)
  return body.token
}

async function createLastPiece(token) {
  const form = new FormData()
  form.append('Name', `Last piece ${new Date().toISOString()}`)
  form.append('Description', 'Created by the last piece test.')
  form.append('Price', '10')
  form.append('Category', 'Home')
  form.append('AvailableQuantity', '1')
  form.append('Image', new Blob([Buffer.from(pixel, 'base64')], { type: 'image/png' }), 'last-piece.png')
  const { status, body } = await send('/api/v1/products/CreateProduct', { token, form })
  if (status !== 201) throw new Error(`Creating the product failed with ${status}.`)
  return body.id
}

const adminToken = await signIn()
const customers = [await createCustomer(), await createCustomer()]
const productId = await createLastPiece(adminToken)

const results = await Promise.all(Array.from({ length: orderCount }, (_, index) => send('/api/v1/orders', {
  token: customers[index % customers.length],
  json: { items: [{ productId, quantity: 1 }] },
})))

const placed = results.filter(result => result.status === 201)
const rejected = results.filter(result => result.status !== 201)
const { body: product } = await send(`/api/v1/products/${productId}`, { token: customers[0] })
const passed = placed.length === 1 && rejected.every(result => result.status === 400) && product.availableQuantity === 0

console.log(`Orders sent at the same moment: ${orderCount}`)
console.log(`Placed: ${placed.length}`)
console.log(`Rejected: ${rejected.length}`)
console.log(`Rejection message: ${rejected[0]?.body?.message ?? 'none'}`)
console.log(`Stock left: ${product.availableQuantity}`)
console.log(passed ? 'PASSED: only one order got the last piece.' : 'FAILED: expected exactly one placed order and no stock left.')
process.exit(passed ? 0 : 1)
