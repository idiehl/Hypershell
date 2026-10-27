import type { MeridianNode, NodeShape, Point } from './types.ts';

/** Backdrop geometry, shared by contact, collision, outlines and link endpoints. */
export interface ShapeBox extends Point { id?: string; width: number; height: number; shape?: NodeShape; radius?: number }

/** Transaction-local broad phase. It only excludes disjoint outer rectangles;
 * exact curved/polygon collision rules still decide every returned candidate.
 * Splitting by the longest axis accommodates both tiny and enormous Work nodes. */
export function createShapeBoundsIndex<T extends ShapeBox>(boxes: readonly T[]): (region: ShapeBox) => T[] {
  type Entry = { box: T; order: number };
  type Branch = { left: number; top: number; right: number; bottom: number; entries?: Entry[]; children?: [Branch, Branch] };
  const build = (entries: Entry[]): Branch => {
    let left = Infinity, top = Infinity, right = -Infinity, bottom = -Infinity;
    for (const { box } of entries) { left = Math.min(left, box.x); top = Math.min(top, box.y); right = Math.max(right, box.x + box.width); bottom = Math.max(bottom, box.y + box.height); }
    if (entries.length <= 12) return { left, top, right, bottom, entries };
    const horizontal = right - left >= bottom - top;
    entries.sort((a, b) => horizontal ? (a.box.x + a.box.width / 2) - (b.box.x + b.box.width / 2) || a.order - b.order : (a.box.y + a.box.height / 2) - (b.box.y + b.box.height / 2) || a.order - b.order);
    const middle = Math.floor(entries.length / 2);
    return { left, top, right, bottom, children: [build(entries.slice(0, middle)), build(entries.slice(middle))] };
  };
  const root = build(boxes.map((box, order) => ({ box, order })));
  return region => {
    const matches: Entry[] = [], right = region.x + region.width, bottom = region.y + region.height;
    const visit = (branch: Branch) => {
      if (region.x >= branch.right || right <= branch.left || region.y >= branch.bottom || bottom <= branch.top) return;
      if (branch.entries) for (const entry of branch.entries) { const box = entry.box; if (region.x < box.x + box.width && right > box.x && region.y < box.y + box.height && bottom > box.y) matches.push(entry); }
      else { visit(branch.children![0]); visit(branch.children![1]); }
    };
    visit(root); return matches.sort((a, b) => a.order - b.order).map(entry => entry.box);
  };
}
const EPS = 1e-7;
const polygons: Partial<Record<NodeShape, readonly (readonly [number, number])[]>> = {
  triangle: [[.5, 0], [1, 1], [0, 1]], diamond: [[.5, 0], [1, .5], [.5, 1], [0, .5]],
  pentagon: [[.5, 0], [1, (1 - Math.cos(Math.PI * 2 / 5)) / (1 + Math.cos(Math.PI / 5))], [.5 + .5 * Math.sin(Math.PI / 5) / Math.sin(Math.PI * 2 / 5), 1], [.5 - .5 * Math.sin(Math.PI / 5) / Math.sin(Math.PI * 2 / 5), 1], [0, (1 - Math.cos(Math.PI * 2 / 5)) / (1 + Math.cos(Math.PI / 5))]],
  hexagon: [[.25, 0], [.75, 0], [1, .5], [.75, 1], [.25, 1], [0, .5]],
  octagon: [[1 - Math.SQRT1_2, 0], [Math.SQRT1_2, 0], [1, 1 - Math.SQRT1_2], [1, Math.SQRT1_2], [Math.SQRT1_2, 1], [1 - Math.SQRT1_2, 1], [0, Math.SQRT1_2], [0, 1 - Math.SQRT1_2]],
};
/** Regular geometric backdrops retain their intrinsic proportions. Ellipse,
 * rectangles and labels deliberately retain the authored frame's aspect. */
