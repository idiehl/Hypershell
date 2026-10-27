import assert from 'node:assert/strict';
import { spawn, spawnSync, type ChildProcess } from 'node:child_process';
import { randomUUID } from 'node:crypto';
import { once } from 'node:events';
import { mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import { createServer } from 'node:net';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { test } from 'node:test';
import { addNode, newProject } from '../src/core/model.ts';
import { createModule, instantiateModule } from '../src/core/modules.ts';
import { validateProject } from '../src/core/rules.ts';
import type { StoredModule } from '../src/core/types.ts';

const root = fileURLToPath(new URL('../', import.meta.url));

test('immutable local modules survive restart and reject malformed or competing writes', { timeout: 120_000 }, async t => {
  const temporary = await mkdtemp(path.join(tmpdir(), 'meridian-module-tests-'));
  const dataRoot = path.join(temporary, 'data');
  const buildRoot = path.join(temporary, 'build');
  let host: ChildProcess | undefined;
  let base = '', logs = '';
  async function start() {
    base = `http://127.0.0.1:${await freePort()}`;
    host = spawn('dotnet', [path.join(buildRoot, 'Meridian.Host.dll'), '--data-root', dataRoot, '--urls', base], { cwd: root, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
    logs = '';
    host.stdout!.on('data', chunk => { logs = (logs + chunk).slice(-12_000); });
    host.stderr!.on('data', chunk => { logs = (logs + chunk).slice(-12_000); });
    for (let attempt = 0; attempt < 200; attempt++) {
      if (host.exitCode !== null) assert.fail(`Host exited during startup: ${logs}`);
      try { if ((await fetch(base + '/__meridian_status')).ok) return; } catch { /* Listener is starting. */ }
      await new Promise(resolve => setTimeout(resolve, 50));
    }
    assert.fail(`Host did not start: ${logs}`);
  }
  async function stop() {
    if (!host || host.exitCode !== null) return;
    const exited = once(host, 'exit'); host.kill(); await exited; host = undefined;
  }
  const json = (method: string, route: string, body?: unknown, headers: Record<string, string> = {}) => fetch(base + route, {
    method, headers: { ...(body === undefined ? {} : { 'Content-Type': 'application/json' }), ...headers }, body: body === undefined ? undefined : JSON.stringify(body),
  });
  const create = (module: unknown, headers: Record<string, string> = {}) => json('POST', '/api/modules', { module }, headers);
  const saved = fixture();
  try {
    const intermediate = path.join(temporary, 'obj') + path.sep;
    const build = spawnSync('dotnet', ['build', path.join(root, 'src/host/Meridian.Host.csproj'), '--nologo', '--output', buildRoot,
      `-p:MSBuildProjectExtensionsPath=${intermediate}`, `-p:IntermediateOutputPath=${intermediate}`], { cwd: root, encoding: 'utf8', timeout: 60_000, windowsHide: true });
    assert.equal(build.status, 0, `${build.stdout}\n${build.stderr}`);
    await start();
    await t.test('creation preserves complete contents while catalog contains only metadata and counts', async () => {
      assert.deepEqual(await (await json('GET', '/api/modules')).json(), []);
      assert.ok((await (await json('GET', '/__meridian_status')).json()).capabilities.includes('modules-v1'));
      const response = await create(saved);
      assert.equal(response.status, 201, await response.clone().text());
      assert.deepEqual(await response.json(), saved);
      assert.deepEqual(await (await json('GET', `/api/modules/${saved.id}`)).json(), saved);
      const catalog = await json('GET', '/api/modules');
      assert.equal(catalog.headers.get('Cache-Control'), 'no-store');
      assert.deepEqual(await catalog.json(), [{ id: saved.id, name: saved.name, createdAt: saved.createdAt, rootId: saved.rootId, nodeCount: 3, linkCount: 1,
        contents: [{ kind: 'input', count: 1 }, { kind: 'module', count: 1 }, { kind: 'output', count: 1 }] }]);
    });
    await t.test('module kind and dependency declarations survive regular project and export routes', async () => {
      const project = structuredClone(saved.project);
      const response = await json('POST', '/api/projects', project);
      assert.equal(response.status, 201, await response.clone().text());
      const committed = await response.json();
      assert.deepEqual(committed.nodes, project.nodes);
      assert.equal((await json('POST', '/api/export', committed)).status, 200);
      for (const dependencyIds of [null, ['signal', 'signal'], [' input'], ['input'], ['two words'], ['../outside'], ['x'.repeat(97)], Array.from({ length: 1001 }, (_, i) => `x${i}`)]) {
        const invalid = structuredClone(committed);
        invalid.nodes.find((node: { id: string }) => node.id === 'input').dependencyIds = dependencyIds;
        assert.equal((await json('PUT', `/api/projects/${project.id}`, { project: invalid, expectedRevision: 1 })).status, 400);
      }
    });
    await t.test('stored modules are immutable and simultaneous creation has one winner', async () => {
      const diskPath = path.join(dataRoot, 'modules', saved.id + '.json');
      const original = await readFile(diskPath, 'utf8');
      assert.equal((await create({ ...saved, name: 'replacement' })).status, 409);
      assert.equal(await readFile(diskPath, 'utf8'), original);
      const concurrent = fixture();
      const results = await Promise.all(Array.from({ length: 8 }, () => create(concurrent)));
      assert.deepEqual(results.map(result => result.status).sort(), [201, 409, 409, 409, 409, 409, 409, 409]);
      assert.deepEqual(await (await json('GET', `/api/modules/${concurrent.id}`)).json(), concurrent);
    });
    await t.test('saved definitions and read-only catalog survive a host restart', async () => {
      await stop(); await start();
      assert.deepEqual(await (await json('GET', `/api/modules/${saved.id}`)).json(), saved);
      assert.equal((await (await json('GET', '/api/modules')).json()).length, 2);
    });
    await t.test('invalid metadata, branches, fields, and circuits fail atomically', async () => {
      const invalidCases: Array<[string, (module: any) => void]> = [
        ['unsafe identity', module => { module.id = '../escape'; }],
        ['non-GUID identity', module => { module.id = 'module-id'; }],
        ['unsupported schema', module => { module.schemaVersion = 2; }],
        ['empty name', module => { module.name = ' '; }],
        ['untrimmed name', module => { module.name = ' name '; }],
        ['invalid timestamp', module => { module.createdAt = 'yesterday'; }],
        ['missing project', module => { delete module.project; }],
        ['non-module root', module => { module.project.nodes[1].kind = 'container'; }],
        ['unrelated branch', module => { module.project.nodes[2].parentId = 'canvas-root'; }],
        ['cycle', module => { module.project.nodes[2].parentId = 'output'; module.project.nodes[3].parentId = 'input'; }],
        ['missing parent', module => { module.project.nodes[3].parentId = 'missing'; }],
        ['identity collision', module => { module.project.assets[0].id = 'input'; }],
        ['unknown kind', module => { module.project.nodes[2].kind = 'unsafe'; }],
        ['module operation', module => { module.project.nodes[1].operation = 'AND'; }],
        ['wrong optional field type', module => { module.project.nodes[2].value = 'true'; }],
        ['wrong dependency shape', module => { module.project.nodes[2].dependencyIds = [5]; }],
        ['self dependency', module => { module.project.nodes[2].dependencyIds = ['input']; }],
        ['storage canvas dependency', module => { module.project.nodes[2].dependencyIds = ['canvas']; }],
        ['storage root reference', module => { module.project.nodes[2].kind = 'reference'; module.project.nodes[2].targetId = 'canvas-root'; }],
        ['storage project dependency', module => { module.project.nodes[2].dependencyIds = [module.project.id]; }],
        ['snapshot context', module => { module.project.snapshots['outside'] = { node: module.project.nodes[2], savedAt: saved.createdAt, value: null }; }],
        ['malformed canvas dependency', module => { module.project.dependencies = [false]; }],
        ['invalid link relation', module => { module.project.links[0].relation = 'other'; }],
        ['invalid port', module => { module.project.links[0].targetPort = 1; }],
        ['duplicate port', module => { module.project.links.push({ ...module.project.links[0], id: 'duplicate' }); }],
        ['invalid functional endpoint', module => { module.project.links[0].sourceId = 'module-root'; }],
        ['canvas-root link', module => { module.project.links[0].kind = 'visual'; module.project.links[0].sourceId = 'canvas-root'; }],
        ['malformed route', module => { module.project.links[0].points = [{ x: 'bad', y: 1 }]; }],
        ['functional feedback', module => { module.project.nodes[2].kind = 'output'; module.project.links.push({ ...module.project.links[0], id: 'return', sourceId: 'output', targetId: 'input' }); }],
        ['inherited node restriction', module => { module.project.nodes[1].rules = { nodeKinds: ['text'] }; }],
        ['widened inherited rules', module => { module.project.nodes[0].rules = { nodeKinds: ['module', 'input', 'output'] }; module.project.nodes[1].rules = { nodeKinds: ['text'] }; }],
      ];
      for (const [label, change] of invalidCases) {
        const invalid = fixture(); change(invalid);
        const response = await create(invalid);
        assert.equal(response.status, 400, `${label}: ${await response.clone().text()}`);
      }
      assert.equal((await (await json('GET', '/api/modules')).json()).length, 2);
    });
    await t.test('unresolved declarations remain explicit and declaration cycles are allowed', async () => {
      const module = fixture();
      module.project.nodes[2].dependencyIds = ['output', 'unavailable-external-node'];
      module.project.nodes[3].dependencyIds = ['input'];
      const response = await create(module);
      assert.equal(response.status, 201, await response.clone().text());
      assert.deepEqual((await response.json()).project.nodes, module.project.nodes);
    });
    await t.test('unknown identifiers and write-origin guards are explicit', async () => {
      assert.equal((await json('GET', `/api/modules/${randomUUID()}`)).status, 404);
      assert.equal((await json('GET', '/api/modules/bad-id')).status, 400);
      assert.equal((await create(fixture(), { Origin: 'https://unrelated.example' })).status, 403);
      assert.equal((await create(fixture(), { 'Sec-Fetch-Site': 'cross-site' })).status, 403);
      assert.equal((await fetch(base + '/api/modules', { method: 'POST', headers: { 'Content-Type': 'text/plain' }, body: '{}' })).status, 415);
    });
    await t.test('corrupt module storage is surfaced and preserved', async () => {
      const diskPath = path.join(dataRoot, 'modules', saved.id + '.json');
      const original = await readFile(diskPath, 'utf8');
      await writeFile(diskPath, '{damaged');
      assert.equal((await json('GET', `/api/modules/${saved.id}`)).status, 503);
      assert.equal((await json('GET', '/api/modules')).status, 503);
      assert.equal((await create(saved)).status, 409);
      assert.equal(await readFile(diskPath, 'utf8'), '{damaged');
      await writeFile(diskPath, original);
    });
    await t.test('the shared authoring API saves a definition and creates independent instances from its response', async () => {
      const source = newProject('Source');
      const pack = addNode(source, source.entryCanvasId, 'module', 'Boolean control');
      const input = addNode(source, source.entryCanvasId, 'input', 'Enable'); input.parentId = pack.id;
      const output = addNode(source, source.entryCanvasId, 'output', 'Result'); output.parentId = pack.id;
      source.links.push({ id: randomUUID(), canvasId: source.entryCanvasId, sourceId: input.id, targetId: output.id, kind: 'functional', relation: 'causal' });
      const module = createModule(source, pack.id, 'Boolean control');
      const response = await create(module);
      assert.equal(response.status, 201, await response.clone().text());
      const stored = await response.json() as StoredModule;
      assert.deepEqual(stored, module);
      const destination = newProject('Independent copies');
      const first = instantiateModule(destination, stored, { canvasId: destination.entryCanvasId, x: 80, y: 100 });
      const second = instantiateModule(first.project, stored, { canvasId: destination.entryCanvasId, x: 800, y: 100 });
      assert.notEqual(first.rootId, second.rootId);
      assert.deepEqual(validateProject(second.project), []);
      assert.equal((await json('POST', '/api/projects', second.project)).status, 201);
    });
  } finally {
    await stop();
    assert.ok(path.resolve(temporary).startsWith(path.resolve(tmpdir()) + path.sep + 'meridian-module-tests-'));
    await rm(temporary, { recursive: true, force: true });
  }
});

async function freePort(): Promise<number> {
  const server = createServer(); server.listen(0, '127.0.0.1'); await once(server, 'listening');
  const address = server.address(); assert.ok(address && typeof address !== 'string');
  const port = address.port;
  await new Promise<void>((resolve, reject) => server.close(error => error ? reject(error) : resolve()));
  return port;
}

function fixture(): any {
  const node = { id: 'module-root', canvasId: 'canvas', parentId: 'canvas-root', kind: 'module', title: 'Signal assembly', x: 80, y: 90, width: 600, height: 360 };
  return {
    schemaVersion: 1, id: randomUUID(), name: 'Signal assembly', createdAt: new Date().toISOString(), rootId: 'module-root',
    project: {
      schemaVersion: 1, id: randomUUID(), title: 'Signal assembly', revision: 0, entryCanvasId: 'canvas',
      canvases: [{ id: 'canvas', rootId: 'canvas-root', title: 'Module definition' }],
      nodes: [{ ...node, id: 'canvas-root', kind: 'canvas', parentId: null }, node,
        { ...node, id: 'input', parentId: 'module-root', kind: 'input', title: 'Enable', value: true, dependencyIds: ['external-requirement'] },
        { ...node, id: 'output', parentId: 'module-root', kind: 'output', title: 'Result' }],
      links: [{ id: 'signal-link', canvasId: 'canvas', sourceId: 'input', targetId: 'output', kind: 'functional', relation: 'causal' }],
      assets: [{ id: 'asset', name: 'notes.txt', mime: 'text/plain', base64: Buffer.from('private module content').toString('base64') }],
      snapshots: {}, dependencies: [],
    },
  };
}
