#!/usr/bin/env bash
# Manual, pull-based deployment of the Agitprop stack.
#
# The CI publishes every build under its commit SHA and moves the "stable" tag onto it. A host
# that runs this script once with a SHA pins itself to that build and stops automatic updates;
# running it with "stable" hands the stack back to Watchtower.
#
# Usage:
#   ./deploy.sh <commit-sha> [--dry-run]   # pin/freeze at that build
#   ./deploy.sh stable [--dry-run]         # follow the moving tag again
#   ./deploy.sh current [--dry-run]        # restart whatever .env names now
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

if [[ "$target" != "current" && ! "$target" =~ ^[0-9a-f]{7,40}$ && ! "$target" =~ ^[a-z][a-z0-9._-]{1,63}$ ]]; then
  echo "error: '$target' is not a commit SHA (7-40 lowercase hex characters), a tag such as 'stable', or 'current'" >&2
  exit 2
fi

for required in "$compose_file" "$env_file"; do
  if [[ ! -f "$required" ]]; then
    echo "error: $required not found in $(pwd); run this script from the unzipped bundle" >&2
    exit 1
  fi
done

current_tag="$(grep -oE '/web:[^[:space:]]+' "$env_file" | cut -d: -f2 || true)"
current_tag="${current_tag:-unknown}"

if [[ "$target" == "current" ]]; then
  target="$current_tag"
  if [[ "$target" == "unknown" ]]; then
    echo "error: .env has no image tag to keep" >&2
    exit 1
  fi
fi

if [[ "$target" == "$current_tag" ]]; then
  echo "already on $target; nothing to do (name another tag or commit to move this stack)"
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
if [[ "$target" == "stable" ]]; then
  echo "Watchtower follows 'stable' again, so the next merge deploys itself."
else
  echo "pinned to $target: Watchtower will not move this stack until you run ./deploy.sh stable"
fi
echo "rollback: ./deploy.sh $current_tag"
