# Inspector panels

Every workspace, including the Library, has a left inspector. The selector at
its top chooses Work, Properties, Appearance, Relations, Links, or Module. The
selected panel is remembered locally. A selected node has a separate **Node Hub**
on the right; content editing no longer occupies an inspector tab. A saved legacy
Content-tab preference opens Properties instead.

The creation, selection, text, reset-view, and history tools remain above the
workspace, immediately to the right of the left inspector. Closing the inspector
returns that width to the workspace. On narrower screens the inspector and Hub
use opposite edge overlays, with explicit close/clear-selection controls.

## Canvas

Canvas is the only panel with a node search field. It searches the current canvas
by Tag, GUID, title, content, or node type, including nodes hidden inside collapsed parents.
Results retain their hierarchy and omit body excerpts and media previews.
Enter jumps to the first actual match; Escape clears the search.

Each outline entry starts as one compact row: the user's Tag, a small
nested-square badge when it has sub-nodes, node type, a target icon, and **Details
(+)**. Details uses a centered vector plus with a square hit area and a circular
hover/open highlight; its tooltip is simply “Details”.
Solid horizontal guides fill each nested row's indentation; deeper visible
levels have longer guides and the local canvas root has none. The guides share
the responsive indentation limit so actions remain reachable in deep outlines.

Clicking the title or blank row area selects the node on the current canvas and
toggles its child rows. Selection does not move the camera, expand the node's
canvas contents, or switch Gallery pages. Hidden nodes remain hidden until an
explicit reveal or Target action. Other-canvas search entries can disclose their
hierarchy; Target is required to navigate there. The initial canvas root is open
so top-level entries are discoverable. Target separately reveals necessary
parents, selects the destination, and centers the camera.

Details independently opens a minimal drawer: GUID, immediate sub-node count,
link count, tier, and a boxed red **X** at its upper-right. It contains no duplicate
child, connection, or search-excerpt lists. Links remain inspectable in the
Links panel. Sub-node counts include all immediate children, including hidden
or search-filtered ones; grandchildren are not counted again. Link counts
include endpoint-incident and incoming/outgoing conceptual links, with self-links
counted once. A container merely owning a link between its children does not
make that link incident to the container. The canvas root cannot be deleted;
other-canvas search results require navigation before deletion.

The search placeholder is **Search a GUID or Name**. Full text, attachment, cell,
and type matching remain available. Match totals are announced to assistive
technology without a visible node-count row. The application's normal Saved /
revision banner is removed; automatic saving and save-error notices continue.

### Tiers across canvases

The branch icon in Details is followed by the tier number. The library root is
Tier 1, its immediate children Tier 2, and each contained generation adds one.
Opening a canvas replaces its launcher at the same tier: a Tier 3 Canvas
launcher opens a Tier 3 root with Tier 4 children. Numbering does not restart at
each canvas boundary. Multiple library appearances use the shallowest reachable
route, and cycles cannot keep increasing the count.

`src/core/tiers.ts` derives these values from the current library and project
hierarchies without changing saved document fields. Bounded navigation metadata
in session storage retains known ancestry for linked workspaces outside the
currently loaded library graph, including reloads and Back/Forward navigation.
Current library structure takes precedence over saved metadata. A directly
opened workspace with no known library ancestry has local generations starting
at Tier 1; this fallback is not saved as invented library ancestry.

The outline can be navigated with Up, Down, Home, and End. Right opens a branch or
moves into it; Left closes the branch or moves toward its parent. These actions
do not change the document's collapsed states.

## Properties

Properties contains node Tag, Type, applicable Display Pattern, Alignment behavior,
editable GUID, and applicable role-specific value or operation controls. A Work
root retains its type. Other nodes may be converted when
their existing relationships, references, and inherited rules remain valid.
Container variants use the same options offered during creation.

Module is absent from ordinary node-creation and conversion choices. Create a
Module from a selection through the Module tool. Existing Module nodes retain
their structural identity and display a disabled Module type; their stored
definitions and independent insertion behavior remain available.

