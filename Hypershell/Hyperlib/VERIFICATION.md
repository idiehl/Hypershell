# HyperLib implementation verification

The application is installed at `I:\dev\Hypershell\Hyperlib`.

## Base Template Library update — 2026-09-27

The reusable catalog contains **17 starters**: Woodland Editorial and Wave Laboratory preserve the two original designs, with 15 additional designs and one demo Work/Page per new design. Installation preserves the original pages and citations and adds the demo collection as one undoable content batch.

Verified against isolated copies, without changing the running library during testing:

- Core: 87 assertions; catalog: 250 assertions; library installation/scaffolding: 134 assertions; renderer contracts: 191 assertions; editor JavaScript: 7 tests.
- All 17 Work projects actually compiled and rendered their complete block collections over HTTP. Content edits reused the same compiled generation; all verification-owned hosts and renderers exited cleanly.
- Runtime: 10 focused checks for failed compilation, last-working rendering, restart recovery, latest-save activation, appearance compatibility, rich-text sanitization, and no build on content edits.
- Installation: original content/custom fields preserved, canonical edited starter defaults copied, binary assets preserved, reusable/Work independence, batch undo/redo, interrupted marker recovery, canceled write recovery, concurrent scaffold identity consistency, stale revision protection, and path boundary/link rejection.
- Native assistant: catalog listing, `starterId` selection, preserved editable-chrome metadata, same-batch creation, and Studio opening checks passed using fixtures. No external model or research request was sent.
- Browser: keyboard template selection, new Work and fully populated page, selectable/editable headers and footers, header deletion without fallback regeneration, undo, footer text/color/alignment, dividers, whole-page background, persistent thin outline, transparent inline typing background, and persisted content reopening in reading mode.
- Actual local timer, compiled C# wave phase event, and contact-form validation/receipt were exercised. Forms are intentionally local previews; external delivery is a Studio integration.
- At a 390-pixel viewport, the editor and page had no horizontal overflow; partial-width content collapsed to one column. Half-width portfolio banners remain readable at desktop editing widths.

Reports, recovery backups and browser screenshots for this update are under `I:\dev\Hypershell\output\base-template-library-20260927`. Source implementation and native changes remain in the isolated `hyperlib-authoring` checkout. No secondary backlog item was included (the inspected lists were empty).

Installed at revision 35 after the user stopped the previous MyNet host. Live verification confirmed 17 reusable templates, 17 Works, 17 Pages, and 15 new demo Works. All six original Woodland blocks, all eight existing Wave blocks, and both pages' source records are byte-equivalent as serialized before/after migration. The previous host, library sources, journal and content are recoverable under `live-backup-20260927-170903`. The stable launcher targets the updated native publish; existing native sessions load its assistant changes on their next restart.

## Original release verification — 2026-09-26

## Results

| Area | Evidence |
|---|---|
| Content contracts and storage | 87 Core assertions passed; atomic journal recovery, revision conflicts, validated batches, undo/redo, hierarchy, source records, media validation, portable import, Work-local projections |
| HTTP gateway | 42 checks passed against the final published host; health identity, contracts, origin/host guards, unsupported writes, missing content, local media |
| Renderer lifecycle | 10 runtime assertions passed; immutable candidates, failed-build preservation, unchanged-content/no build, restart recovery, latest-save selection, appearance compatibility, safe rich-text rendering |
| Studio | VISUAL_PAGE_CORE_OK and HYPERPAGE_STUDIO_OK; native root and both Work projects opened in their own contexts; authoring contracts validated |
| Native MyNet | HYPERLIB_NATIVE_OK and ATLAS_MYNET_OK; launch ownership, bridge origin restrictions, cancellation, process contracts and port selection |
| AI providers | WORKSPACE_AI_PROVIDER_OK; provider/search/citation fixtures and restricted content tools |
| Browser authoring | Direct paragraph edit, autosave, reload, undo, page creation from template, image insertion/import, appearance changes and schema-bound properties |
| Browser interactions | JavaScript counter changed 0 to 1; the compiled C# wave control changed 0 to 1 phase shifts through its event handler |
| Responsive and keyboard | Reading mode after reload; keyboard navigation/Edit/Undo/Create; navigation collapse and narrow drawer; save status visible; 390-pixel layout has no wave-widget horizontal overflow |
| Final lifecycle | Three active renderer processes stopped with the host; restart reused the unchanged Wave Lab generation; final host and renderer shutdown confirmed |

The original release contained exactly two active content pages: **A Woodland Habitat** in Natural World / Animal Atlas and **Exploring a Wave** in Science Lab / Foundations / Wave Lab / Fundamentals / Waves / Sound. Browser-test content changes were undone through the content API. The journal retains these reversible verification operations; imported fixture media remains available for recovery.

The structural batch verifier creates three Works, two Parts per Work and ten Chapters per Work (five per Part), checks persisted counts, and undoes the batch as one operation. Source metadata/citation associations are verified through save/reload and provider-fixture changes.

## Live-provider boundary

No real model-provider request or live research query was sent. The companion uses a separate native profile without copied credentials. Configure **AI Access** and the **AI Context** there before using its assistant. Provider and citation handling have fixture coverage; an authenticated end-to-end research conversation remains to be exercised with that configuration.

Ordinary browsers support local reading and editing. MyNet must launch the host to grant the native assistant and Studio bridge. The standalone verification host was stopped after testing. MyNet selects a port from 5267–5276; the standalone script defaults to 54180.

## Repeat checks

Run the app suites from this folder. The runtime verifier uses a temporary fixture and does not edit this library:

```powershell
dotnet run --project tests/HyperLib.Core.Tests -c Release
dotnet run --project tests/HyperLib.Runtime.Verifier -- .
dotnet run --project tests/HyperLib.Http.Tests -- http://127.0.0.1:54180
```

Native source and inherited Studio changes remain isolated in `C:\Users\ihigg\.codex\worktrees\hyperlib-authoring\Hypershell`. From that checkout:

```powershell
dotnet build tools/Hypershell.CSharpVerifier/Hypershell.CSharpVerifier.csproj --artifacts-path artifacts/hyperlib-native
& './artifacts/hyperlib-native/bin/Hypershell.CSharpVerifier/debug/Hypershell.CSharpVerifier.exe' --hyperlib-native-only
& './artifacts/hyperlib-native/bin/Hypershell.CSharpVerifier/debug/Hypershell.CSharpVerifier.exe' --atlas-mynet-only
& './artifacts/hyperlib-native/bin/Hypershell.CSharpVerifier/debug/Hypershell.CSharpVerifier.exe' --workspace-ai-provider-only
& './artifacts/hyperlib-native/bin/Hypershell.CSharpVerifier/debug/Hypershell.CSharpVerifier.exe' --visual-page-core-only
& './artifacts/hyperlib-native/bin/Hypershell.CSharpVerifier/debug/Hypershell.CSharpVerifier.exe' --hyperpage-studio-only
```

Reports and screenshots are in `I:\dev\.codex-build\hyperlib-authoring`. The original running Hypershell installation and the Studio source checkout were not replaced. The parked PowerShell prototype is preserved at `I:\dev\Hypershell\Infrastructure\ArchivedHyperlibModule`. No secondary backlog item was included.
