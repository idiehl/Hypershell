import assert from 'node:assert/strict';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { consolidateCanvases } from '../../src/core/consolidation.ts';
import { applyRecipe } from '../../src/core/authoring.ts';
import { commitSnapshots } from '../../src/core/model.ts';
import { validateProject } from '../../src/core/rules.ts';
import { navigationIndex } from '../../src/core/navigation.ts';
import { reworkGenealogy } from './genealogy.mjs';
import { reworkEpics } from './epics.mjs';

const folder = 'output/branch-atlas';
const baseline = JSON.parse(await readFile(`${folder}/baseline.json`, 'utf8'));
const canvasId = baseline.entryCanvasId, rootId = baseline.canvases.find(c => c.id === canvasId).rootId;
const oldIds = new Set([...baseline.nodes, ...baseline.links, ...baseline.assets, ...baseline.canvases].map(n => n.id));
const branch = prefix => { const c = baseline.canvases.find(c => c.title.startsWith(prefix)); assert.ok(c, prefix); return c.rootId; };
const flat = consolidateCanvases(baseline);
let project = flat.project;
// Old frames were page-sized. Their contents now disclose beside compact spatial tiles.
const oldParents = new Set(project.nodes.map(n => n.parentId));
for (const n of project.nodes) {
  if (n.kind === 'canvas') continue;
  n.childLayout = 'spatial'; n.placement = 'near';
  if (oldParents.has(n.id)) n.collapsed = true;
  if (n.kind === 'container') { n.width = 360; n.height = 200; }
}
const paper = '#F3EDE1', ink = '#263C3D', muted = '#536B67';
const card = { fill: '#FCF8EF', border: '#C6B79D', textColor: ink, accent: '#A27343', titleSize: 27, fontSize: 18, radius: 12 };
const recipe = { version: 1, title: 'Greek Mythology · A living atlas', canvases: [{ key: 'atlas', existingId: canvasId, title: 'Greek mythology', style: { background: paper, gridVisible: false } }], nodes: [] };
const add = (key, fields, existingId) => recipe.nodes.push({ key, canvas: canvasId, ...(existingId ? { existingId } : {}), ...fields });
add('stories', { kind: 'container', title: 'Stories & traditions', text: 'Gods in their stories. Greek and Roman correspondences. Themes, journeys and competing accounts.', x: 50, y: 635, width: 470, height: 215, collapsed: true, childLayout: 'spatial', placement: 'free', style: { ...card, fill: '#DFE6D6' } });
add('original-entrance', { kind: 'container', title: 'Earlier reading map', parent: branch('90 ·'), text: 'The earlier entrance and its annotations, preserved as part of this atlas.', x: 1150, y: 1000, width: 340, height: 180, collapsed: true, childLayout: 'spatial', placement: 'near', style: card });
for (const n of baseline.nodes) if (n.canvasId === canvasId && n.parentId === rootId) add(`old-entrance-${n.id}`, { parent: 'original-entrance' }, n.id);
const major = new Set([branch('10 ·'), branch('30 ·'), branch('40 ·'), branch('60 ·'), branch('90 ·')]);
for (const c of baseline.canvases) {
  if (c.id === canvasId || major.has(c.rootId)) continue;
  let parent = branch('90 ·');
  const title = c.title;
  if (/^(G\d|10\.|11 ·|02 ·)/.test(title)) parent = branch('10 ·');
  else if (/^(I\d|31\.)/.test(title)) parent = branch('30 ·');
  else if (/^(O\d|41\.|42)/.test(title)) parent = branch('40 ·');
  else if (/^H[1-4]\./.test(title)) parent = branch(title.slice(0, 2) + ' ·');
  else if (/^H[1-4] ·/.test(title)) parent = branch('60 ·');
  else if (/^20\./.test(title)) parent = branch('20 ·');
  else if (/^D\d/.test(title)) { const n = Number(title.slice(1, 3)); parent = branch(`20.${n <= 6 ? 1 : n <= 10 ? 2 : n <= 14 ? 3 : n <= 17 ? 4 : 5} ·`); }
  else if (/^R\d/.test(title)) parent = branch('50 ·');
  else if (/^45\./.test(title)) parent = branch('45 ·');
  else if (/^(20 ·|50 ·|45 ·|03 ·|04 ·|05 ·)/.test(title)) parent = 'stories';
  add(`branch-${c.id}`, { parent, style: card, placement: 'near' }, c.rootId);
}
add('eyebrow', { kind: 'text', title: 'ATLAS MERIDIAN  /  ONE CONNECTED CANVAS', text: '', x: 50, y: 28, width: 1270, height: 42, style: { variant: 'label', titleSize: 17, textColor: '#9B683B' } });
add('heading', { kind: 'text', title: 'Greek mythology, unfolding.', text: '', x: 50, y: 94, width: 1440, height: 98, style: { variant: 'label', fontFamily: 'serif', titleSize: 62, textColor: ink } });
add('invitation', { kind: 'text', title: 'Begin with a family. Follow a story.', text: 'Select a tile to inspect its contents. Double-click to reveal them here. Collapse a branch to clear the space again.', x: 55, y: 210, width: 1370, height: 105, style: { variant: 'label', titleSize: 25, fontSize: 20, textColor: muted } });
add('hesiod', { parent: rootId, title: 'Hesiod & early poetry', text: 'Theogony, Works and Days, the Shield of Heracles, and the surviving Catalogue fragments.', x: 570, y: 635, width: 470, height: 215, placement: 'free', style: { ...card, fill: '#E9DDC7' } }, branch('60 ·'));
add('sources', { parent: rootId, title: 'Sources & reading room', text: 'Edition notes, image credits, source registers and the scope of the collection.', x: 1090, y: 635, width: 470, height: 215, placement: 'free', style: { ...card, fill: '#E3E7DE' } }, branch('90 ·'));
add('footer', { kind: 'text', title: '529 genealogy records · 48 epic books · attributed sources throughout', text: 'Alternative accounts remain distinct. Reference appearances point to the same figure in a different family.', x: 55, y: 898, width: 1500, height: 95, style: { variant: 'label', titleSize: 20, fontSize: 17, textColor: muted } });
const authored = applyRecipe(project, recipe); project = authored.project;
const genealogy = reworkGenealogy(project, baseline, { canvasId, x: 50, y: 355 }); project = genealogy.project;
const epics = reworkEpics(project, baseline, { canvasId, epicPositions: { iliad: { x: 570, y: 355 }, odyssey: { x: 1090, y: 355 } } }); project = epics.project;
for (const [prefix, title, text, fill] of [
  ['10 ·', 'The genealogy', '529 figures, from the first powers to heroic families. Expand a god to reveal children and attributed parentage.', '#D9C298'],
  ['30 ·', 'The Iliad', '24 books in sequence. Each book unfolds into the complete translated text, passage by passage.', '#E8D2B9'],
  ['40 ·', 'The Odyssey', '24 books in sequence. Unfold the return journey, its text, and the stories nested within it.', '#C8DDD6'],
]) Object.assign(project.nodes.find(n => n.id === branch(prefix)), { title, text, parentId: rootId, placement: 'free', width: 470, height: 215, style: { ...card, fill, titleSize: 32 } });

