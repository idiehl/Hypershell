import assert from 'node:assert/strict';
import { test } from 'node:test';
import { addCanvas, addNode, commitSnapshots, forkProject, newProject } from '../src/core/model.ts';
import { canMoveNodeBranches, setNodePositionLocked } from '../src/core/position-lock.ts';
import { isProjectShape, validateChange, validateProject } from '../src/core/rules.ts';
import { moveNodeBranch } from '../src/core/containment.ts';
import { moveSelection } from '../src/core/selection.ts';
import { resolveCanvasCollisions } from '../src/core/collision.ts';
import { navigationIndex } from '../src/core/navigation.ts';
import { nodeShape, shapesOverlap } from '../src/core/geometry.ts';
import { copyNodes, pasteNodes } from '../src/core/clipboard.ts';
import { createModuleFromSelection, instantiateModule } from '../src/core/modules.ts';
import { createNodeFromSpec } from '../src/core/creation.ts';
import { applyRecipe } from '../src/core/authoring.ts';
import { createPublicationPayload, buildPublication, readPublication } from '../src/publication.ts';

function fixture() {
  const project = newProject('Position lock contract'), canvas = project.canvases[0];
  const a = addNode(project, canvas.id, 'media', 'Anchor'), b = addNode(project, canvas.id, 'media', 'Movable');
  for (const [index, node] of [a, b].entries()) Object.assign(node, { x: index * 300, y: 40, width: 200, height: 120, sizeMode: 'fixed', childPosition: 'manual', placement: 'free', style: { shape: 'rectangle' } });
  return { project, canvas, a, b };
}

test('locks pin coordinates without freezing content or dimensions and explicit unlock allows movement', () => {
  const f = fixture(), original = structuredClone(f.project);
  const locked = setNodePositionLocked(f.project, f.a.id, true), node = locked.nodes.find(node => node.id === f.a.id)!;
  assert.deepEqual(f.project, original); assert.equal(node.positionLocked, true); assert.equal(node.childPosition, 'manual'); assert.equal(node.placement, 'free');
  assert.equal(setNodePositionLocked(locked, node.id, true), locked);
  const content = structuredClone(locked), edit = content.nodes.find(item => item.id === node.id)!;
  edit.text = 'Editable anchored content'; edit.width *= 2; edit.height += 30;
  assert.deepEqual(validateChange(locked, content), []);
  for (const axis of ['x', 'y'] as const) {
    const moved = structuredClone(locked); moved.nodes.find(item => item.id === node.id)![axis] += 1;
    assert.ok(validateChange(locked, moved).some(issue => issue.nodeId === node.id && /Unlock/.test(issue.message)));
  }
  assert.throws(() => moveNodeBranch(locked, node.id, 100, 100), /Unlock/);
  assert.throws(() => moveSelection(locked, [node.id], 50, 30), /Unlock/);
  const unlocked = setNodePositionLocked(locked, node.id, false);
  assert.equal(unlocked.nodes.find(item => item.id === node.id)!.positionLocked, undefined);
  assert.deepEqual(validateChange(locked, moveSelection(unlocked, [node.id], 50, 30)), []);
  assert.throws(() => setNodePositionLocked(f.project, f.canvas.rootId, true), /non-root/);
  assert.throws(() => setNodePositionLocked(f.project, node.id, 'true' as never), /Boolean/);
});

test('movement checks include hidden descendants and transitive locked Cluster peers but not unrelated siblings', () => {
  const f = fixture(), child = addNode(f.project, f.canvas.id, 'media', 'Hidden anchor'); child.parentId = f.a.id; child.positionLocked = true; f.a.collapsed = true;
  assert.equal(canMoveNodeBranches(f.project, [f.a.id]), false); assert.equal(canMoveNodeBranches(f.project, [child.id]), false);
  assert.equal(canMoveNodeBranches(f.project, [f.b.id]), true); assert.equal(canMoveNodeBranches(f.project, [f.a.id, f.b.id]), false);
  f.project.links.push({ id: 'shared-contact', canvasId: f.canvas.id, sourceId: f.a.id, targetId: f.b.id, kind: 'visual', relation: 'generic', direction: 'none', snap: { locked: true } });
  assert.equal(canMoveNodeBranches(f.project, [f.b.id]), false);
  assert.equal(canMoveNodeBranches(f.project, ['missing']), false); assert.equal(canMoveNodeBranches(f.project, [f.canvas.rootId]), false);
});

