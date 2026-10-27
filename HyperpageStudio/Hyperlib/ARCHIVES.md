# Publish to Atlas Archives

Hyperlib remains the authoring library. Atlas Archives is its public reader.

1. Open a Collection, Work, or another container in Hyperlib and enter **Edit**.
2. Choose **Publish to Archives**. Hyperlib saves current page writing, then
   creates or reopens a local publication draft.
3. Check the objects to include. Container checkboxes select their descendants;
   a partially selected container shows a mixed state. Required ancestors are
   included so the public hierarchy stays intact.
4. Choose **Sync to Archives**. The desktop sends the reviewed snapshot using
   its protected publisher connection. A receipt links to the public branch.

Closing the review keeps the draft. A failed sync keeps the selection and the
previous public edition. Retrying the same draft is safe. Changes to writing or
presentation require **Refresh review**; newly created objects stay unchecked.

Only checked objects are updated. An unchecked object already online remains
published, using its previous edition. Local deletion does not delete public
content. Removing public material is deliberately separate from publishing.

The package carries selected content, required ancestor appearance, each
selected Work's reusable components/styles/scripts, and referenced media. It
does not carry other page documents, local history, credentials, or whole Work
content projections. External image and media URLs remain external URLs.

The receiver runs the matching Razor/Blazor renderer, preserving interactive
components. Standard Hyperlib Work projects are supported. External NuGet or
project references, custom host startup, MSBuild hooks, linked files, and
unresolved dynamic asset paths require adaptation before publication; the
exporter reports unsupported inputs instead of silently dropping them.
Publication is limited to 8,192 files, 256 MiB expanded and a 128 MiB upload.

## Connection and deployment

The desktop credential is DPAPI encrypted for the current Windows user, under
the Hypershell application-data directory, keyed to the library's absolute
path. It is never sent to page scripts or saved inside the portable library.
`tools/Connect-HyperlibArchives.ps1` accepts a SecureString publisher key for a
new installation. The server stores only its SHA-256, outside edition data.

`HyperLib.Archives` serves `/Archives` as a separate loopback service behind
Atlas nginx. Its fixed SDK and host compile immutable selected editions.
Publication compares the prior release, validates hashes and paths, and
renders every selected Work before atomically replacing the public catalog.
The service is an owner publishing boundary: authored C# executes as the
dedicated Archives service account. It is not a multi-tenant code sandbox.

The Atlas checkout's `deploy/ARCHIVES_RUNBOOK.md` documents artifact preparation,
candidate checks, scoped activation and rollback without replacing the main
site. No deployment command publishes library content.
