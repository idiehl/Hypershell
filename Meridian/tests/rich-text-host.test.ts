import assert from 'node:assert/strict';
import { spawn, spawnSync, type ChildProcess } from 'node:child_process';
import { once } from 'node:events';
import { mkdtemp, rm } from 'node:fs/promises';
import { createServer } from 'node:net';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { test } from 'node:test';
import { addCanvas, addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import { createModuleFromSelection } from '../src/core/modules.ts';
import { FONT_FAMILIES } from '../src/core/fonts.ts';
import type { MeridianProject } from '../src/core/types.ts';

const root = fileURLToPath(new URL('../', import.meta.url));
test('host persists inert passage formatting and atomically rejects malformed live, snapshot and Module content', { timeout: 120_000 }, async t => {
  const temporary = await mkdtemp(path.join(tmpdir(), 'meridian-rich-text-tests-'));
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
      try { if ((await fetch(base + '/__meridian_status')).ok) return; } catch { /* Owned listener is starting. */ }
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
    await t.test('all fonts, independent title/body overrides, snapshots and exported packages survive storage and restart', async () => {
      const created = await json('POST', '/api/projects', original);
      assert.equal(created.status, 201, await created.clone().text()); committed = await created.json();
      for (const source of original.nodes) {
        const copy = committed.nodes.find(node => node.id === source.id)!;
        assert.deepEqual(copy.titleRuns, source.titleRuns); assert.deepEqual(copy.textRuns, source.textRuns);
      }
      assert.deepEqual(committed.snapshots, JSON.parse(JSON.stringify(original.snapshots)));
      assert.deepEqual(await (await json('GET', `/api/projects/${committed.id}`)).json(), committed);
      const exported = await json('POST', '/api/export', committed);
      assert.equal(exported.status, 200, await exported.clone().text());
      const body = new FormData(); body.append('file', new Blob([await exported.arrayBuffer()]), 'passages.meridian');
      const imported = await fetch(base + '/api/import', { method: 'POST', body });
      assert.equal(imported.status, 200, await imported.clone().text());
      const copy: MeridianProject = await imported.json();
      assert.deepEqual(copy.nodes.map(node => node.titleRuns), committed.nodes.map(node => node.titleRuns));
      assert.deepEqual(copy.nodes.map(node => node.textRuns), committed.nodes.map(node => node.textRuns));
      await stop(); await start();
      assert.deepEqual(await (await json('GET', `/api/projects/${committed.id}`)).json(), committed);
    });
    await t.test('offsets, surrogate splits, CSS/HTML fields, bad fonts/colors/types and too many ranges reject without advancing saved revision', async () => {
      const first = committed!.nodes.find(node => node.kind === 'media')!;
      for (const invalidRuns of invalidFormats()) {
        for (const pinned of [false, true]) {
          const invalid: any = structuredClone(committed!);
          const node = pinned ? invalid.snapshots[first.id].node : invalid.nodes.find((node: any) => node.id === first.id);
          node.textRuns = invalidRuns;
          const rejected = await json('PUT', `/api/projects/${committed!.id}`, { project: invalid, expectedRevision: committed!.revision });
          assert.equal(rejected.status, 400, await rejected.clone().text());
          assert.equal((await json('POST', '/api/export', invalid)).status, 400);
        }
      }
      const invalid: any = structuredClone(committed!); invalid.nodes.find((node: any) => node.id === first.id).titleRuns = [{ start: 0, end: 9999, style: { bold: true } }];
      assert.equal((await json('PUT', `/api/projects/${committed!.id}`, { project: invalid, expectedRevision: committed!.revision })).status, 400);
      assert.deepEqual(await (await json('GET', `/api/projects/${committed!.id}`)).json(), committed!);
    });
    await t.test('immutable Module Library stores passage typography and rejects unsafe template overrides', async () => {
      const source = original.nodes.filter(node => node.kind === 'media').slice(0, 2);
      const module = createModuleFromSelection(original, source.map(node => node.id), 'Formatted passages');
      const response = await json('POST', '/api/modules', { module });
      assert.equal(response.status, 201, await response.clone().text());
      const loaded = await (await json('GET', `/api/modules/${module.id}`)).json();
      assert.deepEqual(loaded.project.nodes.map((node: any) => node.textRuns), module.project.nodes.map(node => node.textRuns));
      for (const runs of invalidFormats()) {
        const invalid: any = structuredClone(module); invalid.id = crypto.randomUUID(); invalid.project.nodes.find((node: any) => node.kind === 'media').textRuns = runs;
        assert.equal((await json('POST', '/api/modules', { module: invalid })).status, 400);
      }
      await stop(); await start();
      assert.deepEqual(await (await json('GET', `/api/modules/${module.id}`)).json(), loaded);
    });
  } finally { await stop(); await rm(temporary, { recursive: true, force: true }); }
});

