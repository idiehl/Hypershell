import type { MeridianNode, MeridianProject, Rules, StoredModule } from './types.ts';
import { newProject } from './model.ts';
import { transitionContainerSize } from './container-size.ts';
import { isNodeIdentifier, isProjectShape, validateProject } from './rules.ts';
import { summarizeDependencies } from './dependencies.ts';
import { nodeAssetIds } from './appearance.ts';
import { navigationIndex } from './navigation.ts';
import { selectionGraph } from './selection.ts';
import { containingFixedInterior, nodeBorderWidth, nodeContentLayout, nodeSpacing } from './sizing.ts';
import { collisionFreeTranslation, collisionNodeShapes, isIntentionalContainment } from './collision.ts';
import { rebaseTextRuns } from './rich-text.ts';
import { nodeTag } from './node-tag.ts';
import { isExpansionTraySource, repairExpansionTrayCollisions } from './expansion-tray.ts';

export type { StoredModule } from './types.ts';
export interface ModulePlacement { canvasId: string; parentId?: string; x: number; y: number }
export interface ModuleInsertion {
  project: MeridianProject; rootId: string;
  /** Only copied nodes, links and assets; the definition's storage canvas is not inserted. */
  idMap: Record<string, string>;
}

function subtree(project: MeridianProject, rootId: string): Set<string> {
  const children = new Map<string, string[]>();
  for (const node of project.nodes) if (node.parentId) {
    const list = children.get(node.parentId) ?? [];
    list.push(node.id); children.set(node.parentId, list);
  }
  const result = new Set<string>(), queue = [rootId];
  for (let cursor = 0; cursor < queue.length; cursor++) {
    const id = queue[cursor];
    if (result.has(id)) continue;
    result.add(id); queue.push(...children.get(id) ?? []);
  }
  return result;
}

/** A packing enclosure reserves source covers, not their detached tray contents.
 * Retain the legacy raw-node fallback for selected exterior/manual compositions
 * that do not have a tray boundary. Omitted tray ancestors are not captured. */
function capturedPackingExtent(project: MeridianProject, nodes: readonly MeridianNode[]) {
  const nav = navigationIndex(project), included = new Set(nodes.map(node => node.id));
  const physical = nodes.filter(node => !nav.ancestors(node.id).some(ancestor => included.has(ancestor.id) && isExpansionTraySource(ancestor)));
  const roots = physical.filter(node => !node.parentId || !included.has(node.parentId));
  const boxes = roots.map(node => nav.packingBranchBounds({ ...node, parentId: null }));
  return {
    minX: Math.min(...boxes.map(box => box.x), ...physical.map(node => node.x)),
    minY: Math.min(...boxes.map(box => box.y), ...physical.map(node => node.y)),
    maxX: Math.max(...boxes.map(box => box.x + box.width), ...physical.map(node => node.x + node.width)),
    maxY: Math.max(...boxes.map(box => box.y + box.height), ...physical.map(node => node.y + node.height)),
  };
}

function remap(project: MeridianProject, externalReferenceIds: ReadonlySet<string> = new Set()): { project: MeridianProject; idMap: Record<string, string> } {
  const copy = structuredClone(project), idMap: Record<string, string> = Object.create(null);
  for (const item of [copy, ...copy.canvases, ...copy.nodes, ...copy.links, ...copy.assets]) idMap[item.id] = crypto.randomUUID();
  for (const node of copy.nodes) for (const link of node.remoteLinks ?? []) idMap[link.id] = crypto.randomUUID();
  const map = (id: string): string => idMap[id] ?? id;
  const localNodes = new Set(copy.nodes.map(node => node.id));
  copy.id = map(copy.id); copy.entryCanvasId = map(copy.entryCanvasId); copy.revision = 0;
  for (const canvas of copy.canvases) { canvas.id = map(canvas.id); canvas.rootId = map(canvas.rootId); if (canvas.style?.backgroundAssetId) canvas.style.backgroundAssetId = map(canvas.style.backgroundAssetId); }
  for (const node of copy.nodes) {
    const originalId = node.id;
    node.tag = nodeTag(node);
    node.id = map(node.id); node.canvasId = map(node.canvasId);
    if (node.parentId) node.parentId = map(node.parentId);
    if (node.targetId && !externalReferenceIds.has(originalId)) node.targetId = map(node.targetId);
    if (node.assetId) node.assetId = map(node.assetId);
    if (node.style?.backgroundAssetId) node.style.backgroundAssetId = map(node.style.backgroundAssetId);
    if (node.attachments) node.attachments = node.attachments.map(map);
    if (node.contentItems) node.contentItems = node.contentItems.map(item => item.type === 'asset' ? { ...item, id: crypto.randomUUID(), assetId: map(item.assetId) } : { ...item, id: crypto.randomUUID() });
    if (node.dependencyIds) node.dependencyIds = node.dependencyIds.map(map);
    if (node.remoteLinks) node.remoteLinks = node.remoteLinks.map(link => ({ ...link, id: map(link.id), targetId: localNodes.has(link.targetId) ? map(link.targetId) : link.targetId }));
  }
  for (const link of copy.links) {
    link.id = map(link.id); link.canvasId = map(link.canvasId);
    link.sourceId = map(link.sourceId); link.targetId = map(link.targetId);
    if (link.parentId) link.parentId = map(link.parentId);
  }
  for (const asset of copy.assets) asset.id = map(asset.id);
  for (const dependency of copy.dependencies) dependency.canvasId = map(dependency.canvasId);
  return { project: copy, idMap };
}

