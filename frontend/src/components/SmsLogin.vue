<!--
  ============================================================
  短信验证码登录组件：点发送 → 弹出滑动验证 → 通过即发短信
  流程：
    ① 输入手机号 → 点 "Send SMS"
    ② 弹出滑动验证窗口 → 拖到圆形缺口对齐
    ③ 验证通过 → 自动 POST /account/send-sms-code → 启动 60s 冷却
    ④ 填入短信验证码 → 点 "Login"
  ============================================================
-->
<template>
  <div>
    <div class="form-group">
      <label for="smsPhone">Phone Number</label>
      <input class="form-control input-bg" id="smsPhone" v-model="phone" placeholder="Phone number" autofocus maxlength="20" />
    </div>
    <div class="form-group">
      <label for="smsCode">SMS Code</label>
      <div class="sms-send-wrap">
        <input type="text" class="form-control input-bg" id="smsCode" v-model="code" placeholder="Verification code" autocomplete="off" maxlength="6" />
        <button type="button" class="btn-send" :disabled="cooling" @click="startSend">
          {{ cooling ? `Resend (${seconds}s)` : 'Send SMS' }}
        </button>
      </div>
    </div>
    <button class="btn btn-primary btn-block" @click="submit">Login</button>

    <SliderCaptcha ref="sliderRef" @verified="doSendCode" />
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { sendSmsCode, loginBySms } from '../services/api'
import SliderCaptcha from './SliderCaptcha.vue'

const props = defineProps<{ returnUrl: string }>()
const emit = defineEmits<{ error: [msg: string] }>()
const phone = ref('')
const code = ref('')
const cooling = ref(false)
const seconds = ref(60)
const sliderRef = ref<InstanceType<typeof SliderCaptcha> | null>(null)
let timer: ReturnType<typeof setInterval> | null = null

// ① 点击发送 → 弹出滑动验证
function startSend() {
  if (!phone.value) { emit('error', '请输入手机号'); return }
  emit('error', '')
  sliderRef.value?.start()
}

// ③ 滑动验证通过 → 自动调用发短信 API
async function doSendCode() {
  const data = await sendSmsCode({
    phoneNumber: phone.value, countryCode: '+86', scenario: 'Login'
  }) as { code: number; message?: string }
  if (data.code !== 200) { emit('error', data.message || '发送失败'); return }
  cooling.value = true
  seconds.value = 60
  timer = setInterval(() => { seconds.value--; if (seconds.value <= 0) { cooling.value = false; clearInterval(timer!); timer = null } }, 1000)
}

// ④ 短信验证码登录
async function submit() {
  emit('error', '')
  try {
    const data = await loginBySms({
      phoneNumber: phone.value, verifyCode: code.value,
      button: 'login', returnUrl: props.returnUrl || null
    }) as { data?: { location?: string }; message?: string }
    if (data.data?.location) {
      window.location.href = data.data.location
    } else {
      emit('error', data.message || '登录失败')
    }
  } catch {
    emit('error', '服务器错误')
  }
}
</script>

<style scoped>
.sms-send-wrap { position: relative; }
.sms-send-wrap .form-control { padding-right: 90px; }
.input-bg { background: #f8f9fa; }
.sms-send-wrap .btn-send {
  position: absolute; right: 1px; top: 1px; bottom: 1px;
  padding: 0 12px; font-size: 0.875rem; font-family: var(--font);
  background: transparent; color: var(--primary); border: none;
  border-radius: 0 var(--radius) var(--radius) 0;
  cursor: pointer; white-space: nowrap; transition: background 0.15s;
}
.sms-send-wrap .btn-send:hover { background: rgba(37,99,235,0.08); }
.sms-send-wrap .btn-send:disabled { color: var(--text-muted); cursor: default; }
.sms-send-wrap .btn-send:disabled:hover { background: transparent; }
</style>
