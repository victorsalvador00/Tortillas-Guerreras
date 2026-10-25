# 03 — Modelo de datos (versión por defecto)

Modelo típico de una tortillería industrial con distribución en ruta. Las columnas de auditoría
(`creado_en`, `creado_por`, `actualizado_en`, `actualizado_por`) se omiten en las tablas pero aplican
a toda tabla editable (ver `02-convenciones.md`). `PK` = llave primaria, `UX` = único.

> Cuando llegue la información del cliente, este archivo se ajusta siguiendo el procedimiento de
> `10-supuestos-y-pendientes.md`. Los campos marcados `[SUP-xx]` son los más probables de cambiar.

---

## Esquema `seguridad`

Usa las tablas de ASP.NET Core Identity (`AspNetUsers`, `AspNetRoles`, …) en este esquema, con
estos campos extra en el usuario:

| Tabla | Columna | Tipo | Notas |
|---|---|---|---|
| usuarios (Identity) | nombre_completo | text | |
| | activo | boolean | |

**permisos**: `id uuid PK`, `clave text UX` (`inventario.ajustar`), `descripcion text`.
**roles_permisos**: `rol_id`, `permiso_id` (PK compuesta).

**dispositivos**

| Columna | Tipo | Notas |
|---|---|---|
| id | uuid PK | |
| codigo | text UX | `D01`, `D02`… se usa en folios |
| nombre | text | "Moto G de ruta 3" |
| usuario_id | uuid null | usuario asignado actualmente |
| plataforma | text | `android` / `ios` |
| version_app | text | reportada en cada sync |
| ultimo_sync_en | timestamptz null | |
| ultimo_consecutivo | integer | último consecutivo de folio recibido (control) |
| activo | boolean | un dispositivo inactivo no puede sincronizar |
| operaciones_pendientes | integer | pendientes reportados en el último contacto (RN-LOG-09) |
| solicitado_por | uuid null | usuario que registró el teléfono |

**refresh_tokens**: `id`, `usuario_id`, `dispositivo_id null`, `token_hash text`, `expira_en`,
`revocado_en null`, `reemplazado_por uuid null`.

Roles por defecto `[SUP-01]`: Administrador, Direccion, Compras, Produccion, Almacen, Logistica,
Vendedor, Facturacion, Supervisor. **(v0.2)** Se agregan **Captura** (valida pedidos y captura estimados en
oficina) y **Liquidaciones** (recibe el dinero y cierra la liquidación). "Vendedor" es el chofer/operador de ruta.

**bitacora (v0.2)** — historial de cambios (RN-SEG-06). Solo inserciones.

| Columna | Tipo | Notas |
|---|---|---|
| id | uuid PK | |
| fecha_hora | timestamptz | |
| esquema, tabla | text | `ventas`, `pedidos_partidas` |
| entidad_id | uuid | id del registro |
| documento_id | uuid null | registro padre (pedido de la partida) para consultar todo un documento |
| accion | text | `Alta`, `Cambio`, `Baja` |
| cambios | jsonb | `{ "cantidad": [100, 80] }` (antes, después); en `Alta` solo después |
| usuario_id | uuid null | |
| dispositivo_id | uuid null | si vino de la app |
| origen | text | `Web`, `App`, `Sistema` |

Índices `ix_bitacora_tabla_entidad` (`tabla`, `entidad_id`, `fecha_hora`), `ix_bitacora_documento`,
`ix_bitacora_usuario_fecha`.

---

## Esquema `catalogos`

**unidades_medida**

| Columna | Tipo | Notas |
|---|---|---|
| id | uuid PK | |
| clave | text UX | `KG`, `G`, `TON`, `L`, `ML`, `PZA`, `PAQ`, `BUL`, `CAJ` |
| nombre | text | |
| tipo | text | `Masa`, `Volumen`, `Pieza` |
| clave_sat | text | `KGM`, `GRM`, `TNE`, `LTR`, `MLT`, `H87`, `XPK`, `XBG`, `XBX` |
| decimales | smallint | decimales permitidos en captura (KG = 3, PZA = 0) |
| activo | boolean | borrado lógico |

**productos**

| Columna | Tipo | Notas |
|---|---|---|
| id | uuid PK | |
| sku | text UX | |
| nombre | text | |
| descripcion | text null | |
| tipo | text | `MateriaPrima`, `Empaque`, `Semiterminado`, `Terminado`, `Reventa` |
| unidad_base_id | uuid FK | unidad en la que se lleva el inventario |
| es_comprable | boolean | por defecto según tipo (MateriaPrima, Empaque, Reventa) |
| es_producible | boolean | Semiterminado, Terminado |
| es_vendible | boolean | Terminado, Reventa (y Semiterminado si venden masa) |
| maneja_lote | boolean | `[SUP-02]` por defecto `true` en Terminado |
| vida_util_dias | integer null | tortilla 2 días, masa 1 día `[SUP-02]` |
| clave_prod_serv_sat | text null | se confirma con el contador `[SUP-03]` |
| objeto_impuesto_sat | text | `02` por defecto |
| tasa_iva | numeric(9,6) | tortilla y masa 0 %; ver `[SUP-03]` |
| tasa_ieps | numeric(9,6) null | botanas de alta densidad calórica 8 % `[SUP-03]` |
| codigo_barras | text null | |
| stock_minimo | numeric(18,4) null | alerta en tablero |
| nombre_corto | text null | **(v0.2)** máx. 15 caracteres ("T-800", "HNA", "FIT BLANCA"); encabezados de cuadrículas, app y liquidación. Vacío = `sku` |
| aplica_producto_a_cobro | boolean | **(v0.2)** la devolución fuera de tolerancia se carga al chofer (RN-LOG-24). Por defecto `false` |
| dias_tolerancia_devolucion | integer null | **(v0.2)** antigüedad máxima del lote devuelto sin cargo; nulo = parámetro `logistica.dias_tolerancia_devolucion` `[SUP-19]` |
| activo | boolean | |
| version_sync | bigint | baja a la app si `es_vendible` |

