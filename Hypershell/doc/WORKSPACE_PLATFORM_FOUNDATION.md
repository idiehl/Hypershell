# Hypershell workspace platform foundation

Status: workspace foundation and project work-item module implemented and
verified. This document separates code that exists today from the extension,
sync, and AI-hosting architecture that it is preparing.

## Product direction

Hypershell should remain a stable native workspace while allowing users to add
three different kinds of capability:

1. Hyperlib content packs: commands, examples, explanations, tutorials,
   reference entries, and language metadata.
2. Managed workspace contributions: panels, commands, text formats, and AI
   tools that run through host-controlled APIs.
3. Trusted native extensions: .NET components for capabilities that cannot be
   expressed safely through content or a web panel.

The shell, editor, terminal, profile manager, permission broker, and extension
host belong in the core application. Most language knowledge and optional
features should be independently installable packages. A curated starter set
can still ship with the app, so the default installation is useful offline.

## What exists in this slice

- The center workspace now has one tab abstraction for terminals and documents.
- The `+` control can create a terminal, create a text document, or open an
  existing text/code file.
- Text documents have new/open/save/save-as behavior, dirty-state indicators,
  Unicode encoding preservation, binary/oversize rejection, and close prompts.
- Monaco is hosted locally through WebView2. It provides syntax highlighting
  and editor behavior for a broad language set without requiring an internet
  connection after installation.
- Creating a document now opens a language-aware configuration dialog for its
  name, language mode, encoding, line endings, indentation, font, tab color,
  wrapping, minimap, line numbers, bracket guides, formatting assistance, and
  save cleanup rules.
- Every document exposes explicit encoding and line-ending selectors. UTF-8,
  UTF-8 BOM, UTF-16 LE/BE, and UTF-32 LE/BE are detected and written without
  silently changing the file. Uniform CRLF/LF/CR files retain their format;
  mixed endings remain preserved until the user selects a normalization target.
- Monaco now exposes host-routed Find, Find and Replace, Go to Line, and Format
  actions through toolbar controls and keyboard shortcuts. Format first asks
  the host for an installed, language-specific stdin/stdout adapter and falls
  back to Monaco's registered provider or bounded whitespace normalization when
  no external adapter succeeds. Formatting never runs through a terminal or
  command shell and never requests an in-place file edit.
- Document tabs, active-document identity, editor view state, language/editor
  options, encoding, and line endings are restored from a versioned device-local
  session. Dirty documents receive atomic recovery snapshots every five seconds.
  Recovery is limited to 50 open documents and 8 MiB per dirty document, can
  recover an untitled document or a document whose original file disappeared,
  and never writes state into a project directory or portable profile.
- Every editor tab is now bound to a host-owned typed document provider. The
  built-in text provider supplies range-checked operations for every language,
  Markdown adds a heading outline and structured rendering, and JSON adds parse
  diagnostics, a bounded structural outline, and all-or-nothing mutations that
  retain the original source when a proposed edit is invalid.
- Document providers declare `read`, `replace`, `insert`, and `export`
  operations plus their output formats. Provider resolution runs after Open,
  Save As, session restore, and extension refresh, and the active provider is
  visible in the document status bar.
- The editor and document-tab menu now export a separate standalone HTML or
  Microsoft Word copy. HTML export disables raw Markdown HTML, neutralizes
  unsafe links, replaces external images with placeholders, and embeds a
  restrictive content-security policy. Word export creates styled Open XML
  headings, lists, quotes, code blocks, numbering, and page metadata, then
  validates the package before writing it.
- Export is bounded to 8,388,608 source characters and a 64 MiB artifact. The
  chosen extension must match the format, the active source cannot be
  overwritten, output uses an atomic temporary sibling, and an artifact is
  discarded if the source changes during rendering.
- File Explorer and Command Builder now sit inside the same `PanelHost`
  control and registry contract intended for contributed panels. Both have
  stable core IDs and declared placement, movement, floating, sizing, and
  replacement policy metadata.
- Workspace Layout is now a fourth section in the central Settings manager.
  Every registered panel receives validated visibility, host behavior, slot,
  width, and height controls with Reset draft, Cancel preview,
  Apply-to-session, and Save-to-profile semantics.
