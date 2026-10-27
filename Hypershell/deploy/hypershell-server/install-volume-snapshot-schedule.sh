#!/usr/bin/env bash
set -euo pipefail

config_file="${1:-/etc/hypershell-profile-server/digitalocean.env}"
[[ $# -le 1 ]] || {
  echo "Usage: install-volume-snapshot-schedule.sh [CONFIG_FILE]" >&2
  exit 2
}
[[ $EUID -eq 0 ]] || { echo "Run as root." >&2; exit 1; }

script_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
stable_root="/opt/hypershell/profile-server/deploy/hypershell-server"
install_managed_file() {
  local mode="$1"
  local source="$2"
  local destination="$3"

  if [[ -e "$destination" && "$source" -ef "$destination" ]]; then
    chmod "$mode" "$destination"
    return
  fi

  install -m "$mode" "$source" "$destination"
}

for file in \
  backup-profile-server.sh \
  snapshot-digitalocean-volume.sh \
  verify-digitalocean-volume.sh; do
  [[ -f "$script_root/$file" ]] || {
    echo "Required deployment file '$file' is missing." >&2
    exit 1
  }
done
for unit in hypershell-volume-snapshot.service hypershell-volume-snapshot.timer; do
  [[ -f "$script_root/systemd/$unit" ]] || {
    echo "Required systemd unit '$unit' is missing." >&2
    exit 1
  }
done

"$script_root/verify-digitalocean-volume.sh" "$config_file"
[[ "$(readlink -f -- "$config_file")" == \
   /etc/hypershell-profile-server/digitalocean.env ]] || {
  echo "The timer requires the protected canonical configuration path." >&2
  exit 1
}

install -d -m 0755 "$stable_root/systemd"
for file in \
  backup-profile-server.sh \
  snapshot-digitalocean-volume.sh \
  verify-digitalocean-volume.sh; do
  install_managed_file 0755 "$script_root/$file" "$stable_root/$file"
done
install_managed_file 0644 \
  "$script_root/systemd/hypershell-volume-snapshot.service" \
  /etc/systemd/system/hypershell-volume-snapshot.service
install_managed_file 0644 \
  "$script_root/systemd/hypershell-volume-snapshot.timer" \
  /etc/systemd/system/hypershell-volume-snapshot.timer
systemd-analyze verify \
  /etc/systemd/system/hypershell-volume-snapshot.service \
  /etc/systemd/system/hypershell-volume-snapshot.timer
systemctl daemon-reload
systemctl enable --now hypershell-volume-snapshot.timer
[[ "$(systemctl is-enabled hypershell-volume-snapshot.timer)" == enabled ]] || {
  echo "The Hypershell snapshot timer did not enable." >&2
  exit 1
}
systemctl list-timers hypershell-volume-snapshot.timer --no-pager
echo "HYPERSHELL_SNAPSHOT_SCHEDULE_READY|timer:hypershell-volume-snapshot.timer|config:$config_file"