**productos_unidades** (conversiones por producto)

| Columna | Tipo | Notas |
|---|---|---|
| producto_id | uuid PK | |
| unidad_id | uuid PK | |
| factor_a_base | numeric(18,6) | 1 PAQ = 1 KG; 1 BUL = 20 KG; 1 TON = 1000 KG |
| uso_compra | boolean | |
| uso_venta | boolean | |
| uso_produccion | boolean | |
| predeterminada_venta | boolean | máximo una por producto |
| uso_traslado | boolean | **(v0.2)** unidad para surtir y cargar (caja) |
| predeterminada_traslado | boolean | **(v0.2)** máximo una por producto; con ella se muestran "N cajas + M paquetes" (RN-CAT-09) |

**motivos_merma**: `id`, `clave UX`, `nombre`, `aplica_produccion bool`, `aplica_almacen bool`,
`aplica_ruta bool`, `aplica_retorno bool`, `activo`.

**parametros**: `clave text PK`, `valor text`, `tipo text` (`int`, `decimal`, `bool`, `text`),
`descripcion text`. Ver valores por defecto al final.

---

## Esquema `compras`

**proveedores**: `id`, `codigo UX`, `razon_social`, `nombre_comercial null`, `rfc null` (12 o 13 caracteres),
`contacto null`, `telefono null`, `email null`, `dias_credito int`, `activo`.

**proveedores_productos** (catálogo del proveedor, RN-COM-10 a 12): `proveedor_id`, `producto_id` (PK), `unidad_id`
(unidad de compra), `clave_proveedor varchar(50) null`, `precio numeric(18,4) null` (pactado, sin IVA),
`dias_entrega int`, `preferido bool` (único parcial por `producto_id`), `activo bool`, `ultimo_precio`,
`ultima_unidad_id null`, `ultima_compra_en null`, auditoría.

**ordenes_compra**

| Columna | Tipo | Notas |
|---|---|---|
| id | uuid PK | |
| folio | text UX | `OC-000001` |
| proveedor_id | uuid FK | |
| fecha | date | |
| fecha_entrega_esperada | date null | |
| almacen_destino_id | uuid | almacén de inventario |
| estado | text | `Borrador`, `Autorizada`, `RecibidaParcial`, `Recibida`, `Cerrada`, `Cancelada` |
| subtotal, iva, total | numeric(18,2) | calculados |
| notas | text null | |
| autorizada_por, autorizada_en | uuid, timestamptz null | |
| xmin | concurrencia | |

**ordenes_compra_partidas**: `id`, `orden_compra_id`, `producto_id`, `unidad_id`,
`factor_a_base`, `cantidad`, `cantidad_base`, `precio_unitario`, `tasa_iva`, `importe`, `iva numeric(18,2)`,
`cantidad_recibida_base` (acumulada).

**recepciones**

| Columna | Tipo | Notas |
|---|---|---|
| id | uuid PK | |
| folio | text UX | `REC-000001` |
| orden_compra_id | uuid FK null | null = compra directa (requiere permiso) `[SUP-04]` |
| proveedor_id | uuid FK | |
| almacen_id | uuid | |
| fecha_operativa | date | |
| factura_proveedor | text null | folio o UUID de la factura del proveedor |
| estado | text | `Registrada`, `Cancelada` |
| notas | text null | |
| cancelada_en, cancelada_por | timestamptz, uuid null | |

**recepciones_partidas**: `id`, `recepcion_id`, `orden_compra_partida_id null`, `producto_id`,
`unidad_id`, `factor_a_base`, `cantidad`, `cantidad_base`, `precio_unitario numeric(18,4)` (sin IVA, unidad
capturada; alimenta el historial de precios), `costo_unitario_base numeric(18,6)`,
`lote text null`, `fecha_caducidad date null`.

---

## Esquema `produccion`

**lineas**: `id`, `codigo UX`, `nombre` ("Molino 1", "Tortilladora 2"), `tipo` (`Molino`,
`Tortilladora`, `Freidora`, `Empaque`, `Otro`), `activo`. `[SUP-05]`

**recetas**

| Columna | Tipo | Notas |
|---|---|---|
| id | uuid PK | |
| producto_id | uuid FK | producto que se obtiene |
| version | integer | UX (producto_id, version) |
| nombre | text | |
| rendimiento_cantidad | numeric(18,4) | en unidad base del producto |
| merma_esperada_pct | numeric(9,6) | 0.03 = 3 % |
| estado | text | `Borrador`, `Vigente`, `Obsoleta`; una sola `Vigente` por producto |
| vigente_desde | timestamptz null | |
| notas | text null | |
| xmin | concurrencia | índice único parcial `ux_recetas_producto_vigente` (`producto_id` donde `estado = 'Vigente'`, RN-PRO-01) |

**recetas_componentes**: `id`, `receta_id`, `producto_id`, `unidad_id`, `factor_a_base`,
`cantidad`, `cantidad_base`, `orden smallint`.

**ordenes_produccion**

