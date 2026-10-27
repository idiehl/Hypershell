# Canvas library and recursive containment

> Historical implementation record. The current home canvas uses the same
> ordinary editor and graph as every workspace; Section/Subsection controls and
> the separate library authoring interface were removed. See
> [One canvas interface](UNIFIED-LIBRARY.md) for the current migration, persistence,
> discovery, recovery and history contract.

> The later [spatial disclosure pass](SPATIAL-LIBRARY.md) supersedes this milestone's enclosing library frames, double-click navigation and automatic whole-library arrangement on disclosure. Current pointer, placement and contents behavior is described in [SPATIAL-NODES.md](SPATIAL-NODES.md).

The home screen is a Meridian canvas rendered by the same `src/scene.ts` scene
used in the editor and published viewer. Saved canvases appear as canvas nodes
inside sections and subsections. The library is independent of the content of
the canvases it opens. The interface calls the primary workspace a **Canvas**;
existing portable files, internal `MeridianProject` types and `/api/projects`
routes retain their established names and identities.

This milestone implements the spatial repository in handoff N43 and the title,
hierarchy and workspace-opening portions of N44. The handoff's optional canvas
images and repository relationship editing remain unimplemented. Existing
ordinary-workspace visual and functional connections are separate capabilities.

## Using the home canvas

- **New canvas** creates a saved editable canvas in its own window. **Import**
  opens a portable canvas or an exported Meridian HTML file in its own window.
  The home library stays open and adds the newly saved canvas to its hierarchy.
- **New section** creates a root section. Select a section to create a
  **New subsection**, rename it, or move it into another section or canvas node.
- Disclosure controls collapse or expand a node's descendants. A nested node's
  own collapsed state is preserved when an outer branch is hidden and revealed.
  Sibling branches remain independent. Expanding the library hierarchy does not
  open the target workspace or load its node contents into the library.
- **Open canvas** opens the selected saved canvas in a dedicated window, keeping
  the home library's camera, selection and organization in place. A
  library node can target a specific inner canvas as well as a saved workspace's
  entry canvas.
- Search finds library node titles, including hidden descendants. Choosing a
  result reveals its ancestors, selects the match and frames it in the canvas.
  This search does not search the text inside the saved workspaces; the editor's
  existing content search remains available after opening one.
- Drag a node to move its entire branch. Pan the background and use the shared
  scene's zoom and fit controls. **Arrange** restores the library's nested layout.
  Library undo and redo apply to organization changes within the open session.

Library organization is edited in the canvas and its floating selection controls.
There is no library sidebar. An open workspace can still show its own internal
canvases and content outline under **In this canvas**; the **Canvas library**
action returns to the home canvas.

## Layout and discovery

The layout measures each expanded branch and places children within their parent,
with room for its title, summary and opening control. Collapsed branches have
compact display bounds; expanded branches expose the nested hierarchy.

Disclosure, section creation, reparenting, revealing hidden ancestors and **Arrange** recompute
the nested arrangement. Dragging moves only the selected branch and preserves its
relative descendant offsets. Coordinates are saved, but a later arrange operation
may replace a manual position. The library's pan/zoom camera is session state.

Opening the library reconciles its saved hierarchy with the committed canvas
catalog. Newly discovered saved workspaces receive nodes, and existing
canvas-launcher relationships help establish an initial child-canvas hierarchy.
Additional canvases are included without generating containment cycles. Existing
library titles, parent choices, disclosure states and existing coordinates are retained.
Discovery positions only new branches. Searching for an already visible node
changes the camera without rearranging the library. Renaming a library node changes
its library label, without renaming the saved source workspace.

Unavailable saved references remain in the library so that their organization is
not discarded. This is a local library, without accounts, community publication,
automatic cross-machine synchronization or a global canvas identity service.

## Ordinary nodes also contain children

The ordinary editor's **Contents** controls add child text or a child container
to the selected node. Other node creation tools also use the current selection
as the parent, including a selected canvas launcher. Adding a child expands its
parent and makes room for the new child. The **Parent** selector reparents an
existing node; invalid cycles and inherited-rule conflicts are rejected.

Collapse controls hide descendants without deleting them or changing the parent's
own media, value controls or launcher action. Ordinary workspace collapse retains
authored expanded geometry. The home library's automatic arrangement is a separate
layout policy. Moving a branch moves its descendants and internal manual link
routes together. These behaviors use the shared scene in the editor and viewer.

