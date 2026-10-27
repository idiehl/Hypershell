import test from 'node:test';
import assert from 'node:assert/strict';
import type { Gate, MeridianNode as Node, MeridianProject as Project } from '../src/core/types.ts';
import { addCanvas, addNode, cloneProject, commitSnapshots, forkProject, newProject } from '../src/core/model.ts';
import { evaluate } from '../src/core/engine.ts';
import { isProjectShape, reparentNode, validateProject } from '../src/core/rules.ts';
import { createDemoProject } from '../src/core/demo.ts';

function wire(project: Project, source: Node, target: Node, targetPort = 0): void {
  project.links.push({ id: crypto.randomUUID(), canvasId: source.canvasId, sourceId: source.id, targetId: target.id, kind: 'functional', relation: 'causal', targetPort });
}

function circuit(operation: Gate = 'AND'): { project: Project; a: Node; b: Node; joint: Node; output: Node } {
  const project = newProject();
  const canvas = project.entryCanvasId;
  const a = addNode(project, canvas, 'input'), b = addNode(project, canvas, 'input');
  const joint = addNode(project, canvas, 'joint'), output = addNode(project, canvas, 'output');
  joint.operation = operation;
  wire(project, a, joint, 0);
  if (operation !== 'NOT') wire(project, b, joint, 1);
  wire(project, joint, output);
  return { project, a, b, joint, output };
}

test('project creation, canvas roots and clone independence', () => {
  const project = newProject('Research');
  assert.equal(project.revision, 0);
  assert.equal(project.canvases.length, 1);
  const nested = addCanvas(project, 'Second canvas');
  assert.equal(project.nodes.find(node => node.id === nested.rootId)?.parentId, null);
  assert.deepEqual(validateProject(project), []);
  const clone = cloneProject(project); clone.nodes[0]!.title = 'Changed';
  assert.notEqual(clone.nodes[0]!.title, project.nodes[0]!.title);
});

test('all seven Boolean gates follow complete truth tables and react to changed operands', () => {
  const expected: Record<Gate, boolean[]> = {
    AND: [false, false, false, true], OR: [false, true, true, true], NOT: [true, true, false, false],
    NAND: [true, true, true, false], NOR: [true, false, false, false], XOR: [false, true, true, false], XNOR: [true, false, false, true],
  };
  for (const operation of Object.keys(expected) as Gate[]) {
    const { project, a, b, output } = circuit(operation);
    for (let index = 0; index < 4; index++) {
      a.value = index >= 2; b.value = index % 2 === 1;
      assert.equal(evaluate(project).values[output.id], expected[operation][index], `${operation} row ${index}`);
    }
  }
});

test('opening a switch clears dependent outputs, preserves unrelated circuits, and collapse does not stop logic', () => {
  const { project, a, b, joint, output } = circuit(); a.value = true; b.value = true;
  const toggle = addNode(project, project.entryCanvasId, 'switch');
  project.links.find(link => link.targetId === joint.id && link.targetPort === 1)!.sourceId = toggle.id;
  wire(project, b, toggle);
  assert.equal(evaluate(project).values[output.id], true);
  toggle.closed = false;
  assert.equal(evaluate(project).values[output.id], null);
  assert.equal(evaluate(project).values[a.id], true);
  toggle.closed = true; project.nodes[0]!.collapsed = true;
  assert.equal(evaluate(project).values[output.id], true);
});

test('incomplete and invalid circuits remain undefined rather than false or stale', () => {
  const { project, a, b, joint, output } = circuit('OR'); a.value = true; b.value = true;
  assert.equal(evaluate(project).values[output.id], true);
  project.links = project.links.filter(link => link.sourceId !== b.id);
  assert.equal(evaluate(project).values[output.id], null);
  assert.deepEqual(validateProject(project), []);
  joint.operation = 'BOGUS' as Gate;
  assert.match(evaluate(project).errors[joint.id]!, /operation/i);
  assert.equal(evaluate(project).values[output.id], null);
});

