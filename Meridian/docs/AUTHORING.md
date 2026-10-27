# Meridian authoring API

Meridian's user-facing term is **Work** (plural **Works**). Version-one documents,
recipes and APIs retain compatibility names such as `canvases`, `canvasId`,
`entryCanvasId`, and `kind: 'canvas-link'`; they identify Works and must not be
rewritten in existing files. The capture CLI now uses `--work`; its earlier
filter spelling remains accepted for existing automation. For composition and
review principles, read [WORK-AUTHORING-STANDARDS.md](WORK-AUTHORING-STANDARDS.md).

The authoring API creates and updates portable Meridian projects using data. It does not execute recipes as code, fetch resources, write project files, or save to a host. Use it to compose a draft, inspect the result, render it, and revise the composition before installation or publication.

```ts
import { applyRecipe, inspectProject } from '../src/core/authoring.ts';
import { newProject } from '../src/core/model.ts';

const base = newProject('A visual essay');
const { project, aliases, report } = applyRecipe(base, {
  version: 1,
  canvases: [{
    key: 'overview', existingId: base.entryCanvasId,
    title: 'Overview', style: { background: '#F4EAD6', gridVisible: false },
  }],
  entryCanvas: 'overview',
  nodes: [
    {
      key: 'heading', canvas: 'overview', kind: 'text', title: 'Two ways of telling',
      text: 'Follow the relationship, then compare the sources.',
      x: 60, y: 50, width: 900, height: 160,
      style: { variant: 'label', fontFamily: 'serif', titleSize: 48, fontSize: 22, textColor: '#2A2530' },
    },
    {
      key: 'first', canvas: 'overview', kind: 'text', title: 'First account',
      text: 'A concise account.', width: 320, height: 220,
      style: { fill: '#193645', accent: '#EAB76A', fontSize: 18 },
    },
    {
      key: 'second', canvas: 'overview', kind: 'text', title: 'Another account',
      text: 'A different emphasis.', width: 430, height: 280,
      style: { fill: '#422F46', accent: '#D9A6A1', fontSize: 18 },
    },
  ],
  links: [{ key: 'comparison', source: 'first', target: 'second', relation: 'mutual', label: 'Compare' }],
  layouts: [{ kind: 'row', nodes: ['first', 'second'], x: 60, y: 290, gap: 90 }],
});
```

## Atomic operations and identity

`applyRecipe(base: MeridianProject, recipe: AuthoringRecipe): AuthoringResult` returns `{project, aliases, report}`. It clones the base, allocates identities, resolves references, applies layouts, and calls `validateChange` on the complete result. Invalid recipes throw `AuthoringError`, whose `diagnostics` provide a code, message and optional field path or entity identifiers. Neither the base nor the recipe is mutated, including when a late operation fails. All newly created entities receive fresh GUIDs. The base project's ID and revision remain unchanged: the API prepares an edit, and the caller owns saving, snapshot commits and concurrency checks.

`key` is a recipe-local alias, not an identity stored on the project. It must begin with a letter and contain at most 120 letters, digits, dots, underscores, colons, slashes or hyphens. Keys must be unique within each entity kind. The returned `aliases` object has `canvases`, `roots`, `nodes`, `assets` and `links` maps. The `roots` map associates each declared Work key with its root node ID. Save this manifest when future recipes will update those entities.

References are strings: the appropriate recipe alias is checked first, then an existing project ID. Forward references work for node parents, attachment aliases, reference targets and link endpoints. A `canvas-link` target may be a Work alias or ID; the API converts it to the Work root ID. A `reference` target may also name an included saved snapshot by its original ID. Alias scopes are local to the project; they are not global cross-project identities.

An `existingId` updates that entity instead of creating another. Omitted authored properties remain unchanged. Supplied `style`, `rules` and `snapping` objects merge only their supplied subfields; supplied arrays, text and geometry replace the corresponding field. A node update cannot change its kind. Explicit resets and removals are described below; omission never erases a field or entity. Changing an existing Work's title also updates its root title.

### Reusable bindings, explicit resets, and controlled removal

Save the returned aliases manifest. A later recipe can include it as `bindings`:

```ts
const revised = applyRecipe(previous.project, {
  version: 1,
  bindings: previous.aliases,
  nodes: [{
    key: 'heading', // edits the original heading GUID
    unset: ['style.radius', 'style.childPadding', 'titleRuns', 'containerSizes'],
    title: 'A revised reading path',
    style: { shape: 'none', fontFamily: 'garamond', titleSize: 44 },
  }],
  links: [{ key: 'comparison', label: 'Compare the traditions' }],
});
```

Bindings accept the `canvases`, `roots`, `nodes`, `assets`, and `links` maps. Every
ID must already exist in its matching scope in the supplied base project. A bound
declaration key updates that identity; an unbound key creates a new identity.
References can use bindings even when that entity has no declaration in this
recipe. An explicit `existingId` that conflicts with its binding, a stale ID, an
incorrect Work/root pair, or two declarations updating one identity reject the
whole recipe. Returned aliases retain existing bindings plus new identities,
removing bindings to deleted entities. Bindings belong to this project version;
they must be remapped after a fork/import that generates fresh GUIDs.

`unset` is an array on Work, Node, or Link declarations. It removes only named
optional fields before applying supplied values. Supported names include every
allowlisted `style.FIELD`, `snapping.FIELD`, and (for nodes) `rules.FIELD`.
`unset: ['style']` followed by `style: {...}` replaces the complete style; ordinary
`style: {...}` still merges. Node optional fields include `text`, `textRuns`,
`titleRuns`, `tag`, `showTitle`, `contentItems`, `smartLink`, `positionLocked`, `sizeMode`, `containerSizes`, `contentType`,
`childPosition`, `childOffset`, `expansionTray`, `collapsed`, `asset`,
`backgroundAsset`, `attachments`, `hyperlinks`, `remoteLinks`, `target`,
`targetCell`, `canvasTarget`, `dependencies`, `rules`, `snapping`, `style`,
`containerVariant`, `utilityVariant`, `childLayout`, `placement`, `attachSide`,
`grid`, `cell`, `childArrangement`, and optional executable-value fields. `asset`,
`target`, and `dependencies` are the recipe names for saved `assetId`, `targetId`,
and `dependencyIds`; `backgroundAsset` removes only the decorative background
image. Work resets accept `style`, `snapping`, and `backgroundAsset`. Link
resets accept `parent`, `direction`, `targetPort`, `sourceAnchor`, `targetAnchor`,
`points`, `label`, and `style`. Required identity/kind/geometry/title fields cannot
be unset; use `title: ''` for an empty visible title. Unknown paths, duplicates,
and a whole object plus one of its subfields reject. The final graph must still
validate, so removing text that has passage formatting also requires clearing or
replacing its runs. Asset bytes and metadata cannot be unset.

