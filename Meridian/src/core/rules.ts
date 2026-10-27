import type { MeridianNode as Node, MeridianProject as Project, NodeHyperlink, NodeKind, RemoteLink, Rules, ValidationIssue } from './types.ts';
import { isBackgroundAsset, isCanvasStyle, isLinkLabel, isLinkStyle, isNodeStyle } from './appearance.ts';
import { inputPorts, isComputedLink, OPERATION_DEFINITIONS, operationDefinition, outputValueType, valueTypeForLink } from './operations.ts';
import { CHILD_ARRANGEMENTS, cellAddress, isCellLocation, isCellReference, isGridSettings, resolveReferenceTarget } from './cells.ts';
import { isSnapSettings, reconcileSnapLinks, snapEligibility } from './clusters.ts';
import { isTextRuns } from './rich-text.ts';
import { isNodeTag } from './node-tag.ts';
import { isContainerSizes } from './container-size.ts';
import { isRetiredBlockContent } from './retired-content.ts';
import { isExpansionTray } from './expansion-tray-state.ts';
import { activeNodeContent, isNodeContentItems } from './content.ts';
import { isSmartLinkSettings } from './smart-link-state.ts';

const kinds: NodeKind[] = ['canvas', 'container', 'spreadsheet', 'module', 'media', 'block', 'text', 'image', 'document', 'input', 'number', 'switch', 'output', 'joint', 'junction', 'reference', 'canvas-link'];
const ruleKeys = ['nodeKinds', 'linkKinds', 'operations'] as const;
const allowedRules: Record<keyof Rules, readonly string[]> = { nodeKinds: kinds, linkKinds: ['visual', 'functional', 'numeric'], operations: OPERATION_DEFINITIONS.map(item => item.operation) };

export function isNodeIdentifier(value: unknown): value is string {
  return typeof value === 'string' && /^[A-Za-z0-9][A-Za-z0-9_-]{0,95}$/.test(value);
}

export function isRemoteLinks(value: unknown): value is RemoteLink[] {
  if (!Array.isArray(value) || value.length > 1000) return false;
  const ids = new Set<string>(), targets = new Set<string>();
  return value.every(item => {
    if (!item || typeof item !== 'object' || Array.isArray(item)
      || Object.keys(item).some(key => !['id', 'targetId', 'relation', 'label'].includes(key))
      || !isNodeIdentifier(item.id) || !isNodeIdentifier(item.targetId)
      || !['causal', 'chronological', 'mutual', 'cyclical', 'generic'].includes(item.relation)
      || !isLinkLabel(item.label) || ids.has(item.id) || targets.has(`${item.targetId}:${item.relation}`)) return false;
    ids.add(item.id); targets.add(`${item.targetId}:${item.relation}`); return true;
  });
}

export function isNodeHyperlinks(value: unknown): value is NodeHyperlink[] {
  if (!Array.isArray(value) || value.length > 256) return false;
  const seen = new Set<string>();
  return value.every(item => {
    if (!item || typeof item !== 'object' || Array.isArray(item) || Object.keys(item).some(key => !['url', 'label'].includes(key))
      || typeof item.url !== 'string' || item.url.length > 2048 || item.url !== item.url.trim() || /[\u0000-\u0020\u007f]/.test(item.url)
      || !/^(?:https?:\/\/|mailto:)/i.test(item.url)
      || !isLinkLabel(item.label) || seen.has(item.url)) return false;
    try {
      const url = new URL(item.url);
      if (!['http:', 'https:', 'mailto:'].includes(url.protocol) || (url.protocol === 'mailto:' ? !url.pathname : !url.hostname)) return false;
    } catch { return false; }
    seen.add(item.url); return true;
  });
}

export function isCanvasTarget(value: unknown): value is NonNullable<Node['canvasTarget']> {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false;
  const target = value as Record<string, unknown>;
  const identifier = (item: unknown) => typeof item === 'string' && item.trim() === item && /^[A-Za-z0-9][A-Za-z0-9_-]{0,95}$/.test(item);
  return Object.keys(target).every(key => key === 'projectId' || key === 'canvasId') && identifier(target.projectId)
    && (target.canvasId === undefined || identifier(target.canvasId));
}

