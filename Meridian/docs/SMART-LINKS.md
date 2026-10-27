# Smart Links and readable node defaults

Smart Links arrange bulk branches without reserving the fully expanded tree's
empty space in advance. Ordinary links still express the Work's relationships
and basic structure. Existing Works do not opt in automatically.

## Using a Smart Link

1. Choose **Place Smart Link** in the toolbar, or **+ → Smart Link**.
2. Click the Work to place its handle and spine. Clicking a normal node places
   a branch beside it; enclosing/ordered surfaces keep their own layout model.
3. Choose **+ → Node**, then click the Smart Link's line. Confirm the ordinary
   New Node form. The new node joins that chain at the clicked position.
4. Expand a member's descendants. The chain reserves that visible branch's
   footprint and moves neighboring members when needed. Collapse it to compact
   the chain again.

Select a rail to set **Smart Link direction** and **Smart Link gap** in
Properties. Automatic chooses among the four cardinal directions while favoring
a useful existing orientation. Explicit Down, Right, Up or Left retains that
direction. The default clear gap is 48 Work units; supported gaps are 8–10,000.
Gap is clear space between footprints rather than an equal center distance that
could overlap differently sized nodes.

The chain is centered on its handle, and an exterior parent-owned chain aligns
that center with its parent. Children opening downward spread left and right; children
opening rightward spread above and below. Direction chooses the rail axis and
the side on which children appear, not a requirement to append every member
toward one end. For example, Right arranges a horizontal sibling chain below
the parent, while Down arranges a vertical sibling chain to its right.
Standalone handles and rails inside bounded workspaces keep their authored
center; the workspace itself is not treated as an exterior subject tile.

To use existing material, select sibling branches and choose **Create Smart
Link** in Relations or the context menu. **Arrange children with Smart Link**
is also available for an eligible parent. These actions preserve the original
node identities and meaningful graph links. A member's Properties provides
**Detach from Smart Link**; it returns to the rail's parent at its current place.
Undo/Redo, recovery, saving and revision conflicts use the ordinary editor paths.

Addressed Grid and Window cells retain their allocated table membership and
addresses. Select the owning Grid or Window node when adding that collection to
a Smart Link. Moving an individual cell into a freeform branch requires an
explicit table-membership change first; grouping does not silently discard its
address or reconnect its references.

## Spacing and routes

The solver measures current visible content, plans nested rails from the deepest
outward, and applies branch translations together. Hidden descendants travel
with their subject without contributing expanded footprints. Internal authored
route bends move with both endpoints when those endpoints share a translation.
Regular spacing extends on both sides of the parent-centered anchor. Nested
branch widths or heights reserve clearance without making the parent an endpoint
of its direct child chain. Closing a branch releases its extra space again.

Enclosing containers reserve their visible frame. A scrolling Window reserves
its viewport, so rows outside that viewport do not create gaps or obstacles in
the surrounding Work. Nested Smart Links can still arrange material inside an
enclosure. Exterior Galleries reserve their displayed page and navigation frame.
Connections to a bounded Window meet its frame; the Window's own contained cells
do not hide that attachment, while unrelated content still receives a detour.

Other rails and fixed visible content are spatial obstacles. Clear trunks and
stubs use direct paths; obstructed routes receive orthogonal detours. One shared
spatial index avoids comparing every stub with every node. Layout is a bounded,
deterministic heuristic, not a continuously running physics simulation or a
promise of a mathematical global optimum. Position locks and locked edge
contacts remain authoritative. Unrelated ordinary nodes are not automatically
rewritten as Smart Link members.
Pinned members and real obstacles can prevent exact symmetry; the solver keeps
those constraints instead of moving locked content to manufacture centering.

A handle placed inside unrelated content can make a safe route impossible.
Blocked segments are withheld rather than drawn through that content; Properties
asks the author to move the handle clear, and partial blocked routes are amber.
Dense pinned layouts
may require the author's placement decision. Choose normal links, saved Joints
and Pivots for deliberately authored relationships outside the managed chain.

## Hover readability

An eligible pointed or keyboard-focused node with text below 14 screen pixels
temporarily grows toward a 16-pixel reading size, approximately 12-point text.
Already readable nodes remain unchanged. Tiny non-text controls have a modest
48-pixel target. The viewport or scrolling Window bounds cap enlargement.

The actual node is magnified; it is not replaced by a copied card or a second
media player. Saved dimensions, coordinates, camera and link layout do not
change. Leaving, Escape, dragging or switching into Text/creation/selection-area
tools clears the effect. Native controls retain their enlarged hit target through
click release. Expanded enclosing workspaces and open document readers are not
magnified as a whole. Reduced motion removes the enlargement transition.

## New node typography

New ordinary nodes use centered horizontal and vertical text, with 32-unit
titles and 22-unit body text as the starting typography. Automatic text frames
give the title/body composition about 65% of their usable vertical space.
Visible titles determine the natural unwrapped width instead of a fixed-width
card. Hidden/empty titles retain the appropriate body/media width basis.

Manual sizing and explicit typography remain authoritative. Allocated Grid,
Window and Gallery tiles keep their shared frames and fit/wrap their contents.
Existing unmarked nodes retain their previous geometry and typography; choosing
the Default card preset explicitly adopts the new defaults. A source document
can still use its reader and supporting Hub content rather than becoming an
oversized paragraph node.

## Authoring and portability

A rail is a visual Utility identity: `kind: 'junction'`,
`utilityVariant: 'joint'`, and `smartLink: {direction, gap, resolvedDirection?}`.
Its immediate children are its ordered members. It carries no numeric or
functional signal. The optional `style.textLayout: 'balanced'` marks the new
typography profile; legacy omission preserves previous authored presentation.

TypeScript, the host, native packages, snapshots, publication and declarative
recipes validate and preserve these fields. Copies and Modules retain normal
ownership and remap identities through the existing graph rules. Published
expansion operates on a runtime copy; it does not change the authored source.

See [AUTHORING.md](AUTHORING.md#smart-links) for recipes and
[MERIDIAN-TUTORIAL.md](MERIDIAN-TUTORIAL.md) for the maintained native lessons.
