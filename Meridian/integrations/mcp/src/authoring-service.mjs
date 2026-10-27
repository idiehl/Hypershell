import { createHash, randomUUID } from 'node:crypto';
import { spawn } from 'node:child_process';
import { mkdir, readFile, writeFile, rename, readdir, rm, unlink } from 'node:fs/promises';
import { isAbsolute, join, resolve, relative } from 'node:path';
import { fileURLToPath } from 'node:url';
import { validateMeridianOrigin } from './meridian-tools.mjs';

const ROOT = fileURLToPath(new URL('../../../', import.meta.url));
const WORKER = fileURLToPath(new URL('./authoring-worker.mjs', import.meta.url));
const UUID = /^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/i;
const ID = /^[a-zA-Z0-9][a-zA-Z0-9_-]{0,95}$/;
const MAX_PROJECT = 80 * 1024 * 1024, MAX_RECIPE = 2 * 1024 * 1024;
const CAPTURE_STAGES = new Set(['build-publication', 'write-publication', 'launch-browser', 'create-page', 'configure-page', 'navigate-preview', 'choose-work', 'focus-node', 'fit-view', 'adjust-scale', 'wait-media', 'screenshot', 'inspect-nodes']);
const PREVIEW_DIAGNOSTIC_CODE = /^(?:ERR_[A-Z0-9_]{1,60}|EXECUTABLE_MISSING|TIMEOUT|BROWSER_CLOSED|SPAWN_EINVAL|BROWSER_ERROR)$/;
const digest = value => createHash('sha256').update(typeof value === 'string' ? value : JSON.stringify(value)).digest('hex');
const same = (a, b) => JSON.stringify(a) === JSON.stringify(b);
let activeWorkers = 0, activeCaptures = 0;
const waitingWorkers = [];
function drainWorkers() {
  for (let i = 0; i < waitingWorkers.length && activeWorkers < 2;) {
    const job = waitingWorkers[i];
    if (job.capture && activeCaptures >= 1) { i++; continue; }
    waitingWorkers.splice(i, 1); activeWorkers++; if (job.capture) activeCaptures++;
    job.resolve(() => { activeWorkers--; if (job.capture) activeCaptures--; drainWorkers(); });
  }
}
export class AuthoringServiceError extends Error {
  constructor(code, message, details = {}) { super(message); this.code = code; this.details = details; }
}
const reject = (code, message, details) => { throw new AuthoringServiceError(code, message, details); };
const handleId = value => { if (typeof value !== 'string' || !UUID.test(value)) reject('invalid-handle', 'Use a draft or artifact ID returned by Meridian.'); return value; };
const entityId = value => { if (typeof value !== 'string' || !ID.test(value)) reject('invalid-id', 'Invalid Meridian identifier.'); return value; };

export async function runAuthoringWorker(payload) {
  const release = await new Promise(resolveSlot => { waitingWorkers.push({ capture: payload.command === 'capture', resolve: resolveSlot }); drainWorkers(); });
  try { return await executeWorkerProcess(payload); } finally { release(); }
}

function executeWorkerProcess(payload) {
  return new Promise((resolveWorker, rejectWorker) => {
    const child = spawn(process.execPath, ['--experimental-strip-types', '--disable-warning=ExperimentalWarning', WORKER], { shell: false, windowsHide: true, cwd: ROOT, stdio: ['pipe', 'pipe', 'pipe'] });
    const chunks = [];
    let size = 0, settled = false;
    const finish = (error, value) => { if (settled) return; settled = true; clearTimeout(timer); if (error) rejectWorker(error); else resolveWorker(value); };
    const timer = setTimeout(() => { child.kill(); finish(new AuthoringServiceError('worker-timeout', 'The authoring operation exceeded its time limit. Divide the edit into smaller batches.')); }, 60_000);
    child.stdout.on('data', chunk => { size += chunk.length; if (size > 128 * 1024 * 1024) { child.kill(); finish(new AuthoringServiceError('worker-limit', 'The authoring result exceeded its size limit.')); } else chunks.push(chunk); });
    child.stderr.resume(); // Never forward worker paths, document contents, or arbitrary exception output.
    child.on('error', () => finish(new AuthoringServiceError('worker-start', 'The configured Node runtime could not start the authoring worker.')));
    child.on('close', () => {
      if (settled) return;
      try {
        const result = JSON.parse(Buffer.concat(chunks).toString('utf8'));
        if (!result.ok) {
          const failure = new AuthoringServiceError(result.error?.code ?? 'authoring-rejected', result.error?.message ?? 'Meridian rejected the composition.', { diagnostics: result.error?.diagnostics ?? [] });
          if (CAPTURE_STAGES.has(result.error?.workerStage)) Object.defineProperty(failure, 'workerStage', { value: result.error.workerStage, configurable: true });
          if (PREVIEW_DIAGNOSTIC_CODE.test(result.error?.previewDiagnosticCode)) Object.defineProperty(failure, 'previewDiagnosticCode', { value: result.error.previewDiagnosticCode, configurable: true });
          return finish(failure);
        }
        finish(null, result.value);
      } catch { finish(new AuthoringServiceError('worker-failed', 'The authoring worker did not return a valid result.')); }
    });
    child.stdin.on('error', () => {});
    child.stdin.end(JSON.stringify(payload));
  });
}

