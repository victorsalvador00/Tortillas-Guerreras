import { and, desc, eq, gte, inArray } from 'drizzle-orm';
import { clientesDelDia, inventarioCamioneta, type CargaDePedido, type LineaInventario } from '@/dominio/ventas';
import { db } from './cliente';
import * as t from './esquema';
import { asegurarEstado } from './repositorio';

/** Consultas de las pantallas; todo sale de la base local (la app funciona sin red). */

export function estadoDispositivo() {
  return asegurarEstado(db);
}

export function parametro<T extends string | number | boolean>(clave: string, predeterminado: T): T {
  const fila = db.select().from(t.parametros).where(eq(t.parametros.clave, clave)).get();
  if (!fila) return predeterminado;
  if (typeof predeterminado === 'boolean') return (fila.valor.toLowerCase() === 'true') as T;
  if (typeof predeterminado === 'number') {
    const n = Number(fila.valor);
    return (Number.isFinite(n) ? n : predeterminado) as T;
  }
  return fila.valor as T;
}

/** Envíos vigentes (EnRuta) y la fecha desde la que cuentan ventas y retornos locales. */
function enviosVigentes() {
  const lista = db.select().from(t.envios).where(eq(t.envios.estado, 'EnRuta')).all();
  const desde = lista.map((e) => e.fechaOperativa).sort()[0] ?? null;
  return { lista, desde };
}

export interface LineaInventarioVista extends LineaInventario {
  sku: string;
  nombre: string;
  unidad: string;
}

/** Inventario de camioneta: carga recibida − ventas locales + retornos vendibles locales (06). */
export function inventario(): LineaInventarioVista[] {
  const { lista, desde } = enviosVigentes();
  const cargas = lista.length
    ? db.select().from(t.enviosPartidas).where(inArray(t.enviosPartidas.envioId, lista.map((e) => e.id))).all()
    : [];
  const ventas = desde
    ? db
        .select({ productoId: t.ventasLocalesPartidas.productoId, cantidadBase: t.ventasLocalesPartidas.cantidadBase })
        .from(t.ventasLocalesPartidas)
        .innerJoin(t.ventasLocales, eq(t.ventasLocales.id, t.ventasLocalesPartidas.ventaId))
        .where(gte(t.ventasLocales.fechaOperativa, desde))
        .all()
    : [];
  const retornos = desde ? db.select().from(t.retornosLocales).where(gte(t.retornosLocales.fechaOperativa, desde)).all() : [];
  const lineas = inventarioCamioneta(cargas, ventas, retornos);
  const productos = new Map(db.select().from(t.productos).all().map((p) => [p.id, p]));
  return [...lineas.values()]
    .map((l) => {
      const p = productos.get(l.productoId);
      return { ...l, sku: p?.sku ?? '?', nombre: p?.nombre ?? 'Producto', unidad: p?.unidadBaseClave ?? '' };
    })
    .sort((a, b) => a.nombre.localeCompare(b.nombre));
}

export function existenciasCamioneta(): Map<string, number> {
  return new Map(inventario().map((l) => [l.productoId, l.existencia]));
}

export interface ResumenDia {
  ventas: number;
  totalVendido: number;
  efectivoEsperado: number;
  cargado: number;
  pendientes: number;
  rechazadas: number;
}

/** Mi día: totales en centavos. Efectivo esperado = ventas de contado en efectivo + cobros en efectivo (RN-LOG-08). */
export function resumenDia(fecha: string): ResumenDia {
  const ventas = db.select().from(t.ventasLocales).where(eq(t.ventasLocales.fechaOperativa, fecha)).all();
  const cobros = db.select().from(t.cobrosLocales).where(eq(t.cobrosLocales.fechaOperativa, fecha)).all();
  const ops = db.select({ estado: t.operacionesPendientes.estado }).from(t.operacionesPendientes).all();
  const inv = inventario();
  return {
    ventas: ventas.length,
    totalVendido: ventas.reduce((s, v) => s + v.total, 0),
    efectivoEsperado:
      ventas.filter((v) => v.condicionPago === 'Contado' && v.formaPago === 'Efectivo').reduce((s, v) => s + v.total, 0) +
      cobros.filter((c) => c.formaPago === 'Efectivo').reduce((s, c) => s + c.importe, 0),
    cargado: inv.reduce((s, l) => s + l.cargado, 0),
    pendientes: ops.filter((o) => o.estado === 'Pendiente' || o.estado === 'Enviando').length,
    rechazadas: ops.filter((o) => o.estado === 'Rechazada').length,
  };
}

