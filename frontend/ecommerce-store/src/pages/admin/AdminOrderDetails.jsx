import { useState } from 'react'
import OrderStatusBadge from '../../components/OrderStatusBadge'
import OrderProgress from '../../components/OrderProgress'
import { adminRoutes } from '../../navigation/adminRoutes'
import { formatMoney, lineTotal, orderTotal, orderStatuses } from '../../services/orderService'
import './AdminOrderDetails.css'

export default function AdminOrderDetails({ order, onSaveStatus }) {
  const [selectedStatus, setSelectedStatus] = useState(order?.status || 'Pending')
  const [saving, setSaving] = useState(false)
  const [message, setMessage] = useState(null)

  async function handleSave(event) {
    event.preventDefault()
    if (saving) return
    setSaving(true)
    setMessage(null)
    try {
      await onSaveStatus(order.id, selectedStatus)
      setMessage({ type: 'success', text: 'Status updated for this session only. Nothing was saved to a server.' })
    } catch {
      setMessage({ type: 'error', text: 'Could not update the status. Please try again.' })
    } finally {
      setSaving(false)
    }
  }

  return (
    <main className="admin-page order-details-page">
      <a className="back-to-orders" href={adminRoutes.orders}><span aria-hidden="true">←</span> Back to All orders</a>
      {!order ? (
        <section className="order-summary order-not-found">
          <h1>Order not found</h1>
          <p>We couldn’t find this order. Return to All orders to choose an available order.</p>
        </section>
      ) : (
        <>
          <section className="order-summary" aria-labelledby="order-heading">
            <div className="order-summary-top">
              <div>
                <div className="order-title-row"><h1 id="order-heading">Order #{order.orderNumber || order.id}</h1><OrderStatusBadge status={order.status} /></div>
                <div className="order-metadata">
                  <span>Date placed: <span>{order.datePlaced}</span></span>
                  <span>Customer: <span className="order-customer">#{order.customerId}</span></span>
                </div>
              </div>
              <form className="order-status-form" onSubmit={handleSave}>
                <label htmlFor="order-status">Change status:</label>
                <select id="order-status" value={selectedStatus} disabled={saving} onChange={event => { setSelectedStatus(event.target.value); setMessage(null) }}>
                  {orderStatuses.map(status => <option key={status}>{status}</option>)}
                </select>
                <button type="submit" disabled={saving}>{saving ? 'Saving…' : 'Save'}</button>
              </form>
            </div>
            {message && <p className={`order-save-message ${message.type}`} role={message.type === 'error' ? 'alert' : 'status'}>{message.text}</p>}
            <OrderProgress status={order.status} />
          </section>
          <section className="order-items-card" aria-labelledby="items-heading">
            <h2 id="items-heading">Items</h2>
            <div className="order-items-scroll" role="region" aria-labelledby="items-heading" tabIndex={0}>
              <table className="order-items-table" aria-labelledby="items-heading">
                <thead><tr><th scope="col">Product name</th><th scope="col">Unit price</th><th scope="col">Quantity</th><th scope="col">Line total</th></tr></thead>
                <tbody>{order.items.map(item => (
                  <tr key={item.id}><th scope="row">{item.name}</th><td>{formatMoney(item.unitPrice)}</td><td>{item.quantity}</td><td>{formatMoney(lineTotal(item))}</td></tr>
                ))}</tbody>
                <tfoot><tr><th scope="row" colSpan={3}>Order total</th><td>{formatMoney(orderTotal(order.items))}</td></tr></tfoot>
              </table>
            </div>
          </section>
        </>
      )}
    </main>
  )
}
