#!/usr/bin/env bash
set -euo pipefail

[[ "${HYPERSHELL_PUBLISHER_FIXTURE_CONTAINER:-}" == 1 \
   && -f /.dockerenv ]] || {
  echo "This destructive fixture may run only in its disposable test container." >&2
  exit 1
}

publisher=/repo/deploy/hypershell-server/publish-profile-release.sh
[[ -x "$publisher" ]] || {
  echo "Mount the Hypershell repository read-only at /repo." >&2
  exit 1
}

fixture_root=/srv/hypershell
mock_root=/fixture-bin
signer=$(printf 'a%.0s' {1..64})
commit=$(printf 'b%.0s' {1..40})

rm -rf -- "$fixture_root" /var/lib/hypershell-deployments \
  /var/lib/docker/volumes/test-data "$mock_root"
install -d -m 0755 "$mock_root"
install -d -m 0750 /var/lib/docker/volumes/test-data/_data

cat > "$mock_root/docker" <<'MOCK_DOCKER'
#!/usr/bin/env bash
set -euo pipefail
signer=$(printf 'a%.0s' {1..64})
commit=$(printf 'b%.0s' {1..40})
case "${1:-}" in
  inspect)
    if [[ "${2:-}" != -f ]]; then
      exit 0
    fi
    case "${3:-}" in
      *State.Running*) printf 'true\n' ;;
      *Config.User*) printf '1654:1654\n' ;;
      *Config.Env*) printf 'HYPERSHELL_RELEASE_SIGNER_SHA256=%s\n' "$signer" ;;
      *Config.Image*) printf 'hypershell-profile-server:fixture\n' ;;
      *'.Type'*) printf 'volume\n' ;;
      *'.Source'*) printf '/var/lib/docker/volumes/test-data/_data\n' ;;
      *'.Name'*) printf 'test-data\n' ;;
      *) echo "Unexpected docker inspect template: ${3:-}" >&2; exit 1 ;;
    esac
    ;;
  run)
    arguments=" $* "
    if [[ "$arguments" == *' release publish '* ]]; then
      printf 'RELEASE_CREATED|%s|stable|%s\n' \
        "$MOCK_RELEASE_VERSION" "$commit"
    elif [[ "$arguments" == *' release list '* ]]; then
      printf 'RELEASE|%s|stable|%s|2026-08-15T00:00:00Z|retained\n' \
        "$MOCK_RELEASE_VERSION" "$commit"
      if [[ "${MOCK_INVENTORY_MODE:-normal}" == duplicate ]]; then
        printf 'RELEASE|%s|stable|%s|2026-08-15T00:00:00Z|retained\n' \
          "$MOCK_RELEASE_VERSION" "$commit"
      fi
      printf 'RELEASE|3.0.0|stable|%s|2026-08-14T00:00:00Z|retained\n' "$commit"
      printf 'RELEASE|2.0.0|stable|%s|2026-08-13T00:00:00Z|retained\n' "$commit"
      printf 'RELEASE|1.0.0|stable|%s|2026-08-12T00:00:00Z|pinned\n' "$commit"
    else
      echo "Unexpected docker run arguments: $*" >&2
      exit 1
    fi
    ;;
  *) echo "Unexpected docker command: $*" >&2; exit 1 ;;
esac
MOCK_DOCKER

cat > "$mock_root/findmnt" <<'MOCK_FINDMNT'
#!/usr/bin/env bash
printf '/srv/hypershell\n'
MOCK_FINDMNT

cat > "$mock_root/flock" <<'MOCK_FLOCK'
#!/usr/bin/env bash
exit 0
MOCK_FLOCK

cat > "$mock_root/git" <<'MOCK_GIT'
#!/usr/bin/env bash
printf 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\n'
MOCK_GIT

chmod 0755 "$mock_root"/*
export PATH="$mock_root:$PATH"

create_bundle() {
  local version=$1
  local bundle="$fixture_root/releases/incoming/$version"
  install -d -m 0750 "$bundle"
  printf 'manifest-%s\n' "$version" > "$bundle/release-manifest.json"
  printf 'installer-%s\n' "$version" > \
    "$bundle/Hypershell-$version-win-x64.msi"
  printf 'portable-%s\n' "$version" > \
    "$bundle/Hypershell-$version-win-x64-portable.zip"
  chmod 0640 "$bundle"/*
}

install -d -m 0750 "$fixture_root/releases/incoming"
install -d -m 0700 "$fixture_root/releases/published"
for archived_version in 3.0.0 2.0.0 1.0.0 0.9.0; do
  install -d -m 0750 \
    "$fixture_root/releases/published/$archived_version"
done
create_bundle 4.0.0
export MOCK_RELEASE_VERSION=4.0.0
first_output=$("$publisher" \
  --bundle "$fixture_root/releases/incoming/4.0.0" \
  --signer-sha256 "$signer")
[[ "$first_output" == *'HYPERSHELL_RELEASE_PUBLISHED|version:4.0.0|'* \
   && "$first_output" == *'|pruned:1|'* ]]
for retained_version in 4.0.0 3.0.0 2.0.0 1.0.0; do
  [[ -d "$fixture_root/releases/published/$retained_version" ]]
done
[[ ! -e "$fixture_root/releases/published/0.9.0" \
   && ! -e "$fixture_root/releases/incoming/4.0.0" ]]
evidence=$(find /var/lib/hypershell-deployments \
  -maxdepth 1 -type f -name 'release-4.0.0-*.env')
[[ -f "$evidence" \
   && "$(stat -c '%a' "$evidence")" == 600 ]]
grep -Fq 'HYPERSHELL_RELEASE_PRUNED_ARCHIVES=0.9.0' "$evidence"

create_bundle 4.0.1
install -d -m 0750 "$fixture_root/releases/published/0.8.0"
ln -s /tmp "$fixture_root/releases/published/unsafe"
export MOCK_RELEASE_VERSION=4.0.1
set +e
unsafe_output=$("$publisher" \
  --bundle "$fixture_root/releases/incoming/4.0.1" \
  --signer-sha256 "$signer" 2>&1)
unsafe_status=$?
set -e
[[ "$unsafe_status" -ne 0 \
   && "$unsafe_output" == *'non-directory or symlink entry'* \
   && -d "$fixture_root/releases/published/0.8.0" \
   && -d "$fixture_root/releases/published/4.0.0" \
   && -d "$fixture_root/releases/incoming/4.0.1" ]]

rm -- "$fixture_root/releases/published/unsafe"
create_bundle 4.0.2
install -d -m 0750 "$fixture_root/releases/published/0.7.0"
export MOCK_RELEASE_VERSION=4.0.2
export MOCK_INVENTORY_MODE=duplicate
set +e
duplicate_output=$("$publisher" \
  --bundle "$fixture_root/releases/incoming/4.0.2" \
  --signer-sha256 "$signer" 2>&1)
duplicate_status=$?
set -e
[[ "$duplicate_status" -ne 0 \
   && "$duplicate_output" == *'duplicate version'* \
   && -d "$fixture_root/releases/published/0.7.0" \
   && -d "$fixture_root/releases/published/4.0.0" \
   && -d "$fixture_root/releases/incoming/4.0.2" ]]

echo 'HYPERSHELL_SERVER_RELEASE_PUBLISHER_OK|retention|pinned|evidence|strict-inventory|validate-before-prune'
