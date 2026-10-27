import assert from 'node:assert/strict';
import test from 'node:test';
import { isCanvasStyle, isNodeStyle } from '../src/core/appearance.ts';
import { applyRecipe, type AuthoringRecipe } from '../src/core/authoring.ts';
import { copyNodes, pasteNodes } from '../src/core/clipboard.ts';
import { addCanvas, addNode, commitSnapshots, forkProject, newProject } from '../src/core/model.ts';
import { createModuleFromSelection, instantiateModule } from '../src/core/modules.ts';
import { isProjectShape, validateProject } from '../src/core/rules.ts';
import { buildPublication, createPublicationPayload, readPublication } from '../src/publication.ts';

const invalid = ['', 'never', 'hidden', 'HOVER', 'url(external)', 0, false, null, [], {}];

function fixture() {
  const project = newProject('Hover background portability'); project.canvases[0].style = { portalStyle: 'passage' };
  const nodes = ([undefined, 'always', 'hover'] as const).map((backgroundDisplay, index) => {
    const node = addNode(project, project.entryCanvasId, 'media', backgroundDisplay ?? 'Implicit default');
    Object.assign(node, { x: 60 + index * 300, y: 80, width: 240, height: 180, sizeMode: 'fixed', text: 'Content remains visible.',
      style: { fill: '#123456', border: '#89ABCD', backgroundPattern: 'grid', portalStyle: 'vortex', ...(backgroundDisplay ? { backgroundDisplay } : {}) } });
    return node;
  });
  const pinned = addCanvas(project, 'Saved backgrounds');
  for (const node of nodes) { const reference = addNode(project, pinned.id, 'reference', `Pinned ${node.title}`); reference.targetId = node.id; }
  return { project: commitSnapshots(project), nodes };
}

test('background display is optional node-only data with strict always and hover choices', () => {
  assert.ok(isNodeStyle(undefined)); assert.ok(isNodeStyle({}));
  for (const backgroundDisplay of ['always', 'hover']) {
    assert.ok(isNodeStyle({ backgroundDisplay, fill: '#123456', backgroundPattern: 'dots', portalStyle: 'doorway' }));
    assert.equal(isCanvasStyle({ backgroundDisplay }), false, 'Workspace appearance cannot hide its background on hover.');
  }
  for (const backgroundDisplay of invalid) assert.equal(isNodeStyle({ backgroundDisplay }), false);
  assert.equal(isNodeStyle({ backgroundDisplay: 'hover', backgroundTrigger: 'click' }), false);
  const { project, nodes } = fixture(); assert.deepEqual(validateProject(project), []);
  assert.equal(nodes[0].style!.backgroundDisplay, undefined);
  for (const backgroundDisplay of invalid) for (const pinned of [false, true]) {
    const bad = structuredClone(project), node = pinned ? bad.snapshots[nodes[2].id].node : bad.nodes.find(node => node.id === nodes[2].id)!;
    node.style = { ...node.style, backgroundDisplay } as typeof node.style;
    assert.equal(isProjectShape(bad), false); assert.ok(validateProject(bad).length);
  }
  for (const backgroundDisplay of ['always', 'hover']) {
    const bad = structuredClone(project); Object.assign(bad.canvases[0].style!, { backgroundDisplay }); assert.equal(isProjectShape(bad), false);
  }
});

test('hover backgrounds and explicit always survive snapshots, publication, forks, clipboard and Modules', () => {
  const { project, nodes } = fixture(), before = JSON.stringify(project);
  for (const portable of [createPublicationPayload(project).project, readPublication(buildPublication(project, '', '')), forkProject(project)]) {
    for (const source of nodes) {
      const copy = portable.nodes.find(node => node.tag === source.tag)!;
      assert.deepEqual(copy.style, source.style); assert.deepEqual(portable.snapshots[copy.id].node.style, source.style);
      assert.equal(copy.text, source.text);
    }
    assert.equal(portable.canvases[0].style!.portalStyle, 'passage'); assert.deepEqual(validateProject(portable), []);
  }
  const destination = newProject('Pasted backgrounds'), pasted = pasteNodes(destination, copyNodes(project, nodes.map(node => node.id)), destination.entryCanvasId);
  for (const source of nodes) assert.deepEqual(pasted.project.nodes.find(node => node.id === pasted.idMap[source.id])!.style, source.style);
  const module = createModuleFromSelection(project, nodes.map(node => node.id), 'Hover backgrounds');
  const inserted = instantiateModule(destination, module, { canvasId: destination.entryCanvasId, x: 80, y: 80 });
  for (const source of nodes) {
    const stored = module.project.nodes.find(node => node.tag === source.tag)!;
    assert.deepEqual(stored.style, source.style); assert.deepEqual(inserted.project.nodes.find(node => node.id === inserted.idMap[stored.id])!.style, source.style);
  }
  assert.equal(JSON.stringify(project), before);
});

test('declarative background visibility preserves other appearance and rejects invalid node and workspace values atomically', () => {
  const { project, nodes } = fixture(), before = JSON.stringify(project);
  const recipe: AuthoringRecipe = { version: 1, nodes: [{ key: 'node', existingId: nodes[0].id, style: { backgroundDisplay: 'hover' } }] };
  const updated = applyRecipe(project, recipe).project.nodes.find(node => node.id === nodes[0].id)!;
  assert.deepEqual(updated.style, { ...nodes[0].style, backgroundDisplay: 'hover' });
  for (const backgroundDisplay of invalid) {
    const bad = structuredClone(recipe); Object.assign(bad.nodes![0].style!, { backgroundDisplay });
    assert.throws(() => applyRecipe(project, bad), /appearance/);
  }
  assert.throws(() => applyRecipe(project, { version: 1, canvases: [{ key: 'canvas', existingId: project.entryCanvasId, style: { backgroundDisplay: 'hover' } }] } as unknown as AuthoringRecipe), /appearance/);
  assert.equal(JSON.stringify(project), before);
});
