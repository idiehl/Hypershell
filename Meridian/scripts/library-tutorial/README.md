# Library reorganization review helpers

These helpers compose ordinary Meridian container nodes named **Coding**,
**Mythology**, and **Tutorial**. They introduce no new collection data structure.
They never install a project or save the live Library.

Use Node.js 24 from the Meridian project directory:

```powershell
node --experimental-strip-types scripts/library-tutorial/reorganize.mjs help
node --experimental-strip-types scripts/library-tutorial/reorganize.mjs capture
node --experimental-strip-types scripts/library-tutorial/reorganize.mjs compose --tutorial-project output/library-tutorial-20261021/tutorial/draft.json
node --experimental-strip-types scripts/library-tutorial/reorganize.mjs verify
node --experimental-strip-types --test scripts/library-tutorial/reorganize.test.mjs
node --experimental-strip-types scripts/library-tutorial/reorganize.mjs check-live
node --experimental-strip-types scripts/library-tutorial/review-library.mjs
```

All commands accept `--directory DIR`. The network review commands also accept
`--host http://127.0.0.1:5240`; the editor review accepts `--out DIR`.

`capture` performs GET requests bracketed by identical saved-Library reads. It
writes a local baseline, catalog, recovery record and capture receipt without
overwriting an earlier checkpoint. A changed baseline needs a new output folder
and a deliberate rebase.

`compose` uses the typed declarative authoring API. The previous candidate and
aliases keep newly created identities stable across local refinements. It
preserves original nodes, destination IDs, relationship endpoints, historical
snapshots, attachments, dependencies and the second local Work. Original
discovery keys remain in order; deleted First connections launchers remain
tombstoned. It reuses the existing Mythology container ID, retains the original
decorative header, and adds the new tutorial's start Work plus eleven hidden
part launchers. The supplied tutorial project must keep its final project/Work
GUIDs; the candidate deliberately rejects an unrelated project identity.

Outputs include `library-candidate.json` (the version-two Library envelope),
`library-project.json` (its native project), recipe, aliases, inspection,
inventory, strict preservation report, composition receipt, and a standalone
publication. The standalone publication verifies frame layout; external Work
buttons need an editor with their saved projects available.

`reorganize.test.mjs` is a contextual regression suite against the captured
baseline and current candidate. It exercises repeated composition and rejects
destination changes, tombstone loss, snapshot/asset rewrites, hidden hierarchy
changes, user-content rewrites, and changes to the second local Work.

`review-library.mjs` opens the actual editor build in a fresh browser context.
Every API request is inspected. Library data and the staged tutorial are supplied
locally, and all Library saves/recovery/checkpoints exist only in test memory.
Existing source projects can be read through their exact GET endpoints, avoiding
Chromium's DevTools size limit for large embedded media. Their PUT endpoints and
all unrecognized API requests are rejected. The script verifies every portal
destination, clicks all six entry portals, and exercises category
collapse/save/reopen/reexpand/save/reopen. Its receipt binds the candidate and
tutorial hashes and confirms that the live Library remained unchanged.

`check-live` is a read-only eligibility check, **not an atomic save token**. The
Library endpoint checks the expected revision, but it does not provide the
normal-project authoring endpoint's atomic recovery guard. Before a separately
authorized live application, the operator must review current saved/draft state,
create a named checkpoint of the original Library, use its expected revision,
and retain the user's native windows. There is intentionally no automatic
`apply` command in these helpers.

After a separately completed live save, run:

```powershell
node --experimental-strip-types scripts/library-tutorial/verify-live-library.mjs
```

This final check reads `application-receipt.json` and uses the actual saved
Library, catalog and tutorial responses. It refuses every browser mutation,
verifies saved identities/hashes and all 82 destinations, then follows the
Library's Tutorial portal and the first part's portal. Its fit calculation uses
Window frames rather than clipped logical rows. It retains screenshots and the
GET-only request log in `saved-editor-review/` and checks that saved data remains
unchanged after navigation.
