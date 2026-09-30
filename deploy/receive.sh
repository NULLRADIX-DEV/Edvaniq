#!/usr/bin/env bash
# SSH forced command for the CI deploy key, installed once as bin/receive in the deploy directory (EdvaniqDoc/Betrieb.md).
# Accepts only "deploy <commit>" with a tar of the repo's deploy/ folder at that commit on stdin,
# stores it as releases/<commit>/ and runs its deploy.sh. That way deploy logic and compose file always come from the commit.
set -euo pipefail

read -r action sha rest <<< "${SSH_ORIGINAL_COMMAND:-}"
if [[ $action != deploy || ! $sha =~ ^[0-9a-f]{40}$ || -n $rest ]]; then
  echo "usage: deploy <40-char commit>" >&2
  exit 2
fi

base=$(dirname "$(dirname "$(readlink -f "$0")")")
mkdir -p "$base/releases"
incoming=$(mktemp -d "$base/releases/.incoming.XXXXXX")
trap 'rm -rf "$incoming"' EXIT

head -c 1048576 | tar -x -C "$incoming" --strip-components=1
[[ -f $incoming/deploy.sh && -f $incoming/compose.yml ]] || { echo "archive lacks deploy.sh or compose.yml" >&2; exit 1; }

release="$base/releases/$sha"
rm -rf "$release"
mv "$incoming" "$release"
trap - EXIT
exec bash "$release/deploy.sh" "$sha"
