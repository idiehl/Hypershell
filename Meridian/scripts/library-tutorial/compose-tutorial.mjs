/** Native tutorial composition. Local staging only; never changes a live Work. */
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { randomUUID } from 'node:crypto';
import { applyRecipe, inspectProject } from '../../src/core/authoring.ts';
import { newProject, commitSnapshots } from '../../src/core/model.ts';
import { validateProject } from '../../src/core/rules.ts';
import { evaluate } from '../../src/core/engine.ts';
import { buildPublication } from '../../src/publication.ts';

const ROOT = fileURLToPath(new URL('../../', import.meta.url));
const OUT = path.join(ROOT, 'output/library-tutorial-20261021/tutorial');
const readJson = async file => JSON.parse((await readFile(file, 'utf8')).replace(/^\uFEFF/, ''));
const save = async (name, value) => writeFile(path.join(OUT, name), typeof value === 'string' ? value : JSON.stringify(value, null, 2) + '\n');
const T = { bg: '#101D25', panel: '#1A303A', soft: '#152832', ink: '#E5EEF1', muted: '#B6C8CE', accent: '#E6BA73', line: '#728F96' };
const PARTS = [
  ['overview', 'Start here', 'Learn by doing', 'A guided, editable tour of Meridian: navigate, compose, connect, run, save, and share.'],
  ['navigate', '01', 'Navigation & selection', 'Learn where you are, find a node, choose a selection, and move through Works without losing your place.'],
  ['author', '02', 'Create, edit & resize', 'Separate a node’s search Tag from its visible title and content. Practice creation, direct editing, movement, and size.'],
  ['appearance', '03', 'Appearance & backgrounds', 'Use shapes, typography, spacing, background paint, and portal styles deliberately. Hover the examples and compare.'],
  ['patterns', '04', 'Containers & disclosure', 'Choose containment and layout separately. Open nested examples and compare equal tiles with free placement.'],
  ['tables', '05', 'Tables & scrolling Windows', 'Address cells, keep a large set in a small viewport, and distinguish child scrolling from a document’s own preview.'],
  ['media', '06', 'Media, documents & Galleries', 'Embed original files, control image presentation, browse equal-sized Gallery pages, and open full attachments.'],
  ['routes', '07', 'Links, Pivots & Joints', 'Describe relationships with clear routes. Bend a route with a Pivot; use a Joint only for a real shared connection.'],
  ['logic', '08', 'Live values & operations', 'Press Run, change exposed inputs, and follow typed values through numeric and Boolean systems.'],
  ['reuse', '09', 'References, Modules & groups', 'Observe an original, reuse an independent module, and distinguish spatial grouping from locked contacts.'],
  ['save', '10', 'Saving, recovery & checkpoints', 'Know which changes are saved, recover safely, and retain independent copies before experiments.'],
  ['publish', '11', 'Export, publication & portability', 'Compare an editable Work file with a portable interactive HTML publication, and keep original downloads accessible.'],
];
await mkdir(OUT, { recursive: true });
let base; try { base = await readJson(path.join(OUT, 'base.json')); } catch (error) { if (error.code !== 'ENOENT') throw error; base = newProject('Meridian Tutorial · Learn by doing'); await save('base.json', base); }
let prior; try { prior = { project: await readJson(path.join(OUT, 'draft.json')), aliases: await readJson(path.join(OUT, 'aliases.json')) }; } catch (error) { if (error.code !== 'ENOENT') throw error; }
let guide = { sections: [], features: [] }; try { guide = await readJson(path.join(ROOT, 'content/meridian-tutorial/guide.json')); } catch (error) { if (error.code !== 'ENOENT') throw error; }
let labData = { sections: [], features: [] }; try { labData = await readJson(path.join(ROOT, 'content/meridian-tutorial/labs.json')); } catch (error) { if (error.code !== 'ENOENT') throw error; }
const recipe = { version: 1, title: base.title, entryCanvas: 'overview', canvases: [], nodes: [], links: [], routes: [], assets: [], ...(prior ? { bindings: prior.aliases } : {}) };
const common = { fontFamily: 'sans', fontSize: 19, titleSize: 26, fill: T.panel, border: T.line, textColor: T.ink, accent: T.accent, textAlign: 'left', shape: 'rectangle', radius: 0, padding: 16, childPadding: 8, contentGap: 12, contentSizing: 'fixed' };
function add(work, key, title, text, x, y, width, height, extra = {}) {
  const full = `${work}/${key}`;
  recipe.nodes.push({ key: full, canvas: work, kind: 'media', title, tag: `Tutorial · ${work} · ${title || key}`.slice(0, 250), text, contentType: 'text', x, y, width, height, sizeMode: 'fixed', childPosition: 'manual', ...extra, style: { ...common, ...extra.style } });
  return full;
}
function label(work, key, title, text, x, y, width, height, extra = {}) { return add(work, key, title, text, x, y, width, height, { ...extra, style: { shape: 'none', variant: 'label', padding: 0, borderWidth: 0, ...extra.style } }); }
function portal(work, key, title, target, x, y, width = 300, height = 190, extra = {}) { return add(work, key, title, '', x, y, width, height, { ...extra, kind: 'canvas-link', target, style: { textAlign: 'center', titleSize: 22, portalStyle: 'doorway', portalColor: T.accent, fill: T.soft, ...extra.style } }); }
function link(work, key, source, target, extra = {}) { recipe.links.push({ key: `${work}/${key}`, canvas: work, source, target, relation: 'generic', direction: 'forward', ...extra, style: { color: T.line, width: 2, lineStyle: 'solid', ...extra.style } }); return `${work}/${key}`; }
for (const [index, [key, number, title, subtitle]] of PARTS.entries()) {
  recipe.canvases.push({ key, ...(key === 'overview' ? { existingId: base.entryCanvasId } : {}), title: key === 'overview' ? 'Meridian Tutorial · Start here' : `${number} · ${title}`, snapping: { stickyEdges: false, smartAlign: false, snapToGrid: false }, style: { background: T.bg, gridVisible: false, portalColor: T.accent, portalStyle: 'doorway' } });
  label(key, 'eyebrow', `MERIDIAN / LEARN BY DOING${key === 'overview' ? '' : ` / PART ${number}`}`, '', 80, 35, 1600, 35, { style: { titleSize: 15, textColor: T.accent } });
  label(key, 'heading', title, subtitle, 80, 95, 1750, 170, { style: { titleSize: 46, fontSize: 21 } });
  if (key !== 'overview') {
    portal(key, 'hub', 'Tutorial home', 'overview', 1990, 40, 220, 170);
    if (index > 1) portal(key, 'previous', 'Previous part', PARTS[index - 1][0], 2240, 40, 150, 170, { style: { titleSize: 19 } });
    if (index < PARTS.length - 1) portal(key, 'next', 'Next part', PARTS[index + 1][0], 2420, 40, 150, 170, { style: { titleSize: 19 } });
  }
}

