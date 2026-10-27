import test from 'node:test';
import assert from 'node:assert/strict';
import { addCanvas, addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import { createLinkPath, splitLinkWithJunction } from '../src/core/connections.ts';
import { CREATION_TYPES, createLinkFromSpec, createNodeFromSpec, type LinkCreationSpec } from '../src/core/creation.ts';
import { updateNodeType } from '../src/core/node-editing.ts';
import { evaluate } from '../src/core/engine.ts';
import { isProjectShape, validateProject } from '../src/core/rules.ts';
import { isLinkStyle, linkLineStyle } from '../src/core/appearance.ts';
import { applyRecipe } from '../src/core/authoring.ts';
import { buildPublication, readPublication } from '../src/publication.ts';
import type { MeridianProject } from '../src/core/types.ts';
import { arrangeChildren, normalizeContainerLayouts } from '../src/core/grid.ts';
import { gridGeometry, orderedPattern, resolveGridCell } from '../src/core/cells.ts';
import { sizingBounds } from '../src/core/sizing.ts';

function fixture(kind: LinkCreationSpec['kind'] = 'visual') {
  const project = newProject('Routing utility review'), canvasId = project.entryCanvasId, parentId = project.canvases[0].rootId;
  const source = addNode(project, canvasId, kind === 'visual' ? 'text' : kind === 'numeric' ? 'number' : 'input', 'Source');
  const target = addNode(project, canvasId, kind === 'visual' ? 'text' : 'output', 'Destination');
  if (kind !== 'visual') { source.value = kind === 'numeric' ? 27 : true; target.valueType = kind === 'numeric' ? 'number' : 'boolean'; }
  const spec: LinkCreationSpec = { canvasId, parentId, sourceId: source.id, targetId: target.id, kind, relation: 'generic', direction: kind === 'visual' ? 'both' : 'forward', label: 'A connection', style: { color: '#aabbcc', width: 12, lineStyle: 'dotted' } };
  return { project, source, target, spec, canvasId, parentId };
}

test('Utility creation exposes Pivot and Joint through one family without replacing operation nodes', () => {
  const f = fixture();
  assert.ok(CREATION_TYPES.some(type => type.value === 'utility' && type.kind === 'junction'));
  assert.ok(!CREATION_TYPES.some(type => type.value === 'junction'));
  assert.ok(CREATION_TYPES.some(type => type.value === 'joint' && type.kind === 'joint'));
  for (const utilityVariant of ['pivot', 'joint'] as const) {
    const created = createNodeFromSpec(f.project, { canvasId: f.canvasId, parentId: f.parentId, type: 'utility', utilityVariant, x: 200, y: 240, width: 12, height: 12, title: utilityVariant });
    const node = created.project.nodes.find(node => node.id === created.nodeId)!;
    assert.equal(node.kind, 'junction'); assert.equal(node.utilityVariant, utilityVariant); assert.equal(node.sizeMode, 'fixed'); assert.equal(node.style?.shape, 'circle'); assert.equal(node.valueType, undefined);
    assert.deepEqual(validateProject(created.project), []);
  }
  const legacy = createNodeFromSpec(f.project, { canvasId: f.canvasId, parentId: f.parentId, type: 'junction', x: 1, y: 2, width: 12, height: 12, title: 'Legacy' });
  assert.equal(legacy.project.nodes.find(node => node.id === legacy.nodeId)?.utilityVariant, 'joint');
  const converted = updateNodeType(f.project, f.source.id, { kind: 'junction', utilityVariant: 'pivot' });
  assert.equal(converted.nodes.find(node => node.id === f.source.id)?.width, 12);
  assert.equal(updateNodeType(converted, f.source.id, { kind: 'text' }).nodes.find(node => node.id === f.source.id)?.utilityVariant, undefined);
});

test('a completed route commits compact movable pivots and styled segments atomically', () => {
  const f = fixture(), before = structuredClone(f.project), positions = [{ x: 350, y: 180 }, { x: 490, y: 420 }];
  const result = createLinkPath(f.project, f.spec, positions);
  assert.deepEqual(f.project, before); assert.equal(result.nodeIds.length, 2); assert.equal(result.linkIds.length, 3); assert.equal(result.linkId, result.linkIds[2]);
  for (const [index, id] of result.nodeIds.entries()) {
    const node = result.project.nodes.find(node => node.id === id)!;
    assert.equal(node.utilityVariant, 'pivot'); assert.equal(node.parentId, f.parentId);
    assert.deepEqual([node.x + node.width / 2, node.y + node.height / 2], [positions[index].x, positions[index].y]);
    assert.equal(node.placement, 'free'); assert.equal(node.snapping?.stickyEdges, false);
    assert.equal(node.style?.fill, f.spec.style?.color);
  }
  const endpoints = [f.source.id, ...result.nodeIds, f.target.id];
  for (const [index, id] of result.linkIds.entries()) {
    const link = result.project.links.find(link => link.id === id)!;
    assert.deepEqual([link.sourceId, link.targetId], endpoints.slice(index, index + 2));
    assert.deepEqual(link.style, f.spec.style); assert.equal(link.parentId, f.parentId); assert.equal(link.direction, 'both');
    assert.equal(link.label, index === 2 ? f.spec.label : undefined);
  }
  const firstPivot = result.project.nodes.find(node => node.id === result.nodeIds[0])!;
  firstPivot.x += 300; firstPivot.y -= 90;
  assert.equal(result.project.links.find(link => link.id === result.linkIds[0])!.targetId, firstPivot.id);
  assert.deepEqual(validateProject(result.project), []);
});

test('split insertion keeps original link identity, anchors and route while a customized utility occupies the clicked center', () => {
  const f = fixture(), direct = createLinkFromSpec(f.project, f.spec), link = direct.project.links[0];
  link.sourceAnchor = { x: 1, y: .3 }; link.targetAnchor = { x: 0, y: .7 }; link.points = [{ x: 300, y: 120 }, { x: 600, y: 120 }];
  const result = splitLinkWithJunction(direct.project, link.id, { x: 400, y: 120 }, { utilityVariant: 'pivot', width: 32, height: 6, style: { shape: 'rectangle', fill: '#336699', borderWidth: 0 }, segmentIndex: 1 });
  const node = result.project.nodes.find(node => node.id === result.nodeId)!, first = result.project.links.find(link => link.id === result.sourceLinkId)!, last = result.project.links.find(link => link.id === result.targetLinkId)!;
  assert.deepEqual([node.x, node.y, node.width, node.height], [384, 117, 32, 6]); assert.equal(node.utilityVariant, 'pivot');
  assert.equal(last.id, link.id); assert.equal(last.label, link.label); assert.equal(first.label, undefined);
  assert.deepEqual(first.sourceAnchor, link.sourceAnchor); assert.deepEqual(last.targetAnchor, link.targetAnchor);
  assert.deepEqual(first.points, [link.points[0]]); assert.deepEqual(last.points, [link.points[1]]);
  assert.deepEqual(first.style, link.style); assert.deepEqual(last.style, link.style);
  assert.deepEqual(validateProject(result.project), []);
});

test('routing numeric and Boolean values retains the pass-through contract across pivots', () => {
  for (const kind of ['numeric', 'functional'] as const) {
    const f = fixture(kind), result = createLinkPath(f.project, f.spec, [{ x: 260, y: 220 }, { x: 340, y: 220 }]);
    assert.equal(evaluate(result.project).values[f.target.id], f.source.value);
    const first = result.project.nodes.find(node => node.id === result.nodeIds[0])!;
    assert.equal(first.valueType, f.target.valueType);
    const another = addNode(result.project, f.canvasId, kind === 'numeric' ? 'number' : 'input'); another.value = f.source.value;
    assert.throws(() => createLinkFromSpec(result.project, { ...f.spec, sourceId: another.id, targetId: first.id }), /one connection/);
  }
});

test('a visual joint accepts many routes and one shared continuation without inventing signal evaluation', () => {
  const f = fixture(), direct = createLinkFromSpec(f.project, f.spec), result = splitLinkWithJunction(direct.project, direct.linkId, { x: 250, y: 250 });
  const joint = result.project.nodes.find(node => node.id === result.nodeId)!, another = addNode(result.project, f.canvasId, 'text', 'Another origin');
  const branched = createLinkFromSpec(result.project, { ...f.spec, sourceId: another.id, targetId: joint.id });
  assert.equal(joint.utilityVariant, 'joint'); assert.equal(branched.project.links.filter(link => link.targetId === joint.id).length, 2);
  assert.equal(branched.project.links.filter(link => link.sourceId === joint.id).length, 1);
  assert.equal(evaluate(branched.project).values[joint.id], null);
});

test('styles, utility subtypes, saved observations and declarative recipes survive portable publication', () => {
  const f = fixture(), path = createLinkPath(f.project, f.spec, [{ x: 400, y: 190 }]), committed = commitSnapshots(path.project);
  const payload = readPublication(buildPublication(committed, '', ''));
  assert.equal(payload.nodes.find(node => node.kind === 'junction')?.utilityVariant, 'pivot');
  assert.ok(payload.links.every(link => link.style?.lineStyle === 'dotted' && link.style?.width === 12));
  assert.equal(committed.snapshots[path.nodeIds[0]].node.utilityVariant, 'pivot');
  const recipe = applyRecipe(f.project, { version: 1, nodes: [{ key: 'turn', canvas: f.canvasId, kind: 'junction', utilityVariant: 'pivot', x: 500, y: 250, width: 24, height: 4, style: { shape: 'rectangle', fill: '#abcdef' } }],
    links: [{ key: 'in', source: f.source.id, target: 'turn', kind: 'visual', relation: 'generic', style: { lineStyle: 'solid', width: 32 } }] });
  assert.equal(recipe.project.nodes.find(node => node.id === recipe.aliases.nodes.turn)?.utilityVariant, 'pivot');
  assert.equal(recipe.project.links[0].style?.lineStyle, 'solid');
});

test('invalid styles, variants, pivot positions and inherited rules reject without changing the source graph', () => {
  const f = fixture(), before = structuredClone(f.project);
  for (const lineStyle of ['solid', 'dashed', 'dotted'] as const) assert.ok(isLinkStyle({ lineStyle, width: 32, color: '#123456' }));
  for (const style of [{ lineStyle: 'striped' }, { lineStyle: null }, { width: 33 }, { color: 'red' }]) assert.equal(isLinkStyle(style), false);
  assert.equal(linkLineStyle({ dashed: true }), 'dashed'); assert.equal(linkLineStyle({ dashed: false }, 'dashed'), 'solid'); assert.equal(linkLineStyle(undefined, 'dashed'), 'dashed'); assert.equal(linkLineStyle({ dashed: true, lineStyle: 'dotted' }), 'dotted');
  for (const change of [(p: MeridianProject) => { p.nodes[1].utilityVariant = 'pivot'; }, (p: MeridianProject) => { p.nodes[1].kind = 'junction'; p.nodes[1].utilityVariant = 'unknown' as never; }]) {
    const invalid = structuredClone(f.project); change(invalid); assert.equal(isProjectShape(invalid), false);
  }
  assert.throws(() => createLinkPath(f.project, f.spec, [{ x: NaN, y: 0 }]), /finite/);
  assert.throws(() => createLinkPath(f.project, { ...f.spec, style: { lineStyle: 'unsafe' as never } }, [{ x: 1, y: 2 }]), /malformed/);
  const restricted = structuredClone(f.project); restricted.nodes[0].rules = { nodeKinds: ['text'] };
  assert.throws(() => createLinkPath(restricted, f.spec, [{ x: 20, y: 20 }]), /inherited/);
  assert.deepEqual(f.project, before); assert.equal(restricted.nodes.length, f.project.nodes.length); assert.equal(restricted.links.length, 0);
});

test('routing utilities remain compact overlays through ordered normalization and saved-document reopening', () => {
  for (const variant of ['horizontal', 'vertical', 'grid', 'spreadsheet', 'window'] as const) {
    let project = newProject(`Overlay ${variant}`), parent = addNode(project, project.entryCanvasId, 'container', 'Owner');
    Object.assign(parent, { containerVariant: variant, childLayout: 'contained', collapsed: false, sizeMode: 'auto', x: 220, y: 160,
      grid: { columns: 3, rows: 3, cellWidth: 150, cellHeight: 90, fillOrder: 'row', ...(variant === 'window' ? { windowColumns: 2, windowRows: 2, scrollDirection: 'vertical' } : {}) } });
    const contentIds: string[] = [];
    for (let index = 0; index < 9; index++) {
      const child = addNode(project, parent.canvasId, 'text', `Cell ${index}`); child.parentId = parent.id; child.text = 'A short note'; child.style = { shape: 'rectangle' }; contentIds.push(child.id);
    }
    project = arrangeChildren(project, parent.id, orderedPattern(parent)).project; parent = project.nodes.find(node => node.id === parent.id)!;
    const beforeGeometry = gridGeometry(parent, project), beforeContent = project.nodes.filter(node => contentIds.includes(node.id)).map(node => ({ id: node.id, x: node.x, y: node.y, width: node.width, height: node.height, cell: node.cell, shape: node.style?.shape }));
    const path = createLinkPath(project, { canvasId: parent.canvasId, parentId: parent.id, sourceId: contentIds[0], targetId: contentIds[8], kind: 'visual', relation: 'generic', direction: 'none', style: { lineStyle: 'solid', color: '#4499aa' } }, [{ x: parent.x + 50, y: parent.y + 15 }]);
    const split = splitLinkWithJunction(path.project, path.linkId, { x: parent.x + 100, y: parent.y + 15 }, { utilityVariant: 'joint', width: 32, height: 6, style: { shape: 'rectangle', fill: '#cc7722' } });
    const utilityIds = [...path.nodeIds, split.nodeId], beforeUtilities = split.project.nodes.filter(node => utilityIds.includes(node.id));
    const normalized = normalizeContainerLayouts(split.project), normalizedParent = normalized.nodes.find(node => node.id === parent.id)!;
    assert.deepEqual(gridGeometry(normalizedParent, normalized), beforeGeometry, variant);
    assert.deepEqual(normalized.nodes.filter(node => contentIds.includes(node.id)).map(node => ({ id: node.id, x: node.x, y: node.y, width: node.width, height: node.height, cell: node.cell, shape: node.style?.shape })), beforeContent, variant);
    assert.deepEqual(normalized.nodes.filter(node => utilityIds.includes(node.id)), beforeUtilities, variant);
    for (const utility of beforeUtilities) {
      assert.equal(utility.cell, undefined); const bounds = sizingBounds(normalized, normalized.nodes.find(node => node.id === utility.id)!);
      assert.equal(bounds.width, utility.width, variant); assert.equal(bounds.height, utility.height, variant);
    }
    const reopened = normalizeContainerLayouts(JSON.parse(JSON.stringify(commitSnapshots(normalized))));
    assert.deepEqual(reopened.nodes.filter(node => utilityIds.includes(node.id)), beforeUtilities, variant);
    assert.deepEqual(validateProject(reopened), [], variant);
  }
});

test('legacy addressed numeric utilities keep coordinate aliases, values and cell reservations after normalization', () => {
  const project = newProject('Legacy utility coordinates'), canvasId = project.entryCanvasId;
  const parent = addNode(project, canvasId, 'container', 'Legacy ordered grid'); Object.assign(parent, { containerVariant: 'grid', childLayout: 'contained', collapsed: false, grid: { columns: 4, rows: 4, cellWidth: 180, cellHeight: 100, fillOrder: 'row' } });
  const source = addNode(project, canvasId, 'number', 'Value'); source.parentId = parent.id; source.value = 42;
  const utility = addNode(project, canvasId, 'junction', 'Addressed joint'); Object.assign(utility, { parentId: parent.id, utilityVariant: 'joint', valueType: 'number', x: 455, y: 302, width: 32, height: 6, sizeMode: 'fixed', style: { shape: 'rectangle', fill: '#3399cc' }, cell: { column: 4, row: 4, name: 'legacy-result' } });
  project.links.push({ id: 'legacy-signal', canvasId, parentId: parent.id, sourceId: source.id, targetId: utility.id, kind: 'numeric', relation: 'generic' });
  const local = addNode(project, canvasId, 'reference', 'Local cell'); local.targetId = parent.id; local.targetCell = '4:4';
  const externalCanvas = addCanvas(project, 'Saved cell'), external = addNode(project, externalCanvas.id, 'reference', 'External cell'); external.targetId = parent.id; external.targetCell = 'legacy-result';
  const normalized = normalizeContainerLayouts(project), preserved = normalized.nodes.find(node => node.id === utility.id)!;
  assert.deepEqual(preserved, utility); assert.equal(normalized.nodes.find(node => node.id === parent.id)?.grid?.columns, 4); assert.equal(normalized.nodes.find(node => node.id === parent.id)?.grid?.rows, 4);
  assert.equal(resolveGridCell(normalized, parent.id, '4:4')?.id, utility.id); assert.equal(resolveGridCell(normalized, parent.id, 'D4')?.id, utility.id);
  assert.equal(evaluate(normalized).values[local.id], 42); assert.notDeepEqual(normalized.nodes.find(node => node.id === source.id)?.cell, utility.cell);
  const saved = commitSnapshots(normalized), reopened = normalizeContainerLayouts(JSON.parse(JSON.stringify(saved)));
  assert.equal(resolveGridCell(reopened, parent.id, 'legacy-result', { savedOnly: true })?.id, utility.id);
  assert.equal(evaluate(reopened).values[external.id], 42); assert.deepEqual(reopened.nodes.find(node => node.id === utility.id), utility); assert.deepEqual(validateProject(reopened), []);
});
