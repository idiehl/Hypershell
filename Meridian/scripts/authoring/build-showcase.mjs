// An independent visual proof authored through Meridian's transactional recipe API.
// Never reads or writes the live project API. Rebuilds retain this example's IDs.
import assert from 'node:assert/strict';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import { newProject, commitSnapshots } from '../../src/core/model.ts';
import { applyRecipe, inspectProject } from '../../src/core/authoring.ts';
import { validateProject } from '../../src/core/rules.ts';
import { buildPublication, readPublication } from '../../src/publication.ts';

const root = fileURLToPath(new URL('../../', import.meta.url));
const source = path.join(root, 'content/greek-mythology');
const output = path.join(root, 'examples/visual-authoring');
const json = async filename => JSON.parse(await readFile(filename, 'utf8'));
const maybeJson = async filename => { try { return await json(filename); } catch (error) { if (error.code === 'ENOENT') return undefined; throw error; } };
const sha = bytes => createHash('sha256').update(bytes).digest('hex');
const originalFile = path.join(root, 'examples/greek-mythology/greek-mythology.project.json');
const originalHash = sha(await readFile(originalFile));
const [gods, epics, hesiod, media] = await Promise.all(['gods.json', 'epics.json', 'hesiod-rome.json', 'media.json'].map(name => json(path.join(source, name))));
const sources = new Map([...gods.sources, ...epics.sources, ...hesiod.sources].map(entry => [entry.id, entry]));
const profiles = new Map(gods.groups.flatMap(group => group.entries).map(entry => [entry.key, entry]));
const roman = new Map(hesiod.roman.comparisons.map(entry => [entry.key, entry]));
const oldProject = await maybeJson(path.join(output, 'visual-atlas.project.json'));
const oldAliases = await maybeJson(path.join(output, 'visual-atlas.aliases.json'));
if (!!oldProject !== !!oldAliases) throw new Error('Keep this example project and alias map together before rebuilding.');
const base = oldProject ?? newProject('Greek Mythology · Visual atlas');
assert.notEqual(base.id, JSON.parse(await readFile(originalFile, 'utf8')).id);

