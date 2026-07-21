<!--
  ============================================================
  密码登录组件：用户名 + 密码 + 图形验证码
  流程：
    ① 页面加载 → 获取图形验证码
    ② 填写表单 → 点击 Login → POST /account/login
    ③ 成功返回 { data: { location } } → window.location 跳转
       （回到 /connect/authorize 继续 OAuth 流程，或跳到 /welcome）
    ④ 失败 → 显示错误消息 + 刷新验证码
  ============================================================
-->
<template>
  <div>
    <div class="form-group">
      <label for="loginUsername">{{ $t('passwordLogin.username') }}</label>
      <input class="form-control" id="loginUsername" v-model="username" :placeholder="$t('passwordLogin.username')" autofocus maxlength="24" />
    </div>
    <div class="form-group">
      <label for="loginPassword">{{ $t('passwordLogin.password') }}</label>
      <input type="password" class="form-control" id="loginPassword" v-model="password" :placeholder="$t('passwordLogin.password')" autocomplete="off" maxlength="24" />
    </div>
    <div class="form-group">
      <label for="loginCaptcha">{{ $t('passwordLogin.captcha') }}</label>
      <div class="captcha-row">
        <input type="text" class="form-control" id="loginCaptcha" v-model="captcha" :placeholder="$t('passwordLogin.captcha')" autocomplete="off" />
        <img :src="captchaSrc" class="captcha-img" alt="" @click="refreshCaptcha" />
      </div>
    </div>
    <div class="form-group">
      <div class="form-check">
        <input type="checkbox" class="form-check-input" id="rememberLogin" v-model="remember" />
        <label class="form-check-label" for="rememberLogin">{{ $t('passwordLogin.rememberMe') }}</label>
      </div>
    </div>
    <button class="btn btn-primary btn-block" @click="submit">{{ $t('passwordLogin.login') }}</button>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { login, captchaImage } from '../services/api'

const props = defineProps<{ returnUrl: string }>()
const emit = defineEmits<{ error: [msg: string] }>()
const username = ref('')
const password = ref('')
const captcha = ref('')
const remember = ref(false)
const captchaSrc = ref('')

async function refreshCaptcha() {
  captchaSrc.value = await captchaImage()
}

async function submit() {
  emit('error', '')
  try {
    const data = await login({
      username: username.value, password: password.value,
      captchaCode: captcha.value, rememberLogin: remember.value,
      button: 'login', returnUrl: props.returnUrl || null
    }) as { data?: { location?: string }; message?: string }
    if (data.data?.location) {
      window.location.href = data.data.location
    } else {
      emit('error', data.message || '')
      refreshCaptcha()
    }
  } catch {
    emit('error', '')
    refreshCaptcha()
  }
}

onMounted(refreshCaptcha)
</script>
