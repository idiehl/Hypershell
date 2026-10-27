import type { MeridianProject, Point } from './types.ts';
import { navigationIndex } from './navigation.ts';
import { selectionGraph } from './selection.ts';
import { validateChange } from './rules.ts';
import { lockedClusterNodeIds } from './clusters.ts';

export type ArrangementAction = 'align-left' | 'align-center-x' | 'align-right' | 'align-top' | 'align-center-y' | 'align-bottom' | 'space-horizontal' | 'space-vertical';

const actions = new Set<ArrangementAction>([
  'align-left', 'align-center-x', 'align-right', 'align-top', 'align-center-y', 'align-bottom', 'space-horizontal', 'space-vertical',
]);
const finite = (...values: number[]): void => {
  if (!values.every(Number.isFinite)) throw new Error('Arrangement geometry must stay finite.');
};

/**
 * Arrange independent selected tiles using their current rendered bounds. Each
 * tile carries its whole branch, including hidden children, without changing its
 * containment or expanded size. Locked peers carry the same movement unless
 * another selected branch requires a conflicting alignment. Only routes whose
 * endpoints share one movement carry their authored bends with them.
 */
export function arrangeSelection(project: MeridianProject, ids: readonly string[], action: ArrangementAction): { project: MeridianProject; movedRootIds: string[] } {
  if (!actions.has(action)) throw new Error('Choose a supported arrangement action.');
  const selection = selectionGraph(project, ids), nav = navigationIndex(project);
  const roots = selection.rootIds.map(id => nav.nodes.get(id)!);
  const spacing = action === 'space-horizontal' || action === 'space-vertical';
  if (roots.length < (spacing ? 3 : 2)) throw new Error(spacing ? 'Select at least three independent nodes to space evenly.' : 'Select at least two independent nodes to align.');
  const boxes = roots.map(nav.bounds);
  for (const box of boxes) finite(box.x, box.y, box.width, box.height, box.x + box.width, box.y + box.height);
  const byId = new Map(boxes.map(box => [box.id, box]));
  const deltas = new Map<string, Point>(), positions = new Map<string, Point>();
  const record = (id: string, x: number, y: number): void => {
    const box = byId.get(id)!, dx = x - box.x, dy = y - box.y;
    finite(x, y, dx, dy);
    if (dx !== 0 || dy !== 0) { deltas.set(id, { x: dx, y: dy }); positions.set(id, { x, y }); }
  };

  if (spacing) {
    const horizontal = action === 'space-horizontal', axis = horizontal ? 'x' : 'y', size = horizontal ? 'width' : 'height';
    // Stable ordering keeps equal-position tiles deterministic. The outer two
    // tiles stay authored in place; intermediate tiles occupy the available span.
    const ordered = [...boxes].sort((a, b) => a[axis] - b[axis]);
    const first = ordered[0]!, last = ordered.at(-1)!;
    const span = last[axis] + last[size] - first[axis];
    const occupied = ordered.reduce((total, box) => total + box[size], 0);
    const gap = (span - occupied) / (ordered.length - 1);
    finite(span, occupied, gap);
    if (gap < 0) throw new Error('There is not enough room between the outer nodes to space these tiles without overlap.');
    let position = first[axis] + first[size] + gap;
    for (const box of ordered.slice(1, -1)) {
      record(box.id, horizontal ? position : box.x, horizontal ? box.y : position);
      position += box[size] + gap;
      finite(position);
    }
    // Floating-point rounding must not turn a tight arrangement into overlapping
    // tiles. Refuse an unrepresentable fit before accepting any of its movement.
    const coordinate = (box: typeof first) => positions.get(box.id)?.[axis] ?? box[axis];
    if (ordered.some((box, index) => index > 0 && coordinate(box) < coordinate(ordered[index - 1]!) + ordered[index - 1]![size])) {
      throw new Error('There is not enough room between the outer nodes to space these tiles without overlap.');
    }
  } else {
    const left = Math.min(...boxes.map(box => box.x)), right = Math.max(...boxes.map(box => box.x + box.width));
    const top = Math.min(...boxes.map(box => box.y)), bottom = Math.max(...boxes.map(box => box.y + box.height));
    // These forms avoid overflowing merely by adding two large same-sign positions.
    const centerX = action === 'align-center-x' ? left + (right - left) / 2 : 0;
    const centerY = action === 'align-center-y' ? top + (bottom - top) / 2 : 0;
    finite(left, right, top, bottom, centerX, centerY);
    for (const box of boxes) {
      switch (action) {
        case 'align-left': record(box.id, left, box.y); break;
        case 'align-center-x': record(box.id, centerX - box.width / 2, box.y); break;
        case 'align-right': record(box.id, right - box.width, box.y); break;
        case 'align-top': record(box.id, box.x, top); break;
        case 'align-center-y': record(box.id, box.x, centerY - box.height / 2); break;
        case 'align-bottom': record(box.id, box.x, bottom - box.height); break;
      }
    }
  }

  const requestedRoots = selection.rootIds.filter(id => deltas.has(id));
  if (!requestedRoots.length) return { project, movedRootIds: [] };
  // Stationary selected roots are also constraints: spacing promises to keep
  // the outer two tiles fixed, and an alignment must satisfy every chosen tile.
  const requestedNodes = new Map<string, Point>();
  for (const id of selection.rootIds) {
    const delta = deltas.get(id) ?? { x: 0, y: 0 };
    requestedNodes.set(id, delta);
    for (const child of nav.descendants(id)) requestedNodes.set(child.id, delta);
  }
  const movedNodes = new Map<string, Point>();
  for (const id of requestedRoots) {
    const delta = deltas.get(id)!;
    for (const member of lockedClusterNodeIds(project, selection.canvasId, [id])) {
      const prior = requestedNodes.get(member) ?? movedNodes.get(member);
      if (prior && (prior.x !== delta.x || prior.y !== delta.y)) {
        throw new Error('This arrangement would move locked Cluster members differently. Unlock their edge before arranging these nodes.');
      }
      movedNodes.set(member, delta);
    }
  }
  const movedRootIds = project.nodes.filter(node => movedNodes.has(node.id) && !nav.ancestors(node.id).some(parent => movedNodes.has(parent.id))).map(node => node.id);
  const candidate = structuredClone(project);
  for (const node of candidate.nodes) {
    const delta = movedNodes.get(node.id);
    if (!delta) continue;
    const position = positions.get(node.id);
    if (position) { node.x = position.x; node.y = position.y; }
    else { node.x += delta.x; node.y += delta.y; }
    finite(node.x, node.y, node.x + node.width, node.y + node.height);
    if (deltas.has(node.id)) node.placement = 'free';
  }
  for (const link of candidate.links) {
    const source = movedNodes.get(link.sourceId), target = movedNodes.get(link.targetId);
    if (!source || !target || source.x !== target.x || source.y !== target.y || !link.points) continue;
    link.points = link.points.map(point => {
      const translated = { x: point.x + source.x, y: point.y + source.y };
      finite(translated.x, translated.y);
      return translated;
    });
  }
  const issues = validateChange(project, candidate);
  if (issues.length) throw new Error(issues.map(issue => issue.message).join(' '));
  return { project: candidate, movedRootIds };
}
