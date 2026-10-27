# Creating and editing on a canvas

Home and other Meridian canvases use the same editing interface. Home is an
ordinary canvas containing nodes, including Canvas launchers; it has no special
Section type or subsection operation. See [the unified library contract](UNIFIED-LIBRARY.md)
for migration, discovery and home storage details.

## Choose a canvas tool

The left side of the canvas toolbar contains **+**, **−**, a selection-tool
dropdown and **Reset view**, in that order. Undo and Redo follow
these canvas controls, separated by one vertical divider. The **+** menu chooses **Node**, **Link**, or **Module**, with
New Node or New Link as its tooltip. The selection dropdown shows a pointing
finger for **Single select**, the default tool, or a hand for **Select Area**.
Reset view restores the default camera position and zoom. The upper application
bar contains **File** and **Tools** menus; Tools opens **Modules** or
**Composition tools**. Canvas search remains in the Canvas side panel and is
available with **Ctrl+F**.

| Tool | A short left click | Continued use |
| --- | --- | --- |
| **Node** | Click the background to mark a position and open the New Node form. Click a node to create a child of that node. | A successful creation returns to Single select. |
| **Link** | Click a source node to begin a route. Click empty canvas to place a pending pivot; click a different node to finish the connection. | The tool remains active while tracing; a successful completed link returns to Single select. |
| **Module** | Click nodes to build a reusable selection in the Module panel, then choose Create From Selection. | A successful Module creation returns to Single select. |
| **− Delete objects** | Click a node to delete its branch, or click a link to remove that connection. Clicking empty background does nothing. | A successful deletion returns to Single select. Deletion can be undone. |
| **Single select** | Select a node or link and inspect it. Click its title or text to edit in place; double-click text selects a word. Double-click the backdrop to reveal or hide contents. | Drag the backdrop to move a node, or drag empty background to pan. Shift-click adds or removes a node from the selection. |
| **Select Area** | Select and inspect individual objects; drag empty background to select an area. | Shift adds the dragged area to the current selection. Node dragging and mouse-wheel zoom remain available. |

In Node, Link or Delete mode, dragging pans the canvas, including when the drag
starts over a node. Holding the button and releasing without a drag does not
create or delete an object. A creation or deletion click must release within
350 milliseconds and move less than five screen pixels. Single select preserves
ordinary node dragging and background panning; Select Area changes background dragging
to area selection. Shift-drag also selects an area from Single select.
Mouse-wheel zoom remains available.

**A** opens the Node / Link / Module menu at the last canvas pointer position.
Pressing A again cancels the create tool and returns to Single select. **S**
toggles Select Area, **D** toggles Delete, and **F** resets the view. **Ctrl+Z**
undoes a canvas edit and **Ctrl+X** redoes it. These canvas shortcuts do not
replace typing, cut, or text undo inside text editors or form fields.

The active controls are highlighted. **Escape** finishes an inline text edit,
commits its current text, and clears canvas node/link/Module selection. Outside a
form or active drag it also cancels any unfinished link and returns to Single
select. Escape clears selection when a sidebar field has focus as well. A
focused canvas can use **Enter** or **Space** with the active tool; **Shift+F10**
or the menu key opens its context menu. Native video controls and Ctrl-click
image opening retain their own behavior in every mode.

There is no separate horizontal selection bar. The Module panel stores reusable
selections. Grouping and arrangement remain in the node context menu and the
Relations panel for multiple selection.

## Edit text in place

In Single select, click an editable title or text body to place the caret, drag
through words to select a passage, or double-click a word. A compact toolbar
appears beside a nonempty text selection, with Font, Size, Color, Bold, Italic,
Underline, and Clear formatting. Its controls stay at screen size while the
node scales with canvas zoom; selecting a font or color retains the passage.
Typing, paste, text undo, and IME composition use the same rich text engine as
Content. Changes use ordinary automatic saving without repainting the active
surface and losing the caret. Clicking elsewhere finishes the edit; Escape also
clears canvas selection. Ctrl/Meta+C and V copy and paste text while editing;
focus a node's backdrop to copy and paste whole nodes.

A selected node's resize edges take priority over text editing. The edge target
uses screen pixels, so zooming does not make it vanishingly small; on tiny tiles
the inward target is capped to keep interior text accessible. Drag through words
away from that edge band to edit or select text as usual.

Editing canonical Gallery child text updates that child. Referenced or saved
snapshot content and document previews remain read-only on the canvas; they
must not silently edit a different source. Authoring controls do not appear in
the portable viewer. Node disclosure uses the backdrop while a text double-click
keeps native word selection.

## Lock a node's position

