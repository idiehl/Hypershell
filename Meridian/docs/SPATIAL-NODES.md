# Expanding nodes in place

A single click selects a node and shows its properties, complete text and direct child list in the sidebar. Selecting a child from that list inspects it even while its parent is collapsed. It does not reveal the branch. Use the separate **Reveal child** action when you want to display and find that child on the canvas.

A double click reveals or hides a node's children in the current canvas. It does not open a destination canvas or fit the camera to the branch. The disclosure button remains available as a keyboard-friendly alternative. A canvas-link node's **Open canvas** button is the explicit action for opening its destination.

Children have one parent, but ownership does not require a connecting line, a shared edge or a position within the parent's rectangle. Hiding a parent hides all its descendants. Revealing it restores the child nodes while preserving each child's own collapsed state. Collapse changes visibility, not content, identities, saved references or simulation values.

## Parent presentation and child placement

`childLayout: 'spatial'` keeps the parent's tile at its authored width and height in both disclosure states. This suits a text tile with related nodes around it, an illustrated composition, or an expandable chapter node. The legacy `contained` presentation remains available for existing framed groups.

Each child can choose its own placement:

| Placement | Behavior when its parent expands |
| --- | --- |
| `near` | Finds an available position close to the parent without covering nearby visible nodes. |
| `edge` | Attempts to share an edge with the parent's tile. An optional preferred side can be top, right, bottom or left; `auto` chooses an available side. |
| `free` | Keeps its authored canvas coordinates, including positions some distance from the parent. |

The automatic placement pass moves only children that opt into `near` or `edge`. It keeps unrelated nodes stationary. A moved child's descendants and internal route bends move with it, preserving the branch's arrangement. Edge placement tries other available sides when the preferred side is occupied; if no clear seam is available, it falls back to a nearby open position. These layout decisions are part of the editable draft and survive save/reopen. Manually dragging a near/edge child changes it to free placement so a later reveal respects the new location. The inspector explains when a blocked edge falls back to nearby space.

The library also reveals its hierarchy in place. It uses `near` placement by default and stores manually positioned branches as `free`. Opening a canvas remains a separate action from showing its children.

## Relationships and arrowheads

Ownership and visual relationships are separate. Add a relationship only when a connecting line communicates meaning; edge-attached and freely placed children do not need a line.

| Relationship | Default visual treatment |
| --- | --- |
| Causal | One arrow from the source toward the destination. |
| Chronological | One arrow following the authored sequence. |
| Mutual | One line with arrows at both ends. |
| Cyclical | Opposing routes retaining the existing cycle presentation. |
| Generic | A plain line without arrowheads. |

An explicit visual `direction` overrides the default arrowheads: `forward`, `reverse`, `both` or `none`. This does not reverse a computed connection's signal flow. Boolean and numeric connections continue to evaluate from their source to their destination.

## Published canvases

The offline viewer uses the same scene renderer and disclosure rules as the editor. Clicking a tile selects it immediately. If its content drawer is closed, pointer selection waits until the double-click window finishes before opening the drawer, so the overlay cannot intercept disclosure. Keyboard selection opens it immediately. Expanding nodes changes only the viewer's runtime copy; the authored publication remains unchanged. Downloading the published canvas preserves its authored disclosure and placement settings.

Attached MP4, WebM and Ogg video files can appear in document tiles with explicit playback controls and open in the attachment preview. A video tile can attach to the parent's edge like an image tile. Playback depends on the browser's supported codecs; media remains embedded in the portable publication and does not require a network request. Current scene rerenders recreate inline video elements, so selecting another node or changing a control may reset playback; playback-state preservation remains follow-up work.

`tests/spatial.browser.ts` exercises actual editor clicks and double clicks, hidden-child inspection, same-canvas disclosure, fixed camera and parent geometry, collision-free nearby placement, shared-edge placement, free coordinates, nested collapse state, save/reopen and downloaded offline publications. It also verifies SVG arrowheads, the separate canvas-opening action and a locally generated silent WebM attachment in the editor and offline viewer. Storage-boundary details and host checks are documented in [SPATIAL-STORAGE.md](SPATIAL-STORAGE.md).
