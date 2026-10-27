# Programming atlas authoring and review

The C++ and C# collections are new, independent projects. Their recipes, original
drafts, aliases, source notes, publication output, and review evidence belong in
`output/programming-layouts-20261020/{cpp,csharp}/`. The composition script is the
entry point for content; the verifier only reads it.

## Clearance review

Run from the Meridian project root with Node.js 24:

```powershell
node --experimental-strip-types scripts/programming-atlas/verify.mjs --input output/programming-layouts-20261020/cpp/draft.json --aliases output/programming-layouts-20261020/cpp/aliases.json --out output/programming-layouts-20261020/cpp/geometry-review.json
node --experimental-strip-types scripts/programming-atlas/verify.mjs --input output/programming-layouts-20261020/csharp/draft.json --aliases output/programming-layouts-20261020/csharp/aliases.json --out output/programming-layouts-20261020/csharp/geometry-review.json
node --experimental-strip-types --test scripts/programming-atlas/verify.test.mjs
```

The report checks aliases, project validity, effective silhouette clearance,
initial visibility, individually disclosed branches, and all groups expanded in
each Work with the editor's actual disclosure engine. Tray frames and their
rendered connector routes are included. Window children are clipped to their visible viewport. It
never moves or saves the source project. A clean report does not establish
typography, source accuracy, complete scrolling/paging behavior, route-label
clearance, or visual quality; those require actual viewer inspection.

## Offline interaction review

```powershell
node --experimental-strip-types scripts/programming-atlas/review-interactions.mjs --directory output/programming-layouts-20261020/cpp
node --experimental-strip-types scripts/programming-atlas/review-interactions.mjs --directory output/programming-layouts-20261020/csharp
```

This drives a frozen local copy of the actual publication in Chromium and blocks
HTTP(S) requests. It checks portal navigation/history, pointer and keyboard
disclosure, hover collapse, remembered frames, original child identities, simultaneous
disclosure of all groups, Window
wheel/keyboard browsing, every CSV attachment download, the complete Markdown
packet, source hyperlinks, and portable HTML copy contents. It checks Gallery
geometry and next/previous controls only when a Gallery is actually authored;
the current catalog desks use bounded Windows to keep table previews readable.
Artifacts include a `proof.json`, unchanged source payload, byte-verified download
files, attachment-dialog images, and focused screenshots at a measured 100%.

Focused scale adjustment wheels over the clear viewport corner and pans through
the viewer's normal gesture. A wheel at the center of a Window would scroll the
Window instead of changing Work zoom; that is intended interaction behavior.

## Viewer captures

The existing authoring CLI embeds `dist/viewer.js` and `dist/scene.css` into an
offline publication and drives the actual viewer. A build is unnecessary if those
review assets already correspond to the intended renderer.

```powershell
node --experimental-strip-types scripts/author.mjs capture --input output/programming-layouts-20261020/cpp/draft.json --out output/programming-layouts-20261020/cpp/review
node --experimental-strip-types scripts/author.mjs capture --input output/programming-layouts-20261020/csharp/draft.json --out output/programming-layouts-20261020/csharp/review
```

For readable details, supply an exact Work title or ID with `--work`, then an
exact node GUID or unique Tag with `--node`. Add `--scope branch --scale 1` where
appropriate. This navigation reveals collapsed ancestors and selects Gallery
pages without rewriting the saved source. Open every resulting overview and
the important focused images; revise material problems and capture again.

## Optional isolated editor review

For editor behavior beyond the portable viewer, start a separate host using a
new data directory and an unused loopback port. The repository's normal browser
tests use this same host entry point:

```powershell
dotnet run --project src/host -- --urls http://127.0.0.1:5293 --data-root "I:/dev/Meridian/output/programming-layouts-20261020/isolated-data"
```

The host serves the repository's `dist` assets when its assembly has no packaged
`wwwroot`. A background PowerShell launch must use `Start-Process -WindowStyle
Hidden`, keep the process ID and redirect logs. Never start this review host with
the live user's data root. Stop only the process created for this review.

## New-only installation

After content and rendered review, the existing CLI can install independent
copies on the active local host:

```powershell
node --experimental-strip-types scripts/author.mjs install --input output/programming-layouts-20261020/cpp/draft.json --host http://127.0.0.1:5240 --out output/programming-layouts-20261020/cpp/installed.json
node --experimental-strip-types scripts/author.mjs install --input output/programming-layouts-20261020/csharp/draft.json --host http://127.0.0.1:5240 --out output/programming-layouts-20261020/csharp/installed.json
```

`install` forks all included graph identities and POSTs a new project; it does not
modify an existing Work, existing recovery draft, or library document. Retain the
installed response and map the aliases to its fresh IDs before future edits.
Forking preserves array order, so old/new Work, node, link, and asset arrays can
be zipped and the corresponding IDs remapped. Treat an existing successful
installation receipt as completion, rather than repeating the command and making
another copy.

No backend restart or app replacement is required for supported document types.
Home discovers the new projects and their Work launchers from the catalog during
normal navigation to the library. Its reconciliation preserves authored existing
titles, positions, hierarchy, and intentional launcher deletions; discovery is
saved through the editor's normal draft/recovery workflow. Do not externally
overwrite `library.json` or PUT the library merely to add these collections.
