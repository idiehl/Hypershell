import test from 'node:test';
import assert from 'node:assert/strict';
import { addNode, newProject } from '../src/core/model.ts';
import { gridGeometry, gridCellBounds, reflowContainedChildren, normalizeContainerLayouts } from '../src/core/grid.ts';
import { boundedChildInterior, constrainChildGeometry, nodeContentBounds, sizingBounds } from '../src/core/sizing.ts';
import { moveSelection } from '../src/core/selection.ts';
import { revealChildren } from '../src/core/reveal.ts';
import { createNodeFromSpec } from '../src/core/creation.ts';
import { nodeShape, shapeBounds, shapeVertices } from '../src/core/geometry.ts';
import type { ContainerVariant, MeridianProject } from '../src/core/types.ts';

const get = (project: MeridianProject, id: string) => project.nodes.find(node => node.id === id)!;
const close = (actual: number, expected: number, message = '') => assert.ok(Math.abs(actual - expected) < 1e-7, `${message}: ${actual} ≈ ${expected}`);
function pointInShape(point: { x: number; y: number }, shape: ReturnType<typeof nodeShape>) {
  const bounds = shapeBounds(shape);
  if (shape.shape === 'circle' || shape.shape === 'ellipse') return ((point.x - bounds.x - bounds.width / 2) / (bounds.width / 2)) ** 2 + ((point.y - bounds.y - bounds.height / 2) / (bounds.height / 2)) ** 2 <= 1 + 1e-7;
  const vertices = shapeVertices(shape);
  return vertices.every((a, at) => { const b = vertices[(at + 1) % vertices.length]; return (b.x - a.x) * (point.y - a.y) - (b.y - a.y) * (point.x - a.x) >= -1e-6; });
}
function fixture(variant: ContainerVariant = 'horizontal', count = 6, sizeMode: 'auto' | 'fixed' = 'fixed') {
  const project = newProject('Parent frame precedence'), parent = addNode(project, project.entryCanvasId, 'container', '');
  Object.assign(parent, { x: 100.25, y: 80.75, width: 600.5, height: 420.25, sizeMode, collapsed: false, containerVariant: variant, childArrangement: variant === 'contained' ? 'free' : 'grid' });
  parent.grid = { columns: variant === 'window' ? 4 : 3, rows: variant === 'window' ? 8 : 3, cellWidth: 900, cellHeight: 800, windowColumns: 2, windowRows: 3, gap: 0, showHeaders: false };
  const children = Array.from({ length: count }, (_, at) => {
    const node = addNode(project, parent.canvasId, 'media', `Child ${at}`);
    Object.assign(node, { parentId: parent.id, x: parent.x + 6, y: parent.y + 6, width: 900, height: 800, sizeMode: 'fixed', childPosition: 'manual', text: 'A body remains independently readable when zoomed.' });
    node.style = { shape: 'rectangle' }; return node;
  });
  return { project, parent, children };
}

test('empty bounded containers do not grow by their own blank intrinsic frame plus repeated padding', () => {
  for (const sizeMode of ['auto', 'fixed'] as const) for (const variant of ['contained', 'horizontal', 'vertical', 'grid', 'spreadsheet', 'window', 'gallery'] as const) {
    const f = fixture(variant, 0, sizeMode); let current = reflowContainedChildren(f.project, f.parent.id);
    const baseline = structuredClone(current);
    for (let repeat = 0; repeat < 30; repeat++) {
      current = reflowContainedChildren(current, f.parent.id);
      assert.deepEqual([get(current, f.parent.id).width, get(current, f.parent.id).height], [600.5, 420.25]);
      assert.deepEqual(sizingBounds(current, get(current, f.parent.id)), { id: f.parent.id, x: 100.25, y: 80.75, width: 600.5, height: 420.25 });
      assert.deepEqual(current, baseline);
    }
  }
});

