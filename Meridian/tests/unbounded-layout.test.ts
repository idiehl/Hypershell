import test from 'node:test';
import assert from 'node:assert/strict';
import { addNode, newProject } from '../src/core/model.ts';
import { FREEFORM_CLOCK_ORDER, arrangeChildren, gridGeometry, isEnclosingContainer, reflowContainedChildren, normalizeContainerLayouts } from '../src/core/grid.ts';
import { navigationIndex } from '../src/core/navigation.ts';
import { revealChildren } from '../src/core/reveal.ts';
import { activeNodeContent, effectiveContentType, hidesOwnContent } from '../src/core/content.ts';
import { intrinsicContentSize, externalGalleryNavigationBounds } from '../src/core/sizing.ts';
import { shapesOverlap } from '../src/core/geometry.ts';
import { nestNodeBranches } from '../src/core/nesting.ts';
import { expansionTrayBounds } from '../src/core/expansion-tray.ts';
import type { ContainerVariant, MeridianProject } from '../src/core/types.ts';

const get = (project: MeridianProject, id: string) => project.nodes.find(node => node.id === id)!;
function fixture(pattern: ContainerVariant = 'contained', count = 3, bounded = false) {
  const project = newProject('Exterior layout'), canvasId = project.entryCanvasId;
  const parent = addNode(project, canvasId, bounded ? 'container' : 'media', 'Parent');
  Object.assign(parent, { x: 200, y: 200, containerVariant: pattern, collapsed: true, text: 'Parent content', contentType: 'text' });
  if (['spreadsheet', 'window'].includes(pattern)) parent.grid = { columns: 5, rows: 4, cellWidth: 100, cellHeight: 80, windowColumns: 3, windowRows: 2 };
  const children = Array.from({ length: count }, (_, at) => {
    const child = addNode(project, canvasId, 'media', `Child ${at + 1}`);
    Object.assign(child, { parentId: parent.id, x: 200, y: 200, width: 80, height: 50, sizeMode: 'fixed', childPosition: 'auto', text: `Content ${at + 1}` });
    return child;
  });
  return { project, parent, children, canvasId };
}

test('Media keeps one active primary item while inactive legacy fields remain recoverable', () => {
  const f = fixture(); Object.assign(f.parent, { assetId: 'photo', text: 'Legacy caption', hyperlinks: [{ url: 'https://example.test/source', label: 'Source' }] });
  delete f.parent.contentType;
  assert.equal(effectiveContentType(f.parent), 'asset'); assert.deepEqual(activeNodeContent(f.parent), { type: 'asset', assetId: 'photo' });
  f.parent.contentType = 'text'; assert.deepEqual(activeNodeContent(f.parent), { type: 'text', text: 'Legacy caption' });
  f.parent.contentType = 'link'; assert.deepEqual(activeNodeContent(f.parent), { type: 'link', hyperlink: f.parent.hyperlinks![0] });
  assert.equal(f.parent.assetId, 'photo'); assert.equal(f.parent.text, 'Legacy caption');
});

test('expanded bounded parents hide own content while a Media parent retains its content and compact bounds', () => {
  const bounded = fixture('vertical', 2, true), media = fixture('vertical', 2);
  bounded.parent.text = 'Very long body '.repeat(200); media.parent.text = 'Very long body '.repeat(200);
  const collapsedSize = intrinsicContentSize(bounded.project, bounded.parent);
  bounded.parent.collapsed = false;
  assert.equal(hidesOwnContent(bounded.parent), true); assert.ok(intrinsicContentSize(bounded.project, bounded.parent).height < collapsedSize.height);
  assert.equal(hidesOwnContent(media.parent), false); const own = navigationIndex(media.project).bounds(media.parent);
  const revealed = revealChildren(media.project, media.parent.id, true), index = navigationIndex(revealed), parent = get(revealed, media.parent.id);
  assert.deepEqual(index.bounds(parent), own); assert.equal(isEnclosingContainer(parent), false);
  for (const child of media.children) assert.ok(get(revealed, child.id).y >= own.y + own.height + 28);
});

