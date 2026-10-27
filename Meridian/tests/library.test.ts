import test from 'node:test';
import assert from 'node:assert/strict';
import {
  arrangeLibrary, emptyLibrary, libraryBranch, libraryScene, moveLibraryBranch, reconcileLibrary, reparentLibraryEntry,
  type CanvasLibrary, type LibraryCatalogItem, type LibraryEntry,
} from '../src/core/library.ts';
import { navigationIndex } from '../src/core/navigation.ts';
import { validateProject } from '../src/core/rules.ts';
import { isIntentionalContainment, resolveCanvasCollisions } from '../src/core/collision.ts';
import { normalizeContainerLayouts } from '../src/core/grid.ts';
import type { MeridianProject } from '../src/core/types.ts';

function entry(id: string, parentId: string | null = null, kind: LibraryEntry['kind'] = 'section', collapsed = false): LibraryEntry {
  return { id, kind, title: id, parentId, collapsed, x: 100, y: 200, ...(kind === 'canvas' ? { projectId: 'saved-canvas', canvasId: id } : {}) };
}
function library(entries: LibraryEntry[]): CanvasLibrary { return { schemaVersion: 1, revision: 7, entries }; }
function catalog(ids = ['main', 'chapter', 'detail']): LibraryCatalogItem[] {
  return [{ id: 'saved-canvas', title: 'Saved canvas', revision: 3, updatedAt: '2026-10-05T12:00:00Z', entryCanvasId: 'main',
    canvases: ids.map(id => ({ id, rootId: `root-${id}`, title: `Canvas ${id}`, nodeCount: id.length })),
    links: [{ sourceCanvasId: 'main', targetCanvasId: 'chapter' }, { sourceCanvasId: 'chapter', targetCanvasId: 'detail' }],
  }];
}
function canvasEntry(value: CanvasLibrary, canvasId: string): LibraryEntry {
  const result = value.entries.find(item => item.projectId === 'saved-canvas' && item.canvasId === canvasId);
  assert.ok(result, `Missing canvas ${canvasId}`); return result;
}
function expectSpatialScene(value: CanvasLibrary): void {
  const scene = libraryScene(value, catalog());
  assert.deepEqual(validateProject(scene), []);
  for (const entry of value.entries) {
    const node = scene.nodes.find(node => node.id === entry.id)!;
    assert.equal(node.childLayout, 'spatial');
    assert.equal(node.width, 336); assert.equal(node.height, 180);
  }
}
function expectVisibleNoOverlap(value: CanvasLibrary, displayed?: MeridianProject): void {
  const scene = displayed ?? libraryScene(value, catalog()), index = navigationIndex(scene);
  const boxes = value.entries.map(entry => index.nodes.get(entry.id)!).filter(node => index.visibleNode(node.id)?.id === node.id).map(index.bounds);
  for (let left = 0; left < boxes.length; left++) for (let right = left + 1; right < boxes.length; right++) {
    const a = boxes[left], b = boxes[right];
    if (isIntentionalContainment(index, a.id, b.id)) continue;
    assert.ok(a.x + a.width <= b.x || b.x + b.width <= a.x || a.y + a.height <= b.y || b.y + b.height <= a.y, `Tiles ${a.id} and ${b.id} overlap`);
  }
}

test('catalog reconciliation is immutable and idempotent and preserves edited titles, ancestry and disclosure state', () => {
  const initial = emptyLibrary(), beforeInitial = structuredClone(initial), items = catalog();
  const discovered = reconcileLibrary(initial, items);
  assert.deepEqual(initial, beforeInitial);
  assert.equal(discovered.entries.filter(item => item.kind === 'section').length, 1);
  assert.equal(discovered.entries.filter(item => item.kind === 'canvas').length, 3);
  const custom = structuredClone(discovered), root = canvasEntry(custom, 'main'), chapter = canvasEntry(custom, 'chapter');
  custom.entries.push(entry('research-section'));
  root.title = 'My curated title'; root.parentId = 'research-section'; root.collapsed = false;
  chapter.title = 'My chapter title'; chapter.collapsed = false;
  const beforeCustom = structuredClone(custom);
  const reconciled = reconcileLibrary(custom, [{ ...items[0], title: 'An externally changed title' }]);
  assert.deepEqual(reconciled, custom);
  assert.deepEqual(custom, beforeCustom);
  assert.deepEqual(reconcileLibrary(reconciled, items), reconciled);
});

