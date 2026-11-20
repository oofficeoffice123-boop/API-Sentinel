import axios from 'axios'
import type { InternalAxiosRequestConfig } from 'axios'

let accessToken: string | null = null

export function setAccessToken(token: string | null) { accessToken = token }

export const api = axios.create({
  baseURL: import.meta.env.VITE_API_URL || 'http://localhost:5080/api',
  withCredentials: true,
})

api.interceptors.request.use(config => {
  if (accessToken) config.headers.Authorization = `Bearer ${accessToken}`
  else delete config.headers.Authorization
  return config
})

let refreshRequest: Promise<string> | null = null

export function renewAccessToken(): Promise<string> {
  if (!refreshRequest) {
    refreshRequest = axios.post(`${api.defaults.baseURL}/auth/refresh`, {}, { withCredentials: true })
      .then(response => {
        const token = response.data.token as string
        setAccessToken(token)
        return token
      })
      .catch(error => {
        expireSession()
        throw error
      })
      .finally(() => { refreshRequest = null })
  }
  return refreshRequest
}

api.interceptors.response.use(response => response, async error => {
  const config = error.config as (InternalAxiosRequestConfig & { _sentinelRetried?: boolean }) | undefined
  if (error.response?.status !== 401 || !config || config._sentinelRetried || config.url?.includes('/auth/')) return Promise.reject(error)

  if (!accessToken) {
    expireSession()
    return Promise.reject(error)
  }

  config._sentinelRetried = true
  try {
    const token = await renewAccessToken()
    config.headers.Authorization = `Bearer ${token}`
    return await api.request(config)
  } catch (refreshError) {
    return Promise.reject(refreshError)
  }
})

function expireSession() {
  setAccessToken(null)
  window.dispatchEvent(new Event('sentinel:auth-expired'))
}