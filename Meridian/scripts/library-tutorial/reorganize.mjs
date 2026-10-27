#!/usr/bin/env node
/** Local-only Library composition. Network commands use GET exclusively.
 * The saved Library, discovery tombstones, and attributed source content remain
 * separate from this review candidate. No command installs or saves it live. */
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { applyRecipe, inspectProject, inventoryProject } from '../../src/core/authoring.ts';
import { libraryDocumentFromProject, validateLibraryCanvasDocument } from '../../src/core/library.ts';
import { navigationIndex } from '../../src/core/navigation.ts';
import { validateProject } from '../../src/core/rules.ts';
import { buildPublication } from '../../src/publication.ts';

const ROOT = fileURLToPath(new URL('../../', import.meta.url));
export const DEFAULT_DIRECTORY = path.join(ROOT, 'output/library-tutorial-20261021');
export const IDS = {
  mythology: 'a3d00829-1577-4dec-8591-24495b6c885d',
  header: 'c9f781c1-124a-433e-a1d6-37dd1d8a7583',
  cpp: '2a9aa31d-b13c-4d0b-9192-b1a94b7c0fce',
  csharp: '47f9024f-2a26-4a9a-91b9-83927029fbf3',
  living: '3d896c72-36b9-4900-adb6-a68934591233',
  visual: 'e35cba82-f644-416a-ac14-12645893ed88',
  gods: '3c8176d0-884a-4ac9-9a99-a0d5548de629',
};
const CATEGORIES = [
  { key: 'coding', title: 'Coding', x: 80, projects: [IDS.cpp, IDS.csharp] },
  { key: 'mythology', title: 'Mythology', x: 680, projects: [IDS.living, IDS.visual, IDS.gods] },
  { key: 'tutorial', title: 'Tutorial', x: 1280, projects: [] },
];
const WIDTH = 560, TOP = 225, TILE_WIDTH = 512, TILE_HEIGHT = 154, PITCH = 174, INSET = 24, TILE_TOP = 80;
const targetKey = target => `${target.projectId}:${target.canvasId ?? '*'}`;
export const hash = value => createHash('sha256').update(JSON.stringify(value)).digest('hex');
const readJSON = async file => JSON.parse((await readFile(file, 'utf8')).replace(/^\uFEFF/, ''));
const writeJSON = async (file, value, exclusive = false) => {
  await mkdir(path.dirname(file), { recursive: true });
  await writeFile(file, JSON.stringify(value, null, 2) + '\n', exclusive ? { flag: 'wx' } : undefined);
};
async function optionalJSON(file) { try { return await readJSON(file); } catch (error) { if (error.code !== 'ENOENT') throw error; } }
function origin(value) {
  const url = new URL(value ?? 'http://127.0.0.1:5240');
  assert.ok(url.protocol === 'http:' && ['127.0.0.1', 'localhost', '[::1]'].includes(url.hostname) && !url.username && !url.password && url.pathname === '/' && !url.search && !url.hash, 'Host must be a local Meridian HTTP origin.');
  return url;
}
async function get(host, endpoint, allow404 = false) {
  const response = await fetch(new URL(endpoint, host), { method: 'GET', signal: AbortSignal.timeout(15000) });
  if (allow404 && response.status === 404) return null;
  if (!response.ok) throw new Error(`GET ${endpoint} returned ${response.status}; no live change was attempted.`);
  return response.json();
}

