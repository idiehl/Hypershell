import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { createServer } from 'node:http';
import { mkdtemp, rm, writeFile, readFile, mkdir } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, resolve, relative, isAbsolute, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { once } from 'node:events';
import { test } from 'node:test';
import { Client } from '@modelcontextprotocol/sdk/client/index.js';
import { StdioClientTransport } from '@modelcontextprotocol/sdk/client/stdio.js';
import { createStdioCompanion, validateStdioConfig, PRIVATE_STDIO_TRANSPORT, MAX_STDIO_MESSAGE_BYTES, scrubTunnelSecrets } from '../src/stdio.mjs';
import { ALL_TOOL_NAMES } from '../src/meridian-tools.mjs';
import { runAuthoringWorker } from '../src/authoring-service.mjs';
import { privatePreviewFailureMetadata } from '../src/authoring-tools.mjs';

const root = fileURLToPath(new URL('../../../', import.meta.url));
const entrypoint = fileURLToPath(new URL('../src/stdio.mjs', import.meta.url));
const rules = 'PURPOSE-004 [ACCEPTED]\nCompose a graph.\n';
const basic = () => ({ transport: PRIVATE_STDIO_TRANSPORT, authoringEnabled: true, authoringConsent: true, projectIds: ['shared-project'], stateRoot: resolve(tmpdir(), 'meridian-stdio-config-fixture') });
const workspaceCleanups = new WeakMap();

test('private stdio startup requires explicit local authoring consent, its transport and bounded project scope', () => {
  for (const field of ['transport', 'authoringEnabled', 'authoringConsent', 'stateRoot']) {
    const config = basic(); delete config[field];
    assert.throws(() => validateStdioConfig(config));
  }
  for (const field of ['stateRoot', 'rulesPath', 'viewerAssetsRoot', 'auditPath']) {
    assert.throws(() => validateStdioConfig({ ...basic(), [field]: 'relative/path' }));
  }
  for (const projectIds of [[], ['*'], undefined, ['../secret']]) assert.throws(() => createStdioCompanion({ ...basic(), projectIds }));
  assert.throws(() => createStdioCompanion({ ...basic(), meridianOrigin: 'https://arbitrary.example' }));
  assert.equal(validateStdioConfig(basic()).transport, PRIVATE_STDIO_TRANSPORT);
});

test('private preview browser configuration requires an existing absolute local regular file', () => {
  for (const browserExecutablePath of ['relative/browser.exe', 'https://example.com/browser.exe', '\\\\server\\share\\browser.exe', resolve(tmpdir(), 'absent-meridian-browser-' + Date.now()), tmpdir(), null]) {
    assert.throws(() => validateStdioConfig({ ...basic(), browserExecutablePath }));
  }
  assert.equal(validateStdioConfig({ ...basic(), browserExecutablePath: process.execPath }).browserExecutablePath, process.execPath);
});

async function workspace(t) {
  const directory = await mkdtemp(join(tmpdir(), 'meridian-stdio-test-'));
  const cleanups = []; workspaceCleanups.set(t, cleanups);
  t.after(async () => {
    for (const cleanup of [...cleanups].reverse()) await cleanup();
    const part = relative(resolve(tmpdir()), resolve(directory));
    assert(part && !part.startsWith('..') && !isAbsolute(part));
    await rm(directory, { recursive: true, force: true });
  });
  return directory;
}

async function connect(configPath, t, options = {}) {
  const client = new Client({ name: 'meridian-stdio-isolated-test', version: '1.0.0' });
  const transport = new StdioClientTransport({ command: process.execPath, args: [options.entrypoint ?? entrypoint, '--config', configPath], stderr: 'pipe', maxBufferSize: 10 * 1024 * 1024,
    ...(options.env ? { env: options.env } : {}) });
  let stderr = ''; transport.stderr.on('data', chunk => { stderr += chunk.toString(); });
  await client.connect(transport);
  workspaceCleanups.get(t).push(() => client.close());
  const call = async (name, args = {}) => {
    const response = await client.callTool({ name, arguments: args }, undefined, { timeout: 60000 });
    assert(!response.isError, JSON.stringify(response.content));
    return response;
  };
  return { client, transport, call, get stderr() { return stderr; } };
}

