#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat >&2 <<'EOF'
Usage: restore-profile-server.sh --backup PATH --image IMAGE [--port PORT] [--keep]

Restores a verified backup into a newly created, isolated Docker volume and
starts a disposable loopback-only account-service container. Production
container and volume names are explicitly rejected. Without --keep, the test
container and volume are removed after health and restart checks pass.
EOF
  exit 2
}

backup=""
image=""
port=15089
keep=0
secret_dir="/etc/hypershell-profile-server/secrets"
while (($#)); do
  case "$1" in
    --backup) backup="${2:-}"; shift 2 ;;
    --image) image="${2:-}"; shift 2 ;;
    --port) port="${2:-}"; shift 2 ;;
    --secret-dir) secret_dir="${2:-}"; shift 2 ;;
    --keep) keep=1; shift ;;
    *) usage ;;
  esac
done

[[ $EUID -eq 0 ]] || { echo "Run as root." >&2; exit 1; }
[[ -n "$backup" && -n "$image" ]] || usage
[[ "$image" =~ ^[A-Za-z0-9][A-Za-z0-9_./:@-]{0,255}$ ]] || usage
[[ "$port" =~ ^[0-9]+$ && "$port" -ge 1024 && "$port" -le 65535 ]] || usage
command -v docker >/dev/null || { echo "Docker is required." >&2; exit 1; }

backup=$(readlink -f -- "$backup")
secret_dir=$(readlink -f -- "$secret_dir")
[[ -d "$backup" && "$backup" != / ]] || {
  echo "The backup directory does not exist." >&2
  exit 1
}
for file in SHA256SUMS profile-data.tar.gz profile-server-deploy.tar.gz; do
  [[ -f "$backup/$file" ]] || { echo "Backup file '$file' is missing." >&2; exit 1; }
done
(cd "$backup" && sha256sum -c SHA256SUMS)
for file in data-protection.pfx data-protection.password ssh-vault-master-key.bin; do
  [[ -f "$secret_dir/$file" ]] || { echo "Recovery secret '$file' is missing." >&2; exit 1; }
done

stamp=$(date -u +%Y%m%dT%H%M%SZ)-$$
container="hypershell-profile-restore-$stamp"
volume="hypershell-profile-restore-$stamp"
evidence="$backup/restore-evidence-$stamp.json"
docker volume create "$volume" >/dev/null
volume_root=$(docker volume inspect -f '{{.Mountpoint}}' "$volume")
volume_root=$(readlink -f -- "$volume_root")
[[ "$volume_root" == /var/lib/docker/volumes/hypershell-profile-restore-*/_data ]] || {
  echo "The isolated restore volume resolved outside the approved Docker root." >&2
  exit 1
}

cleanup() {
  if [[ $keep -eq 0 ]]; then
    docker rm -f "$container" >/dev/null 2>&1 || true
    docker volume rm "$volume" >/dev/null 2>&1 || true
  fi
}
failure_logs() {
  local status=$?
  trap - ERR
  set +e
  docker logs --tail 80 "$container" >&2 2>/dev/null || true
  cleanup
  exit "$status"
}
trap failure_logs ERR
tar --xattrs --acls --numeric-owner -C "$volume_root" \
  -xzf "$backup/profile-data.tar.gz"

docker run -d \
  --name "$container" \
  --user 1654:1654 \
  --read-only \
  --tmpfs /tmp:size=64m,mode=1777 \
  --security-opt no-new-privileges:true \
  --cap-drop ALL \
  --memory 512m \
  --cpus 1.0 \
  --pids-limit 128 \
  --stop-timeout 30 \
  --publish "127.0.0.1:$port:8080" \
  --volume "$volume:/var/lib/hypershell-profile-server" \
  --mount "type=bind,src=$secret_dir/data-protection.pfx,dst=/run/secrets/hypershell_data_protection_certificate,readonly" \
  --mount "type=bind,src=$secret_dir/data-protection.password,dst=/run/secrets/hypershell_data_protection_password,readonly" \
  --mount "type=bind,src=$secret_dir/ssh-vault-master-key.bin,dst=/run/secrets/hypershell_ssh_vault_master_key,readonly" \
  --env ASPNETCORE_HTTP_PORTS=8080 \
  --env HYPERSHELL_PROFILE_SERVER_DATA=/var/lib/hypershell-profile-server \
  --env HYPERSHELL_PROFILE_SESSION_MINUTES=30 \
  --env HYPERSHELL_PROFILE_ACCOUNT_STORAGE_MIB=512 \
  --env HYPERSHELL_MINIMUM_FREE_STORAGE_MIB=512 \
  --env HYPERSHELL_PUBLIC_PORTAL_ORIGIN=https://admin.atlasuniversalis.com \
  --env HYPERSHELL_DATA_PROTECTION_CERTIFICATE=/run/secrets/hypershell_data_protection_certificate \
  --env HYPERSHELL_DATA_PROTECTION_CERTIFICATE_PASSWORD_FILE=/run/secrets/hypershell_data_protection_password \
  --env HYPERSHELL_SSH_VAULT_MASTER_KEY_FILE=/run/secrets/hypershell_ssh_vault_master_key \
  "$image" >/dev/null

for _ in $(seq 1 60); do
  if curl --fail --silent "http://127.0.0.1:$port/healthz" >/dev/null; then
    break
  fi
  sleep 1
done
curl --fail --silent --show-error "http://127.0.0.1:$port/healthz" >/dev/null
[[ "$(docker inspect -f '{{.RestartCount}}' "$container")" == 0 ]] || {
  echo "The restored container restarted during validation." >&2
  exit 1
}
created_at=$(date -u +%Y-%m-%dT%H:%M:%SZ)
cat > "$evidence" <<EOF
{
  "schemaVersion": 1,
  "validatedAtUtc": "$created_at",
  "backupDirectory": "$(basename "$backup")",
  "image": "$image",
  "health": "ok",
  "restartCount": 0
}
EOF
chmod 0600 "$evidence"

trap - ERR
cleanup
echo "HYPERSHELL_RESTORE_OK|backup:$backup|image:$image|evidence:$evidence|kept:$keep"
if [[ $keep -eq 1 ]]; then
  echo "HYPERSHELL_RESTORE_RESOURCES|container:$container|volume:$volume|port:$port"
fi
