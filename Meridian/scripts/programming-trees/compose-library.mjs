/** Local append-only Library candidate. This script never contacts or saves to a Meridian host. */
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { applyRecipe, inspectProject } from '../../src/core/authoring.ts';
import { libraryDocumentFromProject, validateLibraryCanvasDocument } from '../../src/core/library.ts';
import { validateProject } from '../../src/core/rules.ts';
import { navigationIndex } from '../../src/core/navigation.ts';
import { buildPublication } from '../../src/publication.ts';

const ROOT = fileURLToPath(new URL('../../', import.meta.url));
const DEFAULT_OUTPUT = path.join(ROOT, 'output/programming-trees-20261021');
const CODING_ID = '7d5bf0a1-bf76-4f8b-b743-7ec8667978f4';
const FIRST_CONNECTIONS_ID = '893ce9bf-c07e-457c-b4d9-33f001e8d0b6';
const hash = value => createHash('sha256').update(typeof value === 'string' ? value : JSON.stringify(value)).digest('hex');
const readJSON = async file => JSON.parse(await readFile(file, 'utf8'));
const saveJSON = (directory, name, value) => writeFile(path.join(directory, name), JSON.stringify(value, null, 2) + '\n');
const optionalJSON = async file => { try { return await readJSON(file); } catch (error) { if (error.code === 'ENOENT') return null; throw error; } };
const overlaps = (a, b) => a.x < b.x + b.width && a.x + a.width > b.x && a.y < b.y + b.height && a.y + a.height > b.y;
const targetKey = target => `${target.projectId}:${target.canvasId ?? '*'}`;
function openPosition(proposed, occupied) {
  const obstacles = occupied.map(box => ({ ...box, x: box.x - 18, y: box.y - 18, width: box.width + 36, height: box.height + 36 }));
  const candidates = [];
  // Append near the existing branch in a forward column; do not send a new
  // library child to negative x merely because an old hidden column is dense.
  for (let row = 0; row < 16; row++) for (let column = 0; column < 7; column++) candidates.push({ ...proposed,
    x: proposed.x + column * (proposed.width + 36), y: proposed.y + row * (proposed.height + 36) });
  candidates.sort((a, b) => (a.x - proposed.x) ** 2 + (a.y - proposed.y) ** 2 - (b.x - proposed.x) ** 2 - (b.y - proposed.y) ** 2 || a.y - b.y || a.x - b.x);
  return candidates.find(candidate => !obstacles.some(obstacle => overlaps(candidate, obstacle)))
    ?? { ...proposed, y: Math.max(...occupied.map(box => box.y + box.height)) + 36 };
}

