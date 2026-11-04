const storageKey = 'store.token'

function readPayload(token) {
  const encoded = token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/')
  const bytes = Uint8Array.from(atob(encoded), char => char.charCodeAt(0))
  return JSON.parse(new TextDecoder().decode(bytes))
}

export function toSession(token) {
  try {
    const { sub, email, role, exp } = readPayload(token)
    return { token, userId: sub, email, role, expiresAt: exp * 1000 }
  } catch {
    return null
  }
}

export const readToken = () => localStorage.getItem(storageKey)
export const saveToken = token => localStorage.setItem(storageKey, token)
export const clearToken = () => localStorage.removeItem(storageKey)

export function readSession() {
  const token = readToken()
  return token ? toSession(token) : null
}
