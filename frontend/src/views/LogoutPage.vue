<!--
  ============================================================
  登出确认页：用户确认后 POST /account/logout
  流程：
    ① 用户点击 "Yes" → POST /account/logout（带 XSRF token）
    ② 成功 → window.location 跳转到 /logged-out
    ③ 失败 → 跳转到 /
  ============================================================
-->
<template>
  <div class="card logout-page">
    <h1>Logout</h1>
    <p>Would you like to logout?</p>
    <button class="btn btn-primary" @click="submit">Yes</button>
  </div>
</template>

<script setup lang="ts">
import { apiPost } from '../composables/useFetch'

async function submit() {
  // ① POST /account/logout（apiPost 自动带 XSRF token）
  const data = await apiPost('/account/logout', {}) as { data?: { location?: string } }
  if (data.data?.location) {
    window.location.href = data.data.location
  } else {
    window.location.href = '/'
  }
}
</script>
