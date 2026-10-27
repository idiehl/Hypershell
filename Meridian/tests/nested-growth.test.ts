import test from 'node:test';
import assert from 'node:assert/strict';
import { addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import { reflowContainedChildren, gridGeometry, gridCellBounds, normalizeContainerLayouts } from '../src/core/grid.ts';
import { navigationIndex } from '../src/core/navigation.ts';
import { revealChildren } from '../src/core/reveal.ts';
import { boundedChildInterior } from '../src/core/sizing.ts';
import { expansionTrayBounds } from '../src/core/expansion-tray.ts';
import { validateProject, isProjectShape } from '../src/core/rules.ts';
import type { ContainerVariant, MeridianNode, MeridianProject, NodeStyle } from '../src/core/types.ts';

const get = (project: MeridianProject, id: string) => project.nodes.find(node => node.id === id)!;
function container(project: MeridianProject, title: string, parent?: MeridianNode, variant: ContainerVariant = 'contained', shape?: NodeStyle['shape']) {
  const result = addNode(project, project.entryCanvasId, 'container', title);
  Object.assign(result, { parentId: parent?.id ?? result.parentId, x: parent ? parent.x + 6 : 100, y: parent ? parent.y + 30 : 100,
    width: 130, height: 100, sizeMode: 'fixed', containerVariant: variant, collapsed: true, childPosition: 'manual' });
  if (shape) result.style = { shape };
  if (variant !== 'contained' && variant !== 'gallery') result.grid = { columns: 2, rows: 2, cellWidth: 110, cellHeight: 75,
    windowColumns: 1, windowRows: 1, fillOrder: 'row', scrollDirection: 'vertical' };
  return result;
}
function detail(project: MeridianProject, parent: MeridianNode, at = 0) {
  const child = addNode(project, parent.canvasId, 'media', `Detailed object ${at}`);
  Object.assign(child, { parentId: parent.id, x: parent.x + 6 + at * 310, y: parent.y + 40,
    width: 300, height: 190, sizeMode: 'fixed', text: 'An independently scaled descendant.', childPosition: 'manual', collapsed: true });
  return child;
}
function inside(project: MeridianProject, parent: MeridianNode, child: MeridianNode) {
  const interior = boundedChildInterior(project, parent), box = navigationIndex(project).bounds(child);
  assert.ok(box.x >= interior.x - .001, `${child.title}: left inside ${parent.title}`);
  assert.ok(box.y >= interior.y - .001, `${child.title}: top inside ${parent.title}`);
  assert.ok(box.x + box.width <= interior.x + interior.width + .001, `${child.title}: right inside ${parent.title}`);
  assert.ok(box.y + box.height <= interior.y + interior.height + .001, `${child.title}: bottom inside ${parent.title}`);
}

test('nested disclosure fits contents without growing fixed bounded ancestors or revealing collapsed siblings', () => {
  const project = newProject('Nested disclosure'), outer = container(project, 'Outer'), middle = container(project, 'Middle', outer), inner = container(project, 'Inner', middle);
  outer.collapsed = middle.collapsed = false;
  const item = detail(project, inner), sibling = container(project, 'Sibling', middle);
  sibling.x += 150; const hidden = detail(project, sibling);
  const initial = reflowContainedChildren(reflowContainedChildren(project, middle.id), outer.id), before = structuredClone(initial);
  const itemBefore = get(initial, item.id), expanded = revealChildren(initial, inner.id, true);
  assert.deepEqual(initial, before);
  for (const parent of [outer, middle, inner]) {
    const next = get(expanded, parent.id), prior = get(initial, parent.id);
    assert.equal(next.sizeMode, 'fixed'); assert.ok(next.width <= prior.width + .001); assert.ok(next.height <= prior.height + .001);
  }
  assert.deepEqual([get(expanded, outer.id).width, get(expanded, outer.id).height], [get(initial, outer.id).width, get(initial, outer.id).height]);
  assert.ok(get(expanded, item.id).width <= itemBefore.width); assert.ok(get(expanded, item.id).height <= itemBefore.height);
  inside(expanded, get(expanded, outer.id), get(expanded, middle.id));
  inside(expanded, get(expanded, middle.id), get(expanded, inner.id));
  inside(expanded, get(expanded, inner.id), get(expanded, item.id));
  assert.equal(get(expanded, sibling.id).collapsed, true); assert.equal(navigationIndex(expanded).isNodeVisible(hidden.id), false);
  assert.deepEqual(validateProject(expanded), []);
  // The editor subsequently reflows every ancestor and every changed node.
  let again = expanded;
  for (const parent of [inner, middle, outer]) again = reflowContainedChildren(again, parent.id);
  assert.deepEqual(again, expanded);
  const compact = revealChildren(expanded, inner.id, false);
  assert.deepEqual(get(compact, inner.id).containerSizes?.expanded, { width: get(expanded, inner.id).width, height: get(expanded, inner.id).height, sizeMode: 'fixed' });
  assert.equal(get(compact, inner.id).collapsed, true, 'The independent closed mode does not alter the remembered expanded frame.');
  commitSnapshots(compact);
  const saved = JSON.parse(JSON.stringify(compact)) as MeridianProject;
  assert.equal(isProjectShape(saved), true);
  const reopened = revealChildren(saved, inner.id, true);
  for (const parent of [outer, middle, inner]) assert.deepEqual([get(reopened, parent.id).width, get(reopened, parent.id).height], [get(expanded, parent.id).width, get(expanded, parent.id).height]);
  assert.deepEqual(normalizeContainerLayouts(reopened), reopened);
});

test('nested disclosure retains ordered parent cells while preserving coordinates, fill order and capacities', () => {
  for (const variant of ['horizontal', 'vertical', 'grid', 'spreadsheet', 'window'] as const) {
    const project = newProject(variant), outer = container(project, 'Ordered owner', undefined, variant), inner = container(project, 'Nested workspace', outer), peer = detail(project, outer);
    outer.collapsed = false; outer.width = 240; outer.height = 200;
    detail(project, inner); detail(project, inner, 1);
    inner.cell = { column: 1, row: 1, name: 'nested' }; peer.cell = { column: 2, row: 1 };
    const initial = reflowContainedChildren(project, outer.id), capacity = { columns: get(initial, outer.id).grid!.columns, rows: get(initial, outer.id).grid!.rows };
    const expanded = revealChildren(initial, inner.id, true), owner = get(expanded, outer.id), child = get(expanded, inner.id), sibling = get(expanded, peer.id);
    assert.deepEqual([owner.width, owner.height], [get(initial, outer.id).width, get(initial, outer.id).height], variant);
    assert.deepEqual({ columns: owner.grid!.columns, rows: owner.grid!.rows }, capacity);
    assert.deepEqual(child.cell, get(initial, inner.id).cell); assert.equal(owner.grid!.fillOrder, 'row');
    const geometry = gridGeometry(owner, expanded);
    assert.equal(child.width, geometry.cellWidth); assert.equal(sibling.width, child.width); assert.equal(sibling.height, child.height);
    assert.deepEqual({ x: child.x, y: child.y, width: child.width, height: child.height }, gridCellBounds(owner, child.cell!, expanded));
    inside(expanded, child, get(expanded, project.nodes.find(node => node.parentId === inner.id)!.id));
    assert.deepEqual(validateProject(expanded), []);
    assert.deepEqual(reflowContainedChildren(expanded, outer.id), expanded);
  }
});

test('opening a normalized collapsed ordered child fits its logical cells within its current allocated frame', () => {
  const project = newProject('Normalized nested grid'), outer = container(project, 'Collection', undefined, 'horizontal'), inner = container(project, 'Three detail layers', outer, 'horizontal');
  Object.assign(outer, { collapsed: false, width: 460, height: 170, grid: { columns: 2, rows: 1, cellWidth: 220, cellHeight: 120 } });
  inner.grid = { columns: 3, rows: 1, cellWidth: 220, cellHeight: 150 };
  for (let at = 0; at < 3; at++) detail(project, inner, at);
  detail(project, outer);
  const loaded = normalizeContainerLayouts(project), collapsed = get(loaded, inner.id);
  assert.equal(collapsed.collapsed, true); assert.equal(collapsed.grid!.cellWidth, 220);
  const expanded = revealChildren(loaded, inner.id, true), nested = get(expanded, inner.id), owner = get(expanded, outer.id);
  const geometry = gridGeometry(nested, expanded);
  assert.deepEqual([owner.width, owner.height], [get(loaded, outer.id).width, get(loaded, outer.id).height]);
  for (const child of expanded.nodes.filter(child => child.parentId === inner.id)) { assert.equal(child.width, geometry.cellWidth); assert.equal(child.height, geometry.cellHeight); inside(expanded, nested, child); }
  inside(expanded, owner, nested);
  assert.deepEqual(reflowContainedChildren(expanded, owner.id), expanded);
});

test('shaped bounded ancestors grow their safe interior on nested disclosure', () => {
  for (const shape of ['circle', 'diamond', 'hexagon'] as const) {
    const project = newProject(shape), outer = container(project, 'Shaped owner', undefined, 'contained', shape), inner = container(project, 'Nested', outer);
    outer.collapsed = false; detail(project, inner);
    const initial = reflowContainedChildren(project, outer.id), expanded = revealChildren(initial, inner.id, true);
    inside(expanded, get(expanded, outer.id), get(expanded, inner.id));
    inside(expanded, get(expanded, inner.id), get(expanded, project.nodes.find(node => node.parentId === inner.id)!.id));
    assert.equal(get(expanded, outer.id).style!.shape, shape); assert.deepEqual(validateProject(expanded), []);
    assert.deepEqual(revealChildren(expanded, inner.id, true), expanded, `${shape}: repeated disclosure stays stable`);
  }
});

test('nested Gallery and Window disclosure reserve only their visible object or viewport', () => {
  for (const variant of ['gallery', 'window'] as const) {
    const project = newProject(variant), outer = container(project, 'Owner'), inner = container(project, 'Nested', outer, variant);
    outer.collapsed = false;
    if (variant === 'window') inner.grid = { columns: 2, rows: 12, cellWidth: 150, cellHeight: 100, windowColumns: 1, windowRows: 2 };
    const count = variant === 'gallery' ? 3 : 24;
    for (let at = 0; at < count; at++) detail(project, inner, at);
    const initial = reflowContainedChildren(project, outer.id), expanded = revealChildren(initial, inner.id, true), nested = get(expanded, inner.id);
    inside(expanded, get(expanded, outer.id), nested);
    if (variant === 'window') { assert.equal(nested.grid!.rows, 12); assert.ok(nested.height < 400); }
    else assert.ok(nested.height < 350, 'one embedded object with navigation, not three visible rows');
    assert.deepEqual(validateProject(expanded), []);
  }
});

test('initial fixed-parent fitting and subsequent manual resize reflow retain the wall behavior', () => {
  const project = newProject('Initial fit'), outer = container(project, 'Owner'), item = detail(project, outer);
  const revealed = revealChildren(project, outer.id, true);
  assert.deepEqual([get(revealed, outer.id).width, get(revealed, outer.id).height], [outer.width, outer.height]);
  assert.ok(get(revealed, item.id).width < item.width); inside(revealed, get(revealed, outer.id), get(revealed, item.id));
  const requested = structuredClone(revealed); get(requested, item.id).width = 1000; get(requested, item.id).height = 900;
  const refitted = reflowContainedChildren(requested, outer.id);
  assert.deepEqual([get(refitted, outer.id).width, get(refitted, outer.id).height], [outer.width, outer.height]);
  inside(refitted, get(refitted, outer.id), get(refitted, item.id));
});

test('fixed Gallery ancestors retain their frame during nested Gallery disclosure without exposing all their objects', () => {
  const project = newProject('A gallery inside a gallery'), outer = container(project, 'Outer gallery', undefined, 'gallery'), inner = container(project, 'Nested gallery', outer, 'gallery');
  outer.collapsed = false;
  const item = detail(project, inner); item.text = 'An expansive close reading of one object. '.repeat(32);
  const sibling = detail(project, outer); sibling.text = 'The other object remains a separate page.';
  const before = structuredClone(project), expanded = revealChildren(project, inner.id, true), owner = get(expanded, outer.id), child = get(expanded, inner.id);
  assert.deepEqual(project, before);
  assert.deepEqual([owner.width, owner.height], [outer.width, outer.height]);
  assert.deepEqual([child.width, child.height], [inner.width, inner.height], 'Gallery presentation fits the active object without rewriting its authored frame.');
  assert.equal(owner.sizeMode, 'fixed'); assert.equal(child.sizeMode, 'fixed');
  assert.equal(navigationIndex(expanded).isNodeVisible(sibling.id), false);
  assert.deepEqual(revealChildren(expanded, inner.id, true), expanded);
  assert.deepEqual(validateProject(expanded), []);
});

test('a growing ancestor keeps the nested branch inside and repairs overlap with outside peers', () => {
  const project = newProject('Growth beside a peer'), outer = container(project, 'Owner'), inner = container(project, 'Nested', outer);
  outer.collapsed = false; detail(project, inner);
  const peer = detail(project, get(project, project.canvases[0]!.rootId));
  Object.assign(peer, { x: outer.x + outer.width + 2, y: outer.y, width: 100, height: 100 });
  const initial = reflowContainedChildren(project, outer.id), expanded = revealChildren(initial, inner.id, true), index = navigationIndex(expanded);
  const owner = index.bounds(get(expanded, outer.id)), beside = index.bounds(get(expanded, peer.id));
  assert.ok(owner.x + owner.width <= beside.x + .001 || beside.x + beside.width <= owner.x + .001 || owner.y + owner.height <= beside.y + .001 || beside.y + beside.height <= owner.y + .001);
  inside(expanded, get(expanded, outer.id), get(expanded, inner.id));
  assert.equal(get(expanded, peer.id).parentId, peer.parentId); assert.equal(get(expanded, peer.id).text, peer.text);
});

test('initial fitting keeps the Media Window cover inside while its viewport opens in an exterior tray', () => {
  const project = newProject('Window cover and viewport'), outer = container(project, 'Owner'), media = detail(project, outer);
  Object.assign(outer, { width: 620, height: 420, collapsed: true });
  Object.assign(media, { width: 220, height: 100, collapsed: false, containerVariant: 'window', childLayout: 'spatial', childPosition: 'auto', text: 'Unbounded cover remains visible',
    grid: { columns: 4, rows: 4, cellWidth: 280, cellHeight: 180, windowColumns: 3, windowRows: 2, scrollDirection: 'vertical' } });
  for (let at = 0; at < 16; at++) { const item = detail(project, media, at); Object.assign(item, { width: 600, height: 340, childPosition: 'auto' }); }
  const opened = revealChildren(normalizeContainerLayouts(project), outer.id, true), owner = get(opened, outer.id), window = get(opened, media.id), geometry = gridGeometry(window, opened), interior = boundedChildInterior(opened, owner);
  assert.deepEqual([owner.width, owner.height], [620, 420]);
  assert.ok(geometry.cellWidth > 1); assert.ok(geometry.cellHeight > 1);
  const tray = expansionTrayBounds(opened, window.id)!;
  assert.ok(window.x >= interior.x - .001 && window.y >= interior.y - .001);
  assert.ok(geometry.left >= tray.content.x - .001 && geometry.top >= tray.content.y - .001);
  assert.ok(geometry.left + geometry.visibleColumns * geometry.cellWidth <= tray.content.x + tray.content.width + .001);
  assert.ok(geometry.top + geometry.visibleRows * geometry.cellHeight <= tray.content.y + tray.content.height + .001);
  assert.ok(tray.x >= get(opened, outer.id).x + get(opened, outer.id).width || tray.y >= get(opened, outer.id).y + get(opened, outer.id).height);
  assert.deepEqual(reflowContainedChildren(opened, outer.id), opened, 'repeated fitting retains the effective Window scale');
  assert.deepEqual(normalizeContainerLayouts(opened), opened, 'saved normalization retains the cover and viewport');
});
