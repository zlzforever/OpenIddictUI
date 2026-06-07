<!--
  ============================================================
  滑动验证码（图片拼图式）
  父组件调用 ref.start() 弹出窗口
  服务端返回带圆形缺口的底图 → 拖滑块对齐 → 通过后 emit('verified')
  ============================================================
-->
<template>
  <Teleport to="body">
    <div v-if="visible" class="slider-overlay" @click.self="cancel">
      <div class="slider-popup">
        <div class="slider-popup-header">
          <span>安全验证</span>
          <button class="slider-close" @click="cancel">&times;</button>
        </div>
        <div class="slider-popup-body">
          <p class="slider-hint">请将滑块拖到图中圆形缺口处</p>
          <div class="slider-track" ref="trackRef" :style="{ backgroundImage: bgImage ? `url(${bgImage})` : undefined }">
            <div class="slider-fill" :style="{ width: fillPercent + '%' }"></div>
            <div class="slider-handle" :class="{ dragging, error: showError }" :style="{ left: fillPercent + '%' }" @mousedown.prevent="startDrag" @touchstart.prevent="startDrag">&#10141;</div>
          </div>
          <p v-if="errorMsg" class="slider-error">{{ errorMsg }}</p>
          <p v-if="loading" class="slider-loading">加载中...</p>
        </div>
      </div>
    </div>
  </Teleport>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { sliderInit, sliderVerify } from '../services/api'

const IMG_W = 340
const emit = defineEmits<{ verified: [] }>()

const visible = ref(false)
const loading = ref(false)
const dragging = ref(false)
const showError = ref(false)
const fillPercent = ref(0)
const bgImage = ref('')
const errorMsg = ref('')
const trackRef = ref<HTMLElement | null>(null)

async function start() {
  showError.value = false
  errorMsg.value = ''
  fillPercent.value = 0
  // 释放旧 blob URL
  if (bgImage.value) URL.revokeObjectURL(bgImage.value)
  bgImage.value = ''
  visible.value = true
  try {
    loading.value = true
    const res = await sliderInit()
    // if (!res.ok) throw new Error()

    const blob = await res.blob()
    bgImage.value = URL.createObjectURL(blob)
  } catch {
    errorMsg.value = '初始化失败，请重试'
  } finally {
    loading.value = false
  }
}

function cancel() {
  visible.value = false
}

function startDrag(e: MouseEvent | TouchEvent) {
  dragging.value = true
  showError.value = false
  errorMsg.value = ''
  const track = trackRef.value
  if (!track) return
  const rect = track.getBoundingClientRect()
  const clientX = 'touches' in e ? e.touches[0].clientX : e.clientX
  let pct = ((clientX - rect.left) / rect.width) * 100
  pct = Math.max(0, Math.min(100, pct))
  fillPercent.value = pct

  const onMove = (ev: MouseEvent | TouchEvent) => {
    const cx = 'touches' in ev ? ev.touches[0].clientX : ev.clientX
    let p = ((cx - rect.left) / rect.width) * 100
    p = Math.max(0, Math.min(100, p))
    fillPercent.value = p
  }
  const onUp = () => {
    dragging.value = false
    document.removeEventListener('mousemove', onMove)
    document.removeEventListener('mouseup', onUp)
    document.removeEventListener('touchmove', onMove)
    document.removeEventListener('touchend', onUp)
    verify()
  }
  document.addEventListener('mousemove', onMove)
  document.addEventListener('mouseup', onUp)
  document.addEventListener('touchmove', onMove)
  document.addEventListener('touchend', onUp)
}

async function verify() {
  loading.value = true
  try {
    const track = trackRef.value
    if (!track) return
    const pxPos = Math.round((fillPercent.value / 100) * IMG_W)
    const data = await sliderVerify(pxPos) as { success: boolean }
    if (data.success) {
      visible.value = false
      emit('verified')
    } else {
      showError.value = true
      errorMsg.value = '验证失败，请重试'
      await start()
    }
  } catch {
    showError.value = true
    errorMsg.value = '验证失败，请重试'
  } finally {
    loading.value = false
  }
}

defineExpose({ start })
</script>

<style scoped>
.slider-overlay {
  position: fixed;
  inset: 0;
  z-index: 2000;
  background: rgba(0, 0, 0, 0.4);
  display: flex;
  align-items: center;
  justify-content: center;
}
.slider-popup {
  background: var(--surface);
  border-radius: 12px;
  box-shadow: 0 20px 60px rgba(0, 0, 0, 0.25);
  width: 390px;
  overflow: hidden;
}
.slider-popup-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 1rem 1.25rem;
  font-weight: 600;
  font-size: 0.9375rem;
  border-bottom: 1px solid var(--border);
}
.slider-close {
  background: none;
  border: none;
  font-size: 1.25rem;
  cursor: pointer;
  color: var(--text-muted);
  line-height: 1;
}
.slider-popup-body {
  padding: 1.5rem 1.25rem;
}
.slider-hint {
  font-size: 0.8125rem;
  color: var(--text-muted);
  margin-bottom: 1rem;
  text-align: center;
}

.slider-track {
  position: relative;
  height: 42px;
  background-size: 100% 100%;
  background-position: center;
  border: 1px solid var(--border);
  border-radius: 21px;
  overflow: hidden;
  touch-action: none;
}
.slider-fill {
  position: absolute;
  top: 0;
  left: 0;
  height: 100%;
  background: var(--primary);
  opacity: 0.15;
  border-radius: 21px;
  pointer-events: none;
}
.slider-handle {
  position: absolute;
  top: 3px;
  left: 0;
  z-index: 2;
  width: 36px;
  height: 36px;
  background: #fff;
  border: 1px solid var(--border);
  border-radius: 50%;
  cursor: grab;
  box-shadow: 0 2px 6px rgba(0, 0, 0, 0.15);
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 1rem;
  transition: box-shadow 0.2s;
  transform: translateX(-50%);
}
.slider-handle.dragging {
  cursor: grabbing;
  box-shadow: 0 2px 12px rgba(0, 0, 0, 0.25);
  border-color: var(--primary);
}
.slider-handle.error {
  border-color: var(--error);
  background: #fef2f2;
}

.slider-error {
  color: var(--error);
  font-size: 0.8125rem;
  text-align: center;
  margin-top: 0.75rem;
}
.slider-loading {
  color: var(--text-muted);
  font-size: 0.8125rem;
  text-align: center;
  margin-top: 0.75rem;
}
</style>
