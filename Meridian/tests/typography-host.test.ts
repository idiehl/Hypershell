import assert from 'node:assert/strict';
import { spawn, spawnSync, type ChildProcess } from 'node:child_process';
import { once } from 'node:events';
import { mkdtemp, rm } from 'node:fs/promises';
import { createServer } from 'node:net';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { test } from 'node:test';
import { FONT_FAMILIES } from '../src/core/fonts.ts';
import { addCanvas, addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import { createModuleFromSelection } from '../src/core/modules.ts';
import type { MeridianProject } from '../src/core/types.ts';

const root = fileURLToPath(new URL('../', import.meta.url));
test('host accepts every catalog family and persists bounded text rotation while rejecting unsafe typography atomically', { timeout: 120_000 }, async t => {
  const temporary = await mkdtemp(path.join(tmpdir(), 'meridian-typography-tests-'));
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
    await t.test('all named and legacy fonts save, reopen, export, import and survive backend restart with title and body angles', async () => {
      const created = await json('POST', '/api/projects', original);
      assert.equal(created.status, 201, await created.clone().text()); committed = await created.json();
      for (const source of original.nodes) assert.deepEqual(committed.nodes.find(node => node.id === source.id)?.style, source.style);
      assert.deepEqual(committed.snapshots, JSON.parse(JSON.stringify(original.snapshots)));
      assert.deepEqual(await (await json('GET', `/api/projects/${committed.id}`)).json(), committed);
      const exported = await json('POST', '/api/export', committed);
      assert.equal(exported.status, 200, await exported.clone().text());
      const body = new FormData(); body.append('file', new Blob([await exported.arrayBuffer()]), 'typography.meridian');
      const imported = await fetch(base + '/api/import', { method: 'POST', body });
      assert.equal(imported.status, 200, await imported.clone().text());
      const copy: MeridianProject = await imported.json();
      assert.deepEqual(copy.nodes.map(node => node.style), committed.nodes.map(node => node.style));
      await stop(); await start();
      assert.deepEqual(await (await json('GET', `/api/projects/${committed.id}`)).json(), committed);
    });
    await t.test('wrong types, out-of-range angles, injected families and pinned snapshot styles reject without changing saved revision', async () => {
      const first = committed!.nodes.find(node => node.kind === 'media')!;
      for (const bad of [
        { fontFamily: 'Arial' }, { fontFamily: 'missing-font' }, { fontFamily: 'Arial; color:red' },
        { textRotation: -181 }, { textRotation: 181 }, { textRotation: null }, { textRotation: '90' },
        { titleRotation: false }, { titleRotation: {} }, { titleRotation: 180.001 },
      ]) {
        for (const pinned of [false, true]) {
          const invalid: any = structuredClone(committed!);
          const node = pinned ? invalid.snapshots[first.id].node : invalid.nodes.find((node: any) => node.id === first.id);
          node.style = { ...node.style, ...bad };
          const rejected = await json('PUT', `/api/projects/${committed!.id}`, { project: invalid, expectedRevision: committed!.revision });
          assert.equal(rejected.status, 400, await rejected.clone().text());
          assert.equal((await json('POST', '/api/export', invalid)).status, 400);
        }
      }
      assert.deepEqual(await (await json('GET', `/api/projects/${committed!.id}`)).json(), committed!);
    });
    await t.test('Module Library retains font IDs and angles in reusable templates and validates them on write', async () => {
      const source = original.nodes.filter(node => node.kind === 'media').slice(0, 2);
      const module = createModuleFromSelection(original, source.map(node => node.id), 'Typography');
      const response = await json('POST', '/api/modules', { module });
      assert.equal(response.status, 201, await response.clone().text());
      const loaded = await (await json('GET', `/api/modules/${module.id}`)).json();
      assert.deepEqual(loaded.project.nodes.map((node: any) => node.style), module.project.nodes.map(node => node.style));
      const invalid: any = structuredClone(module); invalid.id = crypto.randomUUID(); invalid.project.nodes.find((node: any) => node.kind === 'media').style.textRotation = '90';
      assert.equal((await json('POST', '/api/modules', { module: invalid })).status, 400);
    });
  } finally { await stop(); await rm(temporary, { recursive: true, force: true }); }
});

function fixture() {
  const project = newProject('Saved typography'), canvas = project.canvases[0];
  for (const [index, font] of FONT_FAMILIES.entries()) {
    const node = addNode(project, canvas.id, 'media', font.label);
    Object.assign(node, { x: index * 300, y: 100, sizeMode: 'fixed', contentType: 'text', text: 'Portable title and body typography.', style: { fontFamily: font.value, textRotation: index % 2 ? -90 : 90, titleRotation: index % 2 ? 180 : -18.5 } });
  }
  const pinnedCanvas = addCanvas(project, 'Pinned'), reference = addNode(project, pinnedCanvas.id, 'reference', 'Saved font');
  reference.targetId = project.nodes.find(node => node.kind === 'media')!.id;
  return commitSnapshots(project);
}
async function freePort(): Promise<number> {
  const server = createServer(); server.listen(0, '127.0.0.1'); await once(server, 'listening');
  const port = (server.address() as { port: number }).port; await new Promise<void>((resolve, reject) => server.close(error => error ? reject(error) : resolve())); return port;
}
