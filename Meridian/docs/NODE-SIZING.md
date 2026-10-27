# Node sizing and container disclosure

New reading nodes use **Automatic** sizing. The tile fits its title and visible content,
with a reading width and 5-pixel content padding by default.
Appearance → Sizing switches between Automatic and Fixed. Editing Width or
Height, or dragging a selected tile's edge, switches it to Fixed. Returning to
Automatic fits the content again. Authored typography remains stored at the
chosen font proportions. **Content sizing → Fit to node** is the default: the
node frame supplies a common 300×180 logical typography basis. Equal-size
frames therefore show the same authored font scale for ordinary short titles,
independent of title length or line count. Actual overflowing content may shrink
further to stay inside the safe interior. Canvas launch portals use their own
frame-based size and center, so title fitting cannot resize the button.
All four vector portal styles use the same fitting policy. Their hover name is
an overlay and does not reserve content space or change the node's dimensions.
**Background display → Show on Hover** affects only background paint. It does not
resize the node, change its content fitting or remove its border/hit area. Its
fill and decorative background retain their authored settings when revealed.
An automatically sized leaf already measures its frame from the authored text.
When that intrinsic frame is used directly, its typography keeps its native
reading scale and is only reduced for actual overflow. Ordered/Gallery allocations
and manual resize previews still use the common frame basis.
**Fixed typography** retains the authored font sizes and native reading behavior
when a deliberate manual size is required. These options are independent of
Automatic/Fixed node dimensions. Changing either never rewrites the text, media,
or stored font values.

Expanded bounded containers are workspace frames: their saved dimensions take
precedence over their children, including when their mode is Automatic. Ordinary
movement and reflow never grow the frame. Open and closed dimensions and sizing
modes are independent, stored in `containerSizes.expanded` and
`containerSizes.collapsed`. Each record contains `width`, `height`, and `sizeMode`.
Dragging an edge or changing Width/Height edits only the current mode and makes
that mode Fixed. Closed containers, including empty ones, remain resizable.
The first closure creates a useful automatic cover; subsequent toggles restore
the saved frame exactly. Automatic covers fit their own content, while manually
sized covers retain their authored dimensions. Empty titles create no header.
Text defaults to Center alignment, including titles, text bodies, document
previews and Gallery entries. Choosing Left or Right in Appearance preserves
that explicit choice when content fits, shrinks or expands.

Appearance → Font offers a searchable catalog of 106 choices, grouped into sans
serif, serif, monospace, display, handwriting/script and language fonts. Each
entry previews its family. Named families use fonts installed on the device,
with a matching local fallback when unavailable; no font downloads are required.
The original Sans serif, Serif and Monospace choices remain compatible with
existing canvases. A portable canvas retains the chosen family, while its actual
glyphs depend on the fonts available on the device that opens it. New Node uses
the same searchable picker and previews its selected family before creation.

Title rotation and Text rotation independently turn their text blocks by any
angle from −180° to 180°. The adjacent controls turn by 90° or reset to upright.
Rotation leaves the node backdrop and embedded pictures upright. Automatic
sizing reserves the rotated text's footprint, and Fit to node scales that
footprint into the safe content area, including shaped backdrops. Rotated text
in fixed-size tiles uses a wrapping composition that keeps the fitted text as
large as the available space permits, rather than shrinking long rotated columns
into mostly empty frames. Authored text and font proportions remain unchanged.
Rotation is
stored with the node and survives copies, Modules, saving and offline publication.
Text remains native browser text at every angle.

Content → Text body is a rich text editor. Select words or passages and use its
toolbar to choose a font, size, color, bold, italic or underline. The selected
range stays active while a toolbar control or font picker has focus. Mixed
selections show mixed values; Clear formatting removes only the selected
passage's overrides, returning it to the node's Appearance settings. With no
selection, formatting applies to subsequently typed text. Ctrl+B, Ctrl+I and
Ctrl+U toggle emphasis; Ctrl+Z and Ctrl+Shift+Z undo and redo within the editor.
Open text editor provides a larger writing window, and Format title provides
the same passage controls for a node's title. New Node offers the same editor.

Formatting edits and typing save automatically through the normal revision and
recovery system. File actions and navigation flush pending text first. Internal
text copying retains the source's effective typography; external pasted text is
inserted literally, without interpreting pasted HTML. Existing plain text stays
compatible. The document stores UTF-16 passage offsets in `textRuns` and
`titleRuns`, with typed font, size, color and emphasis overrides. It never stores
an editable HTML document. These ranges survive snapshots, copies, Modules,
references, creation recipes and portable publications. Mixed fonts and sizes
are included in automatic sizing, fitting, Gallery pages and text rotation.