/** Checkpoint the observed saved document locally, bracketed by identical reads. */
export async function captureBaseline(directory = DEFAULT_DIRECTORY, hostValue) {
  const host = origin(hostValue), first = await get(host, '/api/library');
  const [catalog, recovery, status] = await Promise.all([
    get(host, '/api/library/catalog'), get(host, '/api/library/recovery', true), get(host, '/__meridian_status'),
  ]);
  const last = await get(host, '/api/library');
  assert.deepEqual(last, first, 'The saved Library changed while it was being captured; retry after reviewing the new revision.');
  validateLibraryCanvasDocument(first);
  const prior = await optionalJSON(path.join(directory, 'library-baseline.json'));
  if (prior) assert.deepEqual(prior, first, 'This output directory already contains a different baseline. Preserve it and choose a new directory.');
  else await writeJSON(path.join(directory, 'library-baseline.json'), first, true);
  for (const [name, value] of [['catalog.json', catalog], ['library-recovery-baseline.json', recovery]]) {
    const filename = path.join(directory, name);
    const old = await optionalJSON(filename);
    if (old !== undefined) assert.deepEqual(old, value, `${name} already exists with different data; do not overwrite a checkpoint.`);
    else await writeJSON(filename, value, true);
  }
  const receipt = { host: host.origin, capturedAt: new Date().toISOString(), revision: first.revision,
    baselineSHA256: hash(first), recoveryPresent: recovery !== null, recoverySavedAt: recovery?.savedAt ?? null,
    nodeCount: first.project.nodes.length, targetCount: Object.keys(first.canvasTargets).length,
    discoveryKeys: first.discoveredTargets.length, capabilities: status.capabilities ?? [] };
  // The first receipt belongs to its capture and is never silently overwritten.
  if (!(await optionalJSON(path.join(directory, 'baseline-receipt.json')))) await writeJSON(path.join(directory, 'baseline-receipt.json'), receipt, true);
  return receipt;
}

function originalBindings(project) {
  return {
    canvases: Object.fromEntries(project.canvases.map(work => [`work/${work.id}`, work.id])),
    roots: Object.fromEntries(project.canvases.map(work => [`work/${work.id}`, work.rootId])),
    nodes: Object.fromEntries(project.nodes.filter(node => node.kind !== 'canvas').map(node => [`node/${node.id}`, node.id])),
    assets: Object.fromEntries(project.assets.map(asset => [`asset/${asset.id}`, asset.id])),
    links: Object.fromEntries(project.links.map(link => [`link/${link.id}`, link.id])),
  };
}
function mergeBindings(original, prior) {
  return Object.fromEntries(Object.keys(original).map(scope => [scope, { ...original[scope], ...prior?.[scope] }]));
}
function groupsFor(baseline, catalog, tutorial) {
  validateLibraryCanvasDocument(baseline);
  assert.deepEqual(validateProject(tutorial), [], 'The tutorial project is invalid.');
  assert.equal(tutorial.canvases.length, 12, 'The tutorial contract is one start Work and eleven parts.');
  assert.ok(!catalog.some(item => item.id === tutorial.id), 'Tutorial identity already belongs to an existing catalog project. Use the new tutorial project.');
  const project = baseline.project, work = project.canvases.find(work => work.id === project.entryCanvasId);
  assert.ok(work, 'Library entry Work is missing.');
  const header = project.nodes.find(node => node.id === IDS.header), mythology = project.nodes.find(node => node.id === IDS.mythology);
  assert.ok(header?.parentId === work.rootId && header.kind === 'text', 'The preserved decorative header is missing or has changed role.');
  assert.ok(mythology?.parentId === work.rootId && mythology.kind === 'container', 'The existing Mythology container is missing or has changed role.');
  const byProject = new Map(catalog.map(item => [item.id, item]));
  const groups = CATEGORIES.map(category => ({ ...category, projects: [...category.projects] }));
  groups[2].projects.push(tutorial.id);
  for (const group of groups) {
    group.entries = group.key === 'tutorial' ? [] : group.projects.map(id => {
      const item = byProject.get(id); assert.ok(item, `Catalog project ${id} is missing.`);
      const matches = project.nodes.filter(node => baseline.canvasTargets[node.id]?.projectId === id && baseline.canvasTargets[node.id]?.canvasId === item.entryCanvasId);
      assert.equal(matches.length, 1, `Expected one surviving entry launcher for ${item.title}. Do not recreate a deleted launcher.`);
      return matches[0];
    });
    group.height = (group.key === 'tutorial' ? 1 : group.entries.length) * PITCH + 82;
  }
  const allowedRoots = new Set([IDS.header, IDS.mythology, ...groups[0].entries.map(node => node.id)]);
  assert.deepEqual(project.nodes.filter(node => node.parentId === work.rootId).map(node => node.id).sort(), [...allowedRoots].sort(), 'Unexpected first-generation Library content needs explicit placement; no candidate was composed.');
  assert.deepEqual(project.nodes.filter(node => node.parentId === mythology.id).map(node => node.id).sort(), groups[1].entries.map(node => node.id).sort(), 'The Mythology container contains an unexpected branch.');
  return { groups, work };
}