const recipe = { version: 1, title: 'Greek Mythology · Visual atlas', entryCanvas: 'welcome', canvases: [], assets: [], nodes: [], links: [] };
const dark = { fill: '#17283C', border: '#39516A', textColor: '#E6ECF1', accent: '#D9B56B', radius: 14, fontSize: 16, titleSize: 23 };
const light = { fill: '#FFFCF4', border: '#CFC4AC', textColor: '#243744', accent: '#A1673B', radius: 12, fontSize: 16, titleSize: 23 };
const plum = { fill: '#342B40', border: '#76647B', textColor: '#F3EBE2', accent: '#E7BD77', radius: 14, fontSize: 16, titleSize: 23 };
const palettes = { welcome: dark, family: light, odyssey: dark, rome: light, persephone: plum };
const oldId = (group, key) => oldAliases?.[group]?.[key];
const keep = (group, key) => oldId(group, key) ? { existingId: oldId(group, key) } : {};
function canvas(key, title, background, grid) {
  recipe.canvases.push({ key, ...keep('canvases', key), ...(key === 'welcome' && !oldProject ? { existingId: base.entryCanvasId } : {}),
    title, style: { background, grid, gridVisible: false } });
}
function node(c, key, kind, title, text, x, y, width, height, extra = {}) {
  const { style, ...rest } = extra;
  recipe.nodes.push({ key, ...keep('nodes', key), canvas: c, kind, title, ...(text ? { text } : {}), x, y, width, height,
    style: { ...palettes[c], ...style }, ...rest });
  return key;
}
function text(c, key, title, body, x, y, width, height, style = {}, extra = {}) {
  return node(c, key, 'text', title, body, x, y, width, height, { style, ...extra });
}
function label(c, key, title, body, x, y, width, height, style = {}) {
  if (!title) { title = body; body = ''; style = { ...style, titleSize: style.fontSize ?? 17 }; }
  return text(c, key, title, body, x, y, width, height, { variant: 'label', fontFamily: 'serif', titleSize: 42, fontSize: 17, ...style });
}
function kicker(c, key, title, x = 50, y = 24) {
  return label(c, key, title, '', x, y, 1000, 32, { fontFamily: 'sans', titleSize: 13, textColor: c === 'family' || c === 'rome' ? '#8D5633' : '#D9B56B' });
}
function launch(c, key, title, target, body, x, y, width = 290, height = 130, style = {}) {
  return node(c, key, 'canvas-link', title, body, x, y, width, height, { target, style });
}
function link(c, key, from, to, title, extra = {}) {
  const { style, ...rest } = extra;
  recipe.links.push({ key, ...keep('links', key), canvas: c, source: from, target: to, kind: 'visual', relation: 'causal', label: title,
    style: { color: c === 'family' || c === 'rome' ? '#9B673B' : '#D4B36C', width: 2, ...style }, ...rest });
}
const down = { sourceAnchor: { x: .5, y: 1 }, targetAnchor: { x: .5, y: 0 } };
const across = { sourceAnchor: { x: 1, y: .5 }, targetAnchor: { x: 0, y: .5 } };
function sourceText(ids) {
  return [...new Set(ids)].map(id => {
    const entry = sources.get(id); assert.ok(entry, `Known source ${id}`);
    return `[${id}] ${entry.title}\n${entry.author ?? ''}\n${Array.isArray(entry.locators) ? entry.locators.join('; ') : entry.locators ?? ''}\n${entry.url}\n${entry.rights ?? ''}`;
  }).join('\n\n');
}
const imageRecords = new Map(media.map(record => [record.key, record]));
for (const key of ['zeus', 'circe', 'jupiter', 'persephone', 'athena']) {
  const record = imageRecords.get(key), bytes = await readFile(path.join(source, 'media', record.file));
  assert.equal(sha(bytes), record.sha256, `Preserve original image bytes: ${record.file}`);
  recipe.assets.push({ key: `art-${key}`, ...keep('assets', `art-${key}`), name: record.file, mime: record.mime, base64: bytes.toString('base64') });
}
function image(c, key, art, title, caption, x, y, width, height, style = {}, extra = {}) {
  return node(c, key, 'image', title, caption, x, y, width, height,
    { asset: `art-${art}`, style: { fontSize: 12, titleSize: 16, imageFit: 'contain', ...style }, ...extra });
}
function notes(c, title, prose, ids, artKeys, x = 1110, y = 875) {
  const provenance = artKeys.map(key => {
    const m = imageRecords.get(key);
    return `${m.title}\n${m.caption}\n${m.artist || m.culture}; ${m.date}; object ${m.accession}\n${m.credit}\n${m.url}\n${m.rights}\nOriginal SHA-256: ${m.sha256}`;
  }).join('\n\n');
  const content = `${title}\n\n${prose}\n\nPASSAGES\n\n${sourceText(ids)}\n\nIMAGE PROVENANCE\n\n${provenance}`;
  const key = `notes-${c}`;
  recipe.assets.push({ key, ...keep('assets', key), name: `${c}-reading-notes.txt`, mime: 'text/plain', base64: Buffer.from(content).toString('base64') });
  node(c, `document-${c}`, 'document', 'Passages & provenance', 'Full notes remain downloadable.', x, y, 250, 115,
    { asset: key, style: { fontSize: 11, titleSize: 15 } });
}
function back(c, x = 50, y = 900) {
  launch(c, `back-${c}`, 'Visual atlas', 'welcome', '', x, y, 190, 100, { titleSize: 15, fontSize: 12 });
}

canvas('welcome', '01 · Myths in relation', '#101D2C', '#263A4E');
canvas('family', '02 · A selected divine family', '#F5F0E4', '#DDD4BE');
canvas('odyssey', '03 · A story inside a story', '#122737', '#294355');
canvas('rome', '04 · Shared forms, different worlds', '#F4EFE5', '#D5C9B4');
canvas('persephone', '05 · A return with a cost', '#251F30', '#49354D');

// 1. A visual invitation with real navigation, not a wall of equal-sized cards.
kicker('welcome', 'welcome-kicker', 'ATLAS MERIDIAN  /  A VISUAL READING COLLECTION');
label('welcome', 'welcome-title', 'MYTHS\nIN RELATION', '', 50, 72, 625, 210, { titleSize: 66, textColor: '#F1DFBA' });
label('welcome', 'welcome-deck', '', 'Families. Stories. Objects.\nExplore how an ancient account connects them - and where another account disagrees.',
  55, 305, 570, 150, { fontFamily: 'sans', fontSize: 24 });
