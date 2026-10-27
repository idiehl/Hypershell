import type { MeridianProject } from './types.ts';
import { lockedClusterNodeIds } from './clusters.ts';

/** A rigid movement also owns hidden descendants and locked snap peers. A lock
 * on any of them anchors the complete movement without making other children
 * independently immovable. Unknown/canvas roots cannot be moved by this API. */
export function canMoveNodeBranches(project: MeridianProject, ids: readonly string[]): boolean {
  const nodes = new Map(project.nodes.map(node => [node.id, node])), seeds = new Map<string, string[]>();
  for (const id of ids) {
    const node = nodes.get(id); if (!node || node.kind === 'canvas') return false;
    const canvasSeeds = seeds.get(node.canvasId) ?? []; canvasSeeds.push(id); seeds.set(node.canvasId, canvasSeeds);
  }
  for (const [canvasId, seedIds] of seeds) for (const id of lockedClusterNodeIds(project, canvasId, seedIds))
    if (nodes.get(id)?.positionLocked) return false;
  return true;
}

/** Locks pin existing coordinates, so automatic Freeform/sticky placement must
 * not move the tile on its next disclosure. Unlocking retains that arrangement. */
export function setNodePositionLocked(project: MeridianProject, id: string, locked: boolean): MeridianProject {
  if (typeof locked !== 'boolean') throw new Error('Position lock must be Boolean.');
  const original = project.nodes.find(node => node.id === id);
  if (!original || original.kind === 'canvas') throw new Error('Choose a non-root node to lock its position.');
  if (!!original.positionLocked === locked) return project;
  const candidate = structuredClone(project), node = candidate.nodes.find(node => node.id === id)!;
  if (locked) { node.positionLocked = true; node.childPosition = 'manual'; node.placement = 'free'; }
  else delete node.positionLocked;
  return candidate;
}
