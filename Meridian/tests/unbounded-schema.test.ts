import test from 'node:test';
import assert from 'node:assert/strict';
import { applyRecipe, inspectProject, type AuthoringRecipe } from '../src/core/authoring.ts';
import { activeNodeContent } from '../src/core/content.ts';
import { addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import { isProjectShape, validateProject } from '../src/core/rules.ts';
import { buildPublication, createPublicationPayload, readPublication } from '../src/publication.ts';
import type { ContainerVariant, NodeKind } from '../src/core/types.ts';

test('all non-root node kinds can own child display patterns without changing their content semantics', () => {
  const project = newProject('Every node may be a parent');
  const kinds: NodeKind[] = ['container', 'spreadsheet', 'module', 'media', 'text', 'image', 'document', 'input', 'number', 'switch', 'output', 'joint', 'junction', 'reference', 'canvas-link'];
  for (const kind of kinds) {
    const parent = addNode(project, project.entryCanvasId, kind, `Parent ${kind}`);
    Object.assign(parent, { containerVariant: 'vertical', childLayout: ['container', 'spreadsheet', 'module'].includes(kind) ? 'contained' : 'spatial' });
    const child = addNode(project, project.entryCanvasId, 'media', `Child of ${kind}`);
    Object.assign(child, { parentId: parent.id, contentType: 'text', text: 'A single content item.', childPosition: 'auto' });
  }
  assert.equal(isProjectShape(project), true);
  assert.deepEqual(validateProject(project), []);
});

test('new content and placement selectors reject malformed values while older inactive content remains lossless', () => {
  const project = newProject('Selectors');
  const media = addNode(project, project.entryCanvasId, 'media', 'Selected image');
  Object.assign(media, { contentType: 'asset', assetId: 'primary-image', text: 'An older caption', attachments: ['other-attachment'], hyperlinks: [{ url: 'https://example.org/source' }] });
  project.assets.push({ id: 'primary-image', name: 'image.png', mime: 'image/png', base64: 'AQID' }, { id: 'other-attachment', name: 'notes.txt', mime: 'text/plain', base64: 'AQID' });
  const before = structuredClone(project);
  assert.deepEqual(activeNodeContent(media), { type: 'asset', assetId: 'primary-image' });
  assert.deepEqual(project, before);
  assert.deepEqual(validateProject(project), []);
  for (const changes of [{ contentType: 'mixed' }, { contentType: null }, { contentType: ['text'] }, { childPosition: 'drag' }, { childPosition: 1 }, { childLayout: 'contained' }, { containerVariant: 'unknown' }, { childOffset: { x: NaN, y: 0 } }, { childOffset: { x: 0, y: Infinity } }, { childOffset: { x: 1e10, y: 0 } }, { childOffset: { x: 0 } }, { childOffset: { x: 0, y: 0, execute: 'alert(1)' } }]) {
    const malformed = structuredClone(project);
    Object.assign(malformed.nodes.find(node => node.id === media.id)!, changes);
    assert.equal(isProjectShape(malformed), false, JSON.stringify(changes));
    assert.ok(validateProject(malformed).length);
  }
  const badRoot = structuredClone(project);
  badRoot.nodes[0].containerVariant = 'grid';
  assert.equal(isProjectShape(badRoot), false);
});

test('portable publications preserve unbounded patterns, child placement and exactly one selected content item without losing inactive data', () => {
  const project = newProject('Portable unbounded collection');
  const parent = addNode(project, project.entryCanvasId, 'media', 'Outside grid');
  Object.assign(parent, { contentType: 'asset', assetId: 'photo', text: 'Preserved inactive text', childLayout: 'spatial', containerVariant: 'grid', childOffset: { x: 270, y: 35 }, collapsed: true,
    grid: { columns: 2, rows: 2, cellWidth: 180, cellHeight: 120, gap: 0, fillOrder: 'column' }, hyperlinks: [{ url: 'https://example.org/source' }] });
  const child = addNode(project, project.entryCanvasId, 'media', 'Manual child');
  Object.assign(child, { parentId: parent.id, childPosition: 'manual', contentType: 'text', text: 'The visible item', x: 1300, y: 900, cell: { column: 1, row: 1 } });
  const automatic = addNode(project, project.entryCanvasId, 'media', 'Automatic child');
  Object.assign(automatic, { parentId: parent.id, childPosition: 'auto', contentType: 'link', hyperlinks: [{ url: 'https://example.org/page', label: 'A document' }], cell: { column: 2, row: 1 } });
  project.assets.push({ id: 'photo', name: 'image.png', mime: 'image/png', base64: 'AQID' });
  const saved = commitSnapshots(project), before = JSON.stringify(saved);
  const payload = createPublicationPayload(saved);
  const copiedParent = payload.project.nodes.find(node => node.id === parent.id)!;
  assert.equal(copiedParent.contentType, 'asset');
  assert.equal(copiedParent.text, 'Preserved inactive text');
  assert.equal(copiedParent.childLayout, 'spatial');
  assert.equal(copiedParent.containerVariant, 'grid');
  assert.deepEqual(copiedParent.childOffset, { x: 270, y: 35 });
  assert.equal(payload.project.nodes.find(node => node.id === child.id)?.childPosition, 'manual');
  assert.equal(payload.project.nodes.find(node => node.id === automatic.id)?.childPosition, 'auto');
  assert.equal(payload.project.assets[0].base64, 'AQID');
  const imported = readPublication(buildPublication(saved, '', ''));
  const importedParent = imported.nodes.find(node => node.title === parent.title)!;
  const importedChild = imported.nodes.find(node => node.title === child.title)!;
  assert.notEqual(importedParent.id, parent.id);
  assert.equal(importedChild.parentId, importedParent.id);
  assert.equal(importedChild.childPosition, 'manual');
  assert.deepEqual(importedParent.grid, parent.grid);
  assert.deepEqual(importedParent.childOffset, parent.childOffset);
  assert.deepEqual(activeNodeContent(importedParent), { type: 'asset', assetId: imported.assets[0].id });
  assert.deepEqual(validateProject(imported), []);
  assert.equal(JSON.stringify(saved), before);
});

test('authoring recipes create bounded and unbounded versions of every pattern with portable, inert content selectors', () => {
  const base = newProject('Display pattern recipes');
  const variants: ContainerVariant[] = ['contained', 'horizontal', 'vertical', 'grid', 'spreadsheet', 'window', 'gallery'];
  for (const variant of variants) {
    const grid = { columns: 3, rows: 3, cellWidth: 200, cellHeight: 120, windowColumns: 2, windowRows: 2, scrollDirection: 'horizontal' as const };
    const recipe: AuthoringRecipe = { version: 1, nodes: [
      { key: 'bounded', canvas: base.entryCanvasId, kind: 'container', containerVariant: variant, grid, contentType: 'text', text: 'Cover shown while collapsed' },
      { key: 'unbounded', canvas: base.entryCanvasId, kind: 'media', containerVariant: variant, grid, contentType: 'text', text: 'Content remains alongside children' },
      { key: 'child', canvas: base.entryCanvasId, parent: 'unbounded', kind: 'media', contentType: 'text', text: 'One item', childPosition: 'auto' },
    ] };
    const result = applyRecipe(base, recipe);
    const bounded = result.project.nodes.find(node => node.id === result.aliases.nodes.bounded)!;
    const unbounded = result.project.nodes.find(node => node.id === result.aliases.nodes.unbounded)!;
    assert.equal(bounded.childLayout, 'contained');
    assert.equal(unbounded.childLayout, 'spatial');
    assert.equal(unbounded.containerVariant, variant);
    assert.ok(!inspectProject(result.project).warnings.some(issue => issue.code === 'outside-container' && issue.nodeIds?.includes(unbounded.id)));
    assert.deepEqual(validateProject(readPublication(buildPublication(result.project, '', ''))), []);
  }
  const before = structuredClone(base);
  for (const fields of [{ contentType: 'both' }, { childPosition: 'automatic' }, { childLayout: 'contained' }, { contentType: { execute: 'alert(1)' } }]) {
    assert.throws(() => applyRecipe(base, { version: 1, nodes: [{ key: 'bad', canvas: base.entryCanvasId, kind: 'media', ...fields }] } as AuthoringRecipe));
    assert.deepEqual(base, before);
  }
});

test('unbounded media parents preserve inherited restrictions, cycles and cell occupancy checks', () => {
  const project = newProject('Unbounded constraints');
  const parent = addNode(project, project.entryCanvasId, 'media', 'Restricted parent');
  Object.assign(parent, { childLayout: 'spatial', containerVariant: 'spreadsheet', grid: { columns: 2, rows: 2, cellWidth: 200, cellHeight: 120 }, rules: { nodeKinds: ['media'] } });
  const first = addNode(project, project.entryCanvasId, 'media', 'First');
  Object.assign(first, { parentId: parent.id, cell: { column: 1, row: 1 } });
  assert.deepEqual(validateProject(project), []);
  const duplicate = structuredClone(project), second = addNode(duplicate, project.entryCanvasId, 'media', 'Second');
  Object.assign(second, { parentId: parent.id, cell: { column: 1, row: 1 } });
  assert.ok(validateProject(duplicate).some(issue => /one immediate child/.test(issue.message)));
  const excluded = structuredClone(project);
  excluded.nodes.find(node => node.id === first.id)!.kind = 'number';
  assert.ok(validateProject(excluded).some(issue => /inherited rule/.test(issue.message)));
  const cycle = structuredClone(project);
  cycle.nodes.find(node => node.id === parent.id)!.parentId = first.id;
  assert.ok(validateProject(cycle).some(issue => /cycle/.test(issue.message)));
});

test('inspection classifies unified Media by selected content and ignores inactive captions in typography warnings', () => {
  const project = newProject('Selected-content inspection');
  project.assets.push({ id: 'image-content', name: 'image.png', mime: 'image/png', base64: 'AQID' },
    { id: 'document-content', name: 'reading.pdf', mime: 'application/pdf', base64: 'AQID' },
    { id: 'video-content', name: 'video.mp4', mime: 'video/mp4', base64: 'AQID' });
  for (let at = 0; at < 8; at++) {
    const node = addNode(project, project.entryCanvasId, 'media', `Image ${at}`);
    Object.assign(node, { contentType: 'asset', assetId: 'image-content', text: 'An inactive caption '.repeat(500), x: at * 250, y: 100, style: { fontSize: 12, fontFamily: 'mono' } });
  }
  const document = addNode(project, project.entryCanvasId, 'media', 'Document'), video = addNode(project, project.entryCanvasId, 'media', 'Video');
  Object.assign(document, { contentType: 'asset', assetId: 'document-content', x: 0, y: 500 });
  Object.assign(video, { contentType: 'asset', assetId: 'video-content', x: 300, y: 500 });
  const text = addNode(project, project.entryCanvasId, 'media', 'Text');
  Object.assign(text, { contentType: 'text', text: 'A selected text body', x: 600, y: 500, style: { fontSize: 19, fontFamily: 'serif' } });
  const formerlyImage = addNode(project, project.entryCanvasId, 'image', 'Formerly image');
  Object.assign(formerlyImage, { contentType: 'text', assetId: 'image-content', text: 'Now text', x: 900, y: 500, style: { fontSize: 31, fontFamily: 'sans' } });
  const before = JSON.stringify(project), report = inspectProject(project), canvas = report.canvases[0];
  assert.equal(report.summary.imageCount, 8);
  assert.equal(canvas.imageCount, 8);
  assert.equal(canvas.documentCount, 1);
  assert.deepEqual(canvas.styles.fontSizes, [19, 31]);
  assert.deepEqual(canvas.styles.fontFamilies, ['sans', 'serif']);
  assert.ok(!report.warnings.some(issue => issue.code === 'uniform-presentation'));
  const imageIds = new Set(project.nodes.filter(node => node.title.startsWith('Image ')).map(node => node.id));
  assert.ok(!report.warnings.some(issue => issue.code === 'estimated-text-overflow' && imageIds.has(issue.nodeId!)));
  assert.equal(JSON.stringify(project), before);
});
