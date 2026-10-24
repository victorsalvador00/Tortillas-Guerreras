import type { RespuestaPull, ResultadoOperacion, TipoOperacion } from '@tg/shared';
import { and, eq, inArray, lt } from 'drizzle-orm';
import type { AlmacenSync, EstadoSyncLocal, OperacionLocal } from '@/sync/puertos';
import { db, type Ejecutor } from './cliente';
import * as t from './esquema';

/**
 * Implementación del almacén del motor de sync sobre SQLite + Drizzle. Cada página del pull se aplica en una
 * transacción local junto con su cursor.
 */
export class RepositorioSync implements AlmacenSync {
  async leerEstado(): Promise<EstadoSyncLocal> {
    const e = asegurarEstado(db);
    return {
      dispositivoId: e.dispositivoId,
      cursor: e.cursorSync,
      ultimoSync: e.ultimoSync,
      consecutivoFolio: e.consecutivoFolio,
      consecutivoVerificado: e.consecutivoVerificado,
      rutaId: e.rutaId,
    };
  }

  async aplicarCambios(r: RespuestaPull): Promise<void> {
    db.transaction((tx) => {
      const c = r.cambios;
      // RN-SYN-09: con la ruta nueva se borra lo de la anterior; la cola, las ventas locales y el consecutivo se quedan.
      if (r.rutaCambiada) borrarColeccionesDeRuta(tx);
      for (const p of c.productos) {
        const fila = {
          id: p.id, sku: p.sku, nombre: p.nombre, tipo: p.tipo, unidadBaseId: p.unidadBaseId, unidadBaseClave: p.unidadBaseClave,
          unidadBaseDecimales: p.unidadBaseDecimales, manejaLote: p.manejaLote, tasaIva: p.tasaIva, tasaIeps: p.tasaIeps,
          activo: p.activo, versionSync: p.versionSync, nombreCorto: p.nombreCorto ?? null,
        };
        tx.insert(t.productos).values(fila).onConflictDoUpdate({ target: t.productos.id, set: fila }).run();
        tx.delete(t.productosUnidades).where(eq(t.productosUnidades.productoId, p.id)).run();
        for (const u of p.conversiones) {
          tx.insert(t.productosUnidades)
            .values({ productoId: p.id, ...u, usoTraslado: u.usoTraslado ?? false, predeterminadaTraslado: u.predeterminadaTraslado ?? false })
            .run();
        }
      }
      for (const cl of c.clientes) {
        const fila = { ...cl, grupoClienteId: cl.grupoClienteId ?? null };
        tx.insert(t.clientes).values(fila).onConflictDoUpdate({ target: t.clientes.id, set: fila }).run();
      }
      for (const rc of c.rutasClientes) {
        tx.insert(t.rutasClientes).values(rc).onConflictDoUpdate({ target: t.rutasClientes.clienteId, set: rc }).run();
      }
      for (const p of c.precios) {
        tx.insert(t.precios)
          .values(p)
          .onConflictDoUpdate({ target: [t.precios.listaPrecioId, t.precios.productoId, t.precios.unidadId], set: p })
          .run();
      }
      for (const e of c.envios) {
        const { partidas, ...envio } = e;
        tx.insert(t.envios).values(envio).onConflictDoUpdate({ target: t.envios.id, set: envio }).run();
        tx.delete(t.enviosPartidas).where(eq(t.enviosPartidas.envioId, e.id)).run();
        for (const p of partidas) tx.insert(t.enviosPartidas).values({ ...p, envioId: e.id }).run();
      }
      for (const pe of c.pedidos) {
        const { partidas, ...pedido } = pe;
        tx.insert(t.pedidos).values(pedido).onConflictDoUpdate({ target: t.pedidos.id, set: pedido }).run();
        tx.delete(t.pedidosPartidas).where(eq(t.pedidosPartidas.pedidoId, pe.id)).run();
        for (const p of partidas) tx.insert(t.pedidosPartidas).values({ ...p, pedidoId: pe.id }).run();
      }
      // Los saldos viajan completos: se reemplazan.
      if (c.saldosClientes.length > 0) {
        tx.delete(t.saldosClientes).run();
        for (const s of c.saldosClientes) tx.insert(t.saldosClientes).values(s).run();
      }
      for (const m of c.motivosMerma) {
        tx.insert(t.motivosMerma).values(m).onConflictDoUpdate({ target: t.motivosMerma.id, set: m }).run();
      }
      for (const p of c.parametros) {
        tx.insert(t.parametros).values(p).onConflictDoUpdate({ target: t.parametros.clave, set: p }).run();
      }
      for (const { activo, ...p } of c.preciosCliente ?? []) {
        const llave = and(eq(t.preciosCliente.clienteId, p.clienteId), eq(t.preciosCliente.productoId, p.productoId), eq(t.preciosCliente.unidadId, p.unidadId));
        if (!activo) {
          tx.delete(t.preciosCliente).where(llave).run();
          continue;
        }
        tx.insert(t.preciosCliente)
          .values(p)
          .onConflictDoUpdate({ target: [t.preciosCliente.clienteId, t.preciosCliente.productoId, t.preciosCliente.unidadId], set: p })
          .run();
      }
      for (const x of c.productosExclusivos ?? []) {
        tx.insert(t.productosExclusivos).values(x).onConflictDoUpdate({ target: t.productosExclusivos.id, set: x }).run();
      }
      for (const x of c.cambiosPendientes ?? []) {
        tx.insert(t.cambiosPendientes).values(x).onConflictDoUpdate({ target: t.cambiosPendientes.id, set: x }).run();
      }
      // Consumos y canal viajan completos (no incrementales): se reemplazan.
      if (c.consumosClientes) {
        tx.delete(t.consumosClientes).run();
        for (const x of c.consumosClientes) tx.insert(t.consumosClientes).values(x).run();
      }
      if (c.canales) {
        tx.delete(t.canales).run();
        for (const x of c.canales) tx.insert(t.canales).values(x).run();
      }

      const el = c.eliminados ?? {};
      if (el.productos?.length) {
        tx.delete(t.productos).where(inArray(t.productos.id, el.productos)).run();
        tx.delete(t.productosUnidades).where(inArray(t.productosUnidades.productoId, el.productos)).run();
      }
      if (el.clientes?.length) tx.delete(t.clientes).where(inArray(t.clientes.id, el.clientes)).run();
      if (el.rutasClientes?.length) tx.delete(t.rutasClientes).where(inArray(t.rutasClientes.clienteId, el.rutasClientes)).run();
      if (el.envios?.length) {
        tx.delete(t.envios).where(inArray(t.envios.id, el.envios)).run();
        tx.delete(t.enviosPartidas).where(inArray(t.enviosPartidas.envioId, el.envios)).run();
      }
      if (el.pedidos?.length) {
        tx.delete(t.pedidos).where(inArray(t.pedidos.id, el.pedidos)).run();
        tx.delete(t.pedidosPartidas).where(inArray(t.pedidosPartidas.pedidoId, el.pedidos)).run();
      }
      if (el.motivosMerma?.length) tx.delete(t.motivosMerma).where(inArray(t.motivosMerma.id, el.motivosMerma)).run();
      if (el.productosExclusivos?.length) {
        tx.delete(t.productosExclusivos).where(inArray(t.productosExclusivos.id, el.productosExclusivos)).run();
      }
      if (el.cambiosPendientes?.length) {
        tx.delete(t.cambiosPendientes).where(inArray(t.cambiosPendientes.id, el.cambiosPendientes)).run();
      }

      asegurarEstado(tx);
      // Sin carga ni ruta predeterminada el servidor manda `ruta = null`: "No tienes ruta asignada hoy".
      const ruta = r.ruta;
      tx.update(t.estadoDispositivo)
        .set({
          cursorSync: r.cursor,
          ultimoSync: r.fechaServidor,
          rutaId: ruta?.id ?? null,
          rutaCodigo: ruta?.codigo ?? null,
          rutaNombre: ruta?.nombre ?? null,
          almacenId: ruta?.almacenId ?? null,
        })
        .where(eq(t.estadoDispositivo.id, 1))
        .run();
    });
  }

