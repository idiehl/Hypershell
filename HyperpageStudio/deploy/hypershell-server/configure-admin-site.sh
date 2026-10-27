#!/usr/bin/env bash
set -euo pipefail

mode="${1:-}"
source_config="${2:-}"
if [[ $# -ne 2 || ( "$mode" != bootstrap && "$mode" != activate ) ]]; then
  echo "Usage: configure-admin-site.sh <bootstrap|activate> CONFIG_PATH" >&2
  exit 2
fi
[[ $EUID -eq 0 ]] || { echo "Run as root." >&2; exit 1; }
for command in \
  nginx systemctl curl openssl sha256sum install mktemp \
  awk chmod cp date grep ln mv readlink rm seq sleep; do
  command -v "$command" >/dev/null || {
    echo "Required command '$command' is unavailable." >&2
    exit 1
  }
done
[[ -f "$source_config" && ! -L "$source_config" ]] || {
  echo "The Nginx source must be a regular, non-symlink file." >&2
  exit 1
}

target="/etc/nginx/sites-available/admin.atlasuniversalis.com"
enabled="/etc/nginx/sites-enabled/admin.atlasuniversalis.com"
rollback_root="/etc/nginx/hypershell-rollbacks"
evidence_root="/var/lib/hypershell-deployments"
stamp="$(date -u +%Y%m%dT%H%M%SZ)-$$"
backup_dir="$rollback_root/admin-site-$stamp"
evidence="$evidence_root/admin-site-$stamp.env"
stage=""
evidence_stage=""
rollback_required=false
had_available=false
had_enabled=false
previous_enabled_target=""

grep -Fq 'server_name admin.atlasuniversalis.com;' "$source_config" || {
  echo "The source is not the dedicated Hypershell admin host." >&2
  exit 1
}
grep -Fq 'location ^~ /.well-known/acme-challenge/' "$source_config" || {
  echo "The admin host must preserve the ACME webroot." >&2
  exit 1
}
case "$mode" in
  bootstrap)
    ! grep -Eq '^[[:space:]]*listen[[:space:]].*443' "$source_config" || {
      echo "Bootstrap mode refuses a TLS listener." >&2
      exit 1
    }
    grep -Fq 'return 404;' "$source_config" || {
      echo "Bootstrap mode must keep the portal unavailable over HTTP." >&2
      exit 1
    }
    ;;
  activate)
    grep -Fq 'listen 443 ssl;' "$source_config" || {
      echo "Activation requires the dedicated TLS listener." >&2
      exit 1
    }
    grep -Fq 'proxy_pass http://hypershell_profile_backend;' \
      "$source_config" || {
      echo "Activation requires the pinned Hypershell backend upstream." >&2
      exit 1
    }
    [[ -f /etc/nginx/conf.d/hypershell-profile-upstream.conf ]] || {
      echo "The Hypershell backend upstream is not installed." >&2
      exit 1
    }
    certificate=/etc/letsencrypt/live/admin.atlasuniversalis.com/fullchain.pem
    private_key=/etc/letsencrypt/live/admin.atlasuniversalis.com/privkey.pem
    [[ -f "$certificate" && -f "$private_key" ]] || {
      echo "The dedicated admin certificate is not installed." >&2
      exit 1
    }
    openssl x509 -in "$certificate" -noout -checkend 0 >/dev/null || {
      echo "The dedicated admin certificate is expired." >&2
      exit 1
    }
    openssl x509 -in "$certificate" -noout \
      -checkhost admin.atlasuniversalis.com >/dev/null || {
      echo "The certificate does not cover admin.atlasuniversalis.com." >&2
      exit 1
    }
    ;;
esac

if [[ -e "$target" && ( ! -f "$target" || -L "$target" ) ]]; then
  echo "Refusing to replace a non-regular Nginx site definition." >&2
  exit 1
fi
if [[ -e "$enabled" || -L "$enabled" ]]; then
  [[ -L "$enabled" ]] || {
    echo "Refusing to replace a non-symlink enabled-site entry." >&2
    exit 1
  }
  had_enabled=true
  previous_enabled_target=$(readlink -- "$enabled")
fi

