import assert from 'node:assert/strict'
import { before, after, test } from 'node:test'
import fs from 'node:fs'
import vm from 'node:vm'
import { createServer } from 'vite'
import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { categories, emptyProduct, validateProduct, submitProduct } from '../src/pages/admin/productForm.js'
import { mockOrders } from '../src/data/mockOrders.js'
import { adminRoutes, resolveAdminRoute } from '../src/navigation/adminRoutes.js'
import { orderStatuses, updateOrderStatus, orderTotal, lineTotal } from '../src/services/orderService.js'

// No browser dependencies: real SSR plus handlers run with controlled DOM/state
// collaborators. Canvas painting, image decoding and effects need browser QA.
const read = path => fs.readFileSync(new URL(`../${path}`, import.meta.url), 'utf8')
const render = (component, props = {}) => renderToStaticMarkup(createElement(component, props))
const source = read('src/pages/admin/AddProduct.jsx')
const appSource = read('src/App.jsx')
let server
let App, AddProduct, AllOrders, Details, Progress, Navbar

before(async () => {
  server = await createServer({ server: { middlewareMode: true }, appType: 'custom' })
  const load = async path => (await server.ssrLoadModule(`/src/${path}.jsx`)).default
  ;[App, AddProduct, AllOrders, Details, Progress, Navbar] = await Promise.all([
    load('App'), load('pages/admin/AddProduct'), load('pages/admin/AllOrders'),
    load('pages/admin/AdminOrderDetails'), load('components/OrderProgress'), load('components/Navbar'),
  ])
})
after(async () => { await server?.close() })

function productHarness() {
  const context2d = Object.fromEntries(['fillRect', 'beginPath', 'moveTo', 'lineTo', 'stroke', 'ellipse', 'fill', 'closePath'].map(name => [name, () => {}]))
  const state = {
    values: { ...emptyProduct }, errors: {}, image: null, preview: '', imageError: '', status: null, submitting: false,
    validateProduct, categories, emptyProduct, File, Uint8Array, atob,
    URL: { createObjectURL: file => { assert.ok(file instanceof File); return 'blob:test-preview' } },
    document: { createElement: tag => {
      assert.equal(tag, 'canvas')
      return { getContext: () => context2d, toDataURL: () => 'data:image/png;base64,iVBORw0KGgo=' }
    } },
    fileRef: { current: { value: '', focus: () => { state.focused = 'image' } } },
    formRef: { current: { elements: { namedItem: name => ({ focus: () => { state.focused = name } }) } } },
    calls: [],
    submitProduct: async product => { state.calls.push(product); return submitProduct(product) },
  }
  for (const name of ['values', 'errors', 'image', 'preview', 'imageError', 'status', 'submitting']) {
    state[`set${name[0].toUpperCase()}${name.slice(1)}`] = value => { state[name] = value }
  }
  vm.createContext(state)
  vm.runInContext(source.slice(source.indexOf('  function changeValue('), source.lastIndexOf('\n  return (')), state)
  return state
}

test('required fields, numeric boundaries and category validation', async () => {
  assert.equal(Object.keys(validateProduct(emptyProduct)).length, 5)
  const valid = { name: ' Lamp ', description: ' Desk lamp ', price: '24.99', category: categories[0], stock: '0' }
  assert.deepEqual(validateProduct(valid), {})
  for (const price of ['', ' ', '0', '-1', 'Infinity', 'NaN']) assert.ok(validateProduct({ ...valid, price }).price)
  for (const stock of ['', ' ', '-1', '1.2', 'Infinity', '9007199254740992']) assert.ok(validateProduct({ ...valid, stock }).stock)
  for (const name of ['name', 'description', 'category']) assert.ok(validateProduct({ ...valid, [name]: ' ' })[name])
  assert.equal((await submitProduct(valid)).saved, false)
})

test('product demo helpers, validation focus, correction and normalized submit', async () => {
  const state = productHarness()
  state.showErrors()
  assert.equal(Object.keys(state.errors).length, 5)
  assert.equal(state.focused, 'name')
  state.changeValue({ target: { name: 'name', value: 'Lamp' } })
  assert.equal(state.errors.name, undefined)
  state.fillDemo()
  assert.deepEqual(validateProduct(state.values), {})
  assert.equal(Object.keys(state.errors).length, 0)
  assert.equal(state.image.type, 'image/png')
  assert.equal(state.image.name, 'demo-desk-lamp.png')
  assert.equal(state.preview, 'blob:test-preview')
  state.values.name = ' Lamp '
  await state.handleSubmit({ preventDefault() {} })
  assert.equal(state.calls.length, 1)
  assert.equal(state.calls[0].name, 'Lamp')
  assert.equal(state.calls[0].price, 24.99)
  assert.equal(state.calls[0].stock, 12)
  assert.equal(state.calls[0].image, state.image)
  assert.match(state.status.message, /not been saved to a server/)
  state.showErrors()
  await state.handleSubmit({ preventDefault() {} })
  assert.equal(state.calls.length, 1, 'invalid values never submit')
  assert.equal(state.image, null)
  assert.equal(state.preview, '')
})

