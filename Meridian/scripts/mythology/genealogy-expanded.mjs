// Researched content only. Importing this module performs no host or filesystem I/O.
// Parent sets are attributed selections, never a claim of one universal Greek canon.
import { romanComparisons, hesiodWorks, additionalSources } from './roman-hesiod-expanded.mjs';
export { romanComparisons, hesiodWorks };
const publicDomain = 'Ancient work; original English paraphrases here. The linked early English translation is public domain; no modern translation or site commentary is reproduced.';
const indexRights = 'Finding aid to explicitly identified ancient passages. Genealogical facts are attributed to those passages; modern translations and editorial prose are not reproduced.';
const source = (id, title, author, url, locators, rights = publicDomain) => ({ id: `gx-${id}`, title, author, url, locators, rights });
export const sources = [
  source('theogony', 'Theogony', 'Hesiod; H. G. Evelyn-White translation (1914)', 'https://www.theoi.com/Text/HesiodTheogony.html', ['116–452: primordial and divine families', '453–885: succession and conflict', '886–1022: marriages and descendants']),
  source('works', 'Works and Days', 'Hesiod; H. G. Evelyn-White translation (1914)', 'https://www.theoi.com/Text/HesiodWorksDays.html', ['1–201: justice, Pandora, human races', '202–382: justice and work', '383–828: farming, sailing, conduct and days']),
  source('shield', 'Shield of Heracles', 'Hesiodic corpus; H. G. Evelyn-White translation (1914)', 'https://www.theoi.com/Text/HesiodShield.html', ['1–56: Alcmene and double paternity', '57–138: Heracles and Iolaus', '139–320: shield description', '321–480: Cycnus, Ares and aftermath']),
  source('catalogue', 'Catalogue of Women and Great Ehoiai, fragments', 'Hesiodic corpus; H. G. Evelyn-White translation (1914)', 'https://www.theoi.com/Text/HesiodCatalogues.html', ['Fragment numbers follow Evelyn-White, not modern editions', 'Fragments 2–3, 7–9, 19, 52–53, 66, 85–86, 102', 'Fragment 78: ancient argument concerning the Shield opening']),
  source('apollodorus1', 'Library, book 1', 'Pseudo-Apollodorus; J. G. Frazer translation (1921)', 'https://www.theoi.com/Text/Apollodorus1.html', ['1.1–1.5: divine families', '1.7–1.9: northern and Aetolian heroic families']),
  source('apollodorus2', 'Library, book 2', 'Pseudo-Apollodorus; J. G. Frazer translation (1921)', 'https://www.theoi.com/Text/Apollodorus2.html', ['2.1: Argive and Egyptian genealogies', '2.4: Perseus and Heracles', '2.5–2.7: Heracles and descendants']),
  source('apollodorus3', 'Library, book 3', 'Pseudo-Apollodorus; J. G. Frazer translation (1921)', 'https://www.theoi.com/Text/Apollodorus3.html', ['3.1–3.5: Crete and Thebes', '3.8–3.13: Arcadia, Pleiades, Troy, Aeacids', '3.14–3.16: Athens and Theseus']),
  source('apollodorus-e', 'Library, Epitome', 'Pseudo-Apollodorus; J. G. Frazer translation (1921)', 'https://www.theoi.com/Text/ApollodorusE.html', ['Epitome 1.9: Ariadne and Dionysus', 'Epitome 2: Pelopids', 'Epitome 6–7: returns and alternative parentage']),
  source('hymns', 'Homeric Hymns 5–33', 'Anonymous archaic Greek poets; H. G. Evelyn-White translation (1914)', 'https://www.theoi.com/Text/HomericHymns3.html', ['5.7–32: virgin goddesses', '5.45–291: Aphrodite and Anchises', '19: Pan', '32: Selene and Pandia', '33: Dioscuri']),
  source('orphic', 'Orphic Hymns 41–86', 'Imperial-era Greek hymn collection; Thomas Taylor translation (1792)', 'https://www.theoi.com/Text/OrphicHymns2.html', ['57: Chthonian Hermes and Iacchus', '60: Charites', '71: Melinoe; Zeus in the likeness of Plouton']),
  source('diodorus5', 'Library of History 5.47–67', 'Diodorus Siculus', 'https://www.theoi.com/Text/DiodorusSiculus5B.html', ['5.48: Samothracian variants', '5.55: Rhodes', '5.62: Anius'], indexRights),
  source('pausanias8', 'Description of Greece 8.17–35', 'Pausanias; W. H. S. Jones translation', 'https://www.theoi.com/Text/Pausanias8B.html', ['8.25.4–10: Demeter, Poseidon, Despoina and Arion'], indexRights),
  source('zeus-index', 'Ancient testimonia for Zeus genealogies', 'Ancient authors identified in each locator; Theoi finding aid', 'https://www.theoi.com/Olympios/ZeusFamily.html', ['Ancient source references in the genealogical listing and regional quotations'], indexRights),
  source('poseidon-index', 'Ancient testimonia for Poseidon genealogies', 'Ancient authors identified in each locator; Theoi finding aid', 'https://www.theoi.com/Olympios/PoseidonFamily.html', ['Ancient source references in the genealogical listing and regional quotations'], indexRights),
  source('apollo-index', 'Ancient testimonia for Apollo genealogies', 'Ancient authors identified in each locator; Theoi finding aid', 'https://www.theoi.com/Olympios/ApollonFamily.html', ['Ancient source references in the genealogical listing and regional quotations'], indexRights),
  source('ares-index', 'Ancient testimonia for Ares genealogies', 'Ancient authors identified in each locator; Theoi finding aid', 'https://www.theoi.com/Olympios/AresFamily.html', ['Ancient source references in the genealogical listing and regional quotations'], indexRights),
  source('hermes-index', 'Ancient testimonia for Hermes genealogies', 'Ancient authors identified in each locator; Theoi finding aid', 'https://www.theoi.com/Olympios/HermesFamily.html', ['Ancient source references in the genealogical listing and regional quotations'], indexRights),
  source('dionysus-index', 'Ancient testimonia for Dionysus genealogies', 'Ancient authors identified in each locator; Theoi finding aid', 'https://www.theoi.com/Olympios/DionysosFamily.html', ['Ancient source references in the genealogical listing and regional quotations'], indexRights),
  source('aphrodite-index', 'Ancient testimonia for Aphrodite genealogies', 'Ancient authors identified in each locator; Theoi finding aid', 'https://www.theoi.com/Olympios/AphroditeFamily.html', ['Ancient source references in the genealogical listing and regional quotations'], indexRights),
  source('hephaestus-index', 'Ancient testimonia for Hephaestus genealogies', 'Ancient authors identified in each locator; Theoi finding aid', 'https://www.theoi.com/Olympios/HephaistosFamily.html', ['Proclus on the Timaeus: four daughters of Hephaestus and Aglaia', 'Regional genealogies and variants'], indexRights),
  source('demeter-index', 'Ancient testimonia for Demeter genealogies', 'Ancient authors identified in each locator; Theoi finding aid', 'https://www.theoi.com/Olympios/DemeterFamily.html', ['Hesiod, Pausanias, Hyginus and Diodorus passages'], indexRights),
  source('hades-index', 'Ancient testimonia for Hades genealogies', 'Ancient authors identified in each locator; Theoi finding aid', 'https://www.theoi.com/Khthonios/Haides.html', ['Distinguish Hades, Plouton and Zeus Chthonios in cited traditions'], indexRights),
  source('macaria', 'Suda, entry Makaria', 'Byzantine lexicon, tenth century CE; ancient-tradition reception', 'https://www.theoi.com/Khthonios/Makaria.html', ['s.v. Makaria: daughter of Hades; mother not specified'], indexRights),
  source('nonnus6', 'Dionysiaca, book 6', 'Nonnus, fifth century CE', 'https://www.theoi.com/Text/NonnusDionysiaca6.html', ['6.155–205: Persephone, Zeus and Zagreus'], indexRights),
  ...[1, 2, 3, 4, 5, 6].map(n => source(`fasti${n}`, `Fasti, book ${n}`, 'Ovid; ancient Latin text', `https://www.thelatinlibrary.com/ovid/ovid.fasti${n}.shtml`, [`Book ${n}; verse locators specified with each comparison`], 'Ancient Latin text is public domain. All English explanation is original paraphrase.')),
  source('aeneid1', 'Aeneid, book 1', 'Virgil; ancient Latin text', 'https://www.thelatinlibrary.com/vergil/aen1.shtml', ['1.124–156: Neptune', '1.223–296: Venus, Jupiter and Rome'], 'Ancient Latin text is public domain; original English paraphrases.'),
];

const r = (id, locator) => ({ sourceId: `gx-${id}`, locator });
const refs = (id, locator) => [r(id, locator)];
const records = [];
export const persons = records;
const byKey = new Map();
function person(key, name, category, summary, parents, sourceRefs, options = {}) {
  if (byKey.has(key)) throw new Error(`Duplicate genealogy key: ${key}`);
  const entry = { key, name, category, summary, parents, variants: [], sourceRefs,
    groups: [], mortality: ['mortal', 'hero', 'historical'].includes(category) ? 'mortal' : 'immortal', ...options };
  records.push(entry); byKey.set(key, entry); return entry;
}
function rows(group, category, parents, id, values) {
  for (const [key, name, summary, locator, options = {}] of values)
    person(key, name, category, summary, parents, refs(id, locator), { groups: [group], ...options });
}
function variant(key, parents, note, id, locator) {
  byKey.get(key).variants.push({ parents, note, sourceRefs: refs(id, locator) });
}

rows('origins', 'primordial', [], 'theogony', [
  ['chaos', 'Chaos', 'An initial gap or opening in Hesiod’s account, not a moral force of disorder.', '116–125'],
  ['gaia', 'Gaia', 'Earth: a generating power and active participant in successive struggles over divine rule.', '116–138'],
  ['tartarus', 'Tartarus', 'The deep cosmic region, also personified as a generative power.', '116–122; 820–822'],
  ['eros', 'Eros', 'A primordial power in Hesiod. Later poets also tell of a younger, parented Love; these chronologies are alternatives.', '120–122', { groups: ['origins', 'aphrodite-family'], roman: 'Amor / Cupid' }],
]);
rows('origins', 'primordial', ['chaos'], 'theogony', [
  ['nyx', 'Nyx', 'Night generates a large family of powers, often without a partner in this poem.', '123–125; 211–232'],
  ['erebus', 'Erebus', 'Darkness, paired with Night in the generation of Aether and Day.', '123–125'],
]);
rows('origins', 'primordial', ['nyx', 'erebus'], 'theogony', [
  ['aether', 'Aether', 'The bright upper air in Hesiod’s first generations.', '124–125'],
  ['hemera', 'Hemera', 'Day, distinguished from the later solar deity Helios.', '124–125'],
]);
rows('origins', 'primordial', ['gaia'], 'theogony', [
  ['uranus', 'Uranus', 'Sky, generated by Earth before becoming her partner and the first ruler displaced in the succession story.', '126–138'],
  ['pontus', 'Pontus', 'Sea, generated by Earth without a partner in this account.', '131–132'],
  ['ourea', 'Ourea', 'The Mountains appear collectively; Hesiod supplies no individual names here.', '129–130', { category: 'collective' }],
]);
rows('titans-twelve', 'titan', ['gaia', 'uranus'], 'theogony', [
  ['oceanus', 'Oceanus', 'The encircling river and ancestor of rivers and Oceanids.', '133–138; 337–370'],
  ['tethys', 'Tethys', 'Mother of rivers and Oceanids with Oceanus; distinct from the Nereid Thetis.', '133–138; 337–370'],
  ['coeus', 'Coeus', 'Titan paired with Phoebe; their line leads to Leto and Asteria.', '133–138; 404–411'],
  ['phoebe', 'Phoebe', 'Titan paired with Coeus; grandmother of Apollo, Artemis and Hecate.', '133–138; 404–411'],
  ['crius', 'Crius', 'Titan paired with Eurybia; father of Astraeus, Pallas and Perses.', '133–138; 375–377'],
  ['hyperion', 'Hyperion', 'Titan paired with Theia; father of Sun, Moon and Dawn.', '133–138; 371–374'],
  ['theia', 'Theia', 'Titan and mother of the three luminous siblings Helios, Selene and Eos.', '133–138; 371–374'],
  ['iapetus', 'Iapetus', 'Titan whose children include Atlas, Prometheus and Epimetheus.', '133–138; 507–511'],
  ['themis', 'Themis', 'Divine order; Zeus’s partner in the Horae and one genealogy of the Fates.', '133–138; 901–906'],
  ['mnemosyne', 'Mnemosyne', 'Memory; mother of the nine Muses with Zeus.', '133–138; 53–79; 915–917'],
  ['cronus', 'Cronus', 'Titan ruler who overthrows Uranus and is in turn displaced by Zeus.', '133–138; 154–210; 453–506', { roman: 'Saturn (comparison, not identity)' }],
  ['rhea', 'Rhea', 'Mother of the six children of Cronus; she arranges the concealment of Zeus.', '133–138; 453–491', { roman: 'Ops / Magna Mater in different Roman identifications' }],
]);
rows('elder-allies', 'deity', ['gaia', 'uranus'], 'theogony', [
  ['brontes', 'Brontes', 'One of the three Cyclopes associated with Zeus’s thunder weapons.', '139–146; 501–506'],
  ['steropes', 'Steropes', 'One of the three elder Cyclopes; not one of the pastoral Cyclopes in the Odyssey.', '139–146'],
  ['arges', 'Arges', 'One of the three named elder Cyclopes.', '139–146'],
  ['cottus', 'Cottus', 'A Hundred-Hander enlisted on Zeus’s side in the Titan conflict.', '147–153; 617–735'],
  ['briareus', 'Briareus', 'A Hundred-Hander; later given Cymopoleia as his wife.', '147–153; 817–819'],
  ['gyges', 'Gyges', 'A Hundred-Hander, brother of Cottus and Briareus.', '147–153'],
]);
rows('titan-descendants', 'deity', ['hyperion', 'theia'], 'theogony', [
  ['helios', 'Helios', 'Sun deity; distinguish him from Apollo before later solar identifications.', '371–374', { roman: 'Sol' }],
  ['selene', 'Selene', 'Moon deity; distinguish her from Artemis before later lunar identifications.', '371–374', { roman: 'Luna' }],
  ['eos', 'Eos', 'Dawn, sister of Sun and Moon.', '371–374', { roman: 'Aurora' }],
]);
rows('titan-descendants', 'deity', ['coeus', 'phoebe'], 'theogony', [
  ['leto', 'Leto', 'Mother of Apollo and Artemis; a daughter of Titans, not one of Hesiod’s original twelve.', '404–408; 918–920', { roman: 'Latona' }],
  ['asteria', 'Asteria', 'Mother of Hecate with Perses.', '409–411'],
]);
rows('sea-foundations', 'deity', ['gaia', 'pontus'], 'theogony', [
  ['nereus', 'Nereus', 'Truthful elder of the sea and father of the Nereids.', '233–239'],
  ['thaumas', 'Thaumas', 'Sea-family deity; father of Iris and the Harpies with the Oceanid Electra.', '237; 265–269'],
  ['phorcys', 'Phorcys', 'Sea elder whose family with Ceto includes the Graeae and Gorgons.', '237–239; 270–336'],
  ['ceto', 'Ceto', 'Sea-family deity paired with Phorcys in the monster genealogies.', '237–239; 270–336'],
  ['eurybia', 'Eurybia', 'Daughter of Earth and Sea; mother of Astraeus, Pallas and Perses with Crius.', '239; 375–377'],
]);
rows('titan-descendants', 'deity', ['crius', 'eurybia'], 'theogony', [
  ['astraeus', 'Astraeus', 'Father of winds and stars with Eos.', '375–382'],
  ['pallas-titan', 'Pallas (Titan)', 'Father of Nike, Zelus, Kratos and Bia with Styx; distinguish other figures named Pallas.', '375–403'],
  ['perses', 'Perses (Titan)', 'Father of Hecate; not the brother addressed in Works and Days.', '375–377; 409–411'],
]);
rows('titan-descendants', 'deity', ['perses', 'asteria'], 'theogony', [
  ['hecate', 'Hecate', 'Hesiod gives her wide honours over land, sea and sky, not only later underworld associations.', '409–452'],
]);
rows('oceanids', 'nymph', ['oceanus', 'tethys'], 'theogony', [
  ['doris', 'Doris', 'Oceanid and mother of Nereids with Nereus.', '240–264; 350'],
  ['clymene-oceanid', 'Clymene (Oceanid)', 'Mother of the Iapetids in Hesiod; distinguish other women of this name.', '351; 507–511'],
  ['metis', 'Metis', 'Cunning intelligence; Zeus swallows her while she is pregnant with Athena.', '358; 886–900'],
  ['eurynome', 'Eurynome', 'Oceanid and mother of the three Charites with Zeus.', '358; 907–911'],
  ['styx', 'Styx', 'Oath-bearing river goddess; her children ally with Zeus before the Titan war.', '361; 383–403'],
  ['electra-oceanid', 'Electra (Oceanid)', 'Mother of Iris and the Harpies; distinct from Electra the Pleiad.', '266; 349'],
  ['perseis', 'Perseis', 'Oceanid and mother of Circe and Aeetes with Helios.', '356; 956–962'],
  ['idya', 'Idya', 'Oceanid and mother of Medea with Aeetes.', '352; 958–962'],
  ['dione', 'Dione', 'Listed among Oceanids in Hesiod; elsewhere mother of Aphrodite by Zeus.', '353', { groups: ['oceanids', 'zeus-divine'] }],
  ['peitho', 'Peitho', 'Persuasion: an Oceanid here; other poetry connects her closely with Aphrodite.', '349'],
]);
rows('iapetids', 'titan', ['iapetus', 'clymene-oceanid'], 'theogony', [
  ['atlas', 'Atlas', 'Condemned to support heaven, not to carry the terrestrial globe in Hesiod.', '507–520'],
  ['menoetius', 'Menoetius', 'Zeus strikes him down for violent arrogance.', '507–516'],
  ['prometheus', 'Prometheus', 'Challenges Zeus over sacrifice and fire; his punishment defines a boundary between gods and humans.', '507–616'],
  ['epimetheus', 'Epimetheus', 'Accepts the woman made by the gods, overlooking Prometheus’s warning in Works and Days.', '511–514', { sourceRefs: [r('theogony', '511–514'), r('works', '83–89')] }],
]);
rows('titan-descendants', 'deity', ['pallas-titan', 'styx'], 'theogony', [
  ['nike', 'Nike', 'Victory; one of Styx’s children permanently honoured beside Zeus.', '383–403', { roman: 'Victoria' }],
  ['zelus', 'Zelus', 'Zeal or emulation; sibling of Victory, Strength and Force.', '383–403'],
  ['kratos', 'Kratos', 'Strength or power; a personified ally of Zeus.', '383–403'],
  ['bia', 'Bia', 'Force; a personified ally of Zeus.', '383–403'],
]);
rows('cronus-rhea', 'olympian', ['cronus', 'rhea'], 'theogony', [
  ['hestia', 'Hestia', 'Hearth goddess who remains unmarried and without biological children in the Homeric Hymn to Aphrodite.', '453–457', { roman: 'Vesta', offspringNote: 'Virgin goddess; household association does not imply motherhood.', sourceRefs: [r('theogony', '453–457'), r('hymns', '5.21–32')] }],
  ['demeter', 'Demeter', 'Grain goddess whose family links agriculture, wealth and the underworld.', '453–457', { roman: 'Ceres', groups: ['cronus-rhea', 'demeter-family'] }],
  ['hera', 'Hera', 'Queen of the gods and a power of marriage; Hephaestus has competing birth accounts.', '453–457; 921–929', { roman: 'Juno', groups: ['cronus-rhea', 'hera-family'] }],
  ['hades', 'Hades', 'Ruler of the dead. His underworld subjects should not be turned into biological children.', '455–457', { category: 'deity', roman: 'Dis Pater / Pluto', groups: ['cronus-rhea', 'underworld'], offspringNote: 'Only sparse and varying offspring traditions; Macaria’s mother is not named in the Suda.' }],
  ['poseidon', 'Poseidon', 'Sea and earthquake god whose descendants populate many regional genealogies.', '456–457', { roman: 'Neptune', groups: ['cronus-rhea', 'poseidon-family'] }],
  ['zeus', 'Zeus', 'Sovereign whose children connect cosmic functions, sanctuaries and local royal claims.', '457–506', { roman: 'Jupiter', groups: ['cronus-rhea', 'zeus-divine', 'zeus-heroes', 'zeus-regional'] }],
]);
rows('zeus-divine', 'olympian', ['zeus', 'leto'], 'theogony', [
  ['apollo', 'Apollo', 'God of prophecy, healing, song and archery; his children often anchor local cults and seer lineages.', '918–920', { roman: 'Apollo', groups: ['zeus-divine', 'apollo-family'] }],
  ['artemis', 'Artemis', 'Virgin huntress; attendants and protected children are not biological offspring.', '918–920', { roman: 'Diana', offspringNote: 'Virgin goddess; no biological children in the selected mainstream sources.', sourceRefs: [r('theogony', '918–920'), r('hymns', '5.16–20')] }],
]);
rows('zeus-divine', 'deity', ['zeus', 'hera'], 'theogony', [
  ['ares', 'Ares', 'War god; his divine and mortal children belong to different local and poetic traditions.', '921–923', { category: 'olympian', roman: 'Mars', groups: ['zeus-divine', 'hera-family', 'ares-family'] }],
  ['hebe', 'Hebe', 'Youth personified; later paired with the deified Heracles.', '921–923; 950–955', { roman: 'Juventas', groups: ['zeus-divine', 'hera-family'] }],
  ['eileithyia', 'Eileithyia', 'A childbirth goddess; she may appear in the singular or as plural birth powers.', '921–923', { roman: 'Lucina (a Roman title, especially of Juno)', groups: ['zeus-divine', 'hera-family'] }],
]);
rows('zeus-divine', 'olympian', ['zeus', 'metis'], 'theogony', [
  ['athena', 'Athena', 'Born from Zeus after Metis is swallowed. Athena raises Erichthonius but does not give birth to him.', '886–900; 924–926', { roman: 'Minerva', offspringNote: 'Virgin goddess. Erichthonius is a fosterling; his parentage is Hephaestus and Earth.', sourceRefs: [r('theogony', '886–900; 924–926'), r('apollodorus3', '3.14.6'), r('hymns', '5.7–15')] }],
]);
rows('hera-family', 'olympian', ['hera'], 'theogony', [
  ['hephaestus', 'Hephaestus', 'Hera bears him without a partner in Hesiod. Other accounts make Zeus his father.', '927–929', { roman: 'Vulcan', groups: ['hera-family', 'hephaestus-family', 'zeus-variants'] }],
]);
variant('hephaestus', ['zeus', 'hera'], 'Library reports Zeus and Hera as parents, while explicitly preserving the competing Hera-alone version.', 'apollodorus1', '1.3.5');
rows('aphrodite-family', 'olympian', ['uranus'], 'theogony', [
  ['aphrodite', 'Aphrodite', 'In Hesiod she arises from sea-foam around Uranus’s severed genitals. Homer’s family is different.', '188–206', { roman: 'Venus', birthNote: 'The sea is the setting and medium; no conventional mother is named.', groups: ['aphrodite-family', 'zeus-variants'] }],
]);
variant('aphrodite', ['zeus', 'dione'], 'Homer presents Dione as Aphrodite’s mother and Zeus as her father; do not merge this with the Uranus birth.', 'zeus-index', 'Homer, Iliad 5.370–430');

