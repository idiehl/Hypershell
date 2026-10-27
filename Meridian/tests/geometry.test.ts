import test from 'node:test';
import assert from 'node:assert/strict';
import { nearestBoundary, nodeShape, shapeAnchor, shapeContact, shapePath, shapesOverlap, shapeSeparation, type ShapeBox } from '../src/core/geometry.ts';
import { collisionFreeResize, collisionFreeTranslation, isIntentionalContainment, resolveCanvasCollisions } from '../src/core/collision.ts';
import { snapPosition } from '../src/core/snapping.ts';
import { addNode, newProject } from '../src/core/model.ts';
import { navigationIndex } from '../src/core/navigation.ts';
import { isEdgeAttached, revealChildren } from '../src/core/reveal.ts';

const rectangle = (x: number, y: number, width = 100, height = 100): ShapeBox => ({ x, y, width, height, shape: 'rectangle' });
const shift = (box: ShapeBox, point: { x: number; y: number }) => ({ ...box, x: box.x + point.x, y: box.y + point.y });

test('legacy content parent flags cannot permit overlapping children, while formal bounded containers still enclose them', () => {
  for (const kind of ['text', 'output', 'container'] as const) {
    const project = newProject('Legacy parent collision'), canvasId = project.entryCanvasId;
    const parent = addNode(project, canvasId, kind, 'Parent');
    Object.assign(parent, { x: 100, y: 100, width: 300, height: 200, sizeMode: 'fixed', childLayout: 'contained', collapsed: false });
    const child = addNode(project, canvasId, 'text', 'Child');
    Object.assign(child, { parentId: parent.id, x: 160, y: 150, width: 80, height: 60, sizeMode: 'fixed' });
    const index = navigationIndex(project), before = structuredClone(project);
    assert.equal(shapesOverlap(index.bounds(parent), index.bounds(child)), true);
    assert.equal(isIntentionalContainment(index, parent.id, child.id), kind === 'container');
    assert.equal(isIntentionalContainment(index, child.id, parent.id), kind === 'container');
    const repaired = resolveCanvasCollisions(project, canvasId), next = navigationIndex(repaired);
    assert.equal(shapesOverlap(next.bounds(next.nodes.get(parent.id)!), next.bounds(next.nodes.get(child.id)!)), kind === 'container');
    assert.deepEqual(next.nodes.get(parent.id), parent);
    if (kind === 'container') assert.deepEqual(next.nodes.get(child.id), child);
    else assert.notDeepEqual({ x: next.nodes.get(child.id)!.x, y: next.nodes.get(child.id)!.y }, { x: child.x, y: child.y });
    assert.deepEqual(project, before, 'collision repair is explicit and leaves the saved source geometry untouched');
  }
});

test('polygon collision tests backdrop edges instead of overlapping bounding boxes', () => {
  const diamond = { ...rectangle(0, 0), shape: 'diamond' as const }, corner = rectangle(85, 0, 30, 20);
  assert.equal(shapesOverlap(diamond, corner), false);
  assert.equal(shapesOverlap(diamond, rectangle(70, 30, 30, 30)), true);
  const separation = shapeSeparation(rectangle(70, 30, 30, 30), diamond)!;
  assert.ok(Math.abs(separation.x) > 0 && Math.abs(separation.y) > 0);
  assert.equal(shapesOverlap(shift(rectangle(70, 30, 30, 30), separation), diamond), false);
});

test('sticky contact follows the diagonal edge of a triangular backdrop', () => {
  const parent = { ...rectangle(0, 0), shape: 'triangle' as const }, child = rectangle(72, 20, 20, 20);
  const result = snapPosition(child, [parent], { stickyEdges: true });
  assert.ok(result.x < child.x && result.y > child.y);
  assert.ok(result.guides[0].fromPoint && result.guides[0].toPoint);
  assert.equal(shapesOverlap({ ...child, x: result.x, y: result.y }, parent), false);
  const distance = shapeContact({ ...child, x: result.x, y: result.y }, parent, .001)!;
  assert.ok(Math.hypot(distance.x, distance.y) < .001);
});

test('curved sticky contact uses exact circle support at arbitrary angles', () => {
  const parent = { ...rectangle(0, 0), shape: 'circle' as const }, child = { ...rectangle(94, 72, 40, 40), shape: 'circle' as const };
  const delta = shapeContact(child, parent, 8)!;
  assert.ok(delta.x < 0 && delta.y < 0);
  const snapped = shift(child, delta);
  const centers = Math.hypot(snapped.x + 20 - 50, snapped.y + 20 - 50);
  assert.ok(Math.abs(centers - 70) < 1e-6);
  assert.equal(shapesOverlap(snapped, parent), false);
  assert.equal(shapeContact(child, parent, 4), null);
});

test('curved collision never permits slight intersection between tangent samples', () => {
  const a = { ...rectangle(0, 0), shape: 'ellipse' as const }, angle = Math.PI * .173;
  const b = { ...rectangle(Math.cos(angle) * 99.999, Math.sin(angle) * 99.999), shape: 'ellipse' as const };
  assert.equal(shapesOverlap(a, b), true);
  const free = collisionFreeTranslation([b], [a], { x: 0, y: 0 });
  assert.equal(shapesOverlap(shift(b, free), a), false);
});