| Columna | Tipo | Notas |
|---|---|---|
| id | uuid PK | |
| folio | text UX | `OP-000001` |
| fecha_operativa | date | |
| turno | text | `Matutino`, `Vespertino`, `Nocturno` `[SUP-05]` |
| receta_id | uuid FK | versión usada (no cambia después de liberar) |
| producto_id | uuid FK | |
| linea_id | uuid null | |
| cantidad_planeada | numeric(18,4) | unidad base |
| cantidad_producida | numeric(18,4) null | al terminar |
| almacen_consumo_id | uuid | de donde salen los componentes |
| almacen_destino_id | uuid | a donde entra el producto |
| lote | text | `''` hasta liberar; `AAMMDD-OP000001` al liberar |
| estado | text | `Planeada`, `Liberada`, `EnProceso`, `Terminada`, `Cancelada` |
| iniciada_en, terminada_en | timestamptz null | |
| responsable_id | uuid null | |
| costo_total | numeric(18,2) null | suma de consumos al terminar |
| costo_unitario | numeric(18,6) null | costo_total / cantidad_producida (RN-PRO-05) |
| liberada_en | timestamptz null | |
| notas | text null | |
| xmin | concurrencia | índice `ix_ordenes_produccion_fecha_operativa` |

**ordenes_produccion_consumos**: `id`, `orden_produccion_id`, `producto_id`,
`cantidad_teorica_base`, `cantidad_real_base null` (nulo = no capturado, RN-PRO-03), `lote_consumido null`,
`costo_unitario`.

**ordenes_produccion_mermas**: `id`, `orden_produccion_id`, `producto_id`, `cantidad_base`,
`motivo_merma_id`, `notas`, `registrada_en`.

---

## Esquema `inventario`

**almacenes**

| Columna | Tipo | Notas |
|---|---|---|
| id | uuid PK | |
| codigo | text UX | `MP`, `PT`, `R01`… |
| nombre | text | |
| tipo | text | `MateriaPrima`, `ProductoTerminado`, `General`, `Ruta` |
| ruta_id | uuid null | solo tipo `Ruta` (1 a 1 con `logistica.rutas`) |
| permite_negativo | boolean | `true` solo en `Ruta` (ventas offline) |
| activo | boolean | |

**existencias**: PK (`almacen_id`, `producto_id`, `lote`), `cantidad numeric(18,4)`,
`fecha_caducidad date null` (FEFO, RN-INV-07; se copia en traspasos), `fecha_entrada timestamptz` (FIFO cuando no
hay caducidad), `actualizado_en`, `xmin`. `lote = ''` cuando el producto no maneja lote.

**costos_producto**: `producto_id PK`, `costo_promedio numeric(18,6)`, `actualizado_en`.
Costo promedio ponderado global por producto `[SUP-06]`.

**movimientos** (kardex, inmutable)

| Columna | Tipo | Notas |
|---|---|---|
| id | uuid PK | |
| fecha_hora | timestamptz | |
| fecha_operativa | date | |
| tipo | text | ver tipos abajo |
| almacen_id | uuid | |
| producto_id | uuid | |
| lote | text | |
| cantidad | numeric(18,4) | con signo: + entrada, − salida (unidad base) |
| costo_unitario | numeric(18,6) | |
| costo_total | numeric(18,2) | |
| existencia_resultante | numeric(18,4) | saldo del almacén/producto/lote después del movimiento |
| documento_tipo | text | `Recepcion`, `OrdenProduccion`, `Envio`, `Venta`, `Retorno`, `Liquidacion`, `Ajuste`, `Traspaso`, `ConteoFisico` |
| documento_id | uuid | |
| documento_folio | text | |
| motivo_merma_id | uuid null | |
| usuario_id | uuid | |
| dispositivo_id | uuid null | |
| notas | text null | |
| fecha_caducidad | date null | caducidad del lote al momento del movimiento |

Índices: (`producto_id`, `almacen_id`, `fecha_hora`), (`documento_tipo`, `documento_id`), (`fecha_operativa`).

Los ajustes manuales no tienen tabla propia: son movimientos con `documento_tipo = Ajuste`, folio `AJ-000001`
(secuencia `inventario.folio_aj_seq`), motivo y nota obligatorios (RN-INV-08).

Tipos de movimiento: `EntradaCompra`, `DevolucionProveedor`, `EntradaProduccion`,
`SalidaConsumo`, `Merma`, `TraspasoSalida`, `TraspasoEntrada`, `Venta`, `RetornoCliente`,
`AjusteEntrada`, `AjusteSalida`, **`SalidaCambio` (v0.2)**: producto nuevo que se entrega al cliente a cambio
de producto retirado, sin venta (RN-INV-11).

**traspasos** (entre almacenes no-ruta): `id`, `folio TR-`, `almacen_origen_id`,
`almacen_destino_id`, `fecha_operativa`, `estado` (`Registrado`, `Cancelado`), partidas
**traspasos_partidas**: `traspaso_id`, `producto_id`, `lote`, `cantidad_base`.

**conteos_fisicos**: `id`, `folio CF-`, `almacen_id`, `fecha_operativa`, `estado` (`Abierto`,
`Cerrado`, `Cancelado`), `notas`, `cerrado_en`, `cerrado_por`, `xmin`. **conteos_partidas**: `conteo_id`, `producto_id`,
`lote`, `cantidad_sistema`, `cantidad_contada`, `diferencia`, `capturado_en`.

---

## Esquema `logistica`

**vehiculos**: `id`, `placas UX`, `descripcion`, `capacidad_kg null`, `activo`.

**rutas**

