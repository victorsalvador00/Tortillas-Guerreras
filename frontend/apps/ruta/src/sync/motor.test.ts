import type { RespuestaPull, ResultadoOperacion, SolicitudPush } from '@tg/shared';
import { describe, expect, it } from 'vitest';
import { MotorSync, enLotes } from './motor';
import { ErrorHttp, type AlmacenSync, type ClienteHttp, type EstadoSyncLocal, type OperacionLocal } from './puertos';

/** Almacén local en memoria con la misma semántica que el repositorio SQLite. */
class AlmacenMemoria implements AlmacenSync {
  estado: EstadoSyncLocal = { dispositivoId: 'disp-1', cursor: 0, ultimoSync: null, consecutivoFolio: 0, consecutivoVerificado: true };
  ops: (OperacionLocal & { error?: string })[] = [];
  aplicados: RespuestaPull[] = [];
  secuencia = 0;

  async leerEstado() {
    return { ...this.estado };
  }
  async aplicarCambios(r: RespuestaPull) {
    this.aplicados.push(r);
    this.estado.cursor = r.cursor;
    this.estado.ultimoSync = r.fechaServidor;
  }
  async operacionesPorEnviar() {
    return this.ops.filter((o) => o.estado === 'Pendiente' || o.estado === 'Enviando').sort((a, b) => a.secuencia - b.secuencia);
  }
  async marcarEnviando(ids: string[]) {
    this.ops.filter((o) => ids.includes(o.opId)).forEach((o) => (o.estado = 'Enviando'));
  }
  async revertirEnvio(ids: string[], error: string) {
    this.ops.filter((o) => ids.includes(o.opId)).forEach((o) => {
      o.estado = 'Pendiente';
      o.intentos++;
      o.error = error;
    });
  }
  async registrarResultados(resultados: ResultadoOperacion[]) {
    for (const r of resultados) {
      const op = this.ops.find((o) => o.opId === r.opId);
      if (!op) continue;
      op.estado = r.estado === 'Rechazada' ? 'Rechazada' : 'Aplicada';
      op.error = r.mensaje ?? undefined;
    }
  }
  async contarPendientes() {
    return this.ops.filter((o) => o.estado === 'Pendiente' || o.estado === 'Enviando').length;
  }
  async siguienteSecuencia() {
    return ++this.secuencia;
  }
  async fijarConsecutivo(c: number) {
    this.estado.consecutivoFolio = c;
    this.estado.consecutivoVerificado = true;
  }
  async limpiarAplicadas() {
    return 0;
  }

  agregar(n: number) {
    for (let i = 0; i < n; i++) {
      const s = ++this.secuencia;
      this.ops.push({ opId: `op-${s}`, secuencia: s, tipo: 'RegistrarVenta', datos: { n: s }, creadaEn: '2026-11-03T15:00:00-06:00', estado: 'Pendiente', intentos: 0 });
    }
  }
}

/** Servidor simulado: idempotente por opId (RN-SYN-02), rechaza las operaciones marcadas. */
class ServidorFalso implements ClienteHttp {
  aplicadas = new Set<string>();
  lotes: SolicitudPush[] = [];
  rechazar = new Set<string>();
  fallosRed = 0;
  paginas: RespuestaPull[] = [];
  consultasPull: string[] = [];
  ultimoConsecutivo = 0;

  async get<T>(ruta: string): Promise<T> {
    if (ruta.startsWith('/api/v1/sync/dispositivo')) {
      return { dispositivoId: 'disp-1', codigo: 'D03', activo: true, rutaId: 'r', rutaCodigo: 'R01', ultimoConsecutivo: this.ultimoConsecutivo } as T;
    }
    this.consultasPull.push(ruta);
    return (this.paginas.shift() ?? pagina(0, false)) as T;
  }

