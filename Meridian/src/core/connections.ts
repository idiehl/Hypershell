import type { MeridianLink, MeridianNode, MeridianProject, NodeStyle, Point, UtilityVariant } from './types.ts';
import { valueTypeForLink } from './operations.ts';
import { validateChange } from './rules.ts';
import { navigationIndex } from './navigation.ts';
import { createLinkFromSpec, type LinkCreationSpec } from './creation.ts';

export interface JunctionOptions { parentId?: string; title?: string; segmentIndex?: number; utilityVariant?: UtilityVariant; width?: number; height?: number; style?: NodeStyle }
export interface JunctionInsertion { project: MeridianProject; nodeId: string; sourceLinkId: string; targetLinkId: string }
export interface LinkPathInsertion { project: MeridianProject; linkId: string; nodeIds: string[]; linkIds: string[] }

function utilityNode(canvasId: string, parentId: string, position: Point, options: JunctionOptions, valueType?: MeridianNode['valueType']): MeridianNode {
  const utilityVariant = options.utilityVariant ?? 'joint';
  if (!['pivot', 'joint'].includes(utilityVariant)) throw new Error('Choose Pivot or Joint for the routing utility.');
  const width = options.width ?? 12, height = options.height ?? 12;
  if (![position.x, position.y, width, height].every(Number.isFinite) || width <= 0 || height <= 0) throw new Error('Utility dimensions and position must be finite and positive.');
  return {
    id: crypto.randomUUID(), canvasId, parentId, kind: 'junction', utilityVariant,
    title: options.title ?? (utilityVariant === 'pivot' ? 'Pivot' : 'Joint'),
    tag: options.title?.trim() || (utilityVariant === 'pivot' ? 'Pivot' : 'Joint'),
    x: position.x - width / 2, y: position.y - height / 2, width, height, sizeMode: 'fixed',
    childLayout: 'spatial', placement: 'free', snapping: { stickyEdges: false, snapToGrid: false, smartAlign: false },
    style: { shape: 'circle', fill: '#78ddc1', ...structuredClone(options.style ?? {}) }, ...(valueType ? { valueType } : {}),
  };
}
function checked(before: MeridianProject, candidate: MeridianProject): void {
  const issues = validateChange(before, candidate);
  if (issues.length) throw new Error([...new Set(issues.map(issue => issue.message))].join(' '));
}

/** Commit a complete route at once; cancelled drafts never leave orphan utilities. */
export function createLinkPath(project: MeridianProject, spec: LinkCreationSpec, pivotPoints: readonly Point[]): LinkPathInsertion {
  if (!Array.isArray(pivotPoints) || pivotPoints.length > 1000 || pivotPoints.some(point => !point || !Number.isFinite(point.x) || !Number.isFinite(point.y)))
    throw new Error('Use at most 1000 finite pivot positions.');
  // Validate the requested direct connection first, including type, port and owner rules.
  const direct = createLinkFromSpec(project, spec);
  if (!pivotPoints.length) return { ...direct, nodeIds: [], linkIds: [direct.linkId] };
  const candidate = direct.project, terminal = candidate.links.find(link => link.id === direct.linkId)!;
  const owner = navigationIndex(candidate).linkParent(terminal);
  if (!owner || owner.canvasId !== spec.canvasId) throw new Error('The link needs a parent in its own Work.');
  const valueType = valueTypeForLink(spec.kind), nodeIds: string[] = [], linkIds: string[] = [];
  let sourceId = spec.sourceId;
  for (const position of pivotPoints) {
    const node = utilityNode(spec.canvasId, owner.id, position, { utilityVariant: 'pivot', style: { fill: terminal.style?.color ?? '#78ddc1' } }, valueType);
    candidate.nodes.push(node); nodeIds.push(node.id);
    const segment: MeridianLink = {
      id: crypto.randomUUID(), canvasId: terminal.canvasId, parentId: owner.id, sourceId, targetId: node.id,
      kind: terminal.kind, relation: terminal.relation, ...(terminal.direction ? { direction: terminal.direction } : {}),
      ...(terminal.style ? { style: structuredClone(terminal.style) } : {}), ...(valueType ? { targetPort: 0 } : {}),
      ...(sourceId === spec.sourceId && terminal.sourceAnchor ? { sourceAnchor: { ...terminal.sourceAnchor } } : {}),
    };
    candidate.links.push(segment); linkIds.push(segment.id); sourceId = node.id;
  }
  terminal.sourceId = sourceId; terminal.parentId = owner.id; delete terminal.sourceAnchor;
  linkIds.push(terminal.id); checked(project, candidate);
  return { project: candidate, linkId: terminal.id, nodeIds, linkIds };
}

