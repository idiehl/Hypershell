import assert from 'node:assert/strict';
import test from 'node:test';
import { addCanvas, addNode, commitSnapshots, forkProject, newProject } from '../src/core/model.ts';
import { isCanvasTarget, isProjectShape, validateProject } from '../src/core/rules.ts';
import { applyRecipe } from '../src/core/authoring.ts';
import { buildPublication, createPublicationPayload, readPublication } from '../src/publication.ts';

function fixture() {
  const project = newProject('Ordinary canvas destinations');
  const launcher = addNode(project, project.entryCanvasId, 'canvas-link', 'Saved canvas');
  // Destinations deliberately collide with included IDs: a fork must still open
  // the original saved workspace, rather than accidentally redirect to itself.
  launcher.canvasTarget = { projectId: project.id, canvasId: project.entryCanvasId };
  const consumer = addCanvas(project, 'Pinned launcher');
  addNode(project, consumer.id, 'reference', 'Saved launcher snapshot').targetId = launcher.id;
  return { project: commitSnapshots(project), launcherId: launcher.id };
}

test('external canvas destinations use strict bounded identifier data and only belong to external canvas launchers', () => {
  assert.ok(isCanvasTarget({ projectId: 'saved_project-1' }));
  assert.ok(isCanvasTarget({ projectId: 'a'.repeat(96), canvasId: 'canvas_2' }));
  for (const invalid of [null, [], '', {}, { projectId: '' }, { projectId: 'a'.repeat(97) }, { projectId: '../escape' },
    { projectId: 'https://example.test' }, { projectId: 'safe\n' }, { projectId: ' safe' }, { projectId: 4 },
    { projectId: 'safe', canvasId: null }, { projectId: 'safe', canvasId: '' }, { projectId: 'safe', canvasId: 'a/b' },
    { projectId: 'safe', url: 'file:///private' }]) assert.equal(isCanvasTarget(invalid), false, JSON.stringify(invalid));
  const { project, launcherId } = fixture();
  assert.ok(isProjectShape(project)); assert.deepEqual(validateProject(project), []);
  for (const mutate of [
    (draft: typeof project) => { draft.nodes.find(node => node.id === launcherId)!.kind = 'text'; },
    (draft: typeof project) => { draft.nodes.find(node => node.id === launcherId)!.targetId = project.canvases[0].rootId; },
    (draft: typeof project) => { draft.snapshots[launcherId].node.kind = 'text'; },
    (draft: typeof project) => { draft.snapshots[launcherId].node.targetId = project.canvases[0].rootId; },
  ]) {
    const draft = structuredClone(project); mutate(draft);
    assert.equal(isProjectShape(draft), false); assert.ok(validateProject(draft).length);
  }
});

test('forks and saved snapshots preserve original external destinations even when IDs overlap the included graph', () => {
  const { project, launcherId } = fixture(), destination = project.nodes.find(node => node.id === launcherId)!.canvasTarget;
  const fork = forkProject(project), launcher = fork.nodes.find(node => node.title === 'Saved canvas')!;
  assert.notEqual(fork.id, project.id); assert.notEqual(fork.entryCanvasId, project.entryCanvasId);
  assert.deepEqual(launcher.canvasTarget, destination);
  assert.deepEqual(fork.snapshots[launcher.id].node.canvasTarget, destination);
  launcher.canvasTarget!.projectId = 'separate_destination';
  assert.deepEqual(project.snapshots[launcherId].node.canvasTarget, destination);
  assert.deepEqual(project.nodes.find(node => node.id === launcherId)!.canvasTarget, destination);
});

test('portable publication and import keep ordinary external launcher destinations and strip unknown target metadata', () => {
  const { project, launcherId } = fixture(), destination = project.nodes.find(node => node.id === launcherId)!.canvasTarget;
  const payload = createPublicationPayload(project);
  assert.deepEqual(payload.project.nodes.find(node => node.id === launcherId)!.canvasTarget, destination);
  assert.deepEqual(payload.project.snapshots[launcherId].node.canvasTarget, destination);
  Object.assign(project.nodes.find(node => node.id === launcherId)!.canvasTarget!, { url: 'file:///private/library', auth: 'secret' });
  Object.assign(project.snapshots[launcherId].node.canvasTarget!, { url: 'file:///private/snapshot' });
  const html = buildPublication(project, '', '');
  assert.ok(!html.includes('file:///private')); assert.ok(!html.includes('secret'));
  const imported = readPublication(html), launcher = imported.nodes.find(node => node.title === 'Saved canvas')!;
  assert.deepEqual(launcher.canvasTarget, { projectId: project.id, canvasId: project.entryCanvasId });
  assert.deepEqual(imported.snapshots[launcher.id].node.canvasTarget, launcher.canvasTarget);
  assert.deepEqual(validateProject(imported), []);
});

test('declarative authoring creates external canvas launchers without a special library node type', () => {
  const project = newProject('Recipe destinations');
  const result = applyRecipe(project, { version: 1, nodes: [{ key: 'external', canvas: project.entryCanvasId, kind: 'canvas-link', title: 'Other saved canvas', canvasTarget: { projectId: 'saved-workspace', canvasId: 'saved-canvas' } }] });
  const launcher = result.project.nodes.find(node => node.id === result.aliases.nodes.external)!;
  assert.equal(launcher.kind, 'canvas-link'); assert.equal(launcher.targetId, undefined);
  assert.deepEqual(launcher.canvasTarget, { projectId: 'saved-workspace', canvasId: 'saved-canvas' });
  assert.deepEqual(validateProject(result.project), []);
  assert.equal(project.nodes.length, 1);
});
