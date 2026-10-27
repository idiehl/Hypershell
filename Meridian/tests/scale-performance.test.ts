import test from 'node:test';
import assert from 'node:assert/strict';
import { createShapeBoundsIndex, type ShapeBox } from '../src/core/geometry.ts';
import { collisionFreeTranslation } from '../src/core/collision.ts';
import { addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import { saveChangesScene } from '../src/automatic-save.ts';
import { reconcileSnapLinks } from '../src/core/clusters.ts';

test('spatial broad phase preserves original order, contact rules and very different scales', () => {
  const boxes: ShapeBox[] = Array.from({length: 200}, (_, i) => ({id: String(i), x: i * 200 - 20000, y: (i % 7) * 110, width: i % 11 ? 90 : 0.0001, height: 100}));
  boxes.push({id: 'huge', x: -1e8, y: -1e8, width: 2e8, height: 2e8});
  const query = createShapeBoundsIndex(boxes);
  for (const region of [...boxes, {x: 0, y: 0, width: .000001, height: .000001}, {x: 1e9, y: -1e9, width: 1, height: 1}]) {
    const expected = boxes.filter(box => region.x < box.x + box.width && region.x + region.width > box.x && region.y < box.y + box.height && region.y + region.height > box.y);
    assert.deepEqual(query(region), expected);
  }
  assert.deepEqual(createShapeBoundsIndex([])({x: 0, y: 0, width: 1, height: 1}), []);
});

test('indexed collision candidates give exactly the same deterministic curved and polygon translations', () => {
  const shapes = ['rectangle', 'circle', 'triangle', 'rounded', 'diamond'] as const;
  for (const shape of shapes) for (const offset of [-80, 0, 65]) {
    const peers: ShapeBox[] = [{id: 'owner', x: -200, y: -200, width: 600, height: 600, shape: 'rectangle'},
      ...Array.from({length: 15}, (_, i) => ({id: String(i), x: (i % 5) * 120, y: Math.floor(i / 5) * 110, width: 105, height: 95, shape: shapes[i % shapes.length]}))];
    const moving = [{id: 'moving', x: offset, y: 20, width: 80, height: 80, shape}];
    const ignore = (_a: ShapeBox, b: ShapeBox) => b.id === 'owner';
    assert.deepEqual(collisionFreeTranslation(moving, () => peers, {x: 0, y: 0}, ignore, createShapeBoundsIndex(peers)), collisionFreeTranslation(moving, peers, {x: 0, y: 0}, ignore));
  }
});

test('a distant collision query does not materialize the entire obstacle collection', () => {
  let reads = 0;
  const peers = [{x: 1000, y: 1000, width: 20, height: 20}];
  assert.deepEqual(collisionFreeTranslation([{x: 0, y: 0, width: 10, height: 10}], () => {reads++; return peers;}, {x: 2, y: 3}, undefined, createShapeBoundsIndex(peers)), {x: 2, y: 3});
  assert.equal(reads, 0);
});

test('save acknowledgement keeps the scene but server graph changes refresh it', () => {
  const project = newProject('Save rendering'); addNode(project, project.entryCanvasId, 'text', 'A');
  const saved = commitSnapshots(project); saved.revision++;
  assert.equal(saveChangesScene(project, saved), false);
  const reordered = {...saved, nodes: saved.nodes.map(node => Object.fromEntries(Object.entries(node).reverse()) as typeof node)};
  assert.equal(saveChangesScene(project, reordered), false);
  for (const change of [
    (p: typeof saved) => {p.nodes[1]!.x++;},
    (p: typeof saved) => {p.nodes[1]!.text = 'server normalization';},
    (p: typeof saved) => {p.canvases[0]!.title = 'renamed';},
    (p: typeof saved) => {p.assets.push({id: 'asset', name: 'a.txt', mime: 'text/plain', base64: 'QQ=='});},
    (p: typeof saved) => {p.dependencies.push({canvasId: 'external', title: 'External', required: true});}
  ]) { const next = structuredClone(saved); change(next); assert.equal(saveChangesScene(project, next), true); }
});

test('save refresh is retained for references, conceptual links and snapshot-only assets', () => {
  const project = newProject('Snapshot consumers'), node = addNode(project, project.entryCanvasId, 'media', 'A');
  for (const patch of [{kind: 'reference', targetId: 'outside'}, {remoteLinks: [{id: 'remote', targetId: 'outside', relation: 'generic'}]}, {assetId: 'snapshot-only'}, {style: {backgroundAssetId: 'snapshot-only'}}, {attachments: ['snapshot-only']}]) {
    const next = structuredClone(project); Object.assign(next.nodes.find(item => item.id === node.id)!, patch);
    assert.equal(saveChangesScene(next, structuredClone(next)), true);
  }
});

test('snap reconciliation fast path still keeps explicit locked contacts and creates enabled ones', () => {
  const project = newProject('Snap fast path'), a = addNode(project, project.entryCanvasId, 'media', 'A'), b = addNode(project, project.entryCanvasId, 'media', 'B');
  for (const [i, node] of [a, b].entries()) Object.assign(node, {x: i * 100, y: 0, width: 100, height: 100, sizeMode: 'fixed', style: {shape: 'rectangle'}});
  project.canvases[0]!.snapping = {stickyEdges: false};
  assert.equal(reconcileSnapLinks(project, project.entryCanvasId, [a.id]), project);
  project.canvases[0]!.snapping.stickyEdges = true;
  const snapped = reconcileSnapLinks(project, project.entryCanvasId, [a.id]);
  assert.equal(snapped.links.filter(link => link.snap).length, 1);
  snapped.links.find(link => link.snap)!.snap!.locked = true;
  snapped.canvases[0]!.snapping!.stickyEdges = false;
  assert.equal(reconcileSnapLinks(snapped, project.entryCanvasId, undefined, {createContacts: false}).links.filter(link => link.snap?.locked).length, 1);
});
