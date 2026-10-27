import test from 'node:test';
import assert from 'node:assert/strict';
import { addNode, newProject } from '../src/core/model.ts';
import { updateNodeType } from '../src/core/node-editing.ts';
import { arrangeChildren, gridCellBounds, gridGeometry, reflowContainedChildren } from '../src/core/grid.ts';
import { revealChildren } from '../src/core/reveal.ts';
import { navigationIndex } from '../src/core/navigation.ts';
import { boundedChildInterior } from '../src/core/sizing.ts';
import { validateProject } from '../src/core/rules.ts';
import { expansionTrayBounds } from '../src/core/expansion-tray.ts';
import type { MeridianNode, MeridianProject } from '../src/core/types.ts';

const nodeIn = (project: MeridianProject, id: string) => project.nodes.find(node => node.id === id)!;
const close = (actual: number, expected: number) => assert.ok(Math.abs(actual - expected) < 1e-7);
function withoutModeRecords(project: MeridianProject) { const copy = structuredClone(project); for (const node of copy.nodes) delete node.containerSizes; return copy; }
const overlap = (a: { x: number; y: number; width: number; height: number }, b: { x: number; y: number; width: number; height: number }) =>
  a.x < b.x + b.width && a.x + a.width > b.x && a.y < b.y + b.height && a.y + a.height > b.y;
function leaf(project: MeridianProject, parent: MeridianNode, title: string, x: number, y: number) {
  const node = addNode(project, parent.canvasId, 'text', title);
  Object.assign(node, { parentId: parent.id, x, y, width: 180, height: 90, sizeMode: 'fixed', text: title }); return node;
}
function fixture() {
  const project = newProject('Container sizing transitions'), parent = addNode(project, project.entryCanvasId, 'container', 'Collection');
  Object.assign(parent, { x: 400, y: 250, width: 1200, height: 1000, containerVariant: 'spatial', childLayout: 'spatial', childArrangement: 'free', collapsed: false });
  return { project, parent };
}

test('legacy Freeform repair fits source covers and presents their exterior branches without losing identity or routes', () => {
  const { project, parent } = fixture(), left = leaf(project, parent, 'Left branch', -600, 120), right = leaf(project, parent, 'Right branch', 2400, 1500);
  Object.assign(left, { childLayout: 'spatial', collapsed: false, cell: { column: 1, row: 1, name: 'first' } });
  right.cell = { column: 2, row: 1, name: 'second' };
  parent.grid = { columns: 3, rows: 3, cellWidth: 200, cellHeight: 120 };
  const visible = leaf(project, left, 'Visible child', -650, 235), hidden = leaf(project, right, 'Hidden child', 2600, 1800); right.collapsed = true;
  const link = { id: 'branch-route', canvasId: parent.canvasId, sourceId: left.id, targetId: visible.id, kind: 'visual' as const, relation: 'causal' as const, points: [{ x: -615, y: 190 }] };
  project.links.push(link);
  const before = structuredClone(project), next = updateNodeType(project, parent.id, { kind: 'container', containerVariant: 'contained' });
  assert.deepEqual(project, before); assert.deepEqual(next.nodes.map(node => node.id), before.nodes.map(node => node.id));
  const frame = nodeIn(next, parent.id), index = navigationIndex(next), bounds = index.bounds(frame);
  assert.deepEqual([frame.x, frame.y], [parent.x, parent.y]); assert.ok(frame.width >= parent.width); assert.ok(frame.height >= parent.height);
  assert.equal(nodeIn(next, right.id).collapsed, true);
  for (const child of [left, right]) assert.deepEqual(nodeIn(next, child.id).cell, child.cell);
  for (const child of [left, right]) {
    const box = index.bounds(nodeIn(next, child.id));
    const inner = boundedChildInterior(next, frame); assert.ok(box.x >= inner.x - .001 && box.y >= inner.y - .001);
    assert.ok(box.x + box.width <= bounds.x + bounds.width && box.y + box.height <= bounds.y + bounds.height);
  }
  const shifted = nodeIn(next, left.id), scale = shifted.width / left.width;
  const tray = expansionTrayBounds(next, left.id)!;
  assert.ok(tray.x >= bounds.x + bounds.width || tray.y >= bounds.y + bounds.height || tray.x + tray.width <= bounds.x || tray.y + tray.height <= bounds.y);
  assert.equal(nodeIn(next, visible.id).parentId, left.id);
  assert.deepEqual([nodeIn(next, visible.id).width, nodeIn(next, visible.id).height], [visible.width, visible.height]);
  close(next.links[0].points![0].x, shifted.x + (link.points[0].x - left.x) * scale); close(next.links[0].points![0].y, shifted.y + (link.points[0].y - left.y) * scale);
  const shiftedRight = nodeIn(next, right.id), hiddenScale = shiftedRight.width / right.width;
  close(nodeIn(next, hidden.id).x, shiftedRight.x + (hidden.x - right.x) * hiddenScale); close(nodeIn(next, hidden.id).y, shiftedRight.y + (hidden.y - right.y) * hiddenScale);
  assert.deepEqual(validateProject(next), []);
});

