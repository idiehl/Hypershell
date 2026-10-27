import type { MeridianProject } from './types.ts';
import { navigationIndex } from './navigation.ts';

/** Explicit choices, independent of ordinary canvas selection and document data. */
export interface ModuleSelectionState { overrides: Record<string, boolean> }
export interface ModuleSelection { nodeIds: string[]; linkIds: string[] }

export function emptyModuleSelection(): ModuleSelectionState { return { overrides: Object.create(null) }; }

/** A parent's choice includes its descendants unless a nearer explicit choice overrides it. */
export function resolveModuleSelection(project: MeridianProject, state: ModuleSelectionState, canvasId?: string): ModuleSelection {
  const nav = navigationIndex(project), chosen = new Set<string>();
  for (const node of project.nodes) {
    if (node.kind === 'canvas' || canvasId && node.canvasId !== canvasId) continue;
    const decision = [node, ...nav.ancestors(node.id)].find(item => Object.hasOwn(state.overrides, item.id));
    if (decision && state.overrides[decision.id]) chosen.add(node.id);
  }
  return {
    nodeIds: project.nodes.filter(node => chosen.has(node.id)).map(node => node.id),
    linkIds: project.links.filter(link => chosen.has(link.sourceId) && chosen.has(link.targetId)).map(link => link.id),
  };
}

/** Toggle exactly this branch choice; retain explicit descendant exclusions for later parent clicks. */
export function toggleModuleSelection(project: MeridianProject, state: ModuleSelectionState, nodeId: string): ModuleSelectionState {
  const node = project.nodes.find(item => item.id === nodeId);
  if (!node || node.kind === 'canvas') throw new Error('Choose a node inside the Work for the module selection.');
  const selected = new Set(resolveModuleSelection(project, state, node.canvasId).nodeIds);
  return { overrides: { ...state.overrides, [nodeId]: !selected.has(nodeId) } };
}
