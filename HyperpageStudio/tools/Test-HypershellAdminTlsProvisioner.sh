#!/usr/bin/env bash
set -euo pipefail

[[ "${HYPERSHELL_ADMIN_TLS_FIXTURE_CONTAINER:-}" == 1 \
   && -f /.dockerenv ]] || {
  echo "This admin-TLS fixture may run only inside its disposable container." >&2
  exit 1
}

source_root=/repo/deploy
fixture_root=$(mktemp -d)
fixture_deploy="$fixture_root/deploy"
fixture_server="$fixture_deploy/hypershell-server"
fixture_profile="$fixture_deploy/profile-server"
mock_bin="$fixture_root/bin"
mock_state="$fixture_root/state"
provisioner="$fixture_server/provision-admin-tls.sh"

cleanup() {
  rm -rf --one-file-system -- \
    /etc/letsencrypt /var/www/letsencrypt /var/lib/hypershell-deployments \
    "$fixture_root"
}
trap cleanup EXIT

install -d -m 0755 \
  "$fixture_server" "$fixture_profile" "$mock_bin" "$mock_state" \
  /var/www/letsencrypt/.well-known/acme-challenge \
  /etc/letsencrypt/accounts/acme-v02.api.letsencrypt.org/directory/fixture
install -d -m 0700 /var/lib/hypershell-deployments
touch /etc/letsencrypt/accounts/acme-v02.api.letsencrypt.org/directory/fixture/regr.json
cp "$source_root/hypershell-server/provision-admin-tls.sh" "$fixture_server/"
cp "$source_root/hypershell-server/certbot-reload-nginx.sh" "$fixture_server/"
cp "$source_root/profile-server/nginx-admin-http.conf" "$fixture_profile/"
cp "$source_root/profile-server/nginx-admin-https.conf" "$fixture_profile/"
chmod 0755 "$provisioner" "$fixture_server/certbot-reload-nginx.sh"

cat > "$fixture_server/configure-admin-site.sh" <<'MOCK'
#!/usr/bin/env bash
set -euo pipefail
state=${HYPERSHELL_ADMIN_TLS_STATE:?}
mode=${1:?}
config=${2:?}
[[ -f "$config" ]]
case "$mode" in
  activate)
    [[ -f /etc/letsencrypt/live/admin.atlasuniversalis.com/fullchain.pem ]]
    touch "$state/activated"
    ;;
  bootstrap)
    touch "$state/bootstrapped"
    ;;
  *) exit 71 ;;
esac
echo "HYPERSHELL_ADMIN_SITE_READY|mode:$mode|fixture:true"
MOCK

cat > "$mock_bin/dig" <<'MOCK'
#!/usr/bin/env bash
set -euo pipefail
state=${HYPERSHELL_ADMIN_TLS_STATE:?}
case " $* " in
  *' A '*) printf '167.71.179.90\n' ;;
  *' AAAA '*)
    if [[ -f "$state/bad-aaaa" ]]; then
      printf '2001:db8::9\n'
    else
      printf '2604:a880:800:14::1:aa80:1000\n'
    fi
    ;;
  *) exit 72 ;;
esac
MOCK

cat > "$mock_bin/curl" <<'MOCK'
#!/usr/bin/env bash
set -euo pipefail
state=${HYPERSHELL_ADMIN_TLS_STATE:?}
printf '%s\n' "$*" >> "$state/curl.log"
url=${!#}
if [[ "$url" == http://admin.atlasuniversalis.com/.well-known/acme-challenge/* ]]; then
  if [[ " $* " == *' --ipv6 '* && -f "$state/fail-ipv6-http" ]]; then
    exit 22
  fi
  cat "/var/www/letsencrypt/.well-known/acme-challenge/${url##*/}"
elif [[ "$url" == https://admin.atlasuniversalis.com/Account/Login ]]; then
  if [[ " $* " == *' --ipv6 '* && -f "$state/fail-ipv6-https" ]]; then
    exit 22
  fi
  printf '<title>Hypershell Admin</title>\n'
else
  echo "Unexpected curl fixture URL: $url" >&2
  exit 73
fi
MOCK

cat > "$mock_bin/certbot" <<'MOCK'
#!/usr/bin/env bash
set -euo pipefail
state=${HYPERSHELL_ADMIN_TLS_STATE:?}
printf '%s\n' "$*" > "$state/certbot.args"
cert_root=/etc/letsencrypt/live/admin.atlasuniversalis.com
install -d -m 0700 "$cert_root"
/usr/bin/openssl req -x509 -newkey rsa:2048 -nodes \
  -keyout "$cert_root/privkey.pem" \
  -out "$cert_root/fullchain.pem" \
  -days 90 -subj '/CN=admin.atlasuniversalis.com' \
  -addext 'subjectAltName=DNS:admin.atlasuniversalis.com' >/dev/null 2>&1
MOCK

chmod 0755 "$fixture_server/configure-admin-site.sh" "$mock_bin"/*
export HYPERSHELL_ADMIN_TLS_STATE="$mock_state"
export PATH="$mock_bin:$PATH"

touch "$mock_state/bad-aaaa"
set +e
dns_output=$($provisioner 2>&1)
dns_status=$?
set -e
[[ $dns_status -ne 0 ]]
[[ "$dns_output" == *'AAAA resolves to'* ]]
[[ ! -e "$mock_state/certbot.args" && ! -e "$mock_state/activated" ]]
rm -f "$mock_state/bad-aaaa"

touch "$mock_state/fail-ipv6-http"
set +e
http_output=$($provisioner 2>&1)
http_status=$?
set -e
[[ $http_status -ne 0 ]]
[[ "$http_output" == *'not reachable through --ipv6'* ]]
[[ ! -e "$mock_state/certbot.args" && ! -e "$mock_state/activated" ]]
rm -f "$mock_state/fail-ipv6-http"

success_output=$($provisioner)
[[ "$success_output" == *'HYPERSHELL_ADMIN_TLS_READY|'* ]]
[[ -e "$mock_state/activated" && ! -e "$mock_state/bootstrapped" ]]
grep -Fq -- '--webroot-path /var/www/letsencrypt' "$mock_state/certbot.args"
grep -Fq -- '--cert-name admin.atlasuniversalis.com' "$mock_state/certbot.args"
grep -Fq -- '--domains admin.atlasuniversalis.com' "$mock_state/certbot.args"
grep -Fq -- '--ipv4' "$mock_state/curl.log"
grep -Fq -- '--ipv6' "$mock_state/curl.log"
hook=/etc/letsencrypt/renewal-hooks/deploy/hypershell-reload-nginx.sh
[[ "$(stat -c '%a %U:%G' "$hook")" == '755 root:root' ]]
[[ "$(sha256sum "$hook" | awk '{print $1}')" == \
   "$(sha256sum "$fixture_server/certbot-reload-nginx.sh" | awk '{print $1}')" ]]
find /var/lib/hypershell-deployments -type f -name 'admin-tls-*.env' |
  grep -q .

rm -f "$mock_state/activated" "$mock_state/bootstrapped"
touch "$mock_state/fail-ipv6-https"
set +e
https_output=$($provisioner 2>&1)
https_status=$?
set -e
[[ $https_status -ne 0 ]]
[[ "$https_output" == *'restoring the HTTP-only bootstrap'* ]]
[[ -e "$mock_state/activated" && -e "$mock_state/bootstrapped" ]]

echo 'HYPERSHELL_ADMIN_TLS_FIXTURE_OK|exact-dns|dual-stack-acme|certbot-webroot|hostname-validation|renewal-hook|fail-closed-activation|evidence'
