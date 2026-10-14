// Genera src/generado/index.ts con la reexportación de cada archivo que produjo Orval.
import { readdirSync, statSync, writeFileSync, existsSync } from 'node:fs';
import { join } from 'node:path';

const raiz = new URL('../src/generado/', import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1');
const lineas = ['// Archivo generado por scripts/indice.mjs; no editar.'];
for (const entrada of readdirSync(raiz).sort()) {
  const ruta = join(raiz, entrada);
  if (statSync(ruta).isDirectory()) {
    if (entrada === 'modelos') {
      lineas.push("export * from './modelos';");
    } else if (existsSync(join(ruta, `${entrada}.ts`))) {
      lineas.push(`export * from './${entrada}/${entrada}';`);
    }
  }
}
writeFileSync(join(raiz, 'index.ts'), lineas.join('\n') + '\n');
console.log(`index.ts con ${lineas.length - 1} módulos`);
