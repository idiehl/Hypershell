import assert from 'node:assert/strict';
import { spawn, spawnSync, type ChildProcess } from 'node:child_process';
import { once } from 'node:events';
import { mkdtemp, rm } from 'node:fs/promises';
import { createServer } from 'node:net';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import test from 'node:test';
import { applyRecipe, type AuthoringRecipe } from '../src/core/authoring.ts';
import { copyNodes, pasteNodes } from '../src/core/clipboard.ts';
import { isExpansionTray, isExpansionTraySize } from '../src/core/expansion-tray-state.ts';
import { addNode, commitSnapshots, forkProject, newProject } from '../src/core/model.ts';
import { createModule, instantiateModule } from '../src/core/modules.ts';
import { isProjectShape, validateProject } from '../src/core/rules.ts';
import type { MeridianNode, MeridianProject } from '../src/core/types.ts';
import { buildPublication, createPublicationPayload, readPublication } from '../src/publication.ts';

const invalidSizes: unknown[] = [null, [], {}, { width: 400 }, { height: 300 }, { width: 0, height: 300 }, { width: 400, height: -1 },
  { width: 40, height: 300 }, { width: 400, height: 72 }, { width: 39.999, height: 300 }, { width: 400, height: 71.999 },
  { width: '400', height: 300 }, { width: 400, height: 1e9 + 1 }, { width: 1e9 + 1, height: 300 }, { width: 400, height: 300, script: 'alert(1)' }];
const invalidStates: unknown[] = [null, [], {}, { offset: null }, { offset: [] }, { offset: {} }, { offset: { x: 1 } },
  { offset: { x: '1', y: 2 } }, { offset: { x: 1e9 + 1, y: 2 } }, { offset: { x: 1, y: -1e9 - 1 } },
  { offset: { x: 1, y: 2, script: 'alert(1)' } }, { offset: { x: 1, y: 2 }, sourceId: 'synthetic' },
  ...invalidSizes.map(size => ({ offset: { x: 1, y: 2 }, size }))];
for (const layoutVersion of [null, 0, 1, 4, 2.5, '2', '3', {}, []]) invalidStates.push({ offset: { x: 1, y: 2 }, layoutVersion });
for (const size of [{ width: 10, height: 11 }, { width: 11, height: 10 }, { width: 11, height: '11' }, { width: 11, height: 11, script: true }])
  invalidStates.push({ offset: { x: 1, y: 2 }, layoutVersion: 3, size });

test('compact tray version 3 validates its own minimum while preserving older metric boundaries and portable state', () => {
  const state = { offset: { x: 300, y: -10 }, layoutVersion: 3 as const, size: { width: 11, height: 11 } };
  assert.equal(isExpansionTray(state), true); assert.equal(isExpansionTraySize(state.size, 3), true);
  assert.equal(isExpansionTray({ ...state, layoutVersion: 2 }), false);
  assert.equal(isExpansionTray({ offset: state.offset, size: state.size }), false);
  for (const size of [{ width: 10, height: 11 }, { width: 11, height: 10 }, { width: 11, height: Infinity }, { width: 11, height: 1e9 + 1 }])
    assert.equal(isExpansionTray({ ...state, size }), false);
  const { project, source, owner } = fixture(); source.expansionTray = state;
  const saved = commitSnapshots(project), copied = forkProject(saved), imported = readPublication(buildPublication(saved, '', ''));
  for (const portable of [saved, copied, imported]) assert.deepEqual(portable.nodes.find(node => node.tag === source.tag)!.expansionTray, state);
  assert.deepEqual(saved.snapshots[source.id]!.node.expansionTray, state);
  const clipboard = pasteNodes(saved, copyNodes(saved, [owner.id]), saved.entryCanvasId);
  assert.deepEqual(clipboard.project.nodes.find(node => node.id === clipboard.idMap[source.id])!.expansionTray, state);
  const stored = createModule(saved, owner.id, 'Compact tray');
  assert.deepEqual(stored.project.nodes.find(node => node.tag === source.tag)!.expansionTray, state);
  const authored = applyRecipe(project, { version: 1, nodes: [{ key: 'source', existingId: source.id, expansionTray: state }] }).project;
  assert.deepEqual(authored.nodes.find(node => node.id === source.id)!.expansionTray, state);
});

