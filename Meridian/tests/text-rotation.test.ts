import test from 'node:test';
import assert from 'node:assert/strict';
import { addNode, newProject } from '../src/core/model.ts';
import { galleryObjectLayout, nodeContentLayout, textAdvance } from '../src/core/sizing.ts';
import { rotatedTextEnvelope, textRotation } from '../src/text-rotation.ts';

test('native text envelopes preserve exact cardinal geometry and account for diagonal corners', () => {
  assert.deepEqual(rotatedTextEnvelope(160, 20, 0), { width: 160, height: 20 });
  assert.deepEqual(rotatedTextEnvelope(160, 20, 90), { width: 20, height: 160 });
  assert.deepEqual(rotatedTextEnvelope(160, 20, -90), { width: 20, height: 160 });
  assert.deepEqual(rotatedTextEnvelope(160, 20, 180), { width: 160, height: 20 });
  const diagonal = rotatedTextEnvelope(160, 20, 45);
  assert.ok(Math.abs(diagonal.width - 180 / Math.sqrt(2)) < 1e-10);
  assert.ok(Math.abs(diagonal.width - diagonal.height) < 1e-10);
  assert.equal(textRotation(undefined), 0); assert.equal(textRotation(NaN), 0);
});

test('automatic typography reserves rotated space without changing content or authored geometry', () => {
  const project = newProject('Rotation'), node = addNode(project, project.entryCanvasId, 'media', 'Rotation');
  Object.assign(node, { text: 'Rotating a complete text block', contentType: 'text', sizeMode: 'auto', width: 380,
    style: { fontSize: 18, titleSize: 24, fontFamily: 'calibri' } });
  const plain = nodeContentLayout(project, node), before = structuredClone(node);
  node.style = { ...node.style, titleRotation: 0, textRotation: 0 };
  assert.deepEqual(nodeContentLayout(project, node), plain);
  Object.assign(node.style, { titleRotation: 90, textRotation: 90 });
  const vertical = nodeContentLayout(project, node);
  assert.ok(vertical.width < plain.width, 'A quarter turn creates a narrow reading tile.');
  assert.ok(vertical.height > plain.height * 2, 'The long reading dimension becomes vertical.');
  assert.equal(node.text, before.text); assert.equal(node.width, before.width); assert.equal(node.height, before.height);
  assert.equal(node.style.fontSize, before.style?.fontSize);
  Object.assign(node.style, { titleRotation: -45, textRotation: 45 });
  const diagonal = nodeContentLayout(project, node);
  assert.ok(diagonal.headerHeight > plain.headerHeight); assert.ok(diagonal.bodyHeight > plain.bodyHeight);
});

test('Gallery objects inherit rotations while item overrides and original media envelopes remain independent', () => {
  const project = newProject('Gallery rotation'), parent = addNode(project, project.entryCanvasId, 'container', 'Collection');
  parent.containerVariant = 'gallery'; parent.style = { titleRotation: 90, textRotation: -45, fontFamily: 'cambria' };
  const child = addNode(project, project.entryCanvasId, 'media', 'Object');
  Object.assign(child, { parentId: parent.id, text: 'A complete paragraph in the collection.', contentType: 'text' });
  const inherited = galleryObjectLayout(project, parent, child);
  child.style = { titleRotation: 0, textRotation: 0 };
  const upright = galleryObjectLayout(project, parent, child);
  assert.ok(inherited.headerHeight > upright.headerHeight); assert.ok(inherited.bodyHeight > upright.bodyHeight);
  const bytes = Buffer.alloc(24); Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]).copy(bytes);
  bytes.writeUInt32BE(100, 16); bytes.writeUInt32BE(240, 20);
  project.assets.push({ id: 'image', name: 'Portrait.png', mime: 'image/png', base64: bytes.toString('base64') });
  Object.assign(child, { contentType: 'asset', assetId: 'image', style: { textRotation: 90, titleRotation: 45 } });
  const image = galleryObjectLayout(project, parent, child).media;
  Object.assign(child.style, { textRotation: 0, titleRotation: 0 });
  assert.deepEqual(galleryObjectLayout(project, parent, child).media, image);
});

test('all named monospace choices use mono estimates in non-DOM authoring', () => {
  const mono = textAdvance('iiiWWW', { fontSize: 20, family: 'mono' });
  assert.equal(textAdvance('iiiWWW', { fontSize: 20, family: 'consolas' }), mono);
  assert.equal(textAdvance('iiiWWW', { fontSize: 20, family: 'courier-new' }), mono);
});
