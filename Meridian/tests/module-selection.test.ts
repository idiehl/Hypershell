import test from 'node:test';
import assert from 'node:assert/strict';
import { addCanvas, addNode, newProject } from '../src/core/model.ts';
import { createModuleFromSelection, instantiateModule, validateModule } from '../src/core/modules.ts';
import { emptyModuleSelection, resolveModuleSelection, toggleModuleSelection } from '../src/core/module-selection.ts';
import { normalizeContainerLayouts } from '../src/core/grid.ts';
import { navigationIndex } from '../src/core/navigation.ts';
import { nodeShape, shapesOverlap } from '../src/core/geometry.ts';
import { validateProject } from '../src/core/rules.ts';

function node(project: ReturnType<typeof newProject>, kind: Parameters<typeof addNode>[2], title: string, x: number, y: number) {
  const item = addNode(project, project.entryCanvasId, kind, title);
  Object.assign(item, { x, y, width: 150, height: 100, sizeMode: 'fixed', childPosition: 'manual' });
  return item;
}

test('module selection inherits parent choices, retains explicit exclusions and automatically includes internal links', () => {
  const project = newProject(), parent = node(project, 'container', 'Parent', 0, 0), child = node(project, 'media', 'Child', 10, 50), grandchild = node(project, 'media', 'Grandchild', 200, 50);
  child.parentId = parent.id; grandchild.parentId = child.id; parent.collapsed = true;
  const other = node(project, 'media', 'Outside', 400, 0);
  project.links = [{ id: 'inside', canvasId: project.entryCanvasId, sourceId: parent.id, targetId: child.id, kind: 'visual', relation: 'generic' },
    { id: 'outside', canvasId: project.entryCanvasId, sourceId: child.id, targetId: other.id, kind: 'visual', relation: 'generic' }];
  const before = structuredClone(project), empty = emptyModuleSelection();
  let state = toggleModuleSelection(project, empty, parent.id);
  assert.deepEqual(resolveModuleSelection(project, state), { nodeIds: [parent.id, child.id, grandchild.id], linkIds: ['inside'] });
  state = toggleModuleSelection(project, state, child.id);
  assert.deepEqual(resolveModuleSelection(project, state).nodeIds, [parent.id]);
  state = toggleModuleSelection(project, state, grandchild.id);
  assert.deepEqual(resolveModuleSelection(project, state).nodeIds, [parent.id, grandchild.id]);
  state = toggleModuleSelection(project, state, parent.id);
  assert.deepEqual(resolveModuleSelection(project, state).nodeIds, [grandchild.id]);
  state = toggleModuleSelection(project, state, parent.id);
  assert.deepEqual(resolveModuleSelection(project, state).nodeIds, [parent.id, grandchild.id]);
  assert.deepEqual(empty.overrides, Object.create(null)); assert.deepEqual(project, before);
});

