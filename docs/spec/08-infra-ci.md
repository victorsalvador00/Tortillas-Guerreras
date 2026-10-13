# 08 — Infraestructura y CI/CD

## Ambientes

| Ambiente | Dónde | Despliegue |
|---|---|---|
| Desarrollo | Local: `deploy/docker-compose.dev.yml` (PostgreSQL 16) | — |
| Pruebas | Droplet 1 GB, PostgreSQL en contenedor | Automático en merge a `main` |
| Producción | Droplet 4 GB + PostgreSQL administrado + Spaces | Tag `v*` con aprobación manual |

## Producción (Droplet)

`deploy/docker-compose.prod.yml`:
- `api`: imagen `ghcr.io/<org>/tg-api:<tag>`, puerto interno 8080, variables de entorno.
- `caddy`: puertos 80/443, HTTPS automático. Rutas:
  - `api.<dominio>` → `api:8080`
  - `app.<dominio>` → `/srv/web` (SPA, fallback a `index.html`)
  - `factura.<dominio>` → `/srv/portal`

Variables (`.env` en el servidor, nunca en el repo): `ConnectionStrings__Default`,
`Jwt__Key`, `Jwt__Issuer`, `Spaces__Key`, `Spaces__Secret`, `Spaces__Bucket`, `Spaces__Endpoint`,
`Smtp__*`, `Hangfire__DashboardUser`, `Hangfire__DashboardPassword`.

Respaldos: PostgreSQL administrado (diario + restauración a punto en el tiempo) y `pg_dump` diario
adicional a Spaces con retención de 30 días (job de cron en el Droplet).

## GitHub Actions

| Workflow | Disparador | Pasos |
|---|---|---|
| `backend-ci.yml` | PR y push con cambios en `backend/**` | restore, build, test (Testcontainers), publicar resultados |
| `frontend-ci.yml` | cambios en `frontend/**` | pnpm install, lint, typecheck, test, build web y portal |
| `deploy-staging.yml` | push a `main` | imagen → GHCR, bundle de migraciones, SSH: respaldo + migrar + `compose up -d`, copiar SPA |
| `deploy-prod.yml` | tag `v*`, ambiente `production` con aprobación | igual que staging contra producción |
| `app-update.yml` | push a `main` (preview) y tag (production) con cambios en `apps/ruta` | `eas update --channel …` |
| `ghcr-cleanup.yml` | semanal | conservar las 5 imágenes más recientes |

Secretos por ambiente en GitHub: `SSH_HOST`, `SSH_USER`, `SSH_KEY`, `EXPO_TOKEN`.

## Dominios `[SUP-18]`
`app.`, `api.` y `factura.` sobre el dominio del cliente.
