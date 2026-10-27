#!/usr/bin/env bash
set -euo pipefail

[[ $# -eq 0 ]] || {
  echo "Usage: provision-admin-tls.sh" >&2
  exit 2
}
[[ $EUID -eq 0 ]] || { echo "Run as root." >&2; exit 1; }

for command in \
  awk certbot chmod curl date dig dirname find flock grep install mktemp mv \
  openssl python3 rm sed sha256sum stat; do
  command -v "$command" >/dev/null || {
    echo "Required command '$command' is unavailable." >&2
    exit 1
  }
done

script_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)
domain=admin.atlasuniversalis.com
expected_a=167.71.179.90
expected_aaaa=2604:a880:800:14:0:1:aa80:1000
webroot=/var/www/letsencrypt
challenge_root="$webroot/.well-known/acme-challenge"
certificate=/etc/letsencrypt/live/$domain/fullchain.pem
private_key=/etc/letsencrypt/live/$domain/privkey.pem
account_root=/etc/letsencrypt/accounts
hook_source="$script_root/certbot-reload-nginx.sh"
hook_root=/etc/letsencrypt/renewal-hooks/deploy
hook_target="$hook_root/hypershell-reload-nginx.sh"
http_config="$script_root/../profile-server/nginx-admin-http.conf"
https_config="$script_root/../profile-server/nginx-admin-https.conf"
activator="$script_root/configure-admin-site.sh"
evidence_root=/var/lib/hypershell-deployments
lock_file=/run/lock/hypershell-admin-tls.lock
challenge_stage=""
challenge_path=""
evidence_stage=""
hook_stage=""

for source_file in "$hook_source" "$http_config" "$https_config" "$activator"; do
  [[ -f "$source_file" && ! -L "$source_file" ]] || {
    echo "Required reviewed source is missing or symbolic: $source_file" >&2
    exit 1
  }
done
[[ -x "$activator" ]] || {
  echo "The reviewed admin-site activator is not executable." >&2
  exit 1
}

install -d -m 0755 /run/lock
exec 9>"$lock_file"
flock -n 9 || {
  echo "Another admin TLS provisioning run is active." >&2
  exit 1
}

cleanup() {
  local status=$?
  trap - EXIT
  [[ -z "$challenge_stage" ]] || rm -f -- "$challenge_stage"
  [[ -z "$challenge_path" ]] || rm -f -- "$challenge_path"
  [[ -z "$evidence_stage" ]] || rm -f -- "$evidence_stage"
  [[ -z "$hook_stage" ]] || rm -f -- "$hook_stage"
  exit "$status"
}
trap cleanup EXIT