rows('titan-descendants', 'nymph', ['oceanus'], 'apollodorus3', [
  ['pleione', 'Pleione', 'Ocean’s daughter and mother of the seven Pleiades with Atlas. The cited passage identifies her father but does not supply a mother.', '3.10.1'],
]);
rows('titan-descendants', 'nymph', ['atlas', 'pleione'], 'apollodorus3', [
  ['maia', 'Maia', 'One of the seven Pleiades. Hermes is born to her and Zeus in a cave on Cyllene; her secluded setting contrasts with her son’s immediate mobility.', '3.10.1–2', { groups: ['titan-descendants', 'zeus-divine'] }],
  ['electra-pleiad', 'Electra (Pleiad)', 'Atlas’s daughter in the Trojan and Samothracian lines. Keep her distinct from the Oceanid Electra, mother of Iris and the Harpies.', '3.10.1; 3.12.1'],
  ['taygete', 'Taygete', 'A Pleiad whose son Lacedaemon provides a divine ancestry for the Spartan region.', '3.10.1, 3'],
  ['alcyone-pleiad', 'Alcyone (Pleiad)', 'Pleiad assigned several children of Poseidon; distinguish the woman transformed into a kingfisher in a different story.', '3.10.1'],
  ['celaeno-pleiad', 'Celaeno (Pleiad)', 'Pleiad and mother of Lycus by Poseidon. Several unrelated mythic figures bear her name.', '3.10.1'],
  ['sterope-pleiad', 'Sterope (Pleiad)', 'Also called Asterope. The Library makes her mother of Oenomaus by Ares.', '3.10.1'],
  ['merope-pleiad', 'Merope (Pleiad)', 'Pleiad paired with the mortal Sisyphus, unlike her sisters’ divine parentage pairings.', '3.10.1'],
]);
rows('zeus-divine', 'olympian', ['zeus', 'maia'], 'theogony', [
  ['hermes', 'Hermes', 'Messenger, boundary-crosser and guide. His offspring traditions range from the rustic Pan to local founders and the skilled thief Autolycus.', '938–939', { roman: 'Mercury', groups: ['zeus-divine', 'hermes-family'] }],
]);
rows('zeus-divine', 'olympian', ['zeus', 'semele'], 'theogony', [
  ['dionysus', 'Dionysus', 'The mortal Semele bears an immortal son. Other traditions describe an earlier child of Persephone; that later theology is labelled separately rather than inserted silently into Hesiod.', '940–942', { roman: 'Bacchus / Liber', groups: ['zeus-divine', 'dionysus-family'] }],
]);
rows('zeus-divine', 'deity', ['zeus', 'demeter'], 'theogony', [
  ['persephone', 'Persephone', 'Daughter of Demeter and Zeus, taken by Hades with Zeus’s consent in the hymn tradition. Genealogy does not erase the abduction or Demeter’s resistance.', '912–914', { roman: 'Proserpina', groups: ['zeus-divine', 'demeter-family', 'underworld'] }],
]);
variant('persephone', ['zeus', 'styx'], 'The Library also reports a Zeus–Styx parentage; it is not Hesiod’s Demeter genealogy.', 'apollodorus1', '1.3.1');
rows('zeus-divine', 'deity', ['zeus', 'themis'], 'theogony', [
  ['eunomia', 'Eunomia', 'Good Order, one of the three Horae named in Hesiod. These sisters connect Zeus’s kingship with civic and seasonal order.', '901–903'],
  ['dike', 'Dike', 'Justice, a Hora who also appears in Works and Days as a divine witness to crooked human judgement.', '901–903', { sourceRefs: [r('theogony', '901–903'), r('works', '256–262')] }],
  ['eirene', 'Eirene', 'Peace, one of the three named Horae. The poem gives a genealogy of social goods, not merely a set of weather spirits.', '901–903', { roman: 'Pax' }],
  ['clotho', 'Clotho', 'One of the three named Moirai. Hesiod presents both a Night genealogy and a Zeus–Themis genealogy within the same poem.', '904–906', { groups: ['zeus-divine', 'night-underworld'] }],
  ['lachesis', 'Lachesis', 'One of the three named Moirai. Her family placement changes between passages rather than yielding a single reconciled pedigree.', '904–906', { groups: ['zeus-divine', 'night-underworld'] }],
  ['atropos', 'Atropos', 'One of the three named Moirai, who allot mortals their portions of good and ill in Hesiod’s account.', '904–906', { groups: ['zeus-divine', 'night-underworld'] }],
]);
for (const key of ['clotho', 'lachesis', 'atropos']) variant(key, ['nyx'], 'Hesiod’s earlier Night catalogue supplies an alternative genealogy in the very same poem.', 'theogony', '211–225');
rows('zeus-divine', 'deity', ['zeus', 'eurynome'], 'theogony', [
  ['aglaia', 'Aglaia', 'Splendour, youngest of Hesiod’s three Charites and wife of Hephaestus. Later theological material names their four daughters.', '907–911; 945–946', { groups: ['zeus-divine', 'hephaestus-family'] }],
  ['euphrosyne', 'Euphrosyne', 'Joy or mirth, one of the three Charites. This named triad does not exhaust the different Graces worshipped or described elsewhere.', '907–911'],
  ['thalia-grace', 'Thalia (Grace)', 'A Charis associated with flourishing and festivity. Distinguish her from the Muse Thalia and the Sicilian Thaleia.', '907–911'],
]);
for (const key of ['aglaia', 'euphrosyne', 'thalia-grace']) variant(key, ['zeus', 'eunomia'], 'Orphic Hymn 60 calls the Graces children of Zeus and Eunomia rather than Eurynome.', 'orphic', '60');
rows('zeus-divine', 'deity', ['zeus', 'mnemosyne'], 'theogony', [
  ['calliope', 'Calliope', 'Foremost Muse in Hesiod’s proem, especially attending kings and persuasive speech. The familiar allocation of one artistic discipline to each sister developed beyond this poem.', '53–79'],
  ['clio', 'Clio', 'Named among the nine daughters of Zeus and Memory. Later associated especially with history; Hesiod instead stresses their shared gift of song.', '53–79'],
  ['euterpe', 'Euterpe', 'One of the nine Muses. Later musical specialisation should not be mistaken for a separate birth narrative in Hesiod.', '53–79'],
  ['thalia-muse', 'Thalia (Muse)', 'One of the nine Muses, distinct from the Grace of the same name. Later associated with comedy.', '53–79'],
  ['melpomene', 'Melpomene', 'One of the nine Muses; her later association with tragedy is a reception history, not the subject of Hesiod’s genealogy.', '53–79'],
  ['terpsichore', 'Terpsichore', 'One of the nine Muses. Dance is prominent in the collective scene of the Muses singing and moving on Helicon.', '1–35; 53–79'],
  ['erato', 'Erato', 'One of Zeus and Mnemosyne’s nine daughters, invoked within a shared divine source of poetic performance.', '53–79'],
  ['polyhymnia', 'Polyhymnia', 'A Muse named in the Hesiodic list; later traditions distinguish sacred song and other specialisations among the sisters.', '53–79'],
  ['urania-muse', 'Urania (Muse)', 'The Muse Urania; keep her identity separate from Uranus and from Aphrodite’s title Ourania.', '53–79'],
]);
rows('zeus-divine', 'deity', ['zeus'], 'zeus-index', [
  ['aletheia', 'Aletheia', 'Truth is addressed as Zeus’s daughter in Pindar’s poetic invocation. This is personification, not a fully narrated childhood.', 'Pindar, Olympian 11.1–2'],
  ['kairos', 'Kairos', 'Opportunity has an altar at Olympia; Pausanias reports a hymn describing him as Zeus’s youngest son. No mother is supplied.', 'Pausanias 5.14.9'],
  ['litai', 'Litai', 'Prayers, personified collectively as Zeus’s daughters in Phoenix’s speech to Achilles about Ruin. The Iliad does not supply individual names for them.', 'Homer, Iliad 9.502–514', { category: 'collective' }],
]);
rows('zeus-regional', 'deity', ['zeus', 'selene'], 'zeus-index', [
  ['pandia', 'Pandia', 'A daughter of Zeus and Selene in the Homeric Hymn to the Moon. The short hymn gives her parentage but little independent narrative.', 'Homeric Hymn 32.14–16', { sourceRefs: refs('hymns', '32.14–16') }],
  ['ersa', 'Ersa', 'Dew, assigned to Zeus and Selene in a fragment attributed to Alcman. The surviving notice is much narrower than a developed personal mythology.', 'Alcman, fragment 57'],
  ['nemea-nymph', 'Nemea (nymph)', 'A local eponym linked to Zeus and Selene in scholia. The late reporting of this genealogy is flagged rather than treated as archaic consensus.', 'Scholium on Pindar, Nemean Odes, cited under Nemea', { attestation: 'scholiastic tradition' }],
]);
rows('zeus-regional', 'deity', ['zeus', 'carme'], 'zeus-index', [
  ['britomartis', 'Britomartis', 'Cretan goddess connected with nets and with the name Dictynna. Accounts of her escape from Minos should be read as pursuit and threatened coercion, not consensual courtship.', 'Pausanias 2.30.3; Diodorus 5.76.3; Antoninus Liberalis 40'],
]);
rows('zeus-variants', 'deity', ['zeus', 'persephone'], 'nonnus6', [
  ['zagreus', 'Zagreus', 'In Nonnus, Zeus violates Persephone in serpent form; their horned child is destroyed by Titans. Later Dionysiac traditions connect this child with Dionysus, but the name and synthesis are not universal.', '6.155–205', { attestation: 'late antique Dionysiac narrative', groups: ['zeus-variants', 'underworld'] }],
]);
rows('zeus-variants', 'deity', ['zeus', 'persephone'], 'orphic', [
  ['melinoe', 'Melinoe', 'Orphic Hymn 71 describes a frightening nocturnal power born to Persephone after Zeus approaches in Plouton’s likeness. Simply relabelling her Hades’s daughter would remove the hymn’s central qualification.', '71', { attestation: 'imperial-era Orphic hymn', groups: ['zeus-variants', 'underworld'] }],
]);
rows('zeus-regional', 'deity', ['zeus', 'gaia'], 'zeus-index', [
  ['agdistis', 'Agdistis', 'Pausanias transmits a Phrygian story of a being with male and female sexual characteristics, born from Zeus’s seed on earth. This belongs to a particular cult narrative, not an Olympian household list.', 'Pausanias 7.17.8–12', { attestation: 'Phrygian tradition reported by Pausanias' }],
  ['cyprian-centaurs', 'Cyprian Centaurs', 'An unnamed collective born from Zeus’s seed on Earth in Nonnus. They are distinct from the familiar Thessalian Centaurs; individual names are not invented here.', 'Nonnus, Dionysiaca 5.611–615; 14.193–202', { category: 'collective' }],
]);
rows('zeus-regional', 'deity', ['zeus', 'boetis'], 'zeus-index', [
  ['aegipan', 'Aegipan', 'A goat-associated divine figure whose mother is Boetis in Hyginus’s genealogy and Aix in another account. Do not automatically merge every Aegipan notice with Pan.', 'Hyginus, Fabulae 155'],
]);
variant('aegipan', ['zeus', 'aix'], 'Hyginus’s astronomical account names the goat Aix in this parentage tradition.', 'zeus-index', 'Hyginus, Astronomica 2.13');
rows('zeus-regional', 'collective', ['zeus', 'thaleia-sicilian'], 'zeus-index', [
  ['palici', 'Palici', 'The Sicilian twin powers of a local sanctuary. Different sources give different divine parents; the selected notice does not justify inventing two individual names.', 'Macrobius, Saturnalia 5.19.15', { groups: ['zeus-regional', 'hephaestus-family'] }],
]);
variant('palici', ['hephaestus', 'aetna'], 'Other accounts derive the Palici from Hephaestus and Aetna; this is a competing parent set.', 'hephaestus-index', 'Servius, Commentary on Virgil’s Aeneid 9.584');
rows('zeus-regional', 'collective', ['zeus', 'calliope'], 'zeus-index', [
  ['samothracian-corybantes', 'Samothracian Corybantes', 'Strabo preserves competing explanations for these ritual figures. This Zeus–Calliope pedigree is one reported account, not an equation of every Corybant and Cabeirus.', 'Strabo 10.3.19', { attestation: 'reported variant' }],
]);

rows('zeus-heroes', 'hero', ['zeus', 'alcmene'], 'apollodorus2', [
  ['heracles', 'Heracles', 'Zeus fathers Heracles by impersonating Alcmene’s husband Amphitryon. The hero’s mortal labours, death and later divine marriage belong to a longer story than the label “demigod” alone conveys.', '2.4.8; 2.7.7', { roman: 'Hercules', mortality: 'mortal-then-divine', groups: ['zeus-heroes', 'heroic-descendants'] }],
]);
rows('zeus-heroes', 'hero', ['zeus', 'danae'], 'apollodorus2', [
  ['perseus', 'Perseus', 'Born to Danae despite her father’s attempt to imprison her against an oracle. He survives exposure with his mother, kills Medusa and becomes an ancestor of Heracles.', '2.4.1–5'],
]);
rows('zeus-heroes', 'hero', ['zeus', 'leda'], 'apollodorus3', [
  ['helen', 'Helen', 'Spartan queen whose divine parentage has competing mother traditions. The Library distinguishes her birth from the later contested stories of departure, abduction and the Trojan War.', '3.10.7', { variants: [{ parents: ['zeus', 'nemesis'], note: 'The Library reports Nemesis as the birth mother and Leda as the woman who receives and raises the egg-born child.', sourceRefs: refs('apollodorus3', '3.10.7') }] }],
  ['polydeuces', 'Polydeuces / Pollux', 'One of the Dioscuri. A divided-paternity account gives him Zeus as father and gives Castor Tyndareus; other poetry treats both twins as sons of Zeus.', '3.10.7; 3.11.2'],
]);
rows('zeus-heroes', 'hero', ['tyndareus', 'leda'], 'apollodorus3', [
  ['castor', 'Castor', 'The mortal twin in the divided-paternity account. His bond with Polydeuces allows a story of shared mortality and immortality rather than a simple pair of identical divine births.', '3.10.7; 3.11.2', { mortality: 'mixed-tradition' }],
]);
variant('castor', ['zeus', 'leda'], 'The Homeric Hymn to the Dioscuri praises both twins as sons of Zeus.', 'hymns', '33.1–5');
rows('zeus-heroes', 'hero', ['zeus', 'europa'], 'apollodorus3', [
  ['minos', 'Minos', 'Cretan king born from Zeus’s taking of Europa. His genealogy links Crete with Phoenicia and underlies later stories of law, the Minotaur and Athenian tribute.', '3.1.1–4'],
  ['rhadamanthys', 'Rhadamanthys', 'Brother of Minos and a figure of exemplary judgement. His later afterlife role should be distinguished from the Cretan family pedigree that introduces him.', '3.1.1–2'],
  ['sarpedon-europa', 'Sarpedon (Europa’s son)', 'The Europa genealogy places this Sarpedon beside Minos and Rhadamanthys. The Library discusses the chronological problem of identifying him with Homer’s Trojan-war warrior.', '3.1.1–2', { disambiguation: 'Europa’s son; compare sarpedon-laodamia' }],
]);
rows('zeus-heroes', 'hero', ['zeus', 'laodamia-bellerophon'], 'zeus-index', [
  ['sarpedon-laodamia', 'Sarpedon (Laodamia’s son)', 'Homer’s Lycian leader, son of Bellerophon’s daughter Laodamia. Zeus grieves over his death in the Iliad; this mother belongs to a different genealogy from the Europa account.', 'Homer, Iliad 6.198–205; 16.431–683', { disambiguation: 'Iliadic Sarpedon; compare sarpedon-europa' }],
]);
rows('zeus-heroes', 'hero', ['zeus', 'antiope'], 'apollodorus3', [
  ['amphion', 'Amphion', 'With his twin Zethus, he takes power at Thebes and avenges their mother Antiope. His association with music distinguishes him within the shared twin genealogy.', '3.5.5–6'],
  ['zethus', 'Zethus', 'Amphion’s twin and partner in the Theban foundation story. Their mother’s persecution is part of the account, not an incidental decoration on a divine family tree.', '3.5.5–6'],
]);
rows('zeus-heroes', 'hero', ['zeus', 'aegina'], 'apollodorus3', [
  ['aeacus', 'Aeacus', 'Aegina’s son and ancestor of Peleus and Telamon. The line leads to Achilles and Ajax, giving the Trojan-war families a connection to Zeus without making them all Zeus’s direct children.', '3.12.6'],
]);
rows('zeus-heroes', 'hero', ['zeus', 'callisto'], 'apollodorus3', [
  ['arcas', 'Arcas', 'Arcadian eponym whose mother Callisto is caught in a story of sexual violence and transformation. Accounts differ over disguise and responsibility for the bear transformation.', '3.8.2'],
]);
rows('zeus-heroes', 'hero', ['zeus', 'io'], 'apollodorus2', [
  ['epaphus', 'Epaphus', 'Born after Io’s suffering and wandering, he is placed in Egypt and becomes father of Libya. Greek genealogical geography here should not be mistaken for Egyptian historical genealogy.', '2.1.3–4'],
]);
rows('zeus-heroes', 'hero', ['zeus', 'electra-pleiad'], 'apollodorus3', [
  ['dardanus', 'Dardanus', 'An ancestral founder of the Trojan line. His descent from Electra the Pleiad connects the royal genealogy with Atlas and with Samothracian accounts.', '3.12.1–2'],
  ['iasion', 'Iasion', 'Electra’s son and Demeter’s partner in the birth of Plutus. Traditions about his death and divine relations vary across epic and local narratives.', '3.12.1', { sourceRefs: [r('apollodorus3', '3.12.1'), r('theogony', '969–974')], groups: ['zeus-heroes', 'demeter-family'] }],
]);

