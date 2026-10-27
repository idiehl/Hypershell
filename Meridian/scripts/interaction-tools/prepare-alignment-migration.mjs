/** Read-only host capture and offline Work-default candidates. No mutation endpoint exists here. */
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { validateProject } from '../../src/core/rules.ts';
import { validateLibraryCanvasDocument } from '../../src/core/library.ts';

const ROOT = fileURLToPath(new URL('../../', import.meta.url));
const hash = value => createHash('sha256').update(JSON.stringify(value)).digest('hex');
const own = (value, key) => Object.hasOwn(value, key);
export function makeAlignmentCandidate(project) {
  const candidate = structuredClone(project);
  for (const work of candidate.canvases) work.snapping = { ...work.snapping, smartAlign: true };
  return candidate;
}

export function auditAlignmentCandidate(baseline, candidate) {
  const { canvases: originalWorks, ...original } = baseline;
  const { canvases: newWorks, ...current } = candidate;
  assert.deepEqual(current, original, 'Only Work-level smartAlign may change; all nodes, snapshots, assets, links, dependencies, metadata and revision must remain exact.');
  assert.equal(newWorks.length, originalWorks.length);
  const changedWorks = [];
  for (const [at, work] of originalWorks.entries()) {
    const { snapping: originalSettings, ...originalWork } = work;
    const { snapping: settings, ...newWork } = newWorks[at];
    assert.deepEqual(newWork, originalWork, 'Work identity, order, title and appearance must remain exact.');
    assert.equal(settings?.smartAlign, true, 'Every saved Work must enable alignment.');
    const { smartAlign: ignoredOriginal, ...otherOriginalSettings } = originalSettings ?? {};
    const { smartAlign: ignoredNew, ...otherSettings } = settings ?? {};
    assert.deepEqual(otherSettings, otherOriginalSettings, 'Sticky edge and grid settings must remain exact.');
    if (originalSettings?.smartAlign !== true) changedWorks.push({ id: work.id, title: work.title,
      previous: own(originalSettings ?? {}, 'smartAlign') ? originalSettings.smartAlign : 'inherited', next: true });
  }
  assert.deepEqual(validateProject(candidate), []);
  return { passed: true, projectId: baseline.id, revision: baseline.revision, workCount: originalWorks.length, changedWorks,
    retainedNodes: baseline.nodes.length, retainedLinks: baseline.links.length, retainedAssets: baseline.assets.length,
    retainedSnapshots: Object.keys(baseline.snapshots).length,
    preservedNodeFreeMovementOverrides: baseline.nodes.filter(node => node.snapping?.smartAlign === false).map(node => ({ id: node.id, title: node.tag || node.title })),
    allIndividualNodeSettingsExact: true, allOtherProjectDataExact: true };
}

const get = async (origin, pathname, missingIsNull = false) => {
  const response = await fetch(`${origin}${pathname}`, { method: 'GET', cache: 'no-store', signal: AbortSignal.timeout(45000) });
  if (missingIsNull && response.status === 404) return null;
  assert.equal(response.status, 200, `GET ${pathname} failed: ${response.status}`);
  return response.json();
};
const persist = (directory, filename, value) => writeFile(path.join(directory, filename), JSON.stringify(value, null, 2) + '\n', { flag: 'wx' });

