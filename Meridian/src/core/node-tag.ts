import type { MeridianNode, MeridianProject } from './types.ts';

export const NODE_TAG_MAX_LENGTH = 8000;
export function isNodeTag(value: unknown): value is string {
  return typeof value === 'string' && value.length <= NODE_TAG_MAX_LENGTH;
}

/** Identity metadata never substitutes for visible canvas content. */
export function nodeTag(node: Pick<MeridianNode, 'id' | 'kind' | 'title' | 'tag'>): string {
  const tag = typeof node.tag === 'string' ? node.tag.trim() : '';
  if (tag) return tag;
  const title = typeof node.title === 'string' ? node.title.trim() : '';
  if (title) return title.slice(0, NODE_TAG_MAX_LENGTH);
  const label = node.kind === 'canvas-link' || node.kind === 'canvas' ? 'Work' : node.kind === 'joint' ? 'Operation' : node.kind === 'junction' ? 'Utility' : node.kind.charAt(0).toUpperCase() + node.kind.slice(1);
  return `${label} ${node.id.slice(0, 8)}`;
}

/** Fill legacy identity once; subsequent title edits never rename its stable tag. */
export function ensureNodeTags(project: MeridianProject): MeridianProject {
  const missing = (node: MeridianNode) => node.tag === undefined || typeof node.tag === 'string' && !node.tag.trim();
  if (!project.nodes.some(missing) && !Object.values(project.snapshots).some(snapshot => missing(snapshot.node))) return project;
  const result = structuredClone(project);
  for (const node of [...result.nodes, ...Object.values(result.snapshots).map(snapshot => snapshot.node)]) if (missing(node)) node.tag = nodeTag(node);
  return result;
}
