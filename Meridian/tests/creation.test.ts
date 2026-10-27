import test from 'node:test';
import assert from 'node:assert/strict';
import { addCanvas, addNode, newProject } from '../src/core/model.ts';
import { availableLinkPorts, createLinkFromSpec, createNodeFromSpec, creationOperations, creationPermissions, creationTypes } from '../src/core/creation.ts';
import type { NodeCreationSpec } from '../src/core/creation.ts';
import { validateProject } from '../src/core/rules.ts';
import { evaluate } from '../src/core/engine.ts';
import { navigationIndex } from '../src/core/navigation.ts';
import { resolveGridCell, gridCellBounds } from '../src/core/cells.ts';
import { revealChildren, isEdgeAttached } from '../src/core/reveal.ts';
import { nodeShape } from '../src/core/geometry.ts';
import { boundedChildInterior } from '../src/core/sizing.ts';

function fixture() {
  const project = newProject('Creation tools'), canvasId = project.entryCanvasId;
  return { project, canvasId, parentId: project.canvases[0]!.rootId };
}
function spec(f: ReturnType<typeof fixture>, patch: Partial<NodeCreationSpec> = {}): NodeCreationSpec {
  return { canvasId: f.canvasId, parentId: f.parentId, x: 420, y: 230, width: 280, height: 180, type: 'text', title: 'New thought', text: 'A useful passage', behavior: 'free', ...patch };
}

test('child creation uses automatic Freeform placement, reveals its parent, and leaves the original draft untouched', () => {
  const f = fixture(), parent = addNode(f.project, f.canvasId, 'text', 'A text node that also owns children'); parent.collapsed = true;
  const before = structuredClone(f.project), created = createNodeFromSpec(f.project, spec(f, { parentId: parent.id }));
  assert.deepEqual(f.project, before); const child = created.project.nodes.find(node => node.id === created.nodeId)!;
  assert.equal(child.parentId, parent.id); assert.equal(child.childPosition,'auto'); assert.ok(Number.isFinite(child.x)&&Number.isFinite(child.y));
  assert.equal(child.placement, 'free'); assert.equal(created.project.nodes.find(node => node.id === parent.id)!.collapsed, false);
  assert.equal(navigationIndex(created.project).isNodeVisible(child.id), true); assert.deepEqual(validateProject(created.project), []);
});

test('explicit Sticky and Align Parent edges remain attached on the right or top of an unbounded Freeform parent', () => {
  for (const representation of ['media', 'text', 'legacy-radial'] as const) for (const behavior of ['sticky', 'align'] as const) for (const attachSide of ['right', 'top'] as const) {
    const f = fixture(), parent = addNode(f.project, f.canvasId, representation === 'media' ? 'media' : 'text', 'Unbounded parent');
    Object.assign(parent, { x: 180, y: 140, width: 300, height: 180, sizeMode: 'fixed', collapsed: true, childLayout: 'spatial', style: { shape: 'rectangle' } });
    if (representation === 'media') Object.assign(parent, { containerVariant: 'contained', childArrangement: 'radial' });
    else if (representation === 'legacy-radial') parent.childArrangement = 'radial';
    const before = structuredClone(f.project), created = createNodeFromSpec(f.project, spec(f, { parentId: parent.id, behavior, attachSide, type: 'media', x: parent.x + 20, y: parent.y + 20, width: 120, height: 60, sizeMode: 'fixed', style: { shape: 'rectangle' } }));
    const index = navigationIndex(created.project), shown = index.nodes.get(created.nodeId)!, owner = index.nodes.get(parent.id)!, bounds = index.bounds(shown), frame = index.bounds(owner);
    assert.deepEqual(f.project, before); assert.equal(shown.childPosition, 'manual'); assert.equal(shown.placement, 'edge'); assert.equal(shown.attachSide, attachSide);
    assert.equal(shown.snapping!.stickyEdges, behavior === 'sticky'); assert.equal(shown.snapping!.smartAlign, behavior === 'align');
    if (representation === 'legacy-radial') { assert.equal(owner.containerVariant, 'contained'); assert.equal(owner.childArrangement, 'free'); }
    assert.ok(isEdgeAttached(nodeShape(owner, frame), nodeShape(shown, bounds)));
    if (attachSide === 'right') assert.equal(bounds.x, frame.x + frame.width);
    else assert.equal(bounds.y + bounds.height, frame.y);
    assert.deepEqual(revealChildren(created.project, parent.id, true), created.project, 'Repeated disclosure preserves the authored edge.');
    assert.deepEqual(validateProject(created.project), []);
  }
});

