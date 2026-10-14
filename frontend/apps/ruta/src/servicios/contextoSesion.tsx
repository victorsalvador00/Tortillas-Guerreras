import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { fijarDispositivo } from '@/db/repositorio';
import {
  cerrarSesion,
  dispositivoGuardado,
  iniciarSesion,
  perfilGuardado,
  revisarAprobacion,
  type Perfil,
  type ResultadoInicio,
} from './sesion';

type Estado = 'cargando' | 'sinSesion' | 'espera' | 'listo';

interface ContextoSesion {
  estado: Estado;
  perfil: Perfil | null;
  entrar: (usuario: string, contrasena: string) => Promise<void>;
  revisar: () => Promise<void>;
  salir: () => Promise<void>;
  /** La sesión expiró al sincronizar: se pide iniciar sesión sin borrar las operaciones pendientes. */
  expirar: () => void;
}

const Contexto = createContext<ContextoSesion | null>(null);

export function ProveedorSesion({ children }: { children: ReactNode }) {
  const [estado, setEstado] = useState<Estado>('cargando');
  const [perfil, setPerfil] = useState<Perfil | null>(null);

  const aplicar = useCallback((r: ResultadoInicio) => {
    if (r.tipo === 'listo') {
      if (r.perfil.dispositivo) fijarDispositivo(r.perfil.dispositivo.id, r.perfil.dispositivo.codigo);
      setPerfil(r.perfil);
      setEstado(r.perfil.dispositivo?.activo ? 'listo' : 'espera');
    } else {
      setEstado('espera');
    }
  }, []);

  useEffect(() => {
    void (async () => {
      const guardado = await perfilGuardado();
      const dispositivo = await dispositivoGuardado();
      if (guardado?.dispositivo?.activo) {
        fijarDispositivo(guardado.dispositivo.id, guardado.dispositivo.codigo);
        setPerfil(guardado);
        setEstado('listo');
      } else if (guardado && dispositivo) {
        setPerfil(guardado);
        setEstado('espera');
      } else {
        setEstado('sinSesion');
      }
    })();
  }, []);

  const entrar = useCallback(async (usuario: string, contrasena: string) => aplicar(await iniciarSesion(usuario, contrasena)), [aplicar]);
  const revisar = useCallback(async () => aplicar(await revisarAprobacion()), [aplicar]);
  const salir = useCallback(async () => {
    await cerrarSesion();
    setPerfil(null);
    setEstado('sinSesion');
  }, []);
  const expirar = useCallback(() => setEstado('sinSesion'), []);

  const valor = useMemo(() => ({ estado, perfil, entrar, revisar, salir, expirar }), [estado, perfil, entrar, revisar, salir, expirar]);
  return <Contexto.Provider value={valor}>{children}</Contexto.Provider>;
}

export function useSesion(): ContextoSesion {
  const c = useContext(Contexto);
  if (!c) throw new Error('useSesion requiere ProveedorSesion');
  return c;
}