Right-click a node and choose **Lock Position**. The same action becomes
**Unlock Position** while locked. The saved lock anchors the node's X/Y position;
text, appearance, selection, and disclosure stay available. Dragging or nudging a
parent branch or a rigid snapped Cluster containing a locked node is prevented
as well. A resize can change dimensions without moving the anchored top-left
position. Automatic collision repair moves unlocked peers around locked
branches. An edit requiring a locked node to move is rejected atomically; unlock
it before arranging or changing that layout. Copies and Module templates retain
the flag with fresh GUIDs and can be placed at their initial insertion position.

## Create from the context menu

Right-click the canvas at any time to open its actions at the clicked position.
The menu includes **Create Node**, **Create Link**, **Delete**, **Single select**,
and **Select Area**.
Right-clicking a node changes the creation action to **Create Child Node** and
also offers **Inspect Node**, **Lock/Unlock Position**, relevant **Expand/Collapse Contents** actions, and
**Open Canvas** for a Canvas launcher. Right-clicking a link offers **Inspect
Link**, **Create Pivot**, and **Create Joint**. Delete is unavailable when there
is no object or selection to delete.
Right-clicking empty canvas also offers **Inspect canvas** to select its root.

Context-menu node creation uses the same centered New Node form as Node mode.
Its Create Link action uses the centered New Link form, while the Link
mode traces directly on the canvas. Choosing a context action does not switch
the current canvas tool.

Canvas launchers offer four continuously animated vector portals: **Endless
doorway**, **Luminous passage**, **Curved wormhole** and **Spiral vortex**.
Endless doorway is the default for existing and new canvases. In the canvas's
**Appearance → Canvas portal style**, choose the workspace default. A selected
canvas button or reference to one offers the same choices plus **Use canvas
default**, so individual buttons can have their own appearance. The choice saves
with the canvas and travels with copies, Modules, snapshots and publication.

The control scales from the node's safe frame, independently of its title's
length or number of lines, and remains centered. Its slow perspective travel
accelerates smoothly on hover or keyboard focus, while the portal moves closer.
The destination canvas's name appears above the button; it is separate from the
node's Tag and displayed text. Enter or Space opens the same destination as a
click. Reduced-motion preferences retain static portal art, the destination
label and a visible focus indication. Offscreen and hidden portals pause their
animation clocks.

**Appearance → Canvas portal color** tints a button's complete animated artwork,
retaining its dark depths, moving layers and bright edges. With nothing selected,
it sets the default color for canvas buttons in the current workspace. A selected
canvas launcher or canvas reference has an independent color selector; **Use canvas
color** removes that button's override. **Use original colors** on the workspace
restores each portal design's original palette. These choices persist in saved
canvases, Modules, copied nodes and portable publications.

For individual nodes, **Appearance → Background display → Show on Hover** hides
the background at rest and reveals the configured fill, pattern or background
image when the pointer enters the node. Text, primary media, controls and borders
stay visible. Keyboard focus reveals the background too, including focus within
contained children. Hovering a child inside a container or Expansion Tray reveals
its physical enclosure without activating a distant source tile. Devices without
hover capability retain the ordinary visible background. The choice is saved
with the node; **Always visible** restores its normal display. Unframed text and
the No backdrop shape remain unpainted.

## The New Node form

**Type** is the first field. The available types are Canvas, Container, Operation,
Media, Module, Reference, Number, Input, Switch, Output and Utility.
Ancestor rules filter node types, display patterns and operations;
submission also validates the complete resulting graph.

The form adapts to the chosen type:

| Type | Content and configuration |
| --- | --- |
| **Canvas** | Create a new canvas workspace or choose an available existing canvas. Add a title and one content item to its launcher. Creation keeps the current canvas open; opening a launcher navigates in the current window, with Back and Forward available. |
| **Container** | Choose a Display Pattern and configure grid dimensions or initial cells when applicable. Its own single content item appears while collapsed; expanded children occupy its interior. Any ordinary node can also own children. |
| **Operation** | Choose a permitted Boolean, arithmetic, comparison or routing operation. Pass-through operations can choose Boolean or Number values. Named ports follow the operation. |
| **Media** | Choose one Text body, Media or document file, or Hyperlink. The title is separate. Its children appear outside its border using the selected Display Pattern. |
| **Module** | Create an empty grouping for a reusable node system, with the same available Display Patterns. Creating this node does not insert a stored module automatically. |
| **Reference** | Choose an available node and optionally a coordinate or named cell. Cross-canvas references use saved observations. |
| **Number / Input** | Set a finite numeric value or a Boolean value. |
| **Switch / Output** | Choose the value type; a switch also chooses its open or closed state. |
| **Utility** | Choose Pivot or Joint, with a compact Dot or Line appearance. Pivots bend a route; joints let routes meet and share a continuation. These are separate from calculation operations. |

