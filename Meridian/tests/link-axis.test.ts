import test from 'node:test';
import assert from 'node:assert/strict';
import { axisLockedPoint, orthogonalFinish, linkPivotObstacles, constrainDraftRoute } from '../src/core/link-axis.ts';
import { shapesOverlap } from '../src/core/geometry.ts';
import { newProject, addNode } from '../src/core/model.ts';
import { createLinkPath } from '../src/core/connections.ts';
import { resolveCanvasCollisions } from '../src/core/collision.ts';

test('Shift projects the next route point to the closest horizontal or vertical axis in either direction', () => {
  const origin = { x: 100, y: 80 };
  assert.deepEqual(axisLockedPoint(origin, { x: 360, y: 130 }), { x: 360, y: 80 });
  assert.deepEqual(axisLockedPoint(origin, { x: 70, y: -220 }), { x: 100, y: -220 });
  assert.deepEqual(axisLockedPoint(origin, { x: -200, y: 100 }), { x: -200, y: 80 });
  assert.deepEqual(axisLockedPoint(origin, { x: 150, y: 130 }), { x: 150, y: 80 });
  assert.deepEqual(origin, { x: 100, y: 80 });
});

test('finishing on an offset destination inserts one right angle while aligned destinations need no pivot', () => {
  const origin = { x: 100, y: 80 }, destination = { x: 360, y: 240 };
  const route = [origin, ...orthogonalFinish(origin, destination), destination];
  assert.equal(route.length, 3);
  for (let i = 1; i < route.length; i++) assert.ok(route[i]!.x === route[i - 1]!.x || route[i]!.y === route[i - 1]!.y);
  assert.deepEqual(orthogonalFinish(origin, { x: 100, y: 350 }), []);
  assert.deepEqual(orthogonalFinish(origin, { x: 250, y: 80 }), []);
  assert.deepEqual(destination, { x: 360, y: 240 });
});

test('a projected pivot slides along its locked axis when a peer occupies its requested position', () => {
  const origin = { x: 100, y: 80 }, obstacles = [{ x: 200, y: 40, width: 120, height: 90, shape: 'rectangle' as const }];
  const point = axisLockedPoint(origin, { x: 280, y: 100 }, obstacles);
  assert.equal(point.y, origin.y);
  assert.equal(point.x, 327);
  assert.ok(!shapesOverlap({ ...point, x: point.x - 6, y: point.y - 6, width: 12, height: 12, shape: 'circle' }, obstacles[0]!));
});

test('Shift completion chooses the other clear corner and collision repair preserves the saved axes', () => {
  const project = newProject('Orthogonal peers'), canvasId = project.entryCanvasId;
  const source = addNode(project, canvasId, 'media', 'Source'), target = addNode(project, canvasId, 'media', 'Target'), peer = addNode(project, canvasId, 'media', 'Occupied corner');
  for (const [node, x, y] of [[source, 100, 100], [target, 650, 400], [peer, 650, 100]] as const)
    Object.assign(node, { x, y, width: 180, height: 100, sizeMode: 'fixed', style: { shape: 'rounded' } });
  const start = { x: 430, y: 150 }, end = { x: 740, y: 450 };
  const bends = orthogonalFinish(start, end, linkPivotObstacles(project, canvasId, source.id, target.id));
  assert.deepEqual(bends, [{ x: 430, y: 450 }]);
  const path = createLinkPath(project, { canvasId, sourceId: source.id, targetId: target.id, kind: 'visual', relation: 'generic', direction: 'none' }, [start, ...bends]);
  const repaired = resolveCanvasCollisions(path.project, canvasId);
  for (const id of path.linkIds) {
    const link = repaired.links.find(link => link.id === id)!;
    const from = repaired.nodes.find(node => node.id === link.sourceId)!, to = repaired.nodes.find(node => node.id === link.targetId)!;
    assert.ok(Math.abs(from.x + from.width / 2 - to.x - to.width / 2) < .001 || Math.abs(from.y + from.height / 2 - to.y - to.height / 2) < .001);
  }
});

test('when both corners are occupied, the extra bends are clear and all segments stay orthogonal', () => {
  const origin = { x: 0, y: 0 }, destination = { x: 200, y: 150 };
  const obstacles = [{ x: 180, y: -20, width: 40, height: 40 }, { x: -20, y: 130, width: 40, height: 40 }];
  const bends = orthogonalFinish(origin, destination, obstacles), route = [origin, ...bends, destination];
  assert.equal(bends.length, 2);
  for (const point of bends) assert.ok(obstacles.every(obstacle => !shapesOverlap({ x: point.x - 6, y: point.y - 6, width: 12, height: 12, shape: 'circle' }, obstacle)));
  for (let i = 1; i < route.length; i++) assert.ok(route[i]!.x === route[i - 1]!.x || route[i]!.y === route[i - 1]!.y);
});

