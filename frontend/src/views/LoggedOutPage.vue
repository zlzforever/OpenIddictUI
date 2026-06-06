<!-- 登出成功页：支持 OIDC RP-Initiated Logout 的 post_logout_redirect_uri 自动跳转 -->
<template>
  <div class="card logged-out-page">
    <h1>Logout</h1>
    <p>You are now logged out.</p>
    <p v-if="postLogoutUri">
      Click <a :href="postLogoutUri">here</a> to return
      <strong v-if="clientName"> to {{ clientName }}</strong>.
    </p>
  </div>
</template>

<script setup lang="ts">
import { onMounted } from 'vue'
import { useRoute } from 'vue-router'

const route = useRoute()
const postLogoutUri = route.query.postLogoutRedirectUri as string || ''
const clientName = route.query.clientName as string || ''
const autoRedirect = route.query.automaticRedirectAfterSignOut as string

// 如果服务端指示自动跳转，直接重定向
onMounted(() => {
  if (autoRedirect === 'true' && postLogoutUri) window.location.href = postLogoutUri
})
</script>
