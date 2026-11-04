import './OrderProgress.css'
import OrderStatusBadge from './OrderStatusBadge'

const stages = ['Pending', 'Shipped', 'Delivered']

function StageIcon({ stage, completed }) {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" aria-hidden="true">
      {completed ? <path d="m6 12 4 4 8-8" />
        : stage === 'Pending' ? <><circle cx="12" cy="12" r="8" /><path d="M12 7v5l3 2" /></>
        : stage === 'Shipped' ? <><path d="M3 6h11v11H3z M14 10h4l3 4v3h-7" /><circle cx="7" cy="18" r="2" /><circle cx="18" cy="18" r="2" /></>
          : <><path d="m12 3 8 5v9l-8 4-8-4V8l8-5Z M4 8l8 5 8-5 M12 13v8" /><path d="m9 9 2 2 4-4" /></>}
    </svg>
  )
}

export default function OrderProgress({ status }) {
  const current = stages.indexOf(status)
  return (
    <section className="order-progress" aria-label="Order progress">
      <h2>Order progress</h2>
      {status === 'Cancelled' ? <div className="progress-cancelled"><OrderStatusBadge status={status} /><span>This order was cancelled.</span></div> : (
        <ol>
          {stages.map((stage, index) => (
            <li key={stage} className={index < current ? 'progress-completed' : index === current ? 'progress-current' : 'progress-inactive'} aria-current={index === current ? 'step' : undefined} aria-label={`${stage}: ${index < current ? 'completed' : index === current ? 'current' : 'not reached'}`}>
              <span className={`progress-icon${index === current ? ` order-status-badge--${stage.toLowerCase()}` : ''}`}><StageIcon stage={stage} completed={index < current} /></span>
              <span>{stage}</span>
              {index < stages.length - 1 && <span className="progress-connector" aria-hidden="true" />}
            </li>
          ))}
        </ol>
      )}
    </section>
  )
}
