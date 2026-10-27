#!/usr/bin/env bash
set -euo pipefail

usage() {
  echo "Usage: verify-digitalocean-volume.sh [CONFIG_FILE]" >&2
  exit 2
}

config_file="${1:-/etc/hypershell-profile-server/digitalocean.env}"
mount_point="/srv/hypershell"
[[ $# -le 1 ]] || usage
[[ $EUID -eq 0 ]] || { echo "Run as root." >&2; exit 1; }
command -v curl >/dev/null || { echo "curl is required." >&2; exit 1; }
command -v python3 >/dev/null || { echo "python3 is required." >&2; exit 1; }

config_file=$(readlink -f -- "$config_file")
[[ -f "$config_file" ]] || {
  echo "DigitalOcean volume configuration is missing." >&2
  exit 1
}
[[ "$(stat -c '%U:%G' "$config_file")" == root:root ]] || {
  echo "The DigitalOcean configuration must be owned by root:root." >&2
  exit 1
}
mode=$(stat -c '%a' "$config_file")
[[ "$mode" == 400 || "$mode" == 600 ]] || {
  echo "The DigitalOcean configuration must have mode 0400 or 0600." >&2
  exit 1
}
# shellcheck disable=SC1090
source "$config_file"
: "${DIGITALOCEAN_TOKEN:?DIGITALOCEAN_TOKEN is required}"
: "${HYPERSHELL_VOLUME_ID:?HYPERSHELL_VOLUME_ID is required}"
: "${HYPERSHELL_VOLUME_DEVICE:?HYPERSHELL_VOLUME_DEVICE is required}"
[[ "$HYPERSHELL_VOLUME_ID" =~ ^[0-9a-fA-F-]{36}$ ]] || {
  echo "HYPERSHELL_VOLUME_ID must be a volume UUID." >&2
  exit 1
}
[[ "$HYPERSHELL_VOLUME_DEVICE" == /dev/disk/by-id/scsi-0DO_Volume_* &&
   -b "$HYPERSHELL_VOLUME_DEVICE" ]] || {
  echo "HYPERSHELL_VOLUME_DEVICE must be the attached stable DigitalOcean device path." >&2
  exit 1
}

device=$(readlink -f -- "$HYPERSHELL_VOLUME_DEVICE")
mounted_source=$(findmnt -nro SOURCE -M "$mount_point")
mounted_source=$(readlink -f -- "$mounted_source")
[[ "$mounted_source" == "$device" ]] || {
  echo "The configured DigitalOcean device is not mounted at $mount_point." >&2
  exit 1
}
size_bytes=$(blockdev --getsize64 "$device")
minimum_bytes=$((45 * 1024 * 1024 * 1024))
maximum_bytes=$((55 * 1024 * 1024 * 1024))
[[ "$size_bytes" -ge "$minimum_bytes" && "$size_bytes" -le "$maximum_bytes" ]] || {
  echo "The mounted block device is not the approved 50 GB size." >&2
  exit 1
}

droplet_id=$(curl --fail --silent --connect-timeout 3 \
  http://169.254.169.254/metadata/v1/id)
region=$(curl --fail --silent --connect-timeout 3 \
  http://169.254.169.254/metadata/v1/region)
[[ "$droplet_id" =~ ^[0-9]+$ && "$region" =~ ^[a-z0-9]+$ ]] || {
  echo "DigitalOcean Droplet metadata is unavailable." >&2
  exit 1
}

temporary_dir=$(mktemp -d)
cleanup() {
  rm -f -- "$temporary_dir/curl.conf" "$temporary_dir/volume.json"
  rmdir -- "$temporary_dir"
}
trap cleanup EXIT
chmod 0700 "$temporary_dir"
cat > "$temporary_dir/curl.conf" <<EOF
silent
show-error
fail-with-body
header = "Authorization: Bearer $DIGITALOCEAN_TOKEN"
header = "Content-Type: application/json"
EOF
chmod 0600 "$temporary_dir/curl.conf"
curl --config "$temporary_dir/curl.conf" \
  "https://api.digitalocean.com/v2/volumes/$HYPERSHELL_VOLUME_ID" \
  > "$temporary_dir/volume.json"

volume_name=$(python3 - "$temporary_dir/volume.json" \
  "$HYPERSHELL_VOLUME_ID" "$droplet_id" "$region" <<'PY'
import json
import sys

path, expected_id, droplet_text, expected_region = sys.argv[1:]
with open(path, encoding="utf-8") as stream:
    volume = (json.load(stream).get("volume") or {})
region = volume.get("region") or {}
checks = {
    "id": str(volume.get("id", "")) == expected_id,
    "size": volume.get("size_gigabytes") == 50,
    "region": str(region.get("slug", "")) == expected_region,
    "attachment": int(droplet_text) in (volume.get("droplet_ids") or []),
}
failed = [name for name, passed in checks.items() if not passed]
if failed:
    raise SystemExit(
        "DigitalOcean volume identity failed: " + ", ".join(failed))
name = str(volume.get("name", ""))
if (not name or not name[0].islower()
        or not all(character.islower() or character.isdigit() or character == "-"
                   for character in name)):
    raise SystemExit("DigitalOcean returned an invalid volume name.")
print(name)
PY
)
[[ "$(basename -- "$HYPERSHELL_VOLUME_DEVICE")" == "scsi-0DO_Volume_$volume_name" ]] || {
  echo "The API volume name does not match the mounted stable device identity." >&2
  exit 1
}

echo "HYPERSHELL_VOLUME_VERIFIED|id:$HYPERSHELL_VOLUME_ID|name:$volume_name|droplet:$droplet_id|region:$region|mount:$mount_point"