/** Declare updates against exact identities; no snapshots are regenerated. */
export function makeRecipe(baseline, catalog, tutorial, priorAliases) {
  const { groups, work } = groupsFor(baseline, catalog, tutorial), nav = navigationIndex(baseline.project);
  const recipe = { version: 1, bindings: mergeBindings(originalBindings(baseline.project), priorAliases), canvases: [], nodes: [], links: [], assets: [] };
  // The normal Container already retains its title header. Remove only the
  // extra draft labels from the first local preview, if this is a refinement.
  const obsoleteLabels = CATEGORIES.map(group => `category/${group.key}/label`).filter(key => priorAliases?.nodes[key]);
  if (obsoleteLabels.length) recipe.remove = { nodes: obsoleteLabels };
  const workKey = `work/${work.id}`, changes = new Map();
  const declare = (node, fields) => changes.set(node.id, { key: `node/${node.id}`, existingId: node.id, ...changes.get(node.id), ...fields });
  const common = { fill: '#172B28', border: '#496A61', accent: '#80CDB7', textColor: '#DAE9E4', fontFamily: 'sans', titleSize: 26, fontSize: 17, radius: 0, shape: 'rectangle', contentSizing: 'fixed', padding: 16, childPadding: 12 };
  // Keep the decorative heading as a label at root, rather than inventing a fourth subject.
  declare(baseline.project.nodes.find(node => node.id === IDS.header), {
    title: 'Work Library', tag: 'Work Library', text: 'Choose a collection. Open a Work through its portal; double-click an entry to reveal its related Works.',
    x: 80, y: 50, width: 1760, height: 125, sizeMode: 'fixed', style: { titleSize: 42, fontSize: 19, contentSizing: 'fixed', textAlign: 'left' },
  });
  for (const node of baseline.project.nodes.filter(node => baseline.canvasTargets[node.id])) declare(node, { collapsed: true });
  for (const group of groups) {
    const category = `category/${group.key}`;
    const spec = { key: category, canvas: workKey, kind: 'container', parent: null,
      title: group.title, tag: group.title, text: '', collapsed: false,
      x: group.x, y: TOP, width: WIDTH, height: group.height, sizeMode: 'fixed',
      containerSizes: { expanded: { width: WIDTH, height: group.height, sizeMode: 'fixed' }, collapsed: { width: WIDTH, height: 104, sizeMode: 'fixed' } },
      containerVariant: 'contained', childLayout: 'contained', childArrangement: 'free', childPosition: 'manual', placement: 'free', contentType: 'text', style: common };
    if (group.key === 'mythology') {
      spec.existingId = IDS.mythology;
      spec.unset = ['grid', 'childOffset', 'expansionTray', 'cell', 'style'];
      // A declaration is unique per ID. Its retained alias is still exported below.
      changes.delete(IDS.mythology);
      delete recipe.bindings.nodes[`node/${IDS.mythology}`];
    }
    recipe.nodes.push(spec);
    for (const [index, entry] of group.entries.entries()) {
      const x = group.x + INSET, y = TOP + TILE_TOP + index * PITCH, dx = x - entry.x, dy = y - entry.y;
      const branch = [entry, ...nav.descendants(entry.id)];
      for (const node of branch) declare(node, { x: node.x + dx, y: node.y + dy });
      declare(entry, { parent: category, x, y, width: TILE_WIDTH, height: TILE_HEIGHT, sizeMode: 'fixed', childPosition: 'manual', placement: 'free' });
      // Saved route points, when present, travel with the same original branch.
      const ids = new Set(branch.map(node => node.id));
      for (const link of baseline.project.links) if (link.points?.length && ids.has(link.sourceId) && ids.has(link.targetId))
        recipe.links.push({ key: `link/${link.id}`, existingId: link.id, points: link.points.map(point => ({ x: point.x + dx, y: point.y + dy })) });
    }
  }
  recipe.nodes.push(...changes.values());
  const tutorialCategory = groups[2], start = tutorial.canvases.find(work => work.id === tutorial.entryCanvasId);
  const parts = [start, ...tutorial.canvases.filter(work => work !== start)];
  for (const [index, part] of parts.entries()) {
    const key = `tutorial/${part.id}`;
    recipe.nodes.push({ key, kind: 'canvas-link', canvas: workKey,
      parent: index === 0 ? 'category/tutorial' : `tutorial/${start.id}`,
      title: index === 0 ? tutorial.title : part.title, tag: index === 0 ? tutorial.title : part.title,
      text: '', contentType: 'text', collapsed: true, childLayout: 'spatial', childPosition: 'manual', placement: 'free',
      canvasTarget: { projectId: tutorial.id, canvasId: part.id },
      x: index === 0 ? tutorialCategory.x + INSET : tutorialCategory.x + WIDTH + 64,
      y: index === 0 ? TOP + TILE_TOP : TOP + TILE_TOP + (index - 1) * PITCH,
      width: TILE_WIDTH, height: TILE_HEIGHT, sizeMode: 'fixed',
      style: { fill: '#1C302D', border: '#426258', accent: '#80CDB7', textColor: '#DAE9E4', fontFamily: 'sans', titleSize: 20, fontSize: 16, radius: 0, shape: 'rectangle', contentSizing: 'fixed', portalStyle: 'doorway', portalColor: '#80CDB7' } });
  }
  return recipe;
}