test('private preview audit permits only known error codes and stage names', () => {
  assert.deepEqual(privatePreviewFailureMetadata({ code: 'EINVAL', previewStage: 'capture-worker' }), { errorCode: 'EINVAL', previewStage: 'capture-worker' });
  assert.deepEqual(privatePreviewFailureMetadata({ code: 'authoring-rejected', previewStage: 'navigate-preview', previewDiagnosticCode: 'ERR_INVALID_ARGUMENT' }),
    { errorCode: 'ERR_INVALID_ARGUMENT', previewStage: 'navigate-preview' });
  const sensitive = 'private-sentinel-work-content-and-path';
  assert.deepEqual(privatePreviewFailureMetadata({ code: sensitive, previewStage: sensitive, message: sensitive, stack: sensitive }, sensitive),
    { errorCode: 'unclassified', previewStage: 'preview-response' });
  assert.deepEqual(privatePreviewFailureMetadata({ code: 'authoring-rejected', previewStage: 'navigate-preview', previewDiagnosticCode: 'ERR_' + sensitive }),
    { errorCode: 'authoring-rejected', previewStage: 'navigate-preview' });
});

test('actual SDK stdio records bounded failed preview metadata without changing public errors or leaking error text', async t => {
  const directory = await workspace(t), probePath = join(directory, 'failure-probe.mjs');
  await writeFile(probePath, `import { readFile } from 'node:fs/promises';
import { startStdioFromConfig } from ${JSON.stringify(new URL('../src/stdio.mjs', import.meta.url).href)};
const config = JSON.parse(await readFile(process.argv[3], 'utf8'));
const failure = () => {
  const error = Object.assign(new Error(config.fixtureFailure === 'inner' ? 'Safe fixture preview failure.' : 'private-sentinel-work-content-and-path'), { code: config.fixtureFailure === 'inner' ? 'authoring-rejected' : config.fixtureFailure === 'artifact' ? 'EACCES' : 'EINVAL' });
  if (config.fixtureFailure === 'render') Object.defineProperty(error, 'previewStage', { value: 'capture-worker' });
  if (config.fixtureFailure === 'inner') {
    Object.defineProperty(error, 'previewStage', { value: 'navigate-preview' });
    Object.defineProperty(error, 'previewDiagnosticCode', { value: 'ERR_INVALID_ARGUMENT' });
  }
  throw error;
};
await startStdioFromConfig(process.argv[3], { authoringService: {
  preview: async () => ['render', 'inner'].includes(config.fixtureFailure) ? failure() : { artifacts: config.fixtureFailure === 'success' ? [] : [{ kind: 'image', artifactId: '11111111-1111-4111-8111-111111111111' }] },
  artifact: async () => failure(),
} });
`);
  for (const [fixtureFailure, tool, errorCode, previewStage] of [
    ['render', 'meridian_preview', 'EINVAL', 'capture-worker'],
    ['inner', 'meridian_preview', 'ERR_INVALID_ARGUMENT', 'navigate-preview'],
    ['artifact', 'meridian_preview', 'EACCES', 'inline-image-read'],
    ['artifact', 'meridian_preview_image', 'EACCES', 'preview-image-read'],
    ['success', 'meridian_preview', undefined, undefined],
  ]) {
    const auditPath = join(directory, `${fixtureFailure}-${tool}.jsonl`), configPath = join(directory, `${fixtureFailure}-${tool}.json`);
    await writeFile(configPath, JSON.stringify({ ...basic(), stateRoot: directory, auditPath, fixtureFailure }));
    const session = await connect(configPath, t, { entrypoint: probePath });
    const args = tool === 'meridian_preview_image' ? { artifactId: '11111111-1111-4111-8111-111111111111' } : { draftId: '11111111-1111-4111-8111-111111111111', expectedVersion: 1 };
    const response = await session.client.callTool({ name: tool, arguments: args });
    if (fixtureFailure !== 'success') {
      assert.equal(response.isError, true);
      const publicError = JSON.parse(response.content[0].text);
      assert.equal(publicError.code, fixtureFailure === 'inner' ? 'authoring-rejected' : 'authoring-operation-failed');
      assert(!JSON.stringify(response).includes('private-sentinel'));
      assert(!JSON.stringify(response).includes('previewStage'));
      assert(!JSON.stringify(response).includes('previewDiagnosticCode'));
      if (fixtureFailure === 'inner') assert.equal(publicError.message, 'Safe fixture preview failure.');
    } else assert(!response.isError);
    await session.client.close();
    assert.equal(session.stderr, '');
    const raw = await readFile(auditPath, 'utf8'), records = raw.trim().split('\n').map(JSON.parse);
    assert(!raw.includes('private-sentinel')); assert(!raw.includes('11111111-1111-4111-8111-111111111111'));
    const audit = records.find(record => record.tool === tool);
    assert.equal(audit.errorCode, errorCode); assert.equal(audit.previewStage, previewStage);
    assert.equal(audit.success, fixtureFailure === 'success');
    assert.deepEqual(Object.keys(audit).sort(), ['timestamp', 'transport', 'phase', 'success', 'tool', ...(errorCode ? ['errorCode', 'previewStage'] : [])].sort());
  }
});