function distanceToSegment(point: Point, a: Point, b: Point): number {
  const dx = b.x - a.x, dy = b.y - a.y, length = dx * dx + dy * dy;
  const t = length ? Math.max(0, Math.min(1, ((point.x - a.x) * dx + (point.y - a.y) * dy) / length)) : 0;
  return Math.hypot(point.x - a.x - t * dx, point.y - a.y - t * dy);
}
function endpoint(node: MeridianNode, anchor?: Point): Point {
  return { x: node.x + node.width * (anchor?.x ?? .5), y: node.y + node.height * (anchor?.y ?? .5) };
}

/** Explicit insertion only. Crossed routes are never connected by geometry alone. */
export function splitLinkWithJunction(project: MeridianProject, linkId: string, position: Point, options: JunctionOptions = {}): JunctionInsertion {
  if (!Number.isFinite(position.x) || !Number.isFinite(position.y)) throw new Error('Junction position must be finite.');
  const candidate = structuredClone(project), link = candidate.links.find(item => item.id === linkId);
  if (!link) throw new Error('Choose an existing connection to insert a junction.');
  if (link.snap) throw new Error('Snapped edges have no line to split. Create a separate routed link.');
  const nodes = new Map(candidate.nodes.map(node => [node.id, node]));
  const source = nodes.get(link.sourceId), target = nodes.get(link.targetId);
  if (!source || !target) throw new Error('Connection endpoints are unavailable.');
  const owner = navigationIndex(candidate).linkParent(link);
  const parentId = options.parentId ?? owner?.id;
  const parent = parentId && nodes.get(parentId);
  if (!parent || parent.canvasId !== link.canvasId) throw new Error('The junction parent must belong to the connection Work.');
  const sourceLinkId = crypto.randomUUID();
  const valueType = valueTypeForLink(link.kind);
  const node = utilityNode(link.canvasId, parent.id, position, { ...options, style: { fill: link.style?.color ?? '#78ddc1', ...options.style } }, valueType), nodeId = node.id;
  const bends = link.points ?? [];
  const polyline = [endpoint(source, link.sourceAnchor), ...bends, endpoint(target, link.targetAnchor)];
  let segment = options.segmentIndex;
  if (segment === undefined) {
    segment = 0;
    let distance = Infinity;
    for (let index = 0; index < polyline.length - 1; index++) {
      const current = distanceToSegment(position, polyline[index]!, polyline[index + 1]!);
      if (current < distance) { segment = index; distance = current; }
    }
  }
  if (!Number.isInteger(segment) || segment < 0 || segment > bends.length) throw new Error('Connection segment is invalid.');
  const inbound: MeridianLink = {
    id: sourceLinkId, canvasId: link.canvasId, sourceId: link.sourceId, targetId: nodeId,
    ...(owner ? { parentId: owner.id } : {}),
    kind: link.kind, relation: link.relation,
    ...(link.direction ? { direction: link.direction } : {}),
    ...(link.sourceAnchor ? { sourceAnchor: { ...link.sourceAnchor } } : {}),
    ...(link.style ? { style: { ...link.style } } : {}),
    ...(valueType ? { targetPort: 0 } : {}), points: bends.slice(0, segment),
  };
  link.sourceId = nodeId; delete link.sourceAnchor; link.points = bends.slice(segment);
  if (owner) link.parentId = owner.id;
  candidate.nodes.push(node); candidate.links.push(inbound);
  checked(project, candidate);
  return { project: candidate, nodeId, sourceLinkId, targetLinkId: link.id };
}
