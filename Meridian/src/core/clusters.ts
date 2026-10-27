import type { MeridianLink, MeridianNode, MeridianProject, Point } from './types.ts';
import { isEnclosingContainer, isOrderedContainer } from './cells.ts';
import { navigationIndex } from './navigation.ts';
import { nearestBoundary, nodeShape, shapeBounds, shapeContact, shapesOverlap, shapeVertices, type ShapeBox } from './geometry.ts';
import { resolveSnapping } from './snapping.ts';

/** A contact relationship is a link, while a cluster is its locked connected component. */
export const isSnapLink = (link: MeridianLink): boolean => link.snap !== undefined;
export function isSnapSettings(value: unknown): value is NonNullable<MeridianLink['snap']> {
  return !!value && typeof value === 'object' && !Array.isArray(value) && Object.keys(value).length === 1
    && Object.hasOwn(value, 'locked') && typeof (value as { locked?: unknown }).locked === 'boolean';
}
export interface SnapContact { point: Point; normal: Point; sourceId?: string; targetId?: string }
const pairKey = (source: string, target: string) => JSON.stringify([source, target].sort());

/** Real silhouettes are authoritative; a polygon's invisible bounding corners never create contacts. */
export function snapContact(first: ShapeBox, second: ShapeBox, tolerance = 1e-4): SnapContact | null {
  if (![first.x, first.y, first.width, first.height, second.x, second.y, second.width, second.height, tolerance].every(Number.isFinite)
    || first.width <= 0 || first.height <= 0 || second.width <= 0 || second.height <= 0 || tolerance < 0) return null;
  const a = shapeBounds(first), b = shapeBounds(second);
  if (a.x > b.x + b.width + tolerance || b.x > a.x + a.width + tolerance || a.y > b.y + b.height + tolerance || b.y > a.y + a.height + tolerance
    || shapesOverlap(first, second)) return null;
  const straight = (shape: ShapeBox) => ['rectangle', 'square', 'none'].includes(shape.shape ?? '');
  if (straight(first) && straight(second)
    && Math.min(a.x + a.width, b.x + b.width) <= Math.max(a.x, b.x)
    && Math.min(a.y + a.height, b.y + b.height) <= Math.max(a.y, b.y)) return null;
  const correction = shapeContact(first, second, tolerance);
  if (!correction || Math.hypot(correction.x, correction.y) > tolerance) return null;
  const close: Point[] = [];
  for (const [source, target] of [[first, second], [second, first]]) for (const point of shapeVertices(source, 96)) {
    const hit = nearestBoundary(target, point);
    if (hit.distance <= tolerance + 1e-7) close.push({ x: (point.x + hit.point.x) / 2, y: (point.y + hit.point.y) / 2 });
  }
  // Curved exact support may fall between polygon samples. Project alternating
  // closest boundaries from the centres to converge on their true touching point.
  let point = { x: a.x + a.width / 2, y: a.y + a.height / 2 };
  if (close.length) point = { x: close.reduce((sum, at) => sum + at.x, 0) / close.length, y: close.reduce((sum, at) => sum + at.y, 0) / close.length };
  else {
    point = nearestBoundary(second, point).point;
    for (let i = 0; i < 12; i++) point = nearestBoundary(second, nearestBoundary(first, point).point).point;
  }
  const hit = nearestBoundary(second, point);
  return { point, normal: hit.normal, ...(first.id ? { sourceId: first.id } : {}), ...(second.id ? { targetId: second.id } : {}) };
}

