import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';
import { fileURLToPath, URL } from 'node:url';

// En desarrollo /api va al backend local (dotnet run → http://localhost:5080).
export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: { alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) } },
  server: {
    port: 5173,
    proxy: { '/api': process.env.TG_API_URL ?? 'http://localhost:5080' },
  },
  build: { sourcemap: true, chunkSizeWarningLimit: 1500 },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/pruebas/setup.ts'],
    css: false,
  },
});
