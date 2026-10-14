import { Outlet, createRootRouteWithContext, createRoute, redirect, type AnyRoute } from '@tanstack/react-router';
import { Boton, EstadoVacio } from '@tg/ui';
import type { ReactNode } from 'react';
import { RequierePermiso } from './Pagina';
import { Shell } from './Shell';
import { sesionActiva, type ContextoSesion } from './sesion';

export interface ContextoRouter {
  sesion: ContextoSesion;
}

function NoEncontrado() {
  return (
    <div className="mx-auto max-w-xl px-4 py-16">
      <EstadoVacio
        icono="travel_explore"
        titulo="No encontramos esta página"
        mensaje="Revisa la dirección o vuelve al tablero."
        accion={
          <Boton variante="secundario" onClick={() => window.location.assign('/')}>
            Ir al tablero
          </Boton>
        }
      />
    </div>
  );
}

export const rutaRaiz = createRootRouteWithContext<ContextoRouter>()({
  component: () => <Outlet />,
  notFoundComponent: NoEncontrado,
});

/** Rutas autenticadas dentro del shell; sin sesión redirige al inicio de sesión. */
export const rutaApp = createRoute({
  getParentRoute: () => rutaRaiz,
  id: 'app',
  component: Shell,
  beforeLoad: ({ location }) => {
    if (!sesionActiva()) {
      throw redirect({ to: '/login', search: { redirigir: location.href } });
    }
  },
});

/** Atajo para declarar una pantalla protegida por permiso dentro del shell. */
export function pantalla(path: string, componente: () => ReactNode, permiso?: string | string[]): AnyRoute {
  const Componente = componente;
  return createRoute({
    getParentRoute: () => rutaApp,
    path,
    component: permiso
      ? () => (
          <RequierePermiso permiso={permiso}>
            <Componente />
          </RequierePermiso>
        )
      : Componente,
  });
}
