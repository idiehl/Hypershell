/** Stage spatial-disclosure compositions against captured saved/recovery baselines. Never writes to the host. */
import assert from 'node:assert/strict';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { applyRecipe, inspectProject } from '../../src/core/authoring.ts';
import { commitSnapshots } from '../../src/core/model.ts';
import { navigationIndex } from '../../src/core/navigation.ts';
import { validateProject } from '../../src/core/rules.ts';

const origin = 'http://127.0.0.1:5240', folder = 'output/spatial-reveal';
const ids = ['3d896c72-36b9-4900-adb6-a68934591233', '893ce9bf-c07e-457c-b4d9-33f001e8d0b6'];
const canonical = value => Array.isArray(value) ? value.map(canonical) : value && typeof value === 'object' ? Object.fromEntries(Object.keys(value).sort().map(key => [key, canonical(value[key])])) : value;
const hash = value => createHash('sha256').update(JSON.stringify(canonical(value))).digest('hex');
const fileHash = text => createHash('sha256').update(text).digest('hex');
const write = (path, value) => writeFile(path, JSON.stringify(value, null, 2) + '\n', 'utf8');
async function get(path, optional = false) {
  const response = await fetch(origin + path);
  if (optional && response.status === 404) return null;
  if (!response.ok) throw new Error(`${path}: ${response.status}`);
  return response.json();
}
await mkdir(folder, { recursive: true });
const baselines = [];
for (const id of ids) {
  const destination = `${folder}/${id}`; await mkdir(destination, { recursive: true });
  let baseline;
  try { baseline = JSON.parse(await readFile(`${destination}/baseline.json`, 'utf8')); }
  catch (error) {
    if (error.code !== 'ENOENT') throw error;
    const saved = await get(`/api/projects/${id}`), recovery = await get(`/api/projects/${id}/recovery`, true);
    if (recovery) assert.equal(recovery.baseRevision, saved.revision, 'Review a stale recovery before staging');
    baseline = { capturedAt: new Date().toISOString(), saved, recovery };
    await write(`${destination}/baseline.json`, baseline);
  }
  baselines.push({ ...baseline, destination, base: baseline.recovery?.project ?? baseline.saved });
}
if (process.argv.includes('--inspect')) {
  for (const { base, recovery } of baselines) {
    console.log(JSON.stringify({ id: base.id, title: base.title, recovery: recovery?.savedAt ?? null, canvases: base.id === ids[0] ? base.canvases.slice(0, 2) : base.canvases, entry: base.nodes.filter(node => node.canvasId === base.entryCanvasId), assets: base.assets.map(asset => ({ id: asset.id, name: asset.name, mime: asset.mime, length: asset.base64.length })), links: base.links.filter(link => link.canvasId === base.entryCanvasId) }, null, 2));
  }
  process.exit(0);
}

async function authorStable(base, recipe, destination) {
  const result = applyRecipe(base, recipe);
  let previous;
  try { previous = JSON.parse(await readFile(`${destination}/aliases.json`, 'utf8')); } catch (error) { if (error.code !== 'ENOENT') throw error; }
  const ids = new Map();
  for (const scope of ['canvases', 'roots', 'nodes', 'assets', 'links']) for (const [key, id] of Object.entries(result.aliases[scope])) if (previous?.[scope]?.[key]) ids.set(id, previous[scope][key]);
  const remap = id => ids.get(id) ?? id;
  result.project.entryCanvasId = remap(result.project.entryCanvasId);
  for (const canvas of result.project.canvases) { canvas.id = remap(canvas.id); canvas.rootId = remap(canvas.rootId); }
  for (const node of result.project.nodes) {
    node.id = remap(node.id); node.canvasId = remap(node.canvasId); if (node.parentId) node.parentId = remap(node.parentId);
    if (node.targetId) node.targetId = remap(node.targetId); if (node.assetId) node.assetId = remap(node.assetId);
    if (node.dependencyIds) node.dependencyIds = node.dependencyIds.map(remap);
  }
  for (const link of result.project.links) { link.id = remap(link.id); link.canvasId = remap(link.canvasId); link.sourceId = remap(link.sourceId); link.targetId = remap(link.targetId); }
  for (const asset of result.project.assets) asset.id = remap(asset.id);
  for (const scope of ['canvases', 'roots', 'nodes', 'assets', 'links']) for (const key of Object.keys(result.aliases[scope])) result.aliases[scope][key] = remap(result.aliases[scope][key]);
  await write(`${destination}/recipe.json`, recipe); await write(`${destination}/aliases.json`, result.aliases);
  return result;
}

