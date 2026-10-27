import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { mkdtemp, mkdir, writeFile, readFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, resolve, relative, isAbsolute } from 'node:path';
import { once } from 'node:events';
import { test } from 'node:test';
import { createAuthoringService, runAuthoringWorker, AuthoringServiceError } from '../src/authoring-service.mjs';

const rules = 'PURPOSE-004 [ACCEPTED]\nCompose a graph.\n';
const png = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/ZlsAAAAASUVORK5CYII=', 'base64');
async function fixture(t, { realPreview = false, captureError } = {}) {
  const directory = await mkdtemp(join(tmpdir(), 'meridian-authoring-test-'));
  t.after(async () => {
    const expected = resolve(tmpdir()), target = resolve(directory), part = relative(expected, target);
    assert(part && !part.startsWith('..') && !isAbsolute(part));
    await rm(target, { recursive: true, force: true });
  });
  const initial = await runAuthoringWorker({ command: 'new', title: 'Fixture project' });
  const firstWork = initial.entryCanvasId;
  const seeded = await runAuthoringWorker({ command: 'apply', project: initial, recipe: { version: 1, canvases: [{ key: 'other', title: 'Unrelated Work' }], nodes: [
    { key: 'original', canvas: firstWork, kind: 'media', title: 'Original', x: 0, y: 0, width: 240, height: 100, sizeMode: 'fixed', childLayout: 'spatial', childArrangement: 'free', collapsed: false },
    { key: 'unrelated', canvas: 'other', kind: 'media', title: 'Preserve this', x: 50, y: 80, width: 240, height: 100, sizeMode: 'fixed' },
  ] } });
  let project = seeded.project; project.revision = 3;
  let recovery = false, currentRules = rules, checkpoints = 0, loseResponse = false, hideCheckpoints = false;
  const checkpointRecords = [];
  const requests = [];
  const backend = createServer(async (req, res) => {
    requests.push({ method: req.method, path: req.url });
    res.setHeader('content-type', 'application/json');
    if (req.url === '/__meridian_status') return res.end(JSON.stringify({ application: 'atlas-meridian', capabilities: ['authoring-save-guard-v1', 'authoring-recovery-guard-v1'] }));
    if (req.url === `/api/projects/${project.id}/recovery`) { res.statusCode = recovery ? 200 : 404; return res.end(JSON.stringify(recovery ? { savedAt: 'fixture-recovery' } : {})); }
    if (req.url === `/api/projects/${project.id}/checkpoints`) return res.end(JSON.stringify(hideCheckpoints ? [] : checkpointRecords.map(record => record.metadata)));
    if (req.url.startsWith(`/api/projects/${project.id}/checkpoints/`)) {
      const record = checkpointRecords.find(record => req.url.endsWith(`/${record.metadata.id}`));
      if (!record) { res.statusCode = 404; return res.end('{}'); }
      return res.end(JSON.stringify(record.project));
    }
    if (req.url === `/api/projects/${project.id}` && req.method === 'GET') return res.end(JSON.stringify(project));
    if (req.url === `/api/projects/${project.id}/authoring` && req.method === 'POST') {
      let body = ''; for await (const chunk of req) body += chunk;
      const parsed = JSON.parse(body);
      if (recovery || parsed.expectedRevision !== project.revision || parsed.expectedRecoverySavedAt !== null) { res.statusCode = 409; return res.end('{}'); }
      checkpoints++;
      checkpointRecords.push({ metadata: { id: `checkpoint-${checkpoints}`, name: `${parsed.checkpointName} · saved`, revision: project.revision, createdAt: new Date().toISOString() }, project: structuredClone(project) });
      project = structuredClone(parsed.project); project.revision++;
      if (loseResponse) { loseResponse = false; return res.destroy(); }
      return res.end(JSON.stringify(project));
    }
    res.statusCode = 404; res.end('{}');
  });
  backend.listen(0, '127.0.0.1'); await once(backend, 'listening');
  t.after(() => new Promise(resolveClose => backend.close(resolveClose)));
  const worker = async input => {
    if (input.command === 'capture' && captureError) throw captureError;
    if (input.command !== 'capture' || realPreview) return runAuthoringWorker(input);
    await mkdir(input.directory, { recursive: true });
    await writeFile(join(input.directory, 'preview.png'), png);
    await writeFile(join(input.directory, 'preview.html'), '<!doctype html><title>Fixture preview</title>');
    return { browserErrors: [], canvases: [{ id: input.workId, screenshot: 'preview.png', nodes: [] }] };
  };
  const config = { stateRoot: directory, meridianOrigin: `http://127.0.0.1:${backend.address().port}`, projectIds: [project.id] };
  const service = createAuthoringService(config, { runWorker: worker, readRules: async () => currentRules });
  return { service, config, directory, firstWork, aliases: seeded.aliases, initial: structuredClone(project), requests,
    get project() { return project; }, setRecovery(value) { recovery = value; }, changeRevision() { project.revision++; },
    changeRules() { currentRules += '\nNODE-005 [ACCEPTED]\nConcise names.\n'; }, get checkpoints() { return checkpoints; },
    loseNextSaveResponse() { loseResponse = true; }, hideCheckpoints(value) { hideCheckpoints = value; },
    tamperCheckpoint() { checkpointRecords[0].project.title = 'Different baseline'; }, worker };
}
const errorCode = code => error => error.code === code;
const update = (id, title) => ({ version: 1, nodes: [{ key: 'original', existingId: id, title }] });
async function approved(f, draft) {
  const validation = await f.service.validate({ draftId: draft.draftId, expectedVersion: draft.version });
  assert.equal(validation.passed, true, JSON.stringify(validation.diagnostics));
  const preview = await f.service.preview({ draftId: draft.draftId, expectedVersion: draft.version });
  return { draftId: draft.draftId, expectedVersion: draft.version, validationId: validation.validationId, previewId: preview.previewId, visualReviewConfirmed: true };
}