test('image types, size, replacement, picker cancellation and optional-image recovery', async () => {
  const state = productHarness()
  for (const type of ['image/png', 'image/jpeg', 'image/webp']) {
    const file = new File(['fixture'], 'image', { type })
    state.selectImage({ target: { files: [file] } })
    assert.equal(state.image, file)
    assert.equal(state.imageError, '')
    state.selectImage({ target: { files: [] } })
    assert.equal(state.image, file, 'cancelled picker preserves selection')
  }
  state.selectImage({ target: { files: [new File(['bad'], 'bad.txt', { type: 'text/plain' })] } })
  assert.match(state.imageError, /PNG, JPG, or WebP/)
  state.selectImage({ target: { files: [new File([new Uint8Array(5 * 1024 * 1024 + 1)], 'large.png', { type: 'image/png' })] } })
  assert.match(state.imageError, /5 MB/)
  // Regression: the recovery control must exist even without a selected image.
  assert.ok(source.includes('(image || imageError)'))
  state.clearImage()
  assert.equal(state.imageError, '')
  assert.equal(state.image, null)
  assert.equal(state.preview, '')
  state.values = { name: 'Lamp', description: 'Desk lamp', price: '1', category: categories[0], stock: '0' }
  await state.handleSubmit({ preventDefault() {} })
  assert.equal(state.calls.length, 1)
  assert.equal(state.calls[0].image, null)
})

test('submission busy state, duplicate guard and failure recovery', async () => {
  const state = productHarness()
  state.fillDemo()
  let rejectRequest
  let count = 0
  state.submitProduct = () => { count++; return new Promise((resolve, reject) => { rejectRequest = reject }) }
  const pending = state.handleSubmit({ preventDefault() {} })
  assert.equal(state.submitting, true)
  await state.handleSubmit({ preventDefault() {} })
  assert.equal(count, 1)
  rejectRequest(new Error('test failure'))
  await pending
  assert.equal(state.submitting, false)
  assert.equal(state.status.type, 'error')
  assert.equal(state.values.name, 'Brushed Steel Desk Lamp')
})

test('six unique orders, consistent totals/counts, list/empty/details rendering and accessibility', () => {
  assert.equal(new Set(mockOrders.map(order => order.id)).size, 6)
  const list = render(AllOrders, { orders: mockOrders })
  assert.equal((list.match(/<tr>/g) || []).length, 7)
  assert.match(render(AllOrders, { orders: [] }), /No orders yet/)
  assert.match(render(Details), /Order not found/)
  for (const order of mockOrders) {
    assert.equal(orderTotal(order.items), order.total)
    assert.equal(order.items.reduce((sum, item) => sum + item.quantity, 0), order.itemCount)
    assert.equal(new Set(order.items.map(item => item.id)).size, order.items.length)
    assert.ok(list.includes(adminRoutes.orderDetails(order.id)))
    const detail = render(Details, { order })
    for (const value of [order.orderNumber, order.customerId, order.datePlaced, ...order.items.map(item => item.name)]) assert.ok(detail.includes(value))
    assert.ok(detail.includes(adminRoutes.orders))
    assert.ok(detail.includes('aria-labelledby="items-heading"'))
  }
  assert.equal(lineTotal({ unitPrice: 0.1, quantity: 3 }), 0.3)
  const product = render(AddProduct)
  for (const name of ['name', 'description', 'price', 'category', 'stock', 'image']) assert.ok(product.includes(`id="${name}"`))
  assert.ok(product.includes('Quick demo helpers:'))
  assert.ok(product.includes('Autofill valid product + image'))
  assert.ok(product.includes('Show all 5 field errors'))
})

