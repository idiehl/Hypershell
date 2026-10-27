#!/usr/bin/env bash
set -euo pipefail

[[ $EUID -eq 0 ]] || { echo "Run as root." >&2; exit 1; }
for command in nginx systemctl; do
  command -v "$command" >/dev/null || {
    echo "Required command '$command' is unavailable." >&2
    exit 1
  }
done

nginx -t
systemctl reload nginx
systemctl is-active --quiet nginx
echo 'HYPERSHELL_CERTBOT_NGINX_RELOADED'
