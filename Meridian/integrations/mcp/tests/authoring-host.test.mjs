import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { mkdtemp, rm, mkdir, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, resolve, relative, isAbsolute, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { once } from 'node:events';
import { test } from 'node:test';
import { createAuthoringService, runAuthoringWorker } from '../src/authoring-service.mjs';
import { createCompanion } from '../src/server.mjs';
import { Client } from '@modelcontextprotocol/sdk/client/index.js';
import { StreamableHTTPClientTransport } from '@modelcontextprotocol/sdk/client/streamableHttp.js';

const root = fileURLToPath(new URL('../../../', import.meta.url));
const hostExecutable = join(root, 'output', 'app', 'Meridian.Host.exe');

test('installed .NET host accepts guarded authoring, actual preview, bare-project checkpoint proof, reopen and conflict rejection in isolated storage', { skip: process.platform !== 'win32', timeout: 60000 }, async t => {
  const directory = await mkdtemp(join(tmpdir(), 'meridian-real-host-test-'));
  let host;
  t.after(async () => {
    if (host && host.exitCode === null) { host.kill(); await Promise.race([once(host, 'exit'), new Promise(resolveWait => setTimeout(resolveWait, 5000))]); }
    const part = relative(resolve(tmpdir()), resolve(directory));
    assert(part && !part.startsWith('..') && !isAbsolute(part));
    await rm(directory, { recursive: true, force: true });
  });
  const origin = await new Promise((resolveReady, rejectReady) => {
    host = spawn(hostExecutable, ['--urls', 'http://127.0.0.1:0', '--data-root', join(directory, 'host-data')], {
      cwd: dirname(hostExecutable), shell: false, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'],
      env: { ...process.env, ASPNETCORE_ENVIRONMENT: 'Production', Logging__LogLevel__Default: 'Information' },
    });
    let log = '', settled = false;
    const finish = (error, value) => { if (settled) return; settled = true; clearTimeout(timer); if (error) rejectReady(error); else resolveReady(value); };
    const timer = setTimeout(() => finish(new Error('The isolated installed host did not start.')), 15000);
    const consume = chunk => {
      log = `${log}${chunk.toString('utf8')}`.slice(-12000);
      const match = /Now listening on:\s*(http:\/\/127\.0\.0\.1:\d+)/.exec(log);
      if (match) finish(null, match[1]);
    };
    host.stdout.on('data', consume); host.stderr.on('data', consume);
    host.once('error', () => finish(new Error('The installed host executable could not start in isolated storage.')));
    host.once('exit', () => { if (!settled) finish(new Error('The isolated installed host exited before becoming ready.')); });
  });
  const requests = [];
  const request = async (path, method = 'GET', body) => {
    requests.push({ path, method });
    const response = await fetch(`${origin}${path}`, { method, ...(body ? { headers: { 'content-type': 'application/json' }, body: JSON.stringify(body) } : {}) });
    assert(response.ok, `Fixture host response ${response.status}`);
    return response.json();
  };
  const initial = await runAuthoringWorker({ command: 'new', title: 'Real host fixture' });
  const baseWork = initial.entryCanvasId;
  const seeded = await runAuthoringWorker({ command: 'apply', project: initial, recipe: { version: 1, canvases: [{ key: 'other', title: 'Unrelated' }], nodes: [
    { key: 'original', canvas: baseWork, kind: 'media', title: 'Original', x: 0, y: 0, width: 260, height: 100, sizeMode: 'fixed' },
    { key: 'untouched', canvas: 'other', kind: 'media', title: 'Unchanged', x: 0, y: 0, width: 260, height: 100, sizeMode: 'fixed' },
  ] } });
  const base = await request('/api/projects', 'POST', seeded.project);
  const readRules = async () => 'PURPOSE-004 [ACCEPTED]\nCompose a graph.\n';
  const config = { publicOrigin: 'https://mcp-host-fixture.example', port: 0, authoringEnabled: true, stateRoot: join(directory, 'companion'), meridianOrigin: origin, projectIds: [base.id], viewerAssetsRoot: join(root, 'output', 'app', 'wwwroot') };
  const service = createAuthoringService(config, { readRules });
  const auth = {
    handle: async () => false,
    verifyBearer: req => ({ ok: ['Bearer fixture-read', 'Bearer fixture-author'].includes(req.headers.authorization), scopes: req.headers.authorization === 'Bearer fixture-author' ? ['meridian.read', 'meridian.author'] : ['meridian.read'] }),
    requireBearer: (req, res) => { if (['Bearer fixture-read', 'Bearer fixture-author'].includes(req.headers.authorization)) return true; res.writeHead(401).end(); return false; },
    challenge: () => 'Bearer scope="meridian.author", error="insufficient_scope"',
  };
  const companion = createCompanion(config, { auth, authoringService: service, readRules });
  await companion.listen();
  const companionOrigin = `http://127.0.0.1:${companion.server.address().port}`;
  const client = new Client({ name: 'installed-host-authoring-e2e', version: '1.0.0' });
  const reader = new Client({ name: 'installed-host-read-e2e', version: '1.0.0' });
  t.after(async () => { await client.close(); await reader.close(); await new Promise(resolveClose => companion.server.close(resolveClose)); });
  await client.connect(new StreamableHTTPClientTransport(new URL(`${companionOrigin}/mcp`), { requestInit: { headers: { authorization: 'Bearer fixture-author' } } }));
  await reader.connect(new StreamableHTTPClientTransport(new URL(`${companionOrigin}/mcp`), { requestInit: { headers: { authorization: 'Bearer fixture-read' } } }));
  const names = (await client.listTools()).tools.map(tool => tool.name);
  for (const name of ['meridian_create_draft', 'meridian_apply_recipe', 'meridian_validate', 'meridian_preview', 'meridian_save_work']) assert(names.includes(name));
  const denied = await reader.callTool({ name: 'meridian_inspect', arguments: { projectId: base.id, workId: baseWork } });
  assert.equal(denied.isError, true); assert(denied._meta['mcp/www_authenticate']);
  const readList = await reader.callTool({ name: 'meridian_list_works', arguments: {} });
  assert(!JSON.stringify(readList).includes('Original'));
  const call = async (name, args = {}) => {
    const response = await client.callTool({ name, arguments: args });
    assert(!response.isError, JSON.stringify(response.content));
    return response.structuredContent;
  };
  const rulesVersion = (await call('meridian_authoring_rules')).version;
  await call('meridian_authoring_schema');
  const draft = await call('meridian_create_draft', { projectId: base.id, workId: baseWork, rulesVersion });
  const edited = await call('meridian_apply_recipe', { draftId: draft.draftId, expectedVersion: 1, recipe: { version: 1, nodes: [{ key: 'original', existingId: seeded.aliases.nodes.original, title: 'Revised' }] } });
  const validation = await call('meridian_validate', { draftId: draft.draftId, expectedVersion: edited.version });
  assert(validation.passed, JSON.stringify(validation.diagnostics));
  const previewResponse = await client.callTool({ name: 'meridian_preview', arguments: { draftId: draft.draftId, expectedVersion: edited.version } });
  assert(!previewResponse.isError, JSON.stringify(previewResponse.content));
  const preview = previewResponse.structuredContent;
  const imageBlock = previewResponse.content.find(block => block.type === 'image');
  assert(imageBlock); const image = Buffer.from(imageBlock.data, 'base64'); assert(image.length > 5000);
  const evidenceRoot = join(root, 'output', 'mcp-authoring-20261023'); await mkdir(evidenceRoot, { recursive: true });
  await writeFile(join(evidenceRoot, 'e2e-preview.png'), image);
  const previewUrl = new URL(preview.previewUrl);
  const html = await fetch(`${companionOrigin}${previewUrl.pathname}${previewUrl.search}`);
  assert.equal(html.status, 200);
  assert.match(html.headers.get('content-security-policy'), /sandbox allow-scripts/);
  assert(!html.headers.get('content-security-policy').includes('allow-same-origin'));
  assert((await html.text()).includes('meridian-data'));
  const saveArgs = { draftId: draft.draftId, expectedVersion: edited.version, validationId: validation.validationId, previewId: preview.previewId, visualReviewConfirmed: true };
  const receipt = await call('meridian_save_work', saveArgs);
  assert.equal(receipt.revision, base.revision + 1); assert(receipt.checkpointId);
  const checkpoint = await request(`/api/projects/${base.id}/checkpoints/${receipt.checkpointId}`);
  assert.equal(checkpoint.id, base.id); assert.equal(checkpoint.revision, base.revision);
  assert.equal(checkpoint.metadata, undefined); assert.deepEqual(checkpoint, base);
  let saved = await request(`/api/projects/${base.id}`);
  assert.equal(saved.nodes.find(node => node.id === seeded.aliases.nodes.original).title, 'Revised');
  assert.deepEqual(saved.nodes.filter(node => node.canvasId !== baseWork), base.nodes.filter(node => node.canvasId !== baseWork));
  assert.deepEqual(saved.canvases, base.canvases); assert.deepEqual(saved.links, base.links); assert.deepEqual(saved.assets, base.assets);
  assert.deepEqual(await call('meridian_save_work', saveArgs), receipt);
  const added = await call('meridian_create_draft', { projectId: base.id, title: 'New Graph', rulesVersion });
  const addedEdited = await call('meridian_apply_recipe', { draftId: added.draftId, expectedVersion: 1, recipe: { version: 1, nodes: [
    { key: 'topic', canvas: added.workId, kind: 'media', title: 'System', x: 0, y: 0, width: 220, height: 70, sizeMode: 'fixed', childLayout: 'spatial', childArrangement: 'free', collapsed: false, style: { shape: 'rectangle', radius: 0, contentSizing: 'fixed', titleSize: 24, textAlign: 'center', padding: 8 } },
    { key: 'branch', canvas: added.workId, parent: 'topic', kind: 'media', title: 'Text', x: 420, y: 0, width: 220, height: 70, sizeMode: 'fixed', childPosition: 'manual', style: { shape: 'rectangle', radius: 0, contentSizing: 'fixed', titleSize: 24, textAlign: 'center', padding: 8 } },
  ], links: [{ key: 'edge', canvas: added.workId, parent: 'topic', source: 'topic', target: 'branch', kind: 'visual', relation: 'generic', direction: 'none' }] } });
  const addedValidation = await call('meridian_validate', { draftId: added.draftId, expectedVersion: addedEdited.version }); assert(addedValidation.passed);
  const addedPreviewResponse = await client.callTool({ name: 'meridian_preview', arguments: { draftId: added.draftId, expectedVersion: addedEdited.version } });
  assert(!addedPreviewResponse.isError, JSON.stringify(addedPreviewResponse.content));
  const addedPreview = addedPreviewResponse.structuredContent;
  await writeFile(join(evidenceRoot, 'e2e-new-work-preview.png'), Buffer.from(addedPreviewResponse.content.find(block => block.type === 'image').data, 'base64'));
  const addedReceipt = await call('meridian_save_work', { draftId: added.draftId, expectedVersion: addedEdited.version, validationId: addedValidation.validationId, previewId: addedPreview.previewId, visualReviewConfirmed: true });
  const withAdded = await request(`/api/projects/${base.id}`);
  assert.equal(withAdded.canvases.length, saved.canvases.length + 1);
  assert.deepEqual(withAdded.nodes.filter(node => node.canvasId !== added.workId), saved.nodes);
  assert.equal(addedReceipt.revision, saved.revision + 1); saved = withAdded;
  const stale = await call('meridian_create_draft', { projectId: base.id, workId: baseWork, rulesVersion });
  const staleValidation = await call('meridian_validate', { draftId: stale.draftId, expectedVersion: 1 });
  const stalePreview = await call('meridian_preview', { draftId: stale.draftId, expectedVersion: 1 });
  const externalEdit = structuredClone(saved); externalEdit.nodes.find(node => node.id === seeded.aliases.nodes.original).title = 'User edit';
  await request(`/api/projects/${base.id}`, 'PUT', { project: externalEdit, expectedRevision: saved.revision, requireNoRecovery: true });
  const conflicted = await client.callTool({ name: 'meridian_save_work', arguments: { draftId: stale.draftId, expectedVersion: 1, validationId: staleValidation.validationId, previewId: stalePreview.previewId, visualReviewConfirmed: true } });
  assert(conflicted.isError); assert.equal(JSON.parse(conflicted.content.find(block => block.type === 'text').text).code, 'save-conflict');
  assert.equal((await request(`/api/projects/${base.id}`)).nodes.find(node => node.id === seeded.aliases.nodes.original).title, 'User edit');
  await writeFile(join(evidenceRoot, 'e2e-receipt.json'), JSON.stringify({ installedHost: true, isolatedData: true, officialMcpClient: true, oldReadScopeBlocked: true, imageReturnedInMcp: true, sandboxedArtifactLink: true,
    existingWorkSaved: true, newWorkSaved: true, unrelatedWorksPreserved: true, bareProjectCheckpointVerified: true, idempotentSaveVerified: true, staleSaveRejected: true }, null, 2));
});
