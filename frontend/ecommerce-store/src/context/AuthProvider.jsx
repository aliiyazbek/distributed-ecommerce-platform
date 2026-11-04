import { useCallback, useEffect, useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { AuthContext } from './auth-context'
import { routes } from '../navigation/routes'
import { setUnauthorizedHandler } from '../services/apiClient.js'
import { clearToken, readSession, readToken, saveToken, toSession } from '../services/session.js'

export default function AuthProvider({ children }) {
  const [session, setSession] = useState(readSession)
  const [expired, setExpired] = useState(false)
  const navigate = useNavigate()

  const signIn = useCallback(token => {
    saveToken(token)
    setSession(toSession(token))
    setExpired(false)
  }, [])

  const signOut = useCallback(() => {
    clearToken()
    setSession(null)
  }, [])

  const expire = useCallback(() => {
    const { pathname, search } = window.location
    signOut()
    setExpired(true)
    navigate(routes.signIn, { state: { from: pathname + search } })
  }, [navigate, signOut])

  useEffect(() => setUnauthorizedHandler(expire), [expire])

  useEffect(() => {
    const sync = () => setSession(current => (current?.token === readToken() ? current : readSession()))
    window.addEventListener('storage', sync)
    return () => window.removeEventListener('storage', sync)
  }, [])

  useEffect(() => {
    if (!session) return undefined
    const timer = setTimeout(expire, session.expiresAt - Date.now())
    return () => clearTimeout(timer)
  }, [session, expire])

  const value = useMemo(() => ({ session, expired, signIn, signOut }), [session, expired, signIn, signOut])

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}
