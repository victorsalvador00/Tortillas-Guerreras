import { createRoute, createRouter } from '@tanstack/react-router';
import { Login } from '@/features/auth/Login';
import { Perfil } from '@/features/auth/Perfil';
import { rutasCatalogos } from '@/features/catalogos/rutas';
import { rutasCompras } from '@/features/compras/rutas';
import { rutasFacturacion } from '@/features/facturacion/rutas';
import { rutasInventario } from '@/features/inventario/rutas';
import { rutasLogistica } from '@/features/logistica/rutas';
import { rutasMapa } from '@/features/mapa/rutas';
import { rutasProduccion } from '@/features/produccion/rutas';
import { rutasSeguridad } from '@/features/seguridad/rutas';
import { rutasTablero } from '@/features/tablero/rutas';
import { rutasVentas } from '@/features/ventas/rutas';
import { pantalla, rutaApp, rutaRaiz } from './rutas-base';
import type { ContextoSesion } from './sesion';

const rutaLogin = createRoute({
  getParentRoute: () => rutaRaiz,
  path: '/login',
  component: Login,
});

const arbol = rutaRaiz.addChildren([
  rutaLogin,
  rutaApp.addChildren([
    pantalla('/perfil', Perfil),
    ...rutasTablero(),
    ...rutasCatalogos(),
    ...rutasInventario(),
    ...rutasCompras(),
    ...rutasProduccion(),
    ...rutasVentas(),
    ...rutasLogistica(),
    ...rutasMapa(),
    ...rutasFacturacion(),
    ...rutasSeguridad(),
  ]),
]);

export function crearRouter(sesion: ContextoSesion) {
  return createRouter({
    routeTree: arbol,
    context: { sesion },
    defaultPreload: 'intent',
    scrollRestoration: true,
  });
}