// A directory is an ordered learning path. The actual lessons use native objects.
label('overview', 'welcome', 'Use the real tools, on real objects', 'Open a part through its portal. Read one lesson at a time in the left Window, then try the original objects on the right. In the editor, make a copy before extended experiments. In a portable publication, editing tools are unavailable but exposed controls, disclosure, links, and downloads still work.', 80, 310, 2160, 175, { style: { titleSize: 30, fontSize: 21 } });
const lanes = [
  { title: '1 / Get comfortable', keys: ['navigate', 'author', 'appearance'] },
  { title: '2 / Compose a Work', keys: ['patterns', 'tables', 'media', 'routes'] },
  { title: '3 / Make it useful', keys: ['logic', 'reuse', 'save', 'publish'] },
];
lanes.forEach((lane, column) => {
  const x = 80 + column * 760;
  const parent = add('overview', `lane-${column}`, lane.title, '', x, 560, 650, 1090, { kind: 'container', collapsed: false, childLayout: 'contained', childArrangement: 'column', containerVariant: 'vertical', grid: { columns: 1, rows: lane.keys.length, cellWidth: 620, cellHeight: 235, gap: 0, showHeaders: false }, style: { titleSize: 29, padding: 12, childPadding: 8, fill: T.soft } });
  lane.keys.forEach((work, index) => { const part = PARTS.find(part => part[0] === work); portal('overview', `open-${work}`, `${part[1]} · ${part[2]}`, work, x, 600 + index * 250, 620, 235, { parent, childPosition: 'auto', cell: { column: 1, row: index + 1 } }); });
});

