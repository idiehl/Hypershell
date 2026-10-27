# Hypershell operations and recovery

This runbook covers the account service introduced by the finished-product
roadmap. It is intentionally separate from application usage documentation.
Commands shown here run on the server as `root` from
`/opt/hypershell/profile-server` unless stated otherwise.

## Deployment invariants

- Run exactly one writer against the production data source.
- Keep Kestrel bound to IPv4 loopback; only Nginx is public.
- Create a fresh stopped-container-consistent backup before every promotion.
- Validate the new image against an isolated restored volume before mounting
  the production data source.
- Preserve the previous container, the backup, and the generated cutover
  evidence until the new release has passed its observation period.
- Never copy a private key, access token, TOTP secret, recovery code, PFX
  password, or DigitalOcean token into logs, tickets, or this repository.

## One-time server setup

1. Create and attach the dedicated 50 GiB block volume. The control panel is
   supported, or use `provision-digitalocean-volume.sh` with a temporary token
   limited to `block_storage:read`, `block_storage:create`, and
   `block_storage_action:create`:

   ```bash
   sudo deploy/hypershell-server/provision-digitalocean-volume.sh \
     --token-file /root/hypershell-volume-provision.token \
     --confirm-create-billed-volume \
     --confirm-attach-to-this-droplet
   ```

   The create flag explicitly approves the recurring storage charge; the
   attach flag separately approves attaching the exact volume to the current
   Droplet. The script validates the API identity and stable block device, but
   cannot format, mount, detach, resize, or delete anything. Revoke and remove
   this temporary token immediately after the evidence file is recorded.
2. Ensure the locked host identity used for container-owned files exists:

   ```bash
   groupadd --system --gid 1654 hypershell-runtime
   useradd --uid 1654 --gid hypershell-runtime --no-create-home \
     --home-dir /nonexistent --shell /usr/sbin/nologin hypershell-runtime
   ```

   Both storage preparation and production promotion fail before formatting or
   stopping the service when this `1654:1654` mapping is missing.
3. Run `deploy/hypershell-server/prepare-storage.sh` without
   `--format-empty-device` first. Confirm its expected empty-device refusal,
   inspect `lsblk -f`, and only then repeat with `--format-empty-device` to mount
   the volume at `/srv/hypershell`.
   The script accepts only a stable `/dev/disk/by-id/...` whole-disk path from
   45 through 55 GiB, refuses partitions and mounted devices, and backs up
   `/etc/fstab` before adding the mount. Mount-point and `fstab` conflicts are
   rejected before formatting. Only the existing empty, root-owned `exports`
   bind-source directory may be present under the pre-mount path; any hidden
   data aborts the operation. The mounted source must resolve to the approved
   disk; otherwise the script unmounts it and restores the prior `fstab`.
3. Provision the Data Protection certificate and independent SSH-vault key
   with `provision-data-protection.sh`.
4. Install `nginx-upstream.conf` in `/etc/nginx/conf.d/`, the shared proxy
   snippet in `/etc/nginx/snippets/`, and both HTTPS sites under
   `/etc/nginx/sites-available/`.
5. Create the `admin` A and AAAA records, then run
   `deploy/hypershell-server/provision-admin-tls.sh`. The command refuses to
   issue or activate until exact dual-stack DNS and public ACME probes pass,
   validates the dedicated certificate, installs the reviewed Certbot renewal
   hook, activates through the rollback-safe Nginx installer, and records
   root-only evidence.
6. Configure `/etc/hypershell-profile-server/digitalocean.env` as root-owned
   mode `0600`; install and enable the snapshot timer only after its volume ID
   has been verified against the mounted device.

The DigitalOcean configuration file has this shape and must not be committed:

```text
DIGITALOCEAN_TOKEN=<scoped token>
HYPERSHELL_VOLUME_ID=<volume UUID>
HYPERSHELL_VOLUME_DEVICE=/dev/disk/by-id/scsi-0DO_Volume_<volume-name>
HYPERSHELL_SNAPSHOT_RETENTION=8
```

Use a new, snapshot-only token limited to `block_storage:read` plus
`block_storage_snapshot:create`, `block_storage_snapshot:read`, and
`block_storage_snapshot:delete`. The verifier checks the API UUID, exact 50 GiB
size, `nyc3` region, attached Droplet ID, stable device name, and active mount
before the schedule can be installed. Enroll it with:

```bash
/opt/hypershell/profile-server/deploy/hypershell-server/\
enroll-digitalocean-snapshot-token.sh
```

