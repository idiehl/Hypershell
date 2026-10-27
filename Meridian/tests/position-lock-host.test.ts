import assert from 'node:assert/strict';
import { spawn, spawnSync, type ChildProcess } from 'node:child_process';
import { once } from 'node:events';
import { mkdtemp, rm } from 'node:fs/promises';
import { createServer } from 'node:net';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { test } from 'node:test';
import { addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import { normalizeLibraryDocument } from '../src/core/library.ts';
import { createModuleFromSelection } from '../src/core/modules.ts';
import type { MeridianProject } from '../src/core/types.ts';

const root = fileURLToPath(new URL('../', import.meta.url));
test('position locks persist as inert snapshot data across project, home canvas, recovery and Module storage', { timeout: 120_000 }, async t => {
  const temporary = await mkdtemp(path.join(tmpdir(), 'meridian-position-lock-tests-')), buildRoot = path.join(temporary, 'build'), dataRoot = path.join(temporary, 'data');
  let host: ChildProcess | undefined, logs = '', base = '';
  const json = (method: string, route: string, body?: unknown) => fetch(base + route, { method,
    headers: body === undefined ? {} : { 'Content-Type': 'application/json' }, body: body === undefined ? undefined : JSON.stringify(body) });
  async function start() {
    base = `http://127.0.0.1:${await freePort()}`;
    host = spawn('dotnet', [path.join(buildRoot, 'Meridian.Host.dll'), '--data-root', dataRoot, '--urls', base], { cwd: root, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
    host.stdout!.on('data', chunk => { logs = (logs + chunk).slice(-12_000); }); host.stderr!.on('data', chunk => { logs = (logs + chunk).slice(-12_000); });
    for (let attempt = 0; attempt < 200; attempt++) {
      if (host.exitCode !== null) assert.fail(`Fixture host exited: ${logs}`);
      try { if ((await fetch(base + '/__meridian_status')).ok) return; } catch { /* Only the owned listener is starting. */ }
      await new Promise(resolve => setTimeout(resolve, 50));
    }
    assert.fail(`Fixture host did not start: ${logs}`);
  }
  async function stop() { if (!host || host.exitCode !== null) return; const ended = once(host, 'exit'); host.kill(); await ended; host = undefined; }
  try {
    const intermediate = path.join(temporary, 'obj') + path.sep;
    const built = spawnSync('dotnet', ['build', path.join(root, 'src/host/Meridian.Host.csproj'), '--nologo', '--output', buildRoot,
      `-p:MSBuildProjectExtensionsPath=${intermediate}`, `-p:IntermediateOutputPath=${intermediate}`], { cwd: root, encoding: 'utf8', timeout: 60_000, windowsHide: true });
    assert.equal(built.status, 0, `${built.stdout}\n${built.stderr}`); await start();
    const original = newProject('Anchored positions'), anchor = addNode(original, original.entryCanvasId, 'media', 'Anchor'), unlocked = addNode(original, original.entryCanvasId, 'media', 'Unlocked');
    Object.assign(anchor, { x: 100, y: 50, positionLocked: true, childPosition: 'manual', placement: 'free' }); unlocked.positionLocked = false;
    let saved: MeridianProject;
    await t.test('live, pinned, exported and recovered lock values survive restart without imposing lock history on snapshots', async () => {
      const created = await json('POST', '/api/projects', commitSnapshots(original)); assert.equal(created.status, 201, await created.clone().text()); saved = await created.json();
      assert.equal(saved.nodes.find(node => node.id === anchor.id)!.positionLocked, true); assert.equal(saved.nodes.find(node => node.id === unlocked.id)!.positionLocked, false);
      assert.equal(saved.snapshots[anchor.id].node.positionLocked, true);
      const exported = await json('POST', '/api/export', saved); assert.equal(exported.status, 200, await exported.clone().text());
      const form = new FormData(); form.append('file', new Blob([await exported.arrayBuffer()]), 'anchored.meridian');
      const imported = await fetch(base + '/api/import', { method: 'POST', body: form }); assert.equal(imported.status, 200, await imported.clone().text());
      assert.equal((await imported.json()).nodes.find((node: any) => node.title === 'Anchor').positionLocked, true);
      // Storage admits complete historical/Undo snapshots and coalesced unlock,
      // movement and relock. Interactive/core transactions enforce anchoring.
      const restored = structuredClone(saved); restored.nodes.find(node => node.id === anchor.id)!.x = 230;
      const response = await json('PUT', `/api/projects/${saved.id}`, { project: restored, expectedRevision: saved.revision });
      assert.equal(response.status, 200, await response.clone().text()); saved = await response.json();
      const draft = structuredClone(saved); draft.nodes.find(node => node.id === anchor.id)!.text = 'Anchored draft contents';
      const recovery = await json('PUT', `/api/projects/${saved.id}/recovery`, { project: draft, baseRevision: saved.revision, expectedSavedAt: null });
      assert.equal(recovery.status, 200, await recovery.clone().text());
      await stop(); await start(); assert.deepEqual(await (await json('GET', `/api/projects/${saved.id}`)).json(), saved);
      assert.equal((await (await json('GET', `/api/projects/${saved.id}/recovery`)).json()).project.nodes.find((node: any) => node.id === anchor.id).positionLocked, true);
    });
    await t.test('malformed live and snapshot flags reject atomically and retain the saved revision', async () => {
      for (const value of [null, 0, 'true', {}]) for (const snapshot of [false, true]) {
        const malformed: any = structuredClone(saved!);
        (snapshot ? malformed.snapshots[anchor.id].node : malformed.nodes.find((node: any) => node.id === anchor.id)).positionLocked = value;
        const rejected = await json('PUT', `/api/projects/${saved!.id}`, { project: malformed, expectedRevision: saved!.revision });
        assert.equal(rejected.status, 400, await rejected.clone().text()); assert.equal((await json('POST', '/api/export', malformed)).status, 400);
      }
      assert.deepEqual(await (await json('GET', `/api/projects/${saved!.id}`)).json(), saved!);
    });
    await t.test('home canvas and Module templates retain independent position locks', async () => {
      const library = normalizeLibraryDocument({ schemaVersion: 1, revision: 0, entries: [] }), node = addNode(library.project, library.project.entryCanvasId, 'media', 'Home anchor'); node.positionLocked = true;
      const response = await json('PUT', '/api/library', { library, expectedRevision: 0 }); assert.equal(response.status, 200, await response.clone().text());
      const stored = await response.json(); assert.equal(stored.project.nodes.find((item: any) => item.id === node.id).positionLocked, true);
      const module = createModuleFromSelection(original, [anchor.id], 'Anchored Module');
      const created = await json('POST', '/api/modules', { module }); assert.equal(created.status, 201, await created.clone().text());
      assert.equal((await (await json('GET', `/api/modules/${module.id}`)).json()).project.nodes.find((item: any) => item.title === 'Anchor').positionLocked, true);
      await stop(); await start(); assert.deepEqual(await (await json('GET', '/api/library')).json(), stored);
    });
  } finally { await stop(); await rm(temporary, { recursive: true, force: true }); }
});

async function freePort(): Promise<number> {
  const server = createServer(); await new Promise<void>((resolve, reject) => { server.once('error', reject); server.listen(0, '127.0.0.1', resolve); });
  const address = server.address(); assert.ok(address && typeof address !== 'string'); const port = address.port;
  await new Promise<void>((resolve, reject) => server.close(error => error ? reject(error) : resolve())); return port;
}
