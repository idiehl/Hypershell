import assert from 'node:assert/strict';
import test from 'node:test';
import { addCanvas, addNode, commitSnapshots, forkProject, newProject } from '../src/core/model.ts';
import { isProjectShape, validateProject } from '../src/core/rules.ts';
import { applyRecipe } from '../src/core/authoring.ts';
import { buildPublication, readPublication } from '../src/publication.ts';
import type { ContainerVariant } from '../src/core/types.ts';

const variants: ContainerVariant[] = ['spatial', 'contained', 'grid', 'spreadsheet', 'gallery'];
function fixture() {
  const project = newProject('Container variants');
  const consumer = addCanvas(project, 'Saved variants');
  for (const variant of variants) {
    const container = addNode(project, project.entryCanvasId, 'container', variant);
    container.containerVariant = variant;
    addNode(project, consumer.id, 'reference', `Saved ${variant}`).targetId = container.id;
  }
  return commitSnapshots(project);
}
test('container variants retain their identity through snapshots, forks and standalone publication', () => {
  const project = fixture(), fork = forkProject(project), imported = readPublication(buildPublication(project, '', ''));
  for (const candidate of [project, fork, imported]) {
    assert.deepEqual(validateProject(candidate), []);
    for (const variant of variants) {
      const node = candidate.nodes.find(node => node.title === variant)!;
      assert.equal(node.containerVariant, variant);
      assert.equal(candidate.snapshots[node.id].node.containerVariant, variant);
    }
  }
});
test('display patterns reject unsupported values, canvas roots and malformed saved variants while accepting every non-root kind', () => {
  const project = fixture(), first = project.nodes.find(node => node.kind === 'container')!;
  for (const value of ['section', 'custom', 5, null, {}]) {
    const draft = structuredClone(project); draft.nodes.find(node => node.id === first.id)!.containerVariant = value as any;
    assert.equal(isProjectShape(draft), false); assert.ok(validateProject(draft).length);
  }
  for (const kind of ['media', 'text', 'canvas-link', 'image', 'document', 'joint', 'number'] as const) {
    const draft = structuredClone(project); draft.nodes.find(node => node.id === first.id)!.kind = kind;
    const node = draft.nodes.find(node => node.id === first.id)!;
    node.childLayout = 'spatial';
    if (kind === 'joint') node.operation = 'AND';
    assert.equal(isProjectShape(draft), true, kind); assert.deepEqual(validateProject(draft), [], kind);
  }
  const badRoot = structuredClone(project); badRoot.nodes[0].containerVariant = 'grid';
  assert.equal(isProjectShape(badRoot), false); assert.ok(validateProject(badRoot).length);
  const boundedMedia = structuredClone(project); Object.assign(boundedMedia.nodes.find(node => node.id === first.id)!, { kind: 'media', childLayout: 'contained' });
  assert.equal(isProjectShape(boundedMedia), false); assert.ok(validateProject(boundedMedia).length);
  const snapshot = structuredClone(project); snapshot.snapshots[first.id].node.containerVariant = 'custom' as any;
  assert.equal(isProjectShape(snapshot), false);
  for (const kind of ['container', 'module', 'spreadsheet'] as const) {
    const draft = newProject(); addNode(draft, draft.entryCanvasId, kind).containerVariant = 'gallery';
    assert.ok(isProjectShape(draft), kind); assert.deepEqual(validateProject(draft), []);
  }
});
test('declarative authoring accepts bounded and unbounded pattern data and rejects malformed patterns atomically', () => {
  const project = newProject('Authored gallery');
  const recipe = { version: 1 as const, nodes: [{ key: 'gallery', canvas: project.entryCanvasId, kind: 'container' as const, title: 'Gallery', containerVariant: 'gallery' as const }] };
  const result = applyRecipe(project, recipe), gallery = result.project.nodes.find(node => node.id === result.aliases.nodes.gallery)!;
  assert.equal(gallery.containerVariant, 'gallery'); assert.deepEqual(validateProject(result.project), []);
  for (const kind of ['media', 'text'] as const) {
    const unbounded = applyRecipe(project, { ...recipe, nodes: [{ ...recipe.nodes[0], kind }] });
    const parent = unbounded.project.nodes.find(node => node.id === unbounded.aliases.nodes.gallery)!;
    assert.equal(parent.childLayout, 'spatial'); assert.equal(parent.containerVariant, 'gallery'); assert.deepEqual(validateProject(unbounded.project), []);
  }
  assert.throws(() => applyRecipe(project, { ...recipe, nodes: [{ ...recipe.nodes[0], kind: 'media', childLayout: 'contained' }] }));
  assert.throws(() => applyRecipe(project, { ...recipe, nodes: [{ ...recipe.nodes[0], containerVariant: 'custom' as any }] }));
  assert.equal(project.nodes.length, 1);
});
