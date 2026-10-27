# Meridian authoring and development

Meridian Works are visual information systems. Do not treat them as documents
split into uniformly sized cards. Choose a composition for the relationship being
explained: a genealogy, sequence, comparison, constellation, or annotated image.

The product term is **Work**, plural **Works**. Preserve version-one storage and
API compatibility names (`canvases`, `canvasId`, `canvas-link`) and user-authored
content. For Meridian composition or review, use the `meridian-authoring` skill
and read `docs/MERIDIAN-RULES.txt` plus `docs/WORK-AUTHORING-STANDARDS.md`.
Read the current saved rulebook directly; preserve the user's wording unless
they request editing. Its accepted and inherited entries, and rules the user
adds, guide authoring within their scope. Entries marked PROPOSED, review notes,
empty templates and Ideas and Questions do not establish accepted axioms.
Apply accepted principles and the
inherited project rules; do not promote draft suggestions or an unreviewed Work
into a universal design axiom. Record proposed principles separately until the
user approves them.

When the user dictates authoring principles, translate and organize them in the
saved rulebook, preserving intent and stable IDs. Explicit user directions are
accepted within their stated scope; retain concrete example evidence and keep
additional agent suggestions proposed. Documenting a review does not itself
request changes to the illustrated Works.

## Authoring workflow

AI authoring is a current product priority. Its delivery target is ordinary
ChatGPT desktop, without requiring Codex. The local authoring skill and API are
the foundation; do not describe them as an installed or publicly distributed
ChatGPT integration. Read `docs/CHATGPT-AUTHORING-OPTIONS.md` for the maintained
implementation status and proposed plugin/tool contract before integration work.

For the Library Work, read `docs/LIBRARY-ORGANIZATION.md`. Use ordinary subject
Containers (initially Coding, Mythology, Tutorial), retain existing portal
destinations and intentional deletions, and verify actual Library visibility.
Maintain native tutorial content according to `docs/MERIDIAN-TUTORIAL.md` when
new installed tools or object behavior change its lessons.

- Read `docs/AUTHORING.md` and use `src/core/authoring.ts` or
  `node --experimental-strip-types scripts/author.mjs help` for batch authoring.
- Inspect an existing project before changing it. Preserve IDs and user drafts.
  Prefer a separate project for experiments; CLI `install` always creates a copy.
- Use actual labeled Meridian relationships for meaningful connections. A line
  must communicate something; do not add decorative edges to meet a count.
- Use scale, whitespace, typography, color, and media to communicate hierarchy.
  Keep complementary material about the same subject in its Node Hub when it
  need not occupy the graph. Use document nodes or supporting Works when their
  independent identity or spatial role helps. Read `docs/NODE-HUB.md` and the
  Node Hub recipe section in `docs/AUTHORING.md`. Keep the
  primary reading path legible at a useful zoom.
- Choose images for their explanatory value and retain captions, uncertainty,
  attribution, and source links. Images are interpretations, not evidence of every
  detail of a myth. Distinguish alternative accounts.
- Use recipes with readable local keys instead of inventing or hand-maintaining
  GUIDs. Keep the recipe and alias map with the output so it can be revised.
- Run `inspect`, then `capture` against the actual standalone viewer. Open the
  resulting screenshots. Review overall hierarchy AND readable detail; inspect
  scrolling, clipped titles, missing images, crossing links, and contrast.
- Revise the composition after visual review. Passing schema checks is not proof
  of good visual design. Automated overflow estimates are only hints.
- Verify save/reopen and publication preserve the authored appearance and graph.

## Development

Keep editor and viewer on the same scene renderer. New document fields must be
validated in TypeScript and the host, and explicitly allowed by publication.
Recipes must remain declarative data with atomic validation; never evaluate
author-supplied JavaScript, HTML, or CSS. Respect revision conflicts and recovery.
Use isolated test storage and package outputs; never run tests against live
projects. Build before browser tests. Inspect the active host before deployment.