test('Freeform follows the balanced clock order and repeats on a larger second ring without overlaps', () => {
  const f = fixture('contained', 14), revealed = revealChildren(f.project, f.parent.id, true), index = navigationIndex(revealed), parent = index.bounds(get(revealed, f.parent.id));
  const center = { x: parent.x + parent.width / 2, y: parent.y + parent.height / 2 };
  const radii: number[] = [];
  for (const [at, original] of f.children.entries()) {
    const box = index.bounds(get(revealed, original.id)), dx = box.x + box.width / 2 - center.x, dy = box.y + box.height / 2 - center.y;
    const angle = (FREEFORM_CLOCK_ORDER[at % 12]! - 3) * Math.PI / 6;
    assert.ok(Math.abs(Math.atan2(dy, dx) - Math.atan2(Math.sin(angle), Math.cos(angle))) < 1e-8);
    assert.equal(shapesOverlap(box, parent), false); radii.push(Math.hypot(dx, dy));
    for (const peer of f.children.slice(0, at)) assert.equal(shapesOverlap(box, index.bounds(get(revealed, peer.id))), false);
  }
  assert.ok(radii[12]! > radii[0]!); assert.ok(radii[13]! > radii[1]!);
  assert.deepEqual(f.project.nodes.find(node => node.id === f.children[0]!.id), f.children[0]);
});

test('manual and legacy Freeform positions survive collapse, repeated reveal and JSON reopening', () => {
  const f = fixture('contained', 3);
  Object.assign(f.children[1]!, { x: -600, y: 500, childPosition: 'manual' });
  Object.assign(f.children[2]!, { x: -900, y: -300 }); delete f.children[2]!.childPosition;
  let project = revealChildren(f.project, f.parent.id, true);
  for (let at = 0; at < 3; at++) project = normalizeContainerLayouts(JSON.parse(JSON.stringify(revealChildren(revealChildren(project, f.parent.id, false), f.parent.id, true))) as MeridianProject);
  for (const child of f.children.slice(1)) assert.deepEqual({ x: get(project, child.id).x, y: get(project, child.id).y }, { x: child.x, y: child.y });
  assert.equal(get(project, f.children[1]!.id).childPosition, 'manual');
});

test('automatic radial placement carries hidden descendants and internal bends and avoids nearby peers', () => {
  const f = fixture('contained', 2), first = f.children[0]!; first.collapsed = true;
  const hidden = addNode(f.project, f.canvasId, 'media', 'Hidden'); Object.assign(hidden, { parentId: first.id, x: 900, y: 800 });
  f.project.links.push({ id: 'route', canvasId: f.canvasId, sourceId: first.id, targetId: hidden.id, kind: 'visual', relation: 'generic', points: [{ x: 700, y: 500 }] });
  const initial = revealChildren(f.project, f.parent.id, true), firstAt = get(initial, first.id);
  const obstacle = addNode(f.project, f.canvasId, 'media', 'Peer'); Object.assign(obstacle, { x: firstAt.x, y: firstAt.y, width: 120, height: 80, sizeMode: 'fixed' });
  const revealed = revealChildren(f.project, f.parent.id, true), moved = get(revealed, first.id), delta = { x: moved.x - first.x, y: moved.y - first.y };
  assert.deepEqual({ x: get(revealed, hidden.id).x, y: get(revealed, hidden.id).y }, { x: hidden.x + delta.x, y: hidden.y + delta.y });
  assert.deepEqual(revealed.links[0]!.points, [{ x: 700 + delta.x, y: 500 + delta.y }]);
  assert.deepEqual(get(revealed, obstacle.id), obstacle); assert.equal(shapesOverlap(navigationIndex(revealed).bounds(moved), navigationIndex(revealed).bounds(get(revealed, obstacle.id))), false);
});

test('unbounded ordered patterns arrange equal child tiles outside the parent and retain its own dimensions', () => {
  for (const pattern of ['horizontal', 'vertical', 'grid', 'spreadsheet', 'window'] as const) {
    const f = fixture(pattern, pattern === 'grid' ? 10 : 3), own = navigationIndex(f.project).bounds(f.parent);
    const arranged = reflowContainedChildren(f.project, f.parent.id), parent = get(arranged, f.parent.id), index = navigationIndex(arranged), geometry = gridGeometry(parent, arranged);
    assert.deepEqual(index.bounds(parent), own); assert.ok(geometry.top >= own.y + own.height + 28); assert.equal(parent.childLayout, 'spatial');
    const children = f.children.map(child => get(arranged, child.id));
    for (const child of children) { assert.equal(child.width, parent.grid!.cellWidth); assert.equal(child.height, parent.grid!.cellHeight); assert.equal(child.style?.shape, children[0]!.style?.shape); }
    if (pattern === 'grid') { assert.equal(parent.grid!.columns, 4); assert.equal(parent.grid!.rows, 4); }
    if (pattern === 'horizontal') assert.equal(parent.grid!.rows, 1);
    if (pattern === 'vertical') assert.equal(parent.grid!.columns, 1);
    if (pattern === 'window') { assert.equal(geometry.visibleColumns, 3); assert.equal(geometry.visibleRows, 2); }
  }
});

