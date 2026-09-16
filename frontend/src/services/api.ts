/**
 * API 统一入口：所有接口路径集中管理
 * 使用相对路径（不带 /），由 <base href> 自动处理网关下级目录
 */
import {apiPost, apiGet, apiPut, apiDelete} from '../composables/useFetch'

// 内置 provider 有固定表单，其它值均按已注册的外部 authentication scheme 处理。
export type LoginProvider = 'password' | 'sms' | 'weixin' | (string & {})

export function getLoginProviders() {
    return apiGet('account/providers') as Promise<{
        code: number
        success: boolean
        message?: string
        data?: string[]
    }>
}

export async function login(body: Record<string, unknown>) {
    return apiPost('account/login', body) as Promise<{
        code: number;
        message?: string;
        data?: { location?: string }
    }>
}

export async function loginBySms(body: Record<string, unknown>) {
    return apiPost('account/login-by-sms', body) as Promise<{
        code: number;
        message?: string;
        data?: { location?: string }
    }>
}

// 短信发送固定使用滑块验证码，验证状态由服务端 HttpOnly Cookie 携带。
export type SendSmsCodeRequest = {
    phoneNumber: string
    countryCode?: string
    scenario?: string
}

export async function sendSmsCode(body: SendSmsCodeRequest) {
    return apiPost('account/send-sms-code', body) as Promise<{ code: number; message?: string }>
}

export async function getExternalBindingStatus() {
    return apiGet('account/external-binding/status') as Promise<{
        code: number
        success: boolean
        message?: string
    }>
}

export async function sendExternalBindingCode(body: Omit<SendSmsCodeRequest, 'scenario'>) {
    return apiPost('account/send-sms-code', {...body, scenario: 'BindExternal'}) as Promise<{
        code: number
        success: boolean
        message?: string
        data?: { retryAfter?: number }
    }>
}

export async function bindExternal(body: Record<string, unknown>) {
    return apiPost('account/external-binding', body) as Promise<{
        code: number
        success: boolean
        message?: string
        data?: { location?: string }
    }>
}

export async function logout() {
    return apiPost('account/logout', {}) as Promise<{ data?: { location?: string } }>
}

export async function changePassword(body: Record<string, unknown>) {
    return apiPost('account/change-password', body) as Promise<{ code: number; message?: string }>
}

export async function getSession() {
    return fetch('session', {credentials: 'include'})
}

// ---- Captcha ----

export async function captchaImage() {
    const res = await fetch(`api/v1.0/captcha/image?_t=${Date.now()}`, {credentials: 'include'})
    const blob = await res.blob()
    return URL.createObjectURL(blob)
}

export async function sliderInit() {
    const res = await fetch('api/v1.0/captcha/slider', {credentials: 'include'})
    return res
}

export async function sliderVerify(position: number) {
    return apiPost('api/v1.0/captcha/slider/verify', {position}) as Promise<{ success: boolean }>
}

export async function getConsent(id: string) {
    return apiGet(`api/consent/${id}`)
}

export async function submitConsent(id: string, body: Record<string, unknown>) {
    return apiPost(`api/consent/${id}`, body) as Promise<{ data?: { location?: string } }>
}

// ---- Admin: Applications & Scopes ----
export async function getApplications() {
    return apiGet('api/applications') as Promise<{ data: unknown[] }>
}

export async function saveApplication(body: Record<string, unknown>, id?: string) {
    if (id) return apiPut(`api/applications/${id}`, body) as Promise<{ code: number; message?: string }>
    return apiPost('api/applications', body) as Promise<{ code: number; message?: string }>
}

export async function deleteApplication(id: string) {
    return apiDelete(`api/applications/${id}`) as Promise<{ code: number; message?: string }>
}

export async function getScopes() {
    return apiGet('api/scopes') as Promise<{ data: unknown[] }>
}

export async function saveScope(body: Record<string, unknown>, id?: string) {
    if (id) return apiPut(`api/scopes/${id}`, body) as Promise<{ code: number; message?: string }>
    return apiPost('api/scopes', body) as Promise<{ code: number; message?: string }>
}

export async function deleteScope(id: string) {
    return apiDelete(`api/scopes/${id}`) as Promise<{ code: number; message?: string }>
}
