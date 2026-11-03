import './OrderStatusBadge.css'

const statusClasses = {
  Pending: 'pending',
  Shipped: 'shipped',
  Delivered: 'delivered',
  Cancelled: 'cancelled',
}

export default function OrderStatusBadge({ status }) {
  return (
    <span className={`order-status-badge order-status-badge--${statusClasses[status] || 'unknown'}`}>
      <span className="order-status-dot" aria-hidden="true" />
      {status || 'Unknown'}
    </span>
  )
}
