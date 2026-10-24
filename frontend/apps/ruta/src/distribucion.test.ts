import { readFileSync } from 'node:fs';
import { fileURLToPath, URL } from 'node:url';
import { describe, expect, it } from 'vitest';

/**
 * RN-APP-10: la configuración de distribución que permite instalar cada APK encima del anterior (mismo paquete, firma
 * de EAS y `versionCode` creciente) y publicar a `production` solo desde un tag.
 */
const leer = (ruta: string) => readFileSync(fileURLToPath(new URL(ruta, import.meta.url)), 'utf8');
const eas = JSON.parse(leer('../eas.json'));
const app = JSON.parse(leer('../app.json')).expo;
const workflow = leer('../../../../.github/workflows/app-update.yml');

describe('distribución de la app', () => {
  it('RN-APP-10: el perfil production es un APK de distribución interna con versionCode automático', () => {
    const prod = eas.build.production;
    expect(prod.android.buildType).toBe('apk');
    expect(prod.distribution).toBe('internal');
    expect(prod.channel).toBe('production');
    expect(prod.autoIncrement).toBe(true);
    expect(eas.cli.appVersionSource).toBe('remote');
  });

  it('RN-APP-10: mismo paquete y OTA atadas a la versión del APK', () => {
    expect(app.android.package).toBe('mx.tortillasguerreras.ruta');
    expect(app.runtimeVersion).toEqual({ policy: 'appVersion' });
    expect(app.updates.checkAutomatically).toBe('ON_LOAD');
  });

  it('RN-APP-10: solo un tag publica al canal production', () => {
    const produccion = workflow.slice(workflow.indexOf('Publicar a production'));
    expect(produccion).toContain("if: github.ref_type == 'tag'");
    expect(produccion).toContain('--channel production');
    const preview = workflow.slice(workflow.indexOf('Publicar a preview'), workflow.indexOf('Publicar a production'));
    expect(preview).toContain("if: github.ref_type == 'branch'");
    expect(preview).not.toContain('production');
  });
});