function assertScopedChange(base, project, workId) {
  if (base.id !== project.id || base.revision !== project.revision || base.title !== project.title || base.entryCanvasId !== project.entryCanvasId) reject('scope-violation', 'A Work draft cannot change project identity, revision, title, or entry Work.');
  for (const field of ['canvases', 'nodes', 'links']) {
    const scope = item => field === 'canvases' ? item.id : item.canvasId;
    if (!same(base[field].filter(item => scope(item) !== workId), project[field].filter(item => scope(item) !== workId))) reject('scope-violation', 'This recipe changes another Work. Start a separate draft for that Work.');
  }
  const candidateAssets = new Map(project.assets.map(asset => [asset.id, asset]));
  for (const asset of base.assets) if (!same(asset, candidateAssets.get(asset.id))) reject('shared-asset-edit', 'Existing assets are immutable in a Work draft. Add a new asset instead of replacing shared bytes or metadata.');
  if (!same(base.dependencies, project.dependencies)) reject('scope-violation', 'The recipe changed project dependencies outside its Work scope.');
  for (const [id, snapshot] of Object.entries(base.snapshots)) if (snapshot.node.canvasId !== workId && !same(snapshot, project.snapshots[id])) reject('scope-violation', 'The recipe changed an unrelated saved snapshot.');
}

async function boundedJson(response) {
  let bytes = 0; const chunks = [];
  for await (const chunk of response.body ?? []) { bytes += chunk.length; if (bytes > MAX_PROJECT) reject('backend-limit', 'The project exceeds the companion size limit.'); chunks.push(chunk); }
  return JSON.parse(Buffer.concat(chunks).toString('utf8'));
}