/** Validate a portable definition without evaluating supplied code or resolving external objects. */
export function validateModule(value: unknown): string[] {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return ['Module data must be an object.'];
  const module = value as Partial<StoredModule>;
  const issues: string[] = [];
  if (module.schemaVersion !== 1) issues.push('Unsupported module schema.');
  if (typeof module.id !== 'string' || !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(module.id)) issues.push('A module needs a GUID.');
  if (typeof module.name !== 'string' || module.name !== module.name.trim() || !module.name || module.name.length > 160) issues.push('A module name must contain 1 to 160 characters.');
  if (typeof module.createdAt !== 'string' || !/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})$/.test(module.createdAt) || !Number.isFinite(Date.parse(module.createdAt))) issues.push('Module creation time is invalid.');
  if (!isProjectShape(module.project)) return [...issues, 'Module Work data is malformed.'];
  const project = module.project;
  issues.push(...validateProject(project).map(issue => issue.message));
  if (project.canvases.length !== 1) issues.push('A module definition must contain exactly one Work.');
  if (Object.keys(project.snapshots).length) issues.push('Module definitions do not store runtime snapshots.');
  const storageIds = new Set([project.id, ...project.canvases.flatMap(canvas => [canvas.id, canvas.rootId])]);
  if (project.nodes.some(node => node.targetId && storageIds.has(node.targetId) || node.dependencyIds?.some(id => storageIds.has(id)))) issues.push('Module contents cannot depend on its storage Work.');
  const root = project.nodes.find(node => node.id === module.rootId);
  if (!root || root.kind !== 'module' || root.parentId !== project.canvases[0]?.rootId) issues.push('A module needs one packing container under its Work root.');
  else {
    const included = subtree(project, root.id);
    if (project.nodes.some(node => node.kind !== 'canvas' && !included.has(node.id))) issues.push('Every module node must belong to its packing container.');
    if (project.links.some(link => !included.has(link.sourceId) || !included.has(link.targetId))) issues.push('Module links must stay inside its packing container.');
    if (project.links.some(link => link.parentId !== undefined && !included.has(link.parentId))) issues.push('Module connection owners must stay inside its packing container.');
  }
  return [...new Set(issues)];
}

