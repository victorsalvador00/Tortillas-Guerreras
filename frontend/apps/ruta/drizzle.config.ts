import { defineConfig } from 'drizzle-kit';

// `pnpm db:generar` crea las migraciones de la base local (RN-APP-08) en ./drizzle.
export default defineConfig({
  dialect: 'sqlite',
  driver: 'expo',
  schema: './src/db/esquema.ts',
  out: './drizzle',
});