test('stdio strips inherited tunnel/API credentials before actual authoring workers and their child processes', async t => {
  const environment = { CONTROL_PLANE_API_KEY: 'fixture', OpenAI_Api_Key: 'fixture', OPENAI_ADMIN_KEY: 'fixture', TUNNEL_TOKEN: 'fixture', PATH: 'preserved' };
  scrubTunnelSecrets(environment); assert.deepEqual(environment, { PATH: 'preserved' });
  const directory = await workspace(t), configPath = join(directory, 'config.json'), rulesPath = join(directory, 'rules.txt');
  await writeFile(rulesPath, rules);
  await writeFile(configPath, JSON.stringify({ ...basic(), stateRoot: directory, rulesPath }));
  const probePath = join(directory, 'environment-probe.mjs');
  const stdioUrl = new URL('../src/stdio.mjs', import.meta.url).href, workerUrl = new URL('../src/authoring-service.mjs', import.meta.url).href;
  await writeFile(probePath, `import { spawnSync } from 'node:child_process';
import { startStdioFromConfig } from ${JSON.stringify(stdioUrl)};
import { runAuthoringWorker } from ${JSON.stringify(workerUrl)};
const names = ['CONTROL_PLANE_API_KEY','OPENAI_API_KEY','OPENAI_ADMIN_KEY','TUNNEL_TOKEN','CLOUDFLARED_TOKEN','NGROK_AUTHTOKEN'];
await startStdioFromConfig(process.argv[3], { runWorker: async input => {
  if (names.some(name => process.env[name] !== undefined)) throw new Error('Fixture environment was not scrubbed.');
  const child = spawnSync(process.execPath, ['-e', 'process.exit(' + JSON.stringify(names) + '.some(name => process.env[name] !== undefined) ? 9 : 0)'], { windowsHide: true });
  if (child.status !== 0) throw new Error('Fixture child inherited a tunnel key.');
  return runAuthoringWorker(input);
} });\n`);
  const sentinels = Object.fromEntries(['CONTROL_PLANE_API_KEY', 'OPENAI_API_KEY', 'OPENAI_ADMIN_KEY', 'TUNNEL_TOKEN', 'CLOUDFLARED_TOKEN', 'NGROK_AUTHTOKEN'].map(name => [name, 'private-test-sentinel']));
  const session = await connect(configPath, t, { entrypoint: probePath, env: { ...process.env, ...sentinels } });
  const schema = (await session.call('meridian_authoring_schema')).structuredContent;
  assert.equal(schema.version, 1); assert(schema.nodeFields.includes('existingId'));
  assert(!session.stderr.includes('private-test-sentinel'));
});

