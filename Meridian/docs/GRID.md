# Ordered containers and cell addresses

A spreadsheet node is a formal parent whose immediate children occupy visible
cells. A cell holds an ordinary Meridian node, so it may contain text, a numeric
input, an original document attachment, media, or another spreadsheet with its
own children. Expanding and collapsing stay in the same visual canvas. The parent
retains its full expanded size while its closed appearance is compact. Each child
retains its own collapse state and unique identity.

Ordered Display Patterns are Horizontal, Vertical, Grid, Spreadsheet, Gallery
and Window. Freeform is unordered and uses balanced radial rings for automatic
children while preserving manual placements. These patterns can appear inside
bounded Container nodes or outside unbounded Media and other ordinary nodes.
Spatial is a legacy input variant, migrated to Freeform by current editing tools.
A coordinate is relative to the child's
immediate parent, never a global replacement for its GUID. `1:1` in two different
containers identifies two independent locations. Addresses are shown in Relations
and references rather than stamped on the canvas tile's corner.

## Persistent data and references

`MeridianNode.grid` stores `{columns, rows, cellWidth, cellHeight, gap?,
showHeaders?, fillOrder?, windowColumns?, windowRows?, scrollDirection?}`. Fill
order is `row` or `column`. Window requires both viewport axes; scroll direction
is `vertical` or `horizontal`. Each immediate child may have `cell: {column, row, name?}` with
one-based coordinates. Names such as `primary_source` are optional, case
insensitive, and unique within the same parent. Coordinates are unique within
that parent as well. Names must start with a letter or underscore, use at most 64
letters, digits, underscores, dots or hyphens, and cannot look like a coordinate.

Grid axes have at most 512 columns or rows, and their rectangular extent has at
most 16,384 cells. Cell dimensions are 36–10,000 world units, with a 0–1,000 unit
gap. TypeScript and the native host validate these limits, coordinate occupancy,
and inherited node/link/operation permissions before a complete edit is accepted.
Reducing a grid cannot strand an occupied cell outside its dimensions.

A read-only reference stores its parent identity in `targetId` and an address or
name in `targetCell`. For example, `{targetId: sources.id, targetCell: '2:2'}` or
`{targetId: sources.id, targetCell: 'primary_source'}` observes the node occupying
that location. Legacy `B2` still resolves to `2:2`. Moving an item to a different cell changes what the address reads;
the original GUID still identifies that particular item. An empty or unavailable
cell stays unresolved. Numeric cell references can supply typed numeric links and
joints; a text or document reference presents that cell's content and attachment.

Same-canvas references observe the live child. A reference in another canvas
observes the parent, cell identity, metadata, content, attached bytes and evaluated
value from committed snapshots. Moving or renaming an unsaved source never leaks
into that observation. A save updates the committed observation. Publication and
explicitly included snippet context package the required parent and cell snapshots
without manufacturing the original canvas or its working system. Forks remap
owned identities while preserving relative coordinates and names.

This is an addressable information surface. It does not introduce a formula
language, spreadsheet recalculation engine, XLSX editor, merged cells, or cell
spans. Those behaviors remain separate product decisions.

## Arrangement and scale

`arrangeChildren(project, parentId, pattern, options?)` returns an immutable,
validated `{project, movedRootIds}`. The selected container variant controls its
ordering even if the caller supplies a different generic arrangement pattern:

- **Horizontal / Vertical:** a tight single row or single column. Coordinates
  increment as `1:1, 2:1, 3:1` or `1:1, 1:2, 1:3` respectively.
- **Grid:** use the next square capacity: nine objects fit 3×3; ten use 4×4.
  Children fill by row or column according to `fillOrder`.
- **Spreadsheet / Window:** retain the user-defined full axes and cell pixel
  dimensions. Window shows only its specified viewport axes and scrolls in the
  selected direction. Offscreen cells retain their coordinates and GUIDs.
- **Freeform:** automatic children fill radial rings beginning at 3, 4, then 2
  o’clock. Manual positions preserve the authored composition and free working space.

Ordered child tiles share identical dimensions and backdrop shape. The first
child's backdrop supplies the shared shape. Spreadsheet and Window use explicit
cell pixel dimensions; automatic ordered variants derive a uniform tile size
that accommodates their content. Ordered tiles have no inter-cell gap. The
parent/child border inset defaults to 5 pixels and is configurable in Appearance.
Generic Free, Row, Column, Radial and Stack arrangement APIs remain available for
authored compositions; they do not override an ordered parent's defining layout.

Each moved child carries its full branch, including hidden descendants. Authored
route bends move only when both endpoints receive the same translation. Cell
names follow the child when ordered coordinates are reassigned. Automatically
sized bounded ordered layouts fit the needed contents; bounded Freeform preserves
working space for revealed child branches. Fixed layouts retain their authored
extent. Bounded parents reserve their title above the child surface and hide their
own content while expanded. Unbounded parents retain their own single content
tile beside the external child surface. Ordered children retain uniform dimensions even
when a nested spreadsheet is collapsed; its contained descendants are hidden.
Expanding or collapsing a nested cell reflows enclosing ordered ancestors in the
editor and portable viewer.
This permits small closed previews of much larger information surfaces. Choose
appropriate node sizes and font sizes, then zoom into the content to read it.

The shared `gridGeometry` and `gridCellBounds` helpers describe world-space
geometry for editor and portable viewer. Cell addresses use one-based X:Y values.
Ordinary cell nodes keep their own presentation, selection and editing behavior.

## Documents and authoring

The editor can embed an original spreadsheet document in a cell. CSV and TSV
documents additionally produce a nested native spreadsheet with individually
editable visible cell nodes, retaining the original file as an attachment. Other
spreadsheet formats retain their original bytes as document nodes; they open in
their spreadsheet application rather than pretending to have been parsed.

`parseCsv(text, delimiter?)` handles bounded plain data, quoted commas or tabs,
escaped quotes, and multiline Unicode fields. `exportCsv(rows)` produces CSV
text. Neither executes formulas or imported code. Current editing limits the
native document import to 2,000 populated fields and original attachments to
16 MiB.

Declarative authoring recipes accept `kind: 'spreadsheet'`, `grid`, `cell`,
`targetCell`, and `childArrangement`. Existing recipe layouts remain general
visual layouts; use `arrangeChildren` to establish addressable cell layout after
creating a hierarchy. Example:

```ts
const authored = applyRecipe(base, {
  version: 1,
  nodes: [
    { key: 'sources', canvas: base.entryCanvasId, kind: 'spreadsheet' },
    { key: 'citation', canvas: base.entryCanvasId, parent: 'sources', kind: 'text',
      cell: { column: 1, row: 1, name: 'primary_source' },
      title: 'Hesiod', text: 'Theogony, lines 116–138' },
    { key: 'source_alias', canvas: base.entryCanvasId, kind: 'reference',
      target: 'sources', targetCell: 'primary_source' },
  ],
});
const arranged = arrangeChildren(authored.project, authored.aliases.nodes.sources, 'grid');
```

The caller owns undo, saving, snapshot commits and revision checks. Grid tools
prepare changes without bypassing those protections.
