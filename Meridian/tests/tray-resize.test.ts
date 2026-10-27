import test from 'node:test';
import assert from 'node:assert/strict';
import { addNode, newProject } from '../src/core/model.ts';
import { gridGeometry, normalizeContainerLayouts, reflowContainedChildren } from '../src/core/grid.ts';
import { navigationIndex } from '../src/core/navigation.ts';
import { revealChildren } from '../src/core/reveal.ts';
import { expansionTrayBounds, moveExpansionTray, resizeExpansionTray, EXPANSION_TRAY_PADDING, EXPANSION_TRAY_HEADER, EXPANSION_TRAY_MIN_WIDTH, EXPANSION_TRAY_MIN_HEIGHT } from '../src/core/expansion-tray.ts';
import { boundedChildInterior, constrainChildGeometry, externalGalleryNavigationBounds } from '../src/core/sizing.ts';
import { shapeBounds, nodeShape } from '../src/core/geometry.ts';
import { validateProject } from '../src/core/rules.ts';
import type { ContainerVariant, MeridianNode, MeridianProject } from '../src/core/types.ts';

const get = (project: MeridianProject, id: string) => project.nodes.find(node => node.id === id)!;
const near = (a: number, b: number) => assert.ok(Math.abs(a - b) < 1e-7, `${a} ≈ ${b}`);
function fixture(pattern: ContainerVariant = 'horizontal') {
  const project = newProject('Resized real expansion tray'), owner = addNode(project, project.entryCanvasId, 'container', '');
  Object.assign(owner, { x: 100, y: 100, width: 840, height: 500, sizeMode: 'fixed', collapsed: false, containerVariant: 'horizontal',
    containerSizes: { expanded: { width: 840, height: 500, sizeMode: 'fixed' }, collapsed: { width: 200, height: 120, sizeMode: 'auto' } } });
  const source = addNode(project, owner.canvasId, 'media', 'Source');
  Object.assign(source, { parentId: owner.id, collapsed: true, containerVariant: pattern, sizeMode: 'fixed', width: 200, height: 120 });
  if (pattern === 'spreadsheet' || pattern === 'window') source.grid = { columns: 3, rows: 5, cellWidth: 200, cellHeight: 120, windowColumns: 2, windowRows: 2, scrollDirection: 'vertical' };
  const count = pattern === 'spreadsheet' || pattern === 'window' ? 15 : pattern === 'grid' ? 9 : 3;
  const children = Array.from({ length: count }, (_, at) => {
    const child = addNode(project, owner.canvasId, 'media', `Original ${at}`);
    Object.assign(child, { parentId: source.id, x: 700 + at * 220, y: 180, width: 190, height: 120, sizeMode: 'fixed', collapsed: true, childPosition: 'auto', text: `Original passage ${at}`, style: { fontSize: 18, titleSize: 20, padding: 8 } }); return child;
  });
  return { project: revealChildren(reflowContainedChildren(project, owner.id), source.id, true), owner, source, children };
}

