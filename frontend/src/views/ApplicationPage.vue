<!-- Application 管理页：NaiveUI 6-tab -->
<template>
  <div class="admin-page">
    <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:16px">
      <h2>Applications</h2>
      <n-button type="primary" @click="openAdd">添加</n-button>
    </div>
    <div class="app-list" v-if="apps.length">
      <div class="app-card" v-for="a in apps" :key="a.id">
        <div class="app-card-header">
          <span class="app-card-title">{{ a.displayName || a.clientId }}</span>
          <n-tag :size="'small'" :type="a.enabled==='true'?'info':'error'">{{ a.enabled==='true'?'启用':'禁用' }}</n-tag>
        </div>
        <div class="app-card-info">
          <span class="app-card-meta">{{ a.clientId }}</span>
          <span class="app-card-meta">· {{ a.applicationType||'web' }}</span>
          <span class="app-card-meta">· {{ a.clientType }}</span>
          <span class="app-card-meta" v-if="a.clientUrl">· {{ a.clientUrl }}</span>
        </div>
        <div class="app-card-tags" v-if="(a.grantTypes||[]).length">
          <span class="app-card-label">Grants</span>
          <n-tag v-for="g in a.grantTypes" :key="g" size="tiny" :bordered="true" type="default">{{ g }}</n-tag>
        </div>
        <div class="app-card-tags" v-if="(a.scopes||[]).length">
          <span class="app-card-label">Scopes</span>
          <n-tag v-for="s in a.scopes" :key="s" size="tiny" :bordered="true">{{ s }}</n-tag>
        </div>
        <div class="app-card-actions">
          <n-popconfirm @positive-click="delApp(a)"><template #trigger><n-button size="small" text type="error">删除</n-button></template>确认删除？</n-popconfirm>
          <n-button size="small" text type="primary" @click="openEdit(a)">编辑</n-button>
        </div>
      </div>
    </div>
    <p v-else style="color:var(--text-muted)">暂无数据</p>

    <n-modal :show="showModal" :title="editing?'编辑 Application':'添加 Application'" @update:show="showModal=$event"
      preset="card" style="width:740px;min-height:620px" :mask-closable="false">
      <n-form label-placement="top" size="small">
        <n-tabs type="segment" animated style="min-height:520px">
          <n-tab-pane name="basic" tab="Basic">
            <n-grid :cols="2" :x-gap="12">
              <n-form-item-gi label="ClientId"><n-input v-model:value="form.clientId" :disabled="editing"/></n-form-item-gi>
              <n-form-item-gi label="DisplayName"><n-input v-model:value="form.displayName"/></n-form-item-gi>
              <n-form-item-gi label="AppType"><n-select v-model:value="form.applicationType" :options="appTypes"/></n-form-item-gi>
              <n-form-item-gi label="ClientType"><n-select v-model:value="form.clientType" :options="clientTypes"/></n-form-item-gi>
              <n-form-item-gi label="ConsentType"><n-select v-model:value="form.consentType" :options="consentTypes"/></n-form-item-gi>
              <n-form-item-gi label="ClientSecret">
                <n-input v-model:value="form.clientSecret" type="password"
                  :disabled="form.clientType==='public'" :placeholder="form.clientType==='public'?'public 客户端无需 secret':'留空不修改'"/>
              </n-form-item-gi>
              <n-form-item-gi label="RequirePkce"><n-switch v-model:value="form.requirePkce"/></n-form-item-gi>
              <n-form-item-gi label="Enabled"><n-switch v-model:value="form.enabled"/></n-form-item-gi>
            </n-grid>
          </n-tab-pane>
          <n-tab-pane name="tokens" tab="Tokens">
            <n-grid :cols="3" :x-gap="12">
              <n-form-item-gi label="AccessToken">
                <n-input-number v-model:value="form.accessTokenLifetime" :min="30" placeholder="3600"/>
              </n-form-item-gi>
              <n-form-item-gi label="AuthCode">
                <n-input-number v-model:value="form.authorizationCodeLifetime" :min="10" placeholder="300"/>
              </n-form-item-gi>
              <n-form-item-gi label="RefreshToken">
                <n-input-number v-model:value="form.refreshTokenLifetime" :min="30" placeholder="1209600"/>
              </n-form-item-gi>
              <n-form-item-gi label="IdToken">
                <n-input-number v-model:value="form.identityTokenLifetime" :min="30" placeholder="3600"/>
              </n-form-item-gi>
              <n-form-item-gi label="DeviceCode">
                <n-input-number v-model:value="form.deviceCodeLifetime" :min="10" placeholder="300"/>
              </n-form-item-gi>
              <n-form-item-gi label="UserCode">
                <n-input-number v-model:value="form.userCodeLifetime" :min="10" placeholder="300"/>
              </n-form-item-gi>
            </n-grid>
          </n-tab-pane>
          <n-tab-pane name="grants" tab="Grants">
            <n-checkbox-group v-model:value="form.selectedGrantTypes">
              <n-space>
                <n-checkbox v-for="gt in availableGrantTypes" :key="gt" :value="gt" :label="gt"/>
              </n-space>
            </n-checkbox-group>
          </n-tab-pane>
          <n-tab-pane name="scopes" tab="Scopes">
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
          <n-tab-pane name="uris" tab="URIs">
            <n-form-item label="RedirectUris（每行一个）"><n-input v-model:value="form.redirectUrisText" type="textarea" :rows="4"/></n-form-item>
            <n-form-item label="PostLogoutRedirectUris"><n-input v-model:value="form.postLogoutRedirectUrisText" type="textarea" :rows="3"/></n-form-item>
          </n-tab-pane>
          <n-tab-pane name="display" tab="Display">
            <n-form-item label="Client URL"><n-input v-model:value="form.clientUrl" placeholder="https://example.com"/></n-form-item>
            <n-form-item label="Client Logo URL"><n-input v-model:value="form.clientLogoUrl" placeholder="https://example.com/logo.png"/></n-form-item>
          </n-tab-pane>
        </n-tabs>
      </n-form>
      <template #footer>
        <div style="flex:1">
          <span v-if="valMsg" style="color:var(--error);font-size:0.8125rem">{{ valMsg }}</span>
        </div>
        <n-space justify="end">
          <n-button @click="showModal=false">取消</n-button>
          <n-button type="primary" @click="handleSave">保存</n-button>
        </n-space>
      </template>
    </n-modal>
  </div>
