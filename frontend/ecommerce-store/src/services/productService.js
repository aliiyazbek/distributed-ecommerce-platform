import { request } from './apiClient.js'

export const categories = ['Electronics', 'Clothing', 'Books', 'Home', 'Accessories', 'Stationery', 'Outdoors']

export const pageSize = 10

export function getProducts({ category, page }, signal) {
  const query = new URLSearchParams({ page, pageSize })
  if (category) query.set('category', category)
  return request(`/api/v1/products?${query}`, { signal })
}

export const getProduct = (id, signal) => request(`/api/v1/products/${encodeURIComponent(id)}`, { signal })

export function createProduct({ name, description, price, category, stock, image }) {
  const body = new FormData()
  body.append('Name', name)
  body.append('Description', description)
  body.append('Price', price)
  body.append('Category', category)
  body.append('AvailableQuantity', stock)
  body.append('Image', image)
  return request('/api/v1/products/CreateProduct', { method: 'POST', body, timeout: 30000 })
}
