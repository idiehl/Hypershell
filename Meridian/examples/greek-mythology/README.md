# Greek mythology: gods, epics and Roman worlds

A detailed Meridian testing collection: **48 canvases, 802 nodes, 132 connections, 16 embedded assets and 109 source records**. Its narrative is newly written English paraphrase, with named ancient accounts and alternative genealogies kept distinct.

## Open it

- `greek-mythology.meridian` is the editable project package. Import it from Meridian's project library. If this identity is already installed, use the editor's explicit copy workflow to create an independent experiment.
- `greek-mythology.html` is the standalone publication. Open it directly from disk; it needs no Meridian host or network. It starts stopped. Run enables the labelled logic examples; viewer interactions remain temporary.
- `embed-example.html` embeds the same publication in an iframe. Keep both HTML files together, or upload them to the same web directory.
- `mythology-companion.pdf` is the five-page reading and testing guide.
- `collection-reader.txt` contains the full readable transcript; `source-register.txt` contains the passage and museum provenance register. These files are also embedded in the collection.
- `installation.json` records the local editor link and saved revision after installation.

## Contents

| Area | Coverage |
|---|---|
| Family | 62 figures across Origins/Titans and Olympian maps, with a separate variant-traditions canvas |
| Gods | 21 individual or grouped profiles and 42 stories; all family figures link to a relevant profile or broader tradition |
| Iliad | An overview plus six four-book canvases; all 24 books, themes, people and divine interventions |
| Odyssey | An overview plus six four-book canvases; all 24 books, including the distinction between remembered journeys and narrative order |
| Hesiod | Theogony and Works and Days; 27 sections with contextual notes |
| Rome | 18 comparisons and a separate civic/household canvas, including Janus, Quirinus, Lares and Penates |
| Media | Six public-domain Met images, PNG and WebP diagrams, GIF animation, PDF, TXT, Markdown, SVG, WAV and JSON |
| Lab | Seven Boolean gates, exposed inputs/switch, live and saved references, an unwired output, visual cycles and inherited rules |

The 21 story groups exercise nesting and collapse. Navigation cards target canvas roots; reference appearances retain distinct node identities. Genealogy arrows show the selected descent, not certainty or marriage. Related-story links do not claim that every minor figure has a separate ancient narrative.

## First testing route

1. Open **Begin here**, then the family maps. Zoom from overview to reading scale and follow a related-tradition card.
2. Open a god's dossier. Collapse **Stories and reading questions**; the saved identity reference and image should remain visible.
3. Open one four-book epic canvas and the source register. Compare the summary with its named ancient passage.
4. In the **Lab**, Run the publication. Toggle **Crew prepared**: its live mirror and **Voyage ready** update. Open **Enable voyage**: the output becomes Unknown. Close it: evaluation resumes.
5. In the editor, change **Archive reviewed** on Begin here. The Lab's cross-canvas reference retains the saved value until Save succeeds. Collapse the preparation container: its sibling output remains visible and computes.
6. Stop the viewer, change a control and change canvases. Results remain stopped until Run. Reset restores authored defaults.
7. Save/export, reopen the package, then publish HTML and import that HTML as a new project. Compare identities and attachment hashes using the supplied verification script.

The **Curator archive fixture** is an intentional external-reference snapshot. Publishing without external references produces an explicit missing requirement. It is unrelated to the mythology's source evidence.

## Current product boundaries exposed by the collection

- Fit is an overview; large canvases need zoom and pan for reading. Narrow screens intensify this. A searchable reading/navigation view and remembered authored camera would be useful follow-ups.
- TXT node cards and the published viewer's attachment modal stop at 3,000 characters. The editor's Open attachment dialog shows the full text. Download also preserves the complete transcript or register.
- PNG/JPEG/WebP and imported GIF nodes display as images. PDF preview uses the browser's PDF support and retains open/download fallbacks.
- Markdown opens as text in the published viewer; the editor uses its download fallback. SVG, WAV and JSON remain preserved downloads. The audio fixture is original synthetic sound, not reconstructed ancient music.
- Whole-publication import is supported; selecting a viewer snippet for import is not part of this release.
- The genealogy is a selected, source-labelled tree. It does not claim to cover every local cult, genealogy or later retelling.

## Reproduce the collection

Source material and media records are in `content/greek-mythology`. Generators are in `scripts/mythology`. The content generator creates **new identities** each run; do not regenerate over an edited working project expecting identity continuity.

```powershell
node scripts/mythology/build-collection.mjs
node scripts/mythology/verify-collection.mjs
node scripts/mythology/verify-viewer.mjs
node scripts/mythology/install-collection.mjs
```

The host verifier exports and reimports without changing the live library. The installer creates only a new project and a named baseline checkpoint; rerunning it for an already-installed identity preserves edits. The reader PDF is generated separately by `make-companion.py`; the Met downloads have original-byte hashes in `media.json`. No Hypershell source changes are needed.
