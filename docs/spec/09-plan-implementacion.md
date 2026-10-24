# 09 — Plan de implementación

Instrucciones para Claude Code:
- Toma la **primera tarea no marcada** en orden. Una tarea = una rama = un PR.
- Antes de empezar, lee las secciones del spec que la tarea indica.
- Al terminar, marca `[x]`, verifica la definición de terminado de `CLAUDE.md` y resume en el PR
  qué reglas `RN-…` quedaron cubiertas por pruebas.
- Si una tarea depende de un supuesto `[SUP-xx]`, impleméntalo configurable y no bloquees.

---

## Fase 0 — Fundaciones

- [x] **T-0.1 Estructura del repositorio.** Crear `backend/` (solución, `TG.Api`,
  `TG.SharedKernel`, proyectos vacíos de los 9 módulos, 3 proyectos de pruebas), `frontend/`
  (pnpm workspace con `apps/web`, `apps/portal`, `apps/ruta`, `packages/api-client`,
  `packages/shared`, `packages/ui`), `deploy/docker-compose.dev.yml`, `.editorconfig`,
  `Directory.Build.props` (nullable, warnings as errors en código nuevo), `global.json` (.NET 10).
  *Acepta:* `dotnet build` y `pnpm -r build` pasan; `docker compose up` levanta PostgreSQL.
- [x] **T-0.2 SharedKernel.** `Result<T>`, errores con código, `IUnitOfWork` con conexión
  compartida, interceptor de auditoría, generador de folios por secuencia
  (`<PREFIJO>-000001`), `TimeProvider`, helper de `fecha_operativa`, secuencia `sync.version_seq`
  e interceptor de `version_sync`. *Lee:* 01, 02. *Acepta:* pruebas unitarias de folios y
  `fecha_operativa` en cambio de día (23:59 vs 00:01 hora de México).
- [x] **T-0.3 Host de API.** Minimal APIs, ProblemDetails, OpenAPI + Scalar, Serilog, health check
  `/salud`, CORS para web/portal, rate limiting, Hangfire con PostgreSQL y dashboard protegido.
- [x] **T-0.4 Seguridad.** Identity en esquema `seguridad`, login/refresh/logout, permisos por
  política, roles semilla, dispositivos. *Lee:* `04-modulos/seguridad-catalogos.md`.
  *Acepta:* RN-SEG-01 a 05 con pruebas.
- [x] **T-0.5 Pruebas de arquitectura.** NetArchTest: ningún módulo referencia internos de otro.
- [x] **T-0.6 CI.** `backend-ci.yml` y `frontend-ci.yml`. *Lee:* 08.
- [x] **T-0.7 Web base.** Vite + React + TanStack Router/Query + shadcn/ui + Tailwind; layout con
  menú por permisos; login; cliente generado con Orval desde OpenAPI. *Lee:* 07.

## Fase 1 — Catálogos e inventario

- [x] **T-1.1 Catálogos.** Unidades, productos con conversiones, motivos de merma, parámetros;
  datos semilla de `03-modelo-datos.md`. *Acepta:* RN-CAT-01 a 06.
- [x] **T-1.2 Inventario núcleo.** Almacenes, `IInventarioService`, kardex, existencias con bloqueo,
  costo promedio, FEFO. *Acepta:* RN-INV-01 a 07 y los dos criterios del módulo.
- [x] **T-1.3 Ajustes, traspasos y conteos.** *Acepta:* RN-INV-08 a 10.
- [x] **T-1.4 Web: catálogos e inventario.** Pantallas de 07 para Catálogos e Inventario.

## Fase 2 — Compras

- [x] **T-2.1 Backend de compras.** Proveedores, órdenes, autorización, recepciones con entrada a
  inventario. *Acepta:* RN-COM-01 a 09.
- [x] **T-2.2 Web de compras.**

## Fase 3 — Recetas y producción

- [x] **T-3.1 Recetas.** Versionado y publicación; recetas semilla. *Acepta:* RN-PRO-01.
- [x] **T-3.2 Órdenes de producción.** Estados, consumos teóricos y reales, mermas, terminar con
  movimientos y costo. *Acepta:* RN-PRO-02 a 10 y criterios del módulo.
