// ============================================================
// 外部登录提供商插件注册中心
// 外部脚本通过 window.OpenIddictUI.registerExternalProvider({...}) 注册
// 注册后 ExternalButtons 组件自动渲染对应按钮
// ============================================================

import { ref } from 'vue'

export interface ExternalProvider {
  id: string
  name: string
  icon: string
  color: string
  handler: () => void
}

const providers = ref<ExternalProvider[]>([])

export function useProviders() {
  function register(config: ExternalProvider) {
    const idx = providers.value.findIndex((p) => p.id === config.id)
    if (idx >= 0) providers.value[idx] = config
    else providers.value.push(config)
  }

  function getAll() { return providers.value }

  return { providers, register, getAll }
}

const _providers = useProviders();
(window as unknown as Record<string, unknown>).OpenIddictUI = { registerExternalProvider: _providers.register }
