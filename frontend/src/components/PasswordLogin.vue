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
      <label for="loginUsername">Username</label>
      <input class="form-control" id="loginUsername" v-model="username" placeholder="Username" autofocus maxlength="24" />
    </div>
    <div class="form-group">
      <label for="loginPassword">Password</label>
      <input type="password" class="form-control" id="loginPassword" v-model="password" placeholder="Password" autocomplete="off" maxlength="24" />
    </div>
    <!-- ③ 图形验证码 -->
    <div class="form-group">
      <label for="loginCaptcha">Captcha</label>
      <div class="captcha-row">
        <input type="text" class="form-control" id="loginCaptcha" v-model="captcha" placeholder="Captcha code" autocomplete="off" />
        <img :src="captchaSrc" class="captcha-img" alt="Captcha" @click="refreshCaptcha" />
      </div>
    </div>
    <div class="form-group">
      <div class="form-check">
        <input type="checkbox" class="form-check-input" id="rememberLogin" v-model="remember" />
        <label class="form-check-label" for="rememberLogin">Remember Me</label>
      </div>
    </div>
    <button class="btn btn-primary btn-block" @click="submit">Login</button>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { apiPost } from '../composables/useFetch'

const props = defineProps<{ returnUrl: string }>()
const emit = defineEmits<{ error: [msg: string] }>()
const username = ref('')
const password = ref('')
const captcha = ref('')
const remember = ref(false)
const captchaSrc = ref('')

// 刷新验证码：时间戳参数防浏览器缓存
function refreshCaptcha() {
  captchaSrc.value = '/api/v1.0/captcha/image?_t=' + Date.now()
}

// ② 提交登录
async function submit() {
  emit('error', '')
  try {
    const data = await apiPost('/account/login', {
      username: username.value, password: password.value,
      captchaCode: captcha.value, rememberLogin: remember.value,
      button: 'login', returnUrl: props.returnUrl || null
    }) as { data?: { location?: string }; message?: string }
    // ③ 成功 → 页面跳转（回到 OAuth 流程或 welcome 页）
    if (data.data?.location) {
      window.location.href = data.data.location
    } else {
      emit('error', data.message || '登录失败')
      refreshCaptcha()
    }
  } catch {
    emit('error', '服务器错误')
    refreshCaptcha()
  }
}

// ① 页面初始化：获取验证码
onMounted(refreshCaptcha)
</script>