- Saved panel IDs, slots, replacement targets, order, visibility, widths, and
  heights use the portable workspace snapshot contract. Layout restore clamps
  invalid dimensions and slots to each installed panel's current policy while
  retaining dormant entries for extensions that are absent or unapproved on
  this device.
- Extension manifest and portable/device-local profile contracts now exist in
  code, along with a JSON Schema and a local goal-tracker web-panel example.
- Startup and on-demand extension discovery now inspect one package per folder
  beneath the application-data `extensions` directory. Discovery is read-only:
  it does not execute entry points, create WebViews, register active panels, or
  grant requested capabilities. Valid packages also receive a deterministic
  SHA-256 package-content hash with package-size and file-count limits.
- Runtime validation rejects unknown or missing manifest fields, numeric enum
  values, incompatible schema versions, invalid IDs/versions, missing
  capability declarations, package path traversal, missing resources, reparse
  points, contradictory panel policies, duplicate package IDs, and duplicate
  contribution IDs.
- Validated `ContentOnly` packages can now contribute static Hyperlib command
  libraries. The host resolves either a JSON `contentPath` or a directory-level
  `hyperlib.json`, caps each content file at 16 MiB, validates the complete
  library atomically, and qualifies library and entry IDs with the owning
  extension ID before they enter the Command Lexicon.
- Hyperlib validation now enforces schema and semantic versions, declared
  language and execution-target boundaries, unique IDs and parameters,
  substantive descriptions and definitions, bounded examples, safe relative
  image assets, HTTP(S) documentation links, and execution support disclosure.
  One invalid contribution quarantines the complete package; partially trusted
  package content is never mixed into the catalog.
- Content packages declare whether material is human-authored, AI-assisted,
  AI-generated, or imported, plus a draft/reviewed state, provenance statement,
  and reviewer for reviewed content. Hypershell preserves this publisher
  metadata separately from its own successful host-validation result.
- Language Tools is now a fifth section in the central Settings manager. Its
  read-only device snapshot distinguishes runtimes, formatters, and language
  servers; built-in, detected-native, missing, and ignored command-wrapper
  states; resolved executable locations; language coverage; and invocation
  boundaries. Refresh never starts a process, installs a package, or contacts a
  registry.
- Extensions is now a sixth section in the central Settings manager. It shows
  package identity, runtime, hash, contributions, requested capabilities,
  validation diagnostics, quarantine status, Hyperlib entry counts, content
  provenance/review state, and the exact discovery location. Refreshing this
  section also reloads the Command Builder catalogs for the current session.
- Profiles is now a seventh section in the central Settings manager. It exports
  a strict 4 MiB `.hyperprofile` envelope with independent bundle/profile
  versions and a deterministic SHA-256 content hash. Choosing a bundle performs
  a non-mutating preview with strict UTF-8/schema validation, tamper detection,
  unknown-member rejection, and missing extension/library/terminal diagnostics.
- Profile **Apply** previews the runtime-capable application, terminal,
  Explorer-display, and panel-layout subset for the current session. Profile
  **Save** first atomically writes a merged settings candidate, then adopts its
  command profiles and subscription metadata. Both paths preserve local
  projects, paths, terminal executables and startup commands, extension/AI
  approvals, credentials, open documents, editor recovery, WebView data, and
   panel state. See `doc/PROFILE_PORTABILITY.md` and
   `doc/schemas/hypershell-profile.schema.json`.
- The Profiles section now includes a user-initiated authenticated sync client.
  It stores the endpoint/account ID and ETag checkpoints on the device, and can
  retain the access key only as a separate Windows DPAPI `CurrentUser` protected
  blob when the user opts into automatic sign-in. The app-lifetime session token
  remains memory-only. Metadata is listed separately from content, downloads
  pass through the local inspector, and create/update/tombstone requests use
  strong ETags. Conflicting uploads preserve inspected local and remote
  `.hyperprofile` copies without advancing the checkpoint. See
  `doc/PROFILE_SYNC.md`.
- A matching cross-platform ASP.NET Core profile service now owns
  administrator-provisioned accounts, PBKDF2-hashed access keys, expiring
  in-memory sessions, per-account storage isolation and quotas, strict
  server-side portable-profile validation, atomic ETag mutations, immutable
  revisions, and tombstones. Key rotation, account disablement, restart
  persistence, revocation, and two-account isolation are covered by a live
  Kestrel verifier. A non-root, read-only-root Docker package and loopback-only
  Compose sample are included. The private service is deployed behind TLS at
  `https://hypershell.atlasuniversalis.com/`. See `doc/PROFILE_SERVER.md`.
