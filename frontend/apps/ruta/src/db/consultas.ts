import { desglosarCajas, formatoCajas, type ConversionCajas } from '@tg/shared';
import { and, desc, eq, gte, inArray } from 'drizzle-orm';
import { precioVigente, productoPermitido } from '@/dominio/pedidos';
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
  /** RN-CAT-09: existencia en cajas + paquetes; `null` si el producto no tiene caja. */
  cajas: string | null;
}

/** Conversiones por producto con la unidad base incluida (para el desglose en cajas, RN-CAT-09). */
function conversionesPorProducto(): Map<string, ConversionCajas[]> {
  const mapa = new Map<string, ConversionCajas[]>();
  for (const c of db.select().from(t.productosUnidades).all()) {
    const lista = mapa.get(c.productoId) ?? [];
    lista.push({ unidadClave: c.unidadClave, factorABase: c.factorABase, predeterminadaVenta: c.predeterminadaVenta, predeterminadaTraslado: c.predeterminadaTraslado });
    mapa.set(c.productoId, lista);
  }
  return mapa;
}

/** Inventario de camioneta: carga recibida − ventas locales + retornos vendibles locales − cambios entregados (06). */
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
  const cambios = desde ? db.select().from(t.cambiosLocales).where(gte(t.cambiosLocales.fechaOperativa, desde)).all() : [];
  const lineas = inventarioCamioneta(cargas, ventas, retornos, cambios);
  const productos = new Map(db.select().from(t.productos).all().map((p) => [p.id, p]));
  const conversiones = conversionesPorProducto();
  return [...lineas.values()]
    .map((l) => {
      const p = productos.get(l.productoId);
      const d = desglosarCajas(l.existencia, conversiones.get(l.productoId) ?? [], p?.unidadBaseClave ?? '');
      return {
        ...l, sku: p?.sku ?? '?', nombre: p?.nombreCorto || p?.nombre || 'Producto', unidad: p?.unidadBaseClave ?? '',
        cajas: d ? formatoCajas(d) : null,
      };
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
  /** (v0.2) Borradores de pedido sin enviar (RN-APP-11). */
  pedidosPorEnviar: number;
  /** (v0.2) Cambios físicos con producto entregado hoy. */
  cambiosEntregados: number;
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
    pedidosPorEnviar: db.select({ id: t.pedidosLocales.id }).from(t.pedidosLocales).where(eq(t.pedidosLocales.estado, 'Borrador')).all().length,
    cambiosEntregados: new Set(
      db
        .select({ id: t.cambiosLocales.retornoId })
        .from(t.cambiosLocales)
        .where(and(eq(t.cambiosLocales.fechaOperativa, fecha), eq(t.cambiosLocales.sentido, 'Entregado')))
        .all()
        .map((c) => c.id),
    ).size,
  };
}

/** (v0.2) Cargas de la ruta que bajaron al teléfono (Mi día, RN-SYN-09). */
export function cargas() {
  return db.select().from(t.envios).orderBy(desc(t.envios.fechaOperativa)).all();
}

/** (v0.2) Canal de la ruta del alcance; decide si hay "Pedido de la ruta" (RN-VEN-16). */
export function canalRuta() {
  return db.select().from(t.canales).get() ?? null;
}

export function clienteGenerico() {
  return db.select().from(t.clientes).where(and(eq(t.clientes.esGenerico, true), eq(t.clientes.activo, true))).get() ?? null;
}