test('collision repair moves unlocked peers around stationary locked nodes and anchored ancestor branches', () => {
  for (const descendantLock of [false, true]) {
    const f = fixture(); f.b.x = 130;
    if (descendantLock) {
      const child = addNode(f.project, f.canvas.id, 'media', 'Hidden anchored descendant');
      Object.assign(child, { parentId: f.a.id, x: 30, y: 60, positionLocked: true }); f.a.collapsed = true;
    } else f.a.positionLocked = true;
    const before = structuredClone(f.project), repaired = resolveCanvasCollisions(f.project, f.canvas.id, [f.b.id]);
    assert.deepEqual(f.project, before); assert.deepEqual(validateChange(before, repaired), []);
    const a = repaired.nodes.find(node => node.id === f.a.id)!, b = repaired.nodes.find(node => node.id === f.b.id)!, nav = navigationIndex(repaired);
    assert.equal(a.x, f.a.x); assert.equal(a.y, f.a.y); assert.notEqual(b.x, f.b.x);
    assert.equal(shapesOverlap(nodeShape(a, nav.bounds(a)), nodeShape(b, nav.bounds(b))), false);
  }
  const f = fixture(); f.a.positionLocked = f.b.positionLocked = true; f.b.x = 130;
  assert.equal(resolveCanvasCollisions(f.project, f.canvas.id), f.project, 'Conflicting anchors stay pinned until their owner unlocks them.');
});

test('fresh copies and Module instances retain lock flags while receiving independent identities and initial placement', () => {
  const f = fixture(), locked = setNodePositionLocked(f.project, f.a.id, true);
  const paste = pasteNodes(locked, copyNodes(locked, [f.a.id]), f.canvas.id), copied = paste.project.nodes.find(node => node.id === paste.rootIds[0])!;
  assert.notEqual(copied.id, f.a.id); assert.equal(copied.positionLocked, true); assert.notEqual(copied.x, f.a.x);
  assert.deepEqual(validateChange(locked, paste.project), []);
  const module = createModuleFromSelection(locked, [f.a.id], 'Anchored template'), inserted = instantiateModule(locked, module, { canvasId: f.canvas.id, x: 700, y: 50 });
  const anchored = inserted.project.nodes.filter(node => node.positionLocked); assert.equal(anchored.length, 2);
  assert.equal(anchored.find(node => node.id === f.a.id)!.x, f.a.x); assert.deepEqual(validateChange(locked, inserted.project), []);
  const fork = forkProject(locked); assert.equal(fork.nodes.find(node => node.title === f.a.title)!.positionLocked, true);
});

test('recipes and creation position new nodes before locking, and existing anchored recipe updates reject atomically', () => {
  const f = fixture(), created = createNodeFromSpec(f.project, { canvasId: f.canvas.id, parentId: f.canvas.rootId, type: 'media', title: 'New anchored node', x: 650, y: 75, width: 180, height: 120, positionLocked: true });
  assert.equal(created.project.nodes.find(node => node.id === created.nodeId)!.positionLocked, true);
  const recipe = applyRecipe(f.project, { version: 1, nodes: [{ key: 'new', canvas: f.canvas.id, kind: 'media', text: 'New contents', x: 650, y: 50, positionLocked: true }] });
  assert.equal(recipe.project.nodes.find(node => node.id === recipe.aliases.nodes.new)!.positionLocked, true);
  const locked = setNodePositionLocked(f.project, f.a.id, true), before = structuredClone(locked);
  assert.throws(() => applyRecipe(locked, { version: 1, nodes: [{ key: 'existing', existingId: f.a.id, x: 999 }] }), /Unlock/);
  assert.deepEqual(locked, before);
  const unlocked = applyRecipe(locked, { version: 1, nodes: [{ key: 'existing', existingId: f.a.id, positionLocked: false, x: 650 }] });
  assert.equal(unlocked.project.nodes.find(node => node.id === f.a.id)!.x, 650);
});

test('Boolean lock validation and publication retain live and pinned values while malformed changes remain diagnostics', () => {
  const f = fixture(), remote = addCanvas(f.project, 'Pinned observations'), reference = addNode(f.project, remote.id, 'reference', 'Pinned anchor'); reference.targetId = f.a.id;
  const saved = commitSnapshots(setNodePositionLocked(f.project, f.a.id, true));
  const payload = createPublicationPayload(saved); assert.equal(payload.project.nodes.find(node => node.id === f.a.id)!.positionLocked, true);
  const imported = readPublication(buildPublication(saved, '', '')), copy = imported.nodes.find(node => node.title === f.a.title)!;
  assert.equal(imported.snapshots[copy.id].node.positionLocked, true);
  for (const value of ['true', 1, null, {}]) for (const snapshot of [false, true]) {
    const malformed: any = structuredClone(saved);
    (snapshot ? malformed.snapshots[f.a.id].node : malformed.nodes.find((node: any) => node.id === f.a.id)).positionLocked = value;
    assert.equal(isProjectShape(malformed), false); assert.ok(validateProject(malformed).length); assert.ok(validateChange(saved, malformed).length);
  }
});
