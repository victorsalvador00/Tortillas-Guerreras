import { createContext, createElement, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';

export type ModoTema = 'system' | 'light' | 'dark';

export const CLAVE_TEMA = 'tg-tema';

interface ContextoTema {
  modo: ModoTema;
  oscuro: boolean;
  fijar: (modo: ModoTema) => void;
}

const Contexto = createContext<ContextoTema | null>(null);

function leerGuardado(): ModoTema {
  try {
    const v = localStorage.getItem(CLAVE_TEMA);
    return v === 'light' || v === 'dark' ? v : 'system';
  } catch {
    return 'system';
  }
}

function sistemaOscuro(): boolean {
  return typeof window !== 'undefined' && (window.matchMedia?.('(prefers-color-scheme: dark)').matches ?? false);
}

/**
 * Tema claro / oscuro / según el sistema. Fija `data-theme` en `<html>` (siempre resuelto a claro u oscuro) y guarda
 * la preferencia en el navegador.
 */
export function ProveedorTema({ children }: { children: ReactNode }) {
  const [modo, setModo] = useState<ModoTema>(leerGuardado);
  const [sistema, setSistema] = useState(sistemaOscuro);

  useEffect(() => {
    const consulta = window.matchMedia?.('(prefers-color-scheme: dark)');
    if (!consulta) return;
    const alCambiar = (e: MediaQueryListEvent) => setSistema(e.matches);
    consulta.addEventListener('change', alCambiar);
    return () => consulta.removeEventListener('change', alCambiar);
  }, []);

  const oscuro = modo === 'dark' || (modo === 'system' && sistema);

  useEffect(() => {
    document.documentElement.dataset.theme = oscuro ? 'dark' : 'light';
  }, [oscuro]);

  const fijar = useCallback((nuevo: ModoTema) => {
    setModo(nuevo);
    try {
      if (nuevo === 'system') localStorage.removeItem(CLAVE_TEMA);
      else localStorage.setItem(CLAVE_TEMA, nuevo);
    } catch {
      // Navegación privada: la preferencia dura la sesión.
    }
  }, []);

  const valor = useMemo(() => ({ modo, oscuro, fijar }), [modo, oscuro, fijar]);
  return createElement(Contexto.Provider, { value: valor }, children);
}

export function useTema(): ContextoTema {
  const c = useContext(Contexto);
  if (!c) throw new Error('useTema requiere ProveedorTema');
  return c;
}

export const OPCIONES_TEMA: { modo: ModoTema; etiqueta: string; icono: string }[] = [
  { modo: 'system', etiqueta: 'Según el sistema', icono: 'brightness_auto' },
  { modo: 'light', etiqueta: 'Claro', icono: 'light_mode' },
  { modo: 'dark', etiqueta: 'Oscuro', icono: 'dark_mode' },
];
