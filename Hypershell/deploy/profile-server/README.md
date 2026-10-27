# Hypershell profile-server production deployment

This directory defines the single-instance Hypershell account service. The
desktop API remains at `hypershell.atlasuniversalis.com`; the private Razor
Pages portal is exposed separately at `admin.atlasuniversalis.com`. Kestrel
stays on host loopback and Nginx terminates both HTTPS origins.

## Server layout

The deployment mirror lives at `/opt/hypershell/profile-server`:

```text
deploy/profile-server/compose.yaml
src/Hypershell.ProfileProtocol/
src/Hypershell.ProfileServer/
src/Hypershell/Profiles/ProfileSyncContracts.cs
```

Run Compose from `/opt/hypershell/profile-server/deploy/profile-server`.

Before the first account-service deployment, provision the certificate that
encrypts the ASP.NET Data Protection key ring:

```bash
sudo /opt/hypershell/profile-server/deploy/hypershell-server/provision-data-protection.sh
```

All three generated files under `/etc/hypershell-profile-server/secrets` are
required to restore browser cookies, TOTP secrets, recovery-code state, and
SSH credential envelopes. They remain root-owned and are readable only by the
container runtime group (numeric GID 1654 by default). Back them up separately
from the application volume.

The provisioning script is additive: an existing data-protection certificate
is preserved while a missing 32-byte `ssh-vault-master-key.bin` is created.
It refuses incomplete certificate state and never overwrites existing secret
material. Losing the SSH vault key makes stored private-key envelopes
unrecoverable; exposing it together with the database defeats envelope
separation.

## Build and lifecycle

```bash
docker compose config
docker compose build --pull profile-server
docker compose up -d profile-server
docker compose ps
curl --fail --silent --show-error http://127.0.0.1:5088/healthz
```

The container is intentionally limited to one replica. Do not point a second
container at the same data volume.

## Account administration

Account access keys are displayed once. Put the key directly into a password
manager; do not store it in this directory, shell history, Compose variables,
logs, or support bundles.

```bash
docker compose run --rm --no-deps --interactive=false profile-server account create \
  --id ACCOUNT_ID --display-name "DISPLAY NAME"
docker compose run --rm --no-deps --interactive=false profile-server account list
docker compose run --rm --no-deps --interactive=false profile-server account rotate-key --id ACCOUNT_ID
docker compose run --rm --no-deps --interactive=false profile-server account disable --id ACCOUNT_ID
docker compose run --rm --no-deps --interactive=false profile-server account enable --id ACCOUNT_ID
```

After creating or migrating the private account, enroll its portal password
and second factor from an owner-only password file:

```bash
install -m 0600 /dev/null /root/hypershell-password
# Populate /root/hypershell-password without putting the password in shell history.
docker compose run --rm --no-deps --interactive=false profile-server account set-password \
  --id ACCOUNT_ID --password-file /root/hypershell-password
```

The command prints an authenticator URI and recovery codes once. Remove the
temporary password file after enrollment. The legacy administrator access key
remains available for migration and recovery; desktop installations should use
the browser sign-in flow. Password and authenticator verification automatically
completes the pending desktop request; no separate approval click is required.

Account commands must be serialized. Rotating a key or disabling an account
invalidates its active sessions. Keep `--interactive=false` on commands called
from a script; otherwise Compose can consume the script's remaining standard
input while the one-shot administration container is attached.

## Linked SSH hosts and credential releases

The portal's **Linked Hosts** section enrolls encrypted OpenSSH private keys,
creates Linux/POSIX or Windows OpenSSH host records, and manages host-key pins.
Hypershell exposes the same inventory under **Settings > Profiles**. A new PC
does not receive a stored key during normal profile sync: choose the credential
unlock action, then re-enter the portal password and TOTP/recovery code. The
release is short lived, bound to that ClientDevice, single use, and stored by
the desktop with DPAPI.

