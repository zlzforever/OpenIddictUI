import { createApp } from 'vue'
import { createRouter, createWebHistory } from 'vue-router'
import App from './App.vue'
import HomePage from './views/HomePage.vue'
import CallbackPage from './views/CallbackPage.vue'
import SilentCallbackPage from './views/SilentCallbackPage.vue'

const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/', component: HomePage },
    { path: '/signin-redirect-callback', component: CallbackPage },
    { path: '/signin-silent-callback', component: SilentCallbackPage },
  ],
})

createApp(App).use(router).mount('#app')
