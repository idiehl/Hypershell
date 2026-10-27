import test from 'node:test';
import assert from 'node:assert/strict';
import { BACKGROUND_PATTERNS, isBackgroundAsset, isCanvasStyle, isNodeStyle } from '../src/core/appearance.ts';
import { addCanvas, addNode, commitSnapshots, forkProject, newProject } from '../src/core/model.ts';
import { createSnippet } from '../src/core/selection.ts';
import { createModule, instantiateModule } from '../src/core/modules.ts';
import { isProjectShape, validateProject } from '../src/core/rules.ts';
import { applyRecipe, inspectProject } from '../src/core/authoring.ts';
import { buildPublication, createPublicationPayload, readPublication } from '../src/publication.ts';
import type { BackgroundStyle, MeridianAsset } from '../src/core/types.ts';

const png = (id: string, tag = ''): MeridianAsset => ({ id, name: `${id}.png`, mime: 'image/png', base64: Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), Buffer.from(tag)]).toString('base64') });

test('background vocabulary rejects CSS, URLs, unsupported patterns and unbounded settings on both surfaces', () => {
  for (const pattern of BACKGROUND_PATTERNS) {
    const style: BackgroundStyle = { backgroundPattern: pattern, backgroundScale: 48, backgroundTint: '#abcdef', backgroundOpacity: 0.25, backgroundFit: 'tile', backgroundAssetId: 'local-image' };
    assert.ok(isCanvasStyle(style)); assert.ok(isNodeStyle(style));
  }
  for (const style of [{ backgroundPattern: 'url(external)' }, { backgroundTint: '#fff' }, { backgroundAssetId: 'https://remote/image.png' },
    { backgroundAssetId: '../image' }, { backgroundAssetId: 'a'.repeat(97) }, { backgroundScale: 7 }, { backgroundScale: 513 },
    { backgroundScale: Infinity }, { backgroundOpacity: -0.01 }, { backgroundOpacity: 1.01 }, { backgroundOpacity: NaN },
    { backgroundOpacity: '1' }, { backgroundFit: 'stretch' }, { backgroundImage: 'data:image/svg+xml,...' }]) {
    assert.equal(isCanvasStyle(style), false, JSON.stringify(style)); assert.equal(isNodeStyle(style), false, JSON.stringify(style));
  }
});

test('raster background checks reject scripts, MIME spoofing and malformed base64 and invalidate cached edits', () => {
  const asset = png('image'); assert.ok(isBackgroundAsset(asset));
  asset.base64 = Buffer.from('<svg onload="alert(1)"></svg>').toString('base64'); assert.equal(isBackgroundAsset(asset), false);
  asset.base64 = png('image').base64; asset.mime = 'image/svg+xml'; assert.equal(isBackgroundAsset(asset), false);
  asset.mime = 'image/png'; assert.ok(isBackgroundAsset(asset));
  for (const encoded of ['iVBORw0KGgo=\n', 'iVBORw0KGgo===', 'iV=ORw0KGgo=', 'iVBORw0KGgp=', '']) {
    asset.base64 = encoded; assert.equal(isBackgroundAsset(asset), false, encoded);
  }
  for (const [mime, bytes] of [['image/jpeg', [255, 216, 255]], ['image/gif', 'GIF89a'], ['image/webp', 'RIFF....WEBP'], ['image/bmp', 'BM'], ['image/avif', '....ftypavif....'] ] as const) {
    const encoded = Buffer.from(typeof bytes === 'string' ? bytes : [...bytes]).toString('base64');
    assert.ok(isBackgroundAsset({ id: 'image', name: 'image', mime, base64: encoded }), mime);
  }
});

test('large embedded backgrounds validate without recursive regular expressions or decoding their whole content', () => {
  const bytes = Buffer.alloc(12 * 1024 * 1024); Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]).copy(bytes);
  const asset = { id: 'large', name: 'large.png', mime: 'image/png', base64: bytes.toString('base64') };
  assert.ok(isBackgroundAsset(asset)); assert.ok(isBackgroundAsset(asset));
  asset.base64 = `${asset.base64.slice(0, -1)}!`; assert.equal(isBackgroundAsset(asset), false);
});

