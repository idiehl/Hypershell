# Meridian Tutorial maintenance

The native project **Meridian Tutorial · Learn by doing** is an editable collection
of twelve Works. Its Library entry belongs in the ordinary Tutorial container.

Project GUID: `6dd8dbdb-738e-4329-b818-bfa9ae6a9f1f`.

| Part | Work | Examples and purpose |
| --- | --- | --- |
| Start here | Learn by doing | Three learning paths, part portals, and the complete downloadable guide |
| 01 | Navigation & selection | Library, search, selection, hierarchy, Work portals, and history |
| 02 | Create, edit & resize | Tool shortcuts, Tag versus visible title, Node Hub, the Text tool, direct movement, empty shapes, and position locks |
| 03 | Appearance & backgrounds | Eleven shapes, transparent and hover backgrounds, paint examples, and four colored animated portal styles |
| 04 | Containers & disclosure | Freeform, Horizontal, Vertical, Grid, nested containment, and exterior Expansion Trays |
| 05 | Tables & scrolling Windows | Spreadsheet addresses and names, Window scrolling, cell References, and an original CSV |
| 06 | Media, documents & Galleries | Hub content stacks, first-slot display, image fitting, equal-frame Gallery pages, supported video/audio, document readers, and hyperlinks |
| 07 | Links, Pivots & Joints | A routed relationship around unrelated content, a true shared Joint, and a nonvisual remote GUID relationship |
| 08 | Live values & operations | Typed inputs and outputs, Number, Switch, runtime control, and all 21 current operations |
| 09 | References, Modules & groups | Local and saved cross-Work References, independent reusable Modules, grouping, edge locks, and inherited rules |
| 10 | Saving, recovery & checkpoints | Automatic saving, save confirmation, recovery, copies, checkpoints, and preserved disclosure |
| 11 | Export, publication & portability | Editable files, independent imports, portable HTML, viewer controls, selection, and original downloads |

## Source and identity

- [guide.json](../content/meridian-tutorial/guide.json) contains tool lessons and a
  feature inventory checked against the installed editor's actual controls.
- [lab-fixtures.mjs](../scripts/library-tutorial/lab-fixtures.mjs) declares the
  functional native examples and laboratory lesson notes.
- [labs.json](../content/meridian-tutorial/labs.json) retains their readable
  inventory and feature coverage.
- [compose-tutorial.mjs](../scripts/library-tutorial/compose-tutorial.mjs) combines
  lessons, original embedded files, stable aliases, and native examples through
  the declarative authoring API. Repeated local composition retains identities.
- Output is kept together in `output/library-tutorial-20261021/tutorial/`:
  recipe, draft, aliases, full guide, coverage, evaluation, publication, and
  rendered review. The installed readback and application receipt establish what
  was actually saved, separately from local draft files.

The initial collection has **81 lessons, 210 feature coverage entries, 456 native
nodes, 78 visible graph links, and five original embedded files**. Counts are
version-specific inventory, not a substitute for reviewing lesson accuracy.

## Teaching approach

Read one lesson at a time in a bounded, scrolling Window. Practice on real editable
nodes alongside it. The examples use native behavior: no simulated calculator,
fake spreadsheet, or decorative imitation of a live relationship. Each part has
home, previous, and next navigation where applicable. Long guidance is also kept
in one complete downloadable Markdown attachment.

Teach the installed version. Source changes pending installation do not establish
available UI. The Node Hub release includes named animated square/circle Clusters
for selected sibling branches, alongside ordinary grouping and touching-edge
locks. Audio and video use native playback when supported by the browser.
PNG/JPG conversion remains unavailable. The source inventory and saved native
lessons describe this package; its installation receipt and saved Tutorial
readback separately establish what a running installation provides.

The installed interaction-tools update changes the navigation and editing lessons:
Single Select drags from a node's text as well as its backdrop, and a node
double-click discloses its children. Text is a separate persistent toolbar tool
with shortcut T; Escape finishes inline editing and returns to Single Select.
The former hover name strips are removed. The stable lesson key
`author/collapse-handle` is retained for its replacement lesson, “Move and toggle
directly on the node”; the feature inventory uses `text-tool` instead of
`collapse-handle`.

Explicit entry through a Work portal or selector fits the currently visible
content, including Expansion Trays. F, Reset View, and Fit Work perform that same
visible-content fit; ordinary redraws retain the current view, and Back/Forward
restore remembered views. Target search remains a separate node-focused action.
Alignment lessons cover the default Align behavior, temporary edge/center/corner
guides, explicit Sticky/Free/inherited overrides, and Alt bypass. The guide's
verification metadata records the exact installed package and the native Tutorial
application receipt. The nine revised lessons and original guide attachment are
saved in Tutorial revision **3**, preserving its identities, geometry, and the
separately migrated alignment settings. Application used the host's authoring
writer lock, explicit revision and recovery guards, an original saved checkpoint,
and exact readback verification; its receipt is
`output/interaction-tools-20261021/tutorial/application.json`.

