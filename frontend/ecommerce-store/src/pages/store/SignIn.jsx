import { useState } from 'react'
import { Link, Navigate, useLocation } from 'react-router-dom'
import Alert from '../../components/Alert'
import FormField from '../../components/FormField'
import { useAuth } from '../../context/auth-context'
import { isCartPath, routes } from '../../navigation/routes'
import { authErrorMessage, login } from '../../services/authService.js'
import './Auth.css'

export default function SignIn() {
  const { session, expired, signIn } = useAuth()
  const { state } = useLocation()
  const notice = expired ? 'Your session has expired. Please sign in again.' : state?.notice
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [errors, setErrors] = useState({})
  const [error, setError] = useState('')
  const [submitting, setSubmitting] = useState(false)

  if (session) {
    const adminFromCart = session.role === 'Admin' && isCartPath(state?.from)
    return <Navigate to={adminFromCart ? routes.products : state?.from ?? routes.products} replace />
  }

  async function submit(event) {
    event.preventDefault()
    if (submitting) return
    const nextErrors = {}
    if (!email.trim()) nextErrors.email = 'Email is required.'
    if (!password) nextErrors.password = 'Password is required.'
    setErrors(nextErrors)
    setError('')
    if (Object.keys(nextErrors).length) return
    setSubmitting(true)
    try {
      const { token } = await login({ email: email.trim(), password })
      signIn(token)
    } catch (failure) {
      setError(authErrorMessage(failure))
      setSubmitting(false)
    }
  }

  return (
    <main className="auth-page">
      <form className="card auth-card" onSubmit={submit} noValidate>
        <h1>Sign in</h1>
        {error ? <Alert>{error}</Alert> : notice && <Alert type="info">{notice}</Alert>}
        <fieldset disabled={submitting}>
          <FormField id="signin-email" label="Email" error={errors.email}>
            <input id="signin-email" type="email" autoComplete="email" placeholder="you@example.com" value={email} onChange={event => setEmail(event.target.value)} aria-invalid={Boolean(errors.email)} />
          </FormField>
          <FormField id="signin-password" label="Password" error={errors.password}>
            <input id="signin-password" type="password" autoComplete="current-password" placeholder="••••••••" value={password} onChange={event => setPassword(event.target.value)} aria-invalid={Boolean(errors.password)} />
          </FormField>
          <button type="submit" className="btn btn--block">
            {submitting && <span className="spinner" aria-hidden="true" />}
            {submitting ? 'Signing in...' : 'Sign in'}
          </button>
        </fieldset>
        <p className="auth-foot">Don't have an account? <Link to={routes.signUp} state={state}>Create one</Link></p>
      </form>
    </main>
  )
}