const featureMap = new Map((guide.features ?? []).map(feature => [feature.id, feature]));
for (const feature of labData.features ?? []) if (!featureMap.has(feature.id)) featureMap.set(feature.id, feature);
for (const id of labData.featureIds ?? []) if (!featureMap.has(id)) featureMap.set(id, { id, label: id, where: 'Native tutorial laboratory', availability: id.includes('pending') ? 'unavailable' : 'installed', verificationSource: 'scripts/library-tutorial/lab-fixtures.mjs', coverageSection: labData.notes?.find(note => note.featureIds.includes(id))?.work });
const allFeatures = [...featureMap.values()];
const notes = [];
for (const section of [...guide.sections ?? [], ...labData.sections ?? []]) {
  const work = section.key ?? section.work;
  if (!PARTS.some(part => part[0] === work)) throw new Error(`Unknown lesson section ${work}`);
  for (const note of section.steps ?? section.notes ?? []) notes.push({ work, ...note });
}
let labResult = {};
try {
  const module = await import(pathToFileURL(path.join(ROOT, 'scripts/library-tutorial/lab-fixtures.mjs')).href);
  if (module.addTutorialLabs) labResult = await module.addTutorialLabs({ recipe, add, label, link, portal, theme: T, workIds: Object.fromEntries(PARTS.map(part => [part[0], part[0]])) }) ?? {};
  else if (module.buildTutorialLabs) { labResult = module.buildTutorialLabs(T); recipe.nodes.push(...labResult.nodes ?? []); recipe.links.push(...labResult.links ?? []); recipe.routes.push(...labResult.routes ?? []); }
} catch (error) { if (error.code !== 'ERR_MODULE_NOT_FOUND') throw error; }
for (const note of labResult.notes ?? []) if (!notes.some(item => item.work === note.work && item.key === note.key)) notes.push(note);

// Remote links preserve a named GUID relationship without drawing another edge.
const relatedSource = add('reuse', 'related-source', 'Related account', 'A related source record linked conceptually from Evidence in the Links lesson. This GUID relationship does not draw a line or fetch an external resource.', 2260, 2250, 650, 300);
const evidence = recipe.nodes.find(node => node.key === 'routes/route-source');
if (!evidence) throw new Error('The remote-link lesson requires the original Evidence fixture.');
const priorEvidence = prior?.project.nodes.find(node => node.id === prior?.aliases.nodes['routes/route-source']);
evidence.remoteLinks = [{ id: priorEvidence?.remoteLinks?.[0]?.id ?? randomUUID(), targetId: relatedSource, relation: 'generic', label: 'Related source record' }];

// Native practice objects for navigation, editing, and appearance.
const navParent = add('navigate', 'practice-parent', 'A small hierarchy', 'Double-click this Container. The inner branch can expand independently.', 850, 370, 720, 510, { kind: 'container', collapsed: true, childLayout: 'contained', containerVariant: 'contained', childArrangement: 'free', containerSizes: { collapsed: { width: 720, height: 260, sizeMode: 'fixed' }, expanded: { width: 720, height: 510, sizeMode: 'fixed' } } });
const navChild = add('navigate', 'practice-child', 'One generation deeper', 'The sidebar lists this child beneath its parent.', 885, 465, 355, 310, { parent: navParent, kind: 'container', collapsed: true, childLayout: 'contained', containerVariant: 'contained', childArrangement: 'free' });
add('navigate', 'practice-grandchild', 'Searchable grandchild', 'Use the Work panel’s Target control to find this original.', 910, 550, 300, 180, { parent: navChild, tag: 'Tutorial search beacon' });
add('navigate', 'peer', 'A peer, not a child', 'Single-click to select. Shift-click another node to build a selection. Drag the background to pan.', 1710, 370, 520, 260);
portal('navigate', 'practice-portal', 'Enter the creation lesson', 'author', 1710, 780, 520, 300);

