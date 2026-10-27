import assert from 'node:assert/strict';
import { spawn, spawnSync, type ChildProcess } from 'node:child_process';
import { once } from 'node:events';
import { mkdtemp, rm } from 'node:fs/promises';
import { createServer } from 'node:net';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import test from 'node:test';
import { PORTAL_STYLES } from '../src/core/appearance.ts';
import { addCanvas, addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import { createModuleFromSelection } from '../src/core/modules.ts';
import type { MeridianProject, StoredModule } from '../src/core/types.ts';

const root = fileURLToPath(new URL('../', import.meta.url));

test('host stores all portal styles, retains them across portable workflows and rejects malformed appearance atomically', { timeout: 120_000 }, async t => {
  const temporary = await mkdtemp(path.join(tmpdir(), 'meridian-portal-style-tests-'));
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
    let committed: MeridianProject, draft: MeridianProject, module: StoredModule, checkpointId = '';
    await t.test('save, reopen, export/import and Module Library retain workspace defaults and every node override', async () => {
      const original = fixture(), response = await json('POST', '/api/projects', original);
      assert.equal(response.status, 201, await response.clone().text()); committed = await response.json();
      assertStyles(committed, original);
      const saved = await json('PUT', `/api/projects/${committed.id}`, { project: { ...committed, title: 'Saved portal choices' }, expectedRevision: committed.revision });
      assert.equal(saved.status, 200, await saved.clone().text()); committed = await saved.json();
      assertStyles(committed, original); assert.deepEqual(await read(`/api/projects/${committed.id}`), committed);
      const exported = await json('POST', '/api/export', committed);
      assert.equal(exported.status, 200, await exported.clone().text());
      const body = new FormData(); body.append('file', new Blob([await exported.arrayBuffer()]), 'portals.meridian');
      const imported = await fetch(base + '/api/import', { method: 'POST', body });
      assert.equal(imported.status, 200, await imported.clone().text()); assertStyles(await imported.json(), committed);
      module = createModuleFromSelection(committed, committed.nodes.filter(node => node.kind === 'canvas-link').map(node => node.id), 'Portal choices');
      const stored = await json('POST', '/api/modules', { module }); assert.equal(stored.status, 201, await stored.clone().text());
      const loaded = await read<StoredModule>(`/api/modules/${module.id}`);
      assert.deepEqual(loaded.project.nodes.map(node => node.style), module.project.nodes.map(node => node.style));
    });
    await t.test('recovery, checkpoints, committed snapshots and Module templates survive backend restart', async () => {
      draft = structuredClone(committed!); draft.canvases[0].style!.portalStyle = 'vortex'; draft.canvases[0].style!.portalColor = '#55aacc';
      draft.nodes.find(node => node.kind === 'canvas-link')!.style!.portalStyle = 'wormhole'; draft.nodes.find(node => node.kind === 'canvas-link')!.style!.portalColor = '#ee6677';
      const recovery = await json('PUT', `/api/projects/${committed!.id}/recovery`, { project: draft, baseRevision: committed!.revision });
      assert.equal(recovery.status, 200, await recovery.clone().text());
      const checkpoint = await json('POST', `/api/projects/${committed!.id}/checkpoints`, { name: 'Portal experiment', project: draft });
      assert.equal(checkpoint.status, 201, await checkpoint.clone().text()); checkpointId = (await checkpoint.json()).id;
      await stop(); await start();
      assert.deepEqual(await read(`/api/projects/${committed!.id}`), committed!);
      assert.deepEqual((await read<{ project: MeridianProject }>(`/api/projects/${committed!.id}/recovery`)).project, draft);
      assert.deepEqual(await read(`/api/projects/${committed!.id}/checkpoints/${checkpointId}`), draft);
      const loaded = await read<StoredModule>(`/api/modules/${module!.id}`);
      assert.deepEqual(loaded.project.nodes.map(node => node.style), module!.project.nodes.map(node => node.style));
    });
    await t.test('wrong types and unknown portal choices reject on live nodes, snapshots, canvases and modules without changing stored data', async () => {
      const first = committed!.nodes.find(node => node.kind === 'canvas-link')!;
      for (const portalStyle of ['', 'door', 'DOORWAY', 'url(https://example.test/a.svg)', 1, false, null, [], {}]) {
        for (const surface of ['node', 'snapshot', 'canvas']) {
          const invalid: any = structuredClone(committed!);
          const owner = surface === 'canvas' ? invalid.canvases[0] : surface === 'snapshot'
            ? invalid.snapshots[first.id].node : invalid.nodes.find((node: any) => node.id === first.id);
          owner.style = { ...owner.style, portalStyle };
          const rejected = await json('PUT', `/api/projects/${committed!.id}`, { project: invalid, expectedRevision: committed!.revision });
          assert.equal(rejected.status, 400, `${surface}: ${JSON.stringify(portalStyle)} ${await rejected.clone().text()}`);
          assert.equal((await json('POST', '/api/export', invalid)).status, 400);
        }
      }
      for (const surface of ['node', 'canvas']) {
        const invalid: any = structuredClone(module!); invalid.id = crypto.randomUUID();
        const owner = surface === 'canvas' ? invalid.project.canvases[0] : invalid.project.nodes.find((node: any) => node.kind === 'canvas-link');
        owner.style = { ...owner.style, portalStyle: 'unknown' };
        assert.equal((await json('POST', '/api/modules', { module: invalid })).status, 400);
      }
      for (const surface of ['node', 'canvas']) {
        const invalid: any = structuredClone(committed!);
        const owner = surface === 'canvas' ? invalid.canvases[0] : invalid.nodes.find((node: any) => node.id === first.id);
        owner.style = { ...owner.style, portalSpeed: 4 };
        assert.equal((await json('PUT', `/api/projects/${committed!.id}`, { project: invalid, expectedRevision: committed!.revision })).status, 400);
      }
      assert.deepEqual(await read(`/api/projects/${committed!.id}`), committed!);
      assert.deepEqual((await read<{ project: MeridianProject }>(`/api/projects/${committed!.id}/recovery`)).project, draft!);
      assert.deepEqual(await read(`/api/projects/${committed!.id}/checkpoints/${checkpointId}`), draft!);
      assert.equal((await read<unknown[]>('/api/modules')).length, 1);
    });
    await t.test('invalid portal colors reject on nodes, snapshots, canvas defaults and Module templates atomically', async () => {
      const first = committed!.nodes.find(node => node.kind === 'canvas-link')!;
      for (const portalColor of ['', 'red', '#123', '#12345678', '123456', '#12GG34', 'url(x)', null, 12, false, {}, []]) {
        for (const surface of ['node', 'snapshot', 'canvas']) {
          const invalid: any = structuredClone(committed!);
          const owner = surface === 'canvas' ? invalid.canvases[0] : surface === 'snapshot' ? invalid.snapshots[first.id].node : invalid.nodes.find((node: any) => node.id === first.id);
          owner.style = { ...owner.style, portalColor };
          assert.equal((await json('PUT', `/api/projects/${committed!.id}`, { project: invalid, expectedRevision: committed!.revision })).status, 400);
          assert.equal((await json('POST', '/api/export', invalid)).status, 400);
        }
        const badModule: any = structuredClone(module!); badModule.id = crypto.randomUUID(); badModule.project.nodes.find((node: any) => node.kind === 'canvas-link').style.portalColor = portalColor;
        assert.equal((await json('POST', '/api/modules', { module: badModule })).status, 400);
      }
      assert.deepEqual(await read(`/api/projects/${committed!.id}`), committed!);
      assert.deepEqual((await read<{ project: MeridianProject }>(`/api/projects/${committed!.id}/recovery`)).project, draft!);
      assert.deepEqual(await read(`/api/projects/${committed!.id}/checkpoints/${checkpointId}`), draft!);
      assert.equal((await read<unknown[]>('/api/modules')).length, 1);
    });
  } finally { await stop(); await rm(temporary, { recursive: true, force: true }); }
});

