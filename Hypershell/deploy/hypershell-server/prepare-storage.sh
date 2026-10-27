#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat >&2 <<'EOF'
Usage: prepare-storage.sh --device /dev/disk/by-id/DEVICE [--mount /srv/hypershell] [--runtime-gid 1654] [--format-empty-device]

The script refuses to format a device unless --format-empty-device is present,
the target is a 45-55 GiB block device, it has no filesystem signature or child
partitions, and it is not mounted. Run lsblk before invoking it and use a stable
/dev/disk/by-id path.
EOF
  exit 2
}

device=""
mount_point="/srv/hypershell"
runtime_gid=1654
allow_format=0
while (($#)); do
  case "$1" in
    --device) device="${2:-}"; shift 2 ;;
    --mount) mount_point="${2:-}"; shift 2 ;;
    --runtime-gid) runtime_gid="${2:-}"; shift 2 ;;
    --format-empty-device) allow_format=1; shift ;;
    *) usage ;;
  esac
done

[[ $EUID -eq 0 ]] || { echo "Run as root." >&2; exit 1; }
for command in \
  awk blkid blockdev chmod chown cp date df find findmnt getent grep head install lsblk \
  mkfs.ext4 mount readlink stat umount wc; do
  command -v "$command" >/dev/null || {
    echo "Required command '$command' is unavailable." >&2
    exit 1
  }
