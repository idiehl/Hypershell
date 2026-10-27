import type { MeridianProject, Point } from './types.ts';
import { transitionContainerSize } from './container-size.ts';
import { navigationIndex } from './navigation.ts';
import { selectionGraph } from './selection.ts';
import { validateChange } from './rules.ts';
import { reflowContainedChildren } from './grid.ts';
import { lockedClusterNodeIds, reconcileSnapLinks } from './clusters.ts';
import { isEnclosingContainer, isOrderedContainer } from './cells.ts';
import { boundedChildInterior, containingFixedInterior } from './sizing.ts';

/** Drop a whole selection into a parent as one checked edit; originals stay intact on failure. */
export function nestNodeBranches(project: MeridianProject, ids: readonly string[], parentId: string, delta: Point = { x: 0, y: 0 }): MeridianProject {
  if (!Number.isFinite(delta.x) || !Number.isFinite(delta.y)) throw new Error('Drop position must be finite.');
  const requested = selectionGraph(project, ids);
  const selection = selectionGraph(project, lockedClusterNodeIds(project, requested.canvasId, requested.nodeIds)), included = new Set(selection.nodeIds), roots = new Set(selection.rootIds);
  const parent = project.nodes.find(node => node.id === parentId);
  if (!parent || parent.kind === 'canvas') throw new Error('Drop onto a node inside this Work.');
  if (parent.canvasId !== selection.canvasId) throw new Error('A drop cannot move nodes to another Work.');
  if (included.has(parentId)) throw new Error('A node cannot be dropped into itself or its descendants.');
  if (selection.rootIds.every(id => project.nodes.find(node => node.id === id)?.parentId === parentId)) throw new Error('These nodes already belong to this parent.');
  const candidate = structuredClone(project);
  const lockedMembers = new Set(candidate.links.filter(link => link.snap?.locked && included.has(link.sourceId) && included.has(link.targetId)).flatMap(link => [link.sourceId, link.targetId]));
  const originalIndex = navigationIndex(project);
  const rigidRoots = new Set(!isOrderedContainer(parent) && parent.containerVariant !== 'gallery'
    ? selection.rootIds.filter(id => [id, ...originalIndex.descendants(id).map(node => node.id)].some(id => lockedMembers.has(id))) : []);
  const previousParents = new Set(candidate.nodes.filter(node => roots.has(node.id) && node.parentId).map(node => node.parentId!));
  for (const node of candidate.nodes) if (included.has(node.id)) {
    node.x += delta.x; node.y += delta.y;
    if (roots.has(node.id)) {
      node.parentId = parentId; node.placement = 'free'; node.childPosition = rigidRoots.has(node.id) ? 'manual' : 'auto'; delete node.cell;
    }
  }
  for (const link of candidate.links) if (included.has(link.sourceId) && included.has(link.targetId) && link.points) {
    link.points = link.points.map(point => ({ x: point.x + delta.x, y: point.y + delta.y }));
  }
  transitionContainerSize(candidate.nodes.find(node => node.id === parentId)!, true, undefined, { preserveCurrentRecord: !!containingFixedInterior(candidate, parent) });
  if (rigidRoots.size) {
    const owner = candidate.nodes.find(node => node.id === parentId)!;
    if (isEnclosingContainer(owner)) {
      // A drop just beyond an inner wall belongs to the whole locked
      // composition. Clamp it together before Freeform can pack its roots
      // separately and accidentally alter their locked edge offsets.
      const nav = navigationIndex(candidate), boxes = [...rigidRoots].map(id => nav.branchBounds(nav.nodes.get(id)!));
      const inner = boundedChildInterior(candidate, owner), left = Math.min(...boxes.map(box => box.x)), top = Math.min(...boxes.map(box => box.y));
      const width = Math.max(...boxes.map(box => box.x + box.width)) - left, height = Math.max(...boxes.map(box => box.y + box.height)) - top;
      if (width <= inner.width && height <= inner.height) {
        const dx = Math.max(inner.x, Math.min(left, inner.x + inner.width - width)) - left;
        const dy = Math.max(inner.y, Math.min(top, inner.y + inner.height - height)) - top;
        if (dx || dy) {
          for (const node of candidate.nodes) if (included.has(node.id)) { node.x += dx; node.y += dy; }
          for (const link of candidate.links) if (included.has(link.sourceId) && included.has(link.targetId) && link.points)
            link.points = link.points.map(point => ({ x: point.x + dx, y: point.y + dy }));
        }
      }
    }
  }
  Object.assign(candidate, reconcileSnapLinks(candidate, selection.canvasId, selection.nodeIds, { createContacts: false }));
  // The receiving display pattern owns placement. Freeform's automatic marker
  // makes a first drop radial; subsequent manual child drags retain their positions.
  Object.assign(candidate, reflowContainedChildren(candidate, parentId));
  for (const id of previousParents) if (id !== parentId) Object.assign(candidate, reflowContainedChildren(candidate, id));
  Object.assign(candidate, reconcileSnapLinks(candidate, selection.canvasId, selection.nodeIds, { createContacts: false }));
  const issues = validateChange(project, candidate);
  if (issues.length) throw new Error(issues.map(issue => issue.message).join(' '));
  return candidate;
}

/** Preview uses the same validation as the release, including cycles, rules and occupied cells. */
export function canNestNodeBranches(project: MeridianProject, ids: readonly string[], parentId: string): boolean {
  try { nestNodeBranches(project, ids, parentId); return true; } catch { return false; }
}

/** Candidate order favors the deepest, most specific visible receiving tile. */
export function nestingTargetOrder(project: MeridianProject, ids: readonly string[]): string[] {
  const index = navigationIndex(project), included = new Set(selectionGraph(project, ids).nodeIds);
  const canvasId = index.nodes.get(ids[0])?.canvasId;
  return project.nodes.filter(node => node.canvasId === canvasId && node.kind !== 'canvas' && !included.has(node.id) && index.isNodeVisible(node.id))
    .sort((a, b) => index.ancestors(b.id).length - index.ancestors(a.id).length || index.bounds(a).width * index.bounds(a).height - index.bounds(b).width * index.bounds(b).height)
    .map(node => node.id);
}
