import { request } from './apiClient.js'

export const orderStatuses = ['Pending', 'Shipped', 'Delivered', 'Cancelled']

const dateFormat = new Intl.DateTimeFormat('en-US', { dateStyle: 'medium', timeStyle: 'short' })

export const shortId = id => id.replace(/-/g, '').slice(0, 8).toUpperCase()

export const formatDate = value => dateFormat.format(new Date(value))

export function toOrder(order) {
  const items = order.items.map(item => ({
    productId: item.productId,
    name: item.productName,
    unitPrice: item.unitPrice,
    quantity: item.quantity,
  }))
  return {
    id: order.id,
    orderNumber: shortId(order.id),
    customerId: shortId(order.userId),
    datePlaced: formatDate(order.createdAt),
    lastUpdated: order.updatedAt ? formatDate(order.updatedAt) : null,
    itemCount: items.reduce((count, item) => count + item.quantity, 0),
    total: order.totalPrice,
    status: order.status,
    items,
  }
}

export async function getOrders(signal, page = 1, pageSize = 20) {
  const result = await request(
    `/api/v1/orders?page=${page}&pageSize=${pageSize}`,
    { signal }
  )

  return { items: result.items.map(toOrder), hasNextPage: result.hasNextPage }
}

export async function getOrder(id, signal) {
  return toOrder(await request(`/api/v1/orders/${encodeURIComponent(id)}`, { signal }))
}

export async function placeOrder(lines) {
  const items = lines.map(({ productId, quantity }) => ({ productId, quantity }))
  return toOrder(await request('/api/v1/orders', { method: 'POST', body: { items } }))
}

export async function updateOrderStatus(id, status) {
  return toOrder(await request(`/api/v1/orders/${encodeURIComponent(id)}/status`, { method: 'PATCH', body: { status } }))
}

export function parseStockError(message = '') {
  const match = message.match(/(?:Insufficient stock for '(.+?)'|Product (.+?) has insufficient stock)\..*Available: (\d+)/)
  return match && { name: match[1] ?? match[2], available: Number(match[3]) }
}

export function orderFailure(error) {
  const stock = parseStockError(error.message)
  if (stock) {
    return { productName: stock.name, available: stock.available, message: `Not enough stock for ${stock.name}. Your order was not placed.` }
  }
  if (error.unreachable) {
    return { canRetry: true, message: "We couldn't reach the inventory service. Your order was not placed. Please try again in a moment." }
  }
  return { canRetry: true, message: 'Something went wrong. Your order was not placed. Please try again.' }
}

export function lineTotal(item) {
  return Math.round(item.unitPrice * 100) * item.quantity / 100
}

export function orderTotal(items) {
  return items.reduce((total, item) => total + Math.round(lineTotal(item) * 100), 0) / 100
}

export const formatMoney = value => new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' }).format(value)
