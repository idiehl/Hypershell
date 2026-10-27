import test from 'node:test';
import assert from 'node:assert/strict';
import { arrangeSelection, type ArrangementAction } from '../src/core/arrangement.ts';
import { addCanvas, addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import { navigationIndex } from '../src/core/navigation.ts';
import { evaluate } from '../src/core/engine.ts';
import { validateProject } from '../src/core/rules.ts';
import { setSnapLock } from '../src/core/clusters.ts';
import type { MeridianLink, MeridianNode, MeridianProject } from '../src/core/types.ts';

function fixture() {
  const project = newProject('Arrangement'), canvasId = project.entryCanvasId;
  const a = addNode(project, canvasId, 'text', 'A'), b = addNode(project, canvasId, 'text', 'B'), c = addNode(project, canvasId, 'text', 'C');
  Object.assign(a, { sizeMode: 'fixed', x: -60, y: 40, width: 100, height: 80, placement: 'near' });
  Object.assign(b, { sizeMode: 'fixed', x: 140, y: -20, width: 180, height: 140, placement: 'edge', attachSide: 'left' });
  Object.assign(c, { sizeMode: 'fixed', x: 500, y: 220, width: 80, height: 60 });
  return { project, canvasId, a, b, c, ids: [a.id, b.id, c.id] };
}
const node = (project: MeridianProject, id: string): MeridianNode => project.nodes.find(item => item.id === id)!;
function link(project: MeridianProject, source: MeridianNode, target: MeridianNode, point = { x: 320, y: 160 }): MeridianLink {
  const wire: MeridianLink = { id: crypto.randomUUID(), canvasId: source.canvasId, sourceId: source.id, targetId: target.id,
    kind: 'visual', relation: 'generic', sourceAnchor: { x: 1, y: 0.5 }, targetAnchor: { x: 0, y: 0.5 }, points: [point] };
  project.links.push(wire); return wire;
}

test('six alignments use selected tile edges or common overall center, preserving every size and orthogonal coordinate', () => {
  const cases: Array<{ action: ArrangementAction; x?: number[]; y?: number[] }> = [
    { action: 'align-left', x: [-60, -60, -60] },
    { action: 'align-center-x', x: [210, 170, 220] },
    { action: 'align-right', x: [480, 400, 500] },
    { action: 'align-top', y: [-20, -20, -20] },
    { action: 'align-center-y', y: [90, 60, 100] },
    { action: 'align-bottom', y: [200, 140, 220] },
  ];
  for (const scenario of cases) {
    const f = fixture(), before = structuredClone(f.project);
    const result = arrangeSelection(f.project, [f.c.id, f.a.id, f.b.id], scenario.action);
    for (const [index, original] of [f.a, f.b, f.c].entries()) {
      const arranged = node(result.project, original.id);
      assert.equal(arranged.x, scenario.x?.[index] ?? original.x, scenario.action);
      assert.equal(arranged.y, scenario.y?.[index] ?? original.y, scenario.action);
      assert.equal(arranged.width, original.width); assert.equal(arranged.height, original.height);
      assert.equal(arranged.parentId, original.parentId); assert.equal(arranged.attachSide, original.attachSide);
    }
    const movedIds = [f.a, f.b, f.c].filter(original => node(result.project, original.id).x !== original.x || node(result.project, original.id).y !== original.y).map(original => original.id);
    assert.deepEqual(result.movedRootIds, movedIds); assert.deepEqual(validateProject(result.project), []);
    assert.deepEqual(f.project, before);
  }
});

test('horizontal spacing creates equal edge gaps for different widths with outer tiles fixed, regardless of click order', () => {
  const f = fixture(); const result = arrangeSelection(f.project, [f.c.id, f.a.id, f.b.id], 'space-horizontal');
  assert.equal(node(result.project, f.a.id).x, -60); assert.equal(node(result.project, f.c.id).x, 500);
  const b = node(result.project, f.b.id);
  assert.equal(b.x, 180); assert.equal(b.y, f.b.y);
  assert.equal(b.x - (f.a.x + f.a.width), f.c.x - (b.x + b.width));
  assert.deepEqual(result.movedRootIds, [f.b.id]);
  assert.equal(node(result.project, f.a.id).placement, 'near'); assert.equal(node(result.project, f.b.id).placement, 'free');
});

test('vertical spacing orders tiles by top position and uses actual heights while holding the top and bottom fixed', () => {
  const f = fixture(); const result = arrangeSelection(f.project, f.ids, 'space-vertical');
  const a = node(result.project, f.a.id), b = node(result.project, f.b.id), c = node(result.project, f.c.id);
  assert.equal(b.y, -20); assert.equal(c.y, 220); assert.equal(a.y, 130);
  assert.equal(a.y - (b.y + b.height), c.y - (a.y + a.height));
  assert.equal(a.x, f.a.x); assert.deepEqual(result.movedRootIds, [f.a.id]);
});

test('a collapsed contained tile aligns and spaces by its independent closed frame, retaining its saved expanded frame', () => {
  const f = fixture(); Object.assign(f.b, { kind: 'container', x: 170, y: 10, width: 28, height: 29, collapsed: true, childLayout: 'contained', containerSizes: { expanded: { width: 900, height: 600, sizeMode: 'fixed' }, collapsed: { width: 28, height: 29, sizeMode: 'fixed' } } });
  const child = addNode(f.project, f.canvasId, 'text', 'Hidden contents'); child.parentId = f.b.id;
  const right = arrangeSelection(f.project, f.ids, 'align-right'), bottom = arrangeSelection(f.project, f.ids, 'align-bottom');
  assert.equal(node(right.project, f.b.id).x, 552); assert.equal(node(bottom.project, f.b.id).y, 251);
  const spaced = arrangeSelection(f.project, f.ids, 'space-horizontal');
  assert.equal(node(spaced.project, f.b.id).x, 256);
  assert.equal(node(spaced.project, f.b.id).width, 28); assert.equal(node(spaced.project, f.b.id).height, 29);
  assert.deepEqual(node(spaced.project, f.b.id).containerSizes?.expanded, { width: 900, height: 600, sizeMode: 'fixed' });
  assert.equal(node(spaced.project, f.b.id).collapsed, true);
  assert.deepEqual(navigationIndex(spaced.project).bounds(node(spaced.project, f.b.id)), { id: f.b.id, x: 256, y: 10, width: 28, height: 29 });
});

test('selected parents and descendants normalize to one rigid branch, including hidden grandchildren and child placement', () => {
  const f = fixture(); Object.assign(f.b, { collapsed: true, childLayout: 'spatial' });
  const child = addNode(f.project, f.canvasId, 'text', 'Hidden child'); Object.assign(child, { parentId: f.b.id, x: 290, y: 75, placement: 'edge', collapsed: true });
  const grandchild = addNode(f.project, f.canvasId, 'image', 'Hidden grandchild'); Object.assign(grandchild, { parentId: child.id, x: 850, y: 500, placement: 'near' });
  const before = structuredClone(f.project), result = arrangeSelection(f.project, [grandchild.id, f.b.id, child.id, f.a.id, f.b.id], 'align-left');
  assert.deepEqual(result.movedRootIds, [f.b.id]);
  for (const original of [f.b, child, grandchild]) {
    const moved = node(result.project, original.id);
    assert.equal(moved.x, original.x - 200); assert.equal(moved.y, original.y);
    assert.equal(moved.parentId, original.parentId); assert.equal(moved.collapsed, original.collapsed);
    assert.equal(moved.placement, original === f.b ? 'free' : original.placement);
  }
  assert.equal(navigationIndex(result.project).isNodeVisible(child.id), false);
  assert.equal(navigationIndex(result.project).isNodeVisible(grandchild.id), false);
  assert.deepEqual(node(result.project, f.c.id), f.c); assert.deepEqual(f.project, before);
});

test('aligning one Cluster member carries transitive locked peers, hidden children and their internal route bends', () => {
  const f = fixture(), peer = addNode(f.project, f.canvasId, 'text', 'Transitive peer');
  for (const [item, x, y] of [[f.a, 0, 100], [f.b, 100, 100], [peer, 200, 100], [f.c, 500, 0]] as const) {
    Object.assign(item, { x, y, width: 100, height: 100, style: { shape: 'rectangle' }, sizeMode: 'fixed' });
  }
  peer.collapsed = true;
  const child = addNode(f.project, f.canvasId, 'text', 'Hidden child');
  Object.assign(child, { parentId: peer.id, x: 260, y: 250 });
  const internal = link(f.project, f.a, peer, { x: 150, y: 300 });
  const locked = setSnapLock(setSnapLock(f.project, f.a.id, f.b.id, true), f.b.id, peer.id, true), before = structuredClone(locked);
  const result = arrangeSelection(locked, [f.a.id, f.c.id], 'align-top');
  for (const item of [f.a, f.b, peer, child]) {
    assert.equal(node(result.project, item.id).x, item.x);
    assert.equal(node(result.project, item.id).y, item.y - 100);
  }
  assert.deepEqual(result.project.links.find(item => item.id === internal.id)!.points, [{ x: 150, y: 200 }]);
  assert.deepEqual(result.project.links.filter(item => item.snap), before.links.filter(item => item.snap));
  assert.deepEqual(result.movedRootIds, [f.a.id, f.b.id, peer.id]);
  assert.equal(navigationIndex(result.project).isNodeVisible(child.id), false);
  assert.deepEqual(validateProject(result.project), []); assert.deepEqual(locked, before);
});

test('selected locked members can align together when each requested branch has the same movement', () => {
  const f = fixture();
  for (const [item, x, y] of [[f.a, 0, 100], [f.b, 100, 100], [f.c, 500, 0]] as const) {
    Object.assign(item, { x, y, width: 100, height: 100, style: { shape: 'rectangle' } });
  }
  const locked = setSnapLock(f.project, f.a.id, f.b.id, true);
  const result = arrangeSelection(locked, f.ids, 'align-top');
  assert.equal(node(result.project, f.a.id).y, 0); assert.equal(node(result.project, f.b.id).y, 0);
  assert.equal(node(result.project, f.b.id).x - node(result.project, f.a.id).x, 100);
  assert.deepEqual(result.movedRootIds, [f.a.id, f.b.id]); assert.deepEqual(validateProject(result.project), []);
});

test('conflicting Cluster alignment and spacing against a stationary locked anchor fail atomically with an unlock message', () => {
  const f = fixture();
  for (const [item, x] of [[f.a, 0], [f.b, 100], [f.c, 500]] as const) {
    Object.assign(item, { x, y: 100, width: 100, height: 100, style: { shape: 'rectangle' } });
  }
  const locked = setSnapLock(f.project, f.a.id, f.b.id, true), before = structuredClone(locked);
  assert.throws(() => arrangeSelection(locked, [f.a.id, f.b.id], 'align-left'), /locked Cluster.*Unlock their edge/);
  assert.deepEqual(locked, before);
  assert.throws(() => arrangeSelection(locked, f.ids, 'space-horizontal'), /locked Cluster.*Unlock their edge/);
  assert.deepEqual(locked, before);
});

test('internal bends and equal-delta cross-branch bends travel together; differential and boundary routes retain world positions', () => {
  const f = fixture(); f.c.x = f.b.x;
  const child = addNode(f.project, f.canvasId, 'text', 'Child'); Object.assign(child, { parentId: f.b.id, x: 240, y: 180 });
  const different = addNode(f.project, f.canvasId, 'text', 'Different movement'); different.x = 440;
  const outside = addNode(f.project, f.canvasId, 'text', 'Outside'); outside.x = 1000;
  const internal = link(f.project, f.b, child), equal = link(f.project, child, f.c), differential = link(f.project, f.b, different), boundary = link(f.project, f.c, outside);
  const before = structuredClone(f.project);
  const result = arrangeSelection(f.project, [...f.ids, different.id], 'align-left');
  for (const wire of [internal, equal]) {
    const moved = result.project.links.find(item => item.id === wire.id)!;
    assert.deepEqual(moved.points, [{ x: 120, y: 160 }]);
    assert.deepEqual(moved.sourceAnchor, wire.sourceAnchor); assert.deepEqual(moved.targetAnchor, wire.targetAnchor);
  }
  for (const wire of [differential, boundary]) assert.deepEqual(result.project.links.find(item => item.id === wire.id), wire);
  assert.deepEqual(f.project, before);
});

test('computed values, inherited rules, snapshots, assets and unrelated canvas content survive arrangement unchanged', () => {
  const f = fixture(); f.project.nodes[0].rules = { nodeKinds: ['text', 'number', 'output'], linkKinds: ['visual', 'numeric'] };
  const input = addNode(f.project, f.canvasId, 'number', 'Value'); Object.assign(input, { x: 900, value: 23 });
  const output = addNode(f.project, f.canvasId, 'output', 'Result'); Object.assign(output, { x: 1100, valueType: 'number', dependencyIds: [input.id] });
  f.project.links.push({ id: crypto.randomUUID(), canvasId: f.canvasId, sourceId: input.id, targetId: output.id, kind: 'numeric', relation: 'causal', points: [{ x: 1060, y: 110 }] });
  const second = addCanvas(f.project, 'Unrelated canvas'), elsewhere = addNode(f.project, second.id, 'document', 'Unrelated document');
  const asset = { id: crypto.randomUUID(), name: 'source.txt', mime: 'text/plain', base64: btoa('Evidence remains intact') };
  f.project.assets.push(asset); elsewhere.assetId = asset.id;
  f.project.dependencies.push({ canvasId: 'unavailable-source', title: 'Required source', required: true });
  f.project = commitSnapshots(f.project);
  const before = structuredClone(f.project), result = arrangeSelection(f.project, [input.id, output.id], 'align-left');
  assert.deepEqual(evaluate(result.project), evaluate(before));
  assert.equal(evaluate(result.project).values[output.id], 23);
  assert.deepEqual(result.project.snapshots, before.snapshots); assert.deepEqual(result.project.assets, before.assets);
  assert.deepEqual(result.project.dependencies, before.dependencies); assert.deepEqual(result.project.canvases, before.canvases);
  assert.deepEqual(result.project.nodes.filter(item => ![input.id, output.id].includes(item.id)), before.nodes.filter(item => ![input.id, output.id].includes(item.id)));
  assert.deepEqual(validateProject(result.project), []); assert.deepEqual(structuredClone(f.project), before);
  node(result.project, input.id).title = 'Independent changed draft'; assert.equal(node(f.project, input.id).title, 'Value');
  result.project.snapshots[input.id].node.title = 'Independent snapshot'; assert.equal(f.project.snapshots[input.id].node.title, 'Value');
});

test('already aligned or evenly spaced selections are true no-ops and retain authored placement modes', () => {
  const f = fixture(); f.b.x = f.a.x; f.c.x = f.a.x;
  const aligned = arrangeSelection(f.project, f.ids, 'align-left');
  assert.deepEqual(aligned.movedRootIds, []); assert.equal(aligned.project, f.project);
  assert.equal(f.b.placement, 'edge'); assert.equal(f.a.placement, 'near');
  f.b.x = 180; f.c.x = 500;
  const spaced = arrangeSelection(f.project, f.ids, 'space-horizontal');
  assert.deepEqual(spaced.movedRootIds, []); assert.equal(spaced.project, f.project); assert.equal(f.b.placement, 'edge');
});

test('zero edge gaps are allowed but insufficient horizontal or vertical space fails atomically', () => {
  const f = fixture(); Object.assign(f.a, { x: 0, y: 0 }); Object.assign(f.b, { x: 20, y: 10 }); Object.assign(f.c, { x: 280, y: 220 });
  const result = arrangeSelection(f.project, f.ids, 'space-horizontal');
  assert.equal(node(result.project, f.b.id).x, 100);
  const before = structuredClone(f.project); f.c.x = 279;
  const short = structuredClone(f.project);
  assert.throws(() => arrangeSelection(f.project, f.ids, 'space-horizontal'), /not enough room.*overlap/); assert.deepEqual(f.project, short);
  f.c.x = before.nodes.find(item => item.id === f.c.id)!.x; f.c.y = 219;
  const vertical = structuredClone(f.project);
  assert.throws(() => arrangeSelection(f.project, f.ids, 'space-vertical'), /not enough room.*overlap/); assert.deepEqual(f.project, vertical);
});

test('fractional tight spacing writes the planned root coordinate without a delta round-trip that introduces overlap', () => {
  const f = fixture();
  Object.assign(f.a, { x: 0, width: 337.667775958724 });
  Object.assign(f.b, { x: 9.268844425894116, width: 248.40015304100785 });
  Object.assign(f.c, { x: 586.0679289997319, width: 414.87914274283816 });
  const result = arrangeSelection(f.project, f.ids, 'space-horizontal'), b = node(result.project, f.b.id);
  assert.equal(b.x, f.a.width); assert.equal(b.x + b.width, f.c.x);
  assert.deepEqual(result.movedRootIds, [f.b.id]);
});

test('alignment and spacing require independent roots rather than counting nested selections more than once', () => {
  const f = fixture(), child = addNode(f.project, f.canvasId, 'text', 'Nested'); child.parentId = f.b.id;
  assert.throws(() => arrangeSelection(f.project, [f.b.id, child.id, f.b.id], 'align-left'), /at least two independent/);
  assert.throws(() => arrangeSelection(f.project, [f.a.id, f.b.id, child.id], 'space-horizontal'), /at least three independent/);
});

test('invalid actions and malformed, empty, canvas-root, missing and cross-canvas selections fail before changing content', () => {
  const f = fixture(), second = addCanvas(f.project, 'Other'), other = addNode(f.project, second.id, 'text');
  const before = structuredClone(f.project);
  assert.throws(() => arrangeSelection(f.project, f.ids, 'scatter' as ArrangementAction), /supported arrangement/);
  for (const ids of [[], ['missing'], [f.project.canvases[0].rootId, f.a.id], [other.id, f.a.id]]) assert.throws(() => arrangeSelection(f.project, ids, 'align-left'));
  const malformed = structuredClone(f.project); malformed.nodes[1].x = NaN;
  assert.throws(() => arrangeSelection(malformed, f.ids, 'align-left'), /malformed|finite/);
  assert.deepEqual(f.project, before);
});

test('an invalid inherited rule is rejected atomically even for an apparent no-op', () => {
  const f = fixture(); f.project.nodes[0].rules = { nodeKinds: ['input'] };
  const before = structuredClone(f.project);
  assert.throws(() => arrangeSelection(f.project, f.ids, 'align-left'), /inherited rule/); assert.deepEqual(f.project, before);
});

test('overflow in selection extents, translated descendants and carried route bends fails atomically', () => {
  const f = fixture(); f.a.x = 1.5e308; f.b.x = -1.5e308;
  let before = structuredClone(f.project);
  assert.throws(() => arrangeSelection(f.project, [f.a.id, f.b.id], 'align-center-x'), /finite/); assert.deepEqual(f.project, before);
  // Orthogonal alignment does not require a finite horizontal span.
  assert.doesNotThrow(() => arrangeSelection(f.project, [f.a.id, f.b.id], 'align-top'));
  f.a.x = 0; f.b.x = 1e308;
  const child = addNode(f.project, f.canvasId, 'text', 'Far-away hidden child'); Object.assign(child, { parentId: f.a.id, x: 1.5e308 });
  before = structuredClone(f.project);
  assert.doesNotThrow(() => arrangeSelection(f.project, [f.a.id, f.b.id], 'align-left')); // A and its child stay in place; B moves independently.
  // Aligning A to the right moves its child beyond the finite world coordinate range.
  assert.throws(() => arrangeSelection(f.project, [f.a.id, f.b.id], 'align-right'), /finite/); assert.deepEqual(f.project, before);
  child.x = 0;
  const wire = link(f.project, f.a, child, { x: 1.5e308, y: 1 });
  before = structuredClone(f.project);
  assert.throws(() => arrangeSelection(f.project, [f.a.id, f.b.id], 'align-right'), /finite/); assert.deepEqual(f.project, before);
  assert.deepEqual(wire.points, [{ x: 1.5e308, y: 1 }]);
});
