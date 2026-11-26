import { execFileSync } from 'node:child_process'
import { API, P } from './lib.mjs'

const file = `req5-shared-${Date.now()}.txt`
execFileSync('docker', ['exec', `${P}-api-1-1`, 'sh', '-c', `echo shared > /app/wwwroot/images/${file}`])

const seen = {}
for (let i = 0; i < 9; i++) {
  const res = await fetch(`${API}/images/${file}`)
  const key = `${res.headers.get('x-instance-id')} ${res.status}`
  seen[key] = (seen[key] ?? 0) + 1
}
console.log('file written inside api-1 only, then read 9 times through nginx:', seen)
execFileSync('docker', ['exec', `${P}-api-1-1`, 'rm', `/app/wwwroot/images/${file}`])