export function isDisclosureClusterSettings(value: unknown): value is NonNullable<Node['cluster']> {
  return !!value && typeof value === 'object' && !Array.isArray(value)
    && Object.keys(value).length === 1 && Object.hasOwn(value, 'buttonShape')
    && ['square', 'circle'].includes((value as { buttonShape: string }).buttonShape);
}

/** Structural guard for untrusted project/publication input, separate from editable graph validity. */
export function isProjectShape(value: unknown): value is Project {
  const record = (item: unknown): item is Record<string, unknown> => !!item && typeof item === 'object' && !Array.isArray(item);
  const strings = (item: Record<string, unknown>, keys: string[]): boolean => keys.every(key => typeof item[key] === 'string');
  const optional = (item: Record<string, unknown>, key: string, type: string): boolean => item[key] === undefined || typeof item[key] === type;
  const point = (item: unknown): boolean => record(item) && typeof item.x === 'number' && Number.isFinite(item.x) && typeof item.y === 'number' && Number.isFinite(item.y);
  const childOffset = (item: unknown): boolean => record(item) && Object.keys(item).every(key => key === 'x' || key === 'y') && ['x', 'y'].every(key => typeof item[key] === 'number' && Number.isFinite(item[key]) && Math.abs(item[key] as number) <= 1e9);
  const asset = (item: unknown): boolean => record(item) && strings(item, ['id', 'name', 'mime', 'base64']);
  const snapping = (item: unknown): boolean => item === undefined || record(item) && ['stickyEdges', 'snapToGrid', 'smartAlign'].every(key => optional(item, key, 'boolean'));
  const node = (item: unknown): boolean => {
    if (!record(item) || !strings(item, ['id', 'canvasId', 'kind', 'title']) || !(item.parentId === null || typeof item.parentId === 'string')) return false;
    if (item.tag !== undefined && !isNodeTag(item.tag)) return false;
    if (!['x', 'y', 'width', 'height'].every(key => typeof item[key] === 'number' && Number.isFinite(item[key]))) return false;
    if (item.sizeMode !== undefined && !['auto', 'fixed'].includes(item.sizeMode as string)) return false;
    if (item.containerSizes !== undefined && !isContainerSizes(item.containerSizes)) return false;
    if (item.cluster !== undefined && (!isDisclosureClusterSettings(item.cluster) || item.kind !== 'container'
      || item.containerVariant !== undefined && !['contained', 'spatial'].includes(item.containerVariant as string)
      || item.childLayout !== undefined && item.childLayout !== 'contained'
      || item.childArrangement !== undefined && item.childArrangement !== 'free' || item.grid !== undefined)) return false;
    if (item.smartLink !== undefined && (!isSmartLinkSettings(item.smartLink) || item.kind !== 'junction'
      || item.utilityVariant !== undefined && item.utilityVariant !== 'joint'
      || item.childLayout !== undefined && item.childLayout !== 'spatial'
      || item.childArrangement !== undefined && item.childArrangement !== 'free'
      || item.containerVariant !== undefined || item.grid !== undefined || item.valueType !== undefined)) return false;
    if (item.retiredBlockContent !== undefined && !isRetiredBlockContent(item.retiredBlockContent)) return false;
    if (item.contentType !== undefined && !['text', 'asset', 'link'].includes(item.contentType as string)) return false;
    if (item.childPosition !== undefined && !['auto', 'manual'].includes(item.childPosition as string)) return false;
    if (item.childOffset !== undefined && !childOffset(item.childOffset)) return false;
    if (item.expansionTray !== undefined && !isExpansionTray(item.expansionTray)) return false;
    if (!['text', 'assetId', 'operation', 'targetId', 'valueType'].every(key => optional(item, key, 'string'))) return false;
    if (item.titleRuns !== undefined && !isTextRuns(item.title as string, item.titleRuns)
      || item.textRuns !== undefined && !isTextRuns(typeof item.text === 'string' ? item.text : '', item.textRuns)) return false;
    if (item.canvasTarget !== undefined && (item.kind !== 'canvas-link' || item.targetId !== undefined || !isCanvasTarget(item.canvasTarget))) return false;
    if (item.containerVariant !== undefined && (item.kind === 'canvas' || !['spatial', 'contained', 'horizontal', 'vertical', 'grid', 'spreadsheet', 'gallery', 'window'].includes(item.containerVariant as string))) return false;
    if (item.utilityVariant !== undefined && (item.kind !== 'junction' || !['pivot', 'joint'].includes(item.utilityVariant as string))) return false;
    if (!['collapsed', 'closed', 'exposed', 'positionLocked', 'showTitle'].every(key => optional(item, key, 'boolean'))) return false;
    if (item.contentItems !== undefined && !isNodeContentItems(item.contentItems)) return false;
    if (item.value !== undefined && typeof item.value !== 'boolean' && !(typeof item.value === 'number' && Number.isFinite(item.value))) return false;
    if (item.valueType !== undefined && item.valueType !== 'boolean' && item.valueType !== 'number') return false;
    if (item.childLayout !== undefined && !['contained', 'spatial'].includes(item.childLayout as string)) return false;
    if ((item.kind === 'media' || item.kind === 'block') && item.childLayout === 'contained') return false;
    if (item.placement !== undefined && !['free', 'near', 'edge'].includes(item.placement as string)) return false;
    if (item.attachSide !== undefined && !['auto', 'top', 'right', 'bottom', 'left'].includes(item.attachSide as string)) return false;
    if (item.childArrangement !== undefined && !CHILD_ARRANGEMENTS.includes(item.childArrangement as never)) return false;
    if (item.grid !== undefined && !isGridSettings(item.grid)) return false;
    if (item.containerVariant === 'window' && (!record(item.grid) || item.grid.windowColumns === undefined || item.grid.windowRows === undefined)) return false;
    if (item.cell !== undefined && !isCellLocation(item.cell)) return false;
    if (item.targetCell !== undefined && !isCellReference(item.targetCell)) return false;
    if (!snapping(item.snapping) || !isNodeStyle(item.style)) return false;
    if (item.remoteLinks !== undefined && !isRemoteLinks(item.remoteLinks)) return false;
    if (item.attachments !== undefined && (!Array.isArray(item.attachments) || item.attachments.length > 256
      || !item.attachments.every(isNodeIdentifier) || new Set(item.attachments).size !== item.attachments.length)) return false;
    if (item.hyperlinks !== undefined && !isNodeHyperlinks(item.hyperlinks)) return false;
    if (item.dependencyIds !== undefined && (!Array.isArray(item.dependencyIds) || item.dependencyIds.length > 1000 ||
      !item.dependencyIds.every(id => typeof id === 'string' && /^[A-Za-z0-9][A-Za-z0-9_-]{0,95}$/.test(id)) ||
      new Set(item.dependencyIds).size !== item.dependencyIds.length || item.dependencyIds.includes(item.id))) return false;
    if (item.rules !== undefined) {
      if (!record(item.rules)) return false;
      for (const key of ruleKeys) {
        const list = item.rules[key];
        if (list !== undefined && (!Array.isArray(list) || !list.every(entry => typeof entry === 'string'))) return false;
      }
    }
    return true;
  };
  if (!record(value) || value.schemaVersion !== 1 || !strings(value, ['id', 'title', 'entryCanvasId']) || !Number.isInteger(value.revision)) return false;
  if (!Array.isArray(value.canvases) || !value.canvases.every(item => record(item) && strings(item, ['id', 'rootId', 'title']) && snapping(item.snapping) && isCanvasStyle(item.style))) return false;
  if (!Array.isArray(value.nodes) || !value.nodes.every(node) || !Array.isArray(value.assets) || !value.assets.every(asset)) return false;
  if (!Array.isArray(value.links) || !value.links.every(item => {
    if (!record(item) || !strings(item, ['id', 'canvasId', 'sourceId', 'targetId', 'kind', 'relation'])) return false;
    if (!isLinkStyle(item.style) || !isLinkLabel(item.label)) return false;
    if (item.snap !== undefined && !isSnapSettings(item.snap)) return false;
    if (!optional(item, 'parentId', 'string')) return false;
    if (item.direction !== undefined && !['forward', 'reverse', 'both', 'none'].includes(item.direction as string)) return false;
    if (!optional(item, 'targetPort', 'number')) return false;
    if (item.sourceAnchor !== undefined && !point(item.sourceAnchor) || item.targetAnchor !== undefined && !point(item.targetAnchor)) return false;
    return item.points === undefined || Array.isArray(item.points) && item.points.every(point);
  })) return false;
  if (!record(value.snapshots) || !Object.values(value.snapshots).every(item => record(item) && node(item.node) && (item.value === null || typeof item.value === 'boolean' || typeof item.value === 'number' && Number.isFinite(item.value)) && typeof item.savedAt === 'string' && (item.asset === undefined || asset(item.asset)) && (item.backgroundAsset === undefined || asset(item.backgroundAsset)) && (item.assets === undefined || Array.isArray(item.assets) && item.assets.length <= 1000 && item.assets.every(asset) && new Set(item.assets.map(a => a.id)).size === item.assets.length))) return false;
  if (!Array.isArray(value.dependencies) || !value.dependencies.every(item => record(item) && strings(item, ['canvasId', 'title']) && typeof item.required === 'boolean')) return false;
  return true;
}

