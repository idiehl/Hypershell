import assert from 'node:assert/strict';
import { test } from 'node:test';
import { addCanvas, addNode, forkProject, newProject } from '../src/core/model.ts';
import { canSnapNodes, isSnapLink, lockedClusterNodeIds, reconcileSnapLinks, setSnapLock, snapContact } from '../src/core/clusters.ts';
import { copyNodes, pasteNodes } from '../src/core/clipboard.ts';
import { resolveCanvasCollisions } from '../src/core/collision.ts';
import { splitLinkWithJunction } from '../src/core/connections.ts';
import { navigationIndex } from '../src/core/navigation.ts';
import { nestNodeBranches } from '../src/core/nesting.ts';
import { moveSelection } from '../src/core/selection.ts';
import { moveNodeBranch } from '../src/core/containment.ts';
import { updateNodeType } from '../src/core/node-editing.ts';
import { isProjectShape, validateProject } from '../src/core/rules.ts';
import { createPublicationPayload, buildPublication, readPublication } from '../src/publication.ts';
import type { MeridianNode } from '../src/core/types.ts';

function fixture() {
  const project = newProject('Cluster test'), canvas = project.canvases[0];
  const a = addNode(project, canvas.id, 'media', 'First'), b = addNode(project, canvas.id, 'media', 'Second'), c = addNode(project, canvas.id, 'media', 'Third');
  [a, b, c].forEach((node, i) => Object.assign(node, { x: i * 100, y: 0, width: 100, height: 100, sizeMode: 'fixed', style: { shape: 'rectangle' } }));
  return { project, canvas, a, b, c };
}
function at(project: ReturnType<typeof newProject>, node: MeridianNode) { return project.nodes.find(item => item.id === node.id)!; }

test('only real touching silhouettes create contacts, with a handle on their shared edge', () => {
  const first = { id: 'a', x: 0, y: 0, width: 100, height: 100, shape: 'rectangle' as const };
  const second = { ...first, id: 'b', x: 100 };
  assert.deepEqual(snapContact(first, second)?.point, { x: 100, y: 50 });
  assert.equal(snapContact(first, { ...second, x: 99 }), null, 'overlap is not a snap');
  assert.equal(snapContact(first, { ...second, x: 100.01 }), null, 'proximity is not physical touch');
  assert.equal(snapContact(first, { ...second, y: 100 }), null, 'unrelated corners do not form a shared edge');
  assert.equal(snapContact({ ...first, shape: 'diamond' }, { ...second, y: 75 }), null, 'bounding boxes touch but silhouette does not');
  const curved = snapContact({ ...first, shape: 'circle' }, { ...second, shape: 'circle' });
  assert.ok(curved); assert.ok(Math.abs(curved.point.x - 100) < 1e-4); assert.ok(Math.abs(curved.point.y - 50) < 1e-4);
  assert.equal(snapContact(first, { ...second, width: 0 }), null);
});

test('snapping creates an undirected, shaft-free link and removes detached unlocked contacts', () => {
  const f = fixture(), original = JSON.stringify(f.project);
  const snapped = reconcileSnapLinks(f.project, f.canvas.id, [f.a.id]);
  assert.equal(JSON.stringify(f.project), original);
  assert.equal(snapped.links.length, 1); assert.equal(snapped.links[0].snap?.locked, false);
  assert.equal(snapped.links[0].direction, 'none');
  assert.equal(navigationIndex(snapped).isLinkVisible(snapped.links[0]), false);
  assert.equal(navigationIndex(snapped).isLinkRevealed(snapped.links[0]), true);
  assert.equal(reconcileSnapLinks(snapped, f.canvas.id, [f.a.id]), snapped, 'no-op preserves project identity');
  const moved = moveSelection(snapped, [f.a.id], -20, 5);
  assert.equal(moved.links.length, 0);
  assert.equal(at(moved, f.b).x, 100);
  assert.deepEqual(validateProject(moved), []);
});

test('capture honors canvas and node sticky overrides, and searches only affected contact pairs', () => {
  const f = fixture();
  f.canvas.snapping = { stickyEdges: false };
  assert.equal(reconcileSnapLinks(f.project, f.canvas.id, [f.a.id]), f.project);
  f.a.snapping = { stickyEdges: true };
  const snapped = reconcileSnapLinks(f.project, f.canvas.id, [f.a.id]);
  assert.equal(snapped.links.length, 1); assert.ok(snapped.links.every(link => link.sourceId === f.a.id || link.targetId === f.a.id));
  f.canvas.snapping.stickyEdges = true; f.a.snapping.stickyEdges = false;
  assert.equal(reconcileSnapLinks(f.project, f.canvas.id, [f.a.id]), f.project, 'Free overrides sticky canvas behavior');
});