The token is read without echo from `/dev/tty`, the volume UUID is discovered
through the scoped API, and the root-only canonical configuration is installed
only after the existing verifier passes. Never paste the token into chat or a
command argument. For rotation, rerun with `--replace`; a failed verification
leaves the current configuration unchanged.

Then enable the schedule with:

```bash
sudo deploy/hypershell-server/install-volume-snapshot-schedule.sh
```

Each scheduled run re-verifies that identity, takes a stopped-writer-consistent
application backup, flushes the mounted filesystem, and only then requests the
block-level snapshot. It retains eight snapshots by default.

After the volume is mounted, migrate the live data with a fresh backup and the
currently deployed image:

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

Do not pre-populate the target directory. The promotion process stops the sole
writer, copies and compares the exact current data, and switches the canonical
container to the bind mount. Its evidence records both data roots. A failed
promotion synchronizes any candidate writes back before restoring the original
container; named rollback does the same after a successful migration.

## Source-only server cutover

For a pushed commit that changes only server-side scripts or documentation and
does not require a new container image, use the dedicated source installer
instead of manually extracting `git archive` output:

```bash
deploy/hypershell-server/install-source-snapshot.sh \
  --commit <exact-origin-main-commit> \
  --verify-sha256 <locally-calculated-sha256>:deploy/hypershell-server/<file>
```

Repeat `--verify-sha256` for additional critical files. The installer accepts
only the exact `refs/heads/main` commit in the private bare repository. It
normalizes source modes, proves the supplied hashes, syntax-checks all server
and fixture shell scripts, takes the deployment and backup locks, and swaps the
source directory atomically. Production container identity, image, restart
count, loopback health, and public health must remain unchanged. Preserve the
recorded prior source directory and root-only evidence through the observation
period. This procedure does not replace a container promotion when application
code or runtime dependencies changed.

## Backup, restore proof, and encrypted export

Create a consistent backup:

```bash
sudo deploy/hypershell-server/backup-profile-server.sh /srv/hypershell/backups
```

The script discovers the actual mounted data source, stops only the selected
container, archives the data and deployment, starts it again, and verifies the
container's current loopback port. Use the printed backup directory in the
following commands.

Prove that the backup starts independently:

```bash
sudo deploy/hypershell-server/restore-profile-server.sh \
  --backup /srv/hypershell/backups/hypershell-consistent-TIMESTAMP \
  --image hypershell-profile-server:VERSION
```

The proof uses a disposable volume and writes `restore-evidence-*.json` into
the backup. It never attaches the production volume. Backups created by the
earlier foundation script remain valid when their original `SHA256SUMS`, data
archive, and deployment archive agree; the newer metadata and inspect files
add diagnostics but are not required to restore that legacy layout.

Create an off-host-safe export only after that proof succeeds:

```bash
sudo deploy/hypershell-server/export-profile-server.sh \
  --backup /srv/hypershell/backups/hypershell-consistent-TIMESTAMP \
  --recipient RECOVERY_GPG_FINGERPRINT
```

The resulting `.tar.gz.gpg` contains the data backup and all three external
recovery secrets. The adjacent manifest contains no secret and records the
ciphertext hash, length, restore evidence time, and optional volume snapshot
ID. The admin portal rechecks the length and SHA-256 immediately before each
authenticated download. Store the GPG private key and its recovery material
off the server.

## Parallel validation and production cutover

Build a uniquely tagged image, take a fresh backup, then run:

```bash
sudo deploy/hypershell-server/promote-profile-server.sh \
  --image hypershell-profile-server:VERSION \
  --backup /srv/hypershell/backups/hypershell-consistent-TIMESTAMP
```

Promotion first runs the new image against an isolated restored copy. Only
after that succeeds does it stop the old writer, retain that container under a
timestamped rollback name, start the new image on the same production data
source using the inactive loopback slot, switch the Nginx upstream atomically,
and verify public HTTPS health. Any failed step restores the old container and
upstream automatically. The final line gives the exact evidence file.

Do not run `docker compose up` after a blue/green promotion without setting
`HYPERSHELL_PROFILE_IMAGE` and `HYPERSHELL_PROFILE_PORT` to the values in the
current evidence file.

## Named rollback

If the new service is unhealthy or incompatible, use the exact evidence path
printed by promotion:

```bash
sudo deploy/hypershell-server/rollback-profile-server.sh \
  --evidence /var/lib/hypershell-deployments/cutover-TIMESTAMP.env
```

