import {
  MAXIMO_OPERACIONES_LOTE,
  type EstadoDispositivoServidor,
  type OperacionSync,
  type RespuestaPull,
  type RespuestaPush,
  type SolicitudPush,
} from '@tg/shared';
import { consecutivoInicial } from '@/dominio/identificadores';
import { ErrorHttp, esReintentable, type AlmacenSync, type ClienteHttp, type OperacionLocal, type Reloj } from './puertos';

export interface OpcionesMotor {
  http: ClienteHttp;
  almacen: AlmacenSync;
  reloj: Reloj;
  versionApp: string;
  /** Genera UUID v7 (opId de ReportarEstado y loteId). */
  nuevoId: () => string;
  /** Reintentos ante red/5xx con retroceso exponencial (RN-SYN-04). */
  intentos?: number;
  esperaBaseMs?: number;
  limitePull?: number;
  bateria?: () => Promise<number | null>;
}

export interface ResumenSync {
  enviadas: number;
  aplicadas: number;
  duplicadas: number;
  rechazadas: number;
  cambiosRecibidos: number;
  pendientes: number;
  /** La sesión expiró: las operaciones se conservan y se pide iniciar sesión (05, sesión offline). */
  requiereLogin: boolean;
  error: string | null;
}

/** Parte una lista en lotes de máximo `tamano` (RN-SYN-07). */
export function enLotes<T>(lista: T[], tamano = MAXIMO_OPERACIONES_LOTE): T[][] {
  const lotes: T[][] = [];
  for (let i = 0; i < lista.length; i += tamano) lotes.push(lista.slice(i, i + tamano));
  return lotes;
}

function contarCambios(r: RespuestaPull): number {
  const c = r.cambios;
  return (
    c.productos.length +
    c.clientes.length +
    c.rutasClientes.length +
    c.precios.length +
    c.envios.length +
    c.pedidos.length +
    c.motivosMerma.length +
    c.parametros.length +
    (c.preciosCliente?.length ?? 0) +
    (c.productosExclusivos?.length ?? 0) +
    (c.cambiosPendientes?.length ?? 0) +
    Object.values(c.eliminados ?? {}).reduce((s, l) => s + (l?.length ?? 0), 0)
  );
}

/**
 * Motor de sincronización de la app de ruta: sube las operaciones pendientes (push idempotente por `opId`) y baja
 * cambios incrementales (pull por `cursor`). Nunca pierde una operación: si un lote no obtiene respuesta vuelve a
 * Pendiente; si el servidor la rechaza queda visible con su motivo y no se reintenta.
 */
export class MotorSync {
  private enCurso: Promise<ResumenSync> | null = null;
  private readonly intentos: number;
  private readonly esperaBaseMs: number;
  private readonly limitePull: number;

  constructor(private readonly o: OpcionesMotor) {
    this.intentos = o.intentos ?? 4;
    this.esperaBaseMs = o.esperaBaseMs ?? 1000;
    this.limitePull = o.limitePull ?? 500;
  }

  /** Sincronización completa; si ya hay una en curso devuelve la misma promesa (no se encimen disparadores). */
  sincronizar(): Promise<ResumenSync> {
    this.enCurso ??= this.ejecutar().finally(() => {
      this.enCurso = null;
    });
    return this.enCurso;
  }

  private async ejecutar(): Promise<ResumenSync> {
    const resumen: ResumenSync = {
      enviadas: 0,
      aplicadas: 0,
      duplicadas: 0,
      rechazadas: 0,
      cambiosRecibidos: 0,
      pendientes: 0,
      requiereLogin: false,
      error: null,
    };
    try {
      await this.verificarConsecutivo();
      await this.push(resumen);
      resumen.cambiosRecibidos = await this.pull();
      await this.reportarEstado();
    } catch (e) {
      if (e instanceof ErrorHttp && (e.estado === 401 || e.estado === 403)) {
        resumen.requiereLogin = e.estado === 401;
        resumen.error = e.estado === 401 ? 'Tu sesión terminó. Inicia sesión para sincronizar.' : e.message;
      } else {
        resumen.error = e instanceof Error ? e.message : 'No se pudo sincronizar.';
      }
    }
    resumen.pendientes = await this.o.almacen.contarPendientes();
    return resumen;
  }

  /** RN-SYN-08: al reinstalar se pide al servidor el último consecutivo del dispositivo. */
  async verificarConsecutivo(): Promise<void> {
    const estado = await this.o.almacen.leerEstado();
    if (estado.consecutivoVerificado) return;
    const servidor = await this.conReintentos(() => this.o.http.get<EstadoDispositivoServidor>('/api/v1/sync/dispositivo'));
    await this.o.almacen.fijarConsecutivo(consecutivoInicial(estado.consecutivoFolio, servidor.ultimoConsecutivo));
  }

