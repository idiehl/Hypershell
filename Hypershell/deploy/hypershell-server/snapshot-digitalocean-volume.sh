#!/usr/bin/env bash
set -euo pipefail

config_file="${1:-/etc/hypershell-profile-server/digitalocean.env}"
[[ $EUID -eq 0 ]] || { echo "Run as root." >&2; exit 1; }
config_file=$(readlink -f -- "$config_file")
[[ -f "$config_file" ]] || { echo "DigitalOcean snapshot configuration is missing." >&2; exit 1; }
mode=$(stat -c '%a' "$config_file")
[[ "$mode" == 400 || "$mode" == 600 ]] || {
  echo "The DigitalOcean configuration must have mode 0400 or 0600." >&2
  exit 1
}
script_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
[[ -x "$script_root/verify-digitalocean-volume.sh" ]] || {
  echo "The DigitalOcean volume verifier is missing." >&2
  exit 1
}
"$script_root/verify-digitalocean-volume.sh" "$config_file" >/dev/null
# shellcheck disable=SC1090
source "$config_file"
: "${DIGITALOCEAN_TOKEN:?DIGITALOCEAN_TOKEN is required}"
: "${HYPERSHELL_VOLUME_ID:?HYPERSHELL_VOLUME_ID is required}"
retain="${HYPERSHELL_SNAPSHOT_RETENTION:-8}"
[[ "$retain" =~ ^[0-9]+$ && "$retain" -ge 3 && "$retain" -le 52 ]] || {
  echo "HYPERSHELL_SNAPSHOT_RETENTION must be from 3 through 52." >&2
  exit 1
}
[[ "$HYPERSHELL_VOLUME_ID" =~ ^[0-9a-fA-F-]{36}$ ]] || {
  echo "HYPERSHELL_VOLUME_ID must be a volume UUID." >&2
  exit 1
}
command -v curl >/dev/null || { echo "curl is required." >&2; exit 1; }
command -v python3 >/dev/null || { echo "python3 is required." >&2; exit 1; }
command -v sync >/dev/null || { echo "sync is required." >&2; exit 1; }
sync -f /srv/hypershell

temporary_dir=$(mktemp -d)
cleanup() {
  rm -f -- "$temporary_dir/curl.conf" "$temporary_dir/create.json" "$temporary_dir/list.json"
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

stamp=$(date -u +%Y%m%dT%H%M%SZ)
name="hypershell-volume-$stamp"
curl --config "$temporary_dir/curl.conf" \
  --request POST \
  --data "{\"name\":\"$name\"}" \
  "https://api.digitalocean.com/v2/volumes/$HYPERSHELL_VOLUME_ID/snapshots" \
  > "$temporary_dir/create.json"
snapshot_id=$(python3 - "$temporary_dir/create.json" <<'PY'
import json, sys
with open(sys.argv[1], encoding="utf-8") as stream:
    document = json.load(stream)
snapshot = document.get("snapshot") or {}
identifier = snapshot.get("id") or ""
if not identifier:
    raise SystemExit("DigitalOcean returned no snapshot ID.")
print(identifier)
PY
)

curl --config "$temporary_dir/curl.conf" \
  "https://api.digitalocean.com/v2/volumes/$HYPERSHELL_VOLUME_ID/snapshots?per_page=200" \
  > "$temporary_dir/list.json"
mapfile -t stale_ids < <(python3 - "$temporary_dir/list.json" "$HYPERSHELL_VOLUME_ID" "$retain" <<'PY'
import json, sys
path, volume_id, retain_text = sys.argv[1:]
retain = int(retain_text)
with open(path, encoding="utf-8") as stream:
    document = json.load(stream)
items = [item for item in document.get("snapshots", [])
         if item.get("resource_type") == "volume"
         and item.get("resource_id") == volume_id
         and str(item.get("name", "")).startswith("hypershell-volume-")]
items.sort(key=lambda item: item.get("created_at", ""), reverse=True)
for item in items[retain:]:
    identifier = str(item.get("id", ""))
    if identifier:
        print(identifier)
PY
)
for stale_id in "${stale_ids[@]}"; do
  [[ "$stale_id" =~ ^[0-9a-fA-F-]{1,64}$ ]] || {
    echo "Refusing an unexpected snapshot ID returned by the API." >&2
    exit 1
  }
  curl --config "$temporary_dir/curl.conf" \
    --request DELETE \
    "https://api.digitalocean.com/v2/volumes/snapshots/$stale_id" \
    >/dev/null
done

echo "HYPERSHELL_VOLUME_SNAPSHOT_OK|volume:$HYPERSHELL_VOLUME_ID|snapshot:$snapshot_id|name:$name|pruned:${#stale_ids[@]}"
