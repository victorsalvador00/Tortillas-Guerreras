import { drizzle } from 'drizzle-orm/expo-sqlite';
import { openDatabaseSync } from 'expo-sqlite';
import * as esquema from './esquema';

/** Base local SQLite de la app; las migraciones se aplican al abrir (ver `app/_layout.tsx`). */
export const sqlite = openDatabaseSync('tg-ruta.db', { enableChangeListener: true });
sqlite.execSync('PRAGMA journal_mode = WAL; PRAGMA foreign_keys = ON;');

export const db = drizzle(sqlite, { schema: esquema });

export type BaseLocal = typeof db;
export type TransaccionLocal = Parameters<Parameters<BaseLocal['transaction']>[0]>[0];
/** Base o transacción: las funciones de escritura funcionan con ambas. */
export type Ejecutor = BaseLocal | TransaccionLocal;