</template>

<script setup lang="ts">
import { computed, ref, onMounted } from 'vue'
import { NTag, NButton, useMessage, NPopconfirm } from 'naive-ui'

const api = document.querySelector('base')?.getAttribute('href') || '/'
const msg = useMessage()

const appTypes = [{label:'web',value:'web'},{label:'native',value:'native'},{label:'machine',value:'machine'}]
const clientTypes = [{label:'public',value:'public'},{label:'confidential',value:'confidential'}]
const consentTypes = [{label:'implicit',value:'implicit'},{label:'explicit',value:'explicit'}]

interface AppInfo { id:string; clientId:string; displayName?:string; applicationType:string; clientType:string; consentType:string; enabled:string; scopes:string[]; grantTypes:string[]; redirectUris:string[]; postLogoutRedirectUris:string[]; clientUrl?:string; clientLogoUrl?:string }
interface ScopeOpt { name:string; displayName?:string }
const apps=ref<AppInfo[]>([]); const allScopes=ref<ScopeOpt[]>([]); const availableGrantTypes=ref<string[]>([])
const showModal=ref(false); const editing=ref(false); const editId=ref(''); const valMsg=ref('')
const form=ref({ clientId:'',clientSecret:'',displayName:'',applicationType:'web',clientType:'confidential',consentType:'implicit',redirectUrisText:'',postLogoutRedirectUrisText:'',clientUrl:'',clientLogoUrl:'',accessTokenLifetime:null as number|null,authorizationCodeLifetime:null as number|null,refreshTokenLifetime:null as number|null,identityTokenLifetime:null as number|null,deviceCodeLifetime:null as number|null,userCodeLifetime:null as number|null,enabled:true,requirePkce:true,selectedScopes:[] as string[],selectedGrantTypes:[] as string[] })

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

