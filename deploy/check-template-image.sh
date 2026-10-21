#!/usr/bin/env bash
# Runs a template service's image like the server does: migration step, then the Api under the x-app limits of
# compose.yml until Docker reports it healthy. CI runs it for the throwaway service Sample.
# Usage: check-template-image.sh <service in lower case, e.g. sample> <image, e.g. nullradix-dev/edvaniq/sample-api:tag>
set -euo pipefail

service=${1:?Usage: check-template-image.sh <service> <image>}
image=${2:?Usage: check-template-image.sh <service> <image>}
timeout=${CHECK_TIMEOUT:-120}
compose="$(dirname "${BASH_SOURCE[0]}")/compose.yml"

# Value of a key in the app anchor of compose.yml, so the limits are not copied here.
limit() {
  local value
  value=$(awk -v key="$1:" '/^x-app:/ { inside = 1; next } inside && /^$/ { exit } inside && $1 == key { print $2 }' "$compose")
  [[ -n $value ]] || { echo "::error::$1 is missing in the app anchor of $compose" >&2; exit 1; }
  echo "$value"
}

memory=$(limit mem_limit)
swap=$(limit memswap_limit)
pids=$(limit pids_limit)
cpus=$(limit cpus)
mysql_image=$(sed -n 's/^ *image: *\(mysql:[^[:space:]]*\).*$/\1/p' "$compose")
[[ -n $mysql_image ]] || { echo "::error::No mysql image in $compose"; exit 1; }

run="template-check-$$"
network=$run
database="$run-db"
api="$run-api"
root_password=$(openssl rand -hex 16)
service_password=$(openssl rand -hex 16)

cleanup() {
  local status=$?
  trap - EXIT
  if (( status != 0 )); then
    for container in "$api" "$database"; do
      docker inspect "$container" > /dev/null 2>&1 || continue
      echo "--- docker logs $container ---"
      docker logs --tail 100 "$container" 2>&1 || true
    done
  fi
  docker rm -fv "$api" "$database" > /dev/null 2>&1 || true
  docker network rm "$network" > /dev/null 2>&1 || true
  exit "$status"
}
trap cleanup EXIT

docker network create --internal "$network" > /dev/null
docker run -d --name "$database" --network "$network" \
  -e MYSQL_ROOT_PASSWORD="$root_password" -e MYSQL_ROOT_HOST=localhost "$mysql_image" > /dev/null

deadline=$((SECONDS + timeout))
until docker exec "$database" mysqladmin ping -h 127.0.0.1 --silent; do
  (( SECONDS < deadline )) || { echo "::error::MySQL did not come up within ${timeout}s"; exit 1; }
  sleep 2
done

docker exec -e MYSQL_PWD="$root_password" "$database" mysql -uroot -e "
  CREATE DATABASE ${service}db;
  CREATE USER '${service}'@'%' IDENTIFIED BY '${service_password}';
  GRANT ALL PRIVILEGES ON ${service}db.* TO '${service}'@'%';"

# What the Api and the migration step get from the app anchor and from <service>.env in compose.yml, except the
# database server, which is a container here.
app=(
  --network "$network"
  --memory "$memory" --memory-swap "$swap" --pids-limit "$pids" --cpus "$cpus"
  --cap-drop ALL --security-opt no-new-privileges:true --read-only --tmpfs /tmp
  -e ASPNETCORE_ENVIRONMENT=Production
  -e "ASPNETCORE_HTTP_PORTS=8080;8081"
  -e Authentication__Schemes__Bearer__ValidIssuer=https://issuer.invalid
  -e Authentication__Schemes__Bearer__Authority=http://identity-api:8080
  -e Authentication__Schemes__Bearer__RequireHttpsMetadata=false
  -e "ConnectionStrings__${service}db=Server=$database;Port=3306;User ID=$service;Password=$service_password;Database=${service}db"
)

echo "Migration step of $image"
docker run --rm "${app[@]}" "$image" migrate

# The health check of compose.yml: no curl in the image, so bash talks to the internal port.
probe='exec 3<>/dev/tcp/127.0.0.1/8081 && printf "GET /health HTTP/1.0\r\n\r\n" >&3 && head -n 1 <&3 | grep -q " 200 "'
docker run -d --name "$api" "${app[@]}" \
  --health-cmd "bash -c '$probe'" --health-interval 30s --health-timeout 5s --health-retries 3 \
  --health-start-period 30s --health-start-interval 2s \
  "$image" > /dev/null

deadline=$((SECONDS + timeout))
until [[ $(docker inspect -f '{{.State.Health.Status}}' "$api") == healthy ]]; do
  [[ $(docker inspect -f '{{.State.Status}}' "$api") == running ]] \
    || { echo "::error::$image stopped before it reported healthy"; exit 1; }
  (( SECONDS < deadline )) || { echo "::error::$image was not healthy within ${timeout}s"; exit 1; }
  sleep 2
done

docker logs "$api" 2>&1 | grep -F 'GC memory limit' || true
echo "$image is healthy with $memory memory, $cpus cpu and $pids pids"