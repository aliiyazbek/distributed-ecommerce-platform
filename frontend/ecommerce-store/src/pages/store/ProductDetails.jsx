import { useCallback, useEffect, useState } from 'react'
import { Link, Navigate, useLocation, useParams } from 'react-router-dom'
import Alert from '../../components/Alert'
import EmptyState from '../../components/EmptyState'
import ProductImage from '../../components/ProductImage'
import QuantityPicker from '../../components/QuantityPicker'
import { useAuth } from '../../context/auth-context'
import { useCart } from '../../context/cart-context'
import useLoad from '../../hooks/useLoad'
import { routes } from '../../navigation/routes'
import { formatMoney } from '../../services/orderService.js'
import { getProduct, getStock } from '../../services/productService.js'
import StatusPage from '../shared/StatusPage'
import './ProductDetails.css'

function StockStatus({ stock }) {
  if (stock.loading) return <p className="stock stock--loading"><span className="spinner" aria-hidden="true" />Checking stock...</p>
  if (stock.error) return <p className="stock stock--down" role="alert">Stock information is unavailable right now. Please try again later.</p>
  if (stock.data === 0) return <p className="stock stock--out">Out of stock</p>
  return <p className="stock stock--in">{stock.data} in stock</p>
}

function BuyBox({ product, stock }) {
  const { addToCart } = useCart()
  const [quantity, setQuantity] = useState(1)
  const [added, setAdded] = useState(false)
  const available = stock.data > 0
  const amount = available ? Math.min(quantity, stock.data) : 1

  useEffect(() => {
    if (!added) return undefined
    const timer = setTimeout(() => setAdded(false), 2500)
    return () => clearTimeout(timer)
  }, [added])

  function add() {
    addToCart(product, amount)
    setAdded(true)
  }

  return (
    <div className="buy-box">
      <div className="buy-box-row">
        <QuantityPicker value={amount} max={stock.data} label="quantity" disabled={!available} onChange={setQuantity} />
        <button type="button" className="btn btn--block" disabled={!available} onClick={add}>Add to cart</button>
      </div>
      {added && <p className="buy-box-added" role="status">Added to cart</p>}
    </div>
  )
}

export default function ProductDetails() {
  const { id } = useParams()
  const location = useLocation()
  const { session } = useAuth()
  const loadProduct = useCallback(signal => getProduct(id, signal), [id])
  const loadStock = useCallback(signal => getStock(id, signal), [id])
  const details = useLoad(loadProduct)
  const stock = useLoad(loadStock)
  const [productAdded, setProductAdded] = useState(Boolean(location.state?.added))
  const product = details.data ?? location.state?.product
  const status = details.error?.status

  if (status === 401) return <Navigate to={routes.signIn} state={{ from: location }} replace />
  if (status === 400 || status === 404) return <StatusPage kind="not-found" message="Product not found." />

  return (
    <main className="page product-details-page">
      {productAdded && <Alert type="success" onDismiss={() => setProductAdded(false)}>Product added.</Alert>}
      <Link className="back-link" to={routes.products + (location.state?.listSearch ?? '')}><span aria-hidden="true">←</span> Back to Products</Link>

      {!product && details.loading && (
        <div className="card product-details" role="status" aria-label="Loading product">
          <span className="skeleton product-details-image" />
          <div className="product-details-body">
            <span className="skeleton product-details-line" />
            <span className="skeleton product-details-line" />
          </div>
        </div>
      )}

      {!product && details.error && (
        <div className="card">
          <EmptyState title="This product couldn't be loaded" text="Please try again.">
            <button type="button" className="btn" onClick={details.retry}>Try again</button>
          </EmptyState>
        </div>
      )}

      {product && (
        <article className="card product-details">
          <ProductImage className="product-details-image" src={product.imageUrl} alt={product.name} />
          <div className="product-details-body">
            <span className="product-details-category">{product.category}</span>
            <h1>{product.name}</h1>
            <p className="product-details-price">{formatMoney(product.price)}</p>
            <p className="product-details-description">{product.description}</p>
            <div className="stock-area">
              <StockStatus stock={stock} />
            </div>
            {session?.role !== 'Admin' && <BuyBox product={product} stock={stock} />}
          </div>
        </article>
      )}
    </main>
  )
}
