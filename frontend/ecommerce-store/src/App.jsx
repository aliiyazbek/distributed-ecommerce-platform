import { useEffect, useState } from 'react'
import Navbar from './components/Navbar'
import AddProduct from './pages/admin/AddProduct'
import AllOrders from './pages/admin/AllOrders'
import AdminOrderDetails from './pages/admin/AdminOrderDetails'
import { mockOrders } from './data/mockOrders'
import { updateOrderStatus } from './services/orderService'
import { resolveAdminRoute } from './navigation/adminRoutes'
import './pages/admin/AdminLayout.css'

export default function App() {
  const [route, setRoute] = useState(() => resolveAdminRoute(window.location.hash))
  const [orders, setOrders] = useState(mockOrders)

  async function saveOrderStatus(id, status) {
    const updated = await updateOrderStatus(orders.find(order => order.id === id), status)
    setOrders(current => current.map(order => order.id === id ? updated : order))
  }

  useEffect(() => {
    const syncRoute = () => setRoute(resolveAdminRoute(window.location.hash))
    window.addEventListener('hashchange', syncRoute)
    return () => window.removeEventListener('hashchange', syncRoute)
  }, [])

  return (
    <>
      <Navbar activePage={route.page} />
      {route.page === 'orders'
        ? route.orderId !== undefined
          ? <AdminOrderDetails key={route.orderId} order={orders.find(order => order.id === route.orderId)} onSaveStatus={saveOrderStatus} />
          : <AllOrders orders={orders} />
        : <AddProduct />}
    </>
  )
}
