import test from 'node:test';
import assert from 'node:assert/strict';
import { addNode, newProject } from '../src/core/model.ts';
import { intrinsicContentSize, nodeContentLayout, sizingBounds } from '../src/core/sizing.ts';

test('tags never affect content sizing and empty media retains a usable authored frame without a header', () => {
  const project = newProject('Identity and visible content'), node = addNode(project, project.entryCanvasId, 'media', '');
  Object.assign(node, { tag: 'A searchable official identity', title: '', text: '', width: 240, height: 130, sizeMode: 'auto' });
  const empty = nodeContentLayout(project, node);
  assert.equal(empty.headerHeight, 0); assert.equal(empty.bodyHeight, 0);
  assert.deepEqual(intrinsicContentSize(project, node), { width: 240, height: 130 });
  node.tag = 'A much longer official tag. '.repeat(60);
  assert.deepEqual(nodeContentLayout(project, node), empty);
  node.text = 'The actual visible passage.';
  const body = nodeContentLayout(project, node);
  assert.equal(body.headerHeight, 0); assert.ok(body.bodyHeight > 0);
  node.title = 'A visible heading'; assert.ok(nodeContentLayout(project, node).headerHeight > 0);
  Object.assign(node, { title: '', text: '', kind: 'container', containerVariant: 'gallery', collapsed: false });
  assert.deepEqual(sizingBounds(project, node), { id: node.id, x: node.x, y: node.y, width: 240, height: 130 }, 'A completely blank Gallery also retains a usable frame.');
});

test('Block ignores dormant visible content in every disclosure state without losing its child hierarchy', () => {
  const project = newProject('Blank organizational block'), node = addNode(project, project.entryCanvasId, 'block', 'Block identity');
  Object.assign(node, { tag: 'An official Block tag', title: 'Dormant heading', text: 'Dormant text', contentType: 'text', width: 200, height: 120, sizeMode: 'fixed' });
  const child = addNode(project, project.entryCanvasId, 'media', 'Visible child'); child.parentId = node.id;
  const before = structuredClone(project);
  for (const collapsed of [true, false]) {
    node.collapsed = collapsed; const layout = nodeContentLayout(project, node);
    assert.equal(layout.headerHeight, 0); assert.equal(layout.bodyHeight, 0); assert.equal(layout.media, undefined);
    assert.deepEqual(sizingBounds(project, node), { id: node.id, x: node.x, y: node.y, width: 200, height: 120 });
    assert.equal(child.parentId, node.id);
  }
  delete node.collapsed; assert.deepEqual(project, before);
});
