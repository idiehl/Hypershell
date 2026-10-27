# Meridian authoring example register

These records retain the user's October 20–24, 2026 reviews and attached
screenshots. The first four are concrete **failure examples** for accepted rules in
[the Work rulebook](../MERIDIAN-RULES.txt). They do not certify a complete Work or
claim that its saved content has been changed. Project, Work, and node GUIDs were
not supplied for EX-001 through EX-004; do not infer those identities from a
visible title alone. Later records identify live metadata corroboration where it
is available and distinguish it from the screenshot's unknown saved revision.

The screenshot copies are retained in this folder rather than depending on the
original temporary clipboard paths. Each was copied without modification, and
its SHA-256 hash was checked against its source.

## EX-012 — Center Smart Link children around their parent

**User direction, October 24, 2026:** A downward-opening branch should use the
horizontal space on both sides of its parent. A parent belongs near the center
of its child chain, rather than at the chain's far end toward the other nodes.

**Retained evidence:** [Original screenshot copy](2026-10-24/12-one-sided-smart-link-chain.png).
SHA-256: `45168a65622f8261ca1d06400b1318a4fe40dad2e6b6f4853095f0565e695f44`.

**Observed failure:** In the visible C# namespaces hierarchy, the subject and
System are at the left end of their horizontal child spines. Their children
populate only below and to the right, so each generation drifts toward the bulk
of the surrounding hierarchy and leaves usable space to the other side.

**Accepted correction:** Center the parent on the child chain. Downward-opening
children spread left and right; rightward-opening children spread above and
below. Directional consistency governs the direction in which generations open,
not a compulsory drift along the sibling axis. Preserve member order, dynamic
clearance, and explicit locks. Genuine fixed obstacles may constrain exact symmetry.

**Rule:** LAYOUT-010, refining the interaction of LAYOUT-005, LAYOUT-008 and LAYOUT-009.

**Review criterion:** Expand adjacent branches and an asymmetric nested branch.
Check that unconstrained child chains extend on both sides of their parent,
keep the intended generation direction, avoid unrelated content, and compact
without drift. The screenshot documents the failure and requested correction;
it does not certify every expansion state or the complete Work.

**Identified context:** The visible subject is C# namespaces, with System and
its Activities, AddIn, Buffers, CodeDom and Collections branches. The existing
Work identity is `617d8cba-c876-4b0d-925f-5ec50489eed5` in project
`47f9024f-2a26-4a9a-91b9-83927029fbf3`. The image itself does not establish a
saved revision or prove the state of unrelated Works.

## EX-008 — A Work is a graph, timeline, or chart

**User direction, October 22, 2026, attachment 1:**

> Creating a Meridian Work is akin to creating a graph, timeline, or chart.

**Retained evidence:** [Original screenshot copy](2026-10-22/08-graph-not-document.png).
SHA-256: `6e8352ebb4dda191fec78b26fb0df13722d78933dcdf1693c0cba948ab197220`.

**Observed failure:** An oversized title, introductory navigation prose, and a
"Scope & reading" essay occupy the left side. The object hierarchy is distant
and much smaller. Counts and instructions describe the Work instead of presenting
the subject through its relationships, producing the feel of a document page.

**Accepted correction:** Compose the subject as a graph, timeline, or chart.
Keep nodes concise and remove visible workspace/dataset statistics, navigation
instructions, organization commentary, and citation scaffolding. Preserve sources,
attribution, necessary factual qualifications, and extended reading in metadata
or supporting documents rather than deleting them. The rule concerns the primary
graph; an embedded document can still be a document.

**Rules:** PURPOSE-004 and PRESENTATION-006.

**Review criterion:** Read the graph itself without its introductory essay. Its
objects and spatial relationships should communicate the subject. Check that
supporting material remains reachable without reintroducing meta text in nodes.

## EX-009 — Nodes identify content without meta labels

**User direction, October 22, 2026, attachment 2:** A node in a hierarchical
collection should contain the object itself, and nothing more. Documents and
sections should have simple one- or two-word descriptive titles.

**Retained evidence:** [Original screenshot copy](2026-10-22/09-content-not-meta-labels.png).
SHA-256: `28372f4df3d5bb37106db45b0a82c0ecc78953709f40a9b229ba25516854744a`.