rows('zeus-regional', 'hero', ['zeus', 'niobe-phoroneus'], 'apollodorus2', [
  ['argus-king', 'Argus (early king)', 'Son of Niobe, daughter of Phoroneus, and eponym of Argos. This is neither the many-eyed guardian Argus nor Jason’s shipbuilder.', '2.1.1–2'],
  ['pelasgus', 'Pelasgus', 'Acusilaus makes him a son of Zeus and Niobe; Hesiod instead calls him earth-born. The disagreement marks competing explanations of regional origins.', '2.1.1', { variants: [{ parents: ['gaia'], note: 'Earth-born according to Hesiod as reported by the Library; this is not a conventional two-parent birth.', sourceRefs: refs('apollodorus2', '2.1.1') }] }],
]);
rows('zeus-regional', 'hero', ['zeus', 'taygete'], 'apollodorus3', [
  ['lacedaemon', 'Lacedaemon', 'An eponym of the Spartan region through the Pleiad Taygete. His local royal genealogy is distinct from the Dioscuri’s later Spartan stories.', '3.10.3'],
]);
rows('zeus-regional', 'hero', ['zeus', 'protogeneia-deucalion'], 'apollodorus1', [
  ['aethlius', 'Aethlius', 'An early Elean ancestor, son of Deucalion’s daughter Protogeneia in this account. Other genealogical fragments give a different mother.', '1.7.2'],
]);
variant('aethlius', ['zeus', 'calyce'], 'The Hesiodic fragment gives Calyce, requiring a different family arrangement from the Library’s Protogeneia line.', 'catalogue', 'fragment 8 (Evelyn-White numbering)');
rows('zeus-regional', 'hero', ['aethlius', 'calyce'], 'apollodorus1', [
  ['endymion', 'Endymion', 'Elean king linked with Selene. The Library makes Aethlius his father but also records Zeus as an alternative; keep those edges mutually attributed.', '1.7.5'],
]);
variant('endymion', ['zeus', 'calyce'], 'The Library explicitly reports that some call him a son of Zeus.', 'apollodorus1', '1.7.5');
rows('zeus-regional', 'hero', ['deucalion', 'pyrrha'], 'apollodorus1', [
  ['hellen', 'Hellen', 'Eponymous ancestor of the Hellenes in the post-flood family. The Library’s selected Deucalion pedigree coexists with an explicitly reported Zeus alternative.', '1.7.2'],
]);
variant('hellen', ['zeus', 'pyrrha'], 'Some accounts assign Zeus as father; this alternative does not make Deucalion and Zeus interchangeable identities.', 'apollodorus1', '1.7.2');
rows('zeus-regional', 'hero', ['zeus', 'thyia-deucalion'], 'catalogue', [
  ['magnes', 'Magnes', 'A regional ancestor born to Deucalion’s daughter Thyia in this fragment. A separate Aeolid pedigree also circulates.', 'fragment 3 (Evelyn-White numbering)'],
  ['macedon', 'Macedon', 'Eponymous Macedonian ancestor, paired with Magnes in the Hesiodic fragment. This is a mythic kinship claim, not a historical account of population origins.', 'fragment 3 (Evelyn-White numbering)'],
]);
rows('zeus-regional', 'hero', ['zeus', 'pandora-deucalion'], 'catalogue', [
  ['graecus', 'Graecus', 'Son of Pandora, daughter of Deucalion, in the surviving fragment. She is not the first woman Pandora, and she is not Thyia, mother of Magnes and Macedon.', 'fragment 2 (Evelyn-White numbering)'],
]);
rows('zeus-regional', 'hero', ['zeus', 'himalia'], 'diodorus5', [
  ['spartaeus', 'Spartaeus', 'One of three named sons in Diodorus’s Rhodian account. Their naming belongs to a local explanation of the island’s earliest inhabitants.', '5.55.4–5'],
  ['cronius', 'Cronius', 'Himalia’s son in the Rhodian narrative; do not confuse his name with Cronus the Titan.', '5.55.4–5'],
  ['cytus', 'Cytus', 'The third named son of Zeus and Himalia in Diodorus’s account of Rhodes.', '5.55.4–5'],
]);
rows('zeus-regional', 'hero', ['zeus'], 'zeus-index', [
  ['arcesius', 'Arcesius', 'Laertes’s father in Odysseus’s ancestry. Ovid’s speech for Ulysses carries the male line back through Arcesius to Jupiter; other genealogies differ.', 'Ovid, Metamorphoses 13.144–147'],
  ['corinthus', 'Corinthus', 'An eponym of Corinth whom Pausanias reports as Zeus’s son. The surviving notice supplies no mother and should remain a local attribution.', 'Pausanias 2.1.1'],
  ['crinacus', 'Crinacus', 'Ancestor connected with Olenus in the Hesiodic fragments. This brief pedigree supports a genealogical node, not an invented biography.', 'Hesiod, Catalogue fragment 52; Diodorus 5.81.4'],
  ['phasis', 'Phasis', 'A river personification called Zeus’s son in Valerius Flaccus. Hesiod places Phasis among the rivers of Oceanus and Tethys instead.', 'Valerius Flaccus, Argonautica 5.205', { category: 'deity', groups: ['zeus-variants', 'sea-foundations'] }],
]);
variant('phasis', ['oceanus', 'tethys'], 'Hesiod’s river catalogue includes Phasis among the children of Oceanus and Tethys.', 'theogony', '337–345');
rows('zeus-regional', 'hero', ['zeus', 'sithnid-nymph'], 'zeus-index', [
  ['megarus', 'Megarus', 'Megarian tradition makes him the son of Zeus and a local Sithnid nymph and credits him with surviving Deucalion’s flood.', 'Pausanias 1.40.1'],
]);
rows('zeus-regional', 'hero', ['zeus', 'eurymedusa'], 'zeus-index', [
  ['myrmidon', 'Myrmidon', 'A Thessalian eponym linked with Eurymedusa and Zeus in an ant-disguise story. The report survives in polemical testimony, not an independent early epic.', 'Clement of Alexandria, Exhortation to the Greeks 2.39.6', { attestation: 'imperial-era report of myth' }],
]);
rows('zeus-regional', 'hero', ['zeus', 'othreis'], 'zeus-index', [
  ['meliteus', 'Meliteus', 'A regional founder born to the nymph Othreis. Antoninus Liberalis’s story relates his exposure and survival to the naming of Melite.', 'Antoninus Liberalis 13'],
]);
rows('zeus-regional', 'hero', ['zeus', 'cassiopeia-crete'], 'apollodorus3', [
  ['atymnius', 'Atymnius', 'A Cretan youth whose Zeus–Cassiopeia parentage appears in the Library’s account of competing attachments among Minos, Sarpedon and Miletus.', '3.1.2'],
]);
rows('zeus-regional', 'hero', ['zeus', 'plouto'], 'zeus-index', [
  ['tantalus', 'Tantalus', 'An ancestral Lydian king and father of Pelops. His intimacy with the gods ends in notorious punishment; his mother Plouto is distinct from Plutus, the wealth god.', 'Pausanias 2.22.3; Diodorus 4.74.1; Hyginus, Fabulae 155'],
]);
rows('zeus-regional', 'creature', ['zeus', 'elara'], 'apollodorus1', [
  ['tityus', 'Tityus', 'An enormous figure whom Zeus hides beneath Earth through his mother Elara. His later punishment follows an assault on Leto; an earth-born variant also circulates.', '1.4.1', { mortality: 'mortal' }],
]);
variant('tityus', ['gaia'], 'The Odyssey calls Tityus a son of Earth rather than supplying the Zeus–Elara pedigree.', 'zeus-index', 'Homer, Odyssey 11.576–581');
rows('zeus-regional', 'hero', ['zeus', 'electra-pleiad'], 'zeus-index', [
  ['emathion-samothrace', 'Emathion (Samothrace)', 'Nonnus gives Electra a son Emathion who rules Samothrace. He should not be merged with Eos’s son Emathion merely because the names match.', 'Nonnus, Dionysiaca 3.124–140'],
]);
rows('zeus-regional', 'hero', ['zeus', 'io'], 'zeus-index', [
  ['ceroessa', 'Ceroessa', 'A local Byzantine genealogy makes her the daughter of Zeus and Io and mother of Byzas. This later foundation tradition is distinct from Io’s better-attested Epaphus line.', 'Local Byzantine tradition collected under Zeus’s family in Byzantium', { attestation: 'later local foundation tradition', evidenceLevel: 'regional report' }],
]);
rows('zeus-regional', 'hero', ['zeus', 'hora-scythian'], 'zeus-index', [
  ['colaxes', 'Colaxes', 'Valerius Flaccus gives this northern warrior a Zeus–Hora pedigree. Herodotus’s Colaxais occupies a different generational position; these are not silently collapsed.', 'Valerius Flaccus, Argonautica 6.48–59'],
]);
rows('zeus-regional', 'hero', ['zeus', 'borysthenes-daughter'], 'zeus-index', [
  ['targitaus', 'Targitaus', 'Herodotus reports a Scythian claim that their first man was born from Zeus and the river Borysthenes’s daughter. The Greek names mediate a reported foreign tradition.', 'Herodotus 4.5.1–2'],
]);
rows('zeus-regional', 'hero', ['zeus', 'libyan-nymph'], 'zeus-index', [
  ['iarbas', 'Iarbas', 'Virgil makes the African king a son of Jupiter Ammon and a Garamantian nymph. His Roman epic setting and Ammon identification remain explicit.', 'Virgil, Aeneid 4.198–218', { attestation: 'Roman epic / Jupiter Ammon' }],
]);
rows('zeus-regional', 'hero', ['zeus', 'lamia-libya'], 'zeus-index', [
  ['herophile', 'Herophile (Libyan Sibyl)', 'Pausanias reports a Sibyl descended from Zeus and Lamia, daughter of Poseidon. The chapter discusses several Sibyl traditions rather than one universal prophetic biography.', 'Pausanias 10.12.1–2'],
]);
rows('zeus-regional', 'hero', ['zeus', 'lamia-acheilus'], 'zeus-index', [
  ['acheilus', 'Acheilus', 'A rarely attested son whose beauty contest with Aphrodite is reported from Ptolemy Hephaestion. The fragile, late narrative is kept distinct from well-attested archaic pedigrees.', 'Ptolemy Hephaestion, New History 6, summary in Photius', { attestation: 'imperial narrative preserved in Byzantine summary' }],
]);
rows('zeus-regional', 'hero', ['zeus', 'gaia'], 'zeus-index', [
  ['manes', 'Manes', 'A Lydian ancestral figure assigned to Zeus and Earth in the reported genealogy. His placement represents an origin tradition, not a historical royal birth record.', 'Dionysius of Halicarnassus, Roman Antiquities 1.27.1'],
]);
rows('zeus-variants', 'hero', ['zeus', 'lysithoe'], 'zeus-index', [
  ['heracles-lysithoe', 'Heracles (Lysithoe tradition)', 'Cicero’s catalogue distinguishes several figures called Hercules, including a Jupiter–Lysithoe son. This is not an extra mother casually attached to Alcmene’s Heracles.', 'Cicero, On the Nature of the Gods 3.16.42'],
]);
rows('zeus-variants', 'historical', ['zeus', 'olympias'], 'zeus-index', [
  ['alexander-legend', 'Alexander (divine-birth legend)', 'Alexander was a historical Macedonian king whose parents were Philip II and Olympias. Claims of divine descent belong to royal legend and reception, not verified biological history.', 'Plutarch, Alexander 2–3 and 27; divine-parentage tradition', { mortality: 'mortal', evidenceLevel: 'historical person with mythic claim', groups: ['zeus-variants'] }],
]);
variant('alexander-legend', ['philip-ii', 'olympias'], 'Historical dynastic parentage; this is a different evidentiary category from a god’s mythic family.', 'zeus-index', 'Plutarch, Alexander 2–3');

// Poseidon's descendants: local kings, divine beings and literary genealogies.
rows('poseidon-family', 'deity', ['poseidon', 'amphitrite'], 'theogony', [
  ['triton', 'Triton', 'Hesiod places Triton in a golden home under the sea with Poseidon and Amphitrite. The individual divine son precedes the widespread artistic use of plural tritons as sea creatures. His family belongs to the sea’s ruling household.', '930–933'],
]);
rows('poseidon-family', 'nymph', ['poseidon', 'amphitrite'], 'apollodorus3', [
  ['benthesicyme', 'Benthesicyme', 'Poseidon entrusts the infant Eumolpus to this daughter in Ethiopia. Her role connects a divine sea family with the complicated migration and upbringing of a future Eleusinian ally; she is a foster parent in that story, not Eumolpus’s biological mother.', '3.15.4'],
]);
rows('poseidon-family', 'deity', ['poseidon'], 'theogony', [
  ['cymopoleia', 'Cymopoleia', 'Poseidon gives Cymopoleia in marriage to Briareus after the Hundred-Hander has helped establish Zeus’s order. The short notice supplies a father and a marriage, but no mother or extended biography. The alliance joins an elder defender to the Olympian sea household.', '817–819'],
]);
rows('poseidon-family', 'deity', ['poseidon', 'demeter'], 'pausanias8', [
  ['despoina', 'Despoina', 'Pausanias reports an Arcadian tradition in which Poseidon pursues and rapes Demeter despite her attempt to escape as a mare. Their daughter is publicly called Despoina, “Mistress”; her personal name was reserved for initiates. She is distinguished from Persephone in this account.', '8.25.4–10', { groups: ['poseidon-family', 'demeter-family'], birthNote: 'Conceived through sexual violence in the cited Arcadian tradition.' }],
  ['arion', 'Arion', 'The extraordinary horse belongs to the same Arcadian story of Poseidon’s assault on Demeter. Pausanias discusses poetic testimony about its ownership and divine descent. Arion is an individual supernatural horse, not a human demigod; the pedigree is a regional account.', '8.25.4–10'],
]);
rows('poseidon-family', 'creature', ['poseidon', 'medusa'], 'theogony', [
  ['pegasus', 'Pegasus', 'Pegasus and Chrysaor emerge when Perseus cuts off Medusa’s head. Hesiod later places the winged horse with Zeus, carrying thunder and lightning. Its divine ancestry should not obscure Medusa’s death, or import Ovid’s later version of her story into Hesiod.', '278–286', { mortality: 'immortal' }],
  ['chrysaor', 'Chrysaor', 'Born alongside Pegasus at Medusa’s beheading, Chrysaor becomes father of the three-headed Geryon by the Oceanid Callirhoe. Hesiod does not supply the extensive personal adventures given to Perseus or Bellerophon; his importance here is the link between two generations of extraordinary beings.', '280–294'],
]);
rows('poseidon-family', 'hero', ['poseidon', 'tyro'], 'apollodorus1', [
  ['pelias', 'Pelias', 'Poseidon approaches Tyro in the likeness of the river Enipeus. Their son Pelias becomes ruler at Iolcus and sends Jason after the golden fleece. His divine paternity does not prevent his death through Medea’s deception of his daughters.', '1.9.8; 1.9.16; 1.9.27'],
  ['neleus', 'Neleus', 'Twin brother of Pelias, Neleus becomes associated with Pylos and the family of Nestor. Heracles kills him and most of his sons in the mythographic account. The twins’ shared birth branches into different regional dynasties and conflicting heroic alliances.', '1.9.8–9; 2.7.3'],
]);
rows('poseidon-family', 'hero', ['poseidon', 'iphimedeia'], 'apollodorus1', [
  ['otus', 'Otus', 'One of the gigantic Aloadae, who threaten heaven by piling mountains together. Otus and Ephialtes die after Artemis frustrates them. Their name links them to Aloeus’s household, while the account assigns their biological fatherhood to Poseidon.', '1.7.4', { mortality: 'mortal' }],
  ['ephialtes-aloadae', 'Ephialtes (Aload)', 'With Otus, Ephialtes grows to immense size and challenges the gods. This is the Aload brother, not a similarly named Giant in the Gigantomachy. The distinction prevents separate battles and genealogies from merging around one familiar name.', '1.7.4', { mortality: 'mortal' }],
]);
rows('poseidon-family', 'hero', ['poseidon', 'canace'], 'apollodorus1', [
  ['hopleus', 'Hopleus', 'One of the five named sons of Canace and Poseidon. The Library preserves him chiefly as a genealogical link; this atlas does not invent an individual adventure to fill the silence.', '1.7.4'],
  ['nireus-canace', 'Nireus (Canace’s son)', 'Named among Canace’s five sons by Poseidon. He is distinguished here from the beautiful Nireus who leads a contingent from Syme in the Iliad.', '1.7.4'],
  ['epopeus-canace', 'Epopeus (Canace’s son)', 'The Library lists Epopeus with Hopleus, Nireus, Aloeus and Triopas as Canace’s sons. Other regional Epopeus traditions need their own attribution rather than automatic consolidation.', '1.7.4'],
  ['aloeus', 'Aloeus', 'A son of Poseidon and Canace, and husband of Iphimedeia. The gigantic Otus and Ephialtes carry the name Aloadae from his household even where Poseidon is identified as their father.', '1.7.4'],
  ['triopas-canace', 'Triopas (Canace tradition)', 'A member of the five-son genealogy assigned to Canace and Poseidon. Several regional figures bear the name Triopas; the selected parentage is a source-specific identification.', '1.7.4'],
]);
rows('poseidon-family', 'hero', ['poseidon', 'libya'], 'apollodorus2', [
  ['agenor', 'Agenor', 'Agenor’s family links the mythic geography of Egypt, Phoenicia and Greece. In the Library he and Belus are sons of Libya and Poseidon. The search for his daughter Europa disperses the next generation, including Cadmus, into foundation stories.', '2.1.4; 3.1.1'],
  ['belus', 'Belus', 'Brother of Agenor in the Library’s genealogy, Belus fathers Aegyptus and Danaus by Anchinoe. This mythical family explains relationships among regions through ancestry and conflict; it is not a historical account of Egyptian or Greek population origins.', '2.1.4'],
]);
rows('poseidon-family', 'hero', ['poseidon', 'alcyone-pleiad'], 'apollodorus3', [
  ['hyrieus', 'Hyrieus', 'Alcyone’s son Hyrieus belongs to the Boeotian genealogy connected with Hyria. In another birth narrative he receives Orion after hosting three gods. This hosting role is distinct from Orion’s alternative biological pedigree through Poseidon and Euryale.', '3.10.1'],
  ['hyperenor', 'Hyperenor', 'A son of Poseidon and the Pleiad Alcyone in the Library’s list. The entry preserves his place in that family without treating every similarly named local founder as the same person.', '3.10.1'],
  ['aethusa', 'Aethusa', 'The daughter named with Hyrieus and Hyperenor in Alcyone’s family. Pausanias later records her as mother of Eleuther by Apollo, linking a Pleiad genealogy to Boeotian musical and civic traditions.', '3.10.1', { category: 'nymph', mortality: 'unspecified', groups: ['poseidon-family', 'apollo-family'] }],
]);
rows('poseidon-family', 'hero', ['poseidon', 'celaeno-pleiad'], 'apollodorus3', [
  ['lycus-pleiad', 'Lycus (Celaeno’s son)', 'Poseidon makes this Lycus dwell in the Isles of the Blessed. His mother is the Pleiad Celaeno; other figures named Lycus, and another Celaeno who is daughter of Ergeus, belong to different genealogical accounts.', '3.10.1'],
]);
rows('poseidon-family', 'hero', ['poseidon', 'aethra'], 'apollodorus3', [
  ['theseus', 'Theseus', 'Theseus’s parentage can be assigned to Poseidon or to the Athenian king Aegeus, who visits Aethra on the same night in the Library. The hero’s divine and royal affiliations support different relationships; they are not two additional fathers in an ordinary biological family.', '3.15.7; 3.16.1'],
]);
variant('theseus', ['aegeus', 'aethra'], 'Aegeus is the competing human father in the double-conception narrative.', 'apollodorus3', '3.15.7; 3.16.1');
rows('poseidon-family', 'hero', ['poseidon', 'amymone'], 'apollodorus2', [
  ['nauplius', 'Nauplius', 'Amymone and Poseidon produce Nauplius in the Danaid stories. Ancient genealogy can stretch the same name across the foundation of Nauplia and later heroic generations; this entry retains the explicitly stated pedigree without claiming a secure historical chronology.', '2.1.5'],
]);
rows('poseidon-family', 'hero', ['poseidon', 'gaia'], 'apollodorus2', [
  ['antaeus', 'Antaeus', 'A Libyan opponent of Heracles, Antaeus gains strength through contact with Earth. Heracles defeats him by lifting him away from the ground. His genealogy makes the physical source of that strength a family relationship rather than a separate magic object.', '2.5.11'],
]);
rows('poseidon-family', 'hero', ['poseidon', 'chione-boreas'], 'apollodorus3', [
  ['eumolpus', 'Eumolpus', 'Exposed at sea by Chione, Eumolpus is rescued by Poseidon and raised in Benthesicyme’s household. His later movements connect Ethiopia, Thrace and Eleusis. His mythical ancestry and cult associations should not be mistaken for evidence of an actual individual’s migration.', '3.15.4–5'],
]);
rows('poseidon-family', 'hero', ['poseidon', 'salamis'], 'apollodorus3', [
  ['cychreus', 'Cychreus', 'The Salaminian hero gives a divine sea lineage to an island’s royal past. The Library makes him son of Poseidon and Salamis and connects his succession with Telamon; local snake traditions supply a further cultic identity.', '3.12.7'],
]);
rows('poseidon-family', 'hero', ['poseidon', 'euryale-orion'], 'apollodorus1', [
  ['orion', 'Orion', 'The Library gives Orion the power to walk across the sea and names Poseidon and Euryale as parents. Other accounts explain his birth through an earth-generated gift from three visiting gods. Neither version supplies a single standard biography across Greek and Roman texts.', '1.4.3'],
]);
variant('orion', ['zeus', 'poseidon', 'hermes'], 'Ovid’s collective creation story follows the gods’ visit to Hyrieus; these are generative contributors, not a conventional three-parent union.', 'fasti5', '5.493–544');
rows('poseidon-family', 'hero', ['poseidon', 'pitane'], 'poseidon-index', [
  ['evadne', 'Evadne', 'Pindar makes Evadne a daughter of Pitane and Poseidon, brought up in Aepytus’s household. She bears Apollo’s prophetic son Iamus. The ode uses this family to explain an inherited divinatory office, not simply a list of divine romances.', 'Pindar, Olympian 6.28–70', { groups: ['poseidon-family', 'apollo-family'] }],
]);
rows('poseidon-family', 'hero', ['poseidon', 'eurynome-bellerophon'], 'catalogue', [
  ['bellerophon', 'Bellerophon', 'A surviving Hesiodic fragment assigns Bellerophon to Poseidon and Eurynome, while other accounts situate him as Glaucus’s son. His adventures with Pegasus, the Chimera and hostile royal commands show why genealogical rank cannot protect a hero from danger or reversal.', 'fragment 7'],
]);
variant('bellerophon', ['glaucus-sisyphus', 'eurynome-bellerophon'], 'The mortal Sisyphid genealogy competes with Poseidon’s paternity.', 'apollodorus2', '2.3.1');
rows('poseidon-family', 'hero', ['poseidon', 'thoosa'], 'poseidon-index', [
  ['polyphemus', 'Polyphemus', 'The Odyssey identifies the Cyclops as Poseidon’s son by Thoosa. His blinding brings a personal grievance into Odysseus’s conflict with the sea god. He is not one of the three primordial Cyclopes who forge Zeus’s thunderbolt.', 'Homer, Odyssey 1.68–73; 9.105–566', { category: 'creature', mortality: 'mortal' }],
]);
rows('poseidon-family', 'hero', ['poseidon', 'periboea-giant'], 'poseidon-index', [
  ['nausithous', 'Nausithous', 'The Odyssey makes Nausithous son of Poseidon and Periboea, daughter of a king of Giants. He leads the Phaeacians away from their former home. The pedigree connects a hospitable seafaring people with both the sea god and a threatening earlier world.', 'Homer, Odyssey 6.4–12; 7.54–66'],
]);
rows('poseidon-family', 'hero', ['poseidon', 'alope'], 'poseidon-index', [
  ['hippothoon', 'Hippothoon', 'Pausanias records Hippothoon as Poseidon’s son by Alope. His importance includes a place among the Athenian tribal eponyms; a civic genealogy can therefore remain significant even when an individual has comparatively little surviving epic narrative.', 'Pausanias 1.5.2; 1.39.3'],
]);
rows('poseidon-family', 'hero', ['poseidon', 'europa-tityus'], 'poseidon-index', [
  ['euphemus', 'Euphemus', 'The Argonaut Euphemus is linked to future settlement in Pindar’s fourth Pythian ode. His mother Europa is a daughter of Tityus, distinct from Zeus’s Phoenician partner Europa. Repeated names otherwise make these two divine families easy to confuse.', 'Pindar, Pythian 4.19–56'],
]);
rows('poseidon-family', 'hero', ['poseidon', 'molione'], 'apollodorus2', [
  ['cteatus', 'Cteatus', 'Cteatus and Eurytus are the Molionid opponents who help defend Elis against Heracles. The Library records their extraordinary joined form and attributes their fatherhood to Poseidon while also identifying them with Actor’s household.', '2.7.2'],
  ['eurytus-molionid', 'Eurytus (Molionid)', 'Cteatus’s brother belongs to the same paired Molionid tradition. He is distinguished from the archer Eurytus of Oechalia, whose conflict with Heracles follows a different genealogy and sequence of events.', '2.7.2'],
]);
for (const key of ['cteatus', 'eurytus-molionid']) variant(key, ['actor-elian', 'molione'], 'Human household parentage, against the divine fatherhood also reported.', 'apollodorus2', '2.7.2');
rows('poseidon-family', 'hero', ['poseidon', 'melia-bebrycian'], 'apollodorus1', [
  ['amycus', 'Amycus', 'The Bebrycian king compels strangers to box with him until Polydeuces defeats him. The Library calls him son of Poseidon and the nymph Melia. Divine parentage here belongs to an adversary, not to a model of hospitality or justice.', '1.9.20'],
]);
rows('poseidon-family', 'nymph', ['poseidon', 'amphitrite'], 'apollodorus1', [
  ['rhode', 'Rhode', 'The island figure Rhode is associated with Helios and the ancestry of Rhodes. Her mother varies between Amphitrite, Halia and Aphrodite in different texts. The separate source paths are more informative than placing all three mothers into one combined household.', '1.4.5'],
]);
variant('rhode', ['poseidon', 'halia'], 'Diodorus’s Rhodian account names Halia.', 'diodorus5', '5.55.1–4');
variant('rhode', ['poseidon', 'aphrodite'], 'Pindar’s island genealogy associates Rhode with Aphrodite.', 'poseidon-index', 'Pindar, Olympian 7.13–14');
rows('poseidon-family', 'hero', ['poseidon', 'ceroessa'], 'zeus-index', [
  ['byzas', 'Byzas', 'A founder of Byzantion in a late-antique local history, Byzas is made son of Poseidon and Io’s daughter Ceroessa. The family links the city to the Bosporus journey of Io; other foundation accounts supply different founders and origins.', 'Hesychius of Miletus, Patria of Constantinople 5–12'],
]);
// Plato names all ten sons. These are characters of his Atlantis narrative.
for (const [key, name, order] of [
  ['atlas-atlantis', 'Atlas (Atlantis)', 'eldest, ruling the central kingdom'], ['gadeirus', 'Gadeirus / Eumelus', 'Atlas’s twin'],
  ['ampheres', 'Ampheres', 'one of the second pair'], ['evaemon', 'Evaemon', 'one of the second pair'],
  ['mneseus', 'Mneseus', 'one of the third pair'], ['autochthon', 'Autochthon', 'one of the third pair'],
  ['elasippus', 'Elasippus', 'one of the fourth pair'], ['mestor-atlantis', 'Mestor (Atlantis)', 'one of the fourth pair'],
  ['azaes', 'Azaes', 'one of the fifth pair'], ['diaprepes', 'Diaprepes', 'one of the fifth pair'],
]) person(key, name, 'hero', `Plato names ${name} as ${order} among the ten sons of Poseidon and Cleito. The tenfold family organizes his imagined island’s kingship. It belongs to the Critias narrative, not an independently established history of a lost civilization.`, ['poseidon', 'cleito'], refs('poseidon-index', 'Plato, Critias 113c–114c'), { groups: ['poseidon-family', 'atlantis'], attestation: 'Plato’s philosophical narrative' });

