import {
  aPesos,
  fechaOperativa,
  datosCapturarPedido,
  type GpsSync,
  type TipoOperacion,
  datosEntregarPedido,
  datosProponerUbicacion,
  datosRegistrarCobro,
  datosRegistrarRetorno,
  datosRegistrarVenta,
} from '@tg/shared';
import { formatearFolio } from '@/dominio/identificadores';
import { aBase, calcularVenta, type PartidaCaptura } from '@/dominio/ventas';
import { nuevoId } from '@/servicios/ids';
import { and, eq } from 'drizzle-orm';
import { db, type TransaccionLocal } from './cliente';
import * as t from './esquema';
import { asegurarEstado, tomarConsecutivo, tomarSecuencia } from './repositorio';

/**
 * Operaciones que registra el vendedor. Cada una guarda su registro local, la operación pendiente de enviar y, si
 * lleva folio, incrementa el consecutivo — todo en UNA transacción de SQLite (RN-APP-01).
 */

/** (v0.2) `Cheque` solo de contado. */
export type FormaPagoApp = 'Efectivo' | 'Transferencia' | 'Credito' | 'Cheque';

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
  formaPago: FormaPagoApp;
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
  formaPago: FormaPagoApp;
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

// ───────────────────────────── (v0.2) Pedidos ─────────────────────────────

export interface PartidaPedidoApp {
  productoId: string;
  unidadId: string;
  unidadClave: string;
  descripcion: string;
  cantidad: number;
  factorABase: number;
  tipo: 'Venta' | 'CambioFisico';
  notas: string | null;
}

export interface BorradorPedido {
  /** Sin id se crea un borrador nuevo. */
  id?: string;
  clienteId: string;
  fechaEntrega: string;
  ordenCompraCliente: string | null;
  notas: string | null;
  esRuta: boolean;
  partidas: PartidaPedidoApp[];
}

function escribirPartidasPedido(tx: TransaccionLocal, pedidoId: string, partidas: PartidaPedidoApp[]) {
  tx.delete(t.pedidosLocalesPartidas).where(eq(t.pedidosLocalesPartidas.pedidoId, pedidoId)).run();
  for (const p of partidas.filter((x) => x.cantidad > 0)) {
    tx.insert(t.pedidosLocalesPartidas)
      .values({
        id: nuevoId(), pedidoId, productoId: p.productoId, unidadId: p.unidadId, unidadClave: p.unidadClave,
        descripcion: p.descripcion, cantidad: p.cantidad, cantidadBase: aBase(p.cantidad, p.factorABase), tipo: p.tipo, notas: p.notas,
      })
      .run();
  }
}

/** Guarda (o actualiza) un borrador local: no sube nada al servidor (RN-APP-11). Devuelve su id. */
export function guardarBorrador(b: BorradorPedido): string {
  const id = b.id ?? nuevoId();
  db.transaction((tx) => {
    const actual = b.id ? tx.select().from(t.pedidosLocales).where(eq(t.pedidosLocales.id, b.id)).get() : undefined;
    if (actual && actual.estado !== 'Borrador') throw new Error('El pedido ya se envió y no se puede editar.');
    const fila = {
      clienteId: b.clienteId, fechaEntrega: b.fechaEntrega, ordenCompraCliente: b.ordenCompraCliente, notas: b.notas, esRuta: b.esRuta,
    };
    if (actual) {
      tx.update(t.pedidosLocales).set(fila).where(eq(t.pedidosLocales.id, id)).run();
    } else {
      tx.insert(t.pedidosLocales).values({ id, ...fila, estado: 'Borrador', creadoEn: instanteLocal() }).run();
    }
    escribirPartidasPedido(tx, id, b.partidas);
  });
  return id;
}

/** Envía el borrador: encola `CapturarPedido` (RN-SYN-10) y lo deja `Enviado`, ya no editable. */
export function enviarPedido(id: string, gps: GpsSync | null): void {
  const creadaEn = instanteLocal();
  db.transaction((tx) => {
    const p = tx.select().from(t.pedidosLocales).where(and(eq(t.pedidosLocales.id, id), eq(t.pedidosLocales.estado, 'Borrador'))).get();
    if (!p) throw new Error('El pedido ya se envió.');
    const partidas = tx.select().from(t.pedidosLocalesPartidas).where(eq(t.pedidosLocalesPartidas.pedidoId, id)).all();
    if (partidas.length === 0) throw new Error('Agrega al menos un producto.');
    const datos = datosCapturarPedido.parse({
      pedidoId: id, clienteId: p.clienteId, fechaEntrega: p.fechaEntrega, ordenCompraCliente: p.ordenCompraCliente || null,
      esRuta: p.esRuta, notas: p.notas || null, gps,
      partidas: partidas.map((x) => ({ productoId: x.productoId, unidadId: x.unidadId, cantidad: x.cantidad, tipo: x.tipo, notas: x.notas || null })),
    });
    tx.update(t.pedidosLocales).set({ estado: 'Enviado', enviadoEn: creadaEn }).where(eq(t.pedidosLocales.id, id)).run();
    encolar(tx, 'CapturarPedido', datos, creadaEn, id, `Pedido para ${p.fechaEntrega}`);
  });
}

