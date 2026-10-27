#!/usr/bin/env bash
set -euo pipefail

[[ "${HYPERSHELL_SNAPSHOT_ENROLLMENT_FIXTURE_CONTAINER:-}" == 1 \
   && -f /.dockerenv ]] || {
  echo "This snapshot-enrollment fixture may run only inside its disposable container." >&2
  exit 1
}

enroller=/repo/deploy/hypershell-server/enroll-digitalocean-snapshot-token.sh
[[ -x "$enroller" ]] || {
  echo "The snapshot-token enroller was not mounted executable at $enroller." >&2
  exit 1
}

fixture_root=$(mktemp -d)
mock_bin="$fixture_root/bin"
mock_state="$fixture_root/state"
token_file="$fixture_root/token"
config_dir=/etc/hypershell-profile-server
config_file="$config_dir/digitalocean.env"
stable_device=/dev/disk/by-id/scsi-0DO_Volume_hypershell-data
mock_device=/dev/hypershell-snapshot-fixture

cleanup() {
  rm -f -- "$stable_device" "$mock_device"
  rm -rf --one-file-system -- "$config_dir" "$fixture_root"
}
trap cleanup EXIT

install -d -m 0700 "$mock_bin" "$mock_state" "$config_dir"
install -d -m 0755 /dev/disk/by-id /srv
install -d -m 0750 /srv/hypershell
mknod "$mock_device" b 240 2
ln -s "$mock_device" "$stable_device"
printf 'dop_v1_fixture_token_value_1234567890\n' > "$token_file"
chmod 0600 "$token_file"

cat > "$mock_bin/curl" <<'MOCK'
#!/usr/bin/env bash
set -euo pipefail
state=${HYPERSHELL_SNAPSHOT_ENROLLMENT_STATE:?}
url=${!#}
case "$url" in
  http://169.254.169.254/metadata/v1/id)
    printf '515885116\n'
    ;;
  http://169.254.169.254/metadata/v1/region)
    printf 'nyc3\n'
    ;;
  *'/v2/volumes?name=hypershell-data&region=nyc3')
    if [[ -f "$state/no-match" ]]; then
      printf '{"volumes":[]}'
    else
      printf '{"volumes":[{"id":"11111111-2222-3333-4444-555555555555","name":"hypershell-data","size_gigabytes":50,"region":{"slug":"nyc3"},"droplet_ids":[515885116]}]}'
    fi
    ;;
  *'/v2/volumes/11111111-2222-3333-4444-555555555555')
    printf '{"volume":{"id":"11111111-2222-3333-4444-555555555555","name":"hypershell-data","size_gigabytes":50,"region":{"slug":"nyc3"},"droplet_ids":[515885116]}}'
    ;;
  *)
    echo "Unexpected curl fixture URL: $url" >&2
    exit 81
    ;;
esac
MOCK

cat > "$mock_bin/findmnt" <<'MOCK'
#!/usr/bin/env bash
set -euo pipefail
[[ "$*" == '-nro SOURCE -M /srv/hypershell' ]]
printf '/dev/hypershell-snapshot-fixture\n'
MOCK

cat > "$mock_bin/blockdev" <<'MOCK'
#!/usr/bin/env bash
set -euo pipefail
[[ "$1" == --getsize64 && "$2" == /dev/hypershell-snapshot-fixture ]]
printf '53687091200\n'
MOCK

chmod 0755 "$mock_bin"/*
export HYPERSHELL_SNAPSHOT_ENROLLMENT_STATE="$mock_state"
export PATH="$mock_bin:$PATH"

success_output=$("$enroller" --token-file "$token_file")
[[ "$success_output" == *'HYPERSHELL_SNAPSHOT_CREDENTIAL_READY|'* ]]
[[ "$success_output" != *'dop_v1_'* ]]
[[ "$(stat -c '%a %U:%G' "$config_file")" == '400 root:root' ]]
grep -Fq 'HYPERSHELL_VOLUME_ID=11111111-2222-3333-4444-555555555555' "$config_file"
grep -Fq 'HYPERSHELL_VOLUME_DEVICE=/dev/disk/by-id/scsi-0DO_Volume_hypershell-data' "$config_file"
grep -Fq 'HYPERSHELL_SNAPSHOT_RETENTION=8' "$config_file"

before_hash=$(sha256sum "$config_file")
set +e
duplicate_output=$("$enroller" --token-file "$token_file" 2>&1)
duplicate_status=$?
set -e
[[ $duplicate_status -ne 0 ]]
[[ "$duplicate_output" == *'already exists'* ]]
[[ "$before_hash" == "$(sha256sum "$config_file")" ]]

touch "$mock_state/no-match"
set +e
failed_rotation_output=$("$enroller" --token-file "$token_file" --replace 2>&1)
failed_rotation_status=$?
set -e
[[ $failed_rotation_status -ne 0 ]]
[[ "$failed_rotation_output" == *'found 0'* ]]
[[ "$failed_rotation_output" != *'dop_v1_'* ]]
[[ "$before_hash" == "$(sha256sum "$config_file")" ]]
rm -f "$mock_state/no-match"

rotation_output=$("$enroller" --token-file "$token_file" --replace)
[[ "$rotation_output" == *'HYPERSHELL_SNAPSHOT_CREDENTIAL_READY|'* ]]
[[ "$rotation_output" != *'dop_v1_'* ]]

echo 'HYPERSHELL_SNAPSHOT_ENROLLMENT_OK|hidden-input|root-only-file|volume-discovery|identity-verification|atomic-install|explicit-rotation|token-redaction'
