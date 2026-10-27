# Clusters, reusable modules and node dependencies

A module is a packing node containing ordinary Meridian nodes and their internal
connections. The local module library stores an immutable definition of that
assembly. Inserting it creates an independent copy with fresh identities;
subsequent edits to the source, definition copy or another instance do not update
that copy.

This milestone covers the local Boolean-system implementation of handoff
N47-N49, N56-N58 and N60-N61. It does not add module versions, cloud sharing,
remote dependency fetching, waveform processing or electrical simulation.

## Lock snapped nodes into a Cluster

A snapped edge is a visual relationship without a connecting line. With sticky
alignment enabled, dragging independent nodes until their actual shapes touch
records that relationship. Hover over the shared edge to reveal a lock. Click it
to lock the two nodes' relative positions into a Cluster. Locking another shared
edge joins that node to the same group.

Dragging, keyboard movement and position edits move the complete locked group,
including its descendants and route bends. Arrange carries locked peers through
the same movement; incompatible requests are rejected with an unlock message.
Resizing stops at nearby nodes;
changes that would overlap members of a Cluster are rejected. A lock is stored
on its snapped relationship and survives saving, reopening and module reuse.
Use the edge lock or the **Links** panel to unlock it. An unlocked snapped
relationship disappears when the nodes move apart.

Clusters are not containers or saved templates. Locking neither changes parent
relationships nor creates a Module Library entry. Ordered container cells and
Gallery items are positioned by their parent and cannot be independently locked
into a Cluster. A bounded container remains the movement boundary for its
children.

## Create a named collapsible Cluster

Select two or more sibling branches and choose **Create Cluster** in the selection
toolbar, the right-click menu, or the Relations panel. The nodes can be separated;
touching edges are not required. Give the Cluster a name and choose a **Square**
or **Circle** collapsed button. Creation starts expanded and is one undoable edit.

The Cluster groups the original branches without duplicating them. Their GUIDs,
content, links, relative positions, and individual disclosure states remain intact.
Selecting a member of an edge-locked assembly includes the complete assembly so
that hiding part of it cannot sever the existing lock.

Choose **Collapse Cluster** in Properties or Relations, or use the expanded
group's usual hover collapse bar. Its contents hide behind one compact animated
button. Hovering or keyboard-focusing that button reveals the Cluster name.
A single click, Enter, or Space expands the original composition again. A drag
moves the collapsed group rather than expanding it; modifier-click still selects
it. The orbit and breathing animation respect reduced-motion preferences.

**Appearance > Collapsed button shape** switches between Square and Circle.
The Tag remains the name used in navigation and on hover. The expanded frame,
collapsed frame, branch geometry, and nested expansion states persist through
save/reopen, copies, Modules, and standalone publication. Existing external links
retain their original endpoints; this does not invent replacement semantic links
to the collapsed button.

Disclosure grouping uses an ordinary Freeform Container with explicit `cluster`
metadata. It is shown as **Cluster** in the interface, and its structural Type and
Display Pattern stay fixed so another layout cannot silently rearrange its
remembered composition. Existing edge-lock Clusters remain available independently.
Selected roots must share one parent; ordered cells and Gallery allocations are
not independent free compositions.

The pure `createDisclosureCluster`, `isDisclosureCluster`, and
`setDisclosureClusterShape` helpers live in `src/core/disclosure-clusters.ts`.
Creation returns `{ project, rootId }`. The persisted metadata is exactly
`cluster: { buttonShape: 'square' | 'circle' }`; TypeScript, host validation, and
publication all preserve and validate it.

## Define, save and reuse a Module

1. Open the **+** menu and choose **Create Module**. The **Module** side panel
   opens and the selection tool stays active.
2. Click nodes to include or exclude them. Selected nodes have a gently pulsing
   outline following their shape. Links whose two endpoints are selected are
   included and highlighted automatically. Clicking empty canvas space keeps the
   selection; dragging the background still pans.
3. Selecting a parent includes its children by default. The panel displays the
   hierarchy with checkboxes, including collapsed descendants. Explicitly
   excluded children stay excluded when their parent is toggled. This selection
   does not change the canvas hierarchy or contents.
4. Enter a name and choose **Create From Selection**. The local **Module
   Library** stores the exact selected nodes and internal links as an immutable
   definition, including content, attachments, appearance, collapse states,
   rules, snapped locks and relative geometry.
