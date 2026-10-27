# Automatic saving

The editor commits every accepted edit automatically. Changes to text or property
fields commit when the field's change event fires, usually when focus leaves it.
Dragging, resizing, disclosure, spreadsheet edits, arrangement, undo, and redo
use the same edit pipeline. Camera movement and selection are not content edits.

A burst of edits starts a save after 500 ms without another committed action,
after five actions, or within two seconds of the first action, whichever comes
first. There is one save request in flight. Changes made during that request stay
in the draft; once its response supplies the next revision, the latest draft is
saved next. The header displays pending, saving, saved revision, or a paused
failure. Saving does not rebuild a focused field or discard uncommitted typing.

**Save** and **Ctrl+S** flush pending work immediately. Returning to the canvas
library also waits for the current commit and queued changes. Saving updates
cross-canvas snapshots, so saved-reference values advance with automatic saves.
The canvas library already commits each library arrangement separately.

Revision conflicts pause automatic commits. The editor keeps the draft open and
offers **Save a copy**. It never retries against a newer revision by silently
adopting that revision, so two windows cannot overwrite each other's content.
Network and other save failures also pause automatic commits; **Save** explicitly
retries the latest draft. This is local, revision-checked persistence, not a
multi-user merge or an online synchronization service.

Host recovery drafts retain their guarded token and original base revision. A
draft belonging to another window is never adopted or cleared. If a recovery
write conflicts, repeated background recovery attempts stop. The window also
tries to keep its own fallback draft in browser storage after failures, when
hidden, and before unloading. Fallback copies contain authored content and
attachments but omit saved snapshots, which are restored from the saved canvas.
They are limited to two million serialized characters and never evict another
window's fallback to make room. Large attachments or browser quota limits can
prevent this fallback; the editor tells the user to keep the window open and
save a copy or download the canvas when host recovery also fails. Browser backups
can be restored on reopening within the available browser profile; private app
profile lifetimes can limit their persistence.

Restoring a draft with the current saved base resumes automatic saving. Restoring
an older-base draft keeps automatic saving paused and requires a separate copy,
preserving the newer saved canvas. Closing with pending or failed content still
uses the browser's unsaved-work confirmation.

Validation is covered by `tests/autosave.test.ts` for timing, coalescing, serial
requests, and failure pause/retry, and `tests/autosave.browser.ts` for actual host
commits, cross-canvas snapshots, focus retention, in-flight edits, undo/redo,
navigation flushing, retry, and two-window conflict recovery.
