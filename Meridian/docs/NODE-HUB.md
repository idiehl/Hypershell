# Node Hub

Node Hub binds several content items to one Meridian node while keeping its
workspace representation concise. It is independent of the node's structural
Type and child display pattern.

## Workspace and selection

The ordinary inspector is on the left. It retains Work, Properties, Appearance,
Relations, Links, and Module, with its saved panel choice, width, search, and
hierarchy behavior. The creation, selection, text, reset-view, and history tools
begin immediately to its right above the workspace.

The right Node Hub belongs to an actively selected node. It closes when that
selection is cleared. An unselected Work root is not a synthetic selection. On
narrow displays the panels use opposite edge overlays with explicit dismissal,
so the workspace remains available.

The former Content inspector tab is removed. A saved Content preference opens
Properties, preserving access to structural controls.

## Title and display slot

At the top are **Title** and **Show title on Work**. Hiding the title removes its
workspace header without erasing the saved title, formatting, Tag, or search
identity. Title remains separate from the node's structural role and first
content item.

An empty Hub has an **Add content (+)** button. Adding the first item gives it
the **Display slot**. Only this first item supplies the tile's media, text, or
hyperlink. Supporting items remain attached to the same node and are read in
the Hub; they do not become child nodes or enlarge the primary tile.

A bounded Container still replaces its cover content with its child workspace
while expanded. Structural controls and navigation remain available on their
applicable node types. Changing which content item is first does not change a
node's type, descendants, connections, or identity.

## Add, edit, and reorder content

**Add content** opens a modal form with **Content type**. Its choices are:

| Choice | Input and presentation |
| --- | --- |
| Text | Authored text, with optional text formatting. |
| Image | Supported raster file with an inline image preview. |
| Video | Supported video file with native playback controls. |
| Audio | Supported audio file with native playback controls. |
| Markdown | A Markdown file or authored Markdown source rendered by the inert reader. |
| CSV / TSV table | An original table file or authored CSV source, with paging/search and original download. |
| PDF | A PDF attachment using the supported browser reader. |
| Text / code document | A supported text or source file shown as inert text. |
| Hyperlink | URL and optional Label, opened only through a user action. |
| File attachment | An arbitrary retained attachment; unsupported preview formats keep their original download. |

Confirming the form adds the staged item and any new attachment in one edit.
Cancel leaves the node and project assets unchanged. **Edit content** uses the
same form and preserves the item's identity. **Remove content** removes the item
from the ordered stack; removing the Display slot promotes the next item.

Drag **Move content** to another item's position to reorder. **Move content up**
and **Move content down** provide a keyboard-accessible alternative. Reordering
changes the display immediately when a different item becomes first.

Each section provides an inline preview and **Pop out content**, which opens the
appropriate larger reader/player. Document viewers retain their supported local
search, pagination, scrolling, and original-file download. Pop out is a trusted
viewer dialog, not a permission to execute attachment HTML or arbitrary code.

Website Hyperlinks open a separate browser popup while the Work remains open.
The desktop host supplies its own website WebView with navigation controls;
browser-hosted Meridian and portable publications request a browser popup or
tab. This includes primary and supporting Hub links. Email links remain with the
host's existing handlers; original downloads retain their existing behavior.
See [Website browser](WEBSITE-BROWSER.md)
for the native-host and publication boundaries.

Content changes use the normal editor undo/redo, automatic saving, recovery,
revision-conflict, and checkpoint behavior.

## Compatibility and storage

The saved node fields are optional `showTitle` and ordered `contentItems`.
Omitting `showTitle` preserves the legacy visible-title default. Each content
item has its own stable ID and an inert `text`, `asset`, or `link` representation.
Media formats are attachment MIME/filename distinctions within an asset item;
they do not replace the node's structural Type.

Legacy nodes retain their original `text`, `assetId`, `attachments`,
`hyperlinks`, and `contentType` fields. Opening a Hub derives an ordered view
without rewriting the Work: the selected primary item comes first, followed by
the retained supporting content. An explicit Hub edit writes `contentItems`;
dormant compatibility fields remain recoverable and do not override that stack.
An explicitly empty stack displays no primary content.

Only first-slot content and visible title participate in tile content sizing.
The complete stack remains available to supported search, reference snapshots,
module reuse, selection copies, publication, and native package flows. Saved
Reference observations retain their own attachment bytes. A Reference without
its own content stack shows the source's stack with a **From original** badge.
The first Hub edit creates a local presentation override: the Reference keeps
its own title, target, executable fields, and observed value, and the original
node remains unchanged. Required pinned media bytes are retained in that local
override, even when the current source has since changed.

TypeScript validation, host validation, declarative authoring, and publication
allowlisting share this contract. The current model bounds a node's stack to
1,000 items and retains the existing attachment/project capacity checks, including
the 16 MiB individual upload bound. These bounds do not imply that every browser
can decode arbitrary media or that every file type has an editable viewer.

## Structural types and Modules

Ordinary node creation still chooses Type. Media, Container, Work portal,
Reference, operations, values, and utilities keep their existing roles.
Module is removed from ordinary creation and conversion choices. To create one,
select the desired assembly and use the Module tool. Existing Module nodes and
immutable stored definitions remain compatible and can be inserted independently.

## Publication

The shared scene displays the first item. The published viewer's node Hub exposes
the supporting items and their larger viewers without adding them to the graph.
Publication includes used Hub attachments and respects its existing dependency
and saved-reference inclusion policies. Recipient interactions remain separate
from authored data.

This feature does not implement the clean composition, transparent desktop host,
PDF rendition, or high-resolution image-export proposals in
[future_work_cycles.md](future_work_cycles.md); that proposal remains deferred.

## Implementation and verification references

- [Content schema and legacy projection](../src/core/content.ts)
- [Hub editing form and ordered controls](../src/node-hub.ts)
- [Shared Hub previews and larger viewers](../src/hub-content-view.ts)
- [Inspector and Type controls](../src/inspector-panels.ts)
- [Shared scene](../src/scene.ts)
- [Publication payload](../src/publication.ts)
- [Host validation](../src/host/ProjectValidator.cs)
- [Inspector documentation](INSPECTOR-PANELS.md)
- [Tutorial maintenance](MERIDIAN-TUTORIAL.md)

Release evidence must distinguish source, isolated test package, installed
package, and already-open native sessions. A source change alone does not prove
that an existing window has loaded the new UI.