function inherited(node: Node, nodes: Map<string, Node>): Record<keyof Rules, Set<string> | undefined> {
  const result: Record<keyof Rules, Set<string> | undefined> = { nodeKinds: undefined, linkKinds: undefined, operations: undefined };
  let parent = node.parentId ? nodes.get(node.parentId) : undefined;
  const visited = new Set<string>([node.id]);
  while (parent && !visited.has(parent.id)) {
    visited.add(parent.id);
    for (const key of ruleKeys) {
      const values = parent.rules?.[key];
      if (Array.isArray(values)) {
        const old = result[key];
        result[key] = new Set(values.filter(value => !old || old.has(value)));
      }
    }
    parent = parent.parentId ? nodes.get(parent.parentId) : undefined;
  }
  return result;
}

export function validateProject(project: Project): ValidationIssue[] {
  if (!isProjectShape(project)) return [{ message: 'Project data is malformed or has an unsupported schema.' }];
  return validateProjectGraph(project);
}

/** The caller has already checked the complete shape, including snapshots. */
function validateProjectGraph(project: Project): ValidationIssue[] {
  const issues: ValidationIssue[] = [];
  const issue = (message: string, nodeId?: string, linkId?: string): void => { issues.push({ message, ...(nodeId ? { nodeId } : {}), ...(linkId ? { linkId } : {}) }); };
  if (project.schemaVersion !== 1) issue('Unsupported project schema.');
  if (!project.id || !Number.isInteger(project.revision) || project.revision < 0) issue('Project identity or revision is invalid.');
  const nodes = new Map(project.nodes.map(node => [node.id, node]));
  const canvases = new Map(project.canvases.map(canvas => [canvas.id, canvas]));
  const assets = new Map<string, Project['assets'][number]>();
  for (const asset of project.assets) if (!assets.has(asset.id)) assets.set(asset.id, asset);
  const inheritedRules = new Map<Node, ReturnType<typeof inherited>>();
  const limitsFor = (node: Node) => { let limits = inheritedRules.get(node); if (!limits) { limits = inherited(node, nodes); inheritedRules.set(node, limits); } return limits; };
  const ids = new Set<string>([project.id]);
  for (const entity of [...project.nodes, ...project.canvases, ...project.links, ...project.assets]) {
    if (!entity.id || ids.has(entity.id)) issue('Every included object must have a unique identity.', 'kind' in entity ? entity.id : undefined);
    ids.add(entity.id);
  }
  let remoteCount = 0;
  for (const node of project.nodes) for (const link of node.remoteLinks ?? []) {
    remoteCount++;
    if (ids.has(link.id)) issue('Every included object must have a unique identity.', node.id);
    ids.add(link.id);
  }
  if (remoteCount > 20000) issue('A document supports at most 20000 conceptual links.');
  if (!canvases.has(project.entryCanvasId)) issue('The entry Work does not exist.');
  const background = (assetId: string | undefined, pinned?: Project['assets'][number], ownerId?: string): void => {
    if (!assetId) return;
    const asset = pinned ?? assets.get(assetId);
    // Missing media can be repaired in the editor; present background assets must be inert rasters.
    if (asset && !isBackgroundAsset(asset)) issue('Background images must contain a supported local raster image.', ownerId);
  };
  for (const canvas of project.canvases) background(canvas.style?.backgroundAssetId, undefined, canvas.rootId);
  for (const node of project.nodes) background(node.style?.backgroundAssetId, undefined, node.id);
  for (const [key, snapshot] of Object.entries(project.snapshots)) {
    if (snapshot.node.id !== key) issue('Saved snapshot identity does not match its reference key.', key);
    if (snapshot.asset && snapshot.node.assetId !== snapshot.asset.id && activeNodeContent(snapshot.node).assetId !== snapshot.asset.id) issue('Saved snapshot asset does not match its node.', key);
    if (snapshot.backgroundAsset && snapshot.node.style?.backgroundAssetId !== snapshot.backgroundAsset.id) issue('Saved snapshot background asset does not match its node.', key);
    background(snapshot.node.style?.backgroundAssetId, snapshot.backgroundAsset, key);
    if (snapshot.node.targetCell !== undefined && (snapshot.node.kind !== 'reference' || !snapshot.node.targetId)) issue('Only references with a target can address a cell.', key);
  }
  for (const canvas of project.canvases) {
    const root = nodes.get(canvas.rootId);
    if (!root || root.kind !== 'canvas' || root.parentId !== null || root.canvasId !== canvas.id) issue('Work root is missing or invalid.', canvas.rootId);
  }
  const occupiedCells = new Set<string>(), namedCells = new Set<string>();
  for (const node of project.nodes) {
    const canvas = canvases.get(node.canvasId);
    if (!canvas) issue('Node belongs to a missing Work.', node.id);
    if (!kinds.includes(node.kind)) issue('Unknown node kind.', node.id);
    if (![node.x, node.y, node.width, node.height].every(Number.isFinite) || node.width <= 0 || node.height <= 0) issue('Node geometry must be finite with positive dimensions.', node.id);
    if (node.kind === 'canvas') {
      if (canvas?.rootId !== node.id || node.parentId !== null) issue('A Work has exactly one root.', node.id);
    } else {
      const parent = node.parentId ? nodes.get(node.parentId) : undefined;
      if (!parent || parent.canvasId !== node.canvasId) issue('A node needs one parent in its own Work.', node.id);
    }
    const ancestors = new Set<string>([node.id]);
    let cursor = node.parentId ? nodes.get(node.parentId) : undefined;
    while (cursor) {
      if (ancestors.has(cursor.id)) { issue('Containment cannot contain a cycle.', node.id); break; }
      ancestors.add(cursor.id); cursor = cursor.parentId ? nodes.get(cursor.parentId) : undefined;
    }
    const limits = limitsFor(node);
    if (limits.nodeKinds && !limits.nodeKinds.has(node.kind)) issue('Node kind is excluded by an inherited rule.', node.id);
    if (node.kind === 'joint') {
      if (!operationDefinition(node.operation, node.valueType)) issue('Choose a supported joint operation.', node.id);
      else if (node.operation && limits.operations && !limits.operations.has(node.operation)) issue('Joint operation is excluded by an inherited rule.', node.id);
    }
    if (node.kind === 'module' && node.operation !== undefined) issue('Module nodes group contents and do not perform operations.', node.id);
    if (node.kind === 'junction' && node.operation !== undefined) issue('Route junctions pass connections through and do not perform operations.', node.id);
    if (node.kind === 'input' && node.value !== undefined && typeof node.value !== 'boolean') issue('Boolean input values must be Boolean.', node.id);
    if (node.kind === 'number' && node.value !== undefined && (typeof node.value !== 'number' || !Number.isFinite(node.value))) issue('Number input values must be finite numbers.', node.id);
    if (node.kind === 'input' && node.valueType !== undefined && node.valueType !== 'boolean' || node.kind === 'number' && node.valueType !== undefined && node.valueType !== 'number') issue('Input value type cannot contradict its node kind.', node.id);
    if (node.closed !== undefined && typeof node.closed !== 'boolean') issue('Switch state must be Boolean.', node.id);
    if (node.rules) for (const key of ruleKeys) {
      const values = node.rules[key];
      if (values === undefined) continue;
      if (!Array.isArray(values) || values.some(value => !allowedRules[key].includes(value))) { issue('Rule contains an unsupported value.', node.id); continue; }
      const limit = limits[key];
      if (limit && values.some(value => !limit.has(value))) issue('A descendant rule cannot widen its inherited permissions.', node.id);
    }
    if (node.kind === 'canvas-link' && node.targetId) {
      const target = nodes.get(node.targetId);
      if (target && target.kind !== 'canvas') issue('A Work link must target a Work root.', node.id);
    }
    if (node.targetCell !== undefined && (node.kind !== 'reference' || !node.targetId)) issue('Only references with a target can address a cell.', node.id);
    if (node.cell) {
      const parent = node.parentId ? nodes.get(node.parentId) : undefined;
      if (!parent) issue('An addressed cell needs an immediate parent.', node.id);
      if (parent?.grid && (node.cell.column > parent.grid.columns || node.cell.row > parent.grid.rows)) issue('Cell coordinates must fit the parent grid.', node.id);
      const key = `${node.parentId}:${cellAddress(node.cell)}`;
      if (occupiedCells.has(key)) issue('Each cell coordinate can contain only one immediate child.', node.id);
      occupiedCells.add(key);
      if (node.cell.name) {
        const name = `${node.parentId}:${node.cell.name.toLocaleLowerCase()}`;
        if (namedCells.has(name)) issue('Cell names must be unique within their immediate parent.', node.id);
        namedCells.add(name);
      }
    }
  }
  const ports = new Set<string>();
  const snappedPairs = new Set<string>();
  const eligibleSnap = snapEligibility(project);
  const adjacency = new Map<string, string[]>();
  for (const node of project.nodes) adjacency.set(node.id, []);
  for (const link of project.links) {
    const source = nodes.get(link.sourceId), target = nodes.get(link.targetId);
    if (link.parentId !== undefined) {
      const parent = nodes.get(link.parentId);
      if (!parent || parent.canvasId !== link.canvasId) issue('A connection owner must be an existing node in its own Work.', undefined, link.id);
    }
    if (!source || !target) { issue('Link endpoint is missing.', undefined, link.id); continue; }
    if (source.canvasId !== target.canvasId || source.canvasId !== link.canvasId) issue('Links stay within one Work; use a reference for another Work.', undefined, link.id);
    if (!['visual', 'functional', 'numeric'].includes(link.kind)) { issue('Unknown link kind.', undefined, link.id); continue; }
    if (link.kind !== 'visual' && (source.smartLink || target.smartLink)) issue('Smart Links arrange visual branches and do not carry functional or numeric signals.', undefined, link.id);
    if (!['causal', 'chronological', 'mutual', 'cyclical', 'generic'].includes(link.relation)) issue('Unknown visual relation.', undefined, link.id);
    if (link.snap) {
      if (link.kind !== 'visual' || link.relation !== 'generic' || link.direction !== 'none'
        || link.targetPort !== undefined || link.sourceAnchor !== undefined || link.targetAnchor !== undefined || link.points !== undefined)
        issue('Snapped edges are undirected generic visual links without route points or ports.', undefined, link.id);
      if (!eligibleSnap(link.sourceId, link.targetId)) issue('Snapped edges need separate freely positioned nodes in the same containment space.', undefined, link.id);
      const pair = JSON.stringify([link.sourceId, link.targetId].sort());
      if (snappedPairs.has(pair)) issue('Two nodes share at most one snapped edge relationship.', undefined, link.id);
      snappedPairs.add(pair);
    }
    for (const endpoint of [source, target]) {
      const limit = limitsFor(endpoint).linkKinds;
      if (limit && !limit.has(link.kind)) issue('Link kind is excluded by an inherited rule.', endpoint.id, link.id);
    }
    if (!isComputedLink(link.kind)) continue;
    const channel = valueTypeForLink(link.kind)!;
    const sourceType = outputValueType(project, source);
    const targetPorts = inputPorts(target);
    if (!sourceType && source.kind !== 'reference') issue(`This node does not supply a ${channel === 'boolean' ? 'Boolean' : 'numeric'} value.`, source.id, link.id);
    else if (sourceType && sourceType !== channel) issue('Connection type does not match the source value type.', source.id, link.id);
    if (!targetPorts.length) issue('This node does not accept functional input.', target.id, link.id);
    const port = link.targetPort ?? 0;
    if (!Number.isInteger(port) || port < 0 || port >= targetPorts.length) issue('Functional input port is invalid.', target.id, link.id);
    else if (targetPorts[port]!.type !== channel) issue('Connection type does not match the target input type.', target.id, link.id);
    const key = `${target.id}:${port}`;
    if (ports.has(key)) issue('Only one connection is allowed per functional input.', target.id, link.id);
    ports.add(key); adjacency.get(source.id)?.push(target.id);
  }
  // Local references are dependencies too; external saved snapshots break live dependency chains.
  for (const node of project.nodes) if (node.kind === 'reference' && node.targetId) {
    const resolved = resolveReferenceTarget(project, node);
    if (resolved && !resolved.saved) adjacency.get(resolved.node.id)?.push(node.id);
  }
  const state = new Map<string, number>();
  const visit = (nodeId: string): void => {
    if (state.get(nodeId) === 1) { issue('Functional feedback cycles are not supported yet.', nodeId); return; }
    if (state.get(nodeId) === 2) return;
    state.set(nodeId, 1);
    for (const target of adjacency.get(nodeId) ?? []) visit(target);
    state.set(nodeId, 2);
  };
  for (const node of project.nodes) visit(node.id);
  return issues;
}

