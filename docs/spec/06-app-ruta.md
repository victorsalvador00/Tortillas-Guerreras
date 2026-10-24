# 06 — App de ruta (Expo)

Usuarios: vendedores y repartidores. Plataforma por defecto: Android `[SUP-16]`.

## Pantallas

| Pantalla | Qué hace |
|---|---|
| Inicio de sesión | Usuario y contraseña; registra el dispositivo la primera vez (queda por aprobar) |
| Mi día | Ruta, fecha, carga recibida, totales vendidos, efectivo esperado, operaciones pendientes de enviar, botón Sincronizar |
| Clientes de hoy | Lista en orden de visita según `dias_visita`; estado visitado/no visitado; búsqueda; botón "Cómo llegar" (Google Maps/Waze) |
| Cliente | Datos, saldo `[SUP-08]`, pedidos pendientes con su carga ("En carga ENV-…", "· por despachar" o "Sin carga"), historial de últimas ventas |
| Nueva venta | Productos con existencia de la camioneta, cantidad, precio de lista, total; forma de pago; confirmar (toma GPS) |
| Entregar pedido | Partidas del pedido, carga en la que va (aviso si no va en ninguna o no se ha despachado), cantidades entregadas, confirmar (toma GPS) |
| Retorno | Producto, cantidad, condición (vendible/no vendible), motivo |
| Ticket | Folio, partidas, total, leyenda y URL del portal de autofactura con QR; compartir por WhatsApp; imprimir si hay impresora `[SUP-17]` |
| Inventario de camioneta | Cargado, vendido, devuelto, existencia actual por producto |
| Pendientes | Operaciones por enviar y rechazadas con su motivo |

### Pantallas nuevas o modificadas (v0.2)

| Pantalla | Qué hace |
|---|---|
| Mi día | La ruta sale de la **carga asignada** al chofer (RN-SYN-09); muestra ruta, vehículo y cargas del día. Sin carga ni ruta predeterminada: "No tienes ruta asignada hoy". Agrega "Pedidos por enviar" y "Cambios entregados" |
| Cliente | Botones nuevos **Nuevo pedido** y **Cambio físico**; sección **Cambios pendientes** del cliente con botón "Entregar" |
| Nuevo pedido | Cliente, fecha de entrega (por defecto el siguiente día de visita según `dias_visita`, mínimo hoy (RN-VEN-15)), OC del cliente opcional; productos permitidos (RN-VEN-14) con `nombre_corto`, cantidad en paquetes y ayuda "= N cajas + M"; sección **Cambios** (partidas `CambioFisico`). Se guarda como **borrador local** editable; **Enviar** lo pone en la cola (`CapturarPedido`) y ya no se edita |
| Pedido de la ruta | Solo canales `pedido_por = Ruta` (DET): mismo formulario contra el cliente genérico, una cuadrícula de todos los productos del canal con observaciones |
| Pedidos | Borradores, enviados (con estado de la operación) y rechazados con motivo; duplicar un pedido anterior como borrador |
| Cambio físico | Dos listas: **Retirar** (producto, cantidad, condición, motivo; por defecto `NoVendible` `[SUP-25]`) y **Entregar** (de la existencia de la camioneta). Si se entrega menos de lo retirado, avisa "Quedan N pendientes" con opción "No dejar pendiente" (RN-LOG-16). Confirmar toma GPS |
| Inventario de camioneta | Agrega columna "Cambios" y la vista en cajas + paquetes (RN-CAT-09) |
| Nueva venta / Entregar pedido | Precio especial del cliente (RN-VEN-12); forma de pago **Cheque**; productos exclusivos ocultos; validación de cantidades (RN-APP-09) |

## Base local (expo-sqlite + Drizzle)

Tablas espejo de bajada: `productos`, `productos_unidades`, `clientes`, `rutas_clientes`,
`precios`, `envios`, `envios_partidas`, `pedidos`, `pedidos_partidas`, `motivos_merma`,
`parametros`, `saldos_clientes`.

Tablas propias:
- `operaciones_pendientes` (`op_id`, `secuencia`, `tipo`, `datos_json`, `estado`
  [`Pendiente`, `Enviando`, `Aplicada`, `Rechazada`], `intentos`, `error`, `creada_en`).
- `ventas_locales` + `ventas_locales_partidas`, `retornos_locales`: para mostrar historial e
  inventario de camioneta sin red.
- `estado_dispositivo` (`dispositivo_id`, `codigo`, `ruta_codigo`, `consecutivo_folio`,
  `cursor_sync`, `ultimo_sync`).

