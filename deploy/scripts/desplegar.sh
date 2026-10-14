#!/usr/bin/env bash
# Lo ejecuta el workflow por SSH: respaldo, migraciones y reinicio con la imagen indicada.
set -euo pipefail
cd "$(dirname "$0")/.."
TAG="${1:?Uso: desplegar.sh <tag>}"
sed -i "s/^TG_TAG=.*/TG_TAG=$TAG/" .env
./scripts/respaldo.sh || echo "Aviso: el respaldo falló; se continúa con el despliegue."
docker compose -f docker-compose.prod.yml pull api
./scripts/migrar.sh
docker compose -f docker-compose.prod.yml up -d
docker image prune -f