export function validateChange(before: Project, after: Project): ValidationIssue[] {
  if (!isProjectShape(after)) return [{ message: 'Project data is malformed or has an unsupported schema.' }];
  const issues = validateProjectGraph(after);
  const prior = new Map(before.nodes.map(node => [node.id, node]));
  for (const node of after.nodes) {
    const original = prior.get(node.id);
    if (original?.positionLocked && node.positionLocked && (original.x !== node.x || original.y !== node.y))
      issues.push({ nodeId: node.id, message: 'Unlock this node’s position before moving it.' });
  }
  return issues;
}

export function reparentNode(project: Project, nodeId: string, parentId: string): void {
  const candidate = structuredClone(project);
  const node = candidate.nodes.find(item => item.id === nodeId);
  const parent = candidate.nodes.find(item => item.id === parentId);
  if (!node || !parent) throw new Error('Node or parent does not exist.');
  if (node.kind === 'canvas') throw new Error('Work roots cannot be reparented.');
  if (node.canvasId !== parent.canvasId) throw new Error('Reparenting cannot move a node to another Work.');
  node.parentId = parentId;
  Object.assign(candidate, reconcileSnapLinks(candidate, node.canvasId, [nodeId], { createContacts: false }));
  const issues = validateChange(project, candidate);
  if (issues.length) throw new Error(issues.map(item => item.message).join(' '));
  project.nodes.find(item => item.id === nodeId)!.parentId = parentId;
}
