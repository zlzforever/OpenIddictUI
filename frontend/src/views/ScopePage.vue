<!-- Scope 管理页：NaiveUI -->
<template>
  <div class="admin-page">
    <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:16px">
      <h2>{{ $t('scopes.title') }}</h2>
      <n-button type="primary" @click="openAdd">{{ $t('scopes.add') }}</n-button>
    </div>
    <n-data-table :columns="columns" :data="scopes" :bordered="true" size="small" :pagination="false" />

    <n-modal :show="showModal" :title="editing ? $t('scopes.edit')+' Scope' : $t('scopes.add')+' Scope'" @update:show="showModal=$event"
      preset="card" style="width:500px" :mask-closable="false">
      <n-form label-placement="top" size="small">
        <n-form-item :label="$t('scopes.name')"><n-input v-model:value="form.name" :disabled="editing" /></n-form-item>
        <n-form-item :label="$t('scopes.displayName')"><n-input v-model:value="form.displayName" /></n-form-item>
        <n-form-item :label="$t('scopes.description')"><n-input v-model:value="form.description" /></n-form-item>
      </n-form>
      <template #footer>
        <n-space justify="end">
          <n-button @click="showModal=false">{{ $t('scopes.cancel') }}</n-button>
          <n-button type="primary" @click="handleSave">{{ $t('scopes.save') }}</n-button>
        </n-space>
      </template>
    </n-modal>
  </div>
</template>

<script setup lang="ts">
import { h, ref, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { NTag, NButton, useMessage, NPopconfirm } from 'naive-ui'

const { t } = useI18n()
const api = document.querySelector('base')?.getAttribute('href') || '/'
const msg = useMessage()

interface ScopeInfo { id:string; name:string; displayName?:string; description?:string; system?:boolean }
const scopes=ref<ScopeInfo[]>([]); const showModal=ref(false); const editing=ref(false); const editId=ref('')
const form=ref({ name:'',displayName:'',description:'' })

const columns = [
  { title:t('scopes.name'), key:'name', render:(row:ScopeInfo)=>row.name },
  { title:t('scopes.displayName'), key:'displayName', render:(row:ScopeInfo)=>row.displayName },
  { title:t('scopes.description'), key:'description', ellipsis:{tooltip:true}, render:(row:ScopeInfo)=>row.description },
  { title:t('scopes.edit'), key:'action', width:150, render:(row:ScopeInfo)=>h('div',{style:'display:flex;gap:6px'},[
    h(NButton,{size:'tiny',text:true,type:'primary',onClick:()=>openEdit(row)},{default:()=>t('scopes.edit')}),
    row.system ? h(NButton,{size:'tiny',text:true,disabled:true},{default:()=>t('scopes.systemScope')}) :
    h(NPopconfirm,{onPositiveClick:()=>delScope(row)},{trigger:()=>h(NButton,{size:'tiny',text:true,type:'error'},{default:()=>t('scopes.delete')}),default:()=>t('scopes.confirmDelete')})
  ]) },
]

async function loadData(){ const r=await fetch(`${api}api/scopes`,{credentials:'include'}); if(r.ok) scopes.value=(await r.json()).data||[] }
onMounted(loadData)

function openAdd(){ editing.value=false; editId.value=''; form.value={name:'',displayName:'',description:''}; showModal.value=true }
function openEdit(s:ScopeInfo){ editing.value=true; editId.value=s.id; form.value={name:s.name,displayName:s.displayName||'',description:s.description||''}; showModal.value=true }

async function handleSave(){
  const body={ name:form.value.name,displayName:form.value.displayName||null,description:form.value.description||null }
  const url=editing.value?`${api}api/scopes/${editId.value}`:`${api}api/scopes`
  const r=await fetch(url,{method:editing.value?'PUT':'POST',credentials:'include',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)})
  const d=await r.json()
  if(d.code===200){ showModal.value=false; await loadData(); msg.success(t('scopes.saveSuccess')) } else msg.error(d.message||t('scopes.saveFailed'))
}

async function delScope(s:ScopeInfo){ const r=await fetch(`${api}api/scopes/${s.id}`,{method:'DELETE',credentials:'include'}); const d=await r.json(); if(d.code===200){ await loadData(); msg.success(t('scopes.deleteSuccess')) } else msg.error(d.message||t('scopes.deleteFailed')) }
</script>
