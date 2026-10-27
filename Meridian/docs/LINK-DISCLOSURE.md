# Connections belong to expandable nodes

`MeridianLink.parentId` optionally names the node that owns a connection. It must
be an existing node in the connection's canvas. The owner can be one endpoint,
an ancestor of the endpoints, or an independent tile that discloses a relationship
between already-visible nodes. Ownership does not change signal direction or
evaluation. A node containing connections but no child nodes can still expand.

The shared editor/viewer renderer draws a connection only when its owner is
visible and expanded, both original endpoint nodes are visible, and containment
does not already express the relationship. A container needs no line to an
embedded child or descendant, in either direction. This applies to Enclosed,
Horizontal, Vertical, Grid, Spreadsheet, Window, and Gallery containers, modules,
and ordinary nodes whose children are enclosed. Shafts, arrows, labels, hit targets,
and route handles are all omitted. Sibling connections, connections outside the
container, ordinary spatial parent-child connections, and canvas-root connections
keep their lines. It never
substitutes a collapsed ancestor for a hidden endpoint. Collapsing an ancestor
hides descendant-owned connections even if both endpoints lie elsewhere.

Omitted ownership stays backward compatible without rewriting a document: the
nearest common ancestor of the endpoints, including the endpoints themselves,
owns the connection. Thus a parent-to-child relationship belongs to the parent, a line
between siblings belongs to their common parent, and a line crossing unrelated
branches belongs to the canvas root. Hidden endpoints still hide cross-branch
lines. Explicit ownership takes precedence.

`navigationIndex(project)` exposes `linkParent(link)`, `linksByParent`,
`contents(nodeId)` (direct nodes and owned connections), `isNodeVisible(id)` and
`isLinkRevealed(link)` (disclosure eligibility), `isEmbeddedLink(link)` (containment
already conveys the relationship), and `isLinkVisible(link)` (a canvas line should
be drawn). Presentation never removes stored relationships, route geometry, or
functional/numeric signal flow. The Links panel still lists and edits embedded
connections, with eligible ones marked **Embedded · no line**.
`visibleNode` remains a navigation helper for finding a
visible collapsed ancestor; it is not permission to draw a proxy connection.

Recipes accept a connection's `parent` as a node alias or ID, including forward
aliases. Omission preserves an existing owner and `parent: null` restores inferred
ownership. Publication preserves owners; forks remap them. Junction splitting
retains the original effective owner on both segments. Module extraction retains
included owners and assigns connections whose previous owner lies outside the
saved branch to the new module packing node. Module insertion remaps owners.

The model and host reject missing owners, non-string owners, and owners in another
canvas. Module definitions additionally reject ownership by their storage canvas.

Select a node to inspect its **Connections**, including hidden ones. Select a
connection to change **Contained in**; **Automatic** restores inferred ownership.
Both editor and viewer property panels can reveal or hide a link-only node's
contents. Deleting a node deletes the connections it explicitly owns even when
their endpoints survive elsewhere.

The editor and viewer outlines show only the revealed hierarchy. Large collapsed
chapters do not create thousands of hidden outline controls. Full-project search
still finds hidden content and reveals its ancestor branch when selected. The
core `outline(canvasId, true)` prunes collapsed subtrees; omitting the second
argument retains complete hierarchy inspection for authoring tools.