function atlasRecipe(base) {
  const canvas = base.entryCanvasId, entry = base.nodes.filter(node => node.canvasId === canvas);
  const find = title => { const node = entry.find(node => node.title === title); assert.ok(node, `Missing atlas node: ${title}`); return node; };
  const parent = find('Four ways into the collection');
  const chapters = ['01 / Kinship', '02 / Odyssey', '03 / Rome', 'Story / Persephone'].map(find);
  const nodes = [
    { key: 'atlas-heading', existingId: parent.id, childLayout: 'spatial', x: 50, y: 505, width: 730, height: 120, collapsed: false, style: { variant: 'label', titleSize: 32, textColor: '#F1DFBA' } },
    { key: 'atlas-disclosure-guide', canvas, kind: 'text', title: 'Select once. Reveal twice.', text: 'Double-click a chapter to reveal its child nodes here. Use Open canvas when you want to enter a reading canvas.', x: 970, y: 495, width: 815, height: 130, style: { variant: 'label', textColor: '#BFCDD8', titleSize: 21, fontSize: 18 } },
  ];
  const links = [];
  chapters.forEach((chapter, index) => {
    nodes.push({ key: `chapter-${index}`, existingId: chapter.id, childLayout: 'spatial', placement: 'free', x: 53 + index * 450, y: 680, width: 390, height: 240, collapsed: true, style: { titleSize: 28, fontSize: 20, borderWidth: 1 } });
    const children = entry.filter(node => node.parentId === chapter.id);
    children.forEach((node, childIndex) => {
      nodes.push({ key: `chapter-${index}-child-${childIndex}`, existingId: node.id, childLayout: 'spatial', placement: 'near', x: 77 + index * 450, y: 955 + childIndex * 218, width: 342, height: 180, style: { titleSize: 21, fontSize: 17 } });
      links.push({ key: `chapter-${index}-reveal-${childIndex}`, source: chapter.id, target: node.id, kind: 'visual', relation: 'generic', direction: 'none', style: { color: chapter.style?.accent ?? '#D5B977', width: 1.5 } });
    });
  });
  // Keep the established source branch and its complete content together below the disclosure area.
  const source = find('Sources & reading principles'), directory = find('Explore the complete reference atlas');
  const sourceBranch = [source, ...navigationIndex(base).descendants(source.id)];
  for (const [index, node] of sourceBranch.entries()) nodes.push({ key: `source-${index}`, existingId: node.id, y: node.y - 130 });
  nodes.push({ key: 'reference-directory', existingId: directory.id, y: 1160 });
  return { version: 1, nodes, links };
}