test('exterior ordered collision offsets keep cells and viewport geometry consistent without moving the parent', () => {
  const f = fixture('spreadsheet', 3), parentBefore = structuredClone(f.parent), own = navigationIndex(f.project).bounds(f.parent);
  const peer = addNode(f.project, f.canvasId, 'media', 'Nearby peer'); Object.assign(peer, { x: own.x, y: own.y + own.height + 28, width: 100, height: 80, sizeMode: 'fixed' });
  const arranged = reflowContainedChildren(f.project, f.parent.id), parent = get(arranged, f.parent.id), geometry = gridGeometry(parent, arranged), first = get(arranged, f.children[0]!.id);
  assert.ok(parent.childOffset && (parent.childOffset.x || parent.childOffset.y));
  assert.equal(first.x, geometry.left); assert.equal(first.y, geometry.top);
  assert.equal(parent.x, parentBefore.x); assert.equal(parent.y, parentBefore.y); assert.deepEqual(get(arranged, peer.id), peer);
  const reopened = normalizeContainerLayouts(JSON.parse(JSON.stringify(arranged)) as MeridianProject);
  assert.deepEqual(get(reopened, parent.id).childOffset, parent.childOffset); assert.equal(get(reopened, first.id).x, first.x); assert.equal(get(reopened, first.id).y, first.y);
});

test('unbounded Window placement ignores offscreen logical rows while preserving nearby peers', () => {
  const f = fixture('window', 20), own = navigationIndex(f.project).bounds(f.parent);
  const peer = addNode(f.project, f.canvasId, 'media', 'Below visible window'); Object.assign(peer, { x: own.x, y: own.y + own.height + 28 + 2 * 80 + 5, width: 80, height: 60, sizeMode: 'fixed' });
  const arranged = reflowContainedChildren(f.project, f.parent.id), parent = get(arranged, f.parent.id);
  assert.deepEqual(parent.childOffset, { x: 0, y: 0 }); assert.deepEqual(get(arranged, peer.id), peer);
});

test('an external Gallery shares one outside display slot and navigation reveals only its active item', () => {
  const f = fixture('gallery', 3), arranged = reflowContainedChildren(f.project, f.parent.id); get(arranged, f.parent.id).collapsed = false;
  const children = f.children.map(child => get(arranged, child.id));
  assert.ok(children[0]!.x > f.parent.x); assert.equal(children[0]!.x, children[1]!.x); assert.equal(children[0]!.y, children[2]!.y);
  const index = navigationIndex(arranged, { galleryItems: new Map([[f.parent.id, children[1]!.id]]) });
  assert.equal(index.isNodeVisible(children[0]!.id), false); assert.equal(index.isNodeVisible(children[1]!.id), true); assert.equal(index.isNodeVisible(children[2]!.id), false);
  assert.equal(index.visibleNode(children[1]!.id)?.id, children[1]!.id);
});

test('Freeform automatic reset deliberately repositions manually arranged children', () => {
  const f = fixture('contained', 1); Object.assign(f.children[0]!, { x: -800, y: -800, childPosition: 'manual' });
  const reset = arrangeChildren(f.project, f.parent.id, 'radial').project;
  assert.equal(get(reset, f.children[0]!.id).childPosition, 'auto'); assert.ok(get(reset, f.children[0]!.id).x > f.parent.x);
});

test('bounded Freeform auto children stay in a roomy enclosure and manual children retain their saved places', () => {
  const f = fixture('contained', 3, true);
  delete f.parent.style; // This saved manual position was authored below the legacy header.
  Object.assign(f.children[0]!, { childPosition: 'manual', x: 225, y: 250 });
  const revealed = revealChildren(f.project, f.parent.id, true), parent = get(revealed, f.parent.id), index = navigationIndex(revealed), frame = index.bounds(parent);
  assert.deepEqual({ x: get(revealed, f.children[0]!.id).x, y: get(revealed, f.children[0]!.id).y }, { x: 225, y: 250 });
  for (const child of f.children) {
    const box = index.bounds(get(revealed, child.id));
    assert.ok(box.x >= frame.x + 21 && box.y > frame.y);
    assert.ok(box.x + box.width < frame.x + frame.width && box.y + box.height < frame.y + frame.height);
  }
  assert.ok(frame.width > 2 * f.children[0]!.width + 40); assert.ok(frame.height > f.children[0]!.height + 40);
});

