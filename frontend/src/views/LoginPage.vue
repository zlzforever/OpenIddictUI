<template>
  <div class="login-page" :style="{ backgroundImage: `url(${bgUrl})` }">
    <div class="login-overlay"></div>
    <div class="login-card">
      <div class="card-header">
        <h1>{{ $t('login.title') }}</h1>
        <p>{{ $t('login.subTitle') }}</p>
      </div>
      <div class="tabs">
        <button :class="['tab', { active: activeTab === 'password' }]" @click="activeTab = 'password'">{{ $t('login.tabAccount') }}</button>
        <button :class="['tab', { active: activeTab === 'sms' }]" @click="activeTab = 'sms'">{{ $t('login.tabSms') }}</button>
        <button :class="['tab', { active: activeTab === 'external' }]" @click="activeTab = 'external'">{{ $t('login.tabThirdParty') }}</button>
      </div>
      <div v-if="errorMsg" class="alert alert-danger visible">{{ errorMsg }}</div>
      <div class="login-form-area">
        <div v-show="activeTab === 'password'">
          <PasswordLogin :return-url="returnUrl" @error="setError" />
        </div>
        <div v-show="activeTab === 'sms'">
          <SmsLogin :return-url="returnUrl" @error="setError" />
        </div>
        <div v-show="activeTab === 'external'">
          <ExternalButtons />
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import PasswordLogin from '../components/PasswordLogin.vue'
import SmsLogin from '../components/SmsLogin.vue'
import ExternalButtons from '../components/ExternalButtons.vue'

const route = useRoute()
const returnUrl = (route.query.returnUrl as string) || ''
const activeTab = ref('password')
const errorMsg = ref((route.query.error as string) || '')

const bgUrl = ref('')
onMounted(() => {
  const meta = document.querySelector('meta[name="login-bg"]')
  bgUrl.value = meta?.getAttribute('content') || ''
})

function setError(msg: string) { errorMsg.value = msg }
</script>

<style scoped>
.login-page {
  position: fixed;
  inset: 0;
  background-size: cover;
  background-position: center;
  display: flex;
  align-items: center;
  justify-content: center;
}
.login-overlay {
  position: absolute;
  inset: 0;
  background: rgba(0,0,0,0.45);
}
.login-card {
  position: relative;
  z-index: 1;
  width: 400px;
  padding: 2.5rem;
  background: rgba(255,255,255,0.72);
  backdrop-filter: blur(16px);
  -webkit-backdrop-filter: blur(12px);
  border-radius: 12px;
  box-shadow: 0 8px 32px rgba(0,0,0,0.2);
}
.login-form-area {
  height: 330px;
}
.login-card .card-header {
  padding: 0 0 1.5rem;
  margin: 0;
  text-align: center;
}
.login-card .card-header h1 {
  margin-bottom: 0.25rem;
}
.login-card .card-header p {
  margin: 0;
}
</style>