/** Save one branch as a fresh definition; boundary requirements remain visible as dependencies. */
export function createModule(project: MeridianProject, rootId: string, name: string): StoredModule {
  const problems = validateProject(project);
  if (problems.length) throw new Error(problems.map(issue => issue.message).join(' '));
  const selected = project.nodes.find(node => node.id === rootId);
  if (!selected || selected.kind === 'canvas') throw new Error('Choose a non-root node to save as a module.');
  const included = subtree(project, selected.id);
  const definition = newProject(name.trim());
  const canvas = definition.canvases[0]; canvas.title = name.trim();
  definition.nodes[0].title = name.trim();
  const nodes = project.nodes.filter(node => included.has(node.id)).map(node => structuredClone(node));
  for (const node of nodes) node.canvasId = canvas.id;
  let packing = nodes.find(node => node.id === rootId)!;
  if ((packing.kind === 'container' || packing.kind === 'module') && !packing.cluster) {
    if (packing.titleRuns) packing.titleRuns = rebaseTextRuns(packing.title, name.trim(), packing.titleRuns);
    packing.kind = 'module'; packing.title = name.trim(); packing.parentId = canvas.rootId;
  } else {
    // Preserve a selected media/logic node or disclosure Cluster's semantics
    // inside a new packing container rather than changing its behavior.
    const { minX, minY, maxX, maxY } = capturedPackingExtent(project, nodes);
    const wrapper: MeridianNode = {
      id: crypto.randomUUID(), canvasId: canvas.id, parentId: canvas.rootId, kind: 'module', title: name.trim(), tag: name.trim(),
      x: minX - 24, y: minY - 116, width: Math.max(480, maxX - minX + 48), height: Math.max(300, maxY - minY + 140),
    };
    packing.parentId = wrapper.id; nodes.unshift(wrapper); packing = wrapper;
  }
  // A definition must not widen permissions merely because its old ancestors are absent.
  const inheritedRules: Rules = {};
  let ancestor = selected.parentId ? project.nodes.find(node => node.id === selected.parentId) : undefined;
  const ancestors: MeridianNode[] = [];
  while (ancestor) { ancestors.push(ancestor); ancestor = ancestor.parentId ? project.nodes.find(node => node.id === ancestor!.parentId) : undefined; }
  for (const key of ['nodeKinds', 'linkKinds', 'operations'] as const) {
    const lists = [...ancestors.map(node => node.rules?.[key]), packing.rules?.[key]].filter((list): list is NonNullable<typeof list> => !!list);
    if (lists.length) (inheritedRules as Record<string, string[]>)[key] = lists.reduce<string[]>((allowed, list) => allowed.filter(value => list.includes(value as never)), [...lists[0]]);
  }
  if (Object.keys(inheritedRules).length) packing.rules = inheritedRules;
  const externalContext = summarizeDependencies(project, { rootIds: [selected.id] }).entries.filter(item => !included.has(item.id)).map(item => item.id);
  if (externalContext.length) packing.dependencyIds = [...new Set([...(packing.dependencyIds ?? []), ...externalContext])];
  definition.nodes.push(...nodes);
  definition.links = project.links.filter(link => included.has(link.sourceId) && included.has(link.targetId)).map(link => ({
    ...structuredClone(link), canvasId: canvas.id,
    ...(link.parentId && !included.has(link.parentId) ? { parentId: packing.id } : {}),
  }));
  const used = new Set(nodes.flatMap(nodeAssetIds));
  definition.assets = project.assets.filter(asset => used.has(asset.id)).map(asset => structuredClone(asset));
  const externalIds = new Set(nodes.flatMap(node => [...node.dependencyIds ?? [], ...node.targetId && !included.has(node.targetId) ? [node.targetId] : []]).filter(id => !included.has(id)));
  const referencedCanvases = new Set([...externalIds].map(id => project.nodes.find(node => node.id === id)?.canvasId ?? (Object.hasOwn(project.snapshots, id) ? project.snapshots[id]?.node.canvasId : undefined)).filter(Boolean));
  definition.dependencies = project.dependencies.filter(dependency => referencedCanvases.has(dependency.canvasId)).map(dependency => structuredClone(dependency));
  const fresh = remap(definition);
  const module: StoredModule = { schemaVersion: 1, id: crypto.randomUUID(), name: name.trim(), createdAt: new Date().toISOString(), rootId: fresh.idMap[packing.id], project: fresh.project };
  const issues = validateModule(module);
  if (issues.length) throw new Error(issues.join(' '));
  return module;
}

