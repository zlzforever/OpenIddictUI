<template>
  <div class="callback">
    <p v-if="loading">Signing in...</p>
    <div v-if="error" class="error-box">
      <h3>Login Failed</h3>
      <p>{{ error }}</p>
      <a href="/">Back to Home</a>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { userManager } from '../auth/config'

const loading = ref(true)
const error = ref('')

onMounted(async () => {
  try {
    const user = await userManager.signinCallback()
    if (user) {
      window.location.href = '/'
    } else {
      loading.value = false
      error.value = 'No user data returned'
    }
  } catch (e: unknown) {
    loading.value = false
    const msg = (e as { message?: string; error_description?: string; error?: string })
    error.value = msg.error_description || msg.message || msg.error || String(e)
  }
})
</script>

<style scoped>
.callback { display: flex; flex-direction: column; justify-content: center; align-items: center; height: 100vh; font-family: sans-serif; }
.error-box { text-align: center; }
.error-box h3 { color: #dc2626; margin-bottom: 0.5rem; }
.error-box p { color: #64748b; margin-bottom: 1rem; max-width: 500px; }
.error-box a { color: #2563eb; }
</style>