The existing document model has one containment parent per non-root node. This is
the implemented model, not a claim that the handoff's open multiple-parent or
canvas-appearance identity decisions have been settled. Spreadsheet-style live
cell containers, a canvas-thumbnail editor, and separate-window launching from
embedded ordinary-workspace canvas launchers remain outside this milestone.

## Persistence and API access

The library lives in `<data-root>/library.json`. Its entries store stable IDs,
title, kind, parent ID, collapsed state and absolute coordinates. Canvas entries
also identify their saved workspace and optional inner canvas. Saving library
organization does not modify saved workspace contents, their recovery drafts,
reference snapshots or checkpoints.

The API exposes:

| Endpoint | Purpose |
| --- | --- |
| `GET /api/library` | Read the saved hierarchy and its revision. |
| `PUT /api/library` | Atomically replace a validated hierarchy at an expected revision. |
| `GET /api/library/catalog` | Read lightweight committed canvas metadata and launcher relationships. |

Writes use `{ library, expectedRevision }`. A stale revision returns HTTP 409.
The editor keeps the unsaved library layout visible, offers **Download layout**,
and requires an explicit **Reload library** action before replacing it with saved
state. The host retains the preceding committed file as `library.json.bak`.
Downloaded layout JSON currently supports preservation and API-based recovery;
there is no dedicated layout-import dialog. Undo history is not persisted across
reopening. See [the API contract](LIBRARY-API.md) for field validation, access
restrictions and atomic-write details.

## Reusable composition interfaces

`src/core/library.ts` provides pure operations on library documents:

| Function | Result |
| --- | --- |
| `reconcileLibrary(library, catalog)` | Adds newly discovered canvases while keeping existing library identities and organization. |
| `libraryBranch(library, id)` | Returns the selected entry and all descendants in a cycle-safe set. |
| `arrangeLibrary(library)` | Returns a copy with nested positions recomputed. |
| `moveLibraryBranch(library, id, x, y)` | Returns a copy with the branch translated together. |
| `reparentLibraryEntry(library, id, parentId)` | Returns an arranged copy under the chosen parent, rejecting a move into its own descendants. |
| `libraryScene(library, catalog)` | Produces the common scene document used to render the home canvas. |

These helpers prepare data; they do not write storage. A caller should read the
current document, preserve existing entry IDs, apply its change, and submit the
result to the revision-checked endpoint. For example:

```ts
import { reparentLibraryEntry } from '../src/core/library.ts';

const next = reparentLibraryEntry(savedLibrary, canvasEntryId, sectionEntryId);
const body = { library: next, expectedRevision: savedLibrary.revision };
// PUT body to /api/library. On HTTP 409, retain next and review the current state.
```

For the content inside a saved canvas, use the existing declarative authoring API
and `scripts/author.mjs` workflow described in [AUTHORING.md](AUTHORING.md).
Recipes support `parent`, `collapsed`, canvas targets and layouts, so nested
content can be authored in batches without manually constructing identities.
The authoring CLI does not currently expose dedicated library-edit commands;
library automation uses the pure helpers and host endpoints above.

## Verification scope

`tests/library-host.test.ts` covers API validation, revision conflicts, concurrent
writes, durable backups, restart persistence, catalog projection and preservation
of unavailable references. `tests/library.browser.ts` exercises recursive section
and canvas disclosure, section creation/renaming/reparenting, search reveal,
workspace targets, separate-window opening and the narrow home layout.
It also checks branch dragging through undo, redo and reopening, and conflicting
library windows that preserve the rejected local layout for download without
overwriting the newer saved revision.
`tests/containment.test.ts` and `tests/containment.browser.ts` cover ordinary node
containment and the shared renderer. Test code uses isolated storage; it must not
be pointed at the running user's canvas library. Test declarations describe the
intended regression coverage; the task report records actual run results.

## Verified installation

The October 5, 2026 build passed TypeScript checking, 104 unit/integration checks,
and all 32 browser scenarios in each of Chromium and Firefox. Live installation
verified four saved top-level canvases and 293 inner canvas nodes, nine unchanged
canvas/recovery/checkpoint files, matching compressed and uncompressed frontend
assets, and opening the expanded atlas and returning to the library. A final
style-only correction removed two-pixel wrapped-title clipping, verified against
the live collection. Evidence: `output/library-qa/completion-report.json` and
`output/library-qa/installed-home.png`.
