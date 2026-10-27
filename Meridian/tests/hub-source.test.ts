import test from 'node:test';
import assert from 'node:assert/strict';
import { addCanvas, addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import { applyHubContent, resolveHubView } from '../src/core/hub-source.ts';
import { validateProject } from '../src/core/rules.ts';

function fixture() {
  const p = newProject('Reference Hub'); const source = addNode(p, p.entryCanvasId, 'media', 'Zeus source');
  source.contentItems = [{ id: 'portrait', type: 'asset', assetId: 'portrait-bytes' }, { id: 'story', type: 'text', text: 'Saved Zeus story' }, { id: 'notes', type: 'asset', assetId: 'notes-bytes' }];
  p.assets = [{ id: 'portrait-bytes', name: 'portrait.txt', mime: 'text/plain', base64: btoa('Pinned portrait') }, { id: 'notes-bytes', name: 'notes.md', mime: 'text/markdown', base64: btoa('Pinned notes') }];
  const same = addNode(p, p.entryCanvasId, 'reference', 'Local title'); same.targetId = source.id;
  const other = addCanvas(p, 'Observer'); const external = addNode(p, other.id, 'reference', 'Observed title'); external.targetId = source.id; external.valueType = 'number'; external.exposed = true; external.showTitle = false;
  return { p, source, same, external };
}

test('Reference Hub inherits the same-canvas stack with its own title without migrating the source or Reference', () => {
  const { p, source, same } = fixture(); const before = JSON.stringify(p), view = resolveHubView(p, same);
  assert.equal(view.inherited, true); assert.equal(view.node.id, same.id); assert.equal(view.node.title, 'Local title'); assert.deepEqual(view.node.contentItems, source.contentItems);
  assert.equal(same.contentItems, undefined); assert.equal(JSON.stringify(p), before);
  view.node.contentItems![1] = { id: 'view-only', type: 'text', text: 'Preview change' };
  assert.equal(source.contentItems![1].id, 'story', 'Preview item arrays must not alias the source.');
});

test('saved Reference Hub previews pinned media over newer colliding local asset bytes', () => {
  const { p, source, external } = fixture(); const saved = commitSnapshots(p);
  saved.assets.find(asset => asset.id === 'portrait-bytes')!.base64 = btoa('Newer local portrait');
  saved.nodes.find(node => node.id === source.id)!.contentItems![1] = { id: 'story', type: 'text', text: 'Unsaved source change' };
  const before = JSON.stringify(saved), ref = saved.nodes.find(node => node.id === external.id)!, view = resolveHubView(saved, ref);
  assert.equal(view.inherited, true); assert.equal(view.node.title, 'Observed title'); assert.equal(view.node.showTitle, false);
  assert.equal(view.node.contentItems![1].type === 'text' && view.node.contentItems![1].text, 'Saved Zeus story');
  assert.equal(view.project.assets.find(asset => asset.id === 'portrait-bytes')!.base64, btoa('Pinned portrait'));
  assert.equal(JSON.stringify(saved), before);
});

test('first explicit Reference edit creates a local override, materializes only used pinned assets and preserves the original source', () => {
  const { p, source, external } = fixture(); const saved = commitSnapshots(p);
  saved.assets.find(asset => asset.id === 'portrait-bytes')!.base64 = btoa('Newer source bytes');
  const ref = saved.nodes.find(node => node.id === external.id)!, before = structuredClone(saved), view = resolveHubView(saved, ref);
  const items = [view.node.contentItems![1], view.node.contentItems![0]];
  const result = applyHubContent(saved, ref.id, items, [], view), edited = result.nodes.find(node => node.id === ref.id)!;
  assert.equal(edited.contentItems![0].type, 'text'); assert.equal(edited.targetId, ref.targetId); assert.equal(edited.valueType, 'number'); assert.equal(edited.exposed, true); assert.equal(edited.showTitle, false);
  const assetItem = edited.contentItems![1]; assert.equal(assetItem.type, 'asset'); if (assetItem.type !== 'asset') return;
  assert.notEqual(assetItem.assetId, 'portrait-bytes'); assert.equal(result.assets.find(asset => asset.id === assetItem.assetId)!.base64, btoa('Pinned portrait'));
  assert.equal(result.assets.length, saved.assets.length + 1); assert.deepEqual(result.nodes.find(node => node.id === source.id), before.nodes.find(node => node.id === source.id)); assert.equal(JSON.stringify(saved), JSON.stringify(before));
  assert.equal(resolveHubView(result, edited).inherited, false); assert.deepEqual(validateProject(result), []);
});

test('Hub materialization deduplicates equivalent media, ignores unused uploads, and remaps occupied graph identities', () => {
  const { p, same } = fixture();
  const result = applyHubContent(p, same.id, [{ id: 'new', type: 'asset', assetId: 'upload' }], [{ ...p.assets[0], id: 'upload' }, { id: 'unused', name: 'unused.txt', mime: 'text/plain', base64: btoa('Unused') }]);
  const reusedItem = result.nodes.find(node => node.id === same.id)?.contentItems?.[0];
  assert.equal(result.assets.length, p.assets.length); assert.equal(reusedItem?.type === 'asset' && reusedItem.assetId, 'portrait-bytes');
  const collision = applyHubContent(p, same.id, [{ id: 'new', type: 'asset', assetId: same.id }], [{ id: same.id, name: 'distinct.txt', mime: 'text/plain', base64: btoa('Distinct') }]);
  const item = collision.nodes.find(node => node.id === same.id)!.contentItems![0]; assert.equal(item.type, 'asset'); if (item.type === 'asset') assert.notEqual(item.assetId, same.id);
  assert.deepEqual(validateProject(collision), []);
});

test('explicit empty Reference stacks stay local and invalid edits fail without mutation', () => {
  const { p, same } = fixture(); const result = applyHubContent(p, same.id, []), ref = result.nodes.find(node => node.id === same.id)!;
  assert.equal(resolveHubView(result, ref).inherited, false); assert.deepEqual(ref.contentItems, []); assert.equal(ref.targetId, same.targetId);
  const before = JSON.stringify(p); assert.throws(() => applyHubContent(p, 'missing', [])); assert.throws(() => applyHubContent(p, same.id, [{ id: 'x', type: 'link', url: 'javascript:bad' }])); assert.equal(JSON.stringify(p), before);
});