export function createAuthoringService(config, dependencies = {}) {
  const meridianOrigin = validateMeridianOrigin(config.meridianOrigin ?? 'http://127.0.0.1:5240');
  const configuredState = config.stateRoot ?? config.stateDirectory;
  if (!isAbsolute(configuredState ?? '')) reject('configuration', 'Configure an absolute private authoring stateRoot.');
  const state = join(resolve(configuredState), 'authoring');
  const projectIds = new Set(config.projectIds ?? []);
  if (!projectIds.size || [...projectIds].some(id => typeof id !== 'string' || !ID.test(id))) reject('configuration', 'Configure explicitly shared project IDs before enabling authoring.');
  const worker = dependencies.runWorker ?? runAuthoringWorker, fetchImpl = dependencies.fetchImpl ?? fetch;
  const readRules = dependencies.readRules ?? (() => readFile(config.rulesPath ?? join(ROOT, 'docs', 'MERIDIAN-RULES.txt'), 'utf8'));
  const rulesHash = async () => `sha256:${digest(await readRules())}`;
  const queues = new Map();
  const lock = (id, action) => {
    const current = (queues.get(id) ?? Promise.resolve()).catch(() => {}).then(action);
    queues.set(id, current);
    return current.finally(() => { if (queues.get(id) === current) queues.delete(id); });
  };
  const ensure = async () => { await mkdir(join(state, 'drafts'), { recursive: true, mode: 0o700 }); await mkdir(join(state, 'artifacts'), { recursive: true, mode: 0o700 }); };
  const file = (id, folder = 'drafts') => join(state, folder, `${handleId(id)}.json`);
  async function atomic(path, value) {
    const data = JSON.stringify(value);
    if (Buffer.byteLength(data) > 180 * 1024 * 1024) reject('draft-limit', 'The draft exceeds its private storage limit.');
    const temp = `${path}.${randomUUID()}.tmp`;
    await writeFile(temp, data, { encoding: 'utf8', mode: 0o600, flag: 'wx' });
    await rename(temp, path);
  }
  async function load(id) {
    try { return JSON.parse(await readFile(file(id), 'utf8')); }
    catch (error) { if (error instanceof AuthoringServiceError) throw error; reject('draft-not-found', 'The draft is unavailable. Create a new draft or use its exact returned ID.'); }
  }
  function shared(id) { entityId(id); if (!projectIds.has(id)) reject('project-not-shared', 'This project is not shared with this Meridian connection.'); return id; }
  function editable(draft, expectedVersion) {
    shared(draft.projectId);
    if (draft.saved) reject('draft-saved', 'This draft has already been saved. Start a fresh draft to make further changes.');
    if (!Number.isInteger(expectedVersion) || draft.version !== expectedVersion) reject('stale-draft', 'The draft changed. Inspect its current version before applying another operation.', { currentVersion: draft.version });
    if (draft.saveIntent?.status === 'pending') reject('save-pending', 'The previous save has an uncertain result. Retry save to recover its receipt; a second save request will not be sent.');
  }
  const summary = draft => ({ draftId: draft.id, version: draft.version, projectId: draft.projectId, workId: draft.workId, baseRevision: draft.baseRevision,
    title: draft.project.canvases.find(work => work.id === draft.workId)?.title, rulesHash: draft.rulesHash, saved: !!draft.saved, saveStatus: draft.saved ? 'saved' : draft.saveIntent?.status ?? 'draft',
    aliases: Object.fromEntries(Object.entries(draft.aliases).map(([kind, values]) => [kind, Object.fromEntries(Object.entries(values).slice(0, 100))])),
    aliasCounts: Object.fromEntries(Object.entries(draft.aliases).map(([kind, values]) => [kind, Object.keys(values).length])),
    aliasesTruncated: Object.values(draft.aliases).some(values => Object.keys(values).length > 100),
    ...(draft.validation ? { validationId: draft.validation.id, validationPassed: draft.validation.passed } : {}),
    ...(draft.preview ? { previewId: draft.preview.id, previewArtifacts: draft.preview.artifacts } : {}),
    ...(draft.receipt ? { receipt: draft.receipt } : {}) });
  const metadata = draft => ({ draftId: draft.id, version: draft.version, projectId: draft.projectId, workId: draft.workId, baseRevision: draft.baseRevision,
    title: draft.project.canvases.find(work => work.id === draft.workId)?.title, saved: !!draft.saved, saveStatus: draft.saved ? 'saved' : draft.saveIntent?.status ?? 'draft', createdAt: draft.createdAt });
  async function persist(draft) {
    await atomic(file(draft.id), draft);
    await atomic(join(state, 'drafts', `${draft.id}.meta.json`), metadata(draft));
  }
  async function request(path, method = 'GET', body) {
    let response;
    try { response = await fetchImpl(`${meridianOrigin}${path}`, { method, redirect: 'error', signal: AbortSignal.timeout(30_000), headers: { accept: 'application/json', ...(body ? { 'content-type': 'application/json' } : {}) }, ...(body ? { body: JSON.stringify(body) } : {}) }); }
    catch { reject('backend-unavailable', 'Meridian is not reachable. Open the local application and retry.'); }
    if (!response.ok) {
      if (response.status === 404) reject('not-found', 'The requested Meridian resource is unavailable.', { httpStatus: response.status });
      if (response.status === 409) reject('save-conflict', 'Meridian changed or has a pending recovery draft. No overwrite was attempted; resolve the draft and create a fresh authoring draft.', { httpStatus: response.status });
      reject('backend-rejected', `Meridian rejected this operation (${response.status}).`, { httpStatus: response.status });
    }
    return boundedJson(response);
  }
  async function recoveryAbsent(projectId) {
    const response = await fetchImpl(`${meridianOrigin}/api/projects/${entityId(projectId)}/recovery`, { method: 'GET', redirect: 'error', signal: AbortSignal.timeout(5000) });
    if (response.status === 404) return;
    if (response.ok) reject('pending-recovery', 'This project has an unsaved recovery draft. Save or resolve it in Meridian, then start a fresh authoring draft. Your existing draft has not been overwritten.');
    reject('recovery-unavailable', 'The recovery state could not be checked. No save was attempted.');
  }
  async function matchingCheckpoint(draft) {
    const intent = draft.saveIntent;
    const entries = await request(`/api/projects/${draft.projectId}/checkpoints`);
    if (!Array.isArray(entries)) return null;
    const matches = entries.filter(entry => entry?.name === `${intent.checkpointName} · saved` && entry.revision === intent.baseRevision
      && typeof entry.id === 'string' && ID.test(entry.id));
    if (matches.length !== 1) return null;
    // The real host returns metadata in the listing, and the bare project at
    // /checkpoints/{id}; it does not return the stored on-disk wrapper.
    const checkpoint = await request(`/api/projects/${draft.projectId}/checkpoints/${matches[0].id}`);
    if (checkpoint?.id !== draft.projectId || checkpoint.revision !== intent.baseRevision || digest(checkpoint) !== intent.baselineHash) return null;
    return matches[0];
  }
  async function completeSave(draft, saved, checkpoint, recovered) {
    draft.saved = true; draft.saveIntent.status = 'completed';
    draft.receipt = { projectId: saved.id, workId: draft.workId, revision: saved.revision, checkpointed: true, checkpointId: checkpoint.id,
      checkpointName: checkpoint.name, ...(recovered ? { recovered: true } : {}),
      url: `${meridianOrigin}/?project=${encodeURIComponent(saved.id)}&canvas=${encodeURIComponent(draft.workId)}` };
    await persist(draft);
    return draft.receipt;
  }

  const service = {
    async draftList({ projectId, offset = 0, limit = 25 } = {}) {
      if (projectId !== undefined) shared(projectId);
      if (!Number.isInteger(offset) || offset < 0 || offset > 100000 || !Number.isInteger(limit) || limit < 1 || limit > 50) reject('invalid-page', 'Use an offset from 0 to 100000 and a limit from 1 to 50.');
      await ensure();
      const names = (await readdir(join(state, 'drafts'))).filter(name => /^[a-f0-9-]{36}\.meta\.json$/i.test(name));
      const entries = [];
      for (const name of names) {
        try {
          const item = JSON.parse(await readFile(join(state, 'drafts', name), 'utf8'));
          if (projectIds.has(item.projectId) && (!projectId || item.projectId === projectId)) entries.push(item);
        } catch { /* An interrupted metadata write is not an invitation to load arbitrary files. */ }
      }
      entries.sort((a, b) => String(b.createdAt).localeCompare(String(a.createdAt)) || a.draftId.localeCompare(b.draftId));
      return { drafts: entries.slice(offset, offset + limit), nextOffset: offset + limit < entries.length ? offset + limit : null, total: entries.length };
    },
    async draftDiscard({ draftId, expectedVersion }) {
      return lock(handleId(draftId), async () => {
        const draft = await load(draftId); shared(draft.projectId);
        if (draft.version !== expectedVersion) reject('stale-draft', 'The private draft changed. Inspect its current version before discarding it.', { currentVersion: draft.version });
        if (draft.saveIntent?.status === 'pending') reject('save-pending', 'Verify the pending save result before discarding its private receipt intent. Saved Works have not been removed.');
        for (const entry of await readdir(join(state, 'artifacts'), { withFileTypes: true })) {
          if (entry.isDirectory() && entry.name.startsWith(`${draft.id}-`) && UUID.test(entry.name.slice(draft.id.length + 1))) {
            const target = resolve(state, 'artifacts', entry.name), part = relative(resolve(state, 'artifacts'), target);
            if (!part || part.startsWith('..') || isAbsolute(part)) reject('invalid-artifact', 'Refused to discard an artifact outside private storage.');
            await rm(target, { recursive: true, force: true });
          } else if (entry.isFile() && UUID.test(entry.name.replace(/\.json$/, ''))) {
            try {
              const path = join(state, 'artifacts', entry.name), artifact = JSON.parse(await readFile(path, 'utf8'));
              if (artifact.draftId === draft.id) await unlink(path);
            } catch { /* Keep any unknown artifact rather than risking deletion outside this draft. */ }
          }
        }
        await unlink(file(draft.id));
        await unlink(join(state, 'drafts', `${draft.id}.meta.json`)).catch(() => {});
        return { draftId, discarded: true, savedWorksChanged: false };
      });
    },
    async schema() {
      const schema = await worker({ command: 'schema' });
      return { ...schema, rulesHash: await rulesHash(), templates: {
        conciseBranch: { version: 1, nodes: [
          { key: 'topic', canvas: 'WORK_ID', kind: 'media', title: 'Topic', x: 0, y: 100, width: 260, height: 90, sizeMode: 'fixed', childLayout: 'spatial', childArrangement: 'free', collapsed: false },
          { key: 'branch', canvas: 'WORK_ID', parent: 'topic', kind: 'media', title: 'Branch', x: 420, y: 100, width: 260, height: 90, sizeMode: 'fixed', childLayout: 'spatial', childPosition: 'manual' },
        ], links: [{ key: 'relationship', canvas: 'WORK_ID', source: 'topic', target: 'branch', parent: 'topic', kind: 'visual', relation: 'generic', direction: 'none' }] },
        identityUpdate: { version: 1, nodes: [{ key: 'original', existingId: 'NODE_ID', title: 'Object', style: { fontFamily: 'sans', contentSizing: 'fixed', titleSize: 20 } }] },
      } };
    },
    async inspect(input) {
      const offset = input.offset ?? 0, limit = input.limit ?? 25;
      if (!Number.isInteger(offset) || offset < 0 || offset > 100000 || !Number.isInteger(limit) || limit < 1 || limit > 50) reject('invalid-page', 'Use an offset from 0 to 100000 and a limit from 1 to 50.');
      if (input.entity && !['nodes', 'links', 'assets', 'works'].includes(input.entity)) reject('invalid-entity', 'Choose nodes, links, assets, or works.');
      if (input.query !== undefined && (typeof input.query !== 'string' || input.query.length > 100)) reject('invalid-query', 'Search text must contain at most 100 characters.');
      let project, draft;
      if (input.draftId) { draft = await load(input.draftId); shared(draft.projectId); project = draft.project; }
      else { shared(input.projectId); project = await request(`/api/projects/${input.projectId}`); }
      const workId = input.workId ?? draft?.workId;
      if (workId && !project.canvases.some(work => work.id === workId)) reject('work-not-found', 'The Work is absent from this project.');
      const result = await worker({ command: 'inspect', project, workId, entity: input.entity, nodeId: input.nodeId, query: input.query, offset, limit });
      return { ...result, ...(draft ? { draft: summary(draft) } : {}) };
    },
    async draftCreate({ projectId, workId, title }) {
      shared(projectId); await ensure();
      if (title !== undefined && (typeof title !== 'string' || !title.trim() || title.length > 200)) reject('invalid-title', 'Use a Work title of 1 to 200 characters.');
      if ((await readdir(join(state, 'drafts'))).filter(name => UUID.test(name.replace(/\.json$/, ''))).length >= 64) reject('draft-capacity', 'The private draft store is full. Archive completed drafts locally before creating more.');
      await recoveryAbsent(projectId);
      const base = await request(`/api/projects/${projectId}`);
      let project = base, aliases = {};
      if (workId) { entityId(workId); if (!base.canvases.some(work => work.id === workId)) reject('work-not-found', 'This Work is not present in the shared project.'); }
      else {
        if (!title) reject('missing-title', 'Provide a title when creating a new Work.');
        const result = await worker({ command: 'apply', project: base, recipe: { version: 1, canvases: [{ key: 'work', title }] } });
        project = result.project; aliases = result.aliases; workId = aliases.canvases.work;
      }
      const draft = { id: randomUUID(), version: 1, projectId, workId, baseRevision: base.revision, rulesHash: await rulesHash(), createdAt: new Date().toISOString(), baseProject: base, project, aliases };
      await persist(draft);
      return summary(draft);
    },
    async draftApply({ draftId, expectedVersion, recipe }) {
      return lock(handleId(draftId), async () => {
        const draft = await load(draftId); editable(draft, expectedVersion);
        if (!recipe || typeof recipe !== 'object' || Array.isArray(recipe) || Buffer.byteLength(JSON.stringify(recipe)) > MAX_RECIPE) reject('recipe-limit', 'Supply a declarative version-one recipe of at most 2 MiB.');
        if (Object.hasOwn(recipe, 'title') || Object.hasOwn(recipe, 'entryCanvas')) reject('scope-violation', 'A Work recipe cannot rename the whole project or change its entry Work.');
        if (Array.isArray(recipe.assets) && recipe.assets.some(asset => /^(?:text\/(?:html|javascript|css)|application\/(?:xhtml\+xml|javascript|x-javascript)|image\/svg\+xml)(?:;|$)/i.test(asset?.mime ?? ''))) reject('active-asset', 'Executable HTML, JavaScript, CSS, and SVG assets are not accepted through the authoring connector. Use passive images or document data.');
        const merged = { ...recipe, bindings: { ...draft.aliases, ...recipe.bindings } };
        for (const kind of ['canvases', 'roots', 'nodes', 'assets', 'links']) merged.bindings[kind] = { ...(draft.aliases[kind] ?? {}), ...(recipe.bindings?.[kind] ?? {}) };
        const result = await worker({ command: 'apply', project: draft.project, recipe: merged });
        assertScopedChange(draft.project, result.project, draft.workId);
        draft.project = result.project; draft.aliases = result.aliases; draft.version++;
        delete draft.validation; delete draft.preview; delete draft.saveIntent;
        await persist(draft);
        return { ...summary(draft), inspection: result.report.summary, warnings: result.report.warnings.slice(0, 40) };
      });
    },
    async validate({ draftId, expectedVersion }) {
      return lock(handleId(draftId), async () => {
        const draft = await load(draftId); editable(draft, expectedVersion);
        const report = await worker({ command: 'validate', project: draft.project, workId: draft.workId });
        const baseline = draft.baseProject.canvases.some(work => work.id === draft.workId) ? await worker({ command: 'validate', project: draft.baseProject, workId: draft.workId }) : { diagnostics: [] };
        const signature = issue => JSON.stringify([issue.code, issue.nodeId, [...(issue.nodeIds ?? [])].sort(), issue.linkId]);
        const existing = new Set(baseline.diagnostics.map(signature));
        const unchanged = id => same(draft.baseProject.nodes.find(node => node.id === id), draft.project.nodes.find(node => node.id === id));
        const diagnostics = report.diagnostics.map(issue => existing.has(signature(issue)) && [issue.nodeId, ...(issue.nodeIds ?? [])].filter(Boolean).every(unchanged)
          && (!issue.linkId || same(draft.baseProject.links.find(link => link.id === issue.linkId), draft.project.links.find(link => link.id === issue.linkId)))
          ? { ...issue, severity: 'warning', preExisting: true } : issue);
        const errors = diagnostics.filter(issue => issue.severity === 'error');
        const validation = { id: randomUUID(), version: draft.version, projectHash: digest(draft.project), rulesHash: await rulesHash(), passed: report.validProject && !errors.length && !report.diagnosticLimitReached,
          report: { ...report, diagnostics, errorCount: errors.length, warningCount: diagnostics.length - errors.length }, createdAt: new Date().toISOString() };
        draft.validation = validation; draft.rulesHash = validation.rulesHash;
        await persist(draft);
        return { draftId, version: draft.version, validationId: validation.id, rulesHash: validation.rulesHash, passed: validation.passed, ...validation.report };
      });
    },
    async preview(input) {
      return lock(handleId(input.draftId), async () => {
        let previewStage = 'load-draft';
        try {
        const draft = await load(input.draftId); editable(draft, input.expectedVersion);
        const workId = input.workId ?? draft.workId;
        if (workId !== draft.workId) reject('scope-violation', 'Preview the Work owned by this draft.');
        if (input.nodeId && !draft.project.nodes.some(node => node.id === input.nodeId && node.canvasId === workId)) reject('node-not-found', 'The focus node is absent from this Work.');
        if (input.scope && !['node', 'branch'].includes(input.scope)) reject('invalid-scope', 'Preview scope must be node or branch.');
        if (input.scale !== undefined && (!Number.isFinite(input.scale) || input.scale < 1e-10 || input.scale > 10)) reject('invalid-scale', 'Preview scale must be finite and between 1e-10 and 10.');
        const previewId = randomUUID(), directory = join(state, 'artifacts', `${draft.id}-${previewId}`);
        previewStage = 'create-artifact-dir';
        await mkdir(directory, { mode: 0o700 });
        previewStage = 'capture-worker';
        const review = await worker({ command: 'capture', project: draft.project, workId, nodeId: input.nodeId, scope: input.scope ?? 'node', scale: input.scale,
          viewerAssetsRoot: config.viewerAssetsRoot ?? join(ROOT, 'output', 'app', 'wwwroot'),
          ...(config.browserExecutablePath ? { browserExecutablePath: config.browserExecutablePath } : {}), directory });
        previewStage = 'verify-capture';
        if (review.browserErrors?.length || review.canvases?.length !== 1) reject('preview-failed', 'The actual Meridian viewer reported errors. Inspect the diagnostics and revise the draft.');
        const screenshot = review.canvases[0].screenshot;
        if (typeof screenshot !== 'string' || !/^[A-Za-z0-9._-]+\.png$/.test(screenshot)) reject('preview-failed', 'The preview returned an invalid screenshot handle.');
        previewStage = 'artifact-records';
        const artifacts = [];
        for (const [filename, mime, kind] of [[screenshot, 'image/png', 'image'], ['preview.html', 'text/html; charset=utf-8', 'interactive']]) {
          const artifactId = randomUUID();
          await atomic(file(artifactId, 'artifacts'), { id: artifactId, draftId: draft.id, version: draft.version, relativePath: relative(state, join(directory, filename)), mime, filename, kind });
          artifacts.push({ artifactId, mime, kind, filename });
        }
        previewStage = 'persist-draft';
        draft.preview = { id: previewId, version: draft.version, projectHash: digest(draft.project), createdAt: new Date().toISOString(), artifacts };
        await persist(draft);
        const cards = review.canvases[0].nodes ?? [];
        return { draftId: draft.id, version: draft.version, previewId, artifacts, browserErrors: [], renderedCards: cards.length,
          clippedNodes: cards.filter(node => node.titleClipped || node.clippedContent?.length).map(node => node.id).slice(0, 100),
          scrollingNodes: cards.filter(node => node.bodyRequiresScrolling).map(node => node.id).slice(0, 100),
          reviewRequired: 'Inspect the returned actual viewer image. Before save, explicitly confirm visual review after inspecting both overview and readable detail.' };
        } catch (error) {
          // Internal fixed-stage metadata lets the private stdio audit distinguish
          // host filesystem, worker launch and image-record failures. It contains
          // no arguments, content, paths, messages or captured worker output.
          if (error && typeof error === 'object' && Object.isExtensible(error)) {
            Object.defineProperty(error, 'previewStage', { value: CAPTURE_STAGES.has(error.workerStage) ? error.workerStage : previewStage, enumerable: false, configurable: true });
          }
          throw error;
        }
      });
    },
    async artifact({ artifactId }) {
      handleId(artifactId);
      let record; try { record = JSON.parse(await readFile(file(artifactId, 'artifacts'), 'utf8')); } catch { reject('artifact-not-found', 'The preview artifact is unavailable.'); }
      const draft = await load(record.draftId); shared(draft.projectId);
      const path = resolve(state, record.relativePath), part = relative(state, path);
      if (!part || part.startsWith('..') || isAbsolute(part)) reject('invalid-artifact', 'The artifact is outside private authoring storage.');
      const bytes = await readFile(path);
      if (bytes.length > 96 * 1024 * 1024) reject('artifact-limit', 'The artifact is too large to deliver.');
      return { artifactId, mime: record.mime, filename: record.filename, bytes };
    },
    async save({ draftId, expectedVersion, validationId, previewId, visualReviewConfirmed, checkpointName }) {
      return lock(handleId(draftId), async () => {
        const draft = await load(draftId); shared(draft.projectId);
        if (!Number.isInteger(expectedVersion) || draft.version !== expectedVersion) reject('stale-draft', 'The draft changed. Inspect its current version before saving.', { currentVersion: draft.version });
        if (draft.saved) return draft.receipt;
        if (draft.saveIntent?.status === 'pending') {
          // A lost HTTP response must never become a second POST. Require exact
          // candidate/revision AND a unique, content-matching baseline checkpoint.
          const current = await request(`/api/projects/${draft.projectId}`);
          if (current.revision === draft.saveIntent.expectedRevision && digest(current) === draft.saveIntent.candidateHash) {
            const checkpoint = await matchingCheckpoint(draft);
            if (checkpoint) return completeSave(draft, current, checkpoint, true);
          }
          reject('save-pending', 'The previous save result cannot yet be verified. No second save request was sent. Inspect Meridian and its checkpoints before starting a fresh draft.',
            { currentRevision: current.revision, expectedSavedRevision: draft.saveIntent.expectedRevision });
        }
        if (draft.saveIntent?.status === 'rejected') reject(draft.saveIntent.failureCode ?? 'backend-rejected', 'The previous save was rejected. Revise the draft or start a fresh baseline before attempting another save.');
        const hash = digest(draft.project), validation = draft.validation, preview = draft.preview;
        if (!validation || validation.id !== validationId || validation.version !== draft.version || validation.projectHash !== hash || !validation.passed) reject('validation-required', 'Run successful validation against this exact draft version before saving.');
        if (validation.rulesHash !== await rulesHash()) reject('rules-changed', 'The authoring rules changed after validation. Read the current rules and validate again.');
        if (!preview || preview.id !== previewId || preview.version !== draft.version || preview.projectHash !== hash) reject('preview-required', 'Render and inspect this exact draft version before saving.');
        if (visualReviewConfirmed !== true) reject('visual-review-required', 'Confirm visual review of the actual viewer preview before saving.');
        if (checkpointName !== undefined && (typeof checkpointName !== 'string' || !checkpointName.trim() || checkpointName.length > 80)) reject('invalid-checkpoint', 'Use a checkpoint name of 1 to 80 characters.');
        const health = await request('/__meridian_status');
        if (!['authoring-save-guard-v1', 'authoring-recovery-guard-v1'].every(cap => health.capabilities?.includes(cap))) reject('host-upgrade-required', 'This Meridian host does not advertise revision and recovery guarded authoring saves.');
        await recoveryAbsent(draft.projectId);
        const current = await request(`/api/projects/${draft.projectId}`);
        if (current.revision !== draft.baseRevision || digest(current) !== digest(draft.baseProject)) reject('save-conflict', 'The saved project changed after this draft started. Create a fresh draft and reapply the intended changes.');
        assertScopedChange(draft.baseProject, draft.project, draft.workId);
        const candidate = await worker({ command: 'commit', project: draft.project, workId: draft.workId });
        const intentId = randomUUID();
        draft.saveIntent = { id: intentId, status: 'pending', baseRevision: draft.baseRevision, expectedRevision: draft.baseRevision + 1,
          baselineHash: digest(draft.baseProject), candidateHash: digest({ ...candidate, revision: draft.baseRevision + 1 }),
          checkpointName: `${(checkpointName ?? `Before ChatGPT edit · r${draft.baseRevision}`).slice(0, 60)} · ${intentId}`, createdAt: new Date().toISOString() };
        await persist(draft);
        let saved;
        try {
          saved = await request(`/api/projects/${draft.projectId}/authoring`, 'POST', { project: candidate, expectedRevision: draft.baseRevision, expectedRecoverySavedAt: null,
            checkpointName: draft.saveIntent.checkpointName });
        } catch (error) {
          if ([400, 401, 403, 404, 409, 413, 415, 422].includes(error.details?.httpStatus)) {
            draft.saveIntent.status = 'rejected'; draft.saveIntent.failureCode = error.code; await persist(draft); throw error;
          }
          reject('save-pending', 'The save response was interrupted or uncertain. Retry save to verify the stored result; no duplicate save request will be sent.');
        }
        if (saved.id !== draft.projectId || saved.revision !== draft.saveIntent.expectedRevision || digest(saved) !== draft.saveIntent.candidateHash) reject('save-pending', 'Meridian returned an unexpected save result. No duplicate save request will be sent; inspect the stored Work.');
        const checkpoint = await matchingCheckpoint(draft);
        if (!checkpoint) reject('save-pending', 'The Work response matched, but its exact baseline checkpoint could not be verified. Retry save to verify the receipt; no second save request will be sent.');
        return completeSave(draft, saved, checkpoint, false);
      });
    },
  };
  return service;
}
