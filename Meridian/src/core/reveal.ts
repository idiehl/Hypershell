import type { AttachSide, MeridianNode, MeridianProject, Point } from './types.ts';
import { navigationIndex } from './navigation.ts';
import { validateChange } from './rules.ts';
import { reflowSmartLinks } from './smart-links.ts';
import { nodeShape, shapeContact, shapeVertices, shapesOverlap, type ShapeBox } from './geometry.ts';
import { isIntentionalContainment, resolveCanvasCollisions } from './collision.ts';
import { isGallery } from './gallery.ts';
import { growForNestedDisclosure, isEnclosingContainer, reflowContainedChildren } from './grid.ts';
import { containingFixedInterior, nodeContentLayout } from './sizing.ts';
import { isSizeRememberingContainer, rememberContainerSize, transitionContainerSize } from './container-size.ts';
import { prepareExpansionTrays, placeExpansionTrays, migrateExpansionTrayMetrics } from './expansion-tray.ts';

interface Box extends ShapeBox {}
export interface RevealOptions {
  /** Migration/import may retain distant but valid authored positions. Free placement always does. */
  preserveValidPositions?: boolean;
}
const GAP = 28;
const sides: Exclude<AttachSide, 'auto'>[] = ['right', 'bottom', 'left', 'top'];
const overlaps = shapesOverlap;
const centerDistance = (a: Box, b: Box): number => (a.x + a.width / 2 - b.x - b.width / 2) ** 2 + (a.y + a.height / 2 - b.y - b.height / 2) ** 2;
const boxGap = (a: Box, b: Box): number => Math.hypot(Math.max(0, a.x - b.x - b.width, b.x - a.x - a.width), Math.max(0, a.y - b.y - b.height, b.y - a.y - a.height));
function envelope(boxes: Box[]): Box {
  const x = Math.min(...boxes.map(box => box.x)), y = Math.min(...boxes.map(box => box.y));
  return { x, y, width: Math.max(...boxes.map(box => box.x + box.width)) - x, height: Math.max(...boxes.map(box => box.y + box.height)) - y };
}
const shift = (box: Box, dx: number, dy: number): Box => ({ ...box, x: box.x + dx, y: box.y + dy });
function fits(boxes: Box[], obstacles: Box[], dx = 0, dy = 0): boolean {
  return boxes.every(box => obstacles.every(obstacle => !overlaps(shift(box, dx, dy), obstacle)));
}

/** True only when tile edges actually share a positive-length seam. */
export function isEdgeAttached(parent: Box, child: Box): boolean {
  if (parent.shape && parent.shape !== 'rectangle' && parent.shape !== 'none' || child.shape && child.shape !== 'rectangle' && child.shape !== 'none') {
    const contact = shapeContact(child, parent, .005); return !!contact && Math.hypot(contact.x, contact.y) < .005;
  }
  const horizontal = Math.min(parent.x + parent.width, child.x + child.width) - Math.max(parent.x, child.x) > .01;
  const vertical = Math.min(parent.y + parent.height, child.y + child.height) - Math.max(parent.y, child.y) > .01;
  return horizontal && (Math.abs(parent.y + parent.height - child.y) < .01 || Math.abs(child.y + child.height - parent.y) < .01) ||
    vertical && (Math.abs(parent.x + parent.width - child.x) < .01 || Math.abs(child.x + child.width - parent.x) < .01);
}