- Profile authentication now survives Settings-window closure and can restore
  automatically at application startup. Near-expiry tokens renew on demand;
  offline startup stays local-first; rejected saved keys are removed; clean
  shutdown revokes the in-memory token while retaining the protected key; and
  explicit sign-out forgets both. Deterministic lifecycle tests and a read-only
  production smoke verifier cover these boundaries.
- AI Access is now an eighth section in the central Settings manager. It manages
  multiple named device-local roots and independently grants bounded reads,
  atomic compare-before-write text mutation, live terminal snapshots, and
  terminal staging/execution. Absolute/escaping file paths and reparse-point
  traversal are rejected; replacing a root invalidates its fingerprint; direct
  network, delete, and outside-workspace file operations are not exposed.
- A host-owned workspace operation service now caps text at 4 MiB and directory
  listings at 512 entries, preserves detected text encoding, requires the
  current SHA-256 before replacing a file, rechecks it before an atomic move,
  and enforces the root's persisted confirm, review-only, or auto-execute
  terminal policy. The bounded
  rotating audit stores decisions and target metadata but never contents,
  replacement text, credentials, or commands. See
  `doc/WORKSPACE_AI_PERMISSIONS.md`.
- Structurally valid static Hyperlib content becomes available without an
  executable-extension approval because the package itself receives no code,
  terminal, file, network, secret, or AI capability. A user can still choose to
  compose and run a catalog command through Hypershell's normal core terminal
  workflow.
- Validated `ContentOnly` packages may also declare static `TextEditor`
  extension aliases through the `DocumentProvider` capability. The alias only
  selects a host-owned text, Markdown, or JSON provider; package code is never
  loaded. Core-owned extensions always win, and every mapping involved in a
  multi-package extension conflict remains dormant. Settings > Extensions
  exposes active/dormant mappings and their reasons. See
  `doc/DOCUMENT_PROVIDERS.md` and the `markdown-notes-alias` sample.
- A validated `WebView` package can receive an explicit device-local panel
  approval bound to its extension ID, version, and exact package hash. A changed
  package therefore returns to inactive state until it is approved again.
- WebView panels may now opt in to host message API v1 through an explicit
  `messageApiVersion` declaration. The API performs a mandatory handshake,
  verifies the exact virtual origin and approved capabilities for every
  request, rejects unknown fields/methods, and caps JSON depth, payload size,
  request rate, and replayable request IDs.
- The v1 allowlist contains extension-local device state, bounded theme and
  workspace context, and one deduplicated context-change event. Workspace
  context requires actual `WorkspaceMetadata` approval and exposes project/tab
  labels only—never paths, file contents, terminal transcripts, or commands.
  Panel state is a 48 KiB JSON object stored atomically beneath a host-owned,
  extension-and-panel-qualified application-data path.
- Approved local web panels can replace File Explorer or Command Builder, open
  in the Bottom host, or open as resizable Floating windows. Closing or revoking
  a panel restores the built-in surface. Only one contributed panel may occupy
  each replacement or Bottom host; multiple Floating panels remain legal.
- The title-bar layout control now enters an explicit customization mode.
  Movable approved panels expose a host-owned drag handle and can be dropped on
  policy-validated File Explorer, Command Builder, Bottom, or Floating targets.
  Unavailable targets are dimmed with the manifest rejection reason. A move is
  session-only until **Save layout** is chosen; **Cancel changes** restores the
  exact layout snapshot captured when customization began.
- The WebView panel host maps only the approved package directory and blocks
  external top-level navigation, external resources, downloads, permission
  requests, new windows, host objects, browser accelerators, and developer
  tools. Web messages stay disabled for panels without an API declaration;
  declared v1 messages are origin-bound and method-allowlisted. No file,
  terminal, network, secret, native-code, or AI API is exposed in this slice.
- A host-owned language-tool registry now describes reusable runtime,
  formatter, and language-server adapters independently from editor tabs.
  Runtime entries are discovery diagnostics only. External formatting is an
  explicit action that uses direct native-process launch, argument-list
  boundaries, stdin/stdout, a 10-second timeout, 8 MiB input/output limits,
  64 KiB diagnostics, and process-tree termination on timeout or cancellation.
  `.cmd` and `.bat` wrappers are reported but not launched through a shell.
