#!/usr/bin/env bash
set -euo pipefail
shopt -s nullglob

[[ "${HYPERSHELL_SOURCE_FIXTURE_CONTAINER:-}" == 1 && -f /.dockerenv ]] || {
  echo "This source-cutover fixture may run only inside its disposable container." >&2
  exit 1
}

installer=/repo/deploy/hypershell-server/install-source-snapshot.sh
[[ -f "$installer" ]] || {
  echo "The source installer was not mounted at $installer." >&2
  exit 1
}

fixture_root=$(mktemp -d)
source_repository="$fixture_root/source"
mock_bin="$fixture_root/bin"
mock_state="$fixture_root/state"
hypershell_root=/opt/hypershell
evidence_root=/var/lib/hypershell-deployments

cleanup() {
  rm -rf --one-file-system -- "$hypershell_root"
  rm -rf --one-file-system -- "$evidence_root"
  rm -rf --one-file-system -- "$fixture_root"
}
trap cleanup EXIT

install -d -m 0700 "$source_repository" "$mock_bin" "$mock_state"
git -C "$source_repository" init --initial-branch=main >/dev/null
git -C "$source_repository" config user.email fixture@hypershell.invalid
git -C "$source_repository" config user.name 'Hypershell fixture'
install -d -m 0755 \
  "$source_repository/deploy/hypershell-server" \
  "$source_repository/tools"
cat > "$source_repository/deploy/hypershell-server/example.sh" <<'EOF'
#!/usr/bin/env bash
echo old
EOF
cat > "$source_repository/tools/check.sh" <<'EOF'
#!/usr/bin/env bash
exit 0
EOF
printf 'old source\n' > "$source_repository/README.md"
chmod 0755 \
  "$source_repository/deploy/hypershell-server/example.sh" \
  "$source_repository/tools/check.sh"
git -C "$source_repository" add .
git -C "$source_repository" commit -m 'Initial source' >/dev/null
old_commit=$(git -C "$source_repository" rev-parse HEAD)

cat > "$source_repository/deploy/hypershell-server/example.sh" <<'EOF'
#!/usr/bin/env bash
echo current
EOF
printf 'current source\n' > "$source_repository/README.md"
printf 'verified payload\n' > "$source_repository/verified.txt"
chmod 0755 "$source_repository/deploy/hypershell-server/example.sh"
git -C "$source_repository" add .
git -C "$source_repository" commit -m 'Current source' >/dev/null
current_commit=$(git -C "$source_repository" rev-parse HEAD)
verified_hash=$(sha256sum "$source_repository/verified.txt")
verified_hash=${verified_hash%% *}

install -d -m 0755 "$hypershell_root/git" "$hypershell_root/candidates"
git clone --bare "$source_repository" \
  "$hypershell_root/git/hypershell.git" >/dev/null 2>&1
install -d -m 0755 "$hypershell_root/profile-server"
git -C "$source_repository" -c tar.umask=0022 archive "$old_commit" \
  | tar -x -C "$hypershell_root/profile-server"
printf '%s\n' "$old_commit" \
  > "$hypershell_root/profile-server/.hypershell-source-commit"
find "$hypershell_root/profile-server" -type d -exec chmod 0755 {} +
find "$hypershell_root/profile-server" -type f -exec chmod 0644 {} +
chmod 0755 \
  "$hypershell_root/profile-server/deploy/hypershell-server/example.sh" \
  "$hypershell_root/profile-server/tools/check.sh"

cat > "$mock_bin/docker" <<'MOCK'
#!/usr/bin/env bash
set -euo pipefail
state=${HYPERSHELL_SOURCE_FIXTURE_STATE:?}
printf '%s\n' "$*" >> "$state/docker.log"
case "$1" in
  inspect)
    if [[ "${2:-}" == -f ]]; then
      case "$3" in
        '{{.Id}}') printf 'sha256:fixture-container\n' ;;
        '{{.Config.Image}}') printf 'hypershell-profile-server:fixture\n' ;;
        '{{.RestartCount}}') printf '0\n' ;;
        *) exit 81 ;;
      esac
    else
      printf '{}\n'
    fi
    ;;
  port)
    [[ "$2" == hypershell-profile-server && "$3" == 8080/tcp ]]
    printf '127.0.0.1:5088\n'
    ;;
  *)
    echo "The source installer attempted a container mutation: $*" >&2
    exit 82
    ;;