test('an exact multi-node template preserves media, hierarchy, routes, appearance and conceptual links', () => {
  const project = newProject(), a = node(project, 'media', 'A', -800, 250), b = node(project, 'media', 'B', -500, 420), omitted = node(project, 'media', 'Omitted', 80, 80);
  a.text = 'Full text'; a.style = { shape: 'hexagon', fill: '#224466', padding: 5, backgroundAssetId: 'back' }; b.assetId = 'image'; b.attachments = ['doc'];
  a.remoteLinks = [{ id: 'local-remote', targetId: b.id, relation: 'mutual' }, { id: 'outside-remote', targetId: omitted.id, relation: 'generic' }];
  project.assets = [{ id: 'image', name: 'original.png', mime: 'image/png', base64: 'AQID' }, { id: 'doc', name: 'source.txt', mime: 'text/plain', base64: 'BAUG' }, { id: 'back', name: 'back.png', mime: 'image/png', base64: 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/l9sAAAAASUVORK5CYII=' }, { id: 'unused', name: 'unused.png', mime: 'image/png', base64: 'AA==' }];
  project.links = [{ id: 'wire', canvasId: project.entryCanvasId, sourceId: a.id, targetId: b.id, kind: 'visual', relation: 'mutual', points: [{ x: -650, y: 200 }], sourceAnchor: { x: 1, y: .5 }, style: { color: '#ffeecc', width: 6, lineStyle: 'dotted' } },
    { id: 'crossing', canvasId: project.entryCanvasId, sourceId: a.id, targetId: omitted.id, kind: 'visual', relation: 'generic' }];
  const before = structuredClone(project), module = createModuleFromSelection(project, [b.id, a.id], '  Assembly  ');
  assert.deepEqual(validateModule(module), []); assert.deepEqual(project, before); assert.equal(module.name, 'Assembly');
  const savedA = module.project.nodes.find(item => item.title === 'A')!, savedB = module.project.nodes.find(item => item.title === 'B')!;
  for (const [original, copied] of [[a, savedA], [b, savedB]]) {
    assert.notEqual(copied.id, original.id); assert.deepEqual([copied.x, copied.y, copied.width, copied.height], [original.x, original.y, original.width, original.height]);
    assert.equal(copied.kind, original.kind); assert.equal(copied.title, original.title); assert.equal(copied.parentId, module.rootId);
  }
  assert.equal(savedA.text, a.text); assert.deepEqual({ ...savedA.style, backgroundAssetId: undefined }, { ...a.style, backgroundAssetId: undefined });
  assert.equal(savedA.remoteLinks![0].targetId, savedB.id); assert.equal(savedA.remoteLinks![1].targetId, omitted.id);
  assert.equal(module.project.assets.length, 3); assert.equal(module.project.assets.find(item => item.id === savedB.assetId)?.base64, 'AQID');
  assert.equal(module.project.links.length, 1); assert.equal(module.project.links[0].parentId, module.rootId); assert.deepEqual(module.project.links[0].points, project.links[0].points); assert.deepEqual(module.project.links[0].style, project.links[0].style);
  const wrapper = module.project.nodes.find(item => item.id === module.rootId)!;
  assert.equal(wrapper.sizeMode, 'fixed'); assert.equal(wrapper.containerVariant, 'contained'); assert.equal(wrapper.childArrangement, 'free');
});

test('an omitted middle parent promotes selected descendants without copying it or retaining obsolete cells', () => {
  const project = newProject(), parent = node(project, 'container', 'Parent', 0, 0), middle = node(project, 'container', 'Middle', 50, 90), child = node(project, 'media', 'Leaf', 80, 140);
  middle.parentId = parent.id; child.parentId = middle.id; child.cell = { column: 1, row: 1, name: 'child' }; child.placement = 'edge'; child.attachSide = 'right';
  parent.rules = { nodeKinds: ['container', 'media'], linkKinds: ['visual'] }; middle.rules = { nodeKinds: ['media'], linkKinds: ['visual'] };
  const module = createModuleFromSelection(project, [parent.id, child.id], 'Sparse branch');
  const savedParent = module.project.nodes.find(item => item.title === parent.title)!, savedChild = module.project.nodes.find(item => item.title === child.title)!;
  assert.equal(savedChild.parentId, savedParent.id); assert.equal(module.project.nodes.some(item => item.title === middle.title), false);
  assert.equal(savedChild.cell, undefined); assert.equal(savedChild.placement, 'free'); assert.equal(savedChild.childPosition, 'manual');
  assert.deepEqual([savedChild.x, savedChild.y, savedChild.width, savedChild.height], [child.x, child.y, child.width, child.height]);
  assert.deepEqual(savedChild.rules, middle.rules); assert.deepEqual(validateModule(module), []);
});

test('explicitly omitted descendants contribute no unrelated requirements while crossing functional inputs remain declared', () => {
  const project = newProject(), parent = node(project, 'container', 'Parent', 0, 0), output = node(project, 'output', 'Chosen output', 20, 60), omitted = node(project, 'media', 'Omitted', 20, 180), input = node(project, 'input', 'Source', 500, 0);
  output.parentId = parent.id; omitted.parentId = parent.id; omitted.dependencyIds = ['unrelated'];
  input.dependencyIds = ['transitive'];
  project.links = [{ id: 'input-wire', canvasId: project.entryCanvasId, sourceId: input.id, targetId: output.id, kind: 'functional', relation: 'causal' }];
  const module = createModuleFromSelection(project, [parent.id, output.id], 'Partial');
  const packing = module.project.nodes.find(item => item.id === module.rootId)!;
  assert.deepEqual(new Set(packing.dependencyIds), new Set([input.id, 'transitive'])); assert.equal(packing.dependencyIds?.includes('unrelated'), false);
  assert.equal(module.project.links.length, 0); assert.equal(module.project.nodes.some(item => item.id === input.id), false);
});

test('different source ancestor limits remain on their own branches rather than cross-restricting unrelated selected roots', () => {
  const project = newProject(), parentA = node(project, 'container', 'Text owner', 0, 0), parentB = node(project, 'container', 'Media owner', 300, 0), a = node(project, 'text', 'Text', 10, 60), b = node(project, 'media', 'Media', 310, 60);
  parentA.rules = { nodeKinds: ['text'], linkKinds: ['visual'] }; parentB.rules = { nodeKinds: ['media'], linkKinds: ['numeric'] }; a.parentId = parentA.id; b.parentId = parentB.id;
  const module = createModuleFromSelection(project, [a.id, b.id], 'Different branches');
  assert.deepEqual(module.project.nodes.find(item => item.title === 'Text')?.rules, parentA.rules);
  assert.deepEqual(module.project.nodes.find(item => item.title === 'Media')?.rules, parentB.rules);
  const destination = newProject(); destination.nodes[0].rules = { nodeKinds: ['text'] };
  const before = structuredClone(destination);
  assert.throws(() => instantiateModule(destination, module, { canvasId: destination.entryCanvasId, x: 900, y: 900 }), /inherited/); assert.deepEqual(destination, before);
});

test('inserting two modules creates independent GUIDs including conceptual links and avoids collisions as a whole composition', () => {
  const project = newProject(), a = node(project, 'media', 'A', 100, 100), b = node(project, 'media', 'B', 300, 100);
  a.remoteLinks = [{ id: 'conceptual', targetId: b.id, relation: 'generic' }];
  project.links = [{ id: 'wire', canvasId: project.entryCanvasId, sourceId: a.id, targetId: b.id, kind: 'visual', relation: 'generic', points: [{ x: 280, y: 80 }] }];
  const module = createModuleFromSelection(project, [a.id, b.id], 'Assembly'), definition = structuredClone(module);
  const first = instantiateModule(project, module, { canvasId: project.entryCanvasId, x: 100, y: 100 });
  const second = instantiateModule(first.project, module, { canvasId: project.entryCanvasId, x: 100, y: 100 });
  const rootA = second.project.nodes.find(item => item.id === first.rootId)!, rootB = second.project.nodes.find(item => item.id === second.rootId)!;
  assert.equal(shapesOverlap(nodeShape(rootA), nodeShape(rootB)), false);
  for (const id of Object.keys(first.idMap)) assert.notEqual(first.idMap[id], second.idMap[id]);
  const savedA = module.project.nodes.find(item => item.title === 'A')!, savedB = module.project.nodes.find(item => item.title === 'B')!, savedRoot = module.project.nodes.find(item => item.id === module.rootId)!;
  const copiedA = first.project.nodes.find(item => item.id === first.idMap[savedA.id])!, copiedB = first.project.nodes.find(item => item.id === first.idMap[savedB.id])!, root = first.project.nodes.find(item => item.id === first.rootId)!;
  assert.deepEqual([copiedB.x - copiedA.x, copiedB.y - copiedA.y, copiedA.width, copiedA.height], [200, 0, 150, 100]);
  assert.equal(copiedA.remoteLinks![0].targetId, copiedB.id); assert.equal(first.idMap[savedA.remoteLinks![0].id], copiedA.remoteLinks![0].id);
  const wire = first.project.links.find(item => item.id === first.idMap[module.project.links[0].id])!;
  assert.deepEqual(wire.points, module.project.links[0].points!.map(point => ({ x: point.x + root.x - savedRoot.x, y: point.y + root.y - savedRoot.y })));
  assert.deepEqual(module, definition); assert.deepEqual(validateProject(second.project), []);
});

test('a new manual module wrapper preserves its captured geometry through editor layout normalization', () => {
  const project = newProject(), a = node(project, 'media', 'A', -300, 600), b = node(project, 'media', 'B', 200, 800);
  const module = createModuleFromSelection(project, [a.id, b.id], 'Manual layout');
  const normalized = normalizeContainerLayouts(module.project);
  for (const original of module.project.nodes) {
    const result = normalized.nodes.find(item => item.id === original.id)!;
    assert.deepEqual([result.x, result.y, result.width, result.height], [original.x, original.y, original.width, original.height]);
  }
  const bounds = navigationIndex(module.project).bounds(module.project.nodes.find(item => item.id === module.rootId)!);
  assert.ok(bounds.width > b.x + b.width - a.x); assert.ok(bounds.height > b.y + b.height - a.y);
});

test('a cell reference to an omitted cell keeps the original external target even when its old parent is selected', () => {
  const project = newProject(), grid = node(project, 'container', 'Grid', 0, 0), omittedCell = node(project, 'media', 'Omitted cell', 20, 50), reference = node(project, 'reference', 'Outside cell alias', 300, 0);
  grid.grid = { columns: 2, rows: 2, cellWidth: 100, cellHeight: 80 }; omittedCell.parentId = grid.id; omittedCell.cell = { column: 1, row: 1 };
  reference.targetId = grid.id; reference.targetCell = '1:1';
  const module = createModuleFromSelection(project, [grid.id, reference.id], 'Cell boundary');
  assert.equal(module.project.nodes.find(item => item.title === reference.title)?.targetId, grid.id);
  assert.notEqual(module.project.nodes.find(item => item.title === grid.title)?.id, grid.id);
  assert.throws(() => instantiateModule(project, module, { canvasId: project.entryCanvasId, x: 900, y: 900 }), /would reconnect/);
});

test('partial ordered templates retain their pattern and match independent copies after reload normalization without resurrecting excluded cells', () => {
  const source = newProject(), parent = node(source, 'container', 'Ordered strip', 500, 300);
  Object.assign(parent, { width: 812, height: 160, sizeMode: 'auto', containerVariant: 'horizontal', childLayout: 'contained',
    grid: { columns: 4, rows: 1, cellWidth: 200, cellHeight: 100, gap: 0, showHeaders: false } });
  const children = Array.from({ length: 4 }, (_, at) => {
    const child = node(source, 'media', `Cell ${at + 1}`, 506 + at * 200, 330);
    Object.assign(child, { parentId: parent.id, width: 200, height: 100, childPosition: 'auto', cell: { column: at + 1, row: 1 } }); return child;
  });
  const project = normalizeContainerLayouts(source), module = createModuleFromSelection(project, [parent.id, children[0].id, children[2].id, children[3].id], 'Partial ordered strip');
  const normalizedDefinition = normalizeContainerLayouts(module.project), destination = newProject();
  const inserted = instantiateModule(destination, module, { canvasId: destination.entryCanvasId, x: 5000, y: 4000 });
  const reopened = normalizeContainerLayouts(inserted.project), definitionRoot = normalizedDefinition.nodes.find(item => item.id === module.rootId)!, copyRoot = reopened.nodes.find(item => item.id === inserted.rootId)!;
  const definitionNav = navigationIndex(normalizedDefinition), copyNav = navigationIndex(reopened);
  assert.equal(normalizedDefinition.nodes.find(item => item.title === parent.title)?.containerVariant, 'horizontal');
  assert.equal(reopened.nodes.some(item => item.title === children[1].title), false);
  for (const original of normalizedDefinition.nodes.filter(item => item.kind !== 'canvas')) {
    const copy = reopened.nodes.find(item => item.id === inserted.idMap[original.id])!;
    const a = definitionNav.bounds(original), b = copyNav.bounds(copy);
    assert.deepEqual(copy.cell, original.cell);
    const expected = [original.x - definitionRoot.x, original.y - definitionRoot.y, original.width, original.height, a.x - definitionRoot.x, a.y - definitionRoot.y, a.width, a.height];
    const actual = [copy.x - copyRoot.x, copy.y - copyRoot.y, copy.width, copy.height, b.x - copyRoot.x, b.y - copyRoot.y, b.width, b.height];
    actual.forEach((value, at) => assert.ok(Math.abs(value - expected[at]!) < 1e-9, 'Fractional cell geometry survives a distant translation.'));
  }
  assert.deepEqual(validateProject(reopened), []); assert.deepEqual(validateProject(normalizedDefinition), []);
});

test('module selection rejects empty, stale, canvas-root, mixed-canvas and unnamed captures atomically', () => {
  const project = newProject(), a = node(project, 'media', 'A', 0, 0), otherCanvas = addCanvas(project, 'Other'), b = addNode(project, otherCanvas.id, 'media');
  const before = structuredClone(project);
  assert.throws(() => createModuleFromSelection(project, [], 'Empty'), /at least one/);
  assert.throws(() => createModuleFromSelection(project, ['stale'], 'Stale'), /does not exist/);
  assert.throws(() => createModuleFromSelection(project, [project.canvases[0].rootId], 'Root'), /Work roots/);
  assert.throws(() => createModuleFromSelection(project, [a.id, b.id], 'Mixed'), /one Work/);
  assert.throws(() => createModuleFromSelection(project, [a.id], ' '), /name/);
  assert.throws(() => toggleModuleSelection(project, emptyModuleSelection(), project.canvases[0].rootId), /inside the Work/);
  assert.deepEqual(project, before);
});