// Apollo: prophecy, medicine, music and numerous local founder genealogies.
rows('apollo-family', 'hero', ['apollo', 'coronis'], 'apollodorus3', [
  ['asclepius', 'Asclepius', 'A gifted healer trained by Chiron, Asclepius crosses a boundary when he restores the dead and is struck by Zeus. The Library names Coronis but also preserves a Messenian claim for Arsinoe. Later worship honors him as a healing god.', '3.10.3–4', { mortality: 'mortal-then-divine' }],
]);
variant('asclepius', ['apollo', 'arsinoe-messenia'], 'A competing Messenian mother, explicitly reported in the Library.', 'apollodorus3', '3.10.3');
rows('apollo-family', 'deity', ['apollo', 'cyrene'], 'apollo-index', [
  ['aristaeus', 'Aristaeus', 'Pindar links Cyrene’s encounter with Apollo to the Libyan city and the birth of Aristaeus. Later traditions connect him with pastoral skills and beekeeping. His mother’s hunting identity and the poem’s civic setting matter as much as the father’s divine name.', 'Pindar, Pythian 9.1–70'],
]);
rows('apollo-family', 'hero', ['apollo', 'creusa'], 'apollo-index', [
  ['ion', 'Ion', 'Euripides’s Ion makes the foundling at Delphi Apollo’s son by Creusa, who recounts sexual violence and abandonment. Recognition reunites mother and son while an announced public story assigns fatherhood differently. The drama exposes the human cost behind a civic genealogy.', 'Euripides, Ion 8–26, 859–922, 1553–1622', { birthNote: 'Creusa describes Apollo’s coercion; not presented as a consensual romance.' }],
]);
rows('apollo-family', 'hero', ['apollo', 'evadne'], 'apollo-index', [
  ['iamus', 'Iamus', 'Pindar tells how the exposed infant Iamus survives, is named in connection with violets, and receives a prophetic role from Apollo. His descendants, the Iamidae, ground their divinatory authority in this descent. The story links family, landscape and ritual office.', 'Pindar, Olympian 6.35–75'],
]);
rows('apollo-family', 'hero', ['oeagrus', 'calliope'], 'apollodorus1', [
  ['orpheus', 'Orpheus', 'The musician’s descent joins a Thracian father to the Muse Calliope in one Library account; Apollo is another proposed father. His attempt to retrieve his wife from the dead makes music powerful but unable to erase every boundary imposed by mortality.', '1.3.2'],
]);
variant('orpheus', ['apollo', 'calliope'], 'Apollo is offered as an alternative to Oeagrus.', 'apollodorus1', '1.3.2');
rows('apollo-family', 'hero', ['apollo', 'calliope'], 'apollodorus1', [
  ['linus-musician', 'Linus (musician)', 'The Library assigns the musical Linus to Apollo and Calliope, while other testimonia name Urania. His genealogy is distinct from the Argive infant Linus, son of Psamathe. Shared names do not require combining their different deaths and local stories.', '1.3.2'],
]);
variant('linus-musician', ['apollo', 'urania-muse'], 'A competing Muse mother in the mythographic list.', 'apollo-index', 'Hyginus, Fabulae 159');
rows('apollo-family', 'hero', ['apollo', 'psamathe-argive'], 'apollo-index', [
  ['linus-argive', 'Linus (Argive infant)', 'Pausanias recounts an Argive child of Apollo and Psamathe whose exposure and death lead to suffering and ritual remembrance. This local infant belongs to a separate narrative from the adult musician and teacher also called Linus.', 'Pausanias 1.43.7–8'],
]);
rows('apollo-family', 'hero', ['apollo', 'phthia'], 'apollodorus1', [
  ['dorus-apollo', 'Dorus (Apollo’s son)', 'One of the three sons of Phthia and Apollo killed by Aetolus after welcoming him. He is distinguished from Dorus the son of Hellen, an ancestor used in other accounts of Greek-speaking peoples.', '1.7.6'],
  ['laodocus', 'Laodocus', 'Named with Dorus and Polypoetes among Phthia’s sons by Apollo. Their killing precedes Aetolus’s naming of Aetolia in the Library; the sparse record preserves a violent displacement within a foundation genealogy.', '1.7.6'],
  ['polypoetes-phthia', 'Polypoetes (Phthia’s son)', 'One of Apollo and Phthia’s three sons in the Aetolian story. He is not the later Lapith commander Polypoetes, son of Pirithous, who appears in the Trojan expedition.', '1.7.6'],
]);
rows('apollo-family', 'hero', ['apollo', 'rhoeo'], 'diodorus5', [
  ['anius', 'Anius', 'Rhoeo’s father exposes her in a chest after her pregnancy; she reaches Delos and places the infant before Apollo’s altar. Anius later becomes a Delian ruler and priest. The tale ties survival, paternity and the sanctuary’s authority together.', '5.62.1–2'],
]);
rows('apollo-family', 'hero', ['apollo', 'aria-miletus'], 'apollodorus3', [
  ['miletus', 'Miletus', 'The Library makes Miletus son of Apollo and Aria and connects his departure from Crete with the foundation of the Anatolian city. Different sources give different maternal names; a city-founder pedigree should be read as a localized story rather than a uniform personal record.', '3.1.2'],
]);
rows('apollo-family', 'hero', ['apollo', 'manto'], 'apollodorus-e', [
  ['mopsus-apollo', 'Mopsus (Apollo’s son)', 'Mopsus, the son of Apollo and the prophetess Manto, wins a contest of divination against Calchas after the Trojan War. He is distinct from the Argonaut seer Mopsus son of Ampyx, despite their shared name and profession.', 'Epitome 6.3–4'],
]);
rows('apollo-family', 'hero', ['priam', 'hecuba'], 'apollodorus3', [
  ['troilus', 'Troilus', 'Troilus belongs to Priam and Hecuba’s Trojan family, but the Library also reports Apollo as his father. The alternative divine parentage does not make him immortal or save him from Achilles in traditions about events beyond the Iliad’s narrated action.', '3.12.5'],
]);
variant('troilus', ['apollo', 'hecuba'], 'Divine paternity is expressly presented as an alternative.', 'apollodorus3', '3.12.5');
rows('apollo-family', 'hero', ['apollo', 'chione-daedalion'], 'apollo-index', [
  ['philammon', 'Philammon', 'Ovid links the musician Philammon and the thief Autolycus through their mother Chione, assigning Apollo and Mercury different fatherhoods. The episode distinguishes gifts of music from cunning without making the two children sons of both gods.', 'Ovid, Metamorphoses 11.291–317'],
]);
rows('apollo-family', 'hero', ['apollo', 'corycia'], 'apollo-index', [
  ['lycoras', 'Lycoras', 'A local Delphic genealogy makes Lycoras the son of Apollo and the nymph Corycia, connecting his name with Lycoreia and hers with the Corycian cave. Pausanias reports this among several explanations of the region’s early names.', 'Pausanias 10.6.3'],
]);
rows('apollo-family', 'hero', ['apollo', 'celaeno-delphi'], 'apollo-index', [
  ['delphus', 'Delphus', 'The eponym of Delphi has competing maternal genealogies in Pausanias: Celaeno, Thyia or Melaena. This Celaeno is a local descendant of Lycoras, not the Pleiad. The alternatives show how neighboring sacred places competed for a role in Apollo’s landscape.', 'Pausanias 10.6.3–4'],
]);
variant('delphus', ['apollo', 'thyia-delphi'], 'Pausanias records Thyia as another local mother.', 'apollo-index', 'Pausanias 10.6.4');
variant('delphus', ['apollo', 'melaena'], 'Pausanias also records the river daughter Melaena.', 'apollo-index', 'Pausanias 10.6.4');
rows('apollo-family', 'hero', ['apollo', 'aethusa'], 'apollo-index', [
  ['eleuther', 'Eleuther', 'A son of Apollo and Aethusa in Tanagran tradition, Eleuther is also remembered for a victory through his singing voice. Pausanias distinguishes the quality of his performance from authorship of the song, preserving a useful detail about ancient musical reputation.', 'Pausanias 9.20.1; 10.7.2'],
]);
rows('apollo-family', 'hero', ['apollo', 'stilbe'], 'apollo-index', [
  ['lapithes', 'Lapithes', 'Diodorus places Lapithes and Centaurus among Apollo’s sons by Stilbe. Their names explain two related yet opposed peoples within a genealogical scheme. This account should not replace the separate Ixion story often used for the origin of the Centaurs.', 'Diodorus Siculus 4.69.1'],
  ['centaurus-stilbe', 'Centaurus (Stilbe tradition)', 'This Centaurus is brother of Lapithes and son of Apollo and Stilbe in Diodorus. He is kept separate from the figure generated through Ixion and the cloud-image of Hera in another account of Centaur ancestry.', 'Diodorus Siculus 4.69.1'],
]);
rows('apollo-family', 'hero', ['apollo'], 'apollo-index', [
  ['trophonius', 'Trophonius', 'Pausanias favors Apollo over Erginus as father of the renowned builder and oracular figure. His consultation of the oracle influences that judgment. The passage itself shows a later observer choosing between genealogies rather than reporting an uncontested family fact.', 'Pausanias 9.37.5'],
]);

rows('ares-family', 'deity', ['ares', 'aphrodite'], 'theogony', [
  ['phobos', 'Phobos', 'Fear is both a battlefield experience and a divine figure born to Ares and Aphrodite. Hesiod places Phobos beside Deimos as powers who drive warriors into confusion. Genealogy turns the emotional conditions of battle into members of its god’s household.', '933–937'],
  ['deimos', 'Deimos', 'Terror accompanies Fear in the martial family of Ares and Aphrodite. Neither figure requires an independent royal biography to be significant: their presence explains what battle feels like to those subjected to it, alongside the weapons and actions of heroic combat.', '933–937'],
  ['harmonia', 'Harmonia', 'Harmonia marries Cadmus and connects the divine family of Ares and Aphrodite to Thebes. Her name suggests concord, yet the descendants experience severe conflict. A Samothracian account instead makes her a daughter of Zeus and Electra, a separately attributed genealogy.', '933–937; 975–978', { groups: ['ares-family', 'aphrodite-family', 'zeus-variants'] }],
]);
variant('harmonia', ['zeus', 'electra-pleiad'], 'Diodorus reports the Samothracian alternative.', 'diodorus5', '5.48.2–3');
rows('ares-family', 'deity', ['ares', 'aphrodite'], 'ares-index', [
  ['anteros', 'Anteros', 'Cicero’s catalogue of divine genealogies assigns Anteros to Mars and Venus. The name concerns love returned or opposed, but this brief parentage does not warrant a single fixed ancient biography. Roman names in the witness remain part of its attribution.', 'Cicero, On the Nature of the Gods 3.23.59'],
]);
rows('ares-family', 'hero', ['ares', 'aglaurus'], 'apollodorus3', [
  ['alcippe', 'Alcippe', 'Ares kills Halirrhothius when he tries to rape Alcippe, his daughter by Aglaurus. The ensuing trial provides a mythic explanation of the Areopagus. Alcippe’s place in the story concerns an attempted assault and its aftermath, not a divine romance.', '3.14.2'],
]);
rows('ares-family', 'hero', ['ares', 'sterope-pleiad'], 'apollodorus3', [
  ['oenomaus', 'Oenomaus', 'The king challenges his daughter’s suitors to fatal chariot races until Pelops prevails. The Library names Sterope as his mother by Ares; a rival maternal line names Harpina. His divine ancestry intensifies rather than resolves a story of violence and succession.', '3.10.1'],
]);
variant('oenomaus', ['ares', 'harpina'], 'An Elean tradition identifies Harpina.', 'ares-index', 'Pausanias 5.22.6');
rows('ares-family', 'hero', ['ares', 'astyoche'], 'ares-index', [
  ['ascalaphus', 'Ascalaphus', 'Ascalaphus and Ialmenus lead warriors from Orchomenus in the Iliad’s Catalogue of Ships. Ares fathers them with Astyoche. This Ascalaphus is the warrior, distinct from the underworld witness connected with Persephone’s pomegranate.', 'Homer, Iliad 2.511–516; 13.518–525'],
  ['ialmenus', 'Ialmenus', 'Brother of Ascalaphus and a commander in the Trojan expedition, Ialmenus belongs to the mortal heroic branch of Ares’s family. His role in the Catalogue of Ships connects divine ancestry with the geography of the Greek forces.', 'Homer, Iliad 2.511–516'],
]);
rows('ares-family', 'hero', ['ares', 'pelopia'], 'apollodorus2', [
  ['cycnus-ares', 'Cycnus (Ares’s son)', 'The Library names Pelopia as mother of the Cycnus killed by Heracles near the river Echedorus in this passage. The Hesiodic Shield develops a combat against a son of Ares at a different narrative setting; same-name accounts should not be forced into identical itineraries.', '2.7.7', { sourceRefs: [r('apollodorus2', '2.7.7'), r('shield', '57–138; 321–480')] }],
]);
rows('ares-family', 'hero', ['ares', 'cyrene-thrace'], 'apollodorus2', [
  ['diomedes-thrace', 'Diomedes (Thracian king)', 'This son of Ares owns the man-eating mares captured by Heracles. He is distinct from the Argive Diomedes son of Tydeus who fights at Troy. Keeping separate identities matters when the same heroic name appears on opposing sides of unrelated stories.', '2.5.8'],
]);
rows('ares-family', 'hero', ['ares', 'otrera'], 'apollodorus2', [
  ['hippolyta', 'Hippolyta', 'The Amazon queen possesses the belt Heracles seeks. The Library initially describes her willingness to give it before Hera provokes conflict. Her genealogy belongs to a society of warrior women; it does not justify treating every named Amazon as her daughter or sister.', '2.5.9'],
]);
rows('ares-family', 'hero', ['ares', 'otrera'], 'apollodorus-e', [
  ['penthesilea', 'Penthesilea', 'The Amazon arrives to aid Troy after Hector’s death and is killed by Achilles. This action belongs to the wider Trojan cycle, beyond the Iliad’s endpoint at Hector’s funeral. Her parentage is shared with Hippolyta in the selected mythographic tradition.', 'Epitome 5.1'],
]);
rows('ares-family', 'hero', ['ares', 'chryse'], 'ares-index', [
  ['phlegyas', 'Phlegyas', 'Pausanias gives Phlegyas a divine father and makes his name central to an aggressive people near Orchomenus. Other texts connect him with Coronis’s family. The regional genealogy should not erase differences among accounts of his violence or Apollo’s punishment.', 'Pausanias 9.36.1'],
]);
rows('ares-family', 'hero', ['oeneus', 'althaea'], 'apollodorus1', [
  ['meleager', 'Meleager', 'The Calydonian boar hunt brings Meleager into a fatal conflict with maternal relatives. Oeneus is the usual father in the selected account, with Ares offered as an alternative. His life depends on family decisions and a burning brand despite the possible divine pedigree.', '1.8.2–3'],
]);
variant('meleager', ['ares', 'althaea'], 'The Library records Ares as an alternative father.', 'apollodorus1', '1.8.2');
rows('ares-family', 'hero', ['ares', 'demonice'], 'apollodorus1', [
  ['evenus', 'Evenus', 'Named among Demonice’s sons by Ares, Evenus later pursues Idas after Marpessa’s removal. His death and the river bearing his name turn a family conflict into a local landscape story.', '1.7.7–8'],
  ['molus-ares', 'Molus (Demonice’s son)', 'The Library names Molus with Evenus, Pylus and Thestius as a son of Ares and Demonice. The source preserves a genealogical place rather than an extensive individual narrative.', '1.7.7'],
  ['pylus', 'Pylus', 'One of the four sons in Demonice and Ares’s Aetolian genealogy. His presence is retained without inventing a detailed adventure or assuming every place called Pylos must derive from this one figure.', '1.7.7'],
  ['thestius', 'Thestius', 'Thestius connects Ares’s family to Leda, Althaea and other members of the Aetolian-Spartan genealogical network. The Library’s descent through Demonice is one selected line, not a universal solution to every local Thestius tradition.', '1.7.7; 1.8.1; 3.10.5'],
]);
rows('ares-family', 'hero', ['ares'], 'apollodorus3', [
  ['tereus', 'Tereus', 'The Thracian king’s marriage to Procne is followed by his rape of Philomela and mutilation intended to silence her. The story ends in revenge and transformation. His descent from Ares does not make the violence admirable or consensual.', '3.14.8'],
]);

