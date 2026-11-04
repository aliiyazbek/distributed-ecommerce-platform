import { useState } from "react";

import Navbar from "./components/Navbar";
import SignUp from "src/SignUp.jsx";
import SignIn from "./pages/SigIn";
import Products from "./pages/Products";
import Cart from "./pages/Cart";

export default function App() {
  const [accounts, setAccounts] = useState([]);
  const [user, setUser] = useState(null);
  const [page, setPage] = useState("signin");
  const [cart, setCart] = useState([]);
  const [placed, setPlaced] = useState(false);

  const cartCount = cart.reduce((s, i) => s + i.qty, 0);

  const addToCart = (p) =>
    setCart((c) => (c.some((i) => i.id === p.id) ? c.map((i) => (i.id === p.id ? { ...i, qty: i.qty + 1 } : i)) : [...c, { ...p, qty: 1 }]));
  const setQty = (id, qty) => setCart((c) => c.map((i) => (i.id === id ? { ...i, qty: Math.max(1, qty) } : i)));
  const remove = (id) => setCart((c) => c.filter((i) => i.id !== id));
  const placeOrder = () => { setCart([]); setPlaced(true); };
  const signOut = () => { setUser(null); setCart([]); setPage("signin"); };
  const login = (u) => { setUser(u); setPage("products"); };

  let content;
  if (!user) {
    content =
      page === "signup" ? (
        <SignUp
          accounts={accounts}
          goSignIn={() => setPage("signin")}
          onCreate={(acc) => { setAccounts([...accounts, acc]); login({ email: acc.email }); }}
        />
      ) : (
        <SignIn accounts={accounts} goSignUp={() => setPage("signup")} onSuccess={login} />
      );
  } else if (page === "cart") {
    content = <Cart items={cart} setQty={setQty} remove={remove} placeOrder={placeOrder} placed={placed} />;
  } else if (page === "orders") {
    content = (
      <div className="container">
        <h1>My orders</h1>
        <div className="card empty" style={{ marginTop: 20 }}>No orders yet.</div>
      </div>
    );
  } else {
    content = <Products onAdd={addToCart} />;
  }

  return (
    <>
      <Navbar
        user={user}
        page={page}
        setPage={(p) => { setPlaced(false); setPage(p); }}
        cartCount={cartCount}
        onSignOut={signOut}
      />
      {content}
    </>
  );
}
