<!-- Application 管理页：NaiveUI 6-tab -->
<template>
  <div class="admin-page">
    <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:16px">
      <h2>{{ $t('applications.title') }}</h2>
      <n-button type="primary" @click="openAdd">{{ $t('applications.add') }}</n-button>
    </div>
    <div class="app-list" v-if="apps.length">
      <div class="app-card" v-for="a in apps" :key="a.id">
        <div class="app-card-header">
          <span class="app-card-title">{{ a.displayName || a.clientId }}</span>
          <n-tag :size="'small'" :type="a.enabled==='true'?'info':'error'">{{ a.enabled==='true'?$t('applications.enabled'):$t('applications.disabled') }}</n-tag>
        </div>
        <div class="app-card-info">
          <span class="app-card-meta">{{ a.clientId }}</span>
          <span class="app-card-meta">· {{ a.applicationType||'web' }}</span>
          <span class="app-card-meta">· {{ a.clientType || 'public' }}</span>
          <span class="app-card-meta" v-if="a.clientUrl">· {{ a.clientUrl }}</span>
        </div>
        <div class="app-card-tags" v-if="(a.grantTypes||[]).length">
          <span class="app-card-label">{{ $t('applications.grants') }}</span>
          <n-tag v-for="g in a.grantTypes" :key="g" size="tiny" :bordered="true" type="default">{{ g }}</n-tag>
        </div>
        <div class="app-card-tags" v-if="(a.scopes||[]).length">
          <span class="app-card-label">{{ $t('applications.scopes') }}</span>
          <n-tag v-for="s in a.scopes" :key="s" size="tiny" :bordered="true">{{ s }}</n-tag>
        </div>
        <div class="app-card-actions">
          <n-popconfirm @positive-click="delApp(a)"><template #trigger><n-button size="small" text type="error">{{ $t('applications.delete') }}</n-button></template>{{ $t('applications.confirmDelete') }}</n-popconfirm>
          <n-button size="small" text type="primary" @click="openEdit(a)">{{ $t('applications.edit') }}</n-button>
        </div>
      </div>
    </div>
    <p v-else style="color:var(--text-muted)">{{ $t('applications.noData') }}</p>

    <n-modal :show="showModal" :title="editing?$t('applications.edit')+' Application':$t('applications.add')+' Application'" @update:show="updateModalVisibility"
      preset="card" style="width:740px;min-height:620px" :mask-closable="false">
      <n-form label-placement="top" size="small">
        <n-tabs type="segment" animated style="min-height:520px">
          <n-tab-pane name="basic" :tab="$t('applications.tabBasic')">
            <n-grid :cols="2" :x-gap="12">
              <n-form-item-gi :label="$t('applications.clientId')"><n-input v-model:value="form.clientId" :disabled="editing"/></n-form-item-gi>
              <n-form-item-gi :label="$t('applications.displayName')"><n-input v-model:value="form.displayName"/></n-form-item-gi>
              <n-form-item-gi :label="$t('applications.appType')"><n-select v-model:value="form.applicationType" :options="appTypes"/></n-form-item-gi>
              <n-form-item-gi :label="$t('applications.clientType')"><n-select v-model:value="form.clientType" :options="clientTypes"/></n-form-item-gi>
              <n-form-item-gi :label="$t('applications.consentType')"><n-select v-model:value="form.consentType" :options="consentTypes"/></n-form-item-gi>
              <n-form-item-gi :label="$t('applications.clientSecret')">
                <n-input v-model:value="form.clientSecret" type="password"
                  :disabled="form.clientType==='public'" :placeholder="form.clientType==='public'?$t('applications.noSecretForPublic'):$t('applications.leaveBlank')"/>
              </n-form-item-gi>
              <n-form-item-gi v-if="form.clientType==='confidential'" :label="$t('applications.jsonWebKeySet')">
                <n-input v-model:value="form.jsonWebKeySet" type="textarea" :rows="3"/>
              </n-form-item-gi>
              <n-form-item-gi :label="$t('applications.requirePkce')"><n-switch v-model:value="form.requirePkce"/></n-form-item-gi>
              <n-form-item-gi :label="$t('applications.enabledLabel')"><n-switch v-model:value="form.enabled"/></n-form-item-gi>
            </n-grid>
          </n-tab-pane>
          <n-tab-pane name="tokens" :tab="$t('applications.tabTokens')">
            <n-grid :cols="3" :x-gap="12">
              <n-form-item-gi :label="$t('applications.accessToken')">
                <n-input-number v-model:value="form.accessTokenLifetime" :min="30" placeholder="3600"/>
              </n-form-item-gi>
              <n-form-item-gi :label="$t('applications.authCode')">
                <n-input-number v-model:value="form.authorizationCodeLifetime" :min="10" placeholder="300"/>
              </n-form-item-gi>
              <n-form-item-gi :label="$t('applications.refreshToken')">
                <n-input-number v-model:value="form.refreshTokenLifetime" :min="30" placeholder="1209600"/>
              </n-form-item-gi>
              <n-form-item-gi :label="$t('applications.idToken')">
                <n-input-number v-model:value="form.identityTokenLifetime" :min="30" placeholder="3600"/>
              </n-form-item-gi>
              <n-form-item-gi :label="$t('applications.deviceCode')">
                <n-input-number v-model:value="form.deviceCodeLifetime" :min="10" placeholder="300"/>
              </n-form-item-gi>
              <n-form-item-gi :label="$t('applications.userCode')">
                <n-input-number v-model:value="form.userCodeLifetime" :min="10" placeholder="300"/>
              </n-form-item-gi>
            </n-grid>
          </n-tab-pane>
          <n-tab-pane name="grants" :tab="$t('applications.tabGrants')">
            <n-checkbox-group v-model:value="form.selectedGrantTypes">
              <n-space>
                <n-checkbox v-for="gt in availableGrantTypes" :key="gt" :value="gt" :label="gt"/>
              </n-space>
            </n-checkbox-group>
          </n-tab-pane>
          <n-tab-pane name="scopes" :tab="$t('applications.tabScopes')">
            <div style="min-height:80px">
            <div style="display:flex;flex-wrap:wrap;gap:8px;margin-bottom:10px">
              <n-tag v-for="s in sysScopes" :key="s.name" :type="form.selectedScopes.includes(s.name)?'primary':'default'"
                :bordered="true" size="medium" checkable :checked="form.selectedScopes.includes(s.name)"
                @update:checked="()=>toggleScope(s.name)" style="cursor:pointer;user-select:none"
                :style="{ borderStyle: form.selectedScopes.includes(s.name) ? 'solid' : 'dashed' }">
                {{ s.displayName||s.name }}
              </n-tag>
            </div>
            <div style="display:flex;flex-wrap:wrap;gap:8px">
              <n-tag v-for="s in apiScopes" :key="s.name" :type="form.selectedScopes.includes(s.name)?'primary':'default'"
                :bordered="true" size="medium" checkable :checked="form.selectedScopes.includes(s.name)"
                @update:checked="()=>toggleScope(s.name)" style="cursor:pointer;user-select:none"
                :style="{ borderStyle: form.selectedScopes.includes(s.name) ? 'solid' : 'dashed' }">
                {{ s.displayName||s.name }}
              </n-tag>
            </div>
            </div>
          </n-tab-pane>
          <n-tab-pane name="uris" :tab="$t('applications.tabUris')">
            <n-form-item :label="$t('applications.redirectUris')"><n-input v-model:value="form.redirectUrisText" type="textarea" :rows="4"/></n-form-item>
            <n-form-item :label="$t('applications.postLogoutUris')"><n-input v-model:value="form.postLogoutRedirectUrisText" type="textarea" :rows="3"/></n-form-item>
          </n-tab-pane>
          <n-tab-pane name="display" :tab="$t('applications.tabDisplay')">
            <n-form-item :label="$t('applications.clientUrl')"><n-input v-model:value="form.clientUrl" placeholder="https://example.com"/></n-form-item>
            <n-form-item :label="$t('applications.clientLogoUrl')"><n-input v-model:value="form.clientLogoUrl" placeholder="https://example.com/logo.png"/></n-form-item>
          </n-tab-pane>
        </n-tabs>
      </n-form>
      <template #footer>
        <div style="flex:1">
          <span v-if="valMsg" style="color:var(--error);font-size:0.8125rem">{{ valMsg }}</span>
        </div>
        <n-space justify="end">
          <n-button @click="closeModal">{{ $t('applications.close') }}</n-button>
          <n-button type="primary" @click="handleSave">{{ $t('applications.save') }}</n-button>
        </n-space>
      </template>
    </n-modal>
  </div>
