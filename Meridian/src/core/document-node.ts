import type { ContainerSize, MeridianAsset, MeridianNode, MeridianProject } from './types.ts';
import { activeNodeContent } from './content.ts';
import { documentPreviewKind } from './document-preview.ts';
import { navigationIndex } from './navigation.ts';
import { constrainChildGeometry, nodeSizingMode, containingFixedInterior } from './sizing.ts';
import { revealChildren } from './reveal.ts';

/** A reading surface is presentation of an original attachment, never a copied node. */
export function documentNodeAsset(project: MeridianProject, node: MeridianNode): MeridianAsset | undefined {
  if (!['document', 'media', 'text'].includes(node.kind)) return;
  const content = activeNodeContent(node);
  if (content.type !== 'asset' || !content.assetId) return;
  const asset = project.assets.find(asset => asset.id === content.assetId)
    ?? Object.values(project.snapshots).flatMap(snapshot => [snapshot.asset, ...snapshot.assets ?? []]).find(asset => asset?.id === content.assetId);
  return asset && documentPreviewKind(asset) ? asset : undefined;
}
/** Legacy documents may omit collapsed; do not open thousands of old previews on load. */
export function documentNodeExpanded(node: MeridianNode): boolean {
  return node.collapsed !== true && node.containerSizes?.expanded !== undefined;
}
export function rememberDocumentNodeSize(project: MeridianProject, node: MeridianNode, options: { authored?: boolean } = {}): void {
  if (!documentNodeAsset(project, node) || !node.containerSizes?.expanded) return;
  const mode = documentNodeExpanded(node) ? 'expanded' : 'collapsed';
  // Ordered allocation is display geometry, not a replacement for the user's reading frame.
  if (!options.authored && containingFixedInterior(project, node) && node.containerSizes[mode]) return;
  node.containerSizes = { ...node.containerSizes, [mode]: { width: node.width, height: node.height, sizeMode: node.sizeMode ?? nodeSizingMode(node) } };
}

/** Uses existing validated open/closed frame records; no new storage or asset conversion. */
export function revealDocumentNode(project: MeridianProject, id: string, expanded: boolean): MeridianProject {
  const original = project.nodes.find(node => node.id === id);
  if (!original || !documentNodeAsset(project, original)) return revealChildren(project, id, expanded);
  if (documentNodeExpanded(original) === expanded && original.containerSizes?.expanded) return structuredClone(project);
  const candidate = structuredClone(project), node = candidate.nodes.find(node => node.id === id)!;
  const current = navigationIndex(project).bounds(original);
  const currentSize: ContainerSize = { width: current.width, height: current.height, sizeMode: original.sizeMode ?? nodeSizingMode(original) };
  const allocated = !!containingFixedInterior(candidate, node), currentMode = documentNodeExpanded(original) ? 'expanded' : 'collapsed';
  const records = { ...node.containerSizes };
  if (!allocated || !records[currentMode]) records[currentMode] = currentSize;
  const seed: ContainerSize = expanded ? { width: Math.min(1e9, current.width * 2.2),
    height: Math.min(1e9, Math.max(current.height * 2.8, current.width * 1.45)), sizeMode: 'fixed' }
    : records.collapsed ?? currentSize;
  const next = records[expanded ? 'expanded' : 'collapsed'] ?? seed;
  const bounded = constrainChildGeometry(candidate, node, { x: node.x, y: node.y, width: next.width, height: next.height });
  node.width = bounded.width; node.height = bounded.height; node.sizeMode = expanded ? 'fixed' : next.sizeMode;
  node.collapsed = !expanded;
  node.containerSizes = { ...records, [expanded ? 'expanded' : 'collapsed']: allocated ? { ...next } : { width: node.width, height: node.height, sizeMode: node.sizeMode } };
  return revealChildren(candidate, id, expanded);
}
