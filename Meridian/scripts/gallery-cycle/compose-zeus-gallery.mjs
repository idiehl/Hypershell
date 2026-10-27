#!/usr/bin/env node
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import path from 'node:path';
import { createHash } from 'node:crypto';
import { isDeepStrictEqual } from 'node:util';
import { updateNodeType } from '../../src/core/node-editing.ts';
import { applyRecipe, inspectProject } from '../../src/core/authoring.ts';
import { commitSnapshots } from '../../src/core/model.ts';
import { isProjectShape, validateProject, validateChange } from '../../src/core/rules.ts';

// Local, reviewable composition only: this utility contains no HTTP calls and
// never saves a canvas, recovery draft or checkpoint to a Meridian host.
const targetId = '005fe68a-bc30-4b11-af1d-4dadec799b1c';
const selfLinkIds = ['cf9cd9b4-bf6f-4a68-aea6-f6da85e305fe', '4748f775-f7f6-420c-a226-462ca0b56863'];
const defaults = path.resolve('output/gallery-qa-20261008/content-prep');
const args = new Map();
for (let index = 2; index < process.argv.length; index += 2) {
  const option = process.argv[index], value = process.argv[index + 1];
  if (!['--input', '--recipe', '--manifest', '--out'].includes(option) || !value || value.startsWith('--') || args.has(option))
    throw new Error('Use --input current-base.json --out candidate.json [--recipe reviewed-items.recipe.json] [--manifest reviewed-manifest.json]. No action taken.');
  args.set(option, value);
}
const input = path.resolve(args.get('--input') ?? '');
const output = path.resolve(args.get('--out') ?? '');
if (!args.has('--input') || !args.has('--out')) throw new Error('--input and --out are required. No action taken.');
if (input.toLowerCase() === output.toLowerCase()) throw new Error('Candidate must have a different path from the current base.');
const recipePath = path.resolve(args.get('--recipe') ?? path.join(defaults, 'zeus-gallery-items.recipe.json'));
const manifestPath = path.resolve(args.get('--manifest') ?? path.join(defaults, 'zeus-gallery-manifest.json'));
const baseText = await readFile(input, 'utf8');
const base = JSON.parse(baseText);
const manifest = JSON.parse(await readFile(manifestPath, 'utf8'));
const recipe = JSON.parse(await readFile(recipePath, 'utf8'));
const assert = (condition, message) => { if (!condition) throw new Error(`${message} No live save was attempted.`); };
const hash = bytes => createHash('sha256').update(bytes).digest('hex');
const validate = (project, label) => {
  assert(isProjectShape(project), `${label} has an invalid project shape.`);
  const issues = validateProject(project);
  assert(!issues.length, `${label} is invalid: ${issues.map(issue => issue.message).join(' ')}`);
};
const equal = (actual, expected, message) => assert(isDeepStrictEqual(actual, expected), message);
validate(base, 'Current base');
assert(base.id === manifest.projectId && manifest.nodeId === targetId, 'The reviewed gallery belongs to a different project or node.');
const original = base.nodes.find(node => node.id === targetId);
assert(original, 'The referenced Zeus node no longer exists.');
assert(original.canvasId === manifest.canvasId, 'The referenced node moved to another canvas; review the placement before composition.');
assert(original.kind === 'image', 'The referenced node is already converted or no longer a Media node; review before applying.');
assert(!base.nodes.some(node => node.parentId === targetId), 'The referenced node already has children; composition refuses to duplicate or replace them.');
assert(original.assetId === manifest.originalAssetId, 'The original primary asset changed; review the newly selected image before composition.');
const originalAsset = base.assets.find(asset => asset.id === original.assetId);
assert(originalAsset?.mime === 'image/jpeg', 'The original image attachment is missing or changed media type.');
const reviewedOriginal = manifest.items.find(item => item.existingAssetId === manifest.originalAssetId);
assert(reviewedOriginal, 'The manifest does not identify the preserved original image.');
const reviewedBytes = await readFile(reviewedOriginal.imagePath);
const currentBytes = Buffer.from(originalAsset.base64, 'base64');
assert(currentBytes.equals(reviewedBytes), 'The primary image bytes changed; research and review the current image before composition.');

assert(recipe.version === 1 && Object.keys(recipe).every(key => ['version', 'nodes', 'assets'].includes(key)), 'Only the reviewed child/asset recipe is accepted.');
assert(Array.isArray(recipe.nodes) && recipe.nodes.length === 5 && manifest.items.length === 5, 'Expected exactly five reviewed image children.');
assert(Array.isArray(recipe.assets) && recipe.assets.length === 4, 'Expected four reviewed image attachments.');
for (const [index, item] of manifest.items.entries()) {
  const spec = recipe.nodes[index];
  assert(spec.key === `zeus-image-${item.objectId}` && !spec.existingId && spec.kind === 'image' && spec.parent === targetId && spec.canvas === manifest.canvasId,
    'The child recipe identity, type, collection order or parent changed.');
  if (!item.existingAssetId) {
    const asset = recipe.assets.find(asset => asset.key === `zeus-asset-${item.objectId}`);
    assert(asset && !asset.existingId && spec.asset === asset.key && asset.mime === 'image/jpeg', 'A reviewed child attachment is missing or attempts to update an existing attachment.');
    assert(Buffer.from(asset.base64, 'base64').equals(await readFile(item.imagePath)), 'A reviewed image attachment changed bytes.');
  }
  // All child positions are copied from the fresh parent, rather than the old
  // research snapshot. The Gallery renderer presents these inside one tile.
  Object.assign(spec, {x: original.x, y: original.y, width: original.width, height: original.height,
    style: {...original.style, imageFit: 'contain'}});
}
Object.assign(recipe.nodes[0], {title: original.title, text: original.text ?? '', asset: original.assetId});