export function eliminarBorrador(id: string): void {
  db.transaction((tx) => {
    const p = tx.select().from(t.pedidosLocales).where(eq(t.pedidosLocales.id, id)).get();
    if (p?.estado !== 'Borrador') return;
    tx.delete(t.pedidosLocalesPartidas).where(eq(t.pedidosLocalesPartidas.pedidoId, id)).run();
    tx.delete(t.pedidosLocales).where(eq(t.pedidosLocales.id, id)).run();
  });
}

/** Copia un pedido anterior (enviado, rechazado o borrador) como borrador nuevo con otra fecha de entrega. */
export function duplicarPedido(id: string, fechaEntrega: string): string {
  const p = db.select().from(t.pedidosLocales).where(eq(t.pedidosLocales.id, id)).get();
  if (!p) throw new Error('El pedido ya no está en este teléfono.');
  const partidas = db.select().from(t.pedidosLocalesPartidas).where(eq(t.pedidosLocalesPartidas.pedidoId, id)).all();
  return guardarBorrador({
    clienteId: p.clienteId, fechaEntrega, ordenCompraCliente: null, notas: p.notas, esRuta: p.esRuta,
    partidas: partidas.map((x) => ({
      productoId: x.productoId, unidadId: x.unidadId, unidadClave: x.unidadClave, descripcion: x.descripcion, cantidad: x.cantidad,
      factorABase: x.cantidad > 0 ? x.cantidadBase / x.cantidad : 1, tipo: x.tipo, notas: x.notas,
    })),
  });
}

// ───────────────────────────── (v0.2) Cambio físico ─────────────────────────────

export interface PartidaCambio {
  productoId: string;
  cantidadBase: number;
  condicion: 'Vendible' | 'NoVendible';
  motivoMermaId: string | null;
}

/**
 * Cambio físico (RN-LOG-15/16): lo retirado vuelve como devolución (lo no vendible es merma) y lo entregado sale de la
 * camioneta. Lo que se entrega de menos queda pendiente salvo "No dejar pendiente". Las entregas contra un pendiente
 * anterior llevan su `cambioPendienteId` (RN-LOG-17). Retorno `CambioFisico` con folio, en una transacción.
 */
export function registrarCambioFisico(entrada: {
  clienteId: string;
  retirar: PartidaCambio[];
  entregar: { productoId: string; cantidadBase: number; cambioPendienteId: string | null }[];
  dejarPendiente: boolean;
  gps: GpsSync | null;
}): ResultadoLocal {
  const ahora = new Date();
  const creadaEn = instanteLocal(ahora);
  const id = nuevoId();
  return db.transaction((tx) => {
    const { folio, consecutivo } = folioSiguiente(tx);
    const fecha = fechaOperativa(ahora);
    const base = { retornoId: id, folio, clienteId: entrada.clienteId, fechaOperativa: fecha, creadaEn };
    for (const p of entrada.retirar) {
      tx.insert(t.cambiosLocales)
        .values({
          ...base, id: nuevoId(), productoId: p.productoId, cantidadBase: p.cantidadBase, sentido: 'Retirado', condicion: p.condicion,
          motivoMermaId: p.motivoMermaId,
        })
        .run();
    }
    for (const p of entrada.entregar) {
      tx.insert(t.cambiosLocales)
        .values({
          ...base, id: nuevoId(), productoId: p.productoId, cantidadBase: p.cantidadBase, sentido: 'Entregado', condicion: 'Vendible',
          cambioPendienteId: p.cambioPendienteId,
        })
        .run();
    }
    const datos = datosRegistrarRetorno.parse({
      retornoId: id, folio, consecutivo, tipo: 'CambioFisico', clienteId: entrada.clienteId, ventaId: null, pedidoId: null,
      partidas: [
        ...entrada.retirar.map((p) => ({
          productoId: p.productoId, cantidad: p.cantidadBase, condicion: p.condicion, motivoMermaId: p.motivoMermaId,
          sentido: 'Retirado' as const, dejarPendiente: entrada.dejarPendiente,
        })),
        ...entrada.entregar.map((p) => ({
          productoId: p.productoId, cantidad: p.cantidadBase, condicion: 'Vendible' as const, motivoMermaId: null,
          sentido: 'Entregado' as const, cambioPendienteId: p.cambioPendienteId,
        })),
      ],
      gps: entrada.gps,
    });
    encolar(tx, 'RegistrarRetorno', datos, creadaEn, id, `Cambio físico ${folio}`);
    return { id, folio };
  });
}

/** RN-VEN-06: propone la ubicación de un cliente sin ubicación aprobada. */
export function proponerUbicacion(clienteId: string, gps: GpsSync): void {
  const creadaEn = instanteLocal();
  db.transaction((tx) => {
    encolar(tx, 'ProponerUbicacionCliente', datosProponerUbicacion.parse({ clienteId, gps }), creadaEn, clienteId, 'Ubicación de cliente');
  });
}
