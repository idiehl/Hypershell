import type { ContainerVariant, LinkRelation, MeridianNode, MeridianProject, NodeKind, RemoteLink, UtilityVariant } from './types.ts';
import { addCanvas, commitSnapshots } from './model.ts';
import { isNodeIdentifier, validateProject } from './rules.ts';
import { arrangeChildren, reflowContainedChildren } from './grid.ts';
import { creationOperations } from './creation.ts';
import { isCellChild, isOrderedContainer, orderedPattern } from './cells.ts';
import { reconcileSnapLinks } from './clusters.ts';

function check(project: MeridianProject): void {
  const issues = validateProject(project);
  if (issues.length) throw new Error([...new Set(issues.map(issue => issue.message))].join(' '));
}
function draftFor(project: MeridianProject, nodeId: string): { project: MeridianProject; node: MeridianNode } {
  check(project);
  const draft = structuredClone(project), node = draft.nodes.find(node => node.id === nodeId);
  if (!node) throw new Error('The selected node does not exist.');
  return { project: draft, node };
}

export interface RemoteLinkSpec { targetId: string; relation?: LinkRelation; label?: string }

/** Record an inert conceptual relationship; an unavailable destination is legitimate. */
export function addRemoteLink(project: MeridianProject, nodeId: string, spec: RemoteLinkSpec): MeridianProject {
  if (!isNodeIdentifier(spec.targetId)) throw new Error('Use a target GUID of 1 to 96 letters, digits, underscores or hyphens, starting with a letter or digit.');
  const { project: draft, node } = draftFor(project, nodeId);
  const remote: RemoteLink = { id: crypto.randomUUID(), targetId: spec.targetId, relation: spec.relation ?? 'generic',
    ...(spec.label !== undefined ? { label: spec.label } : {}) };
  node.remoteLinks = [...node.remoteLinks ?? [], remote];
  check(draft); return draft;
}

export function removeRemoteLink(project: MeridianProject, nodeId: string, remoteLinkId: string): MeridianProject {
  const { project: draft, node } = draftFor(project, nodeId);
  if (!node.remoteLinks?.some(link => link.id === remoteLinkId)) throw new Error('The conceptual link does not exist on this node.');
  node.remoteLinks = node.remoteLinks.filter(link => link.id !== remoteLinkId);
  if (!node.remoteLinks.length) delete node.remoteLinks;
  check(draft); return draft;
}

/** Change one local identity and every included reference to it in a single undoable graph replacement. */
export function renameNode(project: MeridianProject, nodeId: string, newId: string): MeridianProject {
  if (!isNodeIdentifier(newId) || ['constructor', 'prototype', '__proto__'].includes(newId))
    throw new Error('Use a GUID of 1 to 96 letters, digits, underscores or hyphens, starting with a letter or digit. Reserved object keys cannot be GUIDs.');
  const { project: draft } = draftFor(project, nodeId);
  if (newId === nodeId) return draft;
  const occupied = new Set([draft.id, ...draft.canvases.map(item => item.id), ...draft.nodes.map(item => item.id),
    ...draft.links.map(item => item.id), ...draft.assets.map(item => item.id), ...Object.keys(draft.snapshots),
    ...draft.nodes.flatMap(item => item.remoteLinks?.map(link => link.id) ?? [])]);
  if (occupied.has(newId)) throw new Error('That GUID is already used by another object or saved observation.');
  const remap = (id: string): string => id === nodeId ? newId : id;
  const rewrite = (node: MeridianNode): void => {
    node.id = remap(node.id);
    if (node.parentId) node.parentId = remap(node.parentId);
    if (node.targetId) node.targetId = remap(node.targetId);
    if (node.dependencyIds) node.dependencyIds = node.dependencyIds.map(remap);
    if (node.remoteLinks) for (const link of node.remoteLinks) link.targetId = remap(link.targetId);
    // canvasTarget denotes another document. Its project/canvas identities never follow a local node rename.
  };
  for (const node of draft.nodes) rewrite(node);
  for (const canvas of draft.canvases) canvas.rootId = remap(canvas.rootId);
  for (const link of draft.links) {
    link.sourceId = remap(link.sourceId); link.targetId = remap(link.targetId);
    if (link.parentId) link.parentId = remap(link.parentId);
  }
  const snapshots: MeridianProject['snapshots'] = Object.create(null);
  for (const [key, snapshot] of Object.entries(draft.snapshots)) {
    rewrite(snapshot.node); snapshots[remap(key)] = snapshot;
  }
  draft.snapshots = snapshots;
  check(draft); return draft;
}

export interface NodeTypeSpec { kind: NodeKind; containerVariant?: ContainerVariant; utilityVariant?: UtilityVariant }

