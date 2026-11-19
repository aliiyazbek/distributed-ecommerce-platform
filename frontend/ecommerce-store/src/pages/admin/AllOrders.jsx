import EmptyState from '../../components/EmptyState'
import OrdersList from '../../components/OrdersList'

export default function AllOrders() {
  return (
    <OrdersList title="All orders" showCustomer live>
      <EmptyState title="No orders yet" text="Orders will appear here when customers place them." />
    </OrdersList>
  )
}
