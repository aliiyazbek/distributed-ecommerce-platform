import { useEffect, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import Alert from '../../components/Alert'
import FormField from '../../components/FormField'
import { routes } from '../../navigation/routes'
import { categories, createProduct } from '../../services/productService.js'
import { emptyProduct, mapApiErrors, validateProduct } from './productForm.js'
import './AddProduct.css'

export default function AddProduct() {
  const navigate = useNavigate()
  const [values, setValues] = useState({ ...emptyProduct })
  const [errors, setErrors] = useState({})
  const [image, setImage] = useState(null)
  const [preview, setPreview] = useState('')
  const [imageError, setImageError] = useState('')
  const [failed, setFailed] = useState(false)
  const [submitting, setSubmitting] = useState(false)
  const formRef = useRef(null)
  const fileRef = useRef(null)

  useEffect(() => () => { if (preview) URL.revokeObjectURL(preview) }, [preview])

  function changeValue(event) {
    const { name, value } = event.target
    const next = { ...values, [name]: value }
    setValues(next)
    setFailed(false)
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
    setFailed(false)
    if (fileRef.current) fileRef.current.value = ''
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
    setImage(file)
    setPreview(URL.createObjectURL(file))
  }

  async function handleSubmit(event) {
    event.preventDefault()
    if (submitting) return
    setFailed(false)
    const nextErrors = validateProduct(values)
    const nextImageError = imageError || (image ? '' : 'A product image is required.')
    setErrors(nextErrors)
    setImageError(nextImageError)
    const firstError = Object.keys(nextErrors)[0]
    if (firstError || nextImageError) {
      if (firstError) formRef.current.elements.namedItem(firstError)?.focus()
      else fileRef.current?.focus()
      return
    }
    setSubmitting(true)
    try {
      const product = await createProduct({
        name: values.name.trim(), description: values.description.trim(),
        price: Number(values.price), category: values.category, stock: Number(values.stock), image,
      })
      navigate(routes.product(product.id), { state: { added: true } })
    } catch (failure) {
      const { image: apiImageError, ...apiErrors } = mapApiErrors(failure.fields)
      setErrors(apiErrors)
      setImageError(apiImageError ?? '')
      setFailed(!apiImageError && Object.keys(apiErrors).length === 0)
      setSubmitting(false)
    }
  }

  return (
    <main className="page add-product-page">
      <h1>Add product</h1>
      <form className="card add-product-form" ref={formRef} noValidate onSubmit={handleSubmit} aria-label="Add product">
        {failed && <Alert>The product could not be added. Please try again.</Alert>}
        <fieldset disabled={submitting}>
          <FormField id="name" label="Product name" error={errors.name}>
            <input {...fieldProps('name')} placeholder="e.g. Brushed Steel Desk Lamp" />
          </FormField>
          <FormField id="description" label="Description" error={errors.description}>
            <textarea {...fieldProps('description')} placeholder="Write a short description of the product..." rows={3} />
          </FormField>
          <div className="product-fields">
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
              <span id="image-hint" className="upload-hint">PNG, JPG or WebP · Max 5 MB</span>
            </div>
            {image && <div className="image-meta"><span>{image.name}</span><button type="button" onClick={clearImage}>Remove image</button></div>}
            {imageError && <p id="image-error" className="field-error" role="alert">{imageError}</p>}
          </div>
          <button type="submit" className="btn btn--block">
            {submitting && <span className="spinner" aria-hidden="true" />}
            {submitting ? 'Adding product...' : 'Add product'}
          </button>
        </fieldset>
      </form>
    </main>
  )
}