Tag is the node's navigation and search name. It is independent of the optional
Title in Node Hub, and never appears as filler inside a workspace node.
Legacy nodes initially take their existing Title as their Tag; opening a project
does not save this normalization by itself. The next ordinary edit/save preserves
that Tag, including when its visible Title is cleared. Blank legacy names receive
a type and short GUID fallback. Tags do not replace unique GUIDs.
For a canvas root, editing Tag also renames its workspace metadata; editing the
visible Title does not rename the workspace.

Block is retired from creation, Properties, grid-cell creation, and rule choices.
A blank Media node provides the same shape-only surface and unbounded display
patterns. Legacy Blocks become blank Media nodes in the editing graph, preserving
GUIDs, Tags, geometry, appearance, assets and children. Any dormant title or text is
retained as inert `retiredBlockContent`; opening alone does not save the migration.

Alignment choices are Sticky, Align to edges, Free, and inheritance. On the canvas
root these edit the canvas's default behavior. On another node they apply an
override or return to inheritance. Collision prevention remains independent of
the snapping mode.

Custom GUIDs contain 1 to 96 letters, digits, underscores, or hyphens and start
with a letter or digit. Apply commits the change atomically. Included child parent
references, connection endpoints and owners, references, dependency identities,
and saved observations follow the renamed node. Occupied and reserved identities
are rejected. External canvas destinations retain their original identity.

## Node Hub

The right Hub appears for an actively selected node. It contains **Title**,
**Show title on Work**, and an **Add content (+)** control. An empty node starts
with no synthetic content card. Clearing the node selection closes the Hub.

Add content opens a staged form. **Content type** selects Text, Image, Video,
Audio, Markdown, CSV / TSV table, PDF, Text / code document, Hyperlink, or File
attachment. The remaining fields adapt to that choice. Confirming adds the item;
Cancel leaves the node and attachments unchanged.

The first Hub item is the **Display slot**: only its content appears in the
node's workspace tile. Further items remain attached to that same node and are
readable in the Hub without creating descendants or cluttering the graph. Drag
the **Move content** handle to reorder items, or use **Move content up/down**.
Moving another item into the first position changes the visible content while
preserving the node's structural type, links, descendants, and identity.

Each item has inline preview, **Pop out content**, **Edit content**, and
**Remove content** controls. Pop out opens the appropriate larger viewer. Hiding
the title preserves its saved text, formatting, Tag, and search identity.
Reordering, editing, removal, and title changes use the normal undo, automatic
save, recovery, and checkpoint flow. See [NODE-HUB.md](NODE-HUB.md) for the model,
legacy-content behavior, viewer support, and limits.

## Appearance and Relations

Appearance holds the node's visual controls, including backdrop shape, colors,
typography, dimensions, and position. Node Hub owns the title and ordered content
items. A blank or hidden title has no visible header, and blank text has no
synthetic placeholder. A Container may therefore have a meaningful Tag and a
completely text-free workspace surface. Media nodes may also have no visible
title or content, without requiring a separate node type.
Relations holds the
hierarchy, child nodes, inherited permissions, child placement and layout settings,
and addressable grid controls. These panels do not repeat the canvas search field.

Relations also contains **Child behavior & appearance**. Each Apply affects only
immediate children: movement inheritance or overrides, disclosure placement,
appearance presets/shapes/fonts, and tile dimensions. Optional fields preserve
individual values until an explicit Apply. Fit child tiles to cells sizes the
tiles to the parent's current grid cells; ordinary cell-spacing edits retain
authored tile sizes. Descendants keep their own settings. Bulk edits use one undo
step and the same inherited-rule validation and collision checks as single edits.

## Links

Links lists connections for which the selected node is either an endpoint or the
containing owner. These roles can overlap, but a connection appears only once.
Hidden connections remain inspectable and removable from the list, with their
visibility status stated explicitly. Existing self-links remain identifiable.

The Remote links section accepts a target GUID, relationship, and optional label.
These are conceptual relationships with no line, arrow, or interactive mark drawn
on the visual canvas. The target may be in the current document, represented by a
saved reference snapshot, or outside the document. Incoming conceptual links from
other included nodes also appear. Available included nodes provide a jump action.
This stage records the relationship; it does not fetch a remote node's content.

Creating, changing, or removing a remote link uses the same undo, autosave,
recovery, copy, and publication system as other edits.