rows('hermes-family', 'deity', ['hermes', 'dryops-daughter'], 'hymns', [
  ['pan', 'Pan', 'The Homeric Hymn gives Pan a mother identified as a daughter of Dryops. Hermes carries the unusual infant to the gods, who welcome him. Later accounts offer other mothers, including Penelope; these alternatives do not automatically concern the same narrative setting.', 'Hymn 19.27–47'],
]);
variant('pan', ['hermes', 'penelope-pan'], 'A different parentage is reported in the Library’s Epitome; it belongs to a hostile alternative story about Penelope.', 'apollodorus-e', 'Epitome 7.38');
rows('hermes-family', 'deity', ['hermes', 'aphrodite'], 'hermes-index', [
  ['hermaphroditus', 'Hermaphroditus', 'Diodorus gives the deity a name joining Hermes and Aphrodite and describes combined bodily characteristics. Ovid tells a separate story of Salmacis forcing an unwanted union. That episode should not be recast as a consensual romance or a universal ancient account of gender.', 'Diodorus Siculus 4.6.5; Ovid, Metamorphoses 4.285–388', { groups: ['hermes-family', 'aphrodite-family'] }],
]);
rows('hermes-family', 'hero', ['hermes', 'chione-daedalion'], 'hermes-index', [
  ['autolycus', 'Autolycus', 'In Ovid’s account Autolycus inherits a talent for theft and deception from Mercury, while his half-brother Philammon is associated with Apollo’s music. Other traditions make him Odysseus’s maternal grandfather, placing cunning in a multigenerational heroic family.', 'Ovid, Metamorphoses 11.301–317; Homer, Odyssey 19.394–466'],
]);
rows('hermes-family', 'hero', ['hermes', 'theobule'], 'hermes-index', [
  ['myrtilus', 'Myrtilus', 'Oenomaus’s charioteer helps Pelops win the fatal race and is then killed by him. Hyginus supplies Theobule as his mother, while another version names Clytie. The betrayal links divine parentage to the curses surrounding the Pelopid house.', 'Hyginus, Fabulae 224; Astronomica 2.13'],
]);
variant('myrtilus', ['hermes', 'clytie-myrtilus'], 'Alternative mother in the astronomical account.', 'hermes-index', 'Hyginus, Astronomica 2.13');
rows('hermes-family', 'hero', ['hermes', 'herse'], 'apollodorus3', [
  ['cephalus-hermes', 'Cephalus (Hermes’s son)', 'The Library makes this Cephalus son of Hermes and Herse and connects him with Eos. He is kept distinct from the Cephalus associated with Deion, Procris and the fatal hunting accident in other genealogies.', '3.14.3'],
]);
rows('hermes-family', 'hero', ['hermes', 'aglaurus'], 'hermes-index', [
  ['ceryx', 'Ceryx', 'Pausanias records competing accounts behind the Eleusinian herald family: descent through Hermes and Aglaurus, or descent from Eumolpus. Such pedigrees authorize ritual office. They should remain visible alternatives rather than one combined biological household.', 'Pausanias 1.38.3'],
]);
variant('ceryx', ['eumolpus'], 'Competing Eleusinian paternal genealogy; the cited notice does not name a mother.', 'hermes-index', 'Pausanias 1.38.3');
rows('hermes-family', 'hero', ['hermes', 'antianeira'], 'hermes-index', [
  ['echion-hermes', 'Echion (Hermes’s son)', 'Apollonius names Echion and Eurytus among the sons of Hermes who join the Argo. This Echion is distinguished from the Theban earthborn warrior of the same name, whose genealogy belongs to the dragon’s teeth.', 'Apollonius Rhodius, Argonautica 1.51–56'],
  ['eurytus-hermes', 'Eurytus (Hermes’s son)', 'One of Antianeira’s sons by Hermes in the Argonautic roster. The keyed identity separates this voyager from the Molionid Eurytus and the Oechalian archer, preventing an accidental merger of three unrelated stories.', 'Apollonius Rhodius, Argonautica 1.51–56'],
]);
rows('hermes-family', 'hero', ['hermes', 'eupolemeia'], 'hermes-index', [
  ['aethalides', 'Aethalides', 'The Argonautic herald is Hermes’s son by Eupolemeia. Apollonius emphasizes his extraordinary memory and unusual destiny, making his connection to the messenger god functional within the voyage rather than merely a prestigious name in a list.', 'Apollonius Rhodius, Argonautica 1.51–64'],
]);
rows('hermes-family', 'hero', ['hermes', 'polymele'], 'hermes-index', [
  ['eudorus', 'Eudorus', 'The Iliad identifies Eudorus as Hermes’s son by the dancer Polymele and a commander among Achilles’s Myrmidons. His grandfather raises him after his mother’s marriage. The poem distinguishes biological descent from the household that actually rears a child.', 'Homer, Iliad 16.179–192'],
]);
rows('hermes-family', 'hero', ['hermes'], 'apollodorus2', [
  ['abderus', 'Abderus', 'Heracles leaves Abderus with Diomedes’s dangerous mares, which kill him. The Library calls the young man Hermes’s son and relates the founding of Abdera to his memory. The account gives no mother, and none is supplied here.', '2.5.8'],
]);
rows('hermes-family', 'hero', ['hermes', 'acacallis'], 'hermes-index', [
  ['cydon', 'Cydon', 'Pausanias reports a Cretan claim that Hermes and Acacallis were Cydon’s parents. The local ancestor gives his name to Cydonia. As with other city founders, alternative genealogies express a community’s affiliations rather than a single recoverable historical pedigree.', 'Pausanias 8.53.4'],
]);
rows('hermes-family', 'hero', ['hermes', 'rhene'], 'diodorus5', [
  ['saon', 'Saon', 'Diodorus explicitly offers competing parents for a Samothracian early inhabitant: Hermes and Rhene, or Zeus and an unnamed nymph. His organizing role in the island’s society belongs to local origin narrative; the alternative is preserved in the graph.', '5.48.1', { groups: ['hermes-family', 'zeus-variants'] }],
]);
variant('saon', ['zeus', 'samothracian-nymph'], 'Diodorus’s explicitly reported alternative.', 'diodorus5', '5.48.1');
rows('hermes-family', 'hero', ['hermes', 'chthonophyle'], 'hermes-index', [
  ['polybus-sicyon', 'Polybus (Sicyon)', 'The Sicyonian genealogy makes Polybus son of Hermes and Chthonophyle. Pausanias connects this line to Phlias and the local royal succession. This Polybus is distinguished from the Corinthian foster father of Oedipus.', 'Pausanias 2.6.6'],
]);
rows('hermes-family', 'hero', ['hermes', 'alcidameia'], 'hermes-index', [
  ['bounus', 'Bounus', 'Bounus receives Ephyra’s territory in the Corinthian royal narrative. Pausanias names Hermes and Alcidameia as parents. The short genealogy supports a local succession story without supplying evidence for a broader panhellenic cult or adventure cycle.', 'Pausanias 2.3.10'],
]);
rows('hermes-family', 'hero', ['hermes', 'phylodameia'], 'hermes-index', [
  ['pharis', 'Pharis', 'Pausanias identifies Pharis as a son of Hermes and Phylodameia in a Messenian place-name genealogy. The entry retains this regional ancestor as such, without turning a single local notice into a major Olympian narrative.', 'Pausanias 4.30.2'],
]);

rows('dionysus-family', 'deity', ['dionysus', 'aphrodite'], 'dionysus-index', [
  ['priapus', 'Priapus', 'Pausanias and Diodorus connect Priapus with Dionysus and Aphrodite, while later testimonia offer other fathers. Gardens, fertility and protective images are central to his reception. The Zeus alternative is explicitly marked rather than used to redefine every version of the deity.', 'Pausanias 9.31.2; Diodorus Siculus 4.6.1', { groups: ['dionysus-family', 'aphrodite-family', 'zeus-variants'] }],
]);
variant('priapus', ['zeus', 'aphrodite'], 'A later lexicographic genealogy, not the only or earliest parentage.', 'zeus-index', 'Suda, s.v. Priapos');
rows('dionysus-family', 'hero', ['dionysus', 'ariadne'], 'apollodorus-e', [
  ['oenopion', 'Oenopion', 'One of the four sons assigned to Dionysus and Ariadne in the Library’s Epitome, Oenopion is associated with Chios. His name and family encourage connections with wine culture, but his local narratives require their own sources.', 'Epitome 1.9'],
  ['staphylus', 'Staphylus', 'Staphylus belongs to Dionysus and Ariadne’s named sons in the Epitome. The grape-associated name makes him apt for wine genealogies; several regional accounts bearing that name are not assumed to form one continuous biography.', 'Epitome 1.9'],
  ['thoas-lemnos', 'Thoas (Lemnos)', 'The Lemnian king is included among Ariadne and Dionysus’s sons. His daughter Hypsipyle saves him from the killing of the island’s men in the Argonautic tradition. He is distinguished from other kings called Thoas.', 'Epitome 1.9; Library 1.9.17'],
  ['peparethus', 'Peparethus', 'Named as a son of Dionysus and Ariadne in the Epitome, Peparethus provides a family connection for an Aegean island. The surviving notice is brief; no detailed adventure is invented here to make every child’s biography equally long.', 'Epitome 1.9'],
]);
rows('dionysus-family', 'hero', ['dionysus', 'ariadne'], 'dionysus-index', [
  ['phlias', 'Phlias', 'Hyginus’s Argonaut catalogue names Phlias as a son of Liber and Ariadne, while Pausanias places him within the Sicyonian genealogy. The wine god’s family thus participates in both a collective heroic expedition and regional ancestry.', 'Hyginus, Fabulae 14; Pausanias 2.6.6'],
  ['eurymedon-dionysus', 'Eurymedon (Dionysus’s son)', 'Hyginus lists this son of Liber and Ariadne among the Argonauts. The keyed name keeps him apart from other figures called Eurymedon, including the Giant king in the Phaeacian pedigree.', 'Hyginus, Fabulae 14'],
  ['ceramus', 'Ceramus', 'Pausanias explains the name of the Athenian Kerameikos through Ceramus, son of Dionysus and Ariadne. The local eponym links an urban district to a divine family; the genealogy is an ancient explanation of a name, not modern linguistic proof.', 'Pausanias 1.3.1'],
]);
rows('dionysus-family', 'hero', ['dionysus', 'physcoa'], 'dionysus-index', [
  ['narcaeus', 'Narcaeus', 'In the Elean tradition reported by Pausanias, Narcaeus and his mother Physcoa introduce worship of Dionysus. He also gives a local Athena sanctuary its epithet. Cult foundation and regional memory carry the story beyond biological descent.', 'Pausanias 5.16.7'],
]);
rows('dionysus-family', 'hero', ['oeneus', 'althaea'], 'apollodorus1', [
  ['deianeira', 'Deianeira', 'The Library places Deianeira in Oeneus and Althaea’s household but offers Dionysus as another father. She later marries Heracles, and her attempt to preserve his love through Nessus’s supposed charm has fatal consequences. The narrative concerns deception, not a reliably effective love remedy.', '1.8.1; 2.7.5–7'],
]);
variant('deianeira', ['dionysus', 'althaea'], 'The Library expressly records an alternative divine father.', 'apollodorus1', '1.8.1');
rows('dionysus-family', 'deity', ['dionysus', 'nicaea'], 'dionysus-index', [
  ['telete', 'Telete', 'Nonnus makes Telete the child of Dionysus and Nicaea after the god rapes the intoxicated, sleeping nymph. Her ritual name and later associations must not turn that coercive birth narrative into a consensual relationship. This is a late-antique poetic genealogy.', 'Nonnus, Dionysiaca 16.244–405', { attestation: 'late-antique epic', birthNote: 'Sexual violence against an incapacitated mother.' }],
]);
rows('dionysus-family', 'deity', ['dionysus', 'aura'], 'dionysus-index', [
  ['iacchus', 'Iacchus', 'Nonnus places Iacchus in the aftermath of Dionysus’s rape of Aura and the birth of twins. Other texts relate Iacchus differently to Dionysus and Eleusinian ritual. This genealogy is one late narrative, not a universal identity for every ritual use of the name.', 'Nonnus, Dionysiaca 48.849–978', { attestation: 'late-antique epic', birthNote: 'Sexual violence in the cited narrative.' }],
]);
variant('iacchus', ['dionysus', 'aphrodite'], 'The Orphic hymn names a different pair.', 'orphic', 'Hymn 57');
rows('dionysus-family', 'deity', ['dionysus'], 'dionysus-index', [
  ['pasithea', 'Pasithea', 'Nonnus identifies Pasithea as Dionysus’s daughter and connects her with Hypnos. She should not be automatically folded into Hesiod’s named three Graces, whose parents are Zeus and Eurynome. The mother is not supplied in this selected notice.', 'Nonnus, Dionysiaca 15.87; 33.37'],
  ['hymenaeus', 'Hymenaeus', 'Seneca’s wedding chorus calls Hymenaeus a child of the thyrsus-bearing wine god. Other ancient genealogies exist for this wedding figure; this witness preserves one Roman poetic affiliation rather than a single universally accepted parentage.', 'Seneca, Medea 110–115'],
]);

rows('aphrodite-family', 'hero', ['aphrodite', 'anchises'], 'hymns', [
  ['aeneas', 'Aeneas', 'The Homeric Hymn makes Aeneas the child of Aphrodite and Anchises after she approaches the mortal in disguise. He becomes a Trojan survivor in later epic and the central ancestor of Virgil’s Roman destiny narrative. Greek and Roman stories develop different scales around the same family.', 'Hymn 5.45–291', { roman: 'Aeneas' }],
]);
rows('aphrodite-family', 'hero', ['aphrodite', 'anchises'], 'apollodorus3', [
  ['lyrus', 'Lyrus', 'The Library briefly names Lyrus as another son of Aphrodite and Anchises and says that he died childless. The notice deliberately remains brief; it does not give him Aeneas’s travels or invent a second Trojan foundation dynasty.', '3.12.2'],
]);
rows('aphrodite-family', 'hero', ['aphrodite', 'butes-argonaut'], 'aphrodite-index', [
  ['eryx', 'Eryx', 'Diodorus connects the Sicilian king and sacred mountain with Aphrodite and Butes. Eryx’s conflict with Heracles and his mother’s cult give the local genealogy wider heroic associations. This Butes is the voyager rescued from the Sirens, not every person of that name.', 'Diodorus Siculus 4.23.2'],
]);
rows('aphrodite-family', 'hero', ['aphrodite', 'phaethon-eos'], 'apollodorus3', [
  ['astynous', 'Astynous', 'The Library’s Athenian-to-Syrian genealogy names Astynous as son of Aphrodite and Phaethon. This Phaethon descends from Eos and Cephalus, unlike the more familiar solar charioteer. Keeping the two identities separate prevents a false line from Helios.', '3.14.3'],
]);
rows('aphrodite-family', 'nymph', ['aphrodite', 'adonis'], 'aphrodite-index', [
  ['beroe', 'Beroe', 'Nonnus makes Beroe the daughter of Aphrodite and Adonis and builds a city-focused myth around her. This late-antique personification of Beirut is not evidence that earlier Adonis narratives universally gave the couple a child.', 'Nonnus, Dionysiaca 41.155–180', { attestation: 'late-antique epic' }],
]);
variant('eros', ['ares', 'aphrodite'], 'A younger, parented Eros in later poetry; incompatible with treating Hesiod’s primordial Eros as chronologically identical in every account.', 'aphrodite-index', 'Nonnus, Dionysiaca 5.88–89');

rows('hephaestus-family', 'deity', ['hephaestus', 'aglaia'], 'hephaestus-index', [
  ['eucleia', 'Eucleia', 'Good Repute is one of the four daughters attributed to Hephaestus and Aglaia in the later Orphic material discussed by Proclus. The grouping joins craft to the youngest Grace, unlike narratives that give Hephaestus an unhappy marriage with Aphrodite.', 'Proclus, Commentary on Plato’s Timaeus, testimony on Hephaestus and Aglaia'],
  ['eupheme', 'Eupheme', 'A figure of favorable speech or acclamation, Eupheme belongs to the same four-daughter genealogy of Hephaestus and Aglaia. The small amount of testimony supports the named relationship, not an elaborate independent life story.', 'Proclus, Commentary on Plato’s Timaeus, testimony on Hephaestus and Aglaia'],
  ['euthenia', 'Euthenia', 'Prosperity is named among the daughters of Hephaestus and Aglaia in later philosophical testimony to an Orphic tradition. Her personified quality suits a household of accomplished making and grace; that interpretation does not replace the source’s limited factual notice.', 'Proclus, Commentary on Plato’s Timaeus, testimony on Hephaestus and Aglaia'],
  ['philophrosyne', 'Philophrosyne', 'Friendly welcome or goodwill is personified in this daughter of Hephaestus and Aglaia. She belongs to a later named quartet, not to Hesiod’s three Charites, and is retained without borrowing episodes from other similarly named personifications.', 'Proclus, Commentary on Plato’s Timaeus, testimony on Hephaestus and Aglaia'],
]);
rows('hephaestus-family', 'hero', ['hephaestus', 'gaia'], 'apollodorus3', [
  ['erichthonius', 'Erichthonius', 'The Library tells how Hephaestus attempts to rape Athena and his semen reaches Earth, from which Erichthonius is born. Athena becomes his protector and foster mother, not his biological mother. The distinction is essential to both her virginity and Athenian autochthony.', '3.14.6', { birthNote: 'Born from Earth after an attempted assault; Athena is a foster parent.' }],
]);
rows('hephaestus-family', 'hero', ['hephaestus', 'anticleia-periphetes'], 'apollodorus3', [
  ['periphetes', 'Periphetes', 'The club-bearing attacker near Epidaurus is overcome by Theseus, who takes his weapon. The Library identifies him as son of Hephaestus and Anticleia. This Anticleia is kept distinct from Odysseus’s mother of the same name.', '3.16.1'],
]);
rows('hephaestus-family', 'hero', ['hephaestus'], 'apollodorus1', [
  ['palaemonius', 'Palaemonius', 'One of the Argonauts listed by the Library as a son of Hephaestus. The roster’s brief pedigree is sufficient to place him in this branch but does not provide a mother or a full independent biography.', '1.9.16'],
]);
rows('hephaestus-family', 'deity', ['hephaestus', 'cabeiro'], 'hephaestus-index', [
  ['cadmilus', 'Cadmilus', 'Strabo preserves Acusilaus’s genealogy making Cadmilus son of Hephaestus and Cabeiro, followed by further Cabeiric generations. Other reported accounts differ. These mystery-associated figures should not be compressed into a universally agreed Olympian family.', 'Acusilaus, reported by Strabo 10.3.21'],
]);

rows('demeter-family', 'deity', ['demeter', 'iasion'], 'theogony', [
  ['plutus', 'Plutus', 'Wealth is born from Demeter and Iasion after their union in a thrice-ploughed field. The agricultural setting makes prosperity dependent on cultivated earth. Plutus is a distinct figure from Plouton, an underworld title associated with Hades.', '969–974'],
]);
rows('demeter-family', 'hero', ['demeter', 'iasion'], 'demeter-index', [
  ['philomelus', 'Philomelus', 'Hyginus places Philomelus alongside Plutus as Demeter and Iasion’s son and explains his celestial honor through inventing a ploughing arrangement. The account links subsistence work with wealth but belongs to a later astronomical mythographic tradition.', 'Hyginus, Astronomica 2.4'],
]);
rows('underworld', 'deity', ['hades'], 'macaria', [
  ['macaria-hades', 'Macaria (Hades’s daughter)', 'A brief entry in the tenth-century Suda calls Macaria a daughter of Hades. It supplies no mother or developed biography. This late lexicographic notice must not be expanded into a universal ancient family for Hades or confused with Heracles’s daughter Macaria.', 'Suda, s.v. Makaria', { attestation: 'Byzantine lexicon, tenth century CE' }],
]);