`remove: {nodes: ['obsolete-note'], links: ['old-relation']}` removes existing
aliases or IDs. Node removal includes descendants, endpoint connections, owned
connections, and obsolete local snapshots, matching editor branch deletion.
An explicitly edited link can instead retain its ID while being retargeted away
from a removed endpoint in the same atomic recipe; its final endpoints and owner
must validate. Explicit `remove.links` always removes that identity and cannot
be combined with updating it.
Work roots cannot be removed. Other Works, unattached assets, and unrelated
nodes remain intact. A surviving reference, dependency, or remote conceptual
connection to a removed node rejects the complete edit unless the recipe also
clears or retargets it. Reparent or rescue a child in an earlier edit before
removing its old branch. A removed identity cannot simultaneously be updated or
resurrected through its binding. Ordered parents touched by removal are reflowed
with the same editor rules. The API still does not save or authorize deletion on
a host; revision, recovery, checkpoint, and undo protections remain the caller's
responsibility.

## Recipe version 1

The top-level fields are `version: 1`, optional `title`, `entryCanvas`, `bindings`, `remove`, and optional arrays `canvases`, `nodes`, `assets`, `links`, `routes`, and `layouts`. Unknown fields are errors. A declaration has `key`, optional `existingId`, optional `unset` (except assets/routes), and the fields below.

| Declaration | Fields |
| --- | --- |
| Work | `title`, `snapping`, `style`, `backgroundAsset`. A new Work creates its own root. |
| Node | `canvas`, `parent`, `kind`, `title`, `titleRuns`, `tag`, `showTitle`, `contentItems`, `smartLink`, `x`, `y`, `width`, `height`, `sizeMode`, `containerSizes`, `contentType`, `childPosition`, `childOffset`, `text`, `textRuns`, `collapsed`, `asset`, `backgroundAsset`, `attachments`, `hyperlinks`, `remoteLinks`, `target`, `targetCell`, `canvasTarget`, `containerVariant`, `dependencies`, `value`, `valueType`, `closed`, `operation`, `exposed`, `rules`, `snapping`, `style`, `childLayout`, `placement`, `attachSide`, `grid`, `cell`, `childArrangement`. |
| Asset | `name`, `mime`, `base64`. New assets require all three; data must be canonical base64. Filesystem paths and remote URLs are not attachment data. |
| Link | `canvas`, `parent`, `source`, `target`, `kind`, `relation`, `direction`, `targetPort`, `sourceAnchor`, `targetAnchor`, `points`, `label`, `style`. |

New nodes require `canvas` and a supported non-Work `kind`. Their parent defaults to the Work root. `parent: null` explicitly returns an existing node to its Work root. Positions are Work-absolute world coordinates, including descendants. New ordinary nodes default to `(100,100)` and `200×120`; containers and module packing nodes default to `480×300`. Set deliberate geometry for designed compositions. Boolean inputs default false, number inputs default zero, switches default closed and operation joints default AND. Set `valueType: 'number'` for numeric outputs, switches, routing junctions and identity joints. A junction without `valueType` only joins visual routes. See [NODE-SYSTEMS.md](NODE-SYSTEMS.md) for the operation registry and evaluation rules.

Use `kind: 'media'` for a content-bearing unbounded parent. Use ordered Node Hub
`contentItems` for its displayed and supporting material; a title is separate.
Older text/image/document identifiers and legacy `contentType`, `text`, `asset`,
`attachments`, and `hyperlinks` remain compatible. Without an explicit Hub stack,
the legacy single-primary-content inference still applies. Current creation
controls use the unified Media type; Hub content does not change structural Type.

### Node Hub content

`contentItems` is an ordered array of up to 1,000 inert records. Only its first
item is displayed on the Work. Additional items remain accessible through the
selected node's Hub and larger readers/players. `showTitle` defaults to true;
false hides the visible title without erasing the title, its runs, or identity.

- Text: `{id, type: 'text', text, textRuns?}`.
- File/media: `{id, type: 'asset', assetId}`. The asset's MIME/name selects its
  supported reader/player. `assetId` resolves a recipe asset alias or existing ID.
- Hyperlink: `{id, type: 'link', url, label?}` using HTTP, HTTPS or mailto.

Each item's ID must be unique within its Hub and stable across edits and
reordering. Supplying the array replaces the complete previous order; preserve
unrelated existing items rather than accidentally dropping them. Omission
preserves an existing stack; `[]` explicitly clears it. Unsetting `contentItems`
returns to the retained legacy content, which may reveal older material.
Legacy fields remain recoverable but do not override an explicit Hub stack.

For example, the visible object below reads only "Zeus", with a longer account
and resource link available from its Hub. The hidden Title remains its metadata:

```ts
const result = applyRecipe(base, {
  version: 1,
  assets: [{ key: 'account', name: 'Zeus.md', mime: 'text/markdown',
    base64: btoa('# Zeus\n\nSupporting stories and artwork notes.') }],
  nodes: [{ key: 'zeus', canvas: base.entryCanvasId, kind: 'media',
    title: 'Zeus', showTitle: false, x: 100, y: 100, width: 240, height: 120,
    contentItems: [
      { id: 'subject', type: 'text', text: 'Zeus' },
      { id: 'account', type: 'asset', assetId: 'account' },
      { id: 'resource', type: 'link', url: 'https://example.com/zeus', label: 'Record' },
    ],
  }],
});
```