test('actual SDK stdio transport closes an oversized message before invoking any authoring tool', { timeout: 15000 }, async t => {
  const directory = await workspace(t), configPath = join(directory, 'config.json'), auditPath = join(directory, 'audit.jsonl');
  await writeFile(configPath, JSON.stringify({ ...basic(), stateRoot: directory, auditPath }));
  const session = await connect(configPath, t);
  const oversized = session.client.callTool({ name: 'meridian_apply_recipe', arguments: {
    draftId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', expectedVersion: 1, recipe: { version: 1, text: 'x'.repeat(MAX_STDIO_MESSAGE_BYTES) },
  } }, undefined, { timeout: 5000 });
  await assert.rejects(oversized, /closed|connection|epipe/i);
  const audit = await readFile(auditPath, 'utf8').catch(() => '');
  assert(!audit.includes('meridian_apply_recipe'), 'The oversized request must be rejected by the SDK read buffer before tool execution.');
});

test('official SDK stdio lists all tools without OAuth/public URLs, scopes projects, and keeps stdout protocol-only', async t => {
  const directory = await workspace(t);
  const backend = createServer((req, res) => {
    res.setHeader('content-type', 'application/json');
    if (req.url === '/__meridian_status') res.end(JSON.stringify({ application: 'atlas-meridian', status: 'ready' }));
    else if (req.url === '/api/library/catalog') res.end(JSON.stringify([
      { id: 'shared-project', canvases: [{ id: 'shared-work', title: 'C++ Types' }] },
      { id: 'unshared', canvases: [{ id: 'secret', title: 'Not shared' }] },
    ]));
    else { res.statusCode = 404; res.end('{}'); }
  });
  backend.listen(0, '127.0.0.1'); await once(backend, 'listening');
  t.after(() => new Promise(done => backend.close(done)));
  const rulesPath = join(directory, 'rules.txt'); await writeFile(rulesPath, rules);
  const auditPath = join(directory, 'audit.jsonl');
  const configPath = join(directory, 'config.json');
  await writeFile(configPath, JSON.stringify({ ...basic(), meridianOrigin: `http://127.0.0.1:${backend.address().port}`, stateRoot: directory, rulesPath, auditPath }));
  const session = await connect(configPath, t);
  const tools = (await session.client.listTools()).tools;
  assert.deepEqual(tools.map(tool => tool.name).sort(), [...ALL_TOOL_NAMES].sort());
  assert(tools.every(tool => JSON.stringify(tool._meta.securitySchemes) === '[{"type":"noauth"}]'));
  assert.match(tools.find(tool => tool.name === 'meridian_preview').description, /no public preview link/);
  assert.equal((await session.call('meridian_capabilities')).structuredContent.backend.status, 'ready');
  assert.deepEqual((await session.call('meridian_list_works')).structuredContent.works, [{ projectId: 'shared-project', workId: 'shared-work', name: 'C++ Types' }]);
  const currentRules = (await session.call('meridian_authoring_rules')).structuredContent;
  assert.equal(currentRules.rules[0].id, 'PURPOSE-004');
  const denied = await session.client.callTool({ name: 'meridian_inspect', arguments: { projectId: 'unshared' } });
  assert(denied.isError); assert.equal(JSON.parse(denied.content[0].text).code, 'project-not-shared');
  await session.client.close();
  assert.equal(session.stderr, '');
  const audit = (await readFile(auditPath, 'utf8')).trim().split('\n').map(JSON.parse);
  assert(audit.every(record => record.transport === PRIVATE_STDIO_TRANSPORT));
  assert(!JSON.stringify(audit).includes('Not shared')); assert(!JSON.stringify(audit).includes('projectId'));
  assert(audit.some(record => record.tool === 'meridian_inspect' && record.success === false));
});