test('square and circle use centered minimum-side geometry; no backdrop uses content bounds', () => {
  const wide = { ...rectangle(0, 0, 200, 100), shape: 'circle' as const };
  assert.deepEqual(shapeAnchor(wide, 1, 0), { x: 150, y: 50 });
  assert.equal(shapesOverlap(wide, rectangle(0, 0, 35, 35)), false);
  assert.equal(shapesOverlap({ ...wide, shape: 'none' }, rectangle(0, 0, 35, 35)), true);
  assert.equal(shapePath({ ...wide, shape: 'square' }), 'M50,0 L150,0 L150,100 L50,100Z');
});

test('outline paths and real edge hit testing follow polygon and ellipse backdrops', () => {
  const diamond = { ...rectangle(0, 0), shape: 'diamond' as const };
  assert.equal(shapePath(diamond), 'M50,0 L100,50 L50,100 L0,50Z');
  assert.equal(nearestBoundary(diamond, { x: 100, y: 0 }).distance > 30, true);
  const edge = nearestBoundary(diamond, { x: 75, y: 25 });
  assert.ok(edge.distance < 1e-8); assert.ok(edge.normal.x > .7 && edge.normal.y < -.7);
  assert.match(shapePath({ ...rectangle(0, 0, 180, 100), shape: 'ellipse' }), /a90,50/);
});

test('default outlines match legacy rounded joints, container frames, labels and explicit rounded shape radius', () => {
  const project = newProject('Default geometry'), canvasId = project.entryCanvasId;
  const joint = addNode(project, canvasId, 'joint', 'Joint'), frame = addNode(project, canvasId, 'container', 'Frame'), text = addNode(project, canvasId, 'text', 'Label');
  assert.equal(nodeShape(joint).shape, 'rounded'); assert.equal(nodeShape(joint).radius, 11);
  assert.equal(nodeShape(frame).radius, 15);
  text.style = { variant: 'label' }; assert.equal(nodeShape(text).shape, 'none');
  text.style = { shape: 'rounded' }; assert.equal(nodeShape(text).radius, 20);
});

test('whole branch collision escape preserves relative coordinates around multiple peers', () => {
  const moving = [rectangle(0, 0, 80, 80), rectangle(100, 0, 30, 30)];
  const obstacles = [rectangle(70, 20, 40, 40), rectangle(140, -20, 40, 40), rectangle(20, 100, 120, 20)];
  const delta = collisionFreeTranslation(moving, obstacles, { x: 40, y: 20 });
  assert.ok(moving.every(box => obstacles.every(obstacle => !shapesOverlap(shift(box, delta), obstacle))));
  assert.deepEqual(collisionFreeTranslation(moving, [...obstacles].reverse(), { x: 40, y: 20 }), delta);
});

test('resize stops at collision while preserving the opposite edge', () => {
  const box = rectangle(100, 100, 100, 100), obstacle = rectangle(0, 100, 60, 100);
  const resized = collisionFreeResize(box, [obstacle], { ...box, x: 30, width: 170 });
  assert.ok(Math.abs(resized.x - 60) < 1e-5);
  assert.ok(Math.abs(resized.x + resized.width - 200) < 1e-5);
  assert.equal(shapesOverlap(resized, obstacle), false);
});

test('canvas collision repair excludes containment, keeps identity and shifts internal routes with branches', () => {
  const project = newProject('Collision repair'), canvasId = project.entryCanvasId;
  const frame = addNode(project, canvasId, 'container', 'Frame'); Object.assign(frame, { sizeMode: 'fixed', x: 0, y: 0, width: 300, height: 300, childLayout: 'contained' });
  const inside = addNode(project, canvasId, 'text', 'Inside'); Object.assign(inside, { sizeMode: 'fixed', parentId: frame.id, x: 40, y: 80, width: 150, height: 100 });
  const other = addNode(project, canvasId, 'container', 'Other'); Object.assign(other, { sizeMode: 'fixed', x: 200, y: 100, width: 200, height: 180 });
  const hidden = addNode(project, canvasId, 'text', 'Hidden'); Object.assign(hidden, { sizeMode: 'fixed', parentId: other.id, x: 220, y: 140, width: 100, height: 80, collapsed: true });
  project.links.push({ id: 'inner', canvasId, sourceId: other.id, targetId: hidden.id, parentId: other.id, kind: 'visual', relation: 'generic', points: [{ x: 250, y: 125 }] });
  const repaired = resolveCanvasCollisions(project, canvasId, [other.id]);
  assert.equal(project.nodes.find(node => node.id === other.id)!.x, 200);
  assert.deepEqual(repaired.nodes.map(node => node.id), project.nodes.map(node => node.id));
  const adjusted = repaired.nodes.find(node => node.id === other.id)!, child = repaired.nodes.find(node => node.id === hidden.id)!;
  assert.equal(child.x - adjusted.x, hidden.x - other.x); assert.equal(child.y - adjusted.y, hidden.y - other.y);
  assert.equal(repaired.links[0].points![0].x - 250, adjusted.x - other.x);
  assert.equal(shapesOverlap(nodeShape(frame), nodeShape(adjusted)), false);
  assert.deepEqual(repaired.nodes.find(node => node.id === inside.id), inside);
  assert.equal(resolveCanvasCollisions(repaired, canvasId), repaired);
});