| Container | Expanded | Collapsed |
| --- | --- | --- |
| Freeform (unordered) | Balanced automatic radial rings, or preserved manual positions with free working space. | Own single content item only. |
| Horizontal (ordered) | One tight row, sharing the full usable width and full usable height. | Own content in an independently sized cover. |
| Vertical (ordered) | One tight column, sharing the full usable height and full usable width. | Own content in an independently sized cover. |
| Grid (ordered) | Equal-size, equal-shape children in a square address space sized to the next square capacity. | Own content, with cells hidden. |
| Spreadsheet | Declared rows and columns, including deliberately empty cells. | Own content, with cells hidden. |
| Gallery | One object fills the page viewport; paging has its own strip and media keeps its aspect ratio. | Parent's title and own single content item. |
| Window (ordered) | Declared spreadsheet dimensions with only the configured viewport visible; scroll to reveal other cells. | Own content, with cells hidden. |

Display Pattern applies to any non-root node. Container, Spreadsheet, and Module
parents are bounded: their own content hides while children occupy the interior.
Media and other ordinary parents are unbounded: their own content remains visible,
and child surfaces appear outside their border. Changing to a bounded type repairs
escaped child branches. Whole branches
move together, including hidden descendants and internal route points. Child
GUIDs, cell names, content, and independent collapsed states survive. Ordered
coordinates are reassigned when an ordering/variant change requires it; named
aliases continue identifying the same objects.
Expanding does not open another canvas or automatically move the camera.

The inner edge of an expanded bounded parent is a wall for manual child resizing.
Initial oversized child compositions shrink to fit the available interior.
**Expanding a bounded nested Container** fits its revealed workspace within its
current bounded owner; it does not enlarge ancestors. Ordered cells keep their common allocation.
An unbounded child uses an exterior Expansion Tray instead, as described below.
Freeform preserves valid manually authored positions and sizes and fits escaped
compositions inside its wall. Collapsing restores the child's saved cover size;
reopening restores its saved workspace size, subject to an enclosing ordered
owner's allocation. Border thickness, child inset, the parent title,
and the visible backdrop shape are reserved before fitting. Regular backdrops
retain their proportions inside the authored frame: Circle, Square, Triangle,
Diamond, Pentagon, Hexagon and Octagon do not stretch with a wide frame. The
child interior follows that actual silhouette, including its slanted edges and
curves, rather than the surrounding rectangular frame. Rectangles, rounded
rectangles and ellipses retain their deliberately variable proportions.
Freeform fitting preserves relative positions and scales the composition only
when needed; it does not delete content or change identities.

Dragging a child's resize edge or entering Width/Height stops at this inner
boundary. Resizing the parent refits its children. Ordered children keep uniform
tile allocations. Their effective width AND height divide the entire available
interior, whether that is larger or smaller than the requested cell layout.
One Horizontal or Vertical child fills the usable interior completely; additional
children divide it into equal columns or rows. Grid keeps its square address
capacity and can have deliberately unused slots. The requested spreadsheet dimensions
remain stored. Window fits the visible viewport while retaining its full
scrollable address space. Unbounded Media disclosure has no containing wall.
An unbounded branch nested inside a bounded ancestor opens in an Expansion Tray
outside that ancestor. Its source tile retains its interior allocation.
Reading surfaces fit both small and large tiles regardless of their hierarchical
position. Shaped reading tiles use their safe interior. Expanded structural nodes
fit their title separately, preserving the
child workspace's coordinates. This derived display fit never changes stored
fonts, text, or media, and restores ordinary reading styles when room returns.
Tiny frames cap the effective border so the stroke cannot consume their interior.
The authored border setting remains recoverable at ordinary sizes.

Node backdrops, link paths and selection outlines use SVG/CSS geometry; text stays
native browser text rather than becoming a bitmap. Raster pictures and videos
retain their original resolution. The shared camera supports **0.01%–102400%**,
including fractional percentage labels, cursor-centered wheel zoom and fitting
very small branches. Zoom only changes the view. Manual geometry accepts small
positive dimensions, including fractions for hierarchical detail. Browser
precision and available resources remain practical limits; this range is not a
promise of unlimited numerical precision or unlimited visible graph size.
The renderer keeps its drawing origin near the camera without changing authored
coordinates, so distant detail views do not require enormous CSS translations.
Selection outlines retain a readable screen thickness at deep zoom.

