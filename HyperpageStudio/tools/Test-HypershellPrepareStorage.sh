#!/usr/bin/env bash
set -euo pipefail
shopt -s nullglob

[[ "${HYPERSHELL_STORAGE_FIXTURE_CONTAINER:-}" == 1 && -f /.dockerenv ]] || {
  echo "This storage fixture may run only inside its disposable container." >&2
  exit 1
}

preparer=/repo/deploy/hypershell-server/prepare-storage.sh
[[ -f "$preparer" ]] || {
  echo "The storage preparer was not mounted at $preparer." >&2
  exit 1
}

fixture_root=$(mktemp -d)
mock_bin="$fixture_root/bin"
mock_state="$fixture_root/state"
original_fstab="$fixture_root/fstab.original"
stable_device=/dev/disk/by-id/scsi-0DO_Volume_hypershell-data
mock_device=/dev/hypershell-storage-fixture
mount_point=/srv/hypershell
evidence_root=/var/lib/hypershell-deployments

cleanup() {
  rm -f -- "$stable_device" "$mock_device"
  rm -rf --one-file-system -- "$mount_point"
  rm -rf --one-file-system -- "$evidence_root"
  rm -rf --one-file-system -- "$fixture_root"
}
trap cleanup EXIT

install -d -m 0700 "$mock_bin" "$mock_state"
install -d -m 0755 /dev/disk/by-id /srv
cp --preserve=all /etc/fstab "$original_fstab"
mknod "$mock_device" b 240 1
ln -s "$mock_device" "$stable_device"

cat > "$mock_bin/lsblk" <<'MOCK'
#!/usr/bin/env bash
set -euo pipefail
case "$1" in
  -ndo)
    case "$2" in
      PKNAME) [[ "$3" == /dev/vda1 ]]; printf 'vda\n' ;;
      TYPE) [[ "$3" == /dev/hypershell-storage-fixture ]]; printf 'disk\n' ;;
      *) exit 81 ;;
    esac
    ;;
  -nrpo)
    case "$2" in
      NAME)
        [[ "$3" == /dev/hypershell-storage-fixture ]]
        printf '/dev/hypershell-storage-fixture\n'
        ;;
      MOUNTPOINTS)
        [[ "$3" == /dev/hypershell-storage-fixture ]]
        printf '\n'
        ;;
      *) exit 82 ;;
    esac
    ;;
  *) exit 83 ;;
esac
MOCK

cat > "$mock_bin/blockdev" <<'MOCK'
#!/usr/bin/env bash
set -euo pipefail
[[ "$1" == --getsize64 && "$2" == /dev/hypershell-storage-fixture ]]
printf '53687091200\n'
MOCK

cat > "$mock_bin/blkid" <<'MOCK'
#!/usr/bin/env bash
set -euo pipefail
state=${HYPERSHELL_STORAGE_FIXTURE_STATE:?}
[[ "$1" == -s && "$3" == -o && "$4" == value \
   && "$5" == /dev/hypershell-storage-fixture ]]
[[ -f "$state/formatted" ]] || exit 2
case "$2" in
  TYPE) printf 'ext4\n' ;;
  UUID) printf '12345678-1234-1234-1234-123456789abc\n' ;;
  *) exit 84 ;;
esac
MOCK

cat > "$mock_bin/findmnt" <<'MOCK'
#!/usr/bin/env bash
set -euo pipefail
state=${HYPERSHELL_STORAGE_FIXTURE_STATE:?}
case "$*" in
  '-nro SOURCE /') printf '/dev/vda1\n' ;;
  '--verify --verbose')
    [[ ! -f "$state/fail-verify" ]]
    ;;
  '-nro TARGET -T /srv/hypershell')
    [[ -f "$state/mounted" ]]
    printf '/srv/hypershell\n'
    ;;
  '-nro SOURCE -M /srv/hypershell')
    [[ -f "$state/mounted" ]]
    if [[ -f "$state/wrong-source" ]]; then
      printf '/dev/vdb\n'
    else
      printf '/dev/hypershell-storage-fixture\n'
    fi
    ;;
  *)
    echo "Unexpected findmnt fixture arguments: $*" >&2
    exit 85
    ;;
esac
MOCK

cat > "$mock_bin/mkfs.ext4" <<'MOCK'
#!/usr/bin/env bash
set -euo pipefail
state=${HYPERSHELL_STORAGE_FIXTURE_STATE:?}
[[ "$1" == -L && "$2" == hypershell-data \
   && "$3" == /dev/hypershell-storage-fixture ]]
printf '%s\n' "$*" >> "$state/mkfs.log"
touch "$state/formatted"
MOCK

