import OrderStatusBadge from '../../components/OrderStatusBadge'
import { mockOrders } from '../../data/mockOrders'
import { adminRoutes } from '../../navigation/adminRoutes'
import './AllOrders.css'

const currency = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' })

export default function AllOrders({ orders = mockOrders }) {
  return (
    <main className="admin-page all-orders-page">
      <h1 id="orders-heading">All orders</h1>
      <div className="orders-card">
        {orders.length === 0 ? (
          <div className="orders-empty" role="status">
            <h2>No orders yet</h2>
            <p>Orders will appear here when they are available.</p>
          </div>
        ) : (
          <div className="orders-scroll" role="region" aria-labelledby="orders-heading" tabIndex={0}>
            <table className="orders-table" aria-labelledby="orders-heading">
              <colgroup>
                <col className="order-number-column" /><col className="customer-column" />
                <col className="date-column" /><col className="items-column" />
                <col className="total-column" /><col className="status-column" /><col className="action-column" />
              </colgroup>
              <thead>
                <tr>
                  <th scope="col">Order number</th><th scope="col">Customer</th>
                  <th scope="col">Date placed</th><th scope="col">Items</th>
                  <th scope="col">Total</th><th scope="col">Status</th><th scope="col" aria-label="Order details" />
                </tr>
              </thead>
              <tbody>
                {orders.map(order => (
                  <tr key={order.id}>
                    <th scope="row" className="order-number">#{order.orderNumber || order.id}</th>
                    <td><span className="customer-id">#{order.customerId}</span></td>
                    <td>{order.datePlaced}</td>
                    <td>{order.itemCount} {order.itemCount === 1 ? 'item' : 'items'}</td>
                    <td className="order-total">{currency.format(order.total)}</td>
                    <td><OrderStatusBadge status={order.status} /></td>
                    <td className="order-action">
                      <a href={adminRoutes.orderDetails(order.id)} aria-label={`View order #${order.orderNumber || order.id}`}>
                        <svg aria-hidden="true" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.3"><path d="m6 4 4 4-4 4" /></svg>
                      </a>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </main>
  )
}
