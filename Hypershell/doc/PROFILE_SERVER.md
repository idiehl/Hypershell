# Hypershell profile server

Status: the account service, password/TOTP portal, browser-mediated desktop sign-in,
desktop token rotation, signed-release API, v2 roaming profiles/conversations/
package vault, Docker image, and integration verifier are implemented. The
linked-host registry, envelope-encrypted SSH credential vault, host-key pins,
and PC-bound credential release flow are also implemented. The existing
profile API v1 remains compatible at
`https://hypershell.atlasuniversalis.com/`.

The service is a single-instance cross-platform ASP.NET Core application. It
uses SQLite WAL metadata and content-addressed blobs alongside the compatible
immutable profile-revision store. It has no public registration, email flow,
or multi-node dependency. Project documents remain in local or remote
workspaces rather than profile records.

## Security and data model

- Accounts are provisioned by an administrator. The legacy access key is shown
  once and retained only as a PBKDF2-SHA256 hash for migration and recovery.
- Portal login requires a password plus TOTP or a one-time recovery code. Login
  is rate limited, cookies are secure/HTTP-only, POSTs use antiforgery tokens,
  and ASP.NET Data Protection keys are encrypted by an external certificate in
  production.
- A new desktop opens the private browser login. Successful password and TOTP/
  recovery-code authentication automatically completes its pending one-time
  request; there is no separate device-approval click. The service then issues
  a short-lived access token and rotating refresh credential. Windows DPAPI
  protects the refresh credential locally, and reuse of an old refresh token
  revokes that token family.
- Client PCs and their sessions are persisted in SQLite and can be revoked.
  Rotating the legacy key or disabling the account invalidates active access.
- Every profile route authenticates and scopes storage to the account. Account
  and profile directory names are SHA-256 storage keys rather than user input.
- Uploaded bundles are checked again server-side for size, strict schema,
  supported versions, profile identity, content counts, and deterministic
  SHA-256 integrity.
- Mutations require a strong ETag precondition. Revisions are immutable,
  metadata changes are atomic, and deletion creates a tombstone that prevents
  an offline client from silently recreating old state.
- Sign-in is limited to 10 requests per minute per observed client address;
  authenticated API traffic is limited to 240 requests per minute.
- Account metadata, profile metadata, and revision payloads receive
  owner-only file permissions on Linux.
- AI conversations have no fixed total-item cap. Writes and reads are bounded
  and paginated; provider continuation context is stored separately. The
  service rejects image payloads and bulk action output and redacts API-token
  and private-key patterns before persistence.
- The package vault accepts only validated, inert ContentOnly `.hyperlib`
  archives, rejects traversal/symlinks/duplicates, recomputes deterministic
  content identity, and stores blobs by SHA-256.
- SSH private keys are parsed in memory, encrypted with a random per-credential
  AES-256-GCM data key, and stored only as an authenticated envelope. A
  separate 32-byte server master key wraps each data key and is never stored in
  SQLite. Production supplies that master key through a read-only Docker
  secret; the service refuses missing, malformed, or broadly accessible key
  files.
- A released SSH credential is bound to the signed-in account and registered
  ClientDevice, requires fresh portal password plus TOTP/recovery
  authentication, expires after ten minutes, and can be consumed only once.
  The desktop immediately re-encrypts it with Windows DPAPI and clears response
  buffers. Rotation/revocation invalidates outstanding releases.
- SSH host-key fingerprints are compare-before-write pins. An unexpected key
  change is a hard conflict; deliberate replacement requires both the previous
  pin and an explicit desktop warning. Audit records contain IDs and bounded
  action summaries, never credential bytes or passphrases.

The current file store is deliberately a **single-instance service**. Run one
profile-server replica against a data volume and serialize account-management
commands. A future database-backed implementation will be required before
horizontal scaling.

## Local .NET quick start

.NET 10 is required. From the repository root, choose a dedicated data
directory and create the first account:

~~~powershell
dotnet run --project src\Hypershell.ProfileServer\Hypershell.ProfileServer.csproj -c Release -- account create --id ihigg --display-name "Primary account" --data-root .\profile-server-data
~~~

Copy the ACCESS_KEY value to a password manager when it is shown. Only its hash
is written to accounts.json.