export type ClienteRuta = typeof t.clientes.$inferSelect & {
  ordenVisita: number;
  diasVisita: number[];
  visitado: boolean;
  saldo: number;
};

/** Clientes de la ruta (los de hoy si se pasa la fecha) con marca de visitado. */
export function clientesRuta(fecha: string, soloHoy: boolean): ClienteRuta[] {
  const filas = db
    .select({ c: t.clientes, rc: t.rutasClientes })
    .from(t.clientes)
    .innerJoin(t.rutasClientes, eq(t.rutasClientes.clienteId, t.clientes.id))
    .where(and(eq(t.clientes.activo, true), eq(t.rutasClientes.activo, true)))
    .all();
  const visitados = new Set([
    ...db.select({ id: t.ventasLocales.clienteId }).from(t.ventasLocales).where(eq(t.ventasLocales.fechaOperativa, fecha)).all().map((v) => v.id),
    ...db.select({ id: t.retornosLocales.clienteId }).from(t.retornosLocales).where(eq(t.retornosLocales.fechaOperativa, fecha)).all().map((r) => r.id),
  ]);
  const saldos = new Map(db.select().from(t.saldosClientes).all().map((s) => [s.clienteId, s.saldo]));
  const clientes = filas.map(({ c, rc }) => ({
    ...c,
    ordenVisita: rc.ordenVisita,
    diasVisita: rc.diasVisita ?? [],
    visitado: visitados.has(c.id),
    saldo: saldos.get(c.id) ?? 0,
  }));
  return soloHoy
    ? clientesDelDia(clientes, fecha)
    : clientes.sort((a, b) => Number(a.esGenerico) - Number(b.esGenerico) || a.ordenVisita - b.ordenVisita);
}

/** La carga del pedido está en la camioneta si su envío ya bajó a la app (solo bajan los despachados). */
function conCarga<P extends { envioId: string | null; envioFolio: string | null }>(p: P): P & { carga: CargaDePedido } {
  const enCamioneta = !!p.envioId && !!db.select({ id: t.envios.id }).from(t.envios).where(eq(t.envios.id, p.envioId)).get();
  return { ...p, carga: { folio: p.envioFolio, enCamioneta } };
}

export function cliente(id: string) {
  const c = db.select().from(t.clientes).where(eq(t.clientes.id, id)).get();
  if (!c) return null;
  const saldo = db.select().from(t.saldosClientes).where(eq(t.saldosClientes.clienteId, id)).get()?.saldo ?? 0;
  const pedidos = db
    .select()
    .from(t.pedidos)
    .where(and(eq(t.pedidos.clienteId, id), inArray(t.pedidos.estado, ['Asignado', 'Confirmado', 'EntregadoParcial'])))
    .all()
    .map(conCarga);
  const ventas = db.select().from(t.ventasLocales).where(eq(t.ventasLocales.clienteId, id)).orderBy(desc(t.ventasLocales.creadaEn)).limit(10).all();
  return { ...c, saldo, pedidos, ventas };
}

export interface ProductoVenta {
  productoId: string;
  sku: string;
  nombre: string;
  tasaIva: number;
  tasaIeps: number | null;
  existencia: number;
  unidadBase: string;
  unidades: { unidadId: string; clave: string; factorABase: number; decimales: number; precio: number | null; predeterminada: boolean }[];
}

