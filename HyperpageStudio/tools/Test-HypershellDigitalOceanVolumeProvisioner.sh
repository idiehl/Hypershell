#!/usr/bin/env bash
set -euo pipefail
shopt -s nullglob

[[ "${HYPERSHELL_VOLUME_FIXTURE_CONTAINER:-}" == 1 && -f /.dockerenv ]] || {
  echo "This destructive-safety fixture may run only inside its disposable container." >&2
  exit 1
}

provisioner=/repo/deploy/hypershell-server/provision-digitalocean-volume.sh
[[ -f "$provisioner" ]] || {
  echo "The provisioner was not mounted at $provisioner." >&2
  exit 1
}

fixture_root=$(mktemp -d)
mock_bin="$fixture_root/bin"
mock_state="$fixture_root/state"
token_file="$fixture_root/provision.token"
stable_device=/dev/disk/by-id/scsi-0DO_Volume_hypershell-data
mock_device=/dev/hypershell-fixture-volume
evidence_root=/var/lib/hypershell-deployments

cleanup() {
  local evidence_files=("$evidence_root"/volume-provision-*.env)
  rm -f -- "$stable_device" "$mock_device"
  ((${#evidence_files[@]} == 0)) || rm -f -- "${evidence_files[@]}"
  rm -rf --one-file-system -- "$fixture_root"
}
trap cleanup EXIT

install -d -m 0700 "$mock_bin" "$mock_state"
install -d -m 0755 /dev/disk/by-id
printf 'dop_v1_%064d\n' 0 > "$token_file"
chmod 0600 "$token_file"
mknod "$mock_device" b 240 0
ln -s "$mock_device" "$stable_device"

cat > "$mock_bin/curl" <<'MOCK'
#!/usr/bin/env bash
set -euo pipefail

state=${HYPERSHELL_VOLUME_FIXTURE_STATE:?}
method=GET
data_file=""
url=""
arguments=("$@")
for ((index=0; index<${#arguments[@]}; index++)); do
  case "${arguments[$index]}" in
    --request)
      method=${arguments[$((index + 1))]}
      index=$((index + 1))
      ;;
    --data-binary)
      data_file=${arguments[$((index + 1))]#@}
      index=$((index + 1))
      ;;
    http://*|https://*) url=${arguments[$index]} ;;
  esac
done

case "$url" in
  http://169.254.169.254/metadata/v1/id)
    printf '424242\n'
    exit 0
    ;;
  http://169.254.169.254/metadata/v1/region)
    printf 'nyc3\n'
    exit 0
    ;;
esac

printf '%s %s\n' "$method" "$url" >> "$state/curl.log"
volume_object() {
  local attachments='[]'
  [[ -f "$state/attached" ]] && attachments='[424242]'
  printf '{"id":"11111111-2222-3333-4444-555555555555","name":"hypershell-data","size_gigabytes":50,"region":{"slug":"nyc3"},"filesystem_type":null,"filesystem_label":null,"droplet_ids":%s}' "$attachments"
}
volume_json() {
  printf '{"volume":%s}\n' "$(volume_object)"
}

if [[ "$method" == GET && "$url" == *'/v2/volumes?name='* ]]; then
  if [[ -f "$state/exists" ]]; then
    printf '{"volumes":[%s]}\n' "$(volume_object)"
  else
    printf '{"volumes":[]}\n'
  fi
elif [[ "$method" == POST && "$url" == 'https://api.digitalocean.com/v2/volumes' ]]; then
  [[ -n "$data_file" && -f "$data_file" ]]
  cp "$data_file" "$state/create-request.json"
  python3 - "$data_file" <<'PY'
import json
import sys

with open(sys.argv[1], encoding="utf-8") as stream:
    request = json.load(stream)
assert request == {
    "size_gigabytes": 50,
    "name": "hypershell-data",
    "region": "nyc3",
    "description": "Dedicated Hypershell application storage",
    "tags": ["hypershell"],
}
assert "filesystem_type" not in request
PY
  touch "$state/exists"
  volume_json
elif [[ "$method" == POST && "$url" == */actions ]]; then
  [[ -n "$data_file" && -f "$data_file" ]]
  cp "$data_file" "$state/attach-request.json"
  python3 - "$data_file" <<'PY'
import json
import sys

with open(sys.argv[1], encoding="utf-8") as stream:
    request = json.load(stream)
assert request == {"type": "attach", "droplet_id": 424242, "region": "nyc3"}
PY
  touch "$state/attached"
  printf '{"action":{"id":991,"status":"in-progress","type":"attach"}}\n'
elif [[ "$method" == GET && "$url" == */v2/volumes/11111111-2222-3333-4444-555555555555 ]]; then
  [[ -f "$state/exists" ]]
  volume_json
else
  echo "Unexpected fixture request: $method $url" >&2
  exit 91
fi
MOCK

cat > "$mock_bin/findmnt" <<'MOCK'
#!/usr/bin/env bash
set -euo pipefail
[[ "$*" == '-nro SOURCE /' ]] || exit 92
printf '/dev/vda1\n'
MOCK

cat > "$mock_bin/lsblk" <<'MOCK'
#!/usr/bin/env bash
set -euo pipefail
case "$1" in
  -ndo)
    case "$2" in
      PKNAME) [[ "$3" == /dev/vda1 ]]; printf 'vda\n' ;;
      TYPE) [[ "$3" == /dev/hypershell-fixture-volume ]]; printf 'disk\n' ;;
      *) exit 93 ;;
    esac
    ;;
  -nrpo)
    [[ "$2" == NAME && "$3" == /dev/hypershell-fixture-volume ]]
    printf '/dev/hypershell-fixture-volume\n'
    ;;
  *) exit 94 ;;
