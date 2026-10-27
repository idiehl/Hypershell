#!/usr/bin/env bash
set -euo pipefail

usage() {
  echo "Usage: rollback-profile-server.sh --evidence /var/lib/hypershell-deployments/cutover-TIMESTAMP.env" >&2
  exit 2
}

evidence=""
while (($#)); do
  case "$1" in
    --evidence) evidence="${2:-}"; shift 2 ;;
    *) usage ;;
  esac
done
[[ $EUID -eq 0 ]] || { echo "Run as root." >&2; exit 1; }
[[ -n "$evidence" ]] || usage
evidence=$(readlink -f -- "$evidence")
[[ "$evidence" == /var/lib/hypershell-deployments/cutover-*.env && -f "$evidence" ]] || usage

read_field() {
  local key=$1 value
  value=$(sed -n "s/^${key}=//p" "$evidence")
  [[ $(grep -c "^${key}=" "$evidence") -eq 1 ]] || return 1
  printf '%s' "$value"
}

read_optional_field() {
  local key=$1 default_value=$2 count value
  count=$(grep -c "^${key}=" "$evidence" || true)
  if [[ "$count" -eq 0 ]]; then
    printf '%s' "$default_value"
    return 0
  fi
  [[ "$count" -eq 1 ]] || return 1
  value=$(sed -n "s/^${key}=//p" "$evidence")
  printf '%s' "$value"
}

old_port=$(read_field HYPERSHELL_CUTOVER_OLD_PORT)
new_port=$(read_field HYPERSHELL_CUTOVER_NEW_PORT)
rollback_container=$(read_field HYPERSHELL_CUTOVER_ROLLBACK_CONTAINER)
rollback_upstream=$(read_field HYPERSHELL_CUTOVER_ROLLBACK_UPSTREAM)
migration_mode=$(read_optional_field HYPERSHELL_CUTOVER_DATA_MIGRATION 0)
old_data_source=$(read_optional_field HYPERSHELL_CUTOVER_OLD_DATA_SOURCE "")
new_data_source=$(read_optional_field HYPERSHELL_CUTOVER_NEW_DATA_SOURCE "")
[[ "$old_port" =~ ^[0-9]{4,5}$ && "$new_port" =~ ^[0-9]{4,5}$ ]] || usage
[[ "$rollback_container" =~ ^hypershell-profile-server-rollback-[0-9TZ-]+$ ]] || usage
[[ "$migration_mode" == 0 || "$migration_mode" == 1 ]] || usage
rollback_upstream=$(readlink -f -- "$rollback_upstream")
[[ "$rollback_upstream" == /var/lib/hypershell-deployments/upstream-*.conf && -f "$rollback_upstream" ]] || usage

container="hypershell-profile-server"
upstream_path="/etc/nginx/conf.d/hypershell-profile-upstream.conf"
docker inspect "$container" >/dev/null 2>&1 || { echo "The active container is missing." >&2; exit 1; }
docker inspect "$rollback_container" >/dev/null 2>&1 || { echo "The rollback container is missing." >&2; exit 1; }
[[ "$(docker inspect -f '{{.State.Running}}' "$rollback_container")" == false ]] || {
  echo "The rollback container must be stopped." >&2
  exit 1
}

