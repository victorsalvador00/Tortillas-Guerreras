# 06 — App de ruta (Expo)

Usuarios: vendedores y repartidores. Plataforma por defecto: Android `[SUP-16]`.

## Pantallas

| Pantalla | Qué hace |
|---|---|
| Inicio de sesión | Usuario y contraseña; registra el dispositivo la primera vez (queda por aprobar) |
| Mi día | Ruta, fecha, carga recibida, totales vendidos, efectivo esperado, operaciones pendientes de enviar, botón Sincronizar |
| Clientes de hoy | Lista en orden de visita según `dias_visita`; estado visitado/no visitado; búsqueda; botón "Cómo llegar" (Google Maps/Waze) |
| Cliente | Datos, saldo `[SUP-08]`, pedidos pendientes, historial de últimas ventas |
| Nueva venta | Productos con existencia de la camioneta, cantidad, precio de lista, total; forma de pago; confirmar (toma GPS) |
| Entregar pedido | Partidas del pedido, cantidades entregadas, confirmar (toma GPS) |
| Retorno | Producto, cantidad, condición (vendible/no vendible), motivo |
| Ticket | Folio, partidas, total, leyenda y URL del portal de autofactura con QR; compartir por WhatsApp; imprimir si hay impresora `[SUP-17]` |
| Inventario de camioneta | Cargado, vendido, devuelto, existencia actual por producto |
| Pendientes | Operaciones por enviar y rechazadas con su motivo |

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

## Criterios de aceptación
- En modo avión se registran 20 ventas; al recuperar red se envían todas, el servidor las aplica una
  vez y la pantalla Pendientes queda en cero.
- Cerrar la app a la fuerza a mitad de un envío no duplica ventas al reabrir.

## Implementación (T-5.2 a T-5.5)

- Proyecto en `frontend/apps/ruta` (Expo SDK 54, expo-router). Nombre visible: **Guerreras Ruta**; logotipo del cliente en login, encabezado y ticket.
- Base local con Drizzle (`src/db/esquema.ts`, migraciones en `drizzle/`). Además de las tablas de arriba hay `cobros_locales` (para el efectivo esperado de Mi día) y `retornos_locales` guarda una fila por partida.
- Motor de sync puro en `src/sync/motor.ts` (probado con Vitest): push en lotes ≤ 200 por secuencia, reintentos 1-2-4 s ante red/5xx, rechazos visibles sin reintento, pull con `cursor`/`hayMas`, `ReportarEstado` al final de cada sincronización y `GET /sync/dispositivo` la primera vez (RN-SYN-08).
- `ReportarEstado` se envía en un lote propio (no se guarda en `operaciones_pendientes`) y consume un número de `secuencia`.
- Las actualizaciones OTA se revisan solo al abrir la app, antes de mostrar pantallas (RN-APP-08).
