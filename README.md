# Guerreras · Sistema de Gestión

Sistema para una empresa de productos de maíz (Guerreras, por Familia Ochoa): compras, recetas, producción,
inventario por kardex, logística en ruta con liquidación diaria, ventas con app offline y facturación (autofactura y
factura global). La especificación completa está en [`docs/spec/`](docs/spec/00-indice.md).

| Parte | Carpeta | Tecnología |
|---|---|---|
| API (monolito modular) | `backend/` | ASP.NET Core 10, EF Core 10, PostgreSQL 16, Hangfire |
| Panel administrativo | `frontend/apps/web` | React + Vite + TanStack Router/Query/Table + Tailwind |
| Portal de autofactura | `frontend/apps/portal` | React + Vite |
| App de ruta (offline) | `frontend/apps/ruta` | Expo SDK 54 + expo-sqlite + Drizzle |
| Componentes y diseño | `frontend/packages/ui` | Diseño del SGO (barra azul, menú con etiquetas, tema oscuro neón) |
| Cliente del API | `frontend/packages/api-client` | Generado con Orval desde OpenAPI |

## Arrancar en local

Requisitos: .NET SDK 10, Node 22+, pnpm 10 y Docker.

```bash
# 1. PostgreSQL de desarrollo (puerto 5433)
docker compose -f deploy/docker-compose.dev.yml up -d

# 2. API en http://localhost:5080 — migra y carga datos demo al iniciar (Development)
dotnet run --project backend/src/TG.Api   # perfil Development (Properties/launchSettings.json)
#    Documentación: http://localhost:5080/docs · Salud: /salud · Hangfire: /hangfire (admin/admin)

# 3. Web en http://localhost:5173 y portal en http://localhost:5174
pnpm -C frontend install
pnpm -C frontend --filter web dev
pnpm -C frontend --filter portal dev

# 4. App de ruta (Expo Go o emulador Android); define EXPO_PUBLIC_API_URL con la IP de tu equipo
pnpm -C frontend --filter ruta start
```

Usuarios de demostración (contraseña `Tortillas2026!`): `admin`, `direccion`, `compras`, `produccion`, `almacen`,
`logistica`, `vendedor1`–`vendedor3`, `facturacion`, `supervisor`. Para la app, el vendedor registra el teléfono y un
administrador o supervisor lo aprueba en **Seguridad › Dispositivos**.

## Pruebas

```bash
dotnet test --solution backend/TortillasGuerreras.sln   # unitarias, arquitectura e integración (Testcontainers)
pnpm -C frontend -r lint && pnpm -C frontend -r typecheck && pnpm -C frontend -r test
```

Cada regla de negocio `RN-…` del spec tiene al menos una prueba con su ID en `[Trait("Regla", …)]` o en el nombre.

## Producción

`deploy/docker-compose.prod.yml` (API + Caddy con HTTPS para `api.`, `app.` y `factura.` del dominio del cliente),
`deploy/scripts/` (despliegue, migraciones con bundles de EF y respaldo diario a Spaces) y los workflows de
`.github/workflows/`. Variables en `deploy/.env.example`.

## Pendientes del cliente

Los valores marcados `[SUP-xx]` son supuestos configurables (ver `docs/spec/10-supuestos-y-pendientes.md`). La
integración real de facturación (CONTPAQi o PAC, decisión D-06) está pendiente; hoy funciona con el proveedor simulado.
