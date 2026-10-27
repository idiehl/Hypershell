import test from 'node:test';
import assert from 'node:assert/strict';
import { addCanvas, addNode, commitSnapshots, forkProject, newProject } from '../src/core/model.ts';
import { arrangeChildren, cellAddress, columnLabel, exportCsv, gridCellBounds, gridGeometry, isCellName, parseCellAddress, parseCsv, resolveGridCell, resolveReferenceTarget } from '../src/core/grid.ts';
import { evaluate } from '../src/core/engine.ts';
import { outputValueType } from '../src/core/operations.ts';
import { isProjectShape, validateProject } from '../src/core/rules.ts';
import { createProjectSearch, navigationIndex } from '../src/core/navigation.ts';
import { createPublicationPayload, readPublication, buildPublication } from '../src/publication.ts';
import { createSnippet } from '../src/core/selection.ts';
import { summarizeDependencies } from '../src/core/dependencies.ts';
import { applyRecipe } from '../src/core/authoring.ts';
import type { ChildArrangement, MeridianProject } from '../src/core/types.ts';
import { intrinsicContentSize } from '../src/core/sizing.ts';

function fixture(sizeMode: 'auto' | 'fixed' = 'fixed') {
  const project = newProject('Addressable cells'), canvasId = project.entryCanvasId;
  const sheet = addNode(project, canvasId, 'spreadsheet', 'Sources');
  sheet.sizeMode = sizeMode;
  const text = addNode(project, canvasId, 'text', 'Citation'); Object.assign(text, { parentId: sheet.id, cell: { column: 1, row: 1, name: 'primary_source' }, text: 'Hesiod, Theogony', width: 140, height: 70, sizeMode: 'fixed' });
  const number = addNode(project, canvasId, 'number', 'Count'); Object.assign(number, { parentId: sheet.id, cell: { column: 2, row: 1 }, value: 24, width: 170, height: 80, sizeMode: 'fixed' });
  return { project, canvasId, sheet, text, number };
}
const find = (project: MeridianProject, id: string) => project.nodes.find(node => node.id === id)!;

test('coordinate labels cross alphabet boundaries, roundtrip and reject out-of-range or ambiguous names', () => {
  assert.equal(columnLabel(1), 'A'); assert.equal(columnLabel(26), 'Z'); assert.equal(columnLabel(27), 'AA'); assert.equal(columnLabel(512), 'SR');
  for (const column of [1, 26, 27, 52, 512]) assert.deepEqual(parseCellAddress(cellAddress(column, 512)), { column, row: 512 });
  assert.deepEqual(parseCellAddress('aa12'), { column: 27, row: 12 });
  for (const bad of ['A0', 'A513', 'SS1', 'A01', '-A1', 'A1!B2']) assert.equal(parseCellAddress(bad), undefined);
  assert.equal(isCellName('source_index'), true); assert.equal(isCellName('A1'), false); assert.equal(isCellName('Name with spaces'), false);
});

test('spreadsheet defaults are expanded formal containers with bounded visible cells and compact collapse geometry', () => {
  const f = fixture('auto'); assert.deepEqual(validateProject(f.project), []);
  assert.equal(f.sheet.childLayout, 'contained'); assert.equal(f.sheet.collapsed, false);
  const geometry = gridGeometry(f.sheet, f.project); assert.equal(geometry.width, navigationIndex(f.project).bounds(f.sheet).width); assert.equal(geometry.height, navigationIndex(f.project).bounds(f.sheet).height);
  assert.equal(gridCellBounds(f.sheet, { column: 1, row: 1 }).x, f.sheet.x + 6);
  f.sheet.collapsed = true; assert.equal(navigationIndex(f.project).bounds(f.sheet).width, intrinsicContentSize(f.project, f.sheet).width);
  assert.equal(navigationIndex(f.project).isNodeVisible(f.text.id), false); assert.equal(f.sheet.width, 666);
});

test('address/name lookup resolves only immediate children within the selected parent', () => {
  const f = fixture(), inner = addNode(f.project, f.canvasId, 'text', 'Nested'); inner.parentId = f.text.id; inner.cell = { column: 1, row: 1 };
  assert.equal(resolveGridCell(f.project, f.sheet.id, 'A1')?.id, f.text.id);
  assert.equal(resolveGridCell(f.project, f.sheet.id, 'PRIMARY_SOURCE')?.id, f.text.id);
  assert.equal(resolveGridCell(f.project, f.sheet.id, 'C1'), undefined);
  assert.equal(resolveGridCell(f.project, f.text.id, 'A1')?.id, inner.id);
});

