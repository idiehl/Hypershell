import test from 'node:test';
import assert from 'node:assert/strict';
import { addCanvas, addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import { createSnippet, deleteSelectionGraph, groupSelection, moveSelection, selectionGraph } from '../src/core/selection.ts';
import { evaluate } from '../src/core/engine.ts';
import { navigationIndex } from '../src/core/navigation.ts';
import { validateProject } from '../src/core/rules.ts';
import { createPublicationPayload } from '../src/publication.ts';
import type { MeridianLink, MeridianProject } from '../src/core/types.ts';

function fixture() {
  const project = newProject('Selection canvas'), canvasId = project.entryCanvasId;
  const branch = addNode(project, canvasId, 'text', 'Parent');
  Object.assign(branch, { x: 80, y: 100, childLayout: 'spatial', placement: 'near', collapsed: true });
  const child = addNode(project, canvasId, 'input', 'Child'); Object.assign(child, { parentId: branch.id, x: 280, y: 200, value: true, placement: 'edge' });
  const sibling = addNode(project, canvasId, 'output', 'Sibling'); Object.assign(sibling, { x: 550, y: 180 });
  const outside = addNode(project, canvasId, 'text', 'Outside'); Object.assign(outside, { x: 900, y: 130 });
  const internal: MeridianLink = { id: crypto.randomUUID(), canvasId, sourceId: branch.id, targetId: child.id, parentId: branch.id, kind: 'visual', relation: 'causal', points: [{ x: 250, y: 140 }] };
  const computed: MeridianLink = { id: crypto.randomUUID(), canvasId, sourceId: child.id, targetId: sibling.id, kind: 'functional', relation: 'causal', points: [{ x: 510, y: 260 }] };
  const boundary: MeridianLink = { id: crypto.randomUUID(), canvasId, sourceId: sibling.id, targetId: outside.id, kind: 'visual', relation: 'generic', points: [{ x: 820, y: 290 }] };
  project.links.push(internal, computed, boundary);
  return { project, branch, child, sibling, outside, internal, computed, boundary };
}

test('selection normalization deduplicates overlapping branches while selected-only retains explicit descendants', () => {
  const f = fixture(), before = structuredClone(f.project);
  const all = selectionGraph(f.project, [f.child.id, f.branch.id, f.branch.id]);
  assert.deepEqual(all.rootIds, [f.branch.id]); assert.deepEqual(all.nodeIds, [f.branch.id, f.child.id]);
  assert.deepEqual(selectionGraph(f.project, [f.branch.id], { includeDescendants: false }).nodeIds, [f.branch.id]);
  assert.deepEqual(selectionGraph(f.project, [f.branch.id, f.child.id], { includeDescendants: false }).nodeIds, [f.branch.id, f.child.id]);
  assert.deepEqual(f.project, before);
});

test('moving roots plus a selected descendant translates it once and moves only wholly internal route bends', () => {
  const f = fixture(), before = structuredClone(f.project);
  const moved = moveSelection(f.project, [f.branch.id, f.child.id, f.sibling.id], 37, -19);
  for (const original of [f.branch, f.child, f.sibling]) {
    const node = moved.nodes.find(node => node.id === original.id)!;
    assert.equal(node.x, original.x + 37); assert.equal(node.y, original.y - 19);
  }
  assert.equal(moved.nodes.find(node => node.id === f.branch.id)?.placement, 'free');
  assert.equal(moved.nodes.find(node => node.id === f.child.id)?.placement, 'edge');
  assert.deepEqual(moved.links.find(link => link.id === f.internal.id)?.points, [{ x: 287, y: 121 }]);
  assert.deepEqual(moved.links.find(link => link.id === f.computed.id)?.points, [{ x: 547, y: 241 }]);
  assert.deepEqual(moved.links.find(link => link.id === f.boundary.id)?.points, f.boundary.points);
  assert.deepEqual(moved.nodes.find(node => node.id === f.outside.id), f.outside);
  assert.deepEqual(f.project, before);
});

test('deleting a branch removes incident and independently owned links without deleting outside endpoints', () => {
  const f = fixture(); f.boundary.parentId = f.branch.id;
  f.project = commitSnapshots(f.project); const before = structuredClone(f.project);
  const deleted = deleteSelectionGraph(f.project, [f.branch.id, f.child.id]);
  assert.deepEqual(deleted.nodes.filter(node => node.kind !== 'canvas').map(node => node.id), [f.sibling.id, f.outside.id]);
  assert.equal(deleted.links.length, 0); assert.equal(deleted.snapshots[f.child.id], undefined);
  assert.equal(evaluate(deleted).values[f.sibling.id], null);
  assert.deepEqual(structuredClone(f.project), before); assert.deepEqual(validateProject(deleted), []);
});

test('grouping siblings preserves all coordinates, descendant state, identities and computation', () => {
  const f = fixture(), before = structuredClone(f.project);
  const { project, rootId } = groupSelection(f.project, [f.branch.id, f.child.id, f.sibling.id], { title: 'Decision branch', x: 59, y: -110 });
  const root = project.nodes.find(node => node.id === rootId)!;
  assert.equal(root.kind, 'container'); assert.equal(root.childLayout, 'contained'); assert.equal(root.containerVariant, 'contained'); assert.equal(root.collapsed, false);
  assert.equal(root.title, 'Decision branch'); assert.equal(root.x, 59); assert.equal(root.y, -110);
  for (const original of [f.branch, f.child, f.sibling]) {
    const node = project.nodes.find(node => node.id === original.id)!;
    assert.equal(node.x, original.x); assert.equal(node.y, original.y); assert.equal(node.collapsed, original.collapsed);
    assert.equal(node.parentId, original === f.child ? f.branch.id : rootId);
  }
  assert.deepEqual(project.links, before.links); assert.equal(evaluate(project).values[f.sibling.id], true);
  assert.deepEqual(structuredClone(f.project), before);
});

test('grouping rejects different parents and inherited container restrictions atomically', () => {
  const f = fixture(); let before = structuredClone(f.project);
  assert.throws(() => groupSelection(f.project, [f.child.id, f.sibling.id]), /same parent/);
  assert.deepEqual(f.project, before);
  f.branch.rules = { nodeKinds: ['input'], linkKinds: ['visual', 'functional'] }; before = structuredClone(f.project);
  assert.throws(() => groupSelection(f.project, [f.child.id]), /inherited rule/);
  assert.deepEqual(f.project, before);
});

test('automatic grouping encloses selected branches and preserves internal relationships while respecting an explicit frame origin', () => {
  const f = fixture(); Object.assign(f.outside, { x: f.branch.x, y: f.branch.y - 210, width: 360, height: 200 });
  const before = structuredClone(f.project), result = groupSelection(f.project, [f.branch.id, f.sibling.id]);
  const root = result.project.nodes.find(node => node.id === result.rootId)!, nav = navigationIndex(f.project);
  const frame = navigationIndex(result.project).bounds(root);
  for (const original of [f.branch, f.sibling]) { const child = result.project.nodes.find(node => node.id === original.id)!, box = navigationIndex(result.project).bounds(child); assert.ok(box.x >= frame.x + 5); assert.ok(box.y >= frame.y); assert.ok(box.x + box.width <= frame.x + frame.width); assert.ok(box.y + box.height <= frame.y + frame.height); }
  const branch = result.project.nodes.find(node => node.id === f.branch.id)!, child = result.project.nodes.find(node => node.id === f.child.id)!;
  assert.equal(child.x - branch.x, f.child.x - f.branch.x); assert.equal(child.y - branch.y, f.child.y - f.branch.y);
  for (const original of before.nodes.filter(node => ![f.branch.id, f.child.id, f.sibling.id].includes(node.id))) { const node = result.project.nodes.find(node => node.id === original.id)!; assert.equal(node.x, original.x); assert.equal(node.y, original.y); }
  const exact = groupSelection(f.project, [f.branch.id, f.sibling.id], { x: 7, y: 11 });
  assert.equal(exact.project.nodes.find(node => node.id === exact.rootId)?.x, 7); assert.equal(exact.project.nodes.find(node => node.id === exact.rootId)?.y, 11);
  const fixedX = groupSelection(f.project, [f.branch.id, f.sibling.id], { x: f.outside.x });
  assert.equal(fixedX.project.nodes.find(node => node.id === fixedX.rootId)?.x, f.outside.x);
  assert.deepEqual(f.project, before);
});

test('empty, root, missing, cross-canvas, nonfinite and malformed selections fail without mutation', () => {
  const f = fixture(), second = addCanvas(f.project, 'Other'), other = addNode(f.project, second.id, 'text');
  const before = structuredClone(f.project);
  for (const ids of [[], ['missing'], [f.project.canvases[0].rootId], [f.branch.id, other.id]]) {
    assert.throws(() => selectionGraph(f.project, ids)); assert.throws(() => moveSelection(f.project, ids, 1, 2));
    assert.throws(() => deleteSelectionGraph(f.project, ids)); assert.throws(() => groupSelection(f.project, ids));
    assert.throws(() => createSnippet(f.project, ids, { includeDescendants: true, includeExternalReferences: false }));
  }
  assert.throws(() => moveSelection(f.project, [f.branch.id], NaN, 0), /finite/);
  assert.throws(() => groupSelection(f.project, [f.branch.id], { x: Infinity }), /finite/);
  assert.throws(() => createSnippet(f.project, [f.branch.id], {} as never), /explicitly/);
  const malformed = structuredClone(f.project); malformed.nodes[1].width = -1;
  assert.throws(() => deleteSelectionGraph(malformed, [f.branch.id]), /geometry/);
  assert.deepEqual(f.project, before);
});

test('snippet is one fresh canvas with deduplicated descendants, remapped internal references and independent assets', () => {
  const f = fixture(); const reference = addNode(f.project, f.project.entryCanvasId, 'reference', 'Local reference');
  reference.parentId = f.branch.id; reference.targetId = f.child.id; reference.dependencyIds = [f.child.id];
  const asset = { id: crypto.randomUUID(), name: 'evidence.txt', mime: 'text/plain', base64: btoa('Evidence') };
  f.project.assets.push(asset); f.branch.assetId = asset.id;
  const before = structuredClone(f.project);
  const result = createSnippet(f.project, [f.branch.id, f.child.id, f.sibling.id], { includeDescendants: true, includeExternalReferences: false });
  const copy = result.project, map = result.idMap, report = result.report;
  assert.equal(copy.canvases.length, 1); assert.equal(copy.nodes.length, 5); assert.equal(copy.links.length, 2);
  assert.equal(report.selectedNodes, 3); assert.equal(report.copiedNodes, 4); assert.equal(report.includedDescendants, 1); assert.equal(report.crossingLinks, 1);
  const originalIds = new Set([f.project.id, ...f.project.canvases, ...f.project.nodes, ...f.project.links, ...f.project.assets].map(item => typeof item === 'string' ? item : item.id));
  for (const id of [copy.id, ...copy.canvases, ...copy.nodes, ...copy.links, ...copy.assets].map(item => typeof item === 'string' ? item : item.id)) {
    assert.match(id, /^[a-f0-9-]{36}$/); assert.equal(originalIds.has(id), false);
  }
  assert.equal(copy.nodes.find(node => node.id === map[reference.id])?.targetId, map[f.child.id]);
  assert.deepEqual(copy.nodes.find(node => node.id === map[reference.id])?.dependencyIds, [map[f.child.id]]);
  assert.equal(copy.links.find(link => link.id === map[f.internal.id])?.parentId, map[f.branch.id]);
  assert.equal(copy.assets[0].base64, asset.base64); assert.equal(copy.nodes.find(node => node.id === map[f.branch.id])?.assetId, map[asset.id]);
  assert.equal(evaluate(copy).values[map[f.sibling.id]], true);
  assert.deepEqual(validateProject(copy), []); assert.deepEqual(f.project, before);
  copy.nodes[1].title = 'Independent'; assert.equal(f.branch.title, 'Parent');
});

test('selected-only copy reports hidden descendants and lifts disconnected descendants without copying intermediates', () => {
  const f = fixture(); const grandchild = addNode(f.project, f.project.entryCanvasId, 'text', 'Grandchild'); grandchild.parentId = f.child.id;
  const result = createSnippet(f.project, [f.branch.id, grandchild.id], { includeDescendants: false, includeExternalReferences: false });
  assert.equal(result.report.copiedNodes, 2); assert.equal(result.report.omittedDescendants, 1); assert.equal(result.report.omittedHiddenDescendants, 1);
  assert.equal(result.report.crossingLinks, 1); assert.equal(result.project.links.length, 0);
  for (const original of [f.branch, grandchild]) assert.equal(result.project.nodes.find(node => node.id === result.idMap[original.id])?.parentId, result.project.canvases[0].rootId);
  assert.equal(result.project.nodes.some(node => node.title === 'Child'), false);
});

test('copied branch roots retain effective inherited rule intersections when ancestors are omitted', () => {
  const project = newProject('Rule context'), canvas = project.entryCanvasId;
  project.nodes[0].rules = { nodeKinds: ['text', 'container'], linkKinds: ['visual'], operations: ['AND', 'OR'] };
  const parent = addNode(project, canvas, 'text', 'Rules'); parent.rules = { operations: ['AND'] };
  const selected = addNode(project, canvas, 'text', 'Selected'); selected.parentId = parent.id;
  const child = addNode(project, canvas, 'text', 'Nested'); child.parentId = selected.id;
  const result = createSnippet(project, [selected.id], { includeDescendants: true, includeExternalReferences: false });
  assert.deepEqual(result.project.nodes.find(node => node.id === result.idMap[selected.id])?.rules,
    { nodeKinds: ['text', 'container'], linkKinds: ['visual'], operations: ['AND'] });
  assert.deepEqual(validateProject(result.project), []);
});

test('omitted connection owners become the new canvas root and are reported, without importing owner content', () => {
  const f = fixture(); f.computed.parentId = f.outside.id;
  const result = createSnippet(f.project, [f.child.id, f.sibling.id], { includeDescendants: false, includeExternalReferences: false });
  assert.equal(result.report.reassignedLinkOwners, 1);
  assert.equal(result.project.links[0].parentId, result.project.canvases[0].rootId);
  assert.equal(result.project.nodes.some(node => node.title === f.outside.title), false);
});

test('cross-boundary computed inputs stay disconnected and explicitly name their absent source', () => {
  const f = fixture(); f.project = commitSnapshots(f.project);
  const result = createSnippet(f.project, [f.sibling.id], { includeDescendants: false, includeExternalReferences: true });
  const copied = result.project.nodes.find(node => node.id === result.idMap[f.sibling.id])!;
  assert.equal(result.project.links.length, 0); assert.deepEqual(copied.dependencyIds, [f.child.id]);
  assert.equal(evaluate(result.project).values[copied.id], null);
  assert.equal(result.report.externalRequirements.find(item => item.id === f.child.id)?.status, 'snapshot');
  assert.match(result.report.externalRequirements.find(item => item.id === f.child.id)!.note, /wire.*disconnected/);
  assert.equal(result.report.includedSnapshots, 1); assert.equal(result.project.nodes.length, 2);
});

test('external observations are optional and never copy live sources or present snapshots as complete canvases', () => {
  const f = fixture(), remote = addCanvas(f.project, 'External sources'), source = addNode(f.project, remote.id, 'number', 'Original'); source.value = 42;
  const ref = addNode(f.project, f.project.entryCanvasId, 'reference', 'Observation'); ref.targetId = source.id;
  f.project.dependencies.push({ canvasId: remote.id, title: remote.title, required: true }); f.project = commitSnapshots(f.project);
  const before = structuredClone(f.project);
  for (const include of [false, true]) {
    const result = createSnippet(f.project, [ref.id], { includeDescendants: true, includeExternalReferences: include });
    assert.equal(result.project.nodes.length, 2); assert.equal(result.project.canvases.length, 1);
    assert.equal(result.project.nodes.find(node => node.id === result.idMap[ref.id])?.targetId, source.id);
    assert.equal(result.report.includedSnapshots, include ? 1 : 0);
    assert.equal(evaluate(result.project).values[result.idMap[ref.id]], include ? 42 : null);
    assert.equal(result.report.externalRequirements.find(item => item.id === source.id)?.status, include ? 'snapshot' : 'excluded');
    assert.equal(result.report.externalRequirements.find(item => item.id === remote.id)?.status, 'missing');
    assert.ok(createPublicationPayload(result.project).missing.includes(remote.title));
  }
  assert.deepEqual(structuredClone(f.project), before);
});

test('same-canvas outside originals need an explicitly included existing snapshot; no snapshot is fabricated', () => {
  const f = fixture(), ref = addNode(f.project, f.project.entryCanvasId, 'reference', 'Local observation'); ref.targetId = f.child.id;
  const unsaved = createSnippet(f.project, [ref.id], { includeDescendants: false, includeExternalReferences: true });
  assert.equal(unsaved.report.includedSnapshots, 0); assert.equal(evaluate(unsaved.project).values[unsaved.idMap[ref.id]], null);
  assert.equal(unsaved.report.externalRequirements[0].status, 'excluded');
  f.project = commitSnapshots(f.project);
  const saved = createSnippet(f.project, [ref.id], { includeDescendants: false, includeExternalReferences: true });
  assert.equal(saved.report.includedSnapshots, 1); assert.equal(evaluate(saved.project).values[saved.idMap[ref.id]], true);
});

test('deleted local snapshots cannot be resurrected and transitive dependency cycles remain finite', () => {
  const f = fixture(), ref = addNode(f.project, f.project.entryCanvasId, 'reference', 'Observation'); ref.targetId = f.outside.id;
  f.project = commitSnapshots(f.project); f.project.nodes = f.project.nodes.filter(node => node.id !== f.outside.id); f.project.links = f.project.links.filter(link => link.targetId !== f.outside.id);
  const deleted = createSnippet(f.project, [ref.id], { includeDescendants: false, includeExternalReferences: true });
  assert.equal(deleted.report.includedSnapshots, 0); assert.equal(deleted.report.externalRequirements[0].status, 'missing');
  const p = newProject('Dependency cycle'), a = addNode(p, p.entryCanvasId, 'text', 'A'), b = addNode(p, p.entryCanvasId, 'text', 'B'), c = addNode(p, p.entryCanvasId, 'text', 'C');
  a.dependencyIds = [b.id]; b.dependencyIds = [c.id]; c.dependencyIds = [b.id];
  const cyclic = createSnippet(p, [a.id], { includeDescendants: false, includeExternalReferences: true });
  assert.equal(cyclic.report.externalRequirements.length, 2); assert.equal(cyclic.project.nodes.length, 2);
});

test('external snapshot attachments have fresh IDs and missing assets are reported without importing unrelated media', () => {
  const f = fixture(), ref = addNode(f.project, f.project.entryCanvasId, 'reference', 'Picture reference'); ref.targetId = f.outside.id;
  const used = { id: crypto.randomUUID(), name: 'diagram.txt', mime: 'text/plain', base64: btoa('Diagram') };
  const unrelated = { id: crypto.randomUUID(), name: 'private.txt', mime: 'text/plain', base64: btoa('Unselected') };
  f.project.assets.push(used, unrelated); f.outside.assetId = used.id; f.project = commitSnapshots(f.project);
  const result = createSnippet(f.project, [ref.id], { includeDescendants: false, includeExternalReferences: true });
  assert.equal(result.project.assets.length, 0); assert.equal(result.project.snapshots[f.outside.id].asset?.base64, used.base64);
  assert.notEqual(result.project.snapshots[f.outside.id].asset?.id, used.id);
  assert.equal(JSON.stringify(result.project).includes(unrelated.base64), false);
  const missing = addNode(f.project, f.project.entryCanvasId, 'image', 'Missing picture'); missing.assetId = 'missing-asset';
  const absent = createSnippet(f.project, [missing.id], { includeDescendants: false, includeExternalReferences: false });
  assert.deepEqual(absent.report.missingAssets, ['missing-asset']);
});

test('grouped collapsed selection and snippet preserve owned-link disclosure and survive serialization/publication', () => {
  const f = fixture(), grouped = groupSelection(f.project, [f.branch.id, f.sibling.id]);
  const root = grouped.project.nodes.find(node => node.id === grouped.rootId)!; root.collapsed = true;
  const snippet = createSnippet(grouped.project, [root.id], { includeDescendants: true, includeExternalReferences: false });
  const reopened: MeridianProject = JSON.parse(JSON.stringify(createPublicationPayload(snippet.project).project));
  const nav = navigationIndex(reopened); assert.equal(nav.isNodeVisible(snippet.idMap[f.child.id]), false);
  assert.equal(nav.isLinkVisible(reopened.links.find(link => link.id === snippet.idMap[f.internal.id])!), false);
  assert.equal(evaluate(reopened).values[snippet.idMap[f.sibling.id]], true);
  assert.deepEqual(validateProject(reopened), []);
});

test('numeric boundary snapshots do not reconnect excluded wires or turn an undefined output into a saved value', () => {
  let project = newProject('Numeric boundary');
  const input = addNode(project, project.entryCanvasId, 'number', 'Quantity'); input.value = 42;
  const output = addNode(project, project.entryCanvasId, 'output', 'Result'); output.valueType = 'number';
  project.links.push({ id: crypto.randomUUID(), canvasId: project.entryCanvasId, sourceId: input.id, targetId: output.id, kind: 'numeric', relation: 'causal' });
  project = commitSnapshots(project);
  for (const include of [false, true]) {
    const result = createSnippet(project, [output.id], { includeDescendants: false, includeExternalReferences: include });
    assert.deepEqual(result.project.nodes.find(node => node.id === result.idMap[output.id])?.dependencyIds, [input.id]);
    assert.equal(evaluate(result.project).values[result.idMap[output.id]], null);
    assert.equal(result.project.links.length, 0);
    assert.equal(result.report.externalRequirements[0].status, include ? 'snapshot' : 'excluded');
    assert.match(result.report.externalRequirements[0].note, /wire.*disconnected/);
    if (include) assert.equal(result.project.snapshots[input.id].value, 42);
  }
});

test('selected grandparent and grandchild retain their respective inherited rules when the middle parent is omitted', () => {
  const project = newProject('Omitted context'), canvas = project.entryCanvasId;
  const grandparent = addNode(project, canvas, 'text', 'Grandparent'); grandparent.rules = { operations: ['AND', 'OR'] };
  const parent = addNode(project, canvas, 'text', 'Parent'); parent.parentId = grandparent.id; parent.rules = { operations: ['AND'], linkKinds: ['visual'] };
  const grandchild = addNode(project, canvas, 'text', 'Grandchild'); grandchild.parentId = parent.id;
  const result = createSnippet(project, [grandparent.id, grandchild.id], { includeDescendants: false, includeExternalReferences: false });
  const copiedGrandparent = result.project.nodes.find(node => node.id === result.idMap[grandparent.id])!;
  const copiedGrandchild = result.project.nodes.find(node => node.id === result.idMap[grandchild.id])!;
  assert.equal(copiedGrandparent.parentId, result.project.canvases[0].rootId);
  assert.equal(copiedGrandchild.parentId, result.project.canvases[0].rootId);
  assert.deepEqual(copiedGrandparent.rules, { operations: ['AND', 'OR'] });
  assert.deepEqual(copiedGrandchild.rules, { linkKinds: ['visual'], operations: ['AND'] });
  assert.equal(result.project.nodes.some(node => node.title === 'Parent'), false);
});

test('external saved observations retain their recorded dependency context when the live original has unsaved changes', () => {
  let project = newProject('Pinned requirements');
  const remote = addCanvas(project, 'Original context');
  const savedRequirement = addNode(project, remote.id, 'text', 'Saved requirement');
  const replacement = addNode(project, remote.id, 'text', 'Unsaved replacement');
  const original = addNode(project, remote.id, 'text', 'Original'); original.dependencyIds = [savedRequirement.id];
  const reference = addNode(project, project.entryCanvasId, 'reference', 'Observation'); reference.targetId = original.id;
  project = commitSnapshots(project); project.nodes.find(node => node.id === original.id)!.dependencyIds = [replacement.id];
  const result = createSnippet(project, [reference.id], { includeDescendants: false, includeExternalReferences: true });
  assert.ok(result.report.externalRequirements.some(item => item.id === savedRequirement.id));
  assert.equal(result.report.externalRequirements.some(item => item.id === replacement.id), false);
  assert.ok(result.project.snapshots[savedRequirement.id]);
  assert.deepEqual(result.project.snapshots[original.id].node.dependencyIds, [savedRequirement.id]);
  assert.equal(result.project.nodes.length, 2);
});

test('an independent selected copy does not silently satisfy an external observation dependency on its original identity', () => {
  let project = newProject('Original versus independent copy');
  const input = addNode(project, project.entryCanvasId, 'number', 'Independent quantity'); input.value = 3;
  const remote = addCanvas(project, 'Saved context'), source = addNode(project, remote.id, 'text', 'Observed source'); source.dependencyIds = [input.id];
  const ref = addNode(project, project.entryCanvasId, 'reference', 'Observation'); ref.targetId = source.id;
  project = commitSnapshots(project); project.nodes.find(node => node.id === input.id)!.value = 9;
  const result = createSnippet(project, [input.id, ref.id], { includeDescendants: false, includeExternalReferences: true });
  assert.equal(result.project.nodes.find(node => node.id === result.idMap[input.id])?.value, 9);
  assert.equal(result.project.snapshots[input.id].value, 3);
  assert.deepEqual(result.project.snapshots[source.id].node.dependencyIds, [input.id]);
  assert.ok(result.report.externalRequirements.some(item => item.id === input.id && item.status === 'snapshot'));
  assert.equal(result.project.nodes.length, 3); assert.equal(result.report.includedSnapshots, 2);
  assert.equal(createPublicationPayload(result.project).missing.length, 0);
});