image('welcome', 'welcome-zeus', 'zeus', 'A probable Zeus', 'Greek marble, 3rd or 2nd century BCE.\nMet 26.60.46. Identification remains qualified.', 750, 70, 255, 380);
image('welcome', 'welcome-circe', 'circe', 'A story painted on a vase', 'Attic, ca. 440 BCE. Met 41.83.\nOdysseus pursues Circe; an image interprets myth.', 1035, 70, 300, 380);
const routes = [
  ['family', '01  /  Kinship', 'A compact family map.\nSelected descent and alternatives.', '#334B50'],
  ['odyssey', '02  /  Narrative', 'Follow the telling.\nThen enter its remembered voyage.', '#304365'],
  ['rome', '03  /  Comparison', 'Pair a shared form\nwith a different Roman setting.', '#59452F'],
  ['persephone', '04  /  Consequence', 'An abduction, a famine,\nand an incomplete return.', '#50374F'],
];
for (let i = 0; i < routes.length; i++) {
  const [target, title, body, fill] = routes[i];
  launch('welcome', `route-${target}`, title, target, body, 50 + i * 330, 530, 300, 190, { fill, titleSize: 24, fontSize: 18, borderWidth: 0 });
}
label('welcome', 'welcome-method', 'ONE TELLING IS NOT THE WHOLE TRADITION',
  'Every diagram names its selected account. Ancient artworks and later readings stay distinct.\nOpen the passage notes to inspect sources, locators, image credits and fuller context.',
  50, 765, 980, 150, { fontFamily: 'sans', titleSize: 18, fontSize: 17, textColor: '#BFCDD8' });
notes('welcome', 'Myths in relation: reading method', gods.editorialNotes.join('\n\n'), ['theogony-rhea', 'odyssey-09', 'hr-aeneid1'], ['zeus', 'circe'], 1090, 785);

// 2. Kinship: constrain the selection, make the relation explicit, segregate variants.
kicker('family', 'family-kicker', 'KINSHIP  /  SELECTED PASSAGES, NOT AN EXHAUSTIVE FAMILY TREE');
label('family', 'family-title', 'A dynasty survives its father', '', 50, 65, 1050, 100, { titleSize: 45 });
text('family', 'family-premise', 'The account on this map',
  'Hesiod lists six children of Cronus and Rhea. Rhea saves Zeus from being swallowed.\nTheogony 453-506.', 50, 190, 270, 160, { fill: '#E8E0CD', borderWidth: 0, titleSize: 18, fontSize: 16 });
text('family', 'family-cronus', 'CRONUS', 'Resists succession.', 400, 195, 215, 115, { titleSize: 26, fill: '#E4D0B0' });
text('family', 'family-rhea', 'RHEA', 'Saves the infant Zeus.', 700, 195, 215, 115, { titleSize: 26, fill: '#D8E2D4' });
image('family', 'family-zeus-art', 'zeus', 'A later image of Zeus', 'Met 26.60.46. Greek marble; probable identification.', 1110, 155, 220, 255, { fill: '#E8E0D1' });
label('family', 'family-children', 'Six children in Hesiod', '', 440, 362, 435, 68, { titleSize: 28, textAlign: 'center', textColor: '#6D5639' });
link('family', 'cr-parent', 'family-cronus', 'family-children', 'parent', { ...down, targetAnchor: { x: .2, y: 0 } });
link('family', 'rh-parent', 'family-rhea', 'family-children', 'parent', { ...down, targetAnchor: { x: .8, y: 0 } });
const siblings = [
  ['hestia', 'Hestia', 'Hearth and hospitality.'], ['demeter', 'Demeter', 'Grain and the human world.'],
  ['hera', 'Hera', 'Zeus\'s sister and wife.'], ['hades', 'Hades', 'The realm of the dead.'],
  ['poseidon', 'Poseidon', 'The sea and divine dignity.'], ['zeus', 'Zeus', 'Survives Cronus.\nAllots divine honors.'],
];
for (let i = 0; i < siblings.length; i++) {
  const [key, title, body] = siblings[i];
  text('family', `family-${key}`, title, body, 50 + i * 220, 495, 185, 130,
    { titleSize: 25, fontSize: 15, fill: key === 'zeus' ? '#E4D0B0' : '#FFFCF4' });
  link('family', `six-${key}`, 'family-children', `family-${key}`, '', { ...down,
    sourceAnchor: { x: .5, y: 1 }, points: [{ x: 657.5, y: 462 }, { x: 142.5 + i * 220, y: 462 }] });
}
text('family', 'family-persephone', 'Persephone', 'Daughter of Demeter and Zeus.\nTheogony 912-914.', 900, 730, 300, 125, { titleSize: 28, fill: '#DDE4D2' });
link('family', 'demeter-parent', 'family-demeter', 'family-persephone', 'mother', { ...down, targetAnchor: { x: .22, y: 0 }, points: [{ x: 362.5, y: 683 }, { x: 966, y: 683 }] });
link('family', 'zeus-parent', 'family-zeus', 'family-persephone', 'father', { ...down, targetAnchor: { x: .78, y: 0 }, points: [{ x: 1242.5, y: 683 }, { x: 1134, y: 683 }] });
text('family', 'family-variant', 'Keep an alternative separate',
  'Aphrodite arises from sea foam in Hesiod; Iliad 5 presents her as the daughter of Zeus and Dione. These are alternative tellings, not simultaneous extra parents.',
  50, 740, 740, 130, { fill: '#E5DFEA', border: '#9C87A5', titleSize: 23, fontSize: 16 });
