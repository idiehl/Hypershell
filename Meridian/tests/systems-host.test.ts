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
import { createModule } from '../src/core/modules.ts';
import { evaluate } from '../src/core/engine.ts';
import { NODE_SHAPES } from '../src/core/appearance.ts';
import { validateProject } from '../src/core/rules.ts';
import { addRemoteLink, renameNode } from '../src/core/node-editing.ts';
import { libraryDocumentFromProject, normalizeLibraryDocument } from '../src/core/library.ts';
import type { MeridianProject } from '../src/core/types.ts';

const root = fileURLToPath(new URL('../', import.meta.url));

test('scalar systems and guarded authoring preserve typed graphs and exact revision/draft baselines', { timeout: 120_000 }, async t => {
  const temporary = await mkdtemp(path.join(tmpdir(), 'meridian-systems-tests-'));
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
  const author = (project: MeridianProject, expectedRevision: number, expectedRecoverySavedAt: string | null, checkpointName = 'Before composition') =>
    json('POST', `/api/projects/${project.id}/authoring`, { project, expectedRevision, expectedRecoverySavedAt, checkpointName });
  const recover = async (project: MeridianProject, expectedSavedAt: string | null = null) => {
    const response = await json('PUT', `/api/projects/${project.id}/recovery`, { project, baseRevision: project.revision, expectedSavedAt });
    assert.equal(response.status, 200, await response.clone().text()); return response.json();
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

    await t.test('conceptual links, custom GUIDs, additional attachments and hyperlinks persist through native storage and reject malformed drafts', async () => {
      let project = newProject('Node identity and content'); const node = addNode(project, project.entryCanvasId, 'document', 'Sources');
      project.assets.push({ id: 'supplemental-asset', name: 'source.txt', mime: 'text/plain', base64: 'AQID' });
      node.attachments = ['supplemental-asset']; node.hyperlinks = [{ url: 'https://example.test/source', label: 'Source' }, { url: 'mailto:reader@example.test' }];
      project = addRemoteLink(project, node.id, { targetId: 'remote-zeus-guid', relation: 'mutual', label: 'Alternative account' });
      project = renameNode(commitSnapshots(project), node.id, 'my-sources');
      const committed = await create(project);
      assert.deepEqual(await read(`/api/projects/${committed.id}`), committed);
      assert.deepEqual(committed.nodes.find(node => node.id === 'my-sources')?.attachments, ['supplemental-asset']);
      assert.deepEqual(committed.snapshots['my-sources'].node.remoteLinks, project.snapshots['my-sources'].node.remoteLinks);
      const exported = await json('POST', '/api/export', committed); assert.equal(exported.status, 200, await exported.clone().text());
      const body = new FormData(); body.append('file', new Blob([await exported.arrayBuffer()]), 'sources.meridian');
      const imported = await fetch(base + '/api/import', { method: 'POST', body }); assert.equal(imported.status, 200, await imported.clone().text());
      assert.deepEqual(await imported.json(), committed);
      const cases: Array<(invalid: MeridianProject) => void> = [
        invalid => { invalid.nodes[1].remoteLinks![0].targetId = '../escape'; },
        invalid => { invalid.nodes[1].remoteLinks![0].relation = 'unsupported' as any; },
        invalid => { invalid.nodes[1].remoteLinks![0].id = invalid.id; },
        invalid => { invalid.nodes[1].remoteLinks!.push(structuredClone(invalid.nodes[1].remoteLinks![0])); },
        invalid => { (invalid.nodes[1].remoteLinks![0] as any).fetch = true; },
        invalid => { invalid.nodes[1].attachments = ['same', 'same']; },
        invalid => { invalid.nodes[1].hyperlinks = [{ url: 'javascript:alert(1)' }]; },
        invalid => { invalid.nodes[1].hyperlinks = [{ url: 'https://example.test\n' }]; },
        invalid => { invalid.nodes[1].hyperlinks = [{ url: 'mailto:' }]; },
        invalid => { invalid.snapshots['my-sources'].node.remoteLinks![0].targetId = 'bad/target'; },
        invalid => { invalid.snapshots['my-sources'].node.hyperlinks = [{ url: 'file:///C:/private' }]; },
      ];
      for (const change of cases) {
        const invalid = structuredClone(committed); change(invalid);
        const response = await json('PUT', `/api/projects/${committed.id}`, { project: invalid, expectedRevision: committed.revision });
        assert.equal(response.status, 400, await response.clone().text());
        assert.deepEqual(await read(`/api/projects/${committed.id}`), committed);
      }
    });

    await t.test('the home canvas accepts a custom root and launcher GUID without losing destinations or hierarchy', async () => {
      const source = normalizeLibraryDocument(await read('/api/library'));
      const launcher = addNode(source.project, source.project.entryCanvasId, 'canvas-link', 'Greek mythology');
      launcher.canvasTarget = { projectId: 'external-project', canvasId: 'external-canvas' };
      let renamed = renameNode(source.project, source.project.canvases[0].rootId, 'my-library-root');
      renamed = renameNode(renamed, launcher.id, 'my-greek-atlas');
      const library = libraryDocumentFromProject(source, renamed);
      const saved = await json('PUT', '/api/library', { library, expectedRevision: library.revision });
      assert.equal(saved.status, 200, await saved.clone().text());
      const restored = normalizeLibraryDocument(await saved.json());
      assert.equal(restored.project.canvases[0].rootId, 'my-library-root');
      assert.deepEqual(restored.canvasTargets['my-greek-atlas'], { projectId: 'external-project', canvasId: 'external-canvas' });
      assert.equal(restored.project.nodes.find(node => node.id === 'my-greek-atlas')?.parentId, 'my-library-root');
      assert.deepEqual(await read('/api/library'), restored);
    });

    await t.test('basic backdrop shapes round-trip through saved nodes, snapshots and native packages', async () => {
      const project = newProject('Shape persistence');
      for (const shape of NODE_SHAPES) addNode(project, project.entryCanvasId, 'text', shape.label).style = { shape: shape.value };
      const committed = await create(commitSnapshots(project));
      assert.deepEqual(committed.nodes, project.nodes);
      for (const node of committed.nodes.filter(node => node.style?.shape)) assert.equal(committed.snapshots[node.id].node.style?.shape, node.style?.shape);
      const exported = await json('POST', '/api/export', committed);
      assert.equal(exported.status, 200, await exported.clone().text());
      const body = new FormData(); body.append('file', new Blob([await exported.arrayBuffer()]), 'shapes.meridian');
      const imported = await fetch(base + '/api/import', { method: 'POST', body });
      assert.equal(imported.status, 200, await imported.clone().text());
      assert.deepEqual(await imported.json(), committed);
      for (const invalidShape of ['star', 'url(external.svg)', 4, null]) {
        const invalid = structuredClone(committed); invalid.nodes[1].style = { shape: invalidShape } as any;
        const response = await json('PUT', `/api/projects/${committed.id}`, { project: invalid, expectedRevision: committed.revision });
        assert.equal(response.status, 400, String(invalidShape));
        assert.deepEqual(await read(`/api/projects/${committed.id}`), committed);
      }
      const invalidSnapshot = structuredClone(committed);
      invalidSnapshot.snapshots[committed.nodes[1].id].node.style = { shape: 'star' } as any;
      assert.equal((await json('POST', '/api/projects', { ...invalidSnapshot, id: crypto.randomUUID() })).status, 400);
    });

    await t.test('ordinary external canvas launchers preserve strict destination data in saves, snapshots and native packages', async () => {
      const project = newProject('Canvas destinations');
      const launcher = addNode(project, project.entryCanvasId, 'canvas-link', 'Open another canvas');
      launcher.canvasTarget = { projectId: 'saved-project', canvasId: 'saved-canvas' };
      addNode(project, project.entryCanvasId, 'canvas-link', 'Open entry canvas').canvasTarget = { projectId: 'saved-project' };
      const committed = await create(commitSnapshots(project));
      assert.deepEqual(committed.nodes, project.nodes);
      assert.deepEqual(committed.snapshots[launcher.id].node.canvasTarget, launcher.canvasTarget);
      const exported = await json('POST', '/api/export', committed);
      assert.equal(exported.status, 200, await exported.clone().text());
      const form = new FormData(); form.append('file', new Blob([await exported.arrayBuffer()]), 'destinations.meridian');
      const imported = await fetch(base + '/api/import', { method: 'POST', body: form });
      assert.equal(imported.status, 200, await imported.clone().text()); assert.deepEqual(await imported.json(), committed);
      for (const destination of [null, [], {}, { projectId: '' }, { projectId: '../escape' }, { projectId: 'https://external.test' },
        { projectId: 'safe\n' }, { projectId: 'safe', canvasId: null }, { projectId: 'safe', canvasId: 'a'.repeat(97) },
        { projectId: 'safe', url: 'file:///private' }]) {
        const invalid = structuredClone(committed); invalid.nodes.find(node => node.id === launcher.id)!.canvasTarget = destination as any;
        assert.equal((await json('PUT', `/api/projects/${committed.id}`, { project: invalid, expectedRevision: committed.revision })).status, 400, JSON.stringify(destination));
        assert.deepEqual(await read(`/api/projects/${committed.id}`), committed);
      }
      for (const change of [
        (invalid: MeridianProject) => { invalid.nodes.find(node => node.id === launcher.id)!.kind = 'text'; },
        (invalid: MeridianProject) => { invalid.nodes.find(node => node.id === launcher.id)!.targetId = invalid.canvases[0].rootId; },
        (invalid: MeridianProject) => { invalid.snapshots[launcher.id].node.canvasTarget = { projectId: 'https://external.test' }; },
      ]) {
        const invalid = structuredClone(committed); change(invalid);
        assert.equal((await json('PUT', `/api/projects/${committed.id}`, { project: invalid, expectedRevision: committed.revision })).status, 400);
      }
    });

    await t.test('container variants round-trip and malformed current or saved variants are rejected', async () => {
      const project = newProject('Saved container variants');
      for (const variant of ['spatial', 'contained', 'grid', 'spreadsheet', 'gallery'] as const) addNode(project, project.entryCanvasId, 'container', variant).containerVariant = variant;
      addNode(project, project.entryCanvasId, 'module', 'Module gallery').containerVariant = 'gallery';
      addNode(project, project.entryCanvasId, 'spreadsheet', 'Sheet variant').containerVariant = 'spreadsheet';
      const committed = await create(commitSnapshots(project));
      assert.deepEqual(committed.nodes, project.nodes);
      for (const node of committed.nodes.filter(node => node.containerVariant)) assert.equal(committed.snapshots[node.id].node.containerVariant, node.containerVariant);
      const container = committed.nodes.find(node => node.kind === 'container')!;
      for (const value of ['section', 'custom', 4, null]) {
        const invalid = structuredClone(committed); invalid.nodes.find(node => node.id === container.id)!.containerVariant = value as any;
        assert.equal((await json('PUT', `/api/projects/${committed.id}`, { project: invalid, expectedRevision: committed.revision })).status, 400);
      }
      const invalidKind = structuredClone(committed); Object.assign(invalidKind.nodes.find(node => node.id === container.id)!, { kind: 'media', childLayout: 'contained' });
      assert.equal((await json('PUT', `/api/projects/${committed.id}`, { project: invalidKind, expectedRevision: committed.revision })).status, 400);
      const invalidSnapshot = structuredClone(committed); invalidSnapshot.snapshots[container.id].node.containerVariant = 'section' as any;
      assert.equal((await json('PUT', `/api/projects/${committed.id}`, { project: invalidSnapshot, expectedRevision: committed.revision })).status, 400);
      assert.deepEqual(await read(`/api/projects/${committed.id}`), committed);
    });

    await t.test('numeric nodes, typed junctions, switches and snapshot values round-trip through save and native package', async () => {
      const { project, resultId } = fixture();
      const committed = await create(commitSnapshots(project));
      assert.equal(evaluate(committed).values[resultId], 36);
      assert.deepEqual(committed.nodes, project.nodes);
      const exported = await json('POST', '/api/export', committed);
      assert.equal(exported.status, 200, await exported.clone().text());
      const body = new FormData(); body.append('file', new Blob([await exported.arrayBuffer()]), 'systems.meridian');
      const imported = await fetch(base + '/api/import', { method: 'POST', body });
      assert.equal(imported.status, 200, await imported.clone().text());
      assert.deepEqual(await imported.json(), committed);
      assert.deepEqual(await read(`/api/projects/${project.id}`), committed);
    });
    await t.test('numeric assemblies save as inert module definitions with complete operations and value types', async () => {
      const { project, groupId } = fixture();
      const stored = createModule(project, groupId, 'Numeric assembly');
      const response = await json('POST', '/api/modules', { module: stored });
      assert.equal(response.status, 201, await response.clone().text());
      assert.deepEqual(await read(`/api/modules/${stored.id}`), stored);
      const result = stored.project.nodes.find(node => node.title === 'Total')!;
      assert.equal(evaluate(stored.project).values[result.id], 36);
    });
    await t.test('wrong scalar kinds, conflicting declarations, invalid operations and occupied ports are rejected atomically', async () => {
      const committed = await create();
      const changes: Array<[string, (project: any) => void]> = [
        ['number Boolean value', project => { project.nodes.find((node: any) => node.kind === 'number').value = true; }],
        ['Boolean numeric value', project => { const node = project.nodes.find((node: any) => node.kind === 'number'); node.kind = 'input'; node.value = 1; }],
        ['conflicting number type', project => { project.nodes.find((node: any) => node.kind === 'number').valueType = 'boolean'; }],
        ['unknown value type', project => { project.nodes.find((node: any) => node.kind === 'switch').valueType = 'string'; }],
        ['unknown operation', project => { project.nodes.find((node: any) => node.kind === 'joint').operation = 'EXEC'; }],
        ['junction operation', project => { project.nodes.find((node: any) => node.kind === 'junction').operation = 'ADD'; }],
        ['channel mismatch', project => { project.links[0].kind = 'functional'; }],
        ['port out of range', project => { project.links[0].targetPort = 5; }],
        ['occupied port', project => { project.links.push({ ...project.links[0], id: crypto.randomUUID() }); }],
        ['invalid snapshot value', project => { project.snapshots.outside = { node: { ...project.nodes.find((node: any) => node.kind === 'number'), id: 'outside' }, value: '36', savedAt: new Date().toISOString() }; }],
      ];
      for (const [label, change] of changes) {
        const invalid = structuredClone(committed); change(invalid);
        const response = await json('PUT', `/api/projects/${committed.id}`, { project: invalid, expectedRevision: committed.revision });
        assert.equal(response.status, 400, `${label}: ${await response.clone().text()}`);
        assert.deepEqual(await read(`/api/projects/${committed.id}`), committed, label);
      }
    });
    await t.test('cross-canvas computed links and feedback through numeric references are rejected', async () => {
      const { project, resultId } = fixture();
      const other = addCanvas(project, 'Other');
      const foreign = addNode(project, other.id, 'number');
      project.links[0].sourceId = foreign.id;
      assert.equal((await json('POST', '/api/projects', project)).status, 400);
      const cyclic = fixture();
      const reference = addNode(cyclic.project, cyclic.project.entryCanvasId, 'reference'); reference.targetId = cyclic.resultId;
      cyclic.project.links[0].sourceId = reference.id;
      assert.ok(validateProject(cyclic.project).some(issue => /feedback/.test(issue.message)));
      assert.equal((await json('POST', '/api/projects', cyclic.project)).status, 400);
    });
    await t.test('node, operation and connection restrictions remain inherited across ordinary saved canvases', async () => {
      const source = fixture();
      const group = source.project.nodes.find(node => node.id === source.groupId)!;
      group.rules = { nodeKinds: ['number', 'joint', 'junction', 'switch', 'output'], operations: ['MULTIPLY'], linkKinds: ['numeric'] };
      const committed = await create(source.project);
      const invalidCases: Array<[string, (project: any) => void]> = [
        ['excluded child kind', project => { project.nodes.find((node: any) => node.id === source.groupId).rules.nodeKinds = ['number', 'joint', 'switch', 'output']; }],
        ['excluded operation', project => { project.nodes.find((node: any) => node.kind === 'joint').operation = 'ADD'; }],
        ['excluded numeric connection', project => { project.nodes.find((node: any) => node.id === source.groupId).rules.linkKinds = ['visual']; }],
        ['widened child operation permissions', project => { project.nodes.find((node: any) => node.kind === 'joint').rules = { operations: ['MULTIPLY', 'ADD'] }; }],
        ['unknown inherited operation', project => { project.nodes.find((node: any) => node.id === source.groupId).rules.operations = ['EXEC']; }],
        ['malformed rules', project => { project.nodes.find((node: any) => node.id === source.groupId).rules = null; }],
        ['missing parent', project => { project.nodes.find((node: any) => node.kind === 'number').parentId = 'absent'; }],
        ['containment cycle', project => { const first = project.nodes.find((node: any) => node.id === source.groupId); first.parentId = project.nodes.find((node: any) => node.kind === 'number').id; }],
      ];
      for (const [label, change] of invalidCases) {
        const invalid = structuredClone(committed); change(invalid);
        const response = await json('PUT', `/api/projects/${committed.id}`, { project: invalid, expectedRevision: committed.revision });
        assert.equal(response.status, 400, `${label}: ${await response.clone().text()}`);
      }
      const valid = structuredClone(committed);
      valid.nodes.find(node => node.kind === 'number')!.rules = { linkKinds: [] };
      assert.deepEqual(validateProject(valid), [], 'Own permissions restrict descendants, not the parent node itself');
      const response = await json('PUT', `/api/projects/${committed.id}`, { project: valid, expectedRevision: committed.revision });
      assert.equal(response.status, 200, await response.clone().text());
    });
    await t.test('saved numeric reference types follow snapshots and malformed snapshot fields are rejected', async () => {
      let { project } = fixture();
      const source = project.nodes.find(node => node.kind === 'number')!;
      const other = addCanvas(project, 'Snapshot consumer');
      const reference = addNode(project, other.id, 'reference'); reference.targetId = source.id;
      const output = addNode(project, other.id, 'output'); output.valueType = 'number';
      project.links.push({ id: crypto.randomUUID(), canvasId: other.id, sourceId: reference.id, targetId: output.id, kind: 'numeric', relation: 'causal' });
      project = commitSnapshots(project);
      const committed = await create(project);
      const cases: Array<[string, (project: any) => void]> = [
        ['wrong snapshot identity', project => { project.snapshots[source.id].node.id = 'wrong'; }],
        ['missing snapshot value', project => { delete project.snapshots[source.id].value; }],
        ['missing snapshot timestamp', project => { delete project.snapshots[source.id].savedAt; }],
        ['null node value', project => { project.snapshots[source.id].node.value = null; }],
        ['malformed snapshot geometry', project => { project.snapshots[source.id].node.x = 'left'; }],
        ['malformed snapshot rules', project => { project.snapshots[source.id].node.rules = { nodeKinds: [4] }; }],
      ];
      for (const [label, change] of cases) {
        const invalid = structuredClone(committed); change(invalid);
        assert.equal((await json('POST', '/api/export', invalid)).status, 400, label);
      }
      const copied = structuredClone(committed);
      const live = copied.nodes.find(node => node.id === source.id)!;
      live.kind = 'input'; live.value = true;
      copied.links = copied.links.filter(link => link.sourceId !== live.id);
      assert.deepEqual(validateProject(copied), []);
      assert.equal(evaluate(copied).values[output.id], 12);
      assert.equal((await json('POST', '/api/export', copied)).status, 200, 'Unsaved live type changes cannot leak into another canvas saved reference');
    });
    await t.test('authoring without a draft increments once and checkpoints the exact saved baseline', async () => {
      const committed = await create(), candidate = { ...structuredClone(committed), title: 'Authored composition' };
      const response = await author(candidate, committed.revision, null);
      assert.equal(response.status, 200, await response.clone().text());
      const next = await response.json(); assert.equal(next.revision, committed.revision + 1);
      assert.equal(next.title, candidate.title);
      const checkpoints = await read(`/api/projects/${committed.id}/checkpoints`);
      assert.equal(checkpoints.length, 1); assert.equal(checkpoints[0].name, 'Before composition · saved');
      assert.deepEqual(await read(`/api/projects/${committed.id}/checkpoints/${checkpoints[0].id}`), committed);
      assert.equal((await json('GET', `/api/projects/${committed.id}/recovery`)).status, 404);
      assert.equal((await author(candidate, committed.revision, null)).status, 409);
      assert.equal((await read(`/api/projects/${committed.id}/checkpoints`)).length, 1);
    });
    await t.test('authoring incorporates an exact draft and preserves saved and draft checkpoints', async () => {
      const committed = await create(), draft = { ...structuredClone(committed), title: 'Unsaved draft title' };
      draft.nodes.find(node => node.kind === 'number')!.value = 21;
      const recovery = await recover(draft);
      const candidate = structuredClone(draft); candidate.nodes.find(node => node.kind === 'container')!.collapsed = true;
      const response = await author(candidate, committed.revision, recovery.savedAt, 'Before hierarchy');
      assert.equal(response.status, 200, await response.clone().text());
      assert.deepEqual(await response.json(), { ...candidate, revision: committed.revision + 1 });
      const checkpoints = await read(`/api/projects/${committed.id}/checkpoints`);
      assert.equal(checkpoints.length, 2);
      for (const [suffix, expected] of [['saved', committed], ['draft', draft]] as const) {
        const checkpoint = checkpoints.find((entry: any) => entry.name === `Before hierarchy · ${suffix}`);
        assert.ok(checkpoint);
        assert.deepEqual(await read(`/api/projects/${committed.id}/checkpoints/${checkpoint.id}`), expected);
      }
      assert.equal((await json('GET', `/api/projects/${committed.id}/recovery`)).status, 404);
    });
    await t.test('missing or raced draft tokens and mismatched revisions preserve all files and create no checkpoints', async () => {
      const committed = await create(), draft = { ...structuredClone(committed), title: 'First draft' };
      const first = await recover(draft), later = await recover({ ...draft, title: 'Newer draft' }, first.savedAt);
      const candidate = { ...draft, title: 'Composition from old draft' };
      assert.equal((await author(candidate, committed.revision, first.savedAt)).status, 409);
      assert.equal((await author(candidate, committed.revision, null)).status, 409);
      assert.equal((await author({ ...candidate, revision: committed.revision + 1 }, committed.revision, later.savedAt)).status, 409);
      const missing = await json('POST', `/api/projects/${committed.id}/authoring`, { project: candidate, expectedRevision: committed.revision, checkpointName: 'Missing token' });
      assert.equal(missing.status, 400);
      assert.equal((await author(candidate, committed.revision, later.savedAt, '')).status, 400);
      assert.deepEqual(await read(`/api/projects/${committed.id}`), committed);
      assert.deepEqual(await read(`/api/projects/${committed.id}/recovery`), later);
      assert.deepEqual(await read(`/api/projects/${committed.id}/checkpoints`), []);
    });
    await t.test('stale recovery bases cannot be silently incorporated into a newer saved revision', async () => {
      const committed = await create(), recovery = await recover({ ...committed, title: 'Old draft' });
      const regular = await json('PUT', `/api/projects/${committed.id}`, { project: { ...committed, title: 'Saved elsewhere' }, expectedRevision: committed.revision });
      assert.equal(regular.status, 200); const current = await regular.json();
      const response = await author({ ...current, title: 'Silent rebase' }, current.revision, recovery.savedAt);
      assert.equal(response.status, 409, await response.clone().text());
      assert.match((await response.json()).error, /stale recovery/);
      assert.deepEqual(await read(`/api/projects/${committed.id}`), current);
      assert.deepEqual(await read(`/api/projects/${committed.id}/recovery`), recovery);
      assert.deepEqual(await read(`/api/projects/${committed.id}/checkpoints`), []);
    });
    await t.test('simultaneous authoring candidates have one winner and one recoverable baseline', async () => {
      const committed = await create();
      const responses = await Promise.all(['First composition', 'Second composition'].map(title => author({ ...committed, title }, committed.revision, null)));
      assert.deepEqual(responses.map(response => response.status).sort(), [200, 409]);
      const saved = await read(`/api/projects/${committed.id}`);
      assert.equal(saved.revision, 2);
      assert.equal((await read(`/api/projects/${committed.id}/checkpoints`)).length, 1);
    });
  } finally {
    if (host && host.exitCode === null) { const exit = once(host, 'exit'); host.kill(); await exit; }
    assert.ok(path.resolve(temporary).startsWith(path.resolve(tmpdir()) + path.sep + 'meridian-systems-tests-'));
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
  const project = newProject('Numeric host fixture'), canvas = project.entryCanvasId;
  const group = addNode(project, canvas, 'container', 'Arithmetic');
  const price = addNode(project, canvas, 'number', 'Price'); price.value = 12;
  const quantity = addNode(project, canvas, 'number', 'Quantity'); quantity.value = 3;
  const joint = addNode(project, canvas, 'joint', 'Product'); joint.operation = 'MULTIPLY';
  const control = addNode(project, canvas, 'switch', 'Enabled'); control.valueType = 'number';
  const route = addNode(project, canvas, 'junction', 'Split'); route.valueType = 'number';
  const result = addNode(project, canvas, 'output', 'Total'); result.valueType = 'number';
  for (const node of [price, quantity, joint, control, route, result]) node.parentId = group.id;
  for (const [source, target, port] of [[price, joint, 0], [quantity, joint, 1], [joint, control, 0], [control, route, 0], [route, result, 0]] as const)
    project.links.push({ id: crypto.randomUUID(), canvasId: canvas, sourceId: source.id, targetId: target.id, targetPort: port, kind: 'numeric', relation: 'causal' });
  assert.deepEqual(validateProject(project), []);
  return { project, groupId: group.id, resultId: result.id };
}
