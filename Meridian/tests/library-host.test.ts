import assert from 'node:assert/strict';
import { spawn, spawnSync, type ChildProcess } from 'node:child_process';
import { once } from 'node:events';
import { mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import { createServer } from 'node:net';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { test } from 'node:test';
import type { MeridianProject } from '../src/core/types.ts';
import { normalizeLibraryDocument, type LibraryCanvasDocument } from '../src/core/library.ts';
import { addCanvas, addNode } from '../src/core/model.ts';

interface LibraryEntry {
  id: string; kind: 'section' | 'canvas'; title: string; parentId: string | null;
  collapsed: boolean; x: number; y: number; projectId?: string; canvasId?: string;
}
interface Library { schemaVersion: 1; revision: number; entries: LibraryEntry[] }

const root = fileURLToPath(new URL('../', import.meta.url));

test('library hierarchy persists independently, guards concurrent saves, and exposes a lightweight catalog', { timeout: 120_000 }, async t => {
  const temporary = await mkdtemp(path.join(tmpdir(), 'meridian-library-tests-'));
  const dataRoot = path.join(temporary, 'data');
  const buildRoot = path.join(temporary, 'build');
  let host: ChildProcess | undefined;
  let base = '', logs = '';
  let library: Library = { schemaVersion: 1, revision: 0, entries: [] };
  async function start() {
    const port = await freePort();
    base = `http://127.0.0.1:${port}`;
    host = spawn('dotnet', [path.join(buildRoot, 'Meridian.Host.dll'), '--data-root', dataRoot, '--urls', base], { cwd: root, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
    logs = '';
    host.stdout!.on('data', chunk => { logs = (logs + chunk).slice(-12_000); });
    host.stderr!.on('data', chunk => { logs = (logs + chunk).slice(-12_000); });
    for (let attempt = 0; attempt < 200; attempt++) {
      if (host.exitCode !== null) assert.fail(`Host exited during startup: ${logs}`);
      try {
        const response = await fetch(base + '/__meridian_status');
        if (response.ok) return;
      } catch { /* The isolated listener is starting. */ }
      await new Promise(resolve => setTimeout(resolve, 50));
    }
    assert.fail(`Host did not start: ${logs}`);
  }
  async function stop() {
    if (!host || host.exitCode !== null) return;
    const exited = once(host, 'exit');
    host.kill();
    await exited;
    host = undefined;
  }
  const json = (method: string, route: string, body?: unknown, extraHeaders: Record<string, string> = {}) => fetch(base + route, {
    method, headers: { ...(body === undefined ? {} : { 'Content-Type': 'application/json' }), ...extraHeaders },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const save = (value: unknown = library, expectedRevision = library.revision) => json('PUT', '/api/library', { library: value, expectedRevision });
  try {
    // Both compiler intermediates and host storage stay outside installed/live outputs.
    const intermediate = path.join(temporary, 'obj') + path.sep;
    const build = spawnSync('dotnet', ['build', path.join(root, 'src/host/Meridian.Host.csproj'), '--nologo', '--output', buildRoot,
      `-p:MSBuildProjectExtensionsPath=${intermediate}`, `-p:IntermediateOutputPath=${intermediate}`], { cwd: root, encoding: 'utf8', timeout: 60_000, windowsHide: true });
    assert.equal(build.status, 0, `${build.stdout}\n${build.stderr}`);
    await start();
    const created = await json('POST', '/api/projects', fixture());
    assert.equal(created.status, 201, await created.clone().text());
    const project = await created.json() as MeridianProject;
    const projectPath = path.join(dataRoot, 'projects', project.id, 'project.json');
    const originalProjectBytes = await readFile(projectPath, 'utf8');

    await t.test('initial document and status advertise library support; catalog omits all content and assets', async () => {
      assert.deepEqual(await (await json('GET', '/api/library')).json(), library);
      assert.ok((await (await json('GET', '/__meridian_status')).json()).capabilities.includes('library-v1'));
      const response = await json('GET', '/api/library/catalog');
      assert.equal(response.headers.get('Cache-Control'), 'no-store');
      const catalog = await response.json();
      assert.equal(catalog.length, 1);
      const updatedAt = JSON.parse(originalProjectBytes).updatedAt;
      assert.deepEqual(catalog[0], {
        id: project.id, title: project.title, revision: 1, updatedAt, entryCanvasId: 'overview',
        canvases: [{ id: 'overview', rootId: 'root-overview', title: 'Overview', nodeCount: 3 }, { id: 'chapter', rootId: 'root-chapter', title: 'Chapter', nodeCount: 0 }],
        links: [{ sourceCanvasId: 'overview', targetCanvasId: 'chapter' }],
      });
      assert.ok(!JSON.stringify(catalog).includes('private attachment bytes'));
      assert.ok(!JSON.stringify(catalog).includes('base64'));
    });
    await t.test('recursive sections and canvas entries round-trip without touching saved canvases', async () => {
      library.entries = [entry('section'), entry('chapter', 'section'), entry('workspace', 'chapter', 'canvas'), { ...entry('interior', 'workspace', 'canvas'), canvasId: 'chapter', collapsed: true, x: -340.25, y: 12.5 }];
      const response = await save();
      assert.equal(response.status, 200, await response.clone().text());
      library = await response.json();
      assert.equal(library.revision, 1);
      assert.deepEqual(await (await json('GET', '/api/library')).json(), library);
      assert.equal(await readFile(projectPath, 'utf8'), originalProjectBytes);
    });
    await t.test('simultaneous writers have one winner and backup contains the preceding complete revision', async () => {
      const previous = structuredClone(library);
      const responses = await Promise.all(['Window A', 'Window B'].map(title => save({ ...library, entries: library.entries.map((item, index) => index === 0 ? { ...item, title } : item) })));
      assert.deepEqual(responses.map(response => response.status).sort(), [200, 409]);
      library = await responses.find(response => response.status === 200)!.json();
      assert.equal(library.revision, 2);
      assert.deepEqual(JSON.parse(await readFile(path.join(dataRoot, 'library.json.bak'), 'utf8')), previous);
      assert.equal((await save(previous, previous.revision)).status, 409);
      assert.equal((await save({ ...library, revision: 0 }, library.revision)).status, 409);
      assert.deepEqual(await (await json('GET', '/api/library')).json(), library);
    });
    await t.test('invalid trees and malformed values leave the last committed hierarchy intact', async () => {
      const cases: Array<[string, unknown]> = [
        ['duplicate identifiers', [entry('same'), entry('same')]],
        ['missing parent', [entry('child', 'missing')]],
        ['self cycle', [entry('self', 'self')]],
        ['indirect cycle', [entry('a', 'b'), entry('b', 'c'), entry('c', 'a')]],
        ['unsafe identifier', [entry('../escape')]],
        ['unknown kind', [{ ...entry('a'), kind: 'folder' }]],
        ['missing title', [{ ...entry('a'), title: '' }]],
        ['missing parentId', [{ ...entry('a'), parentId: undefined }]],
        ['invalid parent', [{ ...entry('a'), parentId: false }]],
        ['invalid coordinate', [{ ...entry('a'), x: 1e10 }]],
        ['null coordinate', [{ ...entry('a'), y: null }]],
        ['invalid collapse flag', [{ ...entry('a'), collapsed: 'true' }]],
        ['missing project reference', [{ ...entry('a', null, 'canvas'), projectId: undefined }]],
        ['invalid canvas reference', [{ ...entry('a', null, 'canvas'), canvasId: '../invalid' }]],
        ['references on section', [{ ...entry('a'), projectId: 'library-fixture' }]],
        ['unknown field', [{ ...entry('a'), html: '<script />' }]],
        ['non-object entry', [null]],
        ['excess entries', Array.from({ length: 10001 }, (_, index) => entry(`entry-${index}`))],
      ];
      for (const [reason, entries] of cases) {
        const response = await save({ ...library, entries });
        assert.equal(response.status, 400, `${reason}: ${await response.text()}`);
      }
      for (const invalid of [{ ...library, schemaVersion: 2 }, { ...library, entries: {} }, { ...library, extra: true }, null])
        assert.equal((await save(invalid)).status, 400);
      assert.deepEqual(await (await json('GET', '/api/library')).json(), library);
      assert.equal(await readFile(projectPath, 'utf8'), originalProjectBytes);
    });
    await t.test('deep trees and unavailable references remain navigable data, survive restart, and update backup', async () => {
      const previous = structuredClone(library);
      const entries = Array.from({ length: 1500 }, (_, index) => entry(`nested-${index}`, index === 0 ? null : `nested-${index - 1}`));
      entries.push({ ...entry('unavailable', 'nested-1499', 'canvas'), projectId: 'removed-externally', canvasId: 'missing-inner-canvas' });
      const response = await save({ ...library, entries });
      assert.equal(response.status, 200, await response.clone().text());
      library = await response.json();
      assert.deepEqual(JSON.parse(await readFile(path.join(dataRoot, 'library.json.bak'), 'utf8')), previous);
      await stop(); await start();
      assert.deepEqual(await (await json('GET', '/api/library')).json(), library);
      assert.equal(await readFile(projectPath, 'utf8'), originalProjectBytes);
    });
    await t.test('concurrent readers and saves always return a complete committed hierarchy without transient storage errors', async () => {
      for (let round = 0; round < 40; round++) {
        const previous = structuredClone(library);
        const candidate = { ...library, entries: library.entries.map((item, index) => index ? item : { ...item, title: `Concurrent save ${round}` }) };
        const responses = await Promise.all([save(candidate), ...Array.from({ length: 8 }, () => json('GET', '/api/library'))]);
        const [written, ...reads] = responses;
        assert.equal(written.status, 200, `Save ${round}: ${await written.clone().text()}`);
        library = await written.json();
        assert.equal(library.revision, previous.revision + 1);
        for (const response of reads) {
          assert.equal(response.status, 200, `Read during save ${round}: ${await response.clone().text()}`);
          const observed = await response.json() as Library;
          assert.ok(observed.revision === previous.revision || observed.revision === library.revision,
            `Read observed unexpected revision ${observed.revision} during save ${round}`);
          assert.deepEqual(observed, observed.revision === previous.revision ? previous : library);
        }
      }
      assert.deepEqual(await (await json('GET', '/api/library')).json(), library);
      assert.equal(await readFile(projectPath, 'utf8'), originalProjectBytes);
    });
    await t.test('same-origin and JSON guards apply to library writes', async () => {
      assert.equal((await json('PUT', '/api/library', { library, expectedRevision: library.revision }, { Origin: 'https://unrelated.example' })).status, 403);
      assert.equal((await json('PUT', '/api/library', { library, expectedRevision: library.revision }, { 'Sec-Fetch-Site': 'cross-site' })).status, 403);
      assert.equal((await fetch(base + '/api/library', { method: 'PUT', headers: { 'Content-Type': 'text/plain' }, body: '{}' })).status, 415);
      assert.deepEqual(await (await json('GET', '/api/library')).json(), library);
    });
    await t.test('corrupt persisted library is reported without overwriting it or its backup', async () => {
      const libraryPath = path.join(dataRoot, 'library.json');
      const backupBefore = await readFile(libraryPath + '.bak', 'utf8');
      await writeFile(libraryPath, '{damaged');
      assert.equal((await json('GET', '/api/library')).status, 503);
      assert.equal((await save()).status, 503);
      assert.equal(await readFile(libraryPath, 'utf8'), '{damaged');
      assert.equal(await readFile(libraryPath + '.bak', 'utf8'), backupBefore);
      await writeFile(libraryPath, JSON.stringify(library));
    });
    await t.test('ordinary home graphs, recovery tokens and checkpoints preserve the complete library envelope', async () => {
      let unified = normalizeLibraryDocument(library);
      const canvasId = unified.project.entryCanvasId;
      const note = addNode(unified.project, canvasId, 'text', 'An ordinary home note');
      note.text = 'Recovery text'; note.style = { shape: 'ellipse' };
      const image = addNode(unified.project, canvasId, 'image', 'Home image'); image.assetId = 'home-image';
      unified.project.assets.push({ id: 'home-image', name: 'small.png', mime: 'image/png', base64: Buffer.from('retained media bytes').toString('base64') });
      const inner = addCanvas(unified.project, 'Home-owned research');
      const launcher = addNode(unified.project, canvasId, 'canvas-link', 'Research'); launcher.targetId = inner.rootId;
      unified.project.links.push({ id: 'home-link', canvasId, sourceId: note.id, targetId: image.id, kind: 'visual', relation: 'causal', direction: 'both', label: 'illustration' });
      const response = await save(unified, unified.revision); assert.equal(response.status, 200, await response.clone().text());
      unified = await response.json();
      assert.equal(unified.schemaVersion, 2); assert.equal(unified.project.revision, unified.revision);
      assert.deepEqual(await (await json('GET', '/api/library')).json(), unified);
      assert.equal((await save({ ...library, revision: unified.revision }, unified.revision)).status, 409, 'A legacy writer cannot discard the new home graph by downgrading its format.');
      assert.deepEqual(await (await json('GET', '/api/library')).json(), unified);
      assert.ok((await (await json('GET', '/__meridian_status')).json()).capabilities.includes('library-canvas-v2'));
      const expected = unified.revision;
      const invalids = [
        { ...unified, project: { ...unified.project, revision: expected - 1 } },
        { ...unified, project: { ...unified.project, nodes: unified.project.nodes.map(node => node.id === note.id ? { ...node, parentId: 'missing' } : node) } },
        { ...unified, canvasTargets: { unknown: { projectId: 'some-project' } } },
        { ...unified, canvasTargets: { [note.id]: { projectId: 'some-project' } } },
        { ...unified, discoveredTargets: ['../escape:canvas'] },
        { ...unified, discoveredTargets: ['saved:canvas', 'saved:canvas'] },
      ];
      for (const invalid of invalids) assert.equal((await save(invalid, expected)).status, 400);
      assert.deepEqual(await (await json('GET', '/api/library')).json(), unified);
      assert.equal((await json('GET', '/api/library/recovery')).status, 404);
      const draftA = structuredClone(unified); draftA.project.nodes.find(node => node.id === note.id)!.text = 'Draft A';
      const putDraft = (value: LibraryCanvasDocument, token: string | null) => json('PUT', '/api/library/recovery', { library: value, baseRevision: value.revision, expectedSavedAt: token });
      const aResponse = await putDraft(draftA, null); assert.equal(aResponse.status, 200, await aResponse.clone().text());
      const a = await aResponse.json();
      assert.deepEqual(a.library, draftA); assert.deepEqual(a.project, draftA.project); assert.equal(a.baseRevision, expected);
      const draftB = structuredClone(draftA); draftB.project.nodes.find(node => node.id === note.id)!.text = 'Newer draft B';
      assert.equal((await putDraft(draftB, null)).status, 409);
      const bResponse = await putDraft(draftB, a.savedAt); assert.equal(bResponse.status, 200);
      const b = await bResponse.json(); assert.notEqual(a.savedAt, b.savedAt);
      assert.equal((await json('DELETE', '/api/library/recovery', { baseRevision: expected, savedAt: a.savedAt })).status, 409);
      const cpResponse = await json('POST', '/api/library/checkpoints', { library: draftA, name: 'Before changing the note' });
      assert.equal(cpResponse.status, 201, await cpResponse.clone().text());
      const checkpoint = await cpResponse.json();
      assert.deepEqual(await (await json('GET', `/api/library/checkpoints/${checkpoint.id}`)).json(), draftA);
      assert.deepEqual(await (await json('GET', '/api/library/checkpoints')).json(), [checkpoint]);
      const savedAResponse = await save(draftA, expected); assert.equal(savedAResponse.status, 200);
      unified = await savedAResponse.json();
      assert.deepEqual(await (await json('GET', '/api/library/recovery')).json(), b, 'A newer recovery must survive committing the earlier candidate.');
      const rebasedB = structuredClone(draftB); rebasedB.revision = unified.revision; rebasedB.project.revision = unified.revision;
      const savedBResponse = await save(rebasedB, unified.revision); assert.equal(savedBResponse.status, 200);
      unified = await savedBResponse.json();
      assert.equal((await json('GET', '/api/library/recovery')).status, 404, 'The exact committed recovery is consumed.');
      assert.equal((await save(draftA, expected)).status, 409);
      await stop(); await start();
      assert.deepEqual(await (await json('GET', '/api/library')).json(), unified);
      assert.deepEqual(await (await json('GET', `/api/library/checkpoints/${checkpoint.id}`)).json(), draftA);
      const currentDraftResponse = await putDraft(unified, null); assert.equal(currentDraftResponse.status, 200);
      const currentDraft = await currentDraftResponse.json();
      assert.equal((await json('DELETE', '/api/library/recovery', { baseRevision: currentDraft.baseRevision, savedAt: currentDraft.savedAt })).status, 204);
      assert.equal((await json('GET', '/api/library/recovery')).status, 404);
      assert.equal((await json('PUT', '/api/library/recovery', { library: unified, baseRevision: unified.revision, expectedSavedAt: null }, { Origin: 'https://unrelated.example' })).status, 403);
      assert.equal(await readFile(projectPath, 'utf8'), originalProjectBytes);
    });
  } finally {
    await stop();
    const expectedPrefix = path.resolve(tmpdir()) + path.sep + 'meridian-library-tests-';
    assert.ok(path.resolve(temporary).startsWith(expectedPrefix));
    await rm(temporary, { recursive: true, force: true });
  }
});

function entry(id: string, parentId: string | null = null, kind: LibraryEntry['kind'] = 'section'): LibraryEntry {
  return { id, parentId, kind, title: id, collapsed: false, x: 80, y: 100, ...(kind === 'canvas' ? { projectId: 'library-fixture' } : {}) };
}
async function freePort(): Promise<number> {
  const server = createServer(); server.listen(0, '127.0.0.1'); await once(server, 'listening');
  const address = server.address(); assert.ok(address && typeof address !== 'string');
  const port = address.port;
  await new Promise<void>((resolve, reject) => server.close(error => error ? reject(error) : resolve()));
  return port;
}
function fixture(): MeridianProject {
  const node = { id: 'note', canvasId: 'overview', parentId: 'root-overview', kind: 'text' as const, title: 'Note', text: 'Private content', x: 50, y: 50, width: 240, height: 100 };
  return {
    schemaVersion: 1, id: 'library-fixture', title: 'Canvas collection', revision: 0, entryCanvasId: 'overview',
    canvases: [{ id: 'overview', rootId: 'root-overview', title: 'Overview' }, { id: 'chapter', rootId: 'root-chapter', title: 'Chapter' }],
    nodes: [
      { ...node, id: 'root-overview', parentId: null, kind: 'canvas', title: 'Overview' },
      { ...node, id: 'root-chapter', parentId: null, canvasId: 'chapter', kind: 'canvas', title: 'Chapter' }, node,
      { ...node, id: 'chapter-link', kind: 'canvas-link', targetId: 'root-chapter' },
      { ...node, id: 'chapter-link-duplicate', kind: 'canvas-link', targetId: 'root-chapter' },
    ], links: [], assets: [{ id: 'asset', name: 'private.txt', mime: 'text/plain', base64: Buffer.from('private attachment bytes').toString('base64') }],
    snapshots: {}, dependencies: [],
  };
}
