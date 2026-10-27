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
import { createModule, instantiateModule } from '../src/core/modules.ts';
import type { MeridianProject } from '../src/core/types.ts';

const root = fileURLToPath(new URL('../', import.meta.url));

test('spatial disclosure and relationship styling persist through host boundaries', { timeout: 120_000 }, async t => {
  const temporary = await mkdtemp(path.join(tmpdir(), 'meridian-spatial-tests-'));
  const buildRoot = path.join(temporary, 'build'), dataRoot = path.join(temporary, 'data');
  let host: ChildProcess | undefined, logs = '', base = '';
  const json = (method: string, route: string, body?: unknown) => fetch(base + route, { method,
    headers: body === undefined ? {} : { 'Content-Type': 'application/json' }, body: body === undefined ? undefined : JSON.stringify(body) });
  const read = async (route: string) => {
    const response = await json('GET', route); assert.equal(response.status, 200, await response.clone().text()); return response.json();
  };
  const create = async (project = fixture().project): Promise<MeridianProject> => {
    const response = await json('POST', '/api/projects', project); assert.equal(response.status, 201, await response.clone().text()); return response.json();
  };
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

    await t.test('all disclosure, placement, edge and relationship choices survive save, snapshots and native packages', async () => {
      const { project } = fixture();
      const committed = await create(commitSnapshots(project));
      assert.deepEqual(committed.nodes, project.nodes);
      assert.deepEqual(committed.links, project.links);
      assert.deepEqual(await read(`/api/projects/${project.id}`), committed);
      const exported = await json('POST', '/api/export', committed);
      assert.equal(exported.status, 200, await exported.clone().text());
      const body = new FormData(); body.append('file', new Blob([await exported.arrayBuffer()]), 'spatial.meridian');
      const imported = await fetch(base + '/api/import', { method: 'POST', body });
      assert.equal(imported.status, 200, await imported.clone().text());
      assert.deepEqual(await imported.json(), committed);
      for (const node of committed.nodes) assert.deepEqual(committed.snapshots[node.id].node, node);
    });
    await t.test('invalid spatial node fields are rejected for live nodes and saved snapshots without changing saved content', async () => {
      const committed = await create(commitSnapshots(fixture().project));
      const nodeId = committed.nodes.find(node => node.kind === 'text')!.id;
      const cases: Array<[string, unknown]> = [
        ['childLayout', 'grid'], ['childLayout', null], ['childLayout', true],
        ['placement', 'inside'], ['placement', null], ['placement', {}],
        ['attachSide', 'diagonal'], ['attachSide', null], ['attachSide', 1],
      ];
      for (const [field, value] of cases) for (const location of ['live', 'snapshot']) {
        const invalid: any = structuredClone(committed);
        const node = location === 'live' ? invalid.nodes.find((item: { id: string }) => item.id === nodeId) : invalid.snapshots[nodeId].node;
        node[field] = value;
        const response = await json('PUT', `/api/projects/${committed.id}`, { project: invalid, expectedRevision: committed.revision });
        assert.equal(response.status, 400, `${location} ${field}: ${await response.clone().text()}`);
      }
      assert.deepEqual(await read(`/api/projects/${committed.id}`), committed);
    });
    await t.test('invalid relationship directions and relation names are rejected atomically', async () => {
      const committed = await create();
      for (const [field, value] of [['direction', 'backward'], ['direction', null], ['direction', true], ['direction', ['forward']], ['relation', 'adjacent'], ['relation', null]]) {
        const invalid: any = structuredClone(committed); invalid.links[0][field as string] = value;
        const response = await json('PUT', `/api/projects/${committed.id}`, { project: invalid, expectedRevision: committed.revision });
        assert.equal(response.status, 400, `${field}: ${await response.clone().text()}`);
      }
      assert.deepEqual(await read(`/api/projects/${committed.id}`), committed);
    });
    await t.test('connection owners must exist in the same canvas and are checked on save and module import', async () => {
      const committed = await create();
      for (const value of [null, 1, {}, '', 'missing-owner']) {
        const invalid: any = structuredClone(committed); invalid.links[0].parentId = value;
        const response = await json('PUT', `/api/projects/${committed.id}`, { project: invalid, expectedRevision: committed.revision });
        assert.equal(response.status, 400, `owner ${JSON.stringify(value)}: ${await response.clone().text()}`);
      }
      const invalid = structuredClone(committed), other = addCanvas(invalid, 'Other owner');
      invalid.links[0].parentId = other.rootId;
      assert.equal((await json('PUT', `/api/projects/${committed.id}`, { project: invalid, expectedRevision: committed.revision })).status, 400);
      assert.deepEqual(await read(`/api/projects/${committed.id}`), committed);
      const { project, groupId } = fixture(), stored = createModule(project, groupId, 'Owned links');
      stored.project.links[0].parentId = stored.project.canvases[0].rootId;
      assert.equal((await json('POST', '/api/modules', { module: stored })).status, 400);
    });
    await t.test('spatial module definitions and independent insertions retain edge preferences and visual relationships', async () => {
      const { project, groupId } = fixture();
      const stored = createModule(project, groupId, 'Expandable spatial story');
      const response = await json('POST', '/api/modules', { module: stored });
      assert.equal(response.status, 201, await response.clone().text());
      assert.deepEqual(await read(`/api/modules/${stored.id}`), stored);
      const destination = newProject('Independent spatial story');
      const inserted = instantiateModule(destination, stored, { canvasId: destination.entryCanvasId, x: 600, y: 300 });
      const copied = await create(inserted.project);
      assert.deepEqual(copied.links.map(link => ({ relation: link.relation, direction: link.direction })), stored.project.links.map(link => ({ relation: link.relation, direction: link.direction })));
      assert.deepEqual(copied.nodes.filter(node => node.kind !== 'canvas').map(node => ({ childLayout: node.childLayout, placement: node.placement, attachSide: node.attachSide })),
        stored.project.nodes.filter(node => node.kind !== 'canvas').map(node => ({ childLayout: node.childLayout, placement: node.placement, attachSide: node.attachSide })));
      for (const scope of ['node', 'link']) {
        const invalid: any = structuredClone(stored); invalid.id = crypto.randomUUID();
        if (scope === 'node') invalid.project.nodes[1].childLayout = 'stack';
        else invalid.project.links[0].direction = 'backward';
        assert.equal((await json('POST', '/api/modules', { module: invalid })).status, 400);
      }
    });
    await t.test('library near and free placement persist, legacy omission remains valid and unknown values do not overwrite saved hierarchy', async () => {
      const library = { schemaVersion: 1, revision: 0, entries: [
        { id: 'section', kind: 'section', title: 'Reading room', parentId: null, collapsed: false, x: 50, y: 50 },
        { id: 'near', kind: 'section', title: 'Nearby chapter', parentId: 'section', collapsed: false, x: 400, y: 50, placement: 'near' },
        { id: 'free', kind: 'canvas', title: 'Placed canvas', parentId: 'section', collapsed: true, x: 1200, y: 900, placement: 'free', projectId: 'unavailable-project' },
      ] };
      const response = await json('PUT', '/api/library', { library, expectedRevision: 0 });
      assert.equal(response.status, 200, await response.clone().text());
      const saved = await response.json();
      assert.deepEqual(saved, { ...library, revision: 1 });
      for (const placement of ['edge', 'auto', null, true, 3]) {
        const invalid: any = structuredClone(saved); invalid.entries[1].placement = placement;
        assert.equal((await json('PUT', '/api/library', { library: invalid, expectedRevision: 1 })).status, 400);
      }
      assert.deepEqual(await read('/api/library'), saved);
    });
  } finally {
    if (host && host.exitCode === null) { const exit = once(host, 'exit'); host.kill(); await exit; }
    assert.ok(path.resolve(temporary).startsWith(path.resolve(tmpdir()) + path.sep + 'meridian-spatial-tests-'));
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

function fixture() {
  const project = newProject('Spatial host fixture'), canvasId = project.entryCanvasId;
  const group = addNode(project, canvasId, 'text', 'Story and its sources');
  Object.assign(group, { childLayout: 'spatial', placement: 'free', attachSide: 'auto', text: 'The story remains readable while its related contents unfold.' });
  const layouts = ['contained', 'spatial'] as const;
  const placements = ['free', 'near', 'edge'] as const;
  const sides = ['auto', 'top', 'right', 'bottom', 'left'] as const;
  const relations = ['causal', 'chronological', 'mutual', 'cyclical', 'generic'] as const;
  const directions = ['forward', 'reverse', 'both', 'none'] as const;
  for (let index = 0; index < sides.length; index++) {
    const child = addNode(project, canvasId, 'text', `Child ${index + 1}`); child.parentId = group.id;
    Object.assign(child, { childLayout: layouts[index % layouts.length], placement: placements[index % placements.length], attachSide: sides[index], x: 600 + index * 250, y: 350 });
    project.links.push({ id: crypto.randomUUID(), canvasId, parentId: group.id, sourceId: group.id, targetId: child.id, kind: 'visual', relation: relations[index], direction: directions[index % directions.length] });
  }
  return { project, groupId: group.id };
}
