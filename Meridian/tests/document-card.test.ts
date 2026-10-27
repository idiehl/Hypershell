import test from 'node:test';
import assert from 'node:assert/strict';
import { addNode, newProject } from '../src/core/model.ts';
import { nodeContentLayout } from '../src/core/sizing.ts';

for (const [extension, mime] of [['csv', 'text/csv'], ['md', 'text/markdown'], ['txt', 'text/plain'], ['pdf', 'application/pdf']] as const) {
  test(`compact ${extension} document geometry follows its authored title rather than the original filename`, () => {
    const project = newProject('Document presentation');
    const asset = { id: 'original', name: `Index.${extension}`, mime, base64: Buffer.from('Original bytes').toString('base64') };
    project.assets.push(asset);
    const node = addNode(project, project.entryCanvasId, 'document', 'Type Index');
    Object.assign(node, { assetId: asset.id, contentType: 'asset', collapsed: true, width: 500, sizeMode: 'auto', style: { shape: 'rectangle', titleSize: 20, fontSize: 16, padding: 5, contentGap: 5 } });
    const compact = nodeContentLayout(project, node), before = structuredClone(project);
    asset.name = `A considerably longer original source filename which belongs in metadata rather than the Work graph.${extension}`;
    assert.deepEqual(nodeContentLayout(project, node), compact);
    assert(compact.width < 250 && compact.height < 110, 'The title and icon actions should fit an ordinary compact reading tile.');
    assert.equal(project.assets[0]?.base64, before.assets[0]?.base64);
    assert.equal(project.assets[0]?.mime, before.assets[0]?.mime);
    assert.equal(node.assetId, before.nodes.find(item => item.id === node.id)?.assetId);
  });
}
