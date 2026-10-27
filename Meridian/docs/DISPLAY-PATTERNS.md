# Bounded and unbounded child display

The node's **Type** describes its role. Its **Display Pattern** describes how its
children are shown. Every non-root node can own children; a parent does not have
to be a Container. The same controls apply in the home canvas and every other
canvas.

**Container** is bounded: expanding it reveals a child workspace inside its
border. Its own embedded content is hidden while it is expanded and returns when
it is collapsed. **Media** is unbounded: its own content remains visible and its
children are revealed outside its tile on the same canvas. Canvas launchers and
working nodes can also own unbounded children without losing their existing role.
Legacy module and spreadsheet containers remain bounded.

The selected node's **Node Hub** holds an ordered collection of text, media,
documents, and hyperlinks. The first item is its **Display slot** and supplies
the one primary item visible in the workspace. Reordering the Hub changes this
display without adding child nodes. Other items remain readable inside the Hub
or its larger viewers. Title is separate, with an independent **Show title on
Work** control. Content remains independent of structural Type: a Container can
have a cover image or passage, and a Media node can display text.

New Hub edits save an explicit ordered `contentItems` list. Older mixed-content
nodes retain their original fields; merely opening them does not rewrite the
Work. Their selected legacy primary item comes first, followed by their other
text, attachments, and links. Without an explicit legacy selection, an existing
attachment takes precedence, then text, then the first hyperlink. Explicit Hub
content takes precedence over dormant compatibility fields. The older `text`,
`image`, and `document` kind identifiers remain readable, while ordinary creation
offers the unified Media type. Module creation uses a selected assembly through
the Module tool rather than a new-node Type option. See [NODE-HUB.md](NODE-HUB.md).

| Display Pattern | Child workspace |
| --- | --- |
| Freeform | Unordered, variable-size branches. Automatic children use balanced radial placement; manual positions are preserved. A bounded parent fits the composition within its saved workspace, with working room around children. An unbounded parent retains its own body and children occupy the surrounding canvas. |
| Horizontal | One tightly packed ordered row. Coordinates are `1:1`, `2:1`, `3:1`, and so on. |
| Vertical | One tightly packed ordered column. Coordinates are `1:1`, `1:2`, `1:3`, and so on. |
| Grid | A square ordered capacity, rounded up to the next square. Nine children use `3×3`; ten use `4×4`. Fill order is by row or by column. |
| Spreadsheet | Explicit columns, rows and pixel dimensions for each uniform child tile. Each occupied location has an `X:Y` address and optional name. |
| Window | An explicit full spreadsheet with a smaller visible window and horizontal or vertical scrolling. Bounded windows scroll inside the parent; unbounded windows create a clipped child surface outside it. |
| Gallery | One immediate child at a time, with previous/next navigation. A bounded gallery displays the object in its parent; an unbounded gallery displays the active object's own tile outside the parent. Browsing the gallery does not change its saved content. |

Ordered children share size and shape. Default content padding is 5 canvas
pixels and the child-border inset is 5; Appearance can override inner padding, child padding, content
spacing and border appearance. An external ordered child surface may be shifted
as a whole to keep clear of nearby peers, preserving cell alignment and the
parent's own position.

When an unbounded parent is itself inside a bounded workspace, its children
open in an **Expansion Tray** outside the outermost bounded ancestor. Its own
tile stays in place. The tray presents the original descendants and their
existing hierarchy, with one view-only tether to the source; it creates no
proxy objects or graph links. Dragging the empty tray rim moves that branch; dragging an
edge or corner resizes the tray and scales its children to fit. Closing
an ancestor hides its trays without resetting their positions, sizes or the children's
expansion states, and reopening restores them. Nested bounded children still
open inside their own frames. See [NODE-SIZING.md](NODE-SIZING.md) for sizing
and persistence details.

## Freeform placement

New children without an authored position use the clock order
`3, 4, 2, 5, 1, 6, 12, 7, 11, 8, 10, 9`. The first child appears to the right,
the second below and to the right, and the third above and to the right. The
sequence balances the lower and upper space as it proceeds around the parent.
After twelve children another, more distant ring starts in the same order.
Placement accounts for complete revealed child branches and nearby obstacles.

A manually moved child retains its position through collapse, expansion,
autosave and reopening. Older children without the new placement marker retain
their authored positions. Selecting a layout deliberately can rearrange its
children; merely opening an old Freeform composition does not discard an
authored arrangement.

## Dragging a node into a parent

In Single select, drag a tile over a receiving node. A nesting symbol appears beside
the pointer and the receiving border is highlighted. Release to make the dragged
branch a child. While a valid receiver is highlighted its bounds are temporarily
excluded from ordinary drag collision prevention. Other peers still prevent
overlap. When there is no valid receiver, ordinary collision prevention applies.

This uses the existing hierarchy: identities, descendants and internal links
travel with the branch. The drop is one undoable edit and saves automatically.
An ordered receiver assigns a free cell; a Freeform receiver uses automatic
radial placement until the child is moved manually. A collapsed receiver expands
so its new child is immediately visible. A node cannot receive its
own ancestor, a canvas root, a cross-canvas branch, a branch excluded by inherited
rules, or a child that would exceed an explicit ordered surface's capacity.

Ordinary tiles in ordered cells can also be dragged to another parent; releasing
them on empty canvas space preserves their original cell.

Embedding already expresses the relationship in bounded containers, so
parent-to-child lines are suppressed there. The stored relationships remain
available in Links. Unbounded parent/child links may render normally when their
owner is expanded and their endpoints are visible; hierarchy alone does not
automatically create a line.

## Data and authoring contract

The legacy persisted `containerVariant: 'contained'` now means **Freeform**.
The older `'spatial'` variant is accepted when reading older documents. Current
ordered values remain `'horizontal'`, `'vertical'`, `'grid'`, `'spreadsheet'`,
`'window'` and `'gallery'`. They can be attached to any non-root node.

- `kind: 'media'` uses `childLayout: 'spatial'`; malformed bounded Media data is
  rejected. Formal bounded containers use `childLayout: 'contained'`.
- `contentType: 'text' | 'asset' | 'link'` selects one active primary item.
- `childPosition: 'auto' | 'manual'` distinguishes newly automatic children from
  deliberate positions. Omission retains older authored positions.
- `childOffset: {x, y}` is an optional parent-relative translation of an external
  ordered child surface. It has finite, bounded coordinates and contains no
  executable instructions. Bounded layout ignores it.
- `expansionTray: {offset: {x, y}, size?: {width, height}, layoutVersion?: 2 | 3}` remembers the source-relative
  content origin and optional resized outer frame of a detached child workspace.
  It retains original node identities, parents
  and disclosure states; it is presentation state, not a new node.

TypeScript validation, the host validator, snapshots, recipes and portable
publication retain these fields. Hierarchy cycles, inherited permissions,
unique identities, cell coordinates/names and explicit capacities are still
validated. Source data is never migrated on disk solely by reading it.