test('every bounded ordered pattern fills its complete usable frame on both axes regardless of requested child pixels', () => {
  for (const sizeMode of ['auto', 'fixed'] as const) for (const variant of ['horizontal', 'vertical', 'grid', 'spreadsheet', 'window'] as const) {
    const f = fixture(variant, variant === 'grid' ? 10 : 6, sizeMode), before = structuredClone(f.project);
    const result = reflowContainedChildren(f.project, f.parent.id), parent = get(result, f.parent.id), inner = boundedChildInterior(result, parent), grid = gridGeometry(parent, result);
    assert.deepEqual(f.project, before); assert.deepEqual([parent.width, parent.height], [600.5, 420.25]);
    close(inner.x, parent.x + 6); close(inner.y, parent.y + 6); close(inner.width, 588.5); close(inner.height, 408.25);
    close(grid.cellWidth * grid.visibleColumns, inner.width); close(grid.cellHeight * grid.visibleRows, inner.height);
    for (const source of f.children) {
      const child = get(result, source.id), cell = gridCellBounds(parent, child.cell!, result);
      close(child.x, cell.x); close(child.y, cell.y); close(child.width, cell.width); close(child.height, cell.height);
      assert.equal(child.text, source.text); assert.equal(child.style?.shape, 'rectangle');
    }
    assert.deepEqual(reflowContainedChildren(result, parent.id), result);
    assert.deepEqual(normalizeContainerLayouts(result), result);
  }
});

test('parent resizing stretches or shrinks rendered cells below the authoring grid minimum without growing the parent', () => {
  const f = fixture('grid', 9), first = reflowContainedChildren(f.project, f.parent.id);
  for (const [width, height] of [[1200.125, 700.875], [17.25, 25.75], [300.5, 180.25]]) {
    const candidate = structuredClone(first), parent = get(candidate, f.parent.id); parent.width = width; parent.height = height;
    const result = reflowContainedChildren(candidate, parent.id), current = get(result, parent.id), inner = boundedChildInterior(result, current), grid = gridGeometry(current, result);
    close(current.width, width); close(current.height, height); close(grid.cellWidth * 3, inner.width); close(grid.cellHeight * 3, inner.height);
    assert.ok(current.grid!.cellWidth >= 36 && current.grid!.cellHeight >= 36, 'Portable grid metadata retains valid pixel requests.');
    assert.deepEqual(reflowContainedChildren(result, parent.id), result);
  }
});

test('headers, explicit padding and nonrectangular silhouettes reserve a safe child surface instead of changing the frame', () => {
  for (const shape of ['rectangle', 'circle', 'diamond', 'triangle', 'hexagon'] as const) {
    const f = fixture('horizontal', 3); f.parent.title = 'A header'; f.parent.style = { shape, borderWidth: 3, padding: 5, childPadding: 11 };
    const result = reflowContainedChildren(f.project, f.parent.id), parent = get(result, f.parent.id), inner = boundedChildInterior(result, parent), silhouette = nodeShape(parent, sizingBounds(result, parent));
    const grid = gridGeometry(parent, result); close(grid.cellWidth * 3, inner.width); close(grid.cellHeight, inner.height);
    for (const source of f.children) {
      const child = get(result, source.id);
      for (const [x, y] of [[child.x, child.y], [child.x + child.width, child.y], [child.x, child.y + child.height], [child.x + child.width, child.y + child.height]])
        assert.ok(pointInShape({ x, y }, silhouette), `${shape} child remains inside actual backdrop.`);
    }
    assert.deepEqual([parent.width, parent.height], [600.5, 420.25]);
    assert.deepEqual(reflowContainedChildren(result, parent.id), result);
  }
});

test('aspect-locked child shapes remain inscribed in shared full-size cells without stretching the silhouette', () => {
  const f = fixture('horizontal', 3); for (const child of f.children) child.style = { shape: 'circle' };
  const result = reflowContainedChildren(f.project, f.parent.id), parent = get(result, f.parent.id), grid = gridGeometry(parent, result), inner = boundedChildInterior(result, parent);
  close(grid.cellWidth * 3, inner.width); close(grid.cellHeight, inner.height);
  for (const source of f.children) {
    const child = get(result, source.id), physical = shapeBounds(nodeShape(child, sizingBounds(result, child)));
    close(physical.width, physical.height); close(child.width, grid.cellWidth); close(child.height, grid.cellHeight);
    close(physical.x + physical.width / 2, child.x + child.width / 2); close(physical.y + physical.height / 2, child.y + child.height / 2);
  }
});

