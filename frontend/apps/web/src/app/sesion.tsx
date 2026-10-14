import { cerrarSesion, configurarCliente, login, refrescarSesion, type PerfilDto, type SesionDto } from '@tg/api-client';
import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from 'react';

/**
 * Sesión de la web: access token (15 min) solo en memoria y refresh token rotativo (RN-SEG-01) en el navegador para
 * sobrevivir a recargas. El cliente del API refresca solo ante un 401.
 */
const CLAVE_REFRESH = 'tg-refresh';

let tokenAcceso: string | null = null;
let alCambiarPerfil: ((perfil: PerfilDto | null) => void) | null = null;

function leerRefresh(): string | null {
  try {
    return localStorage.getItem(CLAVE_REFRESH);
  } catch {
    return null;
  }
}

function guardarSesion(sesion: SesionDto | null): void {
  tokenAcceso = sesion?.accessToken ?? null;
  try {
    if (sesion) localStorage.setItem(CLAVE_REFRESH, sesion.refreshToken);
    else localStorage.removeItem(CLAVE_REFRESH);
  } catch {
    // Navegación privada: la sesión dura lo que la pestaña.
  }
  alCambiarPerfil?.(sesion?.usuario ?? null);
}

async function refrescar(): Promise<boolean> {
  const refreshToken = leerRefresh();
  if (!refreshToken) return false;
  try {
    guardarSesion(await refrescarSesion({ refreshToken }));
    return true;
  } catch {
    guardarSesion(null);
    return false;
  }
}

configurarCliente({
  urlBase: import.meta.env.VITE_API_URL ?? '',
  obtenerToken: () => tokenAcceso,
  refrescarSesion: refrescar,
  alExpirarSesion: () => guardarSesion(null),
});

/** Hay sesión activa (lo consulta el guardia de rutas sin esperar a que React vuelva a pintar). */
export function sesionActiva(): boolean {
  return tokenAcceso !== null;
}

/** Restaura la sesión al abrir la app (antes de montar el enrutador). */
export async function restaurarSesion(): Promise<PerfilDto | null> {
  if (!leerRefresh()) return null;
  try {
    const sesion = await refrescarSesion({ refreshToken: leerRefresh()! });
    guardarSesion(sesion);
    return sesion.usuario;
  } catch {
    guardarSesion(null);
    return null;
  }
}

export interface ContextoSesion {
  perfil: PerfilDto | null;
  iniciar: (usuario: string, contrasena: string) => Promise<PerfilDto>;
  cerrar: () => Promise<void>;
  tienePermiso: (permiso: string | string[] | undefined) => boolean;
}

const Contexto = createContext<ContextoSesion | null>(null);

export function ProveedorSesion({ inicial, children }: { inicial: PerfilDto | null; children: ReactNode }) {
  const [perfil, setPerfil] = useState<PerfilDto | null>(inicial);
  alCambiarPerfil = setPerfil;

  const iniciar = useCallback(async (usuario: string, contrasena: string) => {
    const sesion = await login({ usuario, contrasena });
    guardarSesion(sesion);
    return sesion.usuario;
  }, []);

  const cerrar = useCallback(async () => {
    const refreshToken = leerRefresh();
    try {
      if (refreshToken) await cerrarSesion({ refreshToken });
    } finally {
      guardarSesion(null);
    }
  }, []);

  const tienePermiso = useCallback(
    (permiso: string | string[] | undefined) => {
      if (!permiso) return true;
      const lista = Array.isArray(permiso) ? permiso : [permiso];
      return lista.some((p) => perfil?.permisos.includes(p));
    },
    [perfil],
  );

  const valor = useMemo(() => ({ perfil, iniciar, cerrar, tienePermiso }), [perfil, iniciar, cerrar, tienePermiso]);
  return <Contexto.Provider value={valor}>{children}</Contexto.Provider>;
}

export function useSesion(): ContextoSesion {
  const c = useContext(Contexto);
  if (!c) throw new Error('useSesion requiere ProveedorSesion');
  return c;
}