/** Strict content and identity audit. Only the named presentation fields may differ. */
export function auditPreservation(baseline, candidate, catalog, tutorial, aliases) {
  validateLibraryCanvasDocument(baseline); validateLibraryCanvasDocument(candidate);
  assert.deepEqual(validateProject(candidate.project), []);
  const { groups, work } = groupsFor(baseline, catalog, tutorial), a = baseline.project, b = candidate.project;
  const nodes = new Map(b.nodes.map(node => [node.id, node]));
  for (const key of ['schemaVersion', 'revision']) assert.deepEqual(candidate[key], baseline[key], `Library ${key} changed before save.`);
  for (const field of ['id', 'schemaVersion', 'revision', 'title', 'entryCanvasId', 'canvases', 'assets', 'snapshots', 'dependencies']) assert.deepEqual(b[field], a[field], `Preserved project ${field} changed.`);
  const topEntries = new Set(groups.flatMap(group => group.entries.map(node => node.id)));
  const presentation = new Set(['x', 'y', 'childPosition', 'collapsed']);
  const topPresentation = new Set([...presentation, 'parentId', 'width', 'height', 'sizeMode', 'placement']);
  const headerPresentation = new Set([...presentation, 'title', 'tag', 'text', 'width', 'height', 'sizeMode', 'style']);
  const mythologyPresentation = new Set([...topPresentation, 'title', 'tag', 'text', 'contentType', 'containerVariant', 'childArrangement', 'childLayout', 'containerSizes', 'grid', 'cell', 'childOffset', 'expansionTray', 'style']);
  for (const original of a.nodes) {
    const current = nodes.get(original.id); assert.ok(current, `Lost original node ${original.id}.`);
    const allowed = original.id === IDS.header ? headerPresentation : original.id === IDS.mythology ? mythologyPresentation : topEntries.has(original.id) ? topPresentation : baseline.canvasTargets[original.id] ? presentation : new Set();
    const fields = new Set([...Object.keys(original), ...Object.keys(current)]);
    for (const field of fields) if (!allowed.has(field)) assert.deepEqual(current[field], original[field], `Preserved node ${original.id}.${field} changed.`);
    if (baseline.canvasTargets[original.id]) assert.equal(current.collapsed, true, `Existing entry ${original.id} must start collapsed.`);
  }
  const oldLinks = new Map(a.links.map(link => [link.id, link]));
  assert.equal(b.links.length, a.links.length, 'No new decorative or semantic links are needed for categories.');
  for (const current of b.links) {
    const original = oldLinks.get(current.id); assert.ok(original, `Unexpected Library link ${current.id}.`);
    for (const field of new Set([...Object.keys(original), ...Object.keys(current)])) if (field !== 'points') assert.deepEqual(current[field], original[field], `Relationship ${current.id}.${field} changed.`);
    if (!original.points?.length) assert.deepEqual(current.points, original.points, `Unexpected route points on ${current.id}.`);
    else {
      const source = a.nodes.find(node => node.id === original.sourceId), moved = nodes.get(original.sourceId), dx = moved.x - source.x, dy = moved.y - source.y;
      assert.deepEqual(current.points, original.points.map(point => ({ x: point.x + dx, y: point.y + dy })), `Route ${current.id} failed to travel with its original branch.`);
    }
  }
  const oldKeys = new Set(Object.values(baseline.canvasTargets).map(targetKey));
  const tombstones = baseline.discoveredTargets.filter(key => !oldKeys.has(key));
  assert.deepEqual(candidate.discoveredTargets.slice(0, baseline.discoveredTargets.length), baseline.discoveredTargets, 'The ordered original discovery history was rewritten.');
  for (const [id, target] of Object.entries(baseline.canvasTargets)) assert.deepEqual(candidate.canvasTargets[id], target, `External destination ${id} changed.`);
  const candidateKeys = new Set(Object.values(candidate.canvasTargets).map(targetKey));
  for (const key of tombstones) assert.ok(!candidateKeys.has(key), `Deleted launcher ${key} was resurrected.`);
  assert.equal(Object.keys(candidate.canvasTargets).length, Object.keys(baseline.canvasTargets).length + tutorial.canvases.length);
  const categories = b.nodes.filter(node => node.parentId === work.rootId && node.kind === 'container');
  assert.deepEqual(categories.map(node => node.title).sort(), ['Coding', 'Mythology', 'Tutorial']);
  assert.equal(aliases.nodes['category/mythology'], IDS.mythology);
  const permittedRoots = new Set([IDS.header, ...categories.map(node => node.id)]);
  assert.deepEqual(b.nodes.filter(node => node.parentId === work.rootId).map(node => node.id).sort(), [...permittedRoots].sort(), 'The first generation must be three subject containers plus the retained decorative label.');
  for (const group of groups) {
    const category = nodes.get(aliases.nodes[`category/${group.key}`]);
    assert.ok(category.kind === 'container' && category.collapsed === false && category.width === WIDTH && category.containerVariant === 'contained');
    for (const entry of group.entries) assert.equal(nodes.get(entry.id).parentId, category.id);
  }
  const tutorialStart = nodes.get(aliases.nodes[`tutorial/${tutorial.entryCanvasId}`]);
  assert.equal(tutorialStart.parentId, aliases.nodes['category/tutorial']);
  for (const targetWork of tutorial.canvases) {
    const node = nodes.get(aliases.nodes[`tutorial/${targetWork.id}`]); assert.ok(node, `Missing tutorial part ${targetWork.title}.`);
    assert.deepEqual(node.canvasTarget, { projectId: tutorial.id, canvasId: targetWork.id });
    assert.equal(node.collapsed, true);
    if (targetWork.id !== tutorial.entryCanvasId) assert.equal(node.parentId, tutorialStart.id);
  }
  const nav = navigationIndex(b), visible = nav.outline(work.id, true);
  assert.equal(visible.filter(row => row.node.kind === 'canvas-link').length, 6, 'The initial Library must show two Coding, three Mythology, and one Tutorial launcher.');
  return { passed: true, baselineRevision: baseline.revision, baselineSHA256: hash(baseline), candidateSHA256: hash(candidate), tutorialSHA256: hash(tutorial),
    retainedNodes: a.nodes.length, retainedTargets: Object.keys(baseline.canvasTargets).length, retainedLinks: a.links.length,
    retainedAssets: a.assets.length, retainedSnapshots: Object.keys(a.snapshots).length, retainedDiscoveryKeys: baseline.discoveredTargets.length,
    deletedTargetsStillDeleted: tombstones, addedTutorialTargets: tutorial.canvases.length, newNodes: b.nodes.length - a.nodes.length,
    initialVisibleNodes: visible.length, initialVisibleLaunchers: 6,
    categoryIds: Object.fromEntries(groups.map(group => [group.title, aliases.nodes[`category/${group.key}`]])),
    header: { id: IDS.header, role: 'decorative root label; not a fourth subject category', title: 'Work Library' },
    limitations: ['Preservation and schema checks do not replace actual renderer review.', 'This file is a local review candidate; no live Library write has occurred.'] };
}