esac
MOCK

cat > "$mock_bin/curl" <<'MOCK'
#!/usr/bin/env bash
set -euo pipefail
state=${HYPERSHELL_SOURCE_FIXTURE_STATE:?}
printf '%s\n' "$*" >> "$state/curl.log"
if [[ "$*" == *'https://hypershell.atlasuniversalis.com/healthz'* ]]; then
  public_count=0
  [[ ! -f "$state/public-count" ]] || public_count=$(<"$state/public-count")
  public_count=$((public_count + 1))
  printf '%s\n' "$public_count" > "$state/public-count"
  if [[ -f "$state/fail-public-after-swap" && $public_count -ge 2 ]]; then
    exit 22
  fi
fi
printf '{"status":"ready"}\n'
MOCK

chmod 0755 "$mock_bin/docker" "$mock_bin/curl"
export HYPERSHELL_SOURCE_FIXTURE_STATE="$mock_state"
export PATH="$mock_bin:$PATH"

installed_output=$("$installer" \
  --commit "$current_commit" \
  --verify-sha256 "$verified_hash:verified.txt" 2>&1)
[[ "$installed_output" == *'HYPERSHELL_SOURCE_INSTALLED|'* ]]
[[ "$installed_output" == *"commit:$current_commit"* ]]
[[ "$(cat "$hypershell_root/profile-server/.hypershell-source-commit")" == \
   "$current_commit" ]]
[[ "$(cat "$hypershell_root/profile-server/verified.txt")" == \
   'verified payload' ]]
[[ "$(stat -c '%a %u:%g' \
   "$hypershell_root/profile-server/deploy/hypershell-server/example.sh")" == \
   '755 0:0' ]]
[[ "$(stat -c '%a %u:%g' "$hypershell_root/profile-server/README.md")" == \
   '644 0:0' ]]
[[ -z "$(find "$hypershell_root/profile-server" \
   -type f -perm /0022 -print -quit)" ]]

