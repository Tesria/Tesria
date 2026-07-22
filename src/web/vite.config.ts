import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    // In development the SPA is served by Vite and the API runs separately.
    // Proxy /api to the ASP.NET Core backend so the frontend can use
    // same-origin relative URLs (which also work in production).
    proxy: {
      '/api': {
        target: 'http://localhost:5291',
        changeOrigin: true,
      },
    },
  },
  build: {
    // Output goes into the API's wwwroot at Docker build time (see Dockerfile).
    outDir: 'dist',
  },
})
