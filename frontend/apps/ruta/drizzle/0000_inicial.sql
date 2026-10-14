CREATE TABLE `clientes` (
	`id` text PRIMARY KEY NOT NULL,
	`codigo` text NOT NULL,
	`nombre` text NOT NULL,
	`canal_clave` text NOT NULL,
	`lista_precio_id` text NOT NULL,
	`condicion_pago` text NOT NULL,
	`dias_credito` integer NOT NULL,
	`limite_credito` real,
	`requiere_factura` integer NOT NULL,
	`rfc` text,
	`razon_social` text,
	`telefono` text,
	`direccion` text,
	`latitud` real,
	`longitud` real,
	`ubicacion_aprobada` integer NOT NULL,
	`es_generico` integer NOT NULL,
	`activo` integer NOT NULL,
	`version_sync` integer NOT NULL
);
--> statement-breakpoint
CREATE TABLE `cobros_locales` (
	`id` text PRIMARY KEY NOT NULL,
	`cliente_id` text NOT NULL,
	`importe` integer NOT NULL,
	`forma_pago` text NOT NULL,
	`fecha_operativa` text NOT NULL,
	`creada_en` text NOT NULL
);
--> statement-breakpoint
CREATE TABLE `envios` (
	`id` text PRIMARY KEY NOT NULL,
	`folio` text NOT NULL,
	`fecha_operativa` text NOT NULL,
	`tipo` text NOT NULL,
	`estado` text NOT NULL,
	`despachado_en` text,
	`version_sync` integer NOT NULL
);
--> statement-breakpoint
CREATE TABLE `envios_partidas` (
	`id` text PRIMARY KEY NOT NULL,
	`envio_id` text NOT NULL,
	`producto_id` text NOT NULL,
	`lote` text NOT NULL,
	`cantidad_base` real NOT NULL
);
--> statement-breakpoint
CREATE INDEX `ix_envios_partidas_envio` ON `envios_partidas` (`envio_id`);--> statement-breakpoint
CREATE TABLE `estado_dispositivo` (
	`id` integer PRIMARY KEY NOT NULL,
	`dispositivo_id` text,
	`codigo` text,
	`ruta_id` text,
	`ruta_codigo` text,
	`ruta_nombre` text,
	`almacen_id` text,
	`consecutivo_folio` integer DEFAULT 0 NOT NULL,
	`consecutivo_verificado` integer DEFAULT false NOT NULL,
	`secuencia_operacion` integer DEFAULT 0 NOT NULL,
	`cursor_sync` integer DEFAULT 0 NOT NULL,
	`ultimo_sync` text
);
--> statement-breakpoint
CREATE TABLE `motivos_merma` (
	`id` text PRIMARY KEY NOT NULL,
	`clave` text NOT NULL,
	`nombre` text NOT NULL,
	`aplica_ruta` integer NOT NULL,
	`aplica_retorno` integer NOT NULL,
	`activo` integer NOT NULL,
	`version_sync` integer NOT NULL
);
--> statement-breakpoint
CREATE TABLE `operaciones_pendientes` (
	`op_id` text PRIMARY KEY NOT NULL,
	`secuencia` integer NOT NULL,
	`tipo` text NOT NULL,
	`datos_json` text NOT NULL,
	`estado` text NOT NULL,
	`intentos` integer DEFAULT 0 NOT NULL,
	`error` text,
	`creada_en` text NOT NULL,
	`resuelta_en` text,
	`documento_id` text,
	`descripcion` text
);
--> statement-breakpoint
CREATE UNIQUE INDEX `operaciones_pendientes_secuencia_unique` ON `operaciones_pendientes` (`secuencia`);--> statement-breakpoint
CREATE INDEX `ix_operaciones_estado` ON `operaciones_pendientes` (`estado`);--> statement-breakpoint
CREATE TABLE `parametros` (
	`clave` text PRIMARY KEY NOT NULL,
	`valor` text NOT NULL,
	`version_sync` integer NOT NULL
);
--> statement-breakpoint
CREATE TABLE `pedidos` (
	`id` text PRIMARY KEY NOT NULL,
	`folio` text NOT NULL,
	`cliente_id` text NOT NULL,
	`fecha_entrega` text NOT NULL,
	`estado` text NOT NULL,
	`total` real NOT NULL,
	`version_sync` integer NOT NULL
);
--> statement-breakpoint
CREATE TABLE `pedidos_partidas` (
	`id` text PRIMARY KEY NOT NULL,
	`pedido_id` text NOT NULL,
	`producto_id` text NOT NULL,
	`unidad_id` text NOT NULL,
	`cantidad` real NOT NULL,
	`cantidad_base` real NOT NULL,
	`precio_unitario` real NOT NULL,
	`tasa_iva` real NOT NULL,
	`tasa_ieps` real,
	`cantidad_entregada_base` real NOT NULL
);
--> statement-breakpoint
CREATE INDEX `ix_pedidos_partidas_pedido` ON `pedidos_partidas` (`pedido_id`);--> statement-breakpoint
CREATE TABLE `precios` (
	`lista_precio_id` text NOT NULL,
	`producto_id` text NOT NULL,
	`unidad_id` text NOT NULL,
	`precio_unitario` real NOT NULL,
	`version_sync` integer NOT NULL,
	PRIMARY KEY(`lista_precio_id`, `producto_id`, `unidad_id`)
);
--> statement-breakpoint
CREATE TABLE `productos` (
	`id` text PRIMARY KEY NOT NULL,
	`sku` text NOT NULL,
	`nombre` text NOT NULL,
	`tipo` text NOT NULL,
	`unidad_base_id` text NOT NULL,
	`unidad_base_clave` text NOT NULL,
	`unidad_base_decimales` integer NOT NULL,
	`maneja_lote` integer NOT NULL,
	`tasa_iva` real NOT NULL,
	`tasa_ieps` real,
	`activo` integer NOT NULL,
	`version_sync` integer NOT NULL
);
--> statement-breakpoint
CREATE TABLE `productos_unidades` (
	`producto_id` text NOT NULL,
	`unidad_id` text NOT NULL,
	`unidad_clave` text NOT NULL,
	`factor_a_base` real NOT NULL,
	`decimales` integer NOT NULL,
	`uso_venta` integer NOT NULL,
	`predeterminada_venta` integer NOT NULL,
	PRIMARY KEY(`producto_id`, `unidad_id`)
);
--> statement-breakpoint
CREATE TABLE `retornos_locales` (
	`id` text PRIMARY KEY NOT NULL,
	`retorno_id` text NOT NULL,
	`folio` text NOT NULL,
	`tipo` text NOT NULL,
	`cliente_id` text,
	`producto_id` text NOT NULL,
	`cantidad_base` real NOT NULL,
	`condicion` text NOT NULL,
	`motivo_merma_id` text,
	`fecha_operativa` text NOT NULL,
	`creada_en` text NOT NULL
);
--> statement-breakpoint
CREATE TABLE `rutas_clientes` (
	`cliente_id` text PRIMARY KEY NOT NULL,
	`ruta_id` text NOT NULL,
	`orden_visita` integer NOT NULL,
	`dias_visita` text NOT NULL,
	`activo` integer NOT NULL,
	`version_sync` integer NOT NULL
);
--> statement-breakpoint
CREATE TABLE `saldos_clientes` (
	`cliente_id` text PRIMARY KEY NOT NULL,
	`saldo` real NOT NULL
);
--> statement-breakpoint
CREATE TABLE `ventas_locales` (
	`id` text PRIMARY KEY NOT NULL,
	`folio` text NOT NULL,
	`tipo` text NOT NULL,
	`cliente_id` text NOT NULL,
	`pedido_id` text,
	`condicion_pago` text NOT NULL,
	`forma_pago` text NOT NULL,
	`subtotal` integer NOT NULL,
	`ieps` integer NOT NULL,
	`iva` integer NOT NULL,
	`total` integer NOT NULL,
	`fecha_operativa` text NOT NULL,
	`creada_en` text NOT NULL,
	`latitud` real,
	`longitud` real,
	`precision_m` real
);
--> statement-breakpoint
CREATE UNIQUE INDEX `ventas_locales_folio_unique` ON `ventas_locales` (`folio`);--> statement-breakpoint
CREATE TABLE `ventas_locales_partidas` (
	`id` text PRIMARY KEY NOT NULL,
	`venta_id` text NOT NULL,
	`producto_id` text NOT NULL,
	`unidad_id` text NOT NULL,
	`unidad_clave` text NOT NULL,
	`descripcion` text NOT NULL,
	`cantidad` real NOT NULL,
	`cantidad_base` real NOT NULL,
	`precio_unitario` real NOT NULL,
	`importe` integer NOT NULL,
	`ieps` integer NOT NULL,
	`iva` integer NOT NULL,
	`total` integer NOT NULL
);
--> statement-breakpoint
CREATE INDEX `ix_ventas_locales_partidas_venta` ON `ventas_locales_partidas` (`venta_id`);