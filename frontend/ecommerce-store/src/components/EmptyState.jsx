import './EmptyState.css'

export default function EmptyState({ title, text, children }) {
  return (
    <div className="empty-state" role="status">
      <h2>{title}</h2>
      {text && <p>{text}</p>}
      {children}
    </div>
  )
}
