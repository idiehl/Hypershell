# Meridian Work authoring standards

The user-editable rulebook is [MERIDIAN-RULES.txt](MERIDIAN-RULES.txt). Read its
current saved contents alongside this document. Rules entered by the user are
instructions within their stated scope. The rulebook's ACCEPTED and INHERITED
entries guide authoring; PROPOSED entries, review notes, unfilled templates and
Ideas and Questions remain nonbinding. Preserve the user's wording
unless asked to edit it; the user does not need to maintain duplicate rule lists
here. This document supplies the maintained framework and inherited guidance.

This is the maintained source for reusable composition principles. It is read by
the `meridian-authoring` Codex skill and routed from the project's `AGENTS.md`.
It persists independently of a chat's context. Changes to these files should be
reviewed and backed up with the project; a skill does not alter model weights
or guarantee that every aesthetic decision will be correct.

## Status and authority

- **Accepted:** a principle explicitly established by the user, with its scope.
- **Inherited:** an existing project instruction, carried forward without
  claiming it was approved as part of the new formal review.
- **Proposed:** a candidate awaiting the user's decision. Do not enforce it as
  an axiom or describe it as the user's preference.
- **Superseded:** retained only to explain which accepted rule replaced it.

Specific current user instructions take precedence. Principles address the
declared subject and scope; an approved Greek mythology composition does not
automatically prescribe every other Work's palette or structure.

## Accepted terminology

**TERM-001 — Work / Works.** Accepted October 19, 2026, from the user's explicit
request to rename the application object. Use Work in navigation, inspectors,
creation controls, viewer text and authoring discussion. Preserve user-authored
text and version-one compatibility identifiers such as `canvases`, `canvasId`,
`entryCanvasId`, and `canvas-link`. Existing documents remain readable without
identity or field-name migration.

## Accepted authoring requirements

The user explicitly established these directions on October 20, 2026 through the
sectioned-rulebook request and subsequent dictation with four screenshot examples.
Later October 21–22 corrections and scoped examples extend this accepted set.
Their full wording, scope, review criteria, and practical distinctions live in
the rulebook; maintain them there rather than duplicating the complete rules here.

- **ROUTE-001:** Never overlap a node with a link; keep routes clear of unrelated
  node bodies and content. Intended endpoints and necessary containment/tether
  crossings are distinguished in the rulebook.
- **ROUTE-002:** Rearrange first; route necessary extended links around node edges
  with saved routing utilities, without inventing false shared connections.
- **NODE-004:** Choose a document or embedded spreadsheet when it presents the
  material more effectively than a collection of nodes.
- **CONTAINER-005:** Use meaningful expandable groups to reduce the visible size
  of large Works and disclose relevant content without losing access or context.
- **LAYOUT-001 / LAYOUT-002 / LAYOUT-004:** Decide what space and distance mean;
  choose progression, radial organization, a Master, or equal peers as the
  subject requires, and guide the viewer's traversal accordingly.
- **LAYOUT-005 / LAYOUT-006:** For a requested oriented tree, use genuine branching
  generations downward or rightward rather than a single row, column, or chain.
  Distinguish spatial tree direction from node Display Patterns, and name Works
  for their contents. Accepted in the user's October 21 correction; EX-005 retains
  its scope and evidence.
- **LAYOUT-007:** Any branch must be able to reveal all of its sub-nodes without
  overlapping or interfering with unrelated nodes. Review the fully expanded
  branch, including deeper descendants and its neighboring content, rather than
  relying on a clear collapsed overview. Accepted October 22, 2026; EX-007 retains
  the user's exact direction and the scoped positive screenshot example.
- **SCALE-001:** Make effective use of workspace through variable scale and
  differing levels of detail.
- **SCALE-002:** Vary size and proportion for importance or effective use of
  available space; keep the local meaning clear. EX-006 is the user's October 22,
  2026 positive example of variable scale making very effective use of space.
- **SCALE-003:** Fit the reading surface to the content. Use appropriate scrolling
  or document presentation rather than shrinking long text into an oversized
  frame; distinguish a Window viewport over children from body-text scrolling.
- **PRESENTATION-004:** Maintain a coherent palette, typography, and overall style
  throughout each Work, including authored embedded diagrams.
- **PURPOSE-004:** Compose a graph, timeline, or chart of the subject's
  relationships. Do not lay out the Work like a web page or a traditional text
  document. Supporting reading material can retain its appropriate document form.
- **NODE-005 / PRESENTATION-005:** Hierarchical object nodes identify the object
  itself. Keep labels concise; use short document/section titles such as
  "Type Index" and "Index Guide", preserving precise technical object names.
- **NODE-006 / NODE-007:** Use Node Hubs for complementary material about the
  same subject without adding it to the spatial graph. Choose the first Display
  slot deliberately; preserve separate nodes for distinct objects or meaningful
  relationships. Hub content does not change structural Type or functional
  behavior. The user's October 24 Node Hub direction establishes this use case;
  it does not require universal Hub use or automatic migration of older Works.
- **PRESENTATION-006:** Remove visible workspace/dataset statistics, navigation
  instructions, organization commentary, citations/source commentary, and
  irrelevant file-name/format scaffolding from the primary graph. Preserve
  provenance, original attachments, necessary factual qualifications, and complete
  sources in metadata or supporting documentation. This is not permission to
  erase evidence or misrepresent a relationship.
- **LAYOUT-008:** Maintain a consistent growth direction and routing rhythm
  across peer branches. Staggering and different link lengths are useful when
  they provide room and clear routes; an accidentally detached, reversed branch
  does not belong in an otherwise coherent horizontal hierarchy.
