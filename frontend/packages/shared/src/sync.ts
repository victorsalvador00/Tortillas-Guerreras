import { z } from 'zod';

/**
 * Contrato del protocolo de sincronización de la app de ruta (05-sincronizacion-offline.md). El backend
 * (`TG.Sincronizacion`) serializa exactamente estas formas en camelCase. Importes y cantidades viajan como números
 * decimales; la app calcula en centavos y el servidor recalcula.
 */

// ───────────────────────────── Bajada: GET /api/v1/sync/pull?desde=&limite= ─────────────────────────────

export interface ConversionSync {
  unidadId: string;
  unidadClave: string;
  factorABase: number;
  decimales: number;
  usoVenta: boolean;
  predeterminadaVenta: boolean;
}

export interface ProductoSync {
  id: string;
  sku: string;
  nombre: string;
  tipo: string;
  unidadBaseId: string;
  unidadBaseClave: string;
  unidadBaseDecimales: number;
  manejaLote: boolean;
  tasaIva: number;
  tasaIeps: number | null;
  activo: boolean;
  versionSync: number;
  conversiones: ConversionSync[];
}

export interface ClienteSync {
  id: string;
  codigo: string;
  nombre: string;
  canalClave: string;
  listaPrecioId: string;
  condicionPago: 'Contado' | 'Credito';
  diasCredito: number;
  limiteCredito: number | null;
  requiereFactura: boolean;
  rfc: string | null;
  razonSocial: string | null;
  telefono: string | null;
  /** Dirección en una línea (calle, colonia, municipio). */
  direccion: string | null;
  latitud: number | null;
  longitud: number | null;
  ubicacionAprobada: boolean;
  esGenerico: boolean;
  activo: boolean;
  versionSync: number;
  /** Grupo del cliente (RN-VEN-14). */
  grupoClienteId: string | null;
}

export interface RutaClienteSync {
  rutaId: string;
  clienteId: string;
  ordenVisita: number;
  /** 1 = lunes … 7 = domingo. */
  diasVisita: number[];
  activo: boolean;
  versionSync: number;
}

export interface PrecioSync {
  listaPrecioId: string;
  productoId: string;
  unidadId: string;
  /** Sin impuestos [SUP-07]. */
  precioUnitario: number;
  versionSync: number;
}

export interface EnvioPartidaSync {
  id: string;
  productoId: string;
  lote: string;
  cantidadBase: number;
}

export interface EnvioSync {
  id: string;
  folio: string;
  fechaOperativa: string;
  tipo: 'CargaInicial' | 'Recarga';
  estado: string;
  despachadoEn: string | null;
  versionSync: number;
  partidas: EnvioPartidaSync[];
}

export interface PedidoPartidaSync {
  id: string;
  productoId: string;
  unidadId: string;
  cantidad: number;
  cantidadBase: number;
  precioUnitario: number;
  tasaIva: number;
  tasaIeps: number | null;
  cantidadEntregadaBase: number;
  /** RN-VEN-17: el cambio físico va a precio 0 y se entrega con un retorno. */
  tipo: 'Venta' | 'CambioFisico';
}

export interface PedidoSync {
  id: string;
  folio: string;
  clienteId: string;
  fechaEntrega: string;
  estado: string;
  total: number;
  /** Carga (envío) en la que va el pedido; `null` si no va en ninguna (RN-LOG-11). */
  envioId: string | null;
  envioFolio: string | null;
  versionSync: number;
  partidas: PedidoPartidaSync[];
  /** RN-VEN-16: pedido global de la ruta (al cliente genérico). */
  esRuta: boolean;
}

/** RN-VEN-12: gana sobre la lista desde `vigenteDesde`. `activo = false`: la app lo borra. */
export interface PrecioClienteSync {
  clienteId: string;
  productoId: string;
  unidadId: string;
  precioUnitario: number;
  vigenteDesde: string;
  activo: boolean;
  versionSync: number;
}

/** RN-VEN-14: el producto solo se ofrece al cliente o a los clientes del grupo; llegan todos, también de otras rutas. */
export interface ProductoExclusivoSync {
  id: string;
  productoId: string;
  clienteId: string | null;
  grupoClienteId: string | null;
  versionSync: number;
}