add('author', 'editable', 'Visible title', 'This is the displayed content. Edit the title or this passage directly on the Work. Its search Tag is a separate property.', 850, 370, 620, 300, { tag: 'Tutorial · author · Find me by my Tag' });
add('author', 'blank', '', '', 1640, 370, 420, 300, { tag: 'Tutorial · author · A named empty shape', style: { fill: '#244553', borderWidth: 3 } });
label('author', 'blank-caption', 'A Tag without filler text', 'The empty shape above still has a searchable name. No Block type is required.', 1640, 720, 420, 150, { style: { titleSize: 24, fontSize: 19 } });
const richText = 'Bold, italic, and underlined passages can share one text body. Undo restores your last edit.';
add('author', 'rich-text', 'Format a passage', richText, 850, 850, 620, 300, { textRuns: [{ start: 0, end: 4, style: { bold: true, color: T.accent } }, { start: 6, end: 12, style: { italic: true } }, { start: 18, end: 28, style: { underline: true } }] });
add('author', 'locked', 'Position locked', 'Try dragging this node, then use its context menu to Unlock Position. The lock concerns position, not its meaning or content.', 1640, 950, 520, 260, { positionLocked: true });

const shapes = ['rectangle', 'square', 'rounded', 'circle', 'ellipse', 'triangle', 'diamond', 'pentagon', 'hexagon', 'octagon', 'none'];
shapes.forEach((shape, index) => {
  const x = 850 + index % 4 * 330, y = 380 + Math.floor(index / 4) * 340;
  const round = ['circle', 'square'].includes(shape);
  add('appearance', `shape-${shape}`, shape === 'none' ? 'No backdrop' : shape[0].toUpperCase() + shape.slice(1), '', x, y, round ? 250 : 280, 250, { style: { shape, titleSize: 22, textAlign: 'center', padding: shape === 'triangle' ? 36 : 20, ...(shape === 'rounded' ? { radius: 35 } : {}) } });
});
add('appearance', 'hover', 'Hover to reveal the background', 'Text remains visible while the backdrop appears on hover.', 850, 1460, 590, 230, { style: { backgroundDisplay: 'hover', fill: '#4D4736', border: T.accent } });
label('appearance', 'transparent', 'A transparent label', 'This label uses the Work’s own background. Transparency changes presentation; it does not create another node type.', 1550, 1460, 590, 230, { style: { titleSize: 27, textColor: T.ink } });
['doorway', 'passage', 'wormhole', 'vortex'].forEach((style, index) => portal('appearance', `portal-${style}`, style[0].toUpperCase() + style.slice(1), 'overview', 850 + index * 330, 1830, 280, 260, { style: { portalStyle: style, portalColor: index % 2 ? '#82CFC5' : T.accent } }));
label('appearance', 'portal-caption', 'Same destination, four styles', 'Hover a portal to reveal its destination. Each button has its own color override; Work defaults apply when an override is absent.', 850, 2150, 1270, 150, { style: { titleSize: 27 } });
label('appearance', 'paint-heading', 'Backgrounds · browse the collection', 'The same frame can use a plain fill, procedural paint, or an embedded image. Gallery arrows change the example without changing its allocation.', 850, 2370, 1270, 125, { style: { titleSize: 27 } });
const paints = add('appearance', 'background-gallery', '', '', 850, 2550, 1270, 650, { kind: 'container', collapsed: false, childLayout: 'contained', containerVariant: 'gallery', childArrangement: 'free', style: { padding: 8, childPadding: 8 } });
for (const [index, pattern] of ['none', 'dots', 'grid', 'isometric', 'linen', 'contours', 'aurora', 'stars', 'image'].entries()) {
  add('appearance', `paint-${pattern}`, pattern === 'image' ? 'Embedded image background' : `${pattern[0].toUpperCase() + pattern.slice(1)} background`, 'Background paint is independent of the displayed text and content.', 865, 2565, 1230, 560, { parent: paints, ...(pattern === 'image' ? { backgroundAsset: 'sample-image' } : {}), style: { textAlign: 'center', titleSize: 32, fontSize: 22, backgroundPattern: pattern === 'image' ? 'none' : pattern, backgroundTint: T.accent, backgroundOpacity: pattern === 'image' ? .2 : .4, backgroundScale: 72, backgroundFit: 'tile' } });
}

