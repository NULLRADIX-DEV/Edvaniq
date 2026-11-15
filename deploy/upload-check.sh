#!/usr/bin/env bash
# Upload check over the public address (docs/tests/upload-20mb.md). Logs in with an account from the environment
# (EDVANIQ_ACCOUNT, EDVANIQ_PASSWORD), uploads a file of exactly 20 MB, one byte more and one far too large, and prints
# status and duration of each. The files are only a PDF header and zeros: they get through the upload and fail later
# in the worker as unreadable, without reaching the AI.
#
#   EDVANIQ_ACCOUNT=… EDVANIQ_PASSWORD=… deploy/upload-check.sh https://<domain> [rate]
#
# rate caps the upload like a slow line, in curl's notation (100K is about 0.8 Mbit/s, 20 MB then take about 3.5 min).
set -euo pipefail

base=${1:?usage: upload-check.sh <base-url> [rate]}
base=${base%/}
rate=${2:-}
: "${EDVANIQ_ACCOUNT:?EDVANIQ_ACCOUNT is not set}"
: "${EDVANIQ_PASSWORD:?EDVANIQ_PASSWORD is not set}"

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
umask 077

mib=$((1024 * 1024))
file() {
  printf '%%PDF-1.4\n' > "$work/$1"
  truncate -s "$2" "$work/$1"
}
file exact.pdf $((20 * mib))
file over.pdf $((20 * mib + 1))
file far.pdf $((25 * mib))

# The password goes into a file in the private temp folder, never onto the command line.
json() {
  local value=${1//'\'/'\\'}
  printf '%s' "${value//\"/\\\"}"
}
printf '{"name":"%s","password":"%s"}' "$(json "$EDVANIQ_ACCOUNT")" "$(json "$EDVANIQ_PASSWORD")" > "$work/login.json"

status=$(curl -sS -c "$work/cookies" -o /dev/null -w '%{http_code}' --max-time 20 \
  -H 'Content-Type: application/json' --data-binary @"$work/login.json" "$base/session/login")
rm -f "$work/login.json"
[[ $status == 204 ]] || { echo "login answered $status"; exit 1; }

failed=0
upload() {
  local name=$1 expected=$2 limit=() result status
  [[ -n $rate ]] && limit=(--limit-rate "$rate")
  result=$(curl -sS -b "$work/cookies" -o "$work/answer" -w '%{http_code} %{time_total} %{size_upload}' --max-time 900 \
    "${limit[@]}" -F "file=@$work/$name;type=application/pdf;filename=upload-check.pdf" \
    "$base/api/content/documents/files?confirmation=material-1") || result="000 - -"
  read -r status seconds sent <<< "$result"
  local title
  title=$(grep -o '"title":"[^"]*"' "$work/answer" 2> /dev/null | head -n 1 || true)
  printf '%-10s %s (expected %s), %s s, %s bytes sent %s\n' "$name" "$status" "$expected" "$seconds" "$sent" "$title"
  [[ $status == "$expected" ]] || failed=1
}

upload exact.pdf 201
upload over.pdf 413
upload far.pdf 413

curl -sS -b "$work/cookies" -o /dev/null --max-time 20 -X DELETE "$base/session" || true
exit $failed