esac
MOCK

cat > "$mock_bin/blockdev" <<'MOCK'
#!/usr/bin/env bash
set -euo pipefail
[[ "$1" == --getsize64 && "$2" == /dev/hypershell-fixture-volume ]]
printf '53687091200\n'
MOCK

cat > "$mock_bin/udevadm" <<'MOCK'
#!/usr/bin/env bash
exit 0
MOCK

chmod 0755 "$mock_bin"/*
export HYPERSHELL_VOLUME_FIXTURE_STATE="$mock_state"
export PATH="$mock_bin:$PATH"

chmod 0644 "$token_file"
set +e
unsafe_token_output=$("$provisioner" --token-file "$token_file" 2>&1)
unsafe_token_status=$?
set -e
[[ $unsafe_token_status -ne 0 ]]
[[ "$unsafe_token_output" == *'mode 0400 or 0600'* ]]
[[ ! -f "$mock_state/curl.log" ]]
chmod 0600 "$token_file"

set +e
unconfirmed_output=$("$provisioner" --token-file "$token_file" 2>&1)
unconfirmed_status=$?
set -e
[[ $unconfirmed_status -ne 0 ]]
[[ "$unconfirmed_output" == *'--confirm-create-billed-volume'* ]]
[[ ! -f "$mock_state/exists" ]]
[[ "$unconfirmed_output" != *'dop_v1_'* ]]

touch "$mock_state/exists"
set +e
unattached_output=$("$provisioner" --token-file "$token_file" 2>&1)
unattached_status=$?
set -e
[[ $unattached_status -ne 0 ]]
[[ "$unattached_output" == *'--confirm-attach-to-this-droplet'* ]]
[[ ! -f "$mock_state/attached" ]]
[[ "$unattached_output" != *'dop_v1_'* ]]
rm -f "$mock_state/exists"

confirmed_output=$("$provisioner" \
  --token-file "$token_file" \
  --confirm-create-billed-volume \
  --confirm-attach-to-this-droplet 2>&1)
[[ "$confirmed_output" == *'HYPERSHELL_VOLUME_PROVISIONED|'* ]]
[[ "$confirmed_output" == *'without --format-empty-device'* ]]
[[ "$confirmed_output" != *'dop_v1_'* ]]
[[ -f "$mock_state/create-request.json" && -f "$mock_state/attach-request.json" ]]
[[ $(grep -c '^POST https://api.digitalocean.com/v2/volumes$' "$mock_state/curl.log") -eq 1 ]]
[[ $(grep -c '/actions$' "$mock_state/curl.log") -eq 1 ]]

evidence_files=("$evidence_root"/volume-provision-*.env)
[[ ${#evidence_files[@]} -eq 1 && -f "${evidence_files[0]}" ]]
[[ "$(stat -c '%u:%g' "${evidence_files[0]}")" == 0:0 ]]
[[ "$(stat -c '%a' "${evidence_files[0]}")" == 600 ]]
grep -q '^HYPERSHELL_VOLUME_ID=11111111-2222-3333-4444-555555555555$' \
  "${evidence_files[0]}"
if grep -q 'dop_v1_' "${evidence_files[0]}"; then
  echo "The fixture detected a token leak in provisioning evidence." >&2
  exit 1
fi

create_count=$(grep -c '^POST https://api.digitalocean.com/v2/volumes$' \
  "$mock_state/curl.log")
attach_count=$(grep -c '/actions$' "$mock_state/curl.log")
idempotent_output=$("$provisioner" --token-file "$token_file" 2>&1)
[[ "$idempotent_output" == *'HYPERSHELL_VOLUME_PROVISIONED|'* ]]
[[ $(grep -c '^POST https://api.digitalocean.com/v2/volumes$' "$mock_state/curl.log") -eq $create_count ]]
[[ $(grep -c '/actions$' "$mock_state/curl.log") -eq $attach_count ]]

if grep -R -q 'dop_v1_' "$mock_state" /var/lib/hypershell-deployments; then
  echo "The fixture detected a token leak in logs or evidence." >&2
  exit 1
fi
if grep -q ' DELETE ' "$mock_state/curl.log"; then
  echo "The fixture detected an unexpected delete request." >&2
  exit 1
fi

echo 'HYPERSHELL_DIGITALOCEAN_VOLUME_PROVISIONER_OK|token-file-policy|billing-confirmation|attach-confirmation|raw-volume|stable-device|idempotent|token-redaction'
