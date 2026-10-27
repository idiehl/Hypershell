import test from 'node:test';
import assert from 'node:assert/strict';
import { addNode, newProject } from '../src/core/model.ts';
import { boundedChildInterior, constrainChildGeometry, externalGalleryNavigationBounds } from '../src/core/sizing.ts';
import { gridGeometry, normalizeContainerLayouts, reflowContainedChildren } from '../src/core/grid.ts';
import { navigationIndex } from '../src/core/navigation.ts';
import { revealChildren } from '../src/core/reveal.ts';
import { resolveCanvasCollisions } from '../src/core/collision.ts';
import { validateProject } from '../src/core/rules.ts';
import { expansionTrayBounds } from '../src/core/expansion-tray.ts';
import type { ContainerVariant, MeridianNode, MeridianProject } from '../src/core/types.ts';

const inProject = (project: MeridianProject, id: string) => project.nodes.find(node => node.id === id)!;
function fixture(variant: ContainerVariant = 'contained', count = 4) {
  const project = newProject('Parent owns the frame'), parent = addNode(project, project.entryCanvasId, 'container', 'My canvases');
  Object.assign(parent, { x: 120, y: 90, width: 320, height: 240, sizeMode: 'fixed', collapsed: false, containerVariant: variant, childLayout: 'contained', childArrangement: 'free' });
  parent.grid = { columns: 2, rows: 2, cellWidth: 420, cellHeight: 160, gap: 0, fillOrder: 'row' };
  const children = Array.from({ length: count }, (_, at) => {
    const child = addNode(project, parent.canvasId, 'media', `Entry ${at + 1}`);
    Object.assign(child, { parentId: parent.id, x: 20 + at * 480, y: 430 + at * 50, width: 420, height: 160, sizeMode: 'fixed', text: `Preserved content ${at}`, childPosition: 'manual' });
    child.style = { fontSize: 23, padding: 9, shape: 'rectangle' }; return child;
  });
  return { project, parent, children };
}
function assertInside(project: MeridianProject, parent: MeridianNode, ids: string[]) {
  const interior = boundedChildInterior(project, parent), index = navigationIndex(project);
  for (const id of ids) {
    const bounds = index.bounds(inProject(project, id));
    assert.ok(bounds.x >= interior.x - .001 && bounds.y >= interior.y - .001, `${id} begins inside its parent`);
    assert.ok(bounds.x + bounds.width <= interior.x + interior.width + .001 && bounds.y + bounds.height <= interior.y + interior.height + .001, `${id} ends inside its parent`);
  }
}

test('fixed square Grid fits library tiles inside its actual centered backdrop without growing its frame', () => {
  const f = fixture('grid'); Object.assign(f.parent, { width: 756, height: 502, style: { shape: 'square', borderWidth: 2, childPadding: 5 } });
  const before = structuredClone(f.project), fitted = reflowContainedChildren(f.project, f.parent.id), parent = inProject(fitted, f.parent.id);
  assert.deepEqual(f.project, before); assert.deepEqual([parent.width, parent.height], [756, 502]);
  const interior = boundedChildInterior(fitted, parent); assert.equal(interior.x, parent.x + (756 - 502) / 2 + 7);
  assertInside(fitted, parent, f.children.map(node => node.id));
  const sizes = f.children.map(child => { const node = inProject(fitted, child.id); assert.equal(node.text, child.text); assert.deepEqual(node.style, child.style); return [node.width, node.height]; });
  assert.ok(sizes[0]![0]! < 420); assert.ok(sizes.every(size => size[0] === sizes[0]![0] && size[1] === sizes[0]![1]));
  assert.deepEqual(reflowContainedChildren(fitted, parent.id), fitted); assert.deepEqual(validateProject(fitted), []);
});

test('fixed rows and columns retain ordered coordinates and equal tile sizes while fitting the authored frame', () => {
  for (const variant of ['horizontal', 'vertical'] as const) {
    const f = fixture(variant, 6), fitted = reflowContainedChildren(f.project, f.parent.id), parent = inProject(fitted, f.parent.id);
    assert.deepEqual([parent.width, parent.height], [320, 240]); assertInside(fitted, parent, f.children.map(child => child.id));
    const first = inProject(fitted, f.children[0]!.id);
    for (const [at, child] of f.children.entries()) {
      const node = inProject(fitted, child.id); assert.deepEqual(node.cell, variant === 'horizontal' ? { column: at + 1, row: 1 } : { column: 1, row: at + 1 });
      assert.equal(node.width, first.width); assert.equal(node.height, first.height);
    }
    assert.deepEqual(normalizeContainerLayouts(fitted), fitted);
  }
});

