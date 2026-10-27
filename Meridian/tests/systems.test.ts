import test from 'node:test';
import assert from 'node:assert/strict';
import type { LinkKind, MeridianNode as Node, MeridianProject as Project, Operation } from '../src/core/types.ts';
import { addCanvas, addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import { evaluate } from '../src/core/engine.ts';
import { isProjectShape, validateProject } from '../src/core/rules.ts';
import { applyOperation, inputPorts, operationDefinition, outputValueType } from '../src/core/operations.ts';
import { splitLinkWithJunction } from '../src/core/connections.ts';
import { summarizeDependencies } from '../src/core/dependencies.ts';
import { createModule, instantiateModule } from '../src/core/modules.ts';

function wire(project: Project, source: Node, target: Node, kind: LinkKind = 'numeric', targetPort = 0): string {
  const id = crypto.randomUUID();
  project.links.push({ id, canvasId: source.canvasId, sourceId: source.id, targetId: target.id, kind, relation: 'causal', targetPort });
  return id;
}
function circuit(operation: Operation = 'ADD') {
  const project = newProject('Number system'), canvasId = project.entryCanvasId;
  const a = addNode(project, canvasId, 'number', 'A'), b = addNode(project, canvasId, 'number', 'B');
  const joint = addNode(project, canvasId, 'joint'), output = addNode(project, canvasId, 'output', 'Result');
  joint.operation = operation;
  output.valueType = operationDefinition(operation)!.output;
  wire(project, a, joint);
  if (inputPorts(joint).length > 1) wire(project, b, joint, 'numeric', 1);
  const resultLink = wire(project, joint, output, output.valueType === 'number' ? 'numeric' : 'functional');
  return { project, a, b, joint, output, resultLink };
}

test('arithmetic joints react to finite operands and expose explicit named numeric ports', () => {
  const cases: [Operation, number, number, number][] = [
    ['ADD', 8, 3, 11], ['SUBTRACT', 8, 3, 5], ['MULTIPLY', 8, 3, 24], ['DIVIDE', 9, 3, 3],
    ['MODULO', 8, 3, 2], ['POWER', 2, 3, 8], ['MIN', 8, 3, 3], ['MAX', 8, 3, 8], ['ABS', -8, 3, 8], ['NEGATE', 8, 3, -8],
  ];
  for (const [operation, first, second, expected] of cases) {
    const { project, a, b, joint, output } = circuit(operation);
    a.value = first; b.value = second;
    assert.deepEqual(validateProject(project), [], operation);
    assert.equal(evaluate(project).values[output.id], expected, operation);
    assert.deepEqual(inputPorts(joint).map(port => port.type), inputPorts(joint).map(() => 'number'));
    assert.equal(outputValueType(project, joint), 'number');
    a.value = first + 1;
    assert.equal(evaluate(project).values[output.id], applyOperation(operation, inputPorts(joint).length === 1 ? [first + 1] : [first + 1, second]), `${operation} reacts`);
  }
});

test('comparisons connect numeric operands to Boolean systems without implicit coercion', () => {
  for (const [operation, aValue, bValue, expected] of [['GREATER', 8, 3, true], ['LESS', 8, 3, false], ['EQUAL', 3, 3, true]] as const) {
    const { project, a, b, output } = circuit(operation); a.value = aValue; b.value = bValue;
    const invert = addNode(project, a.canvasId, 'joint'); invert.operation = 'NOT';
    wire(project, output, invert, 'functional');
    assert.deepEqual(validateProject(project), []);
    assert.equal(evaluate(project).values[output.id], expected);
    assert.equal(evaluate(project).values[invert.id], !expected);
  }
  assert.equal(applyOperation('ADD', [true, 3]), null);
  assert.equal(applyOperation('AND', [1, 1]), null);
});

test('missing operands, division by zero and nonfinite results propagate undefined', () => {
  const { project, a, b, joint, output } = circuit('DIVIDE'); a.value = 8; b.value = 2;
  assert.equal(evaluate(project).values[output.id], 4);
  b.value = 0;
  assert.equal(evaluate(project).values[joint.id], null);
  assert.equal(evaluate(project).values[output.id], null);
  assert.match(evaluate(project).errors[joint.id]!, /zero/);
  joint.operation = 'POWER'; a.value = 1e308; b.value = 2;
  assert.equal(evaluate(project).values[output.id], null);
  assert.match(evaluate(project).errors[joint.id]!, /finite/);
  a.value = -1; b.value = .5;
  assert.equal(evaluate(project).values[output.id], null);
  project.links = project.links.filter(link => link.sourceId !== b.id);
  assert.deepEqual(validateProject(project), []);
  assert.equal(evaluate(project).values[output.id], null);
  assert.equal(applyOperation('MODULO', [3, 0]), null);
});

test('opening a numeric switch clears downstream results while zero and unrelated signals remain defined', () => {
  const { project, a, b, joint, output } = circuit(); a.value = 3; b.value = -3;
  const control = addNode(project, a.canvasId, 'switch'); control.valueType = 'number';
  project.links.find(link => link.targetId === joint.id && link.targetPort === 0)!.sourceId = control.id;
  wire(project, a, control);
  assert.equal(evaluate(project).values[output.id], 0);
  control.closed = false;
  assert.equal(evaluate(project).values[output.id], null);
  assert.equal(evaluate(project).values[b.id], -3);
  control.closed = true;
  assert.equal(evaluate(project).values[output.id], 0);
});

test('numeric calculations continue through collapsed nested containers with independent sibling outputs', () => {
  const { project, a, b, joint, output } = circuit('MULTIPLY'); a.value = 4; b.value = 5;
  const parent = addNode(project, a.canvasId, 'container'), inner = addNode(project, a.canvasId, 'text');
  inner.parentId = parent.id; inner.text = 'Inspectable working detail';
  for (const node of [a, b, joint]) node.parentId = inner.id;
  inner.collapsed = true; parent.collapsed = true;
  assert.equal(output.parentId, parent.parentId);
  assert.deepEqual(validateProject(project), []);
  assert.equal(evaluate(project).values[output.id], 20);
  a.value = 6;
  assert.equal(evaluate(project).values[output.id], 30);
});

test('connection type, port occupancy and numeric feedback are validated', () => {
  const { project, a, joint, output } = circuit();
  const sourceLink = project.links.find(link => link.sourceId === a.id)!;
  sourceLink.kind = 'functional';
  assert.ok(validateProject(project).some(issue => /type/.test(issue.message)));
  assert.equal(evaluate(project).values[output.id], null);
  sourceLink.kind = 'numeric'; sourceLink.targetPort = 2;
  assert.ok(validateProject(project).some(issue => /port/.test(issue.message)));
  sourceLink.targetPort = 0;
  wire(project, a, joint);
  assert.ok(validateProject(project).some(issue => /one connection/.test(issue.message)));
  project.links.pop(); sourceLink.sourceId = output.id;
  assert.ok(validateProject(project).some(issue => /feedback/.test(issue.message)));
  assert.equal(evaluate(project).values[output.id], null);
});

test('input and snapshot shape rejects nonfinite numbers and conflicting input node types', () => {
  const { project, a } = circuit();
  a.value = Infinity; assert.equal(isProjectShape(project), false);
  a.value = NaN; assert.equal(isProjectShape(project), false);
  a.value = false; assert.ok(validateProject(project).some(issue => /finite numbers/.test(issue.message)));
  a.value = 0; a.valueType = 'boolean';
  assert.ok(validateProject(project).some(issue => /contradict/.test(issue.message)));
  delete a.valueType;
  project.snapshots[a.id] = { node: structuredClone(a), value: Infinity, savedAt: new Date().toISOString() };
  assert.equal(isProjectShape(project), false);
});

test('numeric references use local live values and other-canvas saved values with their saved types', () => {
  let { project, a } = circuit(); a.value = 12;
  const local = addNode(project, a.canvasId, 'reference'); local.targetId = a.id;
  const remoteCanvas = addCanvas(project, 'Saved source consumer');
  const remote = addNode(project, remoteCanvas.id, 'reference'); remote.targetId = a.id;
  const output = addNode(project, remoteCanvas.id, 'output'); output.valueType = 'number'; wire(project, remote, output);
  assert.equal(evaluate(project).values[output.id], null);
  project = commitSnapshots(project); project.nodes.find(node => node.id === a.id)!.value = 24;
  assert.equal(evaluate(project).values[local.id], 24);
  assert.equal(evaluate(project).values[output.id], 12);
  assert.equal(outputValueType(project, remote), 'number');
  assert.equal(evaluate(commitSnapshots(project)).values[output.id], 24);
  project.nodes.find(node => node.id === a.id)!.kind = 'input'; project.nodes.find(node => node.id === a.id)!.value = true;
  assert.equal(outputValueType(project, remote), 'number', 'Unsaved remote type changes must not leak into saved references');
});

test('external numeric snapshots can supply a typed circuit without the source workspace', () => {
  const project = newProject(), source = addNode(project, project.entryCanvasId, 'reference'); source.targetId = 'external-number';
  project.snapshots['external-number'] = { node: { id: 'external-number', canvasId: 'outside', parentId: 'outside-root', kind: 'number', title: 'Saved number', x: 0, y: 0, width: 200, height: 120, value: 42 }, value: 42, savedAt: new Date().toISOString() };
  const output = addNode(project, project.entryCanvasId, 'output'); output.valueType = 'number'; wire(project, source, output);
  assert.deepEqual(validateProject(project), []);
  assert.equal(evaluate(project).values[output.id], 42);
});

test('visual route insertion preserves endpoint identity, labels, bends and containment atomically', () => {
  const project = newProject(), group = addNode(project, project.entryCanvasId, 'container');
  const a = addNode(project, project.entryCanvasId, 'text'), b = addNode(project, project.entryCanvasId, 'text');
  a.parentId = group.id; b.parentId = group.id;
  const id = wire(project, a, b, 'visual'), original = project.links[0]!;
  original.label = 'Descends from'; original.points = [{ x: 250, y: 200 }, { x: 450, y: 200 }];
  original.sourceAnchor = { x: 1, y: .4 }; original.targetAnchor = { x: 0, y: .7 };
  const inserted = splitLinkWithJunction(project, id, { x: 350, y: 200 }, { segmentIndex: 1 });
  assert.equal(project.nodes.length, 4); assert.equal(project.links.length, 1);
  const node = inserted.project.nodes.find(item => item.id === inserted.nodeId)!;
  assert.equal(node.kind, 'junction'); assert.equal(node.parentId, group.id); assert.equal(node.valueType, undefined);
  assert.deepEqual([node.x, node.y, node.width, node.height], [344, 194, 12, 12]);
  const inbound = inserted.project.links.find(item => item.id === inserted.sourceLinkId)!;
  const outbound = inserted.project.links.find(item => item.id === id)!;
  assert.deepEqual(inbound.points, [{ x: 250, y: 200 }]); assert.deepEqual(outbound.points, [{ x: 450, y: 200 }]);
  assert.deepEqual(inbound.sourceAnchor, original.sourceAnchor); assert.deepEqual(outbound.targetAnchor, original.targetAnchor);
  assert.equal(outbound.label, original.label); assert.equal(inbound.label, undefined);
  assert.deepEqual(validateProject(inserted.project), []);
  group.rules = { nodeKinds: ['text'] };
  assert.throws(() => splitLinkWithJunction(project, id, { x: 350, y: 200 }), /inherited/);
  assert.equal(project.links.length, 1);
});

test('typed route junctions preserve values and permit fanout without combining source signals', () => {
  for (const kind of ['numeric', 'functional'] as const) {
    const project = newProject(), source = addNode(project, project.entryCanvasId, kind === 'numeric' ? 'number' : 'input');
    source.value = kind === 'numeric' ? 17 : true;
    const output = addNode(project, source.canvasId, 'output'); output.valueType = kind === 'numeric' ? 'number' : 'boolean';
    const id = wire(project, source, output, kind);
    const inserted = splitLinkWithJunction(project, id, { x: 350, y: 200 });
    const junction = inserted.project.nodes.find(node => node.id === inserted.nodeId)!;
    assert.equal(junction.valueType, output.valueType);
    const secondOutput = addNode(inserted.project, source.canvasId, 'output'); secondOutput.valueType = output.valueType;
    wire(inserted.project, junction, secondOutput, kind);
    assert.equal(evaluate(inserted.project).values[output.id], source.value);
    assert.equal(evaluate(inserted.project).values[secondOutput.id], source.value);
    wire(inserted.project, source, junction, kind);
    assert.ok(validateProject(inserted.project).some(issue => /one connection/.test(issue.message)));
  }
});

test('visual crossings and even shared visual endpoints never evaluate or join numeric systems', () => {
  const { project, a, b, joint, output } = circuit(); a.value = 2; b.value = 3;
  const route = addNode(project, a.canvasId, 'junction');
  wire(project, a, route, 'visual'); wire(project, b, route, 'visual'); wire(project, route, joint, 'visual');
  assert.deepEqual(validateProject(project), []);
  assert.equal(evaluate(project).values[route.id], null);
  assert.equal(evaluate(project).values[output.id], 5);
  assert.equal(inputPorts(route).length, 0);
});

test('inherited numeric link and operation restrictions stay binding through nested parents', () => {
  const { project, a, b, joint } = circuit();
  const group = addNode(project, a.canvasId, 'container'); group.rules = { operations: ['ADD'], linkKinds: ['numeric'] };
  for (const node of [a, b, joint]) node.parentId = group.id;
  assert.deepEqual(validateProject(project), []);
  joint.operation = 'MULTIPLY'; assert.ok(validateProject(project).some(issue => /operation is excluded/.test(issue.message)));
  joint.operation = 'ADD'; group.rules.linkKinds = ['visual'];
  assert.ok(validateProject(project).some(issue => /Link kind is excluded/.test(issue.message)));
});

test('numeric module copies preserve operations and expose boundary numeric requirements', () => {
  const { project, a, b, joint, output } = circuit('MULTIPLY'); a.value = 3; b.value = 7;
  const group = addNode(project, a.canvasId, 'container');
  for (const node of [b, joint, output]) node.parentId = group.id;
  assert.ok(summarizeDependencies(project, { rootIds: [group.id] }).entries.some(entry => entry.id === a.id && entry.reasons.includes('functional')));
  const partial = createModule(project, group.id, 'Needs an outside number');
  assert.ok(partial.project.nodes.find(node => node.id === partial.rootId)!.dependencyIds?.includes(a.id));
  a.parentId = group.id;
  const complete = createModule(project, group.id, 'Product');
  const destination = newProject();
  const result = instantiateModule(destination, complete, { canvasId: destination.entryCanvasId, x: 0, y: 0 });
  const copiedOutput = result.project.nodes.find(node => node.title === 'Result')!;
  assert.equal(evaluate(result.project).values[copiedOutput.id], 21);
  assert.deepEqual(validateProject(result.project), []);
});

test('explicit identity joints preserve Boolean and numeric channels without conversion', () => {
  assert.equal(applyOperation('IDENTITY', [false]), false);
  assert.equal(applyOperation('IDENTITY', [0], 'number'), 0);
  assert.equal(applyOperation('IDENTITY', [1]), null);
  assert.equal(applyOperation('IDENTITY', [true], 'number'), null);
});