In a Reference, explicit Hub content customizes its local presentation without
changing the source, target identity or observed value. Preserve pinned asset
bytes when materializing an inherited stack; use the maintained Reference/Hub
helpers when deriving that local override. Supporting items do not enlarge a
different displayed item. Review the intended first slot and all relevant Hub
readers, provenance and downloads in the actual viewer. See
[NODE-HUB.md](NODE-HUB.md) and rulebook NODE-006 / NODE-007.

### Smart Links

Declare a visual Utility with `kind: 'junction'`, `utilityVariant: 'joint'`, and
`smartLink: {direction: 'auto' | 'down' | 'right' | 'up' | 'left', gap: 48}`.
Its immediate children are its ordered members. Keep existing identities through
bindings; reorder the canonical member order through the Smart Link helpers.
`resolvedDirection` is an optional cardinal direction retained by the automatic
solver. `gap` is finite from 8 to 10,000. Functional/numeric signals, grid
patterns and incompatible Utility settings are rejected on the rail.

Member chains spread in both directions along the rail axis and are centered
on the handle. An exterior parent-owned rail aligns that axis center with its parent.
Right/Left are horizontal sibling chains below/above the parent; Down/Up are
vertical sibling chains to its right/left. Keep the hierarchy's generation
direction while allowing siblings to use both sides of that axis. Do not offset
every new generation toward the same end of its sibling chain. Position locks
and actual obstacles retain authority when perfect centering is not possible.
Standalone rails and rails inside bounded workspaces retain their authored
handle center rather than anchoring to the entire workspace frame.

```ts
const result = applyRecipe(base, {
  version: 1,
  nodes: [
    { key: 'rail', canvas: base.entryCanvasId, kind: 'junction',
      utilityVariant: 'joint', title: '', tag: 'Topics', x: 100, y: 100,
      width: 12, height: 12, sizeMode: 'fixed', collapsed: false,
      childLayout: 'spatial', childArrangement: 'free',
      smartLink: { direction: 'down', gap: 48 } },
    { key: 'alpha', canvas: base.entryCanvasId, parent: 'rail',
      kind: 'media', title: 'Alpha' },
    { key: 'beta', canvas: base.entryCanvasId, parent: 'rail',
      kind: 'media', title: 'Beta' },
  ],
});
```

Recipes reflow Smart Links in touched Works after ordinary content/layout edits.
The shared viewer reflows its runtime copy on disclosure. For direct in-memory
edits, use `createSmartLink`, `addSmartLinkMember`, `configureSmartLink`,
`detachSmartLinkMember` and `reflowSmartLinks` from `core/smart-links.ts`.
Do not pre-space every hidden descendant or rewrite unrelated manual branches.
Retain position locks and check the actual expanded state and routed spines.
See [SMART-LINKS.md](SMART-LINKS.md) for UI, bounds and blocked-route behavior.

New ordinary nodes receive `style.textLayout: 'balanced'`, centered alignment,
32-unit titles and 22-unit body text. Automatic textual sizing targets a 65%
vertical composition budget and full visible-title width. Explicit manual
geometry/typography and allocated Grid/Window/Gallery frames remain authoritative.
Omitting the marker on an older saved node preserves its previous presentation;
it is not an instruction to migrate all existing Works. Hover readability is
temporary viewer behavior and is never persisted as an enlarged frame.

`titleRuns` and `textRuns` optionally style passages in the canonical `title` and
`text` strings. A run is `{start, end, style}`, using zero-based UTF-16 offsets
with an exclusive end. For example, `text: 'Alpha beta'` and
`textRuns: [{start: 6, end: 10, style: {fontFamily: 'garamond', fontSize: 24,
color: '#D08F44', bold: true, italic: true, underline: true}}]` style only “beta”.
Unmarked passages inherit the node's Appearance settings. Style fields are
optional; `false` explicitly removes inherited emphasis. Runs must be sorted,
nonoverlapping, contain at least one supported setting, stay within the string,
and never split a Unicode surrogate pair. Each text block accepts up to 4096
runs; sizes are finite from 1–512 px, fonts use the allowlisted catalog, and colors
use six-digit hex. `src/core/rich-text.ts` supplies normalized formatting,
replacement and rebasing helpers. Plain string updates preserve unchanged
prefix/suffix formatting; supplying runs with the update defines the full new
formatting. Arbitrary HTML, CSS, unknown style fields and invalid ranges reject
the entire recipe atomically.

`containerVariant` is the **Display Pattern** on any non-root node. `'contained'`
means Freeform; the ordered variants remain horizontal, vertical, grid,
spreadsheet, window and gallery. A Container is bounded and replaces its own
cover content with its child workspace when expanded. Media uses spatial
disclosure outside its body. Patterned working nodes may also have spatial
children. Window patterns require explicit full grid dimensions and visible
`windowColumns`/`windowRows`; external ordered surfaces retain their position in
the optional finite `childOffset: {x,y}`. See [DISPLAY-PATTERNS.md](DISPLAY-PATTERNS.md).

An unbounded parent nested inside a bounded workspace reveals its original
children in an exterior Expansion Tray. Optional
`expansionTray: {offset: {x, y}, size?: {width, height}, layoutVersion?: 2 | 3}` remembers the tray's
content origin relative to its source node and, after resizing, its outer frame
dimensions. It has no identity and does not change parentage. Both coordinates
must be finite and within +/-1,000,000,000. Optional size dimensions must be finite,
both greater than 10 and neither greater than 1,000,000,000 for compact
`layoutVersion: 3`. Version 2 retains its greater-than-40 minimum.
Unversioned legacy frames reserve their previous banner and require height
greater than 72. New trays use version 3 with a plain 5-pixel inset and no banner.
Unknown fields are rejected atomically. Without a size, the tray initially wraps
its visible content. The editor initializes this state when needed. Descendant
geometry and each node's `collapsed` flag remain the canonical content and
disclosure state. Copies, Module templates and publication retain the tray
offset and size while remapping only actual graph identities.

