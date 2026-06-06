<!--
  ============================================================
  已授权应用列表页：读取当前用户 grant 记录并展示
  使用 useSession 全局缓存，无需重复请求 /session
  ============================================================
-->
<template>
  <div class="welcome-page">
    <p class="section-label">Authorized Applications</p>
    <table class="info-table" v-if="grants.length">
      <tr v-for="g in grants" :key="g.clientId">
        <td class="key">{{ g.displayName || g.clientId }}</td>
        <td class="value">{{ g.scopes.join(', ') }}</td>
      </tr>
    </table>
    <p v-else style="color:var(--text-muted)">No authorized applications.</p>
  </div>
</template>

<script setup lang="ts">
import { onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useSession } from '../composables/useSession'

const router = useRouter()
const { clients: grants } = useSession()

// 调用 load() 获取 session 数据（已有缓存则直接返回）
onMounted(async () => {
  const data = await useSession().load()
  if (!data.username) { router.replace('/account/login') }
})
</script>

<style scoped>
.welcome-page { padding: 2rem 3rem; }
.section-label { font-size: 0.8125rem; text-transform: uppercase; letter-spacing: 0.08em; color: var(--text-muted); margin-bottom: 0.75rem; }
.info-table { width: 100%; border-collapse: collapse; }
.info-table tr { border-bottom: 1px solid var(--border); }
.info-table td { padding: 0.625rem 0; font-size: 0.9375rem; }
.info-table .key { width: 30%; color: var(--text-muted); padding-right: 1rem; }
.info-table .value { word-break: break-all; }
</style>
