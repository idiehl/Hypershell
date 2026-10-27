import test from 'node:test';
import assert from 'node:assert/strict';
import { spawn, spawnSync, type ChildProcess } from 'node:child_process';
import { once } from 'node:events';
import { cp, mkdir, mkdtemp, readdir, rm } from 'node:fs/promises';
import { createServer } from 'node:net';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { addCanvas, addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import type { MeridianProject } from '../src/core/types.ts';

test('Node Hub host save, recovery, native package and strict validation agree with the shared schema', { timeout: 120000 }, async () => {
  const root = fileURLToPath(new URL('../', import.meta.url));
  const temporary = await mkdtemp(path.join(tmpdir(), 'meridian-hub-host-'));
  const source = path.join(temporary, 'src', 'host'), brand = path.join(temporary, 'assets', 'brand');
  await mkdir(source, { recursive: true }); await mkdir(brand, { recursive: true });
  for (const name of await readdir(path.join(root, 'src', 'host'))) if (/\.(?:cs|csproj)$/.test(name)) await cp(path.join(root, 'src', 'host', name), path.join(source, name));
  for (const name of ['meridian.ico', 'meridian.png']) await cp(path.join(root, 'assets', 'brand', name), path.join(brand, name));
  const build = spawnSync('dotnet', ['build', path.join(source, 'Meridian.Host.csproj'), '--nologo'], { cwd: temporary, encoding: 'utf8', timeout: 60000, windowsHide: true });
  assert.equal(build.status, 0, build.stdout + build.stderr);
  const port = await new Promise<number>((resolve, reject) => { const server = createServer(); server.on('error', reject); server.listen(0, '127.0.0.1', () => { const address = server.address(); server.close(() => resolve(typeof address === 'object' && address ? address.port : 0)); }); });
  const origin = `http://127.0.0.1:${port}`;
  let child: ChildProcess | undefined;
  try {
    child = spawn('dotnet', [path.join(source, 'bin', 'Debug', 'net10.0', 'Meridian.Host.dll'), '--data-root', path.join(temporary, 'data'), '--urls', origin], { cwd: temporary, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
    let logs = ''; child.stdout?.on('data', data => logs = (logs + data).slice(-4000)); child.stderr?.on('data', data => logs = (logs + data).slice(-4000));
    let ready = false;
    for (let i = 0; i < 200; i++) { try { if ((await fetch(origin + '/__meridian_status')).ok) { ready = true; break; } } catch { /* isolated server is starting */ } await new Promise(resolve => setTimeout(resolve, 50)); }
    assert.ok(ready, logs);
    const json = (route: string, body: unknown, method = 'POST') => fetch(origin + route, { method, headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
    const project = newProject('Hub host'); const node = addNode(project, project.entryCanvasId, 'media', 'Zeus');
    project.assets = ['portrait', 'notes'].map(id => ({ id, name: id + '.txt', mime: 'text/plain', base64: btoa(id + ' original bytes') }));
    node.showTitle = false; node.text = 'Dormant legacy text'; node.contentItems = [{ id: 'picture', type: 'asset', assetId: 'portrait' }, { id: 'notes', type: 'asset', assetId: 'notes' }, { id: 'link', type: 'link', url: 'https://example.com/', label: 'Source' }];
    const observer = addCanvas(project, 'Observer'); const reference = addNode(project, observer.id, 'reference', 'Zeus ref'); reference.targetId = node.id;
    const submitted = commitSnapshots(project);
    const response = await json('/api/projects', submitted); assert.equal(response.status, 201, await response.clone().text());
    const saved = await response.json() as MeridianProject; assert.equal(saved.revision, 1);
    const current = await (await fetch(origin + '/api/projects/' + saved.id)).json() as MeridianProject;
    assert.deepEqual(current.nodes.find(n => n.id === node.id)?.contentItems, node.contentItems); assert.equal(current.nodes.find(n => n.id === node.id)?.showTitle, false); assert.equal(current.snapshots[node.id].assets?.[0].id, 'notes');
    const exported = await json('/api/export', saved); assert.equal(exported.status, 200, await exported.clone().text());
    const form = new FormData(); form.append('file', new Blob([await exported.arrayBuffer()], { type: 'application/octet-stream' }), 'hub.meridian');
    const importedResponse = await fetch(origin + '/api/import', { method: 'POST', body: form }); assert.equal(importedResponse.status, 200, await importedResponse.clone().text());
    const imported = await importedResponse.json(); assert.deepEqual(imported, saved);
    const recovery = structuredClone(saved); recovery.nodes.find(n => n.id === node.id)!.contentItems!.reverse();
    const recovered = await json('/api/projects/' + saved.id + '/recovery', { project: recovery, baseRevision: 1, expectedSavedAt: null }, 'PUT'); assert.equal(recovered.status, 200, await recovered.clone().text());
    assert.equal((await (await fetch(origin + '/api/projects/' + saved.id + '/recovery')).json()).project.nodes.find((n: { id: string }) => n.id === node.id).contentItems[0].type, 'link');
    for (const bad of [
      { contentItems: [{ id: 'unsafe', type: 'link', url: 'javascript:alert(1)' }] },
      { contentItems: [{ id: 'script', type: 'text', text: 'plain', html: '<script>' }] },
      { contentItems: [{ id: 'dup', type: 'text', text: 'a' }, { id: 'dup', type: 'asset', assetId: 'portrait' }] },
      { showTitle: 'yes' },
    ]) {
      const invalid = structuredClone(project); Object.assign(invalid.nodes.find(n => n.id === node.id)!, bad);
      const rejected = await json('/api/projects', invalid); assert.equal(rejected.status, 400, await rejected.text());
    }
    const malformed = structuredClone(project) as unknown as { snapshots: Record<string, { assets?: unknown[] }> }; malformed.snapshots[node.id] = { ...submitted.snapshots[node.id], assets: ['not-an-asset'] };
    const rejectedSnapshot = await json('/api/projects', malformed); assert.equal(rejectedSnapshot.status, 400);
  } finally {
    if (child && child.exitCode === null) { const exited = once(child, 'exit'); child.kill(); await exited; }
    assert.equal(path.dirname(path.resolve(temporary)).toLowerCase(), path.resolve(tmpdir()).toLowerCase());
    await rm(temporary, { recursive: true, force: true });
  }
});
