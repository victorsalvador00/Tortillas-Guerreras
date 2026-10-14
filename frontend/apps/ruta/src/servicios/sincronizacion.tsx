import NetInfo from '@react-native-community/netinfo';
import { sumarDias, fechaOperativa } from '@tg/shared';
import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { AppState } from 'react-native';
import { parametro } from '@/db/consultas';
import { RepositorioSync } from '@/db/repositorio';
import { MotorSync, type ResumenSync } from '@/sync/motor';
import { VERSION_APP } from './config';
import { nuevoId } from './ids';
import { clienteHttp } from './sesion';

const repositorio = new RepositorioSync();

export const motor = new MotorSync({
  http: clienteHttp,
  almacen: repositorio,
  versionApp: VERSION_APP,
  nuevoId,
  reloj: { ahora: () => new Date(), dormir: (ms) => new Promise((r) => setTimeout(r, ms)) },
});

const CADA_MS = 5 * 60_000;

interface ContextoSync {
  sincronizando: boolean;
  ultimo: ResumenSync | null;
  conRed: boolean;
  /** Se incrementa después de cada sincronización para refrescar las pantallas. */
  version: number;
  sincronizar: () => Promise<ResumenSync>;
  /** Avisa a las pantallas que cambió la base local (nueva venta, retorno…). */
  notificarCambio: () => void;
}

const Contexto = createContext<ContextoSync | null>(null);

/**
 * Disparadores de sincronización (RN-APP-04): al abrir la app, al recuperar la red, cada 5 minutos con red y manual.
 * Las operaciones Aplicadas se limpian tras `ruta.dias_retencion_local` días (RN-APP-05).
 */
export function ProveedorSync({ children, alRequerirLogin }: { children: ReactNode; alRequerirLogin: () => void }) {
  const [sincronizando, setSincronizando] = useState(false);
  const [ultimo, setUltimo] = useState<ResumenSync | null>(null);
  const [conRed, setConRed] = useState(true);
  const [version, setVersion] = useState(0);
  const requerirLogin = useRef(alRequerirLogin);
  requerirLogin.current = alRequerirLogin;

  const sincronizar = useCallback(async () => {
    setSincronizando(true);
    try {
      const r = await motor.sincronizar();
      setUltimo(r);
      if (r.requiereLogin) requerirLogin.current();
      const dias = parametro('ruta.dias_retencion_local', 7);
      await repositorio.limpiarAplicadas(`${sumarDias(fechaOperativa(), -dias)}T00:00:00`);
      return r;
    } finally {
      setSincronizando(false);
      setVersion((v) => v + 1);
    }
  }, []);

  useEffect(() => {
    void sincronizar();
    let anterior = true;
    const quitarRed = NetInfo.addEventListener((estado) => {
      const hay = !!estado.isConnected && estado.isInternetReachable !== false;
      setConRed(hay);
      if (hay && !anterior) void sincronizar();
      anterior = hay;
    });
    const intervalo = setInterval(() => {
      void NetInfo.fetch().then((e) => {
        if (e.isConnected) void sincronizar();
      });
    }, CADA_MS);
    const quitarApp = AppState.addEventListener('change', (estado) => {
      if (estado === 'active') void sincronizar();
    });
    return () => {
      quitarRed();
      clearInterval(intervalo);
      quitarApp.remove();
    };
  }, [sincronizar]);

  const notificarCambio = useCallback(() => setVersion((v) => v + 1), []);
  const valor = useMemo(
    () => ({ sincronizando, ultimo, conRed, version, sincronizar, notificarCambio }),
    [sincronizando, ultimo, conRed, version, sincronizar, notificarCambio],
  );
  return <Contexto.Provider value={valor}>{children}</Contexto.Provider>;
}

export function useSync(): ContextoSync {
  const c = useContext(Contexto);
  if (!c) throw new Error('useSync requiere ProveedorSync');
  return c;
}
