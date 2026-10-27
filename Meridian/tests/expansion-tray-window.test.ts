import test from 'node:test';
import assert from 'node:assert/strict';
import { addNode, newProject } from '../src/core/model.ts';
import { navigationIndex } from '../src/core/navigation.ts';
import { gridCellBounds } from '../src/core/cells.ts';
import { displayNodeBounds, prepareWindowOffsets, windowParents } from '../src/window-scene.ts';

test('tray descendants escape an ancestor Window transform and Target scrolls their source into view', () => {
  const project = newProject('Window outlet'), owner = addNode(project, project.entryCanvasId, 'container', 'Window');
  Object.assign(owner, { x: 80, y: 90, width: 400, height: 260, sizeMode: 'fixed', containerVariant: 'window', collapsed: false,
    grid: { columns: 1, rows: 4, windowColumns: 1, windowRows: 1, cellWidth: 360, cellHeight: 220 } });
  const source = addNode(project, project.entryCanvasId, 'media', 'Source');
  const cell = { column: 1, row: 3 }, bounds = gridCellBounds(owner, cell, project);
  Object.assign(source, { parentId: owner.id, cell, ...bounds, sizeMode: 'fixed', collapsed: false, expansionTray: { offset: { x: 600, y: -200 } } });
  const detail = addNode(project, project.entryCanvasId, 'media', 'Detail');
  Object.assign(detail, { parentId: source.id, x: 900, y: 200, width: 180, height: 100, sizeMode: 'fixed' });
  const index = navigationIndex(project), offsets = new Map([[owner.id, { x: 0, y: 0 }]]);
  assert.deepEqual(windowParents(index, source.id).map(node => node.id), [owner.id]);
  assert.deepEqual(windowParents(index, detail.id), []);
  prepareWindowOffsets(project, index, offsets, detail.id);
  assert.equal(offsets.get(owner.id)!.y, bounds.height * 2);
  assert.equal(displayNodeBounds(index, source, offsets).y, bounds.y - bounds.height * 2);
  assert.equal(displayNodeBounds(index, detail, offsets).y, detail.y);
});

test('an exterior Window tray retains its own scroll scope while ignoring surrounding Windows', () => {
  const project = newProject('Nested scrolling outlet'), outer = addNode(project, project.entryCanvasId, 'container', 'Outer');
  Object.assign(outer, { x: 0, y: 0, width: 500, height: 300, sizeMode: 'fixed', containerVariant: 'window', collapsed: false,
    grid: { columns: 1, rows: 4, windowColumns: 1, windowRows: 1, cellWidth: 400, cellHeight: 250 } });
  const source = addNode(project, project.entryCanvasId, 'media', 'Tray Window');
  Object.assign(source, { parentId: outer.id, ...gridCellBounds(outer, { column: 1, row: 2 }, project), cell: { column: 1, row: 2 }, sizeMode: 'fixed', collapsed: false,
    containerVariant: 'window', expansionTray: { offset: { x: 700, y: 100 } }, grid: { columns: 1, rows: 3, windowColumns: 1, windowRows: 1, cellWidth: 160, cellHeight: 90 } });
  const detail = addNode(project, project.entryCanvasId, 'media', 'Far cell');
  Object.assign(detail, { parentId: source.id, ...gridCellBounds(source, { column: 1, row: 3 }, project), cell: { column: 1, row: 3 }, sizeMode: 'fixed' });
  const index = navigationIndex(project), offsets = new Map<string, { x: number; y: number }>();
  assert.deepEqual(windowParents(index, detail.id).map(node => node.id), [source.id]);
  prepareWindowOffsets(project, index, offsets, detail.id);
  assert.equal(offsets.get(source.id)!.y, 180);
  assert.equal(offsets.get(outer.id)!.y, source.height);
  assert.equal(displayNodeBounds(index, detail, offsets).y, detail.y - 180);
});
