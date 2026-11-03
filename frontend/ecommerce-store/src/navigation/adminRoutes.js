export const adminRoutes = {
  addProduct: '#/admin/add-product',
  orders: '#/admin/orders',
  orderDetails: id => `#/admin/orders/${encodeURIComponent(id)}`,
}

export function resolveAdminRoute(hash) {
  if (hash === adminRoutes.orders || hash === `${adminRoutes.orders}/`) return { page: 'orders' }
  const prefix = `${adminRoutes.orders}/`
  if (hash.startsWith(prefix) && hash.length > prefix.length) {
    try {
      return { page: 'orders', orderId: decodeURIComponent(hash.slice(prefix.length)) }
    } catch {
      return { page: 'orders', orderId: null }
    }
  }
  return { page: 'addProduct' }
}
