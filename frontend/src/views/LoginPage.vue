<template>
  <div class="login-page" :style="{ backgroundImage: `url(${bgUrl})` }">
    <div class="login-overlay"></div>
    <div class="login-card">
      <div class="card-header">
        <h1>{{ $t('login.title') }}</h1>
        <p>{{ $t('login.subTitle') }}</p>
      </div>
      <div v-if="errorMsg" class="alert alert-danger visible">{{ errorMsg }}</div>
      <div v-if="loadingProviders" class="login-form-area login-loading" aria-live="polite"></div>
      <template v-else>
        <div v-if="supportedProviders.length > 0" class="tabs">
          <button v-if="hasProvider('password')" :class="['tab', { active: activeTab === 'password' }]" @click="activeTab = 'password'">{{ $t('login.tabAccount') }}</button>
          <button v-if="hasProvider('sms')" :class="['tab', { active: activeTab === 'sms' }]" @click="activeTab = 'sms'">{{ $t('login.tabSms') }}</button>
          <button v-if="hasExternalProvider" :class="['tab', { active: activeTab === 'external' }]" @click="activeTab = 'external'">{{ $t('login.tabThirdParty') }}</button>
        </div>
        <div v-if="supportedProviders.length === 0" class="login-form-area empty-providers">
          {{ $t('login.noProviders') }}
        </div>
        <div v-else class="login-form-area">
          <div v-if="activeTab === 'password'">
            <PasswordLogin :return-url="returnUrl" @error="setError" />
          </div>
          <div v-else-if="activeTab === 'sms'">
            <SmsLogin :return-url="returnUrl" @error="setError" />
          </div>
          <div v-else-if="activeTab === 'external'">
            <ExternalButtons :providers="supportedProviders" :return-url="returnUrl" />
          </div>
        </div>
      </template>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, ref, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import PasswordLogin from '../components/PasswordLogin.vue'
import SmsLogin from '../components/SmsLogin.vue'
import ExternalButtons from '../components/ExternalButtons.vue'
import { getLoginProviders, type LoginProvider } from '../services/api'

const route = useRoute()
const { t } = useI18n()
const returnUrl = (route.query.returnUrl as string) || ''
const activeTab = ref<'password' | 'sms' | 'external'>('password')
const supportedProviders = ref<LoginProvider[]>([])
const loadingProviders = ref(true)
const errorMsg = ref((route.query.error as string) || '')

const bgUrl = ref('')
onMounted(async () => {
  const meta = document.querySelector('meta[name="login-bg"]')
  bgUrl.value = meta?.getAttribute('content') || ''

  try {
    const response = await getLoginProviders()
    if (response.success && Array.isArray(response.data)) {
      supportedProviders.value = response.data
        .map((provider) => provider.trim().toLowerCase())
        .filter((provider, index, providers): provider is LoginProvider =>
          provider.length > 0 && providers.indexOf(provider) === index)
    }
    activeTab.value = supportedProviders.value.includes('password')
      ? 'password'
      : supportedProviders.value.includes('sms')
        ? 'sms'
        : 'external'
  } catch {
    errorMsg.value = t('login.providersLoadFailed')
  } finally {
    loadingProviders.value = false
  }
})

function setError(msg: string) { errorMsg.value = msg }
function hasProvider(provider: LoginProvider) { return supportedProviders.value.includes(provider) }
const hasExternalProvider = computed(() => supportedProviders.value.some((provider) =>
  provider !== 'password' && provider !== 'sms'))
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
.login-loading { min-height: 330px; }
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