test('omitted, inherited and Free behavior keep the unbounded Freeform radial default even with a supplied parent edge', () => {
  for (const behavior of [undefined, 'inherit', 'free'] as const) {
    const f = fixture(), parent = addNode(f.project, f.canvasId, 'media', 'Automatic parent');
    Object.assign(parent, { x: 180, y: 140, width: 300, height: 180, sizeMode: 'fixed', collapsed: true, containerVariant: 'contained', childLayout: 'spatial', childArrangement: 'radial' });
    const before = structuredClone(f.project), created = createNodeFromSpec(f.project, spec(f, { parentId: parent.id, behavior, attachSide: 'right', type: 'media' }));
    const index = navigationIndex(created.project), shown = index.nodes.get(created.nodeId)!, owner = index.nodes.get(parent.id)!;
    assert.deepEqual(f.project, before); assert.equal(shown.childPosition, 'auto'); assert.equal(shown.placement, 'free');
    assert.ok(shown.x > owner.x + owner.width, 'The first automatic child occupies the exterior three o clock radial position.');
    assert.deepEqual(validateProject(created.project), []);
  }
});

test('bounded and ordered parents retain their layout authority over an explicit Sticky Parent edge', () => {
  for (const kind of ['container', 'media'] as const) for (const variant of ['contained', 'horizontal'] as const) {
    if (kind === 'media' && variant === 'contained') continue;
    const f = fixture(), parent = addNode(f.project, f.canvasId, kind, 'Pattern owner');
    Object.assign(parent, { x: 180, y: 140, width: 420, height: 240, sizeMode: 'fixed', collapsed: false, containerVariant: variant, childLayout: kind === 'container' ? 'contained' : 'spatial', childArrangement: variant === 'horizontal' ? 'row' : 'free' });
    if (variant === 'horizontal') parent.grid = { columns: 1, rows: 1, cellWidth: 140, cellHeight: 70, gap: 0 };
    const before = structuredClone(f.project), created = createNodeFromSpec(f.project, spec(f, { parentId: parent.id, behavior: 'sticky', attachSide: 'right', type: 'media', width: 140, height: 70, sizeMode: 'fixed', x: 950, y: 700 }));
    const index = navigationIndex(created.project), shown = index.nodes.get(created.nodeId)!, owner = index.nodes.get(parent.id)!, box = index.bounds(shown);
    assert.deepEqual(f.project, before); assert.equal(shown.childPosition, 'auto');
    if (variant === 'horizontal') {
      assert.deepEqual(shown.cell, { column: 1, row: 1 }); assert.deepEqual({ x: box.x, y: box.y, width: box.width, height: box.height }, gridCellBounds(owner, shown.cell!, created.project));
    } else {
      const area = boundedChildInterior(created.project, owner);
      assert.ok(box.x >= area.x && box.y >= area.y && box.x + box.width <= area.x + area.width + .001 && box.y + box.height <= area.y + area.height + .001);
    }
    assert.deepEqual(validateProject(created.project), []);
  }
});

test('a Canvas is created with an ordinary launcher in the source canvas, or reuses an existing root', () => {
  const f = fixture(), created = createNodeFromSpec(f.project, spec(f, { type: 'canvas', title: 'Divine genealogy' }));
  assert.equal(created.project.canvases.length, 2); assert.equal(created.project.entryCanvasId, f.canvasId);
  const canvas = created.project.canvases.find(canvas => canvas.id === created.canvasId)!, launcher = created.project.nodes.find(node => node.id === created.nodeId)!;
  assert.equal(launcher.kind, 'canvas-link'); assert.equal(launcher.canvasId, f.canvasId); assert.equal(launcher.targetId, canvas.rootId);
  const reused = createNodeFromSpec(created.project, spec(f, { type: 'canvas', title: 'Another entrance', canvasTargetId: canvas.rootId }));
  assert.equal(reused.project.canvases.length, 2); assert.equal(reused.canvasId, undefined); assert.deepEqual(validateProject(reused.project), []);
});