/** Capture an exact multi-selection. Explicitly omitted descendants never enter the definition. */
export function createModuleFromSelection(project: MeridianProject, selectedIds: readonly string[], name: string): StoredModule {
  const title = name.trim();
  if (!title || title.length > 160) throw new Error('A module name must contain 1 to 160 characters.');
  const selection = selectionGraph(project, selectedIds, { includeDescendants: false });
  const included = new Set(selection.nodeIds), source = navigationIndex(project);
  const definition = newProject(title), canvas = definition.canvases[0];
  canvas.title = title; definition.nodes[0].title = title;
  const packing: MeridianNode = {
    id: crypto.randomUUID(), canvasId: canvas.id, parentId: canvas.rootId, kind: 'module', title, tag: title,
    x: 0, y: 0, width: 160, height: 100, sizeMode: 'fixed', collapsed: false,
    containerVariant: 'contained', childLayout: 'contained', childArrangement: 'free', childPosition: 'manual', placement: 'free',
  };
  const externalReferenceIds = new Set<string>();
  const nodes = project.nodes.filter(node => included.has(node.id)).map(original => {
    const node = structuredClone(original); node.canvasId = canvas.id;
    const parent = source.ancestors(original.id).find(ancestor => included.has(ancestor.id));
    node.parentId = parent?.id ?? packing.id;
    if (node.parentId !== original.parentId) {
      // A former cell/edge alias belongs to its original immediate parent.
      delete node.cell; node.placement = 'free'; node.childPosition = 'manual';
    }
    // Omitted ancestors must not widen the permissions of the captured branch.
    const rules: Rules = {};
    for (const key of ['nodeKinds', 'linkKinds', 'operations'] as const) {
      const limits = [original, ...source.ancestors(original.id)].map(ancestor => ancestor.rules?.[key]).filter((list): list is NonNullable<typeof list> => !!list);
      if (limits.length) (rules as Record<string, string[]>)[key] = limits.reduce<string[]>((allowed, limit) => allowed.filter(value => (limit as readonly string[]).includes(value)), [...limits[0]]);
    }
    if (Object.keys(rules).length) node.rules = rules;
    if (node.kind === 'reference' && node.targetId && node.targetCell) {
      const target = source.original(node.id);
      if (!target || !included.has(target.id)) externalReferenceIds.add(original.id);
    }
    return node;
  });
  definition.nodes.push(packing, ...nodes);
  definition.links = project.links.filter(link => included.has(link.sourceId) && included.has(link.targetId)).map(original => {
    const link = structuredClone(original); link.canvasId = canvas.id;
    const owner = source.linkParent(original);
    link.parentId = owner && included.has(owner.id) ? owner.id : packing.id;
    return link;
  });
  // Inspect only the selected nodes, then follow their actual dependencies.
  // Removing containment in this read-only context avoids pulling requirements
  // from descendants the user explicitly omitted from this module.
  const context = { ...project, nodes: project.nodes.map(node => ({ ...node, parentId: null })) };
  const externalContext = summarizeDependencies(context, { rootIds: selection.nodeIds }).entries.filter(item => !included.has(item.id) && isNodeIdentifier(item.id)).map(item => item.id);
  if (externalContext.length) packing.dependencyIds = [...new Set(externalContext)];
  const used = new Set(nodes.flatMap(nodeAssetIds));
  definition.assets = project.assets.filter(asset => used.has(asset.id)).map(asset => structuredClone(asset));
  const externalIds = new Set([...externalContext, ...nodes.flatMap(node => [
    ...node.dependencyIds ?? [], ...node.targetId && (!included.has(node.targetId) || externalReferenceIds.has(node.id)) ? [node.targetId] : [],
  ])].filter(id => !included.has(id) || nodes.some(node => externalReferenceIds.has(node.id) && node.targetId === id)));
  const referencedCanvases = new Set([...externalIds].map(id => project.nodes.find(node => node.id === id)?.canvasId ?? project.snapshots[id]?.node.canvasId).filter(Boolean));
  definition.dependencies = project.dependencies.filter(dependency => referencedCanvases.has(dependency.canvasId)).map(dependency => structuredClone(dependency));
  // Measure only the captured composition. Detached tray contents remain real
  // module nodes, but are not interior area for the new packing enclosure.
  const { minX, minY, maxX, maxY } = capturedPackingExtent(definition, nodes);
  const spacing = nodeSpacing(packing.style), border = nodeBorderWidth(packing), sideInset = spacing.childPadding + border;
  packing.width = Math.max(160, maxX - minX + 2 * sideInset);
  const header = nodeContentLayout(definition, packing, {}, false, packing.width - 2 * (spacing.padding + border)).headerHeight;
  packing.x = minX - sideInset; packing.y = minY - header - sideInset;
  packing.height = Math.max(60, maxY - minY + header + 2 * sideInset);
  const fresh = remap(definition, externalReferenceIds);
  const module: StoredModule = { schemaVersion: 1, id: crypto.randomUUID(), name: title, createdAt: new Date().toISOString(), rootId: fresh.idMap[packing.id], project: fresh.project };
  const issues = validateModule(module);
  if (issues.length) throw new Error(issues.join(' '));
  return module;
}

