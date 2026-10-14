// Metro en el monorepo pnpm: Expo SDK 54 resuelve los paquetes del workspace; se agregan los .sql de Drizzle.
const path = require('path');
const { getDefaultConfig } = require('expo/metro-config');

const config = getDefaultConfig(__dirname);
config.resolver.sourceExts.push('sql');

// Una sola copia de React: paquetes que no la declaran como dependencia (p. ej. drizzle-orm/expo-sqlite) tomarían la
// copia de la web (otra versión) que pnpm deja en la raíz, y fallarían con "Invalid hook call". Se resuelven desde la app.
const desdeLaApp = path.join(__dirname, 'package.json');
config.resolver.resolveRequest = (context, moduleName, platform) =>
  moduleName === 'react' || moduleName.startsWith('react/')
    ? context.resolveRequest({ ...context, originModulePath: desdeLaApp }, moduleName, platform)
    : context.resolveRequest(context, moduleName, platform);

module.exports = config;