export function shapeAspectRatio(shape: NodeShape | undefined): number | undefined {
  if (['circle', 'square', 'diamond', 'octagon'].includes(shape ?? '')) return 1;
  if (shape === 'triangle' || shape === 'hexagon') return 2 / Math.sqrt(3);
  if (shape === 'pentagon') return 2 * Math.sin(Math.PI * 2 / 5) / (1 + Math.cos(Math.PI / 5));
  return undefined;
}
/** An axis-aligned reading rectangle whose corners lie within the silhouette.
 * These fractions are applied to the physical backdrop, never its outer frame. */
export function shapeContentInsets(shape: NodeShape | undefined) {
  return shape === 'circle' || shape === 'ellipse' ? { x: (1 - Math.SQRT1_2) / 2, top: (1 - Math.SQRT1_2) / 2, bottom: (1 - Math.SQRT1_2) / 2 }
    : shape === 'triangle' ? { x: .25, top: .5, bottom: 0 }
    : shape === 'diamond' ? { x: .25, top: .25, bottom: .25 }
    : shape === 'pentagon' ? { x: .2, top: .25, bottom: .02 }
    : shape === 'hexagon' ? { x: 1 / 6, top: 1 / 6, bottom: 1 / 6 }
    : shape === 'octagon' ? { x: (1 - Math.SQRT1_2) / 2, top: (1 - Math.SQRT1_2) / 2, bottom: (1 - Math.SQRT1_2) / 2 }
    : { x: 0, top: 0, bottom: 0 };
}
export function nodeShape(node: MeridianNode, bounds: ShapeBox = node): ShapeBox {
  if (node.cluster && node.collapsed) return { ...bounds, id: node.id, shape: node.cluster.buttonShape, radius: 0 };
  const style = node.style;
  const shape = style?.shape ?? (node.kind === 'junction' ? 'circle' : style?.variant === 'label' && node.kind === 'text' ? 'none' : 'rounded');
  const radius = style?.radius ?? (style?.shape === 'rounded' ? 20 : node.kind === 'canvas' || node.kind === 'container' ? 15 : 11);
  return { ...bounds, id: node.id, shape, radius };
}
export function shapeBounds(box: ShapeBox): ShapeBox {
  const ratio = shapeAspectRatio(box.shape); if (ratio === undefined) return box;
  const height = Math.min(box.height, box.width / ratio), width = height * ratio;
  return { ...box, x: box.x + (box.width - width) / 2, y: box.y + (box.height - height) / 2, width, height };
}
export function shapeVertices(box: ShapeBox, curveSegments = 96): Point[] {
  const bounds = shapeBounds(box), { x, y, width, height } = bounds;
  if (box.shape === 'circle' || box.shape === 'ellipse') return Array.from({ length: curveSegments }, (_, i) => {
    const angle = Math.PI * 2 * i / curveSegments; return { x: x + width / 2 * (1 + Math.cos(angle)), y: y + height / 2 * (1 + Math.sin(angle)) };
  });
  if (!box.shape || box.shape === 'rounded') {
    const radius = Math.min(box.radius ?? 11, width / 2, height / 2), segments = Math.max(4, Math.ceil(curveSegments / 4));
    const corners = [[x + width - radius, y + radius, -Math.PI / 2], [x + width - radius, y + height - radius, 0], [x + radius, y + height - radius, Math.PI / 2], [x + radius, y + radius, Math.PI]];
    return corners.flatMap(([cx, cy, start]) => Array.from({ length: segments + 1 }, (_, i) => { const angle = start + Math.PI / 2 * i / segments; return { x: cx + radius * Math.cos(angle), y: cy + radius * Math.sin(angle) }; }));
  }
  const polygon = box.shape && polygons[box.shape];
  return (polygon || [[0, 0], [1, 0], [1, 1], [0, 1]]).map(([px, py]) => ({ x: x + px * width, y: y + py * height }));
}
function support(box: ShapeBox, normal: Point): Point {
  const bounds = shapeBounds(box), cx = bounds.x + bounds.width / 2, cy = bounds.y + bounds.height / 2;
  if (box.shape === 'circle' || box.shape === 'ellipse') {
    const rx = bounds.width / 2, ry = bounds.height / 2, length = Math.hypot(rx * normal.x, ry * normal.y);
    return { x: cx + rx * rx * normal.x / length, y: cy + ry * ry * normal.y / length };
  }
  if (box.shape === 'rounded' || !box.shape) {
    const r = Math.min(box.radius ?? 11, bounds.width / 2, bounds.height / 2), length = Math.hypot(normal.x, normal.y);
    return { x: cx + Math.sign(normal.x) * (bounds.width / 2 - r) + r * normal.x / length, y: cy + Math.sign(normal.y) * (bounds.height / 2 - r) + r * normal.y / length };
  }
  return shapeVertices(box).reduce((best, point) => point.x * normal.x + point.y * normal.y > best.x * normal.x + best.y * normal.y ? point : best);
}
function axes(a: ShapeBox, b: ShapeBox): Point[] {
  const result: Point[] = [{ x: 1, y: 0 }, { x: 0, y: 1 }];
  for (const box of [a, b]) {
    if (box.shape && polygons[box.shape]) {
      const points = shapeVertices(box);
      for (let i = 0; i < points.length; i++) { const p = points[i], q = points[(i + 1) % points.length], dx = q.x - p.x, dy = q.y - p.y, length = Math.hypot(dx, dy); result.push({ x: -dy / length, y: dx / length }); }
    } else if (!box.shape || ['circle', 'ellipse', 'rounded'].includes(box.shape)) {
      // Exact curved supports, sampled separating axes: conservative collision,
      // never an inscribed polygon that permits two curved backdrops to overlap.
      for (let i = 1; i < 128; i++) { const angle = Math.PI * i / 128; result.push({ x: Math.cos(angle), y: Math.sin(angle) }); }
    }
  }
  const dx = a.x + a.width / 2 - b.x - b.width / 2, dy = a.y + a.height / 2 - b.y - b.height / 2, length = Math.hypot(dx, dy);
  if (length) result.push({ x: dx / length, y: dy / length });
  return result;
}
function interval(box: ShapeBox, axis: Point): [number, number] {
  const max = support(box, axis), min = support(box, { x: -axis.x, y: -axis.y });
  return [min.x * axis.x + min.y * axis.y, max.x * axis.x + max.y * axis.y];
}
function broadOverlap(a: ShapeBox, b: ShapeBox, gap = 0): boolean {
  const first = shapeBounds(a), second = shapeBounds(b);
  return first.x < second.x + second.width + gap && first.x + first.width > second.x - gap && first.y < second.y + second.height + gap && first.y + first.height > second.y - gap;
}
/** Positive-area collision. Exact contact is allowed and does not count as overlap. */
export function shapesOverlap(a: ShapeBox, b: ShapeBox): boolean {
  if (!broadOverlap(a, b)) return false;
  for (const axis of axes(a, b)) { const av = interval(a, axis), bv = interval(b, axis); if (av[1] <= bv[0] + EPS || bv[1] <= av[0] + EPS) return false; }
  return true;
}
/** Least separating translation for moving a. Includes diagonal polygon normals. */
export function shapeSeparation(a: ShapeBox, b: ShapeBox): Point | null {
  if (!broadOverlap(a, b)) return null;
  let best: Point | null = null, distance = Infinity;
  for (const axis of axes(a, b)) {
    const av = interval(a, axis), bv = interval(b, axis);
    if (av[1] <= bv[0] + EPS || bv[1] <= av[0] + EPS) return null;
    for (const delta of [bv[0] - av[1], bv[1] - av[0]]) if (Math.abs(delta) < distance) { distance = Math.abs(delta); best = { x: delta * axis.x, y: delta * axis.y }; }
  }
  return best;
}
/** A near edge/curve may attract the moving shape along that edge's normal. */
export function shapeContact(a: ShapeBox, b: ShapeBox, tolerance: number): Point | null {
  if (!broadOverlap(a, b, tolerance)) return null;
  let best: Point | null = null, distance = Infinity;
  const normals = axes(a, b);
  for (const axis of normals) {
    const av = interval(a, axis), bv = interval(b, axis);
    for (const delta of [bv[0] - av[1], bv[1] - av[0]]) {
      if (Math.abs(delta) > tolerance || Math.abs(delta) >= distance - EPS) continue;
      const contact = { ...a, x: a.x + delta * axis.x, y: a.y + delta * axis.y };
      // Projection contact is real only when every other projection overlaps.
      if (normals.some(normal => { const first = interval(contact, normal), second = interval(b, normal); return first[1] < second[0] - EPS || second[1] < first[0] - EPS; })) continue;
      distance = Math.abs(delta); best = { x: delta * axis.x, y: delta * axis.y };
    }
  }
  return best;
}
export function shapeAnchor(box: ShapeBox, dx: number, dy: number): Point {
  const bounds = shapeBounds(box), cx = box.x + box.width / 2, cy = box.y + box.height / 2;
  if (!dx && !dy) return { x: cx, y: cy };
  if (box.shape === 'circle' || box.shape === 'ellipse') { const divisor = Math.hypot(dx / (bounds.width / 2), dy / (bounds.height / 2)); return { x: cx + dx / divisor, y: cy + dy / divisor }; }
  const points = shapeVertices(box); let best = Infinity;
  for (let i = 0; i < points.length; i++) {
    const a = points[i], b = points[(i + 1) % points.length], ax = a.x - cx, ay = a.y - cy, ex = b.x - a.x, ey = b.y - a.y, cross = dx * ey - dy * ex;
    if (Math.abs(cross) < EPS) continue;
    const distance = (ax * ey - ay * ex) / cross, fraction = (ax * dy - ay * dx) / cross;
    if (distance >= 0 && fraction >= -EPS && fraction <= 1 + EPS) best = Math.min(best, distance);
  }
  return { x: cx + dx * best, y: cy + dy * best };
}
/** SVG path uses the same authored polygon/curve as the CSS backdrop. */
export function shapePath(box: ShapeBox): string {
  const b = shapeBounds(box), x = b.x, y = b.y, w = b.width, h = b.height;
  if (box.shape === 'circle' || box.shape === 'ellipse') return `M${x},${y + h / 2}a${w / 2},${h / 2} 0 1 0 ${w},0a${w / 2},${h / 2} 0 1 0 ${-w},0Z`;
  if (!box.shape || box.shape === 'rounded') {
    const r = Math.min(box.radius ?? 11, w / 2, h / 2);
    return `M${x + r},${y}H${x + w - r}A${r},${r} 0 0 1 ${x + w},${y + r}V${y + h - r}A${r},${r} 0 0 1 ${x + w - r},${y + h}H${x + r}A${r},${r} 0 0 1 ${x},${y + h - r}V${y + r}A${r},${r} 0 0 1 ${x + r},${y}Z`;
  }
  return shapeVertices(box).map((point, i) => `${i ? 'L' : 'M'}${point.x},${point.y}`).join(' ') + 'Z';
}
export interface BoundaryHit { point: Point; normal: Point; distance: number }
/** Closest real backdrop edge, for resizing without visible grab dots. */
export function nearestBoundary(box: ShapeBox, point: Point): BoundaryHit {
  const vertices = shapeVertices(box, 192); let result: BoundaryHit = { point, normal: { x: 0, y: 0 }, distance: Infinity };
  for (let i = 0; i < vertices.length; i++) {
    const a = vertices[i], b = vertices[(i + 1) % vertices.length], dx = b.x - a.x, dy = b.y - a.y, length = Math.hypot(dx, dy);
    const t = Math.max(0, Math.min(1, ((point.x - a.x) * dx + (point.y - a.y) * dy) / (length * length)));
    const closest = { x: a.x + dx * t, y: a.y + dy * t }, distance = Math.hypot(point.x - closest.x, point.y - closest.y);
    if (distance < result.distance) result = { point: closest, normal: { x: dy / length, y: -dx / length }, distance };
  }
  return result;
}