// Named cosmic and sea collectives are expanded where the selected text names members.
rows('night-underworld', 'deity', ['nyx'], 'theogony', [
  ['moros', 'Moros', 'Doom appears among Night’s unpartnered children in Hesiod’s genealogy. Its place is cosmological and personificatory; the poem does not give the figure a conventional human biography.', '211–225'],
  ['ker', 'Ker', 'A destructive doom named in Night’s family. Singular Ker and plural Keres can function differently in poetry; this entry does not manufacture individual names for an unnamed collective.', '211–225'],
  ['thanatos', 'Thanatos', 'Death is a child of Night and brother of Sleep in Hesiod. Unlike Hades, who rules a realm, Thanatos personifies death itself. The distinction avoids making every underworld function interchangeable.', '211–225; 756–766'],
  ['hypnos', 'Hypnos', 'Sleep belongs to Night’s family and shares a dwelling near Death. Hesiod contrasts their effects on living beings. The common family creates a poetic relation between experiences that are alike in appearance but radically different in consequence.', '211–225; 756–766', { roman: 'Somnus' }],
  ['momus', 'Momus', 'Blame or fault-finding is personified among Night’s children. The genealogical notice establishes a named power without supplying a continuous mythic life.', '211–225'],
  ['oizys', 'Oizys', 'Misery is named in Night’s family. The placement groups a painful human condition among cosmic powers rather than assigning it to a mortal inventor or a single moral failure.', '211–225'],
  ['nemesis', 'Nemesis', 'Nemesis stands among Night’s children in Hesiod. The figure concerns indignation and the response to overstepping; other traditions make her Helen’s mother, a separately attributed story rather than a replacement for Leda in every account.', '223–225'],
  ['apate', 'Apate', 'Deception is a named daughter of Night in this genealogy. The compact notice belongs to a sequence of personified experiences and conditions; no invented partner or child is supplied.', '224–225'],
  ['philotes', 'Philotes', 'Affection or sexual intimacy is personified among Night’s children. The word’s range matters: the entry is not automatically the same figure as Aphrodite or Eros, nor does the genealogy itself provide a romance narrative.', '224–225'],
  ['geras', 'Geras', 'Old Age belongs to Night’s family in Hesiod. Its placement expresses an unavoidable condition of mortal life, distinct from Death even though the two powers occupy neighboring conceptual territory.', '225'],
  ['eris', 'Eris', 'Strife generates a further family of painful social conditions in the Theogony. Works and Days later distinguishes harmful strife from productive rivalry. The two treatments should be read in their poetic contexts rather than reduced to a single unchanging moral label.', '225–232', { sourceRefs: [r('theogony', '225–232'), r('works', '11–26')] }],
]);
rows('night-underworld', 'collective', ['nyx'], 'theogony', [
  ['oneiroi', 'Oneiroi', 'Dreams are named collectively as Night’s children; Hesiod gives no individual names here. Later figures such as Morpheus belong to other sources and are not retrospectively inserted into this list.', '211–213'],
  ['hesperides', 'Hesperides', 'Night’s daughters guard golden apples beyond Ocean in Hesiod. Their individual names and number vary in later sources. The collective record acknowledges the group without selecting a later named list as if Hesiod had supplied it.', '215–216'],
  ['keres', 'Keres', 'The plural destructive powers are named with the Fates among Night’s children. The source names a group rather than an unlimited series of individual characters; its collective status remains explicit.', '217–222'],
]);
rows('night-underworld', 'deity', ['eris'], 'theogony', [
  ['ponos', 'Ponos', 'Toil is among Strife’s children in the Theogony. This harmful cosmological grouping differs from Works and Days’ praise of disciplined labor that sustains a household.', '226–232'],
  ['lethe', 'Lethe', 'Forgetfulness is named as Strife’s child. This personification should be distinguished from later descriptions of an underworld river bearing the same name.', '226–232'],
  ['limos', 'Limos', 'Hunger belongs to the family generated by Strife. The relationship links bodily deprivation to the poem’s wider catalogue of suffering and conflict.', '226–232'],
  ['algea', 'Algea', 'Pains are presented collectively among Strife’s offspring. No individual names are assigned in the selected lines.', '226–232', { category: 'collective' }],
  ['ate', 'Ate', 'Ruin or delusion is a child of Strife in Hesiod. The Iliad instead calls Ate Zeus’s eldest daughter in Agamemnon’s explanation of his disastrous conduct. The two genealogies remain alternatives.', '230'],
  ['horkos', 'Horkos', 'Oath is the final named child in Strife’s catalogue. Its power troubles those who swear falsely, linking family imagery to the moral and social consequences of speech.', '231–232'],
]);
variant('ate', ['zeus'], 'The Iliad’s different personified family in Agamemnon’s account.', 'zeus-index', 'Homer, Iliad 19.91–133');
rows('sea-foundations', 'deity', ['thaumas', 'electra-oceanid'], 'theogony', [
  ['iris', 'Iris', 'Iris and the Harpies share Thaumas and Electra as parents in Hesiod. Her later role as a divine messenger need not make her Hermes’s sister: function and genealogy are different kinds of relationship.', '265–269', { roman: 'Iris' }],
  ['aello', 'Aello', 'One of Hesiod’s two named Harpies, a swift winged daughter of Thaumas and Electra. The poem names Aello and Ocypete; other numbers and names belong to different testimonia.', '267–269', { category: 'creature' }],
  ['ocypete', 'Ocypete', 'The second named Harpy in Hesiod’s paired genealogy. Her identity is kept separate from later expanded Harpy lists instead of treating their varying names as additional sisters in every account.', '267–269', { category: 'creature' }],
]);
rows('sea-foundations', 'creature', ['phorcys', 'ceto'], 'theogony', [
  ['stheno', 'Stheno', 'One of the three Gorgons. Hesiod distinguishes Stheno and Euryale’s immortality from Medusa’s mortality, so “Gorgon” does not imply that all three share the same vulnerability or fate.', '270–283'],
  ['euryale-gorgon', 'Euryale (Gorgon)', 'An immortal Gorgon sister of Stheno and Medusa. She is a separate person from Euryale the mother of Orion; repeating names should not produce a false branch between the sea monsters and the hunter.', '270–283'],
  ['medusa', 'Medusa', 'The mortal Gorgon is killed by Perseus, and Pegasus and Chrysaor emerge from her severed neck. Hesiod describes Poseidon lying with her in a meadow. Ovid’s much later account of assault in Minerva’s sanctuary is a distinct telling.', '270–283', { mortality: 'mortal' }],
  ['pemphredo', 'Pemphredo', 'One of the two Graeae named by Hesiod, born grey-haired. Later texts often name three sisters, so this selected pair should not be presented as the only ancient list.', '270–273'],
  ['deino', 'Deino', 'The second Graea in Hesiod’s named pair. The familiar shared eye and tooth are developed in the Perseus narrative of the Library rather than supplied by these few Theogony lines.', '270–273'],
]);
rows('underworld', 'creature', ['gaia', 'tartarus'], 'theogony', [
  ['typhon', 'Typhon', 'Earth and Tartarus produce the final great challenger to Zeus after the Titan war in Hesiod’s sequence. Typhon’s defeat tests the new ruler’s supremacy. He belongs to a different episode from the Titanomachy and should not be inserted among the twelve Titans.', '820–885'],
]);
rows('underworld', 'creature', ['gaia', 'tartarus'], 'apollodorus2', [
  ['echidna', 'Echidna', 'The Library gives Echidna Earth and Tartarus as parents. Hesiod’s surrounding genealogy is less straightforward to resolve, so the explicit mythographic witness is used here. She becomes mother of several dangerous creatures through Typhon.', '2.1.2'],
]);
rows('underworld', 'creature', ['typhon', 'echidna'], 'theogony', [
  ['orthus', 'Orthus', 'The dog associated with Geryon is named among Echidna’s children by Typhon. His genealogy links Heracles’s western cattle labor to the wider family of extraordinary adversaries.', '306–309'],
  ['cerberus', 'Cerberus', 'Hesiod places the underworld dog in Typhon and Echidna’s family and gives him a different head count from the familiar three-headed image. Cerberus serves Hades but is not his biological son in this genealogy.', '310–312'],
  ['hydra', 'Lernaean Hydra', 'The many-headed adversary raised by Hera belongs to Typhon and Echidna’s offspring. Hera’s role is that of a fosterer and opponent of Heracles, not the Hydra’s biological mother.', '313–318'],
]);
rows('titan-descendants', 'deity', ['helios', 'perseis'], 'theogony', [
  ['circe', 'Circe', 'Hesiod names Circe with Aeetes as a child of Helios and Perseis. Her famous meeting with Odysseus belongs to the Odyssey; shared solar ancestry does not make her every enchantress in later Greek literature.', '956–962'],
  ['aeetes', 'Aeetes', 'The father of Medea is a son of Helios and Perseis. His household places the golden-fleece expedition within a divine solar dynasty and connects Medea through her mother Idya to Ocean as well.', '956–962'],
]);
rows('heroic-descendants', 'hero', ['aeetes', 'idya'], 'theogony', [
  ['medea', 'Medea', 'Hesiod identifies Medea’s divine ancestry through Aeetes and Idya and later connects her with Jason. Subsequent poets give sharply different emphases to her assistance, exile and violence. A genealogy alone cannot stand in for those distinct narratives.', '961–962; 992–1002'],
]);
rows('titan-descendants', 'deity', ['eos', 'astraeus'], 'theogony', [
  ['boreas', 'Boreas', 'The north wind belongs to Eos and Astraeus’s family. Hesiod names Boreas, Zephyrus and Notus here; adding a fourth named wind would require another witness rather than silently extending this list.', '378–382'],
  ['zephyrus', 'Zephyrus', 'The west wind is one of the three winds explicitly named as children of Dawn and Astraeus in the selected passage. Individual later stories should be distinguished from this brief cosmic genealogy.', '378–382'],
  ['notus', 'Notus', 'The south wind shares the named wind family of Eos and Astraeus. The catalogue associates patterns of weather with the sky’s generative household rather than giving each wind a human kingdom.', '378–382'],
]);

const nereidNames = [
  ['ploto','Ploto'],['eucrante','Eucrante'],['sao','Sao'],['amphitrite','Amphitrite'],['eudora','Eudora'],['thetis','Thetis'],['galene','Galene'],['glauce-nereid','Glauce'],['cymothoe','Cymothoe'],['speo','Speo'],['thoe','Thoe'],['halie','Halie'],['pasithea-nereid','Pasithea (Nereid)'],['erato-nereid','Erato (Nereid)'],['eunice','Eunice'],['melite-nereid','Melite (Nereid)'],['eulimene','Eulimene'],['agaue-nereid','Agaue (Nereid)'],['doto','Doto'],['proto','Proto'],['pherusa','Pherusa'],['dynamene','Dynamene'],['nisaea','Nisaea'],['actaea','Actaea'],['protomedea','Protomedea'],['doris-nereid','Doris (Nereid)'],['panopea','Panopea'],['galatea','Galatea'],['hippothoe','Hippothoe'],['hipponoe','Hipponoe'],['cymodoce','Cymodoce'],['cymatolege','Cymatolege'],['cymo','Cymo'],['eione','Eione'],['alimede','Alimede'],['glauconome','Glauconome'],['pontoporea','Pontoporea'],['leagore','Leagore'],['euagore','Euagore'],['laomedea','Laomedea'],['polynoe','Polynoe'],['autonoe-nereid','Autonoe (Nereid)'],['lysianassa','Lysianassa'],['euarne','Euarne'],['psamathe','Psamathe (Nereid)'],['menippe','Menippe'],['neso','Neso'],['eupompe','Eupompe'],['themisto-nereid','Themisto (Nereid)'],['pronoe','Pronoe'],['nemertes','Nemertes'],
];
for (const [key, name] of nereidNames) person(key, name, 'nymph', `${name} is named in Hesiod’s catalogue of Nereus and Doris’s sea-daughters. The selected passage establishes the family but gives most members no individual adventure. Repeated names are distinguished from Muses, Graces, Oceanids and mortal women elsewhere in this atlas.`, ['nereus', 'doris'], refs('theogony', '240–264'), { groups: ['nereids'], mortality: 'immortal' });
Object.assign(byKey.get('amphitrite'), { summary: 'Amphitrite is named among Nereus and Doris’s daughters in Hesiod and becomes Poseidon’s partner and Triton’s mother. The Library also gives her an Oceanid genealogy. Her placement in the sea court should therefore retain the selected source rather than silently join both parental families.', groups: ['nereids','poseidon-family'], roman: 'Amphitrite / Salacia (partial comparison)' });
variant('amphitrite', ['oceanus'], 'The Library calls Amphitrite a daughter of Ocean; the selected clause does not name her mother.', 'apollodorus1', '1.4.5');
Object.assign(byKey.get('thetis'), { summary: 'Thetis is a Nereid who marries the mortal Peleus and bears Achilles. In the Iliad she advocates for her son while knowing that his life will be short. Her divine power can obtain armor and influence Zeus but cannot abolish the poem’s mortal limits.', groups: ['nereids','heroic-descendants'], sourceRefs: [r('theogony','240–264; 1006–1007'),r('apollodorus3','3.13.4–6')] });
Object.assign(byKey.get('psamathe'), { summary: 'The Nereid Psamathe bears Phocus to Aeacus after trying to escape him through transformation into a seal. Her son’s murder by his half-brothers fractures the Aeacid family. She is distinct from the Argive mortal Psamathe, mother of Linus.', groups: ['nereids','heroic-descendants'], sourceRefs: [r('theogony','240–264'),r('apollodorus3','3.12.6')] });
for (const key of ['cymodoce','cymatolege']) byKey.get(key).summary = `${byKey.get(key).name} is singled out with Amphitrite in Hesiod’s sea-daughter catalogue for calming waves and winds. The passage gives a particular maritime function within the family, without supplying a separate marriage or a conventional heroic adventure.`;
byKey.get('nemertes').summary = 'The final Nereid in Hesiod’s transmitted list shares the unerring character associated with her father Nereus. The poem calls the group fifty daughters, although the transmitted name list represented in this edition contains fifty-one entries; the discrepancy is documented rather than hidden.';

// Selected descendants make the transition from divine households to heroic generations visible.
rows('heroic-descendants','hero',['aeacus','endeis'],'apollodorus3',[
  ['peleus','Peleus','Exiled after the killing of Phocus, Peleus eventually marries the Nereid Thetis and fathers Achilles. His line is one route from Zeus to the Trojan generation. The divine wedding does not create an immortal family: both father and son remain subject to loss.','3.12.6; 3.13.1–6'],
  ['telamon','Telamon','Peleus’s brother in the principal Library genealogy becomes king of Salamis and father of Ajax and Teucer. The text explicitly preserves another account in which he is a friend rather than a brother of Peleus, illustrating the instability of even prominent heroic pedigrees.','3.12.6–7'],
]);
rows('heroic-descendants','hero',['aeacus','psamathe'],'apollodorus3',[
  ['phocus','Phocus','The son of Aeacus and the Nereid Psamathe is killed through the jealousy of Peleus and Telamon. The crime drives the surviving brothers into exile and reshapes the family’s geography, linking an internal household conflict to later heroic centers.','3.12.6'],
]);
rows('heroic-descendants','hero',['peleus','thetis'],'apollodorus3',[
  ['achilles','Achilles','A mortal son of a sea goddess, Achilles is central to the Iliad’s study of anger, grief and honor. His death is foretold but does not occur within that poem, which ends with Hector’s funeral. Later sources narrate his death and posthumous reception.','3.13.6–8; Epitome 5.3'],
]);
rows('heroic-descendants','hero',['telamon','periboea-ajax'],'apollodorus3',[
  ['ajax-telamon','Ajax (Telamon’s son)','The Salaminian warrior descends from Aeacus through Telamon. His enormous strength and defense of comrades do not secure Achilles’s armor after that hero’s death. The resulting catastrophe belongs to the wider Trojan tradition, after the Iliad’s narrated events.','3.12.7; Epitome 5.6–7'],
]);
rows('heroic-descendants','hero',['telamon','hesione'],'apollodorus3',[
  ['teucer','Teucer','The archer is son of Telamon and the Trojan princess Hesione. His family therefore joins Greek and Trojan royal lines before the later war. He is distinct from the earlier Teucer associated with the ancestry and name of the Trojan people.','3.12.7'],
]);
rows('heroic-descendants','hero',['heracles','deianeira'],'apollodorus2',[
  ['hyllus','Hyllus','The son of Heracles and Deianeira becomes an important ancestor in stories about the Heraclids’ return to the Peloponnese. The genealogy organizes later claims to territory; it should not be treated as a literal contemporary record of population movement.','2.7.8; 2.8.1–2'],
]);
rows('heroic-descendants','deity',['heracles','hebe'],'apollodorus2',[
  ['alexiares','Alexiares','The Library names Alexiares and Anicetus as the sons born to Heracles and Hebe after the hero’s reception among the gods. It offers names and parentage rather than full independent adventures; these children belong to his immortal household.','2.7.7'],
  ['anicetus','Anicetus','Anicetus is named with Alexiares in the divine family created by Heracles’s marriage to Hebe. The brief entry follows apotheosis and must not be mixed without explanation into the earlier mortal children produced during his life of labors.','2.7.7'],
]);
rows('heroic-descendants','hero',['heracles','auge'],'apollodorus2',[
  ['telephus','Telephus','The son of Heracles and Auge is exposed and reared in a story involving a doe before becoming associated with Mysia. His later encounter with the Greeks joins Heracles’s descendants to the routes and mistakes of the Trojan expedition.','2.7.4; 3.9.1'],
]);
rows('heroic-descendants','hero',['heracles','astyoche-tlepolemus'],'apollodorus2',[
  ['tlepolemus','Tlepolemus','A son of Heracles and Astyoche, Tlepolemus settles Rhodes and fights at Troy. His confrontation with Sarpedon in the Iliad brings two lines descending from Zeus into fatal conflict; divine ancestry gives neither side immunity.','2.7.6; Homer, Iliad 5.628–669'],
]);
rows('zeus-variants','hero',['ixion','dia'],'apollodorus-e',[
  ['pirithous','Pirithous','Theseus’s companion belongs to Ixion’s line in many accounts, while Homer and later mythographers also give him Zeus as father. His attempt to claim Persephone ends in underworld punishment. The variant matters without becoming a reason to erase the mortal household.','Epitome 1.20–24'],
]);
variant('pirithous',['zeus','dia'],'The divine alternative is explicitly attested.','zeus-index','Homer, Iliad 14.317–318; Hyginus, Fabulae 155');
rows('zeus-variants','deity',['oceanus','tethys'],'apollodorus3',[
  ['asopus','Asopus','The river father of Aegina is assigned to Ocean and Tethys, Poseidon and Pero, or Zeus and Eurynome in the same Library passage. His pursuit of Zeus after Aegina’s abduction gives the genealogy a conflict between paternal protection and Olympian power.','3.12.6'],
]);
variant('asopus',['poseidon','pero-asopus'],'Acusilaus’s version as reported by the Library.','apollodorus3','3.12.6');
variant('asopus',['zeus','eurynome'],'Another alternative reported in the same passage.','apollodorus3','3.12.6');