test('authoring schema is bounded, declarative, and exposes identity-preserving examples', async t => {
  const f = await fixture(t), schema = await f.service.schema();
  assert.equal(schema.version, 1);
  assert.match(schema.rulesHash, /^sha256:/);
  assert(schema.nodeFields.includes('existingId'));
  assert(schema.nodeFields.includes('style'));
  assert(!schema.nodeFields.includes('cluster'));
  assert(Buffer.byteLength(JSON.stringify(schema)) < 25000);
});

test('preview failures retain their internal code with only a fixed non-enumerable stage annotation', async t => {
  const expected = Object.assign(new Error('Sensitive fixture path and document text must not enter audit metadata.'), { code: 'EINVAL', errno: -22 });
  const f = await fixture(t, { captureError: expected });
  const draft = await f.service.draftCreate({ projectId: f.project.id, workId: f.firstWork });
  await assert.rejects(f.service.preview({ draftId: draft.draftId, expectedVersion: draft.version }), error => {
    assert.equal(error, expected);
    assert.equal(error.code, 'EINVAL');
    assert.equal(error.previewStage, 'capture-worker');
    assert(!Object.keys(error).includes('previewStage'));
    return true;
  });
  assert(f.requests.every(request => request.method === 'GET'));
  assert.equal((await f.service.draftDiscard({ draftId: draft.draftId, expectedVersion: draft.version })).savedWorksChanged, false);
});

test('preview failures propagate only fixed worker stage and safe browser diagnostic code', async t => {
  const expected = new AuthoringServiceError('authoring-rejected', 'Sensitive internal worker message.');
  Object.defineProperties(expected, { workerStage: { value: 'navigate-preview', configurable: true }, previewDiagnosticCode: { value: 'ERR_INVALID_ARGUMENT', configurable: true } });
  const f = await fixture(t, { captureError: expected });
  const draft = await f.service.draftCreate({ projectId: f.project.id, workId: f.firstWork });
  await assert.rejects(f.service.preview({ draftId: draft.draftId, expectedVersion: draft.version }), error => {
    assert.equal(error.code, 'authoring-rejected'); assert.equal(error.previewStage, 'navigate-preview'); assert.equal(error.previewDiagnosticCode, 'ERR_INVALID_ARGUMENT');
    assert.deepEqual(error.details, {});
    return true;
  });
  await f.service.draftDiscard({ draftId: draft.draftId, expectedVersion: draft.version });
});

test('inspection is scoped, paginated, includes exact editable node data without asset bytes', async t => {
  const f = await fixture(t);
  const result = await f.service.inspect({ projectId: f.project.id, workId: f.firstWork, entity: 'nodes', limit: 1 });
  assert.equal(result.items.length, 1);
  assert.equal(result.total, 2);
  assert.equal(result.nextOffset, 1);
  await assert.rejects(f.service.inspect({ projectId: 'not-shared' }), errorCode('project-not-shared'));
  await assert.rejects(f.service.inspect({ projectId: f.project.id, limit: 51 }), errorCode('invalid-page'));
});