| Columna | Tipo | Notas |
|---|---|---|
| id | uuid PK | |
| codigo | text UX | `R01` (se usa en folios). **(v0.2)** Códigos del cliente sin guion: `R1`–`R14` autoservicio, `R51`… detalle, `R101`… institucional, `R151`… CEDIS |
| nombre | text | |
| almacen_id | uuid | almacén tipo `Ruta` |
| canal_id | uuid | **(v0.2)** canal de la ruta (`ventas.canales`); define formato de liquidación y si aplica producto a cobro |
| vendedor_id | uuid null | usuario con rol Vendedor. **(v0.2)** = *chofer predeterminado*: se propone al crear la carga (RN-LOG-12) |
| vehiculo_id | uuid null | **(v0.2)** vehículo predeterminado; se propone al crear la carga |
| activo | boolean | |
| version_sync | bigint | |
| xmin | concurrencia | el código no se cambia después de crear la ruta |

**rutas_clientes**: PK (`ruta_id`, `cliente_id`), `orden_visita smallint`,
`dias_visita smallint[]` (1 = lunes … 7 = domingo), `activo`, `version_sync`.

**envios** (carga de camioneta)

| Columna | Tipo | Notas |
|---|---|---|
| id | uuid PK | |
| folio | text UX | `ENV-000001` |
| fecha_operativa | date | |
| ruta_id | uuid | |
| tipo | text | `CargaInicial`, `Recarga` |
| chofer_id | uuid | **(v0.2)** usuario con rol Vendedor que lleva la carga (RN-LOG-12). Migración: se llena con `rutas.vendedor_id` |
| vehiculo_id | uuid null | **(v0.2)** |
| almacen_origen_id | uuid | normalmente `PT` |
| almacen_destino_id | uuid | almacén de la ruta |
| estado | text | `Preparado`, `EnRuta`, `Liquidado`, `Cancelado` |
| despachado_en | timestamptz null | al pasar a `EnRuta` |
| liquidacion_id | uuid null | |
| version_sync | bigint | baja a la app |
| notas | text null | |
| xmin | concurrencia | índice `ix_envios_ruta_fecha` |

**envios_partidas**: `id`, `envio_id`, `producto_id`, `lote`, `cantidad_base`, `version_sync`. En `Preparado`,
`lote = ''` significa "FEFO al despachar".

**retornos**

| Columna | Tipo | Notas |
|---|---|---|
| id | uuid PK | generado por la app si viene de ruta |
| folio | text UX | `R01-D03-000045` (app) o `RET-000001` (web) |
| op_id | uuid UX null | idempotencia de sync |
| tipo | text | `DevolucionCliente`, `RechazoEntrega`, **`CambioFisico` (v0.2)** |
| ruta_id | uuid | |
| cliente_id | uuid null | |
| venta_id | uuid null | venta original si aplica |
| fecha_operativa | date | |
| fecha_hora_dispositivo | timestamptz null | |
| dispositivo_id | uuid null | |
| latitud, longitud | numeric(9,6) null | |
| precision_m | numeric(9,2) null | |
| estado | text | `Registrado`, `Cancelado` |
| notas | text null | índice único filtrado `ux_retornos_op_id` |

**retornos_partidas**: `id`, `retorno_id`, `producto_id`, `lote`, `cantidad_base`,
`condicion` (`Vendible`, `NoVendible`), `motivo_merma_id null`,
**`sentido` (v0.2)** (`Retirado` = entra a la camioneta, `Entregado` = sale de la camioneta a cambio; en
`DevolucionCliente`/`RechazoEntrega` siempre `Retirado`), **`cambio_pendiente_id uuid null` (v0.2)** (partida
`Entregado` que salda un pendiente).

**cambios_pendientes (v0.2)** — cambios que se le deben a un cliente (RN-LOG-16).

| Columna | Tipo | Notas |
|---|---|---|
| id | uuid PK | |
| cliente_id | uuid | |
| ruta_id | uuid | ruta donde se generó |
| producto_id | uuid | |
| cantidad_base | numeric(18,4) | lo que se quedó a deber |
| cantidad_saldada_base | numeric(18,4) | acumulado de entregas posteriores |
| retorno_origen_id | uuid | retorno `CambioFisico` que lo generó |
| fecha_operativa | date | |
| estado | text | `Pendiente`, `Saldado`, `Cancelado` |
| cancelado_por, cancelado_en, motivo_cancelacion | null | cancelar requiere `logistica.cambios_cancelar` |
| version_sync | bigint | baja a la app de la ruta del cliente |
| xmin | concurrencia | índice `ix_cambios_pendientes_cliente_estado` |

**liquidaciones**

| Columna | Tipo | Notas |
|---|---|---|
| id | uuid PK | |
| folio | text UX | `LIQ-000001` |
| ruta_id | uuid | |
| fecha_operativa | date | UX (ruta_id, fecha_operativa) |
| chofer_id | uuid | **(v0.2)** chofer de las cargas del día (RN-LOG-13) |
| formato | text | **(v0.2)** `Completo`, `Simplificado` (copiado del canal de la ruta al abrirse, RN-LOG-19) |
| estado | text | `Abierta`, `Cerrada`. **(v0.2)** `Abierta` → `ProductoConfirmado` → `Revisada` → `Cerrada` (RN-LOG-20) |
| efectivo_esperado | numeric(18,2) | ventas contado + cobros en efectivo. **(v0.2)** − gastos autorizados (RN-LOG-08) |
| efectivo_entregado | numeric(18,2) null | |
| diferencia_efectivo | numeric(18,2) null | |
| total_venta | numeric(18,2) null | **(v0.2)** snapshot al confirmar producto |
| total_credito | numeric(18,2) null | **(v0.2)** ventas a crédito del día (solo lectura, de Ventas) |
| total_cobros | numeric(18,2) null | **(v0.2)** cobros del día por forma de pago (solo lectura, de Ventas) |
| total_transferencias | numeric(18,2) null | **(v0.2)** ventas y cobros con forma de pago `Transferencia` (solo lectura) |
| total_cheques | numeric(18,2) null | **(v0.2)** ventas con forma de pago `Cheque` (los cobros no cambian) |
| total_gastos | numeric(18,2) | **(v0.2)** suma de `liquidaciones_gastos` autorizados |
| diferencia_precios | numeric(18,2) | **(v0.2)** Σ (precio de lista − precio cobrado) × cantidad de las ventas con `PrecioDistinto` (RN-LOG-23) |
| producto_confirmado_por, producto_confirmado_en | uuid, timestamptz null | **(v0.2)** almacén |
| revisada_por, revisada_en | uuid, timestamptz null | **(v0.2)** supervisor |
| cerrada_por, cerrada_en | | liquidaciones (caja) |
| cerrada_forzada | boolean | cerrada con pendientes de sincronizar (RN-LOG-09) |
| notas | text null | |
| xmin | concurrencia | |

