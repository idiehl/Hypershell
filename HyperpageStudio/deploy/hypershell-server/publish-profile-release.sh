#!/usr/bin/env bash
set -euo pipefail

usage() {
  echo "Usage: publish-profile-release.sh --bundle /srv/hypershell/releases/incoming/VERSION --signer-sha256 SHA256 [--pin]" >&2
  exit 2
}

bundle=""
signer_fingerprint=""
pin=0
container="hypershell-profile-server"
while (($#)); do
  case "$1" in
    --bundle) bundle="${2:-}"; shift 2 ;;
    --signer-sha256) signer_fingerprint="${2:-}"; shift 2 ;;
    --pin) pin=1; shift ;;
    *) usage ;;
  esac
done

[[ $EUID -eq 0 ]] || { echo "Run as root." >&2; exit 1; }
[[ -n "$bundle" && -n "$signer_fingerprint" ]] || usage
signer_fingerprint=${signer_fingerprint,,}
[[ "$signer_fingerprint" =~ ^[0-9a-f]{64}$ ]] || {
  echo "The release-signer SHA-256 fingerprint is malformed." >&2
  exit 1
}
for command in \
  awk basename chmod chown date docker find findmnt flock git grep head \
  install mktemp mv readlink rm sed sha256sum sort stat wc; do
  command -v "$command" >/dev/null || {
    echo "Required command '$command' is unavailable." >&2
    exit 1
  }
done