function openAdd(){ editing.value=false; editId.value=''; form.value={ clientId:'',clientSecret:'',displayName:'',applicationType:'web',clientType:'confidential',consentType:'implicit',redirectUrisText:'',postLogoutRedirectUrisText:'',clientUrl:'',clientLogoUrl:'',accessTokenLifetime:null,authorizationCodeLifetime:null,refreshTokenLifetime:null,identityTokenLifetime:null,deviceCodeLifetime:null,userCodeLifetime:null,enabled:true,requirePkce:true,selectedScopes:[],selectedGrantTypes:[] }; showModal.value=true }
function openEdit(a:AppInfo){ editing.value=true; editId.value=a.id; form.value={ clientId:a.clientId,clientSecret:'',displayName:a.displayName||'',applicationType:a.applicationType||'web',clientType:a.clientType,consentType:a.consentType,redirectUrisText:(a.redirectUris||[]).join('\n'),postLogoutRedirectUrisText:(a.postLogoutRedirectUris||[]).join('\n'),clientUrl:a.clientUrl||'',clientLogoUrl:a.clientLogoUrl||'',accessTokenLifetime:null,authorizationCodeLifetime:null,refreshTokenLifetime:null,identityTokenLifetime:null,deviceCodeLifetime:null,userCodeLifetime:null,enabled:a.enabled==='true',requirePkce:true,selectedScopes:[...(a.scopes||[])],selectedGrantTypes:[...(a.grantTypes||[])] }; showModal.value=true }

async function handleSave(){
  valMsg.value=''
  if(!form.value.clientId.trim()){ valMsg.value='ClientId 不能为空'; return }
  if(form.value.clientType==='confidential' && !form.value.clientSecret.trim()){ valMsg.value='confidential 客户端必须设置 ClientSecret'; return }
  if(form.value.selectedGrantTypes.includes('authorization_code') && !form.value.redirectUrisText.trim()){ valMsg.value='authorization_code grant 必须设置 RedirectUris'; return }
  const body={ clientId:form.value.clientId,clientSecret:form.value.clientSecret||null,displayName:form.value.displayName,applicationType:form.value.applicationType,clientType:form.value.clientType,consentType:form.value.consentType,redirectUris:form.value.redirectUrisText.split('\n').filter(s=>s.trim()),postLogoutRedirectUris:form.value.postLogoutRedirectUrisText.split('\n').filter(s=>s.trim()),clientUrl:form.value.clientUrl||null,clientLogoUrl:form.value.clientLogoUrl||null,accessTokenLifetime:form.value.accessTokenLifetime,authorizationCodeLifetime:form.value.authorizationCodeLifetime,refreshTokenLifetime:form.value.refreshTokenLifetime,identityTokenLifetime:form.value.identityTokenLifetime,deviceCodeLifetime:form.value.deviceCodeLifetime,userCodeLifetime:form.value.userCodeLifetime,scopes:form.value.selectedScopes,grantTypes:form.value.selectedGrantTypes,enabled:form.value.enabled,requirePkce:form.value.requirePkce }
  const url=editing.value?`${api}api/applications/${editId.value}`:`${api}api/applications`
  const r=await fetch(url,{method:editing.value?'PUT':'POST',credentials:'include',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)})
  const d=await r.json(); if(d.code===200){ showModal.value=false; await loadApps(); msg.success('保存成功') } else msg.error(d.message||'操作失败')
}

async function delApp(a:AppInfo){ const r=await fetch(`${api}api/applications/${a.id}`,{method:'DELETE',credentials:'include'}); const d=await r.json(); if(d.code===200){ await loadApps(); msg.success('删除成功') } else msg.error(d.message||'删除失败') }
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