back('family');
launch('family', 'family-return-story', 'Follow Persephone', 'persephone', '', 270, 900, 260, 100, { titleSize: 17 });
notes('family', 'Selected family and variants', gods.family.filter(f => ['cronus', 'rhea', ...siblings.map(s => s[0]), 'persephone', 'aphrodite'].includes(f.key))
  .map(f => `${f.name}\n${f.summary}\n${f.variantNotes || ''}`).join('\n\n'),
  ['theogony-rhea', 'theogony-household', 'theogony-sky', 'iliad-five', 'iliad-fifteen', 'hymn-hestia'], ['zeus'], 1090, 900);

// 3. Narrative structure: a performed retrospective tale is not a map of travel.
kicker('odyssey', 'odyssey-kicker', 'HOMER  /  THE ORDER OF THE ODYSSEY');
label('odyssey', 'odyssey-title', 'A story inside a story', '', 50, 65, 1120, 90, { titleSize: 49 });
label('odyssey', 'odyssey-deck', '', 'The book sequence follows a telling. Books 9-12 turn backward as Odysseus speaks to his hosts.',
  55, 163, 1250, 63, { fontFamily: 'sans', fontSize: 21, textColor: '#BDD7DC' });
const frames = [
  ['ithaca', '01  /  An absent father', 'Books 1-4\nTelemachus searches for news.'],
  ['hosts', '02  /  Among hosts', 'Books 5-8\nOdysseus reaches the Phaeacians.'],
  ['tale', '03  /  The remembered voyage', 'Books 9-12\nOdysseus tells his earlier travels.'],
  ['home', '04  /  Return and recognition', 'Books 13-24\nThe present homecoming resumes.'],
];
for (let i = 0; i < frames.length; i++) {
  const [key, title, body] = frames[i];
  text('odyssey', `frame-${key}`, title, body, 50, 265 + i * 155, 310, 115,
    { titleSize: 20, fontSize: 16, fill: key === 'tale' ? '#5A492F' : '#1C394A', border: key === 'tale' ? '#D4B36C' : '#476579' });
  if (i) link('odyssey', `frame-order-${i}`, `frame-${frames[i - 1][0]}`, `frame-${key}`, i === 2 ? 'he begins to tell' : i === 3 ? 'present resumes' : 'the focus shifts', down);
}
node('odyssey', 'remembered-frame', 'container', 'ODYSSEUS SPEAKING  /  BOOKS 9-12', '', 415, 260, 965, 610,
  { style: { fill: '#1B3541', border: '#6E8D90', titleSize: 20, radius: 18 } });