[[ -d "$bundle" && ! -L "$bundle" ]] || {
  echo "The incoming release must be a real, non-symlink directory." >&2
  exit 1
}
bundle=$(readlink -f -- "$bundle")
[[ -d "$bundle" && \
   "$bundle" =~ ^/srv/hypershell/releases/incoming/[^/]+$ ]] || {
  echo "The release bundle must be a real directory directly beneath the incoming root." >&2
  exit 1
}
version=$(basename -- "$bundle")
[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$ ]] || {
  echo "The incoming directory name must be the release version." >&2
  exit 1
}
published_root=/srv/hypershell/releases/published
published_bundle="$published_root/$version"
[[ "$(findmnt -nro TARGET -T "$bundle")" == /srv/hypershell ]] || {
  echo "The incoming release is not on the dedicated Hypershell volume." >&2
  exit 1
}
[[ "$(stat -c '%U' "$bundle")" == root ]] || {
  echo "The incoming release directory must be owned by root." >&2
  exit 1
}
bundle_mode=$(stat -c '%a' "$bundle")
(( (8#$bundle_mode & 0022) == 0 )) || {
  echo "The incoming release directory cannot be writable by group or other users." >&2
  exit 1
}

manifest="$bundle/release-manifest.json"
installer="$bundle/Hypershell-$version-win-x64.msi"
portable="$bundle/Hypershell-$version-win-x64-portable.zip"
for artifact in "$manifest" "$installer" "$portable"; do
  [[ -f "$artifact" && ! -L "$artifact" && -s "$artifact" ]] || {
    echo "The expected release artifact is missing or unsafe: $artifact" >&2
    exit 1
  }
  [[ "$(stat -c '%U' "$artifact")" == root ]] || {
    echo "A release artifact is not owned by root: $artifact" >&2
    exit 1
  }
  artifact_mode=$(stat -c '%a' "$artifact")
  (( (8#$artifact_mode & 0022) == 0 )) || {
    echo "A release artifact is writable by group or other users: $artifact" >&2
    exit 1
  }
done
entry_count=$(find "$bundle" -mindepth 1 -maxdepth 1 -printf '.' | wc -c)
regular_count=$(find "$bundle" -mindepth 1 -maxdepth 1 -type f -printf '.' | wc -c)
[[ "$entry_count" -eq 3 && "$regular_count" -eq 3 ]] || {
  echo "The incoming release must contain exactly the manifest, MSI, and portable ZIP." >&2
  exit 1
}
[[ "$(stat -c '%s' "$manifest")" -le 1048576 ]] || {
  echo "The signed release manifest exceeds its server staging limit." >&2
  exit 1
}
total_bytes=$(( $(stat -c '%s' "$manifest") + \
  $(stat -c '%s' "$installer") + $(stat -c '%s' "$portable") ))
[[ "$total_bytes" -le 2147483648 ]] || {
  echo "The staged release exceeds the 2 GiB publication limit." >&2
  exit 1
}

exec 8>/run/lock/hypershell-profile-deploy.lock
flock -n 8 || { echo "Another Hypershell deployment is running." >&2; exit 1; }
exec 9>/run/lock/hypershell-profile-backup.lock
flock -n 9 || { echo "A Hypershell backup is running." >&2; exit 1; }

if [[ -e "$published_root" || -L "$published_root" ]]; then
  [[ -d "$published_root" && ! -L "$published_root" ]] || {
    echo "The published release archive root is unsafe." >&2
    exit 1
  }
else
  install -d -m 0700 "$published_root"
fi
[[ -d "$published_root" \
   && ! -L "$published_root" \
   && "$(readlink -f -- "$published_root")" == "$published_root" \
   && "$(findmnt -nro TARGET -T "$published_root")" == /srv/hypershell \
   && "$(stat -c '%U' "$published_root")" == root ]] || {
  echo "The published release archive root is unsafe." >&2
  exit 1
}
published_mode=$(stat -c '%a' "$published_root")
(( (8#$published_mode & 0077) == 0 )) || {
  echo "The published release archive root must be accessible only by root." >&2
  exit 1
}
[[ ! -e "$published_bundle" ]] || {
  echo "The immutable published-bundle archive already exists." >&2
  exit 1
}

existing_archives=()
while IFS= read -r -d '' archived_bundle; do
  [[ -d "$archived_bundle" && ! -L "$archived_bundle" ]] || {
    echo "The published release archive contains a non-directory or symlink entry." >&2
    exit 1
  }
  archived_version=$(basename -- "$archived_bundle")
  [[ "$archived_version" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$ \
     && "$(readlink -f -- "$archived_bundle")" == \
        "$published_root/$archived_version" \
     && "$(stat -c '%U' "$archived_bundle")" == root ]] || {
    echo "The published release archive contains an unsafe version directory." >&2
    exit 1
  }
  archived_mode=$(stat -c '%a' "$archived_bundle")
  (( (8#$archived_mode & 0022) == 0 )) || {
    echo "A published release archive is writable by group or other users." >&2
    exit 1
  }
  existing_archives+=("$archived_bundle")
done < <(find "$published_root" -mindepth 1 -maxdepth 1 -print0 | sort -z)

docker inspect "$container" >/dev/null 2>&1 || {
  echo "The production profile-server container is missing." >&2
  exit 1
}
[[ "$(docker inspect -f '{{.State.Running}}' "$container")" == true ]] || {
  echo "The production profile-server container is not running." >&2
  exit 1
}
[[ "$(docker inspect -f '{{.Config.User}}' "$container")" == 1654:1654 ]] || {
  echo "The production runtime identity is not the expected 1654:1654 account." >&2
  exit 1
}
active_signer=$(docker inspect -f '{{range .Config.Env}}{{println .}}{{end}}' \
  "$container" | sed -n 's/^HYPERSHELL_RELEASE_SIGNER_SHA256=//p' | head -n 1)
[[ "$active_signer" == "$signer_fingerprint" ]] || {
  echo "Promote the production service with this exact release signer before publishing." >&2
  exit 1
}
image=$(docker inspect -f '{{.Config.Image}}' "$container")
[[ "$image" =~ ^[A-Za-z0-9][A-Za-z0-9_./:@-]{0,255}$ ]] || {
  echo "The production image identity is malformed." >&2
  exit 1
}
repository_commit=$(git --git-dir=/opt/hypershell/git/hypershell.git \
  rev-parse refs/heads/main)
[[ "$repository_commit" =~ ^[0-9a-f]{40,64}$ ]] || {
  echo "The server repository commit could not be recorded." >&2
  exit 1
}

data_type=$(docker inspect -f '{{range .Mounts}}{{if eq .Destination "/var/lib/hypershell-profile-server"}}{{.Type}}{{end}}{{end}}' "$container")
data_source=$(docker inspect -f '{{range .Mounts}}{{if eq .Destination "/var/lib/hypershell-profile-server"}}{{.Source}}{{end}}{{end}}' "$container")
data_name=$(docker inspect -f '{{range .Mounts}}{{if eq .Destination "/var/lib/hypershell-profile-server"}}{{.Name}}{{end}}{{end}}' "$container")
data_source=$(readlink -f -- "$data_source")
if [[ "$data_type" == volume && "$data_source" == /var/lib/docker/volumes/*/_data && -n "$data_name" ]]; then
  data_mount=(--volume "$data_name:/var/lib/hypershell-profile-server")
elif [[ "$data_type" == bind && "$data_source" == /srv/hypershell/server/* ]]; then
  data_mount=(--mount "type=bind,src=$data_source,dst=/var/lib/hypershell-profile-server")
else
  echo "The production data source is outside the approved storage roots." >&2
  exit 1
fi
chown root:1654 "$bundle" "$manifest" "$installer" "$portable"
chmod 0750 "$bundle"
chmod 0640 "$manifest" "$installer" "$portable"

run_release_command() {
  local name=$1
  shift
  docker run --rm \
    --name "$name" \
    --user 1654:1654 \
    --network none \
    --read-only \
    --tmpfs /tmp:size=32m,mode=1777 \
    --security-opt no-new-privileges:true \
    --cap-drop ALL \
    --memory 512m \
    --cpus 1.0 \
    --pids-limit 128 \
    "${data_mount[@]}" \
    --mount "type=bind,src=$bundle,dst=/release,readonly" \
    --env HYPERSHELL_PROFILE_SERVER_DATA=/var/lib/hypershell-profile-server \
    --env HYPERSHELL_MINIMUM_FREE_STORAGE_MIB=512 \
    --env "HYPERSHELL_RELEASE_SIGNER_SHA256=$signer_fingerprint" \
    --env HYPERSHELL_STABLE_RELEASE_RETENTION=3 \
    "$image" release "$@"
}

publish_arguments=(
  publish
  --bundle /release
  --expected-git-commit "$repository_commit"
)
if [[ "$pin" -eq 1 ]]; then
  publish_arguments+=(--pin)
fi
stamp="$(date -u +%Y%m%dT%H%M%SZ)-$$"
publish_output=$(run_release_command \
  "hypershell-release-publish-$$" "${publish_arguments[@]}")
printf '%s\n' "$publish_output"
grep -Eq "^RELEASE_(CREATED|ALREADYPRESENT)\\|$version\\|stable\\|$repository_commit$" \
  <<<"$publish_output" || {
  echo "The release command did not confirm the expected stable version." >&2
  exit 1
}

list_output=$(run_release_command \
  "hypershell-release-list-$$" list --channel stable)
printf '%s\n' "$list_output"

declare -A retained_versions=()
inventory_count=0
while IFS='|' read -r record inventory_version inventory_channel \
    inventory_commit inventory_published_at inventory_state inventory_extra; do
  [[ "$record" == RELEASE ]] || continue
  [[ -z "$inventory_extra" \
     && "$inventory_channel" == stable \
     && "$inventory_version" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$ \
     && "$inventory_commit" =~ ^[0-9a-fA-F]{7,64}$ \
     && -n "$inventory_published_at" \
     && "$inventory_state" =~ ^(pinned|retained)$ ]] || {
    echo "The stable release inventory contained a malformed record." >&2
    exit 1
  }
  [[ -z "${retained_versions[$inventory_version]+present}" ]] || {
    echo "The stable release inventory contained a duplicate version." >&2
    exit 1
  }
  retained_versions["$inventory_version"]=1
  ((inventory_count += 1))
done <<<"$list_output"
[[ "$inventory_count" -gt 0 \
   && -n "${retained_versions[$version]+present}" ]] || {
  echo "The published release is missing from the stable inventory." >&2
  exit 1
}

archives_to_prune=()
for archived_bundle in "${existing_archives[@]}"; do
  archived_version=$(basename -- "$archived_bundle")
  if [[ -z "${retained_versions[$archived_version]+present}" ]]; then
    archives_to_prune+=("$archived_bundle")
  fi
done

pruned_versions=()
for archived_bundle in "${archives_to_prune[@]}"; do
  archived_version=$(basename -- "$archived_bundle")
  rm -rf --one-file-system -- "$archived_bundle"
  [[ ! -e "$archived_bundle" ]] || {
    echo "A stale published release archive could not be removed." >&2
    exit 1
  }
  pruned_versions+=("$archived_version")
done
pruned_csv=$(IFS=,; echo "${pruned_versions[*]}")

manifest_sha256=$(sha256sum -- "$manifest" | awk '{print $1}')
evidence_root=/var/lib/hypershell-deployments
evidence="$evidence_root/release-$version-$stamp.env"
install -d -m 0700 "$evidence_root"
evidence_stage=""
cleanup_evidence() {
  local status=$?
  trap - EXIT
  if [[ -n "$evidence_stage" && -f "$evidence_stage" ]]; then
    rm -f -- "$evidence_stage"
  fi
  exit "$status"
}
trap cleanup_evidence EXIT
evidence_stage=$(mktemp "$evidence_root/.release.XXXXXX")
{
  printf 'HYPERSHELL_RELEASE_SCHEMA=1\n'
  printf 'HYPERSHELL_RELEASE_VERSION=%q\n' "$version"
  printf 'HYPERSHELL_RELEASE_SIGNER_SHA256=%q\n' "$signer_fingerprint"
  printf 'HYPERSHELL_RELEASE_MANIFEST_SHA256=%q\n' "$manifest_sha256"
  printf 'HYPERSHELL_RELEASE_IMAGE=%q\n' "$image"
  printf 'HYPERSHELL_RELEASE_REPOSITORY_COMMIT=%q\n' "$repository_commit"
  printf 'HYPERSHELL_RELEASE_PINNED=%q\n' "$pin"
  printf 'HYPERSHELL_RELEASE_BUNDLE=%q\n' "$published_bundle"
  printf 'HYPERSHELL_RELEASE_PRUNED_ARCHIVES=%q\n' "$pruned_csv"
  printf 'HYPERSHELL_RELEASE_PUBLISHED_AT=%q\n' "$stamp"
} > "$evidence_stage"
chmod 0600 "$evidence_stage"
mv -T --no-clobber -- "$bundle" "$published_bundle"
mv -- "$evidence_stage" "$evidence"
evidence_stage=""
trap - EXIT
echo "HYPERSHELL_RELEASE_PUBLISHED|version:$version|signer:$signer_fingerprint|bundle:$published_bundle|pruned:${#pruned_versions[@]}|evidence:$evidence"
