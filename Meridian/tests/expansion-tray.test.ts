import test from 'node:test';
import assert from 'node:assert/strict';
import { addCanvas, addNode, newProject } from '../src/core/model.ts';
import { normalizeContainerLayouts, reflowContainedChildren } from '../src/core/grid.ts';
import { revealChildren } from '../src/core/reveal.ts';
import { navigationIndex } from '../src/core/navigation.ts';
import { containingFixedInterior } from '../src/core/sizing.ts';
import { expansionTrayBounds, moveExpansionTray, repairExpansionTrayCollisions, visibleExpansionTrays } from '../src/core/expansion-tray.ts';
import { isIntentionalContainment } from '../src/core/collision.ts';
import { moveSelection } from '../src/core/selection.ts';
import { validateProject } from '../src/core/rules.ts';
import type { ContainerVariant, MeridianProject } from '../src/core/types.ts';
const get = (project: MeridianProject, id: string) => project.nodes.find(node => node.id === id)!;
function fixture(pattern: ContainerVariant = 'contained') {
  const project = newProject('Real expansion tray'), owner = addNode(project, project.entryCanvasId, 'container', '');
  Object.assign(owner, { x: 100, y: 100, width: 840, height: 500, sizeMode: 'fixed', collapsed: false, containerVariant: 'horizontal' });
  const source = addNode(project, owner.canvasId, 'media', 'Source');
  Object.assign(source, { parentId: owner.id, collapsed: true, containerVariant: pattern, sizeMode: 'fixed', width: 200, height: 120 });
  if (pattern === 'window') source.grid = { columns: 3, rows: 3, cellWidth: 200, cellHeight: 120, windowColumns: 2, windowRows: 2 };
  const children = Array.from({ length: 3 }, (_, at) => {
    const node = addNode(project, owner.canvasId, 'media', `Detail ${at}`);
    Object.assign(node, { parentId: source.id, x: 20 + at * 250, y: 50, width: 200, height: 120, sizeMode: 'fixed', collapsed: true, childPosition: 'auto', text: `Original content ${at}` }); return node;
  });
  return { project, owner, source, children };
}

test('all exterior display patterns open a real-identity tray beyond the bounded owner and remain stable on reload', () => {
  for (const pattern of ['contained', 'horizontal', 'vertical', 'grid', 'gallery', 'window'] as const) {
    const f = fixture(pattern), identities = f.project.nodes.map(node => [node.id, node.parentId, node.text]);
    const packed = reflowContainedChildren(f.project, f.owner.id), opened = reflowContainedChildren(revealChildren(packed, f.source.id, true), f.owner.id);
    const source = get(opened, f.source.id), frame = expansionTrayBounds(opened, source.id)!;
    assert.ok(frame.x >= f.owner.x + f.owner.width + 39, pattern);
    assert.deepEqual([get(opened, f.owner.id).width, get(opened, f.owner.id).height], [840, 500]);
    assert.deepEqual(opened.nodes.map(node => [node.id, node.parentId, node.text]), identities);
    assert.equal(containingFixedInterior(opened, get(opened, f.children[0].id)), undefined);
    assert.equal(isIntentionalContainment(navigationIndex(opened), f.children[0].id, f.owner.id), false);
    assert.equal(isIntentionalContainment(navigationIndex(opened), source.id, f.owner.id), true);
    assert.deepEqual(normalizeContainerLayouts(opened), opened, `${pattern}: normalization preserves placement`);
    const closed = revealChildren(opened, source.id, false);
    assert.equal(visibleExpansionTrays(closed).length, 0);
    const reopened = reflowContainedChildren(revealChildren(closed, source.id, true), f.owner.id);
    assert.deepEqual(reopened, opened, `${pattern}: close/reopen preserves all geometry and offsets`);
    assert.deepEqual(validateProject(opened), []);
  }
});

test('tray movement changes only real descendants and internal route points while source and parent movement retain the attachment', () => {
  const f = fixture('horizontal');
  f.project.links.push({ id: crypto.randomUUID(), canvasId: f.owner.canvasId, sourceId: f.children[0].id, targetId: f.children[1].id, parentId: f.source.id, kind: 'visual', relation: 'generic', points: [{ x: 400, y: 600 }] });
  const opened = revealChildren(reflowContainedChildren(f.project, f.owner.id), f.source.id, true), frame = expansionTrayBounds(opened, f.source.id)!;
  const before = structuredClone(opened), moved = moveExpansionTray(opened, f.source.id, 80, 60);
  assert.deepEqual(opened, before);
  for (const child of f.children) {
    const a = get(opened, child.id), b = get(moved, child.id); assert.deepEqual([b.x - a.x, b.y - a.y], [80, 60]);
  }
  assert.deepEqual([get(moved, f.source.id).x, get(moved, f.source.id).y], [get(opened, f.source.id).x, get(opened, f.source.id).y]);
  assert.deepEqual(moved.links[0].points, opened.links[0].points!.map(point => ({ x: point.x + 80, y: point.y + 60 })));
  const translated = moveSelection(moved, [f.owner.id], 35, -20), after = expansionTrayBounds(translated, f.source.id)!;
  assert.deepEqual([after.x - frame.x, after.y - frame.y], [115, 40]);
  assert.deepEqual(get(translated, f.source.id).expansionTray, get(moved, f.source.id).expansionTray);
  assert.deepEqual(validateProject(translated), []);
});