The document-reader follow-up is installed and verified separately in
`output/document-viewers-20261022/tutorial/`. Four existing lesson identities cover
embedded document reading, supporting attachments, original CSV versus native
Spreadsheet cells, and reading inside a portable publication. The same original
Markdown guide asset is updated in place. Native Tutorial revision **4** retains
all existing identities and geometry. Its guarded application receipt is
`output/document-viewers-20261022/tutorial/application.json`.

The installed readers keep complete CSV/TSV attachments outside the native grid
limits, with row and column pages, local full-content search, and byte-exact
original downloads. Markdown renders inert headings, emphasis, lists, code and
links without executing HTML or fetching remote images. Plain text, JSON, XML,
HTML and source files remain read-only text; PDF embedding requires a PDF header
and uses the local browser reader. The node's compact and reading frames are
independent; a larger Reading view is also available. DOCX/XLSX preview, office
editing, online storage and collaboration are not introduced by this update.

## Updating and verifying

### Website browser follow-up

The focused packet in `output/website-browser-cycle-20261025/tutorial/` was applied
from exact native Tutorial revision **7** to **8**, with no recovery draft. It revises the
existing Hyperlink and published-viewer lessons and two passages in the original
full-guide attachment. The existing ordinary website example remains the live
exercise; no additional nodes, graph links, or geometry changes are needed.

Teach website destinations separately from Work portals and graph relationships.
HTTP and HTTPS links open a separate browser window while the Work stays open.
The native desktop browser offers Back, Forward, Home, Reload/Stop, and an
editable address; browser-hosted Meridian and portable HTML use the browser's
popup or tab behavior. External pages need their websites, and popup settings
may require the user to allow the window. Email links remain with the host's
existing handlers, and original-file downloads retain their existing behavior.

Six actual viewer checks and six opened captures cover both changed lessons,
the complete-guide searches and byte-exact original download, and the ordinary
hyperlink example opening a separate website. The Work's URL, selected example
frame, and authored payload stay unchanged. Renderer installation and served
asset parity are recorded in `output/website-browser-cycle-20261025/installation.json`
and `installed-verification.json`; the native browser package and new launcher
are recorded in `native/installation.json`. The Tutorial's frozen candidate,
exact baseline, preservation checks, revision/recovery guard, original saved
checkpoint, and exact saved readback are recorded in `tutorial/application.json`.

Windows Start → Atlas Meridian opens the updated Meridian-only native host.
Already-open native windows were retained; reloading their web page does not
replace the native host. A new window through that shortcut supplies the built-in
website browser, while older hosts retain their prior external browser handling.

See [Website browser](WEBSITE-BROWSER.md) for the user-facing behavior and native
deployment boundary.

### Node Hub follow-up

The Node Hub release changes the lessons for creating and inspecting nodes and
for adding media. Teach the left Work/Properties/Appearance/Relations/Links/Module
inspector separately from the selected node's right Hub. The creation/selection
tools sit to the right of the left inspector. There is no Content inspector tab;
legacy Content preferences open Properties.

Use a real Media node for the Hub exercise: enter a Title, hide and reveal it
with **Show title on Work**, add Text through **Add content**, then add an image
and a document. Only the first item appears on the workspace. Move the image
into the Display slot by dragging its handle, then demonstrate the keyboard
Move up/down alternative, inline readers, **Pop out content**, editing, removal,
Undo/Redo, and save/reopen. Supporting content must remain attached to the same
node without creating extra descendants. A long supporting document must not
inflate the primary tile while a different item occupies the Display slot.

Explain structural Type independently from Hub media type. Module is removed
from ordinary creation/conversion choices; select an assembly and use the Module
tool. Existing saved Modules retain their role and reuse behavior. Supported
audio now has a native media control in the first slot and Hub, replacing the
earlier audio-download-only boundary when this release is installed and verified.

Update the corresponding guide lessons, feature inventory, full-guide attachment,
and native practice nodes using their stable identities. Preserve earlier
application receipts as history. This maintenance note does not itself update
the saved native Tutorial or establish a new Tutorial revision; record the
guarded content-application receipt separately after review and installation.

See [NODE-HUB.md](NODE-HUB.md) for the expected UI and compatibility contract.

The Node Hub package was installed and served on 2026-10-24. The native Tutorial
was then updated from revision **4** to **5** through the authoring writer lock,
an exact reviewed-baseline comparison, explicit no-recovery guard, and a saved
checkpoint. Twelve existing lessons, thirteen guide passages, the original guide
attachment, and fourteen related snapshots were updated; identities, geometry,
hierarchy, styles, functional fields, other files, and unrelated lessons were
preserved. Exact readback and the original checkpoint were verified. Receipts:
`output/node-hub-cycle-20261024/installation.json`,
`installed-verification.json`, and `tutorial/application.json`. Existing windows
retain their loaded UI until the user saves and reloads or reopens Meridian.

### Hover reading, typography, and Smart Links