previous_roots=("$hypershell_root"/profile-server-previous-*)
[[ ${#previous_roots[@]} -eq 1 ]]
[[ "$(cat "${previous_roots[0]}/.hypershell-source-commit")" == \
   "$old_commit" ]]
evidence_files=("$evidence_root"/source-*.env)
[[ ${#evidence_files[@]} -eq 1 ]]
[[ "$(stat -c '%a %u:%g' "${evidence_files[0]}")" == '600 0:0' ]]
grep -q "^HYPERSHELL_SOURCE_COMMIT=$current_commit$" "${evidence_files[0]}"
grep -q "^HYPERSHELL_SOURCE_PREVIOUS_COMMIT=$old_commit$" \
  "${evidence_files[0]}"
grep -q '^HYPERSHELL_SOURCE_CONTAINER_RESTARTS=0$' "${evidence_files[0]}"

previous_count=${#previous_roots[@]}
evidence_count=${#evidence_files[@]}
idempotent_output=$("$installer" \
  --commit "$current_commit" \
  --verify-sha256 "$verified_hash:verified.txt" 2>&1)
[[ "$idempotent_output" == *'HYPERSHELL_SOURCE_ALREADY_CURRENT|'* ]]
previous_roots=("$hypershell_root"/profile-server-previous-*)
evidence_files=("$evidence_root"/source-*.env)
[[ ${#previous_roots[@]} -eq $previous_count ]]
[[ ${#evidence_files[@]} -eq $evidence_count ]]

set +e
bad_hash_output=$("$installer" \
  --commit "$current_commit" \
  --verify-sha256 \
  "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa:verified.txt" \
  2>&1)
bad_hash_status=$?
set -e
[[ $bad_hash_status -ne 0 ]]
[[ "$bad_hash_output" == *'failed SHA-256 verification'* ]]
[[ "$(cat "$hypershell_root/profile-server/.hypershell-source-commit")" == \
   "$current_commit" ]]

cat > "$source_repository/tools/non-executable.sh" <<'EOF'
#!/usr/bin/env bash
exit 0
EOF
chmod 0644 "$source_repository/tools/non-executable.sh"
git -C "$source_repository" add .
git -C "$source_repository" commit -m 'Non-executable shell fixture' >/dev/null
non_executable_commit=$(git -C "$source_repository" rev-parse HEAD)
git --git-dir="$hypershell_root/git/hypershell.git" fetch \
  "$source_repository" main:main >/dev/null 2>&1
set +e
non_executable_output=$("$installer" \
  --commit "$non_executable_commit" \
  --verify-sha256 "$verified_hash:verified.txt" 2>&1)
non_executable_status=$?
set -e
[[ $non_executable_status -ne 0 ]]
[[ "$non_executable_output" == \
   *'Deployment or test shell source is not executable: tools/non-executable.sh'* ]]
[[ "$(cat "$hypershell_root/profile-server/.hypershell-source-commit")" == \
   "$current_commit" ]]

set +e
unpushed_output=$("$installer" \
  --commit "$old_commit" \
  --verify-sha256 "$verified_hash:verified.txt" 2>&1)
unpushed_status=$?
set -e
[[ $unpushed_status -ne 0 ]]
[[ "$unpushed_output" == *'not the exact server origin/main commit'* ]]

git -C "$source_repository" rm tools/non-executable.sh >/dev/null
printf 'rollback payload\n' > "$source_repository/verified.txt"
git -C "$source_repository" add verified.txt
git -C "$source_repository" commit -m 'Rollback fixture source' >/dev/null
rollback_commit=$(git -C "$source_repository" rev-parse HEAD)
rollback_hash=$(sha256sum "$source_repository/verified.txt")
rollback_hash=${rollback_hash%% *}
git --git-dir="$hypershell_root/git/hypershell.git" fetch \
  "$source_repository" main:main >/dev/null 2>&1
printf '0\n' > "$mock_state/public-count"
touch "$mock_state/fail-public-after-swap"
set +e
rollback_output=$("$installer" \
  --commit "$rollback_commit" \
  --verify-sha256 "$rollback_hash:verified.txt" 2>&1)
rollback_status=$?
set -e
[[ $rollback_status -ne 0 ]]
[[ "$rollback_output" == *'public health failed after'* ]]
[[ "$(cat "$hypershell_root/profile-server/.hypershell-source-commit")" == \
   "$current_commit" ]]
[[ "$(cat "$hypershell_root/profile-server/verified.txt")" == \
   'verified payload' ]]
failed_candidates=("$hypershell_root"/candidates/failed-source-*)
[[ ${#failed_candidates[@]} -eq 1 ]]
[[ "$(cat "${failed_candidates[0]}/.hypershell-source-commit")" == \
   "$rollback_commit" ]]
previous_roots=("$hypershell_root"/profile-server-previous-*)
evidence_files=("$evidence_root"/source-*.env)
[[ ${#previous_roots[@]} -eq $previous_count ]]
[[ ${#evidence_files[@]} -eq $evidence_count ]]
rm -f "$mock_state/fail-public-after-swap"

if grep -Eq '(^| )(stop|start|restart|run|rm)( |$)' "$mock_state/docker.log"; then
  echo "The source-only installer attempted to mutate the production container." >&2
  exit 1
fi

echo 'HYPERSHELL_SOURCE_SNAPSHOT_INSTALLER_OK|origin-main|hash-proof|atomic-swap|rollback|mode-normalization|executable-shell-sources|root-ownership|container-unchanged|health|evidence|idempotent'
