<!--
  ============================================================
  根组件：导航栏 + router-view
  onMounted 时调用 load() 预加载 session，避免后续页面二次请求
  ============================================================
-->
<template>
  <div id="app-root">
    <nav class="navbar">
      <div class="navbar-inner">
        <div class="navbar-left">
          <a href="/" class="navbar-brand">OpenIddictUI</a>
          <a href="http://localhost:5175" target="_blank" class="nav-link">SPA Client</a>
        </div>
        <!-- 已登录时显示用户名 + 下拉菜单 -->
        <div class="navbar-user" v-if="username">
          <div class="navbar-trigger" @click="toggleMenu">
            <span class="navbar-username">{{ username }}</span>
            <span class="navbar-arrow" :class="{ open: menuOpen }">&#9662;</span>
          </div>
          <div v-if="menuOpen" class="navbar-menu" @click.stop>
            <div class="navbar-menu-item" @click="openChangePwdModal">修改密码</div>
            <div class="navbar-menu-item">个人信息</div>
            <div class="navbar-menu-divider"></div>
            <div class="navbar-menu-item" @click="openLogoutModal">退出</div>
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
        <h2 class="modal-title">修改密码</h2>
        <div v-if="pwdMsg" :class="['alert', pwdOk ? 'alert-success' : 'alert-danger', 'visible']">{{ pwdMsg }}</div>
        <div class="form-group">
          <label>原密码</label>
          <input type="password" class="form-control" v-model="pwdOld" placeholder="原密码" maxlength="50" />
        </div>
        <div class="form-group">
          <label>新密码</label>
          <input type="password" class="form-control" v-model="pwdNew" placeholder="新密码" maxlength="32" />
        </div>
        <div class="form-group">
          <label>确认新密码</label>
          <input type="password" class="form-control" v-model="pwdConfirm" placeholder="确认新密码" maxlength="32" />
        </div>
        <div class="form-group">
          <label>验证码</label>
          <div class="captcha-row">
            <input class="form-control" v-model="pwdCaptcha" placeholder="验证码" />
            <img :src="pwdCaptchaSrc" class="captcha-img" @click="refreshPwdCaptcha" alt=""/>
          </div>
        </div>
        <div class="modal-actions" style="margin-top:1rem">
          <button class="btn btn-primary" @click="submitChangePwd">确认修改</button>
          <button class="btn btn-secondary" @click="closePwdModal">取消</button>
        </div>
      </div>
    </div>

    <!-- 退出确认模态框 -->
    <div v-if="showLogoutModal" class="modal-overlay" @click.self="showLogoutModal = false">
      <div class="modal-box">
        <p class="modal-title">确认退出登录？</p>
        <div class="modal-actions">
          <button class="btn btn-primary" @click="doLogout">确认</button>
          <button class="btn btn-secondary" @click="showLogoutModal = false">取消</button>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { onMounted, onUnmounted, ref } from 'vue'
import { useSession } from './composables/useSession'
import { changePassword, logout as apiLogout, captchaImageUrl } from './services/api'

const { username, load } = useSession()
const menuOpen = ref(false)
const showLogoutModal = ref(false)

// 修改密码表单
const showPwdModal = ref(false)
const pwdOld = ref('')
const pwdNew = ref('')
const pwdConfirm = ref('')
const pwdCaptcha = ref('')
const pwdCaptchaSrc = ref('')
const pwdMsg = ref('')
const pwdOk = ref(false)

// 预加载 session 数据：router 守卫也会调用 load()
// 由于 useSession 内部已做并发去重，两个调用共享同一请求，不会重复
onMounted(load)

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
function refreshPwdCaptcha() { pwdCaptchaSrc.value = captchaImageUrl() }

async function submitChangePwd() {
  pwdMsg.value = ''
  // 前端校验
  if (!pwdOld.value || !pwdNew.value || !pwdConfirm.value) { pwdMsg.value = '请填写完整'; pwdOk.value = false; return }
  if (pwdNew.value !== pwdConfirm.value) { pwdMsg.value = '两次新密码不一致'; pwdOk.value = false; return }

  const data = await changePassword({
    userName: username.value, oldPassword: pwdOld.value,
    newPassword: pwdNew.value, confirmNewPassword: pwdConfirm.value,
    captchaCode: pwdCaptcha.value, button: 'login'
  }) as { code: number; message?: string }
  if (data.code === 200) {
    pwdMsg.value = '修改成功'
    pwdOk.value = true
    setTimeout(() => { closePwdModal() }, 1200)
  } else {
    pwdMsg.value = data.message || '修改失败'
    pwdOk.value = false
    refreshPwdCaptcha()
  }
}

// 退出
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

/* 模态框 */
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
