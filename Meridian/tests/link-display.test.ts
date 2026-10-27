import test from 'node:test';
import assert from 'node:assert/strict';
import { addNode, newProject } from '../src/core/model.ts';
import { navigationIndex } from '../src/core/navigation.ts';
import { displayNodeBounds, routeWindowOffset } from '../src/window-scene.ts';

function fixture() {
  const project = newProject('Scrolled link coordinates'), canvasId = project.entryCanvasId;
  const outer = addNode(project, canvasId, 'container', 'Outer Window'), inner = addNode(project, canvasId, 'container', 'Inner Window');
  for (const node of [outer, inner]) Object.assign(node, { containerVariant: 'window', collapsed: false, sizeMode: 'fixed', grid: { columns: 4, rows: 6, cellWidth: 160, cellHeight: 90, windowColumns: 2, windowRows: 2 } });
  Object.assign(outer, { x: 100, y: 100, width: 720, height: 620 });
  Object.assign(inner, { parentId: outer.id, x: 250, y: 300, width: 350, height: 240 });
  const source = addNode(project, canvasId, 'media', 'Source'), target = addNode(project, canvasId, 'media', 'Target'), sibling = addNode(project, canvasId, 'media', 'Other outer child'), external = addNode(project, canvasId, 'media', 'External');
  Object.assign(source, { parentId: inner.id, x: 310, y: 390, width: 80, height: 40, sizeMode: 'fixed' });
  Object.assign(target, { parentId: inner.id, x: 500, y: 530, width: 80, height: 40, sizeMode: 'fixed' });
  Object.assign(sibling, { parentId: outer.id, x: 620, y: 370, width: 80, height: 40, sizeMode: 'fixed' });
  Object.assign(external, { x: 900, y: 200, width: 80, height: 40, sizeMode: 'fixed' });
  const offsets = new Map([[outer.id, { x: 20, y: 100 }], [inner.id, { x: 10, y: 50 }]]);
  return { project, outer, inner, source, target, sibling, external, offsets };
}

test('displayed node bounds subtract all enclosing Window scroll offsets without changing saved geometry', () => {
  const f = fixture(), index = navigationIndex(f.project), before = structuredClone(f.project), offsets = structuredClone(f.offsets);
  const source = index.bounds(f.source), shown = displayNodeBounds(index, f.source, f.offsets);
  assert.deepEqual(shown, { ...source, x: source.x - 30, y: source.y - 150 });
  const inner = index.bounds(f.inner);
  assert.deepEqual(displayNodeBounds(index, f.inner, f.offsets), { ...inner, x: inner.x - 20, y: inner.y - 100 }, 'A Window tile does not scroll with its own content.');
  assert.deepEqual(displayNodeBounds(index, f.external, f.offsets), index.bounds(f.external));
  assert.deepEqual(f.project, before); assert.deepEqual(f.offsets, offsets);
});

test('a route uses the innermost shared Window content offset including that owner own scrolling', () => {
  const f = fixture(), index = navigationIndex(f.project);
  assert.deepEqual(routeWindowOffset(index, f.source.id, f.target.id, f.offsets), { x: 30, y: 150 });
  assert.deepEqual(routeWindowOffset(index, f.source.id, f.sibling.id, f.offsets), { x: 20, y: 100 });
  assert.deepEqual(routeWindowOffset(index, f.sibling.id, f.source.id, f.offsets), { x: 20, y: 100 });
  assert.deepEqual(routeWindowOffset(index, f.source.id, f.external.id, f.offsets), { x: 0, y: 0 });
  const displayedPivot = { x: 470, y: 260 }, offset = routeWindowOffset(index, f.source.id, f.target.id, f.offsets);
  assert.deepEqual({ x: displayedPivot.x + offset.x, y: displayedPivot.y + offset.y }, { x: 500, y: 410 }, 'Committed utilities use the shared owner content coordinates.');
});

test('missing offsets and compressed Windows do not manufacture scroll translations', () => {
  const f = fixture();
  assert.deepEqual(routeWindowOffset(navigationIndex(f.project), f.source.id, f.target.id, new Map()), { x: 0, y: 0 });
  f.inner.collapsed = true;
  const index = navigationIndex(f.project);
  assert.deepEqual(routeWindowOffset(index, f.source.id, f.target.id, f.offsets), { x: 20, y: 100 });
  const source = index.bounds(f.source);
  assert.deepEqual(displayNodeBounds(index, f.source, f.offsets), { ...source, x: source.x - 20, y: source.y - 100 });
});