test('locking creates transitive clusters, preserving all relative geometry, hidden descendants and route bends', () => {
  const f = fixture(), hidden = addNode(f.project, f.canvas.id, 'media', 'Hidden child');
  hidden.parentId = f.b.id; hidden.x = 140; hidden.y = 170; f.b.collapsed = true;
  let locked = setSnapLock(f.project, f.a.id, f.b.id, true);
  locked = setSnapLock(locked, f.b.id, f.c.id, true);
  locked.links.push({ id: crypto.randomUUID(), canvasId: f.canvas.id, sourceId: f.a.id, targetId: f.c.id, kind: 'visual', relation: 'generic', points: [{ x: 50, y: 150 }] });
  assert.deepEqual(new Set(lockedClusterNodeIds(locked, f.canvas.id, [f.c.id])), new Set([f.a.id, f.b.id, f.c.id, hidden.id]));
  const moved = moveSelection(locked, [f.a.id], 15, 25);
  for (const node of [f.a, f.b, f.c, hidden]) assert.deepEqual({ x: at(moved, node).x, y: at(moved, node).y }, { x: node.x + 15, y: node.y + 25 });
  assert.deepEqual(moved.links.find(link => link.points)?.points, [{ x: 65, y: 175 }]);
  assert.ok(moved.links.filter(isSnapLink).every(link => link.snap?.locked));
  assert.deepEqual(validateProject(moved), []);
  const movedAgain = moveNodeBranch(moved, f.c.id, at(moved, f.c).x + 10, at(moved, f.c).y + 10);
  assert.equal(at(movedAgain, f.a).x, at(moved, f.a).x + 10);
  assert.equal(at(movedAgain, hidden).y, at(moved, hidden).y + 10);
});

test('unlocking splits a cluster and remains possible after a later geometry edit separates its edges', () => {
  const f = fixture(); let locked = setSnapLock(f.project, f.a.id, f.b.id, true);
  at(locked, f.a).width = 90;
  assert.equal(reconcileSnapLinks(locked, f.canvas.id, undefined, { createContacts: false }), locked);
  const unlocked = setSnapLock(locked, f.a.id, f.b.id, false, locked.links[0].id);
  assert.equal(unlocked.links.length, 0); assert.deepEqual(lockedClusterNodeIds(unlocked, f.canvas.id, [f.a.id]), [f.a.id]);
  assert.throws(() => setSnapLock(unlocked, f.a.id, f.b.id, true), /edges together/);
  assert.throws(() => setSnapLock(locked, f.a.id, f.b.id, false, 'stale'), /no longer available/);
});

test('roots, ancestry, cross-canvas, incompatible walls, automatic cells and inherited link restrictions cannot lock', () => {
  const f = fixture(), parent = addNode(f.project, f.canvas.id, 'container');
  assert.equal(canSnapNodes(f.project, f.a.id, f.a.id), false);
  assert.equal(canSnapNodes(f.project, f.a.id, f.canvas.rootId), false);
  const other = addNode(f.project, addCanvas(f.project, 'Other').id, 'media');
  assert.equal(canSnapNodes(f.project, f.a.id, other.id), false);
  f.b.parentId = parent.id;
  assert.equal(canSnapNodes(f.project, f.a.id, f.b.id), false, 'different walls');
  assert.equal(canSnapNodes(f.project, parent.id, f.b.id), false, 'ancestor');
  f.a.parentId = parent.id;
  assert.equal(canSnapNodes(f.project, f.a.id, f.b.id), true, 'Freeform siblings');
  for (const variant of ['horizontal', 'vertical', 'grid', 'spreadsheet', 'window', 'gallery'] as const) { parent.containerVariant = variant; assert.equal(canSnapNodes(f.project, f.a.id, f.b.id), false, variant); }
  parent.containerVariant = 'contained'; parent.rules = { linkKinds: ['numeric'] };
  assert.equal(canSnapNodes(f.project, f.a.id, f.b.id), false);
});

test('collision repair moves the complete rigid cluster and preserves an unrelated neighbor', () => {
  const f = fixture(); f.c.x = 50; f.c.y = 50;
  const locked = setSnapLock(f.project, f.a.id, f.b.id, true), repaired = resolveCanvasCollisions(locked, f.canvas.id, [f.a.id]);
  assert.equal(at(repaired, f.b).x - at(repaired, f.a).x, 100);
  assert.equal(at(repaired, f.b).y - at(repaired, f.a).y, 0);
  assert.deepEqual({ x: at(repaired, f.c).x, y: at(repaired, f.c).y }, { x: f.c.x, y: f.c.y });
  assert.equal(repaired.links[0].snap?.locked, true);
});

