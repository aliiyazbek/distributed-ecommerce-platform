import { useState } from 'react'
import { Link, useLocation, useNavigate } from 'react-router-dom'
import Alert from '../../components/Alert'
import EmptyState from '../../components/EmptyState'
import ProductImage from '../../components/ProductImage'
import QuantityPicker from '../../components/QuantityPicker'
import { useAuth } from '../../context/auth-context'
import { useCart } from '../../context/cart-context'
import { routes } from '../../navigation/routes'
import { formatMoney, lineTotal, orderFailure, orderTotal, placeOrder } from '../../services/orderService.js'
import StatusPage from '../shared/StatusPage'
import './Cart.css'

export default function Cart() {
  const { session } = useAuth()
  const { lines, setQuantity, removeFromCart, clearCart } = useCart()
  const location = useLocation()
  const navigate = useNavigate()
  const [placing, setPlacing] = useState(false)
  const [failure, setFailure] = useState(null)

  if (session?.role === 'Admin') return <StatusPage kind="denied" />

  async function submit() {
    if (placing) return
    if (!session) {
      navigate(routes.signIn, { state: { from: location, notice: 'Please sign in to place your order.' } })
      return
    }
    setPlacing(true)
    setFailure(null)
    try {
      const order = await placeOrder(lines)
      clearCart()
      navigate(routes.order(order.id), { state: { placed: true } })
    } catch (error) {
      setFailure(orderFailure(error))
      setPlacing(false)
    }
  }

  function changeQuantity(productId, quantity) {
    setFailure(null)
    setQuantity(productId, quantity)
  }

  function remove(productId) {
    setFailure(null)
    removeFromCart(productId)
  }

  return (
    <main className="page cart-page">
      <h1>Cart</h1>
      {lines.length === 0 ? (
        <div className="card">
          <EmptyState title="Your cart is empty" text="Browse the products to add something to your cart.">
            <Link className="btn" to={routes.products}>Back to Products</Link>
          </EmptyState>
        </div>
      ) : (
        <>
          {failure && <Alert actionLabel={failure.canRetry ? 'Try again' : undefined} onAction={submit}>{failure.message}</Alert>}
          <div className="cart-layout">
            <section className="card cart-lines" aria-label="Cart items">
              {lines.map(line => {
                const short = failure?.productName === line.name
                return (
                  <div key={line.productId} className={`cart-line${short ? ' cart-line--short' : ''}`}>
                    <Link to={routes.product(line.productId)} tabIndex={-1} aria-hidden="true">
                      <ProductImage className="cart-line-image" src={line.imageUrl} alt="" />
                    </Link>
                    <div className="cart-line-info">
                      <Link className="cart-line-name" to={routes.product(line.productId)}>{line.name}</Link>
                      <span className="cart-line-price">{formatMoney(line.unitPrice)} each</span>
                      {short && <span className="cart-line-problem">{failure.available === 0 ? 'Out of stock' : `Only ${failure.available} available`}</span>}
                    </div>
                    <QuantityPicker value={line.quantity} label={`quantity of ${line.name}`} disabled={placing} onChange={quantity => changeQuantity(line.productId, quantity)} />
                    <span className="cart-line-total">{formatMoney(lineTotal(line))}</span>
                    <button type="button" className="cart-line-remove" aria-label={`Remove ${line.name}`} disabled={placing} onClick={() => remove(line.productId)}>
                      <svg aria-hidden="true" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5"><path d="M4 7h16 M9 7V4h6v3 M6 7l1 13h10l1-13 M10 11v5 M14 11v5" /></svg>
                    </button>
                  </div>
                )
              })}
            </section>
            <aside className="card cart-summary">
              <h2>Order summary</h2>
              <div className="cart-total">
                <span>Order total</span>
                <strong>{formatMoney(orderTotal(lines))}</strong>
              </div>
              <button type="button" className="btn btn--block" disabled={placing} onClick={submit}>
                {placing && <span className="spinner" aria-hidden="true" />}
                {placing ? 'Placing order...' : 'Place order'}
              </button>
              <p>Your whole cart is placed as one order.</p>
            </aside>
          </div>
        </>
      )}
    </main>
  )
}