**liquidaciones_partidas**: `id`, `liquidacion_id`, `producto_id`, `lote` (UX con los dos anteriores), `cargado`,
`vendido`, `devuelto_clientes_vendible`, `devuelto_clientes_no_vendible`, `sobrante_esperado`,
`sobrante_contado null`, `diferencia null`, `destino_sobrante` (`Reintegrar`, `Merma`, `Reproceso`),
`motivo_merma_id null`. **(v0.2)** Se agregan: `cargado_inicial`, `recargado` (`cargado` = suma),
`cambios_entregados` (salidas `SalidaCambio`), `cantidad_a_cobro numeric(18,4) null` (RN-LOG-24; `null` = no capturada, se usa la sugerida),
`precio_a_cobro numeric(18,4) null`, `importe_venta numeric(18,2)` (columna "Total $" del formato).

**liquidaciones_gastos (v0.2)**: `id`, `liquidacion_id`, `concepto text` (diésel, casetas, maniobras…),
`importe numeric(18,2)`, `comprobante text null`, `autorizado boolean`, `capturado_por`, `capturado_en`.

**cargos_chofer (v0.2)** — cuenta del chofer (RN-LOG-25).

| Columna | Tipo | Notas |
|---|---|---|
| id | uuid PK | |
| folio | text UX | `CCH-000001` |
| chofer_id | uuid | |
| tipo | text | `ProductoACobro`, `FaltanteProducto`, `FaltanteEfectivo`, `DiferenciaPrecio`, `Otro` |
| liquidacion_id | uuid null | origen |
| producto_id | uuid null | |
| cantidad_base | numeric(18,4) null | |
| precio_unitario | numeric(18,4) null | |
| importe | numeric(18,2) | positivo = se le cobra al chofer |
| fecha_operativa | date | |
| estado | text | `Pendiente`, `Aplicado` (incluido en un corte), `Cancelado` |
| corte_id | uuid null | |
| notas | text null | `Otro` exige nota |
| cancelado_por, cancelado_en, motivo_cancelacion | null | |
| xmin | concurrencia | índice `ix_cargos_chofer_chofer_estado` |

**cortes_chofer (v0.2)** — corte semanal para comisiones (RN-LOG-28).

| Columna | Tipo | Notas |
|---|---|---|
| id | uuid PK | |
| folio | text UX | `CSC-000001` |
| chofer_id | uuid | |
| desde, hasta | date | semana operativa; UX (`chofer_id`, `desde`) |
| total_venta | numeric(18,2) | ventas registradas del chofer en el periodo (snapshot) |
| total_cargos | numeric(18,2) | cargos aplicados |
| estado | text | `Abierto`, `Cerrado` |
| cerrado_por, cerrado_en | null | |
| xmin | concurrencia | |

Folios `ENV-`, `RET-`, `LIQ-`, **`CCH-`, `CSC-` (v0.2)**.

---

## Esquema `ventas`

**canales**: `id`, `clave UX` (`INST`, `AUTO`, `TRAD`), `nombre`, `requiere_factura_default`, `activo`.
**(v0.2)** Claves `AUTO` Autoservicio, `DET` Detalle (renombra `TRAD`; la migración cambia clave y nombre y el
importador acepta ambas), `INST` Institucional, `CEDIS` CEDIS. Columnas nuevas:

| Columna | Tipo | Notas |
|---|---|---|
| formato_liquidacion | text | `Completo` (AUTO, DET) o `Simplificado` (INST, CEDIS) — RN-LOG-19 |
| aplica_producto_a_cobro | boolean | `true` en AUTO y DET (RN-LOG-24) |
| pedido_por | text | `Cliente` (AUTO, INST, CEDIS: un pedido por tienda/cliente) o `Ruta` (DET: pedido global al genérico) — RN-VEN-16 |
| orden | smallint | orden de las pestañas del estimado |

**grupos_clientes (v0.2)** — corporativos (Walmart, Chedraui, La Comer, City Market…): `id`, `clave UX`,
`nombre`, `canal_id`, `lista_precio_id null` (lista que hereda el cliente nuevo del grupo), `activo`, `version_sync`.

**precios_cliente (v0.2)** — precio especial que gana sobre la lista (RN-VEN-12): PK (`cliente_id`,
`producto_id`, `unidad_id`), `precio_unitario numeric(18,4)` sin impuestos, `vigente_desde date`, `activo`,
`version_sync`.

**productos_exclusivos (v0.2)** — producto que solo se vende a ciertos clientes (RN-VEN-14): `id`, `producto_id`,
`cliente_id null`, `grupo_cliente_id null` (uno de los dos), `activo`, `version_sync`. UX
(`producto_id`, `cliente_id`, `grupo_cliente_id`).