New recipe nodes without explicit `x` or `y` receive `childPosition: 'auto'`;
those with an authored position receive `'manual'`, unless explicitly overridden.
Recipe layouts mark their selected roots manual. Touched ordered surfaces and
Freeform parents with automatic children are reflowed using the editor's same
layout rules. This does not globally rearrange unrelated existing documents.
Manual and older Freeform positions remain authored positions. New automatic
Freeform children use balanced radial rings starting at 3, 4 and 2 o’clock.

An explicit `childArrangement` is retained for nonordered display patterns.
Repeating the same `containerVariant` on a bound node preserves its existing
arrangement when omitted. Changing the display pattern selects its normal
default: bounded Freeform and Gallery use `free`; unbounded spatial parents
use radial placement for new automatic children. Ordered Horizontal, Vertical,
and Grid/Spreadsheet/Window patterns require `row`, `column`, and `grid`
respectively; a conflicting explicit arrangement rejects atomically. Use
`containerVariant: 'contained', childArrangement: 'free'` and manual child
positions for a deliberate serpentine chronology or other authored map.

Automatically sized nodes use these dimensions as preferred reading sizes.
`sizeMode` accepts `auto` or `fixed`. Supplying Width or Height in a recipe chooses
Fixed unless `sizeMode: 'auto'` is explicitly included; this preserves deliberate
large compositions. Inspect and arrange with `navigationIndex(project).bounds`
to use effective visible geometry. See [NODE-SIZING.md](NODE-SIZING.md).

Bounded containers can remember independent disclosure frames. Each supplied
`containerSizes` record requires all three fields: `width`, `height`, and
`sizeMode`. For example:

```ts
{
  key: 'reading-room', canvas: 'overview', kind: 'container',
  title: 'Reading room', collapsed: true,
  width: 360, height: 160, sizeMode: 'fixed',
  containerSizes: {
    collapsed: { width: 360, height: 160, sizeMode: 'fixed' },
    expanded: { width: 1000, height: 720, sizeMode: 'fixed' },
  },
}
```

The node's active `width`/`height` must match its current disclosure mode;
remembered frames are restored when toggling. See the sizing guide for automatic
covers and ordered layouts. Do not omit a frame's `sizeMode` or assume that
remembering an expanded frame changes the currently displayed collapsed cover.

Legacy `attachments` is an array of supplemental asset IDs or recipe asset aliases.
Legacy `hyperlinks` contains `{url, label?}` records using HTTP, HTTPS or mailto
URLs. For an explicit ordered Hub, use `contentItems`; these compatibility fields
do not append items to an existing explicit stack.

HTTP and HTTPS hyperlinks open a separate website browser while the Work stays
open. The native desktop host provides a website WebView; browser-hosted Meridian
and portable publications request an ordinary browser popup, subject to popup
settings. The same handling covers primary, supporting Hub, Gallery, and safe
Markdown-reader links. Mailto links remain with the host's existing handlers;
original downloads retain their existing behavior.
No new recipe field is needed. See [Website browser](WEBSITE-BROWSER.md).

`remoteLinks` contains `{id, targetId, relation, label?}` records for conceptual
connections; `targetId` can be a recipe node alias or an external GUID. These
connections have no Work line and do not fetch remote data. Supplied arrays
replace their previous values and are validated atomically with the graph.

`kind: 'module'` creates an ordinary packing node that can contain other nodes and module nodes. It does not save a definition to the module library. Use the editor's **Save as module** action or `createModule` plus the module host API for that separate operation; see [MODULES.md](MODULES.md).

An ordinary `canvas-link` can open an included Work using `target`, or a saved Work outside this document using `canvasTarget: {projectId, canvasId?}`. These destinations cannot coexist on the same node. External destination identifiers are bounded local identifiers; they are not URLs, and publication does not fetch or include the destination's content. Forking or importing preserves the external destination verbatim so a copied launcher still opens the original saved Work when it is available locally.

`containerVariant` records the **Display Pattern** on any non-root parent: `contained` (Freeform), `horizontal`, `vertical`, `grid`, `spreadsheet`, `gallery` or `window`. Bounded Container, Spreadsheet, and Module nodes reveal children inside their border and show their own single content item when compressed. Media and other ordinary parents display children outside their content tile. New parents default to Freeform; saved `spatial` variants remain readable and normalize to Freeform. Automatic Freeform children fill balanced radial rings, while manual and legacy positions remain stored. Ordered patterns normalize child tiles to the same dimensions and backdrop shape. The layout is recorded by `childLayout`, `childArrangement` and `grid`. `style.shape` chooses a basic backdrop (`rectangle`, `square`, `rounded`, `circle`, `ellipse`, `triangle`, `diamond`, `pentagon`, `hexagon`, `octagon`) or `none` for floating content. Shape backdrops do not clip the node's text or controls.

`kind: 'spreadsheet'` creates an expanded enclosing grid with a 3×3 surface of 200×100 cells. Its children retain ordinary node kinds and identities and can occupy `cell: {column, row, name?}`. A reference's `target` names the parent and `targetCell` names an `X:Y` coordinate or named cell; legacy A1 addresses stay readable. Grid dimensions accept `fillOrder: 'row' | 'column'`. Window containers require a complete `grid` plus `windowColumns` and `windowRows`; `scrollDirection` selects `vertical` or `horizontal`. These bounded viewport dimensions do not replace the complete address space. `grid` settings and `childArrangement` are also available on ordinary parent nodes; see [GRID.md](GRID.md) for bounds, reference semantics and nested spreadsheet documents.

`dependencies` is an optional array of node aliases or literal external IDs, stored as the node's `dependencyIds`. Aliases resolve first, including forward references. Literal IDs may be unavailable and remain explicit requirements; declaration does not fetch, copy or reconnect a source. An omitted array preserves existing declarations; `dependencies: []` clears them. IDs must be unique, cannot name the node itself, and follow the host's bounded identifier format, with at most 1,000 declarations per node. A visual link alone does not declare a dependency.

