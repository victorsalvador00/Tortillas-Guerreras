import { defineConfig } from 'vitest/config';
import { fileURLToPath, URL } from 'node:url';

// Solo se prueban módulos TypeScript puros (dominio y motor de sync); no importan React Native.
export default defineConfig({
  resolve: { alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) } },
  test: { environment: 'node', globals: true, include: ['src/**/*.test.ts'] },
});
