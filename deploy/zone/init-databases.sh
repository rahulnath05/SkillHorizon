#!/bin/sh
set -eu
printf '%s\n' "$ZONE_ID" | grep -Eq '^[a-z][a-z0-9-]{0,30}$'
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres \
  -c "CREATE DATABASE skillhorizon_${ZONE_ID}_assessment"
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres \
  -c "CREATE DATABASE skillhorizon_${ZONE_ID}_progress"
