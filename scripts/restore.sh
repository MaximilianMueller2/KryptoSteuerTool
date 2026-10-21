#!/usr/bin/env bash
# Stellt die Datenbank aus einem Backup wieder her (ersetzt den aktuellen Inhalt!).
# Aufruf:  scripts/restore.sh backups/db-YYYYMMDD-HHMMSS.dump [backups/keys-YYYYMMDD-HHMMSS.tar.gz]
set -euo pipefail

dump="${1:?Pfad zur db-*.dump angeben}"
keys="${2:-}"
db_user="${POSTGRES_USER:-kryptosteuer}"
db_name="${POSTGRES_DB:-kryptosteuer}"

read -r -p "Die Datenbank '$db_name' wird überschrieben. Fortfahren? (ja/nein) " answer
[ "$answer" = "ja" ] || { echo "Abgebrochen."; exit 1; }

docker compose stop api >/dev/null 2>&1 || true
docker compose exec -T postgres psql -U "$db_user" -d postgres -c "DROP DATABASE IF EXISTS \"$db_name\" WITH (FORCE);" -c "CREATE DATABASE \"$db_name\" OWNER \"$db_user\";"
docker compose exec -T postgres pg_restore -U "$db_user" -d "$db_name" --no-owner < "$dump"

if [ -n "$keys" ]; then
  docker volume create krypto-steuer_dpkeys >/dev/null
  docker run --rm -v krypto-steuer_dpkeys:/keys -v "$(cd "$(dirname "$keys")" && pwd)":/in:ro alpine \
    sh -c "rm -rf /keys/* && tar xzf /in/$(basename "$keys") -C /keys && chown -R 1654:1654 /keys"
fi

docker compose up -d api
echo "Wiederhergestellt."
