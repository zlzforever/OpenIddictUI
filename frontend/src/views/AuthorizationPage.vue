<!-- Authorizations 管理页：当前用户的授权记录列表 + 删除 -->
<template>
  <div class="admin-page">
    <h2 style="margin-bottom:16px">Authorizations</h2>
    <a-table :columns="columns" :data-source="auths" row-key="id" size="small" :pagination="false" bordered>
      <template #bodyCell="{ column, record }">
        <template v-if="column.key === 'scopes'">
          <a-tag v-for="s in record.scopes" :key="s" style="margin:1px">{{ s }}</a-tag>
        </template>
        <template v-if="column.key === 'type'">
          <a-tag :color="record.type === 'Permanent' ? 'blue' : 'orange'">{{ record.type }}</a-tag>
        </template>
        <template v-if="column.key === 'action'">
          <a-popconfirm title="确认撤销此授权？" @confirm="delAuth(record)">
            <a-button type="link" size="small" danger>撤销</a-button>
          </a-popconfirm>
        </template>
      </template>
    </a-table>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { message } from 'ant-design-vue'

const api = document.querySelector('base')?.getAttribute('href') || '/'

interface AuthInfo { id:string; clientId:string; scopes:string[]; type:string; status:string; created:string }
const auths = ref<AuthInfo[]>([])

const columns = [
  { title:'Client', dataIndex:'clientId', key:'clientId', width:180 },
  { title:'Scopes', key:'scopes', width:250 },
  { title:'Type', key:'type', width:100 },
  { title:'Status', dataIndex:'status', key:'status', width:80 },
  { title:'Created', dataIndex:'created', key:'created', width:170 },
  { title:'操作', key:'action', width:100 },
]

async function loadData(){ const r=await fetch(`${api}api/authorizations`,{credentials:'include'}); if(r.ok) auths.value=(await r.json()).data||[] }
onMounted(loadData)

async function delAuth(a:AuthInfo){ const r=await fetch(`${api}api/authorizations/${a.id}`,{method:'DELETE',credentials:'include'}); const d=await r.json(); if(d.code===200){ await loadData(); message.success('撤销成功') } else message.error(d.message||'撤销失败') }
</script>
