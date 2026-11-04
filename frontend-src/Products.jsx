import { useMemo, useState } from "react";
import { PRODUCTS, CATEGORIES, PAGE_SIZE, money } from "../data";

export default function Products({ onAdd }) {
  const [cat, setCat] = useState("All");
  const [pageNo, setPageNo] = useState(1);

  const filtered = useMemo(() => (cat === "All" ? PRODUCTS : PRODUCTS.filter((p) => p.cat === cat)), [cat]);
  const pages = Math.max(1, Math.ceil(filtered.length / PAGE_SIZE));
  const start = (pageNo - 1) * PAGE_SIZE;
  const visible = filtered.slice(start, start + PAGE_SIZE);

  return (
    <div className="container">
      <div className="head">
        <h1>Products</h1>
        <div className="chips">
          {CATEGORIES.map((c) => (
            <button key={c} className={`chip ${cat === c ? "active" : ""}`} onClick={() => { setCat(c); setPageNo(1); }}>{c}</button>
          ))}
        </div>
      </div>

      <div className="grid">
        {visible.map((p) => (
          <article key={p.id} className="card product">
            <div className="img" style={{ background: p.bg }} role="img" aria-label={p.name} />
            <div className="body">
              <div className="cat">{p.cat}</div>
              <div className="pname">{p.name}</div>
              <div className="price mono">{money(p.price)}</div>
              <button className="add" onClick={() => onAdd(p)}>Add to cart</button>
            </div>
          </article>
        ))}
      </div>

      <div className="pager">
        <span>
          Showing <b>{filtered.length ? start + 1 : 0}–{start + visible.length}</b> of {filtered.length} products ({PAGE_SIZE} per page)
        </span>
        <div className="pager-btns">
          <button className="pg" disabled={pageNo === 1} onClick={() => setPageNo(pageNo - 1)}>‹ Previous</button>
          {Array.from({ length: pages }, (_, i) => (
            <button key={i} className={`pg ${pageNo === i + 1 ? "active" : ""}`} onClick={() => setPageNo(i + 1)}>{i + 1}</button>
          ))}
          <button className="pg" disabled={pageNo === pages} onClick={() => setPageNo(pageNo + 1)}>Next ›</button>
        </div>
      </div>
    </div>
  );
}
