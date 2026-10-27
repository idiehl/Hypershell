# Image windows

Ctrl-click directly on a raster image to open its original attachment in a separate image window. Keyboard users can focus the image and press Ctrl+Enter. A plain click selects the node. Ctrl-click on other node content still toggles node selection. The image gesture does not move the node or expand its children; Ctrl-drag does not open a window.

Raster previews inside document and reference nodes use the same gesture. Their canvas tiles do not need a separate Open attachment button. Other document attachments keep that button. The image window starts fitted to its available area, offers actual-size viewing, and downloads the original attachment bytes.

`openMediaWindow(asset, title)` in `src/media-window.ts` takes the resolved asset directly from the scene. It works with an unsaved attachment and does not save or reload the canvas. Cross-canvas references use the image in their saved snapshot, including when the draft contains newer bytes under the same asset identity.

Call `bootMediaWindow()` before editor or viewer boot and return when it returns true. Editor windows use the same-origin `/?window=media&mediaChannel=…` route so the managed desktop host can create a trusted child WebView. Standalone publications reopen their own file or URL with a media fragment, retaining offline operation and embedded viewer code. A blob preview belonging to the loopback app uses the app media route because the desktop host does not admit blob popups. Its handshake distinguishes the sandboxed opener's opaque origin from the child window's origin; both directions still require the exact other window and the one-time channel.

The window exchanges a one-time channel handshake with its opener. Each side checks source-window identity, effective origin, and the channel token. Only recognized raster MIME types and base64 asset data are accepted. The receiver constructs plain text and an image element; it never inserts author-supplied HTML or executes attachment content. After receiving the bytes, the window removes its message listener and drops its opener reference. Closing the window releases its object URL.