export function auditLibraryAppend(baseline, candidate, sources, aliases) {
  validateLibraryCanvasDocument(baseline); validateLibraryCanvasDocument(candidate);
  assert.deepEqual(validateProject(candidate.project), []);
  const original = baseline.project, current = candidate.project;
  assert.equal(candidate.revision, baseline.revision);
  const { nodes: oldNodes, links: oldLinks, ...oldProject } = original;
  const { nodes: newNodes, links: newLinks, ...newProject } = current;
  assert.deepEqual(newProject, oldProject, 'Existing Work metadata, assets, snapshots, dependencies or other Works changed.');
  assert.deepEqual(newNodes.slice(0, oldNodes.length), oldNodes, 'An original node, its order, destination, presentation or disclosure state changed.');
  assert.deepEqual(newLinks.slice(0, oldLinks.length), oldLinks, 'An original relationship or its order changed.');
  assert.equal(newNodes.length, oldNodes.length + 2); assert.equal(newLinks.length, oldLinks.length + 2);
  for (const [id, target] of Object.entries(baseline.canvasTargets)) assert.deepEqual(candidate.canvasTargets[id], target);
  assert.equal(Object.keys(candidate.canvasTargets).length, Object.keys(baseline.canvasTargets).length + 2);
  assert.deepEqual(candidate.discoveredTargets.slice(0, baseline.discoveredTargets.length), baseline.discoveredTargets);
  assert.equal(candidate.discoveredTargets.length, baseline.discoveredTargets.length + 2);
  const oldTargets = new Set(Object.values(baseline.canvasTargets).map(targetKey));
  const tombstones = baseline.discoveredTargets.filter(key => !oldTargets.has(key));
  const newTargets = new Set(Object.values(candidate.canvasTargets).map(targetKey));
  for (const tombstone of tombstones) assert.ok(!newTargets.has(tombstone), `Deleted destination resurrected: ${tombstone}`);
  assert.equal([...newTargets].filter(key => key.startsWith(`${FIRST_CONNECTIONS_ID}:`)).length, 0);
  assert.ok(tombstones.filter(key => key.startsWith(`${FIRST_CONNECTIONS_ID}:`)).length === 4);
  const nav = navigationIndex(current), appended = newNodes.slice(oldNodes.length);
  const destinations = [];
  for (const source of sources) {
    const key = `programming-tree/${source.slug}`, node = nav.nodes.get(aliases.nodes[key]);
    assert.ok(node); assert.equal(node.kind, 'canvas-link'); assert.equal(node.parentId, source.parent.id);
    assert.equal(node.title, source.title); assert.equal(node.tag, source.title); assert.equal(node.collapsed, true);
    const template = original.nodes.find(other => other.parentId === source.parent.id && other.kind === 'canvas-link');
    assert.deepEqual(node.style, template.style, 'New launcher must use its siblings\' established appearance.');
    assert.equal(node.width, template.width); assert.equal(node.height, template.height);
    assert.equal(source.parent.parentId, CODING_ID); assert.equal(source.parent.collapsed, true);
    assert.deepEqual(node.canvasTarget, { projectId: source.projectId, canvasId: source.workId });
    assert.deepEqual(candidate.canvasTargets[node.id], node.canvasTarget);
    assert.ok(candidate.discoveredTargets.includes(targetKey(node.canvasTarget)));
    const relationship = current.links.find(link => link.id === aliases.links[`${key}/relationship`]);
    assert.ok(relationship); assert.equal(relationship.sourceId, source.parent.id); assert.equal(relationship.targetId, node.id);
    assert.equal(relationship.parentId, source.parent.id); assert.equal(relationship.kind, 'visual'); assert.equal(relationship.relation, 'generic');
    for (const other of current.nodes) if (other.id !== node.id && other.kind !== 'canvas') assert.ok(!overlaps(nav.bounds(node), nav.bounds(other)), `New Library portal overlaps ${other.title || other.id}.`);
    assert.equal(nav.isNodeVisible(node.id), false, 'New subordinate launcher must remain hidden below the collapsed entry.');
    destinations.push({ slug: source.slug, title: node.title, nodeId: node.id, parentId: node.parentId, canvasTarget: node.canvasTarget,
      position: { x: node.x, y: node.y, width: node.width, height: node.height }, relationshipId: relationship.id });
  }
  assert.equal(new Set(appended.map(node => node.id)).size, 2);
  return { passed: true, revision: baseline.revision, added: { nodes: 2, links: 2, targets: 2, discoveryKeys: 2 },
    preserved: { nodes: oldNodes.length, links: oldLinks.length, canvases: original.canvases.length,
      assets: original.assets.length, snapshots: Object.keys(original.snapshots).length, targets: Object.keys(baseline.canvasTargets).length,
      discoveryKeys: baseline.discoveredTargets.length, tombstones: tombstones.length },
    originalProjectMetadataUnchanged: true, originalEntitiesExact: true, originalSnapshotsExact: true,
    initialLibraryVisibilityUnchanged: true, addedPortalGeometryClearOfEveryNode: true, deletedFirstConnectionsPreserved: true,
    destinations, liveWrites: false };
}

