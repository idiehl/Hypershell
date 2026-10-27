#!/usr/bin/env bash
set -euo pipefail

secret_dir="${1:-/etc/hypershell-profile-server/secrets}"
certificate_path="$secret_dir/data-protection.pfx"
password_path="$secret_dir/data-protection.password"
ssh_vault_key_path="$secret_dir/ssh-vault-master-key.bin"
container_gid="${HYPERSHELL_CONTAINER_GID:-1654}"

if [[ "${EUID}" -ne 0 ]]; then
  echo "Run this command as root." >&2
  exit 1
fi

if [[ ! "$container_gid" =~ ^[0-9]+$ ]]; then
  echo "HYPERSHELL_CONTAINER_GID must be a numeric group ID." >&2
  exit 1
fi

if [[ -e "$certificate_path" && ! -e "$password_path" ]] || \
   [[ ! -e "$certificate_path" && -e "$password_path" ]]; then
  echo "Data-protection certificate state is incomplete in $secret_dir." >&2
  exit 1
fi

install -d -m 0700 -o root -g root "$secret_dir"
if [[ ! -e "$certificate_path" ]]; then
  temporary_dir="$(mktemp -d)"
  trap 'rm -f -- "$temporary_dir/private-key.pem" "$temporary_dir/certificate.pem"; rmdir -- "$temporary_dir"' EXIT

  openssl rand -base64 -out "$password_path" 48
  chown root:"$container_gid" "$password_path"
  chmod 0440 "$password_path"
  openssl req -x509 -newkey rsa:3072 -sha256 -nodes -days 3650 \
    -subj "/CN=Hypershell Data Protection" \
    -keyout "$temporary_dir/private-key.pem" \
    -out "$temporary_dir/certificate.pem"
  openssl pkcs12 -export \
    -inkey "$temporary_dir/private-key.pem" \
    -in "$temporary_dir/certificate.pem" \
    -out "$certificate_path" \
    -passout "file:$password_path"
  chown root:"$container_gid" "$certificate_path"
  chmod 0440 "$certificate_path"
  echo "Created protected ASP.NET key-ring certificate material in $secret_dir."
else
  echo "Preserving existing ASP.NET key-ring certificate material in $secret_dir."
fi

if [[ ! -e "$ssh_vault_key_path" ]]; then
  umask 077
  openssl rand -out "$ssh_vault_key_path" 32
  chown root:"$container_gid" "$ssh_vault_key_path"
  chmod 0440 "$ssh_vault_key_path"
  echo "Created the external SSH credential-vault master key."
else
  if [[ ! -f "$ssh_vault_key_path" ]] || \
     [[ "$(wc -c < "$ssh_vault_key_path")" -ne 32 ]]; then
    echo "The existing SSH vault master-key file is not a regular 32-byte file." >&2
    exit 1
  fi
  echo "Preserving the existing SSH credential-vault master key."
fi

echo "Back up the certificate, password, and SSH vault key separately from the application volume."
