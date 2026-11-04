import { useState } from 'react'
import { Link, Navigate, useLocation } from 'react-router-dom'
import Alert from '../../components/Alert'
import FormField from '../../components/FormField'
import { useAuth } from '../../context/auth-context'
import { isCartPath, routes } from '../../navigation/routes'
import { authErrorMessage, register } from '../../services/authService.js'
import { validateAccount } from './accountForm.js'
import './Auth.css'

export default function SignUp() {
  const { session, signIn } = useAuth()
  const { state } = useLocation()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [errors, setErrors] = useState({})
  const [error, setError] = useState('')
  const [submitting, setSubmitting] = useState(false)

  if (session) return <Navigate to={isCartPath(state?.from) ? routes.cart : routes.products} replace />

  async function submit(event) {
    event.preventDefault()
    if (submitting) return
    const account = { email: email.trim(), password }
    const nextErrors = validateAccount(account)
    setErrors(nextErrors)
    setError('')
    if (Object.keys(nextErrors).length) return
    setSubmitting(true)
    try {
      const { token } = await register(account)
      signIn(token)
    } catch (failure) {
      if (failure.fields?.Email || failure.fields?.Password) {
        setErrors({ email: failure.fields.Email?.[0], password: failure.fields.Password?.[0] })
      } else {
        setError(authErrorMessage(failure))
      }
      setSubmitting(false)
    }
  }

  return (
    <main className="auth-page">
      <form className="card auth-card" onSubmit={submit} noValidate>
        <h1>Create account</h1>
        {error && <Alert>{error}</Alert>}
        <fieldset disabled={submitting}>
          <FormField id="signup-email" label="Email" error={errors.email}>
            <input id="signup-email" type="email" autoComplete="email" placeholder="you@example.com" value={email} onChange={event => setEmail(event.target.value)} aria-invalid={Boolean(errors.email)} />
          </FormField>
          <FormField id="signup-password" label="Password" hint="At least 6 characters, with one uppercase letter and one number." error={errors.password}>
            <input id="signup-password" type="password" autoComplete="new-password" placeholder="••••••••" value={password} onChange={event => setPassword(event.target.value)} aria-invalid={Boolean(errors.password)} />
          </FormField>
          <button type="submit" className="btn btn--block">
            {submitting && <span className="spinner" aria-hidden="true" />}
            {submitting ? 'Creating account...' : 'Create account'}
          </button>
        </fieldset>
        <p className="auth-foot">Already have an account? <Link to={routes.signIn} state={state}>Sign in</Link></p>
      </form>
    </main>
  )
}
