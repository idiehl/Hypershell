import test from 'node:test';
import assert from 'node:assert/strict';
import { addNode, newProject } from '../src/core/model.ts';
import { arrangeChildren, cellAddress, gridCellBounds, gridGeometry, normalizeContainerLayouts, parseCellAddress, reflowContainedChildren, resolveGridCell } from '../src/core/grid.ts';
import { navigationIndex } from '../src/core/navigation.ts';
import { revealChildren } from '../src/core/reveal.ts';
import { resolveCanvasCollisions } from '../src/core/collision.ts';
import { deleteSelectionGraph, moveSelection } from '../src/core/selection.ts';
import { createNodeFromSpec } from '../src/core/creation.ts';
import { updateNodeType } from '../src/core/node-editing.ts';
import { validateProject } from '../src/core/rules.ts';
import { expansionTrayBounds } from '../src/core/expansion-tray.ts';
import type { ContainerVariant, MeridianProject } from '../src/core/types.ts';

const node = (project: MeridianProject, id: string) => project.nodes.find(item => item.id === id)!;
function collection(variant: ContainerVariant, count: number) {
  const project = newProject('Ordered collection'), canvasId = project.entryCanvasId;
  const parent = addNode(project, canvasId, 'container', 'Collection');
  Object.assign(parent, { x: 50, y: 75, width: 300, height: 200, containerVariant: variant, childLayout: 'contained', childArrangement: 'grid', collapsed: false });
  parent.grid = { columns: 6, rows: 5, cellWidth: 140, cellHeight: 90, fillOrder: 'row', windowColumns: 3, windowRows: 2, scrollDirection: 'vertical' };
  const children = Array.from({ length: count }, (_, at) => {
    const child = addNode(project, canvasId, 'text', `Object ${at + 1}`);
    Object.assign(child, { parentId: parent.id, text: `Entry ${at + 1}`, x: 400 + at * 10, y: 600 + at * 5 });
    child.style = { shape: at ? 'diamond' : 'rectangle' };
    return child;
  });
  return { project, parent, children, canvasId };
}

test('ordered coordinates use X:Y and existing alphabet aliases still identify the same cell', () => {
  assert.equal(cellAddress(27, 12), '27:12');
  assert.deepEqual(parseCellAddress('27:12'), { column: 27, row: 12 });
  assert.deepEqual(parseCellAddress('AA12'), { column: 27, row: 12 });
  for (const invalid of ['0:1', '1:0', '513:1', '1:513', '01:1', '1:01', '1.1:2']) assert.equal(parseCellAddress(invalid), undefined);
  const f = collection('spreadsheet', 1); f.children[0]!.cell = { column: 2, row: 3, name: 'portrait' };
  assert.equal(resolveGridCell(f.project, f.parent.id, '2:3')?.id, f.children[0]!.id);
  assert.equal(resolveGridCell(f.project, f.parent.id, 'B3')?.id, f.children[0]!.id);
  assert.equal(resolveGridCell(f.project, f.parent.id, 'portrait')?.id, f.children[0]!.id);
});

test('horizontal and vertical ordered containers pack equal-shaped tiles in a single tight row or column', () => {
  for (const variant of ['horizontal', 'vertical'] as const) {
    const f = collection(variant, 4), before = structuredClone(f.project);
    const arranged = reflowContainedChildren(f.project, f.parent.id), parent = node(arranged, f.parent.id), children = f.children.map(child => node(arranged, child.id));
    assert.equal(parent.grid!.columns, variant === 'horizontal' ? 4 : 1);
    assert.equal(parent.grid!.rows, variant === 'vertical' ? 4 : 1);
    const geometry = gridGeometry(parent, arranged);
    assert.equal(geometry.headerWidth, 0); assert.equal(geometry.headerHeight, 0); assert.equal(geometry.gap, 0);
    assert.equal(children[0]!.x, parent.x + 6); assert.equal(children[0]!.y, geometry.top);
    for (const [at, child] of children.entries()) {
      assert.equal(cellAddress(child.cell!), variant === 'horizontal' ? `${at + 1}:1` : `1:${at + 1}`);
      assert.equal(child.width, geometry.cellWidth); assert.equal(child.height, geometry.cellHeight);
      assert.equal(child.style!.shape, 'rectangle'); assert.equal(child.sizeMode, 'fixed');
      if (at) assert.ok(Math.abs((variant === 'horizontal' ? child.x : child.y) - (variant === 'horizontal' ? children[at - 1]!.x + child.width : children[at - 1]!.y + child.height)) < 1e-9, 'Adjacent tiles share the same edge within floating-point precision.');
    }
    assert.deepEqual(validateProject(arranged), []); assert.deepEqual(f.project, before);
  }
});