/** Retain authored content and children. Invalid inherited rules, cell references or typed wiring reject atomically. */
export function updateNodeType(project: MeridianProject, nodeId: string, spec: NodeTypeSpec): MeridianProject {
  const { project: draft, node } = draftFor(project, nodeId);
  if (node.kind === 'canvas' && spec.kind !== 'canvas' || node.kind !== 'canvas' && spec.kind === 'canvas')
    throw new Error('Work roots remain Work nodes. Choose Work to create a Work launcher inside the workspace.');
  if (node.canvasTarget && spec.kind !== 'canvas-link')
    throw new Error('This node has an external Work destination. Remove or replace that destination before changing its type.');
  if (node.targetCell && spec.kind !== 'reference')
    throw new Error('This node addresses a spreadsheet cell. Remove that reference before changing its type.');
  const previousKind = node.kind;
  node.kind = spec.kind;
  // Blocks retain dormant authored content for a reversible type change, while
  // their visible surface is a fixed, contentless backdrop with exterior children.
  if (node.kind === 'block' && previousKind !== 'block') node.sizeMode = 'fixed';
  if (spec.utilityVariant !== undefined && (node.kind !== 'junction' || !['pivot', 'joint'].includes(spec.utilityVariant))) throw new Error('Only Utility nodes have Pivot or Joint sub-types.');
  if (node.kind === 'junction') {
    node.utilityVariant = spec.utilityVariant ?? node.utilityVariant ?? 'joint';
    node.sizeMode = 'fixed';
    if (previousKind !== 'junction') {
      const center = { x: node.x + node.width / 2, y: node.y + node.height / 2 };
      node.width = 12; node.height = 12; node.x = center.x - 6; node.y = center.y - 6;
      node.style = { ...node.style, shape: 'circle' };
    }
  } else delete node.utilityVariant;
  const container = ['container', 'spreadsheet', 'module'].includes(node.kind);
  if (node.kind !== 'canvas') {
    const requested = spec.containerVariant ?? node.containerVariant ?? (node.kind === 'spreadsheet' ? 'spreadsheet' : 'contained');
    const variant = requested === 'spatial' ? 'contained' : requested;
    node.containerVariant = variant;
    if (node.kind === 'container' || node.kind === 'spreadsheet') node.kind = variant === 'spreadsheet' ? 'spreadsheet' : 'container';
    node.childLayout = container ? 'contained' : 'spatial';
    if (variant === 'gallery') {
      node.childArrangement = 'free';
      // Existing addressed cells still resolve to their original GUIDs after conversion.
      // A legacy grid is metadata only; it never arranges or limits gallery children.
      if (!draft.nodes.some(child => child.parentId === node.id && child.cell)) delete node.grid;
    } else if (variant === 'contained') node.childArrangement = 'radial';
    if (isOrderedContainer(node)) {
      node.childArrangement = orderedPattern(node);
      const addressed = draft.nodes.filter(child => child.parentId === node.id && child.cell);
      const columns = Math.max(3, ...addressed.map(child => child.cell!.column));
      const count = draft.nodes.filter(child => child.parentId === node.id && (isCellChild(child) || child.cell)).length;
      node.grid ??= { columns, rows: Math.max(3, Math.ceil(count / columns), ...addressed.map(child => child.cell!.row)),
        cellWidth: 200, cellHeight: 120, gap: 0, showHeaders: false, fillOrder: 'row' };
      if (variant === 'window') node.grid = { ...node.grid, windowColumns: node.grid.windowColumns ?? 3, windowRows: node.grid.windowRows ?? 3, scrollDirection: node.grid.scrollDirection ?? 'vertical' };
    }
  } else delete node.containerVariant;
  if (node.kind !== 'joint') delete node.operation;
  if (node.kind === 'joint' && !node.operation) {
    const choices = creationOperations(draft, node.parentId ?? node.id);
    const preferred = node.valueType === 'number' || typeof node.value === 'number' ? 'ADD' : 'AND';
    const operation = choices.find(item => item.operation === preferred) ?? choices[0];
    if (!operation) throw new Error('The inherited rules do not allow any joint operation.');
    node.operation = operation.operation;
  }
  if (node.kind === 'number' && node.value === undefined) node.value = 0;
  if (node.kind === 'input' && node.value === undefined) node.value = false;
  if (node.kind === 'number') node.valueType = 'number';
  if (node.kind === 'input') node.valueType = 'boolean';
  if (['switch', 'output'].includes(node.kind)) {
    node.valueType ??= typeof node.value === 'number' ? 'number' : 'boolean';
    if (node.kind === 'switch') node.closed ??= true;
  }
  if (node.kind === 'canvas-link' && !node.targetId && !node.canvasTarget) node.targetId = addCanvas(draft, node.title).rootId;
  // A new placement-owned pattern takes over positions from an edge cluster.
  Object.assign(draft, reconcileSnapLinks(draft, node.canvasId, undefined, { createContacts: false }));
  // An enclosing subtype needs a real usable interior, even after children have
  // previously been disclosed outside its old frame by the Spatial subtype.
  const result = isOrderedContainer(node)
    ? arrangeChildren(draft, node.id, orderedPattern(node)).project
    : node.kind !== 'canvas' && node.containerVariant !== 'gallery'
      ? reflowContainedChildren(draft, node.id) : draft;
  check(result);
  // Other canvases currently read saved observations. Validate the next observations too,
  // so a type edit cannot become incompatible only when automatic saving refreshes them.
  check(commitSnapshots(result));
  return result;
}
