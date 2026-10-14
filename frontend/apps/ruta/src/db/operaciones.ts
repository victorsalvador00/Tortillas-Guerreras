import {
  aPesos,
  fechaOperativa,
  type GpsSync,
  type TipoOperacion,
  datosEntregarPedido,
  datosProponerUbicacion,
  datosRegistrarCobro,
  datosRegistrarRetorno,
  datosRegistrarVenta,
} from '@tg/shared';
import { formatearFolio } from '@/dominio/identificadores';
import { calcularVenta, type PartidaCaptura } from '@/dominio/ventas';
import { nuevoId } from '@/servicios/ids';
import { db, type TransaccionLocal } from './cliente';
import * as t from './esquema';
import { asegurarEstado, tomarConsecutivo, tomarSecuencia } from './repositorio';

/**
 * Operaciones que registra el vendedor. Cada una guarda su registro local, la operación pendiente de enviar y, si
 * lleva folio, incrementa el consecutivo — todo en UNA transacción de SQLite (RN-APP-01).
 */

export interface PartidaVentaApp extends PartidaCaptura {
  descripcion: string;
  unidadClave: string;
}

export interface ResultadoLocal {
  id: string;
  folio: string;
}

function encolar(tx: TransaccionLocal, tipo: TipoOperacion, datos: unknown, creadaEn: string, documentoId: string | null, descripcion: string) {
  tx.insert(t.operacionesPendientes)
    .values({
      opId: nuevoId(),
      secuencia: tomarSecuencia(tx),
      tipo,
      datosJson: JSON.stringify(datos),
      estado: 'Pendiente',
      intentos: 0,
      creadaEn,
      documentoId,
      descripcion,
    })
    .run();
}

function folioSiguiente(tx: TransaccionLocal): { folio: string; consecutivo: number } {
  const estado = asegurarEstado(tx);
  if (!estado.rutaCodigo || !estado.codigo) {
    throw new Error('El dispositivo aún no tiene ruta asignada. Sincroniza primero.');
  }
  const consecutivo = tomarConsecutivo(tx);
  return { folio: formatearFolio(estado.rutaCodigo, estado.codigo, consecutivo), consecutivo };
}

/** `creadaEn` con la zona del teléfono: el servidor saca la fecha operativa de aquí (RN-SYN-06). */
export function instanteLocal(ahora = new Date()): string {
  const desfase = -ahora.getTimezoneOffset();
  const signo = desfase >= 0 ? '+' : '-';
  const hh = String(Math.floor(Math.abs(desfase) / 60)).padStart(2, '0');
  const mm = String(Math.abs(desfase) % 60).padStart(2, '0');
  const local = new Date(ahora.getTime() + desfase * 60_000).toISOString().slice(0, 19);
  return `${local}${signo}${hh}:${mm}`;
}

export function confirmarVenta(entrada: {
  clienteId: string;
  condicionPago: 'Contado' | 'Credito';
  formaPago: 'Efectivo' | 'Transferencia' | 'Credito';
  partidas: PartidaVentaApp[];
  gps: GpsSync | null;
}): ResultadoLocal {
  const venta = calcularVenta(entrada.partidas);
  const ahora = new Date();
  const creadaEn = instanteLocal(ahora);
  const id = nuevoId();
  return db.transaction((tx) => {
    const { folio, consecutivo } = folioSiguiente(tx);
    tx.insert(t.ventasLocales)
      .values({
        id, folio, tipo: 'VentaRuta', clienteId: entrada.clienteId, pedidoId: null, condicionPago: entrada.condicionPago,
        formaPago: entrada.formaPago, subtotal: venta.subtotal, ieps: venta.ieps, iva: venta.iva, total: venta.total,
        fechaOperativa: fechaOperativa(ahora), creadaEn, latitud: entrada.gps?.lat ?? null, longitud: entrada.gps?.lng ?? null,
        precisionM: entrada.gps?.precisionM ?? null,
      })
      .run();
    venta.partidas.forEach((p, i) => {
      const original = entrada.partidas[i];
      tx.insert(t.ventasLocalesPartidas)
        .values({
          id: nuevoId(), ventaId: id, productoId: p.productoId, unidadId: p.unidadId, unidadClave: original.unidadClave,
          descripcion: original.descripcion, cantidad: p.cantidad, cantidadBase: p.cantidadBase, precioUnitario: p.precioUnitario,
          importe: p.importe, ieps: p.ieps, iva: p.iva, total: p.total,
        })
        .run();
    });
    const datos = datosRegistrarVenta.parse({
      ventaId: id, folio, consecutivo, clienteId: entrada.clienteId, condicionPago: entrada.condicionPago,
      formaPago: entrada.formaPago,
      partidas: entrada.partidas.map((p) => ({ productoId: p.productoId, unidadId: p.unidadId, cantidad: p.cantidad, precioUnitario: p.precioUnitario })),
      totalApp: aPesos(venta.total), gps: entrada.gps,
    });
    encolar(tx, 'RegistrarVenta', datos, creadaEn, id, `Venta ${folio}`);
    return { id, folio };
  });
}