function proveAuditRejectsChanges(baseline, candidate, sources, aliases) {
  const nodeId = aliases.nodes['programming-tree/csharp'];
  const cases = [
    ['original node geometry', copy => { copy.project.nodes[1].x += 1; }],
    ['original link', copy => { copy.project.links[0].label = 'Unexpected edit'; }],
    ['original asset', copy => { copy.project.assets[0].base64 = 'AAAA'; }],
    ['original snapshot', copy => { Object.values(copy.project.snapshots)[0].savedAt = '2099-01-01T00:00:00.000Z'; }],
    ['new target', copy => { copy.canvasTargets[nodeId].canvasId = sources[0].entryCanvasId; }],
    ['discovery history', copy => { copy.discoveredTargets.splice(0, 1); }],
    ['new launcher style', copy => { copy.project.nodes.find(node => node.id === nodeId).style.fill = '#000000'; }],
    ['new launcher overlap', copy => { const node = copy.project.nodes.find(node => node.id === nodeId); node.x = sources[0].parent.x; node.y = sources[0].parent.y; }],
  ];
  for (const [name, change] of cases) {
    const copy = structuredClone(candidate); change(copy);
    assert.throws(() => auditLibraryAppend(baseline, copy, sources, aliases), undefined, `Audit failed to reject ${name}.`);
  }
  return cases.map(([name]) => name);
}