test('dropping a locked node nests its whole cluster; a new ordered owner cleanly replaces the lock with cell layout', () => {
  const f = fixture(), parent = addNode(f.project, f.canvas.id, 'container');
  Object.assign(parent, { x: 500, y: 500, width: 300, height: 250, sizeMode: 'fixed', containerVariant: 'horizontal' });
  const locked = setSnapLock(f.project, f.a.id, f.b.id, true);
  const nested = nestNodeBranches(locked, [f.a.id], parent.id);
  assert.equal(at(nested, f.a).parentId, parent.id); assert.equal(at(nested, f.b).parentId, parent.id);
  assert.equal(at(nested, f.c).parentId, f.canvas.rootId); assert.equal(nested.links.length, 0);
  assert.deepEqual(validateProject(nested), []);
});

test('Freeform drops preserve rigid offsets, while a later ordered pattern deliberately takes over child layout', () => {
  const f = fixture(), parent = addNode(f.project, f.canvas.id, 'container');
  Object.assign(parent, { x: 500, y: 500, sizeMode: 'auto' });
  const locked = setSnapLock(f.project, f.a.id, f.b.id, true), nested = nestNodeBranches(locked, [f.a.id], parent.id, { x: 520, y: 600 });
  assert.equal(at(nested, f.b).x - at(nested, f.a).x, 100); assert.equal(at(nested, f.b).y - at(nested, f.a).y, 0);
  assert.equal(nested.links[0].snap?.locked, true);
  const ordered = updateNodeType(nested, parent.id, { kind: 'container', containerVariant: 'horizontal' });
  assert.equal(ordered.links.length, 0); assert.ok(at(ordered, f.a).cell); assert.ok(at(ordered, f.b).cell);
  assert.deepEqual(validateProject(ordered), []);
});

test('copy-paste, fork and offline publication retain independent locked edge links with fresh identities', () => {
  const f = fixture(), locked = setSnapLock(f.project, f.a.id, f.b.id, true), clipboard = copyNodes(locked, [f.a.id, f.b.id]);
  const pasted = pasteNodes(locked, clipboard, f.canvas.id).project;
  const copyLink = pasted.links.find(link => link.id !== locked.links[0].id)!;
  assert.equal(copyLink.snap?.locked, true); assert.notEqual(copyLink.sourceId, f.a.id); assert.notEqual(copyLink.targetId, f.b.id);
  const fork = forkProject(locked); assert.equal(fork.links[0].snap?.locked, true); assert.notEqual(fork.links[0].id, locked.links[0].id);
  assert.equal(createPublicationPayload(locked).project.links[0].snap?.locked, true);
  const imported = readPublication(buildPublication(locked, '', ''));
  assert.equal(imported.links[0].snap?.locked, true); assert.notEqual(imported.links[0].id, locked.links[0].id);
  assert.deepEqual(validateProject(imported), []);
  assert.throws(() => splitLinkWithJunction(locked, locked.links[0].id, { x: 100, y: 50 }), /no line to split/);
});

test('untrusted snap flags, duplicate pairs, directed/functional routes and invalid endpoints are rejected atomically', () => {
  const f = fixture(), locked = setSnapLock(f.project, f.a.id, f.b.id, true);
  for (const value of [null, true, {}, { locked: 1 }, { locked: true, script: 'run' }]) {
    const invalid: any = structuredClone(locked); invalid.links[0].snap = value;
    assert.equal(isProjectShape(invalid), false); assert.ok(validateProject(invalid).length);
  }
  for (const change of [
    (link: any) => { link.direction = 'forward'; }, (link: any) => { delete link.direction; },
    (link: any) => { link.kind = 'numeric'; }, (link: any) => { link.relation = 'causal'; },
    (link: any) => { link.points = []; }, (link: any) => { link.sourceAnchor = { x: .5, y: .5 }; },
    (link: any) => { link.targetPort = 0; }, (link: any) => { link.targetId = link.sourceId; },
    (link: any) => { link.sourceId = f.canvas.rootId; },
  ]) { const invalid = structuredClone(locked); change(invalid.links[0]); assert.ok(validateProject(invalid).length); }
  const duplicate = structuredClone(locked); duplicate.links.push({ ...structuredClone(duplicate.links[0]), id: crypto.randomUUID(), sourceId: f.b.id, targetId: f.a.id });
  assert.ok(validateProject(duplicate).some(issue => issue.message.includes('at most one')));
  assert.equal(locked.links.length, 1);
});