  async operacionesPorEnviar(): Promise<OperacionLocal[]> {
    const filas = db
      .select()
      .from(t.operacionesPendientes)
      .where(inArray(t.operacionesPendientes.estado, ['Pendiente', 'Enviando']))
      .orderBy(t.operacionesPendientes.secuencia)
      .all();
    return filas.map((f) => ({
      opId: f.opId,
      secuencia: f.secuencia,
      tipo: f.tipo as TipoOperacion,
      datos: JSON.parse(f.datosJson) as unknown,
      creadaEn: f.creadaEn,
      estado: f.estado,
      intentos: f.intentos,
    }));
  }

  async marcarEnviando(opIds: string[]): Promise<void> {
    db.update(t.operacionesPendientes).set({ estado: 'Enviando' }).where(inArray(t.operacionesPendientes.opId, opIds)).run();
  }

  async revertirEnvio(opIds: string[], error: string): Promise<void> {
    db.transaction((tx) => {
      for (const id of opIds) {
        const op = tx.select().from(t.operacionesPendientes).where(eq(t.operacionesPendientes.opId, id)).get();
        if (op) {
          tx.update(t.operacionesPendientes)
            .set({ estado: 'Pendiente', intentos: op.intentos + 1, error })
            .where(eq(t.operacionesPendientes.opId, id))
            .run();
        }
      }
    });
  }

