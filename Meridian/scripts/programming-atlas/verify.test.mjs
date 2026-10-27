import assert from 'node:assert/strict';
import test from 'node:test';
import { newProject, addNode } from '../../src/core/model.ts';
import { shapeVertices } from '../../src/core/geometry.ts';
import { windowSurface } from '../../src/window-scene.ts';
import { auditScene, segmentCrossesPolygon, verifyAtlas } from './verify.mjs';

const box = { x: 10, y: 10, width: 100, height: 100, shape: 'rectangle' };
const tile = (project, title, x, y, width = 100, height = 100, kind = 'media') => Object.assign(addNode(project, project.entryCanvasId, kind, title), { x, y, width, height, sizeMode: 'fixed', style: { shape: 'rectangle', contentSizing: 'fixed', padding: 5 } });
const connect = (project, source, target, points = []) => project.links.push({ id: crypto.randomUUID(), canvasId: project.entryCanvasId, parentId: project.canvases[0].rootId, sourceId: source.id, targetId: target.id, kind: 'visual', relation: 'generic', direction: 'none', points });

test('segment clearance distinguishes actual interior, border contact, and shaped silhouette', () => {
  const rectangle = shapeVertices(box);
  assert.equal(segmentCrossesPolygon({ x: 0, y: 60 }, { x: 120, y: 60 }, rectangle), true);
  assert.equal(segmentCrossesPolygon({ x: 0, y: 10 }, { x: 120, y: 10 }, rectangle), false);
  assert.equal(segmentCrossesPolygon({ x: 0, y: 60 }, { x: 10, y: 60 }, rectangle), false);
  const circle = shapeVertices({ ...box, shape: 'circle' }, 192);
  assert.equal(segmentCrossesPolygon({ x: 10, y: 11 }, { x: 25, y: 11 }, circle), false);
  assert.equal(segmentCrossesPolygon({ x: 0, y: 60 }, { x: 120, y: 60 }, circle), true);
  const rounded = shapeVertices({ ...box, shape: 'rounded', radius: 0 });
  assert.equal(segmentCrossesPolygon({ x: 0, y: 60 }, { x: 120, y: 60 }, rounded), true);
});

test('transparent content is an obstacle, while saved bends can clear it', () => {
  const project = newProject('routing'), a = tile(project, 'A', 0, 0), b = tile(project, 'B', 400, 0), label = tile(project, 'Label', 200, 0);
  label.style.shape = 'none'; connect(project, a, b);
  assert.equal(auditScene(project, project.entryCanvasId).issues.filter(item => item.code === 'route-crosses-node').length, 1);
  project.links[0].points = [{ x: 150, y: 140 }, { x: 350, y: 140 }];
  assert.deepEqual(auditScene(project, project.entryCanvasId).issues, []);
});

test('bounded ancestors may contain their descendants; unrelated crossing still fails', () => {
  const project = newProject('containment'), parent = tile(project, 'Parent', 0, 0, 300, 300, 'container');
  const a = tile(project, 'A', 30, 30), b = tile(project, 'B', 400, 30); a.parentId = parent.id;
  connect(project, a, b);
  assert.deepEqual(auditScene(project, project.entryCanvasId).issues, []);
  const c = tile(project, 'Unrelated', 310, 20, 70, 120);
  assert.equal(auditScene(project, project.entryCanvasId).issues.some(item => item.code === 'route-crosses-node' && item.nodeId === c.id), true);
});

test('hidden branches do not paint obstructions, and disclosure preserves source data', () => {
  const project = newProject('disclosure'), parent = tile(project, 'Open me', 0, 0, 300, 250, 'container');
  const child = tile(project, 'Child', 20, 20); child.parentId = parent.id; parent.collapsed = true;
  const serialized = JSON.stringify(project), report = verifyAtlas(project, { nodes: { parent: parent.id, child: child.id }, canvases: { overview: project.entryCanvasId } });
  assert.equal(report.geometry.states.length, 3);
  assert.equal(report.geometry.states[0].visibleNodes, 1);
  assert.equal(report.geometry.states[1].visibleNodes, 2);
  assert.equal(report.geometry.states[2].state, 'all-groups-expanded');
  assert.equal(report.geometry.passed, true);
  assert.equal(JSON.stringify(project), serialized);
  assert.throws(() => verifyAtlas(project, { nodes: { stale: 'missing' } }), /Stale nodes alias/);
});

test('tray frames contain their descendants but their tether cannot cross another note', () => {
  const project = newProject('tray'), source = tile(project, 'Source', 0, 0);
  source.collapsed = false; source.expansionTray = { offset: { x: 250, y: 0 }, size: { width: 120, height: 120 }, layoutVersion: 3 };
  const child = tile(project, 'Original child', 250, 0, 110, 110); child.parentId = source.id;
  const obstacle = tile(project, 'Obstruction', 145, 25, 60, 60);
  const obstructed = auditScene(project, project.entryCanvasId);
  assert.equal(obstructed.visibleTrayFrames, 1);
  assert.equal(obstructed.issues.some(issue => issue.code === 'tray-tether-crosses-node' && issue.nodeId === obstacle.id), true);
  assert.equal(obstructed.issues.some(issue => issue.code === 'tray-frame-overlap' && issue.nodeIds.includes(child.id)), false);
  obstacle.y = 160;
  assert.deepEqual(auditScene(project, project.entryCanvasId).issues, []);
});

test('offscreen Window children do not collide with outside content', () => {
  const project = newProject('Window'), parent = tile(project, 'Window', 0, 0, 300, 120, 'container');
  Object.assign(parent, { collapsed: false, containerVariant: 'window', childArrangement: 'grid', grid: { columns: 1, rows: 2, cellWidth: 290, cellHeight: 110, windowColumns: 1, windowRows: 1, showHeaders: false } });
  const first = tile(project, 'First', 6, 6, 288, 108), second = tile(project, 'Second', 6, 114, 288, 108), outside = tile(project, 'Outside', 6, 140, 288, 108);
  first.parentId = second.parentId = parent.id; first.cell = { column: 1, row: 1 }; second.cell = { column: 1, row: 2 };
  const surface = windowSurface(parent, project); first.y = surface.geometry.top; second.y = surface.geometry.top + surface.height;
  const result = auditScene(project, project.entryCanvasId);
  assert.equal(result.visibleNodes, 3);
  assert.equal(result.issues.some(item => item.nodeIds?.includes(second.id)), false);
  assert.ok(outside.id);
});
