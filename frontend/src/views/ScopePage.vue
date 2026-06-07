<!-- Scope 管理页：NaiveUI -->
<template>
  <div class="admin-page">
    <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:16px">
      <h2>Scopes</h2>
      <n-button type="primary" @click="openAdd">添加</n-button>
    </div>
    <n-data-table :columns="columns" :data="scopes" :bordered="true" size="small" :pagination="false" />

    <n-modal :show="showModal" :title="editing ? '编辑 Scope' : '添加 Scope'" @update:show="showModal=$event"
      preset="card" style="width:500px" :mask-closable="false" @positive-click="handleSave" positive-text="保存">
      <n-form label-placement="top" size="small">
        <n-form-item label="Name"><n-input v-model:value="form.name" :disabled="editing" /></n-form-item>
        <n-form-item label="DisplayName"><n-input v-model:value="form.displayName" /></n-form-item>
        <n-form-item label="Description"><n-input v-model:value="form.description" /></n-form-item>
      </n-form>
    </n-modal>
  </div>
</template>

<script setup lang="ts">
import { h, ref, onMounted } from 'vue'
import { NTag, NButton, useMessage, NPopconfirm } from 'naive-ui'

const api = document.querySelector('base')?.getAttribute('href') || '/'
const msg = useMessage()

interface ScopeInfo { id:string; name:string; displayName?:string; description?:string; system?:boolean }
const scopes=ref<ScopeInfo[]>([]); const showModal=ref(false); const editing=ref(false); const editId=ref('')
const form=ref({ name:'',displayName:'',description:'' })

const columns = [
  { title:'Name', key:'name', render:(row:ScopeInfo)=>row.name },
  { title:'DisplayName', key:'displayName', render:(row:ScopeInfo)=>row.displayName },
  { title:'Description', key:'description', ellipsis:{tooltip:true}, render:(row:ScopeInfo)=>row.description },
  { title:'操作', key:'action', width:150, render:(row:ScopeInfo)=>h('div',{style:'display:flex;gap:6px'},[
    h(NButton,{size:'tiny',text:true,type:'primary',onClick:()=>openEdit(row)},{default:()=>'编辑'}),
    row.system ? h(NButton,{size:'tiny',text:true,disabled:true},{default:()=>'默认'}) :
    h(NPopconfirm,{onPositiveClick:()=>delScope(row)},{trigger:()=>h(NButton,{size:'tiny',text:true,type:'error'},{default:()=>'删除'}),default:()=>'确认删除？'})
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
  if(d.code===200){ showModal.value=false; await loadData(); msg.success('保存成功') } else msg.error(d.message||'操作失败')
}

async function delScope(s:ScopeInfo){ const r=await fetch(`${api}api/scopes/${s.id}`,{method:'DELETE',credentials:'include'}); const d=await r.json(); if(d.code===200){ await loadData(); msg.success('删除成功') } else msg.error(d.message||'删除失败') }
</script>
