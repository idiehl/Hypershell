import type { MeridianProject } from './types.ts';
import { navigationIndex } from './navigation.ts';
import { reparentNode, validateChange } from './rules.ts';
import { lockedClusterNodeIds, reconcileSnapLinks } from './clusters.ts';

/** Immutable graph edits. Callers own undo, saving, revision checks and snapshot commits. */
function change(project: MeridianProject, action: (draft: MeridianProject) => void): MeridianProject {
  const draft = structuredClone(project);
  action(draft);
  const issues = validateChange(project, draft);
  if (issues.length) throw new Error(issues.map(issue => issue.message).join(' '));
  return draft;
}

function requireNode(project: MeridianProject, nodeId: string) {
  const node = project.nodes.find(candidate => candidate.id === nodeId);
  if (!node) throw new Error('The node does not exist.');
  return node;
}

/** Only this node changes; each descendant retains its independent collapsed state. */
export function setNodeCollapsed(project: MeridianProject, nodeId: string, collapsed: boolean): MeridianProject {
  if (typeof collapsed !== 'boolean') throw new Error('Collapsed state must be Boolean.');
  return change(project, draft => { requireNode(draft, nodeId).collapsed = collapsed; });
}

/** Every non-root node can contain children, including canvas links and ordinary text. */
export function reparentNodeBranch(project: MeridianProject, nodeId: string, parentId: string | null): MeridianProject {
  return change(project, draft => {
    const node = requireNode(draft, nodeId);
    const parent = parentId ?? draft.canvases.find(canvas => canvas.id === node.canvasId)?.rootId;
    if (!parent) throw new Error('The parent Work does not exist.');
    // Reuse model validation for cycles, inherited permissions and canvas boundaries.
    reparentNode(draft, nodeId, parent);
  });
}

/** World coordinates remain absolute. Descendants and wholly internal routes travel together. */
export function moveNodeBranch(project: MeridianProject, nodeId: string, x: number, y: number): MeridianProject {
  if (!Number.isFinite(x) || !Number.isFinite(y)) throw new Error('Branch position must be finite.');
  return change(project, draft => {
    const node = requireNode(draft, nodeId);
    const ids = new Set(node.kind === 'canvas' ? [nodeId, ...navigationIndex(draft).descendants(nodeId).map(child => child.id)] : lockedClusterNodeIds(draft, node.canvasId, [nodeId]));
    const dx = x - node.x, dy = y - node.y;
    for (const child of draft.nodes) if (ids.has(child.id)) { child.x += dx; child.y += dy; }
    for (const link of draft.links) if (ids.has(link.sourceId) && ids.has(link.targetId) && link.points) {
      link.points = link.points.map(point => ({ x: point.x + dx, y: point.y + dy }));
    }
    Object.assign(draft, reconcileSnapLinks(draft, node.canvasId, [...ids], { createContacts: false }));
  });
}

/** Reveal an exact node by opening its ancestors, preserving its own and siblings' state. */
export function revealNodeBranch(project: MeridianProject, nodeId: string): MeridianProject {
  return change(project, draft => {
    requireNode(draft, nodeId);
    for (const parent of navigationIndex(draft).ancestors(nodeId)) parent.collapsed = false;
  });
}
