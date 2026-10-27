# Arranging selected branches

Select two or more separate branches, then choose **Arrange** in the selection bar
or the selection inspector. Align their left, horizontal center, right, top,
vertical center or bottom edges. Select at least three separate branches to space
them evenly horizontally or vertically.

The controls use the tiles as currently displayed, including compact collapsed
containers. Alignment uses the outside bounds of the selected tiles. Even spacing
keeps the first and last tiles in place and equalizes the gaps using each tile's
actual width or height. If the available span cannot fit them, the action reports
that the outer tiles need more room; it does not silently create overlapping gaps.
Manual positioning remains available, with collision prevention in every mode.
Enclosed contents may occupy their ancestor's interior; separate tiles and spatial
parents cannot overlap. An alignment that would collide is adjusted to clear space.

A selected parent and its selected descendants form one independent branch. Each
branch carries all its children, including hidden children, by the same offset.
Children keep their relative positions, disclosure state and placement settings.
Only a root that actually moves switches to free placement. Alignment does not
reparent nodes, expand them, change the camera or change computed values.

Route bends move with a branch when both endpoints move by the same offset.
Connections whose endpoints move differently, or whose other endpoint is outside
the arrangement, keep their authored world-space bends; their endpoints remain
attached to the moved tiles.

Each successful arrangement is one undoable edit. Opening or cancelling the menu,
choosing an arrangement already satisfied, or rejecting an invalid arrangement
creates no edit or recovery write. A menu opened against an older editor version
cannot apply to a changed draft. Save/reopen uses the existing document geometry;
no new document fields or storage format are introduced.

This is an implementation choice supporting N14 on page 14 of the handoff, within
the existing single-parent containment model and D06's layout choices. It does not
add automatic canvas-wide reflow or certify complete mobile authoring.

`src/core/arrangement.ts` provides a pure, validated `arrangeSelection` helper for
the editor and future authoring tools. Unit tests cover normalized branches,
display bounds, differing sizes, route translation, finite geometry, source
immutability and invalid/no-op actions. Browser tests cover the selection controls,
hidden descendants, camera preservation, undo/redo, save/reopen and compact-menu
layout. Exact executed results are recorded in the cycle completion report.
