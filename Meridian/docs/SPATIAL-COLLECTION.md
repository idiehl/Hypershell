# Spatial disclosure examples

This pass stages two focused changes to the existing collection. The source
projects are captured in `output/spatial-reveal/<project-id>/baseline.json`, with
both the saved document and its recovery record. The staging and review scripts
never write to the host.

## Myths in relation

The entry canvas of **Greek Mythology · Expanded reference atlas** keeps its
original title, introduction, museum images, captions, reading destinations,
sources, and object identities. The enclosing **Four ways into the collection**
frame becomes a fixed-size spatial heading tile. Its four chapter nodes also
become fixed-size spatial tiles and initially hide their two child destinations.

Double-click a chapter to reveal its destinations on this canvas. Generic lines
make the ownership relationship visible; the automatic placement engine finds
nearby space without moving unrelated nodes. The explicit **Open canvas** action
still enters the chosen reading canvas. Double-clicking again hides the children.

Only this entry canvas is reworked. The other **241 existing canvases** retain
their exact node and link data. All original asset bytes, text, titles, reference
targets, identities, and existing links are preserved. The changes add one
instruction label and eight plain disclosure links. The source branch moves as
a unit to leave a disclosure area beneath the chapter tiles.

## Spatial children

**Meridian · First connections** gains a fourth inner canvas, **Spatial children**,
and a launcher beside the existing numbers example on **Start here**. Existing
nodes and links are unchanged.

The new canvas contains two examples:

- **An idea, with room around it** reveals three nearby children. The evidence
  child uses a causal arrow toward the parent, the next-step child uses a
  chronological arrow away from the parent, and an association uses a generic
  line without arrows.
- **Words that grow into a composition** reveals two image tiles attached to its
  right and bottom edges. A third child, **A note at the margin**, stays at its
  distant authored position without a connecting line or shared edge.

The images are unchanged copies of **A probable Zeus** and **A story painted on
a vase**, already included in the mythology collection. The original captions,
including the qualified Zeus identification, are retained. The original reading
and image-provenance attachment is copied in full; it contains the museum object
identifiers, credits, Open Access statements, and source links. No new external
imagery is fetched. No video file is added to this example.

The two parents start collapsed. Their own body text remains visible and their
dimensions remain fixed through disclosure. The example's warm paper, dark green
text tile, and abutting image surfaces distinguish linked ideas from a continuous
tile composition.

## Reproduce and inspect

Run from the Meridian source directory:

```powershell
node --experimental-strip-types scripts/spatial-reveal/stage.mjs
node --experimental-strip-types scripts/spatial-reveal/review.mjs
```

The first run captures the saved/recovery baselines. Later runs reuse those
baselines rather than silently accepting live changes. Recipe aliases are kept
stable across revisions, so refinement does not allocate new identities for
the same authored additions. The manifest records expected revisions, exact
recovery tokens, canonical baseline and candidate hashes, candidate file hashes,
review canvas IDs, and the preserved/added content counts.

The review script opens the built standalone viewer from local files, blocks
network requests, checks that disclosure remains in the same canvas and retains
parent geometry, checks child visibility and loaded images, captures collapsed
and expanded views, then verifies that recollapse hides the children. Source
document previews are bounded excerpts; their full content opens through the
attachment action.

Installation is a separate root-run operation using the host's atomic
`authoring-recovery-guard-v1` endpoint after reviewing the frozen candidates.
Both the saved revision and the exact recovery token must still match. The host
creates checkpoints before applying the candidate. A changed saved document or
draft must stop installation for another review.

## Installed result

Installed on 5 October 2026 after packaged validation. Expanded reference atlas is revision 3; First connections is revision 4. Each has a **Before spatial children pass · saved** checkpoint matching its captured baseline. Live verification and exact package hashes are recorded in `output/spatial-qa/live-verification.json`.
