# Meridian authoring connector contract

This resource describes the implemented private authoring service. It does not
claim public distribution or substitute for the maintained Work rulebook.
The authoring worker requires Node.js 24 or later. It uses the installed trusted
viewer assets under `output/app/wwwroot` by default, without rebuilding or
replacing the running application.

## Read before composition

Call `meridian_authoring_rules` for accepted and inherited principles and their
current SHA-256 version. Call `meridian_authoring_schema` for recipe fields,
supported style keys, passive-data examples, and compatibility names. Recipes
retain `canvases` and `canvasId`; the product term is Work.

The returned schema is a concise field reference, not a replacement JSON Schema.
The existing Meridian authoring core performs the authoritative atomic typed
validation. Recipes are data. No author-provided JavaScript, HTML, CSS, shell
commands, source paths, or executable SVG assets are evaluated or accepted as
connector operations. Source-code examples can remain ordinary text content.

## Workflow

1. Inspect the explicitly shared project and target Work with bounded pages.
   Asset inventories omit bytes. Preserve existing IDs and original content.
2. Create a draft using the shared `projectId` and an existing `workId`. To add a
   Work, omit `workId` and provide its concise title. This adds a Work inside the
   shared project, without silently sharing another project.
3. Apply a version-one recipe with the exact returned draft version. Use
   `existingId`, or saved bindings, for updates. Omitted fields remain unchanged.
   Explicit `unset` resets supported fields; `remove.nodes` removes a branch.
4. Validate the resulting draft. Diagnostics identify nodes and links, including
   unrelated expanded-surface overlaps and link crossings. Intentional enclosing
   ancestors are exempt. Unchanged, pre-existing composition issues remain
   advisory; changed/new geometry errors block the save. Warnings about labels,
   content, and text measurements require judgment and visual review.
5. Preview using Meridian's installed shared viewer. Review both the overall
   composition and readable detail; use an exact node ID to focus a branch.
   PNG and self-contained interactive HTML are private artifacts addressed by
   opaque IDs. External network requests are blocked during screenshot capture.
6. Save with the exact draft version, successful `validationId`, matching
   `previewId`, and `visualReviewConfirmed: true` after actual image inspection.
   The host atomically checks saved revision and recovery state and creates a
   checkpoint. A conflict leaves the user's saved/recovery material intact.

## Scope and persistence

Drafts persist privately under the configured `stateRoot/authoring`. A process
restart does not discard draft versions, aliases, validation results, or artifact
handles. It does not authorize a stale save: the rulebook digest and current
project baseline are checked again. Applying another recipe invalidates earlier
validation and preview handles. A saved draft is closed; further edits begin
from the newly saved project revision.

List private drafts to resume their exact IDs and versions. Discard completed or
abandoned drafts to free their private copies and preview artifacts. Discarding a
draft never removes or changes its saved Work, library entry, or checkpoints.
Before posting a save, the service persists an exact candidate hash, expected
saved revision, and unique baseline checkpoint label. If the HTTP result is
uncertain, it will not repeat the POST or let the candidate change. A retry can
recover the receipt only when the current saved project matches exactly at the
expected revision and the uniquely named checkpoint contains the exact original
baseline. Missing or mismatched proof leaves the save pending for inspection.
An acknowledged saved receipt is idempotently returned on repeated save calls.

A Work draft cannot change unrelated Works, project identity/title/entry Work,
existing asset bytes or metadata, or unrelated saved snapshots. Add a fresh
asset when a replacement is needed. A new cross-Work Reference may add its
required source snapshot if absent; existing source snapshots and source Work
objects remain untouched. Shared project allowlists remain mandatory.
Pending unsaved recovery drafts must be resolved in Meridian before starting or
saving an authoring draft. This initial implementation deliberately does not
merge or discard recovery drafts automatically.

## Limits and interpretation

Tool pages contain at most 50 entries. Recipes are limited to 2 MiB per batch;
larger edits can use multiple versioned batches. Worker operations use a fixed
local Node entry point without a shell, and have a time/output limit. Private
draft/artifact limits protect local storage and responses. They do not establish
that every theoretical 50,000-node composition has a useful readable overview.
Large inspection fields are explicitly marked as truncated previews. Never
replace original long content with a partial inspection value; omitted recipe
fields preserve the original. Returned alias maps are bounded; the service
retains the complete bindings, so later recipes can continue using earlier keys
without sending the whole manifest through each conversation turn.

Row, column, grid, and radial are placement primitives. They do not automatically
measure and route entire expanded subtrees. Authors must reserve branch space,
use saved joints where routes bend, keep direction consistent, and use Minimum
Effective Variance. Geometry diagnostics cover authored expanded effective
surfaces; mutually exclusive Gallery pages, curved self-links, browser clipping,
and aesthetic composition still require the actual viewer review.

The first authoring acceptance test should use a disposable Work/project fixture:
create → inspect → edit → validate → real preview → guarded save → reopen. It
must also show that unrelated Works remain identical and stale saves fail.