**Observed failure:** The document cards say "Download the complete guide" and
"Complete type index · original CSV", followed by format/filename scaffolding.
The large "C++ standard library" root includes navigation instructions, category
counts, and commentary on classification technique.

**Accepted correction:** Use **Index Guide** and **Type Index**. Remove redundant
file-type and filename labels from the visible collapsed node, retaining the
original attachment and metadata. Hierarchical nodes display their actual object
names rather than counts, organizational prose, or repeated explanations. Preserve
precise technical identifiers where shortening them would identify the wrong item.

**Rules:** NODE-005, PRESENTATION-005, and PRESENTATION-006.

**Review criterion:** Inspect each visible node and document cover at reading
scale. Its text identifies the content directly. Verify that attachments, their
original identities, and full supporting material still open correctly.

## EX-010 — Peer branches share a coherent direction

**User direction, October 22, 2026, attachment 3:** Most branches expand left to
right, while a detached branch uses inconsistent long links, opens downward, and
eventually turns left. It feels disconnected from the arrangement.

**Retained evidence:** [Original screenshot copy](2026-10-22/10-inconsistent-branch-direction.png).
SHA-256: `4007c98c789784a7f989146cc9e1396fcc5053251bcf0f55251cafa30c37f793`.

**Accepted correction:** Bring the outlying peer branch into the same left-to-right
growth structure and route rhythm. Link lengths may vary and siblings may be
staggered to preserve space and clearance. Uniform direction does not require
perfect columns, identical lengths, or a false hierarchy.

**Rules:** LAYOUT-008, supported by LAYOUT-005 and ROUTE-001 / ROUTE-002.

**Review criterion:** Trace all peer branches and multiple generations. Check
direction, attachment points, route clearance, and the relationship to neighboring
branches. No accidental detached or reversed route should remain.

## EX-011 — Minimum Effective Variance

**User direction, October 22, 2026, attachment 4:**

> the goal should be to identify the "Minimum Effective Variance" - that is, the least possible difference in scale should be used.

**Retained evidence:** [Original screenshot copy](2026-10-22/11-excessive-scale-variance.png).
SHA-256: `19481406a8309975474a41a5e8d601f75068d990ad6c7af95e15a5d2dd01ddd6`.

**Observed failure:** A giant root occupies much of the view while categories,
families, and tiny type strips are compressed into a narrow aligned area. The
extreme scale differences increase the effort needed to read and traverse it.

**Accepted correction:** Use the least scale variance that effectively serves
the composition. Spend workspace to establish direction, stagger nodes, and vary
link lengths through clear space rather than shrinking branches to cram a page.
Judge how much panning, zooming, and tracing the viewer needs to see the material,
rather than minimizing its occupied footprint. No numerical ratio was prescribed.

**Rules:** SCALE-005 refines SCALE-001 / SCALE-002, with LAYOUT-007 and ROUTE-001 /
ROUTE-002 preserving full-expansion clearance. The scoped positive properties in
EX-006 / EX-007 remain accepted; they never certified the entire Work's range of
scale, text, or arrangement.

**Review criterion:** Compare peer and adjacent-level sizes at a useful reading
scale, then inspect fully expanded branches. Try spacing, staggering, and route
changes before accepting a drastic size jump. The viewer should not need repeated
large zoom changes merely to move between neighboring levels.

### Evidence scope for EX-008 through EX-011

These are the user's supplied **failure examples**, not after-images or proof
that the C++ Work has already been repaired. The user separately requested a
revision of the existing C++ Work during this cycle. Its saved result and actual
viewer review must be recorded as revision evidence, not inferred from these
original screenshots.

The visible title and content concern **C++ classes and types**. Its project
`2a9aa31d-b13c-4d0b-9192-b1a94b7c0fce` and Work
`6bdf2562-d8db-44ae-af8e-31cfc3d738f7` were previously corroborated by read-only
GETs for EX-006 / EX-007; recording the new attachments performed no new live
GET and establishes no exact saved revision. See the separate
[revision-principles provenance record](2026-10-22/revision-principles-provenance.json)
for byte-exact attachment copies, hashes, dimensions, and the identity limit.

