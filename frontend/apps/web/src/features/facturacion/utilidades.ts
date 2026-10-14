import { clienteApi, type VentaDto } from '@tg/api-client';

/** Motivos de cancelación del SAT (cancelación desde el sistema [SUP-15]). */
export const MOTIVOS_CANCELACION_SAT = [
  { valor: '01', etiqueta: '01 · Comprobante emitido con errores con relación (requiere sustituto)' },
  { valor: '02', etiqueta: '02 · Comprobante emitido con errores sin relación' },
  { valor: '03', etiqueta: '03 · No se llevó a cabo la operación' },
  { valor: '04', etiqueta: '04 · Operación nominativa relacionada en una factura global' },
];

/** Descarga el XML o PDF de una factura con la sesión actual (el API exige el token). */
export async function descargarArchivoFactura(id: string, tipo: 'xml' | 'pdf', nombre: string): Promise<void> {
  const blob = await clienteApi<Blob>({ url: `/api/v1/facturacion/facturas/${id}/${tipo}`, method: 'GET', responseType: 'blob' });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = `${nombre}.${tipo}`;
  a.click();
  URL.revokeObjectURL(url);
}

/** Ventas que todavía se pueden facturar (RN-FAC-01: registradas y sin factura vigente). */
export function ventasFacturables(ventas: VentaDto[]): VentaDto[] {
  return ventas.filter((v) => v.estado === 'Registrada' && !v.facturaId);
}

export interface TotalesSeleccion {
  ventas: number;
  subtotal: number;
  ieps: number;
  iva: number;
  total: number;
}

/** Suma en centavos para no acumular errores de punto flotante. */
export function totalesSeleccion(ventas: VentaDto[], seleccion: ReadonlySet<string>): TotalesSeleccion {
  const elegidas = ventas.filter((v) => seleccion.has(v.id));
  const suma = (f: (v: VentaDto) => number) => elegidas.reduce((a, v) => a + Math.round(f(v) * 100), 0) / 100;
  return {
    ventas: elegidas.length,
    subtotal: suma((v) => v.subtotal),
    ieps: suma((v) => v.ieps),
    iva: suma((v) => v.iva),
    total: suma((v) => v.total),
  };
}

/** RN-FAC-07: PUE para contado y PPD para crédito (si alguna venta es a crédito, la factura es PPD). */
export function metodoPago(ventas: VentaDto[]): 'PUE' | 'PPD' {
  return ventas.some((v) => v.condicionPago === 'Credito') ? 'PPD' : 'PUE';
}
