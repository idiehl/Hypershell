import test from 'node:test';
import assert from 'node:assert/strict';
import { addNode, newProject } from '../src/core/model.ts';
import { normalizeContainerLayouts } from '../src/core/grid.ts';
import { revealChildren } from '../src/core/reveal.ts';
import { navigationIndex } from '../src/core/navigation.ts';
import { expansionTrayBounds, migrateExpansionTrayMetrics, EXPANSION_TRAY_LEGACY_HEADER, EXPANSION_TRAY_LEGACY_PADDING, EXPANSION_TRAY_PADDING } from '../src/core/expansion-tray.ts';
import { boundedChildInterior, boundedChildHeaderBounds } from '../src/core/sizing.ts';
import { nodeShape, shapeBounds, shapeContentInsets } from '../src/core/geometry.ts';
import { validateProject } from '../src/core/rules.ts';
import type { ContainerVariant, MeridianProject, MeridianNode } from '../src/core/types.ts';

const get = (project: MeridianProject, id: string) => project.nodes.find(node => node.id === id)!;
const near = (a: number, b: number, label = '') => assert.ok(Math.abs(a - b) < 1e-7, `${label}: ${a} ≈ ${b}`);

function legacy(pattern: ContainerVariant, fixed = true) {
  const project = newProject('Legacy tray migration'), owner = addNode(project, project.entryCanvasId, 'container', '');
  Object.assign(owner, { x: 0, y: 0, width: 600, height: 400, sizeMode: 'fixed', collapsed: false, containerVariant: 'horizontal', style: { childPadding: 20 },
    containerSizes: { expanded: { width: 600, height: 400, sizeMode: 'fixed' }, collapsed: { width: 200, height: 120, sizeMode: 'fixed' } } });
  const source = addNode(project, owner.canvasId, 'media', 'Original source');
  Object.assign(source, { parentId: owner.id, x: 21, y: 21, width: 558, height: 358, sizeMode: 'fixed', collapsed: false,
    containerVariant: pattern, childLayout: 'spatial', style: { shape: 'rectangle' }, expansionTray: { offset: { x: 679, y: 31 }, ...(fixed ? { size: { width: 440, height: 272 } } : {}) } });
  // Legacy content was 400×200 at (700,52), inside an outer frame at (680,0).
  const count = pattern === 'window' ? 6 : pattern === 'grid' || pattern === 'spreadsheet' ? 4 : 2;
  const columns = pattern === 'vertical' ? 1 : 2, rows = pattern === 'horizontal' ? 1 : pattern === 'vertical' ? 2 : 2;
  source.grid = ['horizontal', 'vertical', 'grid', 'spreadsheet', 'window'].includes(pattern) ? { columns, rows: pattern === 'window' ? 3 : rows, cellWidth: 400 / columns, cellHeight: 200 / rows,
    ...(pattern === 'window' ? { windowColumns: 2, windowRows: 2, scrollDirection: 'vertical' } : {}) } : undefined;
  const children = Array.from({ length: count }, (_, at) => {
    const child = addNode(project, owner.canvasId, 'media', `Original ${at}`);
    const gallery = pattern === 'gallery', free = pattern === 'contained';
    Object.assign(child, { parentId: source.id, x: 700 + (gallery ? 0 : free ? at * 200 : at % columns * 400 / columns), y: 52 + (gallery || free ? 0 : Math.floor(at / columns) * 200 / rows),
      width: gallery ? 400 : free ? 200 : 400 / columns, height: gallery ? 157 : free ? 200 : 200 / rows, sizeMode: 'fixed', collapsed: true,
      childPosition: 'auto', ...(source.grid ? { cell: { column: at % columns + 1, row: Math.floor(at / columns) + 1 } } : {}),
      style: { fontSize: 18, titleSize: 22, textColor: '#abcdef', shape: 'rectangle' }, text: `Unchanged original passage ${at}` });
    return child;
  });
  return { project, owner, source, children };
}

