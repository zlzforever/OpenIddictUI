<template>
  <div class="card">
    <div class="card-header">
      <h1>Login</h1>
      <p>Choose how to login</p>
    </div>
    <div class="tabs">
      <button :class="['tab', { active: activeTab === 'password' }]" @click="activeTab = 'password'">Account</button>
      <button :class="['tab', { active: activeTab === 'sms' }]" @click="activeTab = 'sms'">SMS</button>
      <button :class="['tab', { active: activeTab === 'external' }]" @click="activeTab = 'external'">Third-party</button>
    </div>
    <div v-if="errorMsg" class="alert alert-danger visible">{{ errorMsg }}</div>
    <div v-show="activeTab === 'password'">
      <PasswordLogin :return-url="returnUrl" @error="setError" />
    </div>
    <div v-show="activeTab === 'sms'">
      <SmsLogin :return-url="returnUrl" @error="setError" />
    </div>
    <div v-show="activeTab === 'external'">
      <ExternalButtons />
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { useRoute } from 'vue-router'
import PasswordLogin from '../components/PasswordLogin.vue'
import SmsLogin from '../components/SmsLogin.vue'
import ExternalButtons from '../components/ExternalButtons.vue'

const route = useRoute()
const returnUrl = (route.query.returnUrl as string) || ''
const activeTab = ref('password')
const errorMsg = ref((route.query.error as string) || '')

function setError(msg: string) { errorMsg.value = msg }
</script>
