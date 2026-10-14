#!/usr/bin/env bash
# Aplica los bundles de migración de cada módulo (en orden de dependencias) desde la imagen de la API.
set -euo pipefail
cd "$(dirname "$0")/.."
set -a; source .env; set +a
IMAGEN="ghcr.io/${GHCR_ORG}/tg-api:${TG_TAG}"
for m in Catalogos Seguridad Inventario Compras Produccion Ventas Logistica Facturacion Sincronizacion; do
  echo "Migrando $m…"
  docker run --rm --env-file .env --entrypoint "/app/migraciones/efbundle-$m" "$IMAGEN" \
    --connection "$ConnectionStrings__Default"
done
# Datos base (permisos, roles, unidades, parámetros, motivos); sin datos demo.
docker run --rm --env-file .env -e BaseDatos__Semilla=true -e BaseDatos__SoloInicializar=true "$IMAGEN"