**listas_precio**: `id`, `nombre`, `canal_id null`, `vigente_desde date`, `activo`,
`version_sync`. **listas_precio_partidas**: PK (`lista_precio_id`, `producto_id`, `unidad_id`),
`precio_unitario numeric(18,4)` **sin impuestos** `[SUP-07]`, `version_sync`.

**clientes**

| Columna | Tipo | Notas |
|---|---|---|
| id | uuid PK | |
| codigo | text UX | |
| nombre | text | nombre comercial ("Abarrotes Lupita") |
| canal_id | uuid | |
| grupo_cliente_id | uuid null | **(v0.2)** corporativo |
| lista_precio_id | uuid | |
| condicion_pago | text | `Contado`, `Credito` |
| dias_credito | integer | `[SUP-08]` |
| limite_credito | numeric(18,2) null | |
| requiere_factura | boolean | |
| rfc, razon_social, regimen_fiscal, codigo_postal_fiscal, uso_cfdi, email_facturacion | text null | datos para CFDI 4.0 |
| telefono | text null | |
| calle, colonia, municipio, estado, codigo_postal | text null | |
| latitud, longitud | numeric(9,6) null | |
| ubicacion_aprobada | boolean | la primera captura en ruta queda por aprobar |
| latitud_propuesta, longitud_propuesta | numeric(9,6) null | ubicación capturada en ruta pendiente de aprobar |
| ruta_generico | text null | UX filtrado: código de la ruta del cliente genérico (`GEN-<ruta>`) |
| es_generico | boolean | "Público en general" por ruta |
| activo | boolean | |
| version_sync | bigint | |
| xmin | concurrencia | |

**pedidos** (institucional y autoservicio): `id`, `folio PED-`, `cliente_id`, `fecha`,
`fecha_entrega`, `estado` (`Capturado`, `Confirmado`, `Asignado`, `Entregado`,
`EntregadoParcial`, `Cancelado`), `envio_id null`, `ruta_id null`, `orden_compra_cliente text null`,
`subtotal`, `iva`, `ieps`, `total`, `notas null`, `version_sync`, `xmin`. **pedidos_partidas**: `id`, `pedido_id`,
`producto_id`, `unidad_id`, `factor_a_base`, `cantidad`, `cantidad_base`, `precio_unitario`,
`tasa_iva`, `tasa_ieps null`, `importe`, `cantidad_entregada_base`.

**(v0.2)** `pedidos` agrega: `origen` (`Web`, `App`), `op_id uuid UX null` (idempotencia de `CapturarPedido`),
`capturado_por uuid`, `dispositivo_id uuid null`, `confirmado_por uuid null`, `confirmado_en timestamptz null`,
`es_ruta boolean` (pedido global de detalle al genérico, RN-VEN-16). Índice `ix_pedidos_fecha_entrega_estado`.
`pedidos_partidas` agrega: `tipo` (`Venta`, `CambioFisico`; las de cambio llevan precio 0 y no suman al total,
RN-VEN-17), `cantidad_solicitada numeric(18,4) null` (lo que capturó el vendedor antes de validarse, RN-VEN-18),
`notas text null` (observaciones del estimado).

**ventas**

| Columna | Tipo | Notas |
|---|---|---|
| id | uuid PK | generado por la app |
| op_id | uuid UX null | idempotencia |
| folio | text UX | `R01-D03-000123` (app) o `VTA-000001` (web) |
| origen | text | `App`, `Web` |
| tipo | text | `VentaRuta`, `EntregaPedido` |
| pedido_id | uuid null | |
| cliente_id | uuid | |
| ruta_id | uuid null | |
| almacen_id | uuid | almacén de donde sale (ruta) |
| dispositivo_id | uuid null | |
| vendedor_id | uuid | |
| fecha_hora_dispositivo | timestamptz null | |
| fecha_hora_servidor | timestamptz | |
| fecha_operativa | date | |
| condicion_pago | text | `Contado`, `Credito` |
| forma_pago | text | `Efectivo`, `Transferencia`, `Credito`, **`Cheque` (v0.2)** (no suma al efectivo esperado) |
| subtotal, iva, ieps, total | numeric(18,2) | recalculados por el servidor |
| latitud, longitud | numeric(9,6) null | |
| precision_m | numeric(9,2) null | |
| distancia_cliente_m | numeric(9,2) null | calculada por el servidor |
| requiere_revision | boolean | |
| estado | text | `Registrada`, `Cancelada` |
| motivo_cancelacion | text null | |
| factura_id | uuid null | factura individual o global que la incluye |
| total_app | numeric(18,2) null | total que calculó la app (RN-VEN-02) |
| cancelada_en, cancelada_por | timestamptz, uuid null | |

Índices únicos `ux_ventas_folio` y `ux_ventas_op_id` (filtrado).

**ventas_partidas**: `id`, `venta_id`, `producto_id`, `unidad_id`, `factor_a_base`,
`cantidad`, `cantidad_base`, `lote` (si FEFO repartió en varios lotes van separados por coma; el detalle exacto está
en el kardex), `precio_unitario`, `tasa_iva`, `tasa_ieps`, `importe`, `iva`, `ieps`, `pedido_partida_id null`.
**(v0.2)** `precio_referencia numeric(18,4) null`: precio vigente del cliente (especial o lista) al aplicar la venta;
se llena siempre en ventas de la app (RN-VEN-12, RN-LOG-23).

**cobros** `[SUP-08]`: `id`, `op_id UX null`, `folio`, `cliente_id`, `ruta_id null`,
`dispositivo_id null`, `fecha_operativa`, `forma_pago`, `importe`, `estado` (`Registrado`, `Cancelado`); folio `COB-`.
**cobros_aplicaciones**: `cobro_id`, `venta_id`, `importe`.

