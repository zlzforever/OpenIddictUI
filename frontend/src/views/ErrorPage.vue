<!-- 错误展示页：根据 URL query 中的 errorId 显示对应错误消息 -->
<template>
  <div class="card error-page">
    <div class="error-icon">&#9888;</div>
    <h1>{{ $t('errors.title') }}</h1>
    <p>{{ message }}</p>
    <p class="text-muted" style="font-size:0.75rem">Error code: {{ code }}</p>
    <router-link to="/account/login" class="btn btn-primary" style="margin-top:1rem;display:inline-block;text-decoration:none">{{ $t('errors.returnToLogin') }}</router-link>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'

const { t } = useI18n()
const route = useRoute()
const code = parseInt(route.query.errorId as string, 10) || 0

const messages: Record<number, string> = {
  4001: t('errors.e4001'), 4002: t('errors.e4002'), 4003: t('errors.e4003'),
  4004: t('errors.e4004'), 4005: t('errors.e4005'), 4006: t('errors.e4006'),
  4007: t('errors.e4007'), 4008: t('errors.e4008'), 4009: t('errors.e4009'),
  4010: t('errors.e4010'), 4011: t('errors.e4011'), 4012: t('errors.e4012'),
  4013: t('errors.e4013'), 4014: t('errors.e4014'), 4015: t('errors.e4015'), 4017: t('errors.e4017'),
}

const message = computed(() => messages[code] || t('errors.unknown'))
</script>
