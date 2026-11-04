import { useEffect, useRef, useState } from 'react'
import FormField from '../../components/FormField'
import { categories, emptyProduct, submitProduct, validateProduct } from './productForm'
import './AddProduct.css'

export default function AddProduct() {
  const [values, setValues] = useState({ ...emptyProduct })
  const [errors, setErrors] = useState({})
  const [image, setImage] = useState(null)
  const [preview, setPreview] = useState('')
  const [imageError, setImageError] = useState('')
  const [status, setStatus] = useState(null)
  const [submitting, setSubmitting] = useState(false)
  const formRef = useRef(null)
  const fileRef = useRef(null)

  useEffect(() => () => { if (preview) URL.revokeObjectURL(preview) }, [preview])

  function changeValue(event) {
    const { name, value } = event.target
    const next = { ...values, [name]: value }
    setValues(next)
    setStatus(null)
    if (errors[name]) setErrors({ ...errors, [name]: validateProduct(next)[name] })
  }

  function fieldProps(name) {
    return {
      id: name, name, value: values[name], onChange: changeValue, required: true,
      'aria-invalid': Boolean(errors[name]),
      'aria-describedby': errors[name] ? `${name}-error` : undefined,
    }
  }

  function clearImage() {
    setImage(null)
    setPreview('')
    setImageError('')
    setStatus(null)
    if (fileRef.current) fileRef.current.value = ''
  }

  function setProductImage(file) {
    setImage(file)
    setPreview(URL.createObjectURL(file))
  }

  function selectImage(event) {
    const file = event.target.files?.[0]
    if (!file) return
    clearImage()
    if (!['image/png', 'image/jpeg', 'image/webp'].includes(file.type)) {
      setImageError('Choose a PNG, JPG, or WebP image.')
      return
    }
    if (file.size > 5 * 1024 * 1024) {
      setImageError('Choose an image no larger than 5 MB.')
      return
    }
    setProductImage(file)
  }

  async function handleSubmit(event) {
    event.preventDefault()
    if (submitting) return
    setStatus(null)
    const nextErrors = validateProduct(values)
    setErrors(nextErrors)
    const firstError = Object.keys(nextErrors)[0]
    if (firstError || imageError) {
      if (firstError) formRef.current.elements.namedItem(firstError)?.focus()
      else fileRef.current?.focus()
      return
    }
    setSubmitting(true)
    try {
      const result = await submitProduct({
        name: values.name.trim(), description: values.description.trim(),
        price: Number(values.price), category: values.category, stock: Number(values.stock), image,
      })
      setStatus({ type: 'success', message: result.saved
        ? 'Product added successfully.'
        : 'Form submitted successfully. This product has not been saved to a server. Backend integration is not connected yet.' })
    } catch {
      setStatus({ type: 'error', message: 'The product could not be submitted. Please try again.' })
    } finally {
      setSubmitting(false)
    }
  }

  function fillDemo() {
    setValues({ name: 'Brushed Steel Desk Lamp', description: 'A modern desk lamp with a brushed steel finish and an adjustable shade.', price: '24.99', category: 'Home & Living', stock: '12' })
    setErrors({})
    clearImage()
    const canvas = document.createElement('canvas')
    canvas.width = 320
    canvas.height = 240
    const context = canvas.getContext('2d')
    context.fillStyle = '#f1effb'
    context.fillRect(0, 0, 320, 240)
    context.strokeStyle = '#585465'
    context.lineWidth = 10
    context.beginPath()
    context.moveTo(160, 190)
    context.lineTo(160, 110)
    context.lineTo(195, 65)
    context.stroke()
    context.fillStyle = '#a4a0ae'
    context.beginPath()
    context.ellipse(160, 195, 48, 9, 0, 0, Math.PI * 2)
    context.fill()
    context.beginPath()
    context.moveTo(175, 55)
    context.lineTo(220, 92)
    context.lineTo(155, 112)
    context.closePath()
    context.fill()
    // A local PNG keeps the screenshot's demo helper independent of any API.
    const bytes = Uint8Array.from(atob(canvas.toDataURL('image/png').split(',')[1]), char => char.charCodeAt(0))
    setProductImage(new File([bytes], 'demo-desk-lamp.png', { type: 'image/png' }))
  }

  function showErrors() {
    setValues({ ...emptyProduct })
    setErrors(validateProduct(emptyProduct))
    clearImage()
    formRef.current.elements.namedItem('name')?.focus()
  }

  return (
    <main className="admin-page add-product-page">
      <h1>Add product</h1>
      <form className="product-card" ref={formRef} noValidate onSubmit={handleSubmit} aria-label="Add product">
        <fieldset disabled={submitting}>
          <FormField id="name" label="Product name" error={errors.name}>
            <input {...fieldProps('name')} placeholder="e.g. Brushed Steel Desk Lamp" />
          </FormField>
          <FormField id="description" label="Description" error={errors.description}>
            <textarea {...fieldProps('description')} placeholder="Write a short description of the product..." rows={3} />
          </FormField>
          <div className="product-details">
            <FormField id="price" label="Price" error={errors.price}>
              <input {...fieldProps('price')} type="number" min="0" step="any" placeholder="24.99" />
            </FormField>
            <FormField id="category" label="Category" error={errors.category}>
              <select {...fieldProps('category')}>
                <option value="" disabled>Select category...</option>
                {categories.map(category => <option key={category} value={category}>{category}</option>)}
              </select>
            </FormField>
            <FormField id="stock" label="Stock quantity" error={errors.stock}>
              <input {...fieldProps('stock')} type="number" min="0" step="1" placeholder="12" />
            </FormField>
          </div>
          <div className="form-field">
            <label htmlFor="image">Image</label>
            <div className={`image-upload${imageError ? ' invalid' : ''}`}>
              <input ref={fileRef} id="image" type="file" accept="image/png,image/jpeg,image/webp" onChange={selectImage} aria-invalid={Boolean(imageError)} aria-describedby={imageError ? 'image-error image-hint' : 'image-hint'} />
              {image && preview ? (
                <img className="image-preview" src={preview} alt="Selected product preview" onError={() => {
                  clearImage()
                  setImageError('This image could not be opened. Choose another image.')
                }} />
              ) : (
                <span className="upload-icon" aria-hidden="true">
                  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5"><path d="M13 5H6a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-7 M4 17l5-5 4 4 3-3 4 4 M18 2v7 M14.5 5.5h7" /><circle cx="9" cy="8" r="1" /></svg>
                </span>
              )}
              <span className="upload-title">{image ? 'Click to replace product image' : 'Click to upload a product image'}</span>
              <span id="image-hint" className="upload-hint">PNG, JPG or WebP with live preview · Max 5 MB</span>
            </div>
            {(image || imageError) && <div className="image-meta"><span>{image?.name}</span><button type="button" onClick={clearImage}>Remove image</button></div>}
            {imageError && <p id="image-error" className="field-error" role="alert">{imageError}</p>}
          </div>
          <button type="submit" className="submit-product">{submitting ? 'Submitting...' : 'Add product'}</button>
        </fieldset>
        {status && <p className={`form-status ${status.type}`} role={status.type === 'error' ? 'alert' : 'status'}>{status.message}</p>}
      </form>
      <aside className="demo-helpers" aria-label="Form demo helpers">
        <span>Quick demo helpers:</span>
        <div>
          <button type="button" disabled={submitting} onClick={fillDemo}>Autofill valid product + image</button>
          <button type="button" disabled={submitting} className="demo-errors" onClick={showErrors}>Show all 5 field errors</button>
        </div>
      </aside>
    </main>
  )
}