export async function composeProgrammingLibrary(directory = DEFAULT_OUTPUT) {
  const baseline = await readJSON(path.join(directory, 'library-baseline.json'));
  validateLibraryCanvasDocument(baseline);
  const sources = [];
  for (const [slug, title] of [['csharp', 'C# namespaces'], ['cpp', 'C++ classes and types']]) {
    const [base, aliases, manifest] = await Promise.all(['baseline.json', 'aliases.json', 'manifest.json'].map(name => readJSON(path.join(directory, slug, name))));
    assert.equal(base.id, manifest.projectId); assert.equal(aliases.canvases.tree, manifest.newWorkId);
    const matches = baseline.project.nodes.filter(node => baseline.canvasTargets[node.id]?.projectId === base.id && baseline.canvasTargets[node.id]?.canvasId === base.entryCanvasId);
    assert.equal(matches.length, 1, `Expected one original ${slug} collection entry.`);
    const parent = matches[0]; assert.equal(parent.parentId, CODING_ID); assert.equal(parent.collapsed, true);
    assert.ok(!Object.values(baseline.canvasTargets).some(target => target.projectId === base.id && target.canvasId === aliases.canvases.tree), 'New Work already has a baseline Library destination.');
    assert.ok(!baseline.discoveredTargets.includes(`${base.id}:${aliases.canvases.tree}`), 'A tombstone already records this new Work; do not recreate it.');
    sources.push({ slug, title, projectId: base.id, entryCanvasId: base.entryCanvasId, workId: aliases.canvases.tree, parent,
      sourceDraftSHA256: manifest.draftSHA256 });
  }
  const previous = await optionalJSON(path.join(directory, 'library-composition.json'));
  let priorProject, priorAliases;
  if (previous) {
    assert.equal(previous.baselineSHA256, hash(baseline), 'Library baseline changed; retain this candidate before rebasing.');
    assert.deepEqual(previous.sources.map(source => [source.slug, source.projectId, source.workId]), sources.map(source => [source.slug, source.projectId, source.workId]), 'Source Work identities changed; do not silently replace launcher bindings.');
    [priorProject, priorAliases] = await Promise.all(['library-project.json', 'library.aliases.json'].map(name => readJSON(path.join(directory, name))));
  }
  const recipe = { version: 1, ...(priorAliases ? { bindings: priorAliases } : {}), nodes: [], links: [] };
  const nav = navigationIndex(baseline.project);
  const occupied = baseline.project.nodes.filter(node => node.kind !== 'canvas').map(node => ({ id: node.id,
    x: Math.min(node.x, nav.bounds(node).x), y: Math.min(node.y, nav.bounds(node).y),
    width: Math.max(node.x + node.width, nav.bounds(node).x + nav.bounds(node).width) - Math.min(node.x, nav.bounds(node).x),
    height: Math.max(node.y + node.height, nav.bounds(node).y + nav.bounds(node).height) - Math.min(node.y, nav.bounds(node).y), shape: 'rectangle' }));
  for (const source of sources) {
    const siblings = baseline.project.nodes.filter(node => node.parentId === source.parent.id && node.kind === 'canvas-link');
    assert.ok(siblings.length, 'Expected ordinary Work launcher siblings to supply the established style.');
    const template = siblings[0], width = template.width, height = template.height;
    const proposed = { x: template.x, y: Math.max(...siblings.map(node => Math.max(node.y + node.height, nav.bounds(node).y + nav.bounds(node).height))) + 36, width, height, shape: 'rectangle' };
    const position = openPosition(proposed, occupied);
    const key = `programming-tree/${source.slug}`, x = position.x, y = position.y;
    recipe.nodes.push({ key, canvas: source.parent.canvasId, parent: source.parent.id, kind: 'canvas-link', title: source.title, tag: source.title,
      x, y, width, height, sizeMode: 'fixed', collapsed: true, childLayout: 'spatial', childArrangement: 'free', containerVariant: 'contained',
      childPosition: 'manual', placement: 'free', canvasTarget: { projectId: source.projectId, canvasId: source.workId }, style: structuredClone(template.style) });
    recipe.links.push({ key: `${key}/relationship`, canvas: source.parent.canvasId, parent: source.parent.id,
      source: source.parent.id, target: key, kind: 'visual', relation: 'generic', direction: 'none', style: { color: '#557A68', width: 1.5, lineStyle: 'solid' } });
    occupied.push({ x, y, width, height, shape: 'rectangle' });
  }
  const result = applyRecipe(priorProject ?? baseline.project, recipe);
  const candidate = libraryDocumentFromProject(baseline, result.project);
  const audit = auditLibraryAppend(baseline, candidate, sources, result.aliases);
  // A second composition against returned bindings is an identity/payload stability check.
  const repeated = applyRecipe(result.project, { ...recipe, bindings: result.aliases });
  const repeatedCandidate = libraryDocumentFromProject(baseline, repeated.project);
  assert.deepEqual(repeatedCandidate, candidate, 'Repeated composition must retain IDs and exact payload.');
  assert.deepEqual(repeated.aliases, result.aliases);
  const rejectedTamperingCases = proveAuditRejectsChanges(baseline, candidate, sources, result.aliases);
  const metadata = { baselineRevision: baseline.revision, baselineSHA256: hash(baseline), candidateSHA256: hash(candidate),
    sources: sources.map(({ parent, ...source }) => ({ ...source, parentId: parent.id })),
    repeatedCompositionExact: true, liveWrites: false, composedAt: new Date().toISOString() };
  const outputs = [['library-candidate.json', candidate], ['library-project.json', result.project], ['library.recipe.json', recipe],
    ['library.aliases.json', result.aliases], ['library-preservation.json', { ...audit, repeatedCompositionExact: true, rejectedTamperingCases }],
    ['library-composition.json', metadata], ['library-inspection.json', inspectProject(result.project)]];
  for (const [name, value] of outputs) await saveJSON(directory, name, value);
  const [viewer, css] = await Promise.all(['viewer.js', 'scene.css'].map(name => readFile(path.join(ROOT, 'dist', name), 'utf8')));
  await writeFile(path.join(directory, 'library-publication.html'), buildPublication(result.project, viewer, css));
  return { ...audit, candidateSHA256: metadata.candidateSHA256, repeatedCompositionExact: true };
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  console.log(JSON.stringify(await composeProgrammingLibrary(process.argv[2] ? path.resolve(process.argv[2]) : DEFAULT_OUTPUT)));
}