test('legacy fixed tray migration preserves the exact outer frame while reclaiming the header once across every display pattern', () => {
  for (const pattern of ['contained', 'horizontal', 'vertical', 'grid', 'spreadsheet', 'gallery', 'window'] as const) {
    const f = legacy(pattern), before = structuredClone(f.project), original = expansionTrayBounds(before, f.source.id)!;
    const migrated = normalizeContainerLayouts(f.project), frame = expansionTrayBounds(migrated, f.source.id)!;
    assert.deepEqual(f.project, before, `${pattern}: input stays immutable`);
    for (const key of ['x', 'y', 'width', 'height'] as const) near(frame[key], original[key], `${pattern} outer ${key}`);
    const reclaimed = EXPANSION_TRAY_LEGACY_PADDING - EXPANSION_TRAY_PADDING;
    near(frame.content.x, original.content.x - reclaimed); near(frame.content.width, original.content.width + 2 * reclaimed);
    near(frame.content.y, original.content.y - 32 - reclaimed); near(frame.content.height, original.content.height + 32 + 2 * reclaimed);
    assert.equal(get(migrated, f.source.id).expansionTray!.layoutVersion, 3);
    assert.deepEqual(migrated.nodes.map(node => [node.id, node.parentId, node.collapsed, node.title, node.text, node.style]), before.nodes.map(node => [node.id, node.parentId, node.collapsed, node.title, node.text, node.style]));
    assert.deepEqual(normalizeContainerLayouts(migrated), migrated, `${pattern}: migration and layout are idempotent`);
    const closed = revealChildren(migrated, f.owner.id, false), reopened = revealChildren(normalizeContainerLayouts(closed), f.owner.id, true);
    assert.deepEqual(reopened, migrated, `${pattern}: migrated frame survives ancestor disclosure`);
    assert.deepEqual(validateProject(migrated), []);
  }
});

test('legacy automatic tray migration removes only its empty strip, retaining authored geometry, modes, hierarchy and route points', () => {
  const f = legacy('contained', false), child = f.children[0];
  Object.assign(child, { kind: 'container', containerVariant: 'contained', containerSizes: { expanded: { width: 500, height: 400, sizeMode: 'fixed' }, collapsed: { width: 200, height: 200, sizeMode: 'fixed' } } });
  const hidden = addNode(f.project, child.canvasId, 'media', 'Hidden original');
  Object.assign(hidden, { parentId: child.id, x: 730, y: 90, width: 100, height: 60, sizeMode: 'fixed', collapsed: true });
  f.project.links.push({ id: crypto.randomUUID(), canvasId: child.canvasId, parentId: f.source.id, sourceId: child.id, targetId: f.children[1].id, kind: 'visual', relation: 'generic', points: [{ x: 910, y: 180 }] });
  const original = expansionTrayBounds(f.project, f.source.id)!, before = structuredClone(f.project), candidate = structuredClone(f.project);
  assert.deepEqual(migrateExpansionTrayMetrics(candidate), [f.source.id]);
  const frame = expansionTrayBounds(candidate, f.source.id)!;
  const reclaimed = EXPANSION_TRAY_LEGACY_PADDING - EXPANSION_TRAY_PADDING;
  near(frame.x, original.x + reclaimed); near(frame.y, original.y + EXPANSION_TRAY_LEGACY_HEADER + reclaimed); near(frame.width, original.width - 2 * reclaimed); near(frame.height, original.height - EXPANSION_TRAY_LEGACY_HEADER - 2 * reclaimed);
  assert.deepEqual(candidate.nodes.map(node => ({ ...node, ...(node.id === f.source.id ? { expansionTray: before.nodes.find(item => item.id === node.id)!.expansionTray } : {}) })), before.nodes);
  assert.deepEqual(candidate.links, before.links); assert.deepEqual(migrateExpansionTrayMetrics(candidate), []);
});

test('fixed legacy migration affine-fits hidden originals and internal routes without rewriting authored mode records or typography', () => {
  const f = legacy('horizontal'), child = f.children[0], hidden = addNode(f.project, child.canvasId, 'media', 'Hidden');
  Object.assign(child, { kind: 'container', containerVariant: 'contained', containerSizes: { expanded: { width: 500, height: 400, sizeMode: 'fixed' }, collapsed: { width: 200, height: 200, sizeMode: 'fixed' } } });
  Object.assign(hidden, { parentId: child.id, x: 710, y: 82, width: 100, height: 50, sizeMode: 'fixed', collapsed: true });
  f.project.links.push({ id: crypto.randomUUID(), canvasId: child.canvasId, parentId: f.source.id, sourceId: child.id, targetId: f.children[1].id, kind: 'visual', relation: 'generic', points: [{ x: 800, y: 120 }] });
  const before = structuredClone(f.project), old = expansionTrayBounds(before, f.source.id)!, oldHidden = get(before, hidden.id);
  migrateExpansionTrayMetrics(f.project);
  const next = expansionTrayBounds(f.project, f.source.id)!, sy = next.content.height / old.content.height;
  near(hidden.y, next.content.y + (oldHidden.y - old.content.y) * sy); near(hidden.height, oldHidden.height * sy);
  near(f.project.links[0].points![0].y, next.content.y + (120 - old.content.y) * sy);
  assert.deepEqual(child.containerSizes, get(before, child.id).containerSizes); assert.deepEqual(child.style, get(before, child.id).style);
});