test('Grid capacity stays square with row or column fill and preserves named identities', () => {
  for (const [count, axis] of [[9, 3], [10, 4]] as const) for (const fillOrder of ['row', 'column'] as const) {
    const f = collection('grid', count); f.parent.grid!.fillOrder = fillOrder; f.children[0]!.cell = { column: 5, row: 4, name: 'first_object' };
    const arranged = reflowContainedChildren(f.project, f.parent.id), parent = node(arranged, f.parent.id);
    assert.equal(parent.grid!.columns, axis); assert.equal(parent.grid!.rows, axis);
    for (const [at, child] of f.children.entries()) {
      const coordinate = node(arranged, child.id).cell!;
      assert.equal(coordinate.column, fillOrder === 'row' ? at % axis + 1 : Math.floor(at / axis) + 1);
      assert.equal(coordinate.row, fillOrder === 'column' ? at % axis + 1 : Math.floor(at / axis) + 1);
    }
    assert.equal(resolveGridCell(arranged, parent.id, 'first_object')?.id, f.children[0]!.id);
    const geometry = gridGeometry(parent, arranged); assert.equal(geometry.width, f.parent.width);
    assert.equal(navigationIndex(arranged).bounds(parent).width, Math.max(geometry.width, navigationIndex(arranged).bounds(parent).width));
    assert.deepEqual(validateProject(arranged), []);
  }
});

test('bounded Spreadsheet and Window retain explicit capacity and names while filling their authored viewport', () => {
  for (const variant of ['spreadsheet', 'window'] as const) {
    const f = collection(variant, 3); f.children[0]!.cell = { column: 4, row: 3, name: 'source' }; f.parent.style = { childPadding: 11 };
    const arranged = reflowContainedChildren(f.project, f.parent.id), parent = node(arranged, f.parent.id), geometry = gridGeometry(parent, arranged);
    assert.equal(parent.grid!.columns, 6); assert.equal(parent.grid!.rows, 5);
    assert.deepEqual(node(arranged, f.children[0]!.id).cell, { column: 4, row: 3, name: 'source' });
    for (const child of f.children) { const fixed = node(arranged, child.id); assert.equal(fixed.width, geometry.cellWidth); assert.equal(fixed.height, geometry.cellHeight); assert.deepEqual({ x: fixed.x, y: fixed.y }, { x: gridCellBounds(parent, fixed.cell!, arranged).x, y: gridCellBounds(parent, fixed.cell!, arranged).y }); }
    assert.equal(geometry.visibleColumns, variant === 'window' ? 3 : 6); assert.equal(geometry.visibleRows, variant === 'window' ? 2 : 5);
    assert.equal(geometry.width, f.parent.width); assert.equal(geometry.height, f.parent.height);
    assert.equal(geometry.visibleColumns * geometry.cellWidth, parent.width - 24);
    assert.equal(geometry.top - parent.y + 12 + geometry.visibleRows * geometry.cellHeight, parent.height);
    assert.deepEqual(validateProject(arranged), []);
  }
});

test('ordered reflow carries hidden descendants and internal routes with the uniform child tile', () => {
  const f = collection('horizontal', 2), child = f.children[0]!; child.collapsed = true;
  const hidden = addNode(f.project, f.canvasId, 'text', 'Hidden'); Object.assign(hidden, { parentId: child.id, x: 1400, y: 700 });
  const id = crypto.randomUUID(); f.project.links.push({ id, canvasId: f.canvasId, sourceId: child.id, targetId: hidden.id, kind: 'visual', relation: 'generic', points: [{ x: 800, y: 500 }] });
  const arranged = reflowContainedChildren(f.project, f.parent.id), moved = node(arranged, child.id), dx = moved.x - child.x, dy = moved.y - child.y;
  assert.equal(node(arranged, hidden.id).x, hidden.x + dx); assert.equal(node(arranged, hidden.id).y, hidden.y + dy);
  assert.deepEqual(arranged.links.find(link => link.id === id)!.points, [{ x: 800 + dx, y: 500 + dy }]);
  assert.equal(moved.collapsed, true);
});

test('Enclosed preserves free composition with room while legacy Spatial formal containers become Enclosed', () => {
  const f = collection('spatial', 2); f.parent.childLayout = 'spatial'; f.parent.childArrangement = 'free'; f.parent.style = { childPadding: 5 };
  Object.assign(f.children[0]!, { x: 70, y: 115, width: 80, height: 60, sizeMode: 'fixed' });
  Object.assign(f.children[1]!, { x: 200, y: 175, width: 100, height: 60, sizeMode: 'fixed' });
  const revealed = revealChildren(f.project, f.parent.id, true), parent = node(revealed, f.parent.id), index = navigationIndex(revealed);
  assert.equal(parent.containerVariant, 'contained'); assert.equal(parent.childLayout, 'contained');
  for (const child of f.children) { assert.equal(node(revealed, child.id).x, child.x); assert.equal(node(revealed, child.id).y, child.y); }
  assert.deepEqual([index.bounds(parent).width, index.bounds(parent).height], [f.parent.width, f.parent.height]);
  const collapsed = revealChildren(revealed, parent.id, false); assert.ok(navigationIndex(collapsed).bounds(node(collapsed, parent.id)).width < index.bounds(parent).width);
});