</template>

<script setup lang="ts">
import { computed, ref, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { NTag, NButton, useMessage, NPopconfirm } from 'naive-ui'
import { getApplication, saveApplication } from '../services/api'
import {
  buildApplicationPayload,
  createApplicationForm,
  createApplicationEditController,
  type ApplicationDetail,
  type ApplicationEditState,
  type ApplicationForm,
  validateApplicationForm
} from './applicationForm'

const { t } = useI18n()
const api = document.querySelector('base')?.getAttribute('href') || '/'
const msg = useMessage()

const appTypes = [{label:'web',value:'web'},{label:'native',value:'native'},{label:'machine',value:'machine'}]
const clientTypes = [{label:'public',value:'public'},{label:'confidential',value:'confidential'}]
const consentTypes = [{label:'implicit',value:'implicit'},{label:'explicit',value:'explicit'}]

interface AppInfo extends ApplicationDetail { applicationType:string; clientType:string; consentType:string; enabled:string; scopes:string[]; grantTypes:string[]; redirectUris:string[]; postLogoutRedirectUris:string[] }
interface ScopeOpt { name:string; displayName?:string }
const apps=ref<AppInfo[]>([]); const allScopes=ref<ScopeOpt[]>([]); const availableGrantTypes=ref<string[]>([])
const showModal=ref(false); const editing=ref(false); const editId=ref(''); const valMsg=ref('')
const form=ref<ApplicationForm>(createApplicationForm())

function applyEditState(state: ApplicationEditState){
  form.value=state.form
  editing.value=state.editing
  editId.value=state.editId
  showModal.value=state.showModal
}

const editController=createApplicationEditController(getApplication, applyEditState)

const scopeOpts = computed(() => allScopes.value.map(s => ({ label: s.displayName||s.name, value: s.name })))

const sysNames = ['openid','profile','email','phone','address','roles','offline_access']
const sysScopes = computed(() => allScopes.value.filter(s=>sysNames.includes(s.name)))
const apiScopes = computed(() => allScopes.value.filter(s=>!sysNames.includes(s.name)))

function toggleScope(name:string){
  const i=form.value.selectedScopes.indexOf(name)
  if(i>=0) form.value.selectedScopes.splice(i,1)
  else form.value.selectedScopes.push(name)
}

async function loadApps(){ const r=await fetch(`${api}api/applications`,{credentials:'include'}); if(r.ok) apps.value=(await r.json()).data||[] }
async function loadScopes(){ const r=await fetch(`${api}api/scopes`,{credentials:'include'}); if(r.ok) allScopes.value=(await r.json()).data||[] }
async function loadGrantTypes(){ const r=await fetch(`${api}api/applications/grant-types`,{credentials:'include'}); if(r.ok) availableGrantTypes.value=(await r.json()).data||[]; else availableGrantTypes.value=['authorization_code','refresh_token'] }
onMounted(async ()=>{ await loadApps(); await loadScopes(); await loadGrantTypes() })

function openAdd(){ valMsg.value=''; editController.openAdd() }

function closeModal(){ valMsg.value=''; editController.close() }

function updateModalVisibility(value:boolean){
  if(value){ showModal.value=true }
  else { closeModal() }
}

async function openEdit(a:AppInfo){
  valMsg.value=''
  if(await editController.openEdit(a.id)==='failed'){ msg.error(t('applications.loadFailed')) }
}

async function handleSave(){
  valMsg.value=''
  const validation=validateApplicationForm(form.value,editing.value)
  if(validation){ valMsg.value=t(`applications.${validation}`); return }
  const d=await saveApplication(buildApplicationPayload(form.value,editing.value),editing.value?editId.value:undefined)
  if(d.code===200){ editController.close(); await loadApps(); msg.success(t('applications.saveSuccess')) } else msg.error(d.message||t('applications.saveFailed'))
}

async function delApp(a:AppInfo){ const r=await fetch(`${api}api/applications/${a.id}`,{method:'DELETE',credentials:'include'}); const d=await r.json(); if(d.code===200){ await loadApps(); msg.success(t('applications.deleteSuccess')) } else msg.error(d.message||t('applications.deleteFailed')) }
</script>

<style scoped>
.app-list { display:grid; grid-template-columns:repeat(auto-fill,minmax(340px,1fr)); gap:12px }
.app-card { background:#fff; border:1px solid var(--border); border-radius:8px; padding:16px; display:flex; flex-direction:column; gap:8px; transition:box-shadow .15s }
.app-card:hover { box-shadow:0 2px 8px rgba(0,0,0,0.08) }
.app-card-header { display:flex; justify-content:space-between; align-items:center }
.app-card-title { font-weight:600; font-size:0.9375rem }
.app-card-info { display:flex; gap:6px; flex-wrap:wrap }
.app-card-meta { font-size:0.75rem; color:var(--text-muted) }
.app-card-tags { display:flex; flex-wrap:wrap; gap:4px; align-items:center }
.app-card-label { font-size:0.75rem; color:var(--text-muted); margin-right:4px; white-space:nowrap }
.app-card-actions { display:flex; justify-content:flex-end; gap:12px; margin-top:4px }
</style>
