import http from 'k6/http'
import { Counter } from 'k6/metrics'

const served = {
  'api-1': new Counter('served_by_api_1'),
  'api-2': new Counter('served_by_api_2'),
  'api-3': new Counter('served_by_api_3'),
}
const failed = new Counter('failed_requests')
const BASE = __ENV.BASE || 'http://nginx'
const RATE = Number(__ENV.RATE || 20)
const SECONDS = Number(__ENV.SECONDS || 25)

export const options = {
  scenarios: {
    load: { executor: 'constant-arrival-rate', rate: RATE, timeUnit: '1s', duration: `${SECONDS}s`, preAllocatedVUs: 40, maxVUs: 200 },
  },
  summaryTrendStats: ['avg', 'p(95)', 'max'],
}

export function setup() {
  const res = http.post(`${BASE}/api/users/login`, JSON.stringify({ email: 'rt.customer@test.local', password: 'RtTest123' }),
    { headers: { 'Content-Type': 'application/json' } })
  return { token: res.json('token') }
}

const paths = ['/api/v1/orders?page=1&pageSize=20', '/api/v1/products?page=1&pageSize=10']

export default function (data) {
  const res = http.get(`${BASE}${paths[Math.floor(Math.random() * paths.length)]}`, { headers: { Authorization: `Bearer ${data.token}` } })
  const instance = res.headers['X-Instance-Id']
  if (served[instance]) served[instance].add(1)
  if (res.status < 200 || res.status >= 300) failed.add(1)
}
