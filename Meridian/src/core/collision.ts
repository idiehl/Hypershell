import type { MeridianProject, MeridianNode, Point } from './types.ts';
import { navigationIndex } from './navigation.ts';
import { createShapeBoundsIndex, nodeShape, shapeSeparation, shapesOverlap, type ShapeBox } from './geometry.ts';
import { isEnclosingContainer, gridGeometry } from './cells.ts';
import { createLockedClusterIndex, reconcileSnapLinks } from './clusters.ts';
import { expansionTrayBounds } from './expansion-tray.ts';

/** Exterior scrolling surfaces occupy canvas space without enlarging their parent's tile. */
export function collisionNodeShapes(project: MeridianProject, node: MeridianNode, bounds: ShapeBox = node): ShapeBox[] {
  const shapes = [nodeShape(node, bounds)];
  if (!node.collapsed && node.expansionTray) {
    const tray = expansionTrayBounds(project, node.id);
    if (tray) shapes.push({ id: node.id, ...tray, x: tray.x + bounds.x - node.x, y: tray.y + bounds.y - node.y, shape: 'rectangle' });
  }
  if (!node.collapsed && node.containerVariant === 'window' && node.grid && !isEnclosingContainer(node)) {
    const grid = gridGeometry(node, project);
    shapes.push({ id: node.id, x: grid.left + bounds.x - node.x, y: grid.top + bounds.y - node.y,
      width: Math.min(grid.columns, node.grid.windowColumns ?? 3) * grid.cellWidth,
      height: Math.min(grid.rows, node.grid.windowRows ?? 3) * grid.cellHeight, shape: 'rectangle' });
  }
  return shapes;
}

/** Enclosing layouts own usable interior space; spatial ownership does not permit overlap. */
export function isIntentionalContainment(index: ReturnType<typeof navigationIndex>, a: string, b: string): boolean {
  return index.presentationAncestors(a).some(ancestor => ancestor.id === b && isEnclosingContainer(ancestor)) ||
    index.presentationAncestors(b).some(ancestor => ancestor.id === a && isEnclosingContainer(ancestor));
}
type IgnoreOverlap = (moving: ShapeBox, obstacle: ShapeBox) => boolean;

const moved = (box: ShapeBox, delta: Point): ShapeBox => ({ ...box, x: box.x + delta.x, y: box.y + delta.y });
const score = (point: Point, origin: Point) => (point.x - origin.x) ** 2 + (point.y - origin.y) ** 2;

/** Nearest deterministic free translation of a whole branch/selection, without mutation. */
export function collisionFreeTranslation(moving: readonly ShapeBox[], obstacles: readonly ShapeBox[] | (() => readonly ShapeBox[]), proposed: Point, ignoreOverlap: IgnoreOverlap = () => false, nearby?: (region: ShapeBox) => readonly ShapeBox[]): Point {
  if (!moving.length || Array.isArray(obstacles) && !obstacles.length) return { ...proposed };
  let all: readonly ShapeBox[] | undefined;
  const allObstacles = () => all ??= typeof obstacles === 'function' ? obstacles() : obstacles;
  const conflicts = (delta: Point) => {
    const result: Point[] = [];
    for (const box of moving) {
      const translated = moved(box, delta);
      for (const obstacle of nearby ? nearby(translated) : allObstacles()) {
        // Most peers are far apart. Only inspect ancestry for real geometric
        // overlaps; do not traverse two ownership chains for every distant pair.
        const separation = shapeSeparation(translated, obstacle);
        if (separation && !ignoreOverlap(box, obstacle)) result.push(separation);
      }
    }
    return result;
  };
  const pending = [{ ...proposed }], visited = new Set<string>();
  for (let i = 0; pending.length && i < 256; i++) {
    pending.sort((a, b) => score(a, proposed) - score(b, proposed) || a.y - b.y || a.x - b.x);
    const candidate = pending.shift()!, key = `${candidate.x.toFixed(6)},${candidate.y.toFixed(6)}`;
    if (visited.has(key)) continue; visited.add(key);
    const translations = conflicts(candidate); if (!translations.length) return candidate;
    for (const translation of translations) pending.push({ x: candidate.x + translation.x, y: candidate.y + translation.y });
  }
  // Finite escape positions outside all visible peer bounds guarantee a result,
  // even for crowded concave arrangements formed by many convex tiles.
  const left = Math.min(...moving.map(box => box.x)), top = Math.min(...moving.map(box => box.y));
  const right = Math.max(...moving.map(box => box.x + box.width)), bottom = Math.max(...moving.map(box => box.y + box.height));
  const peers = allObstacles();
  const xs = [Math.min(...peers.map(box => box.x)) - right, Math.max(...peers.map(box => box.x + box.width)) - left];
  const ys = [Math.min(...peers.map(box => box.y)) - bottom, Math.max(...peers.map(box => box.y + box.height)) - top];
  const escapes = [...xs.map(x => ({ x, y: proposed.y })), ...ys.map(y => ({ x: proposed.x, y }))];
  return escapes.sort((a, b) => score(a, proposed) - score(b, proposed) || a.y - b.y || a.x - b.x).find(candidate => !conflicts(candidate).length)!;
}

