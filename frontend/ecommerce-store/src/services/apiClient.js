import { readToken } from './session.js'

const defaultTimeout = 8000

let handleUnauthorized = () => {}

export const setUnauthorizedHandler = handler => {
  handleUnauthorized = handler
}

export class ApiError extends Error {
  constructor(status, message = '', fields = {}) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.fields = fields
  }

  get unreachable() {
    return this.status === 0 || this.status === 503
  }
}

async function readBody(response) {
  if (!response.headers.get('content-type')?.includes('json')) return null
  return response.json().catch(() => null)
}

export async function request(path, { method = 'GET', body, signal, timeout = defaultTimeout } = {}) {
  const token = readToken()
  const isForm = body instanceof FormData
  const headers = {}
  if (token) headers.Authorization = `Bearer ${token}`
  if (body && !isForm) headers['Content-Type'] = 'application/json'

  const timer = AbortSignal.timeout(timeout)
  let response
  try {
    response = await fetch(path, {
      method,
      headers,
      body: isForm ? body : body && JSON.stringify(body),
      signal: signal ? AbortSignal.any([signal, timer]) : timer,
    })
  } catch (failure) {
    if (signal?.aborted) throw failure
    throw new ApiError(0)
  }

  const data = await readBody(response)
  if (response.ok) return data
  if (response.status >= 500 && !data) throw new ApiError(0)
  if (response.status === 401 && token) handleUnauthorized()
  throw new ApiError(response.status, data?.message ?? data?.detail ?? data?.title, data?.errors)
}