test('reconciliation reuses legacy workspace references without adding a duplicate entry canvas', () => {
  const source = library([entry('research'), { ...entry('workspace', 'research', 'canvas'), canvasId: undefined, title: 'Personal name' }]);
  const result = reconcileLibrary(source, catalog());
  assert.equal(result.entries.filter(item => item.kind === 'canvas').length, 3);
  assert.equal(result.entries.find(item => item.id === 'workspace')?.title, 'Personal name');
  assert.equal(canvasEntry(result, 'chapter').parentId, 'workspace');
  assert.deepEqual(reconcileLibrary(result, catalog()), result);
});

test('discovering new saved and inner canvases preserves every existing dragged position and places new branches without sibling overlap', () => {
  const initial = reconcileLibrary(emptyLibrary(), catalog());
  const section = initial.entries.find(item => item.kind === 'section')!;
  let source = moveLibraryBranch(initial, section.id, 980, 1250);
  canvasEntry(source, 'main').collapsed = false;
  source = moveLibraryBranch(source, canvasEntry(source, 'chapter').id, 1700, 1700);
  source.entries.push({ ...entry('personal-section'), title: 'Personal grouping', x: -900, y: 600 });
  const before = structuredClone(source), items = catalog(['main', 'chapter', 'detail', 'new-chapter', 'new-detail']);
  items[0].links.push({ sourceCanvasId: 'chapter', targetCanvasId: 'new-chapter' }, { sourceCanvasId: 'new-chapter', targetCanvasId: 'new-detail' });
  items.push({ ...catalog(['other-main', 'other-detail'])[0], id: 'new-workspace', title: 'New saved canvas', entryCanvasId: 'other-main',
    links: [{ sourceCanvasId: 'other-main', targetCanvasId: 'other-detail' }] });
  const result = reconcileLibrary(source, items);
  for (const original of before.entries) assert.deepEqual(result.entries.find(item => item.id === original.id), original);
  assert.deepEqual(source, before);
  assert.equal(canvasEntry(result, 'new-chapter').parentId, canvasEntry(result, 'chapter').id);
  assert.equal(canvasEntry(result, 'new-detail').parentId, canvasEntry(result, 'new-chapter').id);
  const addedWorkspace = result.entries.find(item => item.projectId === 'new-workspace' && item.canvasId === 'other-main')!;
  const scene = libraryScene(result, items), previousWorkspace = scene.nodes.find(node => node.id === canvasEntry(result, 'main').id)!;
  assert.ok(addedWorkspace.y >= previousWorkspace.y + previousWorkspace.height + 24);
  assert.deepEqual(reconcileLibrary(result, items), result);
});

test('cyclic authored navigation becomes a complete acyclic spanning hierarchy with no duplicate inner canvases', () => {
  const items = catalog(['main', 'a', 'b', 'c', 'detached', 'island-child', 'alone']);
  items[0].links = [
    { sourceCanvasId: 'main', targetCanvasId: 'a' }, { sourceCanvasId: 'main', targetCanvasId: 'b' },
    { sourceCanvasId: 'a', targetCanvasId: 'c' }, { sourceCanvasId: 'b', targetCanvasId: 'c' },
    { sourceCanvasId: 'c', targetCanvasId: 'main' }, { sourceCanvasId: 'main', targetCanvasId: 'a' },
    { sourceCanvasId: 'b', targetCanvasId: 'missing-external' },
    { sourceCanvasId: 'detached', targetCanvasId: 'island-child' }, { sourceCanvasId: 'island-child', targetCanvasId: 'detached' },
  ];
  const result = reconcileLibrary(emptyLibrary(), items), roots = result.entries.filter(item => item.kind === 'canvas');
  assert.equal(roots.length, items[0].canvases.length);
  assert.equal(new Set(roots.map(item => item.canvasId)).size, roots.length);
  assert.equal(canvasEntry(result, 'a').parentId, canvasEntry(result, 'main').id);
  assert.equal(canvasEntry(result, 'b').parentId, canvasEntry(result, 'main').id);
  assert.equal(canvasEntry(result, 'c').parentId, canvasEntry(result, 'a').id);
  assert.equal(libraryBranch(result, canvasEntry(result, 'main').id).size, roots.length);
  assert.deepEqual(validateProject(libraryScene(result, items)), []);
  assert.deepEqual(reconcileLibrary(result, items), result);
});

test('disconnected authored components retain their own child relationships underneath the workspace', () => {
  const items = catalog(['main', 'detached', 'island-child']);
  items[0].links = [{ sourceCanvasId: 'detached', targetCanvasId: 'island-child' }];
  const result = reconcileLibrary(emptyLibrary(), items);
  assert.equal(canvasEntry(result, 'detached').parentId, canvasEntry(result, 'main').id);
  assert.equal(canvasEntry(result, 'island-child').parentId, canvasEntry(result, 'detached').id);
});