/** Placement-owned cells and containment boundaries cannot be independently locked together. */
export function snapEligibility(project: MeridianProject): (sourceId: string, targetId: string) => boolean {
  const nodes = new Map(project.nodes.map(node => [node.id, node])), cached = new Map<string, MeridianNode[]>();
  const ancestors = (node: MeridianNode): MeridianNode[] => {
    const prior = cached.get(node.id); if (prior) return prior;
    const result: MeridianNode[] = [], seen = new Set([node.id]); let id = node.parentId;
    while (id && !seen.has(id)) { seen.add(id); const parent = nodes.get(id); if (!parent) break; result.push(parent); id = parent.parentId; }
    cached.set(node.id, result); return result;
  };
  const ownsPlacement = (parent?: MeridianNode) => parent && (isOrderedContainer(parent) || parent.containerVariant === 'gallery');
  return (sourceId, targetId) => {
    const source = nodes.get(sourceId), target = nodes.get(targetId);
    if (!source || !target || sourceId === targetId || source.canvasId !== target.canvasId || source.kind === 'canvas' || target.kind === 'canvas') return false;
    const first = ancestors(source), second = ancestors(target);
    if (first.some(node => node.id === targetId) || second.some(node => node.id === sourceId)) return false;
    if (ownsPlacement(first[0]) || ownsPlacement(second[0])) return false;
    if (first.find(isEnclosingContainer)?.id !== second.find(isEnclosingContainer)?.id) return false;
    return ![...first, ...second].some(node => node.rules?.linkKinds && !node.rules.linkKinds.includes('visual'));
  };
}
export function canSnapNodes(project: MeridianProject, sourceId: string, targetId: string): boolean { return snapEligibility(project)(sourceId, targetId); }

/** Expand movement through locked contacts and descendant branches, each identity once. */
export function lockedClusterNodeIds(project: MeridianProject, canvasId: string, seedIds: readonly string[]): string[] {
  return createLockedClusterIndex(project, canvasId)(seedIds);
}

/** Reuse ownership and locked contacts during one geometry transaction. Positions may change. */
export function createLockedClusterIndex(project: MeridianProject, canvasId: string): (seedIds: readonly string[]) => string[] {
  const nodes = new Map(project.nodes.map(node => [node.id, node])), children = new Map<string, string[]>(), edges = new Map<string, string[]>();
  for (const node of project.nodes) if (node.canvasId === canvasId && node.parentId) { const ids = children.get(node.parentId) ?? []; ids.push(node.id); children.set(node.parentId, ids); }
  for (const link of project.links) if (link.canvasId === canvasId && link.snap?.locked) {
    for (const [first, second] of [[link.sourceId, link.targetId], [link.targetId, link.sourceId]]) { const ids = edges.get(first) ?? []; ids.push(second); edges.set(first, ids); }
  }
  const order = new Map(project.nodes.map((node, index) => [node.id, index]));
  return seedIds => {
    const included = new Set<string>(), pending = [...seedIds];
    for (let cursor = 0; cursor < pending.length; cursor++) {
      const id = pending[cursor], node = nodes.get(id); if (!node || node.kind === 'canvas' || node.canvasId !== canvasId || included.has(id)) continue;
      included.add(id); pending.push(...children.get(id) ?? [], ...edges.get(id) ?? []);
    }
    return [...included].sort((a, b) => order.get(a)! - order.get(b)!);
  };
}

/** Unlock is always available. A new lock requires current physical contact. */
export function setSnapLock(project: MeridianProject, sourceId: string, targetId: string, locked: boolean, linkId?: string): MeridianProject {
  if (typeof locked !== 'boolean') throw new Error('Cluster lock must be Boolean.');
  if (!canSnapNodes(project, sourceId, targetId)) throw new Error('Choose separate, freely positioned nodes in the same containment space.');
  const key = pairKey(sourceId, targetId), existing = project.links.find(link => isSnapLink(link) && pairKey(link.sourceId, link.targetId) === key);
  if (linkId && (!existing || existing.id !== linkId)) throw new Error('The snapped connection is no longer available.');
  if (existing?.snap?.locked === locked) return project;
  const index = navigationIndex(project), source = index.nodes.get(sourceId)!, target = index.nodes.get(targetId)!;
  const contact = snapContact(nodeShape(source, index.bounds(source)), nodeShape(target, index.bounds(target)));
  if (locked && !contact) throw new Error('Bring the node edges together before locking this cluster.');
  if (!existing && !locked) return project;
  const candidate = structuredClone(project);
  if (existing) {
    if (!locked && !contact) candidate.links = candidate.links.filter(link => link.id !== existing.id);
    else candidate.links.find(link => link.id === existing.id)!.snap = { locked };
  } else candidate.links.push({ id: crypto.randomUUID(), canvasId: source.canvasId, sourceId, targetId, kind: 'visual', relation: 'generic', direction: 'none', snap: { locked } });
  if (locked) for (const node of candidate.nodes) if (node.id === sourceId || node.id === targetId) { node.childPosition = 'manual'; node.placement = 'free'; }
  return candidate;
}