/** Insert a fully remapped independent copy, atomically enforcing destination inherited rules. */
export function instantiateModule(project: MeridianProject, stored: StoredModule, placement: ModulePlacement): ModuleInsertion {
  const issues = validateModule(stored);
  if (issues.length) throw new Error(issues.join(' '));
  if (!Number.isFinite(placement.x) || !Number.isFinite(placement.y)) throw new Error('Module position must be finite.');
  const candidate = structuredClone(project), canvas = candidate.canvases.find(canvas => canvas.id === placement.canvasId);
  if (!canvas) throw new Error('The destination Work does not exist.');
  const parentId = placement.parentId ?? canvas.rootId, parent = candidate.nodes.find(node => node.id === parentId);
  if (!parent || parent.canvasId !== canvas.id) throw new Error('The destination parent must belong to the destination Work.');
  const owned = new Set(stored.project.nodes.map(node => node.id));
  for (const node of stored.project.nodes) if (node.kind === 'reference' && node.targetId && !owned.has(node.targetId) &&
    (candidate.nodes.some(target => target.id === node.targetId) || Object.hasOwn(candidate.snapshots, node.targetId))) {
    throw new Error(`External reference "${node.title}" would reconnect to an existing source. Insert into a new Work first, then explicitly choose or remove this reference's source.`);
  }
  const fresh = remap(stored.project), rootId = fresh.idMap[stored.rootId];
  const root = fresh.project.nodes.find(node => node.id === rootId)!;
  const dx = placement.x - root.x, dy = placement.y - root.y;
  const inserted = fresh.project.nodes.filter(node => node.kind !== 'canvas');
  for (const node of inserted) { node.canvasId = canvas.id; node.x += dx; node.y += dy; }
  root.parentId = parent.id; transitionContainerSize(parent, true, undefined, { preserveCurrentRecord: !!containingFixedInterior(candidate, parent) });
  for (const link of fresh.project.links) {
    link.canvasId = canvas.id;
    if (link.points) link.points = link.points.map(point => ({ x: point.x + dx, y: point.y + dy }));
  }
  candidate.nodes.push(...inserted); candidate.links.push(...fresh.project.links); candidate.assets.push(...fresh.project.assets);
  const nav = navigationIndex(candidate), insertedIds = new Set(inserted.map(node => node.id));
  const physical = (node: MeridianNode) => nav.isNodeVisible(node.id) && !nav.ancestors(node.id).some(ancestor => ancestor.containerVariant === 'window');
  const moving = inserted.filter(physical).flatMap(node => collisionNodeShapes(candidate, node, nav.bounds(node)));
  const obstacles = candidate.nodes.filter(node => node.canvasId === canvas.id && node.kind !== 'canvas' && !insertedIds.has(node.id) && physical(node))
    .flatMap(node => collisionNodeShapes(candidate, node, nav.bounds(node)));
  const shift = collisionFreeTranslation(moving, obstacles, { x: 0, y: 0 }, (a, b) => !!a.id && !!b.id && isIntentionalContainment(nav, a.id, b.id));
  if (!Number.isFinite(shift.x) || !Number.isFinite(shift.y)) throw new Error('No finite clear position is available for the module.');
  for (const node of inserted) { node.x += shift.x; node.y += shift.y; }
  for (const link of fresh.project.links) if (link.points) link.points = link.points.map(point => ({ x: point.x + shift.x, y: point.y + shift.y }));
  for (const dependency of fresh.project.dependencies) {
    const existing = candidate.dependencies.find(item => item.canvasId === dependency.canvasId);
    if (existing) existing.required ||= dependency.required;
    else candidate.dependencies.push(dependency);
  }
  repairExpansionTrayCollisions(candidate);
  const validation = validateProject(candidate);
  if (validation.length) throw new Error(validation.map(issue => issue.message).join(' '));
  const idMap: Record<string, string> = Object.create(null);
  for (const item of [...stored.project.nodes.filter(node => node.kind !== 'canvas'), ...stored.project.links, ...stored.project.assets]) idMap[item.id] = fresh.idMap[item.id];
  for (const node of stored.project.nodes) for (const link of node.remoteLinks ?? []) idMap[link.id] = fresh.idMap[link.id];
  return { project: candidate, rootId, idMap };
}