  /** Sube las operaciones pendientes en lotes ≤ 200 en orden de secuencia (RN-SYN-01, RN-SYN-07). */
  async push(resumen?: ResumenSync): Promise<void> {
    const estado = await this.o.almacen.leerEstado();
    if (!estado.dispositivoId) return;
    const pendientes = await this.o.almacen.operacionesPorEnviar();
    for (const lote of enLotes(pendientes)) {
      const ids = lote.map((op) => op.opId);
      await this.o.almacen.marcarEnviando(ids);
      let respuesta: RespuestaPush;
      try {
        respuesta = await this.conReintentos(() =>
          this.o.http.post<RespuestaPush>('/api/v1/sync/push', this.solicitud(estado.dispositivoId!, lote)),
        );
      } catch (e) {
        await this.o.almacen.revertirEnvio(ids, e instanceof Error ? e.message : 'Sin conexión');
        throw e;
      }
      await this.o.almacen.registrarResultados(respuesta.resultados, this.o.reloj.ahora().toISOString());
      if (resumen) {
        resumen.enviadas += lote.length;
        for (const r of respuesta.resultados) {
          if (r.estado === 'Aplicada') resumen.aplicadas++;
          else if (r.estado === 'Duplicada') resumen.duplicadas++;
          else resumen.rechazadas++;
        }
      }
    }
  }

  /**
   * Baja cambios con `version_sync > cursor` hasta que `hayMas` sea falso. Devuelve cuántos registros llegaron. Si el
   * servidor responde `rutaCambiada` (RN-SYN-09) el cursor vuelve a 0 y se baja todo de la ruta nueva.
   */
  async pull(): Promise<number> {
    let total = 0;
    for (let vuelta = 0; vuelta < 1000; vuelta++) {
      const { cursor, rutaId } = await this.o.almacen.leerEstado();
      const ruta = rutaId ? `&rutaId=${rutaId}` : '';
      const respuesta = await this.conReintentos(() =>
        this.o.http.get<RespuestaPull>(`/api/v1/sync/pull?desde=${cursor}&limite=${this.limitePull}${ruta}`),
      );
      await this.o.almacen.aplicarCambios(respuesta);
      total += contarCambios(respuesta);
      if (respuesta.rutaCambiada && cursor > 0) continue;
      if (!respuesta.hayMas) break;
    }
    return total;
  }

  /** ReportarEstado: pendientes, batería, versión y consecutivo (RN-LOG-09 lo usa para el cierre de ruta). */
  async reportarEstado(): Promise<void> {
    const estado = await this.o.almacen.leerEstado();
    if (!estado.dispositivoId) return;
    const operacion: OperacionSync = {
      opId: this.o.nuevoId(),
      secuencia: await this.o.almacen.siguienteSecuencia(),
      tipo: 'ReportarEstado',
      creadaEn: this.o.reloj.ahora().toISOString(),
      datos: {
        pendientes: await this.o.almacen.contarPendientes(),
        bateria: (await this.o.bateria?.()) ?? null,
        versionApp: this.o.versionApp,
        ultimoConsecutivo: estado.consecutivoFolio,
      },
    };
    await this.conReintentos(() =>
      this.o.http.post<RespuestaPush>('/api/v1/sync/push', {
        dispositivoId: estado.dispositivoId!,
        loteId: this.o.nuevoId(),
        versionApp: this.o.versionApp,
        operaciones: [operacion],
      } satisfies SolicitudPush),
    );
  }

  private solicitud(dispositivoId: string, lote: OperacionLocal[]): SolicitudPush {
    return {
      dispositivoId,
      loteId: this.o.nuevoId(),
      versionApp: this.o.versionApp,
      operaciones: lote.map((op) => ({
        opId: op.opId,
        secuencia: op.secuencia,
        tipo: op.tipo,
        creadaEn: op.creadaEn,
        datos: op.datos as OperacionSync['datos'],
      })),
    };
  }

  /** Reintenta con retroceso exponencial (1 s, 2 s, 4 s…) solo errores de red o 5xx. */
  private async conReintentos<T>(fn: () => Promise<T>): Promise<T> {
    for (let intento = 1; ; intento++) {
      try {
        return await fn();
      } catch (e) {
        if (!esReintentable(e) || intento >= this.intentos) throw e;
        await this.o.reloj.dormir(this.esperaBaseMs * 2 ** (intento - 1));
      }
    }
  }
}
