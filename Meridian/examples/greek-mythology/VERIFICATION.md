# Collection verification — 4 October 2026

Final project: `3c8176d0-884a-4ac9-9a99-a0d5548de629`.
Installed revision: **2**, with a curated baseline checkpoint and a corrected media-guide checkpoint.

**48 canvases · 802 nodes · 132 connections · 60 references · 200 canvas launchers · 29 containers · 16 embedded files · 109 source records.**

## Confirmed

| Check | Result |
|---|---|
| Document contracts | Every node, connection, hierarchy, asset and source identifier validates |
| Epic coverage | Both epics contain all 24 books in order, without duplicates |
| Boolean logic | All 26 gate truth-table cases pass within the authored collection; undefined propagation and switch reopening pass |
| References | Same-canvas changes propagate live; cross-canvas references retain saved values until save; initial saved computational snapshots agree with evaluation |
| Rules and cycles | Conflicting inherited rules and reparenting are rejected; computational feedback is rejected; visual cycles remain valid |
| Collapse | Logic continues with a collapsed parent and its sibling output visible; all 21 research groups were collapse/reopen tested before the final navigation-only additions |
| Editable package | Real-host ZIP export/import deep-equals the authored project, including every identity, relationship, snapshot and asset byte |
| Publication import | New project/node/canvas/link identities are created and internal targets remapped; authored attachment bytes remain intact |
| Media integrity | All 16 assets match source bytes; original six Met image hashes also match provenance records |
| Desktop layout | All 48 final canvases checked at 1500×950: no unintended card overlaps, clipped text, broken images, missing references or runtime errors; both family maps inspected at 100% |
| Offline viewer | Chromium and Firefox navigate, Run and Stop all 48 canvases; controls, live/pinned references, switch reopening, collapse and authored-copy reset pass; zero HTTP requests and runtime errors |
| Image viewing | All nine raster assets decode, across 18 placements per browser |
| Attachment viewing/download | TXT preview and complete download pass; PDF, WAV, SVG and JSON downloads match embedded bytes exactly |
| Embedding | Both browsers pass the supplied iframe at 1500px and 390px widths |
| Local editor | 48 canvas buttons, representative navigation, outline selection, PNG/JPEG previews, full TXT opening and corrected guide checked; revision 2 remains unchanged |
| Existing work | Original demonstration stays at revision 2 with identical content hash; no editor recovery draft or unintended writes |
| Companion PDF | Five pages rendered and visually inspected; the correction changed only page 4; the installed PDF matches the final file byte for byte |

The collection's narrative received an independent source/consistency review. One succession-sequence error was corrected, source references on two variant notes were strengthened, and the Roman Mars/Silvia account was made explicit about coercion. This is a selected educational collection, not an exhaustive scholarly edition or a universal mythology canon.

## Limits and useful next product tests

- **Long-text reading:** the editor opens the complete TXT. Canvas thumbnails and the published viewer's TXT modal stop at 3,000 characters; complete downloads work. A full reader and an explicit preview-length notice would improve the viewer.
- **Large maps:** Fit is an overview. Reading requires zoom/pan, especially on narrow screens. Search, a reading mode, better camera presets and direct passage navigation would help.
- **PDF:** the browser preview container and original-file fallback were verified. Native PDF page rendering was not established in headless Chromium/Firefox. The actual five-page PDF was independently rendered with Poppler and inspected.
- **Rich media:** SVG, WAV and JSON are deliberate preserved-download cases, not claims of native playback or rendering. Markdown has a viewer text preview and editor download fallback. The WAV is original synthetic test audio, not a reconstruction of ancient music.
- **Scope:** whole-publication import works. Viewer snippet import, computational feedback, and additional rich-media tools remain later product work.

## Artifact fingerprints

| File | SHA-256 |
|---|---|
| `greek-mythology.meridian` | `08fd3fa5db7f778b242504368a8546c8a023cb03c6cd9ee041c1e2ba636742fd` |
| `greek-mythology.html` | `e55cc6673f2422fea3d00b8c8d39bc6710c65045091514d8c753124cea8148fe` |
| `mythology-companion.pdf` | `86bef6122ae9f045cb252f3551aebd18c355b69ff6f194129da4334c309bac86` |

Machine-readable evidence is in `output/mythology-qa`: `collection-verification.json`, `viewer-verification.json`, `final-layout-audit.json`, `editor-smoke.json` and `editor-guide-recheck.json`. The reports distinguish the final bundle checks from earlier collapse coverage and do not claim native headless PDF page rendering.

No Hypershell backlog item was incorporated; its tracker was empty. No Hypershell source or existing demonstration content was changed.
