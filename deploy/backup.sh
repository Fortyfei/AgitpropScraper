#!/usr/bin/env bash
# Weekly database backup for the single-VPS stack.
#
# Writes a gzipped pg_dump into BACKUP_DIR, keeps the last four weekly dumps, and keeps one
# archive per month. When ARCHIVE_REPO and GH_TOKEN are set, the monthly archive is uploaded to
# that repository as a release asset, so the off-site copy does not need a storage account.
#
# Usage: ./backup.sh [--dry-run]

set -euo pipefail

cd "$(dirname -- "${BASH_SOURCE[0]}")"
# shellcheck source=deploy/lib.sh
source ./lib.sh

readonly weekly_keep=4
backup_dir="${BACKUP_DIR:-./backups}"
archive_dir="$backup_dir/archive"
stamp="$(date +%Y-%m-%d)"
month="$(date +%Y-%m)"
dry_run=false

if [[ "${1:-}" == "--dry-run" ]]; then
  dry_run=true
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

echo "backing up newsfeed to $backup_dir/newsfeed-$stamp.sql.gz"

if $dry_run; then
  echo "dry run: would dump the newsfeed database, keep $weekly_keep weekly files and one monthly archive"
  exit 0
fi

mkdir -p "$backup_dir" "$archive_dir"

if ! docker compose exec -T -e PGPASSWORD="$POSTGRES_PASSWORD" postgres \
        pg_dump --username postgres --dbname newsfeed \
        | gzip > "$backup_dir/newsfeed-$stamp.sql.gz.partial"; then
  rm -f "$backup_dir/newsfeed-$stamp.sql.gz.partial"
  notify "Agitprop backup FAILED on $(hostname): pg_dump did not complete."
  exit 1
fi

mv "$backup_dir/newsfeed-$stamp.sql.gz.partial" "$backup_dir/newsfeed-$stamp.sql.gz"

if ! gzip --test "$backup_dir/newsfeed-$stamp.sql.gz"; then
  notify "Agitprop backup FAILED on $(hostname): the dump is not a valid gzip file."
  exit 1
fi

size="$(du -h "$backup_dir/newsfeed-$stamp.sql.gz" | cut -f1)"
echo "wrote $size"

# Keep the four most recent weekly dumps; the monthly archive is the long-term copy.
ls -1t "$backup_dir"/newsfeed-*.sql.gz 2>/dev/null | tail -n "+$((weekly_keep + 1))" | xargs -r rm --

archive_file="$archive_dir/newsfeed-$month.sql.gz"
if [[ ! -f "$archive_file" ]]; then
  cp "$backup_dir/newsfeed-$stamp.sql.gz" "$archive_file"
  echo "kept the monthly archive $archive_file"

  if [[ -n "${ARCHIVE_REPO:-}" && -n "${GH_TOKEN:-}" ]]; then
    tag="db-$month"
    if ! gh release view "$tag" --repo "$ARCHIVE_REPO" >/dev/null 2>&1; then
      gh release create "$tag" --repo "$ARCHIVE_REPO" \
        --title "Database $month" \
        --notes "Monthly newsfeed dump from the Agitprop host."
    fi
    GH_TOKEN="$GH_TOKEN" gh release upload "$tag" "$archive_file" --repo "$ARCHIVE_REPO" --clobber
    notify "Agitprop backup: monthly archive $month downloaded to $ARCHIVE_REPO."
  else
    echo "note: ARCHIVE_REPO/GH_TOKEN are not set, so the archive stays on this host"
  fi
fi

notify "Agitprop backup OK on $(hostname): newsfeed-$stamp.sql.gz ($size)."
