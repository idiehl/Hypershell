# Hyperprofile server synchronization

Status: the compatible v1 portable-profile protocol and the v2 local-first
roaming resource protocol are implemented in the desktop and account service.
The v2 layer adds AI Contexts, stable remote project references, complete
paginated AI conversation history, and a private inert Hyperlib package vault.
The private reference deployment remains at
`https://hypershell.atlasuniversalis.com/`; production rollout of schema 2 is a
separate deployment operation. See [PROFILE_SERVER.md](PROFILE_SERVER.md).

The v1 client reuses the complete bounded `.hyperprofile` envelope documented
in [`PROFILE_PORTABILITY.md`](PROFILE_PORTABILITY.md). The v2 adjunct never
sends `settings.json`, credentials, approvals, Windows paths, documents,
terminal transcripts, screenshots, recovery state, or arbitrary files.

## User workflow

**Settings > Profiles > Authenticated profile sync** exposes explicit account,
download, deletion, and synchronization actions:

1. **Save device connection** validates and stores only the server endpoint and
   account ID.
2. **Sign in** exchanges the provisioned account access key for a short-lived
   session token. By default, the access key is protected for the current
   Windows user so Hypershell can sign in on later launches; the session token
   is never persisted.
3. **Sign out and forget key** revokes the current session, clears its in-memory
   state, and removes the protected access key.
4. **Refresh remote list** downloads profile metadata, not profile content.
5. **Download to preview** sends the raw remote bundle through the same bounded
   UTF-8, schema, integrity, and compatibility inspector used for local files.
6. **Sync Now** conditionally synchronizes the portable profile and v2 adjunct,
   appends new conversation items, and uploads missing private Hyperlib
   packages.
7. **Tombstone remote** conditionally marks the selected remote revision as
   deleted while retaining the local profile and all local documents.

Authentication can run automatically only when the user has protected the
device credential. Once signed in, meaningful saved profile or conversation
changes schedule a debounced local-first sync; an unavailable service never
blocks local work. Listing, downloading/adopting another profile, and
tombstoning remain explicit actions. A downloaded ETag becomes the device
checkpoint only after the user chooses **Save as current profile** and the
re-created local profile hash still matches the inspected remote bundle. A
session-only **Apply** does not advance sync state.

## Protocol version 2 roaming resources

The v2 resource families use the same bearer session and strict no-redirect
HTTPS client as v1:

```text
api/hypershell/v2/workspace-profiles
api/hypershell/v2/conversations
api/hypershell/v2/packages
```

The workspace-profile adjunct contains portable AI Context behavior, stable
`ssh://` or `workspace://` project identities, and package identity/version/hash
selection. Every local directory mapping remains in
`profile-roaming.device.json`; applying a profile on another PC preserves that
PC's mapping and retains the stable remote project URI for future sync.
Unrestricted AI Contexts arrive unacknowledged and cannot activate until the
user confirms their risk once on the new PC.

Conversation files use stable IDs and retain the complete ordered message list
locally without a fixed item-count cap. Cloud writes carry at most 100 items and
reads page at up to 200 items, so length is governed by storage and provider
context compaction rather than a 96-item UI limit. Continuation context is
bounded separately; raw screenshots, bulk terminal output, private keys, and
token-shaped secrets are excluded or redacted. Upload checkpoints record the
last local sequence and a strong ETag so only new items are appended.

Personal Hyperlib packages are deterministic `.hyperlib` ContentOnly archives
stored by SHA-256. Package code cannot execute through this surface. Restore
refuses to replace an installed package when the same ID has a different
version or content hash. Official package identity remains in the profile and
will be restored from its signed distribution source rather than copied into
the personal vault.

All three resources use explicit conflict behavior. Hypershell never retries a
stale ETag as last-write-wins: profile and conversation conflicts create both
local and remote JSON copies, and package identity/hash conflicts remain
uninstalled for review.

## Endpoint construction

The user configures a base URL, for example:

```text
https://profiles.example.net/team-a/
```

The client preserves its path and appends:

```text
api/hypershell/v1/profiles
```

The example collection endpoint is therefore:

```text
https://profiles.example.net/team-a/api/hypershell/v1/profiles
```

The base URL must use HTTPS. Plain HTTP is accepted only for a loopback
development endpoint. Credentials, query strings, and fragments are forbidden
in the configured URL, and the client refuses HTTP redirects so credentials are
never forwarded to an unexpected origin.

Sign-in sends a strict, bounded JSON document:

~~~http
POST /api/hypershell/v1/auth/session
Content-Type: application/json