Start the service on loopback:

~~~powershell
dotnet run --project src\Hypershell.ProfileServer\Hypershell.ProfileServer.csproj -c Release -- --data-root .\profile-server-data --urls http://127.0.0.1:5088
~~~

Confirm readiness:

~~~powershell
Invoke-RestMethod http://127.0.0.1:5088/healthz
~~~

In Hypershell, open **Settings > Profiles > Authenticated profile sync** and
enter:

- Endpoint: http://127.0.0.1:5088/
- Account ID: ihigg
- Account access key: the once-displayed key from account creation

Choose **Save connection**, choose whether Windows should protect the key for
automatic sign-in, then choose **Sign in**. Plain HTTP is accepted only for a
loopback endpoint; a non-loopback desktop connection must use HTTPS.

## Docker Compose quick start

The sample in
[samples/profile-server/compose.yaml](../samples/profile-server/compose.yaml)
builds a non-root .NET 10 image, uses a read-only root filesystem, drops Linux
capabilities, persists the data directory in a named volume, and publishes the
service only on host loopback.

~~~powershell
docker compose -f samples\profile-server\compose.yaml build
docker compose -f samples\profile-server\compose.yaml run --rm profile-server account create --id ihigg --display-name "Primary account"
docker compose -f samples\profile-server\compose.yaml up -d
Invoke-RestMethod http://127.0.0.1:5088/healthz
~~~

The account command uses the same persistent volume as the service. Do not run
two account-management commands concurrently.

## Account administration

Run these commands from the repository root. Add --data-root when using the
local .NET process; the Compose commands already use the configured volume.

~~~powershell
# List accounts without exposing key hashes.
dotnet run --project src\Hypershell.ProfileServer\Hypershell.ProfileServer.csproj -c Release -- account list --data-root .\profile-server-data

# Replace an access key and invalidate all sessions. The new key is shown once.
dotnet run --project src\Hypershell.ProfileServer\Hypershell.ProfileServer.csproj -c Release -- account rotate-key --id ihigg --data-root .\profile-server-data

# Disable immediately; enabling does not restore invalidated sessions.
dotnet run --project src\Hypershell.ProfileServer\Hypershell.ProfileServer.csproj -c Release -- account disable --id ihigg --data-root .\profile-server-data
dotnet run --project src\Hypershell.ProfileServer\Hypershell.ProfileServer.csproj -c Release -- account enable --id ihigg --data-root .\profile-server-data
~~~

For a container deployment, replace the command prefix with:

~~~powershell
docker compose -f samples\profile-server\compose.yaml run --rm profile-server
~~~

Treat access-key output as a secret. Do not put a key in shell history,
Compose environment variables, source control, logs, profile files, or support
bundles.

## Configuration

| Setting | Default | Allowed range | Purpose |
| --- | ---: | ---: | --- |
| HYPERSHELL_PROFILE_SERVER_DATA | profile-server-data beside the executable | absolute or resolved path | Account registry and profile revisions |
| HYPERSHELL_PROFILE_SESSION_MINUTES | 30 | 5-1,440 | In-memory session lifetime |
| HYPERSHELL_PROFILE_ACCOUNT_STORAGE_MIB | 512 | 16-102,400 | Per-account immutable revision quota |
| HYPERSHELL_MINIMUM_FREE_STORAGE_MIB | 512 | 64-102,400 | Free-space reserve below which writes are rejected |
| HYPERSHELL_PUBLIC_PORTAL_ORIGIN | https://admin.atlasuniversalis.com | absolute HTTPS origin | Desktop browser sign-in links |
| HYPERSHELL_RELEASE_SIGNER_SHA256 | empty | comma/semicolon-separated SHA-256 fingerprints | Trusted release manifest signers |
| HYPERSHELL_STABLE_RELEASE_RETENTION | 3 | 1-20 | Unpinned stable releases retained |
| HYPERSHELL_DATA_PROTECTION_CERTIFICATE | empty | absolute container path | PFX protecting portal/security state |
| HYPERSHELL_DATA_PROTECTION_CERTIFICATE_PASSWORD_FILE | empty | absolute container path | Root-managed PFX password file |
| HYPERSHELL_SSH_VAULT_MASTER_KEY_FILE | data-root/secrets/ssh-vault-master-key.bin | absolute path to exactly 32 bytes | External AES wrapping key for SSH credential envelopes |
| HYPERSHELL_BACKUP_EXPORTS | data-root/backup-exports | absolute read-only directory | Strict encrypted recovery manifests exposed to the private portal |
| ASPNETCORE_HTTP_PORTS | ASP.NET default | valid port | Container listen port |
| --urls | ASP.NET default | valid ASP.NET URL | Direct process listen address |

