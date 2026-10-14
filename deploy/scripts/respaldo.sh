#!/usr/bin/env bash
# Respaldo diario adicional: pg_dump → Spaces con retención de 30 días (cron del Droplet: 0 3 * * *).
set -euo pipefail
cd "$(dirname "$0")/.."
set -a; source .env; set +a
FECHA=$(date -u +%Y%m%d-%H%M)
ARCHIVO="/tmp/tortillas-$FECHA.dump"
# Convierte la cadena de conexión de Npgsql a parámetros de pg_dump.
declare -A cs; IFS=';' read -ra partes <<< "$ConnectionStrings__Default"
for p in "${partes[@]}"; do [[ -n "$p" ]] && cs["${p%%=*}"]="${p#*=}"; done
PGPASSWORD="${cs[Password]}" docker run --rm -e PGPASSWORD -v /tmp:/tmp postgres:16-alpine \
  pg_dump -h "${cs[Host]}" -p "${cs[Port]:-5432}" -U "${cs[Username]}" -d "${cs[Database]}" -Fc -f "$ARCHIVO"
docker run --rm -v /tmp:/tmp -e AWS_ACCESS_KEY_ID="$Spaces__Key" -e AWS_SECRET_ACCESS_KEY="$Spaces__Secret" \
  amazon/aws-cli s3 cp "$ARCHIVO" "s3://$Spaces__Bucket/respaldos/" --endpoint-url "$Spaces__Endpoint"
rm -f "$ARCHIVO"
LIMITE=$(date -u -d '30 days ago' +%Y%m%d)
docker run --rm -e AWS_ACCESS_KEY_ID="$Spaces__Key" -e AWS_SECRET_ACCESS_KEY="$Spaces__Secret" amazon/aws-cli \
  s3 ls "s3://$Spaces__Bucket/respaldos/" --endpoint-url "$Spaces__Endpoint" | awk '{print $4}' | while read -r f; do
  d=$(echo "$f" | sed -E 's/tortillas-([0-9]{8}).*/\1/'); if [[ "$d" < "$LIMITE" ]]; then
    docker run --rm -e AWS_ACCESS_KEY_ID="$Spaces__Key" -e AWS_SECRET_ACCESS_KEY="$Spaces__Secret" amazon/aws-cli \
      s3 rm "s3://$Spaces__Bucket/respaldos/$f" --endpoint-url "$Spaces__Endpoint"; fi; done
