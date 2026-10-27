import test from 'node:test';
import assert from 'node:assert/strict';
import { addCanvas, addNode, newProject } from '../src/core/model.ts';
import { createLockedClusterIndex } from '../src/core/clusters.ts';
import { collisionFreeTranslation, collisionNodeShapes, resolveCanvasCollisions } from '../src/core/collision.ts';
import { nodeShape, shapesOverlap } from '../src/core/geometry.ts';
import { navigationIndex } from '../src/core/navigation.ts';
import { sizingBounds } from '../src/core/sizing.ts';
import type { MeridianProject, MeridianNode } from '../src/core/types.ts';

function tile(project: MeridianProject, title: string, canvasId = project.entryCanvasId) {
  const node = addNode(project, canvasId, 'media', title);
  Object.assign(node, { x: 0, y: 0, width: 100, height: 100, sizeMode: 'fixed', collapsed: false, childLayout: 'spatial', style: { shape: 'rectangle' } });
  return node;
}
function contact(project: MeridianProject, a: MeridianNode, b: MeridianNode, locked = true) {
  project.links.push({ id: crypto.randomUUID(), canvasId: a.canvasId, sourceId: a.id, targetId: b.id, kind: 'visual', relation: 'generic', direction: 'none', snap: { locked } });
}

test('transaction movement index includes transitive locked peers and hidden descendants once in project order', () => {
  const project = newProject('Movement index'), a = tile(project, 'A'), b = tile(project, 'B'), c = tile(project, 'Hidden child'), d = tile(project, 'Child peer'), e = tile(project, 'Grandchild'), independent = tile(project, 'Unlocked');
  b.collapsed = true; c.parentId = b.id; e.parentId = d.id; contact(project, a, b); contact(project, c, d); contact(project, d, independent, false);
  const before = structuredClone(project), movement = createLockedClusterIndex(project, project.entryCanvasId);
  assert.deepEqual(movement([a.id, d.id, a.id, 'missing', project.canvases[0]!.rootId]), [a.id, b.id, c.id, d.id, e.id]);
  assert.deepEqual(movement([independent.id]), [independent.id]); assert.deepEqual(project, before);
  a.x += 200; c.y -= 300; assert.deepEqual(movement([a.id]), [a.id, b.id, c.id, d.id, e.id], 'position changes do not invalidate ownership/connectivity');
});

test('movement index terminates through ownership and locked-link cycles without crossing Works', () => {
  const project = newProject('Defensive index'), a = tile(project, 'A'), b = tile(project, 'B'), c = tile(project, 'C'), otherCanvas = addCanvas(project, 'Other'), other = tile(project, 'Other', otherCanvas.id);
  a.parentId = c.id; b.parentId = a.id; c.parentId = b.id; contact(project, a, b); contact(project, b, c); contact(project, c, a);
  // Invalid imported relationships must not drag objects from another Work.
  other.parentId = a.id; contact(project, a, other);
  const movement = createLockedClusterIndex(project, project.entryCanvasId);
  assert.deepEqual(movement([c.id, a.id, other.id]), [a.id, b.id, c.id]); assert.deepEqual(movement([other.id]), []);
  assert.deepEqual(createLockedClusterIndex(project, otherCanvas.id)([other.id]), [other.id]);
});

test('fresh movement transactions reflect locking and reparenting without stale global connectivity', () => {
  const project = newProject('Topology transactions'), a = tile(project, 'A'), b = tile(project, 'B'), child = tile(project, 'Child');
  child.parentId = a.id; const first = createLockedClusterIndex(project, project.entryCanvasId); assert.deepEqual(first([a.id]), [a.id, child.id]);
  contact(project, a, b); child.parentId = b.id;
  const locked = createLockedClusterIndex(project, project.entryCanvasId); assert.deepEqual(locked([a.id]), [a.id, b.id, child.id]);
  project.links[0]!.snap!.locked = false;
  const unlocked = createLockedClusterIndex(project, project.entryCanvasId); assert.deepEqual(unlocked([a.id]), [a.id]); assert.deepEqual(unlocked([b.id]), [b.id, child.id]);
});

test('indexed sizing agrees with scanning for ordinary, contained and exterior Gallery children after parent changes', () => {
  for (const variant of ['contained', 'horizontal', 'vertical', 'gallery'] as const) {
    const project = newProject('Parent resolver'), parent = addNode(project, project.entryCanvasId, variant === 'gallery' ? 'media' : 'container', 'Parent'), child = tile(project, 'Child');
    Object.assign(parent, { x: 40, y: 60, width: 640, height: 480, sizeMode: 'fixed', collapsed: false, containerVariant: variant, grid: { rows: 1, columns: 1, cellWidth: 240, cellHeight: 140 } });
    child.parentId = parent.id; child.x = 70; child.y = 90;
    if (variant === 'horizontal' || variant === 'vertical') child.cell = { row: 1, column: 1 };
    const nodes = new Map(project.nodes.map(node => [node.id, node]));
    const indexed = () => sizingBounds(project, child, { node: id => nodes.get(id) });
    assert.deepEqual(indexed(), sizingBounds(project, child), variant);
    parent.x += 90; parent.y += 40; parent.width += 180; parent.height += 100;
    assert.deepEqual(indexed(), sizingBounds(project, child), `${variant}: moved/resized parent`);
    parent.collapsed = true; assert.deepEqual(indexed(), sizingBounds(project, child), `${variant}: collapsed parent`);
  }
});

