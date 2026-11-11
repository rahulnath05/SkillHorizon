#!/bin/sh
# Create the three databases for one residency zone on an existing PostgreSQL
# server. The zone compose file creates these itself when the zone runs its
# own server. Use this when the first wave shares a machine and not a database.
set -eu
zone=$(printf '%s' "${ZONE_ID:-}" | tr '[:upper:]' '[:lower:]')
printf '%s\n' "$zone" | grep -Eq '^[a-z][a-z0-9-]{0,30}$' || {
  echo "Set ZONE_ID to a short lowercase id, such as north." >&2
  exit 1
}
: "${PGHOST:=localhost}"
: "${PGPORT:=5432}"
: "${PGUSER:=skillhorizon}"
: "${PGPASSWORD:=skillhorizon}"
export PGPASSWORD
for suffix in domain assessment progress; do
  db="skillhorizon_${zone}_${suffix}"
  exists=$(psql -h "$PGHOST" -p "$PGPORT" -U "$PGUSER" -d postgres -tAc "SELECT 1 FROM pg_database WHERE datname = '${db}'")
  if [ "$exists" != "1" ]; then
    psql -h "$PGHOST" -p "$PGPORT" -U "$PGUSER" -d postgres -c "CREATE DATABASE ${db}"
  fi
  echo "$db"
done
