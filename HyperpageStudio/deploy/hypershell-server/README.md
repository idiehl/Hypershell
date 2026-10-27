# Hypershell server foundation

These scripts prepare the durable storage and rollback boundary for the
expanded Hypershell account service. They are intentionally not invoked by a
normal application deployment.

## 50 GB block volume

The volume may be created and attached through the DigitalOcean control panel,
or through `provision-digitalocean-volume.sh`. For automated provisioning,
place a temporary scoped token in a root-owned `0400` or `0600` file. Give that
token only `block_storage:read`, `block_storage:create`, and
`block_storage_action:create`, then run:

```bash
deploy/hypershell-server/provision-digitalocean-volume.sh \
  --token-file /root/hypershell-volume-provision.token \
  --confirm-create-billed-volume \
  --confirm-attach-to-this-droplet
```

The two confirmation flags independently authorize the recurring 50 GiB
charge and attachment to the current Droplet. The script never formats, mounts,
detaches, resizes, or deletes a volume. Revoke and remove this temporary token
after provisioning; do not reuse it for scheduled snapshots.

1. Inspect the attached device with `lsblk -f` and verify its stable
   `/dev/disk/by-id/...` path.
2. Register the locked host ownership mapping used by the container, if it is
   not already present:

   ```bash
   groupadd --system --gid 1654 hypershell-runtime
   useradd --uid 1654 --gid hypershell-runtime --no-create-home \
     --home-dir /nonexistent --shell /usr/sbin/nologin hypershell-runtime
   ```

3. Copy `prepare-storage.sh` to the host.
4. Run it without `--format-empty-device` first. The expected refusal confirms
   the target is empty.
5. Re-run with `--format-empty-device` only after verifying the exact device.

Before formatting, `prepare-storage.sh` now rejects an unsafe mount-point
object, an unsafe `/etc/fstab`, or any pre-existing conflicting mount entry. It
also requires the host runtime GID mapping before touching the filesystem. It
permits only an empty, root-owned legacy `exports` directory beneath the
pre-mount path; any other content is rejected before it can be hidden.
After mounting, it resolves the live mount source back to the approved whole
disk. A mount-source mismatch is unmounted and the prior `fstab` is restored.

The resulting layout is rooted at `/srv/hypershell` and separates server data,
release artifacts, packages, Git repositories, working trees, exports, and
backups.

Mounting the volume does not move live data by itself. Create a fresh backup on
the mounted volume, then promote the currently running image with an explicit
empty target:

```bash
backup_output=$(deploy/hypershell-server/backup-profile-server.sh \
  /srv/hypershell/backups)
backup_path=${backup_output#HYPERSHELL_BACKUP_OK|}
backup_path=${backup_path%%$'\n'*}
deploy/hypershell-server/promote-profile-server.sh \
  --image "$(docker inspect -f '{{.Config.Image}}' hypershell-profile-server)" \
  --backup "$backup_path" \
  --target-data-root /srv/hypershell/server/profile-data
```

Promotion copies the stopped production source, compares the copy, changes the
container to a bind mount on the block volume, and records both sources in its
cutover evidence. Automatic and named rollback synchronize current data back to
the retained source before the prior container starts. Keep that container,
source, backup, and evidence through the observation period.

## Consistent profile-server backup

`backup-profile-server.sh` briefly stops the current container, archives its
deployment and durable Docker volume, restarts it, and verifies loopback
health. A backup is not considered recoverable until its hashes are checked
and it has been restored into an isolated container and volume.

`provision-data-protection.sh` creates the root-owned certificate and password
used to encrypt the account service's ASP.NET Data Protection key ring plus a
separate 32-byte SSH vault wrapping key. The files are readable only by the
container runtime GID (1654 by default). The script preserves valid existing
material, can add the vault key to an earlier deployment, and refuses partial
or malformed state. Preserve all three files in the encrypted server recovery
bundle, separate from the application volume snapshot.

## Scheduled snapshots and encrypted recovery

`restore-profile-server.sh` proves a backup against an isolated Docker volume.
`export-profile-server.sh` then creates a GPG-encrypted recovery bundle that
includes the verified data plus the external certificate, password, and SSH
vault key. The portal exposes only exports whose strict manifests and
ciphertext hashes still agree.

`snapshot-digitalocean-volume.sh` uses a separate root-only scoped API token to
snapshot the exact configured block-volume UUID and prunes only older
Hypershell snapshots for that same volume. Install the included systemd service
and timer only after verifying the volume ID. The default schedule is weekly
and retains eight snapshots.