function fixture() {
  const project = newProject('Portal persistence'); project.canvases[0].style = { portalStyle: 'passage', portalColor: '#deaf65' };
  const pinned = addCanvas(project, 'Pinned choices'); pinned.style = { portalStyle: 'vortex', portalColor: '#66aacc' };
  for (const [index, style] of PORTAL_STYLES.entries()) {
    const node = addNode(project, project.entryCanvasId, 'canvas-link', style.label);
    Object.assign(node, { x: 60 + index * 300, y: 80, width: 240, height: 180, sizeMode: 'fixed',
      canvasTarget: { projectId: crypto.randomUUID(), canvasId: crypto.randomUUID() }, style: { portalStyle: style.value, portalColor: ['#cc3366', '#3377cc', '#000000', '#ffffff'][index] } });
    const reference = addNode(project, pinned.id, 'reference', `Pinned ${style.label}`); reference.targetId = node.id;
  }
  return commitSnapshots(project);
}
function assertStyles(actual: MeridianProject, expected: MeridianProject) {
  assert.deepEqual(actual.canvases.map(canvas => canvas.style), expected.canvases.map(canvas => canvas.style));
  for (const source of expected.nodes.filter(node => node.kind === 'canvas-link')) {
    const copy = actual.nodes.find(node => node.tag === source.tag)!;
    assert.equal(copy.style?.portalStyle, source.style?.portalStyle);
    assert.equal(copy.style?.portalColor, source.style?.portalColor);
    assert.equal(actual.snapshots[copy.id].node.style?.portalStyle, source.style?.portalStyle);
    assert.equal(actual.snapshots[copy.id].node.style?.portalColor, source.style?.portalColor);
  }
}
async function freePort(): Promise<number> {
  const server = createServer(); server.listen(0, '127.0.0.1'); await once(server, 'listening');
  const port = (server.address() as { port: number }).port;
  await new Promise<void>((resolve, reject) => server.close(error => error ? reject(error) : resolve())); return port;
}