test('local references are live while other canvases consume saved values until the next save', () => {
  let project = newProject();
  const source = addNode(project, project.entryCanvasId, 'input'); source.value = true;
  const local = addNode(project, project.entryCanvasId, 'reference'); local.targetId = source.id;
  const other = addCanvas(project, 'Other');
  const remote = addNode(project, other.id, 'reference'); remote.targetId = source.id;
  assert.equal(evaluate(project).values[local.id], true);
  assert.equal(evaluate(project).values[remote.id], null);
  project.revision = 12; project = commitSnapshots(project);
  project.nodes.find(node => node.id === source.id)!.value = false;
  const values = evaluate(project).values;
  assert.equal(values[local.id], false);
  assert.equal(values[remote.id], true);
  const saved = commitSnapshots(project);
  assert.equal(saved.revision, 12);
  assert.equal(evaluate(saved).values[remote.id], false);
  assert.equal(project.snapshots[source.id]!.value, true);
});

test('deleting a source clears local references immediately and other-canvas references only after saving', () => {
  let project = newProject();
  const source = addNode(project, project.entryCanvasId, 'input'); source.value = true;
  const local = addNode(project, project.entryCanvasId, 'reference'); local.targetId = source.id;
  const other = addCanvas(project, 'Other');
  const remote = addNode(project, other.id, 'reference'); remote.targetId = source.id;
  project = commitSnapshots(project);
  project.nodes = project.nodes.filter(node => node.id !== source.id);
  assert.equal(evaluate(project).values[local.id], null);
  assert.equal(evaluate(project).values[remote.id], true);
  const fork = forkProject(project);
  assert.equal(evaluate(fork).values[fork.nodes.find(node => node.title === remote.title && node.canvasId !== fork.entryCanvasId)!.id], true);
  const saved = commitSnapshots(project);
  assert.equal(evaluate(saved).values[local.id], null);
  assert.equal(evaluate(saved).values[remote.id], null);
  assert.equal(Object.hasOwn(saved.snapshots, source.id), false);
});

test('a packaged external snapshot resolves without the original canvas or asset', () => {
  const project = newProject();
  const reference = addNode(project, project.entryCanvasId, 'reference'); reference.targetId = 'external-input';
  project.snapshots['external-input'] = {
    node: { id: 'external-input', canvasId: 'external-canvas', parentId: 'external-root', kind: 'input', title: 'Saved', x: 0, y: 0, width: 120, height: 80, value: true },
    value: true, savedAt: '2026-10-04T00:00:00Z',
  };
  assert.equal(evaluate(project).values[reference.id], true);
  assert.equal(evaluate(commitSnapshots(project)).values[reference.id], true);
  project.snapshots['external-input']!.node.id = 'different-node';
  assert.ok(validateProject(project).some(issue => /snapshot identity/i.test(issue.message)));
  assert.equal(evaluate(project).values[reference.id], null);
});

test('functional feedback, including feedback through a local reference, is rejected; visual cycles are allowed', () => {
  const { project, a, joint, output } = circuit();
  const edge = project.links.find(link => link.sourceId === a.id)!;
  edge.sourceId = output.id;
  assert.ok(validateProject(project).some(issue => /feedback/i.test(issue.message)));
  assert.equal(evaluate(project).values[output.id], null);
  const reference = addNode(project, project.entryCanvasId, 'reference'); reference.targetId = output.id;
  edge.sourceId = reference.id;
  assert.ok(validateProject(project).some(issue => /feedback/i.test(issue.message)));
  edge.sourceId = a.id;
  project.links.push({ id: crypto.randomUUID(), canvasId: a.canvasId, sourceId: output.id, targetId: joint.id, kind: 'visual', relation: 'cyclical' });
  assert.deepEqual(validateProject(project), []);
});

test('cross-canvas functional edges and duplicate input ports fail validation and clear runtime values', () => {
  const { project, a, joint, output } = circuit();
  wire(project, a, joint, 0);
  assert.ok(validateProject(project).some(issue => /one connection/i.test(issue.message)));
  assert.equal(evaluate(project).values[output.id], null);
  project.links.pop();
  const other = addCanvas(project, 'Other');
  const foreign = addNode(project, other.id, 'input'); foreign.value = true;
  project.links.find(link => link.targetId === joint.id && link.targetPort === 0)!.sourceId = foreign.id;
  assert.ok(validateProject(project).some(issue => /within one Work/i.test(issue.message)));
  assert.equal(evaluate(project).values[output.id], null);
});

