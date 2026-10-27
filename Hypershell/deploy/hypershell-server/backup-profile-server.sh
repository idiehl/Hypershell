#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat >&2 <<'EOF'
Usage: backup-profile-server.sh [DESTINATION_ROOT] [--container NAME] [--deployment-root PATH] [--health-url URL]

Creates a stopped-container-consistent backup of the exact data source mounted
at /var/lib/hypershell-profile-server. The script supports both Docker named
volumes and a bind mount beneath /srv/hypershell.
EOF
  exit 2
}

destination_root="/srv/hypershell/backups"
container="hypershell-profile-server"
deploy_root="/opt/hypershell/profile-server"
health_url=""
if (($#)) && [[ "${1:-}" != --* ]]; then
  destination_root=$1
  shift
fi
while (($#)); do
  case "$1" in
    --container) container="${2:-}"; shift 2 ;;
    --deployment-root) deploy_root="${2:-}"; shift 2 ;;
    --health-url) health_url="${2:-}"; shift 2 ;;
    *) usage ;;
  esac
done

[[ $EUID -eq 0 ]] || { echo "Run as root." >&2; exit 1; }
[[ "$container" =~ ^[A-Za-z0-9][A-Za-z0-9_.-]{0,127}$ ]] || usage
command -v docker >/dev/null || { echo "Docker is required." >&2; exit 1; }
docker inspect "$container" >/dev/null 2>&1 || {
  echo "Container '$container' does not exist." >&2
  exit 1
}

if [[ -z "$health_url" ]]; then
  published_endpoint=$(docker port "$container" 8080/tcp | head -n 1)
  [[ "$published_endpoint" =~ ^127\.0\.0\.1:([0-9]{4,5})$ ]] || {
    echo "Container '$container' does not publish port 8080 on IPv4 loopback." >&2
    exit 1
  }
  health_url="http://127.0.0.1:${BASH_REMATCH[1]}/healthz"
fi
[[ "$health_url" =~ ^http://127\.0\.0\.1:[0-9]{4,5}/healthz$ ]] || usage

deploy_root=$(readlink -f -- "$deploy_root")
destination_root=$(readlink -m -- "$destination_root")
[[ -d "$deploy_root" && "$deploy_root" != / ]] || {
  echo "The deployment root is missing or unsafe." >&2
  exit 1
}
[[ "$destination_root" == /srv/hypershell/backups || \
   "$destination_root" == /srv/hypershell/backups/* || \
   "$destination_root" == /opt/atlas/backups || \
   "$destination_root" == /opt/atlas/backups/* ]] || {
  echo "The backup destination must remain beneath /srv/hypershell/backups or /opt/atlas/backups." >&2
  exit 1
}

data_root=$(docker inspect -f '{{range .Mounts}}{{if eq .Destination "/var/lib/hypershell-profile-server"}}{{.Source}}{{end}}{{end}}' "$container")
data_root=$(readlink -f -- "$data_root")
[[ -d "$data_root" && ( \
   "$data_root" == /var/lib/docker/volumes/*/_data || \
   "$data_root" == /srv/hypershell/server/* ) ]] || {
  echo "The running container's data source is missing or outside the approved storage roots." >&2
  exit 1
}

exec 9>/run/lock/hypershell-profile-backup.lock
flock -n 9 || { echo "Another Hypershell backup is running." >&2; exit 1; }

stamp=$(date -u +%Y%m%dT%H%M%SZ)
destination="$destination_root/hypershell-consistent-$stamp"
install -d -m 0700 "$destination_root"
install -d -m 0700 "$destination"
was_running=$(docker inspect -f '{{.State.Running}}' "$container")
restore_service() {
  if [[ "$was_running" == true ]]; then
    docker start "$container" >/dev/null 2>&1 || true
  fi
}
trap restore_service EXIT

docker inspect "$container" > "$destination/container-inspect.json"
if [[ "$was_running" == true ]]; then
  docker stop --time 30 "$container" >/dev/null
fi
tar --xattrs --acls --numeric-owner -C "$data_root" \
  -czf "$destination/profile-data.tar.gz" .
tar --xattrs --acls --numeric-owner -C "$(dirname "$deploy_root")" \
  -czf "$destination/profile-server-deploy.tar.gz" "$(basename "$deploy_root")"
cat > "$destination/backup-metadata.env" <<EOF
HYPERSHELL_BACKUP_SCHEMA=2
HYPERSHELL_BACKUP_CREATED_AT_UTC=$stamp
HYPERSHELL_BACKUP_CONTAINER=$container
HYPERSHELL_BACKUP_IMAGE=$(docker inspect -f '{{.Config.Image}}' "$container")
HYPERSHELL_BACKUP_DATA_SOURCE=$data_root
EOF
chmod 0600 "$destination"/*
(cd "$destination" && sha256sum \
  backup-metadata.env \
  container-inspect.json \
  profile-data.tar.gz \
  profile-server-deploy.tar.gz > SHA256SUMS)
chmod 0600 "$destination/SHA256SUMS"

restore_service
trap - EXIT
if [[ "$was_running" == true ]]; then
  for _ in $(seq 1 30); do
    if curl --fail --silent "$health_url" >/dev/null; then
      break
    fi
    sleep 1
  done
  curl --fail --silent --show-error "$health_url" >/dev/null
fi

echo "HYPERSHELL_BACKUP_OK|$destination"
cat "$destination/SHA256SUMS"