## EX-006 — Variable scale makes effective use of available space

**User approval, October 22, 2026, attachment 1:**

> The first screenshot is an example of using variable scaling to take advantage of space very effectively.

**Approved property:** The composition uses different node and branch sizes to
fit useful levels of detail into the available space. The visible C++ subject
categories, their families, and smaller type lists share one branching structure
without requiring every level to occupy the same-sized card. This approval is
specifically about effective use of space through variable scale.

**Retained evidence:** [Original screenshot copy](2026-10-22/06-variable-scale-space.png).
SHA-256: `2d7dd67df4497ff5b8d6cdd332994070134068f7fcefdd66e099eaf7c17097dd`.
It shows the "Text, formatting & patterns" and "Files, streams & locales"
branches, including "Regular expressions" and "String & span streams".

**Rules:** SCALE-001 and SCALE-002. This is a positive example supporting the
existing accepted variable-scale rules, not a requirement to copy its exact
palette, typography, proportions, screen zoom, or subject taxonomy.
The later accepted SCALE-005 correction limits unnecessary variance; this earlier
scoped approval does not override Minimum Effective Variance.

**Review criterion:** Compare the overview with focused reading views. Size
variation should use available space effectively and preserve useful exploration
of smaller branches; do not treat an overview's small text as evidence that
every passage should be read without zooming.

**Identity and evidence limits:** See the shared live context below. Approval of
this property does not certify the entire Work or every layout and disclosure
state in it. The screenshot and its bytes remain the stable review evidence even
if the live Work is subsequently edited.

## EX-007 — A branch can fully expand without interfering with other nodes

**User approval and direction, October 22, 2026, attachment 2:**

> The 2nd screenshot is similar. It demonstrates that it should always be possible to open all the sub-nodes of a branch without overlapping or interfering with other nodes.

**Approved property and accepted requirement:** Plan enough room for all
descendants of a branch to be opened while unrelated nodes remain separate and
usable. The image places smaller families and type lists in clear bands beside
their categories, alongside other branches. The user explicitly identifies this
as the intended full-expansion behavior.

**Retained evidence:** [Original screenshot copy](2026-10-22/07-unobstructed-branch-expansion.png).
SHA-256: `a956a02c1ac87ea9e465028f9d4b36809abf2dec1621bd48ad1ba869ef4ac795`.
Visible context includes "Text, formatting & patterns", "Files, streams &
locales", "Concurrency & asynchronous work", and "Time, calendars & time zones".

**Rules:** LAYOUT-007, supported by SCALE-001 / SCALE-002 and the existing
ROUTE-001 / ROUTE-002 clearance requirements. Intentional containment of a child
within its own Container is different from interference with unrelated nodes.

**Review criterion:** Fully expand every layer of each branch with neighboring
unrelated content visible. Check frames, text, controls, route labels, and
segments. Those unrelated objects must remain visible, distinguishable, and
operable. A clean collapsed overview is insufficient evidence of this property.

**Evidence limit:** This screenshot records a positive example and the user's
universal authoring requirement for branch expansion. A single static image
does not mechanically verify every possible expansion state in the saved Work,
or certify the whole Work's appearance, text, or later revisions.

### Shared live context for EX-006 and EX-007

Read-only GETs of the local Library catalog and project on October 22, 2026
corroborated the visible Work title, category titles, Tags, and hierarchy:

- Project: **C++ · Language & Layout Atlas**,
  `2a9aa31d-b13c-4d0b-9192-b1a94b7c0fce`.
- Work: **C++ classes and types**, `6bdf2562-d8db-44ae-af8e-31cfc3d738f7`.
- Work root: `fd5f4a6d-7ad1-4811-a7bb-b7ddd4d8b9ae`.
- "Text, formatting & patterns": `0014465b-5a77-4e0b-a1ae-62488ab9d1ea`.
- "Files, streams & locales": `3784dbf0-6e00-4698-9f6a-cf05614dfd5c`.
- "String & span streams": `076b313e-7b94-42ea-b4ba-f08feba7ad22`.
- "Concurrency & asynchronous work": `d0a38a9e-ed19-4a72-96de-dc21dd2e37c6`.