test('routes, active navigation, hash change listener and cleanup', () => {
  assert.equal(resolveAdminRoute('').page, 'addProduct')
  assert.equal(resolveAdminRoute(adminRoutes.orders + '/').page, 'orders')
  assert.equal(resolveAdminRoute(adminRoutes.orderDetails('id / #1')).orderId, 'id / #1')
  assert.equal(resolveAdminRoute('#/admin/orders/%').orderId, null)
  const oldWindow = globalThis.window
  try {
    globalThis.window = { location: { hash: '' } }
    for (const hash of [adminRoutes.addProduct, adminRoutes.orders, ...mockOrders.map(order => adminRoutes.orderDetails(order.id)), '#/admin/orders/missing', '#/admin/orders/%']) {
      window.location.hash = hash
      const markup = render(App)
      const route = resolveAdminRoute(hash)
      assert.ok(markup.includes(route.page === 'addProduct' ? 'product-card' : route.orderId === undefined ? 'orders-table' : 'back-to-orders'))
      if (hash.endsWith('/missing') || hash.endsWith('/%')) assert.ok(markup.includes('Order not found'))
      const nav = render(Navbar, { activePage: route.page })
      assert.equal((nav.match(/aria-current="page"/g) || []).length, 1)
      assert.ok(nav.includes(`href="${adminRoutes[route.page]}" class="active"`))
    }
  } finally { if (oldWindow === undefined) delete globalThis.window; else globalThis.window = oldWindow }
  const state = { resolveAdminRoute, window: { location: { hash: adminRoutes.orders }, addEventListener: (event, listener) => { state.event = event; state.listener = listener }, removeEventListener: (event, listener) => { assert.equal(event, state.event); assert.equal(listener, state.listener) } }, setRoute: route => { state.route = route }, useEffect: setup => { state.cleanup = setup() } }
  vm.createContext(state)
  vm.runInContext(appSource.slice(appSource.indexOf('  useEffect('), appSource.indexOf('\n  return (')), state)
  assert.equal(state.event, 'hashchange')
  for (const hash of [adminRoutes.orders, adminRoutes.addProduct, adminRoutes.orderDetails(mockOrders[0].id)]) {
    state.window.location.hash = hash
    state.listener()
    assert.equal(JSON.stringify(state.route), JSON.stringify(resolveAdminRoute(hash)))
  }
  state.cleanup()
})

test('Save synchronizes every status with list, badge and progress; errors preserve saved state', async () => {
  const detailSource = read('src/pages/admin/AdminOrderDetails.jsx')
  const state = { orders: mockOrders, order: mockOrders[0], selectedStatus: 'Pending', saving: false, updateOrderStatus,
    setOrders: update => { state.orders = update(state.orders) }, setSaving: value => { state.saving = value }, setMessage: value => { state.message = value } }
  vm.createContext(state)
  vm.runInContext(appSource.slice(appSource.indexOf('  async function saveOrderStatus('), appSource.indexOf('  useEffect(')), state)
  vm.runInContext(detailSource.slice(detailSource.indexOf('  async function handleSave('), detailSource.indexOf('\n  return (')), state)
  state.onSaveStatus = state.saveOrderStatus
  for (const status of [...orderStatuses, 'Pending']) {
    const previous = state.orders[0].status
    state.selectedStatus = status
    assert.equal(state.orders[0].status, previous)
    await state.handleSave({ preventDefault() {} })
    assert.equal(state.orders[0].status, status)
    assert.equal(state.saving, false)
    assert.match(state.message.text, /Nothing was saved to a server/)
    const progress = render(Progress, { status })
    assert.ok(render(Details, { order: state.orders[0] }).includes(progress))
    assert.ok(render(AllOrders, { orders: state.orders }).includes('order-status-badge--' + status.toLowerCase()))
    if (status === 'Cancelled') {
      assert.ok(progress.includes('order-status-badge--cancelled'))
      assert.ok(!progress.includes('<ol>'))
    } else {
      const current = ['Pending', 'Shipped', 'Delivered'].indexOf(status)
      assert.equal((progress.match(/class="progress-completed"/g) || []).length, current)
      assert.equal((progress.match(/class="progress-inactive"/g) || []).length, 2 - current)
      assert.equal((progress.match(/aria-current="step"/g) || []).length, 1)
      assert.ok(progress.includes('order-status-badge--' + status.toLowerCase()))
    }
    assert.equal(mockOrders[0].status, 'Pending')
    assert.equal(state.orders[1], mockOrders[1])
  }
  let rejectRequest
  let count = 0
  state.onSaveStatus = () => { count++; return new Promise((resolve, reject) => { rejectRequest = reject }) }
  const pending = state.handleSave({ preventDefault() {} })
  assert.equal(state.saving, true)
  await state.handleSave({ preventDefault() {} })
  assert.equal(count, 1)
  rejectRequest(new Error('test failure'))
  await pending
  assert.equal(state.message.type, 'error')
  assert.equal(state.saving, false)
  assert.equal(state.orders[0].status, 'Pending')
  await assert.rejects(updateOrderStatus(undefined, 'Pending'))
  await assert.rejects(updateOrderStatus(mockOrders[0], 'invalid'))
})