test('Grid preserves named identities with square capacity and uniform tiles containing expanded Enclosed child compositions', () => {
  const { project, parent } = fixture(), first = leaf(project, parent, 'Expanded cell', 430, 320), second = leaf(project, parent, 'Other cell', 640, 320);
  Object.assign(first, { kind: 'container', containerVariant: 'contained', cell: { column: 1, row: 1, name: 'account' }, childLayout: 'contained', childArrangement: 'free', collapsed: false });
  second.cell = { column: 2, row: 1, name: 'comparison' };
  const detail = leaf(project, first, 'Expanded detail', 380, 430);
  parent.grid = { columns: 5, rows: 8, cellWidth: 360, cellHeight: 280, gap: 12, showHeaders: true };
  Object.assign(project, reflowContainedChildren(project, first.id));
  const next = updateNodeType(project, parent.id, { kind: 'container', containerVariant: 'grid' });
  const frame = nodeIn(next, parent.id), index = navigationIndex(next), grid = frame.grid!;
  assert.deepEqual([grid.columns, grid.rows], [2, 2]); assert.equal(grid.cellWidth, 360); assert.equal(grid.cellHeight, 280);
  assert.deepEqual(nodeIn(next, first.id).cell, first.cell); assert.deepEqual(nodeIn(next, second.id).cell, second.cell);
  const cell = gridCellBounds(frame, first.cell!, next), firstBox = index.bounds(nodeIn(next, first.id)), detailBox = index.bounds(nodeIn(next, detail.id));
  assert.ok(detailBox.x >= cell.x + 6); assert.equal(firstBox.x, cell.x); assert.equal(firstBox.y, cell.y);
  assert.equal(overlap(detailBox, index.bounds(nodeIn(next, second.id))), false);
  const interior = boundedChildInterior(next, nodeIn(next, first.id));
  assert.ok(detailBox.x >= interior.x - 1e-7 && detailBox.y >= interior.y - 1e-7 &&
    detailBox.x + detailBox.width <= interior.x + interior.width + 1e-7 && detailBox.y + detailBox.height <= interior.y + interior.height + 1e-7,
  'After allocating a larger cell, the child composition refits within its restored title and padding reservations.');
  assert.deepEqual(nodeIn(next, first.id).style, { ...first.style, shape: frame.style?.shape ?? 'rectangle' });
  assert.equal(frame.width, gridGeometry(frame, next).width); assert.equal(frame.height, gridGeometry(frame, next).height);
  assert.deepEqual(validateProject(next), []);
});

test('Spreadsheet retains configured empty cells and fixed enclosing nodes retain their authored extent', () => {
  const { project, parent } = fixture(), child = leaf(project, parent, 'An entry', 430, 320);
  child.cell = { column: 2, row: 1 }; parent.grid = { columns: 5, rows: 8, cellWidth: 240, cellHeight: 150, gap: 4, showHeaders: true };
  const sheet = updateNodeType(project, parent.id, { kind: 'container', containerVariant: 'spreadsheet' });
  assert.deepEqual([nodeIn(sheet, parent.id).grid!.columns, nodeIn(sheet, parent.id).grid!.rows], [5, 8]);
  parent.sizeMode = 'fixed';
  const fixed = updateNodeType(project, parent.id, { kind: 'container', containerVariant: 'contained' });
  assert.deepEqual([nodeIn(fixed, parent.id).width, nodeIn(fixed, parent.id).height], [1200, 1000]);
});

test('Enclosed disclosure repairs outside children once, preserves independent states, and fits around its own description', () => {
  const { project, parent } = fixture();
  Object.assign(parent, { containerVariant: 'contained', childLayout: 'contained', collapsed: true, text: 'A description that reserves its own readable space above the child collection.' });
  const child = leaf(project, parent, 'A child', -900, -300); child.collapsed = true;
  const nested = leaf(project, child, 'Hidden material', -700, -150), before = structuredClone(project);
  const revealed = revealChildren(project, parent.id, true), frame = nodeIn(revealed, parent.id), index = navigationIndex(revealed);
  assert.deepEqual(project, before); assert.equal(nodeIn(revealed, child.id).collapsed, true); assert.equal(index.isNodeVisible(nested.id), false);
  assert.ok(nodeIn(revealed, child.id).y >= boundedChildInterior(revealed, frame).y - .001);
  assert.deepEqual(reflowContainedChildren(revealed, parent.id), revealed);
  const collapsed = revealChildren(revealed, parent.id, false);
  assert.deepEqual(collapsed.nodes.map(node => [node.x, node.y]), revealed.nodes.map(node => [node.x, node.y]));
  assert.deepEqual(withoutModeRecords(revealChildren(collapsed, parent.id, true)), withoutModeRecords(revealed));
});

test('A collapsed outer grid still measures a locally expanded bounded child branch consistently', () => {
  const { project, parent } = fixture(), child = leaf(project, parent, 'An expanded entry', 500, 450);
  Object.assign(parent, { containerVariant: 'grid', childLayout: 'contained', childArrangement: 'grid', collapsed: true });
  Object.assign(child, { kind: 'container', containerVariant: 'contained', cell: { column: 1, row: 1 }, childLayout: 'contained', collapsed: false });
  leaf(project, child, 'Visible after reveal', 740, 600);
  const enclosed = reflowContainedChildren(project, child.id);
  const hidden = arrangeChildren(enclosed, parent.id, 'grid').project, open = structuredClone(hidden); nodeIn(open, parent.id).collapsed = false;
  const shown = arrangeChildren(open, parent.id, 'grid').project;
  assert.deepEqual([nodeIn(hidden, parent.id).width, nodeIn(hidden, parent.id).height], [parent.width, parent.height]);
  assert.deepEqual(nodeIn(shown, child.id).cell, { column: 1, row: 1 });
  assert.deepEqual(reflowContainedChildren(shown, parent.id), shown);
});