{
  "protocolVersion": 1,
  "accountId": "ihigg",
  "accessKey": "<provisioned-access-key>"
}
~~~

The successful response contains the normalized account identity, display
name, random access token, and UTC expiration time. It is capped at 64 KiB and
uses Cache-Control: no-store. Explicit sign-out sends:

~~~http
DELETE /api/hypershell/v1/auth/session
Authorization: Bearer <session-token>
~~~

The strict sign-in request and response schema is
[schemas/hypershell-profile-session.schema.json](schemas/hypershell-profile-session.schema.json).

Every profile request then carries:

```http
Authorization: Bearer <session-token>
User-Agent: Hypershell/<version>
```

The application owns one profile session independently of the Settings window.
The session token is held only in process memory. When the remember option is
enabled, the access key is stored in a separate versioned blob encrypted with
Windows DPAPI `CurrentUser`; it is not recoverable by a different Windows user
or copied device. Neither secret is stored in a portable profile, connection
file, checkpoint file, log/status message, or conflict bundle.

Closing Settings does not sign out. Hypershell renews a near-expiry session on
demand, and a clean application shutdown makes a bounded best-effort revocation
request while retaining the protected key for the next launch. An unavailable
server never blocks local startup or editing. A rejected saved key is removed
and requires the current administrator-issued replacement.

## Protocol version 1

All metadata JSON uses UTF-8 camel-case property names, rejects unknown or
missing members, and carries `protocolVersion: 1`. Remote ETags must be strong,
quoted, opaque values. The server revision is independent from the portable
profile's own `revision` field.

The metadata schema is
[`schemas/hypershell-profile-sync.schema.json`](schemas/hypershell-profile-sync.schema.json).

### List metadata

```http
GET /api/hypershell/v1/profiles
Accept: application/json
```

Successful response:

```json
{
  "protocolVersion": 1,
  "profiles": [
    {
      "id": "default-profile",
      "displayName": "Default",
      "serverRevision": 42,
      "profileRevision": 8,
      "updatedAtUtc": "2026-08-01T19:45:00Z",
      "profileSha256": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
      "etag": "\"profile-42\"",
      "isDeleted": false
    }
  ]
}
```

The response is capped at 1 MiB and 1,000 unique profile IDs. Tombstones remain
visible with `isDeleted: true`; list retrieval never transfers profile content.

### Fetch one profile

```http
GET /api/hypershell/v1/profiles/{profileId}
Accept: application/vnd.hypershell.profile+json
```

Successful response:

```http
HTTP/1.1 200 OK
Content-Type: application/vnd.hypershell.profile+json; charset=utf-8
ETag: "profile-42"
X-Hypershell-Server-Revision: 42

<complete .hyperprofile envelope>
```

The raw response is capped at 4 MiB. Hypershell requires the strong ETag and
positive server-revision header before it accepts the response, then validates
the bundle locally. A response whose enclosed profile ID differs from the
requested ID is rejected.

### Create or update

A profile without a device checkpoint uses a conditional create:

```http
PUT /api/hypershell/v1/profiles/{profileId}
If-None-Match: *
Content-Type: application/vnd.hypershell.profile+json; charset=utf-8

<complete .hyperprofile envelope>
```

A known profile, including a known tombstone that the user intentionally wants
to revive, uses a conditional update:

```http
PUT /api/hypershell/v1/profiles/{profileId}
If-Match: "profile-42"
Content-Type: application/vnd.hypershell.profile+json; charset=utf-8

<complete .hyperprofile envelope>
```

Return `201 Created` for a create and `200 OK` for an update. Both responses
must include a matching strong `ETag`, a matching
`X-Hypershell-Server-Revision`, and this receipt body:

```json
{
  "protocolVersion": 1,
  "profile": {
    "id": "default-profile",
    "displayName": "Default",
    "serverRevision": 43,
    "profileRevision": 9,
    "updatedAtUtc": "2026-08-01T20:15:00Z",
    "profileSha256": "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789",
    "etag": "\"profile-43\"",
    "isDeleted": false
  }
}
```

Mutation receipts are capped at 64 KiB. The client verifies the returned ID,
profile revision, SHA-256, ETag, and server revision before advancing its local
checkpoint. If the local content hash already equals the last successful
checkpoint, the client performs no network write.

### Tombstone

```http
DELETE /api/hypershell/v1/profiles/{profileId}
If-Match: "profile-43"
```

Return `200 OK` with the same receipt shape, matching ETag/revision headers, and
`isDeleted: true`. Retain the last portable-profile hash and profile revision in
the tombstone metadata. The client records the tombstone but does not delete or
modify the local profile.

