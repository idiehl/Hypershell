import type { MeridianNode, MeridianProject } from './types.ts';

/** Older files can contain Blocks. Keep their blank surfaces and recoverable
 * dormant text while retiring the separate type from the editable graph. */
export function normalizeRetiredBlocks(project: MeridianProject): MeridianProject {
  const nodes = [...project.nodes, ...Object.values(project.snapshots).map(snapshot => snapshot.node)];
  if (!nodes.some(node => node.kind === 'block' || node.rules?.nodeKinds?.includes('block'))) return project;
  const result = structuredClone(project);
  const migrate = (node: MeridianNode) => {
    if (node.rules?.nodeKinds?.includes('block')) node.rules.nodeKinds = [...new Set(node.rules.nodeKinds.map(kind => kind === 'block' ? 'media' : kind))];
    if (node.kind !== 'block') return;
    if (node.title || node.text || node.titleRuns?.length || node.textRuns?.length) node.retiredBlockContent ??= {
      title: node.title,
      ...(node.text !== undefined ? { text: node.text } : {}),
      ...(node.titleRuns !== undefined ? { titleRuns: node.titleRuns } : {}),
      ...(node.textRuns !== undefined ? { textRuns: node.textRuns } : {}),
      ...(node.contentType !== undefined ? { contentType: node.contentType } : {}),
    };
    node.kind = 'media'; node.title = ''; node.text = ''; node.contentType = 'text';
    delete node.titleRuns; delete node.textRuns;
  };
  for (const node of result.nodes) migrate(node);
  for (const snapshot of Object.values(result.snapshots)) migrate(snapshot.node);
  return result;
}
