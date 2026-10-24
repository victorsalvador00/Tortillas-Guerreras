# Módulo Logística (LOG)

Mueve el producto del almacén a las rutas y cierra cada día de ruta con una liquidación que cuadra
producto y efectivo.

## Casos de uso
1. Administrar vehículos y rutas (con su almacén tipo `Ruta`, vendedor y clientes en orden de visita).
2. Preparar la carga inicial del día y recargas (envíos), despacharlas.
3. Asignar pedidos institucionales/autoservicio a una ruta o envío.
4. Registrar retornos de clientes (desde la app o la web).
5. Liquidar la ruta al final del día: contar sobrante, recibir efectivo, cerrar.

## Envíos (cargas)

| Estado | Pasa a | Efecto |
|---|---|---|
| Preparado | EnRuta, Cancelado | editable |
| EnRuta | Liquidado | al despachar: `TraspasoSalida` de PT y `TraspasoEntrada` al almacén de la ruta; baja a la app |
| Liquidado | — | se asocia a la liquidación del día |
| Cancelado | — | solo desde `Preparado` |

## Reglas
- **RN-LOG-01** Una ruta tiene exactamente un almacén tipo `Ruta`; se crea junto con la ruta.
- **RN-LOG-02** Despachar un envío exige existencia suficiente en el almacén origen (RN-INV-02) y
  respeta FEFO para lotes.
- **RN-LOG-03** Solo puede haber una liquidación `Abierta` por ruta y fecha operativa; se crea al
  despachar la primera carga del día.
- **RN-LOG-04** Retorno `DevolucionCliente`: genera `RetornoCliente` (+) en el almacén de la ruta.
  Partidas `NoVendible` generan además `Merma` (−) en ese almacén con el motivo indicado.
  `[SUP-13]` por defecto tortilla devuelta = `NoVendible`.
- **RN-LOG-05** Retorno `RechazoEntrega` (el cliente no recibe un pedido): no hay venta; el
  producto sigue en el almacén de la ruta.
- **RN-LOG-06** Liquidación, por producto y lote:
  `sobrante_esperado = cargado − vendido + devuelto_clientes_vendible` (lo no vendible ya salió
  como merma). `diferencia = sobrante_contado − sobrante_esperado`.
  **(v0.2)** `cargado = cargado_inicial + recargado` y se restan los cambios entregados:
  `sobrante_esperado = cargado − vendido − cambios_entregados + devuelto_clientes_vendible`. Lo retirado en un
  cambio físico cuenta como devolución (vendible o no vendible según su condición).
- **RN-LOG-07** Al cerrar la liquidación:
  sobrante `Reintegrar` → traspaso del almacén de ruta a `PT`;
  sobrante `Merma`/`Reproceso` → `Merma` con motivo (reproceso como merma con motivo "Reproceso"
  hasta definir flujo `[SUP-13]`);
  diferencia ≠ 0 → `AjusteSalida`/`AjusteEntrada` con motivo "Faltante de ruta" e incidencia
  `DiferenciaLiquidacion`.
  Al terminar, el almacén de la ruta debe quedar en cero.
  **(v0.2)** Estos movimientos se generan al **confirmar el producto** (`Abierta → ProductoConfirmado`), no al
  cerrar el dinero; desde ese momento el sobrante reintegrado está disponible en `PT` (decisión V2-08).
- **RN-LOG-08** `efectivo_esperado` = ventas de contado en efectivo + cobros en efectivo del día.
  `diferencia_efectivo` = entregado − esperado; ≠ 0 genera incidencia.
  **(v0.2)** `efectivo_esperado` = ventas de contado en efectivo + cobros en efectivo − gastos autorizados.
  Ventas con forma de pago `Transferencia` o `Cheque` no suman al efectivo. Una `diferencia_efectivo` negativa
  genera además el cargo `FaltanteEfectivo` al chofer (RN-LOG-26).
- **RN-LOG-09** No se puede cerrar la liquidación si el dispositivo de la ruta tiene operaciones
  pendientes de sincronizar reportadas en su último contacto (`sync` lo informa); se puede forzar
  con permiso `logistica.forzar_cierre`. **(v0.2)** La validación se hace al **confirmar producto** (que es cuando
  se mueve el inventario) y usa los dispositivos del chofer de la liquidación; `cerrada_forzada` indica que esa
  confirmación se forzó. `logistica.forzar_cierre` lo tienen Supervisor y Administrador.