test('Window logical offscreen children do not collide with or displace neighboring canvas nodes', () => {
  const f = collection('window', 20), arranged = reflowContainedChildren(f.project, f.parent.id), parent = node(arranged, f.parent.id), bounds = navigationIndex(arranged).bounds(parent);
  const peer = addNode(arranged, f.canvasId, 'text', 'Peer'); Object.assign(peer, { x: bounds.x + bounds.width + 20, y: bounds.y + 50, width: 100, height: 80, sizeMode: 'fixed' });
  const repaired = resolveCanvasCollisions(arranged, f.canvasId);
  assert.equal(node(repaired, peer.id).x, peer.x); assert.equal(node(repaired, peer.id).y, peer.y);
  assert.deepEqual(node(repaired, parent.id).grid, parent.grid);
  assert.ok(f.children.some(child => node(repaired, child.id).x + 140 > bounds.x + bounds.width));
});

test('deletion compacts automatic Grid capacity and dragging an ordered child preserves its cell position', () => {
  const f = collection('grid', 10), arranged = reflowContainedChildren(f.project, f.parent.id), first = node(arranged, f.children[0]!.id);
  const moved = moveSelection(arranged, [first.id], 150, 80);
  assert.equal(node(moved, first.id).x, first.x); assert.equal(node(moved, first.id).y, first.y);
  const deleted = deleteSelectionGraph(arranged, [f.children[9]!.id]);
  assert.equal(node(deleted, f.parent.id).grid!.columns, 3); assert.equal(node(deleted, f.parent.id).grid!.rows, 3);
});

test('creating into Grid grows square capacity, creating into a full explicit sheet fails atomically', () => {
  const f = collection('grid', 9), arranged = reflowContainedChildren(f.project, f.parent.id);
  const result = createNodeFromSpec(arranged, { canvasId: f.canvasId, parentId: f.parent.id, type: 'text', title: 'Tenth', x: 0, y: 0, width: 200, height: 120, text: 'Ten' });
  assert.equal(node(result.project, f.parent.id).grid!.columns, 4); assert.equal(node(result.project, f.parent.id).grid!.rows, 4);
  const full = collection('spreadsheet', 1); full.parent.grid = { columns: 1, rows: 1, cellWidth: 80, cellHeight: 70 }; full.children[0]!.cell = { column: 1, row: 1 };
  const before = structuredClone(full.project);
  assert.throws(() => createNodeFromSpec(full.project, { canvasId: full.canvasId, parentId: full.parent.id, type: 'text', title: 'Extra', x: 0, y: 0, width: 200, height: 120 }), /full/);
  assert.deepEqual(full.project, before);
});

test('container subtype changes retain GUIDs and names while adopting the new ordered coordinate system', () => {
  const f = collection('grid', 4); f.children[1]!.cell = { column: 2, row: 1, name: 'named' };
  const converted = updateNodeType(f.project, f.parent.id, { kind: 'container', containerVariant: 'vertical' });
  assert.deepEqual(converted.nodes.map(item => item.id), f.project.nodes.map(item => item.id));
  assert.equal(cellAddress(node(converted, f.children[1]!.id).cell!), '1:2');
  assert.equal(resolveGridCell(converted, f.parent.id, 'named')?.id, f.children[1]!.id);
  assert.deepEqual(validateProject(converted), []);
});

test('display migration is independent and idempotent, keeping snapshots and collapsed branch identities', () => {
  const f = collection('spatial', 2); f.parent.childLayout = 'spatial'; f.parent.collapsed = true;
  const first = f.children[0]!; first.kind = 'container'; first.containerVariant = 'horizontal'; first.childLayout = 'contained'; first.collapsed = true;
  for (let at = 0; at < 2; at++) { const descendant = addNode(f.project, f.canvasId, 'text', `Nested ${at}`); descendant.parentId = first.id; }
  f.project.snapshots[first.id] = { node: structuredClone(first), value: null, savedAt: '2026-10-08T12:00:00Z' };
  const before = structuredClone(f.project), normalized = normalizeContainerLayouts(f.project);
  assert.deepEqual(f.project, before); assert.deepEqual(normalized.snapshots, before.snapshots);
  assert.deepEqual(normalized.nodes.map(item => item.id), before.nodes.map(item => item.id));
  assert.equal(node(normalized, f.parent.id).collapsed, true); assert.equal(node(normalized, first.id).collapsed, true);
  assert.equal(node(normalized, f.parent.id).containerVariant, 'contained');
  assert.equal(node(normalized, first.id).grid, undefined, 'A closed workspace retains hidden geometry until disclosure.');
  const revealed = revealChildren(normalized, first.id, true);
  assert.equal(node(revealed, first.id).grid!.columns, 2); assert.equal(node(revealed, first.id).grid!.rows, 1);
  assert.deepEqual(normalizeContainerLayouts(normalized), normalized);
});

