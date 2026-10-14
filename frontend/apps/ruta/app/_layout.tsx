import { useMigrations } from 'drizzle-orm/expo-sqlite/migrator';
import { Stack } from 'expo-router';
import * as SplashScreen from 'expo-splash-screen';
import { StatusBar } from 'expo-status-bar';
import * as Updates from 'expo-updates';
import { useEffect, useState } from 'react';
import { ActivityIndicator, Text, View } from 'react-native';
import { SafeAreaProvider } from 'react-native-safe-area-context';
import migraciones from '../drizzle/migrations';
import { db } from '@/db/cliente';
import { ProveedorSesion } from '@/servicios/contextoSesion';
import { useColores } from '@/ui/tema';

void SplashScreen.preventAutoHideAsync();

/**
 * RN-APP-08: las actualizaciones OTA se aplican solo al abrir la app, antes de que el vendedor empiece a capturar;
 * nunca a mitad de una venta.
 */
async function aplicarActualizacionAlAbrir(): Promise<void> {
  if (__DEV__ || !Updates.isEnabled) return;
  try {
    const revision = await Promise.race([
      Updates.checkForUpdateAsync(),
      new Promise<null>((r) => setTimeout(() => r(null), 4000)),
    ]);
    if (revision?.isAvailable) {
      await Updates.fetchUpdateAsync();
      await Updates.reloadAsync();
    }
  } catch {
    // Sin red: se usa la versión instalada.
  }
}

export default function Raiz() {
  const c = useColores();
  const { success, error } = useMigrations(db, migraciones);
  const [actualizado, setActualizado] = useState(false);

  useEffect(() => {
    void aplicarActualizacionAlAbrir().finally(() => setActualizado(true));
  }, []);

  useEffect(() => {
    if ((success && actualizado) || error) void SplashScreen.hideAsync();
  }, [success, actualizado, error]);

  if (error) {
    return (
      <View style={{ flex: 1, alignItems: 'center', justifyContent: 'center', padding: 24, backgroundColor: c.fondo }}>
        <Text style={{ color: c.error, textAlign: 'center' }}>No se pudo preparar la base local: {error.message}</Text>
      </View>
    );
  }
  if (!success || !actualizado) {
    return (
      <View style={{ flex: 1, alignItems: 'center', justifyContent: 'center', backgroundColor: c.fondo }}>
        <ActivityIndicator color={c.primario} />
      </View>
    );
  }

  return (
    <SafeAreaProvider>
      <ProveedorSesion>
        <StatusBar style="light" />
        <Stack
          screenOptions={{
            headerStyle: { backgroundColor: c.barra },
            headerTintColor: c.barraTexto,
            headerTitleStyle: { fontWeight: '600' },
            contentStyle: { backgroundColor: c.fondo },
          }}
        >
          <Stack.Screen name="login" options={{ headerShown: false }} />
          <Stack.Screen name="espera" options={{ headerShown: false }} />
          <Stack.Screen name="(app)" options={{ headerShown: false }} />
        </Stack>
      </ProveedorSesion>
    </SafeAreaProvider>
  );
}
