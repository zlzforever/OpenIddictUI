<!-- Application 管理页：antd Table + Modal -->
<template>
  <div class="admin-page">
    <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:16px">
      <h2>Applications</h2>
      <a-button type="primary" @click="openAdd">添加</a-button>
    </div>
    <a-table :columns="columns" :data-source="apps" row-key="id" size="small" :pagination="false" bordered>
      <template #bodyCell="{ column, record }">
        <template v-if="column.key === 'clientType'">
          <a-tag :color="record.clientType === 'public' ? 'green' : 'blue'">{{ record.clientType }}</a-tag>
        </template>
        <template v-if="column.key === 'enabled'">
          <a-tag :color="record.enabled === 'true' ? 'green' : 'red'">{{ record.enabled === 'true' ? '启用' : '禁用' }}</a-tag>
        </template>
        <template v-if="column.key === 'scopes'">
          <a-tag v-for="s in record.scopes" :key="s" style="margin:1px">{{ s }}</a-tag>
        </template>
        <template v-if="column.key === 'action'">
          <a-space>
            <a-button type="link" size="small" @click="openEdit(record)">编辑</a-button>
            <a-popconfirm title="确认删除？" @confirm="delApp(record)">
              <a-button type="link" size="small" danger>删除</a-button>
            </a-popconfirm>
          </a-space>
        </template>
      </template>
    </a-table>

    <a-modal :open="showModal" :title="editing ? '编辑' : '添加'" @ok="handleSave" @cancel="showModal=false" width="680px">
      <a-form layout="vertical">
        <a-row :gutter="16">
          <a-col :span="12"><a-form-item label="ClientId"><a-input v-model:value="form.clientId" :disabled="editing"/></a-form-item></a-col>
          <a-col :span="12"><a-form-item label="DisplayName"><a-input v-model:value="form.displayName"/></a-form-item></a-col>
        </a-row>
        <a-row :gutter="16">
          <a-col :span="8"><a-form-item label="ClientType"><a-select v-model:value="form.clientType"><a-select-option value="public">public</a-select-option><a-select-option value="confidential">confidential</a-select-option></a-select></a-form-item></a-col>
          <a-col :span="8"><a-form-item label="ConsentType"><a-select v-model:value="form.consentType"><a-select-option value="implicit">implicit</a-select-option><a-select-option value="explicit">explicit</a-select-option></a-select></a-form-item></a-col>
          <a-col :span="8"><a-form-item label="ClientSecret"><a-input-password v-model:value="form.clientSecret" placeholder="留空不修改"/></a-form-item></a-col>
        </a-row>
        <a-form-item label="RedirectUris（每行一个）"><a-textarea v-model:value="form.redirectUrisText" :rows="3"/></a-form-item>
        <a-form-item label="PostLogoutRedirectUris"><a-textarea v-model:value="form.postLogoutRedirectUrisText" :rows="2"/></a-form-item>
        <a-form-item label="Scopes">
          <a-checkbox-group v-model:value="form.selectedScopes">
            <a-checkbox v-for="s in allScopes" :key="s.name" :value="s.name" style="margin-right:12px">{{s.displayName||s.name}}</a-checkbox>
          </a-checkbox-group>
        </a-form-item>
        <a-row :gutter="16">
          <a-col :span="12"><a-form-item label="RequirePkce"><a-switch v-model:checked="form.requirePkce"/></a-form-item></a-col>
          <a-col :span="12"><a-form-item label="Enabled"><a-switch v-model:checked="form.enabled"/></a-form-item></a-col>
        </a-row>
      </a-form>
    </a-modal>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { message } from 'ant-design-vue'

const api = document.querySelector('base')?.getAttribute('href') || '/'

interface AppInfo { id:string; clientId:string; displayName?:string; clientType:string; consentType:string; enabled:string; scopes:string[]; redirectUris:string[]; postLogoutRedirectUris:string[] }
interface ScopeOpt { name:string; displayName?:string }

const apps = ref<AppInfo[]>([])
const allScopes = ref<ScopeOpt[]>([])
const showModal = ref(false); const editing = ref(false); const editId = ref('')
const form = ref({ clientId:'',clientSecret:'',displayName:'',clientType:'confidential',consentType:'implicit',redirectUrisText:'',postLogoutRedirectUrisText:'',enabled:true,requirePkce:true,selectedScopes:[] as string[] })

const columns = [
  { title:'ClientId', dataIndex:'clientId', key:'clientId', ellipsis:true, width:180 },
  { title:'Name', dataIndex:'displayName', key:'displayName', width:120 },
  { title:'Type', key:'clientType', width:100 },
  { title:'Scopes', key:'scopes', width:200 },
  { title:'Status', key:'enabled', width:80 },
  { title:'操作', key:'action', width:150, fixed:'right' as const },
]

async function loadApps(){ const r=await fetch(`${api}api/applications`,{credentials:'include'}); if(r.ok) apps.value=(await r.json()).data||[] }
async function loadScopes(){ const r=await fetch(`${api}api/scopes`,{credentials:'include'}); if(r.ok) allScopes.value=(await r.json()).data||[] }
onMounted(async ()=>{ await loadApps(); await loadScopes() })

function openAdd(){ editing.value=false; editId.value=''; form.value={ clientId:'',clientSecret:'',displayName:'',clientType:'confidential',consentType:'implicit',redirectUrisText:'',postLogoutRedirectUrisText:'',enabled:true,requirePkce:true,selectedScopes:[] }; showModal.value=true }
function openEdit(a:AppInfo){ editing.value=true; editId.value=a.id; form.value={ clientId:a.clientId,clientSecret:'',displayName:a.displayName||'',clientType:a.clientType,consentType:a.consentType,redirectUrisText:(a.redirectUris||[]).join('\n'),postLogoutRedirectUrisText:(a.postLogoutRedirectUris||[]).join('\n'),enabled:a.enabled==='true',requirePkce:true,selectedScopes:a.scopes||[] }; showModal.value=true }

async function handleSave(){
  const body={ clientId:form.value.clientId,clientSecret:form.value.clientSecret||null,displayName:form.value.displayName,clientType:form.value.clientType,consentType:form.value.consentType,redirectUris:form.value.redirectUrisText.split('\n').filter(s=>s.trim()),postLogoutRedirectUris:form.value.postLogoutRedirectUrisText.split('\n').filter(s=>s.trim()),scopes:form.value.selectedScopes,grantTypes:['authorization_code','refresh_token'],enabled:form.value.enabled,requirePkce:form.value.requirePkce }
  const url=editing.value?`${api}api/applications/${editId.value}`:`${api}api/applications`
  const r=await fetch(url,{method:editing.value?'PUT':'POST',credentials:'include',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)})
  const d=await r.json()
  if(d.code===200){ showModal.value=false; await loadApps(); message.success('保存成功') } else message.error(d.message||'操作失败')
}

async function delApp(a:AppInfo){ const r=await fetch(`${api}api/applications/${a.id}`,{method:'DELETE',credentials:'include'}); const d=await r.json(); if(d.code===200){ await loadApps(); message.success('删除成功') } else message.error(d.message||'删除失败') }
</script>

<style scoped></style>