test('grid schema refuses fractional geometry, excessive extents, unknown fields and malformed reference addresses', () => {
  for (const patch of [{ columns: 0 }, { rows: 1.5 }, { columns: 512, rows: 512 }, { cellWidth: Infinity }, { cellHeight: 35 }, { gap: -1 }, { unknown: true }]) {
    const f = fixture(); Object.assign(f.sheet.grid!, patch); assert.equal(isProjectShape(f.project), false, JSON.stringify(patch));
  }
  for (const cell of [{ column: 0, row: 1 }, { column: 1, row: 513 }, { column: 1, row: 1, name: 'B9' }, { column: 1, row: 1, extra: true }]) {
    const f = fixture(); Object.assign(f.text, { cell }); assert.equal(isProjectShape(f.project), false);
  }
  const f = fixture(); f.text.targetCell = 'A1'; f.text.targetId = f.sheet.id;
  assert.ok(validateProject(f.project).some(issue => /Only references/.test(issue.message)));
});

test('coordinate and name occupancy is unique per parent while independent grids may reuse both', () => {
  const f = fixture(); f.number.cell = { column: 1, row: 1, name: 'PRIMARY_SOURCE' };
  const issues = validateProject(f.project); assert.ok(issues.some(issue => /coordinate/.test(issue.message))); assert.ok(issues.some(issue => /names/.test(issue.message)));
  const other = addNode(f.project, f.canvasId, 'spreadsheet', 'Other'); f.number.parentId = other.id; assert.deepEqual(validateProject(f.project), []);
  f.number.cell.column = 4; assert.ok(validateProject(f.project).some(issue => /fit the parent/.test(issue.message)));
});

test('same-canvas cell aliases supply live numeric signals and detect address-based feedback', () => {
  const f = fixture(), ref = addNode(f.project, f.canvasId, 'reference', 'Count here'); ref.targetId = f.sheet.id; ref.targetCell = 'B1';
  assert.equal(evaluate(f.project).values[ref.id], 24); assert.equal(outputValueType(f.project, ref), 'number');
  f.number.value = 50; assert.equal(evaluate(f.project).values[ref.id], 50);
  f.number.kind = 'reference'; f.number.targetId = f.sheet.id; f.number.targetCell = 'B1'; delete f.number.value;
  assert.ok(validateProject(f.project).some(issue => /feedback/.test(issue.message)));
});

test('cross-canvas cell aliases preserve saved coordinate identity, name, text, attachment and numeric value until next commit', () => {
  let f = fixture(); const other = addCanvas(f.project, 'Elsewhere'), ref = addNode(f.project, other.id, 'reference', 'Saved count'); ref.targetId = f.sheet.id; ref.targetCell = 'B1';
  let project = commitSnapshots(f.project); find(project, f.number.id).cell = { column: 3, row: 3, name: 'new_name' }; find(project, f.number.id).value = 99;
  assert.equal(resolveReferenceTarget(project, find(project, ref.id))?.node.id, f.number.id); assert.equal(evaluate(project).values[ref.id], 24);
  project = commitSnapshots(project); assert.equal(evaluate(project).values[ref.id], null);
  find(project, ref.id).targetCell = 'new_name'; assert.equal(evaluate(project).values[ref.id], 99);
});

test('missing external parent/cell observations stay unresolved and never fall through to another canvas live cell', () => {
  const f = fixture(), other = addCanvas(f.project, 'Elsewhere'), ref = addNode(f.project, other.id, 'reference', 'Unavailable'); ref.targetId = f.sheet.id; ref.targetCell = 'B1';
  assert.equal(resolveReferenceTarget(f.project, ref), undefined); assert.equal(evaluate(f.project).values[ref.id], null);
  const saved = commitSnapshots(f.project); delete saved.snapshots[f.number.id]; assert.equal(resolveReferenceTarget(saved, find(saved, ref.id)), undefined);
});

test('spreadsheet arrangement preserves addresses and requested cell metadata while fitting uniform tiles into the parent frame', () => {
  const f = fixture(); f.text.cell = { column: 2, row: 2, name: 'primary_source' };
  f.number.cell = undefined; f.number.width = 370; f.number.height = 190;
  const before = structuredClone(f.project), arranged = arrangeChildren(f.project, f.sheet.id, 'grid').project;
  const sheet = find(arranged, f.sheet.id), number = find(arranged, f.number.id), text = find(arranged, f.text.id);
  assert.deepEqual(text.cell, before.nodes.find(node => node.id === text.id)!.cell); assert.deepEqual(number.cell, { column: 1, row: 1 });
  const geometry = gridGeometry(sheet, arranged);
  assert.equal(number.width, geometry.cellWidth); assert.equal(number.height, geometry.cellHeight); assert.equal(text.width, geometry.cellWidth); assert.equal(text.height, geometry.cellHeight); assert.equal(sheet.grid!.cellWidth, 200); assert.equal(sheet.grid!.cellHeight, 100);
  const box = gridCellBounds(sheet, text.cell!, arranged); assert.equal(text.x, box.x); assert.equal(text.y, box.y);
  assert.equal(sheet.width, f.sheet.width); assert.equal(sheet.height, f.sheet.height); assert.deepEqual(f.project, before);
});

