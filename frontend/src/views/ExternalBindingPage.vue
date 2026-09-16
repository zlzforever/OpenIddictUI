<template>
  <div class="card binding-card">
    <div class="card-header">
      <h1>{{ $t('externalBinding.title') }}</h1>
      <p>{{ $t('externalBinding.subTitle') }}</p>
    </div>

    <div v-if="message" :class="['alert', messageOk ? 'alert-success' : 'alert-danger', 'visible']">
      {{ message }}
    </div>

    <div v-if="loading" class="binding-loading">{{ $t('externalBinding.loading') }}</div>
    <template v-else-if="available">
      <div class="form-group">
        <label for="bindingPhone">{{ $t('externalBinding.phone') }}</label>
        <input
          id="bindingPhone"
          v-model="phone"
          class="form-control"
          maxlength="20"
          autocomplete="tel"
          :placeholder="$t('externalBinding.phonePlaceholder')"
        />
      </div>

      <div class="form-group">
        <label for="bindingCode">{{ $t('externalBinding.smsCode') }}</label>
        <div class="sms-send-wrap">
          <input
            id="bindingCode"
            v-model="verifyCode"
            class="form-control"
            maxlength="6"
            autocomplete="one-time-code"
            :placeholder="$t('externalBinding.smsCodePlaceholder')"
          />
          <button class="btn-send" type="button" :disabled="cooling || sending" @click="sendCode">
            {{ cooling ? $t('externalBinding.resend', { seconds }) : $t('externalBinding.sendSms') }}
          </button>
        </div>
      </div>

      <button class="btn btn-primary btn-block" :disabled="binding" @click="submit">
        {{ binding ? $t('externalBinding.binding') : $t('externalBinding.bind') }}
      </button>
    </template>
    <div v-else class="binding-expired">
      <p>{{ $t('externalBinding.expired') }}</p>
      <button class="btn btn-secondary btn-block" type="button" @click="goLogin">
        {{ $t('externalBinding.backToLogin') }}
      </button>
    </div>

    <SliderCaptcha ref="sliderRef" @verified="doSendCode" />
  </div>
</template>

<script setup lang="ts">
import { onMounted, onUnmounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import {
  bindExternal,
  getExternalBindingStatus,
  sendExternalBindingCode,
} from '../services/api'
import SliderCaptcha from '../components/SliderCaptcha.vue'

const { t } = useI18n()
const router = useRouter()
const loading = ref(true)
const available = ref(false)
const message = ref('')
const messageOk = ref(false)
const phone = ref('')
const verifyCode = ref('')
const sending = ref(false)
const binding = ref(false)
const cooling = ref(false)
const seconds = ref(0)
const sliderRef = ref<InstanceType<typeof SliderCaptcha> | null>(null)
let timer: ReturnType<typeof setInterval> | null = null

async function load() {
  try {
    const status = await getExternalBindingStatus()
    if (!status.success) {
      message.value = status.message || t('externalBinding.expired')
      return
    }

    available.value = true
  } catch {
    message.value = t('externalBinding.loadFailed')
  } finally {
    loading.value = false
  }
}

function sendCode() {
  if (!phone.value.trim()) {
    messageOk.value = false
    message.value = t('externalBinding.enterPhone')
    return
  }

  message.value = ''
  sliderRef.value?.start()
}

async function doSendCode() {
  sending.value = true
  message.value = ''
  try {
    const result = await sendExternalBindingCode({
      phoneNumber: phone.value.trim(),
      countryCode: '+86',
    })
    if (!result.success) {
      messageOk.value = false
      message.value = result.message || t('externalBinding.sendFailed')
      return
    }

    startCooldown(result.data?.retryAfter || 60)
    messageOk.value = true
    message.value = t('externalBinding.smsSent')
  } catch {
    messageOk.value = false
    message.value = t('externalBinding.sendFailed')
  } finally {
    sending.value = false
  }
}

async function submit() {
  if (!phone.value.trim() || !verifyCode.value.trim()) {
    messageOk.value = false
    message.value = t('externalBinding.fillAll')
    return
  }

  binding.value = true
  message.value = ''
  try {
    const result = await bindExternal({
      phoneNumber: phone.value.trim(),
      verifyCode: verifyCode.value.trim(),
    })
    if (result.success) {
      window.location.href = result.data?.location || '/'
      return
    }

    messageOk.value = false
    message.value = result.message || t('externalBinding.bindFailed')
  } catch {
    messageOk.value = false
    message.value = t('externalBinding.bindFailed')
  } finally {
    binding.value = false
  }
}

function startCooldown(duration: number) {
  if (timer) clearInterval(timer)
  seconds.value = Math.max(1, Math.ceil(duration))
  cooling.value = true
  timer = setInterval(() => {
    seconds.value -= 1
    if (seconds.value <= 0) {
      cooling.value = false
      if (timer) clearInterval(timer)
      timer = null
    }
  }, 1000)
}

function goLogin() {
  router.replace('/account/login')
}

onMounted(load)
onUnmounted(() => { if (timer) clearInterval(timer) })
</script>

<style scoped>
.binding-card { max-width: 460px; margin: 0 auto; }
.binding-loading, .binding-expired { text-align: center; color: var(--text-muted); }
.binding-expired p { margin-bottom: 1rem; }
.alert-success { background: #f0fdf4; color: var(--success); border: 1px solid #bbf7d0; }
.sms-send-wrap { position: relative; }
.sms-send-wrap .form-control { padding-right: 100px; }
.btn-send {
  position: absolute; right: 1px; top: 1px; bottom: 1px;
  padding: 0 12px; font-size: 0.8125rem; font-family: var(--font);
  background: transparent; color: var(--primary); border: none;
  border-radius: 0 var(--radius) var(--radius) 0; cursor: pointer;
  white-space: nowrap;
}
.btn-send:disabled { color: var(--text-muted); cursor: not-allowed; }
</style>
