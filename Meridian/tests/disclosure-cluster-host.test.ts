import assert from 'node:assert/strict';
import { spawn, spawnSync, type ChildProcess } from 'node:child_process';
import { once } from 'node:events';
import { mkdtemp, rm } from 'node:fs/promises';
import { createServer } from 'node:net';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import test from 'node:test';
import { addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import type { MeridianProject, StoredModule } from '../src/core/types.ts';

const root = fileURLToPath(new URL('../', import.meta.url));

test('disclosure cluster buttons survive host persistence and reject malformed metadata atomically', { timeout: 120_000 }, async t => {
  const temporary = await mkdtemp(path.join(tmpdir(), 'meridian-disclosure-cluster-tests-'));
  const buildRoot = path.join(temporary, 'build'), dataRoot = path.join(temporary, 'data');
  let host: ChildProcess | undefined, logs = '', base = '';
  const json = (method: string, route: string, body?: unknown) => fetch(base + route, { method,
    headers: body === undefined ? {} : { 'Content-Type': 'application/json' }, body: body === undefined ? undefined : JSON.stringify(body) });
  async function read<T>(route: string): Promise<T> {
    const response = await json('GET', route); assert.equal(response.status, 200, await response.clone().text()); return response.json();
  }
  async function start() {
    base = `http://127.0.0.1:${await freePort()}`;
    host = spawn('dotnet', [path.join(buildRoot, 'Meridian.Host.dll'), '--data-root', dataRoot, '--urls', base], { cwd: root, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
    host.stdout!.on('data', chunk => { logs = (logs + chunk).slice(-12_000); });
    host.stderr!.on('data', chunk => { logs = (logs + chunk).slice(-12_000); });
    for (let attempt = 0; attempt < 200; attempt++) {
      if (host.exitCode !== null) assert.fail(`Fixture host exited: ${logs}`);
      try { if ((await fetch(base + '/__meridian_status')).ok) return; } catch { /* Isolated host is starting. */ }
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
    const original = fixture(), clusterId = original.nodes.find(node => node.cluster)!.id;
    let committed: MeridianProject, draft: MeridianProject, module: StoredModule, checkpointId = '';
    await t.test('square and circle states, child identities, frames and snapshots survive save, portable export and reopening', async () => {
      const response = await json('POST', '/api/projects', original);
      assert.equal(response.status, 201, await response.clone().text()); committed = await response.json();
      assert.deepEqual(committed.nodes, original.nodes); assert.deepEqual(committed.snapshots, { ...original.snapshots });
      const updated = structuredClone(committed); updated.nodes.find(node => node.id === clusterId)!.collapsed = false;
      const saved = await json('PUT', `/api/projects/${committed.id}`, { project: updated, expectedRevision: committed.revision });
      assert.equal(saved.status, 200, await saved.clone().text()); committed = await saved.json();
      assert.deepEqual(committed.nodes, updated.nodes); assert.deepEqual(await read(`/api/projects/${committed.id}`), committed);
      const exported = await json('POST', '/api/export', committed); assert.equal(exported.status, 200, await exported.clone().text());
      const body = new FormData(); body.append('file', new Blob([await exported.arrayBuffer()]), 'disclosure-clusters.meridian');
      const imported = await fetch(base + '/api/import', { method: 'POST', body });
      assert.equal(imported.status, 200, await imported.clone().text()); assert.deepEqual(await imported.json(), committed);
    });
    await t.test('recovery, checkpoints and Module Library preserve cluster state after the isolated backend restarts', async () => {
      draft = structuredClone(committed!); draft.nodes.find(node => node.id === clusterId)!.cluster!.buttonShape = 'circle';
      draft.nodes.find(node => node.id === clusterId)!.collapsed = true;
      const recovery = await json('PUT', `/api/projects/${committed!.id}/recovery`, { project: draft, baseRevision: committed!.revision });
      assert.equal(recovery.status, 200, await recovery.clone().text());
      const checkpoint = await json('POST', `/api/projects/${committed!.id}/checkpoints`, { name: 'Cluster button experiment', project: draft });
      assert.equal(checkpoint.status, 201, await checkpoint.clone().text()); checkpointId = (await checkpoint.json()).id;
      const moduleProject = structuredClone(original); moduleProject.snapshots = {};
      module = { schemaVersion: 1, id: crypto.randomUUID(), name: 'Disclosure assemblies', createdAt: new Date().toISOString(),
        rootId: moduleProject.nodes.find(node => node.kind === 'module')!.id, project: moduleProject };
      const stored = await json('POST', '/api/modules', { module }); assert.equal(stored.status, 201, await stored.clone().text());
      await stop(); await start();
      assert.deepEqual(await read(`/api/projects/${committed!.id}`), committed!);
      assert.deepEqual((await read<{ project: MeridianProject }>(`/api/projects/${committed!.id}/recovery`)).project, draft);
      assert.deepEqual(await read(`/api/projects/${committed!.id}/checkpoints/${checkpointId}`), draft);
      assert.deepEqual((await read<StoredModule>(`/api/modules/${module.id}`)).project.nodes, module.project.nodes);
    });
    await t.test('malformed metadata is rejected on live nodes, snapshots, exports and stored modules without overwriting valid state', async () => {
      for (const cluster of [null, true, 1, 'square', [], {}, { buttonShape: null }, { buttonShape: 1 }, { buttonShape: 'rounded' },
        { buttonShape: 'Circle' }, { buttonShape: 'square', script: 'run' }]) {
        for (const snapshot of [false, true]) {
          const invalid: any = structuredClone(committed!);
          (snapshot ? invalid.snapshots[clusterId].node : invalid.nodes.find((node: any) => node.id === clusterId)).cluster = cluster;
          const rejected = await json('PUT', `/api/projects/${committed!.id}`, { project: invalid, expectedRevision: committed!.revision });
          assert.equal(rejected.status, 400, `${snapshot ? 'snapshot' : 'live'} ${JSON.stringify(cluster)}: ${await rejected.clone().text()}`);
          assert.equal((await json('POST', '/api/export', invalid)).status, 400);
        }
        const invalidModule: any = structuredClone(module!); invalidModule.id = crypto.randomUUID();
        invalidModule.project.nodes.find((node: any) => node.id === clusterId).cluster = cluster;
        assert.equal((await json('POST', '/api/modules', { module: invalidModule })).status, 400);
      }
      assert.deepEqual(await read(`/api/projects/${committed!.id}`), committed!);
      assert.deepEqual((await read<{ project: MeridianProject }>(`/api/projects/${committed!.id}/recovery`)).project, draft!);
      assert.deepEqual(await read(`/api/projects/${committed!.id}/checkpoints/${checkpointId}`), draft!);
      assert.equal((await read<unknown[]>('/api/modules')).length, 1);
    });
    await t.test('cluster metadata requires an unordered contained container on both live and saved nodes', async () => {
      for (const change of [
        (node: any) => { node.kind = 'media'; },
        (node: any) => { node.containerVariant = 'horizontal'; },
        (node: any) => { node.containerVariant = null; },
        (node: any) => { node.childLayout = 'spatial'; },
        (node: any) => { node.childLayout = null; },
        (node: any) => { node.childArrangement = 'row'; },
        (node: any) => { node.childArrangement = null; },
        (node: any) => { node.grid = { columns: 1, rows: 1, cellWidth: 50, cellHeight: 50 }; },
        (node: any) => { node.grid = null; },
      ]) for (const snapshot of [false, true]) {
        const invalid: any = structuredClone(committed!);
        change(snapshot ? invalid.snapshots[clusterId].node : invalid.nodes.find((node: any) => node.id === clusterId));
        assert.equal((await json('PUT', `/api/projects/${committed!.id}`, { project: invalid, expectedRevision: committed!.revision })).status, 400);
      }
      assert.deepEqual(await read(`/api/projects/${committed!.id}`), committed!);
    });
    await t.test('omitted Freeform settings and the legacy spatial pattern remain readable without manufacturing defaults', async () => {
      for (const legacy of [false, true]) {
        const project = fixture(), cluster = project.nodes.find(node => node.cluster)!;
        delete cluster.containerVariant; delete cluster.childLayout; delete cluster.childArrangement;
        if (legacy) cluster.containerVariant = 'spatial';
        const response = await json('POST', '/api/projects', project); assert.equal(response.status, 201, await response.clone().text());
        const saved: MeridianProject = await response.json(); assert.deepEqual(saved.nodes, project.nodes);
      }
    });
  } finally { await stop(); await rm(temporary, { recursive: true, force: true }); }
});

function fixture() {
  const project = newProject('Disclosure clusters'), canvas = project.canvases[0], parent = addNode(project, canvas.id, 'module', 'Reusable assembly');
  Object.assign(parent, { x: 20, y: 20, width: 1000, height: 700, sizeMode: 'fixed' });
  for (const [index, buttonShape] of (['square', 'circle'] as const).entries()) {
    const cluster = addNode(project, canvas.id, 'container', `Cluster ${buttonShape}`);
    Object.assign(cluster, { parentId: parent.id, x: 80 + index * 450, y: 80, width: 64, height: 64, sizeMode: 'fixed', collapsed: true,
      cluster: { buttonShape }, containerSizes: { expanded: { width: 360, height: 260, sizeMode: 'fixed' }, collapsed: { width: 64, height: 64, sizeMode: 'fixed' } } });
    for (const [childIndex, name] of ['Picture', 'Caption'].entries()) {
      const child = addNode(project, canvas.id, 'media', `${buttonShape} ${name}`);
      Object.assign(child, { parentId: cluster.id, x: cluster.x + 20 + childIndex * 160, y: cluster.y + 20, width: 140, height: 100,
        sizeMode: 'fixed', childPosition: 'manual', text: 'Preserve original contents' });
    }
  }
  return commitSnapshots(project);
}

async function freePort(): Promise<number> {
  const server = createServer(); server.listen(0, '127.0.0.1'); await once(server, 'listening');
  const port = (server.address() as { port: number }).port;
  await new Promise<void>((resolve, reject) => server.close(error => error ? reject(error) : resolve())); return port;
}