### Status codes

| Status | Client interpretation |
|---|---|
| `200`, `201` | Success after strict response validation |
| `401` | Session token missing, expired, or invalid |
| `403` | Authenticated account lacks access |
| `404`, `410` | Profile is unavailable |
| `409`, `412` | ETag/precondition conflict; no silent overwrite |
| `5xx` | Remote service failure; local state remains unchanged |
| `3xx` | Protocol error; redirect is refused |

## Conflict behavior

On a conflicting upload, Hypershell does not retry with the newer ETag and does
not choose last-write-wins. It atomically writes the attempted local payload to
the device conflict directory, fetches the current remote revision, validates
it with the local profile inspector, and writes a second remote conflict copy
only if it is a valid bundle for the requested profile ID. The pre-conflict
checkpoint remains unchanged.

Default device-local paths are:

```text
%LOCALAPPDATA%\Hypershell\profile-sync.device.json
%LOCALAPPDATA%\Hypershell\profile-sync-checkpoints.device.json
%LOCALAPPDATA%\Hypershell\profile-sync-credential.device.bin
%LOCALAPPDATA%\Hypershell\profile-conflicts\
%LOCALAPPDATA%\Hypershell\profile-roaming.device.json
%LOCALAPPDATA%\Hypershell\conversations\
%LOCALAPPDATA%\Hypershell\profile-package-cache\
```

`HYPERSHELL_APP_DATA` can relocate these files for development and tests. Sync
configuration, ETags, and the Windows-protected credential deliberately stay
outside the portable payload.

## Server requirements

The reference implementation in src/Hypershell.ProfileServer satisfies these
requirements with administrator-provisioned accounts, hashed access keys,
rotating desktop credentials, SQLite WAL metadata, content-addressed blobs,
account-isolated storage, tombstones, rate limits, and a configurable
per-account quota. It remains intentionally single-instance. Run
and production-hardening guidance is in
[PROFILE_SERVER.md](PROFILE_SERVER.md).

A compatible server must:

- authenticate every call and scope IDs to the authenticated owner;
- compare and mutate profile records atomically;
- issue a new strong opaque ETag and monotonically increasing positive server
  revision for every accepted mutation;
- enforce the 4 MiB body limit and verify the `.hyperprofile` schema, SHA-256,
  URL/body profile ID match, and supported versions again server-side;
- preserve tombstones so an offline device cannot unknowingly recreate deleted
  state with `If-None-Match: *`;
- return the exact metadata and headers above without redirecting the client;
- never infer extension approval, AI permissions, local path mappings, document
  access, or terminal access from possession of a profile.

Documents and repository objects remain in local or SSH workspaces. They are
not embedded into profile or conversation JSON.

## Verification

Run the focused client/protocol verifier:

```powershell
dotnet run --project tools\Hypershell.CSharpVerifier\Hypershell.CSharpVerifier.csproj -c Release -- --profile-sync-only
```

It verifies conditional create/update, stable no-op uploads, stale-write
conflict copies, inspector-gated download adoption, current and stale
tombstones, authentication/offline immutability, session-token
non-persistence, DPAPI credential round trips and plaintext leakage checks,
startup restoration, renewal, explicit forgetting, conditional HTTP headers,
endpoint path preservation, redirect refusal, response caps, and strong ETag
enforcement.

The optional `--profile-production-smoke` mode signs in to a configured HTTPS
deployment, lists metadata without uploading or deleting content, signs out,
forgets its isolated test credential, and checks the temporary application-data
directory for plaintext leakage. It requires the process-scoped
`HYPERSHELL_PROFILE_SMOKE_ENDPOINT`, `HYPERSHELL_PROFILE_SMOKE_ACCOUNT_ID`, and
`HYPERSHELL_PROFILE_SMOKE_ACCESS_KEY` environment variables.

Run the focused real-server verifier:

~~~powershell
dotnet run --project tools\Hypershell.CSharpVerifier\Hypershell.CSharpVerifier.csproj -c Release -- --profile-server-only
~~~

It starts the ASP.NET Core service on an ephemeral loopback port and verifies
account isolation, invalid credentials, strict upload validation, conditional
revisions, tombstones and revival, restart persistence, session expiry and
sign-out, credential rotation, account disablement, and absence of plaintext
access keys in server storage. It also drives the real v2 desktop HTTP client,
205-item paginated conversation restore, secret redaction, device-local path
isolation, conflict copies, restart persistence, and private package restore.
