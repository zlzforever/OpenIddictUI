<!-- 通用重定向页：从 query.redirectUrl 读取目标 URL 并跳转 -->
<template>
  <div class="redirect-page">
    <p v-if="url">Redirecting...</p>
    <p v-else>No redirect URL specified.</p>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRoute } from 'vue-router'

const route = useRoute()
const url = ref('')

onMounted(() => {
  url.value = (route.query.redirectUrl as string) || ''
  if (url.value) {
    window.location.href = url.value
  }
})
</script>

<style scoped>
.redirect-page { padding: 3rem; text-align: center; color: var(--text-muted); }
</style>
