# Core Template Library

The library has two top-level collections:

- **Core Template Library** contains alphabetical category Works, with one object or cohesive module per Page. Identical implementations share an entry whose provenance lists every originating Design Group.
- **Demo Works** contains the original collections, preserving their hierarchy, Work/Page identities, authored content, source projects, and media.

Each reference Page includes its name, stable `HLC-…` ID, originating templates, a visual example, and expandable source files. The preview is an isolated snapshot of the actual Razor component, including its styles and local media. Interactive behavior runs when the object is used in a Work; reference previews do not execute scripts or submit forms.

## Use an object

1. Browse a category Work or search its entries by name or ID.
2. Copy an entry's `HLC-…` ID.
3. Open **Core Library** in Hyperpage Studio's Hyperlib Editor and paste the ID into search. An exact ID lookup works even when a different category or Design Group is selected.
4. Choose **Add to Template**, then save the Template. Imported sources, styles, scripts, media, fields, and defaults remain editable and independent of the source Design Group.

The expansion adds 100 foundation components, all 58 earlier Hyperpage starters, and 44 modules from the current Atlas public website, Archives, and Admin interfaces. Together with the 18 original component implementations, this provides 220 distinct components. Existing layouts, configured examples, palettes, typography, Work styles, and custom templates remain available. The 588 original shop entries included 342 copies of the same 18 components; the expansion is measured against distinct implementations rather than duplicate cards.

Foundation forms and Atlas account/payment/admin patterns are local demonstrations, with explicit local-result copy. Connect application-specific authentication, storage, submission, and payment services when adapting them into a real site. No external framework or hosted service is required for the included modules. Fonts use the library's existing local font system.

## Catalog maintenance

`Hypershell.CatalogTool` seeds missing Design Groups without replacing edited groups. The encyclopedia installer first stages and validates all source and preview assets, then commits the hierarchy in one revision-checked operation. An existing installation is idempotent; Undo and Redo restore the whole hierarchy. It refuses stale revisions, changed sources, ambiguous object IDs, and unowned destination directories.

The reference collection is a versioned snapshot. Editing an imported Template does not silently rewrite its Core Library source or this reference edition. Page content remains authored through Hyperlib; Template implementation stays in Hyperlib Editor.

Archives publication includes only the selected catalog Pages' documentation and preview assets. The receiver accepts the bounded catalog static-asset subtree, while retaining its restrictions on Host code and startup configuration.