export async function prepareAlignmentMigration(origin = 'http://127.0.0.1:5240', directory = path.join(ROOT, 'output/interaction-tools-20261021/alignment-migration')) {
  const parsed = new URL(origin);
  assert.ok(parsed.protocol === 'http:' && ['127.0.0.1', 'localhost'].includes(parsed.hostname) && parsed.pathname === '/', 'Only a local Meridian HTTP origin is supported.');
  origin = parsed.origin;
  await mkdir(directory, { recursive: true });
  // Never replace a captured baseline, including after a partial prior capture.
  try { await readFile(path.join(directory, 'capture-started.json')); throw new Error('This capture directory is already in use. Choose a fresh --out directory.'); }
  catch (error) { if (error.code !== 'ENOENT') throw error; }
  const startedAt = new Date().toISOString();
  await persist(directory, 'capture-started.json', { origin, startedAt, getOnly: true });
  const [status, catalog] = await Promise.all([get(origin, '/__meridian_status'), get(origin, '/api/library/catalog')]);
  assert.equal(status.application, 'atlas-meridian'); assert.ok(Array.isArray(catalog));
  await persist(directory, 'catalog-baseline.json', catalog); await persist(directory, 'host-status.json', status);
  const projects = [];
  for (const item of catalog) {
    assert.match(item.id, /^[A-Za-z0-9][A-Za-z0-9_-]{0,95}$/);
    const projectDirectory = path.join(directory, item.id); await mkdir(projectDirectory, { recursive: true });
    const baseline = await get(origin, `/api/projects/${item.id}`);
    const recovery = await get(origin, `/api/projects/${item.id}/recovery`, true);
    const confirmed = await get(origin, `/api/projects/${item.id}`);
    assert.equal(baseline.id, item.id);
    const candidate = makeAlignmentCandidate(baseline), audit = auditAlignmentCandidate(baseline, candidate);
    const stableRead = hash(baseline) === hash(confirmed), hasRecovery = recovery !== null;
    await persist(projectDirectory, 'baseline.json', baseline); await persist(projectDirectory, 'recovery.json', recovery);
    await persist(projectDirectory, 'candidate.json', candidate); await persist(projectDirectory, 'audit.json', audit);
    projects.push({ id: item.id, title: baseline.title, expectedRevision: baseline.revision,
      baselineSHA256: hash(baseline), candidateSHA256: hash(candidate), stableRead, hasRecovery,
      expectedRecoverySavedAt: recovery?.savedAt ?? null, eligibleAtCapture: stableRead && !hasRecovery,
      changedWorks: audit.changedWorks.length, workCount: audit.workCount,
      preservedNodeFreeMovementOverrides: audit.preservedNodeFreeMovementOverrides.length,
      directory: projectDirectory, changeRequired: audit.changedWorks.length > 0 });
  }
  const libraryDirectory = path.join(directory, 'library'); await mkdir(libraryDirectory, { recursive: true });
  const libraryBaseline = await get(origin, '/api/library');
  const libraryRecovery = await get(origin, '/api/library/recovery', true);
  const confirmedLibrary = await get(origin, '/api/library');
  validateLibraryCanvasDocument(libraryBaseline);
  const libraryCandidate = { ...structuredClone(libraryBaseline), project: makeAlignmentCandidate(libraryBaseline.project) };
  const libraryAudit = auditAlignmentCandidate(libraryBaseline.project, libraryCandidate.project);
  const { project: ignoredBaselineProject, ...originalEnvelope } = libraryBaseline;
  const { project: ignoredCandidateProject, ...candidateEnvelope } = libraryCandidate;
  assert.deepEqual(candidateEnvelope, originalEnvelope, 'External destinations, discovery history, revision and Library envelope must remain exact.');
  validateLibraryCanvasDocument(libraryCandidate);
  await persist(libraryDirectory, 'baseline.json', libraryBaseline); await persist(libraryDirectory, 'recovery.json', libraryRecovery);
  await persist(libraryDirectory, 'candidate.json', libraryCandidate); await persist(libraryDirectory, 'audit.json', libraryAudit);
  const library = { expectedRevision: libraryBaseline.revision, baselineSHA256: hash(libraryBaseline), candidateSHA256: hash(libraryCandidate),
    stableRead: hash(libraryBaseline) === hash(confirmedLibrary), hasRecovery: libraryRecovery !== null,
    expectedRecoverySavedAt: libraryRecovery?.savedAt ?? null, changedWorks: libraryAudit.changedWorks.length, workCount: libraryAudit.workCount,
    preservedNodeFreeMovementOverrides: libraryAudit.preservedNodeFreeMovementOverrides.length, directory: libraryDirectory,
    changeRequired: libraryAudit.changedWorks.length > 0 };
  library.eligibleAtCapture = library.stableRead && !library.hasRecovery;
  const finalCatalog = await get(origin, '/api/library/catalog');
  await persist(directory, 'catalog-confirmed.json', finalCatalog);
  const catalogIdsStable = JSON.stringify(catalog.map(item => item.id).sort()) === JSON.stringify(finalCatalog.map(item => item.id).sort());
  const blocked = projects.filter(project => !project.eligibleAtCapture).map(project => ({ id: project.id, title: project.title, reason: project.hasRecovery ? 'A recovery draft exists and must be preserved.' : 'Saved data changed during capture.' }));
  if (!library.eligibleAtCapture) blocked.push({ id: 'library', reason: library.hasRecovery ? 'A Library recovery draft exists and must be preserved.' : 'Library changed during capture.' });
  if (!catalogIdsStable) blocked.push({ id: 'catalog', reason: 'Saved project membership changed during capture; refresh the inventory.' });
  const result = { getOnly: true, liveWrites: false, startedAt, capturedAt: new Date().toISOString(), origin, directory,
    scope: 'Enable Work-level smartAlign on every currently saved project and Library Work, retaining all individual node settings and every other field.',
    projects, library, catalogIdsStable, eligibleAtCapture: blocked.length === 0, blocked,
    totals: { projects: projects.length, projectWorks: projects.reduce((sum, project) => sum + project.workCount, 0),
      libraryWorks: library.workCount, changedProjectWorks: projects.reduce((sum, project) => sum + project.changedWorks, 0), changedLibraryWorks: library.changedWorks,
      preservedNodeFreeMovementOverrides: projects.reduce((sum, project) => sum + project.preservedNodeFreeMovementOverrides, 0) + library.preservedNodeFreeMovementOverrides },
    applyRequirement: 'Before any write, recapture saved revisions/hashes and recovery state, retain exact named checkpoints, use expectedRevision and recovery guards for projects, and recheck Library recovery immediately before its revision-guarded PUT. This helper provides no apply endpoint.' };
  await persist(directory, 'manifest.json', result);
  return result;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const args = new Map(); for (let at = 2; at < process.argv.length; at += 2) args.set(process.argv[at].replace(/^--/, ''), process.argv[at + 1]);
  const result = await prepareAlignmentMigration(args.get('origin'), args.has('out') ? path.resolve(args.get('out')) : undefined);
  console.log(JSON.stringify({ directory: result.directory, eligibleAtCapture: result.eligibleAtCapture, totals: result.totals, blocked: result.blocked,
    projects: result.projects.map(({ title, expectedRevision, changedWorks, preservedNodeFreeMovementOverrides, hasRecovery }) => ({ title, expectedRevision, changedWorks, preservedNodeFreeMovementOverrides, hasRecovery })), library: result.library }));
}