- [x] **T-3.3 Web de producción.** Plan del día, captura en tablet, reporte de variación.

> **Hito de demo:** al cerrar la Fase 3 con datos semilla se puede mostrar al cliente el ciclo
> compra → producción → inventario.

## Fase 4 — Ventas y logística (web)

- [x] **T-4.1 Clientes, canales y listas de precio.** *Acepta:* RN-VEN-10, cliente genérico por ruta.
- [x] **T-4.2 Rutas, vehículos y clientes por ruta.** Crea almacén de ruta. *Acepta:* RN-LOG-01.
- [x] **T-4.3 Pedidos.** Captura, confirmación, asignación.
- [x] **T-4.4 Ventas (handlers compartidos web/app).** Registrar, cancelar, GPS e incidencias.
  *Acepta:* RN-VEN-01 a 09.
- [x] **T-4.5 Envíos y despacho.** *Acepta:* RN-LOG-02, 03.
- [x] **T-4.6 Retornos y liquidación.** *Acepta:* RN-LOG-04 a 10 y criterio del módulo.
- [x] **T-4.7 Web de ventas y logística + mapa.** Incluye incidencias y aprobación de ubicaciones.

## Fase 5 — Sincronización y app de ruta

- [x] **T-5.1 API de sync.** Pull por alcance de ruta, push idempotente, operaciones, estado del
  dispositivo. *Lee:* 05. *Acepta:* RN-SYN-01 a 08; prueba de reenvío y de lote con rechazos mezclados.
- [x] **T-5.2 App base.** Expo + expo-router, login, registro de dispositivo, SQLite + Drizzle con
  migraciones, secure-store, cliente de API compartido. *Lee:* 06.
- [x] **T-5.3 Motor de sync en la app.** Cola de operaciones, pull incremental, disparadores,
  reintentos. *Acepta:* criterios de 06.
- [x] **T-5.4 Pantallas de venta, entrega, retorno, ticket e inventario de camioneta.**
  *Acepta:* RN-APP-01 a 08.
- [x] **T-5.5 EAS Update y build Android.** Canales `preview` y `production`; `app-update.yml`.

## Fase 6 — Facturación (con proveedor simulado)

- [x] **T-6.1 Emisor, facturas y outbox con `ProveedorFacturacionSimulado`.**
  *Acepta:* RN-FAC-01, 06, 07, 09.
- [x] **T-6.2 Portal de autofactura.** *Acepta:* RN-FAC-02, 03, 05, 08.
- [x] **T-6.3 Factura global.** *Acepta:* RN-FAC-04.
- [ ] **T-6.4 Proveedor real.** **Bloqueada por D-06.**

## Fase 7 — Tablero, reportes y salida a producción

- [x] **T-7.1 Tablero** (indicadores de 07) y reportes: variación de producción, mermas por motivo,
  ventas por ruta/canal/producto, continuidad de visitas.
- [x] **T-7.2 Infra de producción.** Compose de producción, Caddy, workflows de despliegue,
  respaldo a Spaces. *Lee:* 08.
- [x] **T-7.3 Carga inicial.** Importadores CSV de productos, clientes, proveedores, recetas y
  existencias iniciales (con plantilla descargable).

## Fase 8 — Ajustes de la versión 0.2 (reunión del 24-oct-2026)

Resumen y decisiones en `11-cambios-v0.2.md`. Las tareas ajustan funciones ya terminadas; por eso se agrupan aquí
en orden de dependencia en lugar de crear `T-x.y-b` dispersas (cada una indica qué tarea anterior ajusta). Fuera de
alcance: facturación y cobranza de clientes. Cada tarea incluye migración, semilla, OpenAPI y cliente TS
regenerado (definición de terminado).

- [x] **T-8.1 Catálogos y canales v0.2** *(ajusta T-1.1, T-4.1)*. Canales `AUTO`, `DET` (renombra `TRAD`),
  `INST`, `CEDIS` con `formato_liquidacion`, `aplica_producto_a_cobro`, `pedido_por`, `orden`; producto
  `nombre_corto`, `aplica_producto_a_cobro`; conversiones `uso_traslado`/`predeterminada_traslado`; helper de cajas +
  paquetes en `@tg/shared` y `ICatalogosService`; `dias_tolerancia_devolucion`; parámetros nuevos de `03`. Web: ficha
  de producto y conversiones. `TRAD` está fijo en `VentasService` (`CrearClienteGenericoAsync`) y en
  `InicializadorVentas`; ambos cambian.
  *Lee:* 03 (catalogos, ventas.canales), `04-modulos/seguridad-catalogos.md`. *Acepta:* RN-CAT-08, 09, 10; migración
  de `TRAD` a `DET` sin perder clientes.