/** Productos vendibles con su precio de lista del cliente y la existencia de la camioneta. */
export function productosParaVenta(listaPrecioId: string): ProductoVenta[] {
  const productos = db.select().from(t.productos).where(eq(t.productos.activo, true)).all();
  const conversiones = db.select().from(t.productosUnidades).all();
  const lista = db.select().from(t.precios).where(eq(t.precios.listaPrecioId, listaPrecioId)).all();
  const existencias = existenciasCamioneta();
  return productos
    .map((p) => {
      const precio = (unidadId: string) => lista.find((x) => x.productoId === p.id && x.unidadId === unidadId)?.precioUnitario ?? null;
      const unidades = [
        { unidadId: p.unidadBaseId, clave: p.unidadBaseClave, factorABase: 1, decimales: p.unidadBaseDecimales, precio: precio(p.unidadBaseId), predeterminada: false },
        ...conversiones
          .filter((c) => c.productoId === p.id && c.usoVenta)
          .map((c) => ({ unidadId: c.unidadId, clave: c.unidadClave, factorABase: c.factorABase, decimales: c.decimales, precio: precio(c.unidadId), predeterminada: c.predeterminadaVenta })),
      ].filter((u) => u.precio != null);
      return {
        productoId: p.id, sku: p.sku, nombre: p.nombre, tasaIva: p.tasaIva, tasaIeps: p.tasaIeps,
        existencia: existencias.get(p.id) ?? 0, unidadBase: p.unidadBaseClave, unidades,
      };
    })
    .filter((p) => p.unidades.length > 0)
    .sort((a, b) => Number(b.existencia > 0) - Number(a.existencia > 0) || a.nombre.localeCompare(b.nombre));
}

export function pedido(id: string) {
  const p = db.select().from(t.pedidos).where(eq(t.pedidos.id, id)).get();
  if (!p) return null;
  const partidas = db.select().from(t.pedidosPartidas).where(eq(t.pedidosPartidas.pedidoId, id)).all();
  const productos = new Map(db.select().from(t.productos).all().map((x) => [x.id, x]));
  const unidades = new Map(db.select().from(t.productosUnidades).all().map((u) => [`${u.productoId}|${u.unidadId}`, u]));
  return {
    ...conCarga(p),
    partidas: partidas.map((x) => {
      const prod = productos.get(x.productoId);
      const unidad = unidades.get(`${x.productoId}|${x.unidadId}`);
      const factor = x.cantidad > 0 ? x.cantidadBase / x.cantidad : (unidad?.factorABase ?? 1);
      return {
        ...x,
        nombre: prod?.nombre ?? 'Producto',
        unidadClave: unidad?.unidadClave ?? prod?.unidadBaseClave ?? '',
        factorABase: factor,
        pendiente: Math.max(0, x.cantidad - x.cantidadEntregadaBase / (factor || 1)),
      };
    }),
  };
}

export function venta(id: string) {
  const v = db.select().from(t.ventasLocales).where(eq(t.ventasLocales.id, id)).get();
  if (!v) return null;
  const partidas = db.select().from(t.ventasLocalesPartidas).where(eq(t.ventasLocalesPartidas.ventaId, id)).all();
  const c = db.select().from(t.clientes).where(eq(t.clientes.id, v.clienteId)).get();
  const op = db.select().from(t.operacionesPendientes).where(eq(t.operacionesPendientes.documentoId, id)).get();
  return { ...v, partidas, cliente: c ?? null, estadoSync: op?.estado ?? 'Aplicada', errorSync: op?.error ?? null };
}

export function motivos(aplica: 'ruta' | 'retorno') {
  return db
    .select()
    .from(t.motivosMerma)
    .where(and(eq(t.motivosMerma.activo, true), aplica === 'ruta' ? eq(t.motivosMerma.aplicaRuta, true) : eq(t.motivosMerma.aplicaRetorno, true)))
    .all();
}

export function operaciones() {
  return db.select().from(t.operacionesPendientes).orderBy(desc(t.operacionesPendientes.secuencia)).limit(300).all();
}

export function nombresProductos(): Map<string, string> {
  return new Map(db.select().from(t.productos).all().map((p) => [p.id, p.nombre]));
}
