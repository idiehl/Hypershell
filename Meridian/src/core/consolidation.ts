import type { MeridianProject } from './types.ts';
import { cloneProject } from './model.ts';
import { validateProject } from './rules.ts';

/** Convert included canvases to branches without changing node, connection, or asset identities.
 * This stages a new graph; callers still control composition, snapshots and revision-safe saving.
 */
export function consolidateCanvases(project: MeridianProject, targetCanvasId = project.entryCanvasId): {
  project: MeridianProject; branches: Record<string, string>;
} {
  const target = project.canvases.find(canvas => canvas.id === targetCanvasId);
  if (!target) throw new Error('The destination Work does not exist.');
  const before = validateProject(project);
  if (before.length) throw new Error(before.map(issue => issue.message).join(' '));
  const result = cloneProject(project);
  const branches = Object.fromEntries(project.canvases.map(canvas => [canvas.id, canvas.rootId]));
  const roots = new Set(Object.values(branches));
  const includedNodes = new Set(result.nodes.map(node => node.id));
  for (const node of result.nodes) {
    node.canvasId = targetCanvasId;
    if (roots.has(node.id) && node.id !== target.rootId) {
      node.kind = 'container'; node.parentId = target.rootId;
      node.collapsed = true; node.childLayout = 'spatial'; node.placement = 'near';
      node.width = 360; node.height = 200;
    }
    if (node.targetId && branches[node.targetId]) node.targetId = branches[node.targetId];
    if (node.kind === 'canvas-link' && node.targetId && includedNodes.has(node.targetId)) node.kind = 'reference';
    if (node.dependencyIds) node.dependencyIds = node.dependencyIds.filter(id => !branches[id]);
  }
  for (const link of result.links) link.canvasId = targetCanvasId;
  result.canvases = result.canvases.filter(canvas => canvas.id === targetCanvasId);
  result.entryCanvasId = targetCanvasId;
  result.dependencies = result.dependencies.filter(dependency => !branches[dependency.canvasId]);
  const issues = validateProject(result);
  if (issues.length) throw new Error(issues.map(issue => issue.message).join(' '));
  return { project: result, branches };
}