5. Choose **Add to Canvas** beside a library entry. Each insertion creates an
   independent assembly with fresh GUIDs, placed in open canvas space. One Undo
   removes the insertion and Redo restores it. Canvas edits autosave normally.

The existing **Save as module** action in **Relations** can still save a complete
branch. **Tools > Modules** opens the searchable library with previews,
dependency details and **Open definition copy**. An editable definition copy is
a separate canvas; editing it does not change the stored entry. Save a revised
assembly as a new module to retain it in the library.

Saving a module captures the selected branch's current configured content, even
when that content is still a draft. It does not save the source canvas itself.
Branch saving turns the chosen container into the saved packing node, or wraps
an ordinary node while preserving its semantic kind. Multi-node selection saving
adds a packing wrapper and preserves each selected node's own kind. Children
whose parent was excluded are promoted to the nearest included ancestor, or to
the wrapper. Their content and positions remain intact.

Branch saving includes the selected node and all contained descendants,
internal links and route bends, used attachments, appearance, positions, rules,
collapse states, configured input values and explicit dependency declarations.
Nested module nodes are ordinary descendants and travel with the assembly.
Module wrappers can participate in visual relationships; they do not acquire
Boolean inputs, outputs or calculation operations merely by being modules.

Inputs, switches and outputs retain their ordinary node titles and controls.
To keep a result visible while hiding implementation details, place the output
beside the internal calculation container, within the outer packing node. Hiding
that calculation container hides its descendants while the sibling output remains
visible and continues to reflect the running Boolean system. Collapsing the outer
packing node hides all of its descendants, including that output.

## Independence and insertion boundaries

Each stored definition has a lookup GUID and a packing-root GUID. The current
implementation allocates fresh identities for a saved definition and allocates
fresh node, link and attachment identities again on each insertion. Internal
parent relationships, reference targets, explicit dependency IDs and link
endpoints are remapped to the corresponding objects in the new instance.
Relative node positions and internal route geometry are preserved.
Conceptual remote-link records also receive fresh identities; genuinely external
target GUIDs remain external.

Insertion validates the complete candidate against the destination's inherited
rules before changing the editor. An incompatible module leaves the destination
unchanged and reports the conflict. Constraints inherited by the source branch
are captured in the saved packing node, rather than silently disappearing when
the branch is reused elsewhere.

Outside nodes, other canvases, boundary-crossing links and runtime reference
snapshots are not copied into stored modules. Known external requirements remain
declared for review, including transitive requirements discoverable at save time.
Incoming functional connections from outside the branch become requirements;
they are not recreated as working connections in a new destination. An unresolved
external reference remains unresolved instead of obtaining a fabricated value.

If an external reference would resolve automatically to a node or saved snapshot
already present in the destination, insertion is rejected. Insert into a new
canvas first, then explicitly choose or remove that reference's source. This
prevents a reused module from silently reconnecting to its source context.
Dependency declarations by themselves do not execute, copy or reconnect anything.

## Declare and inspect dependencies

Select a node and open **Dependencies** in the inspector. The dialog can add an
available node or a typed external node GUID, remove a declaration, and preview
the resulting requirements. **Apply dependencies** commits the declaration as
one undoable edit; Cancel leaves the canvas unchanged.

Declarations are stored as `dependencyIds` on the node. They describe context
needed for equivalent operation, separately from containment and visual links.
A visual relationship alone does not make its endpoint a dependency. The summary
also follows reference targets and known incoming functional connections, then
walks their declared and known functional dependencies. Shared requirements are
listed once, and cycle-safe traversal prevents a declaration cycle from hanging
inspection. This does not make functional feedback cycles executable.

The summary distinguishes:

| Display | Meaning |
| --- | --- |
| Included | The identified context is in the current candidate. Cross-canvas references still use their saved snapshot boundary. |
| Snapshot only | A saved observation is available, but its original canvas and surrounding wiring are not included. |
| Not included | Required node or canvas context is unavailable or is outside the package. |
| Excluded | An available external snapshot was omitted by the selected export policy. |

Module creation and the shelf report actual included contents separately from
requirements. Known transitive requirements outside the saved branch are recorded
on the packing node without copying those outside nodes. This preserves a useful
list of missing GUIDs after extraction, but does not preserve an unavailable
source's complete graph or infer unknown operational context.