- **RN-LOG-10** Una liquidación cerrada no se reabre; correcciones posteriores son ajustes.
  **(v0.2)** Tampoco regresa de `ProductoConfirmado` a `Abierta`: una corrección de producto después de confirmar
  es un ajuste de inventario con motivo y, si aplica, un cargo o cancelación de cargo al chofer.
- **RN-LOG-11** Una carga puede llevar pedidos `Confirmado`/`Asignado` de su misma ruta (`pedidoIds` al crearla).
  La pantalla "Nueva carga" ofrece los pedidos `Asignado` de la ruta sin carga y con entrega
  (`fecha_entrega`, o `fecha` si no tiene) hasta la fecha operativa; lo pendiente de entregar de los
  elegidos se suma a las partidas, más el producto adicional para venta en ruta. Un pedido va en una
  sola carga (VEN-018 si es de otra ruta o ya va en otra carga). Al cancelar la carga sus pedidos
  quedan `Asignado` a la ruta, sin carga.

## Cambios v0.2 (reunión del 24-oct-2026)

### Casos de uso nuevos o modificados
1. Al crear una carga se eligen **ruta, chofer y vehículo** (se proponen los predeterminados de la ruta).
2. **Generar cargas sugeridas** del día desde los pedidos confirmados (RN-LOG-14).
3. Registrar **cambios físicos** (app o web) y consultar/saldar **cambios pendientes** por cliente.
4. Liquidar en **etapas**: almacén confirma producto → supervisor revisa → liquidaciones recibe el dinero y cierra.
5. Capturar **gastos de ruta** y **producto a cobro** en la liquidación.
6. Llevar la **cuenta del chofer** (cargos) y hacer el **corte semanal**.

### Reglas nuevas
- **RN-LOG-12** Toda carga tiene `chofer_id` (usuario activo con rol Vendedor, `LOG-017`) y puede tener
  `vehiculo_id` (activo). Se proponen `rutas.vendedor_id` y `rutas.vehiculo_id`; se pueden cambiar mientras la
  carga está `Preparado`.
- **RN-LOG-13** Todas las cargas de una ruta y fecha operativa llevan el mismo chofer; la liquidación toma ese
  `chofer_id`. Un chofer no puede tener cargas `Preparado`/`EnRuta` en dos rutas distintas la misma fecha
  (`LOG-018`) `[SUP-24]`. Cambiar de chofer a media jornada exige cancelar o liquidar lo anterior.
- **RN-LOG-14** *Cargas sugeridas*: para una fecha de entrega, por cada ruta con pedidos `Confirmado`/`Asignado`
  sin carga, se arma una carga `Preparado` tipo `CargaInicial` con los pedidos (RN-LOG-11, incluye partidas
  `CambioFisico`) más los **cambios pendientes** de los clientes de la ruta que visitan ese día. Si la ruta ya
  tiene carga inicial `Preparado`, se le agregan los pedidos en lugar de crear otra. El usuario revisa y despacha
  como hoy.
- **RN-LOG-15** Retorno `CambioFisico`: partidas `Retirado` generan `RetornoCliente` (+) en el almacén de la ruta
  y, si son `NoVendible`, `Merma` (−) con su motivo (igual que RN-LOG-04); partidas `Entregado` generan
  `SalidaCambio` (−) en el almacén de la ruta. No crea venta ni importe. Exige cliente (no genérico, `LOG-019`).
- **RN-LOG-16** Si en un cambio físico lo entregado de un producto es menor que lo retirado, la diferencia queda
  como **cambio pendiente** del cliente (`cambios_pendientes`, estado `Pendiente`). La app lo propone; el usuario
  puede indicar explícitamente que no queda pendiente (cambio no autorizado), en cuyo caso no se crea.
- **RN-LOG-17** Una partida `Entregado` con `cambio_pendiente_id` salda ese pendiente (`cantidad_saldada_base`);
  al llegar a `cantidad_base` pasa a `Saldado`. No se puede saldar más de lo pendiente (`LOG-020`); el excedente
  se registra como entrega normal del cambio.
- **RN-LOG-18** Los cambios entregados en el día aparecen como columna propia en la liquidación (RN-LOG-06) para que
  la ruta "cuadre": `carga = venta + cambios entregados + sobrante contado − diferencia − devuelto vendible`.
- **RN-LOG-19** El formato de la liquidación se toma del canal de la ruta al abrirse: `Completo` (carga, sobrante,
  devolución, recarga, venta, total $, producto a cobro) o `Simplificado` (carga, sobrante, venta, total $, sin
  producto a cobro). El cálculo de RN-LOG-06/07 es el mismo; solo cambia lo que se captura y se imprime.