test('bounded circle and square children remain proportional inside full shared cells without changing pixel request metadata', () => {
  for (const shape of ['circle', 'square'] as const) {
    const f = collection('window', 2); f.children[0]!.style = { shape }; const before = structuredClone(f.project);
    const arranged = reflowContainedChildren(f.project, f.parent.id);
    const geometry = gridGeometry(node(arranged, f.parent.id), arranged);
    for (const child of f.children) { assert.equal(node(arranged, child.id).width, geometry.cellWidth); assert.equal(node(arranged, child.id).height, geometry.cellHeight); assert.equal(node(arranged, child.id).style!.shape, shape); }
    assert.deepEqual(f.project, before); assert.equal(node(arranged, f.parent.id).grid!.cellWidth, 140); assert.equal(node(arranged, f.parent.id).grid!.cellHeight, 90);
  }
});

test('revealed exterior branches receive trays outside their ordered owner without adding identities', () => {
  const f = collection('grid', 2), child = f.children[0]!; child.childLayout = 'spatial'; child.collapsed = false;
  const detail = addNode(f.project, f.canvasId, 'text', 'Outside composition'); Object.assign(detail, { parentId: child.id, x: child.x - 50, y: child.y + 120 });
  const before = structuredClone(f.project);
  const arranged = reflowContainedChildren(f.project, f.parent.id), frame = expansionTrayBounds(arranged, child.id)!, owner = navigationIndex(arranged).bounds(node(arranged, f.parent.id));
  assert.ok(frame); assert.ok(frame.x >= owner.x + owner.width || frame.y >= owner.y + owner.height || frame.x + frame.width <= owner.x || frame.y + frame.height <= owner.y);
  assert.equal(node(arranged, f.parent.id).containerVariant, 'grid'); assert.equal(node(arranged, detail.id).parentId, child.id);
  assert.deepEqual(arranged.nodes.map(item => item.id), before.nodes.map(item => item.id)); assert.deepEqual(validateProject(arranged), []);
  assert.deepEqual(f.project, before);
});

test('opening a legacy exterior composition preserves its ordered pattern and observations while creating a stable tray', () => {
  const f = collection('grid', 2), child = f.children[0]!; child.childLayout = 'spatial'; child.collapsed = false;
  const detail = addNode(f.project, f.canvasId, 'text', 'Outside composition'); Object.assign(detail, { parentId: child.id, x: child.x - 50, y: child.y + 120 });
  f.project.snapshots[detail.id] = { node: structuredClone(detail), value: null, savedAt: '2026-10-08T12:00:00Z' };
  const before = structuredClone(f.project), normalized = normalizeContainerLayouts(f.project);
  assert.deepEqual(f.project, before); assert.equal(node(normalized, f.parent.id).containerVariant, 'grid');
  assert.deepEqual(normalized.nodes.map(item => item.id), before.nodes.map(item => item.id)); assert.deepEqual(normalized.snapshots, before.snapshots);
  assert.equal(node(normalized, detail.id).parentId, child.id); assert.ok(node(normalized, child.id).expansionTray);
  const frame = expansionTrayBounds(normalized, child.id)!, owner = navigationIndex(normalized).bounds(node(normalized, f.parent.id));
  assert.ok(frame.x >= owner.x + owner.width || frame.y >= owner.y + owner.height || frame.x + frame.width <= owner.x || frame.y + frame.height <= owner.y);
  assert.ok(node(normalized, detail.id).width > 1 && node(normalized, detail.id).height > 1, 'Original details remain usable instead of being crushed to a cell wall.');
  assert.deepEqual(normalizeContainerLayouts(normalized), normalized);
});

test('empty legacy Enclosed titles do not progressively enlarge authored dimensions during repeated document loads', () => {
  const f = collection('spatial', 0); f.parent.childLayout = 'spatial'; f.parent.collapsed = true; f.parent.width = 280;
  f.parent.title = 'A detailed account of the interconnected divine succession'; f.parent.style = { titleSize: 23, fontSize: 17 };
  const normalized = normalizeContainerLayouts(f.project);
  assert.equal(node(normalized, f.parent.id).width, 280); assert.equal(node(normalized, f.parent.id).height, f.parent.height);
  assert.deepEqual(normalizeContainerLayouts(normalized), normalized);
});