- A language-server process host now owns stopped/starting/running/stopping/
  faulted lifecycle state and direct stdio streams. It remains stopped by
  default and has no protocol client or automatic activation in this slice.
  See `doc/LANGUAGE_TOOLING.md` for its current contract and trust boundary.
- A native Project Work Items tool now shares the customizable Explorer dock.
  Each saved project or folder can own an ordered `.hypershell/work-items.json`
  backlog with starred entries, inline creation/editing, expandable details,
  project-local attachment copies, deletion, refresh, and drag reprioritization.
  Hypershell AI receives the selected backlog as secondary work-cycle context
  only when its active Context grants read access to the project path. A scoped
  `AGENTS.md` convention gives repository-based Codex cycles the same optional
  backlog behavior. See `doc/PROJECT_WORK_ITEMS.md`.

The following are not implemented yet: native extension loading, production
profile-service backup operations, public registration or account recovery,
repository/document-object synchronization, a language-server
protocol client and automatic server activation, debuggers, PDF export, binary
Office/PDF import or rich editing,
executable third-party document viewers/providers, document collaboration,
package signatures, automated example execution for community
packs, and a package gallery.

## Workspace architecture

The intended dependency direction is:

```text
MainWindow
  -> WorkspaceTabHost
       -> TerminalTab
       -> TextDocumentTab (Monaco/WebView2)
       -> contributed document views
  -> PanelHost(left/right/bottom/floating)
       -> built-in File Explorer
       -> built-in Command Builder
       -> contributed panels
  -> ExtensionRegistry
       -> content packages
       -> isolated web contributions
       -> approved native .NET extensions
  -> ProfileService
       -> portable profile snapshot
       -> device-local trust and path mappings
  -> PermissionBroker
       -> AI operations
       -> extension operations
       -> audit history
```

`MainWindow` should eventually consume a `WorkspaceTabHost` service instead of
knowing every tab type. The tab interface, panel registry, and panel host added
in this slice establish that migration path without changing the familiar
workspace layout.

## Extension package contract

Every package has a `hypershell.extension.json` manifest. The current contract
is defined by:

- `src/Hypershell/Extensibility/ExtensionContracts.cs`
- `doc/schemas/hypershell-extension.schema.json`
- `samples/extensions/goal-tracker/hypershell.extension.json`
- `doc/EXTENSION_PANEL_MESSAGING.md`
- `doc/schemas/hypershell-panel-message.schema.json`
- `doc/schemas/hyperlib-command-library.schema.json`
- `samples/extensions/powershell-file-essentials/hypershell.extension.json`
- `doc/DOCUMENT_PROVIDERS.md`
- `samples/extensions/markdown-notes-alias/hypershell.extension.json`

The manifest declares its runtime, requested capabilities, contributions, and
panel placement constraints. The host—not extension code—owns final placement,
layout persistence, focus, resizing, and restoration.

### Runtime tiers

| Tier | Intended use | Trust model |
| --- | --- | --- |
| `ContentOnly` | Hyperlib JSON/Markdown, templates, snippets, static metadata | Validate data; no executable code |
| `WebView` | Local panels and a narrow state/read-only-context message API now; document viewers later | Separate web process, exact-hash approval, explicit API declaration, and approved host capabilities |
| `NativeDotNet` | Deep integrations that need native WPF/.NET access | Full-trust install, confirmed on each device |

A native .NET extension runs with the application's effective privileges.
`AssemblyLoadContext` can help load and unload dependencies, but Microsoft
explicitly states that it is not a security boundary. Capability declarations
are therefore useful for disclosure, review, and policy, but they do not turn a
native extension into a sandbox.

### Quality gates

Installing or publishing a package should require all of the following:

1. Validate the manifest against the versioned JSON Schema.
2. Reject absolute paths, traversal, duplicate contribution IDs, unknown
   capabilities, and incompatible contract versions.
3. Validate every Hyperlib entry against its content schema and resolve all
   internal links.
4. Run deterministic example checks where a safe runner exists.
5. Produce a package hash and signature metadata.
6. Show requested capabilities and publisher identity before activation.
7. Require per-device confirmation for executable web/native packages; bind
   every approval to the exact package version and hash.

