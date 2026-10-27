# Gallery containers

A Gallery is a Container variant. Its children are ordinary Meridian nodes: images,
videos, text entries, documents, references, or other supported node types. Instead
of plotting all these children together, the container displays one object inside
its own tile and provides Previous and Next controls.

Create one through the shared New Node form: choose **Container**, then **Gallery**.
Add objects by creating child nodes on the gallery or by choosing **Add object** in
the selected gallery's **Relations** panel. The Relations panel also lists objects
in their presentation order, allows inspecting each child's content, and has
controls to move objects earlier or later. The ordinary Properties panel can
convert an existing node into a Gallery container.

Browsing a gallery is local window state. It does not change the project revision,
trigger automatic saving, or appear in Undo. Adding, editing, removing, and
reordering objects are normal document edits and save automatically. A new window
starts with the first available object. Previous and Next stop at the ends of the
collection rather than wrapping.

An automatically sized Gallery fits its object presentation and navigation controls
instead of reusing empty space from a previous enclosing frame. The shared layout
reserves the collection's largest presentation when no particular object is active;
a view with an active object can fit that object's content. Fixed sizing remains
available for a deliberately authored display surface.

The canvas tile has arrow controls and a current-object counter. When these
controls or the gallery have focus, Left and Right browse the collection; Home
and End go to the first and last object. Video controls and text-editor keys keep
their usual behavior. Ctrl-click still opens an image in its dedicated media
window. Select the gallery normally to inspect it; no additional Inspect button
is placed in its canvas tile.
Searching a child GUID and choosing **Jump to Node** activates that object and
centers its containing gallery, including through nested galleries.

Double-clicking the parent hides or reveals the gallery in the same canvas.
Objects and their descendant link lines are embedded, so expanding a gallery
does not plot all of its children around it. Nested galleries keep independent
disclosure. Their stored coordinates remain available if they are later moved
out of the collection. The shared renderer gives offline publications the same
one-object presentation. Saved cross-canvas references currently include the
gallery summary; browsing the entire collection requires its original context.

Object order follows the parent's immediate children in the project node order.
Existing node GUIDs, attachments, links, and document metadata are retained when
converting a container. Older Gallery variants with spreadsheet coordinates retain
their addressable cells for references, but gallery presentation does not use grid
placement or show every cell at once.
