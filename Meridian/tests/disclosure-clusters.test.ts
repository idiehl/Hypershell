import test from 'node:test';
import assert from 'node:assert/strict';
import { addCanvas, addNode, commitSnapshots, forkProject, newProject } from '../src/core/model.ts';
import { createDisclosureCluster, DISCLOSURE_CLUSTER_BUTTON_SIZE, isDisclosureCluster, setDisclosureClusterShape } from '../src/core/disclosure-clusters.ts';
import { navigationIndex } from '../src/core/navigation.ts';
import { moveSelection } from '../src/core/selection.ts';
import { revealChildren } from '../src/core/reveal.ts';
import { isDisclosureClusterSettings, isProjectShape, validateProject } from '../src/core/rules.ts';
import { nodeShape } from '../src/core/geometry.ts';
import { createPublicationPayload } from '../src/publication.ts';
import { copyNodes, pasteNodes } from '../src/core/clipboard.ts';
import { createModule, instantiateModule } from '../src/core/modules.ts';
import { boundedChildInterior } from '../src/core/sizing.ts';
import type { MeridianProject } from '../src/core/types.ts';

const get = (project: MeridianProject, id: string) => project.nodes.find(node => node.id === id)!;
function fixture() {
  const project = newProject('Disclosure Clusters'), canvasId = project.entryCanvasId;
  const a = addNode(project, canvasId, 'media', 'First'), b = addNode(project, canvasId, 'media', 'Second');
  Object.assign(a, { x: 100, y: 100, width: 120, height: 80, sizeMode: 'fixed', childPosition: 'manual', collapsed: true });
  Object.assign(b, { x: 620, y: 350, width: 120, height: 80, sizeMode: 'fixed', childPosition: 'manual' });
  const hidden = addNode(project, canvasId, 'media', 'Retained hidden detail');
  Object.assign(hidden, { parentId: a.id, x: 280, y: 180, width: 100, height: 70, sizeMode: 'fixed', childPosition: 'manual' });
  project.links.push({ id: crypto.randomUUID(), canvasId, sourceId: a.id, targetId: b.id, kind: 'visual', relation: 'causal', points: [{ x: 480, y: 150 }] });
  return { project, a, b, hidden };
}

test('separated branches form one named expanded Cluster without changing graph identities, relative geometry or hidden state', () => {
  const f = fixture(), before = structuredClone(f.project), result = createDisclosureCluster(f.project, [f.a.id, f.b.id], { name: '  Family and context  ' });
  const root = get(result.project, result.rootId);
  assert.equal(isDisclosureCluster(root), true); assert.equal(root.title, 'Family and context'); assert.equal(root.tag, root.title);
  assert.deepEqual(root.cluster, { buttonShape: 'square' }); assert.equal(root.collapsed, false);
  assert.equal(root.kind, 'container'); assert.equal(root.containerVariant, 'contained'); assert.equal(root.childArrangement, 'free');
  assert.deepEqual(root.containerSizes?.collapsed, { width: 64, height: 64, sizeMode: 'fixed' });
  for (const original of before.nodes) {
    const actual = get(result.project, original.id);
    assert.deepEqual([actual.x, actual.y, actual.width, actual.height, actual.collapsed], [original.x, original.y, original.width, original.height, original.collapsed]);
    assert.equal(actual.parentId, [f.a.id, f.b.id].includes(original.id) ? root.id : original.parentId);
  }
  assert.deepEqual(result.project.links, before.links); assert.deepEqual(f.project, before);
  assert.deepEqual(validateProject(result.project), []);
});