Editable-canvas downloads and HTML publication display the same dependency
summary. **Download External References?** controls inclusion of available
external snapshots. Author-marked required canvases select that option by default.
All local canvases are included; unavailable external canvases are not fetched,
and a saved value is not presented as an entire functioning source canvas. The
standalone viewer's download workflow, contextual packaging of complete remote
canvases and selectable viewer snippets remain separate work.

## Local storage and host API

Definitions are stored as `<data-root>/modules/<module-guid>.json`. Saving uses an
atomic create operation and never replaces an existing module ID. A duplicate
ID returns HTTP 409. Damaged definitions remain on disk and produce an explicit
error instead of disappearing from the catalog. The current local library limit
is 1,000 stored definitions.

| Endpoint | Response or request |
| --- | --- |
| `GET /api/modules` | Metadata array: `id`, `name`, `createdAt`, `rootId`, `nodeCount`, `linkCount`, and `contents: [{kind, count}]`. Counts exclude the carrier canvas root. |
| `GET /api/modules/{id}` | Complete stored definition. |
| `POST /api/modules` | Body `{module}`; creates a new definition and returns HTTP 201. |

A `StoredModule` has `{schemaVersion: 1, id, name, createdAt, rootId, project}`.
The carrier project contains one canvas, one module packing branch, its internal
links and attachments, and no runtime snapshots. Host validation rejects malformed
definitions, unrelated branches, invalid hierarchy and invalid internal graph
connections. The host's existing local-access and same-origin write protections
apply. There are no module replacement, deletion or version-management endpoints
in this milestone.

See [MODULE-API.md](MODULE-API.md) for the exact host contract, size limits and
validation rules. Supporting hosts advertise the `modules-v1` capability.

## Authoring interfaces

`src/core/modules.ts` provides pure helpers:

```ts
import { createModule, createModuleFromSelection, instantiateModule, validateModule } from '../src/core/modules.ts';
import { summarizeDependencies } from '../src/core/dependencies.ts';

const definition = createModule(source, packingNodeId, 'Decision toolkit');
// For an exact multi-node selection, including explicit descendant exclusions:
const selectedDefinition = createModuleFromSelection(source, selectedNodeIds, 'Selected toolkit');
const issues = validateModule(definition);
const requirements = summarizeDependencies(definition.project);
const result = instantiateModule(destination, definition, {
  canvasId: destination.entryCanvasId,
  parentId: destinationParentId, // optional; defaults to the canvas root
  x: 100,
  y: 120,
});
// result: { project, rootId, idMap }
// Save the definition through POST /api/modules and save result.project through
// the normal revision-checked canvas flow. These pure helpers do not write files.
```

`summarizeDependencies(project, {rootIds?, includeExternalReferences?})` returns
`entries`, `local`, `snapshots`, `missing` and `excluded` arrays. Entries identify
the required GUID, title, status, reasons, requiring source IDs, required flag,
and available canvas identity or explanatory note. Passing `rootIds` limits the
initial inspection to those branches while still following their requirements.
The summary neither fetches external data nor changes the document.

Authoring recipes support `kind: 'module'` and `dependencies: [...]`. Dependency
references resolve recipe-local node aliases first; literal external IDs remain
declarations and can be reported missing. See [AUTHORING.md](AUTHORING.md). The
existing `scripts/author.mjs` workflow can compose, inspect and render module nodes
inside canvas drafts. Dedicated CLI commands for the stored-module shelf are not
implemented; use the pure helpers and host endpoints for batch module work.

## Verification scope

Core module and dependency tests cover identity remapping, preserved media and
geometry, internal references, declarations, inherited rules, atomic failure and
external-reference boundaries. Host integration tests use isolated module stores.
`tests/modules.browser.ts` exercises saving through the inspector, name/GUID
lookup, independent insertions, single-step undo/redo, live sibling outputs,
editable definition copies, dependency declarations and summaries, and rejected
insertion into a restricted parent. It also checks that pending definition-copy
creation keeps the source draft protected, and that long node titles initialize
a valid editable module name while preserving their complete content. Browser
coverage runs against the isolated test host; the task report records actual
executed results.

`tests/module-selection.browser.ts` covers the persistent Create Module tool,
hierarchical inclusion and exclusions, selected link highlights, exact template
capture, independent repeated insertions, undo/redo, reopening and standalone
publication. `tests/clusters.browser.ts` exercises actual edge snapping, hover
locking, rigid movement, peer-constrained resizing and unlocking in the editor.
Core and isolated host checks also cover snap validation, saved locks, module
reuse and cluster movement across descendants, routes and container boundaries.
