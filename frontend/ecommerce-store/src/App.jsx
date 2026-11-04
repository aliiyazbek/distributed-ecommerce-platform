import { Route, Routes } from 'react-router-dom'
import Layout from './components/Layout'
import RequireRole from './components/RequireRole'
import AddProduct from './pages/admin/AddProduct'
import AllOrders from './pages/admin/AllOrders'
import OrderDetails from './pages/shared/OrderDetails'
import StatusPage from './pages/shared/StatusPage'
import Cart from './pages/store/Cart'
import MyOrders from './pages/store/MyOrders'
import ProductDetails from './pages/store/ProductDetails'
import Products from './pages/store/Products'
import SignIn from './pages/store/SignIn'
import SignUp from './pages/store/SignUp'

export default function App() {
  return (
    <Routes>
      <Route element={<Layout />}>
        <Route index element={<Products />} />
        <Route path="products/:id" element={<ProductDetails />} />
        <Route path="cart" element={<Cart />} />
        <Route path="signin" element={<SignIn />} />
        <Route path="signup" element={<SignUp />} />
        <Route element={<RequireRole />}>
          <Route path="orders/:id" element={<OrderDetails />} />
        </Route>
        <Route element={<RequireRole role="Customer" />}>
          <Route path="orders" element={<MyOrders />} />
        </Route>
        <Route element={<RequireRole role="Admin" />}>
          <Route path="admin/add-product" element={<AddProduct />} />
          <Route path="admin/orders" element={<AllOrders />} />
        </Route>
        <Route path="*" element={<StatusPage kind="not-found" />} />
      </Route>
    </Routes>
  )
}