test('selecting an edge-locked member includes the complete lock group and its descendants', () => {
  const f = fixture(), peer = addNode(f.project, f.project.entryCanvasId, 'media', 'Touching peer');
  Object.assign(peer, { x: 220, y: 100, width: 120, height: 80, sizeMode: 'fixed', childPosition: 'manual' });
  const lock = { id: crypto.randomUUID(), canvasId: f.project.entryCanvasId, sourceId: f.a.id, targetId: peer.id, kind: 'visual' as const, relation: 'generic' as const, direction: 'none' as const, snap: { locked: true } };
  f.project.links.push(lock); assert.deepEqual(validateProject(f.project), []);
  const before = structuredClone(f.project), result = createDisclosureCluster(f.project, [f.a.id, f.b.id], { name: 'Locked and separated', buttonShape: 'circle' });
  for (const member of [f.a, f.b, peer]) assert.equal(get(result.project, member.id).parentId, result.rootId);
  assert.equal(get(result.project, f.hidden.id).parentId, f.a.id); assert.deepEqual(result.project.links, before.links);
  assert.deepEqual(validateProject(result.project), []); assert.deepEqual(f.project, before);
});

test('collapse, reopen and moving the button preserve exact authored layering, branch states and internal route bends', () => {
  const f = fixture(); Object.assign(f.b, { x: 140, y: 120 }); // Deliberately layered composition must not be repacked.
  get(f.project, f.a.id).positionLocked = true;
  const result = createDisclosureCluster(f.project, [f.a.id, f.b.id], { name: 'Layered composition', buttonShape: 'circle' });
  let current = result.project;
  const expanded = structuredClone(get(current, result.rootId).containerSizes!.expanded), originals = current.nodes.filter(node => node.id !== result.rootId).map(node => [node.id, node.x, node.y, node.width, node.height, node.collapsed]);
  for (let cycle = 0; cycle < 4; cycle++) {
    current = revealChildren(current, result.rootId, false);
    const closed = get(current, result.rootId), nav = navigationIndex(current);
    assert.deepEqual([closed.width, closed.height], [DISCLOSURE_CLUSTER_BUTTON_SIZE, DISCLOSURE_CLUSTER_BUTTON_SIZE]);
    assert.equal(nodeShape(closed).shape, 'circle'); assert.equal(nav.isNodeVisible(f.a.id), false); assert.equal(nav.isNodeVisible(f.b.id), false);
    current = revealChildren(current, result.rootId, true);
    assert.deepEqual(get(current, result.rootId).containerSizes!.expanded, expanded);
    assert.equal(nodeShape(get(current, result.rootId)).shape, 'rectangle');
    assert.deepEqual(current.nodes.filter(node => node.id !== result.rootId).map(node => [node.id, node.x, node.y, node.width, node.height, node.collapsed]), originals);
  }
  // A pinned member should continue protecting the whole assembly against movement.
  assert.throws(() => moveSelection(revealChildren(current, result.rootId, false), [result.rootId], 90, -30), /Unlock/);
  get(current, f.a.id).positionLocked = false;
  const closed = revealChildren(current, result.rootId, false), moved = moveSelection(closed, [result.rootId], 90, -30), reopened = revealChildren(moved, result.rootId, true);
  for (const original of current.nodes.filter(node => node.id !== result.rootId && node.kind !== 'canvas')) {
    const actual = get(reopened, original.id); assert.equal(actual.x, original.x + 90); assert.equal(actual.y, original.y - 30); assert.equal(actual.collapsed, original.collapsed);
  }
  assert.deepEqual(reopened.links[0].points, current.links[0].points!.map(point => ({ x: point.x + 90, y: point.y - 30 })));
});