Embedded images use relative resolution by default. Their original source is
painted at the final **screen-space** size, including the camera, content fit,
Gallery nesting and Window offsets. A tiny child containing the same image as
its parent can therefore show the same image detail when both occupy the same
screen area. Ordinary raster interpolation follows the display's device pixel
density; the world-space frame never becomes a permanently reduced thumbnail.
The shared renderer avoids painting Gallery images into a small intermediate
surface and then enlarging that surface. Text, node frames and links remain
native/vector content, and raster detail remains limited by the original file's
pixel dimensions.

`src/relative-image-resolution.ts` owns this transient image layer. Images keep
their original DOM layout, alt text, selection and Ctrl-click media-window
behavior. Source decoding, resizing, camera movement, scrolling and Gallery
paging update the visible paint; enclosing scroll/content boundaries still clip
it. Updates are batched per animation frame, and scene replacement disposes the
old observers and image surfaces. Import, copies, saving, publication and media
downloads retain the same original attachment bytes. No document fields,
downsampled assets or image-size edits are generated by changing the view.

Save acknowledgements update the status immediately but preserve active canvas
gestures. A resize or drag keeps its preview and pointer capture until release;
the committed geometry then uses the normal undo and automatic-save path.

Grid uses a square address space: nine children use 3×3; ten use 4×4. Fill order
can be by row or by column. Horizontal coordinates are 1:1, 2:1, 3:1; Vertical
coordinates are 1:1, 1:2, 1:3. Ordered children have identical size and shape,
retaining the uniform cell allocation even when a child container is collapsed.
Spreadsheet and Window retain explicitly configured full dimensions and requested
pixel cell dimensions as metadata. Bounded parents fit the effective cells to
their own frame. Window viewport dimensions independently determine how much
of that address space is displayed. Coordinates remain in Relations and
references, without badges cluttering the canvas tiles.

Appearance controls `padding` at the node border, `contentGap` between content
blocks, `textMediaGap` between text and media, and `childPadding` between a
child's outer border and its parent's inner border. Content padding and gaps
default to 5 pixels, including the child inset. Ordered child tiles still touch
one another. Explicit saved insets are preserved. Hovering an expanded bounded
container or tray reveals a thin bar along its top edge showing its Tag. Clicking
that bar collapses the branch and does not enter text editing. The bar reserves
no interior space; it spans the full frame width, touches the top edge, and uses
the owning node's font family. Appearance's **Use default child inset (5 px)** action resets only
that inset, preserving the rest of the node's styling. Zero remains permitted
for a flush composition. Border thickness, pattern, color,
backdrop shape and corner radius remain independently editable. Selection,
hit testing and patterned-border overlays all use the same outer frame;
increasing the border width does not shrink or detach the selected outline.
The fill, selection and content geometry share the actual backdrop bounds.
Bounded Freeform
containers retain working space around complex child compositions. Automatic
children fill rings beginning at 3, 4, then 2 o’clock; dragging a child preserves
its manual position. Reset to automatic rings in Relations reapplies the default.
Legacy Spatial containers are shown and edited as Freeform; older authored child
positions remain stored. All disclosure stays in the same canvas.

Gallery paging is view state. It does not rewrite stored geometry, create an
undo entry, or trigger a save. A bounded Gallery retains the same frame for every
page. Exterior Gallery children share a common frame based on the collection's
authored child dimensions; content length cannot resize a page. The header is
fitted separately, and the object fills the page viewport above its two navigation
arrows. No visible page counter is shown. Images and video fit without distorting
their aspect ratios. Unbounded Galleries reserve a stable exterior page envelope
so neighboring objects can be positioned safely before browsing.
The offline viewer separates overlapping legacy tiles in its runtime copy when
a canvas is first displayed. Reset repeats this transient layout repair; the
authored publication and saved content remain unchanged.

Unbounded children nested inside bounded workspaces reveal their original
descendants in an **Expansion Tray** outside the outermost enclosing bounded
ancestor. The source tile remains in its cell or manual position. A single
solid presentation tether connects it to the tray. Individual visual links from
the source to its children are suppressed while they occupy its tray, without
deleting those relationships. The tether is not an authored link and
does not affect link counts, hierarchy or Tiers. No proxy nodes, replacement
GUIDs or new parent relationships are created. Every Display Pattern retains
its ordinary behavior inside the tray, including Gallery paging and Window
scrolling. Expanded bounded descendants continue to reveal content inside
their own frames; a deeper unbounded descendant can open another tray.

