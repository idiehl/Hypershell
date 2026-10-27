import test from 'node:test';
import assert from 'node:assert/strict';
import { executeWorker } from '../src/authoring-worker.mjs';
import { newProject } from '../../../src/core/model.ts';

test('authoring schema exposes the portable Node Hub and applies its asset aliases without losing order', async () => {
  const schema = await executeWorker({ command: 'schema' });
  assert(schema.nodeFields.includes('showTitle')); assert(schema.nodeFields.includes('contentItems'));
  assert.equal(schema.nodeHub.itemTypes.length, 3); assert.match(schema.nodeHub.firstSlot, /contentItems\[0\]/);
  const project = newProject('Hub companion fixture');
  const result = await executeWorker({ command: 'apply', project, recipe: { version: 1,
    assets: [{ key: 'image', name: 'notes.txt', mime: 'text/plain', base64: btoa('Original attachment') }],
    nodes: [{ key: 'subject', canvas: project.entryCanvasId, kind: 'media', title: 'Zeus', showTitle: false, contentItems: [{ id: 'summary', type: 'text', text: 'The visible subject' }, { id: 'reading', type: 'asset', assetId: 'image' }] }],
  } });
  const node = result.project.nodes.find(node => node.id === result.aliases.nodes.subject);
  assert.equal(node.showTitle, false); assert.equal(node.contentItems[0].text, 'The visible subject'); assert.equal(node.contentItems[1].assetId, result.aliases.assets.image);
  assert.equal(project.nodes.length, 1, 'Worker composition must preserve its source.');
});
