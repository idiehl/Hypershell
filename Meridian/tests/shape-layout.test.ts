import test from 'node:test';
import assert from 'node:assert/strict';
import { shapeAspectRatio, shapeBounds, shapeVertices, shapeAnchor, shapePath, shapesOverlap, nodeShape } from '../src/core/geometry.ts';
import { boundedChildInterior, intrinsicContentSize, nodeContentBounds, nodeContentLayout } from '../src/core/sizing.ts';
import { reflowContainedChildren, gridGeometry } from '../src/core/grid.ts';
import { addNode, newProject } from '../src/core/model.ts';
import { navigationIndex } from '../src/core/navigation.ts';
import type { ChildGeometry } from '../src/core/sizing.ts';
import type { NodeShape } from '../src/core/types.ts';

const shapes: NodeShape[] = ['square', 'circle', 'triangle', 'diamond', 'pentagon', 'hexagon', 'octagon', 'ellipse'];
function corners(box: ChildGeometry) {
  return [{ x: box.x, y: box.y }, { x: box.x + box.width, y: box.y }, { x: box.x + box.width, y: box.y + box.height }, { x: box.x, y: box.y + box.height }];
}
function assertInsideSilhouette(node: Parameters<typeof nodeShape>[0], frame: ChildGeometry, box: ChildGeometry) {
  const backdrop = nodeShape(node, frame), physical = shapeBounds(backdrop);
  for (const point of corners(box)) {
    if (backdrop.shape === 'circle' || backdrop.shape === 'ellipse') {
      const x = (point.x - physical.x - physical.width / 2) / (physical.width / 2), y = (point.y - physical.y - physical.height / 2) / (physical.height / 2);
      assert.ok(x * x + y * y <= 1 + 1e-7, `${node.style?.shape}: curve contains content corner`);
    } else {
      const vertices = shapeVertices(backdrop);
      for (const [at, a] of vertices.entries()) {
        const b = vertices[(at + 1) % vertices.length]!;
        const cross = (b.x - a.x) * (point.y - a.y) - (b.y - a.y) * (point.x - a.x);
        assert.ok(cross >= -1e-6, `${node.style?.shape}: polygon contains content corner`);
      }
    }
  }
}

test('regular shapes retain their real proportions inside wide and tall authored frames', () => {
  for (const shape of shapes.filter(shape => shape !== 'ellipse')) for (const [width, height] of [[700, 180], [180, 700]]) {
    const box = { x: 70, y: 90, width: width!, height: height!, shape }, physical = shapeBounds(box);
    assert.ok(Math.abs(physical.width / physical.height - shapeAspectRatio(shape)!) < 1e-9);
    assert.equal(physical.x + physical.width / 2, box.x + box.width / 2);
    assert.equal(physical.y + physical.height / 2, box.y + box.height / 2);
    assert.ok(physical.width <= box.width && physical.height <= box.height);
    if (!['circle', 'square', 'diamond'].includes(shape)) {
      const vertices = shapeVertices(box), lengths = vertices.map((a, at) => Math.hypot(a.x - vertices[(at + 1) % vertices.length]!.x, a.y - vertices[(at + 1) % vertices.length]!.y));
      assert.ok(Math.max(...lengths) - Math.min(...lengths) < 1e-7, `${shape}: regular polygon sides remain equal`);
    }
  }
  const ellipse = { x: 70, y: 90, width: 700, height: 180, shape: 'ellipse' as const };
  assert.deepEqual(shapeBounds(ellipse), ellipse, 'An ellipse deliberately retains its authored ratio.');
});

test('paths, link anchors and collisions share the centered regular polygon silhouette', () => {
  const diamond = { x: 0, y: 0, width: 700, height: 180, shape: 'diamond' as const };
  assert.equal(shapePath(diamond), 'M350,0 L440,90 L350,180 L260,90Z');
  assert.deepEqual(shapeAnchor(diamond, 1, 0), { x: 440, y: 90 });
  assert.equal(shapesOverlap(diamond, { x: 0, y: 0, width: 200, height: 180, shape: 'rectangle' }), false);
  assert.equal(shapesOverlap(diamond, { x: 320, y: 70, width: 60, height: 40, shape: 'rectangle' }), true);
});

test('shared reading and child surfaces fit every real silhouette after changing shape or border width', () => {
  const project = newProject('Shape walls'), node = addNode(project, project.entryCanvasId, 'container', 'A title that wraps');
  Object.assign(node, { x: 100, y: 100, width: 700, height: 240, sizeMode: 'fixed', collapsed: false });
  for (const shape of shapes) for (const borderWidth of [1, 4, 8]) {
    node.style = { shape, borderWidth, padding: 5, childPadding: 5 };
    const frame = { x: node.x, y: node.y, width: node.width, height: node.height };
    assertInsideSilhouette(node, frame, nodeContentBounds(node, frame));
    assertInsideSilhouette(node, frame, boundedChildInterior(project, node, {}, frame));
  }
});

