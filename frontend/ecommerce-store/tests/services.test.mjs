import assert from 'node:assert/strict'
import { afterEach, test } from 'node:test'
import { ApiError, request, setUnauthorizedHandler } from '../src/services/apiClient.js'
import { authErrorMessage } from '../src/services/authService.js'
import { addLine, countItems, removeLine, setLineQuantity } from '../src/services/cartService.js'
import { formatMoney, getOrders, lineTotal, orderFailure, orderTotal, parseStockError, shortId, toOrder } from '../src/services/orderService.js'
import { toSession } from '../src/services/session.js'
import { mapApiErrors, validateProduct } from '../src/pages/admin/productForm.js'
import { validateAccount } from '../src/pages/store/accountForm.js'

const realFetch = globalThis.fetch
const store = new Map()

Object.defineProperty(globalThis, 'localStorage', {
  configurable: true,
  value: {
    getItem: key => store.get(key) ?? null,
    setItem: (key, value) => store.set(key, value),
    removeItem: key => store.delete(key),
  },
})

afterEach(() => {
  globalThis.fetch = realFetch
  store.clear()
  setUnauthorizedHandler(() => {})
})

const respond = (status, body, type = 'application/json') => () => Promise.resolve(new Response(body, { status, headers: type ? { 'content-type': type } : {} }))

const tokenFor = payload => `header.${Buffer.from(JSON.stringify(payload)).toString('base64url')}.signature`

test('reads the session from the token', () => {
  const token = tokenFor({ sub: 'user-1', email: 'admin@shop.com', role: 'Admin', exp: 1793829212 })
  assert.deepEqual(toSession(token), { token, userId: 'user-1', email: 'admin@shop.com', role: 'Admin', expiresAt: 1793829212000 })
  assert.equal(toSession('not-a-token'), null)
})

test('sends the token and returns the body', async () => {
  store.set('store.token', 'abc')
  let sent
  globalThis.fetch = (path, options) => {
    sent = { path, options }
    return respond(200, '{"ok":true}')()
  }
  assert.deepEqual(await request('/api/v1/orders', { method: 'POST', body: { items: [] } }), { ok: true })
  assert.equal(sent.options.headers.Authorization, 'Bearer abc')
  assert.equal(sent.options.headers['Content-Type'], 'application/json')
  assert.equal(sent.options.body, '{"items":[]}')
})

test('leaves the content type to the browser for uploads', async () => {
  let headers
  globalThis.fetch = (path, options) => {
    headers = options.headers
    return respond(201, '{}')()
  }
  await request('/upload', { method: 'POST', body: new FormData() })
  assert.equal(headers['Content-Type'], undefined)
})

test('turns failed responses into ApiError', async () => {
  globalThis.fetch = respond(400, '{"message":"Nope."}')
  await assert.rejects(request('/x'), error => error instanceof ApiError && error.status === 400 && error.message === 'Nope.')

  globalThis.fetch = respond(400, '{"title":"Invalid","errors":{"Name":["Required."]}}', 'application/problem+json')
  await assert.rejects(request('/x'), error => error.fields.Name[0] === 'Required.')

  globalThis.fetch = respond(429, null, null)
  await assert.rejects(request('/x'), error => error.status === 429)
})

test('treats a dead server or a failed connection as unreachable', async () => {
  globalThis.fetch = respond(500, null, null)
  await assert.rejects(request('/x'), error => error.unreachable)

  globalThis.fetch = () => Promise.reject(new TypeError('failed'))
  await assert.rejects(request('/x'), error => error.unreachable)

  globalThis.fetch = (path, { signal }) => new Promise((resolve, reject) => signal.addEventListener('abort', () => reject(signal.reason)))
  await assert.rejects(request('/x', { timeout: 20 }), error => error.unreachable)

  globalThis.fetch = respond(500, '{"title":"Internal Server Error"}')
  await assert.rejects(request('/x'), error => error.status === 500 && !error.unreachable)
})

test('treats a 503 as unreachable with or without a body', async () => {
  globalThis.fetch = respond(503, null, null)
  await assert.rejects(request('/x'), error => error.unreachable)

  globalThis.fetch = respond(503, '{"title":"Service Unavailable","detail":"The inventory service is unavailable."}', 'application/problem+json')
  await assert.rejects(request('/x'), error => error.unreachable && error.message === 'The inventory service is unavailable.')
})

test('reports an expired session only when a token was sent', async () => {
  let calls = 0
  setUnauthorizedHandler(() => calls++)
  globalThis.fetch = respond(401, null, null)
  await assert.rejects(request('/x'))
  assert.equal(calls, 0)
  store.set('store.token', 'abc')
  await assert.rejects(request('/x'))
  assert.equal(calls, 1)
})

test('auth messages follow the brief', () => {
  assert.equal(authErrorMessage(new ApiError(429)), 'Too many attempts. Please wait a few seconds and try again.')
  assert.equal(authErrorMessage(new ApiError(401, 'Invalid email or password.')), 'Invalid email or password.')
  assert.equal(authErrorMessage(new ApiError(409, 'Email is already registered.')), 'Email is already registered.')
  assert.equal(authErrorMessage(new ApiError(0)), 'Something went wrong. Please try again.')
})

test('maps an order from the API', () => {
  const order = toOrder({
    id: 'd15280b9-030f-461f-a6f5-f0d3adcfd983',
    userId: '7726a55d-965b-4477-a488-d11afd46db70',
    status: 'Pending',
    totalPrice: 49.98,
    createdAt: '2026-11-04T19:53:44.289727Z',
    updatedAt: null,
    items: [{ productId: 'p1', productName: 'Lamp', unitPrice: 24.99, quantity: 2 }],
  })
  assert.equal(order.orderNumber, 'D15280B9')
  assert.equal(order.customerId, '7726A55D')
  assert.equal(order.itemCount, 2)
  assert.equal(order.total, 49.98)
  assert.equal(order.lastUpdated, null)
  assert.deepEqual(order.items, [{ productId: 'p1', name: 'Lamp', unitPrice: 24.99, quantity: 2 }])
  assert.equal(shortId('abc-def'), 'ABCDEF')
})

