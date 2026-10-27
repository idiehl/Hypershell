import test from 'node:test';
import assert from 'node:assert/strict';
import { addCanvas, addNode, commitSnapshots, forkProject, newProject } from '../src/core/model.ts';
import { activeNodeContent, isNodeContentItems, nodeContentItems, projectNodeContent, withNodeContentItems } from '../src/core/content.ts';
import { isProjectShape, validateProject } from '../src/core/rules.ts';
import { createModule, instantiateModule } from '../src/core/modules.ts';
import { createSnippet } from '../src/core/selection.ts';
import { applyRecipe, inventoryProject } from '../src/core/authoring.ts';
import { createProjectSearch } from '../src/core/navigation.ts';
import { copyNodes, pasteNodes } from '../src/core/clipboard.ts';
import { buildPublication, createPublicationPayload, readPublication } from '../src/publication.ts';
import type { NodeContentItem } from '../src/core/types.ts';

function fixture() {
  const p = newProject('Hub test'); const n = addNode(p, p.entryCanvasId, 'media', 'Zeus');
  p.assets = ['old', 'portrait', 'reader'].map(id => ({ id, name: id + '.md', mime: 'text/markdown', base64: btoa(id + ' retained content') }));
  n.assetId = 'old'; n.text = 'Dormant original prose'; n.hyperlinks = [{ url: 'https://example.com/legacy' }];
  n.contentItems = [{ id: 'portrait-slot', type: 'asset', assetId: 'portrait' }, { id: 'reader-slot', type: 'asset', assetId: 'reader' }, { id: 'story-slot', type: 'text', text: 'Supplemental thunderbolt story' }, { id: 'source-slot', type: 'link', url: 'https://example.com/source', label: 'Source' }];
  return { p, n };
}

test('legacy Hub projection preserves original fields and blank nodes have no synthetic content', () => {
  const p = newProject(); const n = addNode(p, p.entryCanvasId, 'media', 'Blank');
  assert.deepEqual(nodeContentItems(n), []);
  n.text = 'Caption'; n.assetId = 'picture'; n.attachments = ['picture', 'notes']; n.hyperlinks = [{ url: 'https://example.com/' }]; n.contentType = 'asset';
  const before = structuredClone(n), items = nodeContentItems(n);
  assert.deepEqual(items.map(item => item.type), ['asset', 'text', 'asset', 'link']);
  assert.equal(items[0].type === 'asset' && items[0].assetId, 'picture');
  assert.deepEqual(n, before); assert.equal(n.contentItems, undefined);
  const legacySnapshot = commitSnapshots(p).snapshots[n.id]; assert.equal(legacySnapshot.assets, undefined); assert.equal(legacySnapshot.node.contentItems, undefined);
  const edited = withNodeContentItems(n, [items[3], ...items.slice(0, 3)]);
  assert.deepEqual(edited.text, n.text); assert.deepEqual(edited.attachments, n.attachments); assert.deepEqual(activeNodeContent(edited), { type: 'link', hyperlink: { url: 'https://example.com/' } });
});

test('only first Hub item is projected; hiding title and clearing stack retain all canonical data', () => {
  const { n } = fixture(); n.showTitle = false;
  const projected = projectNodeContent(n);
  assert.equal(projected.title, ''); assert.equal(n.title, 'Zeus'); assert.equal(projected.assetId, 'portrait'); assert.equal(projected.text, undefined); assert.equal(projected.hyperlinks, undefined);
  const reordered = withNodeContentItems(n, [n.contentItems![2], ...n.contentItems!.filter((_, i) => i !== 2)]);
  assert.equal(activeNodeContent(reordered).text, 'Supplemental thunderbolt story'); assert.equal(projectNodeContent(reordered).assetId, undefined);
  assert.deepEqual(activeNodeContent(withNodeContentItems(n, [])), { type: 'text', text: '' }); assert.equal(n.assetId, 'old');
});

test('Hub validation rejects duplicate identities, unsafe links, active fields and invalid rich text', () => {
  assert.equal(isNodeContentItems([{ id: 'x', type: 'link', url: 'javascript:alert(1)' }]), false);
  assert.equal(isNodeContentItems([{ id: 'x', type: 'asset', assetId: 'a', html: '<script>' }]), false);
  assert.equal(isNodeContentItems([{ id: 'x', type: 'text', text: 'a' }, { id: 'x', type: 'text', text: 'b' }]), false);
  assert.equal(isNodeContentItems([{ id: 'x', type: 'text', text: 'a', textRuns: [{ start: 0, end: 8, style: { bold: true } }] }]), false);
  const { p, n } = fixture(); (n as unknown as Record<string, unknown>).showTitle = 'yes'; assert.equal(isProjectShape(p), false);
});