// Supporting parents. Empty parent lists mean this bounded atlas has not selected
// their ancestry, never that an ancient author declared them parentless.
const supporting = [
  ['semele','Semele','mortal','Cadmus and Harmonia’s daughter asks Zeus to appear in his divine form and dies from the encounter. Dionysus survives through a second gestation in Zeus. The story distinguishes the mortal mother’s fate from the later traditions of her divine recovery.',['cadmus','harmonia'],'apollodorus3','3.4.2–3'],
  ['carme','Carme','nymph','Britomartis’s mother has rival ancestral settings. Pausanias calls her a daughter of Euboulos, while Antoninus Liberalis places her in a Phoenician family. Neither setting should be smoothed into a single unsupported lineage.'],
  ['boetis','Boetis','nymph','Named as Aegipan’s mother in Hyginus’s list of Jupiter’s children. The brief testimony is kept separate from the goat Aex or Aix in an astronomical alternative.'],
  ['aix','Aix / Aex','creature','The goat associated with the alternative Aegipan genealogy in Hyginus’s astronomical account. This record is not silently identified with every goat or nymph called Amalthea in Zeus’s infancy traditions.'],
  ['thaleia-sicilian','Thaleia (Sicily)','nymph','The Sicilian mother of the Palici in a Zeus genealogy. She is distinct from the Grace Thalia and the Muse Thalia; the shared name is insufficient to combine their identities.'],
  ['aetna','Aetna','nymph','A Sicilian mountain-associated figure named in an alternative parentage of the Palici through Hephaestus. She is kept distinct from their mother Thaleia in the Zeus account.'],
  ['alcmene','Alcmene','mortal','Zeus deceives Alcmene by appearing as her husband Amphitryon. She becomes mother of Heracles by Zeus and Iphicles by Amphitryon. The two fatherhoods and the deception are essential to the opening family frame of the Shield.'],
  ['danae','Danae','mortal','Confined by her father Acrisius after a prophecy, Danae bears Perseus to Zeus and is cast out with the infant in a chest. Rescue and survival, rather than a simple courtship, organize the mother and child’s story.'],
  ['leda','Leda','mortal','Leda is the wife of Tyndareus and mother in competing accounts of Helen and the Dioscuri. The Library separates mortal and divine fatherhoods, while other traditions use an egg and make Leda a foster mother to Nemesis’s daughter.'],
  ['tyndareus','Tyndareus','mortal','The Spartan king is Leda’s husband and a mortal father in the Library’s divided parentage of her children. His household and the oath of Helen’s suitors shape the generation before the Trojan expedition.'],
  ['europa','Europa (Phoenician princess)','mortal','Zeus abducts Europa to Crete in bull form. Her sons Minos, Rhadamanthys and Sarpedon belong to one major Cretan genealogy. She is distinct from Europa daughter of Tityus and from Oceanids who share the name.'],
  ['laodamia-bellerophon','Laodamia','mortal','Bellerophon’s daughter is the mother of the Sarpedon who fights at Troy in Homer. This places that warrior in a different maternal genealogy from Europa’s Cretan son Sarpedon.',['bellerophon'],'zeus-index','Homer, Iliad 6.198–205'],
  ['antiope','Antiope','mortal','The mother of Amphion and Zethus suffers persecution in the Theban narrative before her grown sons recognize and defend her. Her identity is separate from the Amazon Antiope associated with Theseus.'],
  ['aegina','Aegina','nymph','Asopus’s daughter is carried away by Zeus and bears Aeacus on the island named for her. Her father’s pursuit and Zeus’s violent response make the abduction and its family consequences explicit.',['asopus','metope'],'apollodorus3','3.12.6'],
  ['callisto','Callisto','mortal','A companion associated with Artemis, Callisto is raped by Zeus and later transformed or killed in varying accounts. Arcas is her son. The contradictory narratives should not be repackaged as an untroubled divine love affair.'],
  ['io','Io','mortal','The Argive woman is transformed into a cow and driven through a long ordeal in stories involving Zeus and Hera. Her descendants connect Greece with Egypt and other regions, but those mythic routes are not historical migration records.'],
  ['niobe-phoroneus','Niobe (Phoroneus’s daughter)','mortal','The mother of Argus and Pelasgus in the Argive genealogy is Phoroneus’s daughter. She is not Tantalus’s daughter Niobe, whose boast and children’s deaths belong to a much later generation.'],
  ['protogeneia-deucalion','Protogeneia','mortal','Deucalion and Pyrrha’s daughter bears Aethlius to Zeus in the selected account. Her genealogy links the family after the flood with later regional founders.',['deucalion','pyrrha'],'apollodorus1','1.7.2'],
  ['calyce','Calyce','mortal','Calyce is Endymion’s mother in the Library and is also named as Aethlius’s mother in a Hesiodic fragment. These shifting generations are retained as alternatives instead of forcing one linear chronology.'],
  ['deucalion','Deucalion','hero','Prometheus’s son survives the flood with Pyrrha and takes part in the renewal of human life. Several later Greek ethnonym genealogies descend from this pair, though sources vary in the named children and divine fathers.',['prometheus'],'apollodorus1','1.7.2'],
  ['pyrrha','Pyrrha','mortal','Pyrrha, daughter of Epimetheus and Pandora, survives the flood with Deucalion. Her children’s genealogies establish several regional ancestral lines. She is distinct from later women called Pyrrha and from Achilles’s disguise using that name.',['epimetheus','pandora-first'],'apollodorus1','1.7.2'],
  ['thyia-deucalion','Thyia (Deucalion’s daughter)','mortal','The Hesiodic fragment makes Thyia daughter of Deucalion and mother of Magnes and Macedon by Zeus. She is distinguished from the Delphic Thyia who appears among proposed mothers of Delphus.',['deucalion'],'catalogue','fragment 3'],
  ['pandora-deucalion','Pandora (Deucalion’s daughter)','mortal','This Pandora is Deucalion’s daughter and Graecus’s mother in a Hesiodic fragment quoted by John the Lydian. She is not the first woman made by the gods in Works and Days.',['deucalion'],'catalogue','fragment 2'],
  ['himalia','Himalia','nymph','A Rhodian nymph named by Diodorus as mother of Spartaeus, Cronius and Cytus. Their genealogy belongs to the historian’s account of early Rhodes and the aftermath of the gods’ struggles.'],
  ['sithnid-nymph','Unnamed Sithnid nymph','nymph','Pausanias identifies Megarus’s mother by her nymph group rather than a personal name. The descriptive key preserves that limit instead of inventing a named consort for Zeus.'],
  ['eurymedusa','Eurymedusa','mortal','The mother of Myrmidon in a tradition associating Zeus with an ant’s form. The genealogical notice is late and brief; the account is not treated as a historical explanation of the Myrmidons.'],
  ['othreis','Othreis','nymph','Antoninus Liberalis names Othreis as mother of Meliteus by Zeus. The child’s exposure and rescue belong to a local foundation narrative, not a panhellenic epic cycle.'],
  ['cassiopeia-crete','Cassiopeia (Atymnius’s mother)','mortal','The Library names this Cassiopeia as Atymnius’s mother. She is not automatically identified with the Ethiopian queen and mother of Andromeda, nor with other similarly named women.'],
  ['plouto','Plouto','nymph','The mother of Tantalus in the selected genealogical witnesses. Her name is distinct from Plouton, the underworld title associated with Hades, and Plutus, the personification of wealth.'],
  ['elara','Elara','mortal','Zeus hides Elara beneath the earth, where Tityus is born in the Library. Earth’s role in the birth helps explain why other poetry can call Tityus earthborn, without turning the two tellings into identical parent sets.'],
  ['hora-scythian','Hora (Colaxes’s mother)','nymph','Valerius Flaccus names Hora in the ancestry of the northern warrior Colaxes. She is not automatically one of the named Horae born to Zeus and Themis.'],
  ['borysthenes-daughter','Unnamed daughter of Borysthenes','nymph','Herodotus reports this river daughter as Targitaus’s mother in the Scythian origin story. The text supplies a relationship, not her individual name; the catalog preserves that distinction.'],
  ['libyan-nymph','Unnamed Garamantian nymph','nymph','Virgil describes Iarbas’s mother as a nymph subjected to Jupiter Ammon’s force. Her individual name is not supplied in the passage, and the assault is not redescribed as a consensual marriage.'],
  ['lamia-libya','Lamia (Libyan Sibyl tradition)','nymph','Pausanias names Lamia, daughter of Poseidon, as mother of an early Libyan Sibyl by Zeus. The Sibyl is earlier than Herophile in his discussion; this Lamia is not silently joined to every monstrous or royal Lamia.',['poseidon'],'zeus-index','Pausanias 10.12.1'],
  ['lamia-acheilus','Lamia (Acheilus’s mother)','nymph','The late testimony preserved from Ptolemy Hephaestion names this Lamia as Acheilus’s mother. The atlas keeps the thinly attested figure separate from the Libyan Sibyl’s mother unless a source makes the identification.'],
  ['lysithoe','Lysithoe','nymph','Cicero’s catalogue of multiple Hercules figures includes a Jupiter–Lysithoe child. This mother belongs to that explicitly plural theological discussion, not a replacement name for Alcmene.'],
  ['olympias','Olympias','historical','The historical mother of Alexander and wife of Philip II appears in later stories of miraculous conception and divine descent. The legends are identified as royal mythology, not evidence overriding Alexander’s historical parentage.'],
  ['philip-ii','Philip II','historical','The Macedonian king is Alexander’s historical father. This record provides an evidentiary counterpoint to divine-birth legends reported by ancient biographers; mythical divine parentage is classified separately.'],
  ['tyro','Tyro','mortal','Poseidon deceives Tyro by appearing as the river Enipeus, whom she desires. Pelias and Neleus result from this encounter. The god’s disguise is central to the story’s account of conception.'],
  ['iphimedeia','Iphimedeia','mortal','The mother of Otus and Ephialtes is married to Aloeus in the Library. The children are called Aloadae through that household even where Poseidon is identified as their biological father.'],
  ['canace','Canace','mortal','Canace bears five named sons to Poseidon in the Library. This selected genealogy does not import every narrative about a woman named Canace into the same biography.'],
  ['libya','Libya','mortal','The personified ancestral figure Libya connects Epaphus’s Egyptian line with Agenor and Belus through Poseidon. Her name is used within mythic geography; the genealogy is not a modern ethnic classification.',['epaphus','memphis'],'apollodorus2','2.1.4'],
  ['aethra','Aethra','mortal','Aethra becomes Theseus’s mother in the double-paternity story involving Aegeus and Poseidon. She raises the child at Troezen until he can lift the stone hiding his father’s tokens, making upbringing distinct from the question of divine descent.'],
  ['aegeus','Aegeus','mortal','The Athenian king is a human father in Theseus’s competing paternity accounts. The hidden sword and sandals create a later recognition between father and son; Poseidon’s claim does not erase this royal and household relationship.'],
  ['amymone','Amymone','mortal','One of Danaus’s daughters, Amymone seeks water in Argos and encounters Poseidon after danger from a satyr. Her son Nauplius and the spring story connect the divine relationship to local landscape.'],
  ['chione-boreas','Chione (Boreas’s daughter)','nymph','The mother of Eumolpus is a daughter of Boreas. She is a different Chione from Daedalion’s daughter, the mother of Apollo’s Philammon and Hermes’s Autolycus.',['boreas'],'apollodorus3','3.15.4'],
  ['salamis','Salamis','nymph','Asopus’s daughter Salamis bears Cychreus to Poseidon in the Library. Her identity links a river family to an island’s name and royal ancestry.',['asopus'],'apollodorus3','3.12.7'],
  ['euryale-orion','Euryale (Orion’s mother)','mortal','Pherecydes’s account, as preserved by the Library, names Euryale as Orion’s mother by Poseidon. She is not the immortal Gorgon sister of Medusa despite the repeated name.'],
  ['pitane','Pitane','nymph','Pindar makes Pitane Evadne’s mother by Poseidon. Her child is raised in another household, so the ode distinguishes birthplace, biological relationship and the foster family that protects a child.'],
  ['eurynome-bellerophon','Eurynome (Bellerophon’s mother)','mortal','Bellerophon’s mother in the Hesiodic fragment is a mortal woman distinguished from the Oceanid Eurynome, mother of the Graces. Her partner differs between the divine and mortal paternity traditions.'],
  ['glaucus-sisyphus','Glaucus (Sisyphus’s son)','mortal','The Corinthian Glaucus provides Bellerophon’s mortal paternal line. He is separate from the sea deity Glaucus and from the Lycian warrior Glaucus in the Iliad.'],
  ['thoosa','Thoosa','nymph','The Odyssey identifies Thoosa as Phorcys’s daughter and Polyphemus’s mother by Poseidon. This makes the Cyclops part of an older sea lineage as well as the Olympian sea god’s family.',['phorcys'],'poseidon-index','Homer, Odyssey 1.68–73'],
  ['periboea-giant','Periboea (Nausithous’s mother)','mortal','A daughter of the Giant king Eurymedon in the Odyssey, Periboea bears Nausithous to Poseidon. She is distinct from the Periboea who becomes Ajax’s mother.'],
  ['alope','Alope','mortal','Pausanias links Alope with Poseidon and Hippothoon in the Eleusinian family traditions. Her child’s later civic importance belongs to the genealogies of Athenian tribal heroes.'],
  ['europa-tityus','Europa (Tityus’s daughter)','mortal','Pindar names this Europa as Euphemus’s mother by Poseidon. She is not the Phoenician Europa abducted by Zeus; explicitly separating them keeps the Argonaut’s parentage intelligible.',['tityus'],'poseidon-index','Pindar, Pythian 4.44–47'],
  ['molione','Molione','mortal','The mother who gives the Molionids their collective name belongs to Actor’s household. The Library also assigns Cteatus and Eurytus to Poseidon, distinguishing divine paternity from the mortal marriage.'],
  ['actor-elian','Actor (Elis)','mortal','Molione’s husband belongs to the mortal household of the Molionid twins. He is separated from numerous other figures called Actor in Greek genealogy.'],
  ['melia-bebrycian','Melia (Bebrycian nymph)','nymph','Named as Amycus’s mother by Poseidon in the Library. She is distinct from other nymphs called Melia and from the collective Meliae generated by Uranus’s blood.'],
  ['halia','Halia','nymph','Diodorus’s Rhodian account names Halia as mother of Rhode and six sons by Poseidon. The local story is one alternative to Amphitrite’s or Aphrodite’s motherhood of Rhode.'],
  ['cleito','Cleito','mortal','In Plato’s Critias, Poseidon surrounds Cleito’s dwelling with rings of land and water and fathers five pairs of sons with her. Her household provides the political structure of Atlantis within a philosophical narrative.'],
  ['coronis','Coronis','mortal','Coronis is Asclepius’s mother in Pindar and one Library account. Her death while pregnant and Apollo’s recovery of the child make this a story of divine violence and rescue, not simply a medical dynasty’s decorative origin.'],
  ['arsinoe-messenia','Arsinoe (Messenia)','mortal','A daughter of Leucippus appears as Asclepius’s mother in a competing Messenian genealogy. The Library records this alternative instead of presenting Coronis as the only ancient maternal claim.'],
  ['cyrene','Cyrene','nymph','Pindar’s hunting figure is brought into a Libyan civic narrative through Apollo and the birth of Aristaeus. She is distinguished here from Cyrene named as mother of the Thracian Diomedes.'],
  ['creusa','Creusa (Athens)','mortal','In Euripides’s Ion, Creusa recounts Apollo’s rape and her exposure of the infant she bore. Recognition with her son exposes a family trauma beneath Athens’s prestigious divine genealogy.'],
  ['oeagrus','Oeagrus','mortal','The Thracian father assigned to Orpheus in one Library account. Apollo competes with him in the same notice, so musical affinity does not establish a single universally agreed biological father.'],
  ['psamathe-argive','Psamathe (Argos)','mortal','The mother of the Argive infant Linus in Pausanias is distinct from the Nereid Psamathe. Her child’s loss generates a local story of punishment and ritual memory.'],
  ['phthia','Phthia','mortal','The Library names Phthia as mother of Dorus, Laodocus and Polypoetes by Apollo. Her own ancestry is not selected here, avoiding speculative links suggested by a shared regional name.'],
  ['rhoeo','Rhoeo','mortal','Rhoeo is exposed in a chest by her father after becoming pregnant and reaches Delos, where she bears Anius. Her appeal to Apollo makes recognition and sanctuary central to the child’s story.'],
  ['aria-miletus','Aria (Miletus’s mother)','mortal','Aria is the maternal name used for Miletus in the Library. Other names in other sources are not silently treated as aliases without explanation.'],
  ['manto','Manto','mortal','The prophetic woman Manto becomes mother of Mopsus by Apollo. The divinatory profession runs through the genealogy, while Mopsus’s contest with Calchas gives it a role in post-Trojan narrative.'],
  ['priam','Priam','mortal','The Trojan king has a large family by Hecuba and other women in the Library. This atlas includes selected children needed for divine-parentage comparisons, not an assertion that its Trojan branch exhausts every named Priamid.'],
  ['hecuba','Hecuba','mortal','The Trojan queen is Troilus’s mother in both the Priam and Apollo paternity accounts. Different genealogies of her own parents and her children remain part of the wider Trojan tradition.'],
  ['chione-daedalion','Chione (Daedalion’s daughter)','mortal','Ovid makes Chione mother of Autolycus by Mercury and Philammon by Apollo. The story describes Mercury putting her to sleep and Apollo approaching in disguise; these encounters should not be summarized as straightforward consensual courtship.'],
  ['corycia','Corycia','nymph','Pausanias links the Corycian nymph with Apollo and Lycoras in a local Delphic genealogy. Her name also explains the sacred cave within that account.'],
  ['celaeno-delphi','Celaeno (Delphi)','mortal','The Delphic Celaeno is a descendant of Lycoras and a proposed mother of Delphus. She is distinct from the Pleiad Celaeno and other women who bear this repeated name.'],
  ['thyia-delphi','Thyia (Delphi)','mortal','A Delphic woman associated with Dionysian ritual is one of the proposed mothers of Delphus in Pausanias. She is kept apart from Deucalion’s daughter Thyia, mother of Magnes and Macedon.'],
  ['melaena','Melaena','nymph','Pausanias gives the river daughter Melaena as another mother of Delphus by Apollo. The multiple local explanations remain alternatives in the same genealogical comparison.'],
  ['stilbe','Stilbe','nymph','Diodorus names Stilbe as mother of Lapithes and Centaurus by Apollo. This is one genealogy relating two peoples, distinct from an Ixion-based origin for Centaurs.'],
  ['aglaurus','Aglaurus','mortal','An Athenian daughter of Cecrops is named in the maternal lines of Alcippe and Ceryx. Sources preserve alternative spellings and related figures; the selected notices concern the daughter rather than collapsing her with a namesake in the preceding generation.'],
  ['harpina','Harpina','nymph','An Elean genealogy makes Harpina the mother of Oenomaus by Ares. Her alternative is retained alongside the Pleiad Sterope’s motherhood.'],
  ['astyoche','Astyoche (Orchomenus)','mortal','The Iliad names Astyoche as mother of Ares’s sons Ascalaphus and Ialmenus. She is separated from Astyoche, mother of Heracles’s son Tlepolemus.'],
  ['pelopia','Pelopia (Cycnus’s mother)','mortal','The Library names Pelopia in the parentage of one Cycnus son of Ares. Repeated Cycnus names and varying locations are not treated as proof that every source tells exactly the same encounter.'],
  ['cyrene-thrace','Cyrene (Diomedes’s mother)','mortal','The Library gives this Cyrene as the mother of the Thracian Diomedes by Ares. The record remains separate from Apollo’s partner Cyrene in the Aristaeus story.'],
  ['otrera','Otrera','hero','The Amazon mother of Hippolyta and Penthesilea in the selected mythographic family. Their warlike identity does not imply that every Amazon has the same parents.'],
  ['chryse','Chryse','mortal','Pausanias names Chryse as Phlegyas’s mother by Ares. The local genealogy provides limited information and is not padded with episodes belonging to other similarly named figures.'],
  ['oeneus','Oeneus','mortal','The Calydonian king and husband of Althaea provides the human household for Meleager and Deianeira. The Library reports competing divine fathers for those children while retaining the royal family’s narrative importance.'],
  ['althaea','Althaea','mortal','Althaea’s children include Meleager and Deianeira, with alternative divine paternities recorded by mythographers. Her decision over the brand tied to Meleager’s life makes maternal grief and vengeance decisive in the Calydonian story.'],
  ['demonice','Demonice','mortal','Demonice is named as mother of Evenus, Molus, Pylus and Thestius by Ares. The branch links the war god to Aetolian founders and later Spartan and Calydonian families.'],
  ['dryops-daughter','Unnamed daughter of Dryops','nymph','The Homeric Hymn identifies Pan’s mother through her father Dryops and does not provide her own name. An honest catalog preserves that absence instead of borrowing a maternal name from a different Pan genealogy.'],
  ['penelope-pan','Penelope (Pan variant)','mortal','The Epitome reports an alternative, hostile story making Penelope Pan’s mother by Hermes. It contradicts the loyal marriage emphasized by the Odyssey and must not be passed off as part of Homer’s homecoming narrative.'],
  ['theobule','Theobule','mortal','Theobule appears as Myrtilus’s mother in Hyginus. A different maternal name, Clytie, occurs in the astronomical account, so the two are kept as alternatives.'],
  ['clytie-myrtilus','Clytie (Myrtilus’s mother)','nymph','The alternative mother of Myrtilus in Hyginus’s astronomical account. This entry does not identify her with every Oceanid or mortal woman named Clytie.'],
  ['herse','Herse','mortal','The Athenian Herse is mother of Hermes’s son Cephalus in the Library. The chosen child is distinguished from the Cephalus in the Procris hunting narrative.'],
  ['antianeira','Antianeira','mortal','Apollonius makes Antianeira mother of Echion and Eurytus by Hermes. Her sons’ shared mother is explicit in the Argonaut catalogue rather than inferred merely from a shared father.'],
  ['eupolemeia','Eupolemeia','mortal','The mother of the Argonautic herald Aethalides by Hermes. The passage distinguishes her son from the other Hermes-born members of the expedition.'],
  ['polymele','Polymele','mortal','The Iliad describes Polymele dancing when Hermes notices her and later records Eudorus’s upbringing by her father. Her marriage and the child’s fostering distinguish social household arrangements from divine biological ancestry.'],
  ['acacallis','Acacallis','mortal','Pausanias records Acacallis in the Cretan genealogy of Cydon through Hermes. Other Cretan children and versions require separate testimony rather than automatic expansion from the name alone.'],
  ['rhene','Rhene','nymph','The mother of Saon by Hermes in one Samothracian account reported by Diodorus. The alternative replaces both her and Hermes with an unnamed nymph and Zeus.'],
  ['samothracian-nymph','Unnamed Samothracian nymph','nymph','Diodorus’s alternative pedigree for Saon names Zeus and an unnamed nymph. The absence of her individual name is preserved rather than silently reusing Rhene from the competing Hermes version.'],
  ['chthonophyle','Chthonophyle','mortal','The Sicyonian family in Pausanias assigns Polybus to Chthonophyle and Hermes while also placing her in a marriage with Phlias. Divine paternity does not erase that separate household relationship.'],
  ['alcidameia','Alcidameia','mortal','Named as Bounus’s mother by Hermes in Pausanias’s Corinthian narrative. The surviving genealogical notice does not warrant inventing an independent adventure.'],
  ['phylodameia','Phylodameia','mortal','Pausanias identifies Phylodameia as Pharis’s mother by Hermes in a Messenian genealogy. The entry preserves a regional parent with limited surviving narrative.'],
  ['ariadne','Ariadne','mortal','Minos and Pasiphae’s daughter aids Theseus and later enters Dionysus’s family in multiple tellings. The Library’s Epitome describes abduction by Dionysus, while other poets frame the relationship differently. Her children should be attributed to the selected witness.',['minos','pasiphae'],'apollodorus-e','Epitome 1.7–9'],
  ['physcoa','Physcoa','mortal','The Elean mother of Narcaeus is credited with her son as an early worshipper of Dionysus in the region. Pausanias relates that family story to women’s ritual traditions.'],
  ['nicaea','Nicaea','nymph','Nonnus’s huntress is raped while intoxicated and asleep, becoming mother of Telete. The account’s sexual violence remains explicit rather than being disguised by the neutral label consort.'],
  ['aura','Aura','nymph','Aura’s tragic story in Nonnus involves Dionysus’s rape and a devastating aftermath for mother and twins. The surviving child Iacchus belongs to that late epic genealogy, not every Eleusinian account.'],
  ['anchises','Anchises','mortal','The Trojan mortal fathers Aeneas after Aphrodite approaches him in disguise. The Homeric Hymn explores the unequal consequences of intimacy between gods and humans rather than offering an uncomplicated romance.'],
  ['butes-argonaut','Butes (Argonaut)','hero','The voyager drawn toward the Sirens is rescued by Aphrodite and becomes father of Eryx in Sicilian tradition. Other men called Butes have different parents and roles.'],
  ['phaethon-eos','Phaethon (Eos’s son)','deity','The beautiful son of Eos and Cephalus is taken by Aphrodite in Hesiod’s account and enters a later genealogy of Astynous. He is separate from the son of the Sun who drives the fatal chariot.',['eos','cephalus-hermes'],'theogony','986–991'],
  ['adonis','Adonis','mortal','The young figure beloved by Aphrodite has competing parentages and death narratives. Nonnus’s Beroe genealogy belongs to a late development, not an assumed feature of all Adonis traditions.'],
  ['anticleia-periphetes','Anticleia (Periphetes’s mother)','mortal','The Library names this Anticleia as mother of Hephaestus’s son Periphetes. She is distinguished from Odysseus’s mother, the daughter of Autolycus.'],
  ['cabeiro','Cabeiro','nymph','Strabo’s discussion of mystery traditions gives Cabeiro a place in differing genealogies through Hephaestus. Competing reports about the Cabeiri and Cadmilus are not flattened into a single universally accepted family.'],
  ['endeis','Endeis','mortal','The mother of Peleus and Telamon in the Library’s principal Aeacid genealogy. Their half-brother Phocus has the Nereid Psamathe as his mother.'],
  ['periboea-ajax','Periboea (Ajax’s mother)','mortal','The daughter of Alcathus becomes Telamon’s wife and mother of Ajax. She is distinct from the Giant-descended Periboea who bears Nausithous to Poseidon.'],
  ['hesione','Hesione','mortal','The Trojan princess is given to Telamon after Heracles’s expedition against Troy and bears Teucer. Her transfer is a consequence of war and capture, not presented as freely chosen courtship.'],
  ['auge','Auge','mortal','Auge becomes Telephus’s mother by Heracles in an account of sexual violence and exposure. Her son’s survival connects Arcadian and Mysian narrative settings.'],
  ['astyoche-tlepolemus','Astyoche (Tlepolemus’s mother)','mortal','Heracles takes Astyoche after the capture of Ephyra and fathers Tlepolemus. She is distinct from the Orchomenian Astyoche who bears Ares’s sons.'],
  ['ixion','Ixion','mortal','The Lapith king is Pirithous’s human father in a principal genealogy. Other stories focus on his offense against Zeus and Hera, making his family central to a cluster of myths about failed reciprocity.'],
  ['dia','Dia','mortal','Pirithous’s mother is paired with Ixion in a human household and with Zeus in an alternative divine conception. Both traditions remain visible without treating her child as having an ordinary combined parent set.'],
  ['pero-asopus','Pero (Asopus’s mother)','nymph','Acusilaus’s river genealogy, quoted in the Library, gives Asopus Poseidon and Pero as parents. She is distinguished from other figures called Pero.'],
  ['metope','Metope','nymph','The Library makes Metope, daughter of the river Ladon, the partner of Asopus and mother of Aegina among many daughters. This supporting entry does not pretend to exhaust the river family.'],
  ['memphis','Memphis','nymph','A daughter of the Nile in the Library’s genealogy, Memphis becomes mother of Libya by Epaphus. The family uses river and regional names to organize a mythic geography.'],
  ['pandora-first','Pandora (first woman)','mortal','In Works and Days the gods construct and endow Pandora, who arrives in Epimetheus’s household. The poem’s account reflects its gendered explanation of human hardship. Divine makers are not converted into ordinary biological parents in this graph.',[],'works','47–105'],
  ['pasiphae','Pasiphae','deity','The daughter of Helios and Perseis becomes Minos’s wife in the Library and mother of the Cretan royal children. Her divine parentage connects Crete with the solar family shared by Circe and Aeetes.',['helios','perseis'],'apollodorus3','3.1.2'],
  ['cadmus','Cadmus','hero','The founder of Thebes marries Harmonia and fathers a family including Semele. His dragon fight, earthborn warriors and later transformations give the city’s origins a history of violence as well as divine marriage.',['agenor'],'apollodorus3','3.1.1; 3.4.1–2; 3.5.4'],
];
for (const [key,name,category,summary,parents=[],sourceId,locator] of supporting) {
  const witness = records.find(p => p.parents.includes(key) || p.variants.some(v=>v.parents.includes(key)));
  const inheritedRefs = witness?.parents.includes(key) ? witness.sourceRefs : witness?.variants.find(v=>v.parents.includes(key))?.sourceRefs;
  person(key,name,category,summary,parents,sourceId ? refs(sourceId,locator) : inheritedRefs ?? refs('apollodorus3','Selected supporting genealogy'), {groups:['genealogical-parents'],coverage:'supporting-parent', mortality: category==='nymph'?'unspecified':(['mortal','hero','historical'].includes(category)?'mortal':'immortal')});
}

