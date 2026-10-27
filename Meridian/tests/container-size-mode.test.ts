import test from 'node:test';
import assert from 'node:assert/strict';
import { addNode, commitSnapshots, forkProject, newProject } from '../src/core/model.ts';
import { isContainerSizes, rememberContainerSize, transitionContainerSize } from '../src/core/container-size.ts';
import { isRetiredBlockContent } from '../src/core/retired-content.ts';
import { revealChildren } from '../src/core/reveal.ts';
import { isProjectShape, validateProject } from '../src/core/rules.ts';
import { createPublicationPayload } from '../src/publication.ts';
import { copyNodes, pasteNodes } from '../src/core/clipboard.ts';
import { createModule, instantiateModule } from '../src/core/modules.ts';

function fixture() {
  const project = newProject('Independent disclosure frames'), parent = addNode(project, project.entryCanvasId, 'container', 'Collection cover');
  Object.assign(parent, { x: 100, y: 100, width: 600, height: 420, sizeMode: 'fixed', collapsed: false, containerVariant: 'contained', childLayout: 'contained' });
  return { project, parent };
}

test('mode helpers never initialize geometry while reading and ignore ordinary Media parents', () => {
  const { project, parent } = fixture(), before = structuredClone(project);
  assert.equal(parent.containerSizes, undefined); assert.equal(isProjectShape(project), true); assert.deepEqual(project, before);
  const media = addNode(project, project.entryCanvasId, 'media', 'Exterior children'); rememberContainerSize(media);
  assert.equal(media.containerSizes, undefined); transitionContainerSize(media, false);
  assert.equal(media.collapsed, true); assert.equal(media.containerSizes, undefined);
});

test('first collapse seeds a useful cover and repeated transitions restore independent authored dimensions', () => {
  const { project, parent } = fixture(), before = structuredClone(project);
  let current = revealChildren(project, parent.id, false), node = current.nodes.find(node => node.id === parent.id)!;
  assert.deepEqual(project, before); assert.deepEqual(node.containerSizes?.expanded, { width: 600, height: 420, sizeMode: 'fixed' });
  assert.ok(node.width > 0 && node.height > 0 && node.height < 100);
  Object.assign(node, { width: 260, height: 160, sizeMode: 'fixed' }); rememberContainerSize(node);
  for (let cycle = 0; cycle < 5; cycle++) {
    current = revealChildren(current, parent.id, true); node = current.nodes.find(node => node.id === parent.id)!;
    assert.deepEqual([node.width, node.height, node.sizeMode], [600, 420, 'fixed']);
    current = revealChildren(current, parent.id, false); node = current.nodes.find(node => node.id === parent.id)!;
    assert.deepEqual([node.width, node.height, node.sizeMode], [260, 160, 'fixed']);
  }
  assert.deepEqual(validateProject(current), []);
});

test('empty containers keep independent nonzero open and closed frames without synthetic content', () => {
  const { project, parent } = fixture(); parent.title = '';
  const closed = revealChildren(project, parent.id, false), node = closed.nodes.find(node => node.id === parent.id)!;
  assert.deepEqual([node.width, node.height], [200, 120]);
  const opened = revealChildren(closed, parent.id, true), restored = opened.nodes.find(node => node.id === parent.id)!;
  assert.deepEqual([restored.width, restored.height], [600, 420]); assert.equal(restored.title, '');
  const before = structuredClone(opened); revealChildren(opened, parent.id, true); assert.deepEqual(opened, before);
});

test('mode records survive saved snapshots, publication, fork, clipboard and reusable Modules', () => {
  const { project, parent } = fixture();
  parent.containerSizes = { expanded: { width: 600, height: 420, sizeMode: 'fixed' }, collapsed: { width: 260, height: 160, sizeMode: 'fixed' } };
  parent.retiredBlockContent = { title: 'Archived heading', text: 'Saved dormant text', titleRuns: [{ start: 0, end: 8, style: { bold: true } }], contentType: 'text' };
  const expected = structuredClone(parent.containerSizes), archive = structuredClone(parent.retiredBlockContent), saved = commitSnapshots(project);
  assert.deepEqual(saved.snapshots[parent.id]!.node.containerSizes, expected);
  const payload = createPublicationPayload(saved);
  assert.deepEqual(payload.project.nodes.find(node => node.id === parent.id)!.containerSizes, expected);
  assert.deepEqual(payload.project.nodes.find(node => node.id === parent.id)!.retiredBlockContent, archive);
  assert.deepEqual(forkProject(saved).nodes.find(node => node.tag === parent.tag)!.containerSizes, expected);
  const pasted = pasteNodes(saved, copyNodes(saved, [parent.id]), project.entryCanvasId).project.nodes.filter(node => node.kind === 'container');
  assert.equal(pasted.length, 2); for (const node of pasted) { assert.deepEqual(node.containerSizes, expected); assert.deepEqual(node.retiredBlockContent, archive); }
  const module = createModule(saved, parent.id, 'Independent covers'), inserted = instantiateModule(saved, module, { canvasId: project.entryCanvasId, x: 900, y: 100 });
  assert.deepEqual(inserted.project.nodes.find(node => node.id === inserted.rootId)!.containerSizes, expected);
});

test('invalid mode records and legacy archives are rejected in live nodes and saved snapshots atomically', () => {
  const { project, parent } = fixture(), saved = commitSnapshots(project);
  const malformed: unknown[] = [null, [], { expanded: { width: 0, height: 4, sizeMode: 'fixed' } }, { expanded: { width: 4, height: Infinity, sizeMode: 'auto' } }, { expanded: { width: 4, height: 3, sizeMode: 'stretch' } }, { expanded: { width: 4, height: 3 } }, { expanded: { width: 4, height: 3, sizeMode: 'fixed', script: 'x' } }, { other: {} }];
  for (const value of malformed) {
    assert.equal(isContainerSizes(value), false);
    for (const snapshot of [false, true]) {
      const candidate = structuredClone(saved), node = snapshot ? candidate.snapshots[parent.id]!.node : candidate.nodes.find(node => node.id === parent.id)!;
      (node as unknown as Record<string, unknown>).containerSizes = value; assert.equal(isProjectShape(candidate), false); assert.ok(validateProject(candidate).length);
    }
  }
  assert.equal(isRetiredBlockContent({ title: '', text: 'x', textRuns: [{ start: 0, end: 2, style: { bold: true } }] }), false);
  assert.equal(isRetiredBlockContent({ title: '', html: '<script>' }), false);
  const node = structuredClone(parent), before = structuredClone(node);
  assert.throws(() => transitionContainerSize(node, false, { width: 0, height: 2, sizeMode: 'fixed' })); assert.deepEqual(node, before);
});
