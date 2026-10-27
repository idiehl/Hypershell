#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat >&2 <<'EOF'
Usage: promote-profile-server.sh --image IMAGE --backup PATH [--candidate-port PORT] [--inactive-port PORT] [--target-data-root /srv/hypershell/server/DIRECTORY] [--release-signer-sha256 SHA256]

Validates IMAGE against an isolated restored copy, then replaces the canonical
profile-server container on the real data source and atomically switches the
Nginx upstream. The previous container is retained under a timestamped rollback
name. When --target-data-root is supplied, stopped production data is copied to
that empty directory and both automatic and named rollback preserve later
writes. Run only after nginx-upstream.conf and the upstream-based HTTPS sites
are installed.
EOF
  exit 2
}

image=""
backup=""
candidate_port=15089
inactive_port=""
container="hypershell-profile-server"
secret_dir="/etc/hypershell-profile-server/secrets"
upstream_path="/etc/nginx/conf.d/hypershell-profile-upstream.conf"
exports_root="/srv/hypershell/exports"
target_data_root=""
requested_signer_fingerprint=""
while (($#)); do
  case "$1" in
    --image) image="${2:-}"; shift 2 ;;
    --backup) backup="${2:-}"; shift 2 ;;
    --candidate-port) candidate_port="${2:-}"; shift 2 ;;
    --inactive-port) inactive_port="${2:-}"; shift 2 ;;
    --container) container="${2:-}"; shift 2 ;;
    --secret-dir) secret_dir="${2:-}"; shift 2 ;;
    --exports-root) exports_root="${2:-}"; shift 2 ;;
    --target-data-root) target_data_root="${2:-}"; shift 2 ;;
    --release-signer-sha256)
      requested_signer_fingerprint="${2:-}"
      shift 2
      ;;
    *) usage ;;
  esac
done

[[ $EUID -eq 0 ]] || { echo "Run as root." >&2; exit 1; }
[[ -n "$image" && -n "$backup" ]] || usage
[[ "$image" =~ ^[A-Za-z0-9][A-Za-z0-9_./:@-]{0,255}$ ]] || usage
[[ "$container" == hypershell-profile-server ]] || {
  echo "The production container must use the canonical name." >&2
  exit 1
}
[[ "$candidate_port" =~ ^[0-9]+$ && "$candidate_port" -ge 1024 && "$candidate_port" -le 65535 ]] || usage
if [[ -n "$requested_signer_fingerprint" ]]; then
  requested_signer_fingerprint=${requested_signer_fingerprint,,}
  [[ "$requested_signer_fingerprint" =~ ^[0-9a-f]{64}$ ]] || {
    echo "The requested release-signer SHA-256 fingerprint is malformed." >&2
    exit 1
  }
fi
command -v docker >/dev/null || { echo "Docker is required." >&2; exit 1; }
command -v nginx >/dev/null || { echo "Nginx is required." >&2; exit 1; }
command -v curl >/dev/null || { echo "curl is required." >&2; exit 1; }
command -v getent >/dev/null || { echo "getent is required." >&2; exit 1; }
getent passwd 1654 >/dev/null || {
  echo "Host user for runtime UID 1654 is missing; create the container ownership mapping before promotion." >&2
  exit 1
}
getent group 1654 >/dev/null || {
  echo "Host group for runtime GID 1654 is missing; create the container ownership mapping before promotion." >&2
  exit 1
}
docker image inspect "$image" >/dev/null 2>&1 || {
  echo "Image '$image' is not present on this host." >&2
  exit 1
}
docker inspect "$container" >/dev/null 2>&1 || {
  echo "The canonical production container is missing." >&2
  exit 1
}

