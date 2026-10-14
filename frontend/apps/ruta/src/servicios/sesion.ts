import * as SecureStore from 'expo-secure-store';
import * as Device from 'expo-device';
import { Platform } from 'react-native';
import { ErrorHttp, type ClienteHttp } from '@/sync/puertos';
import { API_URL, VERSION_APP } from './config';

/**
 * Sesión de la app. Tokens, credenciales y dispositivo se guardan en `expo-secure-store`, nunca en SQLite (RN-APP-07).
 * El refresh token está ligado al dispositivo (RN-SEG-02).
 */

const CLAVES = {
  acceso: 'tg.acceso',
  refresh: 'tg.refresh',
  perfil: 'tg.perfil',
  dispositivo: 'tg.dispositivo',
  usuario: 'tg.usuario',
  contrasena: 'tg.contrasena',
} as const;

export interface Perfil {
  id: string;
  usuario: string;
  nombreCompleto: string;
  roles: string[];
  permisos: string[];
  dispositivo: { id: string; codigo: string | null; nombre: string; activo: boolean } | null;
}

interface SesionApi {
  accessToken: string;
  refreshToken: string;
  usuario: Perfil;
}

interface DispositivoApi {
  id: string;
  codigo: string | null;
  estado: 'PorAprobar' | 'Activo' | 'Inactivo';
  activo: boolean;
}

let tokenAcceso: string | null = null;

async function leer(clave: string): Promise<string | null> {
  return SecureStore.getItemAsync(clave);
}

async function escribir(clave: string, valor: string | null): Promise<void> {
  if (valor === null) await SecureStore.deleteItemAsync(clave);
  else await SecureStore.setItemAsync(clave, valor);
}

async function solicitud<T>(ruta: string, metodo: 'GET' | 'POST', cuerpo?: unknown, token?: string | null): Promise<T> {
  let respuesta: Response;
  try {
    respuesta = await fetch(`${API_URL}${ruta}`, {
      method: metodo,
      headers: {
        Accept: 'application/json',
        ...(cuerpo !== undefined ? { 'Content-Type': 'application/json' } : {}),
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
      },
      body: cuerpo !== undefined ? JSON.stringify(cuerpo) : undefined,
    });
  } catch {
    throw new ErrorHttp(0, 'No hay conexión con el servidor.');
  }
  if (!respuesta.ok) {
    let problema: { title?: string; codigo?: string } = {};
    try {
      problema = (await respuesta.json()) as typeof problema;
    } catch {
      // Sin cuerpo.
    }
    throw new ErrorHttp(respuesta.status, problema.title ?? `Error ${respuesta.status}`, problema.codigo);
  }
  if (respuesta.status === 204) return undefined as T;
  return (await respuesta.json()) as T;
}

async function guardarSesion(s: SesionApi): Promise<void> {
  tokenAcceso = s.accessToken;
  await escribir(CLAVES.acceso, s.accessToken);
  await escribir(CLAVES.refresh, s.refreshToken);
  await escribir(CLAVES.perfil, JSON.stringify(s.usuario));
}

export async function perfilGuardado(): Promise<Perfil | null> {
  const texto = await leer(CLAVES.perfil);
  tokenAcceso ??= await leer(CLAVES.acceso);
  return texto ? (JSON.parse(texto) as Perfil) : null;
}

export async function dispositivoGuardado(): Promise<string | null> {
  return leer(CLAVES.dispositivo);
}

export type ResultadoInicio =
  | { tipo: 'listo'; perfil: Perfil }
  | { tipo: 'esperaAprobacion'; dispositivoId: string };

/**
 * Inicio de sesión. La primera vez registra el teléfono (queda por aprobar, RN-SEG-03); después la sesión queda ligada
 * al dispositivo aprobado.
 */