test('inherited rules cannot be widened; reparenting is atomic on rule or containment conflict', () => {
  const project = newProject();
  const parent = addNode(project, project.entryCanvasId, 'container'); parent.rules = { nodeKinds: ['container', 'text'], linkKinds: ['visual'], operations: ['AND'] };
  const child = addNode(project, project.entryCanvasId, 'container'); child.parentId = parent.id;
  child.rules = { operations: ['AND', 'OR'] };
  assert.ok(validateProject(project).some(issue => /cannot widen/i.test(issue.message)));
  child.rules = { operations: ['AND'] };
  const input = addNode(project, project.entryCanvasId, 'input');
  const oldParent = input.parentId;
  assert.throws(() => reparentNode(project, input.id, child.id), /inherited rule/);
  assert.equal(input.parentId, oldParent);
  assert.throws(() => reparentNode(project, parent.id, child.id), /cycle/);
  assert.equal(parent.parentId, project.canvases[0]!.rootId);
  assert.deepEqual(validateProject(project), []);
});

test('forks remap all included identities, internal links, canvas launches and saved asset snapshots', () => {
  const original = createDemoProject(); original.revision = 9;
  const fork = forkProject(original);
  assert.equal(fork.revision, 0);
  const before = new Set([original.id, ...original.canvases.map(item => item.id), ...original.nodes.map(item => item.id), ...original.links.map(item => item.id), ...original.assets.map(item => item.id)]);
  const after = [fork.id, ...fork.canvases.map(item => item.id), ...fork.nodes.map(item => item.id), ...fork.links.map(item => item.id), ...fork.assets.map(item => item.id)];
  assert.ok(after.every(id => !before.has(id)));
  assert.deepEqual(validateProject(fork), []);
  for (const launch of fork.nodes.filter(node => node.kind === 'canvas-link')) assert.ok(fork.canvases.some(canvas => canvas.rootId === launch.targetId));
  for (const [key, snapshot] of Object.entries(fork.snapshots)) {
    assert.equal(snapshot.node.id, key);
    assert.ok(!before.has(key));
    if (snapshot.asset) assert.ok(fork.assets.some(asset => asset.id === snapshot.asset!.id));
  }
  assert.deepEqual(fork.assets.map(asset => asset.base64), original.assets.map(asset => asset.base64));
  assert.equal(evaluate(fork).values[fork.nodes.find(node => node.kind === 'output')!.id], true);
});

test('demo round trips through JSON with working logic and lossless PNG and TXT assets', () => {
  const project: Project = JSON.parse(JSON.stringify(createDemoProject()));
  assert.equal(project.canvases.length, 2);
  assert.deepEqual(validateProject(project), []);
  assert.equal(evaluate(project).values[project.nodes.find(node => node.kind === 'output')!.id], true);
  const png = project.assets.find(asset => asset.mime === 'image/png')!;
  assert.deepEqual([...Buffer.from(png.base64, 'base64').subarray(0, 8)], [137, 80, 78, 71, 13, 10, 26, 10]);
  assert.match(Buffer.from(project.assets.find(asset => asset.mime === 'text/plain')!.base64, 'base64').toString('utf8'), /original TXT attachment/);
});

test('demo collapse leaves a sibling output visible and exposed controls govern the actual circuit', () => {
  const project = createDemoProject();
  const parent = project.nodes.find(node => node.title === 'Boolean decision')!;
  const output = project.nodes.find(node => node.kind === 'output')!;
  const ready = project.nodes.find(node => node.title === 'Ready to proceed')!;
  const toggle = project.nodes.find(node => node.title === 'Evidence connection')!;
  assert.equal(output.parentId, parent.parentId);
  assert.equal(ready.exposed, true);
  assert.equal(toggle.exposed, true);
  parent.collapsed = true;
  assert.equal(evaluate(project).values[output.id], true);
  ready.value = false;
  assert.equal(evaluate(project).values[output.id], false);
  ready.value = true; toggle.closed = false;
  assert.equal(evaluate(project).values[output.id], null);
  assert.deepEqual(validateProject(project), []);
});

test('malformed input is rejected without throwing and cannot leave old output values', () => {
  const { project, a, output } = circuit();
  assert.equal(isProjectShape(project), true);
  assert.equal(isProjectShape({ ...project, snapshots: { bad: { node: null } } }), false);
  assert.equal(isProjectShape({ ...project, nodes: [null] }), false);
  assert.equal(isProjectShape(null), false);
  (a as unknown as { value: string }).value = 'true';
  assert.equal(isProjectShape(project), false);
  assert.equal(evaluate(project).values[output.id], null);
  assert.match(validateProject(project)[0]!.message, /malformed/);
});