test('draft edits preserve identity, unrelated Works, failed atomic recipes, and durable versions', async t => {
  const f = await fixture(t);
  const created = await f.service.draftCreate({ projectId: f.project.id, workId: f.firstWork });
  const edited = await f.service.draftApply({ draftId: created.draftId, expectedVersion: 1, recipe: update(f.aliases.nodes.original, 'Renamed') });
  assert.equal(edited.version, 2);
  const page = await f.service.inspect({ draftId: created.draftId, query: 'Renamed' });
  assert.equal(page.items[0].id, f.aliases.nodes.original);
  await assert.rejects(f.service.draftApply({ draftId: created.draftId, expectedVersion: 1, recipe: update(f.aliases.nodes.original, 'Stale') }), errorCode('stale-draft'));
  await assert.rejects(f.service.draftApply({ draftId: created.draftId, expectedVersion: 2, recipe: { version: 1, nodes: [{ key: 'bad', existingId: f.aliases.nodes.original, width: -4 }] } }));
  const restarted = createAuthoringService(f.config, { runWorker: f.worker, readRules: async () => rules });
  assert.equal((await restarted.inspect({ draftId: created.draftId, query: 'Renamed' })).draft.version, 2);
  assert.equal(f.project.nodes.find(n => n.id === f.aliases.nodes.original).title, 'Original');
});

test('cross-Work changes and arbitrary recipe fields are rejected without changing a draft', async t => {
  const f = await fixture(t), draft = await f.service.draftCreate({ projectId: f.project.id, workId: f.firstWork });
  await assert.rejects(f.service.draftApply({ draftId: draft.draftId, expectedVersion: 1, recipe: update(f.aliases.nodes.unrelated, 'Do not edit') }), errorCode('scope-violation'));
  await assert.rejects(f.service.draftApply({ draftId: draft.draftId, expectedVersion: 1, recipe: { version: 1, title: 'Whole project' } }), errorCode('scope-violation'));
  await assert.rejects(f.service.draftApply({ draftId: draft.draftId, expectedVersion: 1, recipe: { version: 1, code: 'process.exit()' } }));
  assert.equal((await f.service.inspect({ draftId: draft.draftId })).draft.version, 1);
});

test('new Works are added inside a shared project and save with exact unrelated graph preservation', async t => {
  const f = await fixture(t), draft = await f.service.draftCreate({ projectId: f.project.id, title: 'New Topic' });
  const edited = await f.service.draftApply({ draftId: draft.draftId, expectedVersion: 1, recipe: { version: 1, nodes: [
    { key: 'one', canvas: draft.workId, kind: 'media', title: 'Object', x: 100, y: 100, width: 250, height: 100, sizeMode: 'fixed' },
  ] } });
  const receipt = await f.service.save(await approved(f, edited));
  assert.equal(receipt.revision, 4); assert.equal(receipt.checkpointed, true); assert.equal(f.checkpoints, 1);
  assert.equal(f.project.canvases.length, f.initial.canvases.length + 1);
  assert.deepEqual(f.project.nodes.filter(n => n.canvasId !== draft.workId), f.initial.nodes);
  assert.deepEqual(f.project.canvases.filter(c => c.id !== draft.workId), f.initial.canvases);
  assert.deepEqual(f.project.assets, f.initial.assets);
  assert.deepEqual(f.project.links, f.initial.links);
});

test('expanded overlap and link-through-body diagnostics block new invalid geometry', async t => {
  const f = await fixture(t), draft = await f.service.draftCreate({ projectId: f.project.id, workId: f.firstWork });
  const edited = await f.service.draftApply({ draftId: draft.draftId, expectedVersion: 1, recipe: { version: 1, nodes: [
    { key: 'overlap', canvas: f.firstWork, kind: 'media', title: 'Overlap', x: 120, y: 20, width: 240, height: 100, sizeMode: 'fixed' },
  ] } });
  const validation = await f.service.validate({ draftId: edited.draftId, expectedVersion: 2 });
  assert.equal(validation.passed, false);
  assert(validation.diagnostics.some(d => d.code === 'expanded-node-overlap' && d.nodeIds.includes(f.aliases.nodes.original)));
});