export async function composeLibrary(directory, tutorialFile) {
  const [baseline, catalog, tutorial] = await Promise.all([
    readJSON(path.join(directory, 'library-baseline.json')), readJSON(path.join(directory, 'catalog.json')), readJSON(tutorialFile),
  ]);
  const previous = await optionalJSON(path.join(directory, 'library-composition.json'));
  let priorProject, priorAliases;
  if (previous) {
    assert.equal(previous.baselineSHA256, hash(baseline), 'Candidate belongs to a different baseline; archive it before starting another cycle.');
    assert.equal(previous.tutorialProjectId, tutorial.id, 'Tutorial project identity changed; archive this candidate before creating new bindings.');
    priorProject = await readJSON(path.join(directory, 'library-project.json'));
    priorAliases = await readJSON(path.join(directory, 'library.aliases.json'));
  }
  const recipe = makeRecipe(baseline, catalog, tutorial, priorAliases);
  const result = applyRecipe(priorProject ?? baseline.project, recipe);
  const candidate = libraryDocumentFromProject(baseline, result.project);
  const audit = auditPreservation(baseline, candidate, catalog, tutorial, result.aliases);
  const metadata = { baselineRevision: baseline.revision, baselineSHA256: hash(baseline), tutorialProjectId: tutorial.id,
    tutorialFile: path.resolve(tutorialFile), tutorialSHA256: hash(tutorial), candidateSHA256: hash(candidate), composedAt: new Date().toISOString(),
    categoryIds: audit.categoryIds, tutorialEntryId: result.aliases.nodes[`tutorial/${tutorial.entryCanvasId}`], liveWrites: false };
  const outputs = [['library-candidate.json', candidate], ['library-project.json', result.project], ['library.recipe.json', recipe],
    ['library.aliases.json', result.aliases], ['library-inspection.json', inspectProject(result.project)],
    ['library-inventory.json', inventoryProject(result.project)], ['library-preservation.json', audit], ['library-composition.json', metadata]];
  for (const [name, value] of outputs) await writeJSON(path.join(directory, name), value);
  const [js, css] = await Promise.all(['viewer.js', 'scene.css'].map(name => readFile(path.join(ROOT, 'dist', name), 'utf8')));
  await writeFile(path.join(directory, 'library-publication.html'), buildPublication(result.project, js, css));
  return audit;
}