test('dropping a complete branch into a Media parent preserves hidden graph content and applies an exterior pattern', () => {
  const f = fixture('horizontal', 1); f.parent.collapsed = false;
  const root = addNode(f.project, f.canvasId, 'media', 'Dropped'); Object.assign(root, { x: 1000, y: 1000, collapsed: true, text: 'Retain this body' });
  const hidden = addNode(f.project, f.canvasId, 'media', 'Descendant'); Object.assign(hidden, { parentId: root.id, x: 1200, y: 1100, text: 'Hidden content' });
  f.project.links.push({ id: 'nested-route', canvasId: f.canvasId, sourceId: root.id, targetId: hidden.id, kind: 'visual', relation: 'causal', points: [{ x: 1100, y: 1050 }] });
  const nested = nestNodeBranches(f.project, [root.id], f.parent.id), moved = get(nested, root.id), shift = { x: moved.x - root.x, y: moved.y - root.y };
  assert.equal(moved.parentId, f.parent.id); assert.equal(moved.childPosition, 'auto'); assert.equal(moved.collapsed, true); assert.equal(moved.text, 'Retain this body');
  assert.deepEqual({ x: get(nested, hidden.id).x, y: get(nested, hidden.id).y }, { x: hidden.x + shift.x, y: hidden.y + shift.y });
  assert.deepEqual(nested.links[0]!.points, [{ x: 1100 + shift.x, y: 1050 + shift.y }]);
  assert.ok(moved.y > navigationIndex(nested).bounds(get(nested, f.parent.id)).y + navigationIndex(nested).bounds(get(nested, f.parent.id)).height);
  assert.deepEqual(f.project.nodes.find(node => node.id === root.id), root);
});

test('bounded Freeform keeps a Media Gallery cover in place and displays its active slot and paging toolbar in an exterior tray', () => {
  const project = newProject('Nested exterior gallery'), canvasId = project.entryCanvasId;
  const outer = addNode(project, canvasId, 'container', 'Outer'); Object.assign(outer, { x: 0, y: 0, width: 600, height: 480, collapsed: false });
  const gallery = addNode(project, canvasId, 'media', 'Gallery'); Object.assign(gallery, { parentId: outer.id, x: 40, y: 70, containerVariant: 'gallery', childPosition: 'manual', collapsed: false });
  const items = [[100, 80], [160, 120]].map(([width, height], at) => {
    const item = addNode(project, canvasId, 'media', `Item ${at}`); Object.assign(item, { parentId: gallery.id, width, height, sizeMode: 'fixed' }); return item;
  });
  const arranged = reflowContainedChildren(project, gallery.id), parent = get(arranged, outer.id), heights: number[] = [];
  for (const item of items) {
    const index = navigationIndex(arranged, { galleryItems: new Map([[gallery.id, item.id]]) }), frame = index.bounds(parent), displayed = index.bounds(get(arranged, item.id));
    const tray = expansionTrayBounds(arranged, gallery.id, { galleryItems: new Map([[gallery.id, item.id]]) })!;
    const navigation = externalGalleryNavigationBounds(arranged, get(arranged, gallery.id), get(arranged, item.id), {}, displayed);
    assert.ok(tray.x >= frame.x + frame.width || tray.y >= frame.y + frame.height || tray.x + tray.width <= frame.x || tray.y + tray.height <= frame.y);
    for (const content of [displayed, navigation]) {
      assert.ok(content.x >= tray.x && content.y >= tray.y);
      assert.ok(content.x + content.width <= tray.x + tray.width + .001 && content.y + content.height <= tray.y + tray.height + .001);
    }
    heights.push(frame.height);
  }
  assert.deepEqual(heights, [outer.height, outer.height], 'Browsing the exterior tray never enlarges its bounded ancestor.');
  const saved = reflowContainedChildren(arranged, outer.id);
  assert.equal(get(saved, gallery.id).x, gallery.x); assert.equal(get(saved, gallery.id).y, gallery.y);
  assert.deepEqual(project.nodes.find(node => node.id === gallery.id), gallery);
});