Trays use a plain container backdrop without a title, icon, banner or extra
disclosure buttons. Their corners are square by default; an explicit rounded
shape or corner-radius setting can opt into rounding. An explicitly rectangular
shape stays square even if a preset also supplied a radius. The solid connector paints above enclosing container fills
and borders so its source is visible. Hovering a container or tray exposes a
full-width collapse bar directly against its top edge. Its 28-screen-pixel
height stays usable at different camera scales, and its label follows the
visible title's family, weight and italic style. A Gallery tray reads its active
child's actual rendered title instead of its hidden source cover. A click
collapses the branch; dragging the bar moves the node or tray through the same
gesture path as dragging its backdrop. The tray source stays in its original
cell while its contents move. A completed or canceled drag never triggers a
collapse. Normal selected and keyboard-focus outlines remain available, while
unselected hover adds no extra dashed outline; authored dotted or dashed borders
are unchanged. Click the empty tray background to select
its source, or double-click it to collapse. Drag the empty rim to move its real descendants and internal route points
without moving the source. The tray remembers its source-relative origin in
`expansionTray.offset`; its initial frame is derived from the visible contents.
Drag a tray edge or corner to resize it. Its child composition scales into the
usable interior while the source tile stays unchanged. Text and media use their
normal content fitting, without rewriting stored font settings or attachment
bytes. A resized tray keeps its outer dimensions in `expansionTray.size`.
Window retains its complete address space while its visible cells fit the new
viewport, and Gallery retains a stable page frame while browsing. Nested trays
retain their independent dimensions and travel with their source tiles.
Collapsing the tray collapses its source. Collapsing an ancestor hides its trays and
tethers without resetting descendant disclosure states, dimensions, positions
or route points. Reopening restores that composition and the tray's saved size.
Moving a source or its ancestor carries its tray. A source hidden by Window scrolling or Gallery
paging also hides its tray until the source becomes visible again.

Tray position and size survive saves, copies, Modules, snapshots and offline publication.
The shared viewer uses the same geometry and disclosure behavior. Viewer users
can expand and collapse a branch, while moving and resizing a tray remain authoring actions.

Version-three trays use the compact 5-pixel inset and no banner reservation.
Older unversioned and version-two trays remain readable. On load, an older
fixed-size tray keeps its authored outer position and dimensions while its
original contents refit into the newly available interior; this upgrade is
idempotent. An older automatically sized tray keeps its content positions and
tightens around them. The document records `expansionTray.layoutVersion: 3`
on the next normal save.

Nested bounded containers have no artificial disclosure-depth limit. In small
frames, their allocated padding and title reservation adapt to use at most
one quarter of each safe axis, leaving at least three quarters for children.
Authored padding, fonts and saved mode records remain stored. This avoids the
near-zero interior that fixed reservations formerly caused in deeper layers.

A bounded nested Container can reveal its own contents inside its allocated
frame. Automatic parent fitting preserves its authored expanded/collapsed size
records instead of replacing them with cell dimensions. Its visible geometry
still follows an ordered parent's uniform allocation; explicit manual sizing
updates only the current mode when that node is independently resizable.

The shared sizing layer is `src/core/sizing.ts`, accessed through
`navigationIndex(project, sizingContext).bounds(node)`. Editor and offline
viewer use the same geometry for tiles, shape outlines, links, selection,
snapping, and navigation. Raster aspect ratios come from inert embedded image
headers. Unsupported image metadata and video previews use a stable fallback
aspect ratio; they still preserve the original media attachment.

Compatibility: old container and Canvas launcher nodes without a sizing field
use Automatic. Expanded bounded frames retain their stored workspace dimensions;
automatic closed covers fit their own content. Older leaf nodes retain
their authored dimensions; they can be switched to Automatic in Appearance.
Canvas roots, joints, and junctions retain their operating geometry. Recipes
with explicit Width or Height imply Fixed unless `sizeMode: "auto"` is specified.
Sizing modes survive snapshots, storage, recovery, modules, copies, and offline
publication.

The fixed-position version-one library compatibility scene uses exterior Media
nodes because its entry format has no enclosure dimensions. Migration to the
version-two editing graph creates real bounded containers with stored geometry.
Legacy Blocks load as blank Media nodes; their previous dormant text is retained
in inert migration metadata. There is no Block choice for new nodes.