install -d -m 0755 /etc/nginx/sites-available /etc/nginx/sites-enabled
install -d -m 0700 "$rollback_root" "$evidence_root" "$backup_dir"
if [[ -f "$target" ]]; then
  had_available=true
  cp -a -- "$target" "$backup_dir/available.conf"
fi
if $had_enabled; then
  printf '%s\n' "$previous_enabled_target" > "$backup_dir/enabled-target.txt"
  chmod 0600 "$backup_dir/enabled-target.txt"
fi

rollback_on_exit() {
  local status=$?
  trap - EXIT
  if [[ -n "$stage" ]]; then
    rm -f -- "$stage"
  fi
  if [[ -n "$evidence_stage" ]]; then
    rm -f -- "$evidence_stage"
  fi
  if (( status != 0 )) && $rollback_required; then
    set +e
    if $had_available; then
      cp -a -- "$backup_dir/available.conf" "$target"
    else
      rm -f -- "$target"
    fi
    if $had_enabled; then
      ln -sfn -- "$previous_enabled_target" "$enabled"
    else
      rm -f -- "$enabled"
    fi
    nginx -t >/dev/null 2>&1 && systemctl reload nginx
  fi
  exit "$status"
}
trap rollback_on_exit EXIT

wait_for_bootstrap() {
  local status=""
  for _ in $(seq 1 30); do
    status=$(curl --silent --show-error --noproxy '*' \
      --output /dev/null --write-out '%{http_code}' \
      --header 'Host: admin.atlasuniversalis.com' \
      http://127.0.0.1/ 2>/dev/null || true)
    [[ "$status" == 404 ]] && return 0
    sleep 0.2
  done
  echo "The HTTP bootstrap host returned ${status:-no response} instead of 404." >&2
  return 1
}

wait_for_portal() {
  local portal=""
  for _ in $(seq 1 30); do
    if portal=$(curl --fail --silent --show-error --noproxy '*' \
        --resolve admin.atlasuniversalis.com:443:127.0.0.1 \
        https://admin.atlasuniversalis.com/Account/Login 2>/dev/null) \
        && grep -Fq 'Hypershell Admin' <<<"$portal"; then
      return 0
    fi
    sleep 0.2
  done
  echo "The private admin login did not pass its loopback smoke test." >&2
  return 1
}

rollback_required=true
stage=$(mktemp /etc/nginx/sites-available/.hypershell-admin.XXXXXX)
install -m 0644 -- "$source_config" "$stage"
mv -f -- "$stage" "$target"
stage=""
ln -sfn -- "$target" "$enabled"
nginx -t
systemctl reload nginx
systemctl is-active --quiet nginx

if [[ "$mode" == bootstrap ]]; then
  wait_for_bootstrap
else
  wait_for_portal
fi

source_sha256=$(sha256sum -- "$source_config" | awk '{print $1}')
installed_sha256=$(sha256sum -- "$target" | awk '{print $1}')
[[ "$source_sha256" == "$installed_sha256" ]] || {
  echo "The installed Nginx site does not match its reviewed source." >&2
  exit 1
}
evidence_stage=$(mktemp "$evidence_root/.admin-site.XXXXXX")
{
  printf 'HYPERSHELL_ADMIN_SITE_MODE=%q\n' "$mode"
  printf 'HYPERSHELL_ADMIN_SITE_SOURCE=%q\n' "$(readlink -f -- "$source_config")"
  printf 'HYPERSHELL_ADMIN_SITE_SHA256=%q\n' "$installed_sha256"
  printf 'HYPERSHELL_ADMIN_SITE_BACKUP=%q\n' "$backup_dir"
  printf 'HYPERSHELL_ADMIN_SITE_CONFIG=%q\n' "$target"
  printf 'HYPERSHELL_ADMIN_SITE_ENABLED=%q\n' "$enabled"
  printf 'HYPERSHELL_ADMIN_SITE_APPLIED_AT=%q\n' "$stamp"
} > "$evidence_stage"
chmod 0600 "$evidence_stage"
mv -f -- "$evidence_stage" "$evidence"

rollback_required=false
trap - EXIT
echo "HYPERSHELL_ADMIN_SITE_READY|mode:$mode|sha256:$installed_sha256|evidence:$evidence"