function backgroundProject() {
  const project = newProject('Background collection'), canvas = project.canvases[0];
  project.assets.push(png('canvas-image', 'canvas'), png('node-image', 'node'), png('unused-image'));
  canvas.style = { background: '#152627', backgroundPattern: 'contours', backgroundAssetId: 'canvas-image', backgroundFit: 'tile', backgroundScale: 64, backgroundOpacity: 0.35 };
  const node = addNode(project, canvas.id, 'text', 'Background text');
  node.text = 'Independent content'; node.style = { backgroundPattern: 'linen', backgroundAssetId: 'node-image', backgroundFit: 'cover', backgroundOpacity: 0.3, shape: 'hexagon' };
  return { project, canvas, node };
}

test('saving pins background bytes and portable publication retains only used canvas/node backgrounds', () => {
  const f = backgroundProject(), saved = commitSnapshots(f.project), snapshot = saved.snapshots[f.node.id];
  assert.deepEqual(snapshot.backgroundAsset, f.project.assets[1]);
  saved.assets[1].base64 = png('node-image', 'later revision').base64;
  assert.notEqual(snapshot.backgroundAsset!.base64, saved.assets[1].base64);
  const consumer = addCanvas(saved, 'Pinned background'), reference = addNode(saved, consumer.id, 'reference', 'Saved node'); reference.targetId = f.node.id;
  const payload = createPublicationPayload(saved);
  assert.deepEqual(payload.missing, []); assert.equal(payload.project.assets.length, 2);
  assert.deepEqual(payload.project.snapshots[f.node.id].backgroundAsset, snapshot.backgroundAsset);
  const imported = readPublication(buildPublication(saved, '', ''));
  assert.deepEqual(validateProject(imported), []);
  const copied = imported.nodes.find(node => node.title === f.node.title)!;
  assert.notEqual(copied.style!.backgroundAssetId, f.node.style!.backgroundAssetId);
  assert.equal(imported.snapshots[copied.id].backgroundAsset!.id, copied.style!.backgroundAssetId);
  assert.equal(imported.snapshots[copied.id].backgroundAsset!.base64, snapshot.backgroundAsset!.base64);
  assert.notEqual(imported.canvases[0].style!.backgroundAssetId, 'canvas-image');
});

test('external snapshot-only backgrounds are portable and fully remapped when importing a publication', () => {
  const f = backgroundProject(), saved = commitSnapshots(f.project), snapshot = saved.snapshots[f.node.id];
  const destination = newProject('Only saved source'), reference = addNode(destination, destination.entryCanvasId, 'reference', 'Saved source'); reference.targetId = f.node.id;
  destination.snapshots[f.node.id] = snapshot;
  const payload = createPublicationPayload(destination); assert.deepEqual(payload.missing, []); assert.equal(payload.project.assets.length, 0);
  const fork = forkProject(payload.project), observed = Object.values(fork.snapshots)[0];
  assert.notEqual(observed.node.style!.backgroundAssetId, 'node-image');
  assert.equal(observed.backgroundAsset!.id, observed.node.style!.backgroundAssetId);
  assert.deepEqual(validateProject(fork), []);
  const imported = readPublication(buildPublication(destination, '', ''));
  assert.deepEqual(Object.values(imported.snapshots)[0].backgroundAsset!.base64, snapshot.backgroundAsset!.base64);
});

