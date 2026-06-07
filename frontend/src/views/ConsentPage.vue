<!--
  ============================================================
  OAuth Consent 授权同意页
  流程：
    ① 页面加载 → GET /api/consent/{id} 获取客户端信息 + scope 列表
       （id 由服务端 AuthorizationController 生成，存入 HybridCache）
    ② 展示客户端名称/Logo、请求的 scope（分 identity + resource 两类）
    ③ 用户点 "Yes" → POST /api/consent/{id} { button:"yes", scopesConsented:[...] }
       → 服务端创建永久授权 → 返回 { data: { location } }
       → window.location 跳回 /connect/authorize → 签发 authorization_code
    ④ 用户点 "No" → POST /api/consent/{id} { button:"no" }
       → 服务端返回 access_denied → 回到客户端回调
  ============================================================
-->
<template>
  <div class="card" v-if="data">
    <div class="card-header"><h1>Authorization Request</h1></div>
    <div class="consent-client">
      <img alt="" v-if="data.clientLogoUrl" :src="data.clientLogoUrl" class="consent-logo" />
      <div>
        <div class="consent-client-name">{{ data.clientName }}</div>
      </div>
    </div>
    <p class="text-muted">is requesting access to:</p>
    <h3 v-if="data.identityScopes?.length">Personal Information</h3>
    <ul class="scope-list" v-if="data.identityScopes?.length">
      <li v-for="s in data.identityScopes" :key="s.name" class="scope-item">
        <input type="checkbox" :checked="s.checked" :disabled="s.required" :value="s.name" />
        <div class="scope-info"><strong>{{ s.displayName }}</strong> <span v-if="s.required" class="scope-required">(required)</span></div>
      </li>
    </ul>
    <h3 v-if="data.resourceScopes?.length">Application Access</h3>
    <ul class="scope-list" v-if="data.resourceScopes?.length">
      <li v-for="s in data.resourceScopes" :key="s.name" class="scope-item">
        <input type="checkbox" :checked="s.checked" :disabled="s.required" :value="s.name" />
        <div class="scope-info"><strong>{{ s.displayName }}</strong></div>
      </li>
    </ul>
    <div style="display:flex;gap:0.5rem">
      <button class="btn btn-primary" style="flex:1" @click="submit('yes')">Yes, Allow</button>
      <button class="btn btn-secondary" style="flex:1" @click="submit('no')">No, Deny</button>
    </div>
  </div>
  <div class="card" v-else><p>Loading...</p></div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import { getConsent, submitConsent } from '../services/api'

const route = useRoute()
interface ScopeItem { name: string; displayName: string; checked: boolean; required: boolean }
interface ConsentData { id: string; clientName: string; clientLogoUrl?: string; clientUrl?: string; returnUrl: string; identityScopes: ScopeItem[]; resourceScopes: ScopeItem[] }
const data = ref<ConsentData | null>(null)
const consentId = route.params.id as string || ''
const returnUrl = ref('')

// ① 加载 consent 数据
onMounted(async () => {
  try {
    const res = (await getConsent(consentId)) as { data: ConsentData }
    data.value = res.data
    returnUrl.value = res.data.returnUrl
  } catch { data.value = null }
})

// ③/④ 提交用户选择（同意 / 拒绝）
async function submit(button: string) {
  const checkboxes = document.querySelectorAll<HTMLInputElement>('.scope-item input[type="checkbox"]:checked')
  const scopes = Array.from(checkboxes).map(c => c.value)
  const res = (await submitConsent(consentId, { button, scopesConsented: scopes })) as { data?: { location?: string } }
  if (res.data?.location) window.location.href = res.data.location
}
</script>