```ts
nodes: [
  { key: 'toolkit', canvas: 'overview', kind: 'module', title: 'Decision toolkit', width: 760, height: 440 },
  { key: 'explanation', canvas: 'overview', parent: 'toolkit', kind: 'text', title: 'How the result is used', dependencies: ['result'] },
  { key: 'result', canvas: 'overview', parent: 'toolkit', kind: 'output', title: 'Result' },
]
```

Dependency declarations are preserved by publication and remapped for independent copies. `summarizeDependencies` in `src/core/dependencies.ts` inspects explicit declarations, reference targets and known functional input chains, distinguishing included context, saved observations, missing requirements and excluded snapshots. It does not claim that a snapshot contains its original operating system.

New links require source and target nodes, infer their Work from the source, and default to `kind: 'visual'`, `relation: 'causal'`. Relations are `causal`, `chronological`, `mutual`, `cyclical` and `generic`; visual `direction` can be `forward`, `reverse`, `both` or `none`. Optional `parent` identifies the node whose expansion reveals the link. Its endpoints must also be visible. Omission preserves an existing owner; `parent: null` restores inferred ownership by the nearest common ancestor, including endpoints. Collapsing a branch hides its connections rather than drawing proxy lines to the closed parent. See [LINK-DISCLOSURE.md](LINK-DISCLOSURE.md).

`functional` links carry Boolean values; `numeric` links carry finite numbers. Disclosure never changes computation. Computed connections require matching types and one source per destination input; feedback cycles are rejected. Anchors use normalized coordinates from 0 to 1; route points use world coordinates. Labels are limited to 160 characters. `splitLinkWithJunction` in `src/core/connections.ts` returns an immutable, validated split, preserving the original connection ID on the destination segment, its destination port, label, and route geometry. Both resulting segments retain the original effective disclosure owner.

New asset declarations reuse an existing or newly declared attachment only when its name, MIME type and canonical base64 data all match exactly. Different filenames stay distinct even if their bytes match. Explicit `existingId` asset updates retain that ID and affect its existing consumers. Such updates are applied before deduplication, so a new declaration cannot accidentally reuse obsolete bytes.

## Routed relationships with real Joints and Pivots

`routes` is a convenient declaration for a meaningful connection with editable
routing utilities. It creates ordinary saved `junction` nodes and links, not
decorative SVG lines or synthetic node identities:

```ts
routes: [{
  key: 'lineage', source: 'ancestor', target: 'descendant',
  relation: 'causal', direction: 'forward', label: 'parent of',
  style: { color: '#B68B4C', width: 2 },
  sourceAnchor: { x: 1, y: 0.5 }, targetAnchor: { x: 0, y: 0.5 },
  via: [
    { key: 'lineage-turn', x: 600, y: 300, variant: 'pivot' },
    { key: 'family-joint', x: 600, y: 600, variant: 'joint' },
  ],
}]
```

Waypoint `x,y` are the utility's center in world coordinates. New waypoints
default to Pivot and 12×12; optional `width`, `height`, and node `style` customize
them. Existing bound waypoints retain omitted type, dimensions, and style.
The route takes ordinary link `canvas`, `parent`, `kind`, `relation`, `direction`,
`targetPort`, endpoint anchors, label, and style. New routes default to visual /
causal; bound routes preserve omitted link settings. The owner defaults to the
endpoints' common ancestor; explicit `parent: null` restores that inference.
The terminal segment retains the route's alias and destination label/port.
Inbound segment aliases are `ROUTE/segment/1`, `ROUTE/segment/2`, and so on;
waypoint aliases appear in `aliases.nodes`. Save all of them for later edits.
All segments inherit the route's type, relation, direction, and style. Numeric
and Boolean routes use correctly typed passive utilities and still satisfy
ordinary computation/cycle/port validation.

An unchanged alias manifest and the same waypoint keys update existing IDs.
Shortening a previously bound route requires explicitly listing obsolete
`ROUTE/segment/N` links in `remove.links`; otherwise the recipe rejects rather
than leaving a hidden extra branch. Remove unused waypoint branches explicitly
only after checking whether other connections use them. Shared family joints
can be declared once as ordinary `kind: 'junction', utilityVariant: 'joint'`
nodes, then used as multiple route endpoints. Waypoints are created in route
order; references to waypoints in ordinary links may be forward references.
Each connection should express a real relationship with an informative label.

## Typed appearance

All appearance fields are optional; omission preserves the existing renderer defaults. Values are validated by `src/core/appearance.ts`. Arbitrary CSS, classes, HTML, JavaScript, external font URLs and CSS `url()` values are not accepted.

Node and Work `portalStyle` accepts `doorway`, `passage`, `wormhole`, or
`vortex`; optional `portalColor` is a six-digit `#RRGGBB` tint. A node override
takes precedence over the current workspace default. Clearing the override
restores inheritance, and omitting every color retains each design's original
palette. Node-only `backgroundDisplay: 'always' | 'hover'` changes decorative
background paint, with transparent-at-rest / visible-on-hover behavior for
`hover`; text, primary media, controls, borders, and hit targets remain visible.
`style.shape: 'none'` provides fully transparent floating content.