The GET returned saved revision **23**; the screenshots do not establish that
revision as their exact source. [Provenance metadata](2026-10-22/provenance.json)
retains retrieval time, response hashes, original attachment paths, exact copied
image hashes, and the user's quoted approval. No project mutation was made to
record these examples.

## EX-005 — Tree orientation describes branching generations

**User review, October 21, 2026:** The radial collection example "works well
enough." The previous horizontal/vertical examples did not demonstrate the
requested branching concept. This is limited approval of the radial composition's
general arrangement, not certification of every design or statement in that Work.

**Context image:** [Retained screenshot](2026-10-21/05-branching-orientation-context.png).
The image illustrates the radial example; it does not itself depict the rejected
horizontal/vertical Works. Source SHA-256:
`511f6f3b78c7fb050071052dea0795a3c200887fd657a45dac43526d686418fa`.

**Accepted correction:** A vertical namespace tree starts with root namespaces
at the top and reveals descendants downward: `System` → `Text` → `Json`.
Horizontal means a tree whose generations branch left to right, with siblings
above/below each other. Neither request means one ordered row, one column, or
one chain. Use genuine expandable parents and children, useful variable scale,
and content titles such as "C# namespaces" rather than layout labels.

**Rules:** LAYOUT-005 and LAYOUT-006. A true sequence remains a valid subject for
a linear composition; do not generalize this correction into banning sequences.

**Review criterion:** Reveal multiple sibling branches and at least three
generations. Read the subject hierarchy from the spatial composition and verify
that collapse/reopen preserves membership, geometry, and route clearance.

**Live context:** C# project `47f9024f-2a26-4a9a-91b9-83927029fbf3`, current radial
Work `3a62ff31-e876-4a35-af84-2b4175533f30`, whose saved title matches the attached
heading. The new trees are additions; the user did not request replacing this
radial Work.

## EX-001 — Links must not cross node content

**User attachment 1:** [Retained screenshot](2026-10-20/01-link-overlap.png).
The visible heading identifies an Iliad reading overview.

**Observed failure:** A long diagonal reading-order link passes through the
narrative text between rows of portal cards. Rendering the line behind a node or
using a transparent background would not make the route acceptable.

**Accepted correction:** Rearrange nodes first. If the long relationship still
needs an extended route, use saved routing joints or Pivots to wrap around node
edges through clear space. Use a shared Utility Joint only where a real shared
connection is intended.

**Rules:** ROUTE-001 and ROUTE-002.

**Review criterion:** Trace every route segment and label at useful reading
scale. No route may cross an unrelated node's visible body, text, image, caption,
or controls. Necessary endpoint attachments and ancestor/tray crossings follow
the distinctions in ROUTE-001.

## EX-002 — Embedded authored material must belong to the Work's style

**User attachment 2:**
[Retained screenshot](2026-10-20/02-style-inconsistency.png).

**Observed failure:** A dark navy "Four reading paths" diagram sits inside a
light parchment composition, with a different palette and typographic treatment.
The authored diagram feels detached from the surrounding Work.

**Accepted correction:** Use a coherent palette, font families, typography, and
overall style throughout a Work, including authored embedded diagrams. Variations
should serve a role within the same visual system. Source artworks and facsimiles
retain their own historical appearance.

**Rule:** PRESENTATION-004.

**Review criterion:** Compare the overview and embedded authored elements. A node
or diagram should not feel out of place because it uses an unrelated default
color scheme or font.

## EX-003 — Large collections need useful disclosure

**User attachment 3:**
[Retained screenshot](2026-10-20/03-excessive-disclosure.png).

**Observed failure:** A broad genealogy exposes many generations, dossiers, and
long connecting routes simultaneously. The amount of visible content creates a
large footprint and makes relevant branches difficult to isolate.

**Accepted correction:** Organize substantial collections into meaningful
expandable containers. Hide supporting portions when they are not needed and
allow the viewer to reveal the relevant content while retaining orientation,
branch names, and access to the rest of the collection.

**Rule:** CONTAINER-005. NODE-004 also governs choosing a spreadsheet or document
when a large repeated collection is better presented in that medium.