if [[ "$migration_mode" == 1 ]]; then
  command -v rsync >/dev/null || { echo "rsync is required for migrated-data rollback." >&2; exit 1; }
  old_data_source=$(readlink -f -- "$old_data_source")
  new_data_source=$(readlink -f -- "$new_data_source")
  [[ -d "$old_data_source" && -d "$new_data_source" ]] || {
    echo "A migrated rollback data source is missing." >&2
    exit 1
  }
  [[ "$old_data_source" == /var/lib/docker/volumes/*/_data || \
     "$old_data_source" == /srv/hypershell/server/* ]] || usage
  [[ "$new_data_source" == /srv/hypershell/server/* && \
     "$new_data_source" != "$old_data_source" ]] || usage
  active_data_source=$(docker inspect -f '{{range .Mounts}}{{if eq .Destination "/var/lib/hypershell-profile-server"}}{{.Source}}{{end}}{{end}}' "$container")
  rollback_data_source=$(docker inspect -f '{{range .Mounts}}{{if eq .Destination "/var/lib/hypershell-profile-server"}}{{.Source}}{{end}}{{end}}' "$rollback_container")
  [[ "$(readlink -f -- "$active_data_source")" == "$new_data_source" && \
     "$(readlink -f -- "$rollback_data_source")" == "$old_data_source" ]] || {
    echo "The active and rollback containers do not match the migration evidence." >&2
    exit 1
  }
fi

exec 9>/run/lock/hypershell-profile-deploy.lock
flock -n 9 || { echo "Another Hypershell deployment is running." >&2; exit 1; }
stamp=$(date -u +%Y%m%dT%H%M%SZ)
forward_container="$container-forward-$stamp"
current_upstream=$(mktemp /etc/nginx/conf.d/.hypershell-forward.XXXXXX)
install -m 0644 "$upstream_path" "$current_upstream"
forward_renamed=0
rollback_promoted=0

sync_data() {
  local source=$1 destination=$2 delta
  rsync -aHAX --numeric-ids --delete "$source/" "$destination/"
  delta=$(rsync -aHAXn --numeric-ids --delete --itemize-changes \
    "$source/" "$destination/")
  [[ -z "$delta" ]] || {
    echo "The synchronized rollback data roots differ." >&2
    return 1
  }
}

wait_public_health() {
  for _ in $(seq 1 30); do
    if curl --fail --silent \
      --resolve hypershell.atlasuniversalis.com:443:127.0.0.1 \
      https://hypershell.atlasuniversalis.com/healthz >/dev/null; then
      return 0
    fi
    sleep 1
  done
  curl --fail --silent --show-error \
    --resolve hypershell.atlasuniversalis.com:443:127.0.0.1 \
    https://hypershell.atlasuniversalis.com/healthz >/dev/null
}

restore_forward() {
  local status=$?
  trap - ERR
  set +e
  if [[ $rollback_promoted -eq 1 ]] && docker inspect "$container" >/dev/null 2>&1; then
    docker stop --time 30 "$container" >/dev/null 2>&1
    if [[ "$migration_mode" == 1 ]]; then
      sync_data "$old_data_source" "$new_data_source" >/dev/null 2>&1 || true
    fi
    docker rename "$container" "$rollback_container" >/dev/null 2>&1
  fi
  if [[ $forward_renamed -eq 1 ]] && docker inspect "$forward_container" >/dev/null 2>&1; then
    docker rename "$forward_container" "$container" >/dev/null 2>&1
    docker start "$container" >/dev/null 2>&1
  elif docker inspect "$container" >/dev/null 2>&1; then
    docker start "$container" >/dev/null 2>&1
  fi
  install -m 0644 "$current_upstream" "$upstream_path"
  nginx -t >/dev/null 2>&1 && systemctl reload nginx
  rm -f "$current_upstream"
  exit "$status"
}
trap restore_forward ERR

docker stop --time 30 "$container" >/dev/null
docker rename "$container" "$forward_container"
forward_renamed=1
if [[ "$migration_mode" == 1 ]]; then
  sync_data "$new_data_source" "$old_data_source"
fi
docker rename "$rollback_container" "$container"
rollback_promoted=1
docker start "$container" >/dev/null
for _ in $(seq 1 60); do
  if curl --fail --silent "http://127.0.0.1:$old_port/healthz" >/dev/null; then break; fi
  sleep 1
done
curl --fail --silent --show-error "http://127.0.0.1:$old_port/healthz" >/dev/null
install -m 0644 "$rollback_upstream" "$upstream_path"
nginx -t
systemctl reload nginx
wait_public_health

rm -f "$current_upstream"
trap - ERR
rollback_evidence="/var/lib/hypershell-deployments/rollback-$stamp.env"
cat > "$rollback_evidence" <<EOF
HYPERSHELL_ROLLBACK_SCHEMA=1
HYPERSHELL_ROLLBACK_AT_UTC=$stamp
HYPERSHELL_ROLLBACK_FROM_PORT=$new_port
HYPERSHELL_ROLLBACK_TO_PORT=$old_port
HYPERSHELL_ROLLBACK_FORWARD_CONTAINER=$forward_container
HYPERSHELL_ROLLBACK_SOURCE_EVIDENCE=$evidence
HYPERSHELL_ROLLBACK_DATA_MIGRATION=$migration_mode
EOF
chmod 0600 "$rollback_evidence"
echo "HYPERSHELL_ROLLBACK_OK|port:$old_port|forward:$forward_container|evidence:$rollback_evidence"
