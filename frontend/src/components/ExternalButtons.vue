<!--
  ============================================================
  外部登录提供商按钮组
  数据来自 useProviders() — 外部脚本通过 window.OpenIddictUI.registerExternalProvider() 注入
  ============================================================
-->
<template>
  <div v-if="providers.length === 0" class="empty-providers">{{ $t('login.noExternalProviders') }}</div>
  <button v-for="p in providers" :key="p.id" class="external-provider-btn"
    :style="{ '--provider-color': p.color }" @click="p.handler">
    <span class="provider-icon" v-html="p.icon"></span>
    {{ $t('login.continueWith', { name: p.name }) }}
  </button>
</template>

<script setup lang="ts">
import { useProviders } from '../composables/useProviders'

const { providers } = useProviders()
</script>
