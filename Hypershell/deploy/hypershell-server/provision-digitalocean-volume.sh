#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat >&2 <<'EOF'
Usage: provision-digitalocean-volume.sh --token-file FILE [--name hypershell-data]
       [--confirm-create-billed-volume] [--confirm-attach-to-this-droplet]

Creates or discovers one exact-name, raw 50 GiB DigitalOcean block volume in
the current Droplet's region and attaches it to this Droplet. Creating a volume
incurs a recurring charge and therefore requires the explicit create flag.
Attaching an existing or newly created volume requires the separate attach flag.

This script never formats, mounts, detaches, resizes, or deletes a volume. After
it succeeds, run prepare-storage.sh without --format-empty-device first.
EOF
  exit 2
}

token_file=""
volume_name="hypershell-data"
allow_create=0
allow_attach=0
while (($#)); do
  case "$1" in
    --token-file) token_file="${2:-}"; shift 2 ;;
    --name) volume_name="${2:-}"; shift 2 ;;
    --confirm-create-billed-volume) allow_create=1; shift ;;
    --confirm-attach-to-this-droplet) allow_attach=1; shift ;;
    *) usage ;;
  esac
done

[[ $EUID -eq 0 ]] || { echo "Run as root." >&2; exit 1; }
[[ -n "$token_file" ]] || usage
[[ "$volume_name" =~ ^[a-z][a-z0-9-]{0,62}[a-z0-9]$ ]] || {
  echo "The volume name must be 2-64 lowercase letters, digits, or hyphens." >&2
  exit 1
}

for command in blockdev cat chmod curl date findmnt head install lsblk mktemp \
  python3 readlink rm rmdir seq sleep stat wc; do
  command -v "$command" >/dev/null || {
    echo "$command is required." >&2
    exit 1
  }
done

[[ ! -L "$token_file" && -f "$token_file" ]] || {
  echo "The DigitalOcean token file must be a regular, non-symbolic-link file." >&2
  exit 1
}
token_file=$(readlink -f -- "$token_file")
[[ "$(stat -c '%u:%g' "$token_file")" == 0:0 ]] || {
  echo "The DigitalOcean token file must be owned by root:root." >&2
  exit 1
}
token_mode=$(stat -c '%a' "$token_file")
[[ "$token_mode" == 400 || "$token_mode" == 600 ]] || {
  echo "The DigitalOcean token file must have mode 0400 or 0600." >&2
  exit 1
}
token_size=$(stat -c '%s' "$token_file")
[[ "$token_size" -ge 40 && "$token_size" -le 512 ]] || {
  echo "The DigitalOcean token file has an invalid size." >&2
  exit 1
}
digitalocean_token=$(<"$token_file")
[[ "$digitalocean_token" =~ ^dop_v1_[A-Za-z0-9_-]{32,}$ ]] || {
  echo "The DigitalOcean token file does not contain a scoped API token." >&2
  exit 1
}

