import './Alert.css'

export default function Alert({ type = 'error', children, actionLabel, onAction, onDismiss }) {
  return (
    <div className={`alert alert--${type}`} role={type === 'error' ? 'alert' : 'status'}>
      <span className="alert-text">{children}</span>
      {actionLabel && <button type="button" className="alert-action" onClick={onAction}>{actionLabel}</button>}
      {onDismiss && <button type="button" className="alert-dismiss" aria-label="Dismiss message" onClick={onDismiss}>×</button>}
    </div>
  )
}
