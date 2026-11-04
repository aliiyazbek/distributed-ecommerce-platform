import { request } from './apiClient.js'

export const login = credentials => request('/api/users/login', { method: 'POST', body: credentials })

export const register = credentials => request('/api/users/register', { method: 'POST', body: credentials })

export function authErrorMessage(failure) {
  if (failure.status === 429) return 'Too many attempts. Please wait a few seconds and try again.'
  if (failure.status === 401 || failure.status === 409) return failure.message
  return 'Something went wrong. Please try again.'
}