Inventario de camioneta local = carga recibida − ventas locales + retornos vendibles locales.
**(v0.2)** − cambios entregados locales. Tablas nuevas: espejo `precios_cliente`, `productos_exclusivos`,
`cambios_pendientes`, `consumos_clientes`, `canales`; propias `pedidos_locales` + `pedidos_locales_partidas`
(`estado`: `Borrador`, `Enviado`, `Rechazado`) y `cambios_locales` (una fila por partida con `sentido`). Migración
de Drizzle; los datos existentes se conservan.

## Reglas de la app
- **RN-APP-01** Confirmar una venta guarda venta local + operación pendiente + incrementa el
  consecutivo en una sola transacción de SQLite.
- **RN-APP-02** La app no permite vender más de lo que muestra el inventario de camioneta, salvo
  que `ruta.permite_existencia_negativa = true`, en cuyo caso advierte y deja continuar `[SUP-10]`.
- **RN-APP-03** El GPS se solicita al confirmar con espera máxima de 10 s; si no hay lectura se
  guarda sin GPS.
- **RN-APP-04** Sincronización automática al recuperar red (NetInfo), al abrir la app y cada
  5 min con red; manual con botón.
- **RN-APP-05** Operaciones `Aplicada` se borran de la base local después de
  `ruta.dias_retencion_local` días.
- **RN-APP-06** Montos en centavos enteros; el total mostrado coincide con RN-VEN-02.
- **RN-APP-07** Credenciales y tokens en `expo-secure-store`, nunca en SQLite.
- **RN-APP-08** Actualizaciones OTA (EAS Update) se aplican al abrir la app, nunca a mitad de una
  venta. Cambios de esquema local van con migraciones de Drizzle.

- **RN-APP-09 (v0.2)** *Cantidades confiables*: al confirmar una partida (venta, entrega, pedido o cambio), si la
  cantidad supera `ruta.alerta_cantidad_factor` × el promedio del cliente para ese producto (`consumos_clientes`) o
  `ruta.alerta_cantidad_maxima`, la app muestra "¿Seguro? Capturaste 200 (normalmente 20)" y exige confirmar. El
  resumen antes de enviar muestra las cantidades en grande, en paquetes y en cajas + paquetes.
- **RN-APP-10 (v0.2)** *Distribución y actualizaciones*: Android únicamente, APK instalado directo (perfil EAS
  `production` con `buildType: apk` y distribución interna). Las correcciones de JS se publican por EAS Update al
  canal `production` solo con una versión aprobada (cierre de sprint); se aplican al abrir la app (RN-APP-08). Un
  APK nuevo solo cuando cambia código nativo, y se instala **encima** del anterior (mismo `applicationId` y firma),
  nunca desinstalando: así se conservan la base local, la cola y el consecutivo.
- **RN-APP-11 (v0.2)** Un borrador de pedido no sube nada al servidor; si la app se reinstala, los borradores se
  pierden (se avisa en la pantalla Pedidos). Lo enviado está en la cola y no se pierde (principio 4 de 05).

## Criterios de aceptación
- En modo avión se registran 20 ventas; al recuperar red se envían todas, el servidor las aplica una
  vez y la pantalla Pendientes queda en cero.
- Cerrar la app a la fuerza a mitad de un envío no duplica ventas al reabrir.
- **(v0.2)** En modo avión se capturan 3 pedidos de tiendas y un cambio físico con pendiente; al recuperar red el
  servidor tiene 3 pedidos `Capturado` (origen App) y un `cambio_pendiente`.
- **(v0.2)** Si el supervisor asigna la carga de R52 al chofer que ayer estaba en R51, al sincronizar la app muestra
  los clientes de R52 y las ventas de ayer pendientes de subir se aplican a R51.
- **(v0.2)** Capturar 200 donde el promedio es 20 pide confirmación.

## Implementación (T-5.2 a T-5.5)

- Proyecto en `frontend/apps/ruta` (Expo SDK 54, expo-router). Nombre visible: **Guerreras Ruta**; logotipo del cliente en login, encabezado y ticket.
- Base local con Drizzle (`src/db/esquema.ts`, migraciones en `drizzle/`). Además de las tablas de arriba hay `cobros_locales` (para el efectivo esperado de Mi día) y `retornos_locales` guarda una fila por partida.
- Motor de sync puro en `src/sync/motor.ts` (probado con Vitest): push en lotes ≤ 200 por secuencia, reintentos 1-2-4 s ante red/5xx, rechazos visibles sin reintento, pull con `cursor`/`hayMas`, `ReportarEstado` al final de cada sincronización y `GET /sync/dispositivo` la primera vez (RN-SYN-08).
- `ReportarEstado` se envía en un lote propio (no se guarda en `operaciones_pendientes`) y consume un número de `secuencia`.
- Las actualizaciones OTA se revisan solo al abrir la app, antes de mostrar pantallas (RN-APP-08).
