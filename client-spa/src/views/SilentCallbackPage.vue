<template>
  <div class="callback">
    <p>Silent sign-in...</p>
  </div>
</template>

<script setup lang="ts">
import { onMounted } from 'vue'
import { userManager } from '../auth/config'

onMounted(async () => {
  try {
    await userManager.signinSilentCallback()
  } catch {
    // Silent callback failure is expected (e.g. session expired)
  }
})
</script>

<style scoped>
.callback { display: flex; justify-content: center; align-items: center; height: 100vh; font-family: sans-serif; color: var(--text-muted); }
</style>