| Scope | Supported fields |
| --- | --- |
| Node | `fill`, `border`, `textColor`, `accent`: six-digit hexadecimal colors; `fontFamily`: one of the 106 IDs in `src/core/fonts.ts`, including legacy `sans`, `serif`, `mono`; `fontSize`: 10–72; `titleSize`: 10–96; `textRotation`, `titleRotation`: −180–180 degrees; `contentSizing`: `fit`, `fixed`; `textAlign`: `left`, `center`, `right`; `variant`: `card`, `label`; `imageFit`: `contain`, `cover`; `borderWidth`: 0–8; `borderStyle`: `solid`, `dashed`, `dotted`, `double`; `radius`: 0–48; `shape`: a basic backdrop shape or `none`; `padding`, `contentGap`, `textMediaGap`, `childPadding`: 0–1,000 Work pixels. Omitted content spacing defaults to 5; omitted `childPadding` defaults to 5, without adding gaps between ordered children. Explicit saved insets remain unchanged. Shared background fields below also apply. |
| Work | `background`, `grid`: six-digit hexadecimal colors; `gridVisible`: Boolean. |
| Work and node backgrounds | `backgroundPattern`: `none`, `dots`, `grid`, `isometric`, `linen`, `contours`, `aurora`, `stars`; `backgroundScale`: 8–512 world units; `backgroundTint`: six-digit hexadecimal color; `backgroundAssetId`: bounded embedded asset ID; `backgroundFit`: `cover`, `contain`, `tile`; `backgroundOpacity`: 0–1. |
| Link | `color`: six-digit hexadecimal color; `width`: 1–8; `dashed`: Boolean. Runtime functional signal colors retain their semantic meaning. |

`text` and `label` remain plain text. Appearance changes do not change computation, reference behavior or containment. A label variant provides an unframed presentation without creating a new semantic node kind.

Use a declaration's `backgroundAsset` to reference an embedded image by recipe
asset alias or existing asset ID. Asset allocation and exact-byte deduplication
happen before the reference resolves into `style.backgroundAssetId`. A directly
authored `style.backgroundAssetId` accepts a literal bounded asset ID and does not
resolve recipe aliases. Raster backgrounds support PNG, JPEG, WebP, GIF, AVIF and
BMP with matching MIME and image headers, up to 16 MiB; SVG and remote URLs are not
background images. A node's `asset` remains its primary content and is independent
of its background. See [BACKGROUNDS.md](BACKGROUNDS.md) for presentation and camera
behavior. Saved observations pin background bytes alongside the observed style;
copies, modules, snippets and publications remap the included asset identities.

## Layout operations

A layout has `kind: 'row' | 'column' | 'grid' | 'radial'` and a nonempty `nodes` list. Optional fields are `x`, `y`, `gap`, `columns`, `radius`, `center: {x,y}`, and `startAngle`.

- Row and column layouts begin at `x,y`, defaulting to the selected groups' minimum coordinates. Their default gap is 32 world units.
- Grid layouts use each column's largest group width and each row's largest group height. `columns` defaults to the ceiling of the square root of the item count.
- Radial layouts place group centers around `center`, or `x,y` when center is omitted. Radius defaults to 240. `startAngle` uses degrees clockwise from the positive x axis and defaults to -90.
- Every selected node moves with all descendants. Layout boxes include the complete group's geometric bounds. Internal route points move by the same displacement when both link endpoints belong to that group; route points leading outside the group retain their authored coordinates.
- Selecting both an ancestor and one of its descendants, selecting a node twice, selecting a Work root, or mixing Works in one layout is an error. Multiple layouts run in the stated order.

Layouts are optional helpers, not compulsory templates. Use deliberate absolute geometry, varied proportions, focal images, generous gaps, labels and directional connections when those express the subject better than a repeated grid.

## Inspection and lint

`inspectProject(project: MeridianProject): InspectionReport` returns `summary`, `canvases` and `warnings`. It has no side effects. `summary.nodeCount` and per-Work node counts exclude Work roots. Each Work reports visible node count, content bounds, image/document/container counts, visual, Boolean and numeric link counts, media IDs, missing media nodes, and font/fill/variant variation.

Diagnostics have `{code,message,severity,canvasId?,nodeId?,nodeIds?,linkId?,path?}`. Structural failures have severity `error`. Advisory warnings include:

- `sibling-overlap`: overlapping visible siblings in a source recipe or draft;
  the editor separates peer tiles before editing them.
- `outside-container`: a child extends beyond a non-Work parent; infinite Work roots do not act as clipping frames.
- `estimated-text-overflow`: a wrapping estimate suggests scrolling or clipping. It is not a browser measurement.
- `missing-media`, `missing-reference`, `missing-canvas-target`, `unsaved-reference`: an attachment or navigation/reference dependency needs attention.
- `uniform-presentation`: a substantial set of text nodes shares one surface, variant and typographic treatment.

The report is a review aid, not a design score. Resolve factual/structural failures,
separate overlapping peer tiles, then inspect the rendered Work at reading scale.
Enclosing ancestor frames may contain their descendants. Advisory geometry
messages are capped at 500 to keep very large projects inspectable.

## Command-line entry points

Run `node scripts/author.mjs help` for exact options. The CLI exposes `compose` for local draft files, `inspect` for reports, `inventory` for identity/content/geometry discovery, `publish` for self-contained HTML, `capture` for offline screenshots and browser metrics, `install` for an independent project with fresh IDs, and `apply` for a revision-checked update with recovery refusal and a checkpoint. The editor's Composition tools use the same pure API and apply an accepted recipe as one undoable draft edit.

Recipe aliases are not automatically recovered from display titles. For later edits, provide the exported aliases manifest through `recipe.bindings` or CLI `--aliases file.json` (choose exactly one), populate `existingId`, or inspect and select an exact entity ID. Keep the project JSON, recipe and alias manifest together when iterating on a composition. `compose` and `apply` both write `OUT.aliases.json` and `OUT.inspection.json` alongside the candidate.

`inventoryProject(project)` returns project/revision context, reusable aliases,
Works, node Tags/titles/plain text, current effective node and branch bounds,
Work-local ancestry depth, child counts, display patterns, style, connection
endpoints/labels, and attachment metadata/byte counts. It excludes base64 image or
document payloads. `inventory --input draft.json --out inventory.json` writes
that report plus `inventory.json.aliases.json`. Generated aliases such as
`node-14` represent the IDs in that specific report; retain the manifest rather
than regenerating aliases by row number after a composition changes.

Focused capture uses actual published-viewer navigation, including disclosure of
collapsed ancestors and activation of nested Gallery pages. Search places an
exact node GUID ahead of mentions in titles, text, attachments, or shared source
packets, then prioritizes exact Tags and exact titles. The normal result cap
cannot hide the requested identity behind document mentions:

