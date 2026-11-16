#!/usr/bin/env bash
# Helpers shared by the operator scripts. Source this file, do not execute it.

# Loads the bundle's .env without executing it as shell code: values may contain
# characters that a plain `source` would mangle or interpret.
load_env() {
  local file="${1:-.env}"
  local line key value

  [[ -f "$file" ]] || return 0

  while IFS= read -r line || [[ -n "$line" ]]; do
    [[ -z "$line" || "$line" == \#* ]] && continue
    [[ "$line" != *=* ]] && continue

    key="${line%%=*}"
    value="${line#*=}"

    # Strip one layer of surrounding quotes, which is how .env files usually carry them.
    if [[ "$value" == \"*\" || "$value" == \'*\' ]]; then
      value="${value:1:${#value}-2}"
    fi

    export "$key=$value"
  done < "$file"
}

# Sends a message to a Discord webhook. Never fails the caller: a missing webhook is not an
# error, and a failed notification must not hide the result of the command that sent it.
notify() {
  local message="$1"
  local url="${DISCORD_WEBHOOK_URL:-}"

  if [[ -z "$url" ]]; then
    return 0
  fi

  local escaped="${message//\\/\\\\}"
  escaped="${escaped//\"/\\\"}"
  escaped="${escaped//$'\r'/}"
  escaped="${escaped//$'\n'/ }"

  if ! curl --silent --show-error --fail --max-time 10 \
            --header 'Content-Type: application/json' \
            --data "{\"content\": \"${escaped}\"}" \
            "$url" >/dev/null; then
    echo "warning: Discord notification failed" >&2
  fi
}