export interface ReconcileSnapOptions { tolerance?: number; /** Only commits a relation when sticky capture is enabled. */ createContacts?: boolean }
/** Detached unlocked contacts disappear. Locked relationships remain explicit until unlocked. */
export function reconcileSnapLinks(project: MeridianProject, canvasId: string, movedIds?: readonly string[], options: ReconcileSnapOptions = {}): MeridianProject {
  const canvas = project.canvases.find(canvas => canvas.id === canvasId), affectedIds = movedIds && new Set(movedIds);
  const createContacts = options.createContacts !== false && project.nodes.some(node => node.canvasId === canvasId && node.kind !== 'canvas' && (!affectedIds || affectedIds.has(node.id)) && resolveSnapping(canvas, node).stickyEdges);
  // Ordinary alignment-only edits have no contacts to discover or reconcile.
  // Avoid constructing a whole graph/navigation index for that common case.
  if (!createContacts && !project.links.some(link => link.canvasId === canvasId && isSnapLink(link))) return project;
  const index = navigationIndex(project), eligiblePair = snapEligibility(project), affected = movedIds && new Set(movedIds), tolerance = options.tolerance ?? 1e-4;
  const shapes = new Map<string, ShapeBox>();
  const shape = (id: string) => { let value = shapes.get(id); if (!value) { const node = index.nodes.get(id)!; value = nodeShape(node, index.bounds(node)); shapes.set(id, value); } return value; };
  let changed = false;
  const links = project.links.filter(link => {
    if (link.canvasId !== canvasId || !isSnapLink(link)) return true;
    const eligible = eligiblePair(link.sourceId, link.targetId);
    // Reparenting into an ordered layout or across a wall dissolves the contact,
    // including its lock; that layout now owns the nodes' positions.
    const keep = eligible && (link.snap!.locked || !!snapContact(shape(link.sourceId), shape(link.targetId), tolerance));
    if (!keep) changed = true; return keep;
  });
  if (createContacts) {
    const pairs = new Set(links.filter(isSnapLink).map(link => pairKey(link.sourceId, link.targetId)));
    const visible = project.nodes.filter(node => node.canvasId === canvasId && node.kind !== 'canvas' && index.isNodeVisible(node.id));
    const compared = new Set<string>();
    const moving = affected ? visible.filter(node => affected.has(node.id)) : visible;
    for (const a of moving) {
      if (!resolveSnapping(canvas, a).stickyEdges) continue;
      for (const b of visible) {
        if (a.id === b.id) continue;
        const key = pairKey(a.id, b.id); if (pairs.has(key) || compared.has(key)) continue; compared.add(key);
        if (!affected && !resolveSnapping(canvas, b).stickyEdges || !eligiblePair(a.id, b.id) || !snapContact(shape(a.id), shape(b.id), tolerance)) continue;
        links.push({ id: crypto.randomUUID(), canvasId, sourceId: a.id, targetId: b.id, kind: 'visual', relation: 'generic', direction: 'none', snap: { locked: false } }); pairs.add(key); changed = true;
      }
    }
  }
  return changed ? { ...project, links } : project;
}