test('reparenting rejects cycles and missing nodes without mutation and accepts canvas parents', () => {
  const source = library([entry('section'), entry('main', 'section', 'canvas', true), entry('chapter', 'main', 'canvas'), entry('other')]);
  const before = structuredClone(source);
  assert.throws(() => reparentLibraryEntry(source, 'main', 'main'), /own contents/);
  assert.throws(() => reparentLibraryEntry(source, 'section', 'chapter'), /own contents/);
  assert.throws(() => reparentLibraryEntry(source, 'chapter', 'missing'), /existing/);
  assert.throws(() => reparentLibraryEntry(source, 'missing', 'main'), /existing/);
  assert.deepEqual(source, before);
  const moved = reparentLibraryEntry(source, 'other', 'main');
  assert.equal(moved.entries.find(item => item.id === 'other')?.parentId, 'main');
  assert.equal(moved.entries.find(item => item.id === 'main')?.collapsed, false);
  assert.equal(moved.entries.find(item => item.id === 'chapter')?.parentId, 'main');
  const detached = reparentLibraryEntry(moved, 'chapter', null);
  assert.equal(detached.entries.find(item => item.id === 'chapter')?.parentId, null);
  expectSpatialScene(moved); expectSpatialScene(detached);
});

test('branch dragging preserves descendant offsets, authored metadata and unrelated positions', () => {
  const source = arrangeLibrary(library([entry('section'), entry('main', 'section', 'canvas', true), entry('chapter', 'main', 'canvas'), entry('detail', 'chapter', 'canvas'), entry('other')]));
  const before = structuredClone(source), main = canvasEntry(source, 'main');
  const moved = moveLibraryBranch(source, main.id, main.x + 123.5, main.y - 72.25);
  const branch = libraryBranch(source, main.id);
  for (const original of source.entries) {
    const changed = moved.entries.find(item => item.id === original.id)!;
    assert.deepEqual(changed, branch.has(original.id) ? { ...original, x: original.x + 123.5, y: original.y - 72.25, ...(original.id === main.id ? { placement: 'free' } : {}) } : original);
  }
  assert.deepEqual(source, before);
  assert.throws(() => moveLibraryBranch(source, 'missing', 0, 0), /missing/);
  assert.throws(() => moveLibraryBranch(source, main.id, NaN, 0), /finite/);
  assert.throws(() => moveLibraryBranch(source, main.id, 0, Infinity), /finite/);
  assert.throws(() => moveLibraryBranch(source, main.id, 1e9, 0), /coordinate limits/);
  assert.deepEqual(source, before);
});

test('library scenes preserve real containment, metadata and valid geometry in the shared renderer', () => {
  const source = arrangeLibrary(library([entry('section'), entry('main', 'section', 'canvas'), entry('chapter', 'main', 'canvas', true), entry('detail', 'chapter', 'canvas')]));
  const before = structuredClone(source), scene = libraryScene(source, catalog()), index = navigationIndex(scene);
  assert.deepEqual(validateProject(scene), []);
  assert.equal(scene.nodes.length, source.entries.length + 2);
  assert.equal(scene.revision, source.revision);
  assert.deepEqual(scene.assets, []); assert.equal(scene.links.length, source.entries.filter(entry => entry.parentId).length); assert.ok(scene.links.every(link => link.relation === 'generic'));
  const section = index.nodes.get('section')!, main = index.nodes.get('main')!, chapter = index.nodes.get('chapter')!;
  assert.equal(section.parentId, scene.canvases[0].rootId);
  assert.equal(main.parentId, section.id);
  assert.equal(chapter.parentId, main.id);
  assert.equal(chapter.kind, 'media'); assert.equal(chapter.collapsed, true);
  assert.equal(index.visibleNode('detail')?.id, chapter.id);
  assert.match(main.text!, /4 nodes/);
  assert.deepEqual(source, before);
  expectSpatialScene(source);
});

