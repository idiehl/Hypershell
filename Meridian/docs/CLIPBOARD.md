# Copy and paste nodes

Select a node in the canvas and press **Ctrl+C**, then **Ctrl+V** to paste a copy nearby. On macOS the equivalent shortcuts use Command. Multiple selected nodes copy together, retaining their relative positions and links between them. Every pasted node and copied link receives a new GUID, including the hidden descendants of a copied container.

A copy retains the node's content, media, attachments, appearance, utility subtype, and disclosure state. References between copied nodes are redirected to the corresponding copies. Links to nodes outside the selection are not duplicated. A copied Canvas launcher continues to open its existing destination.

Paste selects the new roots and returns the workspace to Hand mode. It is a single undoable edit and saves automatically. Repeat Ctrl+V to make another independent copy. Copying alone does not change the canvas.

The node clipboard stays available while navigating between canvases in the current Meridian window. It retains the content captured at copy time. If the nearest free position is outside the visible area, the camera pans to show the copy while retaining its zoom.

When an input, text editor, or creation dialog has focus, these shortcuts retain their ordinary text-editing behavior.
