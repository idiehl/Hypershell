#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat >&2 <<'EOF'
Usage: enroll-digitalocean-snapshot-token.sh [--token-file FILE] [--replace]

Securely enrolls the snapshot-only DigitalOcean token, discovers the attached
hypershell-data volume, validates it against this Droplet and the mounted stable
device, and atomically installs /etc/hypershell-profile-server/digitalocean.env.

Without --token-file, the token is read without echo from /dev/tty. A token
file is accepted only when it is a root-owned regular file with mode 0400 or
0600. The token is never printed or passed as a command-line argument.
EOF
  exit 2
}

token_file=""
replace=0
while (($#)); do
  case "$1" in
    --token-file) token_file="${2:-}"; shift 2 ;;
    --replace) replace=1; shift ;;
    *) usage ;;
  esac
done

[[ $EUID -eq 0 ]] || { echo "Run as root." >&2; exit 1; }
for command in \
  blockdev chmod chown curl findmnt install mktemp mv python3 readlink rm \
  rmdir stat; do
  command -v "$command" >/dev/null || {
    echo "Required command '$command' is unavailable." >&2
    exit 1
  }
done

script_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
verifier="$script_root/verify-digitalocean-volume.sh"
[[ -x "$verifier" ]] || {
  echo "The DigitalOcean volume verifier is missing or not executable." >&2
  exit 1
}

token=""
if [[ -n "$token_file" ]]; then
  token_file=$(readlink -f -- "$token_file")
  [[ -f "$token_file" && ! -L "$token_file" \
     && "$(stat -c '%U:%G' "$token_file")" == root:root ]] || {
    echo "The token file must be a root-owned regular, non-symlink file." >&2
    exit 1
  }
  token_mode=$(stat -c '%a' "$token_file")
  [[ "$token_mode" == 400 || "$token_mode" == 600 ]] || {
    echo "The token file must have mode 0400 or 0600." >&2
    exit 1
  }
  IFS= read -r token < "$token_file" || [[ -n "$token" ]]
else
  [[ -r /dev/tty && -w /dev/tty ]] || {
    echo "An interactive terminal is required when --token-file is omitted." >&2
    exit 1
  }
  printf 'DigitalOcean snapshot token: ' > /dev/tty
  IFS= read -r -s token < /dev/tty
  printf '\n' > /dev/tty
fi
[[ ${#token} -ge 32 && ${#token} -le 512 && "$token" != *[[:space:]]* ]] || {
  token=""
  echo "The DigitalOcean token has an invalid shape." >&2
  exit 1
}

volume_name=hypershell-data
stable_device="/dev/disk/by-id/scsi-0DO_Volume_$volume_name"
config_dir=/etc/hypershell-profile-server
config_file="$config_dir/digitalocean.env"
[[ -d "$config_dir" && ! -L "$config_dir" \
   && "$(readlink -f -- "$config_dir")" == "$config_dir" \
   && "$(stat -c '%U:%G' "$config_dir")" == root:root ]] || {
  token=""
  echo "The Hypershell configuration directory is missing or unsafe." >&2
  exit 1
}
if [[ -e "$config_file" || -L "$config_file" ]]; then
  [[ -f "$config_file" && ! -L "$config_file" \
     && "$(stat -c '%U:%G' "$config_file")" == root:root ]] || {
    token=""
    echo "The existing DigitalOcean configuration is unsafe." >&2
    exit 1
  }
  [[ $replace -eq 1 ]] || {
    token=""
    echo "The DigitalOcean configuration already exists; use --replace only for deliberate token rotation." >&2
    exit 1
  }
fi

droplet_id=$(curl --fail --silent --connect-timeout 3 \
  http://169.254.169.254/metadata/v1/id)
region=$(curl --fail --silent --connect-timeout 3 \
  http://169.254.169.254/metadata/v1/region)
[[ "$droplet_id" =~ ^[0-9]+$ && "$region" =~ ^[a-z0-9]+$ ]] || {
  token=""
  echo "DigitalOcean Droplet metadata is unavailable." >&2
  exit 1
}

temporary_dir=$(mktemp -d /run/hypershell-snapshot-token.XXXXXX)
stage=""
cleanup() {
  token=""
  if [[ -n "$stage" ]]; then rm -f -- "$stage"; fi
  rm -f -- "$temporary_dir/curl.conf" "$temporary_dir/volumes.json"
  rmdir -- "$temporary_dir" 2>/dev/null || true
}
trap cleanup EXIT
chmod 0700 "$temporary_dir"
cat > "$temporary_dir/curl.conf" <<EOF
silent
show-error
fail-with-body
header = "Authorization: Bearer $token"
header = "Content-Type: application/json"
EOF
chmod 0600 "$temporary_dir/curl.conf"
curl --config "$temporary_dir/curl.conf" \
  "https://api.digitalocean.com/v2/volumes?name=$volume_name&region=$region" \
  > "$temporary_dir/volumes.json"

volume_id=$(python3 - "$temporary_dir/volumes.json" \
  "$volume_name" "$region" "$droplet_id" <<'PY'
import json
import sys
import uuid

path, expected_name, expected_region, droplet_text = sys.argv[1:]
with open(path, encoding="utf-8") as stream:
    document = json.load(stream)
droplet_id = int(droplet_text)
matches = []
for volume in document.get("volumes", []):
    region = volume.get("region") or {}
    if (volume.get("name") == expected_name
            and volume.get("size_gigabytes") == 50
            and region.get("slug") == expected_region
            and droplet_id in (volume.get("droplet_ids") or [])):
        matches.append(volume)
if len(matches) != 1:
    raise SystemExit(
        "Expected exactly one attached 50 GiB Hypershell volume; found "
        + str(len(matches)))
identifier = str(matches[0].get("id", ""))
try:
    uuid.UUID(identifier)
except ValueError as error:
    raise SystemExit("DigitalOcean returned an invalid volume UUID.") from error
print(identifier)
PY
)

stage=$(mktemp "$config_dir/.digitalocean.env.XXXXXX")
chmod 0600 "$stage"
chown root:root "$stage"
{
  printf 'DIGITALOCEAN_TOKEN=%q\n' "$token"
  printf 'HYPERSHELL_VOLUME_ID=%q\n' "$volume_id"
  printf 'HYPERSHELL_VOLUME_DEVICE=%q\n' "$stable_device"
  printf 'HYPERSHELL_SNAPSHOT_RETENTION=8\n'
} > "$stage"
token=""
"$verifier" "$stage" >/dev/null
mv -f -- "$stage" "$config_file"
stage=""
chmod 0400 "$config_file"
chown root:root "$config_file"

echo "HYPERSHELL_SNAPSHOT_CREDENTIAL_READY|id:$volume_id|name:$volume_name|droplet:$droplet_id|region:$region|config:$config_file"