export function confirmarEntrega(entrada: {
  pedidoId: string;
  clienteId: string;
  formaPago: 'Efectivo' | 'Transferencia' | 'Credito';
  condicionPago: 'Contado' | 'Credito';
  partidas: (PartidaVentaApp & { pedidoPartidaId: string })[];
  gps: GpsSync | null;
}): ResultadoLocal {
  const venta = calcularVenta(entrada.partidas);
  const ahora = new Date();
  const creadaEn = instanteLocal(ahora);
  const id = nuevoId();
  return db.transaction((tx) => {
    const { folio, consecutivo } = folioSiguiente(tx);
    tx.insert(t.ventasLocales)
      .values({
        id, folio, tipo: 'EntregaPedido', clienteId: entrada.clienteId, pedidoId: entrada.pedidoId,
        condicionPago: entrada.condicionPago, formaPago: entrada.formaPago, subtotal: venta.subtotal, ieps: venta.ieps,
        iva: venta.iva, total: venta.total, fechaOperativa: fechaOperativa(ahora), creadaEn,
        latitud: entrada.gps?.lat ?? null, longitud: entrada.gps?.lng ?? null, precisionM: entrada.gps?.precisionM ?? null,
      })
      .run();
    venta.partidas.forEach((p, i) => {
      const o = entrada.partidas[i];
      if (p.cantidad <= 0) return;
      tx.insert(t.ventasLocalesPartidas)
        .values({
          id: nuevoId(), ventaId: id, productoId: p.productoId, unidadId: p.unidadId, unidadClave: o.unidadClave,
          descripcion: o.descripcion, cantidad: p.cantidad, cantidadBase: p.cantidadBase, precioUnitario: p.precioUnitario,
          importe: p.importe, ieps: p.ieps, iva: p.iva, total: p.total,
        })
        .run();
    });
    const datos = datosEntregarPedido.parse({
      ventaId: id, folio, consecutivo, pedidoId: entrada.pedidoId, formaPago: entrada.formaPago,
      partidas: entrada.partidas.map((p) => ({ pedidoPartidaId: p.pedidoPartidaId, cantidad: p.cantidad })),
      totalApp: aPesos(venta.total), gps: entrada.gps,
    });
    encolar(tx, 'EntregarPedido', datos, creadaEn, id, `Entrega ${folio}`);
    return { id, folio };
  });
}

export function registrarRetorno(entrada: {
  tipo: 'DevolucionCliente' | 'RechazoEntrega';
  clienteId: string | null;
  ventaId: string | null;
  pedidoId: string | null;
  partidas: { productoId: string; cantidadBase: number; condicion: 'Vendible' | 'NoVendible'; motivoMermaId: string | null }[];
  gps: GpsSync | null;
}): ResultadoLocal {
  const ahora = new Date();
  const creadaEn = instanteLocal(ahora);
  const id = nuevoId();
  return db.transaction((tx) => {
    const { folio, consecutivo } = folioSiguiente(tx);
    for (const p of entrada.partidas) {
      tx.insert(t.retornosLocales)
        .values({
          id: nuevoId(), retornoId: id, folio, tipo: entrada.tipo, clienteId: entrada.clienteId, productoId: p.productoId,
          cantidadBase: p.cantidadBase, condicion: p.condicion, motivoMermaId: p.motivoMermaId,
          fechaOperativa: fechaOperativa(ahora), creadaEn,
        })
        .run();
    }
    const datos = datosRegistrarRetorno.parse({
      retornoId: id, folio, consecutivo, tipo: entrada.tipo, clienteId: entrada.clienteId, ventaId: entrada.ventaId,
      pedidoId: entrada.pedidoId,
      partidas: entrada.partidas.map((p) => ({ productoId: p.productoId, cantidad: p.cantidadBase, condicion: p.condicion, motivoMermaId: p.motivoMermaId })),
      gps: entrada.gps,
    });
    encolar(tx, 'RegistrarRetorno', datos, creadaEn, id, `Retorno ${folio}`);
    return { id, folio };
  });
}

/** Cobro de ventas a crédito [SUP-08]; `importe` en centavos. */
export function registrarCobro(entrada: { clienteId: string; importe: number; formaPago: 'Efectivo' | 'Transferencia' }): string {
  const ahora = new Date();
  const creadaEn = instanteLocal(ahora);
  const id = nuevoId();
  db.transaction((tx) => {
    tx.insert(t.cobrosLocales)
      .values({ id, clienteId: entrada.clienteId, importe: entrada.importe, formaPago: entrada.formaPago, fechaOperativa: fechaOperativa(ahora), creadaEn })
      .run();
    const datos = datosRegistrarCobro.parse({
      cobroId: id, clienteId: entrada.clienteId, importe: aPesos(entrada.importe), formaPago: entrada.formaPago, ventaIds: null,
    });
    encolar(tx, 'RegistrarCobro', datos, creadaEn, id, `Cobro de $${aPesos(entrada.importe).toFixed(2)}`);
  });
  return id;
}

/** RN-VEN-06: propone la ubicación de un cliente sin ubicación aprobada. */
export function proponerUbicacion(clienteId: string, gps: GpsSync): void {
  const creadaEn = instanteLocal();
  db.transaction((tx) => {
    encolar(tx, 'ProponerUbicacionCliente', datosProponerUbicacion.parse({ clienteId, gps }), creadaEn, clienteId, 'Ubicación de cliente');
  });
}
