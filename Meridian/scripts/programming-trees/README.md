# Programming reference trees

These additions belong to the existing C# and C++ collections in Meridian's
Coding Library container. They append content Works; they preserve the existing
radial examples and every original Work, node, link, attachment, and snapshot.

- **C# namespaces:** 701 API namespaces plus 35 marked connecting prefixes, taken
  from the selected .NET 10.0.12 Core, ASP.NET Core, WindowsDesktop, and .NET
  Framework 4.8.1 reference assemblies. Dotted names form the actual hierarchy.
- **C++ classes and types:** 965 named C++23 standard-library types, classified
  into 13 practical categories and 62 subgroups. These are task categories,
  not inheritance assertions. The catalogue retains primary headers, purpose,
  deprecated/conditional availability, and exact N4950 source links.

The extraction scripts and retained manifests explain their finite scope. A
programming language permits user and third-party names; these documents do not
claim to enumerate arbitrary application or third-party libraries.

## Composition

Run from `I:/dev/Meridian` with Node.js 24:

```powershell
node scripts/programming-trees/capture.mjs
node --experimental-strip-types scripts/programming-trees/compose.mjs
node --experimental-strip-types --test scripts/programming-trees/tree-layout.test.mjs
node --experimental-strip-types scripts/programming-trees/review-csharp.mjs --out output/programming-trees-20261021/csharp/browser-review-final-3
node --experimental-strip-types scripts/programming-trees/review-cpp.mjs --out output/programming-trees-20261021/cpp/browser-review-final-3
node --experimental-strip-types scripts/programming-trees/verify.mjs
```

`capture` is GET-only and refuses to overwrite a different baseline. `compose`
is local-only. It reuses aliases on refinement and asserts original entity and
snapshot preservation. Output in `output/programming-trees-20261021/{csharp,cpp}`
includes the baseline, recipe, draft, aliases, source/geometry manifest, original
downloadable guide, publication, and review evidence. Do not confuse these local
candidates with saved application readbacks.

The nodes use ordinary unbounded Media parents, Freeform manual child positions,
and real shared Utility Joints. Vertical means generations advance downward;
horizontal means generations advance rightward. These are not the bounded
Horizontal/Vertical Display Patterns. Sibling subtree bays remain disjoint.
Each family's gap depends on its own frames, so a large distant branch does not
stretch a small namespace family. Variable frame size represents branch density
and readable depth, not the importance of an API.

The world-unit multiplier keeps deep frames large enough for the installed
renderer's stroke range. Fitted text remains vector text, and generation-dependent
line widths keep buses visible without heavy leaf connections. Every semantic
entry retains its full searchable name in its Tag. Compact titles show namespace
segments; namespace bodies identify the complete dotted name and availability.

The C# Work also has a bounded scrolling reference directory with native official
links for all 701 API namespaces. Leaf cards have active source hyperlinks. Full
type records and source URLs remain in the original CSV files. The long complete
guide is a download attachment rather than a shrunk inline wall of text.

## Review and application

Open actual overview and reading captures. Exercise several generations and
independent sibling branches, collapse/reopen, search/Target, original downloads,
and native source links. Check real link contrast and family reading scale as
well as geometry. The offline browser helpers block all HTTP(S) requests and
change only private viewer exploration state.

Before applying a reviewed candidate, compare the live saved project and recovery
token with its exact baseline. The authoring endpoint checkpoints the original
under its writer lock and checks both revision and recovery before saving. A
conflict requires a deliberate rebase that preserves new user changes. Apply the
two Library launchers only after both destination Works are saved; preserve the
Library revision, recovery, checkpoint, subject containers and deletion records.
No backend or native window restart is required for these supported documents.

The October 21 application is recorded in
`output/programming-trees-20261021/application-receipt.json`: C# revision 10,
C++ revision 2, Library revision 479. The saved project and Library readbacks
match the reviewed candidates, and each original has a verified named checkpoint.
The retained application script refuses to run twice; do not treat it as a
general-purpose update command. To check the actual saved Library navigation
without editing any document, run `node scripts/programming-trees/review-live.mjs`.
That review blocks mutations and external requests and checks the saved hashes
before and after browsing.

The small authoring API fix in this cycle replaces a stack-exhausting base64
regex with linear validation, retaining strict canonical checks. Its regression
imports a 3.75 MB decoded attachment, larger than the complete C# CSV. It changes
no attachment-size allowance or installed application behavior.

Actual saved-editor review also exposed a link-color mismatch: the editor's
global anchor accent overrode an explicitly authored node text color. The shared
scene rule now honors that authored color on native content hyperlinks. A narrow
CSS-only runtime update is recorded in `link-color-fix/application.json`; it
appends only the verified rule to the exact installed `app.css` and `scene.css`,
retains their originals, and verifies served hashes. Saved content, JavaScript,
host binaries and running native sessions are preserved. The focused render
proof in `link-color-fix/verification.json` covers editor/viewer color, contrast,
underline, focus and hyperlink navigation. It is separate from the immutable
pre-application composition proofs.
