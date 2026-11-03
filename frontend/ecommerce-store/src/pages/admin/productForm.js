export const categories = ['Electronics', 'Clothing', 'Home & Living', 'Beauty', 'Sports', 'Books', 'Other']

export const emptyProduct = { name: '', description: '', price: '', category: '', stock: '' }

export function validateProduct(values) {
  const errors = {}
  if (!values.name.trim()) errors.name = 'Enter a product name.'
  if (!values.description.trim()) errors.description = 'Enter a product description.'
  if (!values.price.trim() || !Number.isFinite(Number(values.price)) || Number(values.price) <= 0) {
    errors.price = 'Enter a price greater than zero.'
  }
  if (!categories.includes(values.category)) errors.category = 'Select a category.'
  if (!values.stock.trim() || !Number.isSafeInteger(Number(values.stock)) || Number(values.stock) < 0) {
    errors.stock = 'Enter a whole number of zero or more.'
  }
  return errors
}

// Integration boundary for Ali: replace this local result with the API call.
// product.image is a File or null; use FormData when the API supports uploads.
// This function deliberately makes no network requests and persists nothing.
export async function submitProduct(product) {
  return { saved: false, product }
}