test('Spreadsheet fits full capacity while Window fits only the visible viewport and preserves logical offscreen cells', () => {
  for (const variant of ['spreadsheet', 'window'] as const) {
    const f = fixture(variant, 8); f.parent.grid = { columns: 2, rows: 8, cellWidth: 420, cellHeight: 160, windowColumns: 2, windowRows: 2, scrollDirection: 'vertical' };
    const fitted = reflowContainedChildren(f.project, f.parent.id), parent = inProject(fitted, f.parent.id), geometry = gridGeometry(parent, fitted), interior = boundedChildInterior(fitted, parent);
    assert.deepEqual([parent.grid!.columns, parent.grid!.rows, parent.grid!.cellWidth, parent.grid!.cellHeight], [2, 8, 420, 160]); assert.deepEqual([parent.width, parent.height], [320, 240]);
    assert.ok(geometry.visibleColumns * geometry.cellWidth <= interior.width + .001); assert.ok(geometry.visibleRows * geometry.cellHeight <= interior.height + .001);
    if (variant === 'spreadsheet') assertInside(fitted, parent, f.children.map(child => child.id));
    else assert.ok(f.children.some(child => inProject(fitted, child.id).y >= parent.y + parent.height));
    assert.deepEqual(validateProject(fitted), []);
  }
});

test('dense fixed ordered collections derive cells smaller than schema request minima without losing addresses or validity', () => {
  const f = fixture('grid', 100), fitted = reflowContainedChildren(f.project, f.parent.id), parent = inProject(fitted, f.parent.id);
  const geometry = gridGeometry(parent, fitted); assert.ok(geometry.cellWidth < 36 && geometry.cellHeight < 36);
  assert.ok(parent.grid!.cellWidth >= 36 && parent.grid!.cellHeight >= 36); assertInside(fitted, parent, f.children.map(child => child.id)); assert.deepEqual(validateProject(fitted), []);
});

test('fixed Freeform shrinks the whole manual composition preserving order, disclosure, typography and internal routes', () => {
  const f = fixture('contained', 2), first = f.children[0]!, hidden = addNode(f.project, first.canvasId, 'media', 'Hidden detail');
  first.collapsed = true; Object.assign(hidden, { parentId: first.id, x: first.x + 100, y: first.y + 200, width: 100, height: 60, text: 'Hidden material' });
  const linkId = crypto.randomUUID(); f.project.links.push({ id: linkId, canvasId: first.canvasId, sourceId: first.id, targetId: hidden.id, kind: 'visual', relation: 'generic', points: [{ x: first.x + 70, y: first.y + 80 }] });
  const fitted = reflowContainedChildren(f.project, f.parent.id), parent = inProject(fitted, f.parent.id), moved = inProject(fitted, first.id), detail = inProject(fitted, hidden.id);
  assert.deepEqual([parent.width, parent.height], [320, 240]); assertInside(fitted, parent, f.children.map(child => child.id)); assert.ok(moved.width < first.width); assert.equal(moved.collapsed, true);
  const scale = moved.width / first.width; assert.ok(Math.abs((detail.x - moved.x) - 100 * scale) < .001); assert.ok(Math.abs((detail.y - moved.y) - 200 * scale) < .001);
  assert.equal(detail.text, hidden.text); assert.deepEqual(moved.style, first.style);
  const point = fitted.links.find(link => link.id === linkId)!.points![0]!; assert.ok(Math.abs(point.x - moved.x - 70 * scale) < .001);
  assert.deepEqual(reflowContainedChildren(fitted, parent.id), fitted);
  const collapsed = revealChildren(fitted, parent.id, false), reopened = revealChildren(collapsed, parent.id, true), comparable = structuredClone(reopened), initial = structuredClone(fitted);
  for (const source of [comparable, initial]) for (const node of source.nodes) delete node.containerSizes;
  assert.deepEqual(comparable, initial); assert.deepEqual(reopened.nodes.find(node => node.id === parent.id)?.containerSizes?.expanded, { width: parent.width, height: parent.height, sizeMode: 'fixed' });
});