/** Keep a resize at its original top-left; stop its dimensions at peer contact. */
export function collisionFreeSize(box: ShapeBox, obstacles: readonly ShapeBox[], width: number, height: number): { width: number; height: number } {
  const resized = collisionFreeResize(box, obstacles, { ...box, width, height }); return { width: resized.width, height: resized.height };
}
export function collisionFreeResize(box: ShapeBox, obstacles: readonly ShapeBox[] | ((candidate: ShapeBox) => readonly ShapeBox[]), proposed: ShapeBox): ShapeBox {
  const interpolate = (t: number): ShapeBox => ({ ...box, x: box.x + (proposed.x - box.x) * t, y: box.y + (proposed.y - box.y) * t, width: box.width + (proposed.width - box.width) * t, height: box.height + (proposed.height - box.height) * t });
  const fits = (geometry: ShapeBox) => (typeof obstacles === 'function' ? obstacles(geometry) : obstacles).every(obstacle => !shapesOverlap(geometry, obstacle));
  if (fits(proposed)) return proposed;
  // Shrinking is always safe from an already collision-free size. If loaded
  // legacy content overlaps, preserve the original size until layout repair.
  if (!fits(box)) return box;
  let low = 0, high = 1;
  for (let i = 0; i < 35; i++) { const t = (low + high) / 2; if (fits(interpolate(t))) low = t; else high = t; }
  return interpolate(low);
}

/**
 * Repair visible collisions as one edit. Enclosing layouts retain their interior
 * content; spatial parents and children cannot overlap. Branches keep hidden descendants and internal
 * route bends together; IDs, ownership, collapse state and graph stay intact.
 */
export function resolveCanvasCollisions(project: MeridianProject, canvasId: string, preferredIds: readonly string[] = []): MeridianProject {
  const candidate = structuredClone(project), index = navigationIndex(candidate), movement = createLockedClusterIndex(candidate, canvasId), preferred = new Set(movement(preferredIds));
  const branches = new Map<string, string[]>();
  const movementFor = (id: string) => { let branch = branches.get(id); if (!branch) { branch = movement([id]); branches.set(id, branch); } return branch; };
  // Geometry is stable between translations. Clear after every repair because
  // a moved child can also change its owner's exterior Gallery/tray footprint.
  const geometry = new Map<string, ShapeBox[]>();
  const shapes = (node: MeridianNode) => { let boxes = geometry.get(node.id); if (!boxes) { boxes = collisionNodeShapes(candidate, node, index.bounds(node)); geometry.set(node.id, boxes); } return boxes; };
  const physical = (id: string) => index.isNodeVisible(id) && !index.presentationAncestors(id).some(ancestor => ancestor.containerVariant === 'window');
  const visible = candidate.nodes.filter(node => node.canvasId === canvasId && node.kind !== 'canvas' && physical(node.id));
  const belongsToPreferred = (id: string) => preferred.has(id) || index.ancestors(id).some(node => preferred.has(node.id));
  const anchored = new Set(visible.filter(node => movementFor(node.id).some(id => index.nodes.get(id)?.positionLocked)).map(node => node.id));
  visible.sort((a, b) => Number(anchored.has(b.id)) - Number(anchored.has(a.id)) || Number(belongsToPreferred(a.id)) - Number(belongsToPreferred(b.id)) || index.ancestors(a.id).length - index.ancestors(b.id).length);
  let changed = false;
  const fixed: string[] = [], fixedRanks = new Map<string, number[]>();
  const fix = (...ids: string[]) => { for (const id of ids) { const ranks = fixedRanks.get(id) ?? []; ranks.push(fixed.length); fixedRanks.set(id, ranks); fixed.push(id); } };
  let spatial: ReturnType<typeof createShapeBoundsIndex<ShapeBox>> | undefined;
  const nearby = (region: ShapeBox, branchIds: Set<string>) => {
    spatial ??= createShapeBoundsIndex(visible.flatMap(shapes));
    return spatial(region).flatMap((box, order) => branchIds.has(box.id!) ? [] : (fixedRanks.get(box.id!) ?? []).map(rank => ({ box, rank, order })))
      .sort((a, b) => a.rank - b.rank || a.order - b.order).map(item => item.box);
  };
  for (const node of visible) {
    if (fixedRanks.has(node.id)) continue;
    if (anchored.has(node.id)) { fix(node.id); continue; }
    // Interior siblings are repaired by their parent's layout. A global escape
    // translation must never eject an embedded branch through its owner's wall;
    // the enclosing ancestor moves the entire composition against external peers.
    if (index.ancestors(node.id).some(ancestor => ancestor.expansionTray) || index.presentationAncestors(node.id).some(ancestor => isEnclosingContainer(ancestor) && !ancestor.collapsed)) { fix(node.id); continue; }
    const branchIds = new Set(movementFor(node.id)), branch = [...branchIds].map(id => index.nodes.get(id)!);
    const moving = branch.filter(child => physical(child.id)).flatMap(shapes);
    const obstacles = () => fixed.filter(id => !branchIds.has(id)).flatMap(id => shapes(index.nodes.get(id)!));
    const delta = collisionFreeTranslation(moving, obstacles, { x: 0, y: 0 }, (a, b) => isIntentionalContainment(index, a.id!, b.id!), region => nearby(region, branchIds));
    if (Math.abs(delta.x) > 1e-7 || Math.abs(delta.y) > 1e-7) {
      changed = true;
      for (const child of branch) { child.x += delta.x; child.y += delta.y; }
      for (const link of candidate.links) if (branchIds.has(link.sourceId) && branchIds.has(link.targetId) && link.points) link.points = link.points.map(point => ({ x: point.x + delta.x, y: point.y + delta.y }));
      geometry.clear(); spatial = undefined;
    }
    // Spatial descendants still need their own internal collision pass. Only
    // independent roots of this rigid movement are fixed together at this step.
    fix(...branch.filter(child => physical(child.id) && !index.ancestors(child.id).some(parent => branchIds.has(parent.id))).map(child => child.id));
  }
  return changed ? reconcileSnapLinks(candidate, canvasId, undefined, { createContacts: false }) : project;
}
