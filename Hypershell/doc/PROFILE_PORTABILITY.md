# Portable profiles

Hypershell portable profiles are versioned workspace-configuration files with
the `.hyperprofile` extension. They are the local-first synchronization unit
for profiles; they are not machine backups, extension installers, credential
containers, or document archives.

The current implementation is available under **Settings > Profiles**. It can:

- export the current saved profile with a SHA-256 integrity hash;
- inspect an import without changing application state;
- report schema, integrity, extension, library, and terminal-target problems;
- apply the safe runtime subset for the current session; or
- explicitly save a fully merged profile while preserving device-local state.

The authenticated sync client, revisions/ETags, conflict copies, and tombstones
are now implemented around this same bounded payload. The matching account
service is deployed privately at `https://hypershell.atlasuniversalis.com/`;
remote document objects remain a separate future stage. See
[`PROFILE_SYNC.md`](PROFILE_SYNC.md).

## File contract

The envelope and profile schema are versioned independently:

```json
{
  "$schema": "https://hypershell.dev/schemas/profile-bundle-v1.json",
  "format": "hypershell-profile",
  "bundleVersion": 1,
  "exportedAtUtc": "2026-08-01T12:00:00+00:00",
  "exportedBy": "Hypershell 1.0.0.0",
  "profileSha256": "64 lowercase hexadecimal characters",
  "profile": {
    "schemaVersion": 1,
    "id": "stable-profile-id",
    "displayName": "Developer workspace",
    "revision": 3,
    "updatedAtUtc": "2026-08-01T12:00:00+00:00",
    "workspacePreferences": {},
    "configuration": {},
    "extensions": [],
    "libraries": [],
    "workspaces": []
  }
}
```

The complete JSON Schema is
[`doc/schemas/hypershell-profile.schema.json`](schemas/hypershell-profile.schema.json).
Runtime validation is authoritative and additionally checks duplicate IDs,
active-profile references, finite numeric values, relative workspace paths,
installed dependencies, and the integrity hash.

The hash is SHA-256 over the host's compact canonical serialization of the
`profile` object, not over the complete envelope. Export metadata can therefore
change without changing the identity of the enclosed profile snapshot.

## Portable data

Schema v1 can carry:

- application startup-pane and new-tab preferences;
- Command Builder behavior preferences;
- Explorer display flags, but not Explorer locations or projects;
- terminal font, colors, input behavior, timestamps, backdrop behavior, tab
  title templates, tab colors, and target shell IDs;
- command profiles and selected Hyperlib library IDs;
- extension and external-library subscription references;
- legal panel placements, visibility, order, and dimensions;
- reserved repository workspace references containing repository IDs and
  relative paths only.

An exported layout is always locked (`isUserUnlocked: false`). Missing panels
remain dormant through the existing layout normalizer so a profile can recover
its layout after the corresponding extension is installed and approved.

Extension-specific settings are reserved but must be empty in schema v1. They
will not travel until each extension has a host-owned schema, size limit, and
secret/path filtering policy.

## Always device-local

The portable DTOs do not have fields for:

- extension approvals, package hashes used for trust, AI permissions, or other
  authorization decisions;
- credentials, tokens, secret values, authentication state, or OS key-vault
  references;
- terminal executable paths, arguments, distributions, initialization
  commands, or startup commands;
- absolute workspace, project, Explorer startup, terminal startup, or terminal
  background-image paths;
- project directory definitions and local repository mappings;
- open documents, document contents, editor sessions, recovery snapshots, or
  unsaved buffers;
- WebView browser data or extension-panel runtime state.

Saving an imported profile overlays portable fields onto a clone of the current
device settings. Matching terminal configuration IDs retain their local startup
directory, background image, and startup command. Terminal definitions retain
their executable, arguments, initialization command, startup command, and
local paths. Explorer startup and project data are retained in full.

