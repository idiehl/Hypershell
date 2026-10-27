import assert from 'node:assert/strict';
import { spawn, spawnSync, type ChildProcess } from 'node:child_process';
import { once } from 'node:events';
import { mkdtemp, readFile, rm } from 'node:fs/promises';
import { createServer } from 'node:net';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { inflateRawSync } from 'node:zlib';
import { test } from 'node:test';
import type { MeridianProject } from '../src/core/types.ts';

const root = fileURLToPath(new URL('../', import.meta.url));
const hostProject = path.join(root, 'src', 'host', 'Meridian.Host.csproj');
const dll = path.join(root, 'src', 'host', 'bin', 'Debug', 'net10.0', 'Meridian.Host.dll');
const testPackaged = process.env.MERIDIAN_TEST_PACKAGED === '1';
const packagedExecutable = path.join(root, 'output', 'app', 'Meridian.Host.exe');

test('local host preserves projects and recovery, rejects conflicts, and round-trips portable packages', { timeout: 120_000 }, async t => {
  if (!testPackaged) {
    const build = spawnSync('dotnet', ['build', hostProject, '--nologo'], { cwd: root, encoding: 'utf8', timeout: 60_000 });
    assert.equal(build.status, 0, `${build.stdout}\n${build.stderr}`);
  }
  const temporary = await mkdtemp(path.join(tmpdir(), 'meridian-host-tests-'));
  const dataRoot = path.join(temporary, 'data');
  let host: ChildProcess | undefined;
  let base = '';
  let logs = '';
  async function start() {
    const port = await freePort();
    base = `http://127.0.0.1:${port}`;
    host = spawn(testPackaged ? packagedExecutable : 'dotnet', [...(testPackaged ? [] : [dll]), '--data-root', dataRoot, '--urls', base], { cwd: root, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
    logs = '';
    host.stdout!.on('data', chunk => { logs = (logs + chunk).slice(-12_000); });
    host.stderr!.on('data', chunk => { logs = (logs + chunk).slice(-12_000); });
    for (let attempt = 0; attempt < 200; attempt++) {
      if (host.exitCode !== null) assert.fail(`Host exited during startup: ${logs}`);
      try {
        const response = await fetch(`${base}/__meridian_status`);
        if (response.ok && response.headers.get('X-Atlas-Meridian') === 'true') return;
      } catch { /* The listener is still starting. */ }
      await new Promise(resolve => setTimeout(resolve, 50));
    }
    assert.fail(`Host failed to become healthy: ${logs}`);
  }
  async function stop() {
    if (!host || host.exitCode !== null) return;
    const exited = once(host, 'exit');
    host.kill();
    await exited;
    host = undefined;
  }
  async function json(method: string, route: string, body?: unknown, extraHeaders: Record<string, string> = {}) {
    return fetch(base + route, {
      method,
      headers: { ...(body === undefined ? {} : { 'Content-Type': 'application/json' }), ...extraHeaders },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
  }
  async function imported(bytes: Buffer) {
    const form = new FormData();
    form.append('file', new Blob([new Uint8Array(bytes)], { type: 'application/octet-stream' }), 'shared.meridian');
    return fetch(base + '/api/import', { method: 'POST', body: form });
  }
  let project = fixture();
  let checkpointId = '';
  let draft: MeridianProject;
  try {
    await start();
    if (testPackaged) await t.test('published package includes runnable browser assets and a valid MyNet manifest', async () => {
      const page = await fetch(base + '/');
      assert.equal(page.status, 200);
      assert.match(await page.text(), /Atlas Meridian/);
      for (const asset of ['editor.js', 'app.css', 'viewer.js', 'scene.css']) {
        const response = await fetch(base + '/' + asset);
        assert.equal(response.status, 200, asset);
        assert.ok((await response.text()).length > 100, asset);
      }
      const manifest = JSON.parse(await readFile(path.join(root, 'output', 'meridian.app.json'), 'utf8'));
      assert.equal(manifest.schemaVersion, 1);
      assert.equal(manifest.apps[0].id, 'atlas.meridian');
      assert.equal(path.resolve(manifest.apps[0].launch.executable), packagedExecutable);
      assert.deepEqual(manifest.apps[0].launch.arguments, ['--urls', 'http://127.0.0.1:{port}']);
      assert.deepEqual(manifest.apps[0].ports, Array.from({ length: 10 }, (_, index) => 5240 + index));
      assert.equal(manifest.apps[0].allowExternalResources, false);
    });
    await t.test('create and catalog save managed content with revision one', async () => {
      const result = await json('POST', '/api/projects', project);
      assert.equal(result.status, 201, await errorText(result));
      project = await result.json() as MeridianProject;
      assert.equal(project.revision, 1);
      const catalog = await (await json('GET', '/api/projects')).json();
      assert.equal(catalog.length, 1);
      assert.equal(catalog[0].id, project.id);
      assert.ok(catalog[0].updatedAt);
      assert.deepEqual(await (await json('GET', `/api/projects/${project.id}`)).json(), project);
    });
    await t.test('revision conflict never overwrites the last committed version', async () => {
      const response = await json('PUT', `/api/projects/${project.id}`, { project: { ...project, title: 'Stale version', revision: 0 }, expectedRevision: 0 });
      assert.equal(response.status, 409);
      assert.match((await response.json()).error, /changed/);
      assert.equal((await (await json('GET', `/api/projects/${project.id}`)).json()).title, project.title);
      const saved = await json('PUT', `/api/projects/${project.id}`, { project: { ...project, title: 'Committed version' }, expectedRevision: 1 });
      assert.equal(saved.status, 200, await errorText(saved));
      project = await saved.json() as MeridianProject;
      assert.equal(project.revision, 2);
    });
    await t.test('recovery and named checkpoint preserve unsaved content independently of committed state', async () => {
      draft = { ...structuredClone(project), title: 'Unsaved recovered title' };
      const recovery = await json('PUT', `/api/projects/${project.id}/recovery`, { project: draft, baseRevision: project.revision });
      assert.equal(recovery.status, 200, await errorText(recovery));
      const read = await (await json('GET', `/api/projects/${project.id}/recovery`)).json();
      assert.deepEqual(read.project, draft);
      assert.equal(read.baseRevision, 2);
      assert.ok(read.savedAt);
      const competing = await json('PUT', `/api/projects/${project.id}/recovery`, { project: { ...draft, title: 'Another window' }, baseRevision: 2 });
      assert.equal(competing.status, 409, 'A first autosave cannot overwrite another window recovery');
      const refreshed = await json('PUT', `/api/projects/${project.id}/recovery`, { project: draft, baseRevision: 2, expectedSavedAt: read.savedAt });
      assert.equal(refreshed.status, 200, await errorText(refreshed));
      const refreshedDraft = await refreshed.json();
      assert.notEqual(refreshedDraft.savedAt, read.savedAt);
      const staleDelete = await json('DELETE', `/api/projects/${project.id}/recovery`, { baseRevision: 2, savedAt: read.savedAt });
      assert.equal(staleDelete.status, 409, 'A stale discard cannot delete a newer autosave');
      const staleWrite = await json('PUT', `/api/projects/${project.id}/recovery`, { project: draft, baseRevision: 2, expectedSavedAt: read.savedAt });
      assert.equal(staleWrite.status, 409, 'A stale token cannot replace a newer recovery');
      assert.equal((await (await json('GET', `/api/projects/${project.id}`)).json()).title, 'Committed version');
      const checkpoint = await json('POST', `/api/projects/${project.id}/checkpoints`, { name: 'Before review', project: draft });
      assert.equal(checkpoint.status, 201, await errorText(checkpoint));
      checkpointId = (await checkpoint.json()).id;
      const checkpoints = await (await json('GET', `/api/projects/${project.id}/checkpoints`)).json();
      assert.equal(checkpoints[0].name, 'Before review');
      assert.deepEqual(await (await json('GET', `/api/projects/${project.id}/checkpoints/${checkpointId}`)).json(), draft);
    });
    await t.test('guarded authoring save refuses a recovery draft atomically without changing either version', async () => {
      const status=await(await json('GET','/__meridian_status')).json();
      assert.ok(status.capabilities.includes('authoring-save-guard-v1'));
      const guarded=await json('PUT',`/api/projects/${project.id}`,{project:{...project,title:'External composition'},expectedRevision:project.revision,requireNoRecovery:true});
      assert.equal(guarded.status,409);
      assert.deepEqual(await(await json('GET',`/api/projects/${project.id}`)).json(),project);
      assert.deepEqual((await(await json('GET',`/api/projects/${project.id}/recovery`)).json()).project,draft!);
      const malformed=await json('PUT',`/api/projects/${project.id}`,{project,expectedRevision:project.revision,requireNoRecovery:'true'});
      assert.equal(malformed.status,400);
    });
    await t.test('restart retains committed project, recovery and checkpoint', async () => {
      await stop();
      await start();
      assert.deepEqual(await (await json('GET', `/api/projects/${project.id}`)).json(), project);
      assert.deepEqual((await (await json('GET', `/api/projects/${project.id}/recovery`)).json()).project, draft!);
      assert.deepEqual(await (await json('GET', `/api/projects/${project.id}/checkpoints/${checkpointId}`)).json(), draft!);
      const restored = await json('PUT', `/api/projects/${project.id}`, { project: draft!, expectedRevision: 2 });
      assert.equal(restored.status, 200, await errorText(restored));
      project = await restored.json() as MeridianProject;
      assert.equal(project.revision, 3);
      assert.equal((await json('GET', `/api/projects/${project.id}/recovery`)).status, 404);
    });
    await t.test('simultaneous saves have one winner and preserve the other draft', async () => {
      const responses = await Promise.all(['Window A', 'Window B'].map(title => json('PUT', `/api/projects/${project.id}`, { project: { ...project, title }, expectedRevision: 3 })));
      assert.deepEqual(responses.map(response => response.status).sort(), [200, 409]);
      project = await responses.find(response => response.status === 200)!.json() as MeridianProject;
      assert.equal(project.revision, 4);
      const laterDraft = { ...project, title: 'Later unsaved work' };
      assert.equal((await json('PUT', `/api/projects/${project.id}/recovery`, { project: laterDraft, baseRevision: 4 })).status, 200);
      const saved = await json('PUT', `/api/projects/${project.id}`, { project, expectedRevision: 4 });
      assert.equal(saved.status, 200);
      project = await saved.json() as MeridianProject;
      assert.equal((await (await json('GET', `/api/projects/${project.id}/recovery`)).json()).project.title, 'Later unsaved work');
      const recovery = await (await json('GET', `/api/projects/${project.id}/recovery`)).json();
      const discarded = await json('DELETE', `/api/projects/${project.id}/recovery`, { baseRevision: recovery.baseRevision, savedAt: recovery.savedAt });
      assert.equal(discarded.status, 204);
      assert.equal((await json('GET', `/api/projects/${project.id}/recovery`)).status, 404);
    });
    await t.test('ZIP holds actual asset bytes and reconstructs identical project plus snapshot assets', async () => {
      const exported = await json('POST', '/api/export', project);
      assert.equal(exported.status, 200, await errorText(exported));
      const bytes = Buffer.from(await exported.arrayBuffer());
      const entries = unpack(bytes);
      const manifest = JSON.parse(entries.get('project.json')!.toString('utf8'));
      assert.equal(manifest.schemaVersion, 1);
      assert.equal(manifest.assets[0].base64, undefined);
      assert.equal(manifest.snapshots.note.asset.base64, undefined);
      assert.equal(manifest.assets[0].entry, manifest.snapshots.note.asset.entry);
      assert.deepEqual(entries.get(manifest.assets[0].entry), Buffer.from(project.assets[0].base64, 'base64'));
      const loaded = await imported(bytes);
      assert.equal(loaded.status, 200, await errorText(loaded));
      assert.deepEqual(await loaded.json(), project);
      assert.equal((await (await json('GET', '/api/projects')).json()).length, 1, 'Import must not modify the catalog');
      entries.set('../outside.txt', Buffer.from('blocked'));
      assert.equal((await imported(zip(entries))).status, 400);
      entries.delete('../outside.txt');
      entries.set(manifest.assets[0].entry, Buffer.from('tampered bytes'));
      const tampered = await imported(zip(entries));
      assert.equal(tampered.status, 400);
      assert.match((await tampered.json()).error, /hash/);
      entries.set(manifest.assets[0].entry, Buffer.from(project.assets[0].base64, 'base64'));
      manifest.schemaVersion = 99;
      entries.set('project.json', Buffer.from(JSON.stringify(manifest)));
      assert.equal((await imported(zip(entries))).status, 400);
    });
    await t.test('canvas, node and pinned backgrounds retain real image bytes through portable ZIP export and import', async () => {
      const candidate = fixture();
      const image = (id: string, tag: string) => ({ id, name: `${id}.png`, mime: 'image/png', base64: Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), Buffer.from(tag)]).toString('base64') });
      candidate.assets.push(image('canvas-background', 'canvas'), image('node-background', 'live'));
      candidate.canvases[0].style = { backgroundPattern: 'grid', backgroundScale: 48, backgroundAssetId: 'canvas-background', backgroundFit: 'tile', backgroundOpacity: 0.5 };
      candidate.nodes[1].style = { backgroundPattern: 'linen', backgroundAssetId: 'node-background', backgroundFit: 'cover', backgroundTint: '#aabbcc' };
      candidate.snapshots.note.node.style = structuredClone(candidate.nodes[1].style);
      candidate.snapshots.note.backgroundAsset = image('node-background', 'pinned');
      const exported = await json('POST', '/api/export', candidate);
      assert.equal(exported.status, 200, await errorText(exported));
      const bytes = Buffer.from(await exported.arrayBuffer()), entries = unpack(bytes);
      const manifest = JSON.parse(entries.get('project.json')!.toString('utf8'));
      assert.equal(manifest.snapshots.note.backgroundAsset.base64, undefined);
      assert.notEqual(manifest.snapshots.note.backgroundAsset.entry, manifest.assets[2].entry);
      assert.deepEqual(entries.get(manifest.snapshots.note.backgroundAsset.entry), Buffer.from(candidate.snapshots.note.backgroundAsset.base64, 'base64'));
      const loaded = await imported(bytes); assert.equal(loaded.status, 200, await errorText(loaded));
      assert.deepEqual(await loaded.json(), candidate);
      const replay = structuredClone(candidate); replay.id = 'background-persistence';
      const created = await json('POST', '/api/projects', replay); assert.equal(created.status, 201, await errorText(created));
      const saved = await created.json(); assert.deepEqual(await (await json('GET', `/api/projects/${saved.id}`)).json(), saved);
      assert.deepEqual(saved.canvases[0].style, candidate.canvases[0].style); assert.deepEqual(saved.snapshots.note.backgroundAsset, candidate.snapshots.note.backgroundAsset);
    });
    await t.test('background appearance rejects URL/CSS injection, invalid limits, nonraster data and mismatched pinned images', async () => {
      for (const location of ['canvas', 'node', 'snapshot']) {
        for (const style of [{ backgroundPattern: 'url(remote)' }, { backgroundAssetId: 'https://remote/img.png' }, { backgroundScale: 7 },
          { backgroundScale: 513 }, { backgroundOpacity: -0.1 }, { backgroundOpacity: 1.1 }, { backgroundFit: 'stretch' }, { backgroundTint: 'red' }]) {
          const candidate = fixture(), owner = location === 'canvas' ? candidate.canvases[0] : location === 'node' ? candidate.nodes[1] : candidate.snapshots.note.node;
          owner.style = style as never;
          assert.equal((await json('POST', '/api/export', candidate)).status, 400, `${location}: ${JSON.stringify(style)}`);
        }
      }
      for (const mime of ['image/svg+xml', 'text/html', 'image/png']) {
        const candidate = fixture(); candidate.canvases[0].style = { backgroundAssetId: 'source' }; candidate.assets[0].mime = mime;
        const rejected = await json('POST', '/api/export', candidate); assert.equal(rejected.status, 400); assert.match((await rejected.json()).error, /raster/i);
      }
      const mismatched = fixture(); mismatched.snapshots.note.node.style = { backgroundAssetId: 'source' }; mismatched.snapshots.note.backgroundAsset = { ...mismatched.assets[0], id: 'different' };
      const rejected = await json('POST', '/api/export', mismatched); assert.equal(rejected.status, 400); assert.match((await rejected.json()).error, /does not match/i);
      const missing = fixture(); missing.canvases[0].style = { backgroundAssetId: 'missing-image' }; assert.equal((await json('POST', '/api/export', missing)).status, 200, 'Missing local media remains repairable');
    });
    await t.test('cross-origin writes, invalid identifiers and malformed MIME metadata are refused', async () => {
      assert.equal((await json('POST', '/api/projects', fixture(), { Origin: 'https://unrelated.example' })).status, 403);
      const invalid = { ...fixture(), id: '../escape' };
      assert.equal((await json('POST', '/api/projects', invalid)).status, 400);
      const unsafe = fixture();
      unsafe.assets[0].mime = 'text/html';
      assert.equal((await json('POST', '/api/export', unsafe)).status, 200, 'Arbitrary document bytes remain portable data');
      unsafe.assets[0].mime = 'text/plain\r\nX-Injected: true';
      assert.equal((await json('POST', '/api/export', unsafe)).status, 400);
      assert.equal((await fetch(base + '/api/projects', { method: 'POST', headers: { 'Content-Type': 'text/plain' }, body: '{}' })).status, 415);
      const persisted = JSON.parse(await readFile(path.join(dataRoot, 'projects', project.id, 'project.json'), 'utf8'));
      assert.deepEqual(persisted.project, project);
    });
    await t.test('snapping requires Boolean flags on canvases, nodes and saved snapshot nodes', async () => {
      for (const location of ['canvas', 'node', 'snapshot']) {
        for (const key of ['stickyEdges', 'snapToGrid', 'smartAlign']) {
          for (const invalid of [null, 'true', 1, [], {}]) {
            const candidate = fixture();
            const owner = location === 'canvas' ? candidate.canvases[0] : location === 'node' ? candidate.nodes[1] : candidate.snapshots.note.node;
            owner.snapping = { [key]: invalid } as never;
            const response = await json('POST', '/api/export', candidate);
            assert.equal(response.status, 400, `${location}.${key} must reject ${JSON.stringify(invalid)}`);
            assert.match((await response.json()).error, /snapping/i);
          }
        }
        for (const invalid of [null, false, 'on', []]) {
          const candidate = fixture();
          const owner = location === 'canvas' ? candidate.canvases[0] : location === 'node' ? candidate.nodes[1] : candidate.snapshots.note.node;
          owner.snapping = invalid as never;
          assert.equal((await json('POST', '/api/export', candidate)).status, 400, `${location} settings must be an object`);
        }
      }
    });
    await t.test('an owned stale recovery can keep autosaving without erasing its original conflict base', async () => {
      const fresh = { ...fixture(), id: 'stale-draft-project' };
      const committed = await (await json('POST', '/api/projects', fresh)).json() as MeridianProject;
      const staleDraft = { ...committed, title: 'Original unsaved draft' };
      const first = await (await json('PUT', `/api/projects/${fresh.id}/recovery`, { project: staleDraft, baseRevision: 1 })).json();
      const remoteSave = await json('PUT', `/api/projects/${fresh.id}`, { project: { ...committed, title: 'New remote revision' }, expectedRevision: 1 });
      assert.equal(remoteSave.status, 200);
      const continued = await json('PUT', `/api/projects/${fresh.id}/recovery`, { project: { ...staleDraft, title: 'Continued stale draft' }, baseRevision: 1, expectedSavedAt: first.savedAt });
      assert.equal(continued.status, 200, await errorText(continued));
      const recovery = await continued.json();
      assert.equal(recovery.baseRevision, 1);
      assert.equal(recovery.project.revision, 1);
      assert.equal(recovery.project.title, 'Continued stale draft');
      assert.equal((await (await json('GET', `/api/projects/${fresh.id}`)).json()).revision, 2);
      for (const revision of [2, 3]) {
        const rebase = await json('PUT', `/api/projects/${fresh.id}/recovery`, { project: { ...staleDraft, revision }, baseRevision: revision, expectedSavedAt: recovery.savedAt });
        assert.equal(rebase.status, 409, 'A recovery may not silently change its conflict base');
      }
      assert.equal((await json('PUT', `/api/projects/${fresh.id}/recovery`, { project: staleDraft, baseRevision: 1, expectedSavedAt: first.savedAt })).status, 409);
      assert.equal((await json('DELETE', `/api/projects/${fresh.id}/recovery`, { baseRevision: 1, savedAt: recovery.savedAt })).status, 204);
      assert.equal((await json('PUT', `/api/projects/${fresh.id}/recovery`, { project: staleDraft, baseRevision: 1 })).status, 409, 'Creating a new recovery at an already stale base is rejected');
    });
  } finally {
    await stop();
    const expectedPrefix = path.resolve(tmpdir()) + path.sep + 'meridian-host-tests-';
    assert.ok(path.resolve(temporary).startsWith(expectedPrefix));
    await rm(temporary, { recursive: true, force: true });
  }
});

async function errorText(response: Response) { return response.ok ? '' : await response.clone().text(); }
async function freePort(): Promise<number> {
  const server = createServer();
  server.listen(0, '127.0.0.1');
  await once(server, 'listening');
  const address = server.address();
  assert.ok(address && typeof address !== 'string');
  const port = address.port;
  await new Promise<void>((resolve, reject) => server.close(error => error ? reject(error) : resolve()));
  return port;
}
function fixture(): MeridianProject {
  const note = { id: 'note', canvasId: 'main', parentId: 'root', kind: 'text' as const, title: 'Note', text: 'Keep this content', x: 40, y: 50, width: 200, height: 120, snapping: { stickyEdges: false, smartAlign: true } };
  const asset = { id: 'source', name: 'notes.txt', mime: 'text/plain', base64: Buffer.from('Portable source notes').toString('base64') };
  return {
    schemaVersion: 1, id: 'host-test-project', title: 'Host fixture', revision: 0, entryCanvasId: 'main',
    canvases: [{ id: 'main', rootId: 'root', title: 'Main', snapping: { stickyEdges: true, snapToGrid: true, smartAlign: false } }],
    nodes: [{ id: 'root', canvasId: 'main', parentId: null, kind: 'canvas', title: 'Main', x: 0, y: 0, width: 1200, height: 800 }, note],
    links: [], assets: [asset], snapshots: { note: { node: structuredClone(note), value: true, asset, savedAt: '2026-10-04T00:00:00Z' } }, dependencies: [],
  };
}
function unpack(bytes: Buffer) {
  const result = new Map<string, Buffer>();
  let cursor = 0;
  while (bytes.readUInt32LE(cursor) === 0x04034b50) {
    const method = bytes.readUInt16LE(cursor + 8);
    const size = bytes.readUInt32LE(cursor + 18);
    const nameLength = bytes.readUInt16LE(cursor + 26), extraLength = bytes.readUInt16LE(cursor + 28);
    const name = bytes.subarray(cursor + 30, cursor + 30 + nameLength).toString('utf8');
    cursor += 30 + nameLength + extraLength;
    const content = bytes.subarray(cursor, cursor + size);
    result.set(name, method === 8 ? inflateRawSync(content) : content);
    cursor += size;
  }
  return result;
}
function zip(entries: Map<string, Buffer>) {
  const locals: Buffer[] = [], directory: Buffer[] = [];
  let offset = 0;
  for (const [name, body] of entries) {
    const encoded = Buffer.from(name), crc = crc32(body), local = Buffer.alloc(30), central = Buffer.alloc(46);
    local.writeUInt32LE(0x04034b50); local.writeUInt16LE(20, 4); local.writeUInt32LE(crc, 14);
    local.writeUInt32LE(body.length, 18); local.writeUInt32LE(body.length, 22); local.writeUInt16LE(encoded.length, 26);
    central.writeUInt32LE(0x02014b50); central.writeUInt16LE(20, 4); central.writeUInt16LE(20, 6); central.writeUInt32LE(crc, 16);
    central.writeUInt32LE(body.length, 20); central.writeUInt32LE(body.length, 24); central.writeUInt16LE(encoded.length, 28); central.writeUInt32LE(offset, 42);
    locals.push(local, encoded, body); directory.push(central, encoded); offset += local.length + encoded.length + body.length;
  }
  const end = Buffer.alloc(22), central = Buffer.concat(directory);
  end.writeUInt32LE(0x06054b50); end.writeUInt16LE(entries.size, 8); end.writeUInt16LE(entries.size, 10); end.writeUInt32LE(central.length, 12); end.writeUInt32LE(offset, 16);
  return Buffer.concat([...locals, central, end]);
}
function crc32(bytes: Buffer) {
  let crc = 0xffffffff;
  for (const byte of bytes) { crc ^= byte; for (let bit = 0; bit < 8; bit++) crc = crc >>> 1 ^ (crc & 1 ? 0xedb88320 : 0); }
  return (crc ^ 0xffffffff) >>> 0;
}
