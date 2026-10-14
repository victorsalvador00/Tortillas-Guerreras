import type { PartidaAjusteRequest, PartidaTraspasoRequest, ProductoDto } from '@tg/api-client';

/** Partida en captura (ajustes y traspasos): cantidades en texto hasta validar. */
export interface PartidaCaptura {
  clave: string;
  producto: Pick<ProductoDto, 'id' | 'sku' | 'nombre' | 'manejaLote' | 'unidadBaseClave'> | null;
  lote: string;
  cantidad: string;
  sentido: 'Entrada' | 'Salida';
  costo: string;
  caducidad: string;
}

let consecutivo = 0;

export function nuevaPartida(sentido: PartidaCaptura['sentido'] = 'Salida'): PartidaCaptura {
  consecutivo += 1;
  return { clave: `p${consecutivo}`, producto: null, lote: '', cantidad: '', sentido, costo: '', caducidad: '' };
}

export interface OpcionesValidacion {
  /** Ajustes: entradas con lote obligatorio si el producto maneja lote. */
  exigirLoteEnEntradas?: boolean;
  /** Ajustes: las entradas llevan costo (recalcula el promedio, RN-INV-05). */
  exigirCostoEnEntradas?: boolean;
}

/** Errores por `<clave>.<campo>`; vacío si todo es válido. */
export function validarPartidas(partidas: PartidaCaptura[], opciones: OpcionesValidacion = {}): Record<string, string> {
  const errores: Record<string, string> = {};
  if (partidas.length === 0) {
    errores.general = 'Agrega al menos una partida.';
    return errores;
  }
  for (const p of partidas) {
    if (!p.producto) errores[`${p.clave}.producto`] = 'Elige el producto.';
    const cantidad = Number(p.cantidad);
    if (p.cantidad.trim() === '' || !Number.isFinite(cantidad) || cantidad <= 0) {
      errores[`${p.clave}.cantidad`] = 'Captura una cantidad mayor que cero.';
    } else if (Number(cantidad.toFixed(4)) !== cantidad) {
      errores[`${p.clave}.cantidad`] = 'Máximo 4 decimales.';
    }
    const entrada = p.sentido === 'Entrada';
    if (entrada && opciones.exigirLoteEnEntradas && p.producto?.manejaLote && !p.lote.trim()) {
      errores[`${p.clave}.lote`] = 'Este producto maneja lote: indícalo.';
    }
    if (entrada && opciones.exigirCostoEnEntradas) {
      const costo = Number(p.costo);
      if (p.costo.trim() === '' || !Number.isFinite(costo) || costo < 0) {
        errores[`${p.clave}.costo`] = 'Captura el costo unitario.';
      }
    }
  }
  return errores;
}

/** Ajuste: cantidad con signo (+ entrada, − salida); costo solo en entradas. */
export function aPartidasAjuste(partidas: PartidaCaptura[]): PartidaAjusteRequest[] {
  return partidas.map((p) => {
    const entrada = p.sentido === 'Entrada';
    const cantidad = Number(p.cantidad);
    return {
      productoId: p.producto!.id,
      lote: p.lote.trim() || null,
      cantidad: entrada ? cantidad : -cantidad,
      costoUnitario: entrada && p.costo.trim() !== '' ? Number(p.costo) : null,
      fechaCaducidad: entrada && p.caducidad ? p.caducidad : null,
    };
  });
}

/** Traspaso: cantidades positivas; sin lote = FEFO en el origen. */
export function aPartidasTraspaso(partidas: PartidaCaptura[]): PartidaTraspasoRequest[] {
  return partidas.map((p) => ({ productoId: p.producto!.id, lote: p.lote.trim() || null, cantidad: Number(p.cantidad) }));
}
