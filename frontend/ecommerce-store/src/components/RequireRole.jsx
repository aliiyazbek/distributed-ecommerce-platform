import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { useAuth } from '../context/auth-context'
import { routes } from '../navigation/routes'
import StatusPage from '../pages/shared/StatusPage'

export default function RequireRole({ role }) {
  const { session } = useAuth()
  const location = useLocation()

  if (!session) return <Navigate to={routes.signIn} state={{ from: location }} replace />
  if (role && session.role !== role) return <StatusPage kind="denied" />
  return <Outlet />
}