- **LAYOUT-009:** Use Smart Links for bulk chains that reserve visible expanded
  branch footprints and compact on collapse; ordinary links retain the Work's
  basic relationships. Preserve deliberate manual layout and locks.
- **LAYOUT-010:** Center revealed Smart Link child chains on their parent. A
  downward hierarchy spreads siblings left and right; a rightward hierarchy
  spreads them above and below. Keep generation growth consistent while using
  both directions of the sibling axis. Locks and real obstacles remain constraints.
- **PRESENTATION-007:** New node defaults center text, use a roughly 65% vertical
  composition budget and fit the complete visible title with automatic width.
  Hover readability is transient and does not replace legible composition.
- **SCALE-005:** Use **Minimum Effective Variance**, the least scale difference
  that effectively serves the composition. Spend available workspace to reduce
  viewer effort; prefer spacing, staggering, and clear routes over extreme size
  changes that cram content into a thin aligned strip. This refines SCALE-001 /
  SCALE-002 without withdrawing their approval of useful variable scale.

The graph-first and Minimum Effective Variance directions were accepted
October 22, 2026. EX-008–011 retain
the user's four C++ screenshots and the precise corrections. The examples are
failure evidence, not certification of the entire Work or its subsequent revision.
NODE-006 and the installed contract in NODE-007 were added October 24, 2026.
See [NODE-HUB.md](NODE-HUB.md) for the UI/storage contract and
[AUTHORING.md](AUTHORING.md#node-hub-content) for declarative authoring.

The user may dictate future additions in conversation. Translate and organize
those ideas into the saved rulebook, preserving intent and stable IDs. Explicit
directions establish acceptance within their stated scope; extra agent-inferred
rules remain Proposed. A transcription request does not itself request a rewrite
of the reviewed Works.

## Inherited authoring baseline

These rules come from the existing project `AGENTS.md`; they remain in effect
while we review examples and refine the formal standard.

| ID | Principle | What to inspect |
| --- | --- | --- |
| BASE-001 | Compose a visual information system around the relationship being explained. | Whether lineage, sequence, comparison, or image annotation can be understood from the composition rather than from a list of disconnected cards. |
| BASE-002 | Use meaningful, labeled relationships; preserve alternative accounts and functional semantics. | What each route asserts, whether a Joint/Pivot clarifies it, and whether conflicting accounts remain distinguishable. |
| BASE-003 | Give the primary reading path useful scale, hierarchy and whitespace; keep complete sources accessible. | Overview orientation, readable focused passages, long-text presentation and navigation to full reading material. |
| BASE-004 | Treat images as interpretations with their own provenance and uncertainty. | Subject relevance, useful subject captions, correct dates/cultures, source/credit access and qualified identifications; keep citation scaffolding outside the primary graph under PRESENTATION-006. |
| BASE-005 | Preserve existing identities and requested content through declarative, inspectable updates. | GUIDs, aliases, attachments, reference destinations, hierarchy and expected revision/recovery handling. |
| BASE-006 | Review actual rendered behavior and revise material visual failures. | Opened overview/detail captures, clipped text, contrast, missing images, crossing routes, Gallery/Window/disclosure behavior and publication consistency. |

Additional section baselines drafted in the rulebook are marked PROPOSED until
accepted. The user has approved specific properties in EX-005, EX-006, and EX-007;
no complete example Work has yet been certified through the new formal review.
The present collection is review material, not automatic proof that every
existing design or saved expansion state is an approved exemplar.

## How to record a principle

For each concrete user decision, retain:

1. Stable ID and short statement.
2. Status, date, exact scope and source of approval/correction.
3. Reason and expected viewer behavior.
4. A positive example and, when useful, a contrasting failure example.
5. An observable review criterion and any mechanical check that can enforce it.
6. Exceptions, tradeoffs, and superseded IDs when applicable.

Use original project/Work/node GUIDs, a retained recipe or native document, and
overview/detail captures to identify an example. Describe the approved property
precisely. Do not equate a whole Work being praised with every property in it
being universally required. Keep proposed examples separate from accepted ones.

## Work review record

No Work has yet been certified against the new formal review process. Use this
record shape as the collection is reviewed; create entries only for real reviews.
The user's first four reviewed failure examples and later scoped positive examples
are retained in [the example register](authoring-examples/README.md). They establish
the specific corrections or successful properties recorded there, not approval or
certification of each complete Work. The October 22 examples include exact copied
screenshots, the user's approval wording, and read-only corroborated Work/node IDs.

| Field | Record |
| --- | --- |
| Work | Title plus project and Work GUIDs |
| Purpose / audience | What a visitor should understand or do |
| Reviewed evidence | Native/recipe version and opened overview/detail captures |
| Successful properties | Specific qualities the user accepts |
| Corrections | Concrete problems and intended behavior |
| Principle decisions | IDs accepted, proposed, rejected, or superseded |
| Verification | Relevant rendered and behavioral checks |

## Enforcement boundaries

Use deterministic validation for identity, graph validity, source-metadata
presence and format, dimensions, asset loading, and supported data. Source
accuracy and interpretation still require evidence and judgment. Use actual interaction checks for paging,
disclosure, zoom and navigation. Use approved examples and visual judgment for
legibility, explanatory strength and composition. Neither a rubric score nor
an agent's self-report replaces rendered review.

The live collection, application code, publication output and deployed website
are distinct artifacts. Applying authoring standards does not authorize a
deployment, public publication, broad content rewrite or recovery deletion.