function demoRecipe(base, atlas) {
  const canvas = 'spatial-children', entry = base.canvases.find(canvas => canvas.id === base.entryCanvasId);
  const images = ['A probable Zeus', 'A story painted on a vase'].map(title => atlas.nodes.find(node => node.canvasId === atlas.entryCanvasId && node.title === title));
  assert.ok(images.every(Boolean), 'Original illustrations are unavailable');
  const assets = images.map((node, index) => ({ key: `image-${index}`, ...Object.fromEntries(Object.entries(atlas.assets.find(asset => asset.id === node.assetId)).filter(([key]) => key !== 'id')) }));
  const notes = atlas.assets.find(asset => asset.name === 'welcome-reading-notes.txt');
  assert.ok(notes, 'Original image provenance is unavailable');
  assets.push({ key: 'image-provenance', name: 'spatial-example-original-image-provenance.txt', mime: notes.mime, base64: notes.base64 });
  const ink = '#283D3C', muted = '#526B64', teal = '#153D3A', mint = '#D0E5DC', rust = '#A85C39', paper = '#F1ECE2';
  const label = { variant: 'label', textColor: ink, fontFamily: 'sans', titleSize: 21, fontSize: 18 };
  const card = { fill: '#FBF7EF', border: '#CABBA4', textColor: ink, accent: rust, radius: 12, fontSize: 18, titleSize: 25 };
  const node = (key, kind, title, x, y, width, height, extra = {}) => ({ key, canvas, kind, title, x, y, width, height, style: card, ...extra });
  return {
    version: 1,
    canvases: [{ key: canvas, title: 'Spatial children', style: { background: paper, gridVisible: false } }],
    assets,
    nodes: [
      { key: 'spatial-launcher', canvas: entry.id, parent: entry.rootId, kind: 'canvas-link', title: 'Spatial children', target: canvas, text: 'Try connected ideas, edge-attached image tiles, and a child with no line. Double-click reveals; Open canvas navigates.', x: 70, y: 800, width: 560, height: 210, style: { fill: '#1C3A34', border: '#527264', textColor: '#EDF1DE', accent: '#D8B46B', titleSize: 30, fontSize: 19, radius: 14 } },
      node('spatial-eyebrow', 'text', 'MERIDIAN  /  THE SPACE BETWEEN IDEAS', 70, 35, 1250, 40, { text: '', style: { ...label, titleSize: 15, textColor: rust } }),
      node('spatial-heading', 'text', 'A node is a place to unfold.', 70, 92, 1500, 108, { text: '', style: { ...label, fontFamily: 'serif', titleSize: 61 } }),
      node('spatial-instructions', 'text', 'Select once to inspect. Double-click to reveal.', 75, 214, 1430, 90, { text: 'Everything appears in this canvas. The parent keeps its own shape; its children can connect, attach, or simply occupy another place.', style: { ...label, titleSize: 24, textColor: muted } }),
      node('spatial-back', 'canvas-link', 'First connections', 1720, 45, 300, 155, { target: entry.rootId, text: 'Open the starting canvas.', style: { ...card, fill: teal, textColor: '#F4F0DF', accent: '#D8B46B', border: '#557169', titleSize: 23 } }),
      node('relations-label', 'text', '01 / CONNECTED CHILDREN', 400, 360, 540, 45, { text: '', style: { ...label, titleSize: 17, textColor: rust } }),
      node('relations-parent', 'text', 'An idea, with room around it', 400, 430, 460, 320, {
        childLayout: 'spatial', collapsed: true,
        text: 'An idea can own evidence, a next step, and an association.\n\nDouble-click this tile. Three children find space nearby, and their lines describe the relationships.\n\nDouble-click again to hide the branch.',
        style: { ...card, fill: teal, border: '#557169', textColor: '#F3EFE2', accent: '#DDAD72', titleSize: 31, fontSize: 19 },
      }),
      node('relations-cause', 'text', 'Evidence points inward', 70, 455, 300, 225, { parent: 'relations-parent', placement: 'near', text: 'This causal relationship has an arrow toward the parent. The source and destination identities stay explicit while the visual direction can be chosen.', style: { ...card, fill: '#E9D8C4', titleSize: 24, fontSize: 18 } }),
      node('relations-next', 'text', 'Then comes a next step', 430, 820, 370, 190, { parent: 'relations-parent', placement: 'near', text: 'A chronological link gives the reading an order. Its arrow points from the parent toward this child.', style: { ...card, fill: '#DDE6DA', titleSize: 24 } }),
      node('relations-associated', 'text', 'Related, without a direction', 890, 470, 300, 210, { parent: 'relations-parent', placement: 'near', text: 'A generic line records an association. No arrow is needed, and the child still belongs to the same revealable branch.', style: { ...card, fill: '#E5DFC9', titleSize: 24 } }),
      node('tiles-label', 'text', '02 / AN ADJACENT COMPOSITION', 1280, 360, 680, 45, { text: '', style: { ...label, titleSize: 17, textColor: rust } }),
      node('tiles-parent', 'text', 'Words that grow into a composition', 1280, 430, 530, 440, {
        childLayout: 'spatial', collapsed: true,
        text: 'A body of text can gather images directly against its edges. Expand this tile: two photographs from the mythology collection attach beside and below it.\n\nThe images keep their original captions. Source notes preserve museum credits, uncertainty, and links.\n\nOne more child appears at the far left: a margin note with no line or shared edge. Ownership decides what hides together. Its visual relationship is a separate choice.',
        style: { ...card, fill: '#E9D8C4', border: '#C4A78D', titleSize: 32, fontSize: 19, radius: 0 },
      }),
      node('tiles-zeus', 'image', images[0].title, 1810, 430, 280, 440, { parent: 'tiles-parent', placement: 'edge', attachSide: 'right', asset: 'image-0', text: images[0].text, style: { ...card, fill: '#183632', textColor: mint, border: '#183632', accent: '#D3AE71', radius: 0, imageFit: 'contain', titleSize: 20, fontSize: 15 } }),
      node('tiles-circe', 'image', images[1].title, 1280, 870, 530, 340, { parent: 'tiles-parent', placement: 'edge', attachSide: 'bottom', asset: 'image-1', text: images[1].text, style: { ...card, fill: '#283D3C', textColor: '#F1E5D2', border: '#283D3C', accent: '#D3AE71', radius: 0, imageFit: 'contain', titleSize: 22, fontSize: 15 } }),
      node('tiles-free', 'text', 'A note at the margin', 75, 1120, 365, 235, { parent: 'tiles-parent', placement: 'free', text: 'A child can keep a distant position.\n\nI have no line or attached edge. Collapse the text tile to hide me; expand it to bring me back.', style: { ...card, fill: '#F1ECE2', borderWidth: 0, titleSize: 27, fontFamily: 'serif', fontSize: 20 } }),
      node('spatial-provenance', 'document', 'Image sources & original notes', 75, 875, 365, 160, { asset: 'image-provenance', style: { ...card, fill: '#E8E2D6', titleSize: 21, fontSize: 16 } }),
    ],
    links: [
      { key: 'evidence-inward', source: 'relations-parent', target: 'relations-cause', kind: 'visual', relation: 'causal', direction: 'reverse', style: { color: '#9D6547', width: 2 } },
      { key: 'sequence-outward', source: 'relations-parent', target: 'relations-next', kind: 'visual', relation: 'chronological', direction: 'forward', style: { color: '#50715C', width: 2 } },
      { key: 'association-plain', source: 'relations-parent', target: 'relations-associated', kind: 'visual', relation: 'generic', direction: 'none', style: { color: '#96865B', width: 2 } },
    ],
  };
}

