<template>
  <n-config-provider :locale="naiveLocale.locale" :date-locale="naiveLocale.dateLocale" :theme-overrides="themeOverrides">
  <n-message-provider>
  <div id="app-root">
    <nav class="navbar" :class="{ 'navbar-transparent': isLoginPage }">
      <div class="navbar-inner">
        <div class="navbar-left">
          <router-link to="/" class="navbar-brand">{{ $t('nav.brand') }}</router-link>
          <span v-if="isDev" style="font-size:0.7rem;color:var(--text-muted);background:#f0f0f0;padding:1px 6px;border-radius:3px">{{ isAdmin ? 'ADMIN' : '' }}</span>
          <template v-if="isAdmin">
            <router-link to="/applications" class="nav-link nav-admin">{{ $t('nav.applications') }}</router-link>
            <router-link to="/scopes" class="nav-link nav-admin">{{ $t('nav.scopes') }}</router-link>
          </template>
          <template v-if="username">
            <router-link to="/authorizations" class="nav-link nav-admin">{{ $t('nav.authorizations') }}</router-link>
          </template>
          <a v-if="isDev" href="http://localhost:5175" target="_blank" class="nav-link">{{ $t('nav.spaClient') }}</a>
          <span class="navbar-spacer"></span>
        </div>
        <!-- 语言切换 -->
        <div class="navbar-lang" v-if="supportedLangs.length > 1">
          <button class="lang-btn" v-for="lang in supportedLangs" :key="lang" :class="{ active: locale === lang }" @click="switchLang(lang)">{{ lang === 'zh-CN' ? '中' : 'EN' }}</button>
        </div>
        <!-- 已登录时显示用户名 + 下拉菜单 -->
        <div class="navbar-user" v-if="username">
          <div class="navbar-trigger" @click="toggleMenu">
            <span class="navbar-username">{{ username }}</span>
            <span class="navbar-arrow" :class="{ open: menuOpen }">&#9662;</span>
          </div>
          <div v-if="menuOpen" class="navbar-menu" @click.stop>
            <div class="navbar-menu-item" @click="openChangePwdModal">{{ $t('nav.changePassword') }}</div>
            <div class="navbar-menu-item">{{ $t('nav.profile') }}</div>
            <div class="navbar-menu-divider"></div>
            <div class="navbar-menu-item" @click="openLogoutModal">{{ $t('nav.logout') }}</div>
          </div>
        </div>
      </div>
    </nav>
    <main class="container">
      <router-view />
    </main>

    <!-- 修改密码模态框 -->
    <div v-if="showPwdModal" class="modal-overlay" @click.self="closePwdModal">
      <div class="modal-box modal-form-box">
        <h2 class="modal-title">{{ $t('changePassword.title') }}</h2>
        <div v-if="pwdMsg" :class="['alert', pwdOk ? 'alert-success' : 'alert-danger', 'visible']">{{ pwdMsg }}</div>
        <div class="form-group">
          <label>{{ $t('changePassword.oldPassword') }}</label>
          <input type="password" class="form-control" v-model="pwdOld" :placeholder="$t('changePassword.oldPassword')" maxlength="50" />
        </div>
        <div class="form-group">
          <label>{{ $t('changePassword.newPassword') }}</label>
          <input type="password" class="form-control" v-model="pwdNew" :placeholder="$t('changePassword.newPassword')" maxlength="32" />
        </div>
        <div class="form-group">
          <label>{{ $t('changePassword.confirmPassword') }}</label>
          <input type="password" class="form-control" v-model="pwdConfirm" :placeholder="$t('changePassword.confirmPassword')" maxlength="32" />
        </div>
        <div class="form-group">
          <label>{{ $t('changePassword.captcha') }}</label>
          <div class="captcha-row">
            <input class="form-control" v-model="pwdCaptcha" :placeholder="$t('changePassword.captcha')" />
            <img :src="pwdCaptchaSrc" class="captcha-img" @click="refreshPwdCaptcha" alt=""/>
          </div>
        </div>
        <div class="modal-actions" style="margin-top:1rem">
          <button class="btn btn-primary" @click="submitChangePwd">{{ $t('changePassword.submit') }}</button>
          <button class="btn btn-secondary" @click="closePwdModal">{{ $t('changePassword.cancel') }}</button>
        </div>
      </div>
    </div>

    <!-- 退出确认模态框 -->
    <div v-if="showLogoutModal" class="modal-overlay" @click.self="showLogoutModal = false">
      <div class="modal-box">
        <p class="modal-title">{{ $t('nav.confirmLogout') }}</p>
        <div class="modal-actions">
          <button class="btn btn-primary" @click="doLogout">{{ $t('nav.confirmLogoutBtn') }}</button>
          <button class="btn btn-secondary" @click="showLogoutModal = false">{{ $t('nav.cancel') }}</button>
        </div>
      </div>
    </div>
      </div>
    </n-message-provider>
    </n-config-provider>
  </template>

