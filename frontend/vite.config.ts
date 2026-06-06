import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

// https://vite.dev/config/
export default defineConfig({
  plugins: [vue()],
  base: '/',
  build: {
    outDir: '../src/OpenIddictUI/wwwroot',
    emptyOutDir: true,
  },
})
