/**
 * Importes en centavos enteros (regla 2 de CLAUDE.md). El servidor recalcula y es la fuente de verdad; este cálculo
 * replica RN-VEN-02 para que el total que ve el vendedor coincida con el del servidor (RN-APP-06).
 */

/** Redondeo a entero con la mitad alejándose de cero (MidpointRounding.AwayFromZero de .NET). */
export function redondearAwayFromZero(valor: number): number {
  const signo = valor < 0 ? -1 : 1;
  // El épsilon corrige representaciones binarias como 2.675 * 100 = 267.49999…
  return signo * Math.round(Math.abs(valor) + 1e-9);
}

/** Pesos → centavos. Acepta números o cadenas con hasta 4 decimales (precios). */
export function aCentavos(pesos: number | string): number {
  const n = typeof pesos === 'string' ? Number(pesos) : pesos;
  if (!Number.isFinite(n)) {
    throw new Error(`Importe inválido: ${pesos}`);
  }
  return redondearAwayFromZero(n * 100);
}

/** Centavos → pesos (número con 2 decimales exactos). */
export function aPesos(centavos: number): number {
  return centavos / 100;
}

/** Precio unitario en diezmilésimos (numeric(18,4)) para no perder precisión al multiplicar. */
export function aDiezmilesimos(valor: number | string): number {
  const n = typeof valor === 'string' ? Number(valor) : valor;
  return redondearAwayFromZero(n * 10_000);
}

export interface PartidaParaCalculo {
  /** Cantidad en la unidad capturada (hasta 4 decimales). */
  cantidad: number;
  /** Precio unitario sin impuestos, en pesos (hasta 4 decimales). */
  precioUnitario: number;
  /** Tasa de IVA: 0.16 = 16 %. */
  tasaIva: number;
  /** Tasa de IEPS: 0.08 = 8 %. */
  tasaIeps?: number | null;
}

export interface ImportesPartida {
  importe: number;
  ieps: number;
  iva: number;
  total: number;
}

/**
 * RN-VEN-02 por partida, en centavos: importe = round(cantidad × precio, 2); ieps = round(importe × tasa_ieps, 2);
 * iva = round((importe + ieps) × tasa_iva, 2).
 */
export function calcularPartida(p: PartidaParaCalculo): ImportesPartida {
  const cantidad = aDiezmilesimos(p.cantidad);
  const precio = aDiezmilesimos(p.precioUnitario);
  // cantidad × precio en (1/10⁴)² → centavos dividiendo entre 10⁶.
  const importe = redondearAwayFromZero((cantidad * precio) / 1_000_000);
  const ieps = redondearAwayFromZero(importe * (p.tasaIeps ?? 0));
  const iva = redondearAwayFromZero((importe + ieps) * p.tasaIva);
  return { importe, ieps, iva, total: importe + ieps + iva };
}

export interface TotalesDocumento {
  subtotal: number;
  ieps: number;
  iva: number;
  total: number;
}

/** Redondeo por partida y luego suma (02-convenciones). Todo en centavos. */
export function calcularTotales(partidas: PartidaParaCalculo[]): TotalesDocumento {
  return partidas.map(calcularPartida).reduce<TotalesDocumento>(
    (acc, p) => ({
      subtotal: acc.subtotal + p.importe,
      ieps: acc.ieps + p.ieps,
      iva: acc.iva + p.iva,
      total: acc.total + p.total,
    }),
    { subtotal: 0, ieps: 0, iva: 0, total: 0 },
  );
}
