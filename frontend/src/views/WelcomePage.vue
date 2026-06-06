<!--
  ============================================================
  登录成功后的欢迎页：展示用户 claims + 已授权的客户端列表
  使用 useSession 全局缓存，共用 App.vue / router 的 session 请求结果
  ============================================================
-->
<template>
  <div class="welcome-page">
    <p class="user-name" v-if="user">{{ user }}</p>
    <div class="links">
      <a href="/.well-known/openid-configuration" target="_blank">OpenID Configuration</a>
    </div>
    <pre class="claims-text">{{ claims.map(c => `${c.type}: ${c.value}`).join('\n') }}</pre>
    <div v-if="clients.length">
      <p class="section-label">Consented Clients</p>
      <pre class="claims-text">{{ clients.map(c => `${c.displayName || c.clientId}: ${c.scopes.join(', ')}`).join('\n') }}</pre>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useSession } from '../composables/useSession'

const router = useRouter()
const { claims, clients, load } = useSession()
const user = ref('')

// 调用 load() → 已由 router 或 App.vue 触发过则直接返回缓存，不重复请求
onMounted(async () => {
  const data = await load()
  if (!data.username) { router.replace('/account/login'); return }
  user.value = data.username
})
</script>

<style>
.container:has(.welcome-page) { max-width: none; margin: 0; padding: 0; }
</style>
<style scoped>
.welcome-page { padding: 2rem 3rem; max-width: none; margin: 0; }
.user-name { font-size: 1.25rem; margin: 0 0 0.5rem; }
.links { margin-bottom: 1.5rem; }
.links a { color: var(--primary); font-size: 0.8125rem; text-decoration: none; }
.links a:hover { text-decoration: underline; }
.section-label { color: var(--text-muted); margin: 1.5rem 0 0.5rem; }
.claims-text { font-size: 0.9375rem; line-height: 1.6; overflow-x: auto; margin: 0; padding: 0.5rem 0; color: var(--text); }
</style>