Prepare that credential without exposing it in shell history or logs:

```bash
/opt/hypershell/profile-server/deploy/hypershell-server/\
enroll-digitalocean-snapshot-token.sh
```

Paste the token at the hidden prompt. The enroller discovers the exact attached
`hypershell-data` UUID, validates the API identity against the Droplet metadata
and mounted stable device, then atomically installs the configuration with mode
`0400`. Use `--replace` only for a deliberate token rotation.

The protected configuration uses `HYPERSHELL_SNAPSHOT_RETENTION` for the
retention count. The script reads that value only after sourcing the root-only
configuration, so timer executions and manual executions behave identically.
It also records the stable device path as `HYPERSHELL_VOLUME_DEVICE`. Use a
custom token limited to `block_storage:read` and the create, read, and delete
`block_storage_snapshot` scopes. `verify-digitalocean-volume.sh` proves the API
identity, size, region, Droplet attachment, device link, and mount before
`install-volume-snapshot-schedule.sh` enables the timer. Scheduled execution
creates a consistent application backup and flushes the filesystem before the
block-level snapshot request.

## Promotion and rollback

Install `deploy/profile-server/nginx-upstream.conf` before using the cutover
tools. `promote-profile-server.sh` validates a new image against an isolated
restore, preserves the old container, alternates between loopback ports 5088
and 5089, switches Nginx, and emits a named evidence file.
`rollback-profile-server.sh` consumes that exact evidence file and preserves
the failed-forward container for diagnosis.

The first trusted release signer is activated through the same blue-green
path: create a fresh backup and pass `--release-signer-sha256` to promotion.
The old and new public fingerprints are recorded in cutover evidence, and a
named rollback restores the retained container with its original signer.

After copying a stable bundle to
`/srv/hypershell/releases/incoming/<version>`, publish it with
`publish-profile-release.sh`. The publisher requires the mounted block volume,
an exact three-file bundle, the same signer already active in production, and
the canonical profile-data mount. It imports through an isolated no-network
container, checks stable inventory, archives the bundle immutably, and records
root-only evidence. The archive is reconciled to the same latest-three-plus-
pinned inventory as the content-addressed release store, after every existing
entry has passed containment and ownership checks. No signing key is ever sent
to the server.

The complete sequence, credential-rotation boundaries, and two-PC acceptance
matrix are documented in `doc/OPERATIONS.md`.

## Source-only server snapshot

When a pushed commit changes only deployment scripts, documentation, or other
server-side source and does not require a new runtime image, install it with
`install-source-snapshot.sh`. Supply the exact `origin/main` commit and at least
one locally calculated content hash:

```bash
deploy/hypershell-server/install-source-snapshot.sh \
  --commit <40-character-origin-main-commit> \
  --verify-sha256 <sha256>:deploy/hypershell-server/<changed-script>
```

The installer obtains content only from the private bare repository, rejects
an unpushed commit, normalizes Git file modes to root-owned `0644`/`0755`,
checks every deployment and test shell script, takes both deployment locks,
atomically swaps the canonical source directory, and verifies that the running
container identity, image, restart count, and public/loopback health did not
change. It retains the prior source directory for named manual recovery and
writes root-only evidence. It never rebuilds or restarts the application.

## Private admin site activation

Install the non-proxying HTTP bootstrap host before DNS is active. It exposes
only the ACME challenge directory and returns `404` for every other path:

```bash
deploy/hypershell-server/configure-admin-site.sh bootstrap \
  deploy/profile-server/nginx-admin-http.conf
```

After the A and AAAA records resolve to the Droplet, run the guarded
provisioner:

```bash
deploy/hypershell-server/provision-admin-tls.sh
```

It requires the exact A and AAAA answers, serves a unique ACME marker through
loopback, public IPv4, and public IPv6, requests or reuses the dedicated
certificate through Certbot's `/var/www/letsencrypt` webroot, verifies its
hostname and lifetime, installs the reviewed renewal reload hook, and invokes
the fail-closed TLS activator. Both public HTTPS paths must then expose the
private login page. A post-activation failure restores the HTTP-only bootstrap.

The lower-level `configure-admin-site.sh` modes reject unexpected source
shapes, back up any prior site, run `nginx -t`, reload only a valid
configuration, perform a loopback smoke test, and emit root-only rollback
evidence under `/var/lib/hypershell-deployments`. The TLS provisioner adds its
own certificate fingerprint and dual-stack readiness evidence there.
