import type { MeridianNode, MeridianProject, Point } from './types.ts';
import { shapeBounds, shapesOverlap, type ShapeBox } from './geometry.ts';
import { collisionNodeShapes, collisionFreeTranslation } from './collision.ts';
import { navigationIndex } from './navigation.ts';
import { isEnclosingContainer } from './cells.ts';

const PIVOT_RADIUS = 6;
const pivotBox = (point: Point): ShapeBox => ({ x: point.x - PIVOT_RADIUS, y: point.y - PIVOT_RADIUS, width: PIVOT_RADIUS * 2, height: PIVOT_RADIUS * 2, shape: 'circle' });
const free = (point: Point, obstacles: readonly ShapeBox[]) => obstacles.every(obstacle => !shapesOverlap(pivotBox(point), obstacle));

/** The same visible geometry used by collision repair, excluding a shared
 * enclosure that will intentionally own the completed path's utility nodes. */
export function linkPivotObstacles(project: MeridianProject, canvasId: string, sourceId: string, targetId?: string, bounds?: (node: MeridianNode) => ShapeBox): ShapeBox[] {
  const index = navigationIndex(project), sourceParents = new Set(index.ancestors(sourceId).map(node => node.id));
  const owners = new Set((targetId ? index.ancestors(targetId).filter(node => sourceParents.has(node.id)) : index.ancestors(sourceId))
    .filter(isEnclosingContainer).map(node => node.id));
  return project.nodes.filter(node => node.canvasId === canvasId && node.kind !== 'canvas' && index.isNodeVisible(node.id) && !owners.has(node.id)
    && !index.ancestors(node.id).some(parent => parent.containerVariant === 'window'))
    .flatMap(node => collisionNodeShapes(project, node, bounds?.(node) ?? index.bounds(node)));
}

/** Pending pivots also occupy space before the route becomes a graph edit. */
export function draftPivotObstacles(points: readonly Point[]): ShapeBox[] { return points.map(pivotBox); }

/** Shift locks a route's next segment to its nearest canvas axis. */
export function axisLockedPoint(origin: Point, point: Point, obstacles: readonly ShapeBox[] = [], direction?: 'x' | 'y'): Point {
  const horizontal = direction === 'x' || direction === undefined && Math.abs(point.x - origin.x) >= Math.abs(point.y - origin.y);
  const projected = horizontal
    ? { x: point.x, y: origin.y }
    : { x: origin.x, y: point.y };
  if (free(projected, obstacles)) return projected;
  // Slide along the locked axis instead of letting general collision repair
  // move the new pivot perpendicular to that axis.
  const candidates = obstacles.flatMap(obstacle => {
    const box = shapeBounds(obstacle), margin = PIVOT_RADIUS + 1;
    return horizontal ? [{ x: box.x - margin, y: origin.y }, { x: box.x + box.width + margin, y: origin.y }]
      : [{ x: origin.x, y: box.y - margin }, { x: origin.x, y: box.y + box.height + margin }];
  });
  return candidates.sort((a, b) => Math.hypot(a.x - projected.x, a.y - projected.y) - Math.hypot(b.x - projected.x, b.y - projected.y)
    || a.y - b.y || a.x - b.x).find(candidate => free(candidate, obstacles))!;
}

/** Recheck remembered Shift axes against the completed route's owner. Hovering
 * another destination can change enclosing obstacles; replaying in order keeps
 * the preview and committed path on the same axes without mutating the draft. */
export function constrainDraftRoute(origin: Point, points: readonly Point[], directions: readonly ('x' | 'y' | undefined)[], obstacles: readonly ShapeBox[]): Point[] {
  const result: Point[] = [];
  for (const [at, point] of points.entries()) {
    const previous = result.at(-1) ?? origin, direction = directions[at], occupied = [...obstacles, ...draftPivotObstacles(result)];
    if (direction !== undefined) result.push(axisLockedPoint(previous, point, occupied, direction));
    else {
      // The final destination can turn a former enclosure into an ordinary
      // obstacle. Repair that free point before locking its following segment,
      // so a later canvas repair cannot change the remembered Shift axis.
      const delta = collisionFreeTranslation([pivotBox(point)], occupied, { x: 0, y: 0 });
      result.push({ x: point.x + delta.x, y: point.y + delta.y });
    }
  }
  return result;
}

/** A destination remains connected even when its center is off the locked axis. */
export function orthogonalFinish(origin: Point, destination: Point, obstacles: readonly ShapeBox[] = []): Point[] {
  if (Math.abs(destination.x - origin.x) < .001 || Math.abs(destination.y - origin.y) < .001) return [];
  const preferred = Math.abs(destination.x - origin.x) >= Math.abs(destination.y - origin.y)
    ? { x: destination.x, y: origin.y } : { x: origin.x, y: destination.y };
  if (free(preferred, obstacles)) return [preferred];
  const alternate = { x: origin.x + destination.x - preferred.x, y: origin.y + destination.y - preferred.y };
  if (free(alternate, obstacles)) return [alternate];
  // If both corners are occupied, two bends beyond the visible peer bounds
  // guarantee clear utility positions while retaining orthogonal segments.
  const boxes = obstacles.map(shapeBounds), margin = PIVOT_RADIUS + 1;
  const xs = [Math.min(origin.x, destination.x, ...boxes.map(box => box.x)) - margin,
    Math.max(origin.x, destination.x, ...boxes.map(box => box.x + box.width)) + margin];
  const ys = [Math.min(origin.y, destination.y, ...boxes.map(box => box.y)) - margin,
    Math.max(origin.y, destination.y, ...boxes.map(box => box.y + box.height)) + margin];
  const routes = [...xs.map(x => [{ x, y: origin.y }, { x, y: destination.y }]),
    ...ys.map(y => [{ x: origin.x, y }, { x: destination.x, y }])];
  const length = (points: readonly Point[]) => [origin, ...points, destination].slice(1).reduce((sum, point, i) => {
    const previous = [origin, ...points][i]!; return sum + Math.abs(point.x - previous.x) + Math.abs(point.y - previous.y);
  }, 0);
  return routes.sort((a, b) => length(a) - length(b)).find(points => points.every(point => free(point, obstacles)))!;
}
