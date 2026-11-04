import { useCallback } from 'react'
import { Link, Navigate, useLocation, useSearchParams } from 'react-router-dom'
import EmptyState from '../../components/EmptyState'
import Pagination from '../../components/Pagination'
import ProductImage from '../../components/ProductImage'
import useLoad from '../../hooks/useLoad'
import { routes } from '../../navigation/routes'
import { formatMoney } from '../../services/orderService.js'
import { categories, getProducts, pageSize } from '../../services/productService.js'
import './Products.css'

function ProductGridSkeleton() {
  return (
    <div className="product-grid" role="status" aria-label="Loading products">
      {Array.from({ length: pageSize }, (_, index) => (
        <div key={index} className="card product-tile">
          <span className="skeleton product-tile-image" />
          <div className="product-tile-body">
            <span className="skeleton skeleton-line" />
            <span className="skeleton skeleton-line skeleton-line--wide" />
          </div>
        </div>
      ))}
    </div>
  )
}

export default function Products() {
  const [params, setParams] = useSearchParams()
  const location = useLocation()
  const category = params.get('category') ?? ''
  const page = Math.max(1, Number(params.get('page')) || 1)
  const load = useCallback(signal => getProducts({ category, page }, signal), [category, page])
  const { loading, data, error, retry } = useLoad(load)

  if (error?.status === 401) return <Navigate to={routes.signIn} state={{ from: location }} replace />

  function show(nextCategory, nextPage) {
    const query = {}
    if (nextCategory) query.category = nextCategory
    if (nextPage > 1) query.page = nextPage
    setParams(query)
  }

  const items = data?.items ?? []
  const firstItem = (page - 1) * pageSize + 1

  return (
    <main className="page products-page">
      <div className="products-head">
        <h1>Products</h1>
        <div className="category-filter" role="group" aria-label="Filter by category">
          {['', ...categories].map(name => (
            <button key={name || 'All'} type="button" className={category === name ? 'active' : undefined} aria-pressed={category === name} onClick={() => show(name, 1)}>
              {name || 'All'}
            </button>
          ))}
        </div>
      </div>

      {loading && <ProductGridSkeleton />}

      {error && (
        <div className="card">
          <EmptyState title="Products couldn't be loaded" text="We had trouble loading the products. Please try again.">
            <button type="button" className="btn" onClick={retry}>Try again</button>
          </EmptyState>
        </div>
      )}

      {data && items.length === 0 && (
        <div className="card">
          <EmptyState title="No products in this category" text={category ? `There are no products in ${category} right now.` : 'There are no products right now.'}>
            {category && <button type="button" className="btn btn--secondary" onClick={() => show('', 1)}>View all products</button>}
          </EmptyState>
        </div>
      )}

      {items.length > 0 && (
        <>
          <div className="product-grid">
            {items.map(product => (
              <Link key={product.id} className="card product-tile" to={routes.product(product.id)} state={{ product, listSearch: location.search }}>
                <ProductImage className="product-tile-image" src={product.imageUrl} alt={product.name} />
                <div className="product-tile-body">
                  <span className="product-tile-category">{product.category}</span>
                  <h2>{product.name}</h2>
                  <span className="product-tile-price">{formatMoney(product.price)}</span>
                </div>
              </Link>
            ))}
          </div>
          <div className="products-foot">
            <span>
              Showing <b>{firstItem}–{firstItem + items.length - 1}</b> of {data.totalItems} products ({pageSize} per page)
            </span>
            <Pagination page={page} totalPages={data.totalPages} onChange={nextPage => show(category, nextPage)} />
          </div>
        </>
      )}
    </main>
  )
}
