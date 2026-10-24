import { diaSemanaIso } from './ventas';

/** Reglas v0.2 de la app de ruta (06): precios especiales, exclusivos, pedidos, cambios y cantidades confiables. */

export interface PrecioEspecial {
  productoId: string;
  unidadId: string;
  precioUnitario: number;
  vigenteDesde: string;
}

/**
 * RN-VEN-12: el precio especial del cliente vigente en `fecha` (`vigenteDesde ≤ fecha`) gana sobre el de la lista;
 * sin especial vigente, el de la lista (o `null` si tampoco hay).
 */
export function precioVigente(
  productoId: string,
  unidadId: string,
  fecha: string,
  especiales: readonly PrecioEspecial[],
  precioLista: number | null,
): { precio: number | null; especial: boolean } {
  const especial = especiales.find((e) => e.productoId === productoId && e.unidadId === unidadId && e.vigenteDesde <= fecha);
  return especial ? { precio: especial.precioUnitario, especial: true } : { precio: precioLista, especial: false };
}

export interface Exclusividad {
  productoId: string;
  clienteId: string | null;
  grupoClienteId: string | null;
}

/**
 * RN-VEN-14: un producto con registros de exclusividad solo se ofrece a los clientes listados o a los de los grupos
 * listados; sin registros está disponible para todos.
 */
export function productoPermitido(
  productoId: string,
  cliente: { id: string; grupoClienteId: string | null },
  exclusivos: readonly Exclusividad[],
): boolean {
  const registros = exclusivos.filter((x) => x.productoId === productoId);
  if (registros.length === 0) return true;
  return registros.some(
    (x) => x.clienteId === cliente.id || (x.grupoClienteId != null && x.grupoClienteId === cliente.grupoClienteId),
  );
}

/** Suma `dias` a una fecha `yyyy-MM-dd`. */
function sumar(fecha: string, dias: number): string {
  const [a, m, d] = fecha.split('-').map(Number);
  return new Date(Date.UTC(a, m - 1, d + dias)).toISOString().slice(0, 10);
}

/**
 * Fecha de entrega por defecto de un pedido: el siguiente día de visita del cliente después de hoy (RN-VEN-15 exige
 * mínimo hoy). Sin días de visita, mañana [SUP-31].
 */
export function siguienteDiaVisita(hoy: string, diasVisita: readonly number[]): string {
  if (diasVisita.length === 0) return sumar(hoy, 1);
  for (let i = 1; i <= 7; i++) {
    const fecha = sumar(hoy, i);
    if (diasVisita.includes(diaSemanaIso(fecha))) return fecha;
  }
  return sumar(hoy, 1);
}

export interface AlertaCantidad {
  productoId: string;
  descripcion: string;
  cantidad: number;
  unidad: string;
  /** Promedio del cliente en la misma unidad que se capturó; `null` si la alerta es por el tope. */
  normal: number | null;
}

/**
 * RN-APP-09: una partida pide confirmación si su cantidad supera `factor` × el promedio del cliente para el producto
 * (en unidad base) o el tope `maxima` (en la unidad capturada).
 */
export function alertasCantidad(
  partidas: readonly {
    productoId: string;
    descripcion: string;
    cantidad: number;
    unidadClave: string;
    factorABase: number;
  }[],
  promedios: ReadonlyMap<string, number>,
  factor: number,
  maxima: number,
): AlertaCantidad[] {
  const alertas: AlertaCantidad[] = [];
  for (const p of partidas) {
    if (p.cantidad <= 0) continue;
    const promedioBase = promedios.get(p.productoId);
    const base = p.cantidad * p.factorABase;
    const normal = promedioBase != null && promedioBase > 0 ? Math.round((promedioBase / (p.factorABase || 1)) * 100) / 100 : null;
    const sobrePromedio = promedioBase != null && promedioBase > 0 && factor > 0 && base > factor * promedioBase + 1e-9;
    const sobreMaximo = maxima > 0 && p.cantidad > maxima + 1e-9;
    if (sobrePromedio || sobreMaximo) {
      alertas.push({ productoId: p.productoId, descripcion: p.descripcion, cantidad: p.cantidad, unidad: p.unidadClave, normal });
    }
  }
  return alertas;
}

/** "¿Seguro? Capturaste 200 PAQ (normalmente 20)". */
export function textoAlerta(a: AlertaCantidad): string {
  const normal = a.normal != null ? ` (normalmente ${a.normal})` : '';
  return `¿Seguro? ${a.descripcion}: capturaste ${a.cantidad} ${a.unidad}${normal}`;
}

/**
 * RN-LOG-16: lo que queda pendiente por producto en un cambio físico = retirado − entregado (sin contar lo que salda
 * pendientes anteriores), nunca negativo.
 */
export function pendientesCambio(
  retirados: readonly { productoId: string; cantidadBase: number }[],
  entregados: readonly { productoId: string; cantidadBase: number; cambioPendienteId?: string | null }[],
): Map<string, number> {
  const resultado = new Map<string, number>();
  const r4 = (n: number) => Math.round(n * 10_000) / 10_000;
  for (const r of retirados) resultado.set(r.productoId, r4((resultado.get(r.productoId) ?? 0) + r.cantidadBase));
  for (const e of entregados) {
    if (e.cambioPendienteId || !resultado.has(e.productoId)) continue;
    resultado.set(e.productoId, r4(resultado.get(e.productoId)! - e.cantidadBase));
  }
  for (const [id, n] of resultado) {
    if (n <= 0) resultado.delete(id);
  }
  return resultado;
}
