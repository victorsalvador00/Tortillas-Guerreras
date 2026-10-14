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
