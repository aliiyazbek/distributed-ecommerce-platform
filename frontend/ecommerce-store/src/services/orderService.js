export const orderStatuses = ['Pending', 'Shipped', 'Delivered', 'Cancelled']

// Ali: replace this local result with the order-status API request and return
// the updated order. Nothing is sent or persisted; refresh resets mock changes.
export async function updateOrderStatus(order, status) {
  if (!order) throw new Error('Order not found.')
  if (!orderStatuses.includes(status)) throw new Error('Choose a valid order status.')
  return { ...order, status }
}

export function lineTotal(item) {
  return Math.round(item.unitPrice * 100) * item.quantity / 100
}

export function orderTotal(items) {
  return items.reduce((total, item) => total + Math.round(lineTotal(item) * 100), 0) / 100
}

export const formatMoney = value => new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' }).format(value)