test('repeated pure translations preserve dimensions and valid manual Freeform child compositions exactly', () => {
  const f = fixture('contained', 2, 'auto');
  for (const [at, child] of f.children.entries()) Object.assign(child, { x: f.parent.x + 30 + at * 180, y: f.parent.y + 40, width: 120, height: 80 });
  let current = reflowContainedChildren(f.project, f.parent.id);
  const initial = structuredClone(current);
  for (let repeat = 0; repeat < 30; repeat++) {
    current = moveSelection(current, [f.parent.id], 1.25, -2.75); current = reflowContainedChildren(current, f.parent.id);
    for (const original of initial.nodes.filter(node => node.kind !== 'canvas')) {
      const changed = get(current, original.id); close(changed.width, original.width); close(changed.height, original.height);
      close(changed.x, original.x + (repeat + 1) * 1.25); close(changed.y, original.y - (repeat + 1) * 2.75);
    }
    assert.deepEqual(reflowContainedChildren(current, f.parent.id), current);
  }
});

test('new automatic Freeform children fit around existing valid manual placements instead of rescaling them', () => {
  const f = fixture('contained', 3, 'auto'), manual = f.children[0];
  Object.assign(manual, { x: f.parent.x + 30, y: f.parent.y + 40, width: 120, height: 80, childPosition: 'manual' });
  for (const child of f.children.slice(1)) child.childPosition = 'auto';
  const result = reflowContainedChildren(f.project, f.parent.id), parent = get(result, f.parent.id), inner = boundedChildInterior(result, parent);
  const anchored = get(result, manual.id);
  assert.deepEqual([anchored.x, anchored.y, anchored.width, anchored.height], [manual.x, manual.y, manual.width, manual.height]);
  for (const source of f.children) {
    const child = get(result, source.id);
    assert.ok(child.x >= inner.x - .001 && child.y >= inner.y - .001 && child.x + child.width <= inner.x + inner.width + .001 && child.y + child.height <= inner.y + inner.height + .001);
  }
  assert.deepEqual(reflowContainedChildren(result, parent.id), result);
});

test('adding and expanding nested children fits the current parent cell without growing bounded ancestors', () => {
  const f = fixture('horizontal', 0), created = createNodeFromSpec(f.project, { canvasId: f.parent.canvasId, parentId: f.parent.id, type: 'container', title: '', tag: 'Nested', x: 0, y: 0, width: 900, height: 800, sizeMode: 'fixed' });
  const nested = get(created.project, created.nodeId); nested.collapsed = true;
  const leaf = addNode(created.project, nested.canvasId, 'media', 'Deep content'); Object.assign(leaf, { parentId: nested.id, x: 5000, y: 5000, width: 1000, height: 1000, sizeMode: 'fixed', childPosition: 'manual' });
  const expanded = revealChildren(created.project, nested.id, true), parent = get(expanded, f.parent.id), child = get(expanded, nested.id), detail = get(expanded, leaf.id), inner = boundedChildInterior(expanded, child);
  assert.deepEqual([parent.width, parent.height], [600.5, 420.25]);
  close(child.width, gridGeometry(parent, expanded).cellWidth); close(child.height, gridGeometry(parent, expanded).cellHeight);
  assert.ok(detail.x >= inner.x - .001 && detail.y >= inner.y - .001 && detail.x + detail.width <= inner.x + inner.width + .001 && detail.y + detail.height <= inner.y + inner.height + .001);
  assert.deepEqual(reflowContainedChildren(expanded, parent.id), expanded);
});

