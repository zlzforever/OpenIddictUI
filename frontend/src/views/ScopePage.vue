<!-- Scope 管理页：antd Table + Modal -->
<template>
  <div class="admin-page">
    <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:16px">
      <h2>Scopes</h2>
      <a-button type="primary" @click="openAdd">添加</a-button>
    </div>
    <a-table :columns="columns" :data-source="scopes" row-key="id" size="small" :pagination="false" bordered>
      <template #bodyCell="{ column, record }">
        <template v-if="column.key === 'action'">
          <a-space>
            <a-button type="link" size="small" @click="openEdit(record)">编辑</a-button>
            <a-popconfirm title="确认删除？" @confirm="delScope(record)">
              <a-button type="link" size="small" danger>删除</a-button>
            </a-popconfirm>
          </a-space>
        </template>
      </template>
    </a-table>

    <a-modal :open="showModal" :title="editing ? '编辑' : '添加'" @ok="handleSave" @cancel="showModal=false" width="480px">
      <a-form layout="vertical">
        <a-form-item label="Name"><a-input v-model:value="form.name" :disabled="editing"/></a-form-item>
        <a-form-item label="DisplayName"><a-input v-model:value="form.displayName"/></a-form-item>
        <a-form-item label="Description"><a-input v-model:value="form.description"/></a-form-item>
      </a-form>
    </a-modal>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { message } from 'ant-design-vue'

const api = document.querySelector('base')?.getAttribute('href') || '/'

interface ScopeInfo { id:string; name:string; displayName?:string; description?:string }
const scopes = ref<ScopeInfo[]>([])
const showModal = ref(false); const editing = ref(false); const editId = ref('')
const form = ref({ name:'',displayName:'',description:'' })

const columns = [
  { title:'Name',dataIndex:'name',key:'name' },
  { title:'DisplayName',dataIndex:'displayName',key:'displayName' },
  { title:'Description',dataIndex:'description',key:'description',ellipsis:true },
  { title:'操作',key:'action',width:150 },
]

async function loadData(){ const r=await fetch(`${api}api/scopes`,{credentials:'include'}); if(r.ok) scopes.value=(await r.json()).data||[] }
onMounted(loadData)
const openAdd = () => { editing.value=false; editId.value=''; form.value={name:'',displayName:'',description:''}; showModal.value=true }
const openEdit = (s:ScopeInfo) => { editing.value=true; editId.value=s.id; form.value={name:s.name,displayName:s.displayName||'',description:s.description||''}; showModal.value=true }

async function handleSave(){
  const body={ name:form.value.name,displayName:form.value.displayName||null,description:form.value.description||null }
  const url=editing.value?`${api}api/scopes/${editId.value}`:`${api}api/scopes`
  const r=await fetch(url,{method:editing.value?'PUT':'POST',credentials:'include',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)})
  const d=await r.json()
  if(d.code===200){ showModal.value=false; await loadData(); message.success('保存成功') } else message.error(d.message||'操作失败')
}

async function delScope(s:ScopeInfo){ const r=await fetch(`${api}api/scopes/${s.id}`,{method:'DELETE',credentials:'include'}); const d=await r.json(); if(d.code===200){ await loadData(); message.success('删除成功') } else message.error(d.message||'删除失败') }
</script>

<style scoped></style>
