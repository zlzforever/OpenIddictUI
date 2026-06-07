<!-- Application 管理页：NaiveUI -->
<template>
  <div class="admin-page">
    <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:16px">
      <h2>Applications</h2>
      <n-button type="primary" @click="openAdd">添加</n-button>
    </div>
    <n-data-table :columns="columns" :data="apps" :bordered="true" size="small" :pagination="false" />

    <n-modal :show="showModal" :title="editing ? '编辑 Application' : '添加 Application'" @update:show="showModal=$event"
      preset="card" style="width:720px;min-height:520px" :mask-closable="false" @positive-click="handleSave" positive-text="保存">
      <n-form label-placement="top" size="small">
        <n-tabs type="segment" animated style="min-height:440px">
          <n-tab-pane name="basic" tab="Basic">
            <n-grid :cols="2" :x-gap="12">
              <n-form-item-gi label="ClientId"><n-input v-model:value="form.clientId" :disabled="editing" /></n-form-item-gi>
              <n-form-item-gi label="DisplayName"><n-input v-model:value="form.displayName" /></n-form-item-gi>
              <n-form-item-gi label="ClientType">
                <n-select v-model:value="form.clientType" :options="[{label:'public',value:'public'},{label:'confidential',value:'confidential'}]" />
              </n-form-item-gi>
              <n-form-item-gi label="ConsentType">
                <n-select v-model:value="form.consentType" :options="[{label:'implicit',value:'implicit'},{label:'explicit',value:'explicit'}]" />
              </n-form-item-gi>
              <n-form-item-gi label="ClientSecret" :span="2">
                <n-input v-model:value="form.clientSecret" type="password" :disabled="form.clientType==='public'"
                  :placeholder="form.clientType==='public'?'public 客户端无需 secret':'留空不修改'" />
              </n-form-item-gi>
              <n-form-item-gi label="Enabled"><n-switch v-model:value="form.enabled" /></n-form-item-gi>
              <n-form-item-gi label="RequirePkce"><n-switch v-model:value="form.requirePkce" /></n-form-item-gi>
            </n-grid>
          </n-tab-pane>
          <n-tab-pane name="grants" tab="Grants">
            <n-checkbox-group v-model:value="form.selectedGrantTypes">
              <n-space>
                <n-checkbox v-for="gt in availableGrantTypes" :key="gt" :value="gt" :label="gt" />
              </n-space>
            </n-checkbox-group>
          </n-tab-pane>
          <n-tab-pane name="scopes" tab="Scopes">
            <n-checkbox-group v-model:value="form.selectedScopes">
              <n-space>
                <n-checkbox v-for="s in allScopes" :key="s.name" :value="s.name" :label="s.displayName||s.name" />
              </n-space>
            </n-checkbox-group>
          </n-tab-pane>
          <n-tab-pane name="uris" tab="URIs">
            <n-form-item label="RedirectUris（每行一个）"><n-input v-model:value="form.redirectUrisText" type="textarea" :rows="4" /></n-form-item>
            <n-form-item label="PostLogoutRedirectUris"><n-input v-model:value="form.postLogoutRedirectUrisText" type="textarea" :rows="3" /></n-form-item>
          </n-tab-pane>
          <n-tab-pane name="settings" tab="Settings">
            <n-form-item label="Client URL"><n-input v-model:value="form.clientUrl" placeholder="https://example.com" /></n-form-item>
            <n-form-item label="Client Logo URL"><n-input v-model:value="form.clientLogoUrl" placeholder="https://example.com/logo.png" /></n-form-item>
          </n-tab-pane>
        </n-tabs>
      </n-form>
    </n-modal>
  </div>
</template>

<script setup lang="ts">
import { computed, h, ref, onMounted } from 'vue'
import { NTag, NButton, useMessage, NPopconfirm } from 'naive-ui'

const api = document.querySelector('base')?.getAttribute('href') || '/'
const msg = useMessage()

interface AppInfo { id:string; clientId:string; displayName?:string; clientType:string; consentType:string; enabled:string; scopes:string[]; grantTypes:string[]; redirectUris:string[]; postLogoutRedirectUris:string[]; clientUrl?:string; clientLogoUrl?:string }
interface ScopeOpt { name:string; displayName?:string }
const apps=ref<AppInfo[]>([]); const allScopes=ref<ScopeOpt[]>([]); const availableGrantTypes=ref<string[]>([])
const showModal=ref(false); const editing=ref(false); const editId=ref('')
const form=ref({ clientId:'',clientSecret:'',displayName:'',clientType:'confidential',consentType:'implicit',redirectUrisText:'',postLogoutRedirectUrisText:'',clientUrl:'',clientLogoUrl:'',enabled:true,requirePkce:true,selectedScopes:[] as string[],selectedGrantTypes:[] as string[] })