export interface SaldoClienteSync {
  clienteId: string;
  saldo: number;
}

export interface MotivoMermaSync {
  id: string;
  clave: string;
  nombre: string;
  aplicaRuta: boolean;
  aplicaRetorno: boolean;
  activo: boolean;
  versionSync: number;
}

export interface ParametroSync {
  clave: string;
  valor: string;
  versionSync: number;
}

export interface CambioPendienteSync {
  id: string;
  clienteId: string;
  productoId: string;
  cantidadBase: number;
  cantidadSaldadaBase: number;
  fechaOperativa: string;
  versionSync: number;
}

export interface CambiosSync {
  productos: ProductoSync[];
  clientes: ClienteSync[];
  rutasClientes: RutaClienteSync[];
  precios: PrecioSync[];
  envios: EnvioSync[];
  pedidos: PedidoSync[];
  /** Siempre completos (no incrementales): saldo de crédito vigente [SUP-08]. */
  saldosClientes: SaldoClienteSync[];
  motivosMerma: MotivoMermaSync[];
  parametros: ParametroSync[];
  preciosCliente: PrecioClienteSync[];
  productosExclusivos: ProductoExclusivoSync[];
  /** RN-SYN-11: cambios pendientes de los clientes de la ruta (los saldados o cancelados llegan en `eliminados`). */
  cambiosPendientes?: CambioPendienteSync[];
  /** Ids que la app debe borrar por colección (inactivos o fuera de alcance). `rutasClientes` usa el `clienteId`. */
  eliminados: Partial<Record<'productos' | 'clientes' | 'rutasClientes' | 'envios' | 'pedidos' | 'motivosMerma' | 'productosExclusivos' | 'cambiosPendientes', string[]>>;
}

export interface RespuestaPull {
  cursor: number;
  hayMas: boolean;
  /** Hora del servidor (UTC). */
  fechaServidor: string;
  ruta: { id: string; codigo: string; nombre: string; almacenId: string } | null;
  cambios: CambiosSync;
}

/** GET /api/v1/sync/dispositivo — al reinstalar recupera el consecutivo de folio (RN-SYN-08). */
export interface EstadoDispositivoServidor {
  dispositivoId: string;
  codigo: string;
  activo: boolean;
  rutaId: string | null;
  rutaCodigo: string | null;
  ultimoConsecutivo: number;
}

// ───────────────────────────── Subida: POST /api/v1/sync/push ─────────────────────────────

export const gpsSync = z.object({
  lat: z.number().min(-90).max(90),
  lng: z.number().min(-180).max(180),
  precisionM: z.number().nonnegative().nullable(),
  tomadoEn: z.string(),
});
export type GpsSync = z.infer<typeof gpsSync>;

export const datosRegistrarVenta = z.object({
  ventaId: z.string().uuid(),
  folio: z.string(),
  consecutivo: z.number().int().positive(),
  clienteId: z.string().uuid(),
  condicionPago: z.enum(['Contado', 'Credito']),
  formaPago: z.enum(['Efectivo', 'Transferencia', 'Credito']),
  partidas: z
    .array(
      z.object({
        productoId: z.string().uuid(),
        unidadId: z.string().uuid(),
        cantidad: z.number().positive(),
        precioUnitario: z.number().nonnegative(),
      }),
    )
    .min(1),
  totalApp: z.number().nonnegative(),
  gps: gpsSync.nullable(),
});

export const datosEntregarPedido = z.object({
  ventaId: z.string().uuid(),
  folio: z.string(),
  consecutivo: z.number().int().positive(),
  pedidoId: z.string().uuid(),
  formaPago: z.enum(['Efectivo', 'Transferencia', 'Credito']),
  partidas: z.array(z.object({ pedidoPartidaId: z.string().uuid(), cantidad: z.number().nonnegative() })).min(1),
  totalApp: z.number().nonnegative(),
  gps: gpsSync.nullable(),
});