backup=$(readlink -f -- "$backup")
secret_dir=$(readlink -f -- "$secret_dir")
exports_root=$(readlink -m -- "$exports_root")
[[ -d "$backup" && "$backup" != / ]] || { echo "The backup is missing." >&2; exit 1; }
[[ -f "$backup/backup-metadata.env" && -f "$backup/profile-data.tar.gz" ]] || {
  echo "The selected directory is not a complete profile-server backup." >&2
  exit 1
}
backup_age=$(( $(date +%s) - $(stat -c '%Y' "$backup/profile-data.tar.gz") ))
[[ "$backup_age" -ge 0 && "$backup_age" -le 7200 ]] || {
  echo "Create a fresh consistent backup before promotion (maximum age: two hours)." >&2
  exit 1
}
[[ "$exports_root" == /srv/hypershell/exports || "$exports_root" == /opt/hypershell/exports ]] || {
  echo "The export root is outside the approved locations." >&2
  exit 1
}
[[ -f "$upstream_path" ]] || {
  echo "Install deploy/profile-server/nginx-upstream.conf before cutover." >&2
  exit 1
}
grep -Fq 'upstream hypershell_profile_backend' "$upstream_path" || {
  echo "The installed Nginx upstream is not the Hypershell upstream." >&2
  exit 1
}
for file in data-protection.pfx data-protection.password ssh-vault-master-key.bin; do
  [[ -f "$secret_dir/$file" ]] || { echo "Required secret '$file' is missing." >&2; exit 1; }
done

active_endpoint=$(docker port "$container" 8080/tcp | head -n 1)
[[ "$active_endpoint" =~ ^127\.0\.0\.1:([0-9]{4,5})$ ]] || {
  echo "The production service is not bound to IPv4 loopback." >&2
  exit 1
}
active_port=${BASH_REMATCH[1]}
if [[ -z "$inactive_port" ]]; then
  if [[ "$active_port" == 5088 ]]; then inactive_port=5089; else inactive_port=5088; fi
fi
[[ "$inactive_port" =~ ^[0-9]+$ && "$inactive_port" -ge 1024 && "$inactive_port" -le 65535 ]] || usage
[[ "$inactive_port" != "$active_port" && "$inactive_port" != "$candidate_port" ]] || {
  echo "Candidate, active, and inactive ports must be distinct." >&2
  exit 1
}
if command -v fuser >/dev/null 2>&1 \
   && fuser -n tcp "$inactive_port" >/dev/null 2>&1; then
  echo "Inactive port $inactive_port is already in use." >&2
  exit 1
fi

