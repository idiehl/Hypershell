import type { SnappingSettings } from './types.ts';
import { nearestBoundary, shapeBounds, shapeContact, type ShapeBox } from './geometry.ts';

export const GRID_SIZE = 24;
export interface SnapRect extends ShapeBox {}
export interface SnapGuide { axis: 'x' | 'y'; position: number; from: number; to: number; kind: 'sticky' | 'align'; fromPoint?: { x: number; y: number }; toPoint?: { x: number; y: number } }
export interface SnapResult { x: number; y: number; guides: SnapGuide[] }
export interface SnapOptions {
  scale?: number;
  /** Capture distance measured in screen pixels, independent of canvas zoom. */
  tolerancePx?: number;
  /** Maximum orthogonal gap for smart alignment, measured in screen pixels. */
  alignmentRangePx?: number;
}

/** Omitted node fields inherit the canvas; explicit false overrides an enabled canvas mode. */
export function resolveSnapping(canvas?: { snapping?: SnappingSettings }, node?: { snapping?: SnappingSettings }): Required<SnappingSettings> {
  return {
    stickyEdges: node?.snapping?.stickyEdges ?? canvas?.snapping?.stickyEdges ?? true,
    snapToGrid: node?.snapping?.snapToGrid ?? canvas?.snapping?.snapToGrid ?? false,
    smartAlign: node?.snapping?.smartAlign ?? canvas?.snapping?.smartAlign ?? true,
  };
}

interface Capture {
  delta: number;
  position: number;
  kind: SnapGuide['kind'];
  target: SnapRect;
  orthogonalGap: number;
  featureDistance: number;
}
const validRect = (rect: SnapRect): boolean => [rect.x, rect.y, rect.width, rect.height].every(Number.isFinite) && rect.width > 0 && rect.height > 0;
const spanGap = (a: number, sizeA: number, b: number, sizeB: number): number => Math.max(0, b - a - sizeA, a - b - sizeB);
const spansOverlap = (a: number, sizeA: number, b: number, sizeB: number): boolean => Math.min(a + sizeA, b + sizeB) > Math.max(a, b);
const zero = (value: number): number => value === 0 ? 0 : value;

/**
 * Snap a proposed world-space top-left without mutating any geometry.
 * The scene supplies eligible, visible neighbors, excluding the dragged subtree and ancestors.
 * Each axis selects its nearest edge/alignment capture before falling back to the origin-based grid.
 * Exact capture ties prefer touching edges, then the lower guide coordinate and stable geometry/ID.
 */