- [x] **T-8.2 Bitácora** *(nueva)*. Tabla `seguridad.bitacora`, atributo `[Bitacora]` e interceptor en SharedKernel
  (mismo `IUnitOfWork`), origen y dispositivo desde `IUsuarioActual`; endpoint y pantalla Seguridad → Bitácora;
  componente "Historial" reutilizable en `@tg/ui`. Renombrar la clase `Bitacora` de `TG.Api/Infraestructura/Importacion.cs`
  (bitácora de importación) para que no choque con el atributo. Se hace temprano para que las tareas siguientes ya queden
  auditadas. *Lee:* 01 (Transversales), `seguridad-catalogos.md`. *Acepta:* RN-SEG-06, 07; prueba de que un
  rollback no deja filas de bitácora.
- [x] **T-8.3 Roles y permisos v0.2** *(ajusta T-0.4)*. Roles Captura y Liquidaciones; permisos
  `logistica.liquidar_producto`, `logistica.revisar_liquidacion`, `logistica.cargos_chofer`,
  `logistica.cambios_cancelar`, `ventas.pedidos_app`, `seguridad.bitacora`; migración que los asigna a los roles
  existentes. *Acepta:* un Almacén no puede cerrar el dinero de una liquidación (403).
- [x] **T-8.4 Rutas con canal, chofer y vehículo por carga** *(ajusta T-4.2, T-4.5, T-5.1)*. `rutas.canal_id`
  (migración: canal según el de sus clientes o `DET`), envíos con `chofer_id`/`vehiculo_id` (migración desde la
  ruta), liquidación con `chofer_id`; validación de canal al asignar clientes y genérico con el canal de la ruta
  (RN-LOG-31, `CrearClienteGenericoAsync(ruta, canalId)`); alcance de sync por carga (`RutaDeChoferAsync(usuario, fecha)` reemplaza a
  `RutaDeVendedorAsync`; `IEstadoSincronizacionRuta` usa los dispositivos del chofer de la carga); `rutaCambiada` en el
  pull. Web: rutas y nueva carga. *Lee:* `logistica.md` (RN-LOG-12, 13), 05 (RN-SYN-09). *Acepta:* RN-LOG-12, 13,
  RN-LOG-31, RN-SYN-09 y el criterio de cambio de ruta de 06.
- [x] **T-8.5 Grupos de clientes, precios especiales y productos exclusivos** *(ajusta T-4.1, T-4.4)*. Tablas,
  endpoints, pantallas; precio especial en `RegistrarVenta`, pedidos y venta web; `ventas_partidas.precio_referencia`;
  bajada a la app
  (`precios_cliente`, `productos_exclusivos`). *Lee:* `ventas.md` (RN-VEN-01, 11 a 14). *Acepta:* RN-VEN-01, 11 a 14
  y criterios v0.2 de ventas.
- [x] **T-8.6 Pedidos v0.2 y `CapturarPedido`** *(ajusta T-4.3, T-5.1)*. Columnas nuevas de pedidos y partidas,
  pedido de ruta (DET), partidas `CambioFisico`, `cantidad_solicitada`, bandeja "Por validar" con historial, operación
  de sync idempotente con permiso `ventas.pedidos_app` y validación de cliente en la ruta en Sincronización.
  *Lee:* `ventas.md` (RN-VEN-15 a 18), 05 (RN-SYN-10). *Acepta:* RN-VEN-15 a 18, RN-SYN-10;
  reenvío del mismo `op_id` no duplica el pedido.
