import test from 'node:test';
import assert from 'node:assert/strict';
import { libraryScene, moveLibraryBranch, revealLibraryChildren, type CanvasLibrary, type LibraryEntry } from '../src/core/library.ts';
import { navigationIndex } from '../src/core/navigation.ts';
import { isIntentionalContainment } from '../src/core/collision.ts';

const entry = (id: string, parentId: string | null, x: number, y: number, collapsed = true): LibraryEntry => ({ id, kind: 'section', title: id, parentId, x, y, collapsed });
const library = (entries: LibraryEntry[]): CanvasLibrary => ({ schemaVersion: 1, revision: 4, entries });
function expectNoVisibleOverlap(source: CanvasLibrary) {
  const scene = libraryScene(source, []), index = navigationIndex(scene);
  const boxes = source.entries.map(entry => index.nodes.get(entry.id)!).filter(node => index.visibleNode(node.id)?.id === node.id).map(index.bounds);
  for (let i = 0; i < boxes.length; i++) for (let j = i + 1; j < boxes.length; j++) {
    const a = boxes[i], b = boxes[j];
    if (isIntentionalContainment(index, a.id, b.id)) continue;
    assert.ok(a.x + a.width <= b.x || b.x + b.width <= a.x || a.y + a.height <= b.y || b.y + b.height <= a.y, `${a.id} overlaps ${b.id}`);
  }
}

test('library disclosure repairs legacy interior positions locally while retaining the parent tile and other branches', () => {
  const source = library([entry('parent', null, 80, 210), entry('child', 'parent', 104, 326), entry('detail', 'child', 128, 490), entry('unrelated', null, 440, 210)]);
  const before = structuredClone(source), open = revealLibraryChildren(source, 'parent', true);
  assert.deepEqual(source, before);
  assert.deepEqual(open.entries.find(entry => entry.id === 'parent'), { ...source.entries[0], collapsed: false });
  assert.deepEqual(open.entries.find(entry => entry.id === 'unrelated'), source.entries[3]);
  const child = open.entries.find(entry => entry.id === 'child')!, detail = open.entries.find(entry => entry.id === 'detail')!;
  assert.equal(child.collapsed, true); assert.equal(detail.x - child.x, 24); assert.equal(detail.y - child.y, 164);
  expectNoVisibleOverlap(open);
  const scene = libraryScene(open, []), index = navigationIndex(scene);
  assert.equal(scene.canvases.length, 1);
  assert.equal(index.nodes.get('parent')?.width, 336); assert.equal(index.nodes.get('parent')?.height, 180);
  assert.equal(index.nodes.get('parent')?.kind, 'media');
  assert.equal(index.visibleNode('child')?.id, 'child'); assert.equal(index.visibleNode('detail')?.id, 'child');
  assert.ok(scene.links.every(link => link.relation === 'generic' && link.kind === 'visual'));
});

test('collapse hides children without moving them and reopening keeps the resolved positions', () => {
  const source = library([entry('parent', null, 80, 210), entry('first', 'parent', 900, 900), entry('second', 'parent', 104, 326), entry('elsewhere', null, 80, 650)]);
  const open = revealLibraryChildren(source, 'parent', true), closed = revealLibraryChildren(open, 'parent', false);
  assert.deepEqual(closed.entries.map(entry => [entry.id, entry.x, entry.y]), open.entries.map(entry => [entry.id, entry.x, entry.y]));
  const resolved = open.entries.find(entry => entry.id === 'first')!;
  assert.deepEqual({ ...resolved, x: source.entries[1]!.x, y: source.entries[1]!.y }, source.entries[1]);
  expectNoVisibleOverlap(open);
  assert.deepEqual(revealLibraryChildren(source, 'parent', true, true).entries.find(entry => entry.id === 'first'), source.entries[1]);
  assert.deepEqual(revealLibraryChildren(closed, 'parent', true), open);
  const index = navigationIndex(libraryScene(closed, []));
  assert.equal(index.visibleNode('first')?.id, 'parent'); assert.equal(index.visibleNode('second')?.id, 'parent');
  assert.equal(index.visibleNode('elsewhere')?.id, 'elsewhere');
});

test('dragged legacy exterior branches preserve their authored position and internal composition', () => {
  const source = library([entry('parent', null, 80, 210), entry('child', 'parent', 440, 210), entry('detail', 'child', 800, 210)]);
  const dragged = moveLibraryBranch(source, 'child', 4500, -900);
  assert.equal(dragged.entries[1].placement, 'free');
  const open = revealLibraryChildren(dragged, 'parent', true, false);
  const moved = open.entries.find(entry => entry.id === 'child')!, detail = open.entries.find(entry => entry.id === 'detail')!;
  assert.equal(moved.placement, 'free'); assert.equal(detail.x - moved.x, 360); assert.equal(detail.y - moved.y, 0);
  assert.equal(moved.x, 4500); assert.equal(moved.y, -900); expectNoVisibleOverlap(open);
});

test('opening an ancestor also repairs intersecting legacy positions within already expanded children', () => {
  const source = library([entry('parent', null, 80, 210), entry('child', 'parent', 104, 326, false), entry('detail', 'child', 128, 490), entry('other', 'parent', 464, 326)]);
  const open = revealLibraryChildren(source, 'parent', true);
  expectNoVisibleOverlap(open);
  assert.equal(open.entries[1].collapsed, false);
  assert.deepEqual(revealLibraryChildren(open, 'parent', true), open);
});
