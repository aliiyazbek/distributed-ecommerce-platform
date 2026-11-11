import { Link, NavLink, useLocation, useNavigate } from 'react-router-dom'
import './Navbar.css'
import { useAuth } from '../context/auth-context'
import { useCart } from '../context/cart-context'
import { routes } from '../navigation/routes'

export default function Navbar() {
  const { session, signOut } = useAuth()
  const { count, clearCart } = useCart()
  const { pathname } = useLocation()
  const navigate = useNavigate()
  const isAdmin = session?.role === 'Admin'
  const onProducts = pathname === routes.products || pathname.startsWith('/products')
  const onOrder = pathname.startsWith('/orders/')

  function handleSignOut() {
    clearCart()
    signOut()
    navigate(routes.signIn)
  }

  return (
    <header className="navbar">
      <Link className="brand" to={routes.products} aria-label="Store home">
        <span className="brand-icon" aria-hidden="true">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6">
            <path d="m12 3 8 4.5v9L12 21l-8-4.5v-9L12 3Z M4 7.5l8 4.5 8-4.5 M12 12v9 M8 5.25l8 4.5" />
          </svg>
        </span>
        <span>Kinstore</span>
      </Link>
      <nav className="nav-links" aria-label="Main navigation">
        <Link to={routes.products} className={onProducts ? 'active' : undefined} aria-current={onProducts ? 'page' : undefined}>Products</Link>
          {!isAdmin && (
    <NavLink to={routes.cart}>
      Cart <span className="nav-count">{count}</span>
    </NavLink>
  )}  
          {session?.role === 'Customer' && <NavLink to={routes.myOrders}>My orders</NavLink>}
        {isAdmin && <NavLink to={routes.addProduct}>Add product</NavLink>}
        {isAdmin && <NavLink to={routes.allOrders} className={({ isActive }) => (isActive || onOrder ? 'active' : undefined)}>All orders</NavLink>}
        {!session && <NavLink to={routes.signIn}>Sign in</NavLink>}
        {!session && <NavLink to={routes.signUp} className={({ isActive }) => `nav-primary${isActive ? ' active' : ''}`}>Create account</NavLink>}
      </nav>
      {session && (
        <div className="nav-account">
          <span className="account-email" title={session.email}>{session.email}</span>
          <button type="button" className="sign-out" onClick={handleSignOut}>
            <svg aria-hidden="true" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5"><path d="M10 5H5v14h5 M9 12h12 m-4-4 4 4-4 4" /></svg>
            Sign out
          </button>
        </div>
      )}
    </header>
  )
}