test('resized trays retain exact outer frames and fit original children across all seven display patterns through reopen and normalization', () => {
  for (const pattern of ['contained', 'horizontal', 'vertical', 'grid', 'spreadsheet', 'gallery', 'window'] as const) {
    const f = fixture(pattern), before = structuredClone(f.project), old = expansionTrayBounds(f.project, f.source.id)!;
    const target = { x: old.x + 60, y: old.y + 40, width: 640, height: 420 };
    const resized = resizeExpansionTray(f.project, f.source.id, target), frame = expansionTrayBounds(resized, f.source.id)!;
    assert.deepEqual(f.project, before); assert.deepEqual({ x: frame.x, y: frame.y, width: frame.width, height: frame.height }, target, pattern);
    assert.deepEqual(get(resized, f.source.id).expansionTray!.size, { width: 640, height: 420 });
    assert.deepEqual([get(resized, f.source.id).x, get(resized, f.source.id).y, get(resized, f.source.id).width, get(resized, f.source.id).height], [get(before, f.source.id).x, get(before, f.source.id).y, get(before, f.source.id).width, get(before, f.source.id).height]);
    for (const original of f.children) {
      const child = get(resized, original.id), box = navigationIndex(resized).bounds(child);
      assert.deepEqual(child.style, get(before, original.id).style); assert.equal(child.text, original.text); assert.equal(child.parentId, f.source.id);
      if (pattern !== 'window' || child.cell!.column <= 2 && child.cell!.row <= 2) {
        assert.ok(box.x >= frame.content.x - 1e-7 && box.y >= frame.content.y - 1e-7);
        assert.ok(box.x + box.width <= frame.content.x + frame.content.width + 1e-7 && box.y + box.height <= frame.content.y + frame.content.height + 1e-7, pattern);
      }
    }
    if (pattern === 'window') {
      const node = get(resized, f.source.id), grid = gridGeometry(node, resized);
      assert.equal(grid.columns, 3); assert.equal(grid.rows, 5); near(grid.cellWidth * 2, 640 - 2 * EXPANSION_TRAY_PADDING); near(grid.cellHeight * 2, 420 - 2 * EXPANSION_TRAY_PADDING - EXPANSION_TRAY_HEADER);
      assert.ok(get(resized, f.children.at(-1)!.id).y >= frame.content.y + frame.content.height);
    }
    if (pattern === 'gallery') {
      const navigation = externalGalleryNavigationBounds(resized, get(resized, f.source.id), get(resized, f.children[0].id));
      near(navigation.y + navigation.height, frame.content.y + frame.content.height);
      const frames = f.children.map(child => navigationIndex(resized).bounds(get(resized, child.id)));
      assert.ok(frames.every(box => box.width === frames[0].width && box.height === frames[0].height));
    }
    assert.deepEqual(normalizeContainerLayouts(resized), resized, pattern);
    let reopened = revealChildren(resized, f.owner.id, false); reopened = normalizeContainerLayouts(JSON.parse(JSON.stringify(reopened))); reopened = revealChildren(reopened, f.owner.id, true);
    assert.deepEqual(reopened, resized, `${pattern}: ancestor disclosure preserves saved frame and contents`);
    assert.deepEqual(validateProject(resized), []);
  }
});

test('manual affine scaling preserves hidden descendants and internal routes without changing typography or external routes', () => {
  const f = fixture('horizontal'), first = get(f.project, f.children[0].id), hidden = addNode(f.project, f.owner.canvasId, 'media', 'Hidden');
  Object.assign(hidden, { parentId: first.id, x: first.x + 50, y: first.y + 80, width: 40, height: 30, sizeMode: 'fixed', collapsed: true });
  const internal = { id: crypto.randomUUID(), canvasId: f.owner.canvasId, sourceId: first.id, targetId: f.children[1].id, parentId: f.source.id, kind: 'visual' as const, relation: 'generic' as const, points: [{ x: first.x + 30, y: first.y + 20 }] };
  const external = { ...internal, id: crypto.randomUUID(), sourceId: f.source.id };
  f.project.links.push(internal, external);
  const old = expansionTrayBounds(f.project, f.source.id)!, target = { x: old.x + 20, y: old.y + 30, width: old.width * 1.5, height: old.height * 1.6 };
  const resized = resizeExpansionTray(f.project, f.source.id, target), frame = expansionTrayBounds(resized, f.source.id)!, sx = frame.content.width / old.content.width, sy = frame.content.height / old.content.height;
  near(get(resized, hidden.id).x, frame.content.x + (hidden.x - old.content.x) * sx); near(get(resized, hidden.id).y, frame.content.y + (hidden.y - old.content.y) * sy);
  near(get(resized, hidden.id).width, hidden.width * sx); near(get(resized, hidden.id).height, hidden.height * sy);
  assert.deepEqual(resized.links[0].points, internal.points.map(point => ({ x: frame.content.x + (point.x - old.content.x) * sx, y: frame.content.y + (point.y - old.content.y) * sy })));
  assert.deepEqual(resized.links[1].points, external.points); assert.deepEqual(get(resized, first.id).style, first.style);
});