  async post<T>(_ruta: string, cuerpo: unknown): Promise<T> {
    if (this.fallosRed > 0) {
      this.fallosRed--;
      throw new ErrorHttp(0, 'Sin conexión');
    }
    const s = cuerpo as SolicitudPush;
    this.lotes.push(s);
    const resultados: ResultadoOperacion[] = s.operaciones.map((op) => {
      if (this.rechazar.has(op.opId)) return { opId: op.opId, estado: 'Rechazada', codigo: 'VEN-009', mensaje: 'Folio duplicado.' };
      if (this.aplicadas.has(op.opId)) return { opId: op.opId, estado: 'Duplicada' };
      this.aplicadas.add(op.opId);
      return { opId: op.opId, estado: 'Aplicada' };
    });
    return { resultados, pendientesServidor: 0 } as T;
  }
}

function pagina(cursor: number, hayMas: boolean, productos = 0): RespuestaPull {
  return {
    cursor,
    hayMas,
    fechaServidor: '2026-11-03T21:00:00Z',
    ruta: { id: 'r', codigo: 'R01', nombre: 'Ruta 1', almacenId: 'a' },
    cambios: {
      productos: Array.from({ length: productos }, (_, i) => ({
        id: `p${cursor}-${i}`, sku: 'X', nombre: 'X', tipo: 'Terminado', unidadBaseId: 'u', unidadBaseClave: 'KG',
        unidadBaseDecimales: 3, manejaLote: true, tasaIva: 0, tasaIeps: null, activo: true, versionSync: cursor, conversiones: [],
      })),
      clientes: [], rutasClientes: [], precios: [], envios: [], pedidos: [], saldosClientes: [], motivosMerma: [], parametros: [], preciosCliente: [], productosExclusivos: [],
      eliminados: {},
    },
  };
}

function crearMotor(almacen: AlmacenMemoria, servidor: ServidorFalso, esperas: number[] = []) {
  let n = 0;
  return new MotorSync({
    http: servidor,
    almacen,
    versionApp: '1.0.0',
    nuevoId: () => `id-${++n}`,
    reloj: { ahora: () => new Date('2026-11-03T21:00:00Z'), dormir: async (ms) => void esperas.push(ms) },
  });
}