/** A current read-only eligibility check, never permission or a race-free save token. */
export async function checkLive(directory, hostValue) {
  const host = origin(hostValue), baseline = await readJSON(path.join(directory, 'library-baseline.json'));
  const metadata = await optionalJSON(path.join(directory, 'library-composition.json'));
  if (metadata) {
    const candidate = await readJSON(path.join(directory, 'library-candidate.json'));
    assert.equal(hash(candidate), metadata.candidateSHA256, 'Candidate differs from its local composition receipt.');
    assert.equal(hash(baseline), metadata.baselineSHA256, 'Composition uses a different baseline.');
  }
  const first = await get(host, '/api/library');
  const [recovery, status] = await Promise.all([get(host, '/api/library/recovery', true), get(host, '/__meridian_status')]);
  const last = await get(host, '/api/library');
  assert.deepEqual(first, last, 'The Library changed during preflight.');
  assert.deepEqual(first, baseline, 'The live Library no longer matches this checkpoint; rebase rather than overwrite it.');
  assert.equal(recovery, null, 'The Library has a recovery draft; resolve it in its editor before applying a reviewed candidate.');
  return { checkedAt: new Date().toISOString(), expectedRevision: baseline.revision, baselineSHA256: hash(baseline), ...(metadata ? { candidateSHA256: metadata.candidateSHA256, tutorialSHA256: metadata.tutorialSHA256 } : {}), recoveryPresent: false,
    capabilities: status.capabilities ?? [], liveWrites: false,
    boundary: 'GET checks are not atomic with a later Library save. Recheck drafts, checkpoint the saved Library, use its expected revision, and retain native windows. This helper never performs a live PUT.' };
}