test('plain container tray version 2 validates square minimum dimensions and survives portable copies', () => {
  const state = { offset: { x: 300, y: -10 }, layoutVersion: 2 as const, size: { width: 41, height: 41 } };
  assert.equal(isExpansionTray(state), true); assert.equal(isExpansionTraySize(state.size, 2), true);
  assert.equal(isExpansionTray({ offset: state.offset, size: state.size }), false, 'Unversioned frames retain the legacy banner minimum.');
  for (const size of [{ width: 40, height: 41 }, { width: 41, height: 40 }, { width: 41, height: Infinity }])
    assert.equal(isExpansionTray({ ...state, size }), false);
  const { project, source } = fixture(); source.expansionTray = state;
  const saved = commitSnapshots(project), payload = createPublicationPayload(saved), copied = forkProject(saved), imported = readPublication(buildPublication(saved, '', ''));
  assert.deepEqual(saved.snapshots[source.id]!.node.expansionTray, state);
  assert.deepEqual(payload.project.nodes.find(node => node.id === source.id)!.expansionTray, state);
  for (const portable of [copied, imported]) {
    assert.deepEqual(portable.nodes.find(node => node.tag === source.tag)!.expansionTray, state);
    assert.equal(portable.nodes.length, project.nodes.length);
  }
  const authored = applyRecipe(project, { version: 1, nodes: [{ key: 'source', existingId: source.id, expansionTray: state }] }).project;
  assert.deepEqual(authored.nodes.find(node => node.id === source.id)!.expansionTray, state);
});

function fixture() {
  const project = newProject('Portable expansion tray'), owner = addNode(project, project.entryCanvasId, 'container', 'Owner');
  Object.assign(owner, { x: 100, y: 100, width: 700, height: 400, sizeMode: 'fixed', childLayout: 'contained', containerVariant: 'contained' });
  const source = addNode(project, project.entryCanvasId, 'media', 'Original source');
  Object.assign(source, { parentId: owner.id, x: 140, y: 160, width: 160, height: 110, sizeMode: 'fixed', collapsed: true,
    childOffset: { x: 12, y: 25 }, expansionTray: { offset: { x: 760.5, y: -150.25 } } });
  const child = addNode(project, project.entryCanvasId, 'media', 'Original child');
  Object.assign(child, { parentId: source.id, x: 900, y: 200, width: 140, height: 90, sizeMode: 'fixed', collapsed: false });
  return { project, owner, source, child };
}

test('tray state is optional inert source-relative data, strict on live nodes and pinned snapshots', () => {
  const { project, source } = fixture(), saved = commitSnapshots(project);
  assert.ok(isProjectShape(saved)); assert.deepEqual(validateProject(saved), []);
  assert.ok(isExpansionTray({ offset: { x: -1e9, y: 1e9 } }));
  for (const value of [...invalidStates, { offset: { x: Infinity, y: 0 } }, { offset: { x: NaN, y: 0 } }]) {
    assert.equal(isExpansionTray(value), false);
    for (const pinned of [false, true]) {
      const invalid = structuredClone(saved), node = pinned ? invalid.snapshots[source.id]!.node : invalid.nodes.find(node => node.id === source.id)!;
      (node as unknown as Record<string, unknown>).expansionTray = value;
      assert.equal(isProjectShape(invalid), false); assert.ok(validateProject(invalid).length > 0);
    }
  }
  delete source.expansionTray;
  assert.ok(isProjectShape(project), 'Legacy documents do not need tray metadata.');
});