test('indexed sizing ignores a parent from another Work and handles missing parent identities', () => {
  const project = newProject('Work boundaries'), other = addCanvas(project, 'Other'), parent = addNode(project, other.id, 'container', 'Foreign parent'), child = tile(project, 'Child');
  Object.assign(parent, { width: 900, height: 600, sizeMode: 'fixed', containerVariant: 'horizontal', grid: { rows: 1, columns: 1, cellWidth: 800, cellHeight: 500 } });
  child.parentId = parent.id; child.cell = { row: 1, column: 1 };
  assert.deepEqual(sizingBounds(project, child, { node: id => project.nodes.find(node => node.id === id) }), sizingBounds(project, child));
  assert.equal(sizingBounds(project, child).width, child.width);
  child.parentId = 'missing'; assert.deepEqual(sizingBounds(project, child, { node: () => undefined }), sizingBounds(project, child));
});

test('collision cascade uses translated obstacles, preserves an anchored peer and reaches a stable collision-free result', () => {
  const project = newProject('Collision cache invalidation'), anchor = tile(project, 'Anchor'), a = tile(project, 'A'), b = tile(project, 'B'), c = tile(project, 'C');
  anchor.positionLocked = true; const before = structuredClone(project);
  const repaired = resolveCanvasCollisions(project, project.entryCanvasId), index = navigationIndex(repaired), visible = [anchor, a, b, c].map(node => index.nodes.get(node.id)!);
  assert.deepEqual(index.nodes.get(anchor.id), anchor);
  for (let i = 0; i < visible.length; i++) for (let j = i + 1; j < visible.length; j++) assert.equal(shapesOverlap(nodeShape(visible[i]!, index.bounds(visible[i]!)), nodeShape(visible[j]!, index.bounds(visible[j]!))), false, `${visible[i]!.title}/${visible[j]!.title}`);
  assert.deepEqual(project, before); assert.deepEqual(repaired.nodes.map(node => node.id), project.nodes.map(node => node.id));
  assert.equal(resolveCanvasCollisions(repaired, project.entryCanvasId), repaired);
});

test('collision translation permits intentional containment while still escaping an overlapping external peer', () => {
  const child = { id: 'child', x: 100, y: 100, width: 50, height: 50, shape: 'rectangle' as const };
  const container = { id: 'container', x: 0, y: 0, width: 400, height: 400, shape: 'rectangle' as const };
  const peer = { id: 'peer', x: 120, y: 110, width: 50, height: 50, shape: 'rectangle' as const };
  const allowance = (first: { id?: string }, second: { id?: string }) => first.id === 'child' && second.id === 'container';
  const delta = collisionFreeTranslation([child], [container, peer], { x: 0, y: 0 }, allowance);
  assert.deepEqual(delta, collisionFreeTranslation([child], [peer], { x: 0, y: 0 }));
  const moved = { ...child, x: child.x + delta.x, y: child.y + delta.y };
  assert.equal(shapesOverlap(moved, peer), false); assert.equal(shapesOverlap(moved, container), true);
  assert.deepEqual(collisionFreeTranslation([child], [container], { x: 7, y: 9 }, allowance), { x: 7, y: 9 });
});

test('collision repair keeps locked peer offsets, hidden descendants and internal link bends across several translations', () => {
  const project = newProject('Rigid branch cache'), anchor = tile(project, 'Anchor'), first = tile(project, 'First'), second = tile(project, 'Second'), child = tile(project, 'Hidden'), last = tile(project, 'Last');
  anchor.positionLocked = true; second.x = 100; first.collapsed = true; child.parentId = first.id; child.x = 20; child.y = 240; contact(project, first, second);
  const route = { id: crypto.randomUUID(), canvasId: project.entryCanvasId, sourceId: first.id, targetId: child.id, kind: 'visual' as const, relation: 'generic' as const, points: [{ x: 50, y: 180 }] }; project.links.push(route);
  const repaired = resolveCanvasCollisions(project, project.entryCanvasId), index = navigationIndex(repaired), a = index.nodes.get(first.id)!, b = index.nodes.get(second.id)!, hidden = index.nodes.get(child.id)!;
  assert.equal(b.x - a.x, 100); assert.equal(b.y - a.y, 0); assert.equal(hidden.x - a.x, 20); assert.equal(hidden.y - a.y, 240);
  assert.deepEqual(repaired.links.find(link => link.id === route.id)!.points, [{ x: 50 + a.x, y: 180 + a.y }]);
  const nodes = [anchor, first, second, last].map(node => index.nodes.get(node.id)!);
  for (let i = 0; i < nodes.length; i++) for (let j = i + 1; j < nodes.length; j++) assert.ok(collisionNodeShapes(repaired, nodes[i]!, index.bounds(nodes[i]!)).every(one => collisionNodeShapes(repaired, nodes[j]!, index.bounds(nodes[j]!)).every(two => !shapesOverlap(one, two))));
  assert.equal(resolveCanvasCollisions(repaired, project.entryCanvasId), repaired);
});