test('disclosure repairs free peer collisions and edge children contact a shaped parent', () => {
  const project = newProject('Shaped disclosure'), canvasId = project.entryCanvasId;
  const parent = addNode(project, canvasId, 'text', 'Parent'); Object.assign(parent, { sizeMode: 'fixed', x: 0, y: 0, width: 300, height: 240, collapsed: true, childLayout: 'spatial', style: { shape: 'hexagon' } });
  const a = addNode(project, canvasId, 'text', 'A'); Object.assign(a, { sizeMode: 'fixed', parentId: parent.id, x: 280, y: 35, width: 120, height: 70, placement: 'edge' });
  const b = addNode(project, canvasId, 'text', 'B'); Object.assign(b, { sizeMode: 'fixed', parentId: parent.id, x: 280, y: 35, width: 120, height: 70, placement: 'free' });
  const revealed = revealChildren(project, parent.id, true), index = navigationIndex(revealed), shownA = index.nodes.get(a.id)!, shownB = index.nodes.get(b.id)!;
  assert.equal(shapesOverlap(nodeShape(shownA), nodeShape(shownB)), false);
  assert.ok(isEdgeAttached(nodeShape(index.nodes.get(parent.id)!), nodeShape(shownA)));
});

test('spatial ownership forbids parent-child overlap while enclosed content stays in its frame', () => {
  const project = newProject('Spatial ownership versus enclosure'), canvasId = project.entryCanvasId;
  const spatial = addNode(project, canvasId, 'text', 'Spatial parent');
  Object.assign(spatial, { sizeMode: 'fixed', x: 0, y: 0, width: 250, height: 180, collapsed: false, childLayout: 'spatial' });
  const child = addNode(project, canvasId, 'text', 'Spatial child');
  Object.assign(child, { sizeMode: 'fixed', parentId: spatial.id, x: 40, y: 40, width: 120, height: 90, childLayout: 'spatial' });
  const frame = addNode(project, canvasId, 'container', 'Enclosing frame');
  Object.assign(frame, { sizeMode: 'fixed', x: 500, y: 0, width: 300, height: 300, collapsed: false, childLayout: 'contained' });
  const inside = addNode(project, canvasId, 'text', 'Contained child');
  Object.assign(inside, { sizeMode: 'fixed', parentId: frame.id, x: 540, y: 80, width: 120, height: 90 });
  const repaired = resolveCanvasCollisions(project, canvasId), index = navigationIndex(repaired);
  assert.equal(isIntentionalContainment(index, spatial.id, child.id), false);
  assert.equal(isIntentionalContainment(index, frame.id, inside.id), true);
  assert.equal(shapesOverlap(nodeShape(index.nodes.get(spatial.id)!), nodeShape(index.nodes.get(child.id)!)), false);
  assert.deepEqual(index.nodes.get(frame.id), frame); assert.deepEqual(index.nodes.get(inside.id), inside);
  assert.equal(resolveCanvasCollisions(repaired, canvasId), repaired);
});

test('crowded cross-branch disclosure leaves every visible peer collision-free without changing graph identity', () => {
  let project = newProject('Crowded branches'); const canvasId = project.entryCanvasId, parents: string[] = [];
  for (let chapter = 0; chapter < 8; chapter++) {
    const parent = addNode(project, canvasId, 'text', `Chapter ${chapter}`); parents.push(parent.id);
    Object.assign(parent, { x: chapter % 4 * 340, y: Math.floor(chapter / 4) * 360, width: 160, height: 100, collapsed: true, childLayout: 'spatial' });
    for (let item = 0; item < 8; item++) {
      const node = addNode(project, canvasId, 'text', `Detail ${chapter}.${item}`);
      Object.assign(node, { parentId: parent.id, x: parent.x + 190, y: parent.y + item * 95, width: 160, height: 90, placement: 'free', style: { shape: item % 2 ? 'hexagon' : 'rectangle' } });
    }
  }
  const ids = project.nodes.map(node => node.id), ownership = project.nodes.map(node => node.parentId);
  for (const id of parents) project = revealChildren(project, id, true);
  const index = navigationIndex(project), visible = project.nodes.filter(node => node.kind !== 'canvas' && index.isNodeVisible(node.id));
  for (let i = 0; i < visible.length; i++) for (let j = i + 1; j < visible.length; j++) {
    const a = visible[i], b = visible[j];
    if (isIntentionalContainment(index, a.id, b.id)) continue;
    assert.equal(shapesOverlap(nodeShape(a, index.bounds(a)), nodeShape(b, index.bounds(b))), false, `${a.title} / ${b.title}`);
  }
  assert.deepEqual(project.nodes.map(node => node.id), ids); assert.deepEqual(project.nodes.map(node => node.parentId), ownership);
});