test('a nested expanded bounded composition fits its resized cell without growing either enclosing frame', () => {
  const f = fixture('spreadsheet', 1), child = f.children[0]!;
  Object.assign(child, { kind: 'container', containerVariant: 'contained', collapsed: false, childLayout: 'contained', width: 1400, height: 1000 });
  const detail = addNode(f.project, child.canvasId, 'media', 'Deep child'); Object.assign(detail, { parentId: child.id, x: 900, y: 900, width: 800, height: 600, sizeMode: 'fixed', childPosition: 'manual' });
  const fitted = reflowContainedChildren(f.project, f.parent.id), parent = inProject(fitted, f.parent.id), inner = inProject(fitted, child.id);
  assertInside(fitted, parent, [inner.id]); assertInside(fitted, inner, [detail.id]); assert.deepEqual([parent.width, parent.height], [320, 240]); assert.equal(inProject(fitted, detail.id).title, detail.title);
  assert.deepEqual(reflowContainedChildren(fitted, parent.id), fitted);
});

test('full-edge child geometry respects shaped parent walls and ordered cell ownership', () => {
  const f = fixture('contained', 1); f.parent.style = { shape: 'diamond', borderWidth: 7, childPadding: 11 };
  const interior = boundedChildInterior(f.project, f.parent), child = f.children[0]!;
  const geometry = constrainChildGeometry(f.project, child, { x: -900, y: -900, width: 10000, height: 10000 });
  for (const key of ['x', 'y', 'width', 'height'] as const) assert.ok(Math.abs(geometry[key] - interior[key]) < .001);
  f.parent.containerVariant = 'grid'; child.cell = { column: 1, row: 1 };
  const grid = gridGeometry(f.parent, f.project), cell = constrainChildGeometry(f.project, child, { x: -900, y: -900, width: 10000, height: 10000 });
  assert.deepEqual(cell, { x: grid.left, y: grid.top, width: grid.cellWidth, height: grid.cellHeight });
});

test('left and top resize handles stop at the inner wall without moving the opposite edge', () => {
  const f = fixture('contained', 1), interior = boundedChildInterior(f.project, f.parent), child = f.children[0]!;
  Object.assign(child, { x: interior.x + 20, y: interior.y + 20, width: 70, height: 60 });
  const geometry = constrainChildGeometry(f.project, child, { x: interior.x - 80, y: interior.y - 80, width: 170, height: 160 });
  assert.equal(geometry.x, interior.x); assert.equal(geometry.y, interior.y);
  assert.equal(geometry.x + geometry.width, child.x + child.width); assert.equal(geometry.y + geometry.height, child.y + child.height);
});

test('collapsed nested containers retain hidden content but their compact tile cannot re-expand outside the fitted owner', () => {
  const f = fixture('contained', 1), child = f.children[0]!; Object.assign(child, { kind: 'container', containerVariant: 'contained', collapsed: true, title: 'A deliberately long child title '.repeat(6), style: { titleSize: 55, padding: 9 } });
  const hidden = addNode(f.project, child.canvasId, 'media', 'Hidden child'); Object.assign(hidden, { parentId: child.id, x: child.x + 600, y: child.y + 500, text: 'Original hidden text' });
  const fitted = reflowContainedChildren(f.project, f.parent.id); assertInside(fitted, inProject(fitted, f.parent.id), [child.id]);
  assert.equal(inProject(fitted, child.id).collapsed, true); assert.equal(inProject(fitted, hidden.id).text, hidden.text); assert.equal(navigationIndex(fitted).isNodeVisible(hidden.id), false);
  assert.deepEqual(reflowContainedChildren(fitted, f.parent.id), fitted);
});

test('unbounded Media disclosure preserves external sizes and never acquires a resize wall', () => {
  const f = fixture('horizontal', 2); f.parent.kind = 'media'; f.parent.childLayout = 'spatial';
  const requested = { x: -500, y: 1000, width: 900, height: 800 };
  assert.deepEqual(constrainChildGeometry(f.project, f.children[0]!, requested), requested);
  const fitted = reflowContainedChildren(f.project, f.parent.id); assert.deepEqual([inProject(fitted, f.parent.id).width, inProject(fitted, f.parent.id).height], [320, 240]);
  assert.equal(inProject(fitted, f.children[0]!.id).width, inProject(fitted, f.parent.id).grid!.cellWidth);
  assert.ok(inProject(fitted, f.children[0]!.id).y > f.parent.y + f.parent.height);
});

test('canvas collision repair moves an enclosure with its branch instead of ejecting a contained child', () => {
  const f = fixture('grid', 4), fitted = reflowContainedChildren(f.project, f.parent.id), first = inProject(fitted, f.children[0]!.id);
  const peer = addNode(fitted, first.canvasId, 'media', 'Outside peer'); Object.assign(peer, { x: first.x + 5, y: first.y + 5, width: 90, height: 90, sizeMode: 'fixed' });
  const repaired = resolveCanvasCollisions(fitted, first.canvasId, [first.id]);
  assertInside(repaired, inProject(repaired, f.parent.id), f.children.map(child => child.id));
});

