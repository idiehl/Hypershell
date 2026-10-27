# Canvas navigation and the right panel

Home and other canvases share one right inspector with Canvas, Properties,
Appearance, Content, Relations and Links menus. There is no permanent left sidebar.
The paired arrows close and reopen the panel; its divider remembers the preferred
width. Navigation primarily happens in the canvas.

## Finding content

Search appears only in **Canvas**. Ctrl+F or the toolbar search icon opens that
menu and focuses **Search canvas nodes**. It finds active node text, titles, GUIDs,
attachment filenames/MIME, complete included TXT/MD/CSV/TSV text, cell X:Y
coordinates and names, active hyperlink labels/URLs, conceptual target GUIDs,
and type/display-pattern names. Inactive legacy content remains saved but does
not produce active-content matches. Reference search follows the same live-local
and saved-external observation boundaries as the node itself.

**This canvas** is the default. A nonempty query exposes **All canvases**, which
searches the canvases included in the opened document. It does not scan all
independently saved library workspaces. Matching nodes retain their parent paths,
type, GUID, connection indicators and Jump to Node action. A small matched-field
description and inert text excerpt appear during search. Another canvas's results
include its name and offer navigation; deletion remains in its own active context.

The empty query displays only the current canvas hierarchy. Search and expanded
outlines initially render 100 rows, with **Show more** pages for additional rows.
Searching and expanding the outline do not change canvas disclosure or saved data.

Down from search focuses the first match; Enter jumps to it. Up, Down, Home and End
navigate outline entries; Left and Right navigate hierarchy disclosure. Escape
clears the query. Jump selects the requested node, reveals its ancestors if needed,
and centers the camera. Ancestor disclosure is one undoable editor change;
navigation to already visible content leaves the saved graph unchanged.

## Following references

**Relations** shows the selected node's hierarchical context and descendants.
When included references or Canvas launchers point to the node, **Referenced by**
lists their available contexts. Choose an entry to jump there. A Reference offers
**Open original context** when its original exists in the document; unavailable
originals are stated explicitly. These actions do not clone the original or write
through its reference. Independently saved library GUID resolution and source-save
notifications remain future work.

## Keeping your place

Panel scroll positions are session state, keyed to project, canvas, menu and
selected object. Returning to a panel/object or reopening the panel restores its
position. Same-context edits retain the focused control and text selection where
applicable. Outline actions retain their node/action focus through scene rebuilds.
State for a different node does not focus the previous node's fields. This state
is bounded per window and never stored in the authored canvas.

Canvas launchers navigate in the current window; Back and Forward revisit previous
contexts with their camera state. Expanding a launcher remains a separate action.
The Meridian mark returns to the library. File holds Save, Save as, checkpoints,
duplicate, import, preview, publish and export. Node, Link, Delete, Hand and Select
area use the shared workspace controls; the creation form also creates child nodes.

Below 760 pixels, the inspector becomes a drawer. Its arrow and Ctrl+F reopen it.
Ctrl-click on an image opens its original in a separate media window; Ctrl+Enter
on the focused image is the keyboard equivalent. See MEDIA-WINDOWS.md.

Validation lives in tests/canvas-search.browser.ts,
tests/inspector-continuity.browser.ts, tests/search-content.test.ts,
tests/panel-scroll.browser.ts and tests/panel-toggle.browser.ts. Browser checks use
fresh builds and isolated storage; production-package evidence belongs in the
cycle's output review, rather than being inferred from test names.