test('order pagination preserves the server lookahead flag even for a full final page', async () => {
  const order = {
    id: 'd15280b9-030f-461f-a6f5-f0d3adcfd983',
    userId: '7726a55d-965b-4477-a488-d11afd46db70',
    status: 'Pending', totalPrice: 10, createdAt: '2026-11-04T19:53:44Z',
    items: [],
  }
  const items = Array.from({ length: 20 }, () => order)
  for (const hasNextPage of [true, false]) {
    globalThis.fetch = (path, options) => {
      assert.equal(path, '/api/v1/orders?page=2&pageSize=20')
      assert.equal(options.method, 'GET')
      return respond(200, JSON.stringify({ items, hasNextPage }))()
    }
    const result = await getOrders(undefined, 2)
    assert.equal(result.items.length, 20)
    assert.equal(result.items[0].orderNumber, 'D15280B9')
    assert.equal(result.hasNextPage, hasNextPage)
  }
  globalThis.fetch = respond(200, JSON.stringify({ items: [], hasNextPage: false }))
  assert.deepEqual(await getOrders(undefined, 3), { items: [], hasNextPage: false })
})

test('reads both stock error messages from the backend', () => {
  assert.deepEqual(parseStockError("Insufficient stock for 'Desk Lamp'. Requested: 9, Available: 3."), { name: 'Desk Lamp', available: 3 })
  assert.deepEqual(parseStockError('Concurrency constraint: Product Desk Lamp has insufficient stock. Requested: 1, Available: 0.'), { name: 'Desk Lamp', available: 0 })
  assert.equal(parseStockError('Something else.'), null)
})

test('order failures use the brief messages', () => {
  assert.equal(orderFailure(new ApiError(400, "Insufficient stock for 'Lamp'. Requested: 9, Available: 3.")).message, 'Not enough stock for Lamp. Your order was not placed.')
  assert.equal(orderFailure(new ApiError(0)).message, "We couldn't reach the inventory service. Your order was not placed. Please try again in a moment.")
  assert.equal(orderFailure(new ApiError(503, 'The inventory service is unavailable.')).message, "We couldn't reach the inventory service. Your order was not placed. Please try again in a moment.")
  assert.equal(orderFailure(new ApiError(500)).message, 'Something went wrong. Your order was not placed. Please try again.')
})

test('money stays exact to the cent', () => {
  assert.equal(lineTotal({ unitPrice: 0.1, quantity: 3 }), 0.3)
  assert.equal(orderTotal([{ unitPrice: 59.95, quantity: 2 }, { unitPrice: 38, quantity: 2 }]), 195.9)
  assert.equal(formatMoney(24.5), '$24.50')
})

test('the cart keeps one line per product', () => {
  const lamp = { id: 'p1', name: 'Lamp', price: 10, imageUrl: '/images/lamp.png' }
  let lines = addLine([], lamp, 2)
  lines = addLine(lines, lamp, 3)
  assert.equal(lines.length, 1)
  assert.equal(lines[0].quantity, 5)
  assert.equal(countItems(lines), 5)
  assert.equal(setLineQuantity(lines, 'p1', 0)[0].quantity, 1)
  assert.deepEqual(removeLine(lines, 'p1'), [])
})

test('product form follows the brief and the API', () => {
  const errors = validateProduct({ name: ' ', description: '', price: '0', category: '', stock: '-1' })
  assert.deepEqual(errors, {
    name: 'Product name is required.',
    description: 'Description is required.',
    price: 'Price must be greater than zero.',
    category: 'Category is required.',
    stock: 'Quantity cannot be negative.',
  })
  assert.deepEqual(validateProduct({ name: 'Lamp', description: 'Desk lamp', price: '24.99', category: 'Home', stock: '0' }), {})
  assert.equal(validateProduct({ name: 'Lamp', description: 'x', price: '1', category: 'Home', stock: '1.5' }).stock, 'Stock quantity must be a whole number.')
  assert.equal(validateProduct({ name: 'Lamp', description: 'x', price: '1', category: 'Home', stock: '2147483648' }).stock, 'Stock quantity is too large.')
  assert.equal(validateProduct({ name: 'Lamp', description: 'x', price: '24.999', category: 'Home', stock: '1' }).price, 'Price can have up to 2 decimal places.')
  assert.deepEqual(mapApiErrors({ AvailableQuantity: ['Quantity cannot be negative.'], Name: ['The Name field is required.', 'Product name is required.'] }), {
    stock: 'Quantity cannot be negative.',
    name: 'Product name is required.',
  })
})

test('account form follows the brief', () => {
  assert.deepEqual(validateAccount({ email: '', password: '' }), { email: 'Email is required.', password: 'Password is required.' })
  assert.equal(validateAccount({ email: 'bad', password: 'Pass12' }).email, 'A valid email format is required.')
  assert.equal(validateAccount({ email: 'a@b.co', password: 'Ab1' }).password, 'Password must be at least 6 characters.')
  assert.equal(validateAccount({ email: 'a@b.co', password: 'abcdef1' }).password, 'Password must contain at least one uppercase letter.')
  assert.equal(validateAccount({ email: 'a@b.co', password: 'Abcdefg' }).password, 'Password must contain at least one number.')
  assert.deepEqual(validateAccount({ email: 'a@b.co', password: 'Pass1234' }), {})
})
