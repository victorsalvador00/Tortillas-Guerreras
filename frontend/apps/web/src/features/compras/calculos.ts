import { calcularTotales, type TotalesDocumento } from '@tg/shared';
import type { ProductoDto } from '@tg/api-client';

/** Unidades en que se puede comprar un producto: la base y las conversiones con uso de compra. */
export function unidadesCompra(p: Pick<ProductoDto, 'unidadBaseId' | 'unidadBaseClave' | 'conversiones'>) {
  return [
    { valor: p.unidadBaseId, etiqueta: p.unidadBaseClave, factor: 1 },
    ...p.conversiones
      .filter((c) => c.usoCompra)
      .map((c) => ({ valor: c.unidadId, etiqueta: `${c.unidadClave} (${c.factorABase} ${p.unidadBaseClave})`, factor: c.factorABase })),
  ];
}

export interface PartidaCaptura {
  cantidad: number | string;
  precioUnitario: number | string;
  tasaIva: number;
}

/** Totales de una orden de compra en centavos: precios sin IVA, redondeo por partida (02-convenciones). */
export function totalesOrden(partidas: PartidaCaptura[]): TotalesDocumento {
  return calcularTotales(
    partidas
      .map((p) => ({ cantidad: Number(p.cantidad), precioUnitario: Number(p.precioUnitario), tasaIva: p.tasaIva }))
      .filter((p) => Number.isFinite(p.cantidad) && Number.isFinite(p.precioUnitario) && p.cantidad > 0),
  );
}

/** Lo pendiente de recibir de una partida, expresado en la unidad de la partida. */
export function pendientePartida(p: { cantidadBase: number; cantidadRecibidaBase: number; factorABase: number }): number {
  const pendienteBase = Math.max(0, p.cantidadBase - p.cantidadRecibidaBase);
  return Math.round((pendienteBase / p.factorABase) * 10_000) / 10_000;
}

/** Porcentaje recibido (0–100) de una partida. */
export function avanceRecepcion(p: { cantidadBase: number; cantidadRecibidaBase: number }): number {
  if (p.cantidadBase <= 0) return 0;
  return Math.min(100, Math.round((p.cantidadRecibidaBase / p.cantidadBase) * 100));
}
