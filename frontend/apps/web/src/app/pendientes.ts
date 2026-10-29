import { useRouterState } from '@tanstack/react-router';
import { useObtenerPendientes, type PendientesDto } from '@tg/api-client';
import { useEffect, useMemo } from 'react';
import { useSesion } from './sesion';

/** Cada cuánto se refrescan los globos con la app abierta. */
export const INTERVALO_PENDIENTES_MS = 60_000;

/** Quien no tiene ninguno de estos permisos no tiene nada esperando: ni siquiera se consulta (RN-WEB-01). */
export const PERMISOS_PENDIENTES = [
  'compras.autorizar',
  'compras.recibir',
  'produccion.planear',
  'produccion.operar',
  'produccion.cerrar',
  'logistica.liquidar_producto',
  'logistica.revisar_liquidacion',
  'logistica.liquidar',
  'ventas.pedidos',
  'ventas.supervisar',
  'ventas.clientes',
  'facturacion.emitir',
];

/** Globo rojo de una opción del menú: cuántos documentos esperan y cuáles son. */
export interface Globo {
  total: number;
  /** "2 por autorizar · 1 por recibir". */
  detalle: string;
}

/** RN-WEB-01: el globo de cada ruta del menú, solo donde algo espera. */
export function globosMenu(p: PendientesDto | undefined): ReadonlyMap<string, Globo> {
  const globos = new Map<string, Globo>();
  if (!p) return globos;
  const agregar = (ruta: string, partes: [number | null | undefined, string][]) => {
    const esperan = partes.filter((x): x is [number, string] => (x[0] ?? 0) > 0);
    const total = esperan.reduce((suma, [n]) => suma + n, 0);
    if (total > 0) globos.set(ruta, { total, detalle: esperan.map(([n, etiqueta]) => `${n} ${etiqueta}`).join(' · ') });
  };
  agregar('/compras/ordenes', [
    [p.ordenesCompraPorAutorizar, 'por autorizar'],
    [p.ordenesCompraPorRecibir, 'por recibir'],
  ]);
  agregar('/produccion/plan-sugerido', [[p.planPorProgramar, 'por programar']]);
  agregar('/produccion/ordenes', [
    [p.ordenesPorLiberar, 'por liberar'],
    [p.ordenesPorProducir, 'por producir'],
    [p.ordenesEnProceso, 'en proceso'],
  ]);
  agregar('/logistica/liquidaciones', [
    [p.liquidacionesPorConfirmar, 'por confirmar producto'],
    [p.liquidacionesPorRevisar, 'por revisar'],
    [p.liquidacionesPorCerrar, 'por cerrar'],
  ]);
  agregar('/ventas/pedidos', [[p.pedidosPorValidar, 'por validar']]);
  agregar('/ventas/incidencias', [[p.incidenciasAbiertas, 'abiertas']]);
  agregar('/ventas/clientes', [[p.clientesPorAprobar, 'con ubicación por aprobar']]);
  agregar('/facturacion/solicitudes', [[p.solicitudesAutofactura, 'por atender']]);
  return globos;
}

/** Como en los celulares: hasta 99, luego "99+". */
export function textoGlobo(total: number): string {
  return total > 99 ? '99+' : String(total);
}

/**
 * RN-WEB-01: globos del menú. Se refrescan cada minuto, al navegar y al volver a la pestaña; después de guardar
 * cualquier cambio también, porque el `MutationCache` invalida todas las consultas.
 */
export function usePendientes(): ReadonlyMap<string, Globo> {
  const { tienePermiso } = useSesion();
  const ruta = useRouterState({ select: (s) => s.location.pathname });
  const habilitado = tienePermiso(PERMISOS_PENDIENTES);
  const { data, refetch } = useObtenerPendientes({
    query: {
      enabled: habilitado,
      refetchInterval: INTERVALO_PENDIENTES_MS,
      refetchOnWindowFocus: true,
      // Un globo que no se pudo refrescar conserva su último valor; sin avisos de error cada minuto.
      retry: false,
    },
  });
  useEffect(() => {
    if (habilitado) void refetch();
  }, [ruta, habilitado, refetch]);
  return useMemo(() => globosMenu(data), [data]);
}
