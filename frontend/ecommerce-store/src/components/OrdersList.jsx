import { Link, useNavigate } from 'react-router-dom'
import EmptyState from './EmptyState'
import OrderStatusBadge from './OrderStatusBadge'
import useLoad from '../hooks/useLoad'
import { routes } from '../navigation/routes'
import { formatMoney, getOrders } from '../services/orderService.js'
import './OrdersList.css'
import { useCallback, useState } from 'react'

function OrdersSkeleton() {
  return (
    <div className="orders-skeleton" role="status" aria-label="Loading orders">
      {Array.from({ length: 4 }, (_, index) => <span key={index} className="skeleton" />)}
    </div>
  )
}

export default function OrdersList({ title, showCustomer = false, children }) {
const [page, setPage] = useState(1)
const pageSize =20

const loadOrders = useCallback(
  signal => getOrders(signal, page, pageSize),
  [page]
)

const { loading, data, error, retry } = useLoad(loadOrders)
const orders = data?.items
  const navigate = useNavigate()

  return (
    <main className="page orders-page">
      <h1 id="orders-heading">{title}</h1>
      <div className="card orders-card">
        {loading && <OrdersSkeleton />}
        {error && (
          <EmptyState title="Orders couldn't be loaded" text="We had trouble loading the orders. Please try again.">
            <button type="button" className="btn" onClick={retry}>Try again</button>
          </EmptyState>
        )}
        {orders?.length === 0 && page === 1 && children}
        {orders?.length === 0 && page > 1 && <EmptyState title="No orders on this page" text="Use Previous to return to earlier orders." />}
        {orders?.length > 0 && (
          <div className="orders-scroll" role="region" aria-labelledby="orders-heading" tabIndex={0}>
            <table className={`orders-table${showCustomer ? ' orders-table--customer' : ''}`} aria-labelledby="orders-heading">
              <thead>
                <tr>
                  <th scope="col">Order number</th>
                  {showCustomer && <th scope="col">Customer</th>}
                  <th scope="col">Date placed</th>
                  <th scope="col">Items</th>
                  <th scope="col">Total</th>
                  <th scope="col">Status</th>
                  <th scope="col" aria-label="Order details" />
                </tr>
              </thead>
              <tbody>
                {orders.map(order => (
                  <tr key={order.id} onClick={() => navigate(routes.order(order.id))}>
                    <th scope="row" className="order-number">#{order.orderNumber}</th>
                    {showCustomer && <td><span className="customer-id">#{order.customerId}</span></td>}
                    <td>{order.datePlaced}</td>
                    <td>{order.itemCount} {order.itemCount === 1 ? 'item' : 'items'}</td>
                    <td className="order-total">{formatMoney(order.total)}</td>
                    <td><OrderStatusBadge status={order.status} /></td>
                    <td className="order-action">
                      <Link to={routes.order(order.id)} aria-label={`View order #${order.orderNumber}`} onClick={event => event.stopPropagation()}>
                        <svg aria-hidden="true" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.3"><path d="m6 4 4 4-4 4" /></svg>
                      </Link>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
      <div className="orders-pagination">
  <button
    type="button"
    disabled={loading || page === 1}
    onClick={() => setPage(p => p - 1)}
  >
    Previous
  </button>

  <span>Page {page}</span>

  <button
    type="button"
    disabled={loading || !data?.hasNextPage}
    onClick={() => setPage(p => p + 1)}
  >
    Next
  </button>
</div>
    </main>
  )
}
