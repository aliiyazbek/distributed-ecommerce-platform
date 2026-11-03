import './Navbar.css'
import { adminRoutes } from '../navigation/adminRoutes'

export default function Navbar({ activePage = 'addProduct' }) {
  return (
    <header className="navbar">
      <a className="brand" href={adminRoutes.addProduct} aria-label="Store admin home">
        <span className="brand-icon" aria-hidden="true">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6">
            <path d="m12 3 8 4.5v9L12 21l-8-4.5v-9L12 3Z M4 7.5l8 4.5 8-4.5 M12 12v9 M8 5.25l8 4.5" />
          </svg>
        </span>
        <span lang="ar" dir="rtl">أبو محمد شلمونة</span>
      </a>
      <nav className="nav-links" aria-label="Admin navigation">
        <button type="button" disabled title="Products page is not implemented yet">Products</button>
        <a href={adminRoutes.addProduct} className={activePage === 'addProduct' ? 'active' : undefined} aria-current={activePage === 'addProduct' ? 'page' : undefined}>Add product</a>
        <a href={adminRoutes.orders} className={activePage === 'orders' ? 'active' : undefined} aria-current={activePage === 'orders' ? 'page' : undefined}>All orders</a>
      </nav>
      <div className="nav-account">
        <span className="account-email" title="Demo account display only">yzbek.ali.2003@gmail.com</span>
        <button type="button" className="sign-out" disabled title="Authentication is not implemented yet">
          <svg aria-hidden="true" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5"><path d="M10 5H5v14h5 M9 12h12 m-4-4 4 4-4 4" /></svg>
          Sign out
        </button>
      </div>
    </header>
  )
}
