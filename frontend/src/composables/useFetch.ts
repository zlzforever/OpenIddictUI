// ============================================================
// API 请求封装：自动管理 XSRF Token
// 所有 URL 使用相对路径，由 <base href> 处理网关下级目录
// 使用 header（而非 cookie）传递 XSRF token
// ============================================================

let csrfToken: string | null = null

// 获取 XSRF token：内存缓存 → API 获取
async function ensureToken(): Promise<string> {
  if (csrfToken) return csrfToken
  const res = await fetch('api/antiforgery/token', { credentials: 'include' })
  const data = await res.json() as { token: string }
  csrfToken = data.token
  return csrfToken
}

// POST 请求自动带 XSRF token。服务端返回 "Invalid...token" 时自动刷新 token 重试一次
export async function apiPost(url: string, body: Record<string, unknown>, captchaId?: string, retry = true): Promise<unknown> {
  const token = await ensureToken()
  const headers: Record<string, string> = { 'Content-Type': 'application/json', 'X-XSRF-TOKEN': token }
  if (captchaId) headers['Z-CaptchaId'] = captchaId
  const res = await fetch(url, {
    method: 'POST',
    credentials: 'include',
    headers,
    body: JSON.stringify(body)
  })

  if (!res.ok && retry) {
    try {
      const text = await res.text()
      if (text.includes('Invalid') && text.includes('token')) {
        csrfToken = null
        return apiPost(url, body, captchaId, false)
      }
    } catch { /* ignore parse error */ }
  }

  return res.json()
}

export async function apiGet(url: string): Promise<unknown> {
  const res = await fetch(url, { credentials: 'include' })
  return res.json()
}

export async function apiPut(url: string, body: Record<string, unknown>, captchaId?: string): Promise<unknown> {
  const token = await ensureToken()
  const headers: Record<string, string> = { 'Content-Type': 'application/json', 'X-XSRF-TOKEN': token }
  if (captchaId) headers['Z-CaptchaId'] = captchaId
  const res = await fetch(url, {
    method: 'PUT',
    credentials: 'include',
    headers,
    body: JSON.stringify(body)
  })
  return res.json()
}

export async function apiDelete(url: string, captchaId?: string): Promise<unknown> {
  const token = await ensureToken()
  const headers: Record<string, string> = { 'Content-Type': 'application/json', 'X-XSRF-TOKEN': token }
  if (captchaId) headers['Z-CaptchaId'] = captchaId
  const res = await fetch(url, {
    method: 'DELETE',
    credentials: 'include',
    headers
  })
  return res.json()
}
