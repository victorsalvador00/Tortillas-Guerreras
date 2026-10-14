import type {
  EstadoResultado,
  OperacionSync,
  RespuestaPull,
  ResultadoOperacion,
  TipoOperacion,
} from '@tg/shared';

/**
 * Puertos del motor de sincronización: el motor es TypeScript puro y recibe HTTP, almacenamiento local y reloj
 * inyectados (así se prueba sin teléfono ni red).
 */

/** Error de transporte o de negocio devuelto por el API. */
export class ErrorHttp extends Error {
  constructor(
    readonly estado: number,
    mensaje: string,
    readonly codigo?: string,
  ) {
    super(mensaje);
    this.name = 'ErrorHttp';
  }
}

/** RN-SYN-04: se reintentan los errores de red (estado 0) y 5xx; los rechazos de negocio no. */
export function esReintentable(e: unknown): boolean {
  if (e instanceof ErrorHttp) return e.estado === 0 || e.estado === 408 || e.estado === 429 || e.estado >= 500;
  return e instanceof TypeError;
}

export interface ClienteHttp {
  get<T>(ruta: string): Promise<T>;
  post<T>(ruta: string, cuerpo: unknown): Promise<T>;
}

export type EstadoOperacionLocal = 'Pendiente' | 'Enviando' | 'Aplicada' | 'Rechazada';

export interface OperacionLocal {
  opId: string;
  secuencia: number;
  tipo: TipoOperacion;
  datos: unknown;
  creadaEn: string;
  estado: EstadoOperacionLocal;
  intentos: number;
}

export interface EstadoSyncLocal {
  dispositivoId: string | null;
  cursor: number;
  ultimoSync: string | null;
  consecutivoFolio: number;
  /** Ya se consultó `GET /sync/dispositivo` en esta instalación. */
  consecutivoVerificado: boolean;
}

/** Almacenamiento local que usa el motor (implementado sobre SQLite + Drizzle en la app). */
export interface AlmacenSync {
  leerEstado(): Promise<EstadoSyncLocal>;
  /** Aplica la página de cambios y guarda el cursor en una sola transacción local. */
  aplicarCambios(respuesta: RespuestaPull): Promise<void>;
  /** Operaciones por enviar (Pendiente o Enviando sin respuesta), ordenadas por secuencia. */
  operacionesPorEnviar(): Promise<OperacionLocal[]>;
  marcarEnviando(opIds: string[]): Promise<void>;
  /** Vuelve a Pendiente las operaciones de un lote que no obtuvo respuesta. */
  revertirEnvio(opIds: string[], error: string): Promise<void>;
  registrarResultados(resultados: ResultadoOperacion[], ahora: string): Promise<void>;
  contarPendientes(): Promise<number>;
  siguienteSecuencia(): Promise<number>;
  fijarConsecutivo(consecutivo: number): Promise<void>;
  /** RN-APP-05: borra operaciones Aplicadas anteriores a la fecha dada. */
  limpiarAplicadas(antesDe: string): Promise<number>;
}

export interface Reloj {
  ahora(): Date;
  dormir(ms: number): Promise<void>;
}

export type { EstadoResultado, OperacionSync };
