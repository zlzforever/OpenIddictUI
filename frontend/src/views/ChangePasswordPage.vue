<template>
  <div class="card">
    <div class="card-header"><h1>{{ $t('changePassword.title') }}</h1></div>
    <div v-if="msg" :class="['alert', msgType === 'ok' ? 'alert-success' : 'alert-danger', 'visible']">{{ msg }}</div>
    <div class="form-group">
      <label>{{ $t('passwordLogin.username') }}</label>
      <input class="form-control" v-model="userName" :placeholder="$t('passwordLogin.username')" maxlength="50" />
    </div>
    <div class="form-group">
      <label>{{ $t('changePassword.oldPassword') }}</label>
      <input type="password" class="form-control" v-model="oldPwd" :placeholder="$t('changePassword.oldPassword')" maxlength="50" />
    </div>
    <div class="form-group">
      <label>{{ $t('changePassword.newPassword') }}</label>
      <input type="password" class="form-control" v-model="newPwd" :placeholder="$t('changePassword.newPassword')" maxlength="32" />
    </div>
    <div class="form-group">
      <label>{{ $t('changePassword.captcha') }}</label>
      <div class="captcha-row">
        <input class="form-control" v-model="captcha" :placeholder="$t('changePassword.captcha')" />
        <img alt="" :src="captchaSrc" class="captcha-img" @click="refresh" />
      </div>
    </div>
    <button class="btn btn-primary btn-block" @click="submit">{{ $t('changePassword.submit') }}</button>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { changePassword, captchaImage } from '../services/api'

const { t } = useI18n()
const userName = ref('')
const oldPwd = ref('')
const newPwd = ref('')
const captcha = ref('')
const captchaSrc = ref('')
const msg = ref('')
const msgType = ref('ok')

async function refresh() { captchaSrc.value = await captchaImage() }
onMounted(refresh)

async function submit() {
  msg.value = ''
  const data = await changePassword({
    userName: userName.value, oldPassword: oldPwd.value,
    newPassword: newPwd.value, confirmNewPassword: newPwd.value,
    captchaCode: captcha.value, button: 'login'
  }) as { code: number; message?: string }
  if (data.code === 200) { msg.value = t('changePassword.success'); msgType.value = 'ok' }
  else { msg.value = data.message || t('changePassword.failed'); msgType.value = 'err'; refresh() }
}
</script>