```powershell
npm run author -- capture --input draft.json --out output/detail --node EXACT-GUID --scale 1
npm run author -- capture --input draft.json --out output/branch --node "Family tree" --scope branch
```

`--node` accepts an exact GUID or exact Tag. Duplicate Tags are rejected; use
the exact GUID from inventory. `--scope node` is the default and centers/fits
the requested tile; `--scope branch` fits its currently disclosed subtree.
`--scale` then sets the reader camera at the centered location (`1` means100%),
using native wheel navigation and the renderer's supported scale limits.
An optional `--work` limits the selection by exact Work ID/title. Every
capture records its selected node, scope, requested scale, actual zoom, screen
bounds, image decoding and clipping metrics in `review.json`. The saved source
JSON is never modified by exploring or capturing the viewer.

## A complete review cycle

1. Inspect the source project and decide what the reader should understand first.
   Choose the relationship and reading order before adding cards. Reuse existing
   source material and image credits accurately.
2. Write a recipe with concise visible text, deliberate dimensions, meaningful
   link labels, and typography that distinguishes headings from supporting text.
   Keep complementary reading and media about the same subject in its Node Hub
   when it need not occupy the graph. Use independent document nodes or collapsed
   child branches when they communicate meaningful objects or relationships.
   Use a separate Work when it is a distinct workspace, not merely
   a chapter or another level of detail.
3. Run `npm run author -- compose --recipe recipe.json --out draft.json`.
   Add `--input existing.json` when updating known IDs. This writes local files
   only, including an alias map and an inspection of the saved-snapshot candidate.
4. Run `npm run author -- capture --input draft.json --out output/review`.
   Open every PNG. The offline viewer uses the same renderer as the editor;
   external networking is blocked. `review.json` records actual fonts, images,
   zoom, clipped titles, nested scrolling text, and clipped controls. Inspect the
   overview, then zoom in or make a focused capture where necessary.
5. Revise and recapture concrete problems. Enclosing frames and scrollable
   document previews can be appropriate; overlapping peer tiles, unexplained
   clipping and unreadable overview scales should be corrected.
6. Use `install --input draft.json` to create an independent editable project,
   or `publish --input draft.json --out canvas.html` for a standalone viewer.
   Use the editor's Composition tools when applying recipes to an unsaved draft.

Updating an existing saved project through `apply` requires `--project`,
`--expected-revision`, `--recipe`, and `--out`. The candidate is retained locally,
a checkpoint preserves the previous saved project, and the host checks revision
and absence of recovery atomically at save time. A concurrent autosave or save
causes a conflict instead of replacing that state. The CLI requires the host's
`authoring-save-guard-v1` capability and refuses older hosts that cannot enforce
this condition. Resolve recovery in the editor before retrying; no CLI command
silently discards it.

For a reviewed composition that explicitly incorporates an existing draft,
`POST /api/projects/{id}/authoring` accepts `{project, expectedRevision,
expectedRecoverySavedAt, checkpointName}`. The recovery token must be supplied
explicitly (`null` means no draft). Under one writer lock the host checks both
baselines, checkpoints the saved version and draft separately, commits the new
revision, and consumes that exact draft. A changed revision or token, or a stale
draft base, returns a conflict without replacing content. This endpoint requires
the `authoring-recovery-guard-v1` capability; use it only after reviewing the
candidate against both originals. The original CLI `apply` guard is unchanged.

The visual atlas example includes a reusable template recipe and its researched
source notes. The template appends new Works; it does not replace existing work.
`fontFamily` uses portable system font families, not embedded custom font files.

## Selecting, grouping and reusing part of a Work

Single-click selects and inspects a node. In Single select, clicking canonical
title/body text edits it in place and selecting a passage opens a floating
formatting toolbar. Double-click text selects a word; double-click the backdrop
reveals or hides contents. Escape finishes editing and clears Work selection. Shift-click, Ctrl-click or Meta-click toggles nodes in a
multiple selection. **Select area** draws a selection rectangle on the background;
choose **Fully enclosed** or **Touching** tiles explicitly. Region selection only
selects visible tiles. Shift-drag on empty Work also draws an additive region.
This does not automatically include invisible children in the visible selection.

In the editor, drag a selected tile to move all selected branches together. Hidden
descendants move with their parents, and a descendant selected alongside its
ancestor moves only once. Internal route bends move with the selected graph;
crossing routes keep their authored bends. Arrow keys nudge the selected graph;
Shift increases the step. **Group** wraps selected sibling branches in a new,
bounded Freeform Container. It requires a common parent, preserves identities,
inherited constraints and each branch's internal composition, and creates one
undoable edit. Child branches are reflowed when their existing placements cannot
fit the new enclosure; exact Work positions are not guaranteed.
Right-click exposes grouping and arrangement; **Lock Position** anchors a
node and prevents branch or locked-Cluster movements containing it. Unlocking
restores movement; content remains editable. Ctrl/Meta+G opens grouping; Ctrl/Meta+A selects visible nodes. Delete removes the
selected branches and their contained or endpoint connections; Undo restores them.

The editor uses **Modules** to store and recreate a selection; its horizontal
selection bar and Download selection action have been removed. **Download
selection** remains available in the offline viewer. Its
preflight names a new Work and explicitly offers:

- **Include contained children, including hidden children.** Turning this off copies
  only the selected nodes; the preview reports omitted descendants and how many
  are hidden. Turning it on includes the selected branches even if some children
  are outside the rectangle or hidden by collapse.
- **Download External References?** Author-marked required context enables this
  by default. Inclusion uses available saved observations; it never fetches a
  source Work, clones a live external system or reconnects an omitted wire.

Only connections with both endpoints included are copied. An omitted connection
owner is replaced by the new Work root; the preflight reports these cases and
crossing connections. References to included nodes are remapped internally;
references to outside originals remain explicit dependencies. Copied branch roots
retain the effective inherited restrictions from omitted ancestors. Positions and
route geometry retain their authored coordinates and relative spacing.