// Recipes stay declarative; stable generated identities make subsequent visual revisions comparable.
const sets = { overview: authored.aliases, genealogy: genealogy.aliases, epics: epics.aliases };
const remapping = new Map();
for (const [scope, aliases] of Object.entries(sets)) for (const group of ['nodes', 'links', 'assets']) for (const [key, id] of Object.entries(aliases[group] ?? {})) {
  if (oldIds.has(id)) continue;
  const hex = createHash('sha256').update(`meridian-branch-atlas-v1/${scope}/${group}/${key}`).digest('hex');
  remapping.set(id, `${hex.slice(0,8)}-${hex.slice(8,12)}-4${hex.slice(13,16)}-a${hex.slice(17,20)}-${hex.slice(20,32)}`);
}
const remap = id => remapping.get(id) ?? id;
for (const n of project.nodes) { n.id = remap(n.id); if (n.parentId) n.parentId = remap(n.parentId); if (n.targetId) n.targetId = remap(n.targetId); }
for (const l of project.links) { l.id = remap(l.id); l.sourceId = remap(l.sourceId); l.targetId = remap(l.targetId); if (l.parentId) l.parentId = remap(l.parentId); }
const remapAll = value => typeof value === 'string' ? remap(value) : Array.isArray(value) ? value.map(remapAll) : value && typeof value === 'object' ? Object.fromEntries(Object.entries(value).map(([k,v]) => [k,remapAll(v)])) : value;
assert.deepEqual(validateProject(project), []);
assert.equal(project.canvases.length, 1); assert.equal(project.nodes.filter(n => n.kind === 'canvas-link').length, 0);
assert.ok(baseline.nodes.every(n => project.nodes.some(other => other.id === n.id)));
assert.ok(baseline.links.every(l => project.links.some(other => other.id === l.id)));
assert.deepEqual(project.assets, baseline.assets);
const nav = navigationIndex(project), visible = project.nodes.filter(n => n.kind !== 'canvas' && nav.isNodeVisible(n.id));
assert.equal(visible.length, 10, 'The atlas should open to six closed branches, three headings and one footer');
assert.equal(project.links.filter(l => nav.isLinkVisible(l)).length, 0);
project = commitSnapshots(project);
await mkdir(folder, { recursive: true });
const write = (name, data) => writeFile(`${folder}/${name}.json`, JSON.stringify(data, null, 2) + '\n');
await write('atlas', project); await write('aliases', remapAll(sets));
await write('overview-recipe', recipe); await write('genealogy-recipe', genealogy.recipe); await write('epics-recipe', epics.recipe);
await write('manifest', remapAll({ projectId: project.id, expectedRevision: baseline.revision, expectedRecoverySavedAt: null, oldCanvasBranches: flat.branches, before: { canvases: baseline.canvases.length, nodes: baseline.nodes.length, links: baseline.links.length }, after: { canvases: project.canvases.length, nodes: project.nodes.length, links: project.links.length, assets: project.assets.length }, genealogy: genealogy.report, epics: epics.report, visible: visible.map(n => ({ id:n.id, title:n.title })) }));
console.log(JSON.stringify({ canvases: project.canvases.length, nodes: project.nodes.length, links: project.links.length, visible: visible.length, assets: project.assets.length }));
