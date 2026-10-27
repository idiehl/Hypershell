# Canvas and node backgrounds

Canvas and node appearance share the same background collection. In the right
side panel, choose **Appearance**. Select a node to edit its backdrop, or clear
the selection to edit the current canvas background. The Canvas root's appearance
also opens the canvas controls.

**Background preset** offers Plain; dot grids at 16, 24, 32, and 64 pixels;
square grids at 16, 32, 64, and 128 pixels; an isometric grid; linen; contour lines;
an aurora wash; and a star field. **Background size** can set another spacing from
8 to 512 canvas units. **Pattern color** and **Background opacity** control their
contrast. Canvas grids move and scale with the camera, so zooming keeps the
pattern attached to the canvas rather than to the window.

**Upload background image** accepts PNG, JPEG, WebP, GIF, AVIF, or BMP up to 16 MiB.
The original raster bytes are embedded in the project. Image backgrounds can fill
and crop, show the whole image, or repeat as tiles. Repeated canvas images follow
the camera; a single contained or covering canvas image acts as a centered
wallpaper. Tile size and image opacity are editable. **Remove background image**
returns to the selected pattern, and removal is undoable.

A node's background sits behind its own content and follows its backdrop shape,
including circles and polygons. Changing a background does not replace the
node's title, body, primary media, attachments, or GUID. No backdrop and unframed
text continue to display floating content; their background paint stays hidden
until a backdrop shape is chosen. Image-fit controls for primary media and for a
background image remain independent.

Background edits use ordinary Undo/Redo and automatic saving. Embedded images and
typed appearance fields are preserved by save/reopen, independent copies,
modules, snippets, and offline HTML publications. Editor and viewer use the same
renderer. Built-in decorative patterns are generated from Meridian-owned inert
geometry; custom uploaded SVG, HTML, scripts, URLs, and raw CSS are not accepted
as backgrounds.

Source checks live in the background core, host, appearance, and browser suites.
The protected package review in `scripts/background-cycle/verify-package.ps1`
uses a separate copy of saved data and creates a fresh fixture on port 5293. It
exercises the background collection, exact uploaded raster bytes, shaped-node
clipping, camera scaling, automatic saving, reopening, Undo, and offline viewing,
and checks that the existing Zeus gallery and all other saved collection graphs
remain unchanged. Executed test and installation evidence belongs in the cycle's
completion report.
