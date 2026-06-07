/**
 * API 统一入口：所有接口路径 + 调用方法集中管理
 * 通过 BASE_PATH 前缀支持网关下级目录部署
 */
import { apiPost, apiGet } from '../composables/useFetch'
import { BASE_PATH } from '../utils/basePath'

// ---- 工具 ----
export function apiUrl(path: string): string {
  return `${BASE_PATH}${path.replace(/^\//, '')}`
}

// ---- Account ----
export async function login(body: Record<string, unknown>) {
  return apiPost(apiUrl('/account/login'), body) as Promise<{ code: number; message?: string; data?: { location?: string } }>
}

export async function loginBySms(body: Record<string, unknown>) {
  return apiPost(apiUrl('/account/login-by-sms'), body) as Promise<{ code: number; message?: string; data?: { location?: string } }>
}

export async function sendSmsCode(body: Record<string, unknown>) {
  return apiPost(apiUrl('/account/send-sms-code'), body) as Promise<{ code: number; message?: string }>
}

export async function logout() {
  return apiPost(apiUrl('/account/logout'), {}) as Promise<{ data?: { location?: string } }>
}

export async function changePassword(body: Record<string, unknown>) {
  return apiPost(apiUrl('/account/change-password'), body) as Promise<{ code: number; message?: string }>
}

// ---- Session ----
export async function getSession() {
  return fetch(apiUrl('/session'), { credentials: 'include' })
}

// ---- Captcha ----
export function captchaImageUrl() {
  return apiUrl(`/api/v1.0/captcha/image?_t=${Date.now()}`)
}

export async function sliderInit() {
  return fetch(apiUrl('/api/v1.0/captcha/slider'), { credentials: 'include' })
}

export async function sliderVerify(position: number) {
  return apiPost(apiUrl('/api/v1.0/captcha/slider/verify'), { id: '', position }) as Promise<{ success: boolean; data?: unknown }>
}

// ---- Consent ----
export async function getConsent(id: string) {
  return apiGet(apiUrl(`/api/consent/${id}`))
}

export async function submitConsent(id: string, body: Record<string, unknown>) {
  return apiPost(apiUrl(`/api/consent/${id}`), body) as Promise<{ data?: { location?: string } }>
}