Approvals remain in their separate device-state files and are never opened or
rewritten by the portable-profile service. Workspace AI approvals and their
content-free local audit use the separate boundary documented in
[`WORKSPACE_AI_PERMISSIONS.md`](WORKSPACE_AI_PERMISSIONS.md).

## Export boundary

Export uses these safeguards:

1. The destination must end in `.hyperprofile`.
2. The in-memory portable snapshot is validated before serialization.
3. The complete UTF-8 JSON payload is limited to 4 MiB.
4. The profile hash is computed and included in the envelope.
5. A unique sibling temporary file is created, flushed to disk, and atomically
   moved over the destination.
6. A failed write removes only that temporary sibling.

The export reads the current saved profile model. Session-only Settings
previews are not silently promoted into an exported profile.

## Import and commit boundary

Choosing a bundle is inspection only. Before enabling either import action,
Hypershell:

1. rejects an empty file or a file larger than 4 MiB before JSON parsing;
2. requires strict UTF-8, a maximum JSON depth of 64, no comments, no trailing
   commas, string enums, and no unknown object members;
3. requires the supported envelope and profile schema versions;
4. recomputes and compares the profile SHA-256 hash;
5. validates IDs, strings, colors, counts, duplicate identities, relative
   paths, active command-profile identity, terminal settings, and panel data;
6. reports missing extension versions, Hyperlib contributions, device terminal
   targets, and fresh approval requirements as compatibility warnings.

Validation errors disable both import actions. Compatibility warnings permit an
import because unavailable contributions remain dormant and do not gain trust.

**Apply for this session** changes only the runtime-capable safe subset:
application panes, terminal appearance/behavior, Explorer display flags, and
workspace layout. Command profiles, Command Builder defaults, and subscription
metadata are not changed because those currently share the persistent settings
model.

**Save as current profile** creates a merged `AppSettings` candidate, atomically
writes that candidate before changing the live model, then adopts command
profiles and subscription metadata and reloads the active command catalogs.
This preserves the established Settings distinction between immediate `Apply`
and persistent `Save`.

## Limits

| Boundary | Schema v1 limit |
| --- | ---: |
| Bundle | 4 MiB |
| JSON depth | 64 |
| Terminal configurations | 128 |
| Command profiles | 1–128 |
| Libraries per command profile | 256 |
| Extension references | 512 |
| Library references | 512 |
| Workspace references | 128 |
| Panel placements per layout | 128 |
| Terminal targets per configuration | 64 |

## Cloud synchronization contract

The implemented client uploads and downloads the complete bounded envelope plus
separate server metadata. Its version 1 protocol requires:

- authenticated profile ownership;
- server revision and ETag separate from the client profile revision;
- conditional update (`If-Match`) rather than silent last-write-wins;
- conflict copies with both parent revisions retained;
- tombstones for intentional deletion;
- a content hash and maximum size checked again server-side;
- independent repository/document objects for file content and history.

The exact routes, headers, JSON receipts, status codes, limits, device-state
rules, and server implementation requirements are defined in
[`PROFILE_SYNC.md`](PROFILE_SYNC.md).

Download must still pass the same local import inspector. Server possession of
a profile never implies extension approval, AI permission, a local path mapping,
or permission to write a document or run a terminal command.

## Verification

Run the focused verifier:

```powershell
dotnet run --project tools\Hypershell.CSharpVerifier\Hypershell.CSharpVerifier.csproj -c Release -- --profile-portability-only
```

It verifies export/import round-trip behavior, deterministic integrity hashing,
tamper rejection, unknown-member rejection, pre-parse size rejection, atomic
temporary-file cleanup, compatibility warnings, and preservation of target
device paths, executable state, startup commands, images, projects, and command
profile boundaries.

Run the separate synchronization verifier with `--profile-sync-only`; it covers
conditional mutations, conflicts, inspector-gated downloads, tombstones,
authentication/offline safety, and the hardened HTTP boundary.