test('all patterns move hidden branches and internal route bends while preserving cross-branch authored bends', () => {
  for (const pattern of ['grid', 'row', 'column', 'radial', 'stack'] as ChildArrangement[]) {
    const f = fixture(); f.text.collapsed = true;
    f.sheet.kind = 'container'; f.sheet.containerVariant = 'contained';
    const child = addNode(f.project, f.canvasId, 'text', 'Hidden'); Object.assign(child, { parentId: f.text.id, x: 400, y: 800 });
    const internal = { id: crypto.randomUUID(), canvasId: f.canvasId, sourceId: f.text.id, targetId: child.id, kind: 'visual' as const, relation: 'generic' as const, points: [{ x: 450, y: 650 }] };
    const cross = { ...internal, id: crypto.randomUUID(), targetId: f.number.id }; f.project.links.push(internal, cross);
    const arranged = arrangeChildren(f.project, f.sheet.id, pattern).project, moved = find(arranged, f.text.id), dx = moved.x - f.text.x, dy = moved.y - f.text.y;
    assert.equal(find(arranged, child.id).x, child.x + dx); assert.equal(find(arranged, child.id).y, child.y + dy); assert.equal(moved.collapsed, true);
    assert.deepEqual(arranged.links.find(link => link.id === internal.id)!.points, [{ x: 450 + dx, y: 650 + dy }]);
    if (dx !== find(arranged, f.number.id).x - f.number.x || dy !== find(arranged, f.number.id).y - f.number.y) assert.deepEqual(arranged.links.find(link => link.id === cross.id)!.points, cross.points);
    assert.deepEqual(validateProject(arranged), []);
  }
});

test('spatial grid places cells below the parent instead of enlarging its tile, with stable collapse addresses', () => {
  const f = fixture(); Object.assign(f.sheet, { kind: 'text', childLayout: 'spatial', width: 350, height: 150 });
  delete f.sheet.containerVariant;
  const arranged = arrangeChildren(f.project, f.sheet.id, 'grid', { gap: 20 }).project;
  assert.equal(find(arranged, f.sheet.id).height, 150); assert.ok(find(arranged, f.text.id).y >= f.sheet.y + 170);
  assert.equal(resolveGridCell(arranged, f.sheet.id, 'primary_source')?.id, f.text.id);
});

test('reducing occupied grid dimensions fails atomically instead of silently changing reference coordinates', () => {
  const f = fixture(); f.sheet.grid!.columns = 1; const before = structuredClone(f.project);
  assert.throws(() => arrangeChildren(f.project, f.sheet.id, 'grid'), /fit the parent/); assert.deepEqual(f.project, before);
  f.sheet.grid!.columns = 3; assert.throws(() => arrangeChildren(f.project, f.sheet.id, 'grid', { columns: 1 }), /Move occupied/);
});

test('radial arrangement avoids overlap between very different tile sizes', () => {
  const f = fixture(); f.text.width = 700; f.text.height = 400;
  f.sheet.kind = 'container'; f.sheet.containerVariant = 'contained';
  for (let index = 0; index < 8; index++) { const child = addNode(f.project, f.canvasId, 'text', `Small ${index}`); child.parentId = f.sheet.id; child.width = 80; child.height = 60; child.sizeMode = 'fixed'; }
  const arranged = arrangeChildren(f.project, f.sheet.id, 'radial').project, boxes = arranged.nodes.filter(node => node.parentId === f.sheet.id);
  for (let a = 0; a < boxes.length; a++) for (let b = a + 1; b < boxes.length; b++) assert.ok(boxes[a]!.x + boxes[a]!.width <= boxes[b]!.x || boxes[b]!.x + boxes[b]!.width <= boxes[a]!.x || boxes[a]!.y + boxes[a]!.height <= boxes[b]!.y || boxes[b]!.y + boxes[b]!.height <= boxes[a]!.y);
});

