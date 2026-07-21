<!-- 登出成功页：支持 OIDC RP-Initiated Logout 的 post_logout_redirect_uri 自动跳转 -->
<template>
  <div class="card logged-out-page">
    <h1>{{ $t('logout.title') }}</h1>
    <p>{{ $t('logout.loggedOut') }}</p>
    <p v-if="postLogoutUri">
      <span v-html="$t('logout.clickHere', { url: postLogoutUri })"></span>
      <strong v-if="clientName"> {{ $t('logout.toClient', { name: clientName }) }}</strong>
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

onMounted(() => {
  if (autoRedirect === 'true' && postLogoutUri) window.location.href = postLogoutUri
})
</script>