const scopeOptions = computed(() => allScopes.value.map(s => ({
  label: s.displayName || s.name,
  value: s.name,
})))

const columns = [
  { title:'ClientId', key:'clientId', ellipsis:{tooltip:true}, width:160, render:(row:AppInfo)=>row.clientId },
  { title:'Name', key:'displayName', width:100, render:(row:AppInfo)=>row.displayName },
  { title:'Type', key:'clientType', width:80, render:(row:AppInfo)=>h(NTag,{size:'small',type:row.clientType==='public'?'info':'default'},{default:()=>row.clientType}) },
  { title:'Grants', key:'grantTypes', width:120, render:(row:AppInfo)=>h('div',{style:'display:flex;flex-wrap:wrap;gap:2px'},(row.grantTypes||[]).map(g=>h(NTag,{size:'small',type:'default',bordered:true},{default:()=>g}))) },
  { title:'Scopes', key:'scopes', width:160, render:(row:AppInfo)=>h('div',{style:'display:flex;flex-wrap:wrap;gap:2px'},(row.scopes||[]).map(s=>h(NTag,{size:'small',bordered:true},{default:()=>s}))) },
  { title:'Status', key:'enabled', width:70, render:(row:AppInfo)=>h(NTag,{size:'small',type:row.enabled==='true'?'info':'error'},{default:()=>row.enabled==='true'?'启用':'禁用'}) },
  { title:'操作', key:'action', width:130, render:(row:AppInfo)=>h('div',{style:'display:flex;gap:6px'},[
    h(NButton,{size:'tiny',text:true,type:'primary',onClick:()=>openEdit(row)},{default:()=>'编辑'}),
    h(NPopconfirm,{onPositiveClick:()=>delApp(row)},{trigger:()=>h(NButton,{size:'tiny',text:true,type:'error'},{default:()=>'删除'}),default:()=>'确认删除？'})
  ]) },
]

async function loadApps(){ const r=await fetch(`${api}api/applications`,{credentials:'include'}); if(r.ok) apps.value=(await r.json()).data||[] }
async function loadScopes(){ const r=await fetch(`${api}api/scopes`,{credentials:'include'}); if(r.ok) allScopes.value=(await r.json()).data||[] }
async function loadGrantTypes(){ const r=await fetch(`${api}api/applications/grant-types`,{credentials:'include'}); if(r.ok) availableGrantTypes.value=(await r.json()).data||[]; else availableGrantTypes.value=['authorization_code','refresh_token'] }
onMounted(async ()=>{ await loadApps(); await loadScopes(); await loadGrantTypes() })

function openAdd(){ editing.value=false; editId.value=''; form.value={ clientId:'',clientSecret:'',displayName:'',clientType:'confidential',consentType:'implicit',redirectUrisText:'',postLogoutRedirectUrisText:'',clientUrl:'',clientLogoUrl:'',enabled:true,requirePkce:true,selectedScopes:[],selectedGrantTypes:[] }; showModal.value=true }
function openEdit(a:AppInfo){ editing.value=true; editId.value=a.id; form.value={ clientId:a.clientId,clientSecret:'',displayName:a.displayName||'',clientType:a.clientType,consentType:a.consentType,redirectUrisText:(a.redirectUris||[]).join('\n'),postLogoutRedirectUrisText:(a.postLogoutRedirectUris||[]).join('\n'),clientUrl:a.clientUrl||'',clientLogoUrl:a.clientLogoUrl||'',enabled:a.enabled==='true',requirePkce:true,selectedScopes:a.scopes||[],selectedGrantTypes:a.grantTypes||[] }; showModal.value=true }

async function handleSave(){
  const body={ clientId:form.value.clientId,clientSecret:form.value.clientSecret||null,displayName:form.value.displayName,clientType:form.value.clientType,consentType:form.value.consentType,redirectUris:form.value.redirectUrisText.split('\n').filter(s=>s.trim()),postLogoutRedirectUris:form.value.postLogoutRedirectUrisText.split('\n').filter(s=>s.trim()),clientUrl:form.value.clientUrl||null,clientLogoUrl:form.value.clientLogoUrl||null,scopes:form.value.selectedScopes,grantTypes:form.value.selectedGrantTypes,enabled:form.value.enabled,requirePkce:form.value.requirePkce }
  const url=editing.value?`${api}api/applications/${editId.value}`:`${api}api/applications`
  const r=await fetch(url,{method:editing.value?'PUT':'POST',credentials:'include',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)})
  const d=await r.json()
  if(d.code===200){ showModal.value=false; await loadApps(); msg.success('保存成功') } else msg.error(d.message||'操作失败')
}

async function delApp(a:AppInfo){ const r=await fetch(`${api}api/applications/${a.id}`,{method:'DELETE',credentials:'include'}); const d=await r.json(); if(d.code===200){ await loadApps(); msg.success('删除成功') } else msg.error(d.message||'删除失败') }
</script>
