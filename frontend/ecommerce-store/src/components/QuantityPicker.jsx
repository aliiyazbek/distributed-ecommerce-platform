import './QuantityPicker.css'

export default function QuantityPicker({ value, max, label, disabled, onChange }) {
  return (
    <div className="quantity-picker">
      <button type="button" aria-label={`Decrease ${label}`} disabled={disabled || value <= 1} onClick={() => onChange(value - 1)}>−</button>
      <span aria-live="polite">{value}</span>
      <button type="button" aria-label={`Increase ${label}`} disabled={disabled || value >= max} onClick={() => onChange(value + 1)}>+</button>
    </div>
  )
}