test('stdio retains create/edit/preview/checkpoint-save, restart draft persistence and conflict guards against the installed host in isolated storage', { skip: process.platform !== 'win32', timeout: 120000 }, async t => {
  const directory = await workspace(t), hostExecutable = join(root, 'output', 'app', 'Meridian.Host.exe');
  let host;
  workspaceCleanups.get(t).push(async () => { if (host && host.exitCode === null) { host.kill(); await Promise.race([once(host, 'exit'), new Promise(done => setTimeout(done, 5000))]); } });
  const origin = await new Promise((ready, reject) => {
    host = spawn(hostExecutable, ['--urls', 'http://127.0.0.1:0', '--data-root', join(directory, 'host-data')], {
      cwd: dirname(hostExecutable), shell: false, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'],
      env: { ...process.env, ASPNETCORE_ENVIRONMENT: 'Production', Logging__LogLevel__Default: 'Information' },
    });
    let log = '', settled = false;
    const finish = (error, value) => { if (settled) return; settled = true; clearTimeout(timer); error ? reject(error) : ready(value); };
    const timer = setTimeout(() => finish(new Error('Isolated installed host startup timed out.')), 15000);
    const consume = chunk => { log = `${log}${chunk}`.slice(-12000); const match = /Now listening on:\s*(http:\/\/127\.0\.0\.1:\d+)/.exec(log); if (match) finish(null, match[1]); };
    host.stdout.on('data', consume); host.stderr.on('data', consume);
    host.once('error', () => finish(new Error('Could not start isolated installed host.')));
    host.once('exit', () => finish(new Error('Isolated installed host exited before readiness.')));
  });
  const request = async (path, method = 'GET', body) => {
    const response = await fetch(`${origin}${path}`, { method, ...(body ? { headers: { 'content-type': 'application/json' }, body: JSON.stringify(body) } : {}) });
    assert(response.ok, `Isolated host ${response.status}`); return response.json();
  };
  const initial = await runAuthoringWorker({ command: 'new', title: 'Stdio fixture' });
  const workId = initial.entryCanvasId;
  const seeded = await runAuthoringWorker({ command: 'apply', project: initial, recipe: { version: 1, canvases: [{ key: 'other', title: 'Other' }], nodes: [
    { key: 'original', canvas: workId, kind: 'media', title: 'Original', x: 0, y: 0, width: 260, height: 100, sizeMode: 'fixed' },
    { key: 'other', canvas: 'other', kind: 'media', title: 'Untouched', x: 0, y: 0, width: 260, height: 100, sizeMode: 'fixed' },
  ] } });
  const base = await request('/api/projects', 'POST', seeded.project);
  const rulesPath = join(directory, 'rules.txt'); await writeFile(rulesPath, rules);
  const configPath = join(directory, 'config.json');
  await writeFile(configPath, JSON.stringify({ ...basic(), projectIds: [base.id], meridianOrigin: origin, stateRoot: join(directory, 'companion'), rulesPath, viewerAssetsRoot: join(root, 'output', 'app', 'wwwroot') }));
  let session = await connect(configPath, t);
  const rulesVersion = (await session.call('meridian_authoring_rules')).structuredContent.version;
  await session.call('meridian_authoring_schema');
  const draft = (await session.call('meridian_create_draft', { projectId: base.id, workId, rulesVersion })).structuredContent;
  const edited = (await session.call('meridian_apply_recipe', { draftId: draft.draftId, expectedVersion: 1, recipe: { version: 1, nodes: [{ key: 'original', existingId: seeded.aliases.nodes.original, title: 'Revised' }] } })).structuredContent;
  await session.client.close();
  session = await connect(configPath, t);
  const drafts = (await session.call('meridian_drafts', { projectId: base.id })).structuredContent;
  assert(JSON.stringify(drafts).includes(draft.draftId));
  const inspected = (await session.call('meridian_inspect', { draftId: draft.draftId, query: 'Revised' })).structuredContent;
  assert.equal(inspected.items[0].id, seeded.aliases.nodes.original);
  const validatePreview = async draftState => {
    const validation = (await session.call('meridian_validate', { draftId: draftState.draftId, expectedVersion: draftState.version })).structuredContent;
    assert(validation.passed, JSON.stringify(validation.diagnostics));
    const response = await session.call('meridian_preview', { draftId: draftState.draftId, expectedVersion: draftState.version });
    const image = response.content.find(block => block.type === 'image'); assert(image); assert(Buffer.from(image.data, 'base64').length > 5000);
    assert(!Object.hasOwn(response.structuredContent, 'previewUrl'));
    const retrieved = await session.call('meridian_preview_image', { artifactId: response.structuredContent.artifacts.find(artifact => artifact.kind === 'image').artifactId });
    assert.equal(retrieved.content[0].data, image.data);
    return { draftId: draftState.draftId, expectedVersion: draftState.version, validationId: validation.validationId, previewId: response.structuredContent.previewId, visualReviewConfirmed: true, image };
  };
  const reviewed = await validatePreview(edited), { image, ...saveArgs } = reviewed;
  const receipt = (await session.call('meridian_save_work', saveArgs)).structuredContent;
  assert.equal(receipt.revision, base.revision + 1); assert(receipt.checkpointId);
  assert.deepEqual(await request(`/api/projects/${base.id}/checkpoints/${receipt.checkpointId}`), base);
  let saved = await request(`/api/projects/${base.id}`);
  assert.equal(saved.nodes.find(node => node.id === seeded.aliases.nodes.original).title, 'Revised');
  assert.deepEqual(saved.nodes.filter(node => node.canvasId !== workId), base.nodes.filter(node => node.canvasId !== workId));
  assert.deepEqual((await session.call('meridian_save_work', saveArgs)).structuredContent, receipt);
  const added = (await session.call('meridian_create_draft', { projectId: base.id, title: 'New Graph', rulesVersion })).structuredContent;
  const populated = (await session.call('meridian_apply_recipe', { draftId: added.draftId, expectedVersion: 1, recipe: { version: 1, nodes: [
    { key: 'topic', canvas: added.workId, kind: 'media', title: 'System', x: 0, y: 0, width: 220, height: 90, sizeMode: 'fixed' },
  ] } })).structuredContent;
  const { image: addedImage, ...addedArgs } = await validatePreview(populated);
  await session.call('meridian_save_work', addedArgs);
  const withAdded = await request(`/api/projects/${base.id}`); assert.equal(withAdded.canvases.length, saved.canvases.length + 1);
  assert.deepEqual(withAdded.nodes.filter(node => node.canvasId !== added.workId), saved.nodes); saved = withAdded;
  const stale = (await session.call('meridian_create_draft', { projectId: base.id, workId, rulesVersion })).structuredContent;
  const { image: staleImage, ...staleArgs } = await validatePreview(stale);
  const external = structuredClone(saved); external.nodes.find(node => node.id === seeded.aliases.nodes.original).title = 'User edit';
  await request(`/api/projects/${base.id}`, 'PUT', { project: external, expectedRevision: saved.revision, requireNoRecovery: true });
  const conflict = await session.client.callTool({ name: 'meridian_save_work', arguments: staleArgs }); assert(conflict.isError); assert.equal(JSON.parse(conflict.content[0].text).code, 'save-conflict');
  assert.equal((await request(`/api/projects/${base.id}`)).nodes.find(node => node.id === seeded.aliases.nodes.original).title, 'User edit');
  await session.call('meridian_discard_draft', { draftId: stale.draftId, expectedVersion: stale.version });
  const evidenceRoot = join(root, 'output', 'mcp-secure-tunnel-20261023'); await mkdir(evidenceRoot, { recursive: true });
  await writeFile(join(evidenceRoot, 'stdio-existing-work-preview.png'), Buffer.from(image.data, 'base64'));
  await writeFile(join(evidenceRoot, 'stdio-new-work-preview.png'), Buffer.from(addedImage.data, 'base64'));
  await writeFile(join(evidenceRoot, 'stdio-e2e-receipt.json'), JSON.stringify({ installedHost: true, isolatedData: true, officialStdioClient: true, allToolsListed: true, existingWorkSaved: true, newWorkSaved: true, draftSurvivesRestart: true,
    unrelatedWorksPreserved: true, originalNodeIdentityPreserved: true, checkpointVerified: true, idempotentSaveVerified: true, staleSaveRejected: true, inlinePreviewImage: true, noPublicPreviewUrl: true, artifactImageRetrieved: true }, null, 2));
});