test('Canvas creation refuses snapshot-only roots that have no included workspace to open',()=>{
  const f=fixture(),other=newProject('Observed elsewhere'),root=other.nodes.find(node=>node.id===other.canvases[0].rootId)!;
  f.project.snapshots[root.id]={node:structuredClone(root),value:null,savedAt:new Date().toISOString()};
  const before=structuredClone(f.project);
  assert.throws(()=>createNodeFromSpec(f.project,spec(f,{type:'canvas',canvasTargetId:root.id})),/included Work workspace/);
  assert.deepEqual(f.project,before);
});

test('creation options intersect the immediate parent and every ancestor permission', () => {
  const f = fixture(), root = f.project.nodes.find(node => node.id === f.parentId)!;
  root.rules = { nodeKinds: ['container', 'text', 'joint', 'spreadsheet'], operations: ['AND', 'OR', 'NOT'], linkKinds: ['visual', 'functional'] };
  const parent = addNode(f.project, f.canvasId, 'container'); parent.rules = { nodeKinds: ['text', 'joint', 'spreadsheet'], operations: ['NOT', 'OR'], linkKinds: ['functional'] };
  assert.deepEqual(creationPermissions(f.project, parent.id), { nodeKinds: ['text', 'joint', 'spreadsheet'], operations: ['OR', 'NOT'], linkKinds: ['functional'] });
  assert.deepEqual(creationTypes(f.project, parent.id).map(type => type.value), ['container', 'joint']);
  assert.deepEqual(creationOperations(f.project, parent.id).map(operation => operation.operation), ['OR', 'NOT']);
  const before = structuredClone(f.project);
  assert.throws(() => createNodeFromSpec(f.project, spec(f, { parentId: parent.id, type: 'joint', operation: 'AND' })), /inherited rule/);
  assert.throws(() => createNodeFromSpec(f.project, spec(f, { parentId: parent.id, type: 'container', variant: 'spatial' })), /inherited rule/);
  assert.deepEqual(f.project, before);
  const allowed = createNodeFromSpec(f.project, spec(f, { parentId: parent.id, type: 'joint' }));
  assert.equal(allowed.project.nodes.find(node => node.id === allowed.nodeId)!.operation, 'OR');
});

test('container variants keep ordinary containment identities and CSV creates addressable cells', () => {
  const f = fixture(), created = createNodeFromSpec(f.project, spec(f, { type: 'container', variant: 'spreadsheet', title: 'Sources', grid: { columns: 2, rows: 2, cellWidth: 120, cellHeight: 70, gap: 0, showHeaders: true }, csv: 'Author,Title\nHesiod,Theogony' }));
  const container = created.project.nodes.find(node => node.id === created.nodeId)!;
  assert.equal(container.kind, 'spreadsheet'); assert.equal(container.childLayout, 'contained'); assert.equal(container.childArrangement, 'grid');
  assert.equal(container.containerVariant, 'spreadsheet');
  assert.equal(resolveGridCell(created.project, container.id, 'A2')!.text, 'Hesiod'); assert.equal(resolveGridCell(created.project, container.id, 'B2')!.text, 'Theogony');
  const gallery = createNodeFromSpec(f.project, spec(f, { type: 'container', variant: 'gallery' }));
  const galleryNode = gallery.project.nodes.find(node => node.id === gallery.nodeId)!;
  assert.equal(galleryNode.kind, 'container'); assert.equal(galleryNode.childLayout, 'contained'); assert.equal(galleryNode.childArrangement, 'free');
  assert.equal(galleryNode.grid, undefined);
  assert.equal(galleryNode.containerVariant, 'gallery');
  assert.deepEqual(validateProject(created.project), []); assert.deepEqual(validateProject(gallery.project), []);
});

