# 01 — Arquitectura

## Decisiones

| # | Decisión | Elegido |
|---|---|---|
| D-01 | Backend | ASP.NET Core 10 (LTS) + EF Core 10 + PostgreSQL |
| D-02 | Estilo | Monolito modular, un solo despliegue |
| D-03 | Base de datos | PostgreSQL 16+, un esquema por módulo |
| D-04 | Frontend web | React + Vite + TypeScript (SPA), monorepo pnpm |
| D-05 | App de ruta | Expo (React Native) + expo-sqlite + EAS Update |
| D-06 | Facturación | **Pendiente.** Programar contra `IProveedorFacturacion` con proveedor simulado |
| D-07 | Hosting | DigitalOcean: Droplet 4 GB + PostgreSQL administrado + Spaces |
| D-08 | CI/CD | GitHub + GitHub Actions, un repositorio, despliegue por SSH |
| D-09 | Mapas | Leaflet + OSM en web; enlaces a Google Maps/Waze en la app |

## Componentes

```text
[Web administrativa]  [App de ruta offline]  [Portal de autofactura]
        │ REST+JWT           │ sync por lotes          │ folio + RFC
        └────────────────────┼─────────────────────────┘
                             ▼
              API ASP.NET Core 10 (un contenedor)
   Seguridad · Catalogos · Compras · Produccion · Inventario
   Logistica · Ventas · Facturacion · Sincronizacion · Hangfire
                             │
                     PostgreSQL (esquemas)          Spaces (XML, PDF, evidencias)
                             ▲
          (D-06) Agente de facturación ── lee outbox por HTTPS saliente
```

## Módulos

| Módulo | Esquema | Responsabilidad | Usa (vía `Publico/`) |
|---|---|---|---|
| Seguridad | `seguridad` | Usuarios, roles, permisos, dispositivos, tokens | — |
| Catalogos | `catalogos` | Unidades, productos, conversiones, impuestos, motivos, parámetros | — |
| Compras | `compras` | Proveedores, órdenes de compra, recepciones | Catalogos, Inventario |
| Produccion | `produccion` | Recetas, líneas, órdenes de producción, consumos, mermas | Catalogos, Inventario |
| Inventario | `inventario` | Almacenes, kardex, existencias, costo promedio, conteos, traspasos | Catalogos |
| Logistica | `logistica` | Vehículos, rutas, clientes por ruta, envíos (cargas), retornos, liquidaciones | Catalogos, Inventario, Ventas |
| Ventas | `ventas` | Canales, listas de precio, clientes, pedidos, ventas, cobros, incidencias | Catalogos, Inventario |
| Facturacion | `facturacion` | Emisor, solicitudes de autofactura, facturas, factura global, outbox | Ventas |
| Sincronizacion | `sync` | Endpoints push/pull, idempotencia, enrutamiento de operaciones a módulos | Todos (solo `Publico/`) |

Regla de dependencias: no hay ciclos. `Inventario` y `Catalogos` no dependen de nadie más.
`Logistica` usa `Ventas` para leer clientes y ventas de la ruta en la liquidación.

## Estructura interna de un módulo

```text
TG.Inventario/
  Dominio/            Entidades, value objects, enums, reglas puras (sin EF)
  Aplicacion/         Un handler por caso de uso: <Accion><Entidad>Handler + Validator + Dto
  Infraestructura/    InventarioDbContext, configuraciones EF, migraciones, repositorios
  Endpoints/          InventarioEndpoints.cs (MapGroup("/api/v1/inventario"))
  Publico/            Interfaces y DTO que otros módulos pueden usar (IInventarioService)
  InventarioModule.cs AddInventarioModule(IServiceCollection) / MapInventarioEndpoints()
```

## Transversales

- **Autenticación:** ASP.NET Core Identity (tablas en `seguridad`) + JWT Bearer (15 min) + refresh
  token rotativo (30 días, ligado a dispositivo en la app). Autorización por políticas de permiso
  (`inventario.ajustar`, `produccion.cerrar`, …) asignadas a roles.
- **Unidad de trabajo:** `IUnitOfWork` en SharedKernel abre una `NpgsqlConnection` + transacción por
  solicitud de escritura; todos los `DbContext` participantes usan esa conexión
  (`Database.UseTransaction`).
- **Outbox:** tabla `facturacion.outbox` (y genérica `sync.outbox_eventos` si se necesita). Hangfire
  procesa con reintentos exponenciales.
- **Auditoría:** columnas `creado_en/por`, `actualizado_en/por` llenadas por un interceptor de EF.
  Operaciones de la app agregan `dispositivo_id`, `fecha_hora_dispositivo` y GPS.
- **Errores:** `ProblemDetails` (RFC 9457) con `codigo` de negocio (`INV-001`) y `errores` por campo.
- **Versionado de API:** prefijo `/api/v1`. OpenAPI con `Microsoft.AspNetCore.OpenApi` + Scalar.
- **Concurrencia:** `xmin` como token de concurrencia en agregados editables (órdenes, recetas,
  existencias).
- **Configuración de negocio:** `catalogos.parametros` (clave/valor) con caché en memoria.
- **Archivos:** Spaces (API compatible con S3) vía `AWSSDK.S3` (Apache 2.0).
- **Logs:** Serilog a consola (JSON) y archivo rotativo en el Droplet.