AI can draft or normalize library content, but it should not bypass these
checks. Generated guides need provenance, automated verification results, and a
human-review state before they enter the curated catalog.

### Current local Hyperlib activation flow

1. Create an unpacked package with `tools/New-HyperlibPackage.ps1`, or prepare
   the same structure manually.
2. Declare `LibraryContent`, a library contribution, content origin, review
   state, provenance, and a JSON file or directory `contentPath` in the
   manifest.
3. Open Settings > Extensions. **Install folder** validates an unpacked
   authoring directory; **Pack folder** creates a deterministic `.hyperlib`
   archive; **Install archive** stages and installs that archive.
4. Hypershell validates the
   manifest, package hash, complete Hyperlib library, assets, guide quality,
   authorship disclosure, and review metadata.
5. A fully valid package is marked **VALIDATED · CONTENT ACTIVE** and its
   namespaced library becomes available in the Command Lexicon. A failed entry
   quarantines the package and exposes no content.
6. Select or save the library through the normal language-scoped Command
   Builder profile. No executable extension approval or host API is granted.
7. Updates require confirmation and preserve the replaced package in the
   device-local recovery area. Removal also moves content to recovery instead
   of permanently deleting it. See `doc/HYPERLIB_PACKAGES.md`.

## Panels and layouts

Panel contributions declare:

- preferred and allowed slots;
- minimum, preferred, and maximum width;
- minimum, preferred, and maximum height;
- whether the user may move, resize, or float the panel;
- whether it is eligible to replace File Explorer or Command Builder.

The manifest rules are enforced during every layout restore. A profile stores
the user's chosen placement only when that placement is legal for the installed
manifest version. If an update changes the allowed locations, the host falls
back to the manifest's preferred location and explains the change.

Layouts are profile-saved and locked during ordinary use. Workspace Layout can
preview, cancel, apply, or save validated built-in docking plus approved
extension replacement, Bottom, and Floating presentation. Bottom panels also
have a direct vertical resize grip and Floating windows are resizable. The
explicit title-bar customization mode enables direct movement between these
hosts without making panel headers draggable during ordinary work. Drop targets
are evaluated against the currently installed manifest before WPF accepts the
move. A replacement or Bottom conflict hides the displaced contributed panel;
the status message names it so the result is not silent.

File Explorer and Command Builder have stable surface IDs, are registered as
built-in panel contributions inside `PanelHost`, and restore their saved dock
and width through the same validator used by approved extension panels. Dynamic
replacement uses those existing hosts rather than a special overlay path.

### Current local WebView activation flow

1. Put one package folder beneath the application-data `extensions` directory.
2. Open Settings > Extensions and inspect its validation diagnostics,
   capabilities, version, and SHA-256 package hash.
3. Choose **Approve local panels** and confirm the device-local scope.
4. A panel that declares message API v1 may handshake and use extension-local
   state. It receives workspace context/events only when `WorkspaceMetadata`
   was both requested and approved.
5. Preview the panel by replacing a built-in surface or opening it at the Bottom
   or Floating host.
6. Use the title-bar layout control to drag the active panel between allowed
   hosts. Choose **Done for session**, **Cancel changes**, or **Save layout** at
   the explicit commit boundary.
7. Use Workspace Layout **Apply** for a session-only preview or **Save** to keep
   detailed placement and dimensions in the portable profile. Approval remains
   device-local and never travels with that layout.
8. Revoke approval to close the package's panels immediately. A package update
   also invalidates approval automatically because its hash changes.

## Profiles and cloud storage

Profiles should be local-first. The server is a synchronization origin, not a
requirement for opening the app or editing a document.

The local-first portion, client-side protocol, session sign-in, and reference
account service are now implemented.
The app exports and imports the versioned portable configuration described in
`doc/PROFILE_PORTABILITY.md`, validates its integrity before enabling Apply or
Save, and retains absent extension/panel references as dormant configuration.
The opt-in client in `doc/PROFILE_SYNC.md` adds short-lived session bearer
authentication, strong ETag/server-revision checkpoints, conditional writes
and tombstones, and conflict copies. The single-instance service in
`doc/PROFILE_SERVER.md` supplies administrator-managed accounts, isolated
durable profile revisions, quotas, and revocation. It is deployed at the named
HTTPS host, and the desktop can restore its account session using a
Windows-protected device credential without making profile content sync
automatic. Independent document-object synchronization remains future work.