export async function iniciarSesion(usuario: string, contrasena: string): Promise<ResultadoInicio> {
  await escribir(CLAVES.usuario, usuario);
  await escribir(CLAVES.contrasena, contrasena);
  const dispositivoId = await leer(CLAVES.dispositivo);
  if (dispositivoId) {
    try {
      const sesion = await solicitud<SesionApi>('/api/v1/seguridad/auth/login', 'POST', { usuario, contrasena, dispositivoId });
      await guardarSesion(sesion);
      return { tipo: 'listo', perfil: sesion.usuario };
    } catch (e) {
      // SEG-005: dispositivo aún no aprobado o desactivado.
      if (!(e instanceof ErrorHttp && e.codigo === 'SEG-005')) throw e;
    }
  }
  const sinDispositivo = await solicitud<SesionApi>('/api/v1/seguridad/auth/login', 'POST', { usuario, contrasena });
  await guardarSesion(sinDispositivo);
  if (!dispositivoId) {
    const nombre = `${Device.modelName ?? 'Teléfono'} de ${sinDispositivo.usuario.nombreCompleto}`.slice(0, 100);
    const registrado = await solicitud<DispositivoApi>(
      '/api/v1/seguridad/dispositivos/solicitar',
      'POST',
      { nombre, plataforma: Platform.OS === 'ios' ? 'ios' : 'android', versionApp: VERSION_APP },
      sinDispositivo.accessToken,
    );
    await escribir(CLAVES.dispositivo, registrado.id);
    return { tipo: 'esperaAprobacion', dispositivoId: registrado.id };
  }
  return { tipo: 'esperaAprobacion', dispositivoId };
}

/** Revisa si el administrador ya aprobó el teléfono; si sí, inicia sesión ligada a él. */
export async function revisarAprobacion(): Promise<ResultadoInicio> {
  const dispositivoId = await leer(CLAVES.dispositivo);
  if (!dispositivoId) throw new Error('El teléfono no está registrado.');
  const d = await solicitud<DispositivoApi>(`/api/v1/seguridad/dispositivos/${dispositivoId}`, 'GET', undefined, tokenAcceso);
  if (!d.activo) return { tipo: 'esperaAprobacion', dispositivoId };
  const usuario = await leer(CLAVES.usuario);
  const contrasena = await leer(CLAVES.contrasena);
  if (!usuario || !contrasena) throw new ErrorHttp(401, 'Inicia sesión de nuevo.');
  return iniciarSesion(usuario, contrasena);
}

export async function refrescar(): Promise<boolean> {
  const refreshToken = await leer(CLAVES.refresh);
  if (!refreshToken) return false;
  try {
    await guardarSesion(await solicitud<SesionApi>('/api/v1/seguridad/auth/refresh', 'POST', { refreshToken }));
    return true;
  } catch (e) {
    if (e instanceof ErrorHttp && e.estado === 0) throw e;
    return false;
  }
}

export async function cerrarSesion(): Promise<void> {
  const refreshToken = await leer(CLAVES.refresh);
  try {
    if (refreshToken) await solicitud('/api/v1/seguridad/auth/logout', 'POST', { refreshToken }, tokenAcceso);
  } catch {
    // Sin red: se borra la sesión local de todos modos.
  }
  tokenAcceso = null;
  await escribir(CLAVES.acceso, null);
  await escribir(CLAVES.refresh, null);
  await escribir(CLAVES.perfil, null);
  await escribir(CLAVES.contrasena, null);
}

/** Cliente HTTP del motor de sync: token Bearer y un intento de refresh ante 401. */
export const clienteHttp: ClienteHttp = {
  async get<T>(ruta: string) {
    return conRefresh(() => solicitud<T>(ruta, 'GET', undefined, tokenAcceso));
  },
  async post<T>(ruta: string, cuerpo: unknown) {
    return conRefresh(() => solicitud<T>(ruta, 'POST', cuerpo, tokenAcceso));
  },
};

async function conRefresh<T>(fn: () => Promise<T>): Promise<T> {
  tokenAcceso ??= await leer(CLAVES.acceso);
  try {
    return await fn();
  } catch (e) {
    if (e instanceof ErrorHttp && e.estado === 401 && (await refrescar())) {
      return fn();
    }
    throw e;
  }
}