- [x] **T-8.7 Estimado de producción y plan sugerido** *(ajusta T-3.3)*. Endpoints de `/produccion/estimado` y
  `/plan-sugerido`, `IVentasService.DemandaPorFechaEntregaAsync`/`GuardarPedidoRutaAsync`/`ConfirmarPedidosAsync`,
  pantalla de estimado por canal (con impresión), plan sugerido con "Crear órdenes", Plan del día visible en el
  menú, "Copiar receta". *Lee:* `produccion.md` (RN-PRO-11 a 14). *Acepta:* RN-PRO-11 a 14 y criterios v0.2 de
  producción.
- [x] **T-8.8 Cargas sugeridas** *(ajusta T-4.5)*. `GET/POST /envios/sugeridos`, botón en Cargas del día, incluye
  pedidos y cambios pendientes del día de visita. *Lee:* `logistica.md` (RN-LOG-11, 14). *Acepta:* RN-LOG-14.
- [x] **T-8.9 Cambios físicos y cambios pendientes** *(ajusta T-4.6, T-5.1)*. Retorno `CambioFisico` con
  `sentido`, movimiento `SalidaCambio`, `cambios_pendientes`, saldar y cancelar; web: retorno de cambio y pantalla
  de pendientes; bajada de pendientes. *Lee:* `logistica.md` (RN-LOG-15 a 18), `inventario.md` (RN-INV-11), 05
  (RN-SYN-11). *Acepta:* RN-LOG-15 a 18, RN-INV-11, RN-SYN-11. Incluye sumar los cambios pendientes del día de
  visita a la demanda del plan sugerido (RN-PRO-13) y a las cargas sugeridas (RN-LOG-14), que T-8.7 y T-8.8
  dejaron fuera.
- [x] **T-8.10 Liquidación v0.2** *(ajusta T-4.6, T-4.7)*. Columnas nuevas, formato por canal, etapas con permisos,
  movimientos al confirmar producto, gastos, forma de pago `Cheque`, totales de dinero de solo lectura (créditos y
  cobros sin cambios en Ventas), diferencia de precios, hoja imprimible con firmas. *Lee:* `logistica.md`
  (RN-LOG-06 a 10, 19 a 23). *Acepta:* RN-LOG-06, 07, 08 modificadas, 19 a 23, y criterios v0.2 de liquidación.
- [x] **T-8.11 Cobranza a choferes** *(nueva)*. Producto a cobro con sugerencia por antigüedad del lote,
  `cargos_chofer`, `cortes_chofer`, cancelaciones, exportación; pantallas Cuenta de choferes y Corte semanal; pestaña
  Cargos de la liquidación. *Lee:* `logistica.md` (RN-LOG-24 a 30). *Acepta:* RN-LOG-24 a 30.
- [x] **T-8.12 App de ruta v0.2** *(ajusta T-5.3, T-5.4)*. Migración Drizzle, colecciones nuevas, alcance por carga y
  `rutaCambiada`, pantallas Nuevo pedido / Pedido de la ruta / Pedidos / Cambio físico / cambios pendientes, forma de
  pago Cheque, precios especiales, productos exclusivos, cajas + paquetes, RN-APP-09. *Lee:* 06, 05.
  *Acepta:* RN-APP-09, 11 y criterios v0.2 de 06.
- [ ] **T-8.13 Distribución APK** *(ajusta T-5.5)*. Perfil `production` con `buildType: apk` y distribución interna;
  instrucciones de instalación y de actualización encima de la versión anterior en el README; `app-update.yml` solo
  publica a `production` desde un tag. *Acepta:* RN-APP-10; instalar el APK nuevo sobre el anterior conserva las
  operaciones pendientes.
- [ ] **T-8.14 Carga inicial v0.2** *(ajusta T-7.3)*. Plantillas nuevas `rutas` (código, nombre, canal, chofer, vehículo),
  `rutas_clientes` (ruta, cliente, orden, días), `precios` (lista o cliente, sku, unidad, precio), `grupos_clientes`;
  columnas nuevas en `productos` (`nombre_corto`, `producto_a_cobro`, uso `traslado` en conversiones) y `clientes`
  (`grupo`, canal `DET`/`CEDIS`). Carga del catálogo depurado del cliente **cuando se resuelvan las inconsistencias
  de `docs/cliente/mapeo.md`** (P-06). *Acepta:* importar las plantillas de ejemplo deja rutas, clientes por ruta y
  precios listos para operar.