**incidencias**: `id`, `tipo` (`ExistenciaNegativaRuta`, `GpsFueraDeRango`, `GpsImpreciso`,
`SinGps`, `PrecioDistinto`, `UbicacionPorAprobar`, `DiferenciaLiquidacion`, `CreditoExcedido` (RN-VEN-04),
`TotalDistinto` (RN-VEN-02), `ProductoExclusivo` (RN-VEN-14, [SUP-28])), `entidad_tipo`, `entidad_id`, `detalle jsonb`, `estado` (`Abierta`, `Revisada`),
`revisada_por`, `revisada_en`, `notas_revision null`.

---

## Esquema `facturacion`

**emisor** (una fila): `id`, `rfc`, `razon_social`, `regimen_fiscal`, `codigo_postal_expedicion`,
`serie_facturas`, `serie_global`, `serie_notas_credito`, `es_ejemplo boolean` (semilla con el RFC de pruebas del SAT).

**solicitudes_autofactura**: `id`, `folio_ticket`, `venta_id null` (rechazo sin ticket), `rfc`, `razon_social`,
`regimen_fiscal`, `codigo_postal`, `uso_cfdi`, `email`, `ip_origen`, `estado` (`Recibida`,
`Rechazada`, `EnProceso`, `Timbrada`, `Error`), `motivo_rechazo null`, `factura_id null`.

**facturas**

| Columna | Tipo | Notas |
|---|---|---|
| id | uuid PK | |
| tipo | text | `Ingreso`, `Global`, `NotaCredito` |
| serie, folio | text | |
| uuid_fiscal | uuid null | al timbrar |
| cliente_id | uuid null | |
| receptor_rfc, receptor_nombre, receptor_regimen, receptor_cp, uso_cfdi | text | snapshot |
| metodo_pago_sat | text | `PUE`, `PPD` |
| forma_pago_sat | text | `01` efectivo, `03` transferencia, `99` por definir |
| periodicidad, meses, anio | text null | solo `Global` |
| subtotal, iva, ieps, total | numeric(18,2) | |
| estado | text | `Pendiente`, `EnviadaProveedor`, `Timbrada`, `Error`, `Cancelada` |
| proveedor | text | `Simulado` hasta resolver D-06 |
| xml_url, pdf_url | text null | Spaces |
| error_detalle | text null | |
| intentos | integer | |
| email_receptor | text null | |
| fecha_operativa | date | |
| timbrada_en, cancelada_en | timestamptz null | |
| motivo_cancelacion | text null | motivo SAT 01–04 |
| xmin | concurrencia | |

`folio` es consecutivo por tipo (`facturacion.folio_ingreso_seq`, `facturacion.folio_global_seq`) con la serie del
emisor. Índices `ux_facturas_serie_folio`, `ux_facturas_uuid_fiscal` (parcial), `ix_facturas_fecha_operativa`.

**facturas_ventas**: `factura_id`, `venta_id` (PK), `venta_folio`. Se conservan al cancelar; la vigencia la marca
`ventas.factura_id` (RN-FAC-01).
**facturas_conceptos**: `id`, `factura_id`, `venta_id null`, `producto_id null`, `orden`, snapshot de partidas
(`clave_prod_serv`, `clave_unidad`, `no_identificacion`, `cantidad`, `descripcion`, `valor_unitario numeric(18,4)`,
`importe`, `objeto_imp`, `tasa_iva`, `tasa_ieps null`, `iva`, `ieps`).

**outbox**: `id`, `tipo` (`TimbrarFactura`, `CancelarFactura`), `payload jsonb`, `estado`
(`Pendiente`, `EnProceso`, `Procesado`, `Error`), `intentos`, `siguiente_intento_en`,
`procesado_en`, `error`, `factura_id`, `creado_en`; índice `ix_outbox_estado_siguiente`.

---

## Esquema `sync`

**version_seq**: secuencia global para `version_sync`.

**operaciones**: `op_id uuid PK`, `dispositivo_id`, `lote_id`, `tipo`, `secuencia_dispositivo bigint`,
`creada_en`, `recibida_en`, `estado` (`Aplicada`, `Rechazada`), `resultado jsonb`, `codigo null`, `error text null`.

**lotes**: `id`, `dispositivo_id`, `usuario_id`, `recibido_en`, `total_operaciones`, `aplicadas`, `rechazadas`,
`duplicadas`, `version_app`.

---

## Parámetros por defecto (`catalogos.parametros`)

| Clave | Valor | Supuesto |
|---|---|---|
| `zona_horaria` | `America/Mexico_City` | |
| `gps.umbral_distancia_m` | `150` | `[SUP-09]` |
| `gps.precision_maxima_m` | `100` | `[SUP-09]` |
| `ruta.permite_existencia_negativa` | `true` | `[SUP-10]` |
| `ruta.dias_retencion_local` | `7` | días que la app conserva operaciones ya sincronizadas |
| `autofactura.limite` | `fin_de_mes` | `[SUP-11]` |
| `factura_global.periodicidad` | `Mensual` | `[SUP-11]` |
| `produccion.permitir_consumo_sin_existencia` | `false` | `[SUP-12]` |
| `produccion.umbral_variacion_pct` | `0.05` | |
| `compras.tolerancia_pct` | `0.05` | `[SUP-04]` |
| `inventario.metodo_costeo` | `PromedioPonderado` | `[SUP-06]` |
| `logistica.dias_tolerancia_devolucion` | `11` | **(v0.2)** `[SUP-19]` |
| `logistica.precio_producto_a_cobro` | `ListaCanal` | **(v0.2)** `ListaCanal` o `Costo` `[SUP-20]` |
| `logistica.cargo_faltante_producto` | `true` | **(v0.2)** faltante de producto en liquidación genera cargo al chofer `[SUP-21]` |
| `logistica.cargo_faltante_efectivo` | `true` | **(v0.2)** `[SUP-21]` |
| `logistica.requiere_revision_supervisor` | `true` | **(v0.2)** etapa `Revisada` obligatoria (RN-LOG-20) |
| `logistica.dia_inicio_semana_corte` | `1` | **(v0.2)** 1 = lunes `[SUP-22]` |
| `ventas.dias_estimado_referencia` | `7` | **(v0.2)** el estimado sin pedido toma el de hace N días (RN-PRO-12) `[SUP-23]` |
| `ruta.alerta_cantidad_factor` | `3` | **(v0.2)** la app pide confirmar si la cantidad supera N veces el promedio (RN-APP-09) |
| `ruta.alerta_cantidad_maxima` | `500` | **(v0.2)** tope absoluto por partida en unidad de venta que pide confirmación |
| `ruta.consumo_promedio_visitas` | `8` | **(v0.2)** ventas/entregas que promedia `consumos_clientes` (RN-APP-09) |

