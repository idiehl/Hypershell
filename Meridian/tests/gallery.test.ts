import test from 'node:test';
import assert from 'node:assert/strict';
import { addCanvas, addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import { galleryItems, galleryOwner, gallerySelection, galleryStep, isGallery } from '../src/core/gallery.ts';
import { createNodeFromSpec } from '../src/core/creation.ts';
import { updateNodeType } from '../src/core/node-editing.ts';
import { resolveGridCell, resolveReferenceTarget } from '../src/core/cells.ts';
import { validateProject } from '../src/core/rules.ts';
import { createPublicationPayload } from '../src/publication.ts';
import { navigationIndex } from '../src/core/navigation.ts';
import { revealChildren } from '../src/core/reveal.ts';
import { resolveCanvasCollisions } from '../src/core/collision.ts';
import { applyRecipe, inspectProject } from '../src/core/authoring.ts';

function fixture() {
  const project = newProject('Collection'), canvasId = project.entryCanvasId;
  const gallery = addNode(project, canvasId, 'container', 'Zeus objects');
  Object.assign(gallery, { containerVariant: 'gallery', childLayout: 'contained', childArrangement: 'free' });
  const image = addNode(project, canvasId, 'image', 'Marble head'), text = addNode(project, canvasId, 'text', 'An account'), document = addNode(project, canvasId, 'document', 'A passage');
  for (const item of [image, text, document]) item.parentId = gallery.id;
  image.text = 'Attribution and qualified identification'; text.text = 'A text entry'; document.text = 'Document summary';
  return { project, gallery, image, text, document };
}

test('Gallery browses immediate mixed objects in stored order without changing the graph, revision, or current identity', () => {
  const { project, gallery, image, text, document } = fixture();
  const nested = addNode(project, project.entryCanvasId, 'text', 'Inside the passage'); nested.parentId = document.id;
  const unrelated = addNode(project, project.entryCanvasId, 'text', 'Other canvas content');
  const reference = addNode(project, project.entryCanvasId, 'reference', 'Linked object'); reference.parentId = gallery.id; reference.targetId = unrelated.id;
  const otherCanvas = addCanvas(project, 'Documents'), launcher = addNode(project, project.entryCanvasId, 'canvas-link', 'Open document canvas'); launcher.parentId = gallery.id; launcher.targetId = otherCanvas.rootId;
  const before = structuredClone(project);
  assert.deepEqual(galleryItems(project, gallery.id).map(item => item.id), [image.id, text.id, document.id, reference.id, launcher.id]);
  assert.equal(gallerySelection(project, gallery).item, image);
  assert.equal(galleryStep(project, gallery, image.id, -1), image.id);
  assert.equal(galleryStep(project, gallery, image.id, 1), text.id);
  assert.equal(galleryStep(project, gallery, launcher.id, 1), launcher.id);
  assert.equal(galleryStep(project, gallery, launcher.id, -1), reference.id);
  assert.equal(galleryOwner(project, nested.id)?.id, gallery.id);
  assert.equal(galleryOwner(project, gallery.id), undefined);
  assert.deepEqual(project, before); assert.deepEqual(validateProject(project), []);
});

test('Gallery selection recovers from deletion and handles an empty or missing collection', () => {
  const { project, gallery, image, text, document } = fixture();
  project.nodes = project.nodes.filter(node => node.id !== text.id);
  assert.equal(gallerySelection(project, gallery, text.id).item?.id, image.id);
  assert.equal(gallerySelection(project, gallery, document.id).index, 1);
  project.nodes = project.nodes.filter(node => node.parentId !== gallery.id);
  assert.deepEqual(gallerySelection(project, gallery), { items: [], index: -1 });
  assert.equal(galleryStep(project, gallery, text.id, 1), undefined);
  assert.deepEqual(galleryItems(project, 'missing-gallery'), []);
  assert.deepEqual(galleryItems(project, project.canvases[0].rootId), []);
  assert.equal(isGallery(undefined), false);
});

test('converting an addressed Grid into Gallery preserves cell references, child geometry, content, and GUIDs', () => {
  const { project, gallery, image, text, document } = fixture();
  Object.assign(gallery, { containerVariant: 'grid', childArrangement: 'grid', grid: { columns: 3, rows: 1, cellWidth: 150, cellHeight: 100, gap: 12 } });
  for (const [index, item] of [image, text, document].entries()) Object.assign(item, { cell: { column: index + 1, row: 1, ...(index === 0 ? { name: 'zeus_image' } : {}) }, x: 100 + index * 162, y: 300, width: 150, height: 100 });
  const reference = addNode(project, project.entryCanvasId, 'reference', 'Image reference'); reference.targetId = gallery.id; reference.targetCell = 'zeus_image';
  const before = structuredClone(project), converted = updateNodeType(project, gallery.id, { kind: 'container', containerVariant: 'gallery' });
  const updated = converted.nodes.find(node => node.id === gallery.id)!;
  assert.equal(updated.childLayout, 'contained'); assert.equal(updated.childArrangement, 'free');
  assert.deepEqual(updated.grid, gallery.grid);
  assert.deepEqual(converted.nodes.filter(node => node.parentId === gallery.id), before.nodes.filter(node => node.parentId === gallery.id));
  assert.equal(resolveGridCell(converted, gallery.id, 'A1')?.id, image.id);
  assert.equal(resolveReferenceTarget(converted, reference)?.node.id, image.id);
  assert.deepEqual(project, before); assert.deepEqual(validateProject(commitSnapshots(converted)), []);
  const added = createNodeFromSpec(converted, { canvasId: project.entryCanvasId, parentId: gallery.id, x: 800, y: 500, type: 'text', title: 'Another object', text: 'Beyond the old grid limit', width: 200, height: 120 });
  const child = added.project.nodes.find(node => node.id === added.nodeId)!;
  assert.equal(child.cell, undefined); assert.equal(child.parentId, gallery.id);
  assert.equal(galleryItems(added.project, gallery.id).length, 4);
});

test('converting ordinary media to Gallery retains its primary asset and summary without manufacturing coordinates', () => {
  const { project, image } = fixture();
  const asset = { id: 'original-image', name: 'zeus.png', mime: 'image/png', base64: 'AQID' };
  project.assets.push(asset); image.assetId = asset.id;
  const converted = updateNodeType(project, image.id, { kind: 'container', containerVariant: 'gallery' });
  const gallery = converted.nodes.find(node => node.id === image.id)!;
  assert.equal(gallery.kind, 'container'); assert.equal(gallery.containerVariant, 'gallery');
  assert.equal(gallery.grid, undefined); assert.equal(gallery.childArrangement, 'free');
  assert.equal(gallery.assetId, asset.id); assert.equal(gallery.text, image.text);
  assert.deepEqual(converted.assets, project.assets);
});

test('Gallery collection order, attribution, media bytes, and document links survive saving and publication', () => {
  const { project, gallery, image, text, document } = fixture();
  const picture = { id: 'picture', name: 'zeus.jpg', mime: 'image/jpeg', base64: 'AQID' }, passage = { id: 'passage', name: 'source.txt', mime: 'text/plain', base64: 'BAUG' };
  project.assets.push(picture, passage); image.assetId = picture.id; document.assetId = passage.id;
  image.hyperlinks = [{ url: 'https://example.test/collection', label: 'Image source' }];
  const saved = commitSnapshots(project), published = createPublicationPayload(saved).project;
  assert.equal(saved.snapshots[gallery.id].node.containerVariant, 'gallery');
  assert.deepEqual(galleryItems(published, gallery.id).map(item => item.id), [image.id, text.id, document.id]);
  assert.deepEqual(published.assets, [picture, passage]);
  assert.deepEqual(published.nodes.find(node => node.id === image.id)?.hyperlinks, image.hyperlinks);
  assert.equal(published.nodes.find(node => node.id === image.id)?.text, image.text);
  assert.deepEqual(validateProject(published), []);
});

test('expanded Gallery objects stay embedded, their visual links stay hidden, and navigation retains the full hierarchy', () => {
  const { project, gallery, image, text } = fixture();
  gallery.collapsed = false;
  const nested = addNode(project, project.entryCanvasId, 'text', 'Image annotation'); nested.parentId = image.id; image.collapsed = false;
  const rootId = project.canvases[0].rootId;
  const outside = addNode(project, project.entryCanvasId, 'text', 'Public introduction');
  const collectionLink = { id: crypto.randomUUID(), canvasId: project.entryCanvasId, sourceId: outside.id, targetId: gallery.id, parentId: rootId, kind: 'visual' as const, relation: 'generic' as const };
  const hiddenLinks = [
    { id: crypto.randomUUID(), canvasId: project.entryCanvasId, sourceId: image.id, targetId: text.id, parentId: gallery.id, kind: 'visual' as const, relation: 'chronological' as const },
    { id: crypto.randomUUID(), canvasId: project.entryCanvasId, sourceId: outside.id, targetId: image.id, parentId: rootId, kind: 'visual' as const, relation: 'generic' as const },
    { id: crypto.randomUUID(), canvasId: project.entryCanvasId, sourceId: image.id, targetId: nested.id, parentId: image.id, kind: 'visual' as const, relation: 'causal' as const },
  ];
  project.links.push(collectionLink, ...hiddenLinks);
  const index = navigationIndex(project);
  assert.equal(index.isNodeVisible(gallery.id), true); assert.equal(index.isNodeVisible(outside.id), true);
  for (const node of [image, text, nested]) {
    assert.equal(index.isNodeVisible(node.id), false);
    assert.equal(index.visibleNode(node.id)?.id, gallery.id);
  }
  assert.equal(index.isLinkVisible(collectionLink), true);
  for (const link of hiddenLinks) assert.equal(index.isLinkVisible(link), false);
  const visibleOutline = index.outline(project.entryCanvasId, true).map(row => row.node.id);
  assert.ok(visibleOutline.includes(gallery.id)); assert.ok(!visibleOutline.includes(image.id)); assert.ok(!visibleOutline.includes(nested.id));
  const fullOutline = index.outline(project.entryCanvasId).map(row => row.node.id);
  assert.ok(fullOutline.includes(image.id)); assert.ok(fullOutline.includes(nested.id));
  assert.equal(index.contents(gallery.id).nodes[0].id, image.id);
});

test('nested Gallery objects resolve to the outer visible Gallery while an enclosing collapsed node still owns hidden navigation', () => {
  const { project, gallery, image } = fixture();
  const outer = addNode(project, project.entryCanvasId, 'container', 'Collection of collections');
  Object.assign(outer, { containerVariant: 'gallery', childLayout: 'contained', collapsed: false }); gallery.parentId = outer.id; gallery.collapsed = false;
  assert.equal(navigationIndex(project).visibleNode(image.id)?.id, outer.id);
  assert.equal(navigationIndex(project).isNodeVisible(gallery.id), false);
  const enclosing = addNode(project, project.entryCanvasId, 'container', 'Hidden chapter'); enclosing.collapsed = true; outer.parentId = enclosing.id;
  assert.equal(navigationIndex(project).visibleNode(image.id)?.id, enclosing.id);
  assert.equal(navigationIndex(project).visibleNode(outer.id)?.id, enclosing.id);
});

test('reveal and collapse do not arrange legacy Gallery items or rewrite stored frame, route and cell geometry', () => {
  const { project, gallery, image, text, document } = fixture();
  Object.assign(gallery, { x: 20, y: 20, width: 480, height: 420, sizeMode: 'fixed', childLayout: 'spatial', childArrangement: 'grid', collapsed: true,
    grid: { columns: 3, rows: 1, cellWidth: 160, cellHeight: 90, gap: 12, showHeaders: false } });
  for (const [position, item] of [image, text, document].entries()) Object.assign(item, { x: 950 + position * 160, y: 1000,
    cell: { column: position + 1, row: 1 }, placement: position ? 'near' : 'edge', attachSide: 'right' });
  project.links.push({ id: crypto.randomUUID(), canvasId: project.entryCanvasId, sourceId: image.id, targetId: text.id, parentId: gallery.id,
    kind: 'visual', relation: 'chronological', points: [{ x: 990, y: 980 }, { x: 1140, y: 980 }] });
  const before = structuredClone(project), expanded = revealChildren(project, gallery.id, true), collapsed = revealChildren(expanded, gallery.id, false);
  assert.deepEqual(project, before);
  for (const candidate of [expanded, collapsed]) {
    assert.deepEqual(candidate.nodes.filter(node => node.parentId === gallery.id), before.nodes.filter(node => node.parentId === gallery.id));
    assert.deepEqual(candidate.links, before.links);
    const current = candidate.nodes.find(node => node.id === gallery.id)!;
    assert.deepEqual(current.grid, gallery.grid); assert.equal(current.childArrangement, 'grid');
    assert.deepEqual({ x: current.x, y: current.y, width: current.width, height: current.height }, { x: 20, y: 20, width: 480, height: 420 });
    assert.deepEqual(validateProject(candidate), []);
  }
  assert.equal(expanded.nodes.find(node => node.id === gallery.id)?.collapsed, false);
  assert.equal(collapsed.nodes.find(node => node.id === gallery.id)?.collapsed, true);
  assert.deepEqual(navigationIndex(expanded).bounds(expanded.nodes.find(node => node.id === gallery.id)!), { id: gallery.id, x: 20, y: 20, width: 480, height: 420 });
  assert.deepEqual(navigationIndex(collapsed).bounds(collapsed.nodes.find(node => node.id === gallery.id)!), { id: gallery.id, x: 20, y: 20, width: 480, height: 420 });
  assert.deepEqual(collapsed.nodes.find(node => node.id === gallery.id)!.containerSizes, { expanded: { width: 480, height: 420, sizeMode: 'fixed' }, collapsed: { width: 480, height: 420, sizeMode: 'fixed' } });
});

test('collision repair ignores embedded Gallery objects even when their stored bounds overlap visible neighbors', () => {
  const { project, gallery, image, text, document } = fixture();
  Object.assign(gallery, { x: 20, y: 20, width: 480, height: 420, collapsed: false });
  const peer = addNode(project, project.entryCanvasId, 'text', 'Neighbor'); Object.assign(peer, { x: 900, y: 100, width: 240, height: 180 });
  for (const item of [image, text, document]) Object.assign(item, { x: 900, y: 100, width: 240, height: 180 });
  const before = structuredClone(project), repaired = resolveCanvasCollisions(project, project.entryCanvasId, [image.id]);
  assert.deepEqual(repaired, before); assert.deepEqual(project, before);
});

test('authoring inspection counts every Gallery object but only diagnoses visible canvas geometry', () => {
  const { project, gallery, image, text, document } = fixture();
  Object.assign(gallery, { x: 20, y: 20, width: 480, height: 420, sizeMode: 'fixed', collapsed: false });
  for (const item of [image, text, document]) Object.assign(item, { x: 1400, y: 1100, width: 240, height: 180 });
  text.text = 'Long gallery text that is displayed using the parent gallery viewport. '.repeat(50);
  image.assetId = 'missing-image-bytes';
  const peers = [addNode(project, project.entryCanvasId, 'text', 'Visible neighbor'), addNode(project, project.entryCanvasId, 'text', 'Overlapping visible neighbor')];
  for (const peer of peers) Object.assign(peer, { x: 900, y: 100, width: 240, height: 180, sizeMode: 'fixed' });
  const report = inspectProject(project), canvas = report.canvases[0]!;
  assert.equal(report.summary.nodeCount, 6); assert.equal(canvas.nodeCount, 6); assert.equal(canvas.visibleNodeCount, 3);
  assert.equal(canvas.imageCount, 1); assert.equal(canvas.documentCount, 1); assert.equal(canvas.containerCount, 1);
  assert.deepEqual(canvas.bounds, { x: 20, y: 20, width: 1120, height: 420 });
  const galleryIds = new Set([image.id, text.id, document.id]);
  assert.equal(report.warnings.some(issue => ['sibling-overlap', 'outside-container', 'estimated-text-overflow'].includes(issue.code)
    && (issue.nodeId && galleryIds.has(issue.nodeId) || issue.nodeIds?.some(id => galleryIds.has(id)))), false);
  assert.equal(report.warnings.filter(issue => issue.code === 'sibling-overlap').length, 1);
  assert.ok(report.warnings.some(issue => issue.code === 'missing-media' && issue.nodeId === image.id), 'Hidden object asset integrity still matters.');
});

test('batch layout spaces Gallery frames without distant embedded objects while translating their complete branches and routes', () => {
  const { project, gallery, image, text, document } = fixture();
  Object.assign(gallery, { x: 100, y: 200, width: 440, height: 480, collapsed: false });
  for (const [at, item] of [image, text, document].entries()) Object.assign(item, { x: 4000 + at * 300, y: -2000, width: 240, height: 180 });
  const ordinary = addNode(project, project.entryCanvasId, 'container', 'Ordinary group'); Object.assign(ordinary, { x: 600, y: 200, width: 300, height: 200, collapsed: true });
  const ordinaryChild = addNode(project, project.entryCanvasId, 'text', 'Retained ordinary footprint'); Object.assign(ordinaryChild, { parentId: ordinary.id, x: 1400, y: 200, width: 100, height: 100 });
  const peer = addNode(project, project.entryCanvasId, 'text', 'Neighbor'); Object.assign(peer, { x: 2000, y: 200, width: 100, height: 80 });
  const nested = addNode(project, project.entryCanvasId, 'text', 'Image annotation'); Object.assign(nested, { parentId: image.id, x: 4100, y: -1900, width: 120, height: 90 });
  const inside = { id: crypto.randomUUID(), canvasId: project.entryCanvasId, parentId: gallery.id, sourceId: image.id, targetId: text.id,
    kind: 'visual' as const, relation: 'generic' as const, points: [{ x: 4250, y: -1920 }] };
  const outside = { id: crypto.randomUUID(), canvasId: project.entryCanvasId, parentId: project.canvases[0].rootId, sourceId: text.id, targetId: peer.id,
    kind: 'visual' as const, relation: 'generic' as const, points: [{ x: 3100, y: -800 }] };
  project.links.push(inside, outside);
  const before = structuredClone(project), result = applyRecipe(project, { version: 1, layouts: [{ kind: 'row', nodes: [gallery.id, ordinary.id, peer.id], x: 0, y: 0, gap: 30 }] }).project;
  const placed = (id: string) => result.nodes.find(node => node.id === id)!;
  assert.equal(placed(gallery.id).x, 0); assert.equal(placed(gallery.id).y, 0);
  assert.equal(placed(ordinary.id).x, 470, 'The next group follows the 440px Gallery frame plus the gap.');
  assert.equal(placed(peer.id).x, 1400, 'Ordinary collapsed groups still retain their full authored descendant footprint.');
  for (const item of [image, text, document, nested]) {
    assert.equal(placed(item.id).x, item.x - 100); assert.equal(placed(item.id).y, item.y - 200);
    assert.equal(placed(item.id).parentId, item.parentId);
  }
  assert.deepEqual(result.links.find(link => link.id === inside.id)?.points, [{ x: 4150, y: -2120 }]);
  assert.deepEqual(result.links.find(link => link.id === outside.id)?.points, outside.points);
  assert.deepEqual(project, before); assert.deepEqual(validateProject(result), []);
});
