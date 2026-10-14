import type { ProductoDto } from '@tg/api-client';

/** Cálculos de la orden de producción para mostrar en planta (el servidor recalcula al terminar). */

const redondear4 = (v: number) => Math.round(v * 10_000) / 10_000;

/** RN-PRO-02: consumo teórico = cantidad del componente × planeada / rendimiento. */
export function consumoTeorico(cantidadComponenteBase: number, cantidadPlaneada: number, rendimiento: number): number {
  return rendimiento > 0 ? redondear4((cantidadComponenteBase * cantidadPlaneada) / rendimiento) : 0;
}

/** RN-PRO-03: el teórico se recalcula con la cantidad producida en lugar de la planeada. */
export function teoricoAjustado(teoricoPlaneado: number, planeada: number, producida: number | null | undefined): number {
  if (!producida || planeada <= 0) return teoricoPlaneado;
  return redondear4((teoricoPlaneado * producida) / planeada);
}

/** RN-PRO-08: variación = (real − teórico) / teórico. */
export function variacion(real: number, teorico: number): number | null {
  return teorico > 0 ? (real - teorico) / teorico : null;
}

export function fueraDeUmbral(v: number | null, umbral: number): boolean {
  return v !== null && Math.abs(v) > umbral;
}

export interface ConsumoParaCosto {
  real: number;
  costoUnitario: number;
}

/** RN-PRO-05: costo unitario = Σ(consumo × costo promedio) / producida. */
export function costoEstimado(consumos: ConsumoParaCosto[], producida: number): { total: number; unitario: number | null } {
  const total = Math.round(consumos.reduce((s, c) => s + c.real * c.costoUnitario, 0) * 100) / 100;
  return { total, unitario: producida > 0 ? Math.round((total / producida) * 1_000_000) / 1_000_000 : null };
}

/** Avance de la orden (0–100) para el plan del día. */
export function avanceOrden(estado: string, planeada: number, producida: number | null | undefined): number {
  if (estado === 'Terminada') return planeada > 0 ? Math.min(100, Math.round(((producida ?? 0) / planeada) * 100)) : 100;
  return { Planeada: 0, Liberada: 15, EnProceso: 50 }[estado] ?? 0;
}

/** Unidades para recetas: la base y las conversiones con uso de producción. */
export function unidadesProduccion(p: Pick<ProductoDto, 'unidadBaseId' | 'unidadBaseClave' | 'conversiones'>) {
  return [
    { valor: p.unidadBaseId, etiqueta: p.unidadBaseClave, factor: 1 },
    ...p.conversiones
      .filter((c) => c.usoProduccion)
      .map((c) => ({ valor: c.unidadId, etiqueta: `${c.unidadClave} (${c.factorABase} ${p.unidadBaseClave})`, factor: c.factorABase })),
  ];
}