test('tray offsets and expansion flags survive snapshots, publication and independent graph identities', () => {
  const { project, owner, source, child } = fixture(), before = structuredClone(project), expected = structuredClone(source.expansionTray);
  const saved = commitSnapshots(project);
  assert.deepEqual(saved.snapshots[source.id]!.node.expansionTray, expected); assert.deepEqual(project, before);
  const reference = addNode(saved, project.entryCanvasId, 'reference', 'Pinned exterior source'); reference.targetId = 'remote-source';
  saved.snapshots['remote-source'] = { node: { ...structuredClone(source), id: 'remote-source', canvasId: 'remote-canvas', parentId: 'remote-root' }, value: null, savedAt: '2026-10-17T12:00:00Z' };
  const payload = createPublicationPayload(saved), portable = readPublication(buildPublication(saved, '', ''));
  assert.deepEqual(payload.project.nodes.find(node => node.id === source.id)!.expansionTray, expected);
  assert.deepEqual(payload.project.snapshots['remote-source']!.node.expansionTray, expected);
  for (const fresh of [forkProject(saved), portable]) {
    const copied = fresh.nodes.find(node => node.title === source.title)!, copiedChild = fresh.nodes.find(node => node.title === child.title)!;
    assert.notEqual(copied.id, source.id); assert.deepEqual(copied.expansionTray, expected);
    assert.deepEqual(copied.childOffset, source.childOffset); assert.equal(copied.collapsed, true);
    assert.equal(copiedChild.parentId, copied.id); assert.equal(copiedChild.collapsed, false);
    assert.equal(fresh.nodes.length, saved.nodes.length, 'No proxy identities are added.');
    assert.deepEqual(Object.values(fresh.snapshots).find(snapshot => snapshot.node.canvasId === 'remote-canvas')!.node.expansionTray, expected);
  }
  const clipboard = copyNodes(project, [owner.id]), pasted = pasteNodes(project, clipboard, project.entryCanvasId);
  const pastedSource = pasted.project.nodes.find(node => node.id === pasted.idMap[source.id])!;
  assert.deepEqual(pastedSource.expansionTray, expected); assert.deepEqual(pastedSource.childOffset, source.childOffset);
  assert.equal(pasted.project.nodes.find(node => node.id === pasted.idMap[child.id])!.parentId, pastedSource.id);
  const stored = createModule(project, owner.id, 'Tray template'), inserted = instantiateModule(project, stored, { canvasId: project.entryCanvasId, x: 1600, y: 100 });
  const moduleSource = stored.project.nodes.find(node => node.title === source.title)!, insertedSource = inserted.project.nodes.find(node => node.id === inserted.idMap[moduleSource.id])!;
  assert.notEqual(insertedSource.id, source.id); assert.deepEqual(insertedSource.expansionTray, expected);
  assert.equal(insertedSource.collapsed, true); assert.equal(inserted.project.nodes.find(node => node.title === child.title && node.parentId === insertedSource.id)!.collapsed, false);
});

test('declarative authoring accepts strict tray state without changing its base or legacy child offset', () => {
  const { project, source } = fixture(), before = structuredClone(project), expected = { offset: { x: -400, y: 320.25 } };
  const updated = applyRecipe(project, { version: 1, nodes: [{ key: 'source', existingId: source.id, expansionTray: expected }] }).project;
  assert.deepEqual(updated.nodes.find(node => node.id === source.id)!.expansionTray, expected);
  assert.deepEqual(updated.nodes.find(node => node.id === source.id)!.childOffset, source.childOffset); assert.deepEqual(project, before);
  for (const value of invalidStates) {
    assert.throws(() => applyRecipe(project, { version: 1, nodes: [{ key: 'source', existingId: source.id, expansionTray: value }] } as AuthoringRecipe), /expansionTray/);
    assert.deepEqual(project, before);
  }
});

test('an authored tray frame survives snapshots, clipboard, Module insertion, publication and fresh imported identities', () => {
  const { project, owner, source, child } = fixture(); source.expansionTray!.size = { width: 640.5, height: 380.25 };
  const expected = structuredClone(source.expansionTray!), saved = commitSnapshots(project), before = JSON.stringify(saved);
  assert.deepEqual(saved.snapshots[source.id]!.node.expansionTray, expected);
  const payload = createPublicationPayload(saved), imported = readPublication(buildPublication(saved, '', ''));
  assert.deepEqual(payload.project.nodes.find(node => node.id === source.id)!.expansionTray, expected);
  for (const fresh of [forkProject(saved), imported]) {
    const node = fresh.nodes.find(node => node.tag === source.tag)!, originalChild = fresh.nodes.find(node => node.tag === child.tag)!;
    assert.notEqual(node.id, source.id); assert.deepEqual(node.expansionTray, expected); assert.equal(node.collapsed, true);
    assert.equal(originalChild.parentId, node.id); assert.equal(originalChild.collapsed, false);
  }
  const pasted = pasteNodes(saved, copyNodes(saved, [owner.id]), saved.entryCanvasId);
  assert.deepEqual(pasted.project.nodes.find(node => node.id === pasted.idMap[source.id])!.expansionTray, expected);
  const module = createModule(saved, owner.id, 'Resizable tray'), moduleSource = module.project.nodes.find(node => node.tag === source.tag)!;
  const inserted = instantiateModule(saved, module, { canvasId: saved.entryCanvasId, x: 2100, y: 150 });
  assert.deepEqual(inserted.project.nodes.find(node => node.id === inserted.idMap[moduleSource.id])!.expansionTray, expected);
  const updated = applyRecipe(saved, { version: 1, nodes: [{ key: 'source', existingId: source.id, expansionTray: { ...expected, size: { width: 700.25, height: 420.5 } } }] }).project;
  assert.deepEqual(updated.nodes.find(node => node.id === source.id)!.expansionTray!.size, { width: 700.25, height: 420.5 });
  assert.equal(JSON.stringify(saved), before, 'Portable and authoring operations never mutate the saved source.');
});

