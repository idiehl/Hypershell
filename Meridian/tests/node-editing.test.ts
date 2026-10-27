import assert from 'node:assert/strict';
import { test } from 'node:test';
import { addCanvas, addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import { applyRecipe, inspectProject } from '../src/core/authoring.ts';
import { addRemoteLink, removeRemoteLink, renameNode, updateNodeType } from '../src/core/node-editing.ts';
import { isNodeHyperlinks, isProjectShape, validateProject } from '../src/core/rules.ts';
import { resolveGridCell } from '../src/core/cells.ts';
import { createModule, instantiateModule } from '../src/core/modules.ts';
import { createSnippet } from '../src/core/selection.ts';
import { libraryDocumentFromProject, normalizeLibraryDocument, reconcileLibraryCanvas } from '../src/core/library.ts';
import { createPublicationPayload, readPublication, buildPublication } from '../src/publication.ts';

test('conceptual GUID links are inert, bounded, unique relationships with legitimate unavailable targets', () => {
  const project = newProject(), node = addNode(project, project.entryCanvasId, 'text');
  const before = structuredClone(project);
  const added = addRemoteLink(project, node.id, { targetId: 'remote-greek-zeus', relation: 'mutual', label: 'Alternate account' });
  assert.deepEqual(project, before); assert.deepEqual(added.links, []);
  assert.equal(added.nodes[1].remoteLinks?.[0].targetId, 'remote-greek-zeus'); assert.deepEqual(validateProject(added), []);
  assert.throws(() => addRemoteLink(added, node.id, { targetId: 'remote-greek-zeus', relation: 'mutual' }), /malformed/);
  for (const targetId of ['../node', '', 'a'.repeat(97), 'unsafe node', 'node\n', '__proto__'])
    assert.throws(() => addRemoteLink(project, node.id, { targetId }), /GUID/);
  const removed = removeRemoteLink(added, node.id, added.nodes[1].remoteLinks![0].id);
  assert.equal(removed.nodes[1].remoteLinks, undefined);
  assert.equal(added.nodes[1].remoteLinks?.length, 1);
});

test('remote links reject unsafe fields, duplicate identities, malformed snapshots and oversized lists', () => {
  let project = newProject(); const node = addNode(project, project.entryCanvasId, 'text');
  project = addRemoteLink(project, node.id, { targetId: 'outside' });
  for (const remote of [null, {}, [{ id: 'r', targetId: 'outside', relation: 'url' }],
    [{ id: 'r', targetId: 'outside', relation: 'generic', fetch: true }],
    [{ id: 'r', targetId: 'outside', relation: 'generic', label: 'a'.repeat(161) }],
    Array.from({ length: 1001 }, (_, index) => ({ id: `r-${index}`, targetId: `outside-${index}`, relation: 'generic' }))]) {
    const invalid = structuredClone(project); invalid.nodes[1].remoteLinks = remote as any;
    assert.equal(isProjectShape(invalid), false);
  }
  const duplicate = structuredClone(project); duplicate.nodes[1].remoteLinks![0].id = duplicate.id;
  assert.match(validateProject(duplicate)[0].message, /unique identity/);
  const snapshot = commitSnapshots(project); snapshot.snapshots[node.id].node.remoteLinks![0].targetId = 'bad/target';
  assert.equal(isProjectShape(snapshot), false);
});

test('GUID override rewrites local hierarchy, roots, links, cell references, dependencies, conceptual targets and saved observations atomically', () => {
  let project = newProject('Rename grid'), grid = addNode(project, project.entryCanvasId, 'spreadsheet', 'Grid');
  const cell = addNode(project, project.entryCanvasId, 'text', 'Cell'); cell.parentId = grid.id; cell.cell = { column: 1, row: 1, name: 'source' };
  const reference = addNode(project, project.entryCanvasId, 'reference'); reference.targetId = grid.id; reference.targetCell = 'source'; reference.dependencyIds = [grid.id];
  const other = addNode(project, project.entryCanvasId, 'text');
  project.links.push({ id: crypto.randomUUID(), canvasId: project.entryCanvasId, sourceId: grid.id, targetId: other.id, parentId: grid.id, kind: 'visual', relation: 'generic' });
  project = addRemoteLink(project, other.id, { targetId: grid.id }); project = commitSnapshots(project);
  const original = JSON.stringify(project), renamed = renameNode(project, grid.id, 'my-sources-grid');
  assert.equal(JSON.stringify(project), original); assert.deepEqual(validateProject(renamed), []);
  assert.equal(renamed.nodes.find(node => node.id === cell.id)?.parentId, 'my-sources-grid');
  assert.equal(renamed.nodes.find(node => node.id === reference.id)?.targetId, 'my-sources-grid');
  assert.deepEqual(renamed.nodes.find(node => node.id === reference.id)?.dependencyIds, ['my-sources-grid']);
  assert.equal(renamed.nodes.find(node => node.id === other.id)?.remoteLinks?.[0].targetId, 'my-sources-grid');
  assert.equal(renamed.links[0].parentId, 'my-sources-grid'); assert.equal(renamed.links[0].sourceId, 'my-sources-grid');
  assert.equal(renamed.snapshots[grid.id], undefined); assert.equal(renamed.snapshots['my-sources-grid'].node.id, 'my-sources-grid');
  assert.equal(renamed.snapshots[cell.id].node.parentId, 'my-sources-grid');
  assert.equal(resolveGridCell(renamed, 'my-sources-grid', 'A1')?.id, cell.id);
  const rootId = renamed.canvases[0].rootId, rootRenamed = renameNode(renamed, rootId, 'custom-canvas-root');
  assert.equal(rootRenamed.canvases[0].rootId, 'custom-canvas-root');
  assert.equal(rootRenamed.nodes.find(node => node.id === other.id)?.parentId, 'custom-canvas-root');
});

test('GUID override refuses occupied and reserved identifiers without modifying the graph', () => {
  const project = newProject(), node = addNode(project, project.entryCanvasId, 'text');
  project.assets.push({ id: 'attachment', name: 'source', mime: 'text/plain', base64: 'AQID' });
  const before = structuredClone(project);
  for (const id of [project.id, project.entryCanvasId, project.canvases[0].rootId, 'attachment', 'constructor', 'prototype', '__proto__', 'node/title', ''])
    assert.throws(() => renameNode(project, node.id, id), /already used|GUID/);
  assert.deepEqual(project, before);
});

test('custom home root and canvas launcher GUIDs survive envelope normalization and discovery reconciliation', () => {
  const home = normalizeLibraryDocument({ schemaVersion: 1, revision: 0, entries: [
    { id: 'launcher', parentId: null, kind: 'canvas', title: 'Greek myths', collapsed: true, x: 200, y: 200, projectId: 'remote-project', canvasId: 'remote-canvas' },
  ] });
  const rootId = home.project.canvases[0].rootId;
  let project = renameNode(home.project, rootId, 'library-root'); project = renameNode(project, 'launcher', 'my-greek-canvas');
  const envelope = libraryDocumentFromProject(home, project), normalized = normalizeLibraryDocument(envelope), reconciled = reconcileLibraryCanvas(normalized, []);
  assert.equal(reconciled.project.canvases[0].rootId, 'library-root');
  assert.deepEqual(reconciled.canvasTargets, { 'my-greek-canvas': { projectId: 'remote-project', canvasId: 'remote-canvas' } });
  assert.deepEqual(reconciled.project.nodes.find(node => node.id === 'my-greek-canvas')?.canvasTarget, { projectId: 'remote-project', canvasId: 'remote-canvas' });
});

test('type and subtype edits preserve authored content and children while refusing incompatible typed wires or inherited rules', () => {
  let project = newProject(), container = addNode(project, project.entryCanvasId, 'container');
  container.text = 'Keep this passage'; container.hyperlinks = [{ url: 'https://example.test/source', label: 'Source' }];
  const child = addNode(project, project.entryCanvasId, 'document'); child.parentId = container.id; child.text = 'Keep document';
  const grid = updateNodeType(project, container.id, { kind: 'container', containerVariant: 'grid' });
  assert.equal(grid.nodes.find(node => node.id === child.id)?.cell?.column, 1);
  assert.equal(grid.nodes.find(node => node.id === child.id)?.text, 'Keep document');
  const text = updateNodeType(grid, container.id, { kind: 'text' });
  assert.equal(text.nodes.find(node => node.id === container.id)?.text, 'Keep this passage');
  assert.deepEqual(text.nodes.find(node => node.id === container.id)?.hyperlinks, container.hyperlinks);
  assert.equal(text.nodes.find(node => node.id === child.id)?.parentId, container.id);
  const source = addNode(project, project.entryCanvasId, 'number'), output = addNode(project, project.entryCanvasId, 'output'); output.valueType = 'number';
  project.links.push({ id: crypto.randomUUID(), canvasId: project.entryCanvasId, sourceId: source.id, targetId: output.id, kind: 'numeric', relation: 'causal' });
  const before = structuredClone(project);
  assert.throws(() => updateNodeType(project, source.id, { kind: 'text' }), /supply.*numeric/);
  assert.deepEqual(project, before);
  project.nodes[0].rules = { nodeKinds: ['container', 'document', 'number', 'output'], operations: [] };
  assert.throws(() => updateNodeType(project, container.id, { kind: 'joint' }), /do not allow any joint/);
});

test('type edits reject incompatibility hidden by saved cross-canvas observations before autosave changes those observations', () => {
  let project = newProject(), source = addNode(project, project.entryCanvasId, 'output', 'Boolean source');
  const other = addCanvas(project, 'Other canvas'), reference = addNode(project, other.id, 'reference');
  reference.targetId = source.id;
  const destination = addNode(project, other.id, 'output', 'Boolean destination');
  project.links.push({ id: crypto.randomUUID(), canvasId: other.id, sourceId: reference.id, targetId: destination.id, kind: 'functional', relation: 'causal' });
  project = commitSnapshots(project);
  assert.deepEqual(validateProject(project), []);
  const before = JSON.stringify(project);
  assert.throws(() => updateNodeType(project, source.id, { kind: 'number' }), /functional|boolean|type/i);
  assert.equal(JSON.stringify(project), before);
});

test('type conversion can create a dedicated owned canvas and refuses to discard external launchers or addressed references', () => {
  const project = newProject(), node = addNode(project, project.entryCanvasId, 'text', 'Open a canvas'); node.text = 'Launcher summary';
  const converted = updateNodeType(project, node.id, { kind: 'canvas-link' });
  assert.equal(converted.canvases.length, 2); assert.equal(converted.nodes.find(item => item.id === node.id)?.text, 'Launcher summary');
  assert.ok(converted.canvases.some(canvas => canvas.rootId === converted.nodes.find(item => item.id === node.id)?.targetId));
  assert.throws(() => updateNodeType(project, project.canvases[0].rootId, { kind: 'container' }), /roots remain/);
  const launcher = addNode(project, project.entryCanvasId, 'canvas-link'); launcher.canvasTarget = { projectId: 'remote-project' };
  assert.throws(() => updateNodeType(project, launcher.id, { kind: 'text' }), /external Work destination/);
  const reference = addNode(project, project.entryCanvasId, 'reference'); reference.targetId = node.id; reference.targetCell = 'A1';
  assert.throws(() => updateNodeType(project, reference.id, { kind: 'text' }), /addresses a spreadsheet/);
});

test('additional attachments and hyperlinks are bounded inert content and preserve bytes through publication and independent copies', () => {
  let project = newProject(), root = addNode(project, project.entryCanvasId, 'container', 'Sources');
  const child = addNode(project, project.entryCanvasId, 'document', 'Passage'); child.parentId = root.id;
  const primary = { id: 'primary', name: 'first.txt', mime: 'text/plain', base64: 'AQID' }, supplemental = { id: 'secondary', name: 'second.pdf', mime: 'application/pdf', base64: 'BAUG' };
  project.assets.push(primary, supplemental); child.assetId = primary.id; child.attachments = [supplemental.id];
  child.hyperlinks = [{ url: 'https://example.test/source?q=Zeus', label: 'Reference' }, { url: 'mailto:reader@example.test' }];
  project = addRemoteLink(project, root.id, { targetId: child.id, relation: 'chronological' });
  project = addRemoteLink(project, child.id, { targetId: 'remote-zeus', label: 'Alternative' });
  project = commitSnapshots(project);
  const publication = createPublicationPayload(project); assert.deepEqual(publication.missing, []);
  assert.deepEqual(publication.project.assets.map(asset => asset.base64), ['AQID', 'BAUG']);
  const fork = readPublication(buildPublication(project, '', '')), forkRoot = fork.nodes.find(node => node.title === 'Sources')!, forkChild = fork.nodes.find(node => node.title === 'Passage')!;
  assert.equal(forkRoot.remoteLinks?.[0].targetId, forkChild.id); assert.equal(forkChild.remoteLinks?.[0].targetId, 'remote-zeus');
  assert.notEqual(forkRoot.remoteLinks?.[0].id, project.nodes.find(node => node.id === root.id)?.remoteLinks?.[0].id);
  assert.equal(fork.assets.find(asset => asset.id === forkChild.attachments?.[0])?.base64, 'BAUG');
  assert.deepEqual(forkChild.hyperlinks, child.hyperlinks);
  const snippet = createSnippet(project, [root.id], { includeDescendants: true, includeExternalReferences: false });
  const snippetChild = snippet.project.nodes.find(node => node.title === 'Passage')!, snippetRoot = snippet.project.nodes.find(node => node.title === 'Sources')!;
  assert.equal(snippetRoot.remoteLinks?.[0].targetId, snippetChild.id); assert.equal(snippetChild.remoteLinks?.[0].targetId, 'remote-zeus');
  assert.equal(snippet.project.assets.find(asset => asset.id === snippetChild.attachments?.[0])?.base64, 'BAUG');
  const module = createModule(project, root.id, 'Source module'), destination = newProject('Destination');
  const insertion = instantiateModule(destination, module, { canvasId: destination.entryCanvasId, x: 0, y: 0 });
  const insertedChild = insertion.project.nodes.find(node => node.title === 'Passage')!, insertedRoot = insertion.project.nodes.find(node => node.id === insertion.rootId)!;
  assert.equal(insertedRoot.remoteLinks?.[0].targetId, insertedChild.id); assert.equal(insertedChild.remoteLinks?.[0].targetId, 'remote-zeus');
  assert.equal(insertion.project.assets.find(asset => asset.id === insertedChild.attachments?.[0])?.base64, 'BAUG');
});

test('content metadata rejects active URL schemes, unknown fields, duplicate attachments and snapshot bypasses', () => {
  for (const url of ['javascript:alert(1)', 'data:text/html,test', 'file:///C:/private', '//example.test', 'https://example.test\n', 'mailto:', 'mailto:?subject=Empty', 'http:example.test'])
    assert.equal(isNodeHyperlinks([{ url }]), false, url);
  assert.equal(isNodeHyperlinks([{ url: 'https://example.test', onclick: 'test' }]), false);
  const project = newProject(), node = addNode(project, project.entryCanvasId, 'text');
  node.attachments = ['same', 'same']; assert.equal(isProjectShape(project), false);
  delete node.attachments; node.hyperlinks = [{ url: 'https://example.test' }];
  const snapshot = commitSnapshots(project); snapshot.snapshots[node.id].node.hyperlinks![0].url = 'javascript:alert(1)';
  assert.equal(isProjectShape(snapshot), false);
});

test('batch recipes resolve supplemental asset and conceptual node aliases and inspect missing attachment bytes', () => {
  const result = applyRecipe(newProject('Recipe'), { version: 1,
    canvases: [{ key: 'main', title: 'Sources and accounts' }],
    assets: [{ key: 'passage', name: 'passage.txt', mime: 'text/plain', base64: 'AQID' }],
    nodes: [
      { key: 'sources', canvas: 'main', kind: 'document', title: 'Sources', attachments: ['passage'], hyperlinks: [{ url: 'https://example.test' }],
        remoteLinks: [{ id: 'source-account', targetId: 'zeus', relation: 'generic' }] },
      { key: 'zeus', canvas: 'main', kind: 'text', title: 'Zeus' },
    ] });
  const source = result.project.nodes.find(node => node.title === 'Sources')!;
  assert.deepEqual(source.attachments, [result.aliases.assets.passage]);
  assert.equal(source.remoteLinks![0].targetId, result.aliases.nodes.zeus);
  assert.equal(inspectProject(result.project).canvases.find(canvas => canvas.id === result.aliases.canvases.main)?.media.assetIds[0], result.aliases.assets.passage);
  source.attachments!.push('missing-source');
  assert.ok(inspectProject(result.project).warnings.some(item => item.code === 'missing-media' && item.nodeId === source.id));
});
