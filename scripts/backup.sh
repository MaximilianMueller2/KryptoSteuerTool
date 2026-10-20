#!/usr/bin/env bash
# Sichert Datenbank und Schlüsselring (verschlüsselte Börsen-API-Keys) in ein Verzeichnis (Standard: ./backups).
# Aufruf aus dem Projektordner:  scripts/backup.sh [Zielordner]
# Ohne Schlüsselring sind gespeicherte API-Keys nicht mehr lesbar (sie müssten neu eingetragen werden); die Buchungen sind davon unabhängig.
set -euo pipefail

target="${1:-backups}"
stamp="$(date +%Y%m%d-%H%M%S)"
mkdir -p "$target"
db_user="${POSTGRES_USER:-kryptosteuer}"
db_name="${POSTGRES_DB:-kryptosteuer}"

echo "Sichere Datenbank …"
docker compose exec -T postgres pg_dump -U "$db_user" -d "$db_name" --format=custom > "$target/db-$stamp.dump"

if docker volume inspect krypto-steuer_dpkeys >/dev/null 2>&1; then
  echo "Sichere Schlüsselring …"
  docker run --rm -v krypto-steuer_dpkeys:/keys:ro -v "$(cd "$target" && pwd)":/out alpine \
    tar czf "/out/keys-$stamp.tar.gz" -C /keys .
fi

chmod 600 "$target"/*-"$stamp".* 2>/dev/null || true
echo "Fertig: $target/db-$stamp.dump"
[ -f "$target/keys-$stamp.tar.gz" ] && echo "        $target/keys-$stamp.tar.gz"
echo "Die Dateien enthalten deine Finanzdaten bzw. Schlüssel: verschlüsselt und getrennt vom Rechner ablegen."
