#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat >&2 <<'EOF'
Usage: install-source-snapshot.sh --commit SHA40 \
       --verify-sha256 SHA256:RELATIVE_PATH [--verify-sha256 ...]

Installs the exact origin/main commit from the server's private bare repository
at /opt/hypershell/profile-server. This is a source-only cutover: it does not
build or restart the production container. At least one expected file hash is
required so the operator proves that the intended source reached the server.
EOF
  exit 2
}

commit=""
verification_specs=()
while (($#)); do
  case "$1" in
    --commit) commit="${2:-}"; shift 2 ;;
    --verify-sha256) verification_specs+=("${2:-}"); shift 2 ;;
    *) usage ;;
  esac
done

[[ $EUID -eq 0 ]] || { echo "Run as root." >&2; exit 1; }
[[ "$commit" =~ ^[0-9a-f]{40}$ ]] || usage
((${#verification_specs[@]} >= 1)) || usage
for command in \
  bash cat chmod chown curl date docker find flock git head install mktemp \
  mv readlink rm sha256sum stat tar; do
  command -v "$command" >/dev/null || {
    echo "Required command '$command' is unavailable." >&2
    exit 1
  }
done

bare_repository=/opt/hypershell/git/hypershell.git
active_root=/opt/hypershell/profile-server
candidates_root=/opt/hypershell/candidates
evidence_root=/var/lib/hypershell-deployments
[[ -d "$bare_repository" && ! -L "$bare_repository" ]] || {
  echo "The private bare repository is missing or unsafe." >&2
  exit 1
}
[[ -d "$active_root" && ! -L "$active_root" ]] || {
  echo "The canonical source root is missing or unsafe." >&2
  exit 1
}
[[ "$(stat -c '%u:%g' "$active_root")" == 0:0 ]] || {
  echo "The canonical source root must be owned by root:root." >&2
  exit 1
}
active_mode=$(stat -c '%a' "$active_root")
(( (8#$active_mode & 0022) == 0 )) || {
  echo "The canonical source root cannot be writable by group or other users." >&2
  exit 1
}
[[ -z "$(find "$active_root" -xdev -type f -perm /0022 -print -quit)" ]] || {
  echo "The canonical source root contains a group- or world-writable file." >&2
  exit 1
}
remote_main=$(git --git-dir="$bare_repository" rev-parse refs/heads/main)
[[ "$remote_main" == "$commit" ]] || {
  echo "The requested commit is not the exact server origin/main commit." >&2
  exit 1
}

verification_hashes=()
verification_paths=()
for spec in "${verification_specs[@]}"; do
  expected_hash=${spec%%:*}
  relative_path=${spec#*:}
  expected_hash=${expected_hash,,}
  [[ "$spec" == *:* \
     && "$expected_hash" =~ ^[0-9a-f]{64}$ \
     && "$relative_path" =~ ^[A-Za-z0-9._/-]+$ \
     && "$relative_path" != /* \
     && "$relative_path" != *//* \
     && "/$relative_path/" != */../* \
     && "/$relative_path/" != */./* ]] || {
    echo "Each verification must use SHA256:SAFE_RELATIVE_PATH." >&2
    exit 1
  }
  verification_hashes+=("$expected_hash")
  verification_paths+=("$relative_path")
done

validate_expected_files() {
  local source_root=$1
  local resolved_root resolved_path actual_hash index
  resolved_root=$(readlink -f -- "$source_root")
  for ((index=0; index<${#verification_paths[@]}; index++)); do
    resolved_path=$(readlink -f -- \
      "$source_root/${verification_paths[$index]}")
    [[ -f "$resolved_path" \
       && ! -L "$source_root/${verification_paths[$index]}" \
       && "$resolved_path" == "$resolved_root/"* ]] || {
      echo "A required verification file is missing or escaped the source root." >&2
      return 1
    }
    actual_hash=$(sha256sum "$resolved_path")
    actual_hash=${actual_hash%% *}
    [[ "$actual_hash" == "${verification_hashes[$index]}" ]] || {
      echo "A required source file failed SHA-256 verification: ${verification_paths[$index]}" >&2
      return 1
    }
  done
}

normalize_source_permissions() {
  local source_root=$1
  local metadata mode path record
  find "$source_root" -xdev -type d -exec chmod 0755 {} +
  find "$source_root" -xdev -type f -exec chmod 0644 {} +
  while IFS= read -r -d '' record; do
    metadata=${record%%$'\t'*}
    path=${record#*$'\t'}
    mode=${metadata%% *}
    if [[ "$mode" == 100755 ]]; then
      chmod 0755 "$source_root/$path"
    fi
  done < <(git --git-dir="$bare_repository" ls-tree -rz "$commit")
  chown -R root:root "$source_root"
  [[ -z "$(find "$source_root" -xdev -type f -perm /0022 -print -quit)" ]] || {
    echo "The source snapshot contains a group- or world-writable file." >&2
    return 1
  }
}

validate_shell_sources() {
  local source_root=$1
  local shell_file relative_path
  while IFS= read -r -d '' shell_file; do
    relative_path=${shell_file#"$source_root/"}
    [[ -x "$shell_file" ]] || {
      echo "Deployment or test shell source is not executable: $relative_path" >&2
      return 1
    }
    bash -n "$shell_file"
  done < <(find "$source_root/deploy/hypershell-server" \
    "$source_root/tools" -xdev -type f -name '*.sh' -print0)
}

container=hypershell-profile-server
docker inspect "$container" >/dev/null 2>&1 || {
  echo "The canonical production container is missing." >&2
  exit 1
}
container_id_before=$(docker inspect -f '{{.Id}}' "$container")
container_image_before=$(docker inspect -f '{{.Config.Image}}' "$container")
restart_count_before=$(docker inspect -f '{{.RestartCount}}' "$container")
active_endpoint=$(docker port "$container" 8080/tcp | head -n 1)
[[ "$active_endpoint" =~ ^127\.0\.0\.1:([0-9]{4,5})$ ]] || {
  echo "The production service is not bound to IPv4 loopback." >&2
  exit 1
}
active_port=${BASH_REMATCH[1]}

health_ready() {
  local url=$1
  local response
  response=$(curl --fail --silent --show-error \
    --connect-timeout 5 --max-time 15 "$url") || return 1
  [[ "$response" == *'"status":"ready"'* ]]
}
health_ready "http://127.0.0.1:$active_port/healthz" || {
  echo "Production loopback health is not ready before source installation." >&2
  exit 1
}
health_ready "https://hypershell.atlasuniversalis.com/healthz" || {
  echo "Production public health is not ready before source installation." >&2
  exit 1
}

exec 8>/run/lock/hypershell-profile-deploy.lock
flock -n 8 || { echo "Another Hypershell deployment is running." >&2; exit 1; }
exec 9>/run/lock/hypershell-profile-backup.lock
flock -n 9 || { echo "A Hypershell backup is running." >&2; exit 1; }

active_marker="$active_root/.hypershell-source-commit"
[[ -f "$active_marker" && ! -L "$active_marker" ]] || {
  echo "The canonical source root has no trusted commit marker." >&2
  exit 1
}
active_commit=$(<"$active_marker")
[[ "$active_commit" =~ ^[0-9a-f]{40}$ ]] || {
  echo "The canonical source commit marker is malformed." >&2
  exit 1
}
if [[ "$active_commit" == "$commit" ]]; then
  validate_expected_files "$active_root"
  validate_shell_sources "$active_root"
  [[ "$(docker inspect -f '{{.Id}}' "$container")" == "$container_id_before" \
     && "$(docker inspect -f '{{.RestartCount}}' "$container")" == \
        "$restart_count_before" ]] || {
    echo "The production container changed during source verification." >&2
    exit 1
  }
  echo "HYPERSHELL_SOURCE_ALREADY_CURRENT|commit:$commit|container:$container_id_before|restarts:$restart_count_before"
  exit 0
fi

install -d -m 0755 -o root -g root "$candidates_root"
install -d -m 0700 -o root -g root "$evidence_root"
[[ ! -L "$candidates_root" \
   && "$(readlink -f -- "$candidates_root")" == "$candidates_root" \
   && "$(stat -c '%u:%g' "$candidates_root")" == 0:0 ]] || {
  echo "The source candidate root is unsafe." >&2
  exit 1
}
candidates_mode=$(stat -c '%a' "$candidates_root")
(( (8#$candidates_mode & 0022) == 0 )) || {
  echo "The source candidate root cannot be writable by group or other users." >&2
  exit 1
}
[[ ! -L "$evidence_root" \
   && "$(readlink -f -- "$evidence_root")" == "$evidence_root" \
   && "$(stat -c '%u:%g' "$evidence_root")" == 0:0 ]] || {
  echo "The source evidence root is unsafe." >&2
  exit 1
}
evidence_mode=$(stat -c '%a' "$evidence_root")
(( (8#$evidence_mode & 0022) == 0 )) || {
  echo "The source evidence root cannot be writable by group or other users." >&2
  exit 1
}

stamp=$(date -u +%Y%m%dT%H%M%S%NZ)-$$
candidate="$candidates_root/source-$commit-$stamp"
previous="$active_root-previous-$stamp"
failed_candidate="$candidates_root/failed-source-$commit-$stamp"
rollback_required=0
cleanup() {
  local status=$?
  trap - EXIT
  if [[ $status -ne 0 && $rollback_required -eq 1 && -d "$previous" ]]; then
    if [[ -d "$active_root" && ! -e "$failed_candidate" ]]; then
      mv "$active_root" "$failed_candidate"
    fi
    mv "$previous" "$active_root"
  fi
  if [[ -d "$candidate" \
        && "$candidate" == "$candidates_root/source-$commit-$stamp" ]]; then
    rm -rf --one-file-system -- "$candidate"
  fi
  exit "$status"
}
trap cleanup EXIT

install -d -m 0755 -o root -g root "$candidate"
git -c tar.umask=0022 --git-dir="$bare_repository" archive "$commit" \
  | tar -x -C "$candidate"
printf '%s\n' "$commit" > "$candidate/.hypershell-source-commit"
normalize_source_permissions "$candidate"
validate_expected_files "$candidate"
validate_shell_sources "$candidate"

[[ ! -e "$previous" && ! -L "$previous" ]] || {
  echo "The generated rollback source path already exists." >&2
  exit 1
}
mv "$active_root" "$previous"
rollback_required=1
mv "$candidate" "$active_root"

[[ "$(docker inspect -f '{{.Id}}' "$container")" == "$container_id_before" \
   && "$(docker inspect -f '{{.Config.Image}}' "$container")" == \
      "$container_image_before" \
   && "$(docker inspect -f '{{.RestartCount}}' "$container")" == \
      "$restart_count_before" ]] || {
  echo "The production container changed during the source-only cutover." >&2
  exit 1
}
health_ready "http://127.0.0.1:$active_port/healthz" || {
  echo "Production loopback health failed after the source-only cutover." >&2
  exit 1
}
health_ready "https://hypershell.atlasuniversalis.com/healthz" || {
  echo "Production public health failed after the source-only cutover." >&2
  exit 1
}

evidence="$evidence_root/source-$stamp.env"
evidence_temporary="$evidence.tmp"
[[ ! -e "$evidence" && ! -L "$evidence" \
   && ! -e "$evidence_temporary" && ! -L "$evidence_temporary" ]] || {
  echo "The generated source evidence path already exists." >&2
  exit 1
}
cat > "$evidence_temporary" <<EOF
HYPERSHELL_SOURCE_SCHEMA=2
HYPERSHELL_SOURCE_COMMIT=$commit
HYPERSHELL_SOURCE_PREVIOUS_COMMIT=$active_commit
HYPERSHELL_SOURCE_ACTIVE=$active_root
HYPERSHELL_SOURCE_PREVIOUS=$previous
HYPERSHELL_SOURCE_CONTAINER_ID=$container_id_before
HYPERSHELL_SOURCE_CONTAINER_IMAGE=$container_image_before
HYPERSHELL_SOURCE_CONTAINER_RESTARTS=$restart_count_before
HYPERSHELL_SOURCE_VERIFICATION_COUNT=${#verification_paths[@]}
EOF
for ((index=0; index<${#verification_paths[@]}; index++)); do
  printf 'HYPERSHELL_SOURCE_VERIFICATION_%d_SHA256=%s\n' \
    "$index" "${verification_hashes[$index]}" >> "$evidence_temporary"
  printf 'HYPERSHELL_SOURCE_VERIFICATION_%d_PATH=%s\n' \
    "$index" "${verification_paths[$index]}" >> "$evidence_temporary"
done
chmod 0600 "$evidence_temporary"
chown root:root "$evidence_temporary"
mv "$evidence_temporary" "$evidence"
rollback_required=0
trap - EXIT

echo "HYPERSHELL_SOURCE_INSTALLED|commit:$commit|previous:$active_commit|container:$container_id_before|restarts:$restart_count_before|evidence:$evidence"