test('bounded Freeform places a Media Window visible viewport in a tray without including its hidden logical cells', () => {
  const project = newProject('Nested exterior window'), canvasId = project.entryCanvasId;
  const outer = addNode(project, canvasId, 'container', 'Outer'); Object.assign(outer, { x: 0, y: 0, width: 600, height: 480, collapsed: false });
  const window = addNode(project, canvasId, 'media', 'Window'); Object.assign(window, { parentId: outer.id, x: 40, y: 70, containerVariant: 'window', childPosition: 'manual', collapsed: false });
  window.grid = { columns: 3, rows: 30, cellWidth: 100, cellHeight: 80, windowColumns: 2, windowRows: 2 };
  for (let at = 0; at < 90; at++) { const item = addNode(project, canvasId, 'media', `Item ${at}`); item.parentId = window.id; }
  const arranged = reflowContainedChildren(project, window.id), parent = get(arranged, outer.id), child = get(arranged, window.id), index = navigationIndex(arranged), frame = index.bounds(parent), viewport = gridGeometry(child, arranged);
  const tray = expansionTrayBounds(arranged, window.id)!;
  assert.ok(tray.x >= frame.x + frame.width || tray.y >= frame.y + frame.height || tray.x + tray.width <= frame.x || tray.y + tray.height <= frame.y);
  assert.equal(tray.content.width, viewport.visibleColumns * viewport.cellWidth); assert.equal(tray.content.height, viewport.visibleRows * viewport.cellHeight);
  assert.deepEqual([frame.width, frame.height], [outer.width, outer.height]);
  assert.ok(frame.height < 500, 'the other 28 rows remain outside the visible footprint');
  child.grid!.rows = 300; assert.deepEqual(navigationIndex(arranged).bounds(parent), frame, 'adding hidden logical capacity cannot enlarge the outer enclosure');
  assert.deepEqual(expansionTrayBounds(arranged, window.id), tray, 'Hidden logical rows cannot enlarge the tray viewport.');
  const normalized = normalizeContainerLayouts(arranged);
  assert.equal(get(normalized, window.id).x, window.x); assert.equal(get(normalized, window.id).y, window.y);
  assert.ok(navigationIndex(normalized).bounds(get(normalized, outer.id)).height < 500);
});

test('ordered surfaces retain their pattern while expanded exterior Gallery and Window branches use stable original-node trays', () => {
  for (const pattern of ['gallery', 'window'] as const) {
    const f = fixture(pattern, 3), outer = addNode(f.project, f.canvasId, 'container', 'Ordered owner');
    Object.assign(outer, { x: 0, y: 0, width: 1600, height: 1200, containerVariant: 'horizontal', collapsed: false });
    f.parent.parentId = outer.id; f.parent.collapsed = false; f.parent.childPosition = 'manual';
    const arranged = reflowContainedChildren(f.project, f.parent.id), before = structuredClone(arranged);
    const packed = reflowContainedChildren(arranged, outer.id), frame = expansionTrayBounds(packed, f.parent.id)!, bounds = navigationIndex(packed).bounds(get(packed, outer.id));
    assert.ok(frame); assert.ok(frame.x >= bounds.x + bounds.width || frame.y >= bounds.y + bounds.height || frame.x + frame.width <= bounds.x || frame.y + frame.height <= bounds.y);
    assert.deepEqual(arranged, before);
    const compatible = normalizeContainerLayouts(arranged);
    assert.equal(get(compatible, outer.id).containerVariant, 'horizontal'); assert.equal(get(compatible, outer.id).kind, 'container');
    assert.equal(get(compatible, f.parent.id).kind, 'media'); assert.ok(get(compatible, f.parent.id).expansionTray);
    assert.deepEqual(compatible.nodes.map(node => node.id), arranged.nodes.map(node => node.id));
    for (const item of f.children) assert.equal(get(compatible, item.id).parentId, f.parent.id);
    const collapsed = structuredClone(arranged); get(collapsed, f.parent.id).collapsed = true;
    assert.doesNotThrow(() => reflowContainedChildren(collapsed, outer.id));
    const bounded = structuredClone(arranged), boundedChild = get(bounded, f.parent.id);
    boundedChild.kind = 'container'; boundedChild.childLayout = 'contained';
    assert.doesNotThrow(() => reflowContainedChildren(bounded, outer.id));
  }
});
