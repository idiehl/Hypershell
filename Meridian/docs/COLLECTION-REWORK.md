# Collection containment and typed-system pass

The October 2026 collection pass edits the existing authored canvases in place. It
keeps existing entity IDs, original text, attachment bytes, reference targets,
Boolean controls and relationship meanings. The two existing recovery drafts are
the authoring baselines for their respective collections, including their moved
nodes and the Visual atlas's three draft self-loop links.

| Saved collection | Existing canvases revised | New canvases | Added containers |
| --- | ---: | ---: | ---: |
| Greek Mythology · Expanded reference atlas | 241 of 242 | 0 | 503 |
| Greek Mythology · Visual atlas | 5 of 5 | 0 | 7 |
| Greek Mythology · Gods, Epics and Roman Worlds | 36 of 48 | 0 | 107 |
| Meridian · First connections | 2 of 2 | 1 | 2 |

The collection now has 298 canvases. This is an editorial pass over existing
content, not a new factual edition or a replacement collection.

## Reading hierarchy

- The illustrated atlas opening has four real canvas-link parents. Each opens its
  destination and independently expands to reveal two more specific destinations.
- The complete directory groups existing launchers into Families & divine stories,
  Poems & their worlds, and Objects, sources & experiments.
- All 48 individual epic books separate an expanded event sequence from an
  independently expandable account/source branch and interpretation branch.
- Genealogy maps have a formal family-relationship parent, more compact person
  cards, readable type and a separate source/variant shelf. Existing parentage
  edges and alternative accounts retain their meaning.
- The two selected-family overviews use an explicit visual routing junction for
  Hesiod's six-child branch. All six original child-link IDs remain. A new trunk
  connects the original six-child source to the junction; no parentage was added.
- God dossiers have nested, individually expandable story containers. Hesiod
  reading sequences and the earlier four-book movement pages have individual
  passage or book containers. Greek/Roman comparison pairs remain visible by
  default and can be collapsed separately.
- Original source attachments stay in expandable source shelves. Existing
  comparative constellations, galleries and contained Boolean exercises that did
  not need restructuring retain their original composition. The manifest lists
  each untouched canvas.

## Numbers, decisions and junctions

Open **Meridian · First connections**, then its new **Numbers, decisions and
junctions** launcher. This working canvas uses editable number nodes, numeric
connections, MULTIPLY and ADD operation joints, a numeric routing junction, an
exposed numeric switch and a GREATER comparison with a Boolean result.

With Run active, unit price 12 and quantity 3 produce subtotal 36. The handling
charge of 4 produces total 40. The review threshold is 40, so `40 > 40` is false.
Changing the price to 15 produces subtotal 45, total 49 and a true review result.
The routing junction shares the same subtotal with the readout and ADD joint; it
does not mix the two destinations. The total and review result are ordinary
siblings of the calculation container and remain visible and live when that
container collapses. Opening the order switch clears the dependent total and
review decision to the viewer's Unknown state while the independent subtotal
remains available.

The existing Logic lab also places its output beside its calculation container.
The recovery draft's manually moved canvas header and calculation layout remain
the starting positions.

## Reproducible authoring and guarded installation

`scripts/collection-rework/stage.mjs` reads only captured originals under
`output/collection-rework/`, uses the core authoring API, and writes candidate
documents, recipes, aliases, inspections and a manifest. It never contacts the
live host. New IDs are freshly allocated when staging; do not regenerate a staged
set after its reviewed hashes have been approved for deployment.

`scripts/collection-rework/verify.mjs` renders the actual standalone viewer from
the current built assets and checks numeric evaluation, reactive input changes,
junction sharing, collapse/expand, independent siblings, switch clearing,
recovery of results, chapter navigation and the visual family junction. Its
screenshots and results are under `output/collection-rework/interactive-qa/`.

`node scripts/collection-rework/install.mjs` is a read-only preflight. It checks
the entire saved document and recovery document against captured originals using
canonical SHA-256 hashes, as well as exact revisions and recovery timestamps.
The preflight records candidate hashes. `--commit` performs one guarded authoring
request per collection using the host's `authoring-recovery-guard-v1` capability.
Each request checkpoints the prior save and recovery before replacing that
collection. A concurrent save or recovery update rejects the request. Receipts
and the actual saved documents are retained in the output directory.

All final candidates pass model validation and content/identity invariants. Their
inspection reports contain no sibling-overlap or outside-container warnings.
Representative overview, detailed book, family, comparison, story and numeric
canvas screenshots were inspected and revised. Long original source documents
remain scrollable and downloadable; not every reading page was individually
reviewed at every possible expansion state.