test('Gallery creation rejects addressable-grid controls without modifying the draft', () => {
  const f = fixture(), before = structuredClone(f.project);
  for (const patch of [{ grid: { columns: 2, rows: 1, cellWidth: 120, cellHeight: 80 } }, { csv: 'Zeus,Athena' }]) {
    assert.throws(() => createNodeFromSpec(f.project, spec(f, { type: 'container', variant: 'gallery', ...patch })), /one at a time/);
    assert.deepEqual(f.project, before);
  }
});

test('new children fill unoccupied grid cells and refuse a full grid without changing the draft', () => {
  const f = fixture(), sheet = addNode(f.project, f.canvasId, 'spreadsheet', 'Small sheet'); sheet.grid = { columns: 1, rows: 1, cellWidth: 160, cellHeight: 90 };
  const created = createNodeFromSpec(f.project, spec(f, { parentId: sheet.id })), node = created.project.nodes.find(node => node.id === created.nodeId)!;
  assert.deepEqual(node.cell, { column: 1, row: 1 }); assert.equal(resolveGridCell(created.project, sheet.id, 'A1')!.id, node.id);
  const before = structuredClone(created.project); assert.throws(() => createNodeFromSpec(created.project, spec(f, { parentId: sheet.id })), /grid is full/); assert.deepEqual(created.project, before);
});

test('edge alignment and sticky behaviors are separate and shape choices survive validation', () => {
  const f = fixture();
  for (const behavior of ['free', 'align', 'sticky'] as const) {
    const created = createNodeFromSpec(f.project, spec(f, { behavior, attachSide: 'right', style: { shape: 'hexagon', fill: '#123456', titleSize: 32, fontSize: 18 } }));
    const node = created.project.nodes.find(node => node.id === created.nodeId)!;
    assert.equal(node.placement, behavior === 'free' ? 'free' : 'edge'); assert.equal(node.snapping!.stickyEdges, behavior === 'sticky'); assert.equal(node.snapping!.smartAlign, behavior === 'align');
    assert.equal(node.style!.shape, 'hexagon'); assert.equal(node.attachSide, 'right'); assert.deepEqual(validateProject(created.project), []);
  }
  const circle = createNodeFromSpec(f.project, spec(f, { width: 200, height: 100, style: { shape: 'circle' } }));
  assert.equal(circle.project.nodes.find(node => node.id === circle.nodeId)!.height, 200);
});

test('unsupported geometry, duplicate asset IDs, multiple active content items, and inherited child rules fail atomically', () => {
  const f = fixture(), before = structuredClone(f.project);
  assert.throws(() => createNodeFromSpec(f.project, spec(f, { width: Infinity })), /dimensions/);
  assert.throws(() => createNodeFromSpec(f.project, spec(f, { type: 'media', contentType:'asset',asset: { id: 'image', name: 'a document.pdf', mime: 'application/pdf', base64: 'YWJj' } })), /one content item/);
  assert.throws(() => createNodeFromSpec(f.project, spec(f, { type: 'document', asset: { id: f.project.id, name: 'original.pdf', mime: 'application/pdf', base64: 'YWJj' } })), /unique identity/);
  assert.deepEqual(f.project, before);
  const created = createNodeFromSpec(f.project, spec(f, { type: 'media', asset: { id: 'image', name: 'image.png', mime: 'image/png', base64: 'YWJj' } }));
  assert.equal(created.project.nodes.find(node => node.id === created.nodeId)!.assetId, 'image'); assert.equal(created.project.assets[0]!.base64, 'YWJj');
  f.project.nodes.find(node => node.id === f.parentId)!.rules = { nodeKinds: ['container'] };
  assert.throws(() => createNodeFromSpec(f.project, spec(f, { type: 'container', variant: 'grid', csv: 'Source', grid: { columns: 1, rows: 1, cellWidth: 100, cellHeight: 60 } })), /inherited rule/);
});

