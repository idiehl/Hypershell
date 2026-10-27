import assert from 'node:assert/strict';
import { spawn, spawnSync, type ChildProcess } from 'node:child_process';
import { once } from 'node:events';
import { mkdtemp, rm } from 'node:fs/promises';
import { createServer } from 'node:net';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { test } from 'node:test';
import { addNode, newProject } from '../src/core/model.ts';
import { setSnapLock } from '../src/core/clusters.ts';
import type { MeridianProject, StoredModule } from '../src/core/types.ts';

const root = fileURLToPath(new URL('../', import.meta.url));
test('cluster relationships are durable and strictly validated across host storage and template boundaries', { timeout: 120_000 }, async t => {
  const temporary = await mkdtemp(path.join(tmpdir(), 'meridian-cluster-tests-'));
  const buildRoot = path.join(temporary, 'build'), dataRoot = path.join(temporary, 'data');
  let host: ChildProcess | undefined, logs = '', base = '';
  const json = (method: string, route: string, body?: unknown) => fetch(base + route, { method,
    headers: body === undefined ? {} : { 'Content-Type': 'application/json' }, body: body === undefined ? undefined : JSON.stringify(body) });
  async function start() {
    base = `http://127.0.0.1:${await freePort()}`;
    host = spawn('dotnet', [path.join(buildRoot, 'Meridian.Host.dll'), '--data-root', dataRoot, '--urls', base], { cwd: root, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
    host.stdout!.on('data', chunk => { logs = (logs + chunk).slice(-12_000); });
    host.stderr!.on('data', chunk => { logs = (logs + chunk).slice(-12_000); });
    for (let attempt = 0; attempt < 200; attempt++) {
      if (host.exitCode !== null) assert.fail(`Fixture host exited: ${logs}`);
      try { if ((await fetch(base + '/__meridian_status')).ok) return; } catch { /* Isolated listener is starting. */ }
      await new Promise(resolve => setTimeout(resolve, 50));
    }
    assert.fail(`Fixture host did not start: ${logs}`);
  }
  async function stop() { if (!host || host.exitCode !== null) return; const ended = once(host, 'exit'); host.kill(); await ended; host = undefined; }
  try {
    const intermediate = path.join(temporary, 'obj') + path.sep;
    const built = spawnSync('dotnet', ['build', path.join(root, 'src/host/Meridian.Host.csproj'), '--nologo', '--output', buildRoot,
      `-p:MSBuildProjectExtensionsPath=${intermediate}`, `-p:IntermediateOutputPath=${intermediate}`], { cwd: root, encoding: 'utf8', timeout: 60_000, windowsHide: true });
    assert.equal(built.status, 0, `${built.stdout}\n${built.stderr}`);
    await start();
    const original = fixture(); let committed: MeridianProject;
    await t.test('save, reopening, export, import and restarting preserve locked and unlocked contact flags exactly', async () => {
      const created = await json('POST', '/api/projects', original);
      assert.equal(created.status, 201, await created.clone().text()); committed = await created.json();
      assert.deepEqual(committed.links, original.links);
      assert.deepEqual((await (await json('GET', `/api/projects/${committed.id}`)).json()).links, committed.links);
      const exported = await json('POST', '/api/export', committed);
      assert.equal(exported.status, 200, await exported.clone().text());
      const body = new FormData(); body.append('file', new Blob([await exported.arrayBuffer()]), 'cluster.meridian');
      const imported = await fetch(base + '/api/import', { method: 'POST', body });
      assert.equal(imported.status, 200, await imported.clone().text()); assert.deepEqual((await imported.json()).links, committed.links);
      await stop(); await start();
      assert.deepEqual((await (await json('GET', `/api/projects/${committed.id}`)).json()).links, committed.links);
    });
    await t.test('malformed flags, duplicate contacts, roots, directed routes, ancestry and ordered-cell locks reject without changing saved revision', async () => {
      for (const change of [
        (draft: any) => { draft.links[0].snap = null; },
        (draft: any) => { draft.links[0].snap = {}; },
        (draft: any) => { draft.links[0].snap = { locked: 'yes' }; },
        (draft: any) => { draft.links[0].snap.script = 'run'; },
        (draft: any) => { draft.links[0].direction = 'forward'; },
        (draft: any) => { delete draft.links[0].direction; },
        (draft: any) => { draft.links[0].relation = 'causal'; },
        (draft: any) => { draft.links[0].points = []; },
        (draft: any) => { draft.links[0].targetPort = 0; },
        (draft: any) => { draft.links[0].sourceAnchor = { x: .5, y: .5 }; },
        (draft: any) => { draft.links[0].targetId = draft.links[0].sourceId; },
        (draft: any) => { draft.links[0].sourceId = draft.canvases[0].rootId; },
        (draft: any) => { draft.nodes.find((node: any) => node.id === draft.links[0].targetId).parentId = draft.links[0].sourceId; },
        (draft: any) => { draft.nodes.find((node: any) => node.kind === 'module').containerVariant = 'horizontal'; },
        (draft: any) => { draft.nodes.find((node: any) => node.id === draft.links[0].sourceId).parentId = draft.canvases[0].rootId; },
        (draft: any) => { draft.links.push({ ...structuredClone(draft.links[0]), id: crypto.randomUUID(), sourceId: draft.links[0].targetId, targetId: draft.links[0].sourceId }); },
      ]) {
        const invalid = structuredClone(committed!); change(invalid);
        const rejected = await json('PUT', `/api/projects/${committed!.id}`, { project: invalid, expectedRevision: committed!.revision });
        assert.equal(rejected.status, 400, await rejected.clone().text());
        assert.equal((await json('POST', '/api/export', invalid)).status, 400);
      }
      assert.deepEqual(await (await json('GET', `/api/projects/${committed!.id}`)).json(), committed!);
    });
    await t.test('Module Library preserves cluster relations and rejects unsupported snap payloads in stored templates', async () => {
      const module: StoredModule = { schemaVersion: 1, id: crypto.randomUUID(), name: 'Cluster module', createdAt: new Date().toISOString(),
        rootId: original.nodes.find(node => node.kind === 'module')!.id, project: original };
      const response = await json('POST', '/api/modules', { module });
      assert.equal(response.status, 201, await response.clone().text());
      assert.deepEqual((await (await json('GET', `/api/modules/${module.id}`)).json()).project.links, original.links);
      const invalid: any = structuredClone(module); invalid.id = crypto.randomUUID(); invalid.project.links[0].snap.locked = 1;
      assert.equal((await json('POST', '/api/modules', { module: invalid })).status, 400);
    });
  } finally { await stop(); await rm(temporary, { recursive: true, force: true }); }
});

function fixture() {
  const project = newProject('Saved cluster'), canvas = project.canvases[0], parent = addNode(project, canvas.id, 'module', 'Template');
  const a = addNode(project, canvas.id, 'media', 'A'), b = addNode(project, canvas.id, 'media', 'B'), c = addNode(project, canvas.id, 'media', 'C');
  for (const [i, node] of [a, b, c].entries()) Object.assign(node, { parentId: parent.id, x: 120 + i * 100, y: 180, width: 100, height: 100, sizeMode: 'fixed', childPosition: 'manual', style: { shape: 'rectangle' } });
  const locked = setSnapLock(project, a.id, b.id, true);
  locked.links.push({ id: crypto.randomUUID(), canvasId: canvas.id, sourceId: b.id, targetId: c.id, kind: 'visual', relation: 'generic', direction: 'none', snap: { locked: false } });
  return locked;
}
async function freePort(): Promise<number> {
  const server = createServer(); server.listen(0, '127.0.0.1'); await once(server, 'listening');
  const port = (server.address() as { port: number }).port; await new Promise<void>((resolve, reject) => server.close(error => error ? reject(error) : resolve())); return port;
}