- **RN-LOG-20** Etapas: `Abierta` → (`logistica.liquidar_producto`, Almacén) `ProductoConfirmado` → (`logistica.revisar_liquidacion`,
  Supervisor) `Revisada` → (`logistica.liquidar`, Liquidaciones) `Cerrada`. Con
  `logistica.requiere_revision_supervisor = false` se puede cerrar desde `ProductoConfirmado`. Cada transición guarda
  usuario y hora y queda en la bitácora. Confirmar producto exige conteo de toda partida con esperado ≠ 0 (regla
  actual) y aplica RN-LOG-09. Una transición que no corresponde a la etapa actual se rechaza con `LOG-021`.
- **RN-LOG-21** El supervisor puede regresar de `Revisada` a `ProductoConfirmado` con nota (solo para corregir
  dinero o cargos; el producto ya no cambia, RN-LOG-10). Desde otra etapa: `LOG-021`.
- **RN-LOG-22** La hoja imprimible de la liquidación reproduce el formato del cliente (columnas de RN-LOG-19,
  bloque de dinero, créditos del día por cliente y folio, gastos, producto a cobro y 4 firmas: almacén, vendedor,
  supervisor, liquidaciones).
- **RN-LOG-23** *Diferencia de precios*: la liquidación muestra Σ (`precio_referencia` − precio cobrado) ×
  cantidad de las partidas de ventas del día con incidencia `PrecioDistinto` (positivo = cobró de menos).
  `precio_referencia` es el precio vigente **del cliente** (especial o lista, RN-VEN-12) que Ventas guarda en la
  partida al aplicar la venta (RN-VEN-01), para que un cambio de precio posterior no altere el cálculo. Solo informa; el
  supervisor decide si genera un cargo `DiferenciaPrecio` (RN-LOG-26) `[SUP-21]`.

### Cobranza a choferes
- **RN-LOG-24** *Producto a cobro*: en la etapa de almacén, por partida devuelta `NoVendible` de un producto con
  `aplica_producto_a_cobro` y ruta cuyo canal `aplica_producto_a_cobro`, se captura `cantidad_a_cobro`
  (≤ devuelto no vendible, si no `LOG-022`). El sistema la **propone** cuando la antigüedad del lote (fecha de
  operación − fecha del lote `AAMMDD`, o `fecha_caducidad − vida_util_dias` si el lote no trae fecha) supera la
  tolerancia del producto (`productos.dias_tolerancia_devolucion`, o el parámetro
  `logistica.dias_tolerancia_devolucion` si es nulo) `[SUP-19]`; almacén la confirma o la corrige. Precio según
  `logistica.precio_producto_a_cobro` `[SUP-20]`.
- **RN-LOG-25** Los cargos al chofer se crean en `cargos_chofer` en estado `Pendiente`, ligados a la liquidación y
  al chofer de RN-LOG-13. `importe = round(cantidad × precio, 2)`.
- **RN-LOG-26** Al confirmar producto se crean los cargos `ProductoACobro` (RN-LOG-24) y, si
  `logistica.cargo_faltante_producto`, `FaltanteProducto` por cada partida con `diferencia < 0`
  (|diferencia| × precio según `logistica.precio_producto_a_cobro` `[SUP-20]`). Al cerrar se crea `FaltanteEfectivo` si `diferencia_efectivo < 0` y
  `logistica.cargo_faltante_efectivo` `[SUP-21]`. `DiferenciaPrecio` y `Otro` los captura el supervisor o
  liquidaciones con permiso `logistica.cargos_chofer`. Sobrantes de producto o efectivo **no** generan abono
  automático: se registran como incidencia (regla actual).
- **RN-LOG-27** Un cargo `Pendiente` se puede cancelar con motivo y permiso `logistica.cargos_chofer`; un cargo
  `Aplicado` no se cancela (`LOG-023`; se compensa con un cargo `Otro` negativo).
- **RN-LOG-28** *Corte semanal*: por chofer y semana (`logistica.dia_inicio_semana_corte` `[SUP-22]`), toma las
  ventas registradas del chofer (`ventas.vendedor_id`) en el periodo y los cargos `Pendiente` sin `corte_id` con
  `fecha_operativa ≤ hasta` (incluye los atrasados de semanas ya cerradas, RN-LOG-29).
  Cerrar el corte pasa esos cargos a `Aplicado` con `corte_id`. Solo se cierra si las liquidaciones del chofer en el
  periodo están `Cerradas` (`LOG-024`).
- **RN-LOG-29** Un cargo con `fecha_operativa` de una semana ya cerrada entra al siguiente corte abierto del chofer
  (no se reabre el corte).
