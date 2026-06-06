// ============================================================
// 用户 Session 管理：调用 GET /session 获取当前登录用户信息
// 全局单例 — 多个组件共享同一份缓存，并发请求自动去重
// ============================================================

import { computed, ref } from 'vue'

interface SessionData {
  username: string | null
  claims: { type: string; value: string }[]
  clients: { clientId: string; displayName?: string; scopes: string[] }[]
}

const session = ref<SessionData>({ username: null, claims: [], clients: [] })

// 并发去重：多个调用方（App.vue onMounted + router beforeEach）
// 同时调用 load() 时，只发一次 HTTP 请求，后续调用方等待同一 Promise
let sessionPromise: Promise<SessionData> | null = null
let sessionDone = false

export function useSession() {
  const username = computed(() => session.value.username)
  const claims = computed(() => session.value.claims)
  const clients = computed(() => session.value.clients)

  // ① 已成功加载 → 直接返回缓存
  // ② 正在加载中 → 返回现有 Promise（并发去重）
  // ③ 首次调用 → 发 HTTP 请求并缓存 Promise
  async function load(): Promise<SessionData> {
    if (sessionDone) return session.value
    if (sessionPromise) return sessionPromise

    sessionPromise = (async () => {
      try {
        const res = await fetch('/session', { credentials: 'include' })
        if (!res.ok) throw new Error('not authenticated')
        const data = await res.json()
        const rawClaims: { type: string; value: string }[] = data?.data?.claims ?? []
        const rawClients: { clientId: string; displayName?: string; scopes: string[] }[] = data?.data?.clients ?? []

        // 取 display name：name > email > sub
        const sub = rawClaims.find((c) => c.type === 'nameidentifier')
        const name = rawClaims.find((c) => c.type === 'name')
        const email = rawClaims.find((c) => c.type === 'email')
        const user = sub ? (name?.value || email?.value || sub.value) : null

        session.value = { username: user, claims: rawClaims, clients: rawClients }
        sessionDone = true
      } catch { /* 未登录，保持空状态 */ }
      finally { sessionPromise = null }

      return session.value
    })()

    return sessionPromise
  }

  // 登出后调用：清除缓存，下次 load() 会重新请求
  function reset() {
    sessionDone = false
    sessionPromise = null
    session.value = { username: null, claims: [], clients: [] }
  }

  return { username, claims, clients, session, load, reset }
}
