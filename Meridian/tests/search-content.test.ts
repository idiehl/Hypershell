import test from 'node:test';
import assert from 'node:assert/strict';
import { addCanvas, addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import { createProjectSearch } from '../src/core/navigation.ts';

test('search reaches complete TSV contents, cell aliases and current type labels without changing content', () => {
  const project = newProject('Content discovery');
  const parent = addNode(project, project.entryCanvasId, 'container', 'Closed collection'); parent.collapsed = true;
  const node = addNode(project, project.entryCanvasId, 'media', 'Table');
  Object.assign(node, { parentId: parent.id, contentType: 'asset', assetId: 'source-table', cell: { column: 2, row: 3, name: 'Witnesses' } });
  const body = 'Opening\taccount\n'.repeat(1000) + 'late-table-evidence';
  project.assets.push({ id: 'source-table', name: 'Comparative sources.tsv', mime: 'text/tab-separated-values; charset=utf-8', base64: Buffer.from(body).toString('base64') });
  const utility = addNode(project, project.entryCanvasId, 'junction', 'Turn'); utility.utilityVariant = 'pivot';
  const before = JSON.stringify(project), search = createProjectSearch(project);
  assert.equal(search('late-table-evidence')[0]?.nodeId, node.id);
  assert.equal(search('late-table-evidence')[0]?.source, 'Attachment text');
  assert.equal(search('Comparative sources.tsv')[0]?.nodeId, node.id);
  assert.equal(search('2:3')[0]?.nodeId, node.id);
  assert.equal(search('witnesses')[0]?.nodeId, node.id);
  assert.ok(search('media document').some(result => result.nodeId === node.id));
  assert.equal(search('utility pivot')[0]?.nodeId, utility.id);
  assert.equal(JSON.stringify(project), before);
});

test('search indexes the active item and conceptual link targets while preserving inactive legacy material', () => {
  const project = newProject('Content choices'), node = addNode(project, project.entryCanvasId, 'media', 'Catalog');
  Object.assign(node, { contentType: 'link', text: 'inactive-body-token', assetId: 'inactive-file', hyperlinks: [{ label: 'Museum entry', url: 'https://example.org/active-catalog-token' }], remoteLinks: [{ id: 'conceptual-link', targetId: 'remote_identity_token', relation: 'causal', label: 'Related collection' }] });
  project.assets.push({ id: 'inactive-file', name: 'inactive-file-token.txt', mime: 'text/plain', base64: Buffer.from('inactive-file-body').toString('base64') });
  const before = JSON.stringify(project), search = createProjectSearch(project);
  assert.equal(search('active-catalog-token')[0]?.source, 'Hyperlink');
  assert.equal(search('Museum entry')[0]?.nodeId, node.id);
  assert.equal(search('remote_identity_token')[0]?.source, 'Remote link');
  assert.equal(search('Related collection')[0]?.nodeId, node.id);
  assert.deepEqual(search('inactive-body-token'), []);
  assert.deepEqual(search('inactive-file-token'), []);
  assert.deepEqual(search('inactive-file-body'), []);
  assert.equal(JSON.stringify(project), before);
});

test('reference attachment search reads committed external bytes and live same-canvas bytes', () => {
  let project = newProject('Saved search'), original = addNode(project, project.entryCanvasId, 'media', 'Original document');
  Object.assign(original, { contentType: 'asset', assetId: 'reference-text' });
  project.assets.push({ id: 'reference-text', name: 'Saved evidence.md', mime: 'text/markdown', base64: Buffer.from('committed-evidence-token').toString('base64') });
  const same = addNode(project, project.entryCanvasId, 'reference', 'Local projection'); same.targetId = original.id;
  const other = addCanvas(project, 'Outside context'), external = addNode(project, other.id, 'reference', 'Saved projection'); external.targetId = original.id;
  project = commitSnapshots(project); project.assets.find(asset => asset.id === 'reference-text')!.base64 = Buffer.from('unsaved-evidence-token').toString('base64');
  const before = JSON.stringify(project), search = createProjectSearch(project);
  assert.ok(search('unsaved-evidence-token').some(result => result.nodeId === same.id));
  assert.ok(!search('unsaved-evidence-token').some(result => result.nodeId === external.id));
  assert.ok(search('committed-evidence-token').some(result => result.nodeId === external.id));
  assert.equal(JSON.stringify(project), before);
});

test('search context prefers the field covering most query terms and retains priority for split-term ties', () => {
  const project = newProject('Context ranking'), node = addNode(project, project.entryCanvasId, 'media', 'Meridian catalog');
  Object.assign(node, { tag: 'Research collection', contentType: 'asset', assetId: 'context-document' });
  project.assets.push({ id: 'context-document', name: 'Document.md', mime: 'text/markdown', base64: Buffer.from('document-token evidence-token rare-phrase').toString('base64') });
  const before = JSON.stringify(project), search = createProjectSearch(project);
  assert.equal(search('Media Document')[0]?.nodeId, node.id);
  assert.equal(search('Media Document')[0]?.source, 'Type');
  assert.equal(search('evidence-token rare-phrase')[0]?.source, 'Attachment text');
  assert.match(search('evidence-token rare-phrase')[0]!.snippet, /evidence-token rare-phrase/);
  const split = search('Meridian evidence-token').find(result => result.nodeId === node.id);
  assert.equal(split?.source, 'Title');
  assert.equal(split?.snippet, node.title);
  assert.deepEqual(search('Meridian missing-keyword'), []);
  assert.equal(JSON.stringify(project), before);
});

test('an exact GUID stays first when more than the result cap share a source packet mentioning that identity', () => {
  const project = newProject('Identity lookup'), target = addNode(project, project.entryCanvasId, 'media', 'The actual node');
  target.tag = 'Specific target';
  project.assets.push({ id: 'shared-packet', name: 'Complete source packet.md', mime: 'text/markdown', base64: Buffer.from(`This packet documents node ${target.id} and its sources.`).toString('base64') });
  for (let index = 0; index < 125; index++) {
    const item = addNode(project, project.entryCanvasId, 'media', `Packet ${String(index).padStart(3, '0')}`);
    Object.assign(item, { contentType: 'asset', assetId: 'shared-packet' });
  }
  const impersonatingTag = addNode(project, project.entryCanvasId, 'media', 'A mention with a matching Tag'); impersonatingTag.tag = target.id;
  const before = JSON.stringify(project), search = createProjectSearch(project);
  for (const query of [target.id, target.id.toUpperCase(), `  ${target.id}  `]) {
    const found = search(query);
    assert.equal(found.length, 100); assert.equal(found[0].nodeId, target.id); assert.equal(found[0].source, 'Work / identity');
    assert.equal(found[1].nodeId, impersonatingTag.id);
    assert.equal(search(query, 1)[0].nodeId, target.id);
  }
  assert.equal(JSON.stringify(project), before);
});

test('exact Tags and titles precede partial matches while multi-term context remains searchable', () => {
  const project = newProject('Handle ranking');
  for (let index = 0; index < 110; index++) { const item = addNode(project, project.entryCanvasId, 'media', `Athena sources ${index}`); item.text = 'Grey-eyed Athena from several accounts'; }
  const exactTitle = addNode(project, project.entryCanvasId, 'media', 'Athéna'); exactTitle.tag = 'Title identity';
  const exactTag = addNode(project, project.entryCanvasId, 'media', 'The goddess in context'); exactTag.tag = 'Athena';
  const search = createProjectSearch(project);
  assert.equal(search('athena')[0].nodeId, exactTag.id); assert.equal(search('athena')[0].source, 'Tag');
  assert.equal(search('athena')[1].nodeId, exactTitle.id); assert.equal(search('athena')[1].source, 'Title');
  assert.ok(search('Athena accounts').some(result => result.source === 'Text'));
});