let converted = updateNodeType(base, targetId, {kind: 'container', containerVariant: 'gallery'});
const parent = converted.nodes.find(node => node.id === targetId);
Object.assign(parent, {title: 'Zeus · Gallery', text: 'Five views of Zeus and Jupiter, from Greek and Roman antiquity to Renaissance art.', collapsed: false});
delete parent.assetId;

// Remove only the two known accidental duplicates if the complete records,
// except their GUIDs, still match each other and the reviewed baseline. Any
// user changes to these links are retained for explicit review.
const baseline = JSON.parse(await readFile(path.join(path.dirname(manifestPath), 'source-project.json'), 'utf8'));
const withoutId = link => { const {id, ...fields} = link; return fields; };
const currentSelfLinks = selfLinkIds.map(id => converted.links.find(link => link.id === id));
const reviewedSelfLinks = selfLinkIds.map(id => baseline.links.find(link => link.id === id));
const knownDuplicates = currentSelfLinks.every(link => link && link.kind === 'visual' && link.relation === 'causal' && link.sourceId === targetId && link.targetId === targetId)
  && reviewedSelfLinks.every(Boolean)
  && isDeepStrictEqual(withoutId(currentSelfLinks[0]), withoutId(currentSelfLinks[1]))
  && currentSelfLinks.every((link, index) => isDeepStrictEqual(link, reviewedSelfLinks[index]));
const removedLinkIds = knownDuplicates ? selfLinkIds : [];
if (knownDuplicates) converted.links = converted.links.filter(link => !removedLinkIds.includes(link.id));
const result = applyRecipe(converted, recipe);
const candidate = commitSnapshots(result.project);
validate(candidate, 'Gallery candidate');
assert(!validateChange(base, candidate).length, 'The complete gallery change is invalid.');

const existingNodeIds = new Set(base.nodes.map(node => node.id));
const newNodes = candidate.nodes.filter(node => !existingNodeIds.has(node.id));
assert(newNodes.length === 5 && newNodes.every(node => node.parentId === targetId), 'Composition added unexpected nodes.');
const firstChild = candidate.nodes.find(node => node.id === result.aliases.nodes[recipe.nodes[0].key]);
equal(firstChild.title, original.title, 'The original image title was not preserved on the first item.');
equal(firstChild.text, original.text ?? '', 'The original image caption was not preserved on the first item.');
equal(firstChild.assetId, original.assetId, 'The original image asset identity was not preserved.');
const gallery = candidate.nodes.find(node => node.id === targetId);
for (const field of ['id', 'canvasId', 'parentId', 'x', 'y', 'width', 'height', 'style', 'placement', 'snapping', 'rules', 'attachments', 'hyperlinks', 'remoteLinks', 'dependencyIds'])
  equal(gallery[field], original[field], `The fresh parent ${field} was not preserved.`);
for (const node of base.nodes.filter(node => node.id !== targetId))
  equal(candidate.nodes.find(item => item.id === node.id), node, `Unrelated node ${node.id} changed.`);
for (const link of base.links.filter(link => !removedLinkIds.includes(link.id)))
  equal(candidate.links.find(item => item.id === link.id), link, `Unrelated or edited link ${link.id} changed.`);
for (const asset of base.assets)
  equal(candidate.assets.find(item => item.id === asset.id), asset, `Existing attachment ${asset.id} changed.`);
for (const [key, value] of Object.entries(base))
  if (!['nodes', 'links', 'assets', 'snapshots'].includes(key)) equal(candidate[key], value, `Unrelated project field ${key} changed.`);

const inspection = inspectProject(candidate);
const summary = {
  projectId: base.id, expectedRevision: base.revision, canvasId: original.canvasId, nodeId: targetId,
  input, inputSHA256: hash(baseText), candidate: output,
  oldType: original.kind, newType: gallery.kind, containerVariant: gallery.containerVariant,
  originalTitleRetainedOnFirstItem: firstChild.title, originalCaptionRetained: true,
  primaryImageRetained: {id: originalAsset.id, bytes: currentBytes.length, sha256: hash(currentBytes)},
  parentGeometryRetained: {x: gallery.x, y: gallery.y, width: gallery.width, height: gallery.height},
  childIds: newNodes.map(node => node.id), addedAssetIds: candidate.assets.filter(asset => !base.assets.some(existing => existing.id === asset.id)).map(asset => asset.id),
  removedLinkIds, retainedKnownSelfLinkIds: selfLinkIds.filter(id => candidate.links.some(link => link.id === id)),
  preservation: {unrelatedNodesUnchanged: base.nodes.length - 1, unrelatedLinksUnchanged: base.links.length - removedLinkIds.length,
    existingAssetsUnchanged: base.assets.length, canvasesAndProjectMetadataUnchanged: true,
    snapshots: 'All local saved observations refreshed by commitSnapshots; external observations retained.'},
  inspection: inspection.summary,
  liveSaveAttempted: false,
};
assert(await readFile(input, 'utf8') === baseText, 'The local input changed while composing; rerun against its current contents.');
await mkdir(path.dirname(output), {recursive: true});
await writeFile(output, JSON.stringify(candidate, null, 2));
await writeFile(`${output}.aliases.json`, JSON.stringify(result.aliases, null, 2));
await writeFile(`${output}.inspection.json`, JSON.stringify(inspection, null, 2));
await writeFile(`${output}.diff.json`, JSON.stringify(summary, null, 2));
console.log(JSON.stringify(summary, null, 2));