test('an empty fixed enclosure retains authored dimensions through reveal and repeated normalization', () => {
  const f = fixture('contained', 0), shown = revealChildren(f.project, f.parent.id, true); assert.deepEqual([inProject(shown, f.parent.id).width, inProject(shown, f.parent.id).height], [320, 240]);
  assert.deepEqual(normalizeContainerLayouts(shown), shown);
});

test('an exterior Media Window uses a tray outside its fixed Freeform owner and retains offscreen address space', () => {
  const f = fixture('contained', 1), owner = f.children[0]!;
  Object.assign(owner, { collapsed: false, containerVariant: 'window', childLayout: 'spatial', width: 240, height: 120, grid: { columns: 3, rows: 6, cellWidth: 200, cellHeight: 100, windowColumns: 3, windowRows: 2 } });
  const pages = Array.from({ length: 18 }, (_, at) => { const node = addNode(f.project, owner.canvasId, 'media', `Window entry ${at}`); Object.assign(node, { parentId: owner.id, cell: { column: at % 3 + 1, row: Math.floor(at / 3) + 1 }, x: owner.x + at % 3 * 200, y: owner.y + 148 + Math.floor(at / 3) * 100, width: 200, height: 100, sizeMode: 'fixed', text: `Retained ${at}` }); return node; });
  const fitted = reflowContainedChildren(f.project, f.parent.id), parent = inProject(fitted, f.parent.id), window = inProject(fitted, owner.id), scope = boundedChildInterior(fitted, parent), box = navigationIndex(fitted).bounds(window), tray = expansionTrayBounds(fitted, owner.id)!;
  assert.ok(box.x >= scope.x - .001 && box.y >= scope.y - .001 && box.x + box.width <= scope.x + scope.width + .001 && box.y + box.height <= scope.y + scope.height + .001);
  assert.ok(tray.x >= parent.x + parent.width || tray.y >= parent.y + parent.height || tray.x + tray.width <= parent.x || tray.y + tray.height <= parent.y);
  assert.deepEqual(window.grid!.columns, 3); assert.deepEqual(window.grid!.rows, 6); assert.equal(window.collapsed, false);
  for (const page of pages) assert.equal(inProject(fitted, page.id).text, page.text);
  assert.deepEqual(reflowContainedChildren(fitted, parent.id), fitted); assert.deepEqual(validateProject(fitted), []);
});

test('an exterior Media Gallery retains every page and paging strip in its tray without progressive shrinking', () => {
  const f = fixture('contained', 1), owner = f.children[0]!;
  Object.assign(owner, { collapsed: false, containerVariant: 'gallery', childLayout: 'spatial', width: 240, height: 120 });
  const pages = Array.from({ length: 3 }, (_, at) => { const page = addNode(f.project, owner.canvasId, 'media', `Gallery page ${at}`); Object.assign(page, { parentId: owner.id, x: owner.x + 270, y: owner.y, width: 600, height: 400, sizeMode: 'fixed', text: `Preserved page ${at}` }); return page; });
  const fitted = reflowContainedChildren(f.project, f.parent.id), parent = inProject(fitted, f.parent.id), gallery = inProject(fitted, owner.id), scope = boundedChildInterior(fitted, parent), box = navigationIndex(fitted).bounds(gallery), tray = expansionTrayBounds(fitted, owner.id)!;
  assert.ok(box.x >= scope.x - .001 && box.y >= scope.y - .001 && box.x + box.width <= scope.x + scope.width + .001 && box.y + box.height <= scope.y + scope.height + .001);
  assert.ok(tray.x >= parent.x + parent.width || tray.y >= parent.y + parent.height || tray.x + tray.width <= parent.x || tray.y + tray.height <= parent.y);
  for (const page of pages) {
    const object = inProject(fitted, page.id), navigation = externalGalleryNavigationBounds(fitted, gallery, object);
    assert.ok(navigation.width > 0 && navigation.height > 0); assert.ok(navigation.y + navigation.height <= tray.y + tray.height + .001); assert.equal(object.text, page.text);
  }
  assert.deepEqual(reflowContainedChildren(fitted, parent.id), fitted); assert.deepEqual(validateProject(fitted), []);
});
