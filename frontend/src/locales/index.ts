import { createI18n } from 'vue-i18n'
import enUS from './en-US'
import zhCN from './zh-CN'

const LOCALE_KEY = 'openiddict_locale'

export function getSupportedLangs(): string[] {
  if (typeof document !== 'undefined') {
    const meta = document.querySelector('meta[name="login-langs"]')
    const content = meta?.getAttribute('content') || ''
    return content ? content.split(',').map(s => s.trim()).filter(Boolean) : ['zh-CN']
  }
  return ['zh-CN']
}

function detectLocale(): string {
  const supported = getSupportedLangs()
  const saved = localStorage.getItem(LOCALE_KEY)
  if (saved && supported.includes(saved)) return saved
  const lang = navigator.language?.toLowerCase() || ''
  if (lang.startsWith('zh') && supported.includes('zh-CN')) return 'zh-CN'
  if (supported.includes('en-US')) return 'en-US'
  return supported[0] || 'zh-CN'
}

export const i18n = createI18n({
  legacy: false,
  locale: detectLocale(),
  fallbackLocale: 'en-US',
  messages: { 'en-US': enUS, 'zh-CN': zhCN },
})

export function setLocale(locale: string) {
  const supported = getSupportedLangs()
  if (!supported.includes(locale)) return
  i18n.global.locale.value = locale as 'en-US' | 'zh-CN'
  localStorage.setItem(LOCALE_KEY, locale)
}