source_data_type=$(docker inspect -f '{{range .Mounts}}{{if eq .Destination "/var/lib/hypershell-profile-server"}}{{.Type}}{{end}}{{end}}' "$container")
source_data_source=$(docker inspect -f '{{range .Mounts}}{{if eq .Destination "/var/lib/hypershell-profile-server"}}{{.Source}}{{end}}{{end}}' "$container")
source_data_name=$(docker inspect -f '{{range .Mounts}}{{if eq .Destination "/var/lib/hypershell-profile-server"}}{{.Name}}{{end}}{{end}}' "$container")
source_data_source=$(readlink -f -- "$source_data_source")
if [[ "$source_data_type" == volume && "$source_data_source" == /var/lib/docker/volumes/*/_data && -n "$source_data_name" ]]; then
  source_data_mount=(--volume "$source_data_name:/var/lib/hypershell-profile-server")
elif [[ "$source_data_type" == bind && "$source_data_source" == /srv/hypershell/server/* ]]; then
  source_data_mount=(--mount "type=bind,src=$source_data_source,dst=/var/lib/hypershell-profile-server")
else
  echo "The production data source is not an approved named volume or /srv bind mount." >&2
  exit 1
fi

migration_mode=0
target_data_source=$source_data_source
data_mount=("${source_data_mount[@]}")
if [[ -n "$target_data_root" ]]; then
  command -v rsync >/dev/null || { echo "rsync is required for data migration." >&2; exit 1; }
  target_data_source=$(readlink -m -- "$target_data_root")
  [[ "$target_data_source" =~ ^/srv/hypershell/server/[A-Za-z0-9._/-]+$ ]] || {
    echo "The target data root must remain beneath /srv/hypershell/server." >&2
    exit 1
  }
  [[ "$target_data_source" != "$source_data_source" ]] || {
    echo "The target data root is already the production data source." >&2
    exit 1
  }
  target_parent=$(dirname -- "$target_data_source")
  [[ -d "$target_parent" ]] || {
    echo "The target parent directory is missing." >&2
    exit 1
  }
  [[ "$(findmnt -nro TARGET -T "$target_parent")" == /srv/hypershell ]] || {
    echo "The target data root is not on the dedicated Hypershell mount." >&2
    exit 1
  }
  if [[ -e "$target_data_source" ]] &&
     [[ -n "$(find "$target_data_source" -mindepth 1 -maxdepth 1 -print -quit)" ]]; then
    echo "The target data root must be absent or empty." >&2
    exit 1
  fi
  migration_mode=1
  data_mount=(--mount "type=bind,src=$target_data_source,dst=/var/lib/hypershell-profile-server")
fi

exec 9>/run/lock/hypershell-profile-deploy.lock
flock -n 9 || { echo "Another Hypershell deployment is running." >&2; exit 1; }

script_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
"$script_root/restore-profile-server.sh" \
  --backup "$backup" \
  --image "$image" \
  --port "$candidate_port" \
  --secret-dir "$secret_dir"

stamp=$(date -u +%Y%m%dT%H%M%SZ)
rollback_container="$container-rollback-$stamp"
rollback_root="/var/lib/hypershell-deployments"
rollback_upstream="$rollback_root/upstream-$stamp.conf"
evidence="$rollback_root/cutover-$stamp.env"
install -d -m 0700 "$rollback_root"
install -m 0600 "$upstream_path" "$rollback_upstream"
old_image=$(docker inspect -f '{{.Config.Image}}' "$container")
old_signer_fingerprint=$(docker inspect -f '{{range .Config.Env}}{{println .}}{{end}}' "$container" |
  sed -n 's/^HYPERSHELL_RELEASE_SIGNER_SHA256=//p' | head -n 1)
signer_fingerprint=$old_signer_fingerprint
if [[ -n "$requested_signer_fingerprint" ]]; then
  signer_fingerprint=$requested_signer_fingerprint
fi
[[ -z "$signer_fingerprint" || "$signer_fingerprint" =~ ^[0-9a-f]{64}$ ]] || {
  echo "The active release-signer fingerprint is malformed." >&2
  exit 1
}
old_renamed=0
new_container_started=0

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

rollback_cutover() {
  local status=$?
  trap - ERR
  set +e
  if [[ $old_renamed -eq 1 ]] && docker inspect "$container" >/dev/null 2>&1; then
    docker stop --time 30 "$container" >/dev/null 2>&1 || true
    if [[ $migration_mode -eq 1 && $new_container_started -eq 1 && \
          -d "$target_data_source" ]]; then
      rsync -aHAX --numeric-ids --delete \
        "$target_data_source/" "$source_data_source/" >/dev/null 2>&1 || true
    fi
    docker rm "$container" >/dev/null 2>&1
  fi
  if [[ $old_renamed -eq 1 ]] && docker inspect "$rollback_container" >/dev/null 2>&1; then
    docker rename "$rollback_container" "$container" >/dev/null 2>&1
    docker start "$container" >/dev/null 2>&1
  elif docker inspect "$container" >/dev/null 2>&1; then
    docker start "$container" >/dev/null 2>&1
  fi
  install -m 0644 "$rollback_upstream" "$upstream_path"
  nginx -t >/dev/null 2>&1 && systemctl reload nginx
  exit "$status"
}
trap rollback_cutover ERR

docker stop --time 30 "$container" >/dev/null
docker rename "$container" "$rollback_container"
old_renamed=1
if [[ $migration_mode -eq 1 ]]; then
  if [[ -e "$target_data_source" ]] &&
     [[ -n "$(find "$target_data_source" -mindepth 1 -maxdepth 1 -print -quit)" ]]; then
    echo "The target data root changed during validation; refusing migration." >&2
    false
  fi
  install -d -m 0750 -o 1654 -g 1654 "$target_data_source"
  rsync -aHAX --numeric-ids --delete \
    "$source_data_source/" "$target_data_source/"
  migration_delta=$(rsync -aHAXn --numeric-ids --delete --itemize-changes \
    "$source_data_source/" "$target_data_source/")
  [[ -z "$migration_delta" ]] || {
    echo "The migrated data differs from the stopped production source." >&2
    false
  }
fi
install -d -m 0750 "$exports_root"
chown root:1654 "$exports_root"
docker run -d \
  --name "$container" \
  --user 1654:1654 \
  --restart unless-stopped \
  --read-only \
  --tmpfs /tmp:size=64m,mode=1777 \
  --security-opt no-new-privileges:true \
  --cap-drop ALL \
  --memory 512m \
  --cpus 1.0 \
  --pids-limit 128 \
  --stop-timeout 30 \
  --log-driver json-file \
  --log-opt max-size=10m \
  --log-opt max-file=3 \
  --publish "127.0.0.1:$inactive_port:8080" \
  "${data_mount[@]}" \
  --mount "type=bind,src=$exports_root,dst=/var/lib/hypershell-backup-exports,readonly" \
  --mount "type=bind,src=$secret_dir/data-protection.pfx,dst=/run/secrets/hypershell_data_protection_certificate,readonly" \
  --mount "type=bind,src=$secret_dir/data-protection.password,dst=/run/secrets/hypershell_data_protection_password,readonly" \
  --mount "type=bind,src=$secret_dir/ssh-vault-master-key.bin,dst=/run/secrets/hypershell_ssh_vault_master_key,readonly" \
  --env ASPNETCORE_HTTP_PORTS=8080 \
  --env HYPERSHELL_PROFILE_SERVER_DATA=/var/lib/hypershell-profile-server \
  --env HYPERSHELL_PROFILE_SESSION_MINUTES=30 \
  --env HYPERSHELL_PROFILE_ACCOUNT_STORAGE_MIB=512 \
  --env HYPERSHELL_MINIMUM_FREE_STORAGE_MIB=512 \
  --env HYPERSHELL_PUBLIC_PORTAL_ORIGIN=https://admin.atlasuniversalis.com \
  --env "HYPERSHELL_RELEASE_SIGNER_SHA256=$signer_fingerprint" \
  --env HYPERSHELL_STABLE_RELEASE_RETENTION=3 \
  --env HYPERSHELL_DATA_PROTECTION_CERTIFICATE=/run/secrets/hypershell_data_protection_certificate \
  --env HYPERSHELL_DATA_PROTECTION_CERTIFICATE_PASSWORD_FILE=/run/secrets/hypershell_data_protection_password \
  --env HYPERSHELL_SSH_VAULT_MASTER_KEY_FILE=/run/secrets/hypershell_ssh_vault_master_key \
  --env HYPERSHELL_BACKUP_EXPORTS=/var/lib/hypershell-backup-exports \
  "$image" >/dev/null
new_container_started=1

for _ in $(seq 1 60); do
  if curl --fail --silent "http://127.0.0.1:$inactive_port/healthz" >/dev/null; then break; fi
  sleep 1
done
curl --fail --silent --show-error "http://127.0.0.1:$inactive_port/healthz" >/dev/null
[[ "$(docker inspect -f '{{.RestartCount}}' "$container")" == 0 ]] || {
  echo "The promoted container restarted during migration validation." >&2
  false
}

temporary_upstream=$(mktemp "$(dirname "$upstream_path")/.hypershell-upstream.XXXXXX")
cat > "$temporary_upstream" <<EOF
upstream hypershell_profile_backend {
    server 127.0.0.1:$inactive_port;
    keepalive 16;
}
EOF
chmod 0644 "$temporary_upstream"
mv "$temporary_upstream" "$upstream_path"
nginx -t
systemctl reload nginx
wait_public_health

cat > "$evidence" <<EOF
HYPERSHELL_CUTOVER_SCHEMA=1
HYPERSHELL_CUTOVER_AT_UTC=$stamp
HYPERSHELL_CUTOVER_BACKUP=$backup
HYPERSHELL_CUTOVER_OLD_IMAGE=$old_image
HYPERSHELL_CUTOVER_NEW_IMAGE=$image
HYPERSHELL_CUTOVER_OLD_PORT=$active_port
HYPERSHELL_CUTOVER_NEW_PORT=$inactive_port
HYPERSHELL_CUTOVER_ROLLBACK_CONTAINER=$rollback_container
HYPERSHELL_CUTOVER_ROLLBACK_UPSTREAM=$rollback_upstream
HYPERSHELL_CUTOVER_DATA_MIGRATION=$migration_mode
HYPERSHELL_CUTOVER_OLD_DATA_SOURCE=$source_data_source
HYPERSHELL_CUTOVER_NEW_DATA_SOURCE=$target_data_source
HYPERSHELL_CUTOVER_OLD_RELEASE_SIGNER_SHA256=$old_signer_fingerprint
HYPERSHELL_CUTOVER_NEW_RELEASE_SIGNER_SHA256=$signer_fingerprint
EOF
chmod 0600 "$evidence"
trap - ERR
echo "HYPERSHELL_CUTOVER_OK|image:$image|port:$inactive_port|rollback:$rollback_container|evidence:$evidence"