/** RN-APP-09: promedio del cliente por producto (unidad base) y los umbrales de la ruta. */
export function referenciaCantidades(clienteId: string) {
  return {
    promedios: new Map(
      db.select().from(t.consumosClientes).where(eq(t.consumosClientes.clienteId, clienteId)).all().map((c) => [c.productoId, c.promedioBase]),
    ),
    factor: parametro('ruta.alerta_cantidad_factor', 3),
    maxima: parametro('ruta.alerta_cantidad_maxima', 500),
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
    ...db.select({ id: t.cambiosLocales.clienteId }).from(t.cambiosLocales).where(eq(t.cambiosLocales.fechaOperativa, fecha)).all().map((r) => r.id),
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
  return { ...c, saldo, pedidos, ventas, cambiosPendientes: cambiosPendientesCliente(id), diasVisita: diasVisita(id) };
}

function diasVisita(clienteId: string): number[] {
  return db.select().from(t.rutasClientes).where(eq(t.rutasClientes.clienteId, clienteId)).get()?.diasVisita ?? [];
}

/**
 * (v0.2) Cambios pendientes del cliente (RN-LOG-16) menos lo que ya se saldó en este teléfono y aún no baja del
 * servidor.
 */
export function cambiosPendientesCliente(clienteId: string) {
  const pendientes = db.select().from(t.cambiosPendientes).where(eq(t.cambiosPendientes.clienteId, clienteId)).all();
  if (pendientes.length === 0) return [];
  const saldadoLocal = new Map<string, number>();
  for (const c of db.select().from(t.cambiosLocales).where(eq(t.cambiosLocales.clienteId, clienteId)).all()) {
    if (c.cambioPendienteId) saldadoLocal.set(c.cambioPendienteId, (saldadoLocal.get(c.cambioPendienteId) ?? 0) + c.cantidadBase);
  }
  const productos = new Map(db.select().from(t.productos).all().map((p) => [p.id, p]));
  return pendientes
    .map((p) => {
      const prod = productos.get(p.productoId);
      const restante = Math.round((p.cantidadBase - p.cantidadSaldadaBase - (saldadoLocal.get(p.id) ?? 0)) * 10_000) / 10_000;
      return { ...p, restante, nombre: prod?.nombreCorto || prod?.nombre || 'Producto', unidad: prod?.unidadBaseClave ?? '' };
    })
    .filter((p) => p.restante > 0)
    .sort((a, b) => a.fechaOperativa.localeCompare(b.fechaOperativa));
}

export interface ProductoVenta {
  productoId: string;
  sku: string;
  nombre: string;
  /** (v0.2) `nombre_corto` si lo tiene. */
  nombreCorto: string;
  tasaIva: number;
  tasaIeps: number | null;
  existencia: number;
  unidadBase: string;
  unidades: {
    unidadId: string;
    clave: string;
    factorABase: number;
    decimales: number;
    precio: number | null;
    predeterminada: boolean;
    /** RN-VEN-12: el precio es el especial del cliente. */
    especial: boolean;
  }[];
  /** Para "= N cajas + M" (RN-CAT-09). */
  conversiones: ConversionCajas[];
}

/**
 * Productos que se le ofrecen al cliente: con precio (especial vigente o de su lista, RN-VEN-12), sin los exclusivos
 * de otros clientes (RN-VEN-14) y con la existencia de la camioneta.
 */
export function productosParaVenta(
  cl: { id: string; listaPrecioId: string; grupoClienteId: string | null },
  fecha: string,
): ProductoVenta[] {
  const productos = db.select().from(t.productos).where(eq(t.productos.activo, true)).all();
  const conversiones = db.select().from(t.productosUnidades).all();
  const lista = db.select().from(t.precios).where(eq(t.precios.listaPrecioId, cl.listaPrecioId)).all();
  const especiales = db.select().from(t.preciosCliente).where(eq(t.preciosCliente.clienteId, cl.id)).all();
  const exclusivos = db.select().from(t.productosExclusivos).all();
  const cajas = conversionesPorProducto();
  const existencias = existenciasCamioneta();
  return productos
    .filter((p) => productoPermitido(p.id, cl, exclusivos))
    .map((p) => {
      const deLista = (unidadId: string) => lista.find((x) => x.productoId === p.id && x.unidadId === unidadId)?.precioUnitario ?? null;
      const vigente = (unidadId: string) => precioVigente(p.id, unidadId, fecha, especiales, deLista(unidadId));
      const unidades = [
        { unidadId: p.unidadBaseId, clave: p.unidadBaseClave, factorABase: 1, decimales: p.unidadBaseDecimales, predeterminada: false, ...unidad(vigente(p.unidadBaseId)) },
        ...conversiones
          .filter((c) => c.productoId === p.id && c.usoVenta)
          .map((c) => ({
            unidadId: c.unidadId, clave: c.unidadClave, factorABase: c.factorABase, decimales: c.decimales, predeterminada: c.predeterminadaVenta,
            ...unidad(vigente(c.unidadId)),
          })),
      ].filter((u) => u.precio != null);
      return {
        productoId: p.id, sku: p.sku, nombre: p.nombre, nombreCorto: p.nombreCorto || p.nombre, tasaIva: p.tasaIva, tasaIeps: p.tasaIeps,
        existencia: existencias.get(p.id) ?? 0, unidadBase: p.unidadBaseClave, unidades, conversiones: cajas.get(p.id) ?? [],
      };
    })
    .filter((p) => p.unidades.length > 0)
    .sort((a, b) => Number(b.existencia > 0) - Number(a.existencia > 0) || a.nombre.localeCompare(b.nombre));
}

const unidad = (v: { precio: number | null; especial: boolean }) => ({ precio: v.precio, especial: v.especial });

/** (v0.2) Pedidos capturados en este teléfono con el estado de su operación (Pedidos). */
export function pedidosLocales() {
  const pedidos = db.select().from(t.pedidosLocales).orderBy(desc(t.pedidosLocales.creadoEn)).all();
  const partidas = db.select().from(t.pedidosLocalesPartidas).all();
  const clientes = new Map(db.select().from(t.clientes).all().map((c) => [c.id, c.nombre]));
  const ops = new Map(
    db
      .select()
      .from(t.operacionesPendientes)
      .where(eq(t.operacionesPendientes.tipo, 'CapturarPedido'))
      .all()
      .map((o) => [o.documentoId, o]),
  );
  return pedidos.map((p) => {
    const op = ops.get(p.id);
    return {
      ...p,
      cliente: clientes.get(p.clienteId) ?? 'Cliente fuera de la ruta',
      partidas: partidas.filter((x) => x.pedidoId === p.id),
      estadoOperacion: op?.estado ?? null,
      error: op?.error ?? null,
    };
  });
}

export function pedidoLocal(id: string) {
  const p = db.select().from(t.pedidosLocales).where(eq(t.pedidosLocales.id, id)).get();
  if (!p) return null;
  return { ...p, partidas: db.select().from(t.pedidosLocalesPartidas).where(eq(t.pedidosLocalesPartidas.pedidoId, id)).all() };
}

export function pedido(id: string) {
  const p = db.select().from(t.pedidos).where(eq(t.pedidos.id, id)).get();
  if (!p) return null;
  // RN-VEN-17: las partidas CambioFisico no se entregan con venta (van con un cambio físico).
  const todas = db.select().from(t.pedidosPartidas).where(eq(t.pedidosPartidas.pedidoId, id)).all();
  const partidas = todas.filter((x) => x.tipo !== 'CambioFisico');
  const cambios = todas.filter((x) => x.tipo === 'CambioFisico');
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
    cambios: cambios.map((x) => ({ ...x, nombre: productos.get(x.productoId)?.nombre ?? 'Producto' })),
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