test('a pending Shift pivot inside its expanded bounded source ancestor stays at the requested interior position', () => {
  const project = newProject('Interior Shift route'), canvasId = project.entryCanvasId;
  const parent = addNode(project, canvasId, 'container', 'Parent');
  Object.assign(parent, { x: 100, y: 100, width: 600, height: 300, sizeMode: 'fixed', collapsed: false, containerVariant: 'contained', childLayout: 'contained' });
  const source = addNode(project, canvasId, 'media', 'Source'), target = addNode(project, canvasId, 'media', 'Target'), external = addNode(project, canvasId, 'media', 'Outside');
  Object.assign(source, { parentId: parent.id, x: 140, y: 160, width: 80, height: 40, sizeMode: 'fixed' });
  Object.assign(target, { parentId: parent.id, x: 420, y: 260, width: 80, height: 40, sizeMode: 'fixed' });
  Object.assign(external, { x: 900, y: 200, width: 80, height: 40, sizeMode: 'fixed' });
  const before = structuredClone(project), pending = linkPivotObstacles(project, canvasId, source.id);
  assert.equal(pending.some(box => box.id === parent.id), false);
  assert.deepEqual(axisLockedPoint({ x: 180, y: 180 }, { x: 300, y: 200 }, pending), { x: 300, y: 180 });
  assert.equal(linkPivotObstacles(project, canvasId, source.id, target.id).some(box => box.id === parent.id), false);
  assert.equal(linkPivotObstacles(project, canvasId, source.id, external.id).some(box => box.id === parent.id), true, 'A source enclosure is a peer when the final owner is outside it.');
  assert.deepEqual(project, before);
});

test('remembered axes replay sequentially when destination ownership changes the pivot obstacles', () => {
  const origin = { x: 180, y: 180 }, points = [{ x: 300, y: 180 }, { x: 300, y: 350 }], directions = ['x', 'y'] as const;
  const obstacles = [{ x: 100, y: 100, width: 600, height: 300, shape: 'rectangle' as const }];
  const before = structuredClone(points), result = constrainDraftRoute(origin, points, directions, obstacles);
  assert.deepEqual(result, [{ x: 93, y: 180 }, { x: 93, y: 350 }]);
  assert.equal(result[0]!.y, origin.y); assert.equal(result[1]!.x, result[0]!.x);
  for (const point of result) assert.ok(!shapesOverlap({ x: point.x - 6, y: point.y - 6, width: 12, height: 12, shape: 'circle' }, obstacles[0]!));
  assert.deepEqual(points, before);
  assert.deepEqual(axisLockedPoint(origin, { x: 500, y: 190 }, [], 'y'), { x: 180, y: 190 }, 'An explicitly remembered axis does not switch to the closer axis.');
});

test('draft replay keeps free points intact and separates repeated locked pivots along their remembered axis', () => {
  const origin = { x: 10, y: 10 }, points = [{ x: 100, y: 75 }, { x: 100, y: 75 }];
  const result = constrainDraftRoute(origin, points, [undefined, 'x'], []);
  assert.deepEqual(result[0], points[0]); assert.notEqual(result[0], points[0]);
  assert.equal(result[1]!.y, result[0]!.y); assert.ok(Math.abs(result[1]!.x - result[0]!.x) >= 12);
  assert.deepEqual(points, [{ x: 100, y: 75 }, { x: 100, y: 75 }]);
});

test('a newly blocked free pivot is repaired before its following remembered Shift segment is locked', () => {
  const project = newProject('Cross owner free then Shift'), canvasId = project.entryCanvasId;
  const source = addNode(project, canvasId, 'media', 'Source'), target = addNode(project, canvasId, 'media', 'Target'), blocker = addNode(project, canvasId, 'container', 'Former enclosing owner');
  Object.assign(source, { x: -200, y: 0, width: 80, height: 40, sizeMode: 'fixed' });
  Object.assign(target, { x: 500, y: 300, width: 80, height: 40, sizeMode: 'fixed' });
  Object.assign(blocker, { x: 100, y: 100, width: 180, height: 120, sizeMode: 'fixed', style: { shape: 'rectangle' } });
  const points = [{ x: 130, y: 160 }, { x: 130, y: 280 }], before = structuredClone(points);
  const resolved = constrainDraftRoute({ x: -160, y: 20 }, points, [undefined, 'y'], linkPivotObstacles(project, canvasId, source.id, target.id));
  assert.notDeepEqual(resolved[0], points[0], 'The former interior point becomes a free route utility outside the final obstacle.');
  assert.equal(resolved[1]!.x, resolved[0]!.x);
  assert.deepEqual(points, before);
  const path = createLinkPath(project, { canvasId, sourceId: source.id, targetId: target.id, kind: 'visual', relation: 'generic', direction: 'none' }, resolved);
  const repaired = resolveCanvasCollisions(path.project, canvasId, path.nodeIds);
  const first = repaired.nodes.find(node => node.id === path.nodeIds[0])!, second = repaired.nodes.find(node => node.id === path.nodeIds[1])!;
  assert.equal(first.x + first.width / 2, resolved[0]!.x); assert.equal(first.y + first.height / 2, resolved[0]!.y);
  assert.equal(second.x + second.width / 2, resolved[1]!.x); assert.equal(second.y + second.height / 2, resolved[1]!.y);
  assert.equal(first.x + first.width / 2, second.x + second.width / 2, 'Canvas collision repair preserves the following Shift segment.');
});