function edgePosition(parent: Box, tile: Box, boxes: Box[], obstacles: Box[], preferred: AttachSide): Point | undefined {
  if (parent.shape && !['rectangle', 'rounded', 'none'].includes(parent.shape) || tile.shape && !['rectangle', 'rounded', 'none'].includes(tile.shape)) {
    const initial = [{ x: tile.x, y: tile.y }];
    const vertices = shapeVertices(parent, 32);
    for (let i = 0; i < vertices.length; i++) {
      const a = vertices[i], b = vertices[(i + 1) % vertices.length];
      for (const fraction of [0, .5]) {
        const x = a.x + (b.x - a.x) * fraction, y = a.y + (b.y - a.y) * fraction;
        const dx = x - parent.x - parent.width / 2, dy = y - parent.y - parent.height / 2, length = Math.hypot(dx, dy) || 1;
        initial.push({ x: x - tile.width / 2 + dx / length * Math.max(tile.width, tile.height), y: y - tile.height / 2 + dy / length * Math.max(tile.width, tile.height) });
      }
    }
    const positions = initial.flatMap(position => { const offset = shapeContact({ ...tile, ...position }, parent, Infinity); return offset ? [{ x: position.x + offset.x, y: position.y + offset.y }] : []; });
    const sidePenalty = (point: Point) => preferred === 'auto' ? 0 : preferred === 'right' ? point.x + tile.width / 2 < parent.x + parent.width / 2 ? 1 : 0 : preferred === 'left' ? point.x + tile.width / 2 > parent.x + parent.width / 2 ? 1 : 0 : preferred === 'bottom' ? point.y + tile.height / 2 < parent.y + parent.height / 2 ? 1 : 0 : point.y + tile.height / 2 > parent.y + parent.height / 2 ? 1 : 0;
    positions.sort((a, b) => sidePenalty(a) - sidePenalty(b) || centerDistance({ ...tile, ...a }, parent) - centerDistance({ ...tile, ...b }, parent) || a.y - b.y || a.x - b.x);
    return positions.find(position => fits(boxes, obstacles, position.x - tile.x, position.y - tile.y));
  }
  // A preferred side is attempted first, then any other usable edge. Existing attachments stay stable.
  const ordered = preferred === 'auto' ? sides : [preferred, ...sides.filter(side => side !== preferred)];
  for (const side of ordered) {
    const vertical = side === 'right' || side === 'left';
    const start = vertical ? parent.y : parent.x, span = vertical ? parent.height : parent.width, length = vertical ? tile.height : tile.width;
    const seam = Math.min(24, span, length);
    const minimum = start - length + seam, maximum = start + span - seam;
    const fixed = side === 'right' ? parent.x + parent.width : side === 'left' ? parent.x - tile.width : side === 'bottom' ? parent.y + parent.height : parent.y - tile.height;
    const tangent = vertical ? tile.y : tile.x;
    const values = new Set<number>([Math.max(minimum, Math.min(maximum, tangent)), start + (span - length) / 2, start, start + span - length, minimum, maximum]);
    for (const obstacle of obstacles) {
      const at = vertical ? obstacle.y : obstacle.x, size = vertical ? obstacle.height : obstacle.width;
      values.add(Math.max(minimum, Math.min(maximum, at - length)));
      values.add(Math.max(minimum, Math.min(maximum, at + size)));
    }
    const candidates = [...values].map(value => vertical ? { x: fixed, y: value } : { x: value, y: fixed })
      .sort((a, b) => centerDistance({ ...tile, ...a }, parent) - centerDistance({ ...tile, ...b }, parent) || a.y - b.y || a.x - b.x);
    for (const candidate of candidates) if (fits(boxes, obstacles, candidate.x - tile.x, candidate.y - tile.y)) return candidate;
  }
  return undefined;
}

function nearPosition(parent: Box, tile: Box, boxes: Box[], obstacles: Box[], preserveValidPositions: boolean): Point {
  if (fits(boxes, obstacles) && (preserveValidPositions || boxGap(parent, tile) <= GAP * 2)) return { x: tile.x, y: tile.y };
  const group = envelope(boxes), offsetX = tile.x - group.x, offsetY = tile.y - group.y;
  const centerX = parent.x + parent.width / 2 - group.width / 2, centerY = parent.y + parent.height / 2 - group.height / 2;
  const xs = new Set([centerX, parent.x - group.width - GAP, parent.x + parent.width + GAP, parent.x, parent.x + parent.width - group.width]);
  const ys = new Set([centerY, parent.y - group.height - GAP, parent.y + parent.height + GAP, parent.y, parent.y + parent.height - group.height]);
  // Nearby obstacle edges produce close-fitting slots without a coordinate-sized grid search.
  const nearby = [...obstacles].sort((a, b) => centerDistance(a, parent) - centerDistance(b, parent)).slice(0, 64);
  for (const obstacle of nearby) {
    xs.add(obstacle.x - group.width - GAP); xs.add(obstacle.x + obstacle.width + GAP);
    ys.add(obstacle.y - group.height - GAP); ys.add(obstacle.y + obstacle.height + GAP);
  }
  const nearest = (values: Set<number>, center: number) => [...values].sort((a, b) => Math.abs(a - center) - Math.abs(b - center) || a - b).slice(0, 64);
  const candidates: Point[] = [];
  for (const x of nearest(xs, centerX)) for (const y of nearest(ys, centerY)) candidates.push({ x: x + offsetX, y: y + offsetY });
  // Four finite fallback locations outside every occupied box guarantee a free region.
  const occupied = envelope([parent, ...obstacles]);
  candidates.push(
    { x: occupied.x - group.width - GAP + offsetX, y: centerY + offsetY },
    { x: occupied.x + occupied.width + GAP + offsetX, y: centerY + offsetY },
    { x: centerX + offsetX, y: occupied.y - group.height - GAP + offsetY },
    { x: centerX + offsetX, y: occupied.y + occupied.height + GAP + offsetY },
  );
  candidates.sort((a, b) => centerDistance({ ...tile, ...a }, parent) - centerDistance({ ...tile, ...b }, parent) || a.y - b.y || a.x - b.x);
  for (const candidate of candidates) if (fits(boxes, obstacles, candidate.x - tile.x, candidate.y - tile.y)) return candidate;
  throw new Error('No finite space is available to reveal this branch.');
}

/**
 * Reveal or hide owned children in the same canvas. Only opted-in near/edge children move.
 * An entire branch moves together, including hidden descendants and internal route bends.
 * Free children and unrelated nodes keep their authored positions. Layout is deterministic,
 * and calling with expanded=true also repairs collisions in an already expanded branch.
 */
