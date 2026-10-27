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
import { createLinkPath } from '../src/core/connections.ts';
import type { MeridianProject } from '../src/core/types.ts';

const root = fileURLToPath(new URL('../', import.meta.url));

test('routing utilities and solid/dashed/dotted styles persist and reject unsafe live or saved fields in isolated storage', { timeout: 120_000 }, async () => {
  const temporary = await mkdtemp(path.join(tmpdir(), 'meridian-utility-tests-')), buildRoot = path.join(temporary, 'build');
  let host: ChildProcess | undefined, logs = '', base = '';
  const json = (method: string, route: string, body?: unknown) => fetch(base + route, { method, headers: body === undefined ? {} : { 'Content-Type': 'application/json' }, body: body === undefined ? undefined : JSON.stringify(body) });
  try {
    const intermediate = path.join(temporary, 'obj') + path.sep;
    const build = spawnSync('dotnet', ['build', path.join(root, 'src/host/Meridian.Host.csproj'), '--nologo', '--output', buildRoot,
      `-p:MSBuildProjectExtensionsPath=${intermediate}`, `-p:IntermediateOutputPath=${intermediate}`], { cwd: root, encoding: 'utf8', timeout: 60_000, windowsHide: true });
    assert.equal(build.status, 0, `${build.stdout}\n${build.stderr}`);
    const server = createServer(); await new Promise<void>(resolve => server.listen(0, '127.0.0.1', resolve));
    const port = (server.address() as { port: number }).port; await new Promise<void>(resolve => server.close(() => resolve()));
    base = `http://127.0.0.1:${port}`;
    host = spawn('dotnet', [path.join(buildRoot, 'Meridian.Host.dll'), '--data-root', path.join(temporary, 'data'), '--urls', base], { cwd: root, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
    host.stdout!.on('data', chunk => { logs = (logs + chunk).slice(-12_000); }); host.stderr!.on('data', chunk => { logs = (logs + chunk).slice(-12_000); });
    let started = false;
    for (let attempt = 0; attempt < 200; attempt++) {
      if (host.exitCode !== null) assert.fail(`Host exited during startup: ${logs}`);
      try { if ((await fetch(base + '/__meridian_status')).ok) { started = true; break; } } catch { /* Listener starts asynchronously. */ }
      await new Promise(resolve => setTimeout(resolve, 50));
    }
    assert.ok(started, `Host did not start: ${logs}`);
    const sourceProject = newProject('Utility persistence'), source = addNode(sourceProject, sourceProject.entryCanvasId, 'number', 'Number'), target = addNode(sourceProject, sourceProject.entryCanvasId, 'output', 'Result');
    source.value = 19; target.valueType = 'number';
    const route = createLinkPath(sourceProject, { canvasId: sourceProject.entryCanvasId, sourceId: source.id, targetId: target.id, kind: 'numeric', relation: 'generic', direction: 'forward', style: { color: '#aabbcc', width: 32, lineStyle: 'dotted' } }, [{ x: 330, y: 200 }, { x: 420, y: 210 }]);
    const joint = route.project.nodes.find(node => node.id === route.nodeIds[1])!; joint.utilityVariant = 'joint'; joint.style = { shape: 'rectangle', fill: '#446688', borderWidth: 0 }; joint.width = 32; joint.height = 6;
    route.project.links[0].style!.lineStyle = 'solid'; route.project.links[1].style!.lineStyle = 'dashed';
    const created = await json('POST', '/api/projects', commitSnapshots(route.project)); assert.equal(created.status, 201, await created.clone().text());
    const committed = await created.json() as MeridianProject;
    const reopened = await json('GET', `/api/projects/${committed.id}`); assert.equal(reopened.status, 200); assert.deepEqual(await reopened.json(), committed);
    assert.equal(committed.nodes.find(node => node.id === route.nodeIds[0])?.utilityVariant, 'pivot');
    assert.equal(committed.snapshots[joint.id].node.utilityVariant, 'joint');
    assert.deepEqual(new Set(committed.links.map(link => link.style?.lineStyle)), new Set(['solid', 'dashed', 'dotted']));
    const exported = await json('POST', '/api/export', committed); assert.equal(exported.status, 200, await exported.clone().text());
    const form = new FormData(); form.append('file', new Blob([await exported.arrayBuffer()]), 'utilities.meridian');
    const imported = await fetch(base + '/api/import', { method: 'POST', body: form }); assert.equal(imported.status, 200, await imported.clone().text()); assert.deepEqual(await imported.json(), committed);
    const mutations: Array<(project: MeridianProject) => void> = [
      project => { project.nodes.find(node => node.id === route.nodeIds[0])!.utilityVariant = 'unsafe' as never; },
      project => { project.nodes.find(node => node.id === source.id)!.utilityVariant = 'pivot'; },
      project => { project.snapshots[joint.id].node.utilityVariant = 'unsafe' as never; },
      project => { project.links[0].style!.lineStyle = 'unsafe' as never; },
      project => { project.links[0].style!.width = 33; },
      project => { project.links[0].style!.color = 'red'; },
      project => { project.snapshots[joint.id].node.kind = 'text'; },
    ];
    for (const mutate of mutations) {
      const invalid = structuredClone(committed); mutate(invalid);
      const rejected = await json('PUT', `/api/projects/${committed.id}`, { project: invalid, expectedRevision: committed.revision }); assert.equal(rejected.status, 400, await rejected.clone().text());
      const unchanged = await json('GET', `/api/projects/${committed.id}`); assert.deepEqual(await unchanged.json(), committed);
    }
  } finally {
    if (host && host.exitCode === null) { const closed = once(host, 'exit'); host.kill(); await closed; }
    const resolved = path.resolve(temporary), permitted = path.resolve(tmpdir()) + path.sep;
    assert.ok(resolved.startsWith(permitted) && path.basename(resolved).startsWith('meridian-utility-tests-'));
    await rm(resolved, { recursive: true, force: true });
  }
});
