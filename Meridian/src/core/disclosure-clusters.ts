import type { DisclosureCluster, MeridianNode, MeridianProject } from './types.ts';
import { lockedClusterNodeIds } from './clusters.ts';
import { groupSelection, selectionGraph } from './selection.ts';
import { navigationIndex } from './navigation.ts';
import { boundedChildInterior, nodeBorderWidth, nodeContentLayout, nodeSpacing } from './sizing.ts';
import { isDisclosureClusterSettings, validateChange } from './rules.ts';
import { isNodeTag, NODE_TAG_MAX_LENGTH } from './node-tag.ts';
import { isEnclosingContainer, isOrderedContainer } from './cells.ts';
import { rememberContainerSize } from './container-size.ts';
import { shapeAspectRatio } from './geometry.ts';

export const DISCLOSURE_CLUSTER_BUTTON_SIZE = 64;
export interface DisclosureClusterOptions { name: string; buttonShape?: DisclosureCluster['buttonShape'] }

export function isDisclosureCluster(node: Pick<MeridianNode, 'kind' | 'cluster'>): boolean {
  return node.kind === 'container' && node.cluster !== undefined;
}

/** Creation may add a frame around an existing tight composition. Grow only its
 * Freeform ancestor frames; every member, unrelated sibling and wire stays put. */
function encloseWithoutReflow(project: MeridianProject, clusterId: string): void {
  const ancestors = navigationIndex(project).ancestors(clusterId);
  for (const ancestor of ancestors) {
    if (ancestor.kind === 'canvas' || !isEnclosingContainer(ancestor)) continue;
    const parent = project.nodes.find(node => node.id === ancestor.id)!;
    const nav = navigationIndex(project), boxes = (nav.children.get(parent.id) ?? []).map(child => nav.branchBounds(child));
    if (!boxes.length) continue;
    const extent = { left: Math.min(...boxes.map(box => box.x)), top: Math.min(...boxes.map(box => box.y)),
      right: Math.max(...boxes.map(box => box.x + box.width)), bottom: Math.max(...boxes.map(box => box.y + box.height)) };
    let changed = false, fits = false;
    for (let attempt = 0; attempt < 80; attempt++) {
      const inner = boundedChildInterior(project, parent, {}, parent);
      const left = Math.max(0, inner.x - extent.left), top = Math.max(0, inner.y - extent.top),
        right = Math.max(0, extent.right - inner.x - inner.width), bottom = Math.max(0, extent.bottom - inner.y - inner.height);
      if (Math.max(left, top, right, bottom) <= .00001) { fits = true; break; }
      if (parent.positionLocked && (left > .00001 || top > .00001))
        throw new Error('Unlock the parent container position before adding a Cluster that needs more room above or to its left.');
      // Overshoot by a small tolerance to converge for shaped silhouettes and
      // contracted padding without fitting or translating their contents.
      parent.x -= left; parent.y -= top;
      parent.width += (left + right) * 1.1 + .00002;
      parent.height += (top + bottom) * 1.1 + .00002;
      const ratio = shapeAspectRatio(parent.style?.shape);
      if (ratio !== undefined) { parent.height = Math.max(parent.height, parent.width / ratio); parent.width = parent.height * ratio; }
      changed = true;
      if (![parent.x, parent.y, parent.width, parent.height].every(Number.isFinite) || Math.max(parent.width, parent.height) > 1e9) break;
    }
    if (!fits) throw new Error('The parent container cannot safely enclose this Cluster without changing its composition.');
    if (changed) { parent.sizeMode = 'fixed'; rememberContainerSize(parent); }
  }
}

/**
 * Wrap sibling branches without cloning their identities or changing their own
 * disclosure states. Include an entire edge-locked assembly so an existing lock
 * is never severed by hiding just one of its endpoints.
 */