export function revealChildren(project: MeridianProject, parentId: string, expanded: boolean, options: RevealOptions = {}): MeridianProject {
  let candidate = structuredClone(project);
  const migratedTrays = migrateExpansionTrayMetrics(candidate);
  for (const id of migratedTrays) candidate = reflowContainedChildren(candidate, id);
  let index = navigationIndex(candidate), parent = index.nodes.get(parentId);
  if (!parent) throw new Error('Choose an existing node to reveal its children.');
  if (parent.kind === 'canvas') throw new Error('The Work root is always visible. Reveal one of its nodes instead.');
  const smartManaged=!!parent.smartLink||index.ancestors(parentId).some(ancestor=>!!ancestor.smartLink);
  const switching = (parent.collapsed === true) === expanded;
  const allocatedFrame = !!containingFixedInterior(candidate, parent);
  if (switching && isSizeRememberingContainer(parent)) {
    const closed = !expanded ? nodeContentLayout(candidate, { ...parent, collapsed: true }) : undefined;
    const blank = closed && !closed.headerHeight && !closed.bodyHeight;
    const seed = expanded ? { width: parent.width, height: parent.height, sizeMode: parent.sizeMode ?? 'auto' as const }
      : { width: blank ? Math.min(200, parent.width) : closed!.width, height: blank ? Math.min(120, parent.height) : closed!.height, sizeMode: 'auto' as const };
    transitionContainerSize(parent, expanded, seed, { preserveCurrentRecord: allocatedFrame });
  } else parent.collapsed = !expanded;
  const createdTrays = expanded ? prepareExpansionTrays(candidate) : [];
  for (const id of createdTrays) candidate = reflowContainedChildren(candidate, id);
  index = navigationIndex(candidate); parent = index.nodes.get(parentId)!;
  const enclosed = isEnclosingContainer(parent) && !isGallery(parent);
  if (expanded) {
    candidate = growForNestedDisclosure(candidate, parentId);
    index = navigationIndex(candidate); parent = index.nodes.get(parentId)!;
  }
  if (expanded && (enclosed || parent.containerVariant || parent.childArrangement || (index.children.get(parentId) ?? []).some(child => child.childPosition === 'auto'))) {
    candidate = reflowContainedChildren(candidate, parentId);
    index = navigationIndex(candidate); parent = index.nodes.get(parentId)!;
  }
  if (expanded && !smartManaged && !enclosed && !parent.expansionTray && !isGallery(parent) && index.visibleNode(parentId)?.id === parentId) {
    const children = index.children.get(parentId) ?? [];
    const moving = children.filter(node => node.childPosition !== 'auto' && (node.placement === 'near' || node.placement === 'edge'))
      .sort((a, b) => Number(b.placement === 'edge') - Number(a.placement === 'edge'));
    const branches = new Map(moving.map(node => [node.id, [node, ...index.descendants(node.id)]]));
    const movingIds = new Set([...branches.values()].flat().map(node => node.id));
    const geometry = (node: MeridianNode) => nodeShape(node, index.bounds(node));
    // Every exposed branch shares these ancestor frames; spatial ownership remains an obstacle.
    const obstacles = candidate.nodes.filter(node => node.canvasId === parent.canvasId && node.kind !== 'canvas' && !movingIds.has(node.id) && !moving.some(child => isIntentionalContainment(index, node.id, child.id)) && index.visibleNode(node.id)?.id === node.id).map(geometry);
    const parentBox = geometry(parent);
    for (const child of moving) {
      const branch = branches.get(child.id)!, ids = new Set(branch.map(node => node.id));
      const visible = branch.filter(node => index.visibleNode(node.id)?.id === node.id), boxes = visible.map(geometry), tile = geometry(child);
      const position = child.placement === 'edge' ? edgePosition(parentBox, tile, boxes, obstacles, child.attachSide ?? 'auto') ?? nearPosition(parentBox, tile, boxes, obstacles, false) : nearPosition(parentBox, tile, boxes, obstacles, options.preserveValidPositions ?? false);
      const dx = position.x - child.x, dy = position.y - child.y;
      for (const node of branch) { node.x += dx; node.y += dy; }
      for (const link of candidate.links) if (ids.has(link.sourceId) && ids.has(link.targetId) && link.points) link.points = link.points.map(point => ({ x: point.x + dx, y: point.y + dy }));
      obstacles.push(...visible.map(geometry));
    }
  }
  if (expanded) {
    placeExpansionTrays(candidate, createdTrays);
    if(!smartManaged)candidate = resolveCanvasCollisions(candidate, parent.canvasId, isGallery(parent) ? [parentId] : (index.children.get(parentId) ?? []).map(node => node.id));
  }
  if(smartManaged)reflowSmartLinks(candidate,parent.canvasId);
  if (switching) rememberContainerSize(candidate.nodes.find(node => node.id === parentId)!, undefined, { preserveExisting: allocatedFrame });
  const issues = validateChange(project, candidate);
  if (issues.length) throw new Error(issues.map(issue => issue.message).join(' '));
  return candidate;
}