test('closed fixed containers keep their authored preview and hidden geometry while outside Spreadsheet pixel requests remain unchanged', () => {
  const f = fixture('spreadsheet', 2); f.parent.collapsed = true; f.parent.width = 240.5; f.parent.height = 110.25;
  const preserved = reflowContainedChildren(f.project, f.parent.id);
  assert.deepEqual(sizingBounds(preserved, get(preserved, f.parent.id)), { id: f.parent.id, x: f.parent.x, y: f.parent.y, width: 240.5, height: 110.25 });
  assert.deepEqual(preserved.nodes.map(node => [node.id, node.width, node.height, node.x, node.y]), f.project.nodes.map(node => [node.id, node.width, node.height, node.x, node.y]));
  const outside = fixture('spreadsheet', 2); outside.parent.kind = 'media'; outside.parent.childLayout = 'spatial'; outside.parent.sizeMode = 'fixed';
  outside.parent.grid!.cellWidth = 140; outside.parent.grid!.cellHeight = 90;
  const result = reflowContainedChildren(outside.project, outside.parent.id), parent = get(result, outside.parent.id), grid = gridGeometry(parent, result);
  close(grid.cellWidth, 140); close(grid.cellHeight, 90); close(parent.width, outside.parent.width); close(parent.height, outside.parent.height);
});

test('very small nested Freeform compositions keep rendered fractional dimensions consistent with their collision placement', () => {
  for (const variant of ['contained', 'grid'] as const) {
  const f = fixture(variant, 9); f.parent.width = 48.398; f.parent.height = 25.928; f.parent.title = 'A header';
  const result = reflowContainedChildren(f.project, f.parent.id), parent = get(result, f.parent.id), inner = boundedChildInterior(result, parent), boxes = f.children.map(source => sizingBounds(result, get(result, source.id)));
  for (const box of boxes) {
    assert.ok(box.x >= inner.x - 1e-9 && box.y >= inner.y - 1e-9);
    assert.ok(box.x + box.width <= inner.x + inner.width + 1e-9 && box.y + box.height <= inner.y + inner.height + 1e-9);
  }
  for (let left = 0; left < boxes.length; left++) for (let right = left + 1; right < boxes.length; right++) {
    const a = boxes[left], b = boxes[right];
    assert.ok(a.x + a.width <= b.x + 1e-12 || b.x + b.width <= a.x + 1e-12 || a.y + a.height <= b.y + 1e-12 || b.y + b.height <= a.y + 1e-12, 'Fractional fitted leaves must not be inflated after placement.');
  }
  assert.deepEqual(reflowContainedChildren(result, parent.id), result);
  }
});

test('sub-millipixel authored frames and resize walls retain usable areas within the frame instead of imposing a global pixel floor', () => {
  for (const variant of ['contained', 'grid'] as const) {
    const f = fixture(variant, 9); f.parent.width = .0005; f.parent.height = .0007; f.parent.x = 0; f.parent.y = 0;
    f.parent.style = { borderWidth: 0, padding: 0, childPadding: 0, shape: 'rectangle' };
    const result = reflowContainedChildren(f.project, f.parent.id), parent = get(result, f.parent.id), inner = boundedChildInterior(result, parent), content = nodeContentBounds(parent, parent);
    assert.ok(inner.width <= parent.width && inner.height <= parent.height);
    assert.ok(content.width <= parent.width && content.height <= parent.height);
    for (const source of f.children) {
      const child = get(result, source.id), frame = sizingBounds(result, child), resized = constrainChildGeometry(result, child, { x: -1, y: -1, width: 100, height: 100 });
      assert.ok(frame.width > 0 && frame.height > 0 && frame.x + frame.width <= parent.width + 1e-12 && frame.y + frame.height <= parent.height + 1e-12);
      assert.ok(resized.x >= inner.x && resized.y >= inner.y && resized.x + resized.width <= inner.x + inner.width + 1e-12 && resized.y + resized.height <= inner.y + inner.height + 1e-12);
    }
    assert.deepEqual(reflowContainedChildren(result, parent.id), result);
  }
});
