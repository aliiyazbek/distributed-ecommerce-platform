const storageKey = 'store.cart'

export function readCart() {
  try {
    return JSON.parse(localStorage.getItem(storageKey)) ?? []
  } catch {
    return []
  }
}

export const saveCart = lines => localStorage.setItem(storageKey, JSON.stringify(lines))

export function addLine(lines, product, quantity) {
  if (lines.some(line => line.productId === product.id)) {
    return lines.map(line => line.productId === product.id ? { ...line, quantity: line.quantity + quantity } : line)
  }
  return [...lines, { productId: product.id, name: product.name, unitPrice: product.price, imageUrl: product.imageUrl, quantity }]
}

export function setLineQuantity(lines, productId, quantity) {
  return lines.map(line => line.productId === productId ? { ...line, quantity: Math.max(1, quantity) } : line)
}

export const removeLine = (lines, productId) => lines.filter(line => line.productId !== productId)

export const countItems = lines => lines.reduce((count, line) => count + line.quantity, 0)