test('Cluster shape and frames persist through snapshots, JSON save/reopen, publication, forks, clipboard and Modules', () => {
  const f = fixture(), result = createDisclosureCluster(f.project, [f.a.id, f.b.id], { name: 'Portable group', buttonShape: 'circle' });
  const saved = commitSnapshots(revealChildren(result.project, result.rootId, false)), expected = get(saved, result.rootId);
  assert.equal(isProjectShape(JSON.parse(JSON.stringify(saved))), true);
  assert.deepEqual(saved.snapshots[result.rootId].node.cluster, expected.cluster);
  const portable = createPublicationPayload(saved).project;
  assert.deepEqual(get(portable, result.rootId).cluster, expected.cluster); assert.deepEqual(get(portable, result.rootId).containerSizes, expected.containerSizes);
  assert.equal(isProjectShape(portable), true); assert.deepEqual(validateProject(portable), []);
  for (const project of [forkProject(saved), pasteNodes(saved, copyNodes(saved, [result.rootId]), saved.entryCanvasId).project]) {
    for (const root of project.nodes.filter(isDisclosureCluster)) { assert.deepEqual(root.cluster, expected.cluster); assert.deepEqual(root.containerSizes, expected.containerSizes); }
  }
  const module = createModule(saved, result.rootId, 'Portable Cluster'), inserted = instantiateModule(saved, module, { canvasId: saved.entryCanvasId, x: 1000, y: 1000 });
  const insertedCluster = inserted.project.nodes.find(node => node.cluster && node.parentId === inserted.rootId)!;
  assert.deepEqual(insertedCluster.cluster, expected.cluster);
  assert.deepEqual(insertedCluster.containerSizes, expected.containerSizes);
  const changed = setDisclosureClusterShape(saved, result.rootId, 'square');
  assert.deepEqual(get(changed, result.rootId).cluster, { buttonShape: 'square' }); assert.deepEqual(get(saved, result.rootId).cluster, { buttonShape: 'circle' });
});

test('tight nested Freeform parents grow around a new Cluster without moving members, peers or routes and retain independent closed frames', () => {
  const f = fixture(), outer = addNode(f.project, f.project.entryCanvasId, 'container', 'Outer enclosure'), inner = addNode(f.project, f.project.entryCanvasId, 'container', 'Tight enclosure');
  Object.assign(outer, { x: 80, y: 70, width: 680, height: 390, sizeMode: 'fixed', collapsed: false,
    containerSizes: { expanded: { width: 680, height: 390, sizeMode: 'fixed' }, collapsed: { width: 110, height: 80, sizeMode: 'fixed' } } });
  Object.assign(inner, { parentId: outer.id, x: 90, y: 100, width: 660, height: 340, sizeMode: 'fixed', collapsed: false,
    containerSizes: { expanded: { width: 660, height: 340, sizeMode: 'fixed' }, collapsed: { width: 95, height: 70, sizeMode: 'fixed' } } });
  Object.assign(f.a, { parentId: inner.id, y: 130 }); f.b.parentId = inner.id;
  const outside = addNode(f.project, f.project.entryCanvasId, 'media', 'Unaffected outside peer'); Object.assign(outside, { x: 900, y: 200, width: 170, height: 100, sizeMode: 'fixed' });
  const before = structuredClone(f.project), result = createDisclosureCluster(f.project, [f.a.id, f.b.id], { name: 'New nested Cluster' });
  for (const original of before.nodes.filter(node => ![outer.id, inner.id].includes(node.id))) {
    const actual = get(result.project, original.id);
    assert.deepEqual([actual.x, actual.y, actual.width, actual.height, actual.collapsed], [original.x, original.y, original.width, original.height, original.collapsed]);
  }
  assert.deepEqual(result.project.links, before.links); assert.deepEqual(f.project, before);
  for (const [parentId, childId] of [[inner.id, result.rootId], [outer.id, inner.id]]) {
    const parent = get(result.project, parentId), child = get(result.project, childId), area = boundedChildInterior(result.project, parent);
    assert.ok(child.x >= area.x - .0001 && child.y >= area.y - .0001);
    assert.ok(child.x + child.width <= area.x + area.width + .0001 && child.y + child.height <= area.y + area.height + .0001);
    assert.ok(parent.width >= get(before, parentId).width && parent.height >= get(before, parentId).height);
    assert.deepEqual(parent.containerSizes!.collapsed, get(before, parentId).containerSizes!.collapsed);
  }
  const stable = structuredClone(result.project);
  let current = result.project;
  for (let cycle = 0; cycle < 3; cycle++) {
    current = revealChildren(revealChildren(current, result.rootId, false), result.rootId, true);
    current = revealChildren(revealChildren(current, outer.id, false), outer.id, true);
    for (const original of stable.nodes) {
      const actual = get(current, original.id);
      assert.deepEqual([actual.x, actual.y, actual.width, actual.height, actual.collapsed], [original.x, original.y, original.width, original.height, original.collapsed]);
      assert.deepEqual(actual.containerSizes, original.containerSizes);
    }
    assert.deepEqual(current.links, stable.links);
  }
});