test('a source in an embedded Gallery exposes its tray only for the active object and keeps logical tiers', () => {
  const f = fixture(); f.owner.containerVariant = 'gallery';
  const other = addNode(f.project, f.owner.canvasId, 'media', 'Other Gallery object'); other.parentId = f.owner.id;
  const opened = revealChildren(f.project, f.source.id, true), active = { galleryItems: new Map([[f.owner.id, f.source.id]]) }, inactive = { galleryItems: new Map([[f.owner.id, other.id]]) };
  assert.equal(visibleExpansionTrays(opened, active).length, 1); assert.equal(visibleExpansionTrays(opened, inactive).length, 0);
  assert.equal(navigationIndex(opened, active).isNodeVisible(f.children[0].id), true);
  assert.equal(navigationIndex(opened, inactive).isNodeVisible(f.children[0].id), false);
  assert.deepEqual(navigationIndex(opened).ancestors(f.children[0].id).map(node => node.id), [f.source.id, f.owner.id, opened.canvases[0].rootId]);
});

test('an existing tray on an outer canvas cannot be dragged over its own source cover', () => {
  const f = fixture('horizontal'), opened = revealChildren(reflowContainedChildren(f.project, f.owner.id), f.source.id, true);
  const source = get(opened, f.source.id); source.parentId = opened.canvases[0].rootId;
  opened.nodes = opened.nodes.filter(node => node.id !== f.owner.id);
  const frame = expansionTrayBounds(opened, source.id)!, cover = navigationIndex(opened).bounds(source);
  const moved = moveExpansionTray(opened, source.id, cover.x - frame.x, cover.y - frame.y), result = expansionTrayBounds(moved, source.id)!;
  assert.ok(result.x + result.width <= cover.x + 1e-7 || result.x >= cover.x + cover.width - 1e-7 || result.y + result.height <= cover.y + 1e-7 || result.y >= cover.y + cover.height - 1e-7);
  assert.deepEqual(validateProject(moved), []);
});

test('nested trays retain independent content frames and move with an outer tray without adding hierarchy', () => {
  const f = fixture('horizontal'), nestedSource = f.children[0];
  const detail = addNode(f.project, f.owner.canvasId, 'media', 'Deeper detail');
  Object.assign(detail, { parentId: nestedSource.id, x: 5000, y: 4000, width: 320, height: 200, sizeMode: 'fixed', childPosition: 'auto', collapsed: true });
  const first = revealChildren(reflowContainedChildren(f.project, f.owner.id), f.source.id, true), outerFrame = expansionTrayBounds(first, f.source.id)!;
  const opened = reflowContainedChildren(revealChildren(first, nestedSource.id, true), f.source.id);
  const innerFrame = expansionTrayBounds(opened, nestedSource.id)!;
  assert.equal(visibleExpansionTrays(opened).length, 2);
  assert.deepEqual(expansionTrayBounds(opened, f.source.id), outerFrame, 'The outer tray packs only the nested source cover.');
  assert.deepEqual(normalizeContainerLayouts(opened), opened);
  const moved = moveExpansionTray(opened, f.source.id, 60, 40), innerMoved = expansionTrayBounds(moved, nestedSource.id)!;
  assert.deepEqual([innerMoved.x - innerFrame.x, innerMoved.y - innerFrame.y], [60, 40]);
  assert.equal(get(moved, detail.id).parentId, nestedSource.id);
  const hidden = revealChildren(opened, f.owner.id, false);
  assert.equal(visibleExpansionTrays(hidden).length, 0);
  assert.deepEqual(get(hidden, nestedSource.id).expansionTray, get(opened, nestedSource.id).expansionTray);
});

test('shrinking a Freeform enclosing workspace fits the source cover without shrinking its exterior tray contents', () => {
  const f = fixture(); f.owner.containerVariant = 'contained';
  const opened = revealChildren(reflowContainedChildren(f.project, f.owner.id), f.source.id, true), before = structuredClone(opened);
  const owner = get(opened, f.owner.id); owner.width = 100; owner.height = 120;
  const fitted = reflowContainedChildren(opened, owner.id);
  for (const child of f.children) {
    assert.deepEqual([get(fitted, child.id).width, get(fitted, child.id).height], [get(before, child.id).width, get(before, child.id).height]);
  }
  assert.ok(get(fitted, f.source.id).width < get(before, f.source.id).width);
  assert.deepEqual(normalizeContainerLayouts(fitted), fitted);
  assert.deepEqual(validateProject(fitted), []);
});