const episodeStyle = { titleSize: 23, fontSize: 16, fill: '#24434C', border: '#64837F' };
text('odyssey', 'episode-cyclops', 'Polyphemus', 'Book 9\nEscape from the Cyclops brings a dangerous claim to fame.', 445, 338, 265, 145, episodeStyle, { parent: 'remembered-frame' });
text('odyssey', 'episode-circe', 'Circe', 'Book 10\nTransformation, resistance, and an essential host.', 760, 338, 265, 145, episodeStyle, { parent: 'remembered-frame' });
text('odyssey', 'episode-dead', 'Consulting the dead', 'Book 11\nTiresias gives knowledge that does not remove the risk.', 760, 560, 265, 145, episodeStyle, { parent: 'remembered-frame' });
text('odyssey', 'episode-loss', 'Warnings and loss', 'Book 12\nSirens, sea dangers, the cattle of Helios, and shipwreck.', 445, 560, 265, 145, episodeStyle, { parent: 'remembered-frame' });
link('odyssey', 'episode-one-two', 'episode-cyclops', 'episode-circe', 'then', across);
link('odyssey', 'episode-two-three', 'episode-circe', 'episode-dead', 'seeks knowledge', down);
link('odyssey', 'episode-three-four', 'episode-dead', 'episode-loss', 'then', { sourceAnchor: { x: 0, y: .5 }, targetAnchor: { x: 1, y: .5 } });
link('odyssey', 'frame-enters', 'frame-tale', 'remembered-frame', 'narrates', { sourceAnchor: { x: 1, y: .5 }, targetAnchor: { x: 0, y: .65 }, style: { dashed: true } });
image('odyssey', 'circe-art', 'circe', 'Circe in an ancient image', 'Attic vase, ca. 440 BCE.\nMet 41.83; Persephone Painter.\nAn interpretation, not an illustration made for Homer.',
  1060, 338, 280, 367, { fill: '#203942', fontSize: 12 }, { parent: 'remembered-frame' });
label('odyssey', 'odyssey-boundary', 'Narrative order is not travel chronology',
  'This sequence follows the poem\'s frames. It makes no claim that each place can be securely pinned to a modern coastline.',
  448, 753, 867, 85, { fontFamily: 'sans', titleSize: 20, fontSize: 15, textColor: '#BDD7DC' });
recipe.nodes.find(n => n.key === 'odyssey-boundary').parent = 'remembered-frame';
back('odyssey', 50, 925);
const odyssey = epics.canvases.find(c => c.key === 'odyssey');
notes('odyssey', 'The Odyssey: narrative order and memory', odyssey.variantNotes.join('\n\n') + '\n\n' +
  odyssey.sections.flatMap(section => section.books).filter(book => [9, 10, 11, 12].includes(book.book)).map(book => `Book ${book.book}: ${book.title}\n${book.text}`).join('\n\n'),
  ['odyssey-01', 'odyssey-05', 'odyssey-08', 'odyssey-09', 'odyssey-10', 'odyssey-11', 'odyssey-12', 'odyssey-13', 'odyssey-24'], ['circe'], 1110, 925);

// 4. Comparison: familiar forms do not erase distinct civic and ritual contexts.
kicker('rome', 'rome-kicker', 'GREEK AND ROMAN WORLDS  /  APPROXIMATE CORRESPONDENCES');
label('rome', 'rome-title', 'Shared forms.\nDifferent worlds.', '', 50, 70, 680, 152, { titleSize: 48 });
text('rome', 'rome-premise', 'A comparison, not a rename', 'Poets and images make an identification useful. Local institutions, rites and audiences give it a different setting.',
  815, 80, 515, 130, { fill: '#E8DDC7', borderWidth: 0, titleSize: 22, fontSize: 18 });
image('rome', 'rome-zeus-art', 'zeus', 'Probable Zeus', 'Greek marble. Met 26.60.46.\n3rd or 2nd century BCE.', 50, 290, 225, 310, { fill: '#E7DDCC' });
node('rome', 'rome-zeus', 'reference', 'ZEUS', '', 315, 310, 285, 245,
  { target: 'family-zeus', style: { titleSize: 34, fontSize: 20, fill: '#DCE4DE', border: '#6C8C85' } });
text('rome', 'rome-jupiter', 'JUPITER', 'Capitoline cult joins Jupiter with Juno and Minerva. In Aeneid 1, he promises Rome an imperial future.',
  750, 310, 285, 265, { titleSize: 34, fontSize: 20, fill: '#E9D9B9', border: '#A2814D' });