<script setup lang="ts">
import { computed, inject, onMounted, onUnmounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute } from 'vue-router'
import { useSession } from './composables/useSession'
import { changePassword, logout as apiLogout, captchaImage } from './services/api'
import { setLocale, getSupportedLangs } from './locales'

const { locale, t } = useI18n()
const route = useRoute()
const isLoginPage = computed(() => route.path === '/account/login')
const naiveLocaleMap = inject<Record<string, { locale: any; dateLocale: any }>>('naiveLocale')!
const naiveLocale = computed(() => naiveLocaleMap[locale.value] || naiveLocaleMap['en-US'])

function switchLang(lang: string) {
  locale.value = lang as 'zh-CN' | 'en-US'
  setLocale(lang)
}

const themeOverrides: Record<string, Record<string, string>> = {
  common: {
    primaryColor: '#1677ff',
    primaryColorHover: '#4096ff',
    primaryColorPressed: '#0958d9',
    primaryColorSuppl: '#1677ff',
    successColor: '#1677ff',
    successColorHover: '#4096ff',
    warningColor: '#fa8c16',
    errorColor: '#ff4d4f',
    infoColor: '#1677ff',
    borderColor: '#e2e8f0',
  },
}

const { username, session, load } = useSession()
const menuOpen = ref(false)
const showLogoutModal = ref(false)
const isDev = import.meta.env.DEV
const supportedLangs = ref(getSupportedLangs())
const isAdmin = computed(() => {
  const u = username.value
  const c = session.value.claims
  return u === 'admin' || c.some(x => x.value === 'admin')
})

// 修改密码表单
const showPwdModal = ref(false)
const pwdOld = ref('')
const pwdNew = ref('')
const pwdConfirm = ref('')
const pwdCaptcha = ref('')
const pwdCaptchaSrc = ref('')
const pwdMsg = ref('')
const pwdOk = ref(false)

onMounted(async () => {
  await load()
})

function onDocClick() { menuOpen.value = false }
onMounted(() => document.addEventListener('click', onDocClick))
onUnmounted(() => document.removeEventListener('click', onDocClick))

function toggleMenu(e: MouseEvent) {
  e.stopPropagation()
  menuOpen.value = !menuOpen.value
}

function openChangePwdModal() {
  menuOpen.value = false
  pwdOld.value = ''
  pwdNew.value = ''
  pwdConfirm.value = ''
  pwdCaptcha.value = ''
  pwdMsg.value = ''
  pwdOk.value = false
  refreshPwdCaptcha()
  showPwdModal.value = true
}
function closePwdModal() { showPwdModal.value = false }
async function refreshPwdCaptcha() { pwdCaptchaSrc.value = await captchaImage() }

