# Website browser

Website hyperlinks open separately from a Meridian Work. A person can read a
linked reference and close its window to return to the same selected node,
disclosure state, and camera position.

## Desktop window

The native Meridian host supplies a small website browser with Back, Forward,
Home, Reload/Stop, an editable address, and the usual minimize, maximize, and
close controls. Pages load directly in its WebView, rather than inside an iframe
in a node. A site that declines framing can therefore remain readable.

HTTP and HTTPS navigation belongs in this window. It has a separate InPrivate
website profile and does not receive Meridian's editing bridge or native host
objects. Website popup requests stay inside the website browser. Work and image
windows continue to use their existing local-app window behavior.

The editor is not an HTML proxy or a website archiver. Remote pages still need
their websites and may require their own sign-in, network access, or browser
capabilities. This feature does not fetch a website into the saved Work.

## Browser-hosted Meridian and publications

The same website click requests a separate browser popup when Meridian runs in
a web browser or in a portable HTML publication. The browser supplies that
window's navigation and address controls and may open a tab instead, according
to its settings. Allow popups for the Meridian page if the browser blocks the
request. The Work remains open; no external iframe is added to the graph.

The handling applies to displayed node hyperlinks, supporting Node Hub links,
Gallery links, and links in the safe Markdown reader. A Work portal is a
different action and retains its existing Work-navigation behavior. Email links
remain with the host's existing handlers; this feature does not enable a mail
application. Original attachment downloads remain downloads.

## Authoring and compatibility

No new project field, node type, or hyperlink flag is required. Keep authoring
ordinary `http:` or `https:` destinations with an optional concise label. Existing
links gain the new behavior when their renderer and native host are updated.
Supporting links remain appropriate Node Hub content; do not create extra graph
nodes merely to open a reference.

Website browsing is temporary UI state. It does not edit node content, create a
recovery draft, change a saved revision, or alter the publication payload. HTML
attachments remain inert in the document reader; website browsing does not turn
an attachment into executable document content.

## Release boundary

The website-browser package was installed on October 25, 2026. The renderer's
361 package files and served assets match the reviewed candidate in
`output/website-browser-cycle-20261025/installation.json` and
`installed-verification.json`. The independent native package's 1,006 files
were verified at `output/desktop/`; its installation receipt is
`output/website-browser-cycle-20261025/native/installation.json`.

**Windows Start → Atlas Meridian** now launches
`I:\dev\Meridian\output\desktop\Hypershell.exe --meridian` for a Meridian-only
window with the built-in website browser. The global Hypershell/MyNet launcher,
backend, Secure MCP connection, and already-open windows were preserved. Existing
native windows retain their loaded host; a page reload updates the renderer but
does not replace that host. Open a new window using the updated Atlas Meridian
shortcut to use the built-in browser. Older hosts keep their prior external
browser behavior while the Work remains open.

The targeted Tutorial packet updates the existing hyperlink lesson, the viewer
controls lesson, and the original full-guide attachment. Native Tutorial revision
**8** was saved from exact baseline revision **7**, with no recovery draft, a
verified original checkpoint, and exact saved readback. The two lessons, four
related snapshots, and original guide bytes were changed without adding nodes
or links or altering geometry. Six actual viewer checks and six opened captures
cover the changed lessons, direct website popup, return view, guide searches,
and original download. Receipts remain together in
`output/website-browser-cycle-20261025/tutorial/`.