test('typed links select only matching unoccupied ports and evaluate the resulting system', () => {
  const f = fixture(), number = addNode(f.project, f.canvasId, 'number', 'Operand'); number.value = 12;
  const joint = addNode(f.project, f.canvasId, 'joint', 'Sum'); joint.operation = 'ADD';
  assert.deepEqual(availableLinkPorts(f.project, number.id, joint.id, 'numeric').map(port => port.value), [0, 1]);
  assert.deepEqual(availableLinkPorts(f.project, number.id, joint.id, 'functional'), []);
  const first = createLinkFromSpec(f.project, { canvasId: f.canvasId, sourceId: number.id, targetId: joint.id, kind: 'numeric', relation: 'causal', direction: 'forward', targetPort: 0, parentId: f.parentId, label: 'A' });
  assert.deepEqual(availableLinkPorts(first.project, number.id, joint.id, 'numeric').map(port => port.value), [1]);
  const second = createLinkFromSpec(first.project, { canvasId: f.canvasId, sourceId: number.id, targetId: joint.id, kind: 'numeric', relation: 'causal', targetPort: 1 });
  assert.equal(evaluate(second.project).values[joint.id], 24); assert.deepEqual(validateProject(second.project), []);
  assert.throws(() => createLinkFromSpec(first.project, { canvasId: f.canvasId, sourceId: number.id, targetId: joint.id, kind: 'numeric', relation: 'causal', targetPort: 0 }), /one connection/);
});

test('links retain disclosure owners and refuse cross-canvas endpoints and computed cycles', () => {
  const f = fixture(), parent = addNode(f.project, f.canvasId, 'container');
  const a = addNode(f.project, f.canvasId, 'text', 'A'), b = addNode(f.project, f.canvasId, 'text', 'B'); a.parentId = parent.id; b.parentId = parent.id;
  const created = createLinkFromSpec(f.project, { canvasId: f.canvasId, sourceId: a.id, targetId: b.id, parentId: parent.id, kind: 'visual', relation: 'mutual', direction: 'both', style: { color: '#AABBCC', width: 3, dashed: true } });
  const link = created.project.links.find(link => link.id === created.linkId)!; assert.equal(navigationIndex(created.project).linkParent(link)!.id, parent.id);
  created.project.nodes.find(node => node.id === parent.id)!.collapsed = true; assert.equal(navigationIndex(created.project).isLinkVisible(link), false);
  const other = addCanvas(f.project, 'Elsewhere'), external = addNode(f.project, other.id, 'text', 'External');
  assert.throws(() => createLinkFromSpec(f.project, { canvasId: f.canvasId, sourceId: a.id, targetId: external.id, kind: 'visual', relation: 'generic' }), /within one Work/);
  const input = addNode(f.project, f.canvasId, 'input'), output = addNode(f.project, f.canvasId, 'output');
  const connected = createLinkFromSpec(f.project, { canvasId: f.canvasId, sourceId: input.id, targetId: output.id, kind: 'functional', relation: 'causal' });
  assert.throws(() => createLinkFromSpec(connected.project, { canvasId: f.canvasId, sourceId: output.id, targetId: input.id, kind: 'functional', relation: 'causal' }), /input|cycle/);
});

test('a filled Grid grows to square capacity for a new child while retaining the existing named identity',()=>{
  const f=fixture(),grid=addNode(f.project,f.canvasId,'container','A growing collection');
  Object.assign(grid,{containerVariant:'grid',childLayout:'contained',childArrangement:'grid',grid:{columns:1,rows:1,cellWidth:200,cellHeight:120}});
  const first=addNode(f.project,f.canvasId,'text','First');first.parentId=grid.id;first.cell={column:1,row:1,name:'Primary'};
  const created=createNodeFromSpec(f.project,spec(f,{parentId:grid.id,title:'Second'}));
  assert.deepEqual(created.project.nodes.find(node=>node.id===first.id)!.cell,{column:1,row:1,name:'Primary'});
  assert.deepEqual(created.project.nodes.find(node=>node.id===created.nodeId)!.cell,{column:2,row:1});
  assert.equal(created.project.nodes.find(node=>node.id===grid.id)!.grid!.columns,2);
  assert.equal(created.project.nodes.find(node=>node.id===grid.id)!.grid!.rows,2);
  assert.equal(f.project.nodes.find(node=>node.id===grid.id)!.grid!.rows,1);
  assert.deepEqual(validateProject(created.project),[]);
});