const manifest = [];
for (const [index, captured] of baselines.entries()) {
  const { base, saved, recovery, destination } = captured;
  const recipe = index === 0 ? atlasRecipe(base) : demoRecipe(base, baselines[0].base);
  const authored = await authorStable(base, recipe, destination);
  // Update snapshots only for changed/new originals. Unrelated canvases retain their exact saved observations.
  const refreshed = commitSnapshots(authored.project), originalNodes = new Map(base.nodes.map(node => [node.id, node]));
  for (const node of authored.project.nodes) if (!originalNodes.has(node.id) || hash(node) !== hash(originalNodes.get(node.id))) authored.project.snapshots[node.id] = refreshed.snapshots[node.id];
  const candidate = authored.project;
  assert.deepEqual(validateProject(candidate), []);
  const changedExistingNodes = [];
  for (const original of base.nodes) {
    const edited = candidate.nodes.find(node => node.id === original.id); assert.ok(edited, `Lost original node ${original.id}`);
    for (const key of ['kind', 'title', 'text', 'targetId', 'assetId', 'value', 'valueType', 'closed', 'operation']) assert.deepEqual(edited[key], original[key], `Changed original ${key}: ${original.title}`);
    if (hash(edited) !== hash(original)) changedExistingNodes.push(original.id);
    if (index === 0 && original.canvasId !== base.entryCanvasId || index === 1) assert.deepEqual(edited, original, `Changed untouched node: ${original.title}`);
  }
  for (const original of base.links) assert.deepEqual(candidate.links.find(link => link.id === original.id), original, `Changed original link ${original.id}`);
  for (const original of base.assets) assert.deepEqual(candidate.assets.find(asset => asset.id === original.id), original, `Changed original attachment ${original.name}`);
  for (const original of base.canvases) assert.deepEqual(candidate.canvases.find(canvas => canvas.id === original.id), original, `Changed original canvas ${original.title}`);
  const candidateFile = `${destination}/candidate.json`; await write(candidateFile, candidate);
  await write(`${destination}/inspection.json`, inspectProject(candidate));
  const text = await readFile(candidateFile, 'utf8');
  manifest.push({
    id: base.id, title: base.title, capturedAt: captured.capturedAt,
    expectedRevision: saved.revision, expectedRecoverySavedAt: recovery?.savedAt ?? null,
    baselineFile: `${destination}/baseline.json`, baselineSavedHash: hash(saved), baselineRecoveryHash: hash(recovery),
    candidateFile, candidateHash: hash(candidate), candidateFileSha256: fileHash(text),
    aliasesFile: `${destination}/aliases.json`, recipeFile: `${destination}/recipe.json`,
    changedExistingNodes, addedNodeCount: candidate.nodes.length - base.nodes.length, addedLinkCount: candidate.links.length - base.links.length,
    addedAssetCount: candidate.assets.length - base.assets.length, unchangedCanvasCount: index === 0 ? base.canvases.length - 1 : base.canvases.length,
    reviewCanvasId: index === 0 ? base.entryCanvasId : authored.aliases.canvases['spatial-children'],
    reviewNodes: index === 0 ? { parent: authored.aliases.nodes['atlas-heading'], chapter: authored.aliases.nodes['chapter-0'] } : { relationships: authored.aliases.nodes['relations-parent'], tiles: authored.aliases.nodes['tiles-parent'], free: authored.aliases.nodes['tiles-free'] },
  });
}
await write(`${folder}/manifest.json`, manifest);
console.log(JSON.stringify(manifest.map(({ id, title, expectedRevision, expectedRecoverySavedAt, changedExistingNodes, addedNodeCount, addedLinkCount, addedAssetCount, unchangedCanvasCount, reviewCanvasId }) => ({ id, title, expectedRevision, expectedRecoverySavedAt, changedExistingNodeCount: changedExistingNodes.length, addedNodeCount, addedLinkCount, addedAssetCount, unchangedCanvasCount, reviewCanvasId })), null, 2));
