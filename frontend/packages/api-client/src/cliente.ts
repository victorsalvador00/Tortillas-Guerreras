/**
 * `fetch` que usan todas las funciones generadas por Orval: base URL, token Bearer, un reintento tras refrescar la
 * sesión ante 401 y errores `ProblemDetails` (RFC 9457) convertidos en {@link ErrorApi}.
 */

export interface ConfiguracionCliente {
  /** Origen del API, p. ej. `https://api.dominio.mx`. Vacío = mismo origen (proxy de Vite). */
  urlBase: string;
  obtenerToken: () => string | null;
  /** Refresca la sesión; devuelve `true` si hay un token nuevo. */
  refrescarSesion?: () => Promise<boolean>;
  /** Se llama cuando la sesión no se puede recuperar (401 tras refrescar). */
  alExpirarSesion?: () => void;
  /** Implementación de fetch (la app de ruta puede inyectar la suya). */
  fetch?: typeof fetch;
}

let configuracion: ConfiguracionCliente = { urlBase: '', obtenerToken: () => null };

export function configurarCliente(nueva: ConfiguracionCliente): void {
  configuracion = nueva;
}

export function obtenerConfiguracion(): ConfiguracionCliente {
  return configuracion;
}

/** Error de negocio o de transporte con el `codigo` del API (`INV-001`, `VAL-001`…). */
export class ErrorApi extends Error {
  readonly estado: number;
  readonly codigo: string;
  readonly errores: Record<string, string[]>;
  readonly detalle: unknown;

  constructor(estado: number, titulo: string, codigo: string, errores?: Record<string, string[]>, detalle?: unknown) {
    super(titulo);
    this.name = 'ErrorApi';
    this.estado = estado;
    this.codigo = codigo;
    this.errores = errores ?? {};
    this.detalle = detalle;
  }

  /** Primer mensaje del campo (para mostrarlo bajo el control del formulario). */
  errorDe(campo: string): string | undefined {
    return this.errores[campo]?.[0];
  }
}

export function esErrorApi(e: unknown): e is ErrorApi {
  return e instanceof ErrorApi;
}

/** Mensaje en español para cualquier error capturado en la interfaz. */
export function mensajeError(e: unknown): string {
  if (e instanceof ErrorApi) return e.message;
  if (e instanceof TypeError) return 'No hay conexión con el servidor. Revisa tu red e intenta de nuevo.';
  return 'Ocurrió un error inesperado. Intenta de nuevo.';
}

interface ConfigSolicitud {
  url: string;
  method: string;
  params?: Record<string, unknown>;
  data?: unknown;
  headers?: Record<string, string>;
  signal?: AbortSignal;
  responseType?: string;
}

function construirUrl(url: string, params?: Record<string, unknown>): string {
  const base = `${configuracion.urlBase}${url}`;
  if (!params) return base;
  const busqueda = new URLSearchParams();
  for (const [clave, valor] of Object.entries(params)) {
    if (valor === undefined || valor === null || valor === '') continue;
    if (Array.isArray(valor)) {
      valor.forEach((v) => busqueda.append(clave, String(v)));
    } else {
      busqueda.append(clave, String(valor));
    }
  }
  const query = busqueda.toString();
  return query ? `${base}?${query}` : base;
}

let refrescoEnCurso: Promise<boolean> | null = null;

async function refrescarUnaVez(): Promise<boolean> {
  if (!configuracion.refrescarSesion) return false;
  refrescoEnCurso ??= configuracion.refrescarSesion().finally(() => {
    refrescoEnCurso = null;
  });
  return refrescoEnCurso;
}

async function ejecutar(config: ConfigSolicitud, opciones?: RequestInit): Promise<Response> {
  const token = configuracion.obtenerToken();
  const headers: Record<string, string> = { Accept: 'application/json', ...config.headers };
  if (token) headers.Authorization = `Bearer ${token}`;
  let body: BodyInit | undefined;
  if (config.data !== undefined) {
    if (config.data instanceof FormData) {
      // El navegador pone el boundary; un Content-Type fijo lo rompería.
      delete headers['Content-Type'];
      body = config.data;
    } else {
      headers['Content-Type'] = 'application/json';
      body = JSON.stringify(config.data);
    }
  }
  const f = configuracion.fetch ?? fetch;
  return f(construirUrl(config.url, config.params), {
    ...opciones,
    method: config.method.toUpperCase(),
    headers: { ...headers, ...(opciones?.headers as Record<string, string> | undefined) },
    body,
    signal: config.signal ?? opciones?.signal,
  });
}

async function aError(respuesta: Response): Promise<ErrorApi> {
  let cuerpo: Record<string, unknown> = {};
  try {
    cuerpo = (await respuesta.json()) as Record<string, unknown>;
  } catch {
    // Respuesta sin cuerpo JSON.
  }
  const titulo =
    (cuerpo.title as string | undefined) ??
    (respuesta.status === 403
      ? 'No tienes permiso para esta acción.'
      : respuesta.status === 404
        ? 'No se encontró el recurso.'
        : 'Ocurrió un error inesperado. Intenta de nuevo.');
  // Sin ProblemDetails y con 5xx: el API no respondió (apagado, proxy o red caída).
  if (respuesta.status >= 500 && cuerpo.codigo === undefined) {
    return new ErrorApi(respuesta.status, 'No hay conexión con el servidor. Verifica que el API esté en marcha e intenta de nuevo.', 'GEN-RED');
  }
  const codigo = (cuerpo.codigo as string | undefined) ?? `HTTP-${respuesta.status}`;
  return new ErrorApi(
    respuesta.status,
    titulo,
    codigo,
    cuerpo.errores as Record<string, string[]> | undefined,
    cuerpo.detalle,
  );
}

export async function clienteApi<T>(config: ConfigSolicitud, opciones?: RequestInit): Promise<T> {
  let respuesta = await ejecutar(config, opciones);
  if (respuesta.status === 401 && !config.url.includes('/auth/')) {
    if (await refrescarUnaVez()) {
      respuesta = await ejecutar(config, opciones);
    }
    if (respuesta.status === 401) {
      configuracion.alExpirarSesion?.();
    }
  }
  if (!respuesta.ok) {
    throw await aError(respuesta);
  }
  if (respuesta.status === 204) {
    return undefined as T;
  }
  if (config.responseType === 'blob') {
    return (await respuesta.blob()) as T;
  }
  const tipo = respuesta.headers.get('content-type') ?? '';
  if (tipo.includes('json')) {
    return (await respuesta.json()) as T;
  }
  return (await respuesta.text()) as unknown as T;
}

export default clienteApi;

export type ErrorType<E> = E;
export type BodyType<B> = B;