test('legacy library branches preserve authored extents while manually sized collapsed containers retain their frame', () => {
  const entries = [entry('section'), entry('main', 'section', 'canvas')];
  for (let index = 0; index < 30; index++) entries.push(entry(`child-${index}`, 'main', 'canvas', true));
  const expanded = arrangeLibrary(library(entries));
  expectSpatialScene(expanded);
  const openScene = libraryScene(expanded, catalog()), open = openScene.nodes.find(node => node.id === 'main')!;
  open.sizeMode = 'fixed';
  assert.equal(open.height, 180, 'A spatial parent keeps its own tile geometry.'); expectVisibleNoOverlap(expanded);
  const closedSource = structuredClone(expanded); closedSource.entries.find(item => item.id === 'main')!.collapsed = true;
  const closed = arrangeLibrary(closedSource), closedScene = libraryScene(closed, catalog());
  closedScene.nodes.find(node => node.id === 'main')!.sizeMode = 'fixed';
  const index = navigationIndex(closedScene);
  assert.equal(index.bounds(index.nodes.get('main')!).height, 180);
  assert.equal(index.bounds(index.nodes.get('main')!).width, 336);
  assert.equal(closedScene.nodes.find(node => node.id === 'section')!.height, openScene.nodes.find(node => node.id === 'section')!.height);
  assert.equal(closed.entries.length, expanded.entries.length);
  for (let child = 0; child < 30; child++) assert.equal(index.visibleNode(`child-${child}`)?.id, 'main');
  const reopened = structuredClone(closed); reopened.entries.find(item => item.id === 'main')!.collapsed = false;
  assert.deepEqual(arrangeLibrary(reopened), expanded);
});

test('version-one library compatibility preserves exterior tiles and complete readable branches without inventing enclosure dimensions', () => {
  const entries = [entry('section'), entry('main', 'section', 'canvas'), entry('wide', 'main', 'canvas'), entry('wider', 'wide', 'canvas')];
  for (let index = 0; index < 9; index++) entries.push(entry(`leaf-${index}`, 'wider', 'canvas', true));
  entries.push(entry('sibling-a', 'main', 'canvas', true), entry('sibling-b', 'main', 'canvas', true), entry('section-two'));
  const arranged = arrangeLibrary(library(entries)), scene = libraryScene(arranged, catalog());
  expectSpatialScene(arranged);
  const wide = scene.nodes.find(node => node.id === 'wide')!, siblingA = scene.nodes.find(node => node.id === 'sibling-a')!, siblingB = scene.nodes.find(node => node.id === 'sibling-b')!;
  assert.equal(wide.width, 336); assert.equal(siblingA.width, 336); assert.equal(siblingB.width, 336);
  const normalized = normalizeContainerLayouts(scene), displayed = resolveCanvasCollisions(normalized, normalized.entryCanvasId);
  expectVisibleNoOverlap(arranged, displayed); assert.deepEqual(displayed.nodes.map(node => node.id), scene.nodes.map(node => node.id));
  for (const id of ['section', 'main', 'wide', 'wider']) { const node = displayed.nodes.find(node => node.id === id)!; assert.equal(node.kind, 'media'); assert.equal(node.childLayout, 'spatial'); assert.equal(node.width, 336); assert.equal(node.height, 180); }
  assert.deepEqual(arrangeLibrary(arranged), arranged);
});

test('spatial parent tiles keep their geometry when authored children are moved far away', () => {
  const source = arrangeLibrary(library([entry('section'), entry('main', 'section', 'canvas'), entry('chapter', 'main', 'canvas', true)]));
  const child = canvasEntry(source, 'chapter');
  const moved = moveLibraryBranch(source, child.id, child.x + 1250, child.y + 760), before = structuredClone(moved);
  expectSpatialScene(moved);
  assert.deepEqual(moved, before);
});

test('deep valid hierarchy is measured and arranged without recursive call-stack limits', () => {
  const entries = Array.from({ length: 5000 }, (_, index) => entry(`depth-${index}`, index ? `depth-${index - 1}` : null, 'canvas', false));
  const arranged = arrangeLibrary(library(entries)), scene = libraryScene(arranged, []);
  assert.equal(scene.nodes.length, entries.length + 2);
  assert.equal(libraryBranch(arranged, 'depth-0').size, entries.length);
  assert.ok(scene.nodes.every(node => [node.x, node.y, node.width, node.height].every(Number.isFinite)));
  assert.ok(arranged.entries[4999].y > arranged.entries[0].y);
});

test('unavailable workspace and unavailable inner canvas entries retain their location and display unavailable', () => {
  const source = arrangeLibrary(library([entry('section'), { ...entry('missing-workspace', 'section', 'canvas'), projectId: 'removed-workspace' }, entry('missing-inner', 'section', 'canvas')]));
  const before = structuredClone(source), scene = libraryScene(source, catalog());
  assert.match(scene.nodes.find(node => node.id === 'missing-workspace')!.text!, /unavailable/i);
  assert.match(scene.nodes.find(node => node.id === 'missing-inner')!.text!, /unavailable/i);
  assert.deepEqual(source, before);
});