describe('motor de sincronización', () => {
  it('RN-SYN-07: parte las operaciones en lotes de máximo 200', () => {
    expect(enLotes(Array.from({ length: 450 }, (_, i) => i)).map((l) => l.length)).toEqual([200, 200, 50]);
  });

  it('RN-SYN-07 / RN-SYN-01: envía 450 operaciones en tres lotes ordenados por secuencia', async () => {
    const almacen = new AlmacenMemoria();
    const servidor = new ServidorFalso();
    almacen.agregar(450);
    const r = await crearMotor(almacen, servidor).sincronizar();

    const lotesVenta = servidor.lotes.filter((l) => l.operaciones[0].tipo !== 'ReportarEstado');
    expect(lotesVenta.map((l) => l.operaciones.length)).toEqual([200, 200, 50]);
    const secuencias = lotesVenta.flatMap((l) => l.operaciones.map((o) => o.secuencia));
    expect(secuencias).toEqual([...secuencias].sort((a, b) => a - b));
    expect(r.aplicadas).toBe(450);
    expect(r.pendientes).toBe(0);
  });

  it('Criterio 06: 20 ventas en modo avión se envían al recuperar red y Pendientes queda en cero', async () => {
    const almacen = new AlmacenMemoria();
    const servidor = new ServidorFalso();
    almacen.agregar(20);
    // Agota los 4 intentos de la primera sincronización (modo avión).
    servidor.fallosRed = 4;
    const motor = crearMotor(almacen, servidor);

    const sinRed = await motor.sincronizar();
    expect(sinRed.error).not.toBeNull();
    expect(sinRed.pendientes).toBe(20);
    expect(almacen.ops.every((o) => o.estado === 'Pendiente')).toBe(true);

    const conRed = await motor.sincronizar();
    expect(conRed.pendientes).toBe(0);
    expect(servidor.aplicadas.size).toBe(20 + 1); // + ReportarEstado
  });

  it('RN-SYN-02: reenviar un lote ya aplicado (cierre forzado a mitad del envío) no duplica', async () => {
    const almacen = new AlmacenMemoria();
    const servidor = new ServidorFalso();
    almacen.agregar(5);
    // El servidor aplicó el lote pero la app se cerró antes de guardar la respuesta: quedaron "Enviando".
    servidor.aplicadas = new Set(almacen.ops.map((o) => o.opId));
    almacen.ops.forEach((o) => (o.estado = 'Enviando'));

    const r = await crearMotor(almacen, servidor).sincronizar();
    expect(r.duplicadas).toBe(5);
    expect(r.aplicadas).toBe(0);
    expect(almacen.ops.every((o) => o.estado === 'Aplicada')).toBe(true);
  });

  it('RN-SYN-04: lote con aplicadas, duplicadas y rechazadas; las rechazadas no se reintentan', async () => {
    const almacen = new AlmacenMemoria();
    const servidor = new ServidorFalso();
    almacen.agregar(4);
    servidor.aplicadas.add('op-2');
    servidor.rechazar.add('op-3');
    const motor = crearMotor(almacen, servidor);

    const r = await motor.sincronizar();
    expect([r.aplicadas, r.duplicadas, r.rechazadas]).toEqual([2, 1, 1]);
    expect(almacen.ops.find((o) => o.opId === 'op-3')?.estado).toBe('Rechazada');
    expect(almacen.ops.find((o) => o.opId === 'op-3')?.error).toBe('Folio duplicado.');

    const lotesAntes = servidor.lotes.length;
    await motor.sincronizar();
    const reenviadas = servidor.lotes.slice(lotesAntes).flatMap((l) => l.operaciones.map((o) => o.opId));
    expect(reenviadas).not.toContain('op-3');
  });

  it('RN-SYN-04: reintenta errores de red con retroceso exponencial', async () => {
    const almacen = new AlmacenMemoria();
    const servidor = new ServidorFalso();
    almacen.agregar(1);
    servidor.fallosRed = 2;
    const esperas: number[] = [];
    const r = await crearMotor(almacen, servidor, esperas).sincronizar();
    expect(r.error).toBeNull();
    expect(esperas).toEqual([1000, 2000]);
  });

  it('pull incremental: repite mientras hayMas y guarda el cursor', async () => {
    const almacen = new AlmacenMemoria();
    const servidor = new ServidorFalso();
    servidor.paginas = [pagina(500, true, 3), pagina(900, true, 2), pagina(950, false, 1)];
    const r = await crearMotor(almacen, servidor).sincronizar();
    expect(servidor.consultasPull).toEqual([
      '/api/v1/sync/pull?desde=0&limite=500',
      '/api/v1/sync/pull?desde=500&limite=500',
      '/api/v1/sync/pull?desde=900&limite=500',
    ]);
    expect(almacen.estado.cursor).toBe(950);
    expect(r.cambiosRecibidos).toBe(6);
  });

  it('RN-SYN-08: al reinstalar toma el último consecutivo del servidor', async () => {
    const almacen = new AlmacenMemoria();
    almacen.estado.consecutivoVerificado = false;
    const servidor = new ServidorFalso();
    servidor.ultimoConsecutivo = 123;
    await crearMotor(almacen, servidor).sincronizar();
    expect(almacen.estado.consecutivoFolio).toBe(123);
  });

  it('sesión expirada: conserva las operaciones y pide iniciar sesión', async () => {
    const almacen = new AlmacenMemoria();
    almacen.agregar(3);
    const servidor = new ServidorFalso();
    servidor.post = async () => {
      throw new ErrorHttp(401, 'No autorizado');
    };
    const r = await crearMotor(almacen, servidor).sincronizar();
    expect(r.requiereLogin).toBe(true);
    expect(r.pendientes).toBe(3);
  });

  it('dos disparadores simultáneos comparten la misma sincronización', async () => {
    const almacen = new AlmacenMemoria();
    almacen.agregar(2);
    const servidor = new ServidorFalso();
    const motor = crearMotor(almacen, servidor);
    const [a, b] = await Promise.all([motor.sincronizar(), motor.sincronizar()]);
    expect(a).toBe(b);
    expect(servidor.aplicadas.size).toBe(3);
  });
});