test('a closed exterior descendant keeps its hidden composition through ancestor collapse, document normalization and reopening', () => {
  const f = fixture('horizontal'), first = f.children[0]; first.containerVariant = 'horizontal';
  const hidden = addNode(f.project, f.owner.canvasId, 'media', 'Remembered descendant');
  Object.assign(hidden, { parentId: first.id, x: 700, y: 340, width: 190, height: 120, sizeMode: 'fixed', collapsed: true, childPosition: 'auto', containerVariant: 'horizontal' });
  const opened = revealChildren(normalizeContainerLayouts(f.project), f.source.id, true), moved = moveExpansionTray(opened, f.source.id, 60, 80);
  const expected = structuredClone(get(moved, hidden.id)), closed = revealChildren(moved, f.owner.id, false), loaded = normalizeContainerLayouts(JSON.parse(JSON.stringify(closed)));
  const reopened = reflowContainedChildren(revealChildren(loaded, f.owner.id, true), f.owner.id);
  assert.deepEqual(get(reopened, hidden.id), expected);
  assert.deepEqual(get(reopened, f.source.id).expansionTray, get(moved, f.source.id).expansionTray);
});

test('a closed Media source inside a bounded tray child retains useful descendant proportions until its own first expansion', () => {
  const f = fixture('horizontal'), middle = f.children[1];
  Object.assign(middle, { kind: 'container', title: '', containerVariant: 'horizontal', childLayout: 'contained', width: 420, height: 240,
    containerSizes: { expanded: { width: 420, height: 240, sizeMode: 'fixed' }, collapsed: { width: 190, height: 120, sizeMode: 'fixed' } } });
  const deepSource = addNode(f.project, f.owner.canvasId, 'media', 'Deep source'), detail = addNode(f.project, f.owner.canvasId, 'media', 'Deep original child');
  for (const [node, parentId, x] of [[deepSource, middle.id, 950], [detail, deepSource.id, 1200]] as const)
    Object.assign(node, { parentId, x, y: 200, width: 190, height: 120, sizeMode: 'fixed', collapsed: true, childPosition: 'auto', containerVariant: 'horizontal', childArrangement: 'row', text: `Original ${node.title} passage.`, style: { fontSize: 18, titleSize: 20, padding: 8 } });
  let current = normalizeContainerLayouts(f.project);
  assert.deepEqual([get(current, detail.id).width, get(current, detail.id).height], [190, 120]);
  for (const id of [f.source.id, middle.id, deepSource.id]) {
    current = revealChildren(current, id, true);
    for (const parent of navigationIndex(current).ancestors(id)) current = reflowContainedChildren(current, parent.id);
  }
  const box = navigationIndex(current).bounds(get(current, detail.id));
  assert.ok(box.width / box.height > .5 && box.width / box.height < 3, `Useful descendant proportions: ${box.width} × ${box.height}`);
  assert.deepEqual(normalizeContainerLayouts(current), current);
});

test('tray placement, collision repair and header movement ignore trays in another canvas at the same world coordinates', () => {
  const f = fixture('horizontal'), other = addCanvas(f.project, 'Independent workspace');
  const otherOwner = addNode(f.project, other.id, 'container', ''), otherSource = addNode(f.project, other.id, 'media', 'Source');
  Object.assign(otherOwner, { x: f.owner.x, y: f.owner.y, width: f.owner.width, height: f.owner.height, sizeMode: 'fixed', collapsed: false, containerVariant: 'horizontal' });
  Object.assign(otherSource, { parentId: otherOwner.id, collapsed: true, containerVariant: 'horizontal', sizeMode: 'fixed', width: 200, height: 120 });
  for (const original of f.children) {
    const copy = addNode(f.project, other.id, 'media', original.title);
    Object.assign(copy, { parentId: otherSource.id, x: original.x, y: original.y, width: original.width, height: original.height, sizeMode: 'fixed', collapsed: true, childPosition: 'auto', text: original.text });
  }
  let current = revealChildren(reflowContainedChildren(f.project, f.owner.id), f.source.id, true);
  current = revealChildren(reflowContainedChildren(current, otherOwner.id), otherSource.id, true);
  const first = expansionTrayBounds(current, f.source.id)!, second = expansionTrayBounds(current, otherSource.id)!;
  assert.deepEqual([second.x, second.y, second.width, second.height], [first.x, first.y, first.width, first.height], 'Separate canvases may use identical tray coordinates.');
  const before = structuredClone(current); repairExpansionTrayCollisions(current); assert.deepEqual(current, before);
  const shiftedOther = moveExpansionTray(current, otherSource.id, 80, 60), moved = moveExpansionTray(shiftedOther, f.source.id, 80, 60);
  const result = expansionTrayBounds(moved, f.source.id)!;
  assert.deepEqual([result.x - first.x, result.y - first.y], [80, 60], 'A tray may move onto coordinates occupied only in another workspace.');
  assert.deepEqual(get(moved, otherSource.id), get(shiftedOther, otherSource.id));
});