export function createDisclosureCluster(project: MeridianProject, selectedIds: readonly string[], options: DisclosureClusterOptions): { project: MeridianProject; rootId: string } {
  if (!options || typeof options.name !== 'string' || !options.name.trim() || !isNodeTag(options.name.trim()))
    throw new Error(`Give the Cluster a nonempty name of at most ${NODE_TAG_MAX_LENGTH} characters.`);
  const cluster = { buttonShape: options.buttonShape ?? 'square' };
  if (!isDisclosureClusterSettings(cluster)) throw new Error('Choose a square or circle Cluster button.');
  const requested = selectionGraph(project, selectedIds);
  const selection = selectionGraph(project, lockedClusterNodeIds(project, requested.canvasId, requested.nodeIds));
  const nav = navigationIndex(project), roots = selection.rootIds.map(id => nav.nodes.get(id)!);
  if (roots.some(node => node.parentId !== roots[0].parentId))
    throw new Error('Group nodes with the same parent. Move branches into one parent explicitly first.');
  for (const parent of nav.ancestors(roots[0].id)) {
    if (parent.kind === 'canvas') continue;
    if (parent.collapsed) throw new Error('Expand the parent branch before creating a Cluster within it.');
    if (isOrderedContainer(parent) || parent.containerVariant === 'gallery'
      || !['contained', 'spatial'].includes(parent.containerVariant ?? 'contained')
      || parent.childArrangement !== undefined && parent.childArrangement !== 'free')
      throw new Error('Create Clusters on the Work or inside Freeform parents. Ordered parents control their child allocation.');
  }
  const boxes = roots.map(root => nav.branchBounds(root));
  const left = Math.min(...boxes.map(box => box.x)), top = Math.min(...boxes.map(box => box.y));
  // Reuse the existing grouping transaction for inherited constraints and parent
  // ownership. Any first-pass layout it performs is discarded below: a Cluster
  // records the exact authored composition rather than a freshly packed group.
  const prepared = structuredClone(project);
  for (const node of prepared.nodes) if (selection.nodeIds.includes(node.id)) {
    node.childPosition = 'manual'; node.positionLocked = false;
  }
  const grouped = groupSelection(prepared, selection.rootIds, { title: options.name.trim(), x: left - 12, y: top - 210 });
  const candidate = grouped.project, root = candidate.nodes.find(node => node.id === grouped.rootId)!;
  const rootIds = new Set(selection.rootIds);
  candidate.nodes = candidate.nodes.map(node => {
    const original = nav.nodes.get(node.id);
    if (!original) return node;
    return { ...structuredClone(original), ...(rootIds.has(node.id) ? { parentId: root.id, placement: 'free' as const, childPosition: 'manual' as const } : {}) };
  });
  candidate.links = structuredClone(project.links);
  root.cluster = cluster;
  root.style = { shape: 'rectangle', radius: 0, padding: 8, childPadding: 8 };
  root.sizeMode = 'fixed';
  const spacing = nodeSpacing(root.style), inset = spacing.childPadding + nodeBorderWidth(root);
  const width = Math.max(DISCLOSURE_CLUSTER_BUTTON_SIZE, Math.max(...boxes.map(box => box.x + box.width)) - left + 2 * inset);
  root.width = width;
  const header = nodeContentLayout(candidate, root, {}, false, width - 2 * (spacing.padding + nodeBorderWidth(root))).headerHeight;
  Object.assign(root, { x: left - inset, y: top - inset - header,
    width, height: Math.max(DISCLOSURE_CLUSTER_BUTTON_SIZE, Math.max(...boxes.map(box => box.y + box.height)) - top + 2 * inset + header),
    collapsed: false, containerVariant: 'contained', childLayout: 'contained', childArrangement: 'free' });
  root.containerSizes = {
    expanded: { width: root.width, height: root.height, sizeMode: 'fixed' },
    collapsed: { width: DISCLOSURE_CLUSTER_BUTTON_SIZE, height: DISCLOSURE_CLUSTER_BUTTON_SIZE, sizeMode: 'fixed' },
  };
  encloseWithoutReflow(candidate, root.id);
  const issues = validateChange(project, candidate);
  if (issues.length) throw new Error(issues.map(issue => issue.message).join(' '));
  return { project: candidate, rootId: root.id };
}

/** Change just the launcher silhouette; the open composition and both frames remain intact. */
export function setDisclosureClusterShape(project: MeridianProject, nodeId: string, buttonShape: DisclosureCluster['buttonShape']): MeridianProject {
  if (!isDisclosureClusterSettings({ buttonShape })) throw new Error('Choose a square or circle Cluster button.');
  const node = project.nodes.find(node => node.id === nodeId);
  if (!node || !isDisclosureCluster(node)) throw new Error('Choose an existing Cluster.');
  if (node.cluster!.buttonShape === buttonShape) return project;
  const candidate = structuredClone(project);
  candidate.nodes.find(node => node.id === nodeId)!.cluster = { buttonShape };
  const issues = validateChange(project, candidate);
  if (issues.length) throw new Error(issues.map(issue => issue.message).join(' '));
  return candidate;
}