// Original local attachments, not remote embeds. The CSV can be opened in full.
const csv = 'Item,Category,Count\r\nMedia,Content,3\r\nContainer,Structure,4\r\nPivot,Routing,2\r\nOutput,Runtime,1\r\n';
recipe.assets.push({ key: 'sample-table', name: 'tutorial-sample.csv', mime: 'text/csv', base64: Buffer.from(csv).toString('base64') });
const brand = await readFile(path.join(ROOT, 'assets/brand/meridian.png'));
recipe.assets.push({ key: 'sample-image', name: 'Meridian bundled brand image.png', mime: 'image/png', base64: brand.toString('base64') });
try { const audio = await readFile(path.join(OUT, 'tutorial-tone.wav')); recipe.assets.push({ key: 'sample-audio', name: 'tutorial-tone.wav', mime: 'audio/wav', base64: audio.toString('base64') }); add('media', 'audio', 'Audio · preserved original', '', 2250, 1120, 620, 250, { kind: 'document', asset: 'sample-audio', contentType: 'asset' }); label('media', 'audio-caption', 'Open or download the file', 'Audio is retained as an attachment. This version does not add an inline audio player; open the original in its usual application.', 2250, 1410, 620, 170, { style: { titleSize: 24, fontSize: 18 } }); } catch (error) { if (error.code !== 'ENOENT') throw error; }
try { const video = await readFile(path.join(OUT, 'tutorial-motion.webm')); if (video.length > 1000) { recipe.assets.push({ key: 'sample-video', name: 'tutorial-motion.webm', mime: 'video/webm', base64: video.toString('base64') }); add('media', 'video', 'Video · native playback', '', 2250, 370, 850, 550, { asset: 'sample-video', contentType: 'asset' }); } } catch (error) { if (error.code !== 'ENOENT') throw error; }
add('media', 'image-contain', 'Image · contain', '', 850, 370, 500, 380, { asset: 'sample-image', contentType: 'asset', style: { imageFit: 'contain' } });
add('media', 'image-cover', 'Image · cover', '', 1510, 370, 500, 380, { asset: 'sample-image', contentType: 'asset', style: { imageFit: 'cover' } });
label('media', 'image-credit', 'One local image, two fits', 'Bundled Meridian brand artwork. Contain retains the complete image; Cover fills its available frame and can crop its edges.', 850, 820, 1160, 150, { style: { titleSize: 26 } });
add('tables', 'sample-csv', 'Original CSV attachment', '', 850, 2400, 1150, 450, { kind: 'document', asset: 'sample-table', contentType: 'asset', style: { titleSize: 28 } });
label('tables', 'csv-caption', 'Spreadsheet file ≠ Spreadsheet Display Pattern', 'Open the original attachment to inspect or download all rows. The addressable Meridian pattern above contains real nodes with their own GUIDs.', 850, 2910, 1150, 155, { style: { titleSize: 27 } });
add('media', 'external-link', 'An ordinary hyperlink', '', 850, 1120, 500, 250, { contentType: 'link', hyperlinks: [{ url: 'https://example.com/', label: 'Open the external example website' }], style: { fill: T.ink, textColor: T.bg } });
portal('media', 'work-link', 'A portal to another Work', 'patterns', 1510, 1120, 500, 250);

const practice = [
  ['save', 'Version A', 'Change this text, wait for automatic saving, then return after reopening. Use a checkpoint before replacing a useful arrangement.'],
  ['publish', 'Viewer practice', 'This original remains a normal editable node in Meridian. Its publication is a separate portable reading surface.'],
];
for (const [work, title, text] of practice) {
  add(work, 'practice-note', title, text, 850, 400, 700, 360);
  add(work, 'practice-child', 'A disclosure to preserve', 'Expand, collapse, save, and reopen to observe the retained state.', 1730, 400, 620, 360, { kind: 'container', collapsed: true, childLayout: 'contained', containerVariant: 'contained', childArrangement: 'free' });
  add(work, 'inside', 'An original child', 'The same child is revealed each time; disclosure does not generate a duplicate.', 1760, 505, 555, 210, { parent: `${work}/practice-child` });
}