Portable data includes:

- app and workspace preferences;
- enabled extensions and library subscriptions;
- legal panel placements;
- remote workspace/repository IDs;
- relative document paths and restoration state;
- non-secret extension settings.

Device-local data includes:

- native-extension approvals and exact package hashes;
- workspace-to-local-folder mappings;
- AI read/write/terminal/network approvals;
- credentials, tokens, and OS key-vault references;
- machine-specific executable paths.

Documents should not be embedded in the profile JSON. Store them as repository
objects with revisions, while the profile references repository and relative
path. This keeps profile synchronization small and makes conflicts, history,
and large files manageable.

A minimal server protocol needs profile manifests, content blobs, revisions or
ETags, optimistic conflict detection, and tombstones. A first implementation
can use the existing server as private storage; it does not need real-time
collaboration. Conflict copies are safer than silent last-write-wins behavior.

## AI access model

The requested end state—AI with file, terminal, and document access—is routed
through one permission broker rather than direct UI coupling. The enforceable
broker, OpenAI Responses adapter, filesystem/terminal host adapters, and typed
open-document path are now connected.

Implemented default:

- read/write is scoped to the selected approved root;
- multiple named root approvals may be remembered on the current device;
- terminal execution is a separate capability;
- direct host network and outside-root file tools are not exposed;
- file and open-document mutations require exact review by default or may use a
  separately saved standing auto-apply policy;
- native extension and AI approvals never arrive silently through profile sync;
- every write and command has a compact local audit record.

The initial file surface deliberately omits deletion, binary mutation,
recursive writes, an outside-workspace override, and direct network tools.
Terminal approval permits visible staging and offers confirm-every-command,
review-only, and standing auto-execute policies. A normal shell retains the
Windows user's ambient filesystem and network access, so the latter policy can
run destructive or outside-root commands without a per-command prompt.
See `doc/WORKSPACE_AI_PERMISSIONS.md` for the exact boundary.

Document providers expose typed operations (`read`, `replace`, `insert`,
`export`) so the agent can work with text, Markdown, JSON, office formats, and
future formats without pretending every document is a plain file. The same
broker can route safe operations to installed extensions.

## Language support layers

"Supports a language" should be reported by feature, because the layers have
very different costs:

1. syntax and editing;
2. Hyperlib guides/reference/tutorials;
3. runtime or compiler detection;
4. completion and diagnostics through a language server;
5. formatting, testing, and debugging.

Monaco supplies broad syntax coverage. The host detects a curated set of
installed runtimes, stdin/stdout formatters, and future language servers
without downloading tools. Python, JavaScript, TypeScript, and Java now have
compact official guide packs plus persistent Python/Node.js, capability-gated
TypeScript, and isolated compiled-Java command runners. Saved Java documents
also use the reusable compiler/runtime adapter. HTML, CSS, and browser APIs have separate official
guidance packs, compatible-editor insertion, language-specific document
templates, and saved-page browser preview without being mislabeled as terminal
languages. Hypershell does not install Python, Node.js, Go, Rust, Java,
language servers, compilers, or debuggers. Hyperlib packages can add learning
content cheaply, while new executable runtime integrations require an explicit
host-owned adapter.

## Distribution strategy

Keep one Hypershell application, but do not force all optional content into one
installer. Recommended packaging:

- Core: WPF shell, terminal, editor, profile/extension host, PowerShell starter
  content, and a compact multi-language starter library.
- Official packs: deeper C#, Python, JavaScript/TypeScript, web, Java, Go, Rust,
  and other language material.
- Community packs: additional frameworks, APIs, modules, panels, and tools.
- Per-user cache: downloaded packages stored once by content hash and shared by
  profiles on the device.

This preserves a single coherent product while making download growth
incremental. WebView2's Evergreen runtime can be detected and installed through
Microsoft's supported bootstrapper instead of bundling a full browser runtime
into every Hypershell update.

## Delivery sequence

1. **Implemented:** validated left/right panel placement, resizing, live
   preview/cancel, and durable portable layout persistence.
2. **Implemented:** read-only extension discovery, strict manifest/resource
   validation, quarantine diagnostics, and inactive panel-candidate creation.
3. **Implemented:** exact-hash, device-local WebView panel approval plus
   replacement, Bottom, and Floating hosts.
