import test from 'node:test';
import assert from 'node:assert/strict';
import { addCanvas, addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import { createProjectSearch, decodeTextAsset, displayBounds, navigationIndex } from '../src/core/navigation.ts';

test('collapsed structure preserves its manual frame while automatic covers fit their own content without mutation', () => {
  const p = newProject('Geometry'), node = addNode(p, p.entryCanvasId, 'container', 'Branch');
  delete node.style; // Existing authored geometry keeps the legacy typography.
  Object.assign(node, { sizeMode: 'fixed', width: 1280, height: 900, text: 'This summary remains readable.', collapsed: true });
  const original = structuredClone(node);
  assert.deepEqual(displayBounds(node), { id: node.id, x: node.x, y: node.y, width: 1280, height: 900 });
  assert.deepEqual(node, original);
  node.collapsed = false; assert.equal(displayBounds(node).width, 1280); assert.equal(displayBounds(node).height, 900);
  node.collapsed = true; node.text = ''; assert.equal(displayBounds(node).height, 900);
  node.sizeMode = 'auto'; assert.equal(displayBounds(node).height, 29);
  node.text = original.text; assert.deepEqual(displayBounds(node), { id: node.id, x: node.x, y: node.y, width: 210, height: 52 });
});

test('collapse leaves ordinary parent media, text and controls at their authored size', () => {
  const p = newProject('Parents');
  for (const kind of ['text', 'image', 'document', 'input', 'switch', 'output', 'reference'] as const) {
    const node = addNode(p, p.entryCanvasId, kind, kind); Object.assign(node, { sizeMode: 'fixed', width: 520, height: 340, collapsed: true });
    assert.equal(displayBounds(node).width, 520); assert.equal(displayBounds(node).height, 340);
  }
});

test('search finds hidden descendants, full text attachments, titles, identities and canvas context', () => {
  const p = newProject('Search'), other = addCanvas(p, 'Literary sources');
  const parent = addNode(p, other.id, 'container', 'Closed books'); parent.collapsed = true;
  const doc = addNode(p, other.id, 'document', 'Hésiod'); doc.parentId = parent.id;
  const text = 'Beginning '.repeat(1000) + 'faraway-evidence-token';
  p.assets.push({ id: 'asset-test', name: 'Works and Days.txt', mime: 'text/plain', base64: Buffer.from(text).toString('base64') }); doc.assetId = 'asset-test';
  const before = JSON.stringify(p), search = createProjectSearch(p);
  assert.equal(search('faraway-evidence-token')[0].nodeId, doc.id);
  assert.match(search('faraway-evidence-token')[0].snippet, /faraway-evidence-token/);
  assert.equal(search('hesiod')[0].nodeId, doc.id);
  assert.equal(search('Works Days')[0].source, 'Attachment');
  assert.equal(search(doc.id)[0].nodeId, doc.id);
  assert.equal(search('Literary sources faraway-evidence-token')[0].nodeId, doc.id);
  assert.equal(JSON.stringify(p), before);
  assert.equal(decodeTextAsset(p.assets[0]), text);
});

test('reference search observes saved cross-canvas content and never invents original context', () => {
  let p = newProject('References'); const original = addNode(p, p.entryCanvasId, 'text', 'Original'); original.text = 'savedpomegranate';
  const other = addCanvas(p, 'Comparisons'), reference = addNode(p, other.id, 'reference', 'Read-only alias'); reference.targetId = original.id;
  p = commitSnapshots(p); p.nodes.find(node => node.id === original.id)!.text = 'unsavedolive';
  const search = createProjectSearch(p);
  assert.ok(search('savedpomegranate').some(result => result.nodeId === reference.id));
  assert.ok(!search('unsavedolive').some(result => result.nodeId === reference.id));
  assert.equal(navigationIndex(p).original(reference.id)?.id, original.id);
  p.nodes = p.nodes.filter(node => node.id !== original.id);
  assert.equal(navigationIndex(p).original(reference.id), undefined);
  assert.ok(createProjectSearch(p)('savedpomegranate').some(result => result.nodeId === reference.id));
});

test('outline, ancestors and backlinks include nested and canvas references without cycling', () => {
  const p = newProject('Navigation'), parent = addNode(p, p.entryCanvasId, 'container', 'Parent'), child = addNode(p, p.entryCanvasId, 'text', 'Child'); child.parentId = parent.id;
  const other = addCanvas(p, 'Other'), link = addNode(p, other.id, 'canvas-link', 'Return'); link.targetId = p.entryCanvasId;
  const ref = addNode(p, other.id, 'reference', 'Child elsewhere'); ref.targetId = child.id;
  const index = navigationIndex(p);
  assert.deepEqual(index.ancestors(child.id).map(node => node.id), [parent.id, p.canvases[0].rootId]);
  assert.equal(index.outline(p.entryCanvasId).find(row => row.node.id === child.id)?.depth, 1);
  assert.equal(index.backlinks(child.id)[0].id, ref.id);
  assert.equal(index.backlinks(p.canvases[0].rootId)[0].id, link.id);
  assert.equal(index.original(link.id)?.id, p.canvases[0].rootId);
  parent.parentId = child.id;
  assert.equal(navigationIndex(p).ancestors(child.id).length, 1);
  assert.equal(navigationIndex(p).descendants(parent.id).length, 1);
});