- **RN-LOG-30** El corte se exporta (CSV y hoja imprimible) con ventas por día, cargos por tipo y producto, y total a
  descontar. El cálculo de la comisión queda fuera del sistema.

- **RN-LOG-31** Toda ruta tiene canal. Al asignar clientes a una ruta (`PUT /rutas/{id}/clientes`), cada cliente
  debe ser del canal de la ruta (`LOG-025`). Al crear la ruta, Logística pide a Ventas el genérico `GEN-<ruta>` con
  el canal de la ruta (`IVentasService.CrearClienteGenericoAsync` recibe `canalId`; hoy elige `TRAD` fijo en
  `VentasService`). Cambiar el canal de una ruta exige que sus clientes activos sean del nuevo canal.

### Errores nuevos
`LOG-017` chofer inválido, `LOG-018` chofer con carga en otra ruta, `LOG-019` cambio sin cliente, `LOG-025` cliente de otro canal,
`LOG-020` saldo de pendiente excedido, `LOG-021` transición de liquidación inválida, `LOG-022` cantidad a cobro
mayor que lo devuelto, `LOG-023` cargo aplicado no cancelable, `LOG-024` corte con liquidaciones abiertas,
`LOG-026` la jornada (ruta y fecha) ya tiene otro chofer, `LOG-027` canal inexistente o inactivo.

## Endpoints (`/api/v1/logistica`)
| Método | Ruta | Permiso |
|---|---|---|
| GET/POST/PUT | `/vehiculos`, `/rutas`, `/rutas/{id}/clientes` | `logistica.configurar` |
| GET/POST/PUT | `/envios` | `logistica.ver` / `logistica.cargar` |
| POST | `/envios/{id}/despachar`, `/cancelar` | `logistica.cargar` |
| GET/POST | `/retornos` | `logistica.retornos` |
| GET | `/liquidaciones?rutaId&fecha` | `logistica.ver` |
| PUT | `/liquidaciones/{id}/conteo` | `logistica.liquidar` (**v0.2:** `logistica.liquidar_producto`) |
| POST | `/liquidaciones/{id}/cerrar` | `logistica.liquidar` |
| POST | **(v0.2)** `/envios/sugeridos?fechaEntrega` (genera), `GET` igual (previsualiza) | `logistica.cargar` |
| PUT | **(v0.2)** `/liquidaciones/{id}/conteo` agrega `cantidadACobro` por partida | `logistica.liquidar_producto` |
| PUT | **(v0.2)** `/rutas/{id}` acepta `canalId`; `PUT /rutas/{id}/clientes` valida canal (RN-LOG-31) | `logistica.configurar` |
| POST | **(v0.2)** `/liquidaciones/{id}/confirmar-producto` | `logistica.liquidar_producto` |
| POST | **(v0.2)** `/liquidaciones/{id}/revisar`, `/regresar` | `logistica.revisar_liquidacion` |
| GET/POST/PUT | **(v0.2)** `/liquidaciones/{id}/gastos` | `logistica.liquidar` |
| GET | **(v0.2)** `/liquidaciones/{id}/hoja` (PDF/HTML imprimible) | `logistica.ver` |
| GET | **(v0.2)** `/cambios-pendientes?clienteId&rutaId&estado` | `logistica.ver` |
| POST | **(v0.2)** `/cambios-pendientes/{id}/cancelar` | `logistica.cambios_cancelar` |
| GET/POST | **(v0.2)** `/cargos-chofer?choferId&estado&desde&hasta`, `POST /cargos-chofer/{id}/cancelar` | `logistica.cargos_chofer` |
| GET/POST | **(v0.2)** `/cortes-chofer`, `POST /cortes-chofer/{id}/cerrar`, `GET /cortes-chofer/{id}/exportar` | `logistica.cargos_chofer` |

**(v0.2)** `POST/PUT /envios` reciben `choferId` y `vehiculoId`; `POST /retornos` acepta tipo `CambioFisico` con
partidas `sentido`. El permiso `logistica.liquidar` pasa a ser solo de la etapa de dinero (rol Liquidaciones); la
migración de permisos da `logistica.liquidar_producto` a Almacén y `logistica.revisar_liquidacion` a Supervisor.

## Criterios de aceptación
- Ruta R01 carga 300 KG, vende 260 KG, recibe devolución no vendible de 5 KG, cuenta sobrante de
  44 KG → esperado 40 KG, diferencia +4 KG genera `AjusteEntrada` e incidencia; el almacén R01
  queda en 0 al cerrar.

