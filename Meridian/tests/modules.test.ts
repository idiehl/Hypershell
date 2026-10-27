import test from 'node:test';
import assert from 'node:assert/strict';
import { addNode, commitSnapshots, forkProject, newProject } from '../src/core/model.ts';
import { createModule, instantiateModule, validateModule } from '../src/core/modules.ts';
import { evaluate } from '../src/core/engine.ts';
import { validateProject } from '../src/core/rules.ts';
import { summarizeDependencies } from '../src/core/dependencies.ts';

function fixture() {
  const project = newProject('Reusable circuit'), canvas = project.entryCanvasId;
  const root = addNode(project, canvas, 'container', 'Packing'); root.x = 40; root.y = 60; root.width = 840; root.height = 620;
  root.style = { fill: '#102030', border: '#224466' }; root.snapping = { stickyEdges: true }; root.collapsed = true;
  const inner = addNode(project, canvas, 'container', 'Calculation'); inner.parentId = root.id; inner.x = 80; inner.y = 180;
  const input = addNode(project, canvas, 'input', 'Enable'); input.parentId = inner.id; input.value = true; input.x = 110; input.y = 270;
  const ref = addNode(project, canvas, 'reference', 'Live alias'); ref.parentId = inner.id; ref.targetId = input.id;
  const output = addNode(project, canvas, 'output', 'Result'); output.parentId = root.id; output.exposed = true; output.dependencyIds = [input.id];
  const photo = addNode(project, canvas, 'image', 'Image'); photo.parentId = root.id; photo.assetId = 'photo';
  project.assets = [{ id: 'photo', name: 'diagram.png', mime: 'image/png', base64: 'AQID' }, { id: 'other', name: 'unused.png', mime: 'image/png', base64: 'BAUG' }];
  project.links.push({ id: 'wire', canvasId: canvas, sourceId: ref.id, targetId: output.id, kind: 'functional', relation: 'causal', points: [{ x: 222, y: 333 }], sourceAnchor: { x: 1, y: 0.5 }, targetAnchor: { x: 0, y: 0.5 }, label: 'signal', style: { color: '#abcdef' } });
  return { project, root, inner, input, ref, output, photo };
}

test('saved modules carry only the selected branch, fresh identities, internal routes and used media', () => {
  const { project, root, input, output, ref } = fixture();
  const committed = commitSnapshots(project), before = JSON.stringify(committed);
  const module = createModule(committed, root.id, '  Reusable circuit  ');
  assert.equal(JSON.stringify(committed), before); assert.deepEqual(validateModule(module), []);
  assert.equal(module.name, 'Reusable circuit'); assert.equal(module.project.canvases.length, 1);
  assert.deepEqual(module.project.snapshots, {}); assert.equal(module.project.assets.length, 1);
  const old = new Set([project.id, ...project.canvases.map(item => item.id), ...project.nodes.map(item => item.id), ...project.links.map(item => item.id), ...project.assets.map(item => item.id)]);
  for (const object of [module, module.project, ...module.project.canvases, ...module.project.nodes, ...module.project.links, ...module.project.assets]) assert.equal(old.has(object.id), false);
  const savedRoot = module.project.nodes.find(node => node.id === module.rootId)!;
  assert.equal(savedRoot.kind, 'module'); assert.equal(savedRoot.parentId, module.project.canvases[0].rootId);
  assert.deepEqual(savedRoot.style, root.style); assert.deepEqual(savedRoot.snapping, root.snapping); assert.equal(savedRoot.collapsed, true);
  const savedInput = module.project.nodes.find(node => node.title === input.title)!;
  assert.deepEqual(module.project.nodes.find(node => node.title === output.title)?.dependencyIds, [savedInput.id]);
  assert.equal(module.project.nodes.find(node => node.title === ref.title)?.targetId, savedInput.id);
  assert.deepEqual(module.project.links[0].points, project.links[0].points);
});

test('two inserted copies remap every owned identity independently and translate internal route points', () => {
  const { project, root } = fixture(), module = createModule(project, root.id, 'Circuit');
  const destination = newProject('Destination'), before = structuredClone(destination), definition = structuredClone(module);
  const first = instantiateModule(destination, module, { canvasId: destination.entryCanvasId, x: 1000, y: 2000 });
  const second = instantiateModule(first.project, module, { canvasId: destination.entryCanvasId, x: 2000, y: 2000 });
  assert.deepEqual(destination, before); assert.deepEqual(module, definition); assert.deepEqual(validateProject(second.project), []);
  for (const sourceId of Object.keys(first.idMap)) assert.notEqual(first.idMap[sourceId], second.idMap[sourceId]);
  for (const id of [module.project.id, module.project.entryCanvasId, module.project.canvases[0].rootId]) assert.equal(Object.hasOwn(first.idMap, id), false);
  const retainedIds = new Set([...first.project.nodes, ...first.project.links, ...first.project.assets].map(item => item.id));
  for (const id of Object.values(first.idMap)) assert.ok(retainedIds.has(id));
  const a = first.project.nodes.find(node => node.id === first.rootId)!;
  assert.equal(a.x, 1000); assert.equal(a.y, 2000);
  assert.deepEqual(first.project.links[0].points, [{ x: 1182, y: 2273 }]);
  assert.deepEqual(first.project.links[0].sourceAnchor, { x: 1, y: 0.5 });
  const savedInput = module.project.nodes.find(node => node.title === 'Enable')!, savedOutput = module.project.nodes.find(node => node.title === 'Result')!;
  assert.deepEqual(first.project.nodes.find(node => node.id === first.idMap[savedOutput.id])?.dependencyIds, [first.idMap[savedInput.id]]);
  second.project.nodes.find(node => node.id === first.idMap[savedInput.id])!.value = false;
  const values = evaluate(second.project).values;
  assert.equal(values[first.idMap[savedOutput.id]], false); assert.equal(values[second.idMap[savedOutput.id]], true);
  assert.equal(second.project.assets.length, 2);
});

