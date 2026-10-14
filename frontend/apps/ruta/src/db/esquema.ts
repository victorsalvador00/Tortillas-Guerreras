import { index, integer, primaryKey, real, sqliteTable, text } from 'drizzle-orm/sqlite-core';

/**
 * Base local de la app (06-app-ruta). Tablas espejo de bajada (gana el servidor) y tablas propias de la app.
 * Cualquier cambio de esquema va con una migración de Drizzle (`pnpm db:generar`, RN-APP-08).
 */

const bool = (nombre: string) => integer(nombre, { mode: 'boolean' });

// ───────────────────────────── Espejo de bajada ─────────────────────────────

export const productos = sqliteTable('productos', {
  id: text('id').primaryKey(),
  sku: text('sku').notNull(),
  nombre: text('nombre').notNull(),
  tipo: text('tipo').notNull(),
  unidadBaseId: text('unidad_base_id').notNull(),
  unidadBaseClave: text('unidad_base_clave').notNull(),
  unidadBaseDecimales: integer('unidad_base_decimales').notNull(),
  manejaLote: bool('maneja_lote').notNull(),
  tasaIva: real('tasa_iva').notNull(),
  tasaIeps: real('tasa_ieps'),
  activo: bool('activo').notNull(),
  versionSync: integer('version_sync').notNull(),
});

export const productosUnidades = sqliteTable(
  'productos_unidades',
  {
    productoId: text('producto_id').notNull(),
    unidadId: text('unidad_id').notNull(),
    unidadClave: text('unidad_clave').notNull(),
    factorABase: real('factor_a_base').notNull(),
    decimales: integer('decimales').notNull(),
    usoVenta: bool('uso_venta').notNull(),
    predeterminadaVenta: bool('predeterminada_venta').notNull(),
  },
  (t) => [primaryKey({ columns: [t.productoId, t.unidadId] })],
);

export const clientes = sqliteTable('clientes', {
  id: text('id').primaryKey(),
  codigo: text('codigo').notNull(),
  nombre: text('nombre').notNull(),
  canalClave: text('canal_clave').notNull(),
  listaPrecioId: text('lista_precio_id').notNull(),
  condicionPago: text('condicion_pago', { enum: ['Contado', 'Credito'] }).notNull(),
  diasCredito: integer('dias_credito').notNull(),
  limiteCredito: real('limite_credito'),
  requiereFactura: bool('requiere_factura').notNull(),
  rfc: text('rfc'),
  razonSocial: text('razon_social'),
  telefono: text('telefono'),
  direccion: text('direccion'),
  latitud: real('latitud'),
  longitud: real('longitud'),
  ubicacionAprobada: bool('ubicacion_aprobada').notNull(),
  esGenerico: bool('es_generico').notNull(),
  activo: bool('activo').notNull(),
  versionSync: integer('version_sync').notNull(),
});

export const rutasClientes = sqliteTable('rutas_clientes', {
  clienteId: text('cliente_id').primaryKey(),
  rutaId: text('ruta_id').notNull(),
  ordenVisita: integer('orden_visita').notNull(),
  /** JSON con los días ISO (1 = lunes … 7 = domingo). */
  diasVisita: text('dias_visita', { mode: 'json' }).$type<number[]>().notNull(),
  activo: bool('activo').notNull(),
  versionSync: integer('version_sync').notNull(),
});

export const precios = sqliteTable(
  'precios',
  {
    listaPrecioId: text('lista_precio_id').notNull(),
    productoId: text('producto_id').notNull(),
    unidadId: text('unidad_id').notNull(),
    precioUnitario: real('precio_unitario').notNull(),
    versionSync: integer('version_sync').notNull(),
  },
  (t) => [primaryKey({ columns: [t.listaPrecioId, t.productoId, t.unidadId] })],
);

export const envios = sqliteTable('envios', {
  id: text('id').primaryKey(),
  folio: text('folio').notNull(),
  fechaOperativa: text('fecha_operativa').notNull(),
  tipo: text('tipo').notNull(),
  estado: text('estado').notNull(),
  despachadoEn: text('despachado_en'),
  versionSync: integer('version_sync').notNull(),
});

export const enviosPartidas = sqliteTable(
  'envios_partidas',
  {
    id: text('id').primaryKey(),
    envioId: text('envio_id').notNull(),
    productoId: text('producto_id').notNull(),
    lote: text('lote').notNull(),
    cantidadBase: real('cantidad_base').notNull(),
  },
  (t) => [index('ix_envios_partidas_envio').on(t.envioId)],
);

export const pedidos = sqliteTable('pedidos', {
  id: text('id').primaryKey(),
  folio: text('folio').notNull(),
  clienteId: text('cliente_id').notNull(),
  fechaEntrega: text('fecha_entrega').notNull(),
  estado: text('estado').notNull(),
  total: real('total').notNull(),
  envioId: text('envio_id'),
  envioFolio: text('envio_folio'),
  versionSync: integer('version_sync').notNull(),
});

export const pedidosPartidas = sqliteTable(
  'pedidos_partidas',
  {
    id: text('id').primaryKey(),
    pedidoId: text('pedido_id').notNull(),
    productoId: text('producto_id').notNull(),
    unidadId: text('unidad_id').notNull(),
    cantidad: real('cantidad').notNull(),
    cantidadBase: real('cantidad_base').notNull(),
    precioUnitario: real('precio_unitario').notNull(),
    tasaIva: real('tasa_iva').notNull(),
    tasaIeps: real('tasa_ieps'),
    cantidadEntregadaBase: real('cantidad_entregada_base').notNull(),
  },
  (t) => [index('ix_pedidos_partidas_pedido').on(t.pedidoId)],
);

