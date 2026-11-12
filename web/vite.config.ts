import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// W trybie deweloperskim zapytania /api idą do serwera ASP.NET Core,
// a zbudowana aplikacja trafia do wwwroot tego serwera.
export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/api': 'http://localhost:5150',
    },
  },
  build: {
    outDir: '../src/SzukajkaStartow.Web/wwwroot',
    emptyOutDir: true,
  },
})