[[ -d "$challenge_root" && ! -L "$challenge_root" ]] || {
  echo "The ACME challenge root is missing or symbolic: $challenge_root" >&2
  exit 1
}
[[ "$(stat -c '%U:%G' "$challenge_root")" == root:root ]] || {
  echo "The ACME challenge root must be owned by root:root." >&2
  exit 1
}
challenge_mode=$(stat -c '%a' "$challenge_root")
(( (8#$challenge_mode & 8#022) == 0 )) || {
  echo "The ACME challenge root must not be group- or world-writable." >&2
  exit 1
}

account_found=false
while IFS= read -r account; do
  if [[ -n "$account" ]]; then
    account_found=true
    break
  fi
done < <(find "$account_root" -type f -name regr.json -print 2>/dev/null)
$account_found || {
  echo "No existing Certbot account is available for noninteractive issuance." >&2
  exit 1
}

normalize_ip() {
  python3 - "$1" <<'PY'
import ipaddress
import sys

try:
    print(ipaddress.ip_address(sys.argv[1]))
except ValueError:
    raise SystemExit(1)
PY
}

verify_dns_record() {
  local record_type=$1
  local expected=$2
  local raw=""
  local line=""
  local actual=""
  local normalized_expected=""
  local -a records=()

  raw=$(dig +time=5 +tries=2 +short "$record_type" "$domain") || {
    echo "DNS lookup for $domain $record_type failed." >&2
    return 1
  }
  while IFS= read -r line; do
    [[ -z "$line" ]] || records+=("$line")
  done <<<"$raw"
  [[ ${#records[@]} -eq 1 ]] || {
    echo "$domain must have exactly one $record_type answer; found ${#records[@]}." >&2
    return 1
  }
  actual=$(normalize_ip "${records[0]}") || {
    echo "$domain returned a non-address $record_type answer." >&2
    return 1
  }
  normalized_expected=$(normalize_ip "$expected")
  [[ "$actual" == "$normalized_expected" ]] || {
    echo "$domain $record_type resolves to $actual; expected $normalized_expected." >&2
    return 1
  }
}

verify_dns_record A "$expected_a"
verify_dns_record AAAA "$expected_aaaa"

challenge_name="hypershell-$(openssl rand -hex 16)"
challenge_value=$(openssl rand -hex 32)
challenge_path="$challenge_root/$challenge_name"
challenge_stage=$(mktemp "$challenge_root/.hypershell-admin.XXXXXX")
printf '%s\n' "$challenge_value" > "$challenge_stage"
chmod 0644 "$challenge_stage"
mv -f -- "$challenge_stage" "$challenge_path"
challenge_stage=""
challenge_url="http://$domain/.well-known/acme-challenge/$challenge_name"

fetch_challenge() {
  local family=$1
  shift
  local response=""
  response=$(curl --fail --silent --show-error --noproxy '*' \
    --connect-timeout 10 --max-time 20 "$family" "$@" "$challenge_url") || {
    echo "The ACME marker was not reachable through $family." >&2
    return 1
  }
  [[ "$response" == "$challenge_value" ]] || {
    echo "The ACME marker returned unexpected content through $family." >&2
    return 1
  }
}

fetch_challenge --ipv4 --resolve "$domain:80:127.0.0.1"
fetch_challenge --ipv4
fetch_challenge --ipv6

certbot certonly \
  --webroot \
  --webroot-path "$webroot" \
  --cert-name "$domain" \
  --domains "$domain" \
  --non-interactive \
  --agree-tos \
  --keep-until-expiring

[[ -f "$certificate" && -f "$private_key" ]] || {
  echo "Certbot did not install the dedicated admin certificate." >&2
  exit 1
}
openssl x509 -in "$certificate" -noout -checkend 604800 >/dev/null || {
  echo "The dedicated admin certificate expires in fewer than seven days." >&2
  exit 1
}
openssl x509 -in "$certificate" -noout -checkhost "$domain" >/dev/null || {
  echo "The dedicated certificate does not cover $domain." >&2
  exit 1
}

if [[ -e "$hook_root" || -L "$hook_root" ]]; then
  [[ -d "$hook_root" && ! -L "$hook_root" ]] || {
    echo "The Certbot deploy-hook directory is unsafe." >&2
    exit 1
  }
  [[ "$(stat -c '%U:%G' "$hook_root")" == root:root ]] || {
    echo "The Certbot deploy-hook directory must be owned by root:root." >&2
    exit 1
  }
  hook_mode=$(stat -c '%a' "$hook_root")
  (( (8#$hook_mode & 8#022) == 0 )) || {
    echo "The Certbot deploy-hook directory must not be group- or world-writable." >&2
    exit 1
  }
else
  install -d -o root -g root -m 0755 "$hook_root"
fi
hook_sha256=$(sha256sum -- "$hook_source" | awk '{print $1}')
if [[ -e "$hook_target" || -L "$hook_target" ]]; then
  [[ -f "$hook_target" && ! -L "$hook_target" ]] || {
    echo "Refusing to replace an unsafe Certbot deploy-hook target." >&2
    exit 1
  }
  installed_hook_sha256=$(sha256sum -- "$hook_target" | awk '{print $1}')
  [[ "$installed_hook_sha256" == "$hook_sha256" ]] || {
    echo "A different Certbot deploy hook already occupies $hook_target." >&2
    exit 1
  }
fi
hook_stage=$(mktemp "$hook_root/.hypershell.XXXXXX")
install -m 0755 -o root -g root -- "$hook_source" "$hook_stage"
mv -f -- "$hook_stage" "$hook_target"
hook_stage=""

activation_output=$("$activator" activate "$https_config")
grep -Fq 'HYPERSHELL_ADMIN_SITE_READY|mode:activate|' \
  <<<"$activation_output" || {
    echo "The admin-site activator did not return its readiness proof." >&2
    exit 1
  }
printf '%s\n' "$activation_output"

verify_portal() {
  local family=$1
  local portal=""
  portal=$(curl --fail --silent --show-error --noproxy '*' \
    --connect-timeout 10 --max-time 20 "$family" \
    "https://$domain/Account/Login") || return 1
  grep -Fq 'Hypershell Admin' <<<"$portal"
}

if ! verify_portal --ipv4 || ! verify_portal --ipv6; then
  echo "Public HTTPS verification failed; restoring the HTTP-only bootstrap." >&2
  "$activator" bootstrap "$http_config" >&2 || {
    echo "CRITICAL: automatic HTTP-bootstrap restoration failed." >&2
    exit 1
  }
  exit 1
fi

certificate_sha256=$(openssl x509 -in "$certificate" -outform DER |
  sha256sum | awk '{print $1}')
certificate_expiry=$(openssl x509 -in "$certificate" -noout -enddate |
  sed 's/^notAfter=//')
stamp=$(date -u +%Y%m%dT%H%M%SZ)-$$
install -d -m 0700 "$evidence_root"
evidence="$evidence_root/admin-tls-$stamp.env"
evidence_stage=$(mktemp "$evidence_root/.admin-tls.XXXXXX")
{
  printf 'HYPERSHELL_ADMIN_TLS_DOMAIN=%q\n' "$domain"
  printf 'HYPERSHELL_ADMIN_TLS_A=%q\n' "$expected_a"
  printf 'HYPERSHELL_ADMIN_TLS_AAAA=%q\n' "$expected_aaaa"
  printf 'HYPERSHELL_ADMIN_TLS_CERTIFICATE_SHA256=%q\n' "$certificate_sha256"
  printf 'HYPERSHELL_ADMIN_TLS_CERTIFICATE_EXPIRY=%q\n' "$certificate_expiry"
  printf 'HYPERSHELL_ADMIN_TLS_RENEWAL_HOOK_SHA256=%q\n' "$hook_sha256"
  printf 'HYPERSHELL_ADMIN_TLS_PROVISIONED_AT=%q\n' "$stamp"
} > "$evidence_stage"
chmod 0600 "$evidence_stage"
mv -f -- "$evidence_stage" "$evidence"
evidence_stage=""

trap - EXIT
rm -f -- "$challenge_path"
challenge_path=""
echo "HYPERSHELL_ADMIN_TLS_READY|domain:$domain|certificate-sha256:$certificate_sha256|evidence:$evidence"
