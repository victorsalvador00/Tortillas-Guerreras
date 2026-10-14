import { useFocusEffect } from 'expo-router';
import { useCallback, useState } from 'react';
import { useSync } from '@/servicios/sincronizacion';

/**
 * Lee de la base local y vuelve a leer al enfocar la pantalla o tras cada sincronización/cambio local. Las consultas
 * de Drizzle con expo-sqlite son síncronas y rápidas.
 */
export function useDatos<T>(leer: () => T): T {
  const { version } = useSync();
  const [datos, setDatos] = useState<T>(leer);
  useFocusEffect(
    useCallback(() => {
      setDatos(leer());
      // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [version]),
  );
  return datos;
}
