import { money } from "../data";

export default function Cart({ items, setQty, remove, placeOrder, placed }) {
  const total = items.reduce((s, i) => s + i.price * i.qty, 0);

  return (
    <div className="container">
      <h1>Cart</h1>
      {placed && <div className="toast">Order placed. Thank you!</div>}
      <div className="cart-layout">
        <section className="card">
          {items.length === 0 ? (
            <div className="empty">Your cart is empty. Add something from Products.</div>
          ) : (
            items.map((i) => (
              <div className="row" key={i.id}>
                <div className="thumb" style={{ background: i.bg }} />
                <div className="info">
                  <b>{i.name}</b>
                  <div className="each mono">{money(i.price)} each</div>
                </div>
                <div className="qty">
                  <button onClick={() => setQty(i.id, i.qty - 1)} disabled={i.qty <= 1} aria-label="Decrease">−</button>
                  <span>{i.qty}</span>
                  <button onClick={() => setQty(i.id, i.qty + 1)} aria-label="Increase">+</button>
                </div>
                <div className="line-total mono">{money(i.price * i.qty)}</div>
                <button className="trash" onClick={() => remove(i.id)} aria-label="Remove">🗑</button>
              </div>
            ))
          )}
        </section>

        <aside className="card summary">
          <h3>Order summary</h3>
          <div className="total">
            <span>Order total</span>
            <strong className="mono">{money(total)}</strong>
          </div>
          <button className="btn" disabled={!items.length} onClick={placeOrder}>Place order</button>
          <p className="note">Your whole cart is placed as one order.</p>
        </aside>
      </div>
    </div>
  );
}
