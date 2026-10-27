import assert from 'node:assert/strict';
import test from 'node:test';
import { isNodeStyle, NODE_SPACING_DEFAULT } from '../src/core/appearance.ts';
import { applyRecipe, type AuthoringRecipe } from '../src/core/authoring.ts';
import { createNodeFromSpec } from '../src/core/creation.ts';
import { isGridSettings, parseCellAddress, resolveGridCell } from '../src/core/cells.ts';
import { addNode, commitSnapshots, forkProject, newProject } from '../src/core/model.ts';
import { updateNodeType } from '../src/core/node-editing.ts';
import { isProjectShape, validateProject } from '../src/core/rules.ts';
import { buildPublication, createPublicationPayload, readPublication } from '../src/publication.ts';
import type { ContainerVariant, MeridianProject, NodeStyle } from '../src/core/types.ts';

const spacing: NodeStyle = { padding: 5, contentGap: 12, textMediaGap: 0, childPadding: 40, borderStyle: 'dotted', borderWidth: 3, shape: 'hexagon' };
function fixture() {
  const project = newProject('Ordered container schema');
  const window = addNode(project, project.entryCanvasId, 'container', 'Scrollable sources');
  window.containerVariant = 'window'; window.style = structuredClone(spacing);
  window.grid = { columns: 4, rows: 8, cellWidth: 140, cellHeight: 90, gap: 0, fillOrder: 'column', windowColumns: 3, windowRows: 5, scrollDirection: 'vertical' };
  const child = addNode(project, project.entryCanvasId, 'text', 'Source');
  child.parentId = window.id; child.cell = { column: 2, row: 3, name: 'Source' }; child.text = 'An inert entry.';
  const reference = addNode(project, project.entryCanvasId, 'reference', 'Coordinate reference');
  reference.targetId = window.id; reference.targetCell = '2:3';
  return { project: commitSnapshots(project), windowId: window.id, childId: child.id, referenceId: reference.id };
}

test('container defaults are Enclosed while legacy Spatial remains a readable saved variant', () => {
  const project = newProject();
  for (const kind of ['container', 'module'] as const) {
    const node = addNode(project, project.entryCanvasId, kind);
    assert.equal(node.containerVariant, 'contained'); assert.equal(node.childLayout, 'contained'); assert.equal(node.sizeMode, 'auto');
  }
  const node = project.nodes[1]; node.containerVariant = 'spatial'; node.childLayout = 'spatial';
  assert.ok(isProjectShape(commitSnapshots(project))); assert.deepEqual(validateProject(project), []);
  const created = createNodeFromSpec(project, { canvasId: project.entryCanvasId, parentId: project.canvases[0].rootId, type: 'container', title: 'Legacy API input', x: 0, y: 0, width: 480, height: 300, variant: 'spatial' });
  assert.equal(created.project.nodes.find(node => node.id === created.nodeId)!.containerVariant, 'contained');
});

test('ordered variants and X:Y references preserve named identities without changing old A1 references', () => {
  const f = fixture(); assert.ok(isProjectShape(f.project)); assert.deepEqual(validateProject(f.project), []);
  for (const variant of ['horizontal', 'vertical', 'grid', 'spreadsheet', 'gallery', 'contained', 'window'] as ContainerVariant[]) {
    const copy = structuredClone(f.project); copy.nodes.find(node => node.id === f.windowId)!.containerVariant = variant;
    copy.snapshots[f.windowId].node.containerVariant = variant;
    assert.ok(isProjectShape(copy), variant); assert.deepEqual(validateProject(copy), [], variant);
  }
  assert.deepEqual(parseCellAddress('2:3'), { column: 2, row: 3 }); assert.deepEqual(parseCellAddress('B3'), { column: 2, row: 3 });
  for (const address of ['2:3', 'B3', 'source']) assert.equal(resolveGridCell(f.project, f.windowId, address)!.id, f.childId);
  for (const address of ['0:3', '2:0', '513:1', '1:513', '2.5:1', '01:2']) assert.equal(parseCellAddress(address), undefined, address);
});

test('window metadata is bounded and required on Window nodes in live documents and saved observations', () => {
  const f = fixture();
  assert.ok(isGridSettings({ columns: 1, rows: 1, cellWidth: 36, cellHeight: 36, windowColumns: 512, windowRows: 512, fillOrder: 'row', scrollDirection: 'horizontal' }));
  const mutations: Array<[string, (node: any) => void]> = [
    ['missing grid', node => { delete node.grid; }], ['missing window columns', node => { delete node.grid.windowColumns; }],
    ['missing window rows', node => { delete node.grid.windowRows; }], ['fractional viewport', node => { node.grid.windowRows = 2.5; }],
    ['zero viewport', node => { node.grid.windowColumns = 0; }], ['oversized viewport', node => { node.grid.windowColumns = 513; }],
    ['null viewport', node => { node.grid.windowRows = null; }], ['inert fill order', node => { node.grid.fillOrder = 'eval'; }],
    ['inert scroll direction', node => { node.grid.scrollDirection = 'both'; }], ['unknown grid key', node => { node.grid.script = 'alert(1)'; }],
  ];
  for (const [label, mutate] of mutations) for (const saved of [false, true]) {
    const copy = structuredClone(f.project), node = saved ? copy.snapshots[f.windowId].node : copy.nodes.find(node => node.id === f.windowId)!;
    mutate(node); assert.equal(isProjectShape(copy), false, `${label} ${saved}`); assert.ok(validateProject(copy).length);
  }
});

