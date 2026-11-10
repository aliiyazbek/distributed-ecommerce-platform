// Proves that an edited product shows up IMMEDIATELY despite Redis + ETag.
//   k6 run -e ADMIN_EMAIL=admin@x.com -e ADMIN_PASSWORD=Admin123 k6/freshness-test.js
// (An admin account is needed: register a user, then set its role_id to 1 in the DB.)
import http from 'k6/http';
import { check, fail } from 'k6';

const BASE = __ENV.BASE_URL || 'http://localhost:5169';
export const options = { vus: 1, iterations: 1, thresholds: { checks: ['rate==1'] } };

export default function () {
  const json = { headers: { 'Content-Type': 'application/json' } };
  const login = http.post(`${BASE}/api/users/login`, JSON.stringify({ email: __ENV.ADMIN_EMAIL, password: __ENV.ADMIN_PASSWORD }), json);
  if (login.status !== 200) fail('admin login failed');
  const auth = { Authorization: `Bearer ${login.json('token')}` };

  const first = http.get(`${BASE}/api/v1/Products?page=1&pageSize=1`, { headers: auth }).json('items.0');
  const url = `${BASE}/api/v1/Products/${first.id}`;

  // 1) warm cache + get ETag
  http.get(url, { headers: auth });
  const warm = http.get(url, { headers: auth });
  const etag = warm.headers['Etag'];
  const again = http.get(url, { headers: { ...auth, 'If-None-Match': etag } });
  check(again, { 'unchanged product -> 304': (r) => r.status === 304 });

  // 2) admin edits the product
  const newPrice = (Math.floor(Math.random() * 9000) + 1000) / 100;
  const put = http.put(url, { name: first.name, description: first.description, price: newPrice, category: first.category }, { headers: auth });
  check(put, { 'update ok': (r) => r.status === 200 });

  // 3) next read - with the OLD etag - must be 200 with the new price, not 304 / stale cache
  const after = http.get(url, { headers: { ...auth, 'If-None-Match': etag } });
  check(after, {
    'edited product -> 200 (not 304)': (r) => r.status === 200,
    'new price visible immediately': (r) => Number(r.json('price')) === newPrice,
    'etag changed': (r) => r.headers['Etag'] !== etag,
  });

  // 4) the listing must show it too
  const list = http.get(`${BASE}/api/v1/Products?page=1&pageSize=1`, { headers: auth });
  check(list, { 'list shows new price': (r) => Number(r.json('items.0.price')) === newPrice || r.json('items.0.id') !== first.id });
}