image('rome', 'rome-jupiter-art', 'jupiter', 'Zeus or Jupiter', 'Roman bronze. Met 96.18.6.\n1st-3rd century CE. The museum retains both names.', 1090, 290, 240, 310, { fill: '#E7DDCC' });
link('rome', 'zeus-jupiter-comparison', 'rome-zeus', 'rome-jupiter', 'identified with', { ...across, relation: 'mutual', style: { width: 3 } });
image('rome', 'rome-athena-art', 'athena', 'Athena in Roman bronze', 'Roman, 2nd-3rd century CE.\nMet 74.51.5578. Object culture and depicted identity differ.', 50, 650, 225, 270, { fill: '#E7DDCC', fontSize: 11 });
text('rome', 'rome-athena', 'ATHENA', 'Armed protection, skilled making, and a city\'s identity.', 315, 685, 285, 175,
  { titleSize: 29, fontSize: 20, fill: '#DCE4DE', border: '#6C8C85' });
text('rome', 'rome-minerva', 'MINERVA', 'Capitoline cult and the workers celebrated at the Quinquatrus.\nFasti 3.809-848.', 750, 685, 285, 175,
  { titleSize: 29, fontSize: 19, fill: '#E9D9B9', border: '#A2814D' });
link('rome', 'athena-minerva-comparison', 'rome-athena', 'rome-minerva', 'shared attributes', { ...across, relation: 'mutual', style: { dashed: true } });
text('rome', 'rome-mars-note', 'Try the difficult pairing', 'Mars / Ares: compare war with Cato\'s prayer for a farm, its crops, herds and people.\nOn Agriculture 141.',
  1090, 685, 240, 195, { titleSize: 20, fontSize: 16, fill: '#E2DDE7', borderWidth: 0 });
back('rome', 315, 945);
notes('rome', 'Greek and Roman correspondences', ['jupiter', 'minerva', 'mars'].map(key => {
  const item = roman.get(key); return `${item.greek} / ${item.roman}\n${item.shared}\n\n${item.differences}\n\n${item.story}`;
}).join('\n\n'), ['hr-aeneid1', 'hr-met-minerva', 'hr-fasti3', 'hr-cato141', 'hymn-craft-sea-athena'], ['zeus', 'jupiter', 'athena'], 1090, 945);

// 5. A consequence map around an image: source-specific loss and negotiation.
kicker('persephone', 'persephone-kicker', 'THE HOMERIC HYMN TO DEMETER  /  AN INCOMPLETE RETURN');
label('persephone', 'persephone-title', 'Persephone:\na return with a cost', '', 50, 70, 915, 152, { titleSize: 49, textColor: '#F3DFBB' });
label('persephone', 'persephone-premise', '', 'The hymn marks her unwillingness. This is an abduction, not a voluntary romantic departure.',
  975, 87, 375, 125, { fontFamily: 'sans', fontSize: 20, textColor: '#D8C4D8' });
text('persephone', 'demeter-search', 'Demeter searches', 'Her daughter is missing. Hecate and Helios help reveal what happened.', 50, 310, 300, 160,
  { titleSize: 28, fill: '#3A4650', border: '#87989B' });
text('persephone', 'hades-seizes', 'Hades takes her', 'The earth opens. Persephone cries for help. Zeus has authorized the seizure.', 1050, 310, 300, 160,
  { titleSize: 28, fill: '#453248', border: '#AD86A5' });
image('persephone', 'persephone-art', 'persephone', 'Persephone',
  'Persephone rises toward Demeter with Hermes and Hekate. Attic, ca. 440 BCE. Met 28.57.23. An image interprets the story.',
  475, 295, 450, 405, { titleSize: 23, fontSize: 14, fill: '#413846', border: '#9C8498' });
text('persephone', 'demeter-famine', 'Demeter\'s famine', 'Demeter withholds grain. Human survival and divine honors are endangered.', 50, 575, 300, 160,
  { titleSize: 27, fill: '#3A4650', border: '#87989B' });
text('persephone', 'zeus-negotiates', 'Zeus sends Hermes', 'Return becomes possible, but the pomegranate prevents a permanent stay above.', 1050, 575, 300, 160,
  { titleSize: 27, fill: '#5B483F', border: '#B79978' });
link('persephone', 'search-famine', 'demeter-search', 'demeter-famine', 'withholds grain', down);
link('persephone', 'search-daughter', 'demeter-search', 'persephone-art', 'searches for', { ...across, targetAnchor: { x: 0, y: .24 } });
link('persephone', 'seizes-daughter', 'hades-seizes', 'persephone-art', 'abducts', { sourceAnchor: { x: 0, y: .5 }, targetAnchor: { x: 1, y: .24 } });
link('persephone', 'famine-response', 'demeter-famine', 'zeus-negotiates', 'forces negotiation', { ...down,
  targetAnchor: { x: .5, y: 1 }, points: [{ x: 200, y: 780 }, { x: 1200, y: 780 }] });