export const saldosClientes = sqliteTable('saldos_clientes', {
  clienteId: text('cliente_id').primaryKey(),
  saldo: real('saldo').notNull(),
});

export const motivosMerma = sqliteTable('motivos_merma', {
  id: text('id').primaryKey(),
  clave: text('clave').notNull(),
  nombre: text('nombre').notNull(),
  aplicaRuta: bool('aplica_ruta').notNull(),
  aplicaRetorno: bool('aplica_retorno').notNull(),
  activo: bool('activo').notNull(),
  versionSync: integer('version_sync').notNull(),
});

export const parametros = sqliteTable('parametros', {
  clave: text('clave').primaryKey(),
  valor: text('valor').notNull(),
  versionSync: integer('version_sync').notNull(),
});

// ───────────────────────────── Propias de la app ─────────────────────────────

export const operacionesPendientes = sqliteTable(
  'operaciones_pendientes',
  {
    opId: text('op_id').primaryKey(),
    secuencia: integer('secuencia').notNull().unique(),
    tipo: text('tipo').notNull(),
    datosJson: text('datos_json').notNull(),
    estado: text('estado', { enum: ['Pendiente', 'Enviando', 'Aplicada', 'Rechazada'] }).notNull(),
    intentos: integer('intentos').notNull().default(0),
    error: text('error'),
    creadaEn: text('creada_en').notNull(),
    resueltaEn: text('resuelta_en'),
    /** Venta, retorno o cobro local al que pertenece (para mostrar el rechazo en su ticket). */
    documentoId: text('documento_id'),
    descripcion: text('descripcion'),
  },
  (t) => [index('ix_operaciones_estado').on(t.estado)],
);

export const ventasLocales = sqliteTable('ventas_locales', {
  id: text('id').primaryKey(),
  folio: text('folio').notNull().unique(),
  tipo: text('tipo', { enum: ['VentaRuta', 'EntregaPedido'] }).notNull(),
  clienteId: text('cliente_id').notNull(),
  pedidoId: text('pedido_id'),
  condicionPago: text('condicion_pago').notNull(),
  formaPago: text('forma_pago').notNull(),
  /** Importes en centavos (RN-APP-06). */
  subtotal: integer('subtotal').notNull(),
  ieps: integer('ieps').notNull(),
  iva: integer('iva').notNull(),
  total: integer('total').notNull(),
  fechaOperativa: text('fecha_operativa').notNull(),
  creadaEn: text('creada_en').notNull(),
  latitud: real('latitud'),
  longitud: real('longitud'),
  precisionM: real('precision_m'),
});

export const ventasLocalesPartidas = sqliteTable(
  'ventas_locales_partidas',
  {
    id: text('id').primaryKey(),
    ventaId: text('venta_id').notNull(),
    productoId: text('producto_id').notNull(),
    unidadId: text('unidad_id').notNull(),
    unidadClave: text('unidad_clave').notNull(),
    descripcion: text('descripcion').notNull(),
    cantidad: real('cantidad').notNull(),
    cantidadBase: real('cantidad_base').notNull(),
    precioUnitario: real('precio_unitario').notNull(),
    importe: integer('importe').notNull(),
    ieps: integer('ieps').notNull(),
    iva: integer('iva').notNull(),
    total: integer('total').notNull(),
  },
  (t) => [index('ix_ventas_locales_partidas_venta').on(t.ventaId)],
);

/** Una fila por partida de retorno (para inventario de camioneta sin red). */
export const retornosLocales = sqliteTable('retornos_locales', {
  id: text('id').primaryKey(),
  retornoId: text('retorno_id').notNull(),
  folio: text('folio').notNull(),
  tipo: text('tipo', { enum: ['DevolucionCliente', 'RechazoEntrega'] }).notNull(),
  clienteId: text('cliente_id'),
  productoId: text('producto_id').notNull(),
  cantidadBase: real('cantidad_base').notNull(),
  condicion: text('condicion', { enum: ['Vendible', 'NoVendible'] }).notNull(),
  motivoMermaId: text('motivo_merma_id'),
  fechaOperativa: text('fecha_operativa').notNull(),
  creadaEn: text('creada_en').notNull(),
});

export const cobrosLocales = sqliteTable('cobros_locales', {
  id: text('id').primaryKey(),
  clienteId: text('cliente_id').notNull(),
  importe: integer('importe').notNull(),
  formaPago: text('forma_pago').notNull(),
  fechaOperativa: text('fecha_operativa').notNull(),
  creadaEn: text('creada_en').notNull(),
});

/** Una sola fila (id = 1) con el estado del teléfono. */
export const estadoDispositivo = sqliteTable('estado_dispositivo', {
  id: integer('id').primaryKey(),
  dispositivoId: text('dispositivo_id'),
  codigo: text('codigo'),
  rutaId: text('ruta_id'),
  rutaCodigo: text('ruta_codigo'),
  rutaNombre: text('ruta_nombre'),
  almacenId: text('almacen_id'),
  consecutivoFolio: integer('consecutivo_folio').notNull().default(0),
  consecutivoVerificado: bool('consecutivo_verificado').notNull().default(false),
  secuenciaOperacion: integer('secuencia_operacion').notNull().default(0),
  cursorSync: integer('cursor_sync').notNull().default(0),
  ultimoSync: text('ultimo_sync'),
});
