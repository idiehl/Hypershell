#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat >&2 <<'EOF'
Usage: export-profile-server.sh --backup PATH --recipient GPG_RECIPIENT [--destination PATH] [--snapshot-id ID]

Encrypts a verified consistent backup together with the external Data
Protection and SSH-vault recovery material. Only ciphertext and a non-secret
manifest are written to the portal export directory.
EOF
  exit 2
}

backup=""
recipient=""
destination="/srv/hypershell/exports"
secret_dir="/etc/hypershell-profile-server/secrets"
snapshot_id=""
while (($#)); do
  case "$1" in
    --backup) backup="${2:-}"; shift 2 ;;
    --recipient) recipient="${2:-}"; shift 2 ;;
    --destination) destination="${2:-}"; shift 2 ;;
    --secret-dir) secret_dir="${2:-}"; shift 2 ;;
    --snapshot-id) snapshot_id="${2:-}"; shift 2 ;;
    *) usage ;;
  esac
done

[[ $EUID -eq 0 ]] || { echo "Run as root." >&2; exit 1; }
[[ -n "$backup" && -n "$recipient" ]] || usage
[[ -z "$snapshot_id" || "$snapshot_id" =~ ^[A-Za-z0-9_-]{1,128}$ ]] || usage
command -v gpg >/dev/null || { echo "GnuPG is required." >&2; exit 1; }

backup=$(readlink -f -- "$backup")
secret_dir=$(readlink -f -- "$secret_dir")
destination=$(readlink -m -- "$destination")
[[ -d "$backup" && "$backup" != / ]] || { echo "The backup directory is missing." >&2; exit 1; }
[[ "$destination" == /srv/hypershell/exports || \
   "$destination" == /srv/hypershell/exports/* || \
   "$destination" == /opt/hypershell/exports || \
   "$destination" == /opt/hypershell/exports/* ]] || {
  echo "The encrypted export destination is outside the approved roots." >&2
  exit 1
}
(cd "$backup" && sha256sum -c SHA256SUMS)
for file in data-protection.pfx data-protection.password ssh-vault-master-key.bin; do
  [[ -f "$secret_dir/$file" ]] || { echo "Recovery secret '$file' is missing." >&2; exit 1; }
done
gpg --batch --list-keys "$recipient" >/dev/null 2>&1 || {
  echo "The configured GPG recovery recipient is not installed." >&2
  exit 1
}
latest_evidence=$(find "$backup" -maxdepth 1 -type f -name 'restore-evidence-*.json' -printf '%T@|%p\n' | sort -nr | head -n 1 | cut -d'|' -f2-)
[[ -n "$latest_evidence" ]] || {
  echo "Validate this backup with restore-profile-server.sh before exporting it." >&2
  exit 1
}
restore_at=$(sed -n 's/.*"validatedAtUtc": "\([0-9TZ:+-]*\)".*/\1/p' "$latest_evidence" | head -n 1)
[[ "$restore_at" =~ ^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9:]{8}Z$ ]] || {
  echo "The newest restore evidence is malformed." >&2
  exit 1
}

stamp=$(date -u +%Y%m%dT%H%M%SZ)
id="hypershell-recovery-$stamp"
file_name="$id.tar.gz.gpg"
manifest_name="$id.manifest.json"
install -d -m 0750 "$destination"
chown root:1654 "$destination"
temporary="$destination/.$file_name.$$.tmp"
trap 'rm -f -- "$temporary"' EXIT

tar --xattrs --acls --numeric-owner -czf - \
  -C "$(dirname "$backup")" "$(basename "$backup")" \
  -C "$(dirname "$secret_dir")" "$(basename "$secret_dir")" | \
  gpg --batch --yes --trust-model always \
    --recipient "$recipient" --encrypt --output "$temporary"
chown root:1654 "$temporary"
chmod 0640 "$temporary"
mv "$temporary" "$destination/$file_name"
trap - EXIT

sha256=$(sha256sum "$destination/$file_name" | awk '{print $1}')
size=$(stat -c '%s' "$destination/$file_name")
created_at=$(date -u +%Y-%m-%dT%H:%M:%SZ)
snapshot_json="null"
if [[ -n "$snapshot_id" ]]; then
  snapshot_json="\"$snapshot_id\""
fi
cat > "$destination/$manifest_name" <<EOF
{
  "schemaVersion": 1,
  "id": "$id",
  "createdAtUtc": "$created_at",
  "fileName": "$file_name",
  "size": $size,
  "sha256": "$sha256",
  "restoreValidatedAtUtc": "$restore_at",
  "snapshotId": $snapshot_json
}
EOF
chown root:1654 "$destination/$manifest_name"
chmod 0640 "$destination/$manifest_name"
echo "HYPERSHELL_EXPORT_OK|id:$id|ciphertext:$destination/$file_name|manifest:$destination/$manifest_name|sha256:$sha256"
