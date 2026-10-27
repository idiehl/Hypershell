# Expanded genealogy: source and editorial notes

The declarative module `genealogy-expanded.mjs` contains original paraphrases and attributed parent sets. It performs no host or filesystem I/O. The separately authored `roman-hesiod-expanded.mjs` supplies Roman comparisons, Hesiod reading paths and its additional source metadata.

## Source policy

- `parents` is a selected source path, not a universal canon or a historical claim. `variants` retains competing parent sets, with the exact witness and locator. `parentageEdges` carries the same attribution.
- `unions` groups parent sets for navigation. It does not establish marriage, consent, or a single narrative shared by all children. Stories of deception, rape, abduction and enslavement retain those distinctions.
- Empty ancestry means no genealogy selected or no parent named in the cited notice. An unnamed mother remains explicitly unnamed. Supporting parents are not padded with invented ancestral links.
- Direct ancient texts are preferred. Theoi's family indexes provide a finding aid for scattered ancient testimonia; their modern editorial summaries are not treated as a canon. Source references include readable URLs and ancient-author locators, with direct-text overrides where available.
- No contemporary translations, modern site commentary or modern scholarly prose are reproduced. The English descriptions are original. Sources identify public-domain editions when used; modern translations on finding-aid pages are used only to locate the ancient testimony.
- Late Orphic poetry, Nonnus, late-antique local history and the tenth-century Suda remain marked as such. Sparse late testimony is not retrospectively described as archaic consensus.

## Explicit boundaries

This is a broad catalog, not an exhaustive account of every Greek cult, inscription, fragment, local genealogy or later reinterpretation. It covers all twelve Titans named in Hesiod's opening family, named Muses, Graces, Horae, Fates and Pleiades, the transmitted Nereid names, many Olympian children, regional Zeus descendants, supporting mothers and fathers, and selected later heroic generations. Figures with unrecorded personal stories receive brief honest entries.

The Zeus total counts catalog records, including several clearly labeled collective records and alternative-only parentages. It is not a number of universally agreed biological children. Regional and late figures belong beside, but are not evidentially identical with, Homeric and Hesiodic figures. Alexander's historical parents remain the selected path; divine descent is a separately labeled legend.

Virgin goddesses Athena, Artemis and Hestia have no biological children in the principal traditions selected. Athena's fostering of Erichthonius is separate from the attempted assault and earth-generated birth narrative. Cerberus serves Hades but is a child of Typhon and Echidna. Macaria's Hades pedigree comes from the Suda and supplies neither a mother nor a developed biography.

## Corrections made during primary-source cross-checking

- The linked Evelyn-White Theogony names **51 distinct Nereids** while the poem calls the group fifty. The transmitted list is preserved; a name is not deleted to hide the inconsistency.
- Hesiod names the Graeae **Pemphredo and Enyo**, not the three-name list found in the Library. Enyo here is distinguished from the war goddess.
- The Theogony first identifies Nereus's father as Pontus without explicitly naming a mother in that clause. Earth/Pontus parentage is preserved as the Library's separate version.
- Pausanias 10.12.1 distinguishes an **unnamed earlier Libyan Sibyl**, daughter of Zeus and Lamia, from **Herophile** discussed next. The displayed record corrects the conflation even though its internal stable key is `herophile`.
- Diodorus 5.49.2 makes **Corybas the child of Iasion and Cybele**, not Demeter. No Demeter–Corybas edge was inferred from the modern family-index summary.
- Pandora daughter of Deucalion, mother of Graecus in the fragment, is distinct from Pandora the first woman. Thyia is the mother of Magnes and Macedon; those pedigrees are not merged.
- The Hephaestus–Aetna parentage for the **Palici** cites Servius on Aeneid 9.584. Aeschylus's lost Aetnaeae concerns a Zeus–Thaleia account and is not used to support the competing pair.
- The Graces' Zeus/Eunomia mother in Orphic Hymn 60 is an attributed alternative to Hesiod's Zeus/Eurynome account, not a silent correction of Hesiod.
- Proclus's four daughters of Hephaestus and Aglaia are cited at **On Timaeus 29a, Taylor volume 1 p. 280**, corresponding to Orphic fragment 182 Kern / 272 II Bernabé. The legacy Theoi index did not give that precise locator.
- The ancient argument concerning the Shield opening is **fragment 78** in Evelyn-White's Catalogue collection, not fragment 16. The attribution discussion is not misrepresented as a simple ancient rejection of Hesiodic authorship.
- The Litai appear in **Phoenix's speech to Achilles**, not a story narrated by Achilles.

## Integrity checks

The module checks duplicate person/source keys, missing source identifiers, absent locators, missing summaries, missing parent keys, and unknown branch assignments on import. A separate read-only check verified the selected-parent graph is acyclic, the Titan branch has twelve members, every figure has a navigable branch, every parent-edge key is unique, and every person/variant source reference has a readable URL. Combining all alternative edges into one chronology is deliberately unsupported.

The consuming generator and viewer checks must still verify actual visual readability, reference routing, media, pagination and publication. Data integrity is not a substitute for rendered review.