test('twenty-four nested bounded disclosures retain at least three quarters of every safe axis and preserve all authored mode frames', () => {
  for (const pattern of ['horizontal', 'vertical', 'grid'] as const) {
    let project = newProject(`Deep ${pattern}`); const nodes: MeridianNode[] = [];
    let parentId: string | undefined;
    for (let depth = 0; depth < 24; depth++) {
      const node = addNode(project, project.entryCanvasId, 'container', `Level ${depth + 1}`);
      Object.assign(node, { parentId: parentId ?? project.canvases[0].rootId, x: 0, y: 0, width: 180, height: 120, sizeMode: 'fixed', collapsed: true,
        containerVariant: pattern, style: { childPadding: 20, fontSize: 18, titleSize: 22, shape: 'rectangle' },
        containerSizes: { expanded: { width: 500, height: 360, sizeMode: 'fixed' }, collapsed: { width: 180, height: 120, sizeMode: 'fixed' } } });
      nodes.push(node); parentId = node.id;
    }
    const detail = addNode(project, project.entryCanvasId, 'media', 'Deepest original');
    Object.assign(detail, { parentId, width: 200, height: 120, sizeMode: 'fixed', collapsed: true, style: { shape: 'rectangle' } });
    const original = project.nodes.map(node => [node.id, node.parentId, structuredClone(node.containerSizes), structuredClone(node.style)]);
    for (let depth = 0; depth < nodes.length; depth++) {
      project = revealChildren(project, nodes[depth].id, true);
      const node = get(project, nodes[depth].id), physical = shapeBounds(nodeShape(node, navigationIndex(project).bounds(node))), inset = shapeContentInsets(node.style?.shape), inner = boundedChildInterior(project, node);
      assert.ok(inner.width >= physical.width * (1 - 2 * inset.x) * .75 - 1e-9, `${pattern}: level ${depth + 1} width ${inner.width} physical ${physical.width}, mode ${node.sizeMode}`);
      assert.ok(inner.height >= physical.height * (1 - inset.top - inset.bottom) * .75 - 1e-9, `${pattern}: level ${depth + 1} height`);
      const header = boundedChildHeaderBounds(project, node);
      near(header.y + header.height, inner.y, `${pattern}: level ${depth + 1} title precedes children`);
      assert.ok(header.height > 0 && header.height <= physical.height / 4);
      assert.ok(node.width > 0 && node.height > 0 && inner.width > 0 && inner.height > 0);
    }
    assert.ok(get(project, nodes.at(-1)!.id).height > .1, `${pattern}: no subpixel cliff at level24`);
    assert.deepEqual(project.nodes.map(node => [node.id, node.parentId, node.containerSizes, node.style]), original);
    assert.deepEqual(normalizeContainerLayouts(project), project);
    for (const expanded of [false, true]) project = revealChildren(project, nodes[0].id, expanded);
    assert.deepEqual(project.nodes.map(node => [node.id, node.parentId, node.containerSizes, node.style]), original);
    assert.deepEqual(validateProject(project), []);
  }
});

test('adaptive nesting uses the actual shape-safe rectangle and leaves spacious containers unchanged', () => {
  const project = newProject('Shape-safe deep nesting'), node = addNode(project, project.entryCanvasId, 'container', '');
  Object.assign(node, { x: 20, y: 30, width: 500, height: 400, sizeMode: 'fixed', collapsed: false, style: { shape: 'rectangle', childPadding: 20 } });
  const wide = boundedChildInterior(project, node); near(wide.x, 41); near(wide.y, 51); near(wide.width, 458); near(wide.height, 358);
  for (const shape of ['circle', 'diamond', 'triangle', 'hexagon'] as const) {
    node.style = { ...node.style, shape }; const frame = { x: 20, y: 30, width: 12, height: 8 }, physical = shapeBounds(nodeShape(node, frame)), shapeInsets = shapeContentInsets(shape), inner = boundedChildInterior(project, node, {}, frame);
    const safeWidth = physical.width * (1 - 2 * shapeInsets.x), safeHeight = physical.height * (1 - shapeInsets.top - shapeInsets.bottom);
    near(inner.width, safeWidth * .75); near(inner.height, safeHeight * .75);
    assert.ok(inner.x >= physical.x && inner.y >= physical.y && inner.x + inner.width <= physical.x + physical.width && inner.y + inner.height <= physical.y + physical.height);
  }
});