test('selection copies remap canvas and node backgrounds and pin external snapshot backgrounds independently', () => {
  const f = backgroundProject(), consumer = addCanvas(f.project, 'Consumer'), reference = addNode(f.project, consumer.id, 'reference', 'Reference'); reference.targetId = f.node.id;
  const saved = commitSnapshots(f.project);
  const snippet = createSnippet(saved, [f.node.id], { includeDescendants: true, includeExternalReferences: true });
  assert.equal(snippet.project.assets.length, 2); assert.equal(snippet.report.copiedAssets, 2);
  assert.equal(snippet.project.canvases[0].style!.backgroundAssetId, snippet.idMap['canvas-image']);
  assert.equal(snippet.project.nodes[1].style!.backgroundAssetId, snippet.idMap['node-image']);
  const external = createSnippet(saved, [reference.id], { includeDescendants: true, includeExternalReferences: true });
  const observed = external.project.snapshots[f.node.id];
  assert.notEqual(observed.backgroundAsset!.id, 'node-image'); assert.equal(observed.node.style!.backgroundAssetId, observed.backgroundAsset!.id);
  assert.equal(external.report.copiedAssets, 1); assert.deepEqual(validateProject(external.project), []);
});

test('module definitions and repeated insertion carry node backgrounds with independent image identities', () => {
  const f = backgroundProject(), module = createModule(f.project, f.node.id, 'Background module');
  assert.equal(module.project.assets.length, 1);
  const node = module.project.nodes.find(node => node.title === f.node.title)!;
  assert.equal(node.style!.backgroundAssetId, module.project.assets[0].id); assert.notEqual(node.style!.backgroundAssetId, 'node-image');
  const target = newProject('Insertion'), first = instantiateModule(target, module, { canvasId: target.entryCanvasId, x: 400, y: 100 });
  const second = instantiateModule(first.project, module, { canvasId: target.entryCanvasId, x: 900, y: 100 });
  const images = second.project.nodes.filter(node => node.title === f.node.title).map(node => node.style!.backgroundAssetId);
  assert.equal(new Set(images).size, 2); assert.deepEqual(validateProject(second.project), []);
});

test('invalid background asset data and mismatched saved backgrounds fail validation while missing media stays repairable', () => {
  const f = backgroundProject(), saved = commitSnapshots(f.project);
  saved.snapshots[f.node.id].backgroundAsset!.id = 'mismatched'; assert.ok(validateProject(saved).some(issue => /does not match/.test(issue.message)));
  const unsafe = structuredClone(f.project); unsafe.assets[1].mime = 'image/svg+xml'; assert.ok(validateProject(unsafe).some(issue => /raster/.test(issue.message)));
  const malformed = structuredClone(saved); malformed.snapshots[f.node.id].backgroundAsset = false as never; assert.equal(isProjectShape(malformed), false);
  f.project.assets = []; assert.deepEqual(validateProject(f.project), []);
  const report = inspectProject(f.project); assert.ok(report.warnings.some(issue => issue.nodeId === f.canvas.rootId && issue.code === 'missing-media'));
  assert.deepEqual(new Set(createPublicationPayload(f.project).missing), new Set(['Asset canvas-image', 'Asset node-image']));
});

test('declarative authoring resolves newly embedded background aliases on both canvas and node without changing primary media', () => {
  const base = newProject('Recipe background');
  const image = png('photo');
  const result = applyRecipe(base, { version: 1, assets: [{ key: 'photo', name: image.name, mime: image.mime, base64: image.base64 }],
    canvases: [{ key: 'canvas', existingId: base.entryCanvasId, backgroundAsset: 'photo', style: { backgroundFit: 'tile' } }],
    nodes: [{ key: 'node', canvas: 'canvas', kind: 'text', text: 'Floating text', backgroundAsset: 'photo', style: { backgroundOpacity: 0.4 } }],
  });
  assert.equal(result.project.canvases[0].style!.backgroundAssetId, result.aliases.assets.photo);
  assert.equal(result.project.nodes[1].style!.backgroundAssetId, result.aliases.assets.photo); assert.equal(result.project.nodes[1].assetId, undefined);
  assert.equal(base.assets.length, 0); assert.deepEqual(validateProject(result.project), []);
});