// Lesson text is read at a fixed useful scale in a bounded Window.
for (const [work] of PARTS.filter(part => part[0] !== 'overview')) {
  const steps = notes.filter(note => note.work === work);
  if (!steps.length) continue;
  label(work, 'lesson-index', 'READ · THEN TRY', `${steps.length} lessons. Scroll inside this Window; the demonstrations keep their own positions. Use the Work panel to search every original note.`, 80, 300, 620, 115, { style: { titleSize: 18, fontSize: 18, textColor: T.muted } });
  const panel = add(work, 'lesson-desk', '', '', 80, 460, 650, 760, { kind: 'container', collapsed: false, childLayout: 'contained', containerVariant: 'window', childArrangement: 'grid', grid: { columns: 1, rows: steps.length, cellWidth: 630, cellHeight: 730, windowColumns: 1, windowRows: 1, scrollDirection: 'vertical', showHeaders: false }, style: { padding: 8, childPadding: 8, fill: T.soft } });
  steps.forEach((note, index) => add(work, `lesson-${note.key ?? index + 1}`, `${String(index + 1).padStart(2, '0')} · ${note.title}`, `${note.text}${note.tryIt ? `\n\nTRY IT\n${note.tryIt}` : ''}`, 0, 0, 630, 730, { parent: panel, childPosition: 'auto', cell: { column: 1, row: index + 1 }, style: { fontSize: 20, titleSize: 29, padding: 22 } }));
}
const packet = ['# Meridian Tutorial — Learn by doing', '', 'Authoring and exercises for the installed local Meridian build, October 21, 2026.', '', 'Use the native Works for live examples. Make a copy before extended experiments. A portable publication exposes reading and runtime interaction, not authoring tools.', '', ...PARTS.flatMap(([work, number, title, subtitle]) => [`## ${number} · ${title}`, '', subtitle, '', ...notes.filter(note => note.work === work).flatMap(note => [`### ${note.title}`, '', note.text, '', ...note.tryIt ? [`Try it: ${note.tryIt}`, ''] : []])])].join('\n');
recipe.assets.push({ key: 'tutorial-guide', name: 'Meridian Tutorial — full guide.md', mime: 'text/markdown', base64: Buffer.from(packet).toString('base64') });
add('save', 'guide-document', 'Full tutorial guide', '', 850, 920, 1500, 420, { kind: 'document', asset: 'tutorial-guide', contentType: 'asset', style: { fontSize: 16, titleSize: 28 } });
add('publish', 'guide-document', 'Download an original attachment', '', 850, 920, 1500, 420, { kind: 'document', asset: 'tutorial-guide', contentType: 'asset', style: { fontSize: 16, titleSize: 28 } });
portal('overview', 'guide', 'Full tutorial guide', 'save', 1990, 40, 220, 170);
const result = applyRecipe(prior?.project ?? base, recipe), project = commitSnapshots(result.project);
const issues = validateProject(project); if (issues.length) throw new Error(JSON.stringify(issues));
const [viewer, css] = await Promise.all(['viewer.js', 'scene.css'].map(name => readFile(path.join(ROOT, 'dist', name), 'utf8')));
const coverage = { features: allFeatures, lessonCount: notes.length, byWork: Object.fromEntries(PARTS.map(([work]) => [work, notes.filter(note => note.work === work).length])), notes: notes.map(note => ({ work: note.work, key: note.key, title: note.title, featureIds: note.featureIds ?? [] })), fixtureKeys: labResult.fixtureKeys ?? [], expected: labResult.expected ?? {} };
await Promise.all([save('recipe.json', recipe), save('draft.json', project), save('aliases.json', result.aliases), save('inspection.json', inspectProject(project)), save('publication.html', buildPublication(project, viewer, css)), save('guide.md', packet), save('coverage.json', coverage), save('evaluation.json', evaluate(project))]);
console.log(JSON.stringify({ id: project.id, works: project.canvases.length, nodes: project.nodes.length, links: project.links.length, assets: project.assets.length, lessons: notes.length, features: allFeatures.length, directory: OUT }));