4. **Implemented:** explicit workspace customization mode with manifest-checked
   drag-and-drop movement, conflict disclosure, snapshot cancel, session-only
   completion, and durable save.
5. **Implemented:** strict, atomic `ContentOnly` Hyperlib loading with
   namespaced catalog IDs, authorship provenance, publisher review state,
   Settings diagnostics, and live catalog refresh.
6. **Implemented:** origin-bound WebView panel message API v1 with explicit
   manifest opt-in, handshake, approved-capability disclosure, atomic
   device-local state, bounded read-only context/events, strict limits, and a
   host-connected goal-tracker sample.
7. **Implemented:** editor navigation, explicit encoding/EOL controls, safe
   in-editor format routing, bounded atomic recovery, and document-session
   restoration.
8. **Implemented:** reusable host-owned runtime, bounded stdin/stdout formatter,
   and stopped-by-default language-server process adapters with read-only
   Settings diagnostics and explicit Monaco fallback.
9. **Implemented:** host-owned typed text, Markdown, and JSON operations;
   standalone sanitized HTML and validated Word export; atomic bounded writes;
   and conflict-safe `ContentOnly` extension aliases with Settings diagnostics.
10. **Implemented:** strict, bounded, integrity-checked local profile
    export/import with non-mutating inspection, compatibility diagnostics,
    session Apply, atomic persistent Save, and device-local state preservation.
11. **Implemented:** authenticated server protocol and Settings workflow
    with session-only bearer credentials, strong ETags/server revisions,
    conditional create/update/tombstone operations, local validation, stable
    no-op hashes, device-local checkpoints, and two-sided conflict copies.
12. **Implemented:** a self-hostable, account-scoped ASP.NET Core backend,
    administrator provisioning, hashed access keys, short-lived session
    issuance/sign-out, per-account immutable profile storage, strict validation,
    conditional revisions/tombstones, quotas, revocation, Docker packaging, and
    a real two-account integration verifier.
13. **Implemented:** deploy the service to the named server with a final HTTPS
    endpoint, reverse-proxy edge controls, durable storage, health monitoring,
    and first real account provisioning. Keep document-object synchronization
    separate.
14. **Implemented:** activate the production account in the desktop with an
    app-lifetime session, optional DPAPI-protected automatic sign-in, on-demand
    renewal, local-first offline behavior, visible title-bar status, explicit
    credential forgetting, and a non-mutating production smoke check.
15. **Implemented:** add the device-local permission broker, exact-root
    fingerprinting, traversal/reparse guards, bounded workspace list/read,
    atomic SHA-256-preconditioned text writes, terminal staging and fresh
    execution confirmation, Settings management, and a content-free rotating
    audit. External AI-provider and typed open-document routing remain separate.
16. **Implemented:** the first package-authoring/distribution slice: a starter
    authoring tool plus deterministic `.hyperlib` packing, staged validation,
    install, hash-aware update confirmation and rollback, recoverable removal,
    Settings controls, and an automated lifecycle verifier. The visual author,
    package index/cache, and publisher signatures remain separate follow-on
    work.
17. **Partially implemented:** a centralized command-language registry,
    language-specific composition vocabulary and editor/terminal primary
    actions, persistent Python and reusable Node REPL terminal adapters,
    guarded external-language dispatch, expanded official
    Python/JavaScript/Node.js/TypeScript catalogs, a reusable compiled-language
    toolchain contract with isolated Java and C++ adapters, and non-terminal
    HTML/CSS/Web API packs with compatible-editor creation/insertion, document
    starters, and saved HTML browser preview. Expand next into Go, Rust, and other
    language material; add visual package authoring, an index/download client,
    content-hash caching, and curated update metadata.
18. Complete the full Language Server Protocol client above the existing
    stopped-by-default process host: initialization, document sync, diagnostics,
    completion, hover, symbols, rename, code actions, configuration, consent,
    and restart/backoff UX.
19. Add full-trust native extensions only after signing, hashing, per-device
    approval, recovery mode, and disable-on-crash behavior are ready.

## Reference technologies

- Monaco editor: <https://github.com/microsoft/monaco-editor>
- WebView2 distribution: <https://learn.microsoft.com/microsoft-edge/webview2/concepts/distribution>
- .NET `AssemblyLoadContext` security note: <https://learn.microsoft.com/dotnet/api/system.runtime.loader.assemblyloadcontext>