test('automatic text frames expand around content while preserving recognizable shape proportions', () => {
  const project = newProject('Content inside geometric nodes'), node = addNode(project, project.entryCanvasId, 'media', 'A geometric title');
  Object.assign(node, { x: 100, y: 100, sizeMode: 'auto', width: 420, text: 'All of this content stays inside the actual backdrop. '.repeat(5) });
  for (const shape of shapes) {
    node.style = { shape, fontSize: 18, titleSize: 24, borderWidth: 7, padding: 5 };
    const layout = nodeContentLayout(project, node), frame = { x: node.x, y: node.y, ...intrinsicContentSize(project, node) }, area = nodeContentBounds(node, frame);
    assertInsideSilhouette(node, frame, area);
    assert.ok(area.width + .01 >= layout.contentWidth, `${shape}: text width is reserved`);
    assert.ok(area.height + .01 >= layout.headerHeight + layout.bodyHeight - 10, `${shape}: text height is reserved`);
  }
});

test('small fitted cells keep padding and border reservations inside their physical shape', () => {
  const project = newProject('Small geometric tiles'), node = addNode(project, project.entryCanvasId, 'container', 'Small tile');
  Object.assign(node, { x: 100, y: 100, sizeMode: 'fixed', collapsed: false, width: 6, height: 4 });
  for (const shape of shapes) {
    node.style = { shape, borderWidth: 8, padding: 5, childPadding: 5 };
    const frame = { x: node.x, y: node.y, width: node.width, height: node.height };
    assertInsideSilhouette(node, frame, nodeContentBounds(node, frame));
    assertInsideSilhouette(node, frame, boundedChildInterior(project, node, {}, frame));
  }
});

function fixture(shape: NodeShape, sizeMode: 'fixed' | 'auto', ordered = true) {
  const project = newProject('Geometric enclosure'), node = addNode(project, project.entryCanvasId, 'container', 'Music, Wilderness, and Making');
  Object.assign(node, { x: 100, y: 100, width: 660, height: 240, sizeMode, collapsed: false, containerVariant: ordered ? 'horizontal' : 'contained', childLayout: 'contained', childArrangement: 'free', style: { shape, borderWidth: 8, padding: 5, childPadding: 5 } });
  node.grid = { columns: 4, rows: 1, cellWidth: 160, cellHeight: 80, gap: 0 };
  const children = ['Apollo', 'Artemis', 'Hermes', 'Hephaestus'].map((title, at) => {
    const child = addNode(project, node.canvasId, 'media', title);
    Object.assign(child, { parentId: node.id, x: node.x + at * 180, y: node.y + 110, width: 160, height: 80, text: `Preserve ${title}`, sizeMode: 'fixed', childPosition: 'manual', style: { shape: 'rectangle' } }); return child;
  });
  return { project, node, children };
}

test('fixed shaped rows shrink uniform children into real silhouette walls without changing the parent', () => {
  for (const shape of shapes) {
    const f = fixture(shape, 'fixed'), before = structuredClone(f.project), fitted = reflowContainedChildren(f.project, f.node.id), node = fitted.nodes.find(node => node.id === f.node.id)!, index = navigationIndex(fitted);
    assert.deepEqual(f.project, before); assert.deepEqual([node.width, node.height], [660, 240]);
    const grid = gridGeometry(node, fitted);
    for (const child of f.children) {
      const shown = index.nodes.get(child.id)!, bounds = index.bounds(shown);
      assertInsideSilhouette(node, index.bounds(node), bounds); assert.equal(shown.text, child.text);
      assert.equal(bounds.width, grid.cellWidth); assert.equal(bounds.height, grid.cellHeight);
    }
    assert.deepEqual(reflowContainedChildren(fitted, node.id), fitted);
  }
});

test('automatic shaped rows grow to surround all equal-size cells without silhouette overflow', () => {
  for (const shape of shapes) {
    const f = fixture(shape, 'auto'), fitted = reflowContainedChildren(f.project, f.node.id), node = fitted.nodes.find(node => node.id === f.node.id)!, index = navigationIndex(fitted);
    for (const child of f.children) assertInsideSilhouette(node, index.bounds(node), index.bounds(index.nodes.get(child.id)!));
    assert.deepEqual(reflowContainedChildren(fitted, node.id), fitted, `${shape}: auto arrangement is stable`);
  }
});

test('automatic shaped Freeform fits escaped children proportionally and preserves already valid manual placements', () => {
  for (const shape of shapes) {
    const f = fixture(shape, 'auto', false), fitted = reflowContainedChildren(f.project, f.node.id), node = fitted.nodes.find(node => node.id === f.node.id)!, index = navigationIndex(fitted), first = index.nodes.get(f.children[0]!.id)!;
    const safe = boundedChildInterior(f.project, f.node, {}, f.node);
    for (const child of f.children) {
      const shown = index.nodes.get(child.id)!;
      assertInsideSilhouette(node, index.bounds(node), index.bounds(shown));
      const scale = shown.width / child.width;
      assert.ok(scale > 0 && scale <= 1); assert.ok(Math.abs(shown.height - child.height * scale) < 1e-6);
      if (child.x >= safe.x && child.y >= safe.y && child.x + child.width <= safe.x + safe.width && child.y + child.height <= safe.y + safe.height)
        assert.deepEqual([shown.x, shown.y, shown.width, shown.height], [child.x, child.y, child.width, child.height]);
    }
    assert.deepEqual(reflowContainedChildren(fitted, node.id), fitted, `${shape}: preserved Freeform composition is stable`);
    assert.deepEqual([node.width, node.height], [f.node.width, f.node.height]);
  }
});
