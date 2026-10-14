// Metro en el monorepo pnpm: Expo SDK 54 resuelve los paquetes del workspace; se agregan los .sql de Drizzle.
const { getDefaultConfig } = require('expo/metro-config');

const config = getDefaultConfig(__dirname);
config.resolver.sourceExts.push('sql');

module.exports = config;