test('cell-reference publication contains both external grid identity and selected cell snapshot and strips unknown subfields', () => {
  const f = fixture(), other = addCanvas(f.project, 'Elsewhere'), ref = addNode(f.project, other.id, 'reference', 'Read source'); ref.targetId = f.sheet.id; ref.targetCell = 'primary_source';
  const saved = commitSnapshots(f.project); saved.nodes = saved.nodes.filter(node => node.canvasId === other.id); saved.canvases = [other]; saved.entryCanvasId = other.id;
  Object.assign(saved.snapshots[f.sheet.id]!.node.grid!, { privateField: 'strip' }); Object.assign(saved.snapshots[f.text.id]!.node.cell!, { privateField: 'strip' });
  const payload = createPublicationPayload(saved); assert.ok(payload.project.snapshots[f.sheet.id]); assert.ok(payload.project.snapshots[f.text.id]);
  assert.equal((payload.project.snapshots[f.sheet.id]!.node.grid! as unknown as Record<string, unknown>).privateField, undefined);
  assert.equal((payload.project.snapshots[f.text.id]!.node.cell! as unknown as Record<string, unknown>).privateField, undefined);
  const clean = structuredClone(saved); delete (clean.snapshots[f.sheet.id]!.node.grid! as unknown as Record<string, unknown>).privateField; delete (clean.snapshots[f.text.id]!.node.cell! as unknown as Record<string, unknown>).privateField;
  const reopened = readPublication(buildPublication(clean, '', '')); const restoredRef = reopened.nodes.find(node => node.kind === 'reference')!;
  assert.equal(resolveReferenceTarget(reopened, restoredRef)?.node.text, f.text.text);
});

test('forking and selected snippets preserve coordinate references without creating live copies of outside cells', () => {
  const f = fixture(), ref = addNode(f.project, f.canvasId, 'reference', 'Cell alias'); ref.targetId = f.sheet.id; ref.targetCell = 'B1';
  const saved = commitSnapshots(f.project), fork = forkProject(saved), forkRef = fork.nodes.find(node => node.kind === 'reference')!;
  assert.notEqual(forkRef.targetId, f.sheet.id); assert.equal(evaluate(fork).values[forkRef.id], 24);
  const snippet = createSnippet(saved, [ref.id], { includeDescendants: true, includeExternalReferences: true });
  const snippetRef = snippet.project.nodes.find(node => node.kind === 'reference')!;
  assert.equal(evaluate(snippet.project).values[snippetRef.id], 24); assert.equal(snippet.project.nodes.length, 2); assert.equal(snippet.report.includedSnapshots, 2);
  const outsideChild = createSnippet(saved, [f.sheet.id, ref.id], { includeDescendants: false, includeExternalReferences: true });
  assert.equal(evaluate(outsideChild.project).values[outsideChild.idMap[ref.id]!], 24);
});

test('dependency summary and project search include coordinate targets and full saved cell text', () => {
  const f = fixture(), ref = addNode(f.project, f.canvasId, 'reference', 'Cell alias'); ref.targetId = f.sheet.id; ref.targetCell = 'primary_source';
  assert.ok(summarizeDependencies(f.project, { rootIds: [ref.id] }).entries.some(item => item.id === f.text.id));
  assert.ok(createProjectSearch(f.project)('primary_source').some(item => item.nodeId === f.text.id));
  assert.ok(createProjectSearch(f.project)('Theogony').some(item => item.nodeId === ref.id));
});

test('declarative recipes create spreadsheet/cell fields atomically and enforce address uniqueness', () => {
  const base = newProject('Recipe'), canvas = base.entryCanvasId;
  const recipe = { version: 1 as const, nodes: [{ key: 'sheet', canvas, kind: 'spreadsheet' as const }, { key: 'cell', canvas, parent: 'sheet', kind: 'number' as const, cell: { column: 1, row: 1, name: 'count' }, value: 7 }, { key: 'ref', canvas, kind: 'reference' as const, target: 'sheet', targetCell: 'count' }] };
  const authored = applyRecipe(base, recipe); assert.equal(evaluate(authored.project).values[authored.aliases.nodes.ref!], 7);
  assert.throws(() => applyRecipe(base, { ...recipe, nodes: [...recipe.nodes, { key: 'duplicate', canvas, parent: 'sheet', kind: 'number', cell: { column: 1, row: 1 }, value: 9 }] }), /coordinate/);
  assert.equal(base.nodes.length, 1);
});

test('CSV/TSV parsing roundtrips quoted Unicode multiline fields and refuses malformed or excessive data', () => {
  const rows = [['Title', 'Source'], ['Hésiod', 'A "quoted", source\nSecond line'], ['', '24']];
  assert.deepEqual(parseCsv(exportCsv(rows)), rows); assert.deepEqual(parseCsv('a\tb\r\n1\t2', '\t'), [['a', 'b'], ['1', '2']]);
  assert.deepEqual(parseCsv('\ufeffTitle,Source\nOne,Two'), [['Title', 'Source'], ['One', 'Two']]);
  assert.throws(() => parseCsv('"unfinished'), /unfinished/); assert.throws(() => parseCsv('"text"junk'), /Unexpected/);
  assert.throws(() => parseCsv('x,'.repeat(513)), /columns/); assert.throws(() => parseCsv('x'.repeat(2_000_001)), /limited/);
});
