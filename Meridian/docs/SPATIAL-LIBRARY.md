# Spatial canvas library

> Historical disclosure/window milestone. The current home workspace uses the
> ordinary canvas editor and its inspector, creation form, tools, autosave and
> history. Section-specific authoring and the separate library shell are removed.
> See [One canvas interface](UNIFIED-LIBRARY.md). Current browser coverage is in
> `tests/library.browser.ts` and `tests/library-navigation.browser.ts`; these
> suites exercise shared workspace controls rather than the former library UI.

The canvas library uses the same scene and disclosure engine as an authored
canvas. A single click selects a tile and opens its properties and contents in
the right sidebar. Hidden children remain inspectable in this sidebar. When the sidebar was closed, pointer selection defers opening it until after the double-click gesture window; its selected contents update immediately. This prevents the sidebar from intercepting a second click on a tile near the right edge. A double
click reveals or hides children in the current visual space. Only the explicit
**Open canvas** action opens a dedicated window for the saved canvas. The home
window stays on the library, retaining its selection, camera and layout.

The inspector and tile launcher use the same window-opening flow. New canvases,
demonstrations and imports started from home also open in their own windows;
the new saved canvas is added to the home library without resetting its view.
A window is reserved during the user gesture before asynchronous storage work,
using the same-origin `/?window=waiting` screen. It receives the chosen project
and canvas URL after the pending library layout is saved. A failed layout save
closes the unused window and preserves the downloadable home draft. A blocked
window reports a useful error and leaves home interactive.

Canvas windows retain normal editing, recovery and revision conflict checks.
Multiple canvases in one saved collection can be open at once; a stale save
preserves its local draft and reports the conflict rather than overwriting
another window's saved work. Internal canvas navigation remains within its
dedicated editor window. Each window's document title identifies its current
canvas.

Library tiles keep their own size when expanded. Children appear as independent
tiles joined to their parent with a generic, undirected relationship. The
relationship describes ownership of the library entry, not a computational link
between the contents of the saved canvases.

Disclosure preserves the current view and unrelated tile positions. The shared
placement engine moves a newly revealed child branch only when its automatic
position intersects another visible tile or can be brought closer to its parent.
Already nearby, valid positions remain stable. Moving a parent carries its
descendants; dragging a tile stores
`placement: "free"` so disclosure does not later reposition it. **Place near
parent** returns a dragged entry to automatic placement. **Arrange** is the
explicit action that reflows the entire visible library.

Existing version 1 library documents remain valid. The only new saved entry
property is optional `placement: "near" | "free"`. No documents are rearranged
merely by opening the library. Old positions that were inside enclosing parent
frames are repaired locally when their parent is expanded. Revisions, conflict
handling, undo, and download-before-reload protection remain in force.

## Landing-page audit, 2026-10-05

The live library had 299 entries at revision 9. Compared with its revision 8
backup, the only difference was `My canvases`
(`a3d00829-1577-4dec-8591-24495b6c885d`) changing `collapsed` from `true` to
`false`; every entry position, title, parent, and target was unchanged. All four
saved collections matched their prior installed documents exactly, and no
recovery draft existed at the time of inspection. The former disclosure handler
called a complete library arrangement and fitted the entire expanded branch,
which could make the landing canvas appear to transform or move away.

The new handler performs local disclosure with a preserved camera. The audit
does not infer any unsaved browser state from the absence of recovery files.