## Datos semilla (demo y pruebas)

Valores **de ejemplo**; reemplazar con los reales del cliente.

**Unidades:** KG, G, TON (1000 KG), L, ML, PZA, PAQ, BUL, CAJ.

**Almacenes:** `MP` Materias primas, `PT` Producto terminado, `R01`–`R03` (rutas).

| SKU | Producto | Tipo | Unidad base | Conversiones | IVA |
|---|---|---|---|---|---|
| MP-MAIZ-BCO | Maíz blanco | MateriaPrima | KG | TON = 1000 (compra) | 0 % |
| MP-CAL | Cal (hidróxido de calcio) | MateriaPrima | KG | BUL = 25 (compra) | 16 % |
| MP-HARINA | Harina de maíz nixtamalizada | MateriaPrima | KG | BUL = 20 (compra) | 0 % |
| MP-CONSERV | Conservador | MateriaPrima | KG | | 16 % |
| MP-ACEITE | Aceite vegetal | MateriaPrima | L | CAJ = 12 (compra) | 0 % |
| MP-SAL | Sal | MateriaPrima | KG | | 0 % |
| EMP-BOLSA-1K | Bolsa para 1 kg | Empaque | PZA | CAJ = 1000 (compra) | 16 % |
| EMP-PAPEL | Papel estraza | Empaque | KG | | 16 % |
| SEM-MASA | Masa de maíz | Semiterminado | KG | | 0 % |
| PT-TORT-KG | Tortilla de maíz | Terminado | KG | PAQ = 1 (venta) | 0 % |
| PT-MASA-KG | Masa para venta | Terminado | KG | | 0 % |
| PT-TOSTADA | Tostada (paquete 30 pz) | Terminado | PAQ | | 0 % |
| PT-TOTOPO | Totopo (bolsa 500 g) | Terminado | PAQ | | 16 % + IEPS 8 % |
| RV-SALSA | Salsa embotellada | Reventa | PZA | CAJ = 12 (compra) | 16 % |

**Recetas de ejemplo** (no son recetas reales):

| Receta | Rendimiento | Componentes | Merma esperada |
|---|---|---|---|
| Masa de maíz v1 | 160 KG de SEM-MASA | 100 KG MP-MAIZ-BCO, 1 KG MP-CAL | 2 % |
| Tortilla de maíz v1 | 88 KG de PT-TORT-KG | 100 KG SEM-MASA, 0.3 KG MP-CONSERV, 88 PZA EMP-BOLSA-1K | 3 % |
| Masa para venta v1 | 100 KG de PT-MASA-KG | 100 KG SEM-MASA | 0 % |
| Tostada v1 | 60 PAQ de PT-TOSTADA | 50 KG SEM-MASA, 6 L MP-ACEITE | 4 % |

**Motivos de merma:** Masa pasada/agria, Tortilla rota o deforme, Falla de máquina, Producto
caducado, Contaminación, Devolución no vendible, Daño en transporte, Faltante de ruta, Reproceso (RN-LOG-07,
`[SUP-13]`), Otro.

**Folios por módulo:** `OC-`, `REC-` (compras); `OP-` (producción); `VTA-`, `PED-`, `COB-`, `CLI-` (ventas);
`AJ-`, `TR-`, `CF-` (inventario).

**Vehículos y rutas de ejemplo:** `XA-1234-A`, `XB-5678-B`, `XC-9012-C`; R01 Centro (`vendedor1`), R02 Norte
(`vendedor2`), R03 Sur (`vendedor3`), cada una con su almacén, su cliente genérico y 5 clientes.

**Líneas de producción de ejemplo:** `MOL-1`, `TOR-1`, `TOR-2`, `FRE-1`, `EMP-1`. **Proveedores de ejemplo:**
GRANOS, QUIMICA, EMPAQUES.

**Folios:** secuencia por prefijo `<esquema>.folio_<prefijo>_seq` creada en la migración de cada módulo.

**Canales:** INST, AUTO, TRAD. **(v0.2)** AUTO, DET, INST, CEDIS con los valores de `formato_liquidacion`,
`aplica_producto_a_cobro` y `pedido_por` de arriba; grupos de ejemplo WALMART, CHEDRAUI, LACOMER, CITY (canal AUTO);
rutas de ejemplo `R1` (AUTO), `R51` (DET), `R101` (INST), `R151` (CEDIS). **Listas de precio:** una por canal. **Clientes:** 5 por ruta, más un
"Público en general" genérico por ruta. **Usuarios:** uno por rol (contraseña de desarrollo).