The download is an interactive `.meridian.html` Work with fresh copied identities,
not an edit to the source. Open it in a browser or import it into Meridian for an
independent editable Work. Viewer snippets
use authored content and input values with the positions, route geometry and
expanded/collapsed state currently being viewed; temporary simulation values and
switch changes are not copied. Whole-publication **Save a copy** still downloads
the original authored publication and its original external-inclusion policy.

## Selection API

`src/core/selection.ts` exposes pure helpers for tools and automation. They validate
the complete graph and either return a valid independent result or throw without
changing the input. Existing saves and editor undo remain the caller's concern.

| Helper | Result and boundary |
| --- | --- |
| `selectionGraph(project, ids, {includeDescendants?})` | Returns `{canvasId, rootIds, nodeIds}` in stable document order. IDs must name non-root nodes from one Work; overlapping ancestor/child selections are normalized. Descendants are included by default. |
| `moveSelection(project, ids, dx, dy)` | Returns the moved project, translating the selected branch union once and its internal manual route points. Moved branch roots use free placement. |
| `groupSelection(project, ids, {title?, x?, y?})` | Returns `{project, rootId}` for a new bounded Freeform Container. Selected branch roots must share a parent; a default position is chosen clear of visible tiles. It preserves authored identities and each branch's internal composition, reflowing child branches when existing placements cannot fit the enclosure. Exact Work positions are not guaranteed. |
| `deleteSelectionGraph(project, ids)` | Returns a project without the selected branches, their owned/endpoint connections or their obsolete local snapshots. |
| `createSnippet(project, ids, {includeDescendants, includeExternalReferences, title?})` | Returns `{project, report, idMap}`. Both inclusion choices are mandatory. The result owns a new Work and fresh IDs for copied nodes, links and attachments, preserving internal references, containment, disclosure owners and restrictions. External saved observations retain their observed original identity. |

`createSnippet.report` lists copied nodes/links/attachments, included and omitted
descendants, hidden omissions, crossing connections, reassigned owners, included
snapshots, missing attachments and external requirements. A saved observation is
not an included source Work. Functional wires crossing the boundary remain
disconnected and their missing source is recorded as a requirement; a stored value
does not silently recreate the wire.

```ts
import { selectionGraph, groupSelection, createSnippet } from './core/selection.ts';

const selectedIds = [firstNodeId, secondNodeId];
const graph = selectionGraph(project, selectedIds);
const grouped = groupSelection(project, graph.rootIds, { title: 'Supporting evidence' });
const snippet = createSnippet(grouped.project, [grouped.rootId], {
  includeDescendants: true,
  includeExternalReferences: false,
  title: 'Evidence to reuse',
});
// Review snippet.report, then publish or persist snippet.project deliberately.
```

These helpers are TypeScript APIs, not new recipe fields or CLI commands. Use the
existing publication and guarded persistence tools after inspecting their results.

## Editing inherited rules with a preview

Select the parent, open **Inherited rules** in its properties, and choose **Edit
inherited rules**. Node types, connection types and operations now use named
checkboxes rather than comma-separated text. Each category has an explicit
**Inherit … from parent** option. Clear that option to choose a narrower allowance;
choices excluded by ancestors remain disabled. Selecting no choices means **None**
for that category. It is different from inheriting and is not silently converted
back to an unrestricted list.

The preview changes as the draft choices change. It identifies affected descendants
by name and kind, including hidden descendants, and shows which permissions are
added or removed. **Compare permissions** exposes the full before/after allowances.
Unchanged descendants are counted separately. Search filters the affected list;
large lists reveal additional entries with **Show more**. Conflicts identify the
specific node, joint operation, nested declaration or connection that prevents
application, including endpoint names for a connection crossing the branch.

**Apply rules** is available only when the declaration changes and the complete
graph remains valid. It makes one undoable edit. **Cancel** or closing the dialog
discards the rule draft without changing Work content or creating a recovery
edit. If the Work changes while the preview is open, Apply refuses the stale
preview so the current state can be reviewed again. Explicit Save persists the
accepted rule change through the normal revision and recovery protections.

The existing semantics remain binding:

- A node's own declarations constrain its descendants. Its own kind and operation
  are constrained by its ancestors, not by its own descendant declarations.
- A missing category inherits the ancestor allowance. An explicit empty array
  permits nothing. Removing a local declaration returns to the inherited allowance
  and cannot escape an ancestor restriction.
- A nested declaration cannot list permissions its ancestors exclude, even when
  no current child happens to use those permissions. The preview reports this as a
  conflict instead of silently rewriting the nested declaration.
- A connection must satisfy the inherited connection permissions at both endpoints.
  Its disclosure owner controls when it is visible, not which permissions apply.
- These are the current node/link/operation constraints. They do not define an
  extensible domain runtime, continuous signals or new conversion/feedback semantics.

`src/core/rule-impact.ts` exposes the same read-only preview for authoring tools:

```ts
import { effectiveInheritedRules, previewRuleChange } from './core/rule-impact.ts';

const inherited = effectiveInheritedRules(project, parentNodeId);
const report = previewRuleChange(project, parentNodeId, {
  nodeKinds: ['container', 'text', 'number', 'joint', 'output'],
  linkKinds: ['visual', 'numeric'],
  operations: ['ADD'],
});
// Review report.affected and report.conflicts before preparing a validated edit.
// report.canApply is false for a conflict or an unchanged declaration.
```

`effectiveInheritedRules` returns the complete effective `nodeKinds`, `linkKinds`
and `operations` arrays from the node's ancestors. `previewRuleChange` accepts a
complete replacement of the selected node's own rule object; `undefined` means
remove its local declarations and inherit all categories. It does not mutate the
project, execute a system, save a file or apply the proposal. The result includes
`changed`, `canApply`, `descendantCount`, `unchangedCount`, `affected` and
`conflicts`. Each affected row includes `nodeId`, title, kind, hidden status,
`before`, `after`, and per-category added/removed values. Conflicts use the ordinary
graph validator's node and connection identities. Caller-side application must still
validate the current graph and preserve undo/revision checks; a preview is not an
authorization to apply against a different source state.