test('tray frame dimensions are strict inert numeric data with no extra fields or nonfinite values', () => {
  assert.equal(isExpansionTraySize({ width: 640.5, height: 380.25 }), true);
  assert.equal(isExpansionTraySize({ width: 1e9, height: 1e9 }), true);
  assert.equal(isExpansionTraySize({ width: 40.5, height: 72.5 }), true);
  assert.equal(isExpansionTraySize({ width: 41, height: 73 }), true);
  for (const size of [...invalidSizes, { width: Infinity, height: 300 }, { width: 400, height: NaN }]) {
    assert.equal(isExpansionTraySize(size), false); assert.equal(isExpansionTray({ offset: { x: 1, y: 2 }, size }), false);
  }
});

test('host storage, export/import and module library retain tray state and reject malformed writes atomically', { timeout: 120_000 }, async () => {
  const root = fileURLToPath(new URL('../', import.meta.url)), temporary = await mkdtemp(path.join(tmpdir(), 'meridian-expansion-tray-schema-'));
  const buildRoot = path.join(temporary, 'build'), dataRoot = path.join(temporary, 'data');
  let host: ChildProcess | undefined, logs = '', base = '';
  const json = (method: string, route: string, body?: unknown) => fetch(base + route, { method,
    headers: body === undefined ? {} : { 'Content-Type': 'application/json' }, body: body === undefined ? undefined : JSON.stringify(body) });
  const read = async (route: string) => { const response = await json('GET', route); assert.equal(response.status, 200, await response.clone().text()); return response.json(); };
  try {
    const intermediate = path.join(temporary, 'obj') + path.sep;
    const build = spawnSync('dotnet', ['build', path.join(root, 'src/host/Meridian.Host.csproj'), '--nologo', '--output', buildRoot,
      `-p:MSBuildProjectExtensionsPath=${intermediate}`, `-p:IntermediateOutputPath=${intermediate}`], { cwd: root, encoding: 'utf8', timeout: 60_000, windowsHide: true });
    assert.equal(build.status, 0, `${build.stdout}\n${build.stderr}`);
    base = `http://127.0.0.1:${await freePort()}`;
    host = spawn('dotnet', [path.join(buildRoot, 'Meridian.Host.dll'), '--data-root', dataRoot, '--urls', base], { cwd: root, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
    host.stdout!.on('data', chunk => { logs = (logs + chunk).slice(-12_000); }); host.stderr!.on('data', chunk => { logs = (logs + chunk).slice(-12_000); });
    let started = false;
    for (let attempt = 0; attempt < 200; attempt++) {
      if (host.exitCode !== null) assert.fail(`Host exited during startup: ${logs}`);
      try { if ((await fetch(base + '/__meridian_status')).ok) { started = true; break; } } catch { /* Listener is starting. */ }
      await new Promise(resolve => setTimeout(resolve, 50));
    }
    assert.ok(started, `Host did not start: ${logs}`);
    const { project, source, owner } = fixture(); source.expansionTray!.size = { width: 640.5, height: 380.25 };
    const submitted = commitSnapshots(project), expected = structuredClone(source.expansionTray);
    const created = await json('POST', '/api/projects', submitted); assert.equal(created.status, 201, await created.clone().text());
    const committed: MeridianProject = await created.json(); assert.deepEqual(await read(`/api/projects/${committed.id}`), committed);
    assert.deepEqual(committed.snapshots[source.id]!.node.expansionTray, expected);
    const exported = await json('POST', '/api/export', committed); assert.equal(exported.status, 200, await exported.clone().text());
    const body = new FormData(); body.append('file', new Blob([await exported.arrayBuffer()]), 'tray.meridian');
    const imported = await fetch(base + '/api/import', { method: 'POST', body }); assert.equal(imported.status, 200, await imported.clone().text()); assert.deepEqual(await imported.json(), committed);
    const stored = createModule(committed, owner.id, 'Tray module'), moduleResponse = await json('POST', '/api/modules', { module: stored });
    assert.equal(moduleResponse.status, 201, await moduleResponse.clone().text()); assert.deepEqual(await read(`/api/modules/${stored.id}`), stored);
    for (const value of invalidStates) for (const pinned of [false, true]) {
      const invalid = structuredClone(committed), node = pinned ? invalid.snapshots[source.id]!.node : invalid.nodes.find(node => node.id === source.id)!;
      (node as unknown as Record<string, unknown>).expansionTray = value;
      const rejected = await json('PUT', `/api/projects/${committed.id}`, { project: invalid, expectedRevision: committed.revision });
      assert.equal(rejected.status, 400, `${JSON.stringify(value)}: ${await rejected.clone().text()}`);
      assert.deepEqual(await read(`/api/projects/${committed.id}`), committed);
    }
    const malformedModule = structuredClone(stored); malformedModule.id = crypto.randomUUID();
    (malformedModule.project.nodes.find(node => node.title === source.title)! as MeridianNode & { expansionTray: unknown }).expansionTray = { offset: { x: 1e10, y: 0 } };
    assert.equal((await json('POST', '/api/modules', { module: malformedModule })).status, 400);
    assert.equal((await read('/api/modules')).length, 1);
    const legacy = fixture().project; const legacyCreated = await json('POST', '/api/projects', commitSnapshots(legacy));
    assert.equal(legacyCreated.status, 201, await legacyCreated.clone().text());
    const legacyStored: MeridianProject = await legacyCreated.json();
    assert.equal(legacyStored.nodes.find(node => node.expansionTray)!.expansionTray!.size, undefined, 'Old automatic trays remain valid and do not gain a size record.');
    const plain = fixture(); plain.source.expansionTray = { offset: { x: 700, y: 100 }, layoutVersion: 2, size: { width: 41, height: 41 } };
    const plainCreated = await json('POST', '/api/projects', commitSnapshots(plain.project));
    assert.equal(plainCreated.status, 201, await plainCreated.clone().text());
    const plainSaved: MeridianProject = await plainCreated.json();
    assert.deepEqual(plainSaved.nodes.find(node => node.id === plain.source.id)!.expansionTray, plain.source.expansionTray);
    assert.deepEqual(plainSaved.snapshots[plain.source.id]!.node.expansionTray, plain.source.expansionTray);
    const compact = fixture(); compact.source.expansionTray = { offset: { x: 700, y: 100 }, layoutVersion: 3, size: { width: 11, height: 11 } };
    const compactCreated = await json('POST', '/api/projects', commitSnapshots(compact.project));
    assert.equal(compactCreated.status, 201, await compactCreated.clone().text());
    const compactSaved: MeridianProject = await compactCreated.json();
    assert.deepEqual(compactSaved.nodes.find(node => node.id === compact.source.id)!.expansionTray, compact.source.expansionTray);
    assert.deepEqual(compactSaved.snapshots[compact.source.id]!.node.expansionTray, compact.source.expansionTray);
    for (const layoutVersion of [undefined, 2] as const) {
      const rejected = structuredClone(compactSaved); rejected.nodes.find(node => node.id === compact.source.id)!.expansionTray!.layoutVersion = layoutVersion;
      assert.equal((await json('PUT', `/api/projects/${compactSaved.id}`, { project: rejected, expectedRevision: compactSaved.revision })).status, 400);
      assert.deepEqual(await read(`/api/projects/${compactSaved.id}`), compactSaved);
    }
  } finally {
    if (host && host.exitCode === null) { const exited = once(host, 'exit'); host.kill(); await exited; }
    assert.ok(path.resolve(temporary).startsWith(path.resolve(tmpdir()) + path.sep + 'meridian-expansion-tray-schema-'));
    await rm(temporary, { recursive: true, force: true });
  }
});

async function freePort(): Promise<number> {
  const server = createServer(); server.listen(0, '127.0.0.1'); await once(server, 'listening');
  const address = server.address(); assert.ok(address && typeof address !== 'string'); const port = address.port;
  await new Promise<void>((resolve, reject) => server.close(error => error ? reject(error) : resolve())); return port;
}