droplet_id=$(curl --fail --silent --show-error --connect-timeout 3 \
  http://169.254.169.254/metadata/v1/id)
region=$(curl --fail --silent --show-error --connect-timeout 3 \
  http://169.254.169.254/metadata/v1/region)
[[ "$droplet_id" =~ ^[0-9]+$ && "$region" =~ ^[a-z0-9]+$ ]] || {
  echo "DigitalOcean Droplet metadata is unavailable." >&2
  exit 1
}

temporary_dir=$(mktemp -d)
cleanup() {
  rm -f -- "$temporary_dir/curl.conf" \
    "$temporary_dir/list.json" \
    "$temporary_dir/create-request.json" \
    "$temporary_dir/create-response.json" \
    "$temporary_dir/attach-request.json" \
    "$temporary_dir/attach-response.json" \
    "$temporary_dir/volume.json" \
    "$temporary_dir/evidence.env"
  rmdir -- "$temporary_dir"
}
trap cleanup EXIT
chmod 0700 "$temporary_dir"
cat > "$temporary_dir/curl.conf" <<EOF
silent
show-error
fail-with-body
connect-timeout = 10
max-time = 30
header = "Authorization: Bearer $digitalocean_token"
header = "Content-Type: application/json"
EOF
chmod 0600 "$temporary_dir/curl.conf"
unset digitalocean_token

api_get() {
  local url=$1
  local output=$2
  curl --config "$temporary_dir/curl.conf" --request GET "$url" > "$output"
}

api_post() {
  local url=$1
  local request=$2
  local output=$3
  curl --config "$temporary_dir/curl.conf" --request POST \
    --data-binary "@$request" "$url" > "$output"
}

list_url="https://api.digitalocean.com/v2/volumes?name=$volume_name&region=$region"
api_get "$list_url" "$temporary_dir/list.json"

volume_id=$(python3 - "$temporary_dir/list.json" "$volume_name" "$region" <<'PY'
import json
import sys

path, expected_name, expected_region = sys.argv[1:]
with open(path, encoding="utf-8") as stream:
    volumes = json.load(stream).get("volumes") or []
matches = [volume for volume in volumes
           if str(volume.get("name", "")) == expected_name]
if len(matches) > 1:
    raise SystemExit("DigitalOcean returned multiple exact-name volumes.")
if not matches:
    print("")
    raise SystemExit(0)
volume = matches[0]
region = volume.get("region") or {}
if str(region.get("slug", "")) != expected_region:
    raise SystemExit("The existing volume is in an unexpected region.")
print(str(volume.get("id", "")))
PY
)

created=0
if [[ -z "$volume_id" ]]; then
  [[ $allow_create -eq 1 ]] || {
    echo "No '$volume_name' volume exists. Re-run with --confirm-create-billed-volume to authorize the recurring 50 GiB charge." >&2
    exit 1
  }
  python3 - "$temporary_dir/create-request.json" "$volume_name" "$region" <<'PY'
import json
import sys

path, name, region = sys.argv[1:]
with open(path, "w", encoding="utf-8", newline="\n") as stream:
    json.dump({
        "size_gigabytes": 50,
        "name": name,
        "region": region,
        "description": "Dedicated Hypershell application storage",
        "tags": ["hypershell"],
    }, stream, separators=(",", ":"))
PY
  api_post "https://api.digitalocean.com/v2/volumes" \
    "$temporary_dir/create-request.json" \
    "$temporary_dir/create-response.json"
  volume_id=$(python3 - "$temporary_dir/create-response.json" <<'PY'
import json
import sys

with open(sys.argv[1], encoding="utf-8") as stream:
    print(str((json.load(stream).get("volume") or {}).get("id", "")))
PY
  )
  created=1
fi

[[ "$volume_id" =~ ^[0-9a-fA-F-]{36}$ ]] || {
  echo "DigitalOcean returned an invalid volume UUID." >&2
  exit 1
}
api_get "https://api.digitalocean.com/v2/volumes/$volume_id" \
  "$temporary_dir/volume.json"

attachment_state=$(python3 - "$temporary_dir/volume.json" \
  "$volume_id" "$volume_name" "$region" "$droplet_id" <<'PY'
import json
import sys

path, expected_id, expected_name, expected_region, droplet_text = sys.argv[1:]
with open(path, encoding="utf-8") as stream:
    volume = (json.load(stream).get("volume") or {})
region = volume.get("region") or {}
checks = {
    "id": str(volume.get("id", "")) == expected_id,
    "name": str(volume.get("name", "")) == expected_name,
    "size": volume.get("size_gigabytes") == 50,
    "region": str(region.get("slug", "")) == expected_region,
    "raw": volume.get("filesystem_type") in (None, "")
           and volume.get("filesystem_label") in (None, ""),
}
failed = [name for name, passed in checks.items() if not passed]
if failed:
    raise SystemExit("DigitalOcean volume identity failed: " + ", ".join(failed))
droplet_id = int(droplet_text)
attachments = [int(value) for value in (volume.get("droplet_ids") or [])]
unexpected = [value for value in attachments if value != droplet_id]
if unexpected:
    raise SystemExit("The volume is attached to a different Droplet.")
print("attached" if droplet_id in attachments else "detached")
PY
)

attached=0
if [[ "$attachment_state" == detached ]]; then
  [[ $allow_attach -eq 1 ]] || {
    echo "Volume '$volume_name' is not attached. Re-run with --confirm-attach-to-this-droplet to authorize attachment to Droplet $droplet_id." >&2
    exit 1
  }
  python3 - "$temporary_dir/attach-request.json" \
    "$droplet_id" "$region" <<'PY'
import json
import sys

path, droplet_id, region = sys.argv[1:]
with open(path, "w", encoding="utf-8", newline="\n") as stream:
    json.dump({
        "type": "attach",
        "droplet_id": int(droplet_id),
        "region": region,
    }, stream, separators=(",", ":"))
PY
  api_post "https://api.digitalocean.com/v2/volumes/$volume_id/actions" \
    "$temporary_dir/attach-request.json" \
    "$temporary_dir/attach-response.json"
  attached=1
fi

for _ in $(seq 1 30); do
  api_get "https://api.digitalocean.com/v2/volumes/$volume_id" \
    "$temporary_dir/volume.json"
  if python3 - "$temporary_dir/volume.json" "$droplet_id" <<'PY'
import json
import sys

with open(sys.argv[1], encoding="utf-8") as stream:
    volume = (json.load(stream).get("volume") or {})
raise SystemExit(0 if int(sys.argv[2]) in (volume.get("droplet_ids") or []) else 1)
PY
  then
    break
  fi
  sleep 2
done
python3 - "$temporary_dir/volume.json" "$droplet_id" <<'PY'
import json
import sys

with open(sys.argv[1], encoding="utf-8") as stream:
    volume = (json.load(stream).get("volume") or {})
if int(sys.argv[2]) not in (volume.get("droplet_ids") or []):
    raise SystemExit("DigitalOcean did not report the volume attachment in time.")
PY

stable_device="/dev/disk/by-id/scsi-0DO_Volume_$volume_name"
if command -v udevadm >/dev/null; then
  udevadm trigger --subsystem-match=block >/dev/null 2>&1 || true
  udevadm settle --timeout=30 >/dev/null 2>&1 || true
fi
for _ in $(seq 1 30); do
  [[ -b "$stable_device" ]] && break
  sleep 2
done
[[ -b "$stable_device" ]] || {
  echo "The attached volume did not appear at its stable device path: $stable_device" >&2
  exit 1
}

resolved_device=$(readlink -f -- "$stable_device")
root_source=$(readlink -f -- "$(findmnt -nro SOURCE /)")
root_parent=$(lsblk -ndo PKNAME "$root_source" | head -n 1)
root_device=$root_source
if [[ -n "$root_parent" ]]; then
  root_device=$(readlink -f -- "/dev/$root_parent")
fi
[[ "$resolved_device" != "$root_source" && "$resolved_device" != "$root_device" ]] || {
  echo "Refusing to accept the root disk as Hypershell storage." >&2
  exit 1
}
[[ "$(lsblk -ndo TYPE "$resolved_device")" == disk ]] || {
  echo "The stable volume path does not resolve to a whole disk." >&2
  exit 1
}
[[ "$(lsblk -nrpo NAME "$resolved_device" | wc -l)" -eq 1 ]] || {
  echo "The selected volume has child partitions; refusing it." >&2
  exit 1
}
size_bytes=$(blockdev --getsize64 "$resolved_device")
minimum_bytes=$((45 * 1024 * 1024 * 1024))
maximum_bytes=$((55 * 1024 * 1024 * 1024))
[[ "$size_bytes" -ge "$minimum_bytes" && "$size_bytes" -le "$maximum_bytes" ]] || {
  echo "The attached block device is not the approved 50 GiB size." >&2
  exit 1
}

evidence_root=/var/lib/hypershell-deployments
install -d -m 0700 -o root -g root "$evidence_root"
stamp=$(date -u +%Y%m%dT%H%M%S%NZ)
evidence_path="$evidence_root/volume-provision-$stamp.env"
cat > "$temporary_dir/evidence.env" <<EOF
HYPERSHELL_VOLUME_PROVISIONED_AT_UTC=$stamp
HYPERSHELL_VOLUME_ID=$volume_id
HYPERSHELL_VOLUME_NAME=$volume_name
HYPERSHELL_VOLUME_REGION=$region
HYPERSHELL_VOLUME_DROPLET_ID=$droplet_id
HYPERSHELL_VOLUME_STABLE_DEVICE=$stable_device
HYPERSHELL_VOLUME_RESOLVED_DEVICE=$resolved_device
HYPERSHELL_VOLUME_SIZE_BYTES=$size_bytes
HYPERSHELL_VOLUME_CREATED_THIS_RUN=$created
HYPERSHELL_VOLUME_ATTACHED_THIS_RUN=$attached
EOF
install -m 0600 -o root -g root "$temporary_dir/evidence.env" "$evidence_path"

echo "HYPERSHELL_VOLUME_PROVISIONED|id:$volume_id|name:$volume_name|droplet:$droplet_id|region:$region|device:$stable_device|evidence:$evidence_path"
echo "Next: run prepare-storage.sh --device '$stable_device' without --format-empty-device and confirm that it refuses the empty device. Only then re-run it with --format-empty-device."