test('shrinking a tray refits an expanded bounded child within its actual padding and scales its saved disclosure frames only once', () => {
  const f = fixture('contained'), box = get(f.project, f.children[0].id);
  Object.assign(box, { kind: 'container', title: 'Bounded child', collapsed: false, containerVariant: 'horizontal', childLayout: 'contained', width: 360, height: 240,
    containerSizes: { expanded: { width: 360, height: 240, sizeMode: 'fixed' }, collapsed: { width: 180, height: 100, sizeMode: 'fixed' } }, style: { padding: 8, childPadding: 20 } });
  const detail = addNode(f.project, f.owner.canvasId, 'media', 'Interior original');
  Object.assign(detail, { parentId: box.id, x: box.x + 21, y: box.y + 50, width: 318, height: 169, sizeMode: 'fixed', collapsed: true });
  const aligned = normalizeContainerLayouts(f.project), old = expansionTrayBounds(aligned, f.source.id)!;
  const verticalInset = 2 * EXPANSION_TRAY_PADDING + EXPANSION_TRAY_HEADER;
  const target = { x: old.x, y: old.y, width: (old.width - 2 * EXPANSION_TRAY_PADDING) / 2 + 2 * EXPANSION_TRAY_PADDING, height: (old.height - verticalInset) / 2 + verticalInset };
  const resized = resizeExpansionTray(aligned, f.source.id, target), child = get(resized, box.id), inner = boundedChildInterior(resized, child), item = navigationIndex(resized).bounds(get(resized, detail.id));
  assert.ok(item.x >= inner.x - 1e-7 && item.y >= inner.y - 1e-7 && item.x + item.width <= inner.x + inner.width + 1e-7 && item.y + item.height <= inner.y + inner.height + 1e-7);
  assert.deepEqual(child.containerSizes, { expanded: { width: 180, height: 120, sizeMode: 'fixed' }, collapsed: { width: 90, height: 50, sizeMode: 'fixed' } });
  assert.deepEqual(normalizeContainerLayouts(resized), resized);
});

test('fixed tray content walls stop child resizing while square silhouettes remain undistorted and invalid/pinned resizes reject atomically', () => {
  const f = fixture('contained'); get(f.project, f.children[0].id).style = { shape: 'circle' };
  const old = expansionTrayBounds(f.project, f.source.id)!, resized = resizeExpansionTray(f.project, f.source.id, { x: old.x, y: old.y, width: 540, height: 260 });
  const source = get(resized, f.source.id), frame = expansionTrayBounds(resized, source.id)!, child = get(resized, f.children[0].id), inner = constrainChildGeometry(resized, child, { x: frame.content.x - 50, y: frame.content.y - 50, width: 1e5, height: 1e5 });
  assert.ok(inner.x >= frame.content.x && inner.y >= frame.content.y && inner.x + inner.width <= frame.content.x + frame.content.width && inner.y + inner.height <= frame.content.y + frame.content.height);
  const silhouette = shapeBounds(nodeShape(child, navigationIndex(resized).bounds(child))); near(silhouette.width, silhouette.height);
  const before = structuredClone(resized); assert.throws(() => resizeExpansionTray(resized, source.id, { x: 0, y: 0, width: NaN, height: 200 }), /finite/); assert.deepEqual(resized, before);
  child.positionLocked = true; const pinned = structuredClone(resized); assert.throws(() => resizeExpansionTray(resized, source.id, { ...frame, width: 700 }), /Unlock/); assert.deepEqual(resized, pinned);
});

test('left and top tray edges resize real content, tiny frames retain a positive viewport, and active Gallery page context is honored', () => {
  const f = fixture('gallery'), larger = get(f.project, f.children[1].id); larger.width *= 2; larger.height *= 3;
  const context = { galleryItems: new Map([[f.source.id, larger.id]]) }, old = expansionTrayBounds(f.project, f.source.id, context)!;
  const resized = resizeExpansionTray(f.project, f.source.id, { x: old.x - 30, y: old.y - 20, width: old.width + 30, height: old.height + 20 }, context), frame = expansionTrayBounds(resized, f.source.id, context)!;
  near(frame.x, old.x - 30); near(frame.y, old.y - 20); near(frame.x + frame.width, old.x + old.width); near(frame.y + frame.height, old.y + old.height);
  const tiny = resizeExpansionTray(resized, f.source.id, { x: frame.x, y: frame.y, width: 1, height: 1 }), small = expansionTrayBounds(tiny, f.source.id)!;
  assert.equal(small.width, EXPANSION_TRAY_MIN_WIDTH); assert.equal(small.height, EXPANSION_TRAY_MIN_HEIGHT); assert.equal(small.content.width, 1); assert.equal(small.content.height, 1);
  for (const child of f.children) { const box = navigationIndex(tiny).bounds(get(tiny, child.id)); assert.ok(box.width > 0 && box.height > 0); }
  const navigation = externalGalleryNavigationBounds(tiny, get(tiny, f.source.id), get(tiny, larger.id)); assert.ok(navigation.scale > 0); near(navigation.y + navigation.height, small.content.y + 1);
  assert.deepEqual(normalizeContainerLayouts(tiny), tiny);
});

