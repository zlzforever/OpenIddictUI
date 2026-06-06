<template>
  <div class="app">
    <nav>
      <h3>SPA Demo</h3>
      <div v-if="user" class="nav-right">
        <span>{{ user.profile.name || user.profile.sub }}</span>
        <button @click="callApi">Call API</button>
        <button @click="logout">Logout</button>
      </div>
      <button v-else @click="login">Login</button>
    </nav>
    <main>
      <p v-if="!user">Not logged in. Click Login to sign in via OpenIddictUI.</p>
      <div v-if="user">
        <h4>User Claims</h4>
        <pre>{{ JSON.stringify(user.profile, null, 2) }}</pre>
      </div>
      <div v-if="apiResult">
        <h4>API Response</h4>
        <pre>{{ JSON.stringify(apiResult, null, 2) }}</pre>
      </div>
    </main>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { userManager } from '../auth/config'
import type { User } from 'oidc-client-ts'

const user = ref<User | null>(null)
const apiResult = ref<unknown>(null)

onMounted(async () => {
  user.value = await userManager.getUser()
})

function login() { userManager.signinRedirect() }
function logout() { userManager.signoutRedirect() }

async function callApi() {
  if (!user.value) return
  const res = await fetch('http://localhost:5100/api/me', {
    headers: { Authorization: `Bearer ${user.value.access_token}` }
  })
  apiResult.value = await res.json()
}
</script>

<style>
body { font-family: sans-serif; margin: 0; }
nav { display: flex; align-items: center; gap: 1rem; padding: 0.5rem 1rem; background: #2563eb; color: #fff; }
nav h3 { margin: 0; flex: 1; }
.nav-right { display: flex; align-items: center; gap: 0.5rem; }
nav button { padding: 0.5rem 1rem; border: 1px solid #fff; background: transparent; color: #fff; border-radius: 4px; cursor: pointer; }
nav button:hover { background: rgba(255,255,255,0.15); }
main { padding: 2rem; max-width: 800px; margin: 0 auto; }
pre { background: #f5f5f5; padding: 1rem; border-radius: 4px; overflow-x: auto; }
</style>