test('saved Hub observations retain active and secondary assets through fork and publication', () => {
  const { p, n } = fixture(); n.showTitle = false;
  const other = addCanvas(p, 'Observer'); const reference = addNode(p, other.id, 'reference', 'Zeus observation'); reference.targetId = n.id;
  const saved = commitSnapshots(p), observed = saved.snapshots[n.id];
  assert.equal(observed.asset?.id, 'portrait'); assert.deepEqual(observed.assets?.map(a => a.id).sort(), ['old', 'reader']); assert.deepEqual(validateProject(saved), []);
  const copied = forkProject(saved), copiedNode = copied.nodes.find(x => x.title === 'Zeus')!;
  const ids = new Set(copied.assets.map(a => a.id));
  assert.ok(copiedNode.contentItems!.every(item => item.type !== 'asset' || ids.has(item.assetId)));
  assert.notEqual(copiedNode.contentItems![0].id, n.contentItems![0].id); assert.equal(copiedNode.showTitle, false); assert.deepEqual(validateProject(copied), []);
  const payload = createPublicationPayload(saved); assert.equal(payload.project.nodes.find(x => x.id === n.id)?.contentItems?.length, 4); assert.equal(payload.project.snapshots[n.id].assets?.length, 2);
  const imported = readPublication(buildPublication(saved, '', '')); assert.deepEqual(validateProject(imported), []); assert.equal(imported.nodes.find(x => x.title === 'Zeus')?.contentItems?.length, 4);
});

test('Module copies retain every Hub item and remap all used attachment identities', () => {
  const { p, n } = fixture(); const definition = createModule(p, n.id, 'Zeus kit');
  const dest = newProject('Target'); const inserted = instantiateModule(dest, definition, { canvasId: dest.entryCanvasId, x: 900, y: 900 }).project;
  const node = inserted.nodes.find(x => x.title === 'Zeus')!, ids = new Set(inserted.assets.map(a => a.id));
  assert.equal(node.contentItems?.length, 4); assert.ok(node.contentItems!.filter(x => x.type === 'asset').every(x => ids.has(x.assetId)));
  assert.ok(!inserted.assets.some(a => ['old', 'portrait', 'reader'].includes(a.id))); assert.deepEqual(validateProject(inserted), []);
});

test('external selected-copy observations preserve every Hub asset without reconnecting original graph', () => {
  const { p, n } = fixture(); const other = addCanvas(p, 'Observer'); const ref = addNode(p, other.id, 'reference', 'Zeus reference'); ref.targetId = n.id;
  const saved = commitSnapshots(p); const snippet = createSnippet(saved, [ref.id], { includeDescendants: true, includeExternalReferences: true });
  const observation = snippet.project.snapshots[n.id]; assert.ok(observation); assert.equal(observation.assets?.length, 2);
  const ids = new Set([observation.asset!.id, ...observation.assets!.map(a => a.id)]);
  assert.ok(observation.node.contentItems!.filter(x => x.type === 'asset').every(x => ids.has(x.assetId))); assert.deepEqual(validateProject(snippet.project), []);
  assert.ok(!snippet.project.nodes.some(x => x.id === n.id));
});

test('recipes resolve Hub asset aliases and inventory/search include supplemental hidden content', () => {
  const p = newProject('Recipe Hub');
  const result = applyRecipe(p, { version: 1, assets: [{ key: 'portrait', name: 'Zeus.txt', mime: 'text/plain', base64: btoa('Asset body') }], nodes: [{ key: 'zeus', canvas: p.entryCanvasId, kind: 'media', title: 'Zeus', showTitle: false, contentItems: [{ id: 'picture', type: 'asset', assetId: 'portrait' }, { id: 'notes', type: 'text', text: 'Rare supplemental thunderbolt anecdote' }] }] });
  const node = result.project.nodes.find(x => x.id === result.aliases.nodes.zeus)!;
  assert.equal(node.contentItems![0].type === 'asset' && node.contentItems![0].assetId, result.aliases.assets.portrait);
  assert.equal(inventoryProject(result.project).nodes[0].showTitle, false);
  assert.ok(createProjectSearch(result.project)('thunderbolt').some(x => x.nodeId === node.id));
  const reset = applyRecipe(result.project, { version: 1, bindings: result.aliases, nodes: [{ key: 'zeus', unset: ['contentItems', 'showTitle'] }] }).project;
  assert.equal(reset.nodes.find(x => x.id === node.id)?.contentItems, undefined);
});

test('clipboard copy/paste keeps secondary Hub media and external observation bytes independent', () => {
  const { p, n } = fixture(); const other = addCanvas(p, 'Observer'); const ref = addNode(p, other.id, 'reference', 'Zeus reference'); ref.targetId = n.id;
  const saved = commitSnapshots(p), clipboard = copyNodes(saved, [ref.id]);
  const destination = newProject('Paste target'); const pasted = pasteNodes(destination, clipboard, destination.entryCanvasId).project;
  assert.equal(pasted.snapshots[n.id].assets?.length, 2); assert.equal(pasted.snapshots[n.id].node.contentItems?.length, 4);
  assert.deepEqual(validateProject(pasted), []);
  const allAssets = new Set(pasted.assets.map(a => a.id)); assert.ok(pasted.snapshots[n.id].node.contentItems!.every(item => item.type !== 'asset' || allAssets.has(item.assetId)));
  const direct = pasteNodes(destination, copyNodes(saved, [n.id]), destination.entryCanvasId).project;
  assert.equal(direct.nodes.find(x => x.title === 'Zeus')?.contentItems?.length, 4); assert.deepEqual(validateProject(direct), []);
});
