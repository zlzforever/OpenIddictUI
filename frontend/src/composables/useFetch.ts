// ============================================================
// API 请求封装：自动管理 XSRF Token、token 过期自动刷新
// 流程：内存缓存 → Cookie 回退 → API 获取（三级策略）
// 所有 URL 使用相对路径，由 <base href> 处理网关下级目录
// ============================================================

// 读取 cookie 值（仿 Axios xsrfCookieName 逻辑）
function getCookie(name: string): string {
  const match = document.cookie.match(new RegExp('(?:^|; )' + name.replace(/([.$?*|{}()[\]\\/+^])/g, '\\$1') + '=([^;]*)'))
  return match ? decodeURIComponent(match[1]) : ''
}

let csrfToken: string | null = null

// 获取 XSRF token 的三级策略：
// ① 内存缓存有 → 直接用
// ② 读 cookie XSRF-TOKEN → 直接用（零网络请求）
// ③ 调 API /api/antiforgery/token 获取 → 缓存到内存
async function ensureToken(): Promise<string> {
  if (csrfToken) return csrfToken
  csrfToken = getCookie('XSRF-TOKEN')
  if (csrfToken) return csrfToken
  const res = await fetch('api/antiforgery/token', { credentials: 'include' })
  const data = await res.json() as { token: string }
  csrfToken = data.token
  return csrfToken
}

// POST 请求自动带 XSRF token。服务端返回 "Invalid...token" 时自动刷新 token 重试一次
export async function apiPost(url: string, body: Record<string, unknown>, retry = true): Promise<unknown> {
  const token = await ensureToken()
  const res = await fetch(url, {
    method: 'POST',
    credentials: 'include',
    headers: { 'Content-Type': 'application/json', 'X-XSRF-TOKEN': token },
    body: JSON.stringify(body)
  })

  if (!res.ok && retry) {
    try {
      const text = await res.text()
      if (text.includes('Invalid') && text.includes('token')) {
        csrfToken = null
        return apiPost(url, body, false)
      }
    } catch { /* ignore parse error */ }
  }

  return res.json()
}

export async function apiGet(url: string): Promise<unknown> {
  const res = await fetch(url, { credentials: 'include' })
  return res.json()
}

export async function apiPut(url: string, body: Record<string, unknown>): Promise<unknown> {
  const token = await ensureToken()
  const res = await fetch(url, {
    method: 'PUT',
    credentials: 'include',
    headers: { 'Content-Type': 'application/json', 'X-XSRF-TOKEN': token },
    body: JSON.stringify(body)
  })
  return res.json()
}

export async function apiDelete(url: string): Promise<unknown> {
  const token = await ensureToken()
  const res = await fetch(url, {
    method: 'DELETE',
    credentials: 'include',
    headers: { 'Content-Type': 'application/json', 'X-XSRF-TOKEN': token }
  })
  return res.json()
}