done
[[ "$device" == /dev/disk/by-id/* && -b "$device" ]] || usage
[[ "$runtime_gid" =~ ^[0-9]+$ && "$runtime_gid" -ge 1 ]] || usage
getent group "$runtime_gid" >/dev/null || {
  echo "Host group for runtime GID $runtime_gid is missing; create the container ownership mapping before preparing storage." >&2
  exit 1
}
[[ "$mount_point" == /srv/hypershell ]] || {
  echo "The approved mount point is /srv/hypershell." >&2
  exit 1
}

resolved_device=$(readlink -f -- "$device")
root_source=$(readlink -f -- "$(findmnt -nro SOURCE /)")
root_parent=$(lsblk -ndo PKNAME "$root_source" | head -n 1)
root_device=$root_source
if [[ -n "$root_parent" ]]; then
  root_device=$(readlink -f -- "/dev/$root_parent")
fi
[[ "$resolved_device" != "$root_source" && "$resolved_device" != "$root_device" ]] || {
  echo "Refusing to operate on the root disk." >&2
  exit 1
}
[[ "$(lsblk -ndo TYPE "$resolved_device")" == disk ]] || {
  echo "The selected path does not resolve to a whole disk." >&2
  exit 1
}
[[ "$(lsblk -nrpo NAME "$resolved_device" | wc -l)" -eq 1 ]] || {
  echo "The selected device has child partitions; refusing to modify it." >&2
  exit 1
}
size_bytes=$(blockdev --getsize64 "$resolved_device")
minimum_bytes=$((45 * 1024 * 1024 * 1024))
maximum_bytes=$((55 * 1024 * 1024 * 1024))
[[ "$size_bytes" -ge "$minimum_bytes" && "$size_bytes" -le "$maximum_bytes" ]] || {
  echo "Expected the approved 50 GB volume, but the device is $size_bytes bytes." >&2
  exit 1
}
if lsblk -nrpo MOUNTPOINTS "$resolved_device" | grep -q '[^[:space:]]'; then
  echo "The device or one of its children is already mounted; inspect it manually." >&2
  exit 1
fi

[[ -f /etc/fstab && ! -L /etc/fstab \
   && "$(readlink -f -- /etc/fstab)" == /etc/fstab \
   && "$(stat -c '%u:%g' /etc/fstab)" == 0:0 ]] || {
  echo "/etc/fstab is missing, symbolic, or not owned by root:root." >&2
  exit 1
}
fstab_mode=$(stat -c '%a' /etc/fstab)
(( (8#$fstab_mode & 0022) == 0 )) || {
  echo "/etc/fstab cannot be writable by group or other users." >&2
  exit 1
}
if [[ -e "$mount_point" || -L "$mount_point" ]]; then
  [[ -d "$mount_point" && ! -L "$mount_point" \
     && "$(readlink -f -- "$mount_point")" == "$mount_point" \
     && "$(stat -c '%u' "$mount_point")" == 0 ]] || {
    echo "The approved mount point already exists in an unsafe form." >&2
    exit 1
  }
  mount_entry_count=$(find "$mount_point" \
    -mindepth 1 -maxdepth 1 -printf '.' | wc -c)
  if [[ "$mount_entry_count" -ne 0 ]]; then
    [[ "$mount_entry_count" -eq 1 \
       && -d "$mount_point/exports" \
       && ! -L "$mount_point/exports" \
       && "$(stat -c '%u' "$mount_point/exports")" == 0 \
       && -z "$(find "$mount_point/exports" -mindepth 1 -print -quit)" ]] || {
      echo "The approved mount point contains data or unexpected entries; refusing to hide them beneath a new filesystem." >&2
      exit 1
    }
  fi
fi

filesystem=$(blkid -s TYPE -o value "$resolved_device" 2>/dev/null || true)
existing_mount=$(awk -v target="$mount_point" '$2 == target { print }' /etc/fstab)
if [[ -z "$filesystem" ]]; then
  [[ -z "$existing_mount" ]] || {
    echo "An existing /etc/fstab entry owns the mount point before the device has a filesystem." >&2
    exit 1
  }
  [[ $allow_format -eq 1 ]] || {
    echo "The device is empty. Re-run with --format-empty-device after verifying its identity." >&2
    exit 1
  }
  mkfs.ext4 -L hypershell-data "$resolved_device"
  filesystem=$(blkid -s TYPE -o value "$resolved_device" 2>/dev/null || true)
fi
[[ "$filesystem" == ext4 ]] || {
  echo "Expected ext4 but found $filesystem; refusing to modify it." >&2
  exit 1
}

uuid=$(blkid -s UUID -o value "$resolved_device")
[[ "$uuid" =~ ^[0-9a-fA-F-]{36}$ ]] || {
  echo "The ext4 filesystem returned an invalid UUID." >&2
  exit 1
}
expected_mount="UUID=$uuid $mount_point ext4 defaults,nofail,discard,noatime,x-systemd.device-timeout=30 0 2"
if [[ -n "$existing_mount" && "$existing_mount" != "$expected_mount" ]]; then
  echo "An unexpected /etc/fstab entry already owns $mount_point; refusing to replace it." >&2
  exit 1
fi
install -d -m 0750 -o root -g "$runtime_gid" "$mount_point"
if [[ -z "$existing_mount" ]]; then
  install -d -m 0700 /var/lib/hypershell-deployments
  [[ ! -L /var/lib/hypershell-deployments \
     && "$(readlink -f -- /var/lib/hypershell-deployments)" == \
        /var/lib/hypershell-deployments \
     && "$(stat -c '%u:%g' /var/lib/hypershell-deployments)" == 0:0 ]] || {
    echo "The deployment evidence root is unsafe." >&2
    exit 1
  }
  fstab_backup="/var/lib/hypershell-deployments/fstab-before-storage-$(date -u +%Y%m%dT%H%M%S%NZ)-$$"
  [[ ! -e "$fstab_backup" && ! -L "$fstab_backup" ]] || {
    echo "The generated /etc/fstab backup path already exists." >&2
    exit 1
  }
  cp --preserve=all /etc/fstab "$fstab_backup"
  printf '%s\n' "$expected_mount" >> /etc/fstab
  if ! findmnt --verify --verbose >/dev/null; then
    cp --preserve=all "$fstab_backup" /etc/fstab
    echo "The new /etc/fstab entry failed validation and was restored." >&2
    exit 1
  fi
fi
if ! mount "$mount_point"; then
  if [[ -n "${fstab_backup:-}" ]]; then
    cp --preserve=all "$fstab_backup" /etc/fstab
  fi
  echo "The volume failed to mount; the previous /etc/fstab was restored." >&2
  exit 1
fi
mounted_target=$(findmnt -nro TARGET -T "$mount_point")
mounted_source=$(readlink -f -- "$(findmnt -nro SOURCE -M "$mount_point")")
if [[ "$mounted_target" != "$mount_point" \
      || "$mounted_source" != "$resolved_device" ]]; then
  umount "$mount_point" >/dev/null 2>&1 || true
  if [[ -n "${fstab_backup:-}" ]]; then
    cp --preserve=all "$fstab_backup" /etc/fstab
  fi
  echo "The prepared filesystem did not mount from the approved device at $mount_point." >&2
  exit 1
fi

chown root:"$runtime_gid" "$mount_point"
chmod 0750 "$mount_point"
for directory in server exports; do
  install -d -m 0750 -o root -g "$runtime_gid" "$mount_point/$directory"
done
for directory in releases packages git workspaces backups; do
  install -d -m 0750 -o root -g root "$mount_point/$directory"
done

echo "HYPERSHELL_STORAGE_READY|device:$resolved_device|uuid:$uuid|mount:$mount_point"
df -h "$mount_point"
