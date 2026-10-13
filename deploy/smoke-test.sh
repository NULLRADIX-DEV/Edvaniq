#!/usr/bin/env bash
# Smoke test after a deploy. The workflow Deploy runs it from outside, over the public address: the shell loads, and a
# ping goes through web host, gateway and Planning into its database and back. The new release may still be warming
# up, so the whole check gets a few tries. If it fails after a deploy, the workflow rolls back by itself.
set -euo pipefail

base=${SMOKE_BASE_URL:-https://edvaniq.nullradix.de}
tries=${SMOKE_TRIES:-6}
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

check() {
  local status
  status=$(curl -sS -o "$work/page" -w '%{http_code}' --max-time 15 "$base/") || return 1
  [[ $status == 200 ]] || { echo "GET / answered $status"; return 1; }
  grep -q '_framework/blazor.web.js' "$work/page" || { echo "GET / has no Blazor shell"; return 1; }

  status=$(curl -sS -c "$work/cookies" -o /dev/null -w '%{http_code}' --max-time 15 -X POST "$base/session") || return 1
  [[ $status == 204 ]] || { echo "POST /session answered $status"; return 1; }

  status=$(curl -sS -b "$work/cookies" -o "$work/ping" -w '%{http_code}' --max-time 20 -X POST "$base/api/planning/ping") || return 1
  [[ $status == 200 ]] || { echo "POST /api/planning/ping answered $status: $(head -c 300 "$work/ping")"; return 1; }
  python3 - "$work/ping" << 'PY' || return 1
import json
import sys

ping = json.load(open(sys.argv[1]))
if not (isinstance(ping.get("id"), int) and ping["id"] > 0 and isinstance(ping.get("pingedAt"), str)):
    sys.exit(f"The ping came back without the value from the database: {ping}")
print(f"Ping {ping['id']}, stored at {ping['pingedAt']}")
PY
}

for try in $(seq 1 "$tries"); do
  if check; then
    echo "Smoke test passed against $base"
    exit 0
  fi
  if (( try < tries )); then
    sleep 10
  fi
done
echo "::error::Smoke test failed against $base after $tries tries"
exit 1