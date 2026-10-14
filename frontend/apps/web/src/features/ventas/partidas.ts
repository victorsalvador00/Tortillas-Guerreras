import type { ListaPrecioPartidaDto, ProductoDto } from '@tg/api-client';
import { calcularPartida, type ImportesPartida, type TotalesDocumento } from '@tg/shared';

/** Partida en captura (pedido o venta web). La cantidad queda como texto para no perder lo que escribe el usuario. */
export interface LineaVenta {
  clave: string;
  producto: Pick<ProductoDto, 'id' | 'sku' | 'nombre' | 'unidadBaseId' | 'unidadBaseClave' | 'tasaIva' | 'tasaIeps' | 'conversiones'> | null;
  unidadId: string;
  cantidad: string;
}

export interface LineaCalculada {
  /** Precio de lista sin impuestos (RN-VEN-01); `null` si el producto no está en la lista del cliente. */
  precio: number | null;
  importes: ImportesPartida | null;
}

let consecutivo = 0;

export function nuevaLinea(): LineaVenta {
  consecutivo += 1;
  return { clave: `l${consecutivo}`, producto: null, unidadId: '', cantidad: '' };
}

/** Unidades de venta del producto: la base y las conversiones habilitadas para venta. */
export function unidadesVenta(producto: LineaVenta['producto']): { valor: string; etiqueta: string }[] {
  if (!producto) return [];
  return [
    { valor: producto.unidadBaseId, etiqueta: producto.unidadBaseClave },
    ...producto.conversiones
      .filter((c) => c.usoVenta)
      .map((c) => ({ valor: c.unidadId, etiqueta: `${c.unidadClave} (×${c.factorABase})` })),
  ];
}

/** Unidad sugerida: la predeterminada de venta o la base. */
export function unidadSugerida(producto: NonNullable<LineaVenta['producto']>): string {
  return producto.conversiones.find((c) => c.predeterminadaVenta)?.unidadId ?? producto.unidadBaseId;
}

export function precioDeLista(
  precios: Pick<ListaPrecioPartidaDto, 'productoId' | 'unidadId' | 'precioUnitario'>[] | undefined,
  productoId: string,
  unidadId: string,
): number | null {
  return precios?.find((p) => p.productoId === productoId && p.unidadId === unidadId)?.precioUnitario ?? null;
}

/**
 * Importes por partida y totales del documento en centavos (RN-VEN-02, mismo cálculo que el servidor). Las partidas
 * incompletas o sin precio no suman.
 */
export function calcularLineas(
  lineas: LineaVenta[],
  precios: Pick<ListaPrecioPartidaDto, 'productoId' | 'unidadId' | 'precioUnitario'>[] | undefined,
): { lineas: LineaCalculada[]; totales: TotalesDocumento; completas: boolean } {
  const totales: TotalesDocumento = { subtotal: 0, ieps: 0, iva: 0, total: 0 };
  let completas = lineas.length > 0;
  const calculadas = lineas.map((l) => {
    const cantidad = Number(l.cantidad);
    if (!l.producto || !l.unidadId || !(cantidad > 0)) {
      completas = false;
      return { precio: l.producto ? precioDeLista(precios, l.producto.id, l.unidadId) : null, importes: null };
    }
    const precio = precioDeLista(precios, l.producto.id, l.unidadId);
    if (precio == null) {
      completas = false;
      return { precio, importes: null };
    }
    const importes = calcularPartida({ cantidad, precioUnitario: precio, tasaIva: l.producto.tasaIva, tasaIeps: l.producto.tasaIeps });
    totales.subtotal += importes.importe;
    totales.ieps += importes.ieps;
    totales.iva += importes.iva;
    totales.total += importes.total;
    return { precio, importes };
  });
  return { lineas: calculadas, totales, completas };
}