Display Patterns are **Freeform**, **Horizontal**, **Vertical**,
**Grid**, **Spreadsheet**, **Gallery**, and **Window**. Freeform is unordered:
children retain important visual relationships in a bounded working enclosure
or outside an unbounded parent. Automatic placement uses balanced radial rings;
manual positions remain stored. Horizontal, Vertical, Grid, Spreadsheet and
Window use equally sized and shaped children. Horizontal is one tight
row; Vertical is one tight column. Grid grows to square capacity (3×3 for nine
objects, 4×4 for ten), filled by row or column. Spreadsheet retains configured
axes and pixel cell dimensions. Window adds independently configured visible
axes and a horizontal or vertical scroll viewport over the complete spreadsheet
address space. A Module retains its module identity with these layouts. Gallery
displays one object at a time, with Previous and Next controls. A bounded Gallery
displays that object inside its parent; an unbounded Gallery displays one active
child outside the parent. Its other objects are not plotted simultaneously.

The Content choice is independent of a node's type: one text body, embedded file,
or hyperlink, plus a separate title. Text and Document are no longer separate
creation types. Existing legacy fields are retained without destructive migration.

Automatic sizing fits a node's visible contents. Fixed sizing retains a deliberately
authored surface. Converting legacy Spatial content to an enclosing layout moves complete
branches into usable interior space, preserving identities, cell names, independent
collapse states, and internal route bends. Expanding a nested child reserves its
visible branch footprint before laying out neighboring cells.

Child coordinates use X:Y without visible corner badges; legacy A1 references
still resolve. Default content padding and gaps are 5 pixels; the inset between
children and a parent's inner border is 5 pixels. These can be changed in
Appearance, together with border thickness, pattern, shape and color.
Grid settings include columns, rows, cell sizes and fill order. Optional CSV text
creates ordinary addressable Media children with text content. CSV is treated as text data;
formulas are not executed. Creating a child in an existing grid allocates an
unoccupied cell. A full grid requires more rows or columns before another child
can be added to Spreadsheet or Window; automatic Grid grows to the next square.

Images supported by the form are PNG, JPEG, GIF, WebP, AVIF and BMP. Supported
video attachments are MP4, WebM and Ogg. Files are limited to 16 MiB each and
their original bytes are retained. Browser decoding determines whether a
particular supported video codec can play. Other file formats remain embedded
documents; preview availability depends on the format. Audio can be attached as
a Media content file; the form does not introduce a general media transcoding
service.

## Expand a branch in a tray

Expanding an unbounded node inside a bounded Container or Module opens its
children in an **Expansion Tray** outside the enclosure. The source stays in
place, with one solid tether showing which branch the tray belongs to. Individual
visual source-to-child lines are hidden while those children occupy the tray;
their stored relationships remain available. These are
the original child nodes: selecting, editing, targeting and linking them work
normally, and their parent relationships and Tiers stay unchanged. A nested
bounded Container still opens within its own frame.

The tray is a plain container surface with no title banner, source badge, icon
or close button. Its fill and border follow the source's Appearance settings.
Hovering an expanded container or tray reveals a 28-screen-pixel Tag bar along
its top edge. The bar spans the full frame and touches its top border. Its label
uses the visible title's font family, weight and italic style; a Gallery tray
follows its currently displayed child. Click the bar to collapse, or drag it to
move the whole node or tray. Movement uses the normal snapping, selection,
locking and undo rules. The bar does not enter text editing or reserve interior
room. Tray corners are square by default, with optional rounding in
Appearance.
Click its empty background to select the source; double-click that background
or the source's backdrop to collapse the branch. Drag the tray's empty rim to
reposition the branch; its internal links move with it.
Drag an edge or corner to resize the tray and scale its child composition to fit.
Moving or resizing a tray is one undoable edit. A focused tray background also moves with the arrow keys
(5 canvas pixels, or 20 with Shift). Drag near the enclosing container's outer
edge to snap the tray flush against it; hold Alt to bypass snapping.
Collapsing an ancestor hides all its trays;
reopening restores their positions, saved sizes and each descendant's expansion state.
Window scrolling and Gallery paging hide trays whose sources are offscreen.
The Target action reveals the source as needed before targeting a tray child.

Tray placement and size are saved with the source and travel with copies and Modules.
It creates no extra nodes or graph links. In a portable viewer, disclosure
remains available but the tray cannot be repositioned or resized.

## Appearance and movement

