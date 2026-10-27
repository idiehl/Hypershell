import assert from 'node:assert/strict';
import { spawn, spawnSync, type ChildProcess } from 'node:child_process';
import { once } from 'node:events';
import { mkdtemp, rm } from 'node:fs/promises';
import { createServer } from 'node:net';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import test from 'node:test';
import { applyRecipe, type AuthoringRecipe } from '../src/core/authoring.ts';
import { addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import { isProjectShape, validateProject } from '../src/core/rules.ts';
import type { MeridianProject, NodeKind } from '../src/core/types.ts';
import { buildPublication, createPublicationPayload, readPublication } from '../src/publication.ts';

test('new nodes explicitly choose automatic sizing while canvas roots and joints retain their geometry', () => {
  const project = newProject('Sizing defaults');
  assert.equal(project.nodes[0].sizeMode, 'fixed');
  const automatic: NodeKind[] = ['container', 'spreadsheet', 'module', 'text', 'image', 'document', 'input', 'number', 'switch', 'output', 'reference', 'canvas-link'];
  for (const kind of automatic) assert.equal(addNode(project, project.entryCanvasId, kind).sizeMode, 'auto', kind);
  for (const kind of ['joint', 'junction'] as const) assert.equal(addNode(project, project.entryCanvasId, kind).sizeMode, 'fixed', kind);
});

test('size modes are optional for legacy projects and strict on both current nodes and saved snapshots', () => {
  const project = commitSnapshots(newProject('Sizing validation'));
  delete project.nodes[0].sizeMode; delete project.snapshots[project.nodes[0].id].node.sizeMode;
  assert.ok(isProjectShape(project)); assert.deepEqual(validateProject(project), []);
  for (const mode of ['auto', 'fixed'] as const) {
    project.nodes[0].sizeMode = mode; project.snapshots[project.nodes[0].id].node.sizeMode = mode;
    assert.ok(isProjectShape(project)); assert.deepEqual(validateProject(project), []);
  }
  for (const value of ['stretch', '', null, true, 4, {}, ['auto']]) {
    for (const snapshot of [false, true]) {
      const invalid = structuredClone(project);
      const node = snapshot ? invalid.snapshots[invalid.nodes[0].id].node : invalid.nodes[0];
      (node as any).sizeMode = value;
      assert.equal(isProjectShape(invalid), false, `${snapshot ? 'snapshot' : 'node'}: ${JSON.stringify(value)}`);
      assert.ok(validateProject(invalid).length > 0);
    }
  }
});

test('recipes retain deliberate dimensions, permit explicit auto sizing, and preserve omitted sizing on updates', () => {
  const base = newProject('Sizing recipe'), before = structuredClone(base);
  const result = applyRecipe(base, { version: 1, nodes: [
    { key: 'automatic', canvas: base.entryCanvasId, kind: 'container' },
    { key: 'width', canvas: base.entryCanvasId, kind: 'text', width: 720 },
    { key: 'height', canvas: base.entryCanvasId, kind: 'text', height: 600 },
    { key: 'explicit-auto', canvas: base.entryCanvasId, kind: 'container', width: 920, height: 640, sizeMode: 'auto' },
    { key: 'explicit-fixed', canvas: base.entryCanvasId, kind: 'image', sizeMode: 'fixed' },
    { key: 'joint', canvas: base.entryCanvasId, kind: 'joint' },
  ] });
  assert.deepEqual(base, before);
  const node = (key: string) => result.project.nodes.find(node => node.id === result.aliases.nodes[key])!;
  assert.equal(node('automatic').sizeMode, 'auto'); assert.equal(node('joint').sizeMode, 'fixed');
  assert.equal(node('width').sizeMode, 'fixed'); assert.equal(node('width').width, 720);
  assert.equal(node('height').sizeMode, 'fixed'); assert.equal(node('height').height, 600);
  assert.equal(node('explicit-auto').sizeMode, 'auto'); assert.equal(node('explicit-auto').width, 920);
  assert.equal(node('explicit-fixed').sizeMode, 'fixed');
  const updated = applyRecipe(result.project, { version: 1, nodes: [
    { key: 'retain', existingId: node('width').id, title: 'Retains fixed size' },
    { key: 'manual', existingId: node('automatic').id, height: 450 },
    { key: 'reset', existingId: node('height').id, sizeMode: 'auto' },
  ] }).project;
  assert.equal(updated.nodes.find(item => item.id === node('width').id)!.sizeMode, 'fixed');
  assert.equal(updated.nodes.find(item => item.id === node('automatic').id)!.sizeMode, 'fixed');
  assert.equal(updated.nodes.find(item => item.id === node('height').id)!.sizeMode, 'auto');
  for (const value of ['stretch', null, true, 3]) {
    const invalid = { version: 1, nodes: [{ key: 'invalid', canvas: base.entryCanvasId, kind: 'text', sizeMode: value }] } as unknown as AuthoringRecipe;
    assert.throws(() => applyRecipe(base, invalid), /sizeMode.*auto or fixed/); assert.deepEqual(base, before);
  }
});

test('automatic and fixed size modes survive publication, saved references, and fresh imported identities', () => {
  const project = newProject('Portable sizes');
  const automatic = addNode(project, project.entryCanvasId, 'container', 'Automatic');
  const fixed = addNode(project, project.entryCanvasId, 'text', 'Deliberate scale');
  Object.assign(fixed, { sizeMode: 'fixed', width: 1700, height: 900 });
  const reference = addNode(project, project.entryCanvasId, 'reference', 'Saved remote tile'); reference.targetId = 'remote-sized-node';
  project.snapshots['remote-sized-node'] = { node: { ...structuredClone(fixed), id: 'remote-sized-node', canvasId: 'remote-canvas', parentId: 'remote-root' }, value: null, savedAt: '2026-10-08T12:00:00Z' };
  const payload = createPublicationPayload(commitSnapshots(project));
  assert.equal(payload.project.nodes.find(node => node.id === automatic.id)!.sizeMode, 'auto');
  assert.equal(payload.project.nodes.find(node => node.id === fixed.id)!.sizeMode, 'fixed');
  assert.equal(payload.project.snapshots['remote-sized-node'].node.sizeMode, 'fixed');
  const imported = readPublication(buildPublication(project, '', ''));
  assert.equal(imported.nodes.find(node => node.title === automatic.title)!.sizeMode, 'auto');
  assert.equal(imported.nodes.find(node => node.title === fixed.title)!.sizeMode, 'fixed');
  assert.equal(Object.values(imported.snapshots).find(snapshot => snapshot.node.canvasId === 'remote-canvas')!.node.sizeMode, 'fixed');
});

test('native storage and packages retain size modes and reject invalid modes before replacing saved data', { timeout: 120_000 }, async () => {
  const root = fileURLToPath(new URL('../', import.meta.url));
  const temporary = await mkdtemp(path.join(tmpdir(), 'meridian-sizing-schema-tests-'));
  const buildRoot = path.join(temporary, 'build'), dataRoot = path.join(temporary, 'data');
  let host: ChildProcess | undefined, logs = '', base = '';
  const json = (method: string, route: string, body?: unknown) => fetch(base + route, { method,
    headers: body === undefined ? {} : { 'Content-Type': 'application/json' }, body: body === undefined ? undefined : JSON.stringify(body) });
  const read = async (route: string) => { const response = await json('GET', route); assert.equal(response.status, 200, await response.clone().text()); return response.json(); };
  try {
    const intermediate = path.join(temporary, 'obj') + path.sep;
    const build = spawnSync('dotnet', ['build', path.join(root, 'src/host/Meridian.Host.csproj'), '--nologo', '--output', buildRoot,
      `-p:MSBuildProjectExtensionsPath=${intermediate}`, `-p:IntermediateOutputPath=${intermediate}`], { cwd: root, encoding: 'utf8', timeout: 60_000, windowsHide: true });
    assert.equal(build.status, 0, `${build.stdout}\n${build.stderr}`);
    base = `http://127.0.0.1:${await freePort()}`;
    host = spawn('dotnet', [path.join(buildRoot, 'Meridian.Host.dll'), '--data-root', dataRoot, '--urls', base], { cwd: root, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
    host.stdout!.on('data', chunk => { logs = (logs + chunk).slice(-12_000); });
    host.stderr!.on('data', chunk => { logs = (logs + chunk).slice(-12_000); });
    let started = false;
    for (let attempt = 0; attempt < 200; attempt++) {
      if (host.exitCode !== null) assert.fail(`Host exited during startup: ${logs}`);
      try { if ((await fetch(base + '/__meridian_status')).ok) { started = true; break; } } catch { /* Listener is starting. */ }
      await new Promise(resolve => setTimeout(resolve, 50));
    }
    assert.ok(started, `Host did not start: ${logs}`);
    let project = newProject('Stored sizes');
    const automatic = addNode(project, project.entryCanvasId, 'container', 'Automatic');
    const fixed = addNode(project, project.entryCanvasId, 'image', 'Fixed');
    Object.assign(fixed, { sizeMode: 'fixed', width: 1500, height: 750 });
    project = commitSnapshots(project);
    const response = await json('POST', '/api/projects', project);
    assert.equal(response.status, 201, await response.clone().text());
    const committed: MeridianProject = await response.json();
    assert.deepEqual(await read(`/api/projects/${committed.id}`), committed);
    assert.equal(committed.snapshots[automatic.id].node.sizeMode, 'auto');
    assert.equal(committed.snapshots[fixed.id].node.sizeMode, 'fixed');
    const exported = await json('POST', '/api/export', committed); assert.equal(exported.status, 200, await exported.clone().text());
    const body = new FormData(); body.append('file', new Blob([await exported.arrayBuffer()]), 'sizes.meridian');
    const imported = await fetch(base + '/api/import', { method: 'POST', body }); assert.equal(imported.status, 200, await imported.clone().text());
    assert.deepEqual(await imported.json(), committed);
    for (const value of ['stretch', null, true, 3, {}]) {
      for (const savedSnapshot of [false, true]) {
        const invalid = structuredClone(committed), node = savedSnapshot ? invalid.snapshots[automatic.id].node : invalid.nodes.find(node => node.id === automatic.id)!;
        (node as any).sizeMode = value;
        const rejected = await json('PUT', `/api/projects/${committed.id}`, { project: invalid, expectedRevision: committed.revision });
        assert.equal(rejected.status, 400, await rejected.clone().text());
        assert.deepEqual(await read(`/api/projects/${committed.id}`), committed);
      }
    }
    const legacy = structuredClone(project); legacy.id = crypto.randomUUID();
    for (const node of legacy.nodes) delete node.sizeMode;
    for (const snapshot of Object.values(legacy.snapshots)) delete snapshot.node.sizeMode;
    const legacyResponse = await json('POST', '/api/projects', legacy); assert.equal(legacyResponse.status, 201, await legacyResponse.clone().text());
  } finally {
    if (host && host.exitCode === null) { const exit = once(host, 'exit'); host.kill(); await exit; }
    assert.ok(path.resolve(temporary).startsWith(path.resolve(tmpdir()) + path.sep + 'meridian-sizing-schema-tests-'));
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