export function snapPosition(rect: SnapRect, candidates: readonly SnapRect[], settings: SnappingSettings, options: SnapOptions = {}): SnapResult {
  if (!validRect(rect) || !settings.stickyEdges && !settings.snapToGrid && !settings.smartAlign) return { x: rect.x, y: rect.y, guides: [] };
  const scale = Number.isFinite(options.scale) && options.scale! > 0 ? options.scale! : 1;
  const tolerance = (Number.isFinite(options.tolerancePx) ? Math.max(0, options.tolerancePx!) : 8) / scale;
  if (settings.stickyEdges && (rect.shape && rect.shape !== 'rectangle' && rect.shape !== 'none' || candidates.some(target => target.shape && target.shape !== 'rectangle' && target.shape !== 'none'))) {
    const contacts = candidates.filter(target => validRect(target) && (!rect.id || target.id !== rect.id)).flatMap(target => {
      const offset = shapeContact(rect, target, tolerance); return offset ? [{ target, offset, distance: Math.hypot(offset.x, offset.y) }] : [];
    }).sort((a, b) => a.distance - b.distance || a.target.y - b.target.y || a.target.x - b.target.x || (a.target.id ?? '').localeCompare(b.target.id ?? ''));
    const contact = contacts[0];
    if (contact) {
      const x = rect.x + contact.offset.x, y = rect.y + contact.offset.y;
      const edge = nearestBoundary(contact.target, { x: x + rect.width / 2, y: y + rect.height / 2 });
      const tangent = { x: -edge.normal.y * 20 / scale, y: edge.normal.x * 20 / scale };
      return { x, y, guides: [{ axis: Math.abs(edge.normal.x) >= Math.abs(edge.normal.y) ? 'x' : 'y', position: edge.point.x, from: edge.point.y, to: edge.point.y, kind: 'sticky', fromPoint: { x: edge.point.x - tangent.x, y: edge.point.y - tangent.y }, toPoint: { x: edge.point.x + tangent.x, y: edge.point.y + tangent.y } }] };
    }
    // Shape edges are authoritative. Axis/grid alignment can still operate,
    // but must not snap to invisible corners of a polygon's bounding rectangle.
    return snapPosition(rect, candidates, { ...settings, stickyEdges: false }, options);
  }
  const alignmentRange = (Number.isFinite(options.alignmentRangePx) ? Math.max(0, options.alignmentRangePx!) : 320) / scale;
  const physical = shapeBounds(rect);
  const captures: Record<'x' | 'y', Capture[]> = { x: [], y: [] };
  const offer = (axis: 'x' | 'y', movingPosition: number, position: number, kind: SnapGuide['kind'], target: SnapRect, featureDistance = 0): void => {
    const delta = position - movingPosition;
    const moving = kind === 'align' ? physical : rect;
    const orthogonalGap = axis === 'x' ? spanGap(moving.y, moving.height, target.y, target.height) : spanGap(moving.x, moving.width, target.x, target.width);
    if (Math.abs(delta) <= tolerance) captures[axis].push({ delta, position, kind, target, orthogonalGap, featureDistance });
  };
  for (const target of candidates) {
    if (!validRect(target) || rect.id !== undefined && target.id === rect.id) continue;
    if (settings.stickyEdges) {
      if (spansOverlap(rect.y, rect.height, target.y, target.height)) {
        offer('x', rect.x, target.x + target.width, 'sticky', target);
        offer('x', rect.x + rect.width, target.x, 'sticky', target);
      }
      if (spansOverlap(rect.x, rect.width, target.x, target.width)) {
        offer('y', rect.y, target.y + target.height, 'sticky', target);
        offer('y', rect.y + rect.height, target.y, 'sticky', target);
      }
    }
    if (settings.smartAlign) {
      const reference = shapeBounds(target);
      if (spanGap(physical.y, physical.height, reference.y, reference.height) <= alignmentRange) {
        const movingAnchors = [physical.x, physical.x + physical.width / 2, physical.x + physical.width];
        const referenceAnchors = [reference.x, reference.x + reference.width / 2, reference.x + reference.width];
        for (const [movingAt, movingPosition] of movingAnchors.entries()) for (const [referenceAt, position] of referenceAnchors.entries()) offer('x', movingPosition, position, 'align', reference, Math.abs(movingAt - referenceAt));
      }
      if (spanGap(physical.x, physical.width, reference.x, reference.width) <= alignmentRange) {
        const movingAnchors = [physical.y, physical.y + physical.height / 2, physical.y + physical.height];
        const referenceAnchors = [reference.y, reference.y + reference.height / 2, reference.y + reference.height];
        for (const [movingAt, movingPosition] of movingAnchors.entries()) for (const [referenceAt, position] of referenceAnchors.entries()) offer('y', movingPosition, position, 'align', reference, Math.abs(movingAt - referenceAt));
      }
    }
  }
  const compare = (a: Capture, b: Capture): number => {
    const distance = Math.abs(a.delta) - Math.abs(b.delta);
    if (distance) return distance;
    if (a.kind !== b.kind) return a.kind === 'sticky' ? -1 : 1;
    const proximity = a.orthogonalGap - b.orthogonalGap || a.featureDistance - b.featureDistance;
    if (proximity) return proximity;
    const coordinates = a.position - b.position || a.delta - b.delta || a.target.x - b.target.x || a.target.y - b.target.y || a.target.width - b.target.width || a.target.height - b.target.height;
    if (coordinates) return coordinates;
    const left = a.target.id ?? '', right = b.target.id ?? '';
    return left < right ? -1 : left > right ? 1 : 0;
  };
  const coordinate = (value: number, capture?: Capture): number => zero(capture ? value + capture.delta : settings.snapToGrid ? Math.round(value / GRID_SIZE) * GRID_SIZE : value);
  captures.x.sort(compare); captures.y.sort(compare);
  let xIndex = 0, yIndex = 0;
  let xCapture: Capture | undefined, yCapture: Capture | undefined, x: number, y: number;
  // A correction on the other axis must not turn an edge contact into an unrelated line.
  // Advancing through ordered candidates makes this reconciliation finite and deterministic.
  while (true) {
    xCapture = captures.x[xIndex]; yCapture = captures.y[yIndex];
    x = coordinate(rect.x, xCapture); y = coordinate(rect.y, yCapture);
    const eligible = (capture: Capture, start: number, size: number, targetStart: number, targetSize: number): boolean => capture.kind === 'sticky'
      ? spansOverlap(start, size, targetStart, targetSize)
      : spanGap(start, size, targetStart, targetSize) <= alignmentRange;
    const drawn = shapeBounds({ ...rect, x, y });
    const invalidX = xCapture && !eligible(xCapture, xCapture.kind === 'align' ? drawn.y : y, xCapture.kind === 'align' ? drawn.height : rect.height, xCapture.target.y, xCapture.target.height);
    const invalidY = yCapture && !eligible(yCapture, yCapture.kind === 'align' ? drawn.x : x, yCapture.kind === 'align' ? drawn.width : rect.width, yCapture.target.x, yCapture.target.width);
    if (!invalidX && !invalidY) break;
    if (invalidX) xIndex++;
    if (invalidY) yIndex++;
  }
  const guides: SnapGuide[] = [];
  const drawn = shapeBounds({ ...rect, x, y });
  if (xCapture) {
    const moving = xCapture.kind === 'align' ? drawn : { ...rect, x, y };
    guides.push({ axis: 'x', position: xCapture.position, from: Math.min(moving.y, xCapture.target.y), to: Math.max(moving.y + moving.height, xCapture.target.y + xCapture.target.height), kind: xCapture.kind });
  }
  if (yCapture) {
    const moving = yCapture.kind === 'align' ? drawn : { ...rect, x, y };
    guides.push({ axis: 'y', position: yCapture.position, from: Math.min(moving.x, yCapture.target.x), to: Math.max(moving.x + moving.width, yCapture.target.x + yCapture.target.width), kind: yCapture.kind });
  }
  return { x, y, guides };
}

/** Guides describe the final displayed geometry, after collision/nesting limits.
 * Rechecking with one hundredth of a screen pixel of numeric tolerance cannot
 * advertise a capture that the visible tile did not actually reach. Grid never
 * moves this verification rectangle or supplies a guide by itself. */
export function alignmentGuides(rect: SnapRect, candidates: readonly SnapRect[], settings: SnappingSettings, options: SnapOptions = {}): SnapGuide[] {
  return snapPosition(rect, candidates, { ...settings, snapToGrid: false }, { ...options, tolerancePx: .01 }).guides;
}
