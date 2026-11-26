import { API, wait } from './lib.mjs'

const attempt = async xff => {
  const res = await fetch(`${API}/api/users/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', ...(xff ? { 'X-Forwarded-For': xff } : {}) },
    body: JSON.stringify({ email: 'nobody@test.local', password: 'Wrong123' }),
  })
  return `${res.status}@${res.headers.get('x-instance-id')}`
}

await wait(11000)
const same = []
for (let i = 0; i < 8; i++) same.push(await attempt())
console.log('A) 8 wrong logins, one client          :', same.join('  '))

await wait(11000)
const spoofed = []
for (let i = 0; i < 8; i++) spoofed.push(await attempt(`203.0.113.${i + 1}`))
console.log('B) 8 wrong logins, fake X-Forwarded-For:', spoofed.join('  '))