// Source-specific corrections and limits discovered during cross-checking.
sources.push(source('byzantium','Patria of Constantinople','Hesychius of Miletus; late-antique local history','https://topostext.org/work/801',['sections 5–12: alternative founders, Io, Ceroessa and Byzas'],indexRights));
sources.push(source('aglaia','Later testimony to Hephaestus and Aglaia’s daughters','Proclus, Commentary on Plato’s Timaeus; Thomas Taylor translation (1820)','https://thecollectivepress.org/source/27?p=64',['On Timaeus 29a; Taylor volume 1, page 280; Orphic fragment 182 Kern / 272 II Bernabé'],publicDomain));
for (const key of ['ceroessa','byzas']) byKey.get(key).sourceRefs=refs('byzantium',key==='ceroessa'?'6–9':'5–12');
for (const key of ['eucleia','eupheme','euthenia','philophrosyne']) byKey.get(key).sourceRefs=refs('aglaia','Proclus, On Timaeus 29a; Taylor volume 1, p. 280; Orphic fr. 182 Kern / 272 II Bernabé');
byKey.get('cycnus-ares').summary='The Library names Pelopia as mother of a Cycnus killed by Heracles, while the Hesiodic Shield develops combat against a son of Ares in its own poetic setting. Several people share the name Cycnus. Their differing locations and circumstances should not be forced into one identical itinerary.';
byKey.get('deino').key='enyo-graea'; byKey.get('deino').name='Enyo (Graea)'; byKey.get('deino').summary='Enyo is the second Graea named beside Pemphredo in Hesiod. She is distinct from the war goddess Enyo. The familiar three-sister list with Deino belongs to another account, and should not be retroactively substituted for Hesiod’s named pair.'; byKey.set('enyo-graea',byKey.get('deino'));byKey.delete('deino');
byKey.get('nereus').parents=['pontus']; byKey.get('nereus').summary+=' Hesiod first names Pontus as father without explicitly naming a mother in that clause; Gaia is given in the Library’s alternative.';
variant('nereus',['pontus','gaia'],'The Library supplies both parents explicitly.','apollodorus1','1.2.6');
byKey.get('alexander-legend').parents=['philip-ii','olympias']; byKey.get('alexander-legend').variants=[{parents:['zeus','olympias'],note:'Divine descent is royal legend and reception; it does not replace historical parentage.',sourceRefs:refs('zeus-index','Plutarch, Alexander 2–3 and 27')}];
byKey.get('herophile').name='Libyan Sibyl (unnamed)';byKey.get('herophile').summary='Pausanias calls an early Libyan Sibyl a daughter of Zeus and Lamia, daughter of Poseidon. He distinguishes her from the later Herophile discussed next. The stable key retains its working label, but the displayed name deliberately corrects the common conflation of the two Sibyls.';byKey.get('herophile').sourceRefs=refs('zeus-index','Pausanias 10.12.1–2');

byKey.get('herophile').summary='Pausanias calls an early Libyan Sibyl a daughter of Zeus and Lamia, daughter of Poseidon. He distinguishes her from Herophile, the later Sibyl discussed next. This record preserves the earlier woman’s unnamed status instead of conflating the two prophetic figures.';
for (const key of ['artemis','athena','hestia']) byKey.get(key).offspringNote='No biological children in the principal traditions selected here. Virginity is explicit in Homeric Hymn 5.7–32; foster care, artistic creation and civic patronage are separate relationships.';
byKey.get('palici').variants[0].sourceRefs[0].url='https://www.theoi.com/Georgikos/Palikoi.html';
byKey.get('aetna').sourceRefs=byKey.get('palici').variants[0].sourceRefs;
byKey.get('myrmidon').sourceRefs[0].url='https://www.theoi.com/Text/ClementExhortation1.html';
byKey.get('cyprian-centaurs').sourceRefs[0].url='https://www.theoi.com/Text/NonnusDionysiaca5.html';
byKey.get('samothracian-corybantes').sourceRefs[0].url='https://www.theoi.com/Georgikos/KorybantesSamothrakioi.html';
byKey.get('achilles').sourceRefs=[r('apollodorus3','3.13.6–8'),r('apollodorus-e','Epitome 5.3')];
byKey.get('ajax-telamon').sourceRefs=[r('apollodorus3','3.12.7'),r('apollodorus-e','Epitome 5.6–7')];
sources.push(...additionalSources);

const branchDefinitions = [
 ['origins','Before the Olympians','Hesiod’s beginnings distinguish cosmic places, generative powers and personifications. An empty parent list here means no parent is named in the selected passage, not that every ancient tradition agrees.'],
 ['titans-twelve','The twelve Titans','The twelve children of Earth and Sky explicitly named by Hesiod. Their descendants may also be called Titans, but they are not additional members of this original twelve.'],
 ['elder-allies','Cyclopes and Hundred-Handers','Two named sibling groups born from Earth and Sky. Their roles in Zeus’s victory differ from the pastoral Cyclopes of the Odyssey.'],
 ['titan-descendants','The branching Titan families','Sun, Moon, Dawn, winds, divine allies and their households connect the first divine generation with later gods. A shared function does not make Apollo identical to Helios or Artemis identical to Selene in these texts.'],
 ['iapetids','Atlas, Prometheus and their brothers','Iapetus and Clymene’s sons receive contrasting destinies under Zeus. Atlas supports heaven; Prometheus’s conflict concerns sacrifice and fire.'],
 ['oceanids','Selected Oceanids','Named daughters of Oceanus and Tethys needed for the family maps. Hesiod claims thousands of river daughters, so this is explicitly a selection rather than an exhaustive list.'],
 ['sea-foundations','The older sea families','Pontus’s descendants supply truthful sea elders, winged beings and monsters. Motherhood, foster care and service to a god are kept separate.'],
 ['nereids','Nereus and Doris’s daughters','The transmitted Theogony catalogue in the linked edition names fifty-one distinct daughters while calling the group fifty. All transmitted names are retained, with the discrepancy visible; no invented biographies are added for names without individual stories.'],
 ['night-underworld','Night, Strife and personified powers','Hesiod arranges human experiences and cosmic powers as families. Named individuals and unnamed collectives remain different kinds of record.'],
 ['cronus-rhea','The children of Cronus and Rhea','Hestia, Demeter, Hera, Hades, Poseidon and Zeus form Hesiod’s six-child generation. Hades is their brother even though he is not normally counted among the resident Olympian twelve.'],
 ['hera-family','Hera’s children and competing births','Ares, Hebe and Eileithyia belong to Hera and Zeus’s selected household. Hephaestus can be Hera’s unpartnered child or a son of Zeus and Hera in another account.'],
 ['zeus-divine','Zeus: gods and divine families','Named children of Zeus are expanded from groups such as the Muses, Graces, Seasons and Fates. The selected sources also preserve unpartnered, disguised and otherwise unusual conception stories; a parent pair does not imply a consensual marriage.'],
 ['zeus-heroes','Zeus: the major heroic lines','Mortal heroes and dynasties connect Zeus to Thebes, Argos, Crete, Troy, Aegina and other centers. The two Sarpedon records show incompatible maternal genealogies rather than a single seamlessly merged chronology.'],
 ['zeus-regional','Zeus: local founders and rare children','A bounded register of regional, fragmentary, Roman and later testimonia. A short source notice stays short; divine descent is a narrative claim, not modern historical or biological evidence.'],
 ['zeus-variants','Zeus: explicit alternative parentage','Figures whose Zeus parentage competes with another selected family, plus clearly marked rare traditions. “Alternative” describes the source relationship, not a verdict that an ancient account is false.'],
 ['poseidon-family','Poseidon: sea, horses and heroic houses','Divine beings, mortal founders and dangerous opponents appear in different Poseidon families. Supporting mothers are named or explicitly left unnamed as the passages require.'],
 ['atlantis','Poseidon and Cleito: ten sons','Plato names five pairs of sons to organize the ten kingdoms in the Critias. This Atlas is not the Titan Atlas; the literary island is not presented as a historically verified civilization.'],
 ['apollo-family','Apollo: healers, poets and founders','Medicine, prophecy, performance and civic origins recur in Apollo’s families. Biological fathers and household fathers sometimes compete, and Orpheus’s parentage is explicitly variable.'],
 ['ares-family','Ares: fear, concord and mortal conflict','The war god’s descendants include personified Fear and Terror, Harmonia, local kings and warrior women. Divine ancestry does not confer moral approval on a character’s conduct.'],
 ['hermes-family','Hermes: messages, memory and cunning','Heralds, travelers and local ancestors share this branch with Pan and Hermaphroditus. Repeated names and alternative mothers are kept distinct.'],
 ['dionysus-family','Dionysus: islands, ritual and late epic','Ariadne’s sons, regional worshippers and later personifications belong to different source settings. Nonnus’s coercive narratives remain explicitly marked as such.'],
 ['aphrodite-family','Aphrodite: love and dynasties','Aeneas connects the goddess to Roman epic, while other children belong to local or later traditions. Primordial Eros and the younger parented Eros are compared, not combined into one chronological birth.'],
 ['hephaestus-family','Hephaestus: craft and descent','Aglaia’s daughters, regional heroes and mystery figures expand the smith’s family. Athena’s care of Erichthonius is foster motherhood; the conception story describes attempted sexual assault.'],
 ['demeter-family','Demeter: grain, wealth and Arcadia','Persephone, Plutus, Philomelus, Despoina and Arion arise from distinct accounts. Arcadian Despoina is not silently equated with Persephone; Plutus is not Hades/Plouton.'],
 ['underworld','Underworld powers and limits','Hades’s household, its servants and cosmic adversaries are different relationships. The Suda’s brief Macaria notice is late and sparse; Cerberus is Typhon and Echidna’s child, not Hades’s.'],
 ['heroic-descendants','From gods to the Trojan generation','Selected second and later generations connect Heracles and the Aeacids with later heroic stories. This branch is a bridge, not a claim to list every descendant of every hero.'],
 ['genealogical-parents','Mothers, fathers and supporting families','Supporting figures retain names, source contexts and distinctions among homonyms. Empty ancestry means this atlas has not selected a pedigree, not that a person was believed to have no parents.'],
];

// Add later-inserted direct and alternative children to the navigable family branches.
for (const p of persons) {
 const allParents=[...p.parents,...p.variants.flatMap(v=>v.parents)];
 for (const key of ['poseidon','apollo','ares','hermes','dionysus','aphrodite','hephaestus','demeter'])
   if (allParents.includes(key) && !p.groups.includes(`${key}-family`)) p.groups.push(`${key}-family`);
 if (p.parents.includes('zeus') && !p.groups.some(g=>g.startsWith('zeus-'))) p.groups.push(['hero','mortal','historical'].includes(p.category)?'zeus-heroes':'zeus-divine');
 if (p.variants.some(v=>v.parents.includes('zeus')) && !p.groups.includes('zeus-variants')) p.groups.push('zeus-variants');
 p.zeusConnection=p.parents.includes('zeus')?'main':p.variants.some(v=>v.parents.includes('zeus'))?'variant':null;
 p.parentageStatus=p.parents.length?'selected attributed account':'ancestry not selected or not named';
}
const uniqueRefs=values=>[...new Map(values.map(v=>[`${v.sourceId}|${v.locator}`,v])).values()];
export const branches=branchDefinitions.map(([key,title,intro])=>{
 const members=persons.filter(p=>p.groups.includes(key));
 return {key,title,intro,personKeys:members.map(p=>p.key),sourceRefs:uniqueRefs(members.flatMap(p=>[...p.sourceRefs,...p.variants.flatMap(v=>v.sourceRefs)]))};
});
export const parentageEdges=[];
export const unions=[];
const unionMap=new Map();
for(const child of persons) {
 const sets=[{parents:child.parents,tradition:'main',note:child.birthNote??'Selected attributed parentage; not a claim of universal agreement.',sourceRefs:child.sourceRefs},...child.variants.map((v,i)=>({...v,tradition:'variant',variantIndex:i+1}))];
 for(const account of sets) {
  if(!account.parents.length)continue;
  const suffix=account.tradition==='main'?'main':`variant-${account.variantIndex}`;
  for(const parent of account.parents)parentageEdges.push({key:`${child.key}-${suffix}-${parent}`,parent,child:child.key,tradition:account.tradition,note:account.note,sourceRefs:account.sourceRefs});
  const identity=`${account.tradition}:${account.parents.join('+')}`;
  let union=unionMap.get(identity);
  if(!union){union={key:`parent-set-${unions.length+1}`,parents:[...account.parents],children:[],label:account.parents.map(k=>byKey.get(k)?.name??k).join(' + '),tradition:account.tradition,note:'Grouped parent set for navigation. A shared pair does not imply marriage, consent, or one uniform narrative across the listed children.',sourceRefs:[]};unionMap.set(identity,union);unions.push(union);}
  union.children.push(child.key);union.sourceRefs=uniqueRefs([...union.sourceRefs,...account.sourceRefs]);
 }
}
export const scopeNotes=[
 'This is a broad, explicitly bounded ancient-source catalog, not “all Greek mythology” and not a single canonical family tree. Local variants, lost works, inscriptions, unnamed collectives and later reception make a universally exhaustive list impossible.',
 'Selected parentage is the main display path; it does not mean earliest, most popular, historical, biologically plausible, or universally authoritative. Alternative parent sets retain their own witnesses. Combining all edges into a single chronological pedigree would misrepresent the data.',
 'The Zeus register includes named individuals and clearly labeled collectives. Its count is a catalog-record count, not a scientifically or historically established number of biological children. Mothers with no individual name remain descriptive unnamed records.',
 'Ancient narration includes rape, disguise, abduction, incest and enslavement. A grouping called union or a parent pair is a navigational convenience, never an assertion of marriage or consent. Summaries identify coercion when material to the chosen account.',
 'Divine, mortal, transformed, collective and historical-with-legend figures are distinguished. Genealogical ancestors who explain cities or peoples are mythic origin claims, not modern ethnic or population history.',
 'Athena, Artemis and Hestia have no biological children in the principal sources used here. Athena’s fostering of Erichthonius and Demeter’s care of mortal children do not create biological edges.',
 'Titans: twelve original named children of Earth and Sky are distinguished from their descendants. Hesiod’s Nereid catalogue calls the group fifty but the transmitted list in Evelyn-White’s linked edition supplies fifty-one distinct names; the atlas retains the text rather than deleting a name to enforce the count.',
 'Theoi family indexes are finding aids, not a new ancient canon. Every relationship has an ancient-author locator; direct primary-text pages are preferred when present. Modern translations and site commentary are not reproduced. Brief original paraphrases are used throughout.',
 'The Catalogue of Women and Great Ehoiai fragment numbers follow the linked 1914 Evelyn-White edition; they differ from modern critical numbering. Fragments do not form a complete surviving sequential work.',
 'Late Orphic material, Nonnus, late-antique local history and the Byzantine Suda are explicitly differentiated from archaic epic. An early-sounding deity is not proof that a particular genealogy survives from the earliest period.',
 'The early Libyan Sibyl is distinguished from Herophile in Pausanias 10.12.1–2. Diodorus 5.49.2 makes Corybas the son of Iasion and Cybele, not Demeter; no Demeter–Corybas edge has been inferred from a modern index.',
 'Greek–Roman comparisons concern particular literary and cult contexts. Jupiter, Juno, Mars, Venus and the other Roman figures are not merely renamed Greek characters; parent names alone cannot capture their civic, ritual and historical differences.',
];

// Normalize readable source URLs and fail immediately on broken declarative references.
const sourceMap=new Map(sources.map(s=>[s.id,s]));
if(sourceMap.size!==sources.length)throw new Error('Duplicate genealogy source id');
function normalize(value){
 if(Array.isArray(value)){value.forEach(normalize);return;}
 if(!value||typeof value!=='object')return;
 if(typeof value.sourceId==='string'){
  const entry=sourceMap.get(value.sourceId);if(!entry)throw new Error(`Unknown source: ${value.sourceId}`);
  if(!value.locator)throw new Error(`Missing locator: ${value.sourceId}`);
  value.url??=entry.url;
 }
 Object.values(value).forEach(normalize);
}
normalize([persons,parentageEdges,unions,branches,romanComparisons,hesiodWorks]);
const personKeys=new Set(persons.map(p=>p.key));
if(personKeys.size!==persons.length)throw new Error('Duplicate genealogy person key');
for(const person of persons){
 if(!person.summary||!person.sourceRefs.length)throw new Error(`Unexplained person: ${person.key}`);
 for(const key of [...person.parents,...person.variants.flatMap(v=>v.parents)])if(!personKeys.has(key))throw new Error(`Unknown parent ${key} on ${person.key}`);
 for(const group of person.groups)if(!branchDefinitions.some(b=>b[0]===group))throw new Error(`Unknown branch: ${group}`);
}
export const stats={persons:persons.length,namedIndividuals:persons.filter(p=>p.category!=='collective'&&!p.name.startsWith('Unnamed')).length,collectiveRecords:persons.filter(p=>p.category==='collective').length,zeusChildRecords:persons.filter(p=>p.zeusConnection).length,zeusMain:persons.filter(p=>p.zeusConnection==='main').length,zeusVariants:persons.filter(p=>p.zeusConnection==='variant').length,branches:branches.length,parentageEdges:parentageEdges.length,unions:unions.length,sources:sources.length};
export default {sources,persons,parentageEdges,unions,branches,romanComparisons,hesiodWorks,scopeNotes,stats};