Revoking a Hypershell vault record blocks future releases and removes the
current PC's protected copy. It cannot remove a public key already installed
in a remote host's `authorized_keys`; rotate/remove that server-side key when
access must be cut off at the SSH boundary. Because the accepted design stores
the shared root credential in this vault, compromise of the account service,
database, and external wrapping key together can expose root access. Approval,
reauthentication, audit, host-key pinning, and rotation reduce but do not erase
that risk.

## Signed application releases

Set `HYPERSHELL_RELEASE_SIGNER_SHA256` in the deployment shell to the lowercase
SHA-256 fingerprint of the trusted Authenticode certificate before starting the
service or importing a release. This fingerprint is public trust metadata, not
the private signing key. The private key remains on the release workstation.

Copy a completed release bundle to an owner-only incoming directory, mount that
one directory read-only into a one-shot container, and import it:

```bash
export HYPERSHELL_RELEASE_SIGNER_SHA256=64_HEX_CHARACTER_CERTIFICATE_SHA256
docker compose run --rm --no-deps --interactive=false \
  --volume /srv/hypershell/releases/incoming/1.2.3:/release:ro \
  profile-server release publish --bundle /release
docker compose run --rm --no-deps --interactive=false \
  profile-server release list --channel stable
```

Add `--pin` to preserve a release beyond normal retention. The service keeps
the latest three unpinned stable versions plus all pinned versions. Import is
immutable and idempotent: the same signed version may be imported again, but a
different manifest for an existing version is rejected. The portal exposes
authenticated downloads at `/Admin/downloads`; desktop clients use the v2
release API. Full workstation signing and publication instructions are in
`doc/RELEASES.md`.

## Nginx and TLS

1. Create both DNS records for `admin.atlasuniversalis.com`:
   - A → `167.71.179.90`
   - AAAA → `2604:a880:800:14:0:1:aa80:1000`
2. Install `nginx-rate-limits.conf` under `/etc/nginx/conf.d/`.
3. Install `nginx-upstream.conf` under `/etc/nginx/conf.d/`.
4. Install `nginx-proxy-snippet.conf` under `/etc/nginx/snippets/`.
5. Install `nginx-http.conf` and the fail-closed `nginx-admin-http.conf` as
   their respective enabled sites and obtain the desktop endpoint certificate.
6. After the exact admin A and AAAA records resolve, run
   `deploy/hypershell-server/provision-admin-tls.sh`. It proves the ACME
   webroot over IPv4 and IPv6, obtains and validates the dedicated certificate,
   installs the renewal reload hook, and activates `nginx-admin-https.conf`
   through the rollback-safe site installer.

The desktop endpoint is:

```text
https://hypershell.atlasuniversalis.com/
```

The profile client refuses redirects. Configure that HTTPS URL directly.
Desktop sign-in links use `https://admin.atlasuniversalis.com/Device`; that
origin is pinned through `HYPERSHELL_PUBLIC_PORTAL_ORIGIN`, not inferred from
an untrusted request Host header.

## Backup boundary

Until the dedicated block volume is attached, the durable state is the complete
`hypershell-profile-data` volume. Stop the profile container briefly before
copying it so `accounts.json`, the SQLite WAL database, profile metadata,
content blobs, and immutable revisions form one consistent backup. Include the
external Data Protection certificate and password in a separately encrypted
backup. Test restoration using a separate container and volume before treating
a backup as reliable.

Monitor public `/healthz`, container restart count, HTTP 401/403/429/5xx rates,
volume growth, disk space, and certificate renewal. Docker JSON logs are capped
at three 10 MiB files.

Use `deploy/hypershell-server/backup-profile-server.sh` before every upgrade,
prove the backup with `restore-profile-server.sh`, and deploy with
`promote-profile-server.sh`. The promotion emits a root-only evidence path that
is the sole input to `rollback-profile-server.sh`. Full recovery, rotation,
conflict, snapshot, and acceptance procedures are in `doc/OPERATIONS.md`.
