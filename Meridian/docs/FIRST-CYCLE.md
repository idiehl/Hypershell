# First development cycle

Status checkpoint: **4 October 2026**. This milestone implements local canvas
authoring, basic Boolean logic and portable publication. It does not implement
the entire v0.9 handoff or deploy anything to atlasuniversalis.com.

## Implemented workflow

The local editor provides a project library, multiple canvases with an entry
canvas, nested nodes, collapse/expand, an outline, title/text search, media
attachments, spatial editing and visual/functional connections. Canvas nodes
open another canvas inside the current workspace. Node geometry, containment,
rules and references are editable in the inspector.

The demonstration includes a research collection, losslessly stored PNG and TXT
assets, and a Boolean decision system. Its input and switch controls are exposed
to viewers. The output is a sibling of the logic container: collapsing the
container hides the children while the output remains visible and retains its
computed result.

AND, OR, NOT, NAND, NOR, XOR and XNOR are implemented. NOT takes one operand;
the other gates take two. Each functional input has at most one source. A valid
complete circuit evaluates immediately and reacts to operand changes while Run
is enabled. An open switch or undefined operand clears dependent results to
undefined, shown as **Unknown**, rather than false or a previous value.
Functional cycles are rejected, including cycles through local references.
Visual cyclical relationships remain available.

All nodes, including variables and joints, have identities. A node has one
containment parent, and inherited rules may be narrowed but not widened.
References are read-only. Same-canvas references are live; references to another
canvas read the last saved snapshot. Deleting an original clears local references
immediately; other-canvas references keep the saved snapshot until a save records
the deletion. Missing references do not manufacture replacement values.

## Files and publication

| File | Purpose and behavior |
| --- | --- |
| `.meridian` | Editable ZIP project package. Stores a versioned project manifest and hash-named original asset bytes. All owned canvases travel together. Import preserves project identity when it is new to the local library; an existing identity requires an explicit copy workflow. |
| `.html` | Published viewer, authored project payload, logic and selected assets in one file. Opens directly from disk offline and can be hosted in an iframe. Import produces a fresh project and remaps included identities without executing the HTML. |

Publishing does not modify the source project or upload anything. A viewer uses
a runtime copy of the embedded authored state. Exposed input/switch controls,
navigation and collapse are temporary; Reset restores authored values, and
Save a copy downloads the authored publication rather than the visitor's current
control state. Run and Stop apply across the canvases in that viewer instance.
Opening a published file does not automatically start logic.

The publication dialog includes **Download External References?**. Existing
required dependency metadata selects this option initially. Included external
references are saved snapshots; excluded or unavailable requirements are listed.
Canvases owned by the project remain included regardless of that external option.
The first editor does not yet provide cross-project dependency discovery, remote
fetching, or the full authoring interface for dependency declarations.

Text and PNG/JPEG/WebP images can be authored/viewed directly. Attachments retain
their original bytes. TXT files have text previews; other documents can be
downloaded. Both editor and standalone viewer preview PDFs when the browser supports them,
with original-file open/download fallbacks. DOCX editing and rendering
fidelity are not implemented.

## Persistence and recovery

The ASP.NET Core host stores data under `%LOCALAPPDATA%\AtlasMeridian` by default;
`--data-root` selects an isolated location. Explicit saves advance a revision
and refresh saved reference snapshots. Recovery drafts and named checkpoints
are separate durable records. Undo/redo is in memory for the current editing
session, with up to 100 undo entries; it is not a persistent revision browser.

Project saves and recovery writes use separate concurrency checks. Stale saves
do not overwrite the committed project, and stale recovery tokens cannot
overwrite or delete another draft. A recovered draft based on an older project
revision requires **Save a copy**. Importing published HTML also creates a new
project and retains its pinned snapshots until an explicit save.

Package limits include 16 MiB per asset and 64 MiB of reconstructed asset bytes,
including assets in snapshots. They are validation limits, not performance
guarantees. Full API, package validation and size limits are documented in
[HOST.md](HOST.md).

## Validation checkpoint

The following reflects observed checks at this documentation checkpoint:

- TypeScript checking passed after core/editor integration fixes.
- Core tests passed for all seven gate truth tables, undefined propagation,
  collapse behavior, reference freshness/deletion, inherited rules, invalid
  graph rejection, identity remapping and asset preservation.
- Publication tests passed for inert embedded data, included/excluded snapshots,
  fresh-project import and malformed payload rejection.
- Host integration tests passed for persistence, restart, revision conflicts,
  recovery/checkpoint isolation, package round trips and rejected unsafe inputs.
- The final suite passes 30 model/host/publication checks and six workflow
  scenarios in each of Chromium and Firefox. These cover creation/save/export/
  import, offline HTML and narrow iframe, stale recovery across repeated reloads,
  failed recovery blocking navigation, ownership during a delayed save response,
  and geometry/link/history editing. The packaged executable passes ten host checks.
- Firefox uses a fresh isolated official Playwright runtime at
  `.cache/playwright-diagnostic` on this machine. The shared Firefox runtime
  fails Windows side-by-side activation; no system security settings were changed.
- The demo PNG was decoded and verified as a valid 32 × 32 RGB PNG; its original
  bytes are preserved through project and publication round trips.
- The self-contained Windows host and MyNet registration are installed. The live
  MyNet importer preserved the Ping Lab and HyperLib definitions. Launch occurred
  through normal MyNet command review, port selection and health checking.
- Native MyNet verification created the demonstration, autosaved a collapse change,
  closed the private editor, stopped the host, closed and restarted MyNet, and
  reopened the same project with its recovery prompt. Restoring the draft and saving
  succeeded at revision two with all 18 nodes and both original attachments.
- The example publication ran directly from disk with networking disabled while
  the Meridian host was stopped. No HTTP(S) requests occurred.
- The installed editor bundle SHA-256 matches the current built bundle. MyNet is
  left running with the demonstration available under its Atlas Meridian entry.
- Current lifecycle limitation: the console host requires MyNet's separate verified
  force-stop action after the normal managed stop attempt. It stops only the recorded
  host PID. Graceful command-based shutdown is a later integration refinement.

For repeatable commands, see the [README](../README.md). Browser tests run from
`tests/workflow.browser.ts`; build the browser assets and host before running
them. Test data is isolated from the user's default project store.

## Deferred capabilities

The following remain later milestones: continuous/audio signals, electrical
simulation, arbitrary numeric computation, computational feedback, reusable
module libraries, viewer snippet export, cell/sticky-edge layouts, advanced
document editing, collaboration, public accounts/repositories, and Atlas cloud
deployment. Full document/media indexing and a spatial repository of canvas
nodes are also not part of the current sidebar/library interface.

The current search covers titles and text in the selected canvas, including
hidden descendants in the outline. It does not index attachment contents or all
media metadata. Large-graph performance, mobile authoring and complete WebView2
compatibility require additional verification.

This was an intensive development cycle. The Hypershell secondary backlog was empty; no backlog item was incorporated. Existing Hypershell source work was not edited.
