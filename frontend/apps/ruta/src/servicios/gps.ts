import type { GpsSync } from '@tg/shared';
import * as Location from 'expo-location';

/**
 * RN-APP-03: el GPS se pide al confirmar con espera máxima de 10 s; si no hay lectura, la operación se guarda sin GPS
 * (el servidor nunca rechaza por GPS, RN-VEN-05).
 */
export async function leerGps(esperaMs = 10_000): Promise<GpsSync | null> {
  try {
    const permiso = await Location.requestForegroundPermissionsAsync();
    if (permiso.status !== 'granted') return null;
    const lectura = await Promise.race([
      Location.getCurrentPositionAsync({ accuracy: Location.Accuracy.High }),
      new Promise<null>((resolver) => setTimeout(() => resolver(null), esperaMs)),
    ]);
    const posicion = lectura ?? (await Location.getLastKnownPositionAsync({ maxAge: 120_000 }));
    if (!posicion) return null;
    return {
      lat: Math.round(posicion.coords.latitude * 1e6) / 1e6,
      lng: Math.round(posicion.coords.longitude * 1e6) / 1e6,
      precisionM: posicion.coords.accuracy != null ? Math.round(posicion.coords.accuracy * 100) / 100 : null,
      tomadoEn: new Date(posicion.timestamp).toISOString(),
    };
  } catch {
    return null;
  }
}
