export const routes = {
  products: '/',
  product: id => `/products/${encodeURIComponent(id)}`,
  cart: '/cart',
  signIn: '/signin',
  signUp: '/signup',
  myOrders: '/orders',
  order: id => `/orders/${encodeURIComponent(id)}`,
  addProduct: '/admin/add-product',
  allOrders: '/admin/orders',
}

export const isCartPath = location => (location?.pathname ?? location) === routes.cart