cat > "$mock_bin/mount" <<'MOCK'
#!/usr/bin/env bash
set -euo pipefail
state=${HYPERSHELL_STORAGE_FIXTURE_STATE:?}
[[ "$1" == /srv/hypershell && $# -eq 1 ]]
printf '%s\n' "$*" >> "$state/mount.log"
[[ ! -f "$state/fail-mount" ]]
touch "$state/mounted"
MOCK

cat > "$mock_bin/umount" <<'MOCK'
#!/usr/bin/env bash
set -euo pipefail
state=${HYPERSHELL_STORAGE_FIXTURE_STATE:?}
[[ "$1" == /srv/hypershell && $# -eq 1 ]]
printf '%s\n' "$*" >> "$state/umount.log"
rm -f "$state/mounted"
MOCK

cat > "$mock_bin/df" <<'MOCK'
#!/usr/bin/env bash
set -euo pipefail
[[ "$1" == -h && "$2" == /srv/hypershell ]]
printf 'Filesystem Size Used Avail Use%% Mounted on\n'
printf '/dev/fixture 50G 24K 50G 1%% /srv/hypershell\n'
MOCK

cat > "$mock_bin/getent" <<'MOCK'
#!/usr/bin/env bash
set -euo pipefail
state=${HYPERSHELL_STORAGE_FIXTURE_STATE:?}
[[ "$1" == group && "$2" == 1654 && $# -eq 2 ]]
[[ ! -f "$state/missing-runtime-group" ]]
printf 'hypershell-runtime:x:1654:\n'
MOCK

chmod 0755 "$mock_bin"/*
export HYPERSHELL_STORAGE_FIXTURE_STATE="$mock_state"
export PATH="$mock_bin:$PATH"

touch "$mock_state/missing-runtime-group"
set +e
missing_group_output=$("$preparer" \
  --device "$stable_device" --format-empty-device 2>&1)
missing_group_status=$?
set -e
[[ $missing_group_status -ne 0 ]]
[[ "$missing_group_output" == *'Host group for runtime GID 1654 is missing'* ]]
[[ ! -f "$mock_state/formatted" ]]
rm -f "$mock_state/missing-runtime-group"

ln -s /tmp "$mount_point"
set +e
unsafe_mount_output=$("$preparer" \
  --device "$stable_device" --format-empty-device 2>&1)
unsafe_mount_status=$?
set -e
[[ $unsafe_mount_status -ne 0 ]]
[[ "$unsafe_mount_output" == *'mount point already exists in an unsafe form'* ]]
[[ ! -f "$mock_state/formatted" ]]
rm -f "$mount_point"

set +e
unconfirmed_output=$("$preparer" --device "$stable_device" 2>&1)
unconfirmed_status=$?
set -e
[[ $unconfirmed_status -ne 0 ]]
[[ "$unconfirmed_output" == *'--format-empty-device'* ]]
[[ ! -f "$mock_state/formatted" ]]
cmp --silent /etc/fstab "$original_fstab"

printf 'none /srv/hypershell ext4 defaults 0 2\n' >> /etc/fstab
set +e
conflict_output=$("$preparer" \
  --device "$stable_device" --format-empty-device 2>&1)
conflict_status=$?
set -e
[[ $conflict_status -ne 0 ]]
[[ "$conflict_output" == *'before the device has a filesystem'* ]]
[[ ! -f "$mock_state/formatted" ]]
cp --preserve=all "$original_fstab" /etc/fstab

install -d -m 0750 -o root -g 1654 "$mount_point/exports"
printf 'must remain visible\n' > "$mount_point/exports/existing.txt"
set +e
hidden_data_output=$("$preparer" \
  --device "$stable_device" --format-empty-device 2>&1)
hidden_data_status=$?
set -e
[[ $hidden_data_status -ne 0 ]]
[[ "$hidden_data_output" == *'contains data or unexpected entries'* ]]
[[ ! -f "$mock_state/formatted" ]]
rm -f "$mount_point/exports/existing.txt"

success_output=$("$preparer" \
  --device "$stable_device" --format-empty-device 2>&1)
[[ "$success_output" == *'HYPERSHELL_STORAGE_READY|'* ]]
[[ "$success_output" == *'device:/dev/hypershell-storage-fixture'* ]]
[[ -f "$mock_state/formatted" && -f "$mock_state/mounted" ]]
[[ $(wc -l < "$mock_state/mkfs.log") -eq 1 ]]
[[ $(wc -l < "$mock_state/mount.log") -eq 1 ]]
grep -q '^UUID=12345678-1234-1234-1234-123456789abc /srv/hypershell ext4 defaults,nofail,discard,noatime,x-systemd.device-timeout=30 0 2$' /etc/fstab
[[ "$(stat -c '%a %u:%g' "$mount_point")" == '750 0:1654' ]]
for directory in server exports; do
  [[ "$(stat -c '%a %u:%g' "$mount_point/$directory")" == '750 0:1654' ]]
done
for directory in releases packages git workspaces backups; do
  [[ "$(stat -c '%a %u:%g' "$mount_point/$directory")" == '750 0:0' ]]
done
fstab_backups=("$evidence_root"/fstab-before-storage-*)
[[ ${#fstab_backups[@]} -eq 1 ]]
cmp --silent "${fstab_backups[0]}" "$original_fstab"

rm -rf --one-file-system -- "$mount_point" "$evidence_root"
rm -f "$mock_state/formatted" "$mock_state/mounted" \
  "$mock_state/mkfs.log" "$mock_state/mount.log" "$mock_state/umount.log"
cp --preserve=all "$original_fstab" /etc/fstab
touch "$mock_state/wrong-source"
set +e
wrong_source_output=$("$preparer" \
  --device "$stable_device" --format-empty-device 2>&1)
wrong_source_status=$?
set -e
[[ $wrong_source_status -ne 0 ]]
[[ "$wrong_source_output" == *'did not mount from the approved device'* ]]
[[ -f "$mock_state/formatted" && ! -f "$mock_state/mounted" ]]
[[ $(wc -l < "$mock_state/umount.log") -eq 1 ]]
cmp --silent /etc/fstab "$original_fstab"

if grep -Eqv '^(-L hypershell-data /dev/hypershell-storage-fixture)?$' \
  "$mock_state/mkfs.log"; then
  echo "The fixture detected an unexpected format invocation." >&2
  exit 1
fi

echo 'HYPERSHELL_PREPARE_STORAGE_OK|runtime-identity-preflight|stable-device|root-disk-separation|size|pre-format-refusal|fstab-preflight|legacy-export-guard|explicit-format|uuid|mount-source|rollback|permissions|layout'