function fixture() {
  const project = newProject('Saved formatted passages'), canvas = project.canvases[0];
  for (const [index, font] of FONT_FAMILIES.entries()) {
    const node = addNode(project, canvas.id, 'media', `Title for ${font.label}`);
    Object.assign(node, { x: index * 300, y: 100, sizeMode: 'fixed', contentType: 'text', text: 'Hi😀 there\nnew line',
      titleRuns: [{ start: 0, end: 5, style: { fontFamily: font.value, fontSize: 1, bold: false, color: '#123456' } }],
      textRuns: [{ start: 0, end: 2, style: { fontFamily: font.value, fontSize: 512, bold: true, italic: true, underline: true, color: '#ABCDEF' } },
        { start: 2, end: 4, style: { fontSize: 23.5, bold: false, italic: false, underline: false } }] });
  }
  const pinnedCanvas = addCanvas(project, 'Pinned'), reference = addNode(project, pinnedCanvas.id, 'reference', 'Saved passages');
  reference.targetId = project.nodes.find(node => node.kind === 'media')!.id;
  return commitSnapshots(project);
}
function invalidFormats(): unknown[] {
  return [null, {}, [{ start: 0, end: 1, style: { fontFamily: 'Arial' } }], [{ start: 0, end: 1, style: { fontFamily: 'sans; color:red' } }],
    [{ start: 0, end: 1, style: { fontSize: 0 } }], [{ start: 0, end: 1, style: { fontSize: 512.1 } }], [{ start: 0, end: 1, style: { fontSize: '12' } }],
    [{ start: 0, end: 1, style: { color: 'red' } }], [{ start: 0, end: 1, style: { color: '#FFF' } }], [{ start: 0, end: 1, style: { color: 'url(x)' } }],
    [{ start: 0, end: 1, style: { bold: null } }], [{ start: 0, end: 1, style: { italic: 'true' } }], [{ start: 0, end: 1, style: { underline: 1 } }],
    [{ start: 0, end: 1, style: { html: '<img onerror=x>' } }], [{ start: 0, end: 1, style: { css: 'position:absolute' } }],
    [{ start: 0, end: 1, style: {} }], [{ start: -1, end: 1, style: { bold: true } }], [{ start: 0.5, end: 1, style: { bold: true } }],
    [{ start: 0, end: 999, style: { bold: true } }], [{ start: 0, end: 0, style: { bold: true } }], [{ start: 3, end: 4, style: { bold: true } }],
    [{ start: 0, end: 4, style: { bold: true } }, { start: 1, end: 5, style: { italic: true } }],
    [{ start: 0, end: 1, style: { bold: true }, html: 'x' }], Array.from({ length: 4097 }, () => ({ start: 0, end: 1, style: { bold: true } }))];
}
async function freePort(): Promise<number> {
  const server = createServer(); server.listen(0, '127.0.0.1'); await once(server, 'listening');
  const port = (server.address() as { port: number }).port; await new Promise<void>((resolve, reject) => server.close(error => error ? reject(error) : resolve())); return port;
}
