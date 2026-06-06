<template>
  <div class="card">
    <div class="card-header"><h1>Change Password</h1></div>
    <div v-if="msg" :class="['alert', msgType === 'ok' ? 'alert-success' : 'alert-danger', 'visible']">{{ msg }}</div>
    <div class="form-group">
      <label>Username</label>
      <input class="form-control" v-model="userName" placeholder="Username" maxlength="50" />
    </div>
    <div class="form-group">
      <label>Old Password</label>
      <input type="password" class="form-control" v-model="oldPwd" placeholder="Old password" maxlength="50" />
    </div>
    <div class="form-group">
      <label>New Password</label>
      <input type="password" class="form-control" v-model="newPwd" placeholder="New password" maxlength="32" />
    </div>
    <div class="form-group">
      <label>Captcha</label>
      <div class="captcha-row">
        <input class="form-control" v-model="captcha" placeholder="Captcha code" />
        <img :src="captchaSrc" class="captcha-img" @click="refresh" />
      </div>
    </div>
    <button class="btn btn-primary btn-block" @click="submit">Change Password</button>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { apiPost } from '../composables/useFetch'

const userName = ref('')
const oldPwd = ref('')
const newPwd = ref('')
const captcha = ref('')
const captchaSrc = ref('')
const msg = ref('')
const msgType = ref('ok')

function refresh() { captchaSrc.value = '/api/v1.0/captcha/image?_t=' + Date.now() }
onMounted(refresh)

async function submit() {
  msg.value = ''
  const data = await apiPost('/account/change-password', {
    userName: userName.value, oldPassword: oldPwd.value,
    newPassword: newPwd.value, confirmNewPassword: newPwd.value,
    captchaCode: captcha.value, button: 'login'
  }) as { code: number; message?: string }
  if (data.code === 200) { msg.value = '修改成功'; msgType.value = 'ok' }
  else { msg.value = data.message || '修改失败'; msgType.value = 'err'; refresh() }
}
</script>
