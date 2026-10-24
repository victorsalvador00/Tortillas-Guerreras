/** Conversión mínima que necesita el desglose (sirve con `ConversionDto` de la web y con la tabla local de la app). */
export interface ConversionCajas {
  unidadClave: string;
  factorABase: number;
  predeterminadaVenta: boolean;
  predeterminadaTraslado?: boolean;
}

export interface DesgloseCajas {
  cajas: number;
  unidadTraslado: string;
  paquetes: number;
  unidadVenta: string;
}

/**
 * RN-CAT-09: una cantidad de surtido como "N cajas + M paquetes".
 * `N = floor(cantidad_venta / paquetes_por_caja)`, `M = resto`, con `paquetes_por_caja = factor_traslado / factor_venta`.
 * La unidad de venta es la predeterminada (o la base). `null` si el producto no tiene unidad predeterminada de traslado.
 */
export function desglosarCajas(
  cantidadBase: number,
  conversiones: readonly ConversionCajas[],
  unidadBaseClave: string,
): DesgloseCajas | null {
  const traslado = conversiones.find((c) => c.predeterminadaTraslado);
  if (!traslado) return null;
  const venta = conversiones.find((c) => c.predeterminadaVenta);
  const factorVenta = venta?.factorABase ?? 1;
  const paquetesPorCaja = Math.round(traslado.factorABase / factorVenta);
  // Las cantidades de venta son enteras o con pocos decimales: se redondea para no arrastrar error binario.
  const cantidadVenta = Math.round((cantidadBase / factorVenta) * 10000) / 10000;
  const signo = cantidadVenta < 0 ? -1 : 1;
  const absoluta = Math.abs(cantidadVenta);
  const cajas = Math.floor(absoluta / paquetesPorCaja);
  const paquetes = Math.round((absoluta - cajas * paquetesPorCaja) * 10000) / 10000;
  return {
    cajas: signo * cajas,
    unidadTraslado: traslado.unidadClave,
    paquetes: signo * paquetes || 0,
    unidadVenta: venta?.unidadClave ?? unidadBaseClave,
  };
}

/** "2 CAJ + 5 PAQ"; sin cajas solo los paquetes y sin resto solo las cajas. */
export function formatoCajas(d: DesgloseCajas): string {
  if (d.cajas === 0) return `${d.paquetes} ${d.unidadVenta}`;
  if (d.paquetes === 0) return `${d.cajas} ${d.unidadTraslado}`;
  return `${d.cajas} ${d.unidadTraslado} + ${d.paquetes} ${d.unidadVenta}`;
}
