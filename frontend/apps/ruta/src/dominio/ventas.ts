import { calcularPartida, type ImportesPartida } from '@tg/shared';

/** Carga en la que va un pedido, tal como la ve el vendedor (RN-LOG-11). */
export interface CargaDePedido {
  /** Folio de la carga; `null` si el pedido no va en ninguna. */
  folio: string | null;
  /** La carga ya se despachó y su producto está en la camioneta. */
  enCamioneta: boolean;
}

/** "En carga ENV-000123", "… · por despachar" o "Sin carga". */
export function textoCarga(carga: CargaDePedido): string {
  if (!carga.folio) return 'Sin carga';
  return carga.enCamioneta ? `En carga ${carga.folio}` : `En carga ${carga.folio} · por despachar`;
}

/** Partida capturada en la app (unidad de venta del producto). */
export interface PartidaCaptura {
  productoId: string;
  unidadId: string;
  cantidad: number;
  /** Factor de la unidad a la unidad base del producto. */
  factorABase: number;
  precioUnitario: number;
  tasaIva: number;
  tasaIeps: number | null;
}

export interface PartidaCalculada extends PartidaCaptura, ImportesPartida {
  cantidadBase: number;
}

export interface VentaCalculada {
  partidas: PartidaCalculada[];
  subtotal: number;
  ieps: number;
  iva: number;
  total: number;
}

/** Cantidad en unidad base con 4 decimales (numeric(18,4)). */
export function aBase(cantidad: number, factor: number): number {
  return Math.round(cantidad * factor * 10_000) / 10_000;
}

/**
 * RN-APP-06 / RN-VEN-02: importes en centavos enteros, por partida y luego suma — el mismo cálculo que hace el servidor.
 */
export function calcularVenta(partidas: PartidaCaptura[]): VentaCalculada {
  const calculadas = partidas.map((p) => ({
    ...p,
    cantidadBase: aBase(p.cantidad, p.factorABase),
    ...calcularPartida({ cantidad: p.cantidad, precioUnitario: p.precioUnitario, tasaIva: p.tasaIva, tasaIeps: p.tasaIeps }),
  }));
  return {
    partidas: calculadas,
    subtotal: calculadas.reduce((s, p) => s + p.importe, 0),
    ieps: calculadas.reduce((s, p) => s + p.ieps, 0),
    iva: calculadas.reduce((s, p) => s + p.iva, 0),
    total: calculadas.reduce((s, p) => s + p.total, 0),
  };
}

export interface MovimientoCamioneta {
  productoId: string;
  cantidadBase: number;
}

export interface RetornoCamioneta extends MovimientoCamioneta {
  condicion: 'Vendible' | 'NoVendible';
  tipo: 'DevolucionCliente' | 'RechazoEntrega';
}

export interface CambioCamioneta extends MovimientoCamioneta {
  sentido: 'Retirado' | 'Entregado';
  condicion: 'Vendible' | 'NoVendible';
}

export interface LineaInventario {
  productoId: string;
  cargado: number;
  vendido: number;
  devuelto: number;
  /** (v0.2) Entregado a cambio en cambios físicos. */
  cambios: number;
  existencia: number;
}

/**
 * Inventario de camioneta local (06): carga recibida − ventas locales + retornos vendibles locales − cambios
 * entregados. Lo no vendible sale como merma en el servidor (RN-LOG-04) y un rechazo de entrega no movió producto
 * (RN-LOG-05). Lo retirado vendible en un cambio físico vuelve a la camioneta como una devolución (RN-LOG-15).
 */
export function inventarioCamioneta(
  cargas: MovimientoCamioneta[],
  ventas: MovimientoCamioneta[],
  retornos: RetornoCamioneta[],
  cambios: CambioCamioneta[] = [],
): Map<string, LineaInventario> {
  const lineas = new Map<string, LineaInventario>();
  const linea = (id: string) => {
    let l = lineas.get(id);
    if (!l) {
      l = { productoId: id, cargado: 0, vendido: 0, devuelto: 0, cambios: 0, existencia: 0 };
      lineas.set(id, l);
    }
    return l;
  };
  const r4 = (n: number) => Math.round(n * 10_000) / 10_000;
  for (const c of cargas) linea(c.productoId).cargado = r4(linea(c.productoId).cargado + c.cantidadBase);
  for (const v of ventas) linea(v.productoId).vendido = r4(linea(v.productoId).vendido + v.cantidadBase);
  for (const r of retornos) {
    if (r.tipo === 'DevolucionCliente' && r.condicion === 'Vendible') {
      linea(r.productoId).devuelto = r4(linea(r.productoId).devuelto + r.cantidadBase);
    }
  }
  for (const c of cambios) {
    if (c.sentido === 'Entregado') linea(c.productoId).cambios = r4(linea(c.productoId).cambios + c.cantidadBase);
    else if (c.condicion === 'Vendible') linea(c.productoId).devuelto = r4(linea(c.productoId).devuelto + c.cantidadBase);
  }
  for (const l of lineas.values()) l.existencia = r4(l.cargado - l.vendido + l.devuelto - l.cambios);
  return lineas;
}

export interface ResultadoExistencia {
  /** Se puede confirmar la venta. */
  permitido: boolean;
  /** Productos que quedarían en negativo (se advierte aunque se permita). */
  faltantes: { productoId: string; disponible: number; requerido: number }[];
}

/**
 * RN-APP-02: no se vende más de lo que muestra el inventario de camioneta, salvo que
 * `ruta.permite_existencia_negativa = true`; en ese caso se advierte y se deja continuar [SUP-10].
 */
export function validarExistencia(
  partidas: { productoId: string; cantidadBase: number }[],
  existencias: Map<string, number>,
  permiteNegativo: boolean,
): ResultadoExistencia {
  const requerido = new Map<string, number>();
  for (const p of partidas) requerido.set(p.productoId, (requerido.get(p.productoId) ?? 0) + p.cantidadBase);
  const faltantes = [...requerido.entries()]
    .map(([productoId, cantidad]) => ({ productoId, disponible: existencias.get(productoId) ?? 0, requerido: cantidad }))
    .filter((f) => f.requerido > f.disponible + 1e-9);
  return { permitido: faltantes.length === 0 || permiteNegativo, faltantes };
}

/** Día de la semana ISO (1 = lunes … 7 = domingo) de una fecha `yyyy-MM-dd`. */
export function diaSemanaIso(fecha: string): number {
  const [a, m, d] = fecha.split('-').map(Number);
  const dia = new Date(Date.UTC(a, m - 1, d)).getUTCDay();
  return dia === 0 ? 7 : dia;
}

/** Clientes de hoy: los que tienen visita el día de la semana, en orden de visita; el genérico al final. */
export function clientesDelDia<T extends { diasVisita: number[]; ordenVisita: number; esGenerico: boolean }>(
  clientes: T[],
  fecha: string,
): T[] {
  const dia = diaSemanaIso(fecha);
  return clientes
    .filter((c) => c.esGenerico || c.diasVisita.length === 0 || c.diasVisita.includes(dia))
    .sort((a, b) => Number(a.esGenerico) - Number(b.esGenerico) || a.ordenVisita - b.ordenVisita);
}

/** Enlaces de navegación a la ubicación del cliente (06). */
export function enlacesComoLlegar(lat: number, lng: number): { googleMaps: string; waze: string } {
  return {
    googleMaps: `https://www.google.com/maps/dir/?api=1&destination=${lat},${lng}&travelmode=driving`,
    waze: `https://waze.com/ul?ll=${lat},${lng}&navigate=yes`,
  };
}
