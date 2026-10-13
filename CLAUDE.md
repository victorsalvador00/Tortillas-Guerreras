# TortillasGuerreras — instrucciones para Claude Code

Sistema de gestión para una empresa de productos de maíz: compras, recetas, producción, inventario,
logística (envíos, retornos y liquidación de ruta), ventas en ruta con captura offline y facturación.

## Lee primero

1. `docs/spec/00-indice.md` — mapa de la especificación y contexto del negocio.
2. `docs/spec/09-plan-implementacion.md` — fases y tareas en orden. Trabaja **una tarea a la vez**.
3. `docs/spec/10-supuestos-y-pendientes.md` — lo que todavía no confirma el cliente.

Antes de tocar un módulo, lee su archivo en `docs/spec/04-modulos/` y la parte correspondiente de
`docs/spec/03-modelo-datos.md`.

## Stack (decidido; no cambiar sin un ADR en `docs/adr/`)

| Capa | Tecnología |
|---|---|
| Backend | ASP.NET Core 10 (LTS), Minimal APIs, EF Core 10 + Npgsql, monolito modular |
| Base de datos | PostgreSQL 16+, un esquema por módulo |
| Tareas en segundo plano | Hangfire (núcleo) + Hangfire.PostgreSql |
| Web | React + Vite + TypeScript (SPA), TanStack Router/Query/Table, shadcn/ui + Tailwind, React Hook Form + Zod |
| App de ruta | Expo (React Native) + expo-sqlite + Drizzle ORM + EAS Update |
| Monorepo frontend | pnpm workspaces |
| Mapas | Leaflet + mosaicos OpenStreetMap (web); enlaces a Google Maps/Waze (app) |
| Infra | DigitalOcean (Droplet + PostgreSQL administrado + Spaces), Docker Compose, Caddy |
| CI/CD | GitHub Actions |

## Estructura del repositorio

```text
backend/     TortillasGuerreras.sln
  src/TG.Api/                 Host: endpoints, auth, OpenAPI, Hangfire
  src/TG.SharedKernel/        Tipos base, Result, UnitOfWork, auditoría, reloj, errores
  src/Modules/TG.<Modulo>/    Seguridad, Catalogos, Compras, Produccion, Inventario,
                              Logistica, Ventas, Facturacion, Sincronizacion
  src/TG.Contracts/           DTO compartidos con el agente de facturación
  src/TG.ContpaqiAgent/       (pendiente D-06, no implementar todavía)
  tests/                      TG.Modules.Tests, TG.Api.IntegrationTests, TG.ArchitectureTests
frontend/    monorepo pnpm
  apps/web  apps/portal  apps/ruta
  packages/api-client  packages/shared  packages/ui
deploy/      docker-compose, Caddyfile, scripts
docs/        spec/ y adr/
```

## Comandos

```bash
# Infra local (PostgreSQL + pgAdmin opcional)
docker compose -f deploy/docker-compose.dev.yml up -d

# Backend
dotnet build backend/TortillasGuerreras.sln
dotnet test backend/TortillasGuerreras.sln
dotnet run --project backend/src/TG.Api
dotnet ef migrations add <Nombre> \
  --project backend/src/Modules/TG.<Modulo> \
  --startup-project backend/src/TG.Api \
  --context <Modulo>DbContext --output-dir Infraestructura/Migraciones

# Frontend
pnpm -C frontend install
pnpm -C frontend --filter web dev
pnpm -C frontend --filter @tg/api-client generate   # regenera el cliente desde OpenAPI
pnpm -C frontend -r lint && pnpm -C frontend -r typecheck && pnpm -C frontend -r test
pnpm -C frontend --filter ruta start                 # Expo
```

## Reglas no negociables

1. **Licencias.** Solo dependencias MIT, Apache 2.0, BSD, PostgreSQL License o LGPL. Prohibidas:
   MediatR, AutoMapper, MassTransit v9+, FluentAssertions v8+, Duende IdentityServer, QuestPDF,
   Hangfire Pro, AG Grid Enterprise, MUI X Pro/Premium, Syncfusion, Telerik/Kendo, DevExpress.
   Antes de agregar un paquete que no esté en `docs/spec/02-convenciones.md`, verifica su licencia
   actual y regístralo en esa lista.
2. **Decimales.** Cantidades y dinero son `decimal` en C# y `numeric` en PostgreSQL; nunca
   `double`/`float`. En TypeScript los importes se calculan en centavos enteros; el servidor
   siempre recalcula y es la fuente de verdad.
3. **Inventario solo por kardex.** Las existencias cambian únicamente mediante
   `IInventarioService.RegistrarMovimientosAsync`. Ningún otro módulo escribe en `inventario.*`.
   Los movimientos son inmutables; una corrección es un contra-movimiento.
4. **Fronteras de módulos.** Un módulo no referencia el `DbContext` ni los tipos internos de otro;
   solo su carpeta `Publico/`. `TG.ArchitectureTests` (NetArchTest) lo verifica.
5. **Transacciones.** Una operación que cruza módulos y afecta inventario se ejecuta en una sola
   transacción compartiendo conexión (`IUnitOfWork`). Efectos no críticos van por outbox + Hangfire.
6. **IDs.** `Guid.CreateVersion7()` (UUID v7). El cliente (app) puede generar el ID. Los folios
   legibles son una columna aparte.
7. **Fechas.** `timestamptz` en UTC. La fecha de negocio es `fecha_operativa` (`date`) en
   `America/Mexico_City`. Usa `TimeProvider` inyectado, nunca `DateTime.Now`.
8. **Idempotencia.** Todo endpoint que reciba operaciones de la app acepta reenvíos sin duplicar
   (llave `op_id`).
9. **Pruebas.** Cada regla de negocio (`RN-…` en el spec) tiene al menos una prueba con su ID en el
   nombre o en un `[Trait("Regla", "RN-INV-03")]`. Integración con Testcontainers + PostgreSQL real.
10. **Supuestos.** Lo marcado `[SUP-xx]` no está confirmado. Impleméntalo como valor configurable
    (`catalogos.parametros` o `appsettings`) cuando sea posible. Si falta información para una
    regla, **no la inventes**: agrega un supuesto nuevo en `10-supuestos-y-pendientes.md` y avisa.
11. **Idioma.** Dominio en español sin acentos en identificadores (`OrdenCompra`, `Receta`, `Merma`);
    sufijos técnicos en inglés (`Endpoint`, `Handler`, `Dto`, `Validator`, `Configuration`).
    Tablas y columnas en `snake_case` español. UI en español de México. Commits en español con
    Conventional Commits (`feat(inventario): …`).
12. **Facturación (D-06) está pendiente.** Programa contra `IProveedorFacturacion` y usa
    `ProveedorFacturacionSimulado`. No implementes CONTPAQi ni PAC hasta que se decida.

## Definición de terminado (por tarea)

- Compila sin warnings nuevos; `dotnet test` y `pnpm -r test` pasan.
- Migración de EF Core incluida si cambió el modelo; datos semilla actualizados si aplica.
- Endpoints visibles en OpenAPI y cliente TS regenerado.
- Criterios de aceptación de la tarea en `09-plan-implementacion.md` cumplidos y marcados.
- Si cambiaste una decisión, regla o tabla, actualiza el spec en el mismo PR.
