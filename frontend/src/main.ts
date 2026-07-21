import { createApp, watch } from 'vue'
import { createI18n, useI18n } from 'vue-i18n'
import { zhCN, dateZhCN, enUS, dateEnUS } from 'naive-ui'
import App from './App.vue'
import router from './router'
import naive from 'naive-ui'
import './composables/useProviders'
import './style.css'
import { i18n, setLocale } from './locales'

const app = createApp(App)

app.use(router)
app.use(naive)
app.use(i18n)

// 响应式同步 NaiveUI locale
const naiveLocaleMap: Record<string, { locale: any; dateLocale: any }> = {
  'zh-CN': { locale: zhCN, dateLocale: dateZhCN },
  'en-US': { locale: enUS, dateLocale: dateEnUS },
}
app.provide('naiveLocale', naiveLocaleMap)

app.mount('#app')