Ordinary New Node forms provide a style preset, backdrop shape, width and height,
fill/border/text colors, font family, and title/content font sizes. The shapes
are Rectangle, Square, Rounded rectangle, Circle, Ellipse, Triangle, Diamond,
Pentagon, Hexagon, Octagon and **No backdrop**. No backdrop leaves the content
floating over the canvas. Regular shapes retain their geometric proportions
inside the authored frame instead of stretching across its width. Rectangles,
rounded rectangles and ellipses retain their deliberately variable proportions.
Content and bounded child layouts use the actual silhouette's safe interior.
The preview shows the chosen appearance at a reduced scale; dimensions are
canvas units. Text defaults to Center alignment; Appearance can choose Left or
Right, and an explicitly saved alignment remains effective.

The behavior choices set movement and disclosure placement:

- **Canvas default** inherits the canvas's movement behavior. New canvases and
  existing canvases without an explicit preference initially use Sticky.

- **Free** uses the marked position on the canvas or automatic Freeform placement
  when creating a child, without sticky edges or smart alignment.
- **Align to edges** places a child beside its parent's chosen edge and enables
  smart alignment when it is moved.
- **Sticky** places a child beside its parent's chosen edge and enables sticky
  edge snapping when it is moved.

The edge can be Top, Right, Bottom, Left, or the nearest available edge. Canvas
roots have no parent tile to attach beside; their children retain the marked
position and use the selected movement settings. An explicit Sticky or Align
edge choice takes precedence over automatic radial placement for unbounded
Freeform children. Bounded children stay inside their parent; ordered and Gallery
children follow their parent's display pattern. Those parents do not offer an
outside Parent edge choice. Sticky contact follows the chosen backdrop, including
slanted polygon edges and curved outlines. Holding Alt temporarily bypasses
snapping; collision prevention remains active. Separate tiles cannot overlap,
including after editing their dimensions or shape. A contained child may remain
inside its enclosing ancestor.

Appearance and movement remain editable in the **Appearance** and **Properties**
panels after creation. Resize a selected tile at its actual backdrop edge; there
is no visible corner grip. Selection and hit overlays share the outer
backdrop frame at every border thickness, rather than shrinking into a second
inner outline. Display Pattern identity is retained even where two
variants share the same arrangement algorithm. See [the inspector guide](INSPECTOR-PANELS.md)
for the six panel menus, GUID editing, and conceptual remote links.

## The New Link form

Link mode traces a path directly on the canvas. The first node click
chooses its source, empty-canvas clicks add pending pivots, and a different node
click commits the complete connection as one undoable edit. Clicking a visual
link shaft finishes at an explicit Joint inserted at that location. Pending
pivots do not become stored objects until the route is committed.

Hold **Shift** to constrain the next segment to the nearest horizontal or
vertical axis. Preview and committed geometry use the same constraint. When a
destination is off that axis, the route retains its connection through right-angle
corners placed clear of the endpoint backdrops. This does not replace manually
placed pivots with a general automatic obstacle router.

The context menu's **Create Link** opens the centered form. Choose **Visual**,
**Boolean**, or **Numeric**, then Source and Destination. Starting from a node
preselects the source; starting from empty canvas leaves it for the user to
choose. Links remain within one canvas.
References represent relationships to data in another canvas.

The form provides Generic, Causal, Chronological, Mutual and Cyclical
relationships. Visual links can be plain lines or have arrows toward the
destination, toward the source, or in both directions. Computed Boolean and
Numeric links pass a value from source to destination and offer only matching,
unoccupied named input ports. Invalid endpoints, incompatible ports, duplicate
computed inputs and unsupported feedback cycles are rejected.

Add a label, color, width and Solid, Dashed or Dotted line style. **Contained in** chooses
the node that owns the link, or automatically uses the nearest shared parent.
The link appears only when its owner is expanded and both endpoints are
revealed. Creating a link does not force otherwise hidden branches open.

## Commit, cancel and recover

Selecting a tool, marking a location, tracing an unfinished route, changing the
form, or cancelling makes no document edit. Finishing a traced route,
**Create node**, or the form's **Create link** validates and commits the
result as one undoable action. Creating a child reveals its immediate parent
and selects the new node for inspection. Escape, Cancel, or closing the form
creates nothing; keyboard focus stays in the form while open and returns to
the canvas or relevant previous control when it closes.

Creation and deletion use the ordinary undo/redo and frequent automatic save
path. Successful canvas actions return to Single select. Revision
conflicts and save failures retain the draft rather than silently replacing
newer saved content. If the document changes while a creation form is open,
the editor asks the user to place the object again against the current graph.

These controls are local authoring tools. They do not add account login,
simultaneous multi-user synchronization, or online repository hosting.