test('ordered or Gallery ancestry and pinned tight parents reject creation atomically instead of changing allocation', () => {
  for (const variant of ['horizontal', 'vertical', 'grid', 'gallery', 'window'] as const) {
    const f = fixture(), parent = addNode(f.project, f.project.entryCanvasId, 'container', 'Owned child allocation');
    parent.containerVariant = variant; parent.collapsed = false;
    if (variant === 'window') parent.grid = { columns: 2, rows: 2, cellWidth: 100, cellHeight: 100, windowColumns: 1, windowRows: 1 };
    f.a.parentId = f.b.parentId = parent.id;
    const before = structuredClone(f.project);
    assert.throws(() => createDisclosureCluster(f.project, [f.a.id, f.b.id], { name: 'Cannot override allocation' }), /Freeform parents/);
    assert.deepEqual(f.project, before);
  }
  const f = fixture(), parent = addNode(f.project, f.project.entryCanvasId, 'container', 'Pinned tight frame');
  Object.assign(parent, { x: 90, y: 90, width: 660, height: 350, positionLocked: true, sizeMode: 'fixed', collapsed: false });
  f.a.parentId = f.b.parentId = parent.id;
  const before = structuredClone(f.project);
  assert.throws(() => createDisclosureCluster(f.project, [f.a.id, f.b.id], { name: 'Needs origin room' }), /Unlock the parent/);
  assert.deepEqual(f.project, before);
});

test('malformed metadata, unsupported container placement and invalid selections are rejected atomically', () => {
  const f = fixture(), result = createDisclosureCluster(f.project, [f.a.id, f.b.id], { name: 'Validated group' }), saved = commitSnapshots(result.project);
  for (const value of [null, [], {}, { buttonShape: 'triangle' }, { buttonShape: 'circle', script: 'anything' }, { buttonShape: null }]) {
    assert.equal(isDisclosureClusterSettings(value), false);
    for (const snapshot of [false, true]) {
      const candidate = structuredClone(saved), node = snapshot ? candidate.snapshots[result.rootId].node : get(candidate, result.rootId);
      (node as unknown as Record<string, unknown>).cluster = value;
      assert.equal(isProjectShape(candidate), false); assert.ok(validateProject(candidate).length);
    }
  }
  for (const fields of [{ kind: 'media' }, { containerVariant: 'gallery' }, { childLayout: 'spatial' }, { childArrangement: 'row' }, { grid: { columns: 1, rows: 1, cellWidth: 100, cellHeight: 100 } }]) {
    const candidate = structuredClone(saved); Object.assign(get(candidate, result.rootId), fields); assert.equal(isProjectShape(candidate), false);
  }
  const other = addCanvas(f.project, 'Other Work'), external = addNode(f.project, other.id, 'media', 'Outside');
  const before = structuredClone(f.project);
  for (const ids of [[], ['missing'], [f.project.canvases[0].rootId], [f.a.id, external.id], [f.hidden.id, f.b.id]])
    assert.throws(() => createDisclosureCluster(f.project, ids, { name: 'Invalid' }));
  assert.throws(() => createDisclosureCluster(f.project, [f.a.id], { name: '   ' }));
  assert.throws(() => createDisclosureCluster(f.project, [f.a.id], { name: 'Invalid', buttonShape: 'triangle' as never }));
  assert.deepEqual(f.project, before);
});
