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
    <h1>{{ $t('logout.title') }}</h1>
    <p>{{ $t('logout.confirmMessage') }}</p>
    <button class="btn btn-primary" @click="submit">{{ $t('logout.confirmBtn') }}</button>
  </div>
</template>

<script setup lang="ts">
import { logout } from '../services/api'

async function submit() {
  const data = await logout() as { data?: { location?: string } }
  if (data.data?.location) {
    window.location.href = data.data.location
  } else {
    window.location.href = '/'
  }
}
</script>
