import { useCallback, useState } from 'react'
import { Link, useLocation, useParams } from 'react-router-dom'
import Alert from '../../components/Alert'
import EmptyState from '../../components/EmptyState'
import OrderProgress from '../../components/OrderProgress'
import OrderStatusBadge from '../../components/OrderStatusBadge'
import { useAuth } from '../../context/auth-context'
import useLoad from '../../hooks/useLoad'
import { routes } from '../../navigation/routes'
import { formatMoney, getOrder, lineTotal, orderStatuses, updateOrderStatus } from '../../services/orderService.js'
import StatusPage from './StatusPage'
import './OrderDetails.css'

function StatusForm({ order, onSaved, onFailed }) {
  const [selected, setSelected] = useState(order.status)
  const [saving, setSaving] = useState(false)

  async function save(event) {
    event.preventDefault()
    if (saving) return
    setSaving(true)
    try {
      onSaved(await updateOrderStatus(order.id, selected))
    } catch {
      onFailed()
    } finally {
      setSaving(false)
    }
  }

  return (
    <form className="order-status-form" onSubmit={save}>
      <label htmlFor="order-status">Change status:</label>
      <select id="order-status" value={selected} disabled={saving} onChange={event => setSelected(event.target.value)}>
        {orderStatuses.map(status => <option key={status}>{status}</option>)}
      </select>
      <button type="submit" disabled={saving || selected === order.status}>{saving ? 'Saving…' : 'Save'}</button>
    </form>
  )
}

export default function OrderDetails() {
  const { id } = useParams()
  const { state } = useLocation()
  const { session } = useAuth()
  const load = useCallback(signal => getOrder(id, signal), [id])
  const { loading, data: order, error, retry, setData } = useLoad(load)
  const [placed, setPlaced] = useState(Boolean(state?.placed))
  const [message, setMessage] = useState(null)
  const isAdmin = session.role === 'Admin'
  const backTo = isAdmin ? routes.allOrders : routes.myOrders
  const status = error?.status

  if (status === 403) return <StatusPage kind="denied" message={error.message || undefined} />
  if (status === 400 || status === 404) return <StatusPage kind="not-found" message="Order not found." />

  function statusSaved(updated) {
    setData(updated)
    setMessage({ type: 'success', text: `Order status updated to ${updated.status}.` })
  }

  return (
    <main className="page order-details-page">
      {placed && <Alert type="success" onDismiss={() => setPlaced(false)}>Your order has been placed.</Alert>}
      <Link className="back-link" to={backTo}><span aria-hidden="true">←</span> Back to {isAdmin ? 'All orders' : 'My orders'}</Link>

      {loading && (
        <section className="order-summary" role="status" aria-label="Loading order">
          <span className="skeleton order-loading-line" />
          <span className="skeleton order-loading-line" />
        </section>
      )}

      {error && (
        <div className="card">
          <EmptyState title="This order couldn't be loaded" text="Please try again.">
            <button type="button" className="btn" onClick={retry}>Try again</button>
          </EmptyState>
        </div>
      )}

      {order && (
        <>
          <section className="order-summary" aria-labelledby="order-heading">
            <div className="order-summary-top">
              <div>
                <div className="order-title-row">
                  <h1 id="order-heading">Order #{order.orderNumber}</h1>
                  <OrderStatusBadge status={order.status} />
                </div>
                <div className="order-metadata">
                  <span>Date placed: <span>{order.datePlaced}</span></span>
                  {order.lastUpdated && <span>Last updated: <span>{order.lastUpdated}</span></span>}
                  {isAdmin && <span>Customer: <span className="order-customer">#{order.customerId}</span></span>}
                </div>
              </div>
              {isAdmin && (
                <StatusForm
                  key={order.status}
                  order={order}
                  onSaved={statusSaved}
                  onFailed={() => setMessage({ type: 'error', text: 'Could not update the status. Please try again.' })}
                />
              )}
            </div>
            {message && <p className={`order-save-message ${message.type}`} role={message.type === 'error' ? 'alert' : 'status'}>{message.text}</p>}
            <OrderProgress status={order.status} />
          </section>
          <section className="order-items-card" aria-labelledby="items-heading">
            <h2 id="items-heading">Items</h2>
            <div className="order-items-scroll" role="region" aria-labelledby="items-heading" tabIndex={0}>
              <table className="order-items-table" aria-labelledby="items-heading">
                <thead>
                  <tr><th scope="col">Product name</th><th scope="col">Unit price</th><th scope="col">Quantity</th><th scope="col">Line total</th></tr>
                </thead>
                <tbody>
                  {order.items.map(item => (
                    <tr key={item.productId}><th scope="row">{item.name}</th><td>{formatMoney(item.unitPrice)}</td><td>{item.quantity}</td><td>{formatMoney(lineTotal(item))}</td></tr>
                  ))}
                </tbody>
                <tfoot>
                  <tr><th scope="row" colSpan={3}>Order total</th><td>{formatMoney(order.total)}</td></tr>
                </tfoot>
              </table>
            </div>
          </section>
        </>
      )}
    </main>
  )
}
