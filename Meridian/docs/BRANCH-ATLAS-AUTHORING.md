# Composing large information systems on one canvas

The mythology atlas now uses one canvas. The earlier 242-canvas version remains
recoverable in the `Before single-canvas atlas · saved` checkpoint. Original
nodes, connections and attachments retain their identities; old page roots are
now ordinary collapsed containers and local references.

## Composition patterns

- Use a person's node as the owner of their children. The 529-record genealogy
  uses a selected descent tree, additional-parent reference appearances, and
  separately attributed alternative accounts. Do not imply that variants agree.
- Give connections explicit owners when common ancestry is insufficient. A link
  renders only when its owner is expanded and both actual endpoints are visible.
- For an ordered composition, use an already-expanded spatial child to hold
  free-positioned tiles. Give that group near placement. Expansion can move the
  whole reading area into available space while preserving its internal order.
  Both 24-book maps and the books' passage groups use this pattern.
- Keep full source attachments. Passage tiles reflow whitespace for reading;
  recorded offsets and hashes prove that the original narrative and edition
  notes remain covered without omitted wording.
- Start with a small number of meaningful closed branches. Search reveals the
  relevant ancestry; the outline only renders the currently disclosed hierarchy.
- Inspect the actual shared viewer at overview and reading zoom. Schema checks
  alone do not catch crossing paths over text or a wheel handler blocking reading.

## Reusable tooling

`src/core/consolidation.ts` stages a canvas-to-branch conversion without mutating
the original. `scripts/branch-atlas/stage.mjs` composes the mythology material
using readable recipes and stable generated identities. The saved alias maps,
source offsets and manifest are in `output/branch-atlas`.

`verify-genealogy.mjs` and `verify-epics.mjs` exercise real viewer interactions.
`verify-package.mjs` applies the exact candidate to isolated storage, checks
checkpoint recovery and stale-write rejection, exercises save/reopen, and
round-trips native and HTML publications.

`scripts/offline-authoring/OfflineAuthoring.csproj` supports a closed Meridian
instance. It calls the same ProjectStore authoring methods used by the host API,
holds the exclusive data lock, compares complete saved/library baselines, refuses
recovery drafts, creates a checkpoint and verifies committed readback. It does
not launch a server. For a running instance use the revision-guarded authoring API.

## Representation limit

A figure with multiple parents has one canonical descendant branch and reference
appearances under the other parents. A reference exposes its witness and can open
the canonical figure's context in the same canvas. It does not recursively clone
the entire descendant tree. Very broad families, including Zeus's 85 selected
children in this catalog, occupy a large field when fully disclosed.
