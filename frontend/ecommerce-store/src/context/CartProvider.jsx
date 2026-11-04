import { useCallback, useEffect, useMemo, useState } from 'react'
import { CartContext } from './cart-context'
import { addLine, countItems, readCart, removeLine, saveCart, setLineQuantity } from '../services/cartService.js'

export default function CartProvider({ children }) {
  const [lines, setLines] = useState(readCart)

  useEffect(() => saveCart(lines), [lines])

  useEffect(() => {
    const sync = () => setLines(readCart())
    window.addEventListener('storage', sync)
    return () => window.removeEventListener('storage', sync)
  }, [])

  const addToCart = useCallback((product, quantity) => setLines(current => addLine(current, product, quantity)), [])
  const setQuantity = useCallback((productId, quantity) => setLines(current => setLineQuantity(current, productId, quantity)), [])
  const removeFromCart = useCallback(productId => setLines(current => removeLine(current, productId)), [])
  const clearCart = useCallback(() => setLines([]), [])

  const value = useMemo(
    () => ({ lines, count: countItems(lines), addToCart, setQuantity, removeFromCart, clearCart }),
    [lines, addToCart, setQuantity, removeFromCart, clearCart],
  )

  return <CartContext.Provider value={value}>{children}</CartContext.Provider>
}