  async registrarResultados(resultados: ResultadoOperacion[], ahora: string): Promise<void> {
    db.transaction((tx) => {
      for (const r of resultados) {
        if (r.estado === 'Rechazada') {
          const op = tx.select().from(t.operacionesPendientes).where(eq(t.operacionesPendientes.opId, r.opId)).get();
          if (op?.tipo === 'CapturarPedido' && op.documentoId) {
            tx.update(t.pedidosLocales).set({ estado: 'Rechazado' }).where(eq(t.pedidosLocales.id, op.documentoId)).run();
          }
        }
        tx.update(t.operacionesPendientes)
          .set({
            estado: r.estado === 'Rechazada' ? 'Rechazada' : 'Aplicada',
            error: r.estado === 'Rechazada' ? `${r.codigo ? `${r.codigo}: ` : ''}${r.mensaje ?? 'Rechazada por el servidor.'}` : null,
            resueltaEn: ahora,
          })
          .where(eq(t.operacionesPendientes.opId, r.opId))
          .run();
      }
    });
  }

  async contarPendientes(): Promise<number> {
    return db
      .select({ opId: t.operacionesPendientes.opId })
      .from(t.operacionesPendientes)
      .where(inArray(t.operacionesPendientes.estado, ['Pendiente', 'Enviando']))
      .all().length;
  }

  async siguienteSecuencia(): Promise<number> {
    return db.transaction((tx) => tomarSecuencia(tx));
  }

  async fijarConsecutivo(consecutivo: number): Promise<void> {
    asegurarEstado(db);
    db.update(t.estadoDispositivo)
      .set({ consecutivoFolio: consecutivo, consecutivoVerificado: true })
      .where(eq(t.estadoDispositivo.id, 1))
      .run();
  }

  async limpiarAplicadas(antesDe: string): Promise<number> {
    const viejas = db
      .select({ opId: t.operacionesPendientes.opId })
      .from(t.operacionesPendientes)
      .where(and(eq(t.operacionesPendientes.estado, 'Aplicada'), lt(t.operacionesPendientes.creadaEn, antesDe)))
      .all();
    if (viejas.length > 0) {
      db.delete(t.operacionesPendientes).where(inArray(t.operacionesPendientes.opId, viejas.map((v) => v.opId))).run();
      // Las ventas, retornos y cobros locales de esos días ya no se necesitan para el inventario ni el historial.
      db.delete(t.ventasLocales).where(lt(t.ventasLocales.creadaEn, antesDe)).run();
      db.delete(t.retornosLocales).where(lt(t.retornosLocales.creadaEn, antesDe)).run();
      db.delete(t.cobrosLocales).where(lt(t.cobrosLocales.creadaEn, antesDe)).run();
      db.delete(t.cambiosLocales).where(lt(t.cambiosLocales.creadaEn, antesDe)).run();
      const enviados = db
        .select({ id: t.pedidosLocales.id })
        .from(t.pedidosLocales)
        .where(and(eq(t.pedidosLocales.estado, 'Enviado'), lt(t.pedidosLocales.creadoEn, antesDe)))
        .all()
        .map((p) => p.id);
      if (enviados.length > 0) {
        db.delete(t.pedidosLocalesPartidas).where(inArray(t.pedidosLocalesPartidas.pedidoId, enviados)).run();
        db.delete(t.pedidosLocales).where(inArray(t.pedidosLocales.id, enviados)).run();
      }
    }
    return viejas.length;
  }
}

/** Colecciones que dependen de la ruta del alcance (RN-SYN-09). Catálogos, exclusivos y lo propio de la app se quedan. */
function borrarColeccionesDeRuta(ex: Ejecutor) {
  for (const tabla of [
    t.clientes, t.rutasClientes, t.precios, t.preciosCliente, t.envios, t.enviosPartidas, t.pedidos, t.pedidosPartidas,
    t.saldosClientes, t.cambiosPendientes, t.consumosClientes, t.canales,
  ]) {
    ex.delete(tabla).run();
  }
}

/** La fila única de `estado_dispositivo`. */
export function asegurarEstado(ex: Ejecutor) {
  const actual = ex.select().from(t.estadoDispositivo).where(eq(t.estadoDispositivo.id, 1)).get();
  if (actual) return actual;
  ex.insert(t.estadoDispositivo).values({ id: 1 }).run();
  return ex.select().from(t.estadoDispositivo).where(eq(t.estadoDispositivo.id, 1)).get()!;
}

export function tomarSecuencia(ex: Ejecutor): number {
  const e = asegurarEstado(ex);
  const siguiente = e.secuenciaOperacion + 1;
  ex.update(t.estadoDispositivo).set({ secuenciaOperacion: siguiente }).where(eq(t.estadoDispositivo.id, 1)).run();
  return siguiente;
}

/** Siguiente consecutivo de folio (RN-SYN-08: lo lleva la app y nunca se reinicia). */
export function tomarConsecutivo(ex: Ejecutor): number {
  const e = asegurarEstado(ex);
  const siguiente = e.consecutivoFolio + 1;
  ex.update(t.estadoDispositivo).set({ consecutivoFolio: siguiente }).where(eq(t.estadoDispositivo.id, 1)).run();
  return siguiente;
}

export function fijarDispositivo(dispositivoId: string, codigo: string | null): void {
  asegurarEstado(db);
  db.update(t.estadoDispositivo).set({ dispositivoId, codigo }).where(eq(t.estadoDispositivo.id, 1)).run();
}