async function submitChangePwd() {
  pwdMsg.value = ''
  if (!pwdOld.value || !pwdNew.value || !pwdConfirm.value) { pwdMsg.value = t('changePassword.fillAll'); pwdOk.value = false; return }
  if (pwdNew.value !== pwdConfirm.value) { pwdMsg.value = t('changePassword.mismatch'); pwdOk.value = false; return }

  const data = await changePassword({
    userName: username.value, oldPassword: pwdOld.value,
    newPassword: pwdNew.value, confirmNewPassword: pwdConfirm.value,
    captchaCode: pwdCaptcha.value, button: 'login'
  }) as { code: number; message?: string }
  if (data.code === 200) {
    pwdMsg.value = t('changePassword.success')
    pwdOk.value = true
    setTimeout(() => { closePwdModal() }, 1200)
  } else {
    pwdMsg.value = data.message || t('changePassword.failed')
    pwdOk.value = false
    refreshPwdCaptcha()
  }
}

function openLogoutModal() {
  menuOpen.value = false
  showLogoutModal.value = true
}
async function doLogout() {
  await apiLogout()
  window.location.href = '/welcome'
}
</script>

<style scoped>
.navbar-trigger {
  display: flex; align-items: center; gap: 0.25rem; cursor: pointer;
  padding: 0.25rem 0.5rem; border-radius: 4px; user-select: none;
}
.navbar-trigger:hover { background: var(--border); }
.navbar-arrow { font-size: 0.65rem; transition: transform 0.15s; }
.navbar-arrow.open { transform: rotate(180deg); }
.navbar-user { position: relative; }

.navbar-menu {
  position: absolute; top: 100%; right: 0; z-index: 200;
  background: var(--surface); border: 1px solid var(--border);
  border-radius: var(--radius); box-shadow: 0 4px 12px rgba(0,0,0,0.1); margin-top: 4px;
  overflow: hidden; white-space: nowrap;
}
.navbar-menu-item { padding: 0.5rem 1rem; font-size: 0.875rem; cursor: pointer; }
.navbar-menu-item:hover { background: #f8fafc; }
.navbar-menu-divider { height: 1px; background: var(--border); margin: 0.25rem 0; }

.navbar-lang { display:flex; align-items:center; margin-right:0.75rem; gap:0; }
.lang-btn { background:none; border:none; cursor:pointer; font-size:0.8125rem; padding:2px 6px; border-radius:3px; color:var(--text-muted); font-family:var(--font); }
.lang-btn.active { color:var(--primary); font-weight:600; background:rgba(37,99,235,0.08); }
.lang-divider { color:var(--border); font-size:0.75rem; }

.nav-admin {
  width: 100px; text-align: center; padding: 0.375rem 0; border-radius: var(--radius);
  transition: background 0.15s, color 0.15s;
}
.nav-admin:hover { background: rgba(37,99,235,0.08); color: var(--primary) !important; }
.nav-admin.router-link-exact-active { background: rgba(37,99,235,0.12); color: var(--primary) !important; font-weight: 600; }
.navbar-spacer { flex: 1; }

/* 登录页透明导航 */
.navbar-transparent {
  background: transparent !important;
  border-bottom-color: transparent !important;
  box-shadow: none !important;
}
.navbar-transparent .navbar-brand,
.navbar-transparent .nav-link,
.navbar-transparent .lang-btn,
.navbar-transparent .navbar-username { color: #fff !important; text-shadow: 0 1px 4px rgba(0,0,0,0.3); }
.navbar-transparent .lang-btn.active { background: rgba(255,255,255,0.2); color: #fff !important; }

.modal-overlay {
  position: fixed; inset: 0; z-index: 1000;
  background: rgba(0,0,0,0.35);
  display: flex; align-items: center; justify-content: center;
}
.modal-box {
  background: var(--surface); border-radius: var(--radius);
  padding: 2rem; box-shadow: 0 20px 60px rgba(0,0,0,0.2);
  min-width: 280px; text-align: center;
}
.modal-form-box { min-width: 360px; text-align: left; }
.modal-title { font-size: 1rem; font-weight: 600; margin-bottom: 1.5rem; text-align: center; }
.modal-actions { display: flex; gap: 0.75rem; justify-content: center; }
.modal-actions :deep(.btn) { min-width: 100px; }
.modal-actions-stack :deep(.btn) { min-width: 0; }
</style>
