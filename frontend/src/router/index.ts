// ============================================================
// 路由守卫 + 页面路由配置
// beforeEach 流程：
//   ① URL 规范化（小写转换、hash 清理）
//   ② 白名单放行（/welcome、/logged-out、/consent/*）
//   ③ 调用 /session 检查登录状态
//      → 已登录 → 未在 /welcome 则跳转 /welcome
//      → 未登录 → 未在 /login 则跳转 /login
// ============================================================

import { createRouter, createWebHistory } from 'vue-router'
import { useSession } from '../composables/useSession'

const base = document.querySelector('base')?.getAttribute('href') || '/'

const { load } = useSession()

const router = createRouter({
  history: createWebHistory(base),
  routes: [
    { path: '/', redirect: '/account/login' },
    { path: '/account/login', name: 'login', component: () => import('../views/LoginPage.vue') },
    { path: '/welcome', name: 'welcome', component: () => import('../views/WelcomePage.vue') },
    { path: '/consent/:id', name: 'consent', component: () => import('../views/ConsentPage.vue') },
    { path: '/logout', name: 'logout', component: () => import('../views/LogoutPage.vue') },
    { path: '/logged-out', name: 'logged-out', component: () => import('../views/LoggedOutPage.vue') },
    { path: '/error', name: 'error', component: () => import('../views/ErrorPage.vue') },
    { path: '/not-found', name: 'not-found', component: () => import('../views/NotFoundPage.vue') },
    { path: '/change-password', name: 'change-password', component: () => import('../views/ChangePasswordPage.vue') },
    { path: '/grants', name: 'grants', component: () => import('../views/GrantsPage.vue') },
    { path: '/redirect', name: 'redirect', component: () => import('../views/RedirectPage.vue') },
    { path: '/:pathMatch(.*)*', redirect: '/not-found' },
  ],
})

router.beforeEach(async (to) => {
  // ① URL 规范化：路径统一小写
  if (to.path !== to.path.toLowerCase())
  {
    const lower = to.path.toLowerCase()
    return { path: lower, query: to.query }
  }
  // ①b 安全：URL 中包含 hash fragment 视为异常请求
  if (window.location.hash.length > 1)
  {
    const url = new URL(window.location.href)
    url.hash = ''
    history.replaceState(null, '', url.toString())
    return { path: '/not-found' }
  }

  // ② 白名单：以下页面无需登录
  if (to.path === '/welcome' || to.path === '/logged-out' || to.path.startsWith('/consent'))
  {
    return
  }

  // ③ 检查登录状态（使用全局缓存，不会重复请求）
  try
  {
    const data = await load()
    if (data.username)
    {
      return to.path !== '/welcome' ? { path: '/welcome' } : undefined
    }
  }
  catch { /* 网络异常，走未登录流程 */ }

  return to.path === '/account/login' ? undefined : { path: '/account/login' }
})

export default router
