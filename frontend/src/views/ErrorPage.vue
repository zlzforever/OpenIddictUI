<!-- 错误展示页：根据 URL query 中的 errorId 显示对应错误消息 -->
<template>
  <div class="card error-page">
    <div class="error-icon">&#9888;</div>
    <h1>Error</h1>
    <p>{{ message }}</p>
    <p class="text-muted" style="font-size:0.75rem">Error code: {{ code }}</p>
    <router-link to="/account/login" class="btn btn-primary" style="margin-top:1rem;display:inline-block;text-decoration:none">Return to Login</router-link>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'

const route = useRoute()
const code = parseInt(route.query.errorId as string, 10) || 0

const messages: Record<number, string> = {
  4001: '不支持双因素认证', 4002: '用户被禁止登录', 4003: '用户被锁定',
  4004: '用户名或密码不正确', 4005: '不支持 NativeClient', 4006: '返回地址不合法',
  4007: '选择的操作不正确', 4008: '没有 Scope 可匹配', 4009: '客户端标识出错',
  4010: '授权请求链接不正确', 4011: '登录失败', 4012: '用户不存在',
  4013: '验证码过期', 4014: '验证码不正确', 4015: '密码不符合安全要求', 4017: '短信发送失败',
}

const message = computed(() => messages[code] || '未知错误')
</script>