async function main() {
  const [command = 'help', ...rest] = process.argv.slice(2), args = new Map();
  for (let index = 0; index < rest.length; index += 2) {
    assert.ok(rest[index].startsWith('--') && rest[index + 1] && !rest[index + 1].startsWith('--'), 'Options use --name value.');
    assert.ok(!args.has(rest[index]), `Duplicate option ${rest[index]}.`); args.set(rest[index], rest[index + 1]);
  }
  for (const key of args.keys()) assert.ok(['--directory', '--host', '--tutorial-project'].includes(key), `Unsupported option ${key}.`);
  const directory = path.resolve(args.get('--directory') ?? DEFAULT_DIRECTORY);
  let report;
  if (command === 'capture') report = await captureBaseline(directory, args.get('--host'));
  else if (command === 'compose') {
    assert.ok(args.get('--tutorial-project'), 'Supply --tutorial-project final-installed-identity.json.');
    report = await composeLibrary(directory, path.resolve(args.get('--tutorial-project')));
  } else if (command === 'verify') {
    const metadata = await readJSON(path.join(directory, 'library-composition.json'));
    const [baseline, candidate, catalog, tutorial, aliases] = await Promise.all(['library-baseline.json', 'library-candidate.json', 'catalog.json', metadata.tutorialFile, 'library.aliases.json'].map(file => readJSON(path.isAbsolute(file) ? file : path.join(directory, file))));
    report = auditPreservation(baseline, candidate, catalog, tutorial, aliases);
    assert.equal(report.candidateSHA256, metadata.candidateSHA256, 'Candidate differs from its composition receipt.');
    await writeJSON(path.join(directory, 'library-preservation.json'), report);
  } else if (command === 'check-live') {
    report = await checkLive(directory, args.get('--host'));
    await writeJSON(path.join(directory, 'library-live-preflight.json'), report);
  }
  else if (command === 'help') report = { commands: [
    'capture [--directory DIR] [--host http://127.0.0.1:5240]',
    'compose --tutorial-project PROJECT.json [--directory DIR]',
    'verify [--directory DIR]', 'check-live [--directory DIR] [--host http://127.0.0.1:5240]',
  ], writes: 'Local files only. capture/check-live use GET only; no install/apply command exists.' };
  else throw new Error(`Unknown command ${command}; no action taken.`);
  console.log(JSON.stringify(report, null, 2));
}
if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) main().catch(error => { console.error(error.stack ?? error); process.exitCode = 1; });
