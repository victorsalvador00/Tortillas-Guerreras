CREATE TABLE `cambios_locales` (
	`id` text PRIMARY KEY NOT NULL,
	`retorno_id` text NOT NULL,
	`folio` text NOT NULL,
	`cliente_id` text NOT NULL,
	`producto_id` text NOT NULL,
	`cantidad_base` real NOT NULL,
	`sentido` text NOT NULL,
	`condicion` text NOT NULL,
	`motivo_merma_id` text,
	`cambio_pendiente_id` text,
	`fecha_operativa` text NOT NULL,
	`creada_en` text NOT NULL
);
--> statement-breakpoint
CREATE TABLE `cambios_pendientes` (
	`id` text PRIMARY KEY NOT NULL,
	`cliente_id` text NOT NULL,
	`producto_id` text NOT NULL,
	`cantidad_base` real NOT NULL,
	`cantidad_saldada_base` real NOT NULL,
	`fecha_operativa` text NOT NULL,
	`version_sync` integer NOT NULL
);
--> statement-breakpoint
CREATE TABLE `canales` (
	`id` text PRIMARY KEY NOT NULL,
	`clave` text NOT NULL,
	`nombre` text NOT NULL,
	`formato_liquidacion` text NOT NULL,
	`pedido_por` text NOT NULL
);
--> statement-breakpoint
CREATE TABLE `consumos_clientes` (
	`cliente_id` text NOT NULL,
	`producto_id` text NOT NULL,
	`promedio_base` real NOT NULL,
	PRIMARY KEY(`cliente_id`, `producto_id`)
);
--> statement-breakpoint
CREATE TABLE `pedidos_locales` (
	`id` text PRIMARY KEY NOT NULL,
	`cliente_id` text NOT NULL,
	`fecha_entrega` text NOT NULL,
	`orden_compra_cliente` text,
	`notas` text,
	`es_ruta` integer NOT NULL,
	`estado` text NOT NULL,
	`creado_en` text NOT NULL,
	`enviado_en` text
);
--> statement-breakpoint
CREATE TABLE `pedidos_locales_partidas` (
	`id` text PRIMARY KEY NOT NULL,
	`pedido_id` text NOT NULL,
	`producto_id` text NOT NULL,
	`unidad_id` text NOT NULL,
	`unidad_clave` text NOT NULL,
	`descripcion` text NOT NULL,
	`cantidad` real NOT NULL,
	`cantidad_base` real NOT NULL,
	`tipo` text NOT NULL,
	`notas` text
);
--> statement-breakpoint
CREATE INDEX `ix_pedidos_locales_partidas_pedido` ON `pedidos_locales_partidas` (`pedido_id`);--> statement-breakpoint
CREATE TABLE `precios_cliente` (
	`cliente_id` text NOT NULL,
	`producto_id` text NOT NULL,
	`unidad_id` text NOT NULL,
	`precio_unitario` real NOT NULL,
	`vigente_desde` text NOT NULL,
	`version_sync` integer NOT NULL,
	PRIMARY KEY(`cliente_id`, `producto_id`, `unidad_id`)
);
--> statement-breakpoint
CREATE TABLE `productos_exclusivos` (
	`id` text PRIMARY KEY NOT NULL,
	`producto_id` text NOT NULL,
	`cliente_id` text,
	`grupo_cliente_id` text,
	`version_sync` integer NOT NULL
);
--> statement-breakpoint
ALTER TABLE `clientes` ADD `grupo_cliente_id` text;--> statement-breakpoint
ALTER TABLE `pedidos` ADD `es_ruta` integer DEFAULT false NOT NULL;--> statement-breakpoint
ALTER TABLE `pedidos_partidas` ADD `tipo` text DEFAULT 'Venta' NOT NULL;--> statement-breakpoint
ALTER TABLE `productos` ADD `nombre_corto` text;--> statement-breakpoint
ALTER TABLE `productos_unidades` ADD `uso_traslado` integer DEFAULT false NOT NULL;--> statement-breakpoint
ALTER TABLE `productos_unidades` ADD `predeterminada_traslado` integer DEFAULT false NOT NULL;--> statement-breakpoint
UPDATE `estado_dispositivo` SET `cursor_sync` = 0;