Protocol v1 permits at most 1,000 profile IDs per account and 4 MiB per
portable-profile bundle. Old immutable revisions remain on disk and count
toward the quota; retention and garbage collection are future administrative
features.

The data root contains:

~~~text
accounts.json                    # compatible account/key registry
cloud-metadata.sqlite3           # SQLite WAL account-service metadata
data-protection-keys/
secrets/                            # development default; production uses an external Docker secret
  ssh-vault-master-key.bin
blobs/
  packages/<sha-prefix>/<sha256>/
  releases/<sha-prefix>/<sha256>/
profiles/
  <account-sha256>/
    <profile-sha256>/
      current.json
      revisions/
        00000000000000000001.hyperprofile
        ...
~~~

## Production deployment checklist

1. Run exactly one instance on a durable Linux volume. The Compose sample binds
   only to 127.0.0.1:5088 so a local reverse proxy can reach it without exposing
   raw HTTP publicly.
2. Terminate TLS at a maintained reverse proxy and configure the final public
   HTTPS base URL directly in Hypershell. The desktop refuses redirects, so the
   configured endpoint must not depend on HTTP-to-HTTPS or hostname redirects.
3. Apply sign-in rate limiting at the edge as well. The application partitions
   its limiter by the socket address and intentionally does not trust arbitrary
   forwarded-IP headers.
4. Restrict administrative shell and volume access. Anyone who can replace
   accounts.json or stored profile files controls the service.
5. Use a filesystem or volume snapshot, or briefly stop the service for a
   file-copy backup. Back up the complete data root and test restoration on a
   separate instance.
6. Monitor HTTPS availability, /healthz, disk/quota use, 401/403/429 rates, and
   application errors. Do not log request authorization headers or bodies.
7. Provision real account IDs, deliver each once-displayed access key through a
   secure channel, and verify sign-in from the desktop before broadening access.

The current private deployment uses the final HTTPS endpoint above and has a
provisioned `ihigg` account. The desktop's production smoke check authenticates,
lists metadata, and signs out without mutating profile content. Backup testing,
monitoring, retention, and routine host maintenance remain ongoing operational
responsibilities; deployment credentials remain outside the repository.

## Verification

Run the real loopback integration suite:

~~~powershell
dotnet run --project tools\Hypershell.CSharpVerifier\Hypershell.CSharpVerifier.csproj -c Release -- --profile-server-only
~~~

It starts Kestrel on an ephemeral port and verifies key hashing, invalid
credentials, short-session expiry, explicit sign-out, account isolation,
strict upload rejection, ETag conflicts, immutable revisions, tombstones and
revival, restart persistence, session invalidation after restart/key rotation/
disable, and absence of plaintext keys in persisted server files. The same run
also verifies schema migration 3, account-isolated v2 resources, desktop HTTP
transport, paginated conversation history beyond the former 96-item boundary,
secret redaction, conflict preservation, and private Hyperlib restoration.
It additionally exercises malformed SSH keys, account/device isolation,
fresh portal reauthentication, one-time release/replay rejection, DPAPI local
storage, host-key replacement conflicts, credential rotation, release
invalidation, revocation, and a raw scan for leaked key/passphrase/token data.

The container package can be checked independently:

~~~powershell
docker compose -f samples\profile-server\compose.yaml config
docker build -f src\Hypershell.ProfileServer\Dockerfile -t hypershell-profile-server:dev .
~~~

The wire contract remains documented in
[PROFILE_SYNC.md](PROFILE_SYNC.md). Documents and repository objects are
outside this API and must use a separate future protocol with independent size,
revision, history, conflict, and permission rules.