The targeted packet in `output/smart-links-cycle-20261024/tutorial/` was applied
from the reviewed native revision **5** baseline to revision **6**. It revises
seven existing lessons and eight full-guide passages for
temporary hover reading, centered larger text, automatic title-driven width,
fixed-frame exceptions, and Smart Link creation, membership, direction, and gap.
Installation and served-runtime parity are recorded in `installation.json` and
`installed-verification.json`; the native guarded save, exact readback and original
revision-5 checkpoint are verified in `tutorial/application.json`. Existing native
windows retain their loaded interface until the user saves and reloads/reopens.

The Routes part also gains an original native Smart Link practice chain below
the existing examples: Planning, Delivery, and Review, with Outline and Scope
behind Planning. Opening Planning pushes the neighboring members away; collapsing
it compacts the chain. The origin, branches, and relationships are real Meridian
objects, not a screenshot or simulated widget. All previous identities, geometry,
hierarchy, styles, functional fields, other files, and unrelated lessons are
preserved. The two ordinary relationship links communicate membership; the Smart
Link spine manages layout without pretending to be a computation.

Teach hover enlargement as view state: unreadable small nodes temporarily grow
toward ordinary reading size, while readable nodes, authored geometry, and the
camera remain unchanged. New-node balanced typography uses larger centered
defaults and approximately 65% of an automatic text tile's height for its text
composition. A visible title determines automatic width. Older authored styling,
explicit typography, bounded allocated cells, and fixed dimensions retain their
authority. Choosing the Default card preset explicitly adopts the new defaults.

Review evidence is in `tutorial/browser-review/review.json`, including all changed
lessons, actual expansion/compaction, guide searches, and byte-exact guide
download. The candidate hash must match that review before guarded application.

### Centered Smart Link correction

The focused packet in `output/smart-center-cycle-20261024/tutorial/` was applied
from the exact reviewed native revision **6** baseline to revision **7**, with no
recovery draft. It updates the
existing Smart Link creation, parent choice, Arrange, and route-tool lessons,
plus the complete guide and native practice heading. Teach generation direction
separately from sibling spread: children opening downward use both left and
right space; children opening rightward use space above and below. The parent
is centered on its direct child chain when locks and real obstacles permit it.

The original seven practice identities and two ordinary relationship identities
are retained. The exercise remains below the old Routes examples, with its
supporting Outline and Scope placed on opposite sides of Planning. The actual
Smart Link chain demonstrates centered placement, visible branch-space changes,
and exact compaction after collapse. Unrelated lesson content, hierarchy,
appearance, functional fields, other assets, and unrelated snapshots are preserved.

The installed runtime and served assets match the reviewed candidate:
`output/smart-center-cycle-20261024/installation.json` and
`installed-verification.json`. The Tutorial's fourteen actual viewer checks and
eleven opened captures cover the changed lessons, both centered example states,
guide searches, and a byte-exact guide download. Its frozen candidate hash is
recorded in `tutorial/manifest.json` and `tutorial/browser-review/review.json`.
The authoring writer lock, exact revision/recovery checks, saved readback and
original revision-6 checkpoint are verified in `tutorial/application.json`.
Only thirteen related snapshots were refreshed; no new node or relationship
identities were needed. Already-open windows retain their loaded interface until
the user saves and reloads or reopens Meridian.

### Review and guarded application

Reconcile the installed editor and shared scene controls with the feature
inventory before claiming complete coverage. Keep lesson instructions short,
name the actual panel/control, and add an exercise on a native original whenever
possible. Keep unavailable features clearly identified. Update the full guide and
embedded attachment from the same sources.

Run the composer and inspect its draft. Review all Work overviews and focused
reading views. Check node/link clearance through initial and expanded states.
Exercise Gallery and Window navigation, disclosure and retained tray geometry,
all operations and representative changed inputs, References, independent Module
instances, media decoding/playback, and byte-exact original downloads.

The helpers `review-tutorial.mjs`, `review-editor.mjs`, and `review-library.mjs`
retain coverage and interaction evidence. Editor writes belong only in the
guarded disposable test host. Library harness writes are intercepted in memory.
Do not rerun these against a user's saved project as a test. Save final reviewed
content separately using the normal expected-revision, recovery, and checkpoint
protections. Preserve original GUIDs when updating this installed tutorial;
fresh-ID imports create independent copies rather than updating its Library entry.

For a narrow behavior correction, patch the existing lesson title, Tag, and body
plus the original full-guide Markdown attachment; preserve its asset GUID.
Retain all unrelated graph entities, saved geometry, styles, media, and snapshots.
Refresh only snapshots whose lesson text or attached guide changed. Do not rerun
the full composer against an edited saved Tutorial merely to update prose. The
interaction-tools staging helper captures a fresh GET, binds stable lesson keys
through the existing aliases, and proves that only its allowlisted fields changed.
Rebase it on the current revision after any separately reviewed alignment-default
migration, then use the normal revision, recovery, writer-lock, and checkpoint
protections for application.
