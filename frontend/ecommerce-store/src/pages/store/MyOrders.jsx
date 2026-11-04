import { Link } from 'react-router-dom'
import EmptyState from '../../components/EmptyState'
import OrdersList from '../../components/OrdersList'
import { routes } from '../../navigation/routes'

export default function MyOrders() {
  return (
    <OrdersList title="My orders">
      <EmptyState title="You haven't placed any orders yet.">
        <Link className="btn" to={routes.products}>Browse Products</Link>
      </EmptyState>
    </OrdersList>
  )
}
