import test from 'node:test';
import assert from 'node:assert/strict';
import { addCanvas, addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import { evaluate } from '../src/core/engine.ts';
import { buildPublication, createPublicationPayload, readPublication } from '../src/publication.ts';

test('publication embeds author strings as inert JSON and escaped text', () => {
  const project = newProject('</title><script>alert("title")</script>');
  const node = addNode(project, project.entryCanvasId, 'text', 'Unsafe-looking content');
  node.text = '</script><img src=x onerror=alert(1)> & <style>bad</style>\u2028';
  const html = buildPublication(project, 'window.viewerStarted = true;', 'body{color:white}');
  assert.ok(!html.includes('<img src=x'));
  assert.ok(!html.includes('<script>alert("title")'));
  assert.ok(html.includes('\\u003c/script\\u003e'));
  assert.equal((html.match(/<script\b/g) ?? []).length, 2);
  const imported = readPublication(html);
  assert.equal(imported.title, project.title);
  assert.equal(imported.nodes.find(item => item.kind === 'text')?.text, node.text);
});

test('publication carries only referenced assets and strips unknown filesystem metadata', () => {
  const project = newProject('Portable assets');
  const image = addNode(project, project.entryCanvasId, 'image'); image.assetId = 'used';
  project.assets = [
    { id: 'used', name: 'picture.png', mime: 'image/png', base64: 'AQID', path: 'C:\\private\\picture.png' } as never,
    { id: 'unused', name: 'secret.txt', mime: 'text/plain', base64: 'c2VjcmV0' },
  ];
  (project as unknown as Record<string, unknown>).workspacePath = 'I:\\private\\workspace';
  (image as unknown as Record<string, unknown>).filesystemPath = 'I:\\private\\image';
  const payload = createPublicationPayload(project);
  assert.deepEqual(payload.project.assets.map(asset => asset.id), ['used']);
  assert.ok(!JSON.stringify(payload).includes('private'));
  assert.ok(!JSON.stringify(payload).includes('secret.txt'));
});

test('external pinned references are included or disclosed as missing', () => {
  const project = newProject('External snapshot');
  const reference = addNode(project, project.entryCanvasId, 'reference', 'Remote input'); reference.targetId = 'external-input';
  project.snapshots['external-input'] = {
    node: { id: 'external-input', canvasId: 'remote-canvas', parentId: 'remote-root', kind: 'input', title: 'Saved external input', x: 0, y: 0, width: 160, height: 80, value: true },
    value: true, savedAt: '2026-10-04T12:00:00Z',
  };
  project.dependencies = [{ canvasId: 'remote-canvas', title: 'External canvas', required: true }];
  const included = createPublicationPayload(project);
  assert.equal(included.project.snapshots['external-input'].value, true);
  assert.deepEqual(included.missing, ['External canvas']);
  const excluded = createPublicationPayload(project, { includeExternalReferences: false });
  assert.deepEqual(Object.keys(excluded.project.snapshots), []);
  assert.ok(excluded.missing.includes('External canvas'));
  assert.equal(evaluate(excluded.project).values[reference.id], null);
});

test('owned canvases and their crosscanvas pinned snapshots survive external exclusion', () => {
  let project = newProject('Two canvases');
  const first = project.entryCanvasId;
  const second = addCanvas(project, 'Other canvas');
  const input = addNode(project, second.id, 'input', 'Pinned input'); input.value = true;
  const reference = addNode(project, first, 'reference', 'Other canvas reference'); reference.targetId = input.id;
  project = commitSnapshots(project);
  project.nodes.find(node => node.id === input.id)!.value = false;
  const payload = createPublicationPayload(project, { includeExternalReferences: false });
  assert.equal(payload.project.canvases.length, 2);
  assert.equal(payload.project.snapshots[input.id].value, true);
  assert.equal(evaluate(payload.project).values[reference.id], true);
  assert.equal(evaluate(payload.project).values[input.id], false);
});

test('deleted local originals do not reappear through saved snapshots', () => {
  let project = newProject('Deleted local source');
  const input = addNode(project, project.entryCanvasId, 'input', 'Original'); input.value = true;
  const reference = addNode(project, project.entryCanvasId, 'reference', 'Local reference'); reference.targetId = input.id;
  project = commitSnapshots(project);
  project.nodes = project.nodes.filter(node => node.id !== input.id);
  const payload = createPublicationPayload(project);
  assert.equal(Object.hasOwn(payload.project.snapshots, input.id), false);
  assert.ok(payload.missing.includes('Local reference'));
  assert.equal(evaluate(payload.project).values[reference.id], null);
});

test('import creates fresh identities with containment, links, assets and canvas navigation remapped', () => {
  const project = newProject('Fork me');
  const first = project.canvases[0];
  const second = addCanvas(project, 'Details');
  const input = addNode(project, first.id, 'input', 'Input');
  const output = addNode(project, first.id, 'output', 'Output');
  const navigation = addNode(project, first.id, 'canvas-link', 'Details link'); navigation.targetId = second.rootId;
  const image = addNode(project, second.id, 'image', 'Asset'); image.assetId = 'photo';
  project.assets = [{ id: 'photo', name: 'photo.png', mime: 'image/png', base64: 'AQID' }];
  project.links.push({ id: 'link', canvasId: first.id, sourceId: input.id, targetId: output.id, kind: 'functional', relation: 'causal', targetPort: 0 });
  const fork = readPublication(buildPublication(project, '', ''));
  const sourceIds = new Set([project.id, ...project.canvases.map(item => item.id), ...project.nodes.map(item => item.id), ...project.links.map(item => item.id), ...project.assets.map(item => item.id)]);
  for (const id of [fork.id, ...fork.canvases.map(item => item.id), ...fork.nodes.map(item => item.id), ...fork.links.map(item => item.id), ...fork.assets.map(item => item.id)]) assert.ok(!sourceIds.has(id));
  assert.equal(fork.nodes.find(node => node.title === 'Input')?.parentId, fork.canvases[0].rootId);
  assert.equal(fork.links[0].sourceId, fork.nodes.find(node => node.title === 'Input')?.id);
  assert.equal(fork.links[0].targetId, fork.nodes.find(node => node.title === 'Output')?.id);
  assert.equal(fork.nodes.find(node => node.title === 'Details link')?.targetId, fork.canvases[1].rootId);
  assert.equal(fork.nodes.find(node => node.title === 'Asset')?.assetId, fork.assets[0].id);
  assert.equal(fork.entryCanvasId, fork.canvases[0].id);
});

test('import rejects missing, unsupported, duplicate and malformed payloads without executing markup', () => {
  assert.throws(() => readPublication('<script>globalThis.evil = true</script>'), /not a supported/);
  assert.throws(() => readPublication('<script type="application/json" id="meridian-data">{}</script>'), /not a supported/);
  assert.throws(() => readPublication('<script type="application/json" id="meridian-data">{broken}</script>'), /could not be read/);
  const project = newProject();
  const html = buildPublication(project, '', '');
  assert.throws(() => readPublication(html + html), /more than one/);
  const malformed = html.replace('"nodes":[', '"nodes":[null,');
  assert.throws(() => readPublication(malformed), /malformed/);
});

test('declared and transitive saved context is included consistently with dependency reports', () => {
  const project = newProject('Declared context'), node = addNode(project, project.entryCanvasId, 'module', 'Consumer'); node.dependencyIds = ['external-a'];
  project.snapshots['external-a'] = { node: { id: 'external-a', canvasId: 'external-canvas', parentId: 'external-root', kind: 'text', title: 'Saved A', x: 0, y: 0, width: 100, height: 100, dependencyIds: ['external-b'] }, value: null, savedAt: '2026-10-05T12:00:00Z' };
  project.snapshots['external-b'] = { node: { id: 'external-b', canvasId: 'external-canvas', parentId: 'external-root', kind: 'image', title: 'Saved B', x: 0, y: 0, width: 100, height: 100, assetId: 'saved-image' }, value: null, asset: { id: 'saved-image', name: 'context.png', mime: 'image/png', base64: 'AQID' }, savedAt: '2026-10-05T12:00:00Z' };
  const included = createPublicationPayload(project);
  assert.deepEqual(Object.keys(included.project.snapshots).sort(), ['external-a', 'external-b']);
  assert.deepEqual(included.project.nodes.find(item => item.id === node.id)?.dependencyIds, ['external-a']);
  assert.equal(included.project.snapshots['external-b'].asset?.id, 'saved-image');
  assert.deepEqual(included.missing, []);
  const excluded = createPublicationPayload(project, { includeExternalReferences: false });
  assert.deepEqual(Object.keys(excluded.project.snapshots), []); assert.ok(excluded.missing.includes('Saved A')); assert.ok(excluded.missing.includes('Saved B'));
  const fork = readPublication(buildPublication(project, '', ''));
  const savedA = Object.values(fork.snapshots).find(item => item.node.title === 'Saved A')!, savedB = Object.values(fork.snapshots).find(item => item.node.title === 'Saved B')!;
  assert.deepEqual(fork.nodes.find(item => item.kind === 'module')?.dependencyIds, [savedA.node.id]);
  assert.deepEqual(savedA.node.dependencyIds, [savedB.node.id]);
});