Rollback stops the current writer, returns the retained image to the canonical
container name on the same live data source, restores the old Nginx upstream,
and checks public HTTPS. The forward container is retained. Because both
versions use the same data source serially, accepted profile changes are not
discarded merely by changing application versions. If a future schema change
is not backward compatible, restore the pre-cutover encrypted export into a
new volume instead of running an older binary against newer data.

## Credential rotation

Use a different procedure for each credential class:

- Account recovery access key: `account rotate-key`; this invalidates account
  sessions. Store the once-shown replacement in the password manager.
- Portal password/TOTP: use `account set-password` from an owner-only password
  file, enroll the new TOTP seed, save the new recovery codes, then remove the
  password file. Revoke other portal and desktop sessions afterward.
- Client PC: revoke it from **Client PCs**. This prevents future API and vault
  releases but does not erase files already present on that PC.
- SSH credential: create the replacement key, install its public key on every
  linked host, replace the vault credential, verify a new release on an
  approved PC, then remove the old public key server-side. Revoking only the
  vault record cannot remove an already authorized remote key.
- SSH host-key pin: investigate an unexpected fingerprint out of band. Replace
  a pin only after verifying the host's real key on its console.
- Data Protection certificate or SSH wrapping key: perform a planned
  re-encryption migration. Replacing either file in isolation can invalidate
  cookies or make credential envelopes unrecoverable.
- Code-signing certificate: publish a transition client that trusts both
  fingerprints before using the new signer. See `doc/RELEASES.md`.

## Profile conflict recovery

Hypershell never resolves a stale ETag through last-write-wins. The current
checkpoint remains unchanged and both sides are written under:

```text
%LOCALAPPDATA%\Hypershell\profile-conflicts\
```

Open the local and remote copies, choose the intended settings/resources, save
the merged result as a new local revision, and run **Sync Now**. Keep device
path mappings local. Do not copy unrestricted AI approvals, DPAPI blobs, or
Windows paths between PCs. Package conflicts require matching package ID,
version, and content hash; review and install the intended package instead of
overwriting it silently.

## Two-PC finished-release acceptance

Record evidence for each row rather than treating a successful launch as a
release sign-off:

1. Install and authorize a clean Windows 10/11 x64 PC through the portal.
2. Restore named profiles, library enablement, remote project references, and
   paginated AI conversations without copying device-local secrets.
3. Reauthenticate before releasing the linked SSH key; verify DPAPI storage and
   revocation from **Client PCs** and **Sessions**.
4. Browse, edit, conflict-test, upload, and download files on Linux and Windows
   OpenSSH hosts; interrupt the network during a dirty remote edit.
5. Open independent remote terminals and verify reconnect and working-directory
   behavior.
6. Run one AI remote file task, one terminal task, and one semantic Git task
   under both review and automatic Context policies; inspect the audit record.
7. Clone a remote project and complete status, diff, stage, commit, branch,
   merge-conflict resolution, fetch, pull, and push.
8. Download and install a signed update, reject a tampered copy, and exercise
   the named server and retained-installer rollback paths.
9. Download a verified encrypted recovery export and restore it into an
   isolated service before declaring backups valid.

Automated verification remains mandatory before this manual matrix:

```powershell
dotnet build tools\Hypershell.CSharpVerifier\Hypershell.CSharpVerifier.csproj -c Release
dotnet run --project tools\Hypershell.CSharpVerifier\Hypershell.CSharpVerifier.csproj -c Release --no-build
```

Run the cross-machine completion preflight after the manual matrix:

```powershell
.\tools\Test-HypershellFinishedProduct.ps1 `
  -RunFullVerifier `
  -ExpectedServerImage hypershell-profile-server:VERSION `
  -AcceptanceEvidencePath C:\secure-evidence\hypershell-two-pc.json
```

The acceptance evidence JSON contains booleans named `cleanInstall`,
`deviceAuthorization`, `profileRestore`, `credentialUnlock`, `remoteLinux`,
`remoteWindows`, `aiRemoteTask`, `gitWorkflow`, `signedUpdate`, `rollback`, and
`isolatedRecoveryRestore`. It contains no screenshots, credentials, or private
workspace data. The preflight also verifies repository parity, the complete
local verifier, signer trust, live server health/image, the dedicated volume,
Nginx upstream, snapshot timer, restore evidence, admin DNS, and real HTTPS.
Any failed row means the finished-product milestone is not complete.