**Review criterion:** Inspect the initial overview, then expand representative
branches. The Work should be easier to navigate without requiring every detail
to remain visible. Disclosure must preserve the relationships and paths to the
complete material; it does not justify inventing a hierarchy for equal peers.

## EX-004 — A reading frame must suit its content

**User attachment 4:**
[Retained screenshot](2026-10-20/04-content-frame-mismatch.png).

**Observed failure:** A very wide node contains a tiny, narrow strip of text and
a large amount of unused interior space. The frame and reading scale are not
appropriate to the material. The screenshot establishes that visual failure,
not its underlying code or sizing cause.

**Accepted correction:** Present the passage at a useful reading size and width.
Choose a suitable scrolling reading surface or document for large text, or a
Window arrangement where a viewport over child nodes is appropriate. Do not
shrink text simply to fit an oversized object. Window is a Display Pattern over
children, not a guarantee of body-text scrolling.

**Rules:** SCALE-003 and NODE-004.

**Review criterion:** Read a representative passage at its intended scale and
reach the complete material through the chosen surface. The reader should not
have to enlarge a mostly empty frame just to decipher its contents.

## Other accepted directions from the same dictation

The user also established principles that are not tied to one screenshot:

- **LAYOUT-001:** Decide whether space represents time, conceptual distance,
  priority, or another meaningful relationship.
- **LAYOUT-002:** Choose horizontal/vertical progression, a radial composition,
  or another structure for the material and the viewer's intended traversal.
- **LAYOUT-004:** Express the difference between a central Master and a set of
  equally important peers; do not impose the same hierarchy on every Work.
- **NODE-004:** A document or embedded spreadsheet can be the better medium,
  especially for extensive lists of similar objects.
- **SCALE-002:** Size and proportion may express importance or make effective use
  of space around a heavily populated node. Size does not have one universal
  meaning independent of the composition.

Future dictated rules should be organized into the saved rulebook with stable
IDs. Explicit directions are Accepted within their scope; additional suggestions
from Codex remain Proposed. Retain concrete evidence without treating an example
as approval of every property in the complete Work.

## Source attachment register

Original files came from `C:/Users/ihigg/AppData/Local/Temp/` in the user's message.
The durable copies above are the references future sessions should use.

| Example | Original attachment filename | Retained copy |
| --- | --- | --- |
| EX-001 | `codex-clipboard-2fc8de15-850e-4f78-880d-33ff592977a5.png` | `2026-10-20/01-link-overlap.png` |
| EX-002 | `codex-clipboard-b5322f61-43cb-4ce6-b5a6-d8bf564bc67c.png` | `2026-10-20/02-style-inconsistency.png` |
| EX-003 | `codex-clipboard-4c083a69-578d-40cf-8787-7398e34e6160.png` | `2026-10-20/03-excessive-disclosure.png` |
| EX-004 | `codex-clipboard-a507510f-1e02-4c97-bd83-3fd685d9b0bd.png` | `2026-10-20/04-content-frame-mismatch.png` |
| EX-006 | `codex-clipboard-a96fa569-c474-4d8f-9ac4-75bb04b98d16.png` | `2026-10-22/06-variable-scale-space.png` |
| EX-007 | `codex-clipboard-2851eaea-8ef0-4435-8993-d10ec95d9410.png` | `2026-10-22/07-unobstructed-branch-expansion.png` |
| EX-008 | `codex-clipboard-b010a646-8377-4a18-b744-52357bd4b63f.png` | `2026-10-22/08-graph-not-document.png` |
| EX-009 | `codex-clipboard-08635201-a6ff-4cfe-896d-62ed1c75baad.png` | `2026-10-22/09-content-not-meta-labels.png` |
| EX-010 | `codex-clipboard-52f88f53-3cad-4ae2-99bd-db8d0c72e89d.png` | `2026-10-22/10-inconsistent-branch-direction.png` |
| EX-011 | `codex-clipboard-10c45975-e623-4c24-80f0-3aefd341c53b.png` | `2026-10-22/11-excessive-scale-variance.png` |
| EX-012 | `codex-clipboard-fe5e952f-2670-4272-b12b-1656129588e3.png` | `2026-10-24/12-one-sided-smart-link-chain.png` |
