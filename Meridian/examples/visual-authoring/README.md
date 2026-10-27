# Greek Mythology · Expanded reference atlas

This edition develops the original five visual compositions into a connected reading collection. The family overview remains a doorway into attributed primordial, Titan, Olympian and heroic branches. The epics have an account for every book, four-book movement maps, cross-book connections, thematic comparisons and complete source texts. Greek/Roman comparisons distinguish shared identifications from differences in cult, civic role and literary tradition.

Exact generated counts and scope are in `visual-atlas.manifest.json`. Greek mythology has no single closed genealogy: selected accounts, alternatives, named collective members, regional reports and later reception are identified separately. Unnamed collective members are not replaced with invented children. Historical divine-birth claims remain legends, not biological claims.

## Open and explore

- `visual-atlas.html` is the complete standalone viewer. Open it directly from disk; the editor, a server and networking are unnecessary.
- `visual-atlas.meridian` is the native editable package, retaining the local project's identities and attachment bytes.
- `visual-atlas.project.json` and `visual-atlas.aliases.json` are the editable source and stable authoring identities.
- `installation-expanded.json` records the separate installed copy. Earlier live work and recovery drafts are preserved.
- `baseline-v1/` preserves the five-canvas starting point. The original 48-canvas collection in `../greek-mythology/` is also preserved.

Start at **Myths in relation**, then follow a subject portal or enter the complete reference directory. The **How to explore this atlas** canvas explains the reading levels. Use **Explore** to search all canvases, hidden descendants, and complete included TXT/Markdown documents. Search results reveal and frame the relevant node. A selected reference offers **Open original context**; Back and Forward retrace navigation.

Family branches use directed relationships for selected parentage and dashed relationships for alternatives present on the same map. Saved references reuse the same original figure. Attached notes preserve complete biographies, variants, ancient passage locators and source URLs. Large registers are divided into linked maps.

Each epic book includes an original synopsis, event sequence, characters, expandable interpretive lens, comparison links, previous/next navigation, and the complete book in Samuel Butler's translation. The Reading Room retains each whole source file, including credits and license notices. The Odyssey chronology distinguishes events from narration; it does not invent modern coordinates for fabulous locations.

The media laboratory includes JPEG, PNG, WebP, GIF, TXT, Markdown and PDF, plus lossless SVG/audio attachments with explicit download fallbacks. Its switch-controlled Boolean system keeps a sibling output present when the logic branch is collapsed. Press Run to evaluate. Stop affects every canvas in the current viewer. Viewer interactions remain temporary; Save a copy retains authored defaults.

## Sources and attribution

Explanatory prose is original paraphrase with primary-source locators. Complete public-domain editions are:

- [Homer, Iliad — Samuel Butler](https://www.gutenberg.org/ebooks/2199)
- [Homer, Odyssey — Samuel Butler](https://www.gutenberg.org/ebooks/1727)
- [Hesiod, Homeric Hymns and Homerica — H. G. Evelyn-White](https://www.gutenberg.org/ebooks/348)

Six museum images retain Met collection credits, object links, qualified identifications and SHA-256 hashes in `content/greek-mythology/media.json` and the Reading Room provenance attachment. An artwork illustrates a particular representation. Butler's Romanized names and historical editorial theories remain in the source text; explanatory canvases use Greek names and distinguish those theories from narrative facts. The Shield and surviving Hesiodic fragments retain their attribution qualifications.

## Rebuild and verify

Run from the Meridian project root:

```powershell
npm run build
node --experimental-strip-types scripts/mythology/build-expanded-atlas.mjs --publish
node --experimental-strip-types scripts/mythology/verify-expanded-atlas.mjs
node --experimental-strip-types scripts/mythology/verify-expanded-native.mjs
node --experimental-strip-types scripts/author.mjs capture --input examples/visual-authoring/visual-atlas.project.json --out examples/visual-authoring/qa-expanded/capture-final
node scripts/mythology/verify-expanded-viewer.mjs
$env:PLAYWRIGHT_BROWSERS_PATH = 'I:\dev\Meridian\.cache\playwright-diagnostic'
node scripts/mythology/verify-expanded-viewer.mjs --firefox
```

The generator performs no live installation. It retains declared identities, prunes only retired expansion-owned declarations, and verifies the original collection hash. The expansion recipe is identity-bound to this example. The older `visual-atlas.template.recipe.json` remains a portable **five-canvas foundation template**; its generator and historical QA scripts should not be used to rebuild this edition.

`qa-expanded/` contains actual offline viewer screenshots, browser reports and an embed example. Document previews intentionally scroll. Image captions, ordinary prose, titles, relationship routes and expanded child geometry require visual review in addition to schema checks.

Place the publication beside a webpage and embed the same file:

```html
<iframe src="visual-atlas.html" title="Greek mythology reference atlas"
  style="width:100%;height:85vh;min-height:520px;border:0" loading="lazy">
</iframe>
```

## Product status

Meridian remains an early working release. This collection uses the real editor, renderer, publication and Boolean engine. See `docs/FEATURE-COVERAGE.md` for the N01–N64 audit of the original handoff. Reusable modules, contextual dependency packaging, snippet export, advanced signal/audio systems, the spatial/public repository and collaboration remain future milestones.