test('node border and spacing settings remain finite typed data, allowing explicit zero padding', () => {
  assert.equal(NODE_SPACING_DEFAULT, 5); assert.ok(isNodeStyle(spacing));
  for (const key of ['padding', 'contentGap', 'textMediaGap', 'childPadding']) {
    for (const value of [0, 5, 1000]) assert.ok(isNodeStyle({ [key]: value }), `${key} ${value}`);
    for (const value of [-1, 1001, Infinity, NaN, null, '5', true]) assert.equal(isNodeStyle({ [key]: value }), false, `${key} ${value}`);
  }
  for (const borderStyle of ['solid', 'dashed', 'dotted', 'double']) assert.ok(isNodeStyle({ borderStyle }));
  for (const borderStyle of ['groove', 'url(external)', null, 5]) assert.equal(isNodeStyle({ borderStyle }), false);
  const f = fixture(); f.project.snapshots[f.windowId].node.style!.padding = -1; assert.equal(isProjectShape(f.project), false);
});

test('recipes, snapshots, independent copies and publications retain ordered settings and spacing without unknown metadata', () => {
  const base = newProject('Declarative containers'), before = structuredClone(base);
  const authored = applyRecipe(base, { version: 1, nodes: [{ key: 'window', canvas: base.entryCanvasId, kind: 'container', containerVariant: 'window', style: spacing,
    grid: { columns: 4, rows: 8, cellWidth: 140, cellHeight: 90, fillOrder: 'column', windowColumns: 3, windowRows: 5, scrollDirection: 'vertical' } }] });
  assert.deepEqual(base, before); const window = authored.project.nodes.find(node => node.id === authored.aliases.nodes.window)!;
  assert.deepEqual(window.style, spacing); assert.equal(window.grid!.windowRows, 5);
  for (const style of [{ padding: -1 }, { childPadding: '5' }, { borderStyle: 'url(x)' }]) {
    assert.throws(() => applyRecipe(base, { version: 1, nodes: [{ key: 'bad', canvas: base.entryCanvasId, kind: 'container', style }] } as unknown as AuthoringRecipe));
    assert.deepEqual(base, before);
  }
  const f = fixture(), fork = forkProject(f.project), copy = fork.nodes.find(node => node.title === 'Scrollable sources')!;
  assert.notEqual(copy.id, f.windowId); assert.deepEqual(copy.style, spacing); assert.deepEqual(copy.grid, f.project.snapshots[f.windowId].node.grid);
  Object.assign(f.project.nodes.find(node => node.id === f.windowId)!.grid!, { evaluator: 'C:/private/data.js' });
  Object.assign(f.project.nodes.find(node => node.id === f.windowId)!.style!, { css: 'display:none' });
  const payload = createPublicationPayload(f.project), published = payload.project.nodes.find(node => node.id === f.windowId)!;
  assert.deepEqual(published.style, spacing); assert.equal((published.grid as any).evaluator, undefined);
  const imported: MeridianProject = readPublication(buildPublication(f.project, '', ''));
  const restored = imported.nodes.find(node => node.title === 'Scrollable sources')!;
  assert.deepEqual(restored.grid, published.grid); assert.deepEqual(restored.style, spacing); assert.deepEqual(validateProject(imported), []);
});

test('ordered recipes use editor cell normalization, preserving source data while keeping nested children uniform', () => {
  const base = newProject('Square recipe'), before = structuredClone(base);
  const result = applyRecipe(base, { version: 1, nodes: [
    { key: 'grid', canvas: base.entryCanvasId, kind: 'container', containerVariant: 'grid', grid: { columns: 1, rows: 1, cellWidth: 120, cellHeight: 80, fillOrder: 'column' } },
    ...Array.from({ length: 10 }, (_, at) => ({ key: `item${at}`, canvas: base.entryCanvasId, parent: 'grid', kind: 'text' as const, title: `Item ${at}`, text: 'Some content', style: { shape: at === 0 ? 'hexagon' as const : 'circle' as const } })),
  ] });
  assert.deepEqual(base, before);
  const grid = result.project.nodes.find(node => node.id === result.aliases.nodes.grid)!;
  assert.equal(grid.grid!.columns, 4); assert.equal(grid.grid!.rows, 4);
  const children = result.project.nodes.filter(node => node.parentId === grid.id);
  assert.deepEqual(children.slice(0, 5).map(node => node.cell), [{ column: 1, row: 1 }, { column: 1, row: 2 }, { column: 1, row: 3 }, { column: 1, row: 4 }, { column: 2, row: 1 }]);
  assert.ok(children.every(node => node.width === children[0].width && node.height === children[0].height && node.style!.shape === 'hexagon'));
  assert.deepEqual(validateProject(result.project), []);
  const legacy = applyRecipe(base, { version: 1, nodes: [{ key: 'legacy', canvas: base.entryCanvasId, kind: 'container', containerVariant: 'spatial' }] });
  assert.equal(legacy.project.nodes.find(node => node.id === legacy.aliases.nodes.legacy)!.containerVariant, 'contained');
});

test('converting an unaddressed large collection into a Window initializes sufficient full capacity without replacing children', () => {
  const project = newProject(), parent = addNode(project, project.entryCanvasId, 'container', 'Collected passages');
  const ids = Array.from({ length: 13 }, (_, at) => { const node = addNode(project, project.entryCanvasId, 'text', `Entry ${at}`); node.parentId = parent.id; return node.id; });
  const before = structuredClone(project), converted = updateNodeType(project, parent.id, { kind: 'container', containerVariant: 'window' });
  assert.deepEqual(project, before); const window = converted.nodes.find(node => node.id === parent.id)!;
  assert.equal(window.grid!.columns, 3); assert.equal(window.grid!.rows, 5); assert.equal(window.grid!.windowRows, 3);
  assert.deepEqual(converted.nodes.filter(node => node.parentId === window.id).map(node => node.id), ids); assert.deepEqual(validateProject(converted), []);
});
