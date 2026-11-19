import api from './api'

export async function getSession() {
  return api.get('/auth/session')
}

export async function login(username, password) {
  return api.post('/auth/login', { username, password })
}

export async function logout() {
  return api.post('/auth/logout')
}
