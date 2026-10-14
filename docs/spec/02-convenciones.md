# 02 — Convenciones

## Base de datos

- Nombres en `snake_case` español con `EFCore.NamingConventions` (Apache 2.0).
- Un esquema por módulo. PK `id uuid` (UUID v7). FK `<entidad>_id`.
- Índices `ix_<tabla>_<columnas>`, únicos `ux_<tabla>_<columnas>`.
- Enums como `text` con `CHECK` (legibles en consultas y reportes), mapeados a enums de C#.
- Columnas de auditoría en toda tabla editable: `creado_en timestamptz`, `creado_por uuid`,
  `actualizado_en timestamptz null`, `actualizado_por uuid null`.
- Catálogos: borrado lógico con `activo boolean`. Documentos: estado `Cancelado`, nunca `DELETE`.
- Tablas que baja la app: `version_sync bigint` tomado de la secuencia global `sync.version_seq`
  (se actualiza en cada inserción o cambio mediante interceptor de EF).

| Dato | Tipo PostgreSQL | Tipo C# |
|---|---|---|
| Cantidad (kg, piezas) | `numeric(18,4)` | `decimal` |
| Importe | `numeric(18,2)` | `decimal` |
| Precio unitario | `numeric(18,4)` | `decimal` |
| Costo unitario | `numeric(18,6)` | `decimal` |
| Porcentaje / tasa | `numeric(9,6)` (0.16 = 16 %) | `decimal` |
| Coordenadas | `numeric(9,6)` | `decimal` |
| Fecha y hora | `timestamptz` (UTC) | `DateTimeOffset` |
| Fecha de negocio | `date` | `DateOnly` |

Redondeo de importes: 2 decimales, `MidpointRounding.AwayFromZero`, por partida y luego suma.

## Backend

- Minimal APIs agrupadas por módulo: `app.MapGroup("/api/v1/compras").MapComprasEndpoints()`.
- Recursos en plural (`/ordenes-compra`), acciones de estado como `POST /{id}/autorizar`.
- Listados paginados: `?pagina=1&tamano=50` (máximo 200) → `{ items, total, pagina, tamano }`.
- Un handler por caso de uso (`AutorizarOrdenCompraHandler`), sin MediatR: se registran en DI y se
  invocan directo desde el endpoint.
- Validación con FluentValidation; errores de negocio con `Result<T>` → `ProblemDetails`.
- Códigos de error: `<MOD>-nnn` (`INV-001 Existencia insuficiente`), catalogados en cada módulo.
- Mapeo manual (métodos `ToDto()`), o Mapster si el volumen lo justifica.
- `async` en todo I/O; `CancellationToken` en cada endpoint.

## Frontend (web y portal)

- Carpetas por funcionalidad: `src/features/<modulo>/<pantalla>/`.
- Datos: TanStack Query con el cliente generado (`@tg/api-client`, Orval). Llaves de query
  `[modulo, recurso, params]`.
- Formularios: React Hook Form + Zod; esquemas compartidos en `@tg/shared` cuando la app los usa.
- Formatos: `Intl.NumberFormat('es-MX', { style: 'currency', currency: 'MXN' })`;
  fechas con `date-fns` + `date-fns-tz` en `America/Mexico_City`.
- Componentes: shadcn/ui en `@tg/ui`; tablas con TanStack Table.

## App de ruta

- Ver `06-app-ruta.md`. Mismas validaciones Zod que la web. Importes en centavos enteros.

## Pruebas

- Backend: xUnit + Shouldly + Testcontainers (PostgreSQL 16). Nombres
  `Metodo_Escenario_Resultado`. `[Trait("Regla", "RN-XXX-nn")]`.
- Arquitectura: NetArchTest (MIT) verifica fronteras de módulos.
- Frontend: Vitest + Testing Library. E2E opcional con Playwright.

## Git

- Ramas `feat/<modulo>-<descripcion>`, `fix/…`. PR a `main` con CI en verde.
- Conventional Commits en español. Tags `vX.Y.Z` para producción.

## Librerías aprobadas

| Uso | Paquete | Licencia |
|---|---|---|
| ORM | Microsoft.EntityFrameworkCore 10, Npgsql.EntityFrameworkCore.PostgreSQL | MIT / PostgreSQL |
| Nombres | EFCore.NamingConventions | Apache 2.0 |
| Consultas de reportes | Dapper | Apache 2.0 |
| Auth | Microsoft.AspNetCore.Identity.EntityFrameworkCore, JwtBearer | MIT |
| Validación | FluentValidation | Apache 2.0 |
| Jobs | Hangfire.Core, Hangfire.AspNetCore, Hangfire.PostgreSql | LGPL 3.0 |
| OpenAPI | Microsoft.AspNetCore.OpenApi, Scalar.AspNetCore | MIT |
| Logs | Serilog.AspNetCore | Apache 2.0 |
| Archivos | AWSSDK.S3 | Apache 2.0 |
| QR | QRCoder | MIT |
| PDF | PdfSharp / MigraDoc | MIT |
| Pruebas | xUnit, Shouldly, Testcontainers.PostgreSql, NetArchTest.Rules | Apache 2.0 / BSD / MIT |
| Web | react, vite, @tanstack/*, react-hook-form, zod, tailwindcss, shadcn/ui, recharts, leaflet, react-leaflet, date-fns, date-fns-tz, orval, vitest, @testing-library/react | MIT / BSD |
| App | expo (SDK 54) y módulos expo-* (router, sqlite, secure-store, location, updates, crypto, device, constants, linking, sharing, splash-screen, status-bar, system-ui, application), @expo/vector-icons, @react-native-community/netinfo, react-native-svg, react-native-qrcode-svg, react-native-safe-area-context, react-native-screens, drizzle-kit, babel-plugin-inline-import | MIT |
| App (ORM local) | drizzle-orm | Apache 2.0 |

Prohibidas: ver `CLAUDE.md`, regla 1.