test('resizing an outer tray carries a nested fixed tray without scaling that detached tray or its original contents', () => {
  const f = fixture('horizontal'), nested = get(f.project, f.children[0].id), detail = addNode(f.project, f.owner.canvasId, 'media', 'Detached original');
  Object.assign(detail, { parentId: nested.id, x: 3000, y: 1800, width: 180, height: 120, sizeMode: 'fixed', collapsed: true, childPosition: 'auto' });
  let current = revealChildren(f.project, nested.id, true), inner = expansionTrayBounds(current, nested.id)!;
  current = resizeExpansionTray(current, nested.id, { x: inner.x, y: inner.y, width: 340, height: 260 });
  const savedSize = structuredClone(get(current, nested.id).expansionTray!.size), savedDetail = structuredClone(get(current, detail.id)), outer = expansionTrayBounds(current, f.source.id)!;
  const resized = resizeExpansionTray(current, f.source.id, { x: outer.x, y: outer.y, width: outer.width * 1.4, height: outer.height * 1.5 });
  assert.deepEqual(get(resized, nested.id).expansionTray!.size, savedSize);
  assert.deepEqual([get(resized, detail.id).width, get(resized, detail.id).height], [savedDetail.width, savedDetail.height]);
  assert.equal(get(resized, detail.id).parentId, nested.id); assert.deepEqual(get(resized, detail.id).style, savedDetail.style);
  assert.deepEqual(normalizeContainerLayouts(resized), resized);
});

test('tray edge snapping is opt-in, flush on all four enclosing edges, bypassable, and requires a shared seam', () => {
  const f = fixture('horizontal'), initial = expansionTrayBounds(f.project, f.source.id)!, owner = navigationIndex(f.project).bounds(get(f.project, f.owner.id));
  for (const side of ['right', 'bottom', 'left', 'top'] as const) {
    const x = side === 'right' ? owner.x + owner.width + 7 : side === 'left' ? owner.x - initial.width - 7 : owner.x + 20;
    const y = side === 'bottom' ? owner.y + owner.height + 7 : side === 'top' ? owner.y - initial.height - 7 : owner.y + 20;
    const dx = x - initial.x, dy = y - initial.y, moved = moveExpansionTray(f.project, f.source.id, dx, dy, { snapDistance: 12 }), frame = expansionTrayBounds(moved, f.source.id)!;
    if (side === 'right') near(frame.x, owner.x + owner.width);
    if (side === 'left') near(frame.x + frame.width, owner.x);
    if (side === 'bottom') near(frame.y, owner.y + owner.height);
    if (side === 'top') near(frame.y + frame.height, owner.y);
    const bypassed = expansionTrayBounds(moveExpansionTray(f.project, f.source.id, dx, dy, { snapDistance: 12, bypassSnap: true }), f.source.id)!;
    near(bypassed.x, x); near(bypassed.y, y);
  }
  const x = owner.x + owner.width + 7, y = owner.y + owner.height + 100;
  const separate = expansionTrayBounds(moveExpansionTray(f.project, f.source.id, x - initial.x, y - initial.y, { snapDistance: 12 }), f.source.id)!;
  near(separate.x, x); near(separate.y, y);
});

test('one presentation tether replaces source-to-original fan links without hiding sibling or external graph relationships', () => {
  const f = fixture(), [first, second] = f.children, outside = addNode(f.project, f.owner.canvasId, 'media', 'External');
  const edge = (sourceId: string, targetId: string) => ({ id: crypto.randomUUID(), canvasId: f.owner.canvasId, parentId: f.project.canvases[0].rootId, sourceId, targetId, kind: 'visual' as const, relation: 'generic' as const });
  const fan = edge(f.source.id, first.id), reverseFan = edge(second.id, f.source.id), sibling = edge(first.id, second.id), crossing = edge(first.id, outside.id);
  f.project.links.push(fan, reverseFan, sibling, crossing);
  const index = navigationIndex(f.project);
  assert.equal(index.isLinkVisible(fan), false); assert.equal(index.isLinkVisible(reverseFan), false);
  assert.equal(index.isLinkVisible(sibling), true); assert.equal(index.isLinkVisible(crossing), true);
  assert.equal(index.backlinks(f.source.id).length, 0); assert.equal(f.project.links.length, 4);
});
