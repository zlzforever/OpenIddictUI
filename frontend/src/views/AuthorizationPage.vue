<!-- Authorizations 管理页：NaiveUI -->
<template>
  <div class="admin-page">
    <h2 style="margin-bottom:16px">{{ $t('authorizations.title') }}</h2>
    <n-data-table :columns="columns" :data="auths" :bordered="true" size="small" :pagination="false" />
  </div>
</template>

<script setup lang="ts">
import { h, ref, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { NTag, NButton, useMessage, NPopconfirm } from 'naive-ui'

const { t } = useI18n()
const api = document.querySelector('base')?.getAttribute('href') || '/'
const msg = useMessage()

interface AuthInfo { id:string; clientId:string; scopes:string[]; type:string; status:string; created:string }
const auths=ref<AuthInfo[]>([])

const columns = [
  { title:t('authorizations.client'), key:'clientId', width:180, render:(row:AuthInfo)=>row.clientId },
  { title:t('authorizations.scopes'), key:'scopes', width:250, render:(row:AuthInfo)=>h('div',{style:'display:flex;flex-wrap:wrap;gap:2px'},(row.scopes||[]).map(s=>h(NTag,{size:'small',bordered:true},{default:()=>s}))) },
  { title:t('authorizations.status'), key:'type', width:100, render:(row:AuthInfo)=>h(NTag,{size:'small',type:row.type==='Permanent'?'info':'default'},{default:()=>row.type}) },
  { title:t('authorizations.status'), key:'status', width:80, render:(row:AuthInfo)=>row.status },
  { title:t('authorizations.created'), key:'created', width:170, render:(row:AuthInfo)=>row.created },
  { title:t('authorizations.delete'), key:'action', width:100, render:(row:AuthInfo)=>h(NPopconfirm,{onPositiveClick:()=>delAuth(row)},{trigger:()=>h(NButton,{size:'tiny',text:true,type:'error'},{default:()=>t('authorizations.delete')}),default:()=>t('authorizations.confirmDelete')}) },
]

async function loadData(){ const r=await fetch(`${api}api/authorizations`,{credentials:'include'}); if(r.ok) auths.value=(await r.json()).data||[] }
onMounted(loadData)

async function delAuth(a:AuthInfo){ const r=await fetch(`${api}api/authorizations/${a.id}`,{method:'DELETE',credentials:'include'}); const d=await r.json(); if(d.code===200){ await loadData(); msg.success(t('authorizations.deleteSuccess')) } else msg.error(d.message||t('authorizations.deleteFailed')) }
</script>
