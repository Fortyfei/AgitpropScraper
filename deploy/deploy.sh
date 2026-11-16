#!/usr/bin/env bash
# Manual, pull-based deployment of the Agitprop stack.
#
# The CI publishes images tagged with the commit SHA and a bundle whose .env names those tags.
# Nothing reaches into the server: you run this script over SSH, it pulls the tags you name,
# restarts the stack, and rolls back on its own if the stack does not become healthy.
#
# Usage:
#   ./deploy.sh <commit-sha> [--dry-run]
#   ./deploy.sh current      # restart the tags already in .env
#
# The three most recent previous bundles stay in .deploy-history/, so a rollback is always
# possible even after a few bad deploys.

set -euo pipefail

cd "$(dirname -- "${BASH_SOURCE[0]}")"
# shellcheck source=deploy/lib.sh
source ./lib.sh
load_env

readonly compose_file="docker-compose.yaml"
readonly env_file=".env"
readonly history_dir=".deploy-history"
readonly keep_previous=3
readonly health_timeout_seconds=240

usage() {
  sed -n '2,16p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
}

target="${1:-}"
dry_run=false
if [[ "${2:-}" == "--dry-run" ]]; then
  dry_run=true
fi

if [[ -z "$target" ]]; then
  usage
  exit 2
fi

if [[ "$target" != "current" && ! "$target" =~ ^[0-9a-f]{7,40}$ ]]; then
  echo "error: '$target' is not a commit SHA (7-40 lowercase hex characters) or 'current'" >&2
  exit 2
fi

for required in "$compose_file" "$env_file"; do
  if [[ ! -f "$required" ]]; then
    echo "error: $required not found in $(pwd); run this script from the unzipped bundle" >&2
    exit 1
  fi
done

current_tag="$(grep -oE '/web:[0-9a-f]+' "$env_file" | cut -d: -f2 || true)"
current_tag="${current_tag:-unknown}"

if [[ "$target" == "current" ]]; then
  target="$current_tag"
  if [[ "$target" == "unknown" ]]; then
    echo "error: .env has no image tag to keep" >&2
    exit 1
  fi
fi

if [[ "$target" == "$current_tag" ]]; then
  echo "already on $target; nothing to do (use the image tag of an older commit to roll back)"
  exit 0
fi

echo "deploying $current_tag -> $target"

if $dry_run; then
  echo "dry run: would rewrite the three image tags in $env_file, pull, restart, and gate on health"
  exit 0
fi

set_tags() {
  local sha="$1"
  sed -i -E \
    -e "s|^(CONSUMER_IMAGE=.*):[^:]+$|\1:${sha}|" \
    -e "s|^(RSS_FEED_READER_IMAGE=.*):[^:]+$|\1:${sha}|" \
    -e "s|^(WEB_IMAGE=.*):[^:]+$|\1:${sha}|" \
    "$env_file"
}

container_health() {
  local service="$1" id status
  id="$(docker compose ps -q "$service" 2>/dev/null || true)"
  if [[ -z "$id" ]]; then
    echo "missing"
    return
  fi
  status="$(docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' "$id" 2>/dev/null || echo unknown)"
  echo "$status"
}

wait_healthy() {
  local deadline=$((SECONDS + health_timeout_seconds))
  while (( SECONDS < deadline )); do
    local web postgres
    web="$(container_health web)"
    postgres="$(container_health postgres)"
    if [[ "$web" == "healthy" && "$postgres" == "healthy" ]]; then
      return 0
    fi
    sleep 5
  done
  echo "web=$web postgres=$postgres" >&2
  return 1
}

mkdir -p "$history_dir"
previous_env="$history_dir/.env.$(date +%Y%m%d-%H%M%S)"
cp "$env_file" "$previous_env"
echo "saved the previous bundle to $previous_env"

set_tags "$target"

if ! docker compose pull; then
  echo "error: pulling $target failed; .env is unchanged in $previous_env" >&2
  cp "$previous_env" "$env_file"
  notify "Agitprop deploy to ${target} FAILED: image pull failed. Stack untouched."
  exit 1
fi

if ! docker compose up -d --remove-orphans; then
  echo "error: starting $target failed; rolling back to $current_tag" >&2
  set_tags "$current_tag"
  docker compose up -d --remove-orphans || true
  notify "Agitprop deploy to ${target} FAILED: containers did not start, rolled back to ${current_tag}."
  exit 1
fi

if ! wait_healthy; then
  echo "error: the stack did not become healthy; rolling back to $current_tag" >&2
  set_tags "$current_tag"
  docker compose up -d --remove-orphans || true
  notify "Agitprop deploy to ${target} FAILED: health check timed out, rolled back to ${current_tag}."
  exit 1
fi

# Keep the history bounded: the newest entries are the ones a rollback would need.
ls -1t "$history_dir"/.env.* 2>/dev/null | tail -n "+$((keep_previous + 1))" | xargs -r rm --

# Images tagged by SHA never become dangling, so they would fill the disk one deploy at a time.
docker image prune --all --force --filter "until=672h" >/dev/null || true

notify "Agitprop deployed ${target} (was ${current_tag}); web and postgres are healthy."
echo "deployed $target; web and postgres are healthy"
echo "rollback: ./deploy.sh $current_tag"
