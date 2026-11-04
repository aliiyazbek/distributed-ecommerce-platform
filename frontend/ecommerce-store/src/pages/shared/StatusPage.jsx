import { Link } from 'react-router-dom'
import { routes } from '../../navigation/routes'
import './StatusPage.css'

const pages = {
  denied: { title: 'Access denied', message: "You don't have permission to view this page." },
  'not-found': { title: 'Not found', message: "The page you're looking for doesn't exist." },
}

export default function StatusPage({ kind, message }) {
  const page = pages[kind]

  return (
    <main className="status-page">
      <section className="card status-card">
        <h1>{page.title}</h1>
        <p>{message ?? page.message}</p>
        <Link className="btn" to={routes.products}>Back to Products</Link>
      </section>
    </main>
  )
}