test('cross-Work References capture only a required missing source snapshot without changing the source Work', async t => {
  const f = await fixture(t), draft = await f.service.draftCreate({ projectId: f.project.id, workId: f.firstWork });
  const edited = await f.service.draftApply({ draftId: draft.draftId, expectedVersion: 1, recipe: { version: 1, nodes: [
    { key: 'reference', canvas: f.firstWork, kind: 'reference', target: f.aliases.nodes.unrelated, title: 'Reference', x: 400, y: 0, width: 240, height: 100, sizeMode: 'fixed' },
  ] } });
  await f.service.save(await approved(f, edited));
  assert(f.project.snapshots[f.aliases.nodes.unrelated]);
  const sourceWork = f.initial.nodes.find(node => node.id === f.aliases.nodes.unrelated).canvasId;
  assert.deepEqual(f.project.nodes.filter(node => node.canvasId === sourceWork), f.initial.nodes.filter(node => node.canvasId === sourceWork));
  assert.deepEqual(Object.values(f.project.snapshots).filter(snapshot => snapshot.node.canvasId === sourceWork).map(snapshot => snapshot.node.id), [f.aliases.nodes.unrelated]);
});

test('save requires validation, exact preview, visual review, and fresh rule version', async t => {
  const f = await fixture(t), draft = await f.service.draftCreate({ projectId: f.project.id, workId: f.firstWork });
  await assert.rejects(f.service.save({ draftId: draft.draftId, expectedVersion: 1 }), errorCode('validation-required'));
  const ready = await approved(f, draft);
  await assert.rejects(f.service.save({ ...ready, visualReviewConfirmed: false }), errorCode('visual-review-required'));
  f.changeRules();
  await assert.rejects(f.service.save(ready), errorCode('rules-changed'));
  assert.equal(f.checkpoints, 0);
});

test('recipe application invalidates prior validation and preview', async t => {
  const f = await fixture(t), draft = await f.service.draftCreate({ projectId: f.project.id, workId: f.firstWork });
  const ready = await approved(f, draft);
  await f.service.draftApply({ draftId: draft.draftId, expectedVersion: 1, recipe: update(f.aliases.nodes.original, 'Second') });
  await assert.rejects(f.service.save({ ...ready, expectedVersion: 2 }), errorCode('validation-required'));
  assert.equal(f.checkpoints, 0);
});

test('pending recovery and saved revision conflicts never overwrite user edits', async t => {
  const f = await fixture(t); f.setRecovery(true);
  await assert.rejects(f.service.draftCreate({ projectId: f.project.id, workId: f.firstWork }), errorCode('pending-recovery'));
  f.setRecovery(false);
  const draft = await f.service.draftCreate({ projectId: f.project.id, workId: f.firstWork }), ready = await approved(f, draft);
  f.setRecovery(true);
  await assert.rejects(f.service.save(ready), errorCode('pending-recovery'));
  f.setRecovery(false); f.changeRevision();
  await assert.rejects(f.service.save(ready), errorCode('save-conflict'));
  assert.equal(f.checkpoints, 0);
});

test('preview artifacts use opaque handles and reject path traversal', async t => {
  const f = await fixture(t), draft = await f.service.draftCreate({ projectId: f.project.id, workId: f.firstWork });
  const preview = await f.service.preview({ draftId: draft.draftId, expectedVersion: 1 });
  const image = await f.service.artifact({ artifactId: preview.artifacts.find(a => a.kind === 'image').artifactId });
  assert.equal(image.mime, 'image/png'); assert.deepEqual(image.bytes, png);
  assert(!JSON.stringify(preview).includes(f.directory));
  await assert.rejects(f.service.artifact({ artifactId: '../private-config' }), errorCode('invalid-handle'));
});

test('draft list returns bounded metadata and discard removes only the selected private draft and artifacts', async t => {
  const f = await fixture(t);
  const first = await f.service.draftCreate({ projectId: f.project.id, workId: f.firstWork });
  const second = await f.service.draftCreate({ projectId: f.project.id, title: 'Another Draft' });
  const preview = await f.service.preview({ draftId: first.draftId, expectedVersion: 1 });
  const artifactId = preview.artifacts[0].artifactId;
  const list = await f.service.draftList({ limit: 1 });
  assert.equal(list.total, 2); assert.equal(list.drafts.length, 1); assert.equal(list.nextOffset, 1);
  assert(!JSON.stringify(list).includes('Preserve this'));
  assert(!Object.hasOwn(list.drafts[0], 'project'));
  await assert.rejects(f.service.draftList({ projectId: 'not-shared' }), errorCode('project-not-shared'));
  await assert.rejects(f.service.draftDiscard({ draftId: first.draftId, expectedVersion: 2 }), errorCode('stale-draft'));
  assert.deepEqual(await f.service.draftDiscard({ draftId: first.draftId, expectedVersion: 1 }), { draftId: first.draftId, discarded: true, savedWorksChanged: false });
  assert.equal((await f.service.draftList()).total, 1);
  assert.equal((await f.service.inspect({ draftId: second.draftId })).draft.version, 1);
  await assert.rejects(f.service.artifact({ artifactId }), errorCode('artifact-not-found'));
  assert.deepEqual(f.project, f.initial);
});

