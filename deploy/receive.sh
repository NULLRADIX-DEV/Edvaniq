#!/usr/bin/env bash
# SSH forced command for the CI deploy key, installed once as bin/receive in the deploy directory (EdvaniqDoc/Betrieb.md).
# Accepts only two commands:
#   deploy <commit>  with a tar of the repo's deploy/ folder at that commit on stdin; stores it as releases/<commit>/
#                    and runs its deploy.sh. That way deploy logic and compose file always come from the commit.
#   rollback         runs deploy.sh of the previous release, which is still on the server (docs/deploy.md).
set -euo pipefail

usage() {
  echo "usage: deploy <40-char commit> | rollback" >&2
  exit 2
}

read -r action sha rest <<< "${SSH_ORIGINAL_COMMAND:-}"
base=$(dirname "$(dirname "$(readlink -f "$0")")")

case ${action:-} in
  deploy)
    [[ $sha =~ ^[0-9a-f]{40}$ && -z $rest ]] || usage
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
    ;;
  rollback)
    [[ -z $sha && -z $rest ]] || usage
    previous=$(cat "$base/previous" 2> /dev/null || true)
    if [[ ! $previous =~ ^[0-9a-f]{40}$ || ! -f $base/releases/$previous/deploy.sh ]]; then
      echo "Rollback failed: no previous release to roll back to, nothing changed" >&2
      exit 1
    fi
    echo "Rolling back to $previous"
    exec bash "$base/releases/$previous/deploy.sh" "$previous"
    ;;
  *)
    usage
    ;;
esac