test('saving a functional or media node wraps it without replacing its semantics', () => {
  const project = newProject(), input = addNode(project, project.entryCanvasId, 'input', 'Run'); input.value = true;
  const module = createModule(project, input.id, 'Control');
  const packing = module.project.nodes.find(node => node.id === module.rootId)!;
  const child = module.project.nodes.find(node => node.kind === 'input')!;
  assert.equal(child.parentId, packing.id); assert.equal(child.title, 'Run'); assert.equal(evaluate(module.project).values[child.id], true);
});

test('boundary functional inputs become declared context while decorative edges do not', () => {
  const project = newProject(), root = addNode(project, project.entryCanvasId, 'container', 'Branch');
  const external = addNode(project, project.entryCanvasId, 'input', 'External input');
  const decorative = addNode(project, project.entryCanvasId, 'text', 'Annotation');
  const output = addNode(project, project.entryCanvasId, 'output', 'Result'); output.parentId = root.id;
  project.links = [
    { id: 'incoming', canvasId: project.entryCanvasId, sourceId: external.id, targetId: output.id, kind: 'functional', relation: 'causal' },
    { id: 'visual', canvasId: project.entryCanvasId, sourceId: decorative.id, targetId: root.id, kind: 'visual', relation: 'causal' },
  ];
  const module = createModule(project, root.id, 'Boundary');
  assert.equal(module.project.links.length, 0);
  assert.deepEqual(module.project.nodes.find(node => node.id === module.rootId)?.dependencyIds, [external.id]);
  assert.equal(summarizeDependencies(module.project).missing.some(item => item.id === external.id), true);
  assert.equal(summarizeDependencies(module.project).entries.some(item => item.id === decorative.id), false);
});

test('inherited source constraints survive packing and destination constraints reject atomically', () => {
  const project = newProject();
  project.nodes[0].rules = { nodeKinds: ['container', 'text'], linkKinds: ['visual'] };
  const root = addNode(project, project.entryCanvasId, 'container'), child = addNode(project, project.entryCanvasId, 'text'); child.parentId = root.id;
  const module = createModule(project, root.id, 'Restricted');
  assert.deepEqual(module.project.nodes.find(node => node.id === module.rootId)?.rules, project.nodes[0].rules);
  const destination = newProject(); destination.nodes[0].rules = { nodeKinds: ['text'] };
  const original = structuredClone(destination);
  assert.throws(() => instantiateModule(destination, module, { canvasId: destination.entryCanvasId, x: 0, y: 0 }), /inherited/);
  assert.deepEqual(destination, original);
});

test('external references remain external and cannot silently reconnect on insertion', () => {
  const project = newProject(), input = addNode(project, project.entryCanvasId, 'input', 'Source');
  const reference = addNode(project, project.entryCanvasId, 'reference', 'External alias'); reference.targetId = input.id;
  const module = createModule(project, reference.id, 'Alias');
  assert.equal(module.project.nodes.find(node => node.kind === 'reference')?.targetId, input.id);
  assert.throws(() => instantiateModule(project, module, { canvasId: project.entryCanvasId, x: 600, y: 600 }), /would reconnect/);
  const other = newProject(), copy = instantiateModule(other, module, { canvasId: other.entryCanvasId, x: 600, y: 600 });
  assert.equal(summarizeDependencies(copy.project).missing.some(item => item.id === input.id), true);
});

test('module validation rejects malformed declarations, disconnected content and containment cycles', () => {
  const { project, root } = fixture(), module = createModule(project, root.id, 'Check');
  const malformed = structuredClone(module); malformed.project.nodes.find(node => node.id === malformed.rootId)!.dependencyIds = [malformed.rootId];
  assert.ok(validateModule(malformed).length);
  const detached = structuredClone(module); const child = detached.project.nodes.find(node => node.kind === 'input')!; child.parentId = detached.project.canvases[0].rootId;
  assert.ok(validateModule(detached).some(issue => issue.includes('packing container')));
  const cyclic = structuredClone(module); cyclic.project.nodes.find(node => node.id === cyclic.rootId)!.parentId = cyclic.project.nodes.find(node => node.kind === 'input')!.id;
  assert.ok(validateModule(cyclic).some(issue => issue.includes('cycle')));
  assert.throws(() => createModule(project, project.canvases[0].rootId, 'Invalid'), /non-root/);
});

test('native forks remap dependency declarations with their owned nodes', () => {
  const { project, input, output } = fixture(), fork = forkProject(project);
  assert.notEqual(fork.nodes.find(node => node.title === input.title)?.id, input.id);
  assert.deepEqual(fork.nodes.find(node => node.title === output.title)?.dependencyIds, [fork.nodes.find(node => node.title === input.title)!.id]);
});

test('module extraction preserves known transitive requirements without copying outside nodes', () => {
  const project = newProject(), root = addNode(project, project.entryCanvasId, 'container', 'Included');
  const b = addNode(project, project.entryCanvasId, 'text', 'Outside B'), c = addNode(project, project.entryCanvasId, 'text', 'Outside C');
  root.dependencyIds = [b.id]; b.dependencyIds = [c.id];
  const module = createModule(project, root.id, 'Context');
  assert.deepEqual(new Set(module.project.nodes.find(node => node.id === module.rootId)?.dependencyIds), new Set([b.id, c.id]));
  assert.equal(module.project.nodes.some(node => node.title === 'Outside B' || node.title === 'Outside C'), false);
  assert.deepEqual(new Set(summarizeDependencies(module.project).missing.map(item => item.id)), new Set([b.id, c.id]));
});