text('persephone', 'persephone-time', 'One third below. Two thirds above.',
  'The hymn reunites mother and daughter for part of each year. Its settlement gives no familiar modern six-seed arrangement.',
  405, 830, 620, 170, { titleSize: 30, fontSize: 19, fill: '#544F42', border: '#C0AB75' });
link('persephone', 'negotiated-return', 'zeus-negotiates', 'persephone-time', 'a divided year',
  { sourceAnchor: { x: 0, y: 1 }, targetAnchor: { x: 1, y: .5 }, points: [{ x: 1035, y: 750 }, { x: 1035, y: 902.5 }], style: { dashed: true } });
back('persephone', 50, 865);
notes('persephone', 'Persephone and Demeter: source-specific losses and returns', ['demeter', 'persephone'].map(key => {
  const profile = profiles.get(key); return `${profile.title}\n${profile.summary}\n\n${profile.stories.map(story => `${story.title}\n${story.text}`).join('\n\n')}`;
}).join('\n\n'), ['hymn-demeter-loss', 'hymn-demeter-eleusis', 'hymn-demeter-return', 'theogony-household'], ['persephone'], 1100, 850);

const result = await applyRecipe(base, recipe);
const project = commitSnapshots(result.project);
const issues = validateProject(project); assert.deepEqual(issues, []);
assert.equal(project.canvases.length, 5);
assert.ok(project.canvases.every(c => project.nodes.some(n => n.canvasId === c.id && n.kind === 'image')));
assert.ok(project.links.every(l => l.kind === 'visual' && typeof l.label === 'string'));
await mkdir(output, { recursive: true });
await writeFile(path.join(output, 'visual-atlas.recipe.json'), JSON.stringify(recipe, null, 2));
const template = structuredClone(recipe);
delete template.title;
for (const list of [template.canvases, template.assets, template.nodes, template.links]) for (const item of list) delete item.existingId;
const templateProof = applyRecipe(newProject('Template validation'), template);
assert.equal(templateProof.project.canvases.length, 6, 'Portable template appends five canvases and preserves the existing canvas');
assert.equal(templateProof.project.title, 'Template validation', 'Portable template preserves the receiving project title');
assert.equal(templateProof.project.entryCanvasId, templateProof.aliases.canvases.welcome);
assert.deepEqual(validateProject(commitSnapshots(templateProof.project)), []);
await writeFile(path.join(output, 'visual-atlas.template.recipe.json'), JSON.stringify(template, null, 2));
await writeFile(path.join(output, 'visual-atlas.project.json'), JSON.stringify(project));
await writeFile(path.join(output, 'visual-atlas.aliases.json'), JSON.stringify(result.aliases, null, 2));
await writeFile(path.join(output, 'visual-atlas.inspection.json'), JSON.stringify(await inspectProject(project), null, 2));
const manifest = { title: project.title, projectId: project.id, originalProjectHash: originalHash,
  canvases: project.canvases, nodes: project.nodes.length, links: project.links.length,
  imagePlacements: project.nodes.filter(n => n.kind === 'image').length,
  images: media.filter(m => recipe.assets.some(a => a.name === m.file)), aliases: result.aliases,
  sourceCorpus: 'content/greek-mythology', originalCollectionModified: false };
await writeFile(path.join(output, 'visual-atlas.manifest.json'), JSON.stringify(manifest, null, 2));

if (process.argv.includes('--publish')) {
  const [js, css] = await Promise.all(['viewer.js', 'scene.css'].map(name => readFile(path.join(root, 'dist', name), 'utf8')));
  const html = buildPublication(project, js, css, { includeExternalReferences: true });
  assert.equal(readPublication(html).nodes.length, project.nodes.length);
  await writeFile(path.join(output, 'visual-atlas.html'), html);
}
assert.equal(sha(await readFile(originalFile)), originalHash, 'The original mythology collection is untouched');
console.log(JSON.stringify({ projectId: project.id, canvases: project.canvases.length, nodes: project.nodes.length,
  links: project.links.length, imagePlacements: manifest.imagePlacements, output, published: process.argv.includes('--publish') }, null, 2));
