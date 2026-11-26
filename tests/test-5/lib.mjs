import http from 'node:http'
import { execFileSync } from 'node:child_process'

export const API = process.env.API ?? 'http://localhost:5169'
export const P = process.env.PROJECT ?? 'distributed-ecommerce-platform'
export const PASS = 'RtTest123'
const RS = '\u001e'
export const wait = ms => new Promise(r => setTimeout(r, ms))

export const psql = sql => execFileSync('docker', ['exec', `${P}-postgres-1`, 'psql', '-qAt', '-U', 'ecommerce_user', '-d', 'ecommerce_db', '-c', sql]).toString().trim()
export const rabbit = (...args) => execFileSync('docker', ['exec', `${P}-rabbitmq-1`, 'rabbitmqctl', '-q', ...args, '--no-table-headers'])
  .toString().trim().split('\n').map(line => line.split('\t'))

export async function waitFor(check, what, seconds = 150) {
  for (let i = 0; i < seconds; i++) {
    try { if (await check()) return } catch { /* not up yet */ }
    await wait(1000)
  }
  throw new Error(`${what} did not start`)
}

async function authCall(path, email) {
  for (let attempt = 0; ; attempt++) {
    const res = await fetch(`${API}/api/users/${path}`, {
      method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ email, password: PASS }),
    })
    if (res.status !== 429 || attempt > 10) return res
    await wait(2500)
  }
}
export const register = email => authCall('register', email)
export const login = async email => (await authCall('login', email)).json()

export const status = async (url, init) => (await fetch(url, init)).status
export const negotiate = (hub, token) =>
  status(`${API}/hubs/${hub}/negotiate?negotiateVersion=1${token ? `&access_token=${token}` : ''}`, { method: 'POST' })

export const wsStatus = (hub, token) => new Promise(resolve => {
  const req = http.request(`${API}/hubs/${hub}${token ? `?access_token=${token}` : ''}`, { headers: {
    Connection: 'Upgrade', Upgrade: 'websocket', 'Sec-WebSocket-Version': '13', 'Sec-WebSocket-Key': 'dGhlIHNhbXBsZSBub25jZQ==' } })
  req.on('upgrade', (res, socket) => { socket.destroy(); resolve(res.statusCode) })
  req.on('response', res => { res.resume(); resolve(res.statusCode) })
  req.on('error', e => resolve(e.code))
  req.end()
})

export const listen = (hub, token) => new Promise((resolve, reject) => {
  const pushes = []
  const waiters = new Set()
  const ws = new WebSocket(`${API.replace('http', 'ws')}/hubs/${hub}?access_token=${token}`)
  const closed = new Promise(r => { ws.onclose = () => r(Date.now()) })
  const next = (target, match = () => true, timeout = 5000) => new Promise(done => {
    const found = pushes.find(p => p.target === target && match(p.data))
    if (found) return done(found)
    const waiter = p => {
      if (p.target !== target || !match(p.data)) return
      waiters.delete(waiter)
      clearTimeout(timer)
      done(p)
    }
    const timer = setTimeout(() => { waiters.delete(waiter); done(null) }, timeout)
    waiters.add(waiter)
  })
  ws.onopen = () => ws.send(JSON.stringify({ protocol: 'json', version: 1 }) + RS)
  ws.onerror = () => reject(new Error(`could not open /hubs/${hub}`))
  ws.onmessage = e => {
    for (const frame of String(e.data).split(RS).filter(Boolean)) {
      const msg = JSON.parse(frame)
      if (msg.error) reject(new Error(msg.error))
      else if (msg.type === undefined) resolve({ pushes, next, closed, close: () => ws.close() })
      else if (msg.type === 1) {
        const push = { target: msg.target, data: msg.arguments[0], at: Date.now() }
        pushes.push(push)
        waiters.forEach(w => w(push))
      }
    }
  }
})

export const report = (name, ok, detail = '') => console.log(`${ok ? 'PASS' : 'FAIL'}  ${name.padEnd(62)} ${detail}`)
