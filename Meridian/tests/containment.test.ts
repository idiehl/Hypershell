import test from 'node:test';
import assert from 'node:assert/strict';
import { addCanvas, addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import { moveNodeBranch, reparentNodeBranch, revealNodeBranch, setNodeCollapsed } from '../src/core/containment.ts';
import { navigationIndex } from '../src/core/navigation.ts';
import { evaluate } from '../src/core/engine.ts';
import { validateProject } from '../src/core/rules.ts';
import { createPublicationPayload } from '../src/publication.ts';
import type { MeridianProject } from '../src/core/types.ts';

function fixture() {
  const project = newProject('Nested canvas'), canvas = project.entryCanvasId;
  const parent = addNode(project, canvas, 'text', 'Research');
  Object.assign(parent, { sizeMode: 'fixed', width: 900, height: 700, text: 'A short summary of the research.' });
  const chapter = addNode(project, canvas, 'canvas-link', 'Chapter');
  const destination = addCanvas(project, 'Chapter workspace'); chapter.targetId = destination.rootId;
  Object.assign(chapter, { sizeMode: 'fixed', parentId: parent.id, x: 180, y: 240, width: 520, height: 400, collapsed: true });
  const input = addNode(project, canvas, 'input', 'Source'); Object.assign(input, { parentId: chapter.id, value: true, x: 220, y: 320 });
  const note = addNode(project, canvas, 'text', 'Evidence'); Object.assign(note, { parentId: chapter.id, x: 440, y: 320 });
  const output = addNode(project, canvas, 'output', 'Result'); Object.assign(output, { x: 1100, y: 100 });
  project.links.push({ id: 'internal', canvasId: canvas, sourceId: input.id, targetId: note.id, kind: 'visual', relation: 'causal', points: [{ x: 380, y: 270 }] });
  project.links.push({ id: 'external', canvasId: canvas, sourceId: input.id, targetId: output.id, kind: 'functional', relation: 'causal', points: [{ x: 980, y: 340 }] });
  return { project, parent, chapter, input, note, output, destination };
}

test('collapse hides an ordinary fixed parent\'s descendants while retaining its own authored surface and computation', () => {
  const f = fixture(), before = structuredClone(f.project);
  const closed = setNodeCollapsed(f.project, f.parent.id, true), index = navigationIndex(closed);
  assert.equal(index.visibleNode(f.input.id)?.id, f.parent.id);
  assert.equal(index.bounds(index.nodes.get(f.parent.id)!).width, 900);
  assert.equal(index.bounds(index.nodes.get(f.parent.id)!).height, 700);
  assert.equal(index.nodes.get(f.parent.id)!.width, 900);
  assert.equal(index.nodes.get(f.chapter.id)!.collapsed, true);
  assert.equal(evaluate(closed).values[f.output.id], true);
  assert.deepEqual(closed.links, before.links);
  assert.deepEqual(f.project, before);
  const reopened = setNodeCollapsed(closed, f.parent.id, false), reopenedIndex = navigationIndex(reopened);
  assert.equal(reopenedIndex.visibleNode(f.input.id)?.id, f.chapter.id);
  assert.equal(reopenedIndex.bounds(reopenedIndex.nodes.get(f.parent.id)!).width, 900);
});

test('reveal opens exactly the ancestor chain, preserving the revealed node and sibling branches', () => {
  const f = fixture(); f.parent.collapsed = true;
  const sibling = addNode(f.project, f.project.entryCanvasId, 'text', 'Other branch'); sibling.collapsed = true;
  const revealed = revealNodeBranch(f.project, f.chapter.id), index = navigationIndex(revealed);
  assert.equal(index.nodes.get(f.parent.id)?.collapsed, false);
  assert.equal(index.nodes.get(f.chapter.id)?.collapsed, true);
  assert.equal(index.nodes.get(sibling.id)?.collapsed, true);
  assert.equal(index.visibleNode(f.chapter.id)?.id, f.chapter.id);
  assert.equal(index.visibleNode(f.input.id)?.id, f.chapter.id);
});

test('reparent accepts ordinary and canvas-link parents while rejecting cycles, roots, cross-canvas and inherited violations atomically', () => {
  const f = fixture(), before = structuredClone(f.project);
  const moved = reparentNodeBranch(f.project, f.output.id, f.chapter.id);
  assert.equal(moved.nodes.find(node => node.id === f.output.id)?.parentId, f.chapter.id);
  const detached = reparentNodeBranch(moved, f.output.id, null);
  assert.equal(detached.nodes.find(node => node.id === f.output.id)?.parentId, f.project.canvases[0].rootId);
  assert.throws(() => reparentNodeBranch(f.project, f.parent.id, f.input.id), /cycle/);
  assert.throws(() => reparentNodeBranch(f.project, f.parent.id, f.parent.id), /cycle/);
  assert.throws(() => reparentNodeBranch(f.project, f.project.canvases[0].rootId, f.chapter.id), /roots/);
  assert.throws(() => reparentNodeBranch(f.project, f.input.id, f.destination.rootId), /another Work/);
  assert.deepEqual(f.project, before);
  const restricted = addNode(f.project, f.project.entryCanvasId, 'text', 'Text only'); restricted.rules = { nodeKinds: ['text'] };
  const restrictedBefore = structuredClone(f.project);
  assert.throws(() => reparentNodeBranch(f.project, f.chapter.id, restricted.id), /inherited/);
  assert.deepEqual(f.project, restrictedBefore);
});

test('branch moves translate hidden descendants and internal route points without moving external routes', () => {
  const f = fixture(), before = structuredClone(f.project);
  const moved = moveNodeBranch(f.project, f.chapter.id, f.chapter.x + 73, f.chapter.y - 41), index = navigationIndex(moved);
  for (const original of [f.chapter, f.input, f.note]) {
    assert.equal(index.nodes.get(original.id)?.x, original.x + 73);
    assert.equal(index.nodes.get(original.id)?.y, original.y - 41);
  }
  assert.deepEqual(moved.links.find(link => link.id === 'internal')?.points, [{ x: 453, y: 229 }]);
  assert.deepEqual(moved.links.find(link => link.id === 'external')?.points, before.links[1].points);
  assert.deepEqual(index.nodes.get(f.output.id), f.output);
  assert.deepEqual(f.project, before);
  assert.deepEqual(validateProject(moved), []);
  assert.throws(() => moveNodeBranch(f.project, f.chapter.id, NaN, 0), /finite/);
});

test('save snapshots and portable publication preserve nested state, canvas targets and graph', () => {
  const f = fixture(); const closed = setNodeCollapsed(f.project, f.parent.id, true);
  const saved = commitSnapshots(closed), reopened: MeridianProject = JSON.parse(JSON.stringify(saved));
  const published = createPublicationPayload(reopened).project;
  for (const project of [saved, reopened, published]) {
    assert.equal(project.nodes.find(node => node.id === f.parent.id)?.collapsed, true);
    assert.equal(project.nodes.find(node => node.id === f.chapter.id)?.parentId, f.parent.id);
    assert.equal(project.nodes.find(node => node.id === f.chapter.id)?.targetId, f.destination.rootId);
    assert.equal(project.nodes.find(node => node.id === f.chapter.id)?.collapsed, true);
    assert.equal(evaluate(project).values[f.output.id], true);
    assert.deepEqual(project.links, f.project.links);
  }
  assert.equal(saved.snapshots[f.input.id].value, true);
  assert.equal(saved.snapshots[f.chapter.id].node.collapsed, true);
});

test('missing nodes and invalid state reject without changing the source', () => {
  const f = fixture(), before = structuredClone(f.project);
  assert.throws(() => setNodeCollapsed(f.project, 'missing', true), /does not exist/);
  assert.throws(() => revealNodeBranch(f.project, 'missing'), /does not exist/);
  assert.throws(() => moveNodeBranch(f.project, 'missing', 1, 2), /does not exist/);
  assert.throws(() => reparentNodeBranch(f.project, f.input.id, 'missing'), /does not exist/);
  assert.throws(() => setNodeCollapsed(f.project, f.parent.id, 'yes' as unknown as boolean), /Boolean/);
  assert.deepEqual(f.project, before);
});
