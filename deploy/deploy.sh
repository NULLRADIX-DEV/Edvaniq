#!/usr/bin/env bash
# Rolls out all processes of one commit: deploy.sh <commit>
# Runs on the VPS as the deploy user against the server's Docker, from releases/<commit>/ in the deploy directory.
# That Docker also runs other apps: only touch the edvaniq project and its images.
# Everything is checked before the switch. If the switch fails, the previous release is started again,
# so the running state is never partly old and partly new (docs/deploy.md).
# The output ends up in the public Actions log: never print paths, hosts or users.
set -euo pipefail

fail() {
  echo "Deploy failed: $*" >&2
  exit 1
}

sha=${1:-}
[[ $sha =~ ^[0-9a-f]{40}$ ]] || { echo "usage: deploy.sh <40-char commit>" >&2; exit 2; }

release_dir=$(dirname "$(readlink -f "$0")")
base=$(dirname "$(dirname "$release_dir")")
[[ $release_dir == "$base/releases/$sha" ]] || fail "deploy.sh must run from releases/$sha"

exec 9> "$base/deploy.lock"
flock --nonblock 9 || fail "another deploy is running"

compose() {
  local commit=$1
  shift
  EDVANIQ_TAG="sha-$commit" docker compose -p edvaniq --project-directory "$base" -f "$base/releases/$commit/compose.yml" "$@"
}

# Every service of the release runs, has not restarted and comes from the commit.
check_running() {
  local commit=$1 expected name status restarts revision result=0
  local -a ids
  expected=$(compose "$commit" config --services | wc -l)
  mapfile -t ids < <(compose "$commit" ps --all --quiet)
  if (( ${#ids[@]} != expected )); then
    echo "Expected $expected containers, found ${#ids[@]}" >&2
    return 1
  fi
  while read -r name status restarts revision; do
    if [[ $status != running || $restarts != 0 || $revision != "$commit" ]]; then
      echo "${name#/}: status $status, restarts $restarts, revision ${revision:-none}" >&2
      result=1
    fi
  done < <(docker inspect -f '{{.Name}} {{.State.Status}} {{.RestartCount}} {{index .Config.Labels "org.opencontainers.image.revision"}}' "${ids[@]}")
  return $result
}

switch_to() {
  compose "$1" up --detach --remove-orphans --wait --wait-timeout 120 && sleep 20 && check_running "$1"
}

# Keeps the releases and images of the current and the previous commit only.
prune() {
  local -a keep=("$sha")
  local dir image previous
  previous=$(cat "$base/previous" 2> /dev/null || true)
  if [[ -n $previous ]]; then
    keep+=("$previous")
  fi
  for dir in "$base"/releases/*/; do
    dir=${dir%/}
    if [[ " ${keep[*]} " != *" ${dir##*/} "* ]]; then
      rm -rf "$dir"
    fi
  done
  while read -r image; do
    if [[ " ${keep[*]/#/sha-} " != *" ${image##*:} "* ]]; then
      docker image rm "$image" > /dev/null
    fi
  done < <(docker image ls --format '{{.Repository}}:{{.Tag}}' --filter 'reference=ghcr.io/nullradix-dev/edvaniq/*')
}

# Checks before the switch. Up to here the running state stays as it is.
[[ -f $base/.env ]] || fail ".env is missing, nothing changed"
[[ $(stat -c %a "$base/.env") == 600 ]] || fail ".env must have mode 600, nothing changed"
compose "$sha" config --quiet || fail "compose.yml is invalid, nothing changed"

# Only missing images are pulled, so a rollback to the previous release works without the registry.
echo "Pulling images of $sha"
compose "$sha" --progress quiet pull --policy missing || fail "not all images of $sha are available, nothing changed"
while read -r image; do
  revision=$(docker image inspect -f '{{index .Config.Labels "org.opencontainers.image.revision"}}' "$image")
  [[ $revision == "$sha" ]] || fail "$image has revision ${revision:-none}, nothing changed"
done < <(compose "$sha" config --images)

running=$(cat "$base/current" 2> /dev/null || true)

echo "Switching to $sha"
if switch_to "$sha"; then
  if [[ -n $running && $running != "$sha" ]]; then
    echo "$running" > "$base/previous"
  fi
  echo "$sha" > "$base/current.new"
  mv "$base/current.new" "$base/current"
  prune || echo "Cleanup of old releases failed, the deploy itself succeeded" >&2
  echo "Deployed $sha"
  previous=$(cat "$base/previous" 2> /dev/null || true)
  if [[ -n $previous ]]; then
    echo "Previous release: $previous"
  fi
  exit 0
fi

echo "Switch to $sha failed" >&2
if [[ $running == "$sha" ]]; then
  fail "redeploy of the running commit failed, check the containers"
fi
if [[ -n $running && -d $base/releases/$running ]]; then
  echo "Restoring $running" >&2
  if switch_to "$running"; then
    fail "$running is running again"
  fi
  fail "restoring $running failed too, check the containers"
fi
compose "$sha" down
fail "no previous release to restore, nothing is running"
