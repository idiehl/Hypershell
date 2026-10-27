import test from 'node:test';
import assert from 'node:assert/strict';
import { addCanvas, addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import { copyNodes, pasteNodes } from '../src/core/clipboard.ts';
import { navigationIndex } from '../src/core/navigation.ts';
import { nodeShape, shapesOverlap } from '../src/core/geometry.ts';
import { evaluate } from '../src/core/engine.ts';
import { resolveGridCell } from '../src/core/cells.ts';
import { validateProject } from '../src/core/rules.ts';
import type { MeridianLink } from '../src/core/types.ts';

function fixture() {
  const project = newProject('Clipboard review'), canvasId = project.entryCanvasId;
  const parent = addNode(project, canvasId, 'container', 'Collection');
  Object.assign(parent, { x: 80, y: 100, width: 420, height: 250, sizeMode: 'fixed', collapsed: true,
    style: { shape: 'hexagon', fill: '#112233', border: '#aabbcc', borderWidth: 3, padding: 9, contentGap: 11 } });
  const child = addNode(project, canvasId, 'text', 'Nested passage');
  Object.assign(child, { parentId: parent.id, x: 95, y: 130, width: 210, height: 70, sizeMode: 'fixed', text: 'Captured body', placement: 'edge', attachSide: 'right' });
  const pivot = addNode(project, canvasId, 'junction', 'Bend');
  Object.assign(pivot, { parentId: parent.id, x: 320, y: 175, width: 32, height: 6, utilityVariant: 'pivot', style: { shape: 'rectangle', fill: '#cc5544' }, snapping: { stickyEdges: false } });
  const outside = addNode(project, canvasId, 'text', 'Outside'); Object.assign(outside, { x: 750, y: 100, width: 200, height: 100, sizeMode: 'fixed' });
  const internal: MeridianLink = { id: crypto.randomUUID(), canvasId, sourceId: child.id, targetId: pivot.id, parentId: parent.id,
    kind: 'visual', relation: 'chronological', direction: 'reverse', label: 'Inside', sourceAnchor: { x: 1, y: .7 }, targetAnchor: { x: .5, y: .5 },
    points: [{ x: 310, y: 150 }, { x: 320, y: 150 }], style: { color: '#123456', width: 11, lineStyle: 'dotted' } };
  const crossing: MeridianLink = { id: crypto.randomUUID(), canvasId, sourceId: child.id, targetId: outside.id, kind: 'visual', relation: 'generic' };
  project.links.push(internal, crossing);
  child.remoteLinks = [{ id: crypto.randomUUID(), targetId: pivot.id, relation: 'mutual' }, { id: crypto.randomUUID(), targetId: outside.id, relation: 'generic' }];
  return { project, parent, child, pivot, outside, internal, crossing };
}

test('copy captures an immutable branch once without changing document or revision', () => {
  const f = fixture(), before = structuredClone(f.project);
  const clipboard = copyNodes(f.project, [f.parent.id, f.child.id, f.parent.id]);
  assert.deepEqual(clipboard.rootIds, [f.parent.id]); assert.equal(clipboard.project.nodes.length, 4); assert.equal(clipboard.project.links.length, 1);
  assert.ok(Object.isFrozen(clipboard)); assert.ok(Object.isFrozen(clipboard.project.nodes[1].style));
  assert.throws(() => { clipboard.project.nodes[1].title = 'Changed'; }, TypeError);
  assert.equal(structuredClone(clipboard).project.nodes[1].title, 'Collection');
  assert.deepEqual(f.project, before);
  f.child.text = 'Later edit';
  assert.equal(clipboard.project.nodes.find(node => node.id === f.child.id)?.text, 'Captured body');
});

test('paste remaps every copied identity and reference while preserving utility geometry and link style', () => {
  const f = fixture(), clipboard = copyNodes(f.project, [f.parent.id]), before = structuredClone(f.project);
  const result = pasteNodes(f.project, clipboard, f.project.entryCanvasId);
  assert.equal(result.rootIds.length, 1); assert.equal(result.nodeIds.length, 3); assert.equal(result.linkIds.length, 1);
  const { project, idMap, offset } = result, parent = project.nodes.find(node => node.id === idMap[f.parent.id])!, child = project.nodes.find(node => node.id === idMap[f.child.id])!, pivot = project.nodes.find(node => node.id === idMap[f.pivot.id])!;
  for (const id of [...result.nodeIds, ...result.linkIds, ...child.remoteLinks!.map(link => link.id)]) assert.match(id, /^[a-f0-9-]{36}$/);
  assert.equal(new Set([...f.project.nodes.map(node => node.id), ...result.nodeIds]).size, f.project.nodes.length + result.nodeIds.length);
  assert.equal(parent.parentId, project.canvases[0].rootId); assert.equal(child.parentId, parent.id); assert.equal(pivot.parentId, parent.id);
  assert.equal(parent.collapsed, true); assert.deepEqual(parent.style, f.parent.style); assert.equal(child.text, f.child.text); assert.equal(child.placement, 'edge');
  assert.deepEqual([pivot.width, pivot.height, pivot.utilityVariant, pivot.style], [f.pivot.width, f.pivot.height, 'pivot', f.pivot.style]);
  for (const original of [f.parent, f.child, f.pivot]) {
    const pasted = project.nodes.find(node => node.id === idMap[original.id])!;
    assert.equal(pasted.x, original.x + offset.x); assert.equal(pasted.y, original.y + offset.y);
  }
  assert.equal(child.remoteLinks![0].targetId, pivot.id); assert.equal(child.remoteLinks![1].targetId, f.outside.id);
  for (const [index, remote] of child.remoteLinks!.entries()) assert.notEqual(remote.id, f.child.remoteLinks![index].id);
  const link = project.links.find(link => link.id === result.linkIds[0])!;
  assert.equal(link.sourceId, child.id); assert.equal(link.targetId, pivot.id); assert.equal(link.parentId, parent.id);
  assert.deepEqual(link.style, f.internal.style); assert.equal(link.direction, 'reverse'); assert.deepEqual(link.sourceAnchor, f.internal.sourceAnchor);
  assert.deepEqual(link.points, f.internal.points!.map(point => ({ x: point.x + offset.x, y: point.y + offset.y })));
  assert.equal(project.links.length, f.project.links.length + 1); assert.equal(project.revision, before.revision);
  assert.deepEqual(validateProject(project), []); assert.deepEqual(f.project, before);
});

test('multiple selected nodes and repeated pastes use one collision-free translation without moving originals', () => {
  const project = newProject(), canvasId = project.entryCanvasId;
  const first = addNode(project, canvasId, 'text', 'First'), second = addNode(project, canvasId, 'text', 'Second');
  Object.assign(first, { x: 100, y: 100, width: 180, height: 70, sizeMode: 'fixed', style: { shape: 'rectangle' } });
  Object.assign(second, { x: 320, y: 120, width: 180, height: 70, sizeMode: 'fixed', style: { shape: 'rectangle' } });
  const before = structuredClone(project), clipboard = copyNodes(project, [first.id, second.id]);
  const one = pasteNodes(project, clipboard, canvasId), two = pasteNodes(one.project, clipboard, canvasId);
  assert.equal(one.nodeIds.length, 2); assert.equal(two.nodeIds.length, 2); assert.notDeepEqual(one.offset, two.offset);
  assert.equal(two.project.nodes.find(node => node.id === two.idMap[second.id])!.x - two.project.nodes.find(node => node.id === two.idMap[first.id])!.x, second.x - first.x);
  const nav = navigationIndex(two.project), tiles = two.project.nodes.filter(node => node.kind !== 'canvas');
  for (let i = 0; i < tiles.length; i++) for (let j = i + 1; j < tiles.length; j++) assert.equal(shapesOverlap(nodeShape(tiles[i], nav.bounds(tiles[i])), nodeShape(tiles[j], nav.bounds(tiles[j]))), false);
  for (const original of before.nodes) assert.deepEqual(two.project.nodes.find(node => node.id === original.id), original);
  assert.deepEqual(project, before); assert.deepEqual(validateProject(two.project), []);
});

test('copying an ordered child strips its former address; copying its parent preserves independent addresses', () => {
  const project = newProject(), canvasId = project.entryCanvasId;
  const grid = addNode(project, canvasId, 'spreadsheet', 'Sheet'), cell = addNode(project, canvasId, 'text', 'Cell');
  Object.assign(cell, { parentId: grid.id, cell: { column: 2, row: 1, name: 'Evidence' }, x: 360, y: 240, width: 200, height: 100, sizeMode: 'fixed' });
  const reference = addNode(project, canvasId, 'reference', 'Cell read'); Object.assign(reference, { parentId: grid.id, targetId: grid.id, targetCell: 'Evidence', dependencyIds: [cell.id], cell: { column: 3, row: 1 } });
  const childCopy = pasteNodes(project, copyNodes(project, [cell.id]), canvasId), detached = childCopy.project.nodes.find(node => node.id === childCopy.rootIds[0])!;
  assert.equal(detached.cell, undefined); assert.equal(detached.parentId, project.canvases[0].rootId);
  assert.deepEqual(project.nodes.find(node => node.id === cell.id)?.cell, { column: 2, row: 1, name: 'Evidence' });
  const parentCopy = pasteNodes(project, copyNodes(project, [grid.id]), canvasId), freshGrid = parentCopy.idMap[grid.id], freshCell = parentCopy.idMap[cell.id], freshReference = parentCopy.project.nodes.find(node => node.id === parentCopy.idMap[reference.id])!;
  assert.equal(resolveGridCell(parentCopy.project, freshGrid, 'Evidence')?.id, freshCell);
  assert.equal(freshReference.targetId, freshGrid); assert.equal(freshReference.targetCell, 'Evidence'); assert.deepEqual(freshReference.dependencyIds, [freshCell]);
  assert.deepEqual(validateProject(parentCopy.project), []);
});

test('internal computed inputs remain independent while crossing wires are omitted', () => {
  const project = newProject(), canvasId = project.entryCanvasId;
  const parent = addNode(project, canvasId, 'container'), input = addNode(project, canvasId, 'number'), output = addNode(project, canvasId, 'output');
  input.parentId = parent.id; input.value = 31; output.parentId = parent.id; output.valueType = 'number';
  project.links.push({ id: crypto.randomUUID(), canvasId, sourceId: input.id, targetId: output.id, kind: 'numeric', relation: 'causal' });
  const result = pasteNodes(project, copyNodes(project, [parent.id]), canvasId);
  assert.equal(evaluate(result.project).values[result.idMap[output.id]], 31);
  result.project.nodes.find(node => node.id === result.idMap[input.id])!.value = 84;
  assert.equal(evaluate(result.project).values[result.idMap[output.id]], 84); assert.equal(evaluate(result.project).values[output.id], 31);
  const detachedOutput = pasteNodes(project, copyNodes(project, [output.id]), canvasId);
  assert.equal(evaluate(detachedOutput.project).values[detachedOutput.rootIds[0]], null); assert.equal(detachedOutput.linkIds.length, 0);
});

test('same-project assets are shared safely and a copied media revision survives later attachment edits', () => {
  const project = newProject(), canvasId = project.entryCanvasId, document = addNode(project, canvasId, 'document');
  const asset = { id: crypto.randomUUID(), name: 'evidence.txt', mime: 'text/plain', base64: btoa('Original attachment') };
  const background = { id: crypto.randomUUID(), name: 'paper.png', mime: 'image/png', base64: btoa(String.fromCharCode(137, 80, 78, 71, 13, 10, 26, 10)) };
  project.assets.push(asset, background); document.assetId = asset.id; document.attachments = [asset.id]; document.style = { backgroundAssetId: background.id, shape: 'rounded' };
  document.hyperlinks = [{ url: 'https://example.test/source', label: 'Source' }];
  const clipboard = copyNodes(project, [document.id]), first = pasteNodes(project, clipboard, canvasId);
  assert.equal(first.project.assets.length, 2); assert.equal(first.project.nodes.find(node => node.id === first.rootIds[0])?.assetId, asset.id);
  asset.base64 = btoa('Later edit');
  const next = pasteNodes(project, clipboard, canvasId), pasted = next.project.nodes.find(node => node.id === next.rootIds[0])!;
  assert.notEqual(pasted.assetId, asset.id); assert.equal(next.project.assets.find(item => item.id === pasted.assetId)?.base64, btoa('Original attachment'));
  assert.deepEqual(pasted.attachments, [pasted.assetId]); assert.equal(pasted.style?.backgroundAssetId, background.id); assert.deepEqual(pasted.hyperlinks, document.hyperlinks);
  assert.equal(project.assets.length, 2); assert.deepEqual(validateProject(next.project), []);
});

test('cross-canvas references keep original intent and cross-project launchers still open the original canvas', () => {
  let source = newProject('Original project'); const originalCanvas = source.entryCanvasId, other = addCanvas(source, 'Destination');
  const value = addNode(source, other.id, 'number', 'External value'); value.value = 72;
  const reference = addNode(source, originalCanvas, 'reference', 'Original value'); reference.targetId = value.id;
  const launch = addNode(source, originalCanvas, 'canvas-link', 'Canvas destination'); launch.targetId = other.rootId;
  const explicit = addNode(source, originalCanvas, 'canvas-link', 'Remote destination'); explicit.canvasTarget = { projectId: 'remote-project', canvasId: 'remote-canvas' };
  source = commitSnapshots(source);
  const clipboard = copyNodes(source, [reference.id, launch.id, explicit.id]);
  const otherCanvasPaste = pasteNodes(source, clipboard, other.id);
  assert.equal(otherCanvasPaste.project.nodes.find(node => node.id === otherCanvasPaste.idMap[reference.id])?.targetId, value.id);
  assert.equal(evaluate(otherCanvasPaste.project).values[otherCanvasPaste.idMap[reference.id]], 72);
  const destination = newProject('Another project'), result = pasteNodes(destination, clipboard, destination.entryCanvasId);
  const copiedLaunch = result.project.nodes.find(node => node.id === result.idMap[launch.id])!;
  assert.equal(copiedLaunch.targetId, undefined); assert.deepEqual(copiedLaunch.canvasTarget, { projectId: source.id, canvasId: other.id });
  assert.deepEqual(result.project.nodes.find(node => node.id === result.idMap[explicit.id])?.canvasTarget, explicit.canvasTarget);
  assert.equal(result.project.nodes.find(node => node.id === result.idMap[reference.id])?.targetId, value.id);
  assert.equal(evaluate(result.project).values[result.idMap[reference.id]], 72); assert.deepEqual(validateProject(result.project), []);
});

test('saved external cell references preserve their coordinate and observation when pasted into another project', () => {
  let source = newProject('Observed cells'); const other = addCanvas(source, 'Evidence');
  const sheet = addNode(source, other.id, 'spreadsheet'), cell = addNode(source, other.id, 'number');
  cell.parentId = sheet.id; cell.cell = { column: 2, row: 2, name: 'Answer' }; cell.value = 63;
  const reference = addNode(source, source.entryCanvasId, 'reference'); reference.targetId = sheet.id; reference.targetCell = 'Answer'; reference.dependencyIds = [cell.id];
  source = commitSnapshots(source);
  const clipboard = copyNodes(source, [reference.id]), destination = newProject('Copied observation');
  const result = pasteNodes(destination, clipboard, destination.entryCanvasId);
  assert.equal(result.project.nodes.find(node => node.id === result.rootIds[0])?.targetCell, 'Answer');
  assert.equal(evaluate(result.project).values[result.rootIds[0]], 63);
  assert.equal(result.project.nodes.some(node => node.id === sheet.id || node.id === cell.id), false);
  assert.equal(result.project.snapshots[sheet.id].node.id, sheet.id); assert.equal(result.project.snapshots[cell.id].node.id, cell.id);
  assert.deepEqual(validateProject(result.project), []);
  const conflict = addNode(destination, destination.entryCanvasId, 'number'); conflict.id = cell.id;
  const before = structuredClone(destination);
  assert.throws(() => pasteNodes(destination, clipboard, destination.entryCanvasId), /reference conflicts/);
  assert.deepEqual(destination, before);
});

test('invalid selections and incompatible destination rules reject atomically', () => {
  const f = fixture(), before = structuredClone(f.project);
  for (const ids of [[], ['missing'], [f.project.canvases[0].rootId]]) assert.throws(() => copyNodes(f.project, ids));
  const clipboard = copyNodes(f.project, [f.parent.id]);
  assert.throws(() => pasteNodes(f.project, clipboard, 'missing'), /destination Work/);
  assert.throws(() => pasteNodes(f.project, clipboard, f.project.entryCanvasId, { offset: { x: NaN, y: 0 } }), /finite/);
  const restricted = newProject('Restricted'); restricted.nodes[0].rules = { nodeKinds: ['text'] };
  const restrictedBefore = structuredClone(restricted);
  assert.throws(() => pasteNodes(restricted, clipboard, restricted.entryCanvasId), /inherited rule/);
  assert.deepEqual(restricted, restrictedBefore); assert.deepEqual(f.project, before);
  const malformed = structuredClone(clipboard); malformed.rootIds.push('missing');
  assert.throws(() => pasteNodes(f.project, malformed, f.project.entryCanvasId), /malformed/);
});
