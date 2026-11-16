#!/usr/bin/env bash
# Proves that a backup can actually be restored.
#
# Restores the dump into a scratch database next to the live one, reports how many articles it
# contains, and drops the scratch database again. Nothing touches the live schema.
#
# Usage: ./restore.sh <dump.sql.gz> [--keep]

set -euo pipefail

cd "$(dirname -- "${BASH_SOURCE[0]}")"

readonly scratch_db="newsfeed_restore"
dump="${1:-}"
keep=false

if [[ "${2:-}" == "--keep" ]]; then
  keep=true
fi

if [[ -z "$dump" || ! -f "$dump" ]]; then
  echo "usage: ./restore.sh <dump.sql.gz> [--keep]" >&2
  exit 2
fi

if [[ ! -f .env ]]; then
  echo "error: .env not found in $(pwd); run this script from the unzipped bundle" >&2
  exit 1
fi

load_env

# pg_hba requires the password even on the container's unix socket.
if [[ -z "${POSTGRES_PASSWORD:-}" ]]; then
  echo "error: POSTGRES_PASSWORD is empty in .env" >&2
  exit 1
fi

psql_psql() {
  local database="$1"
  shift
  docker compose exec -T -e PGPASSWORD="$POSTGRES_PASSWORD" postgres \
    psql --username postgres --dbname "$database" "$@"
}

echo "restoring $dump into $scratch_db"

psql_psql postgres --quiet --command "DROP DATABASE IF EXISTS $scratch_db"
psql_psql postgres --quiet --command "CREATE DATABASE $scratch_db"

if ! gunzip --stdout "$dump" | docker compose exec -T -e PGPASSWORD="$POSTGRES_PASSWORD" postgres \
        psql --username postgres --quiet --dbname "$scratch_db" --set ON_ERROR_STOP=1 >/dev/null; then
  echo "error: restore failed; $scratch_db was left in place for inspection" >&2
  exit 1
fi

counts="$(psql_psql "$scratch_db" --tuples-only --no-align --command \
  "SELECT 'articles=' || (SELECT count(*) FROM articles) || ' entities=' || (SELECT count(*) FROM entities) || ' mentions=' || (SELECT count(*) FROM mentions)")"

echo "restore OK: $counts"

if $keep; then
  echo "kept $scratch_db"
else
  psql_psql postgres --quiet --command "DROP DATABASE $scratch_db"
  echo "dropped $scratch_db"
fi
