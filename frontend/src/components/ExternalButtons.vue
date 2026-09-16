<template>
  <div v-if="externalProviders.length === 0" class="empty-providers">{{ $t('login.noExternalProviders') }}</div>
  <button v-for="provider in externalProviders" :key="provider.id" class="external-provider-btn"
    :style="{ '--provider-color': provider.color }" @click="provider.handler">
    <span class="provider-icon" v-html="provider.icon"></span>
    {{ $t('login.continueWith', { name: provider.name }) }}
  </button>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { useProviders, type ExternalProvider } from '../composables/useProviders'
import type { LoginProvider } from '../services/api'

const props = withDefaults(defineProps<{
  providers?: LoginProvider[]
  returnUrl?: string
}>(), { returnUrl: '' })

const { t } = useI18n()
const { providers: registeredProviders } = useProviders()

const externalProviders = computed<ExternalProvider[]>(() => {
  if (!props.providers) {
    return registeredProviders.value
  }

  return props.providers
    .filter((provider) => !isBuiltInProvider(provider))
    .map((provider) => {
      const registered = registeredProviders.value.find((item) =>
        item.id.localeCompare(provider, undefined, { sensitivity: 'accent' }) === 0)
      if (registered) {
        return { ...registered, id: provider }
      }

      const isWeixin = provider.toLowerCase() === 'weixin'
      return {
        id: provider,
        name: isWeixin ? t('login.weixin') : provider,
        icon: isWeixin ? '微' : provider.slice(0, 1).toUpperCase(),
        color: isWeixin ? '#07c160' : '#64748b',
        handler: () => startLogin(provider)
      }
    })
})

function isBuiltInProvider(provider: LoginProvider) {
  return provider.toLowerCase() === 'password' || provider.toLowerCase() === 'sms'
}

function startLogin(provider: LoginProvider) {
  const params = new URLSearchParams({ provider })
  if (props.returnUrl) {
    params.set('returnUrl', props.returnUrl)
  }
  window.location.href = `account/external-login?${params.toString()}`
}
</script>
