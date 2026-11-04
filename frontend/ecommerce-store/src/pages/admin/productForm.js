import { categories } from '../../services/productService.js'

export const emptyProduct = { name: '', description: '', price: '', category: '', stock: '' }

const apiFields = { Name: 'name', Description: 'description', Price: 'price', Category: 'category', AvailableQuantity: 'stock', Image: 'image' }

const maxStock = 2147483647

export function validateProduct(values) {
  const errors = {}
  if (!values.name.trim()) errors.name = 'Product name is required.'
  if (!values.description.trim()) errors.description = 'Description is required.'
  if (!values.price.trim() || !Number.isFinite(Number(values.price)) || Number(values.price) <= 0) {
    errors.price = 'Price must be greater than zero.'
  } else if (!/^\d+(\.\d{1,2})?$/.test(values.price.trim())) {
    errors.price = 'Price can have up to 2 decimal places.'
  }
  if (!categories.includes(values.category)) errors.category = 'Category is required.'
  if (!values.stock.trim()) {
    errors.stock = 'Stock quantity is required.'
  } else if (Number(values.stock) < 0) {
    errors.stock = 'Quantity cannot be negative.'
  } else if (!Number.isInteger(Number(values.stock))) {
    errors.stock = 'Stock quantity must be a whole number.'
  } else if (Number(values.stock) > maxStock) {
    errors.stock = 'Stock quantity is too large.'
  }
  return errors
}

export function mapApiErrors(fields = {}) {
  const errors = {}
  for (const [field, messages] of Object.entries(fields)) {
    if (apiFields[field]) errors[apiFields[field]] = messages.at(-1)
  }
  return errors
}