export const datosRegistrarRetorno = z.object({
  retornoId: z.string().uuid(),
  folio: z.string(),
  consecutivo: z.number().int().positive(),
  tipo: z.enum(['DevolucionCliente', 'RechazoEntrega', 'CambioFisico']),
  clienteId: z.string().uuid().nullable(),
  ventaId: z.string().uuid().nullable(),
  pedidoId: z.string().uuid().nullable(),
  partidas: z
    .array(
      z.object({
        productoId: z.string().uuid(),
        /** Cantidad en unidad base. */
        cantidad: z.number().positive(),
        condicion: z.enum(['Vendible', 'NoVendible']),
        motivoMermaId: z.string().uuid().nullable(),
        /** RN-SYN-11 (solo CambioFisico): `Entregado` sale de la camioneta a cambio. */
        sentido: z.enum(['Retirado', 'Entregado']).optional(),
        cambioPendienteId: z.string().uuid().nullable().optional(),
        /** RN-LOG-16: en `Retirado`, si lo no entregado queda pendiente (por defecto sí). */
        dejarPendiente: z.boolean().optional(),
      }),
    )
    .min(1),
  gps: gpsSync.nullable(),
});

export const datosRegistrarCobro = z.object({
  cobroId: z.string().uuid(),
  clienteId: z.string().uuid(),
  importe: z.number().positive(),
  formaPago: z.enum(['Efectivo', 'Transferencia']),
  ventaIds: z.array(z.string().uuid()).nullable(),
});

export const datosProponerUbicacion = z.object({
  clienteId: z.string().uuid(),
  gps: gpsSync,
});

/** RN-SYN-10: requiere `ventas.pedidos_app`. Los precios los pone el servidor (lista o especial vigente). */
export const datosCapturarPedido = z.object({
  pedidoId: z.string().uuid(),
  clienteId: z.string().uuid(),
  fechaEntrega: z.string().regex(/^\d{4}-\d{2}-\d{2}$/),
  ordenCompraCliente: z.string().nullable(),
  /** Informativo: el servidor decide si es pedido de ruta (RN-VEN-16). */
  esRuta: z.boolean(),
  partidas: z
    .array(
      z.object({
        productoId: z.string().uuid(),
        unidadId: z.string().uuid(),
        cantidad: z.number().positive(),
        tipo: z.enum(['Venta', 'CambioFisico']),
        notas: z.string().nullable(),
      }),
    )
    .min(1),
  notas: z.string().nullable(),
  gps: gpsSync.nullable(),
});

export const datosReportarEstado = z.object({
  pendientes: z.number().int().nonnegative(),
  bateria: z.number().min(0).max(1).nullable(),
  versionApp: z.string(),
  ultimoConsecutivo: z.number().int().nonnegative(),
});

export type TipoOperacion =
  | 'RegistrarVenta'
  | 'EntregarPedido'
  | 'RegistrarRetorno'
  | 'RegistrarCobro'
  | 'ProponerUbicacionCliente'
  | 'ReportarEstado'
  | 'CapturarPedido';

export interface OperacionSync {
  /** UUID v7 generado en el teléfono. */
  opId: string;
  secuencia: number;
  tipo: TipoOperacion;
  /** ISO con zona del teléfono; la fecha operativa sale de aquí (RN-SYN-06). */
  creadaEn: string;
  datos:
    | z.infer<typeof datosRegistrarVenta>
    | z.infer<typeof datosEntregarPedido>
    | z.infer<typeof datosRegistrarRetorno>
    | z.infer<typeof datosRegistrarCobro>
    | z.infer<typeof datosProponerUbicacion>
    | z.infer<typeof datosReportarEstado>
    | z.infer<typeof datosCapturarPedido>;
}

export interface SolicitudPush {
  dispositivoId: string;
  loteId: string;
  versionApp: string;
  operaciones: OperacionSync[];
}

export type EstadoResultado = 'Aplicada' | 'Duplicada' | 'Rechazada';

export interface ResultadoOperacion {
  opId: string;
  estado: EstadoResultado;
  idsServidor?: Record<string, string> | null;
  incidencias?: string[] | null;
  codigo?: string | null;
  mensaje?: string | null;
}

export interface RespuestaPush {
  resultados: ResultadoOperacion[];
  pendientesServidor: number;
}

/** RN-SYN-07: máximo 200 operaciones por lote. */
export const MAXIMO_OPERACIONES_LOTE = 200;
