# Workspace AI permission broker

Status: the device-local approval store, bounded workspace and live-editor
operations, per-root terminal execution policies, compact audit trail, Settings
management surface, OpenAI Responses adapter, review coordinator, and
deterministic safety verifiers are implemented. Provider consent remains
separate from workspace approval and is disabled by default.

The permission broker is the only supported route from an AI provider to
workspace files or terminals. A provider does not receive a raw filesystem,
`DocumentTabViewModel`, terminal session, process handle, or Windows credential.
Profile import, profile sync, and extension installation cannot create or widen
an AI approval.

## Settings workflow

Open **Settings > AI Access**. The setup card gives the user the explicit risk
warning and two scope choices. **Full device** approves every currently ready
local fixed-drive root; **single folder** opens an independent folder picker.
Neither choice depends on the File System Explorer's current location. Direct
roots can still be added manually, while saved Explorer project roots and the
current Explorer folder remain optional convenience suggestions. Any number of
roots can be retained independently, up to the device-state cap. For each root,
the user can grant these device-local capabilities and policies:

- bounded file and directory reads;
- atomic text creation and compare-before-write replacement;
- optional standing auto-apply for file and live-editor changes;
- on-demand reads of the live terminal draft and bounded recent transcript;
  and
- terminal staging and execution with a separately selected execution policy.

Each terminal-enabled root has one host-enforced policy:

- **Review + confirm every command** (default): an execution pauses at the AI
  review card and then at a separate native confirmation dialog;
- **Review once**: an execution pauses at the AI review card, and approving the
  card dispatches it without a second dialog; or
- **Auto-execute**: a persistent device-local standing permission dispatches
  AI-requested execution without a per-command review or confirmation.

Older approvals that do not contain a policy deserialize to the cautious
**Review + confirm every command** default. Disabling terminal permission resets
the stored policy to that default.

File and live-editor changes default to exact review. Enabling **Automatically
apply AI file and editor changes** bypasses that per-change review while keeping
path authorization, size limits, typed-provider validation, and SHA revision
checks. The **fully autonomous** convenience toggle enables this standing
auto-apply, live terminal-state access, and terminal **Auto-execute** together;
turning it off restores file review and confirm-every-command terminal behavior
without revoking the base capabilities.

Write access implies read access because a safe replacement must compare the
current file revision first. A direct host-network capability is reserved in
the contract but disabled in this slice; an executed shell command can still
use the Windows user's available networking. At least one capability must be
selected.

Saving or changing approval requires a warning confirmation. Revocation is
immediate. Permission changes are persisted directly rather than using
Settings **Apply** or **Save**, because a trust decision is neither a preview nor
a portable preference.

## Device-local state

Default paths are:

```text
%LOCALAPPDATA%\Hypershell\ai-permissions.device.json
%LOCALAPPDATA%\Hypershell\ai-operations.audit.jsonl
%LOCALAPPDATA%\Hypershell\ai-operations.audit.jsonl.previous
```

`HYPERSHELL_APP_DATA` relocates them for development and verification. The
permission file is versioned, bounded to 1 MiB, capped at 256 workspace roots,
validated on load, and replaced atomically. Each approval carries a workspace
ID, display label, canonical local root, root fingerprint, capability flags, and approval
time. The fingerprint binds the decision to the root path and directory
identity metadata; replacing the directory invalidates access until it is
reviewed again.

These files are structurally outside `settings.json` portable-profile data,
`.hyperprofile` bundles, sync checkpoints, conflict copies, extension package
state, and server storage.

## Path boundary

Every file request supplies an approved workspace ID and a relative path.
Hypershell rejects:

- absolute or rooted paths;
- normalized paths outside the approved root;
- unavailable or identity-changed roots; and
- symbolic links, junctions, and other reparse points along an existing path.

The host never exposes an outside-workspace override. A broader root must be
approved separately and visibly.

## File operations

The initial host surface supports:

- one-level directory listings capped at 512 entries;
- Unicode text reads capped at 4 MiB; and
- text creation or replacement capped at 4 MiB.

Reads preserve the detected encoding and line-ending metadata and return a
SHA-256 revision. Replacing an existing file requires that exact SHA-256; a
stale revision fails without writing. The replacement is prepared as a sibling
temporary file, checked against the current source again, and moved atomically.
The 4 MiB ceiling is checked against that fully encoded temporary file, so a
UTF-16 replacement cannot expand past the byte limit after validation.
New-file creation must be explicitly requested and its parent directory must
already exist. Delete, rename, recursive mutation, binary writes, and
outside-workspace operations are not exposed.

The provider can also request the live content of the active file-backed editor
document when that path belongs to the selected approved root. Reads and
full-document replacements are routed through the document's typed provider.
An editor replacement either pauses for exact local review or follows the
root's standing auto-apply policy. Both paths recheck the live content SHA-256,
route through the typed document provider, and leave the change visibly unsaved
after application. The provider never receives a raw `DocumentTabViewModel`.

## Terminal boundary

Live terminal-state permission is separate from terminal execution permission.
When granted, the provider can request the current draft command plus at most
400 recent transcript lines and 128 KiB of transcript text. The selected
terminal must currently be rooted inside the approved root. Each result is a
snapshot taken when the tool is called; Hypershell does not yet stream or push
terminal changes to the provider. Draft and transcript content are not written
to the audit log.

Terminal permission allows a command to be staged visibly without execution;
staging still pauses at the review card. Execution follows the root's saved
policy. Under **Review + confirm**, the review object records that a native
confirmation is required and only that confirmation path passes
`userConfirmed: true`. Under **Review once**, approval dispatches with
`userConfirmed: false`, which the broker permits because the saved root policy
is sufficient. Under **Auto-execute**, the tool host dispatches immediately and
returns the actual function result to the model without creating a pending
review. Hypershell selects or creates a terminal whose starting directory is the
approved root and caps command text at 32 KiB.

This is an invocation gate, not an operating-system sandbox. Under every policy,
the shell runs with the current Windows user's permissions and a command can
address resources outside its starting directory or use available networking.
The Settings warning states that consequence before a root can be saved with
standing permission. Approval can be downgraded or revoked at any time.

## Audit boundary

Sensitive operations fail closed when the local audit trail cannot record the
authorized attempt. The JSON-lines log records:

- UTC time;
- workspace ID;
- capability and operation;
- relative target metadata;
- authorization/outcome; and
- a bounded status description.

It never records file contents, replacement text, access keys, or terminal
command text. Terminal targets use only the first 16 hexadecimal characters of
a command SHA-256. The current log rotates to one `.previous` file at 2 MiB.

## Verification

Run the focused verifier:

```powershell
dotnet run --project tools\Hypershell.CSharpVerifier\Hypershell.CSharpVerifier.csproj -c Release -- --workspace-ai-only
```

It verifies default denial, multiple named roots, read-only and expanded capability grants, root
fingerprint invalidation, traversal and absolute-path rejection, bounded
listing/read behavior, atomic compare-before-write updates, stale-write
rejection, encoded-byte-limit enforcement, explicit creation, all three
persisted terminal execution policies, separately approved live terminal snapshots,
outside-file immutability, temporary-file cleanup, content-free audit records,
revocation, and audit rotation.

The separate `--workspace-ai-provider-only` verifier covers fixed-endpoint
OpenAI request construction, `store: false`, strict tools, DPAPI credentials,
live-editor routing, reviewed and autonomous file/editor paths, and confirmed,
review-only, and autonomous terminal routing. See
`AI_WORKSPACE_PROVIDER.md` for the provider data flow and setup boundary.
