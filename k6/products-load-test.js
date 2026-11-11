// k6 load test for the product catalog (Requirement 3 - Performance & Caching)
//
// BEFORE (no cache, no ETag):
//   Run the API with  Caching__Enabled=false  then:
//   k6 run -e LABEL=before -e USE_ETAG=false k6/products-load-test.js
// AFTER (Redis + ETag):
//   Run the API with  Caching__Enabled=true   then:
//   k6 run -e LABEL=after  -e USE_ETAG=true  k6/products-load-test.js
//
// Env: BASE_URL (default http://localhost:5169)  EMAIL/PASSWORD (test customer, auto-registered)
import http from 'k6/http';
import { check, sleep } from 'k6';
import { Trend, Rate, Counter } from 'k6/metrics';

const BASE = __ENV.BASE_URL || 'http://localhost:5169';
const LABEL = __ENV.LABEL || 'run';
const USE_ETAG = (__ENV.USE_ETAG || 'false') === 'true';
const EMAIL = __ENV.EMAIL || 'k6.loadtest@example.com';
const PASSWORD = __ENV.PASSWORD || 'LoadTest123';

const listDuration = new Trend('products_list_duration', true);
const detailDuration = new Trend('product_detail_duration', true);
const cacheHit = new Rate('redis_cache_hit_rate');
const notModified = new Counter('http_304_not_modified');
const fullBodies = new Counter('http_200_full_body');

export const options = {
  scenarios: {
    browse: {
      executor: 'ramping-vus',
      startVUs: 0,
      stages: [
        { duration: '15s', target: 20 },
        { duration: '30s', target: 50 },
        { duration: '30s', target: 100 },
        { duration: '15s', target: 0 },
      ],
    },
  },
  summaryTrendStats: ['avg', 'min', 'med', 'p(50)', 'p(90)', 'p(95)', 'p(99)', 'max'],
  thresholds: {
    http_req_failed: ['rate<0.01'],
    products_list_duration: ['p(95)<500', 'p(99)<1000'],
    product_detail_duration: ['p(95)<300', 'p(99)<800'],
  },
};

export function setup() {
  const json = { headers: { 'Content-Type': 'application/json' } };
  http.post(`${BASE}/api/users/register`, JSON.stringify({ email: EMAIL, password: PASSWORD }), json); // 409 if it exists: fine
  const login = http.post(`${BASE}/api/users/login`, JSON.stringify({ email: EMAIL, password: PASSWORD }), json);
  check(login, { 'login ok': (r) => r.status === 200 });
  const token = login.json('token');

  const auth = { headers: { Authorization: `Bearer ${token}` } };
  const list = http.get(`${BASE}/api/v1/Products?page=1&pageSize=20`, auth);
  const ids = (list.json('items') || []).map((p) => p.id);
  if (ids.length === 0) throw new Error('No products found. Seed products first (see docs/performance-testing.md).');
  return { token, ids };
}

// Each VU remembers the last ETag per URL, exactly like a browser would.
const etags = {};

function get(url, token, trend) {
  const headers = { Authorization: `Bearer ${token}` };
  if (USE_ETAG && etags[url]) headers['If-None-Match'] = etags[url];

  const res = http.get(url, { headers, tags: { name: url.replace(/[0-9a-f-]{36}/, ':id').replace(/\?.*/, '') } });
  trend.add(res.timings.duration);

  if (res.status === 304) notModified.add(1);
  else if (res.status === 200) {
    fullBodies.add(1);
    if (res.headers['Etag']) etags[url] = res.headers['Etag'];
  }
  if (res.headers['X-Cache']) cacheHit.add(res.headers['X-Cache'] === 'HIT');

  check(res, { 'status 200/304': (r) => r.status === 200 || r.status === 304 });
}

export default function (data) {
  const id = data.ids[Math.floor(Math.random() * data.ids.length)];
  get(`${BASE}/api/v1/Products?page=1&pageSize=20`, data.token, listDuration);
  get(`${BASE}/api/v1/Products/${id}`, data.token, detailDuration);
  sleep(0.5);
}

export function handleSummary(data) {
  return { [`k6/results/${LABEL}.json`]: JSON.stringify(data, null, 2) };
}