### Criterios de aceptación (v0.2)
- Ruta R51 (DET): carga inicial 96 T-800 (tolerancia del producto 11 días), recarga 24, vende 99, entrega 10 en
  cambios, retira 6 no vendibles (3 de un lote de hace 13 días), cuenta sobrante 13 → esperado `96 + 24 − 99 − 10 = 11`; diferencia +2 genera
  `AjusteEntrada` e incidencia; se propone `cantidad_a_cobro = 3` y al confirmar producto se crea un cargo
  `ProductoACobro` al chofer; el almacén R51 queda en 0.
- Un cambio físico que retira 20 y entrega 0 deja un pendiente de 20; la carga sugerida del siguiente día de visita
  del cliente incluye 20 y al entregarlos en la app el pendiente queda `Saldado`.
- La liquidación de R151 (CEDIS) se abre con formato `Simplificado` y no permite capturar producto a cobro.
- El corte semanal de un chofer con dos cargos `Pendiente` de la semana los deja `Aplicado`; un cargo nuevo con
  fecha de esa semana cae en el corte siguiente.

## Aclaraciones de implementación
- Una ruta crea al darse de alta su almacén tipo `Ruta` (mismo código) y su cliente genérico `GEN-<ruta>`, que siempre
  queda en `rutas_clientes`. El código de la ruta no se cambia (va en folios). El vendedor debe tener el rol Vendedor.
- `PUT /rutas/{id}/clientes` reemplaza la lista; los que salen quedan `activo = false` para que la app los borre.
- Un envío en `Preparado` puede llevar partidas sin lote (FEFO al despachar); al despachar las partidas se reemplazan
  por los lotes reales. No se despacha una carga para un día cuya liquidación ya está cerrada (`LOG-007`).
- Retornos: si no se indica lote se usa el de la venta original, el más reciente en la camioneta o el último cargado;
  sin condición, el producto terminado con vida útil ≤ 2 días es `NoVendible` `[SUP-13]` con motivo
  `DEV_NO_VENDIBLE`. Un retorno con fecha de una liquidación ya cerrada se aplica y genera incidencia (RN-SYN-06).
- Liquidación: el detalle abierto se calcula al momento (envíos del día, kardex de ventas, retornos y lo que haya en la
  camioneta). Al cerrar, el ajuste por diferencia se calcula contra la existencia real del lote para garantizar que el
  almacén quede en cero; en el caso normal coincide con `sobrante_contado − sobrante_esperado`. Sobrante a merma usa el
  motivo indicado (o `OTRO`); a reproceso, `REPROCESO`. Se exige efectivo entregado y conteo de toda partida con
  esperado ≠ 0. Una incidencia `DiferenciaLiquidacion` por diferencias de producto y otra por diferencia de efectivo.
- RN-LOG-09: `IEstadoSincronizacionRuta.OperacionesPendientesAsync(rutaId, fecha)` (por defecto, suma de
  `operaciones_pendientes` de los dispositivos activos del chofer de las cargas de esa ruta y fecha; sin cargas, del chofer
  predeterminado). Forzar requiere `logistica.forzar_cierre` y `{ "forzar": true }`; queda `cerrada_forzada = true`.
- **(v0.2, T-8.4) Canal, chofer y vehículo.**
  - `rutas.vendedor_id`/`vehiculo_id` son el chofer y vehículo **predeterminados**: se proponen si la carga no trae
    `choferId`/`vehiculoId` (al editar, se conservan los que ya tiene). El chofer se valida contra los usuarios activos
    con rol Vendedor (`LOG-017`) y el vehículo debe estar activo (`LOG-001`). Se revalida al despachar.
  - RN-LOG-13: guardar una carga con otro chofer arrastra a las demás cargas `Preparado` de la misma ruta y fecha; si ya
    hay una carga `EnRuta`/`Liquidado` o una liquidación con otro chofer se rechaza (`LOG-026`). `LOG-018` se aplica solo
    si `logistica.chofer_una_ruta_por_dia = true` `[SUP-24]`. La liquidación toma el chofer de la carga que la abre.
  - `envios.chofer_id` y `liquidaciones.chofer_id` son nulos solo en datos de v0.1 cuya ruta no tenía vendedor; las
    cargas nuevas siempre lo llevan.
  - RN-LOG-31: el genérico de la ruta no cuenta para la validación de canal; al guardar la ruta, Ventas lo alinea al
    canal (y a una lista de ese canal si la suya es de otro). Migración `V02RutasChofer`: canal más común entre los
    clientes activos no genéricos de la ruta (si no hay, `DET`).