test('discarding a saved draft retains the saved Work and checkpoint', async t => {
  const f = await fixture(t), draft = await f.service.draftCreate({ projectId: f.project.id, title: 'Retained Work' });
  const ready = await approved(f, draft);
  await f.service.save(ready);
  const saved = structuredClone(f.project);
  await f.service.draftDiscard({ draftId: draft.draftId, expectedVersion: 1 });
  assert.deepEqual(f.project, saved);
  assert(f.project.canvases.some(work => work.id === draft.workId));
  assert.equal(f.checkpoints, 1);
});

test('a lost save response recovers its exact receipt after restart without repeating the POST', async t => {
  const f = await fixture(t), draft = await f.service.draftCreate({ projectId: f.project.id, title: 'Network Fixture' });
  const ready = await approved(f, draft);
  f.loseNextSaveResponse();
  await assert.rejects(f.service.save(ready), errorCode('save-pending'));
  assert.equal(f.project.revision, 4); assert.equal(f.checkpoints, 1);
  await assert.rejects(f.service.draftApply({ draftId: draft.draftId, expectedVersion: 1, recipe: { version: 1 } }), errorCode('save-pending'));
  await assert.rejects(f.service.draftDiscard({ draftId: draft.draftId, expectedVersion: 1 }), errorCode('save-pending'));
  const restarted = createAuthoringService(f.config, { runWorker: f.worker, readRules: async () => rules });
  const resumed = (await restarted.inspect({ draftId: draft.draftId })).draft;
  assert.equal(resumed.saveStatus, 'pending');
  assert.equal(resumed.validationId, ready.validationId); assert.equal(resumed.previewId, ready.previewId);
  assert(resumed.previewArtifacts.some(artifact => artifact.kind === 'image'));
  const receipt = await restarted.save({ draftId: resumed.draftId, expectedVersion: resumed.version, validationId: resumed.validationId, previewId: resumed.previewId, visualReviewConfirmed: true });
  assert.equal(receipt.recovered, true); assert.equal(receipt.revision, 4); assert.equal(receipt.checkpointId, 'checkpoint-1');
  assert.deepEqual(await restarted.save(ready), receipt);
  assert.equal(f.requests.filter(request => request.method === 'POST').length, 1);
});

test('uncertain saves require the exact checkpoint and current candidate; missing or tampered proof never repeats the write', async t => {
  const f = await fixture(t), draft = await f.service.draftCreate({ projectId: f.project.id, title: 'Proof Fixture' });
  const ready = await approved(f, draft);
  f.loseNextSaveResponse();
  await assert.rejects(f.service.save(ready), errorCode('save-pending'));
  f.hideCheckpoints(true);
  await assert.rejects(f.service.save(ready), errorCode('save-pending'));
  f.hideCheckpoints(false); f.tamperCheckpoint();
  await assert.rejects(f.service.save(ready), errorCode('save-pending'));
  f.changeRevision();
  await assert.rejects(f.service.save(ready), errorCode('save-pending'));
  assert.equal(f.requests.filter(request => request.method === 'POST').length, 1);
  assert.equal(f.checkpoints, 1);
});

test('actual installed shared viewer generates an offline interactive publication and PNG in isolated storage', async t => {
  const f = await fixture(t, { realPreview: true }), draft = await f.service.draftCreate({ projectId: f.project.id, workId: f.firstWork });
  const preview = await f.service.preview({ draftId: draft.draftId, expectedVersion: 1 });
  const image = await f.service.artifact({ artifactId: preview.artifacts.find(a => a.kind === 'image').artifactId });
  const html = await f.service.artifact({ artifactId: preview.artifacts.find(a => a.kind === 'interactive').artifactId });
  assert(image.bytes.length > 5000);
  assert.equal(image.bytes.subarray(1, 4).toString(), 'PNG');
  assert(html.bytes.toString('utf8').includes('meridian-data'));
  assert.equal(preview.browserErrors.length, 0);
  assert(preview.renderedCards >= 1);
});
