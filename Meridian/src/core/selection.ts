import type { MeridianNode, MeridianProject, Rules, SavedSnapshot } from './types.ts';
import { newProject } from './model.ts';
import { navigationIndex } from './navigation.ts';
import { isEnclosingContainer, isOrderedContainer, resolveGridCell } from './cells.ts';
import { reflowContainedChildren } from './grid.ts';
import { isComputedLink } from './operations.ts';
import { validateChange, validateProject } from './rules.ts';
import { nodeAssetIds } from './appearance.ts';
import { lockedClusterNodeIds, reconcileSnapLinks } from './clusters.ts';
import { nodeBorderWidth, nodeContentLayout, nodeSpacing } from './sizing.ts';

export interface SelectionGraph { canvasId: string; rootIds: string[]; nodeIds: string[] }
export interface SnippetOptions { includeDescendants: boolean; includeExternalReferences: boolean; title?: string }
export interface SnippetRequirement {
  id: string; title: string; status: 'snapshot' | 'excluded' | 'missing'; required: boolean;
  sourceIds: string[]; note: string;
}
export interface SnippetReport {
  selectedNodes: number; copiedNodes: number; copiedLinks: number; copiedAssets: number;
  includedDescendants: number; omittedDescendants: number; omittedHiddenDescendants: number;
  crossingLinks: number; reassignedLinkOwners: number; includedSnapshots: number;
  externalRequirements: SnippetRequirement[]; missingAssets: string[];
}

function assertValid(project: MeridianProject): void {
  const issues = validateProject(project);
  if (issues.length) throw new Error(issues.map(issue => issue.message).join(' '));
}

function graph(project: MeridianProject, ids: readonly string[], includeDescendants = true): SelectionGraph {
  if (!Array.isArray(ids) || !ids.length) throw new Error('Select at least one node.');
  const nav = navigationIndex(project), selected = new Set(ids);
  let canvasId: string | undefined;
  for (const id of selected) {
    const node = nav.nodes.get(id);
    if (!node) throw new Error(`Selected node does not exist: ${id}`);
    if (node.kind === 'canvas') throw new Error('Select nodes inside a Work; Work roots cannot be selected for this action.');
    if (canvasId && canvasId !== node.canvasId) throw new Error('Select nodes from one Work.');
    canvasId = node.canvasId;
  }
  const rootIds = project.nodes.filter(node => selected.has(node.id) && !nav.ancestors(node.id).some(parent => selected.has(parent.id))).map(node => node.id);
  const included = new Set(selected);
  if (includeDescendants) for (const id of rootIds) for (const node of nav.descendants(id)) included.add(node.id);
  return { canvasId: canvasId!, rootIds, nodeIds: project.nodes.filter(node => included.has(node.id)).map(node => node.id) };
}

/** Normalize branches in stable document order without counting descendants twice. */
export function selectionGraph(project: MeridianProject, ids: readonly string[], options: { includeDescendants?: boolean } = {}): SelectionGraph {
  assertValid(project);
  return graph(project, ids, options.includeDescendants ?? true);
}

function checkedChange(project: MeridianProject, edit: (draft: MeridianProject) => void): MeridianProject {
  assertValid(project);
  const draft = structuredClone(project);
  edit(draft);
  const issues = validateChange(project, draft);
  if (issues.length) throw new Error(issues.map(issue => issue.message).join(' '));
  return draft;
}

/** Move each selected branch once. Boundary wires retain their authored world-space bends. */
export function moveSelection(project: MeridianProject, ids: readonly string[], dx: number, dy: number): MeridianProject {
  if (!Number.isFinite(dx) || !Number.isFinite(dy)) throw new Error('Selection movement must be finite.');
  return checkedChange(project, draft => {
    const requested = graph(draft, ids);
    const selection = graph(draft, lockedClusterNodeIds(draft, requested.canvasId, requested.nodeIds)), included = new Set(selection.nodeIds), roots = new Set(selection.rootIds);
    for (const node of draft.nodes) if (included.has(node.id)) {
      node.x += dx; node.y += dy;
      if (roots.has(node.id)) node.placement = 'free';
    }
    for (const link of draft.links) if (included.has(link.sourceId) && included.has(link.targetId) && link.points) {
      link.points = link.points.map(point => ({ x: point.x + dx, y: point.y + dy }));
    }
    const parents = new Set(selection.rootIds.map(id => draft.nodes.find(node => node.id === id)?.parentId).filter((id): id is string => !!id));
    for (const parentId of parents) if (isOrderedContainer(draft.nodes.find(node => node.id === parentId)!)) Object.assign(draft, reflowContainedChildren(draft, parentId));
    Object.assign(draft, reconcileSnapLinks(draft, selection.canvasId, selection.nodeIds, { createContacts: false }));
  });
}

/** Delete selected branches and connections they contain, even when their endpoints are elsewhere. */
export function deleteSelectionGraph(project: MeridianProject, ids: readonly string[]): MeridianProject {
  return checkedChange(project, draft => {
    const included = new Set(graph(draft, ids).nodeIds), nav = navigationIndex(draft);
    const parentIds = new Set(draft.nodes.filter(node => included.has(node.id) && node.parentId && !included.has(node.parentId)).map(node => node.parentId!));
    draft.links = draft.links.filter(link => !included.has(link.sourceId) && !included.has(link.targetId) && !included.has(nav.linkParent(link)?.id ?? ''));
    draft.nodes = draft.nodes.filter(node => !included.has(node.id));
    for (const id of included) delete draft.snapshots[id];
    for (const parentId of parentIds) if (draft.nodes.some(node => node.id === parentId && isOrderedContainer(node))) Object.assign(draft, reflowContainedChildren(draft, parentId));
  });
}

function groupPosition(project: MeridianProject, roots: MeridianNode[], options: { x?: number; y?: number }, sideInset: number): { x: number; y: number } {
  const nav = navigationIndex(project), boxes = roots.map(root => nav.branchBounds(root));
  const start = { x: options.x ?? Math.min(...boxes.map(box => box.x)) - sideInset, y: options.y ?? Math.min(...boxes.map(box => box.y)) - 210 };
  if (!Number.isFinite(start.x) || !Number.isFinite(start.y)) throw new Error('Container position must be finite.');
  if (options.x !== undefined && options.y !== undefined) return start;
  // A surrounding frame encloses usable space; its border is not an occupied tile covering all of its children.
  const frames = new Set(nav.ancestors(roots[0].id).filter(isEnclosingContainer).map(node => node.id));
  const obstacles = project.nodes.filter(node => node.canvasId === roots[0].canvasId && node.kind !== 'canvas' && !frames.has(node.id) && nav.isNodeVisible(node.id)).map(nav.bounds);
  const fits = (point: { x: number; y: number }) => Number.isFinite(point.x) && Number.isFinite(point.y) && obstacles.every(box =>
    point.x + 320 + 20 <= box.x || point.x >= box.x + box.width + 20 || point.y + 180 + 20 <= box.y || point.y >= box.y + box.height + 20);
  if (fits(start)) return start;
  const candidates: { x: number; y: number }[] = [];
  for (let ring = 1; ring <= 32; ring++) for (const [dx, dy] of [[-1, -1], [0, -1], [1, -1], [-1, 0], [1, 0], [-1, 1], [0, 1], [1, 1]]) {
    if (options.x !== undefined && dx || options.y !== undefined && dy) continue;
    candidates.push({ x: start.x + dx * ring * 40, y: start.y + dy * ring * 40 });
  }
  if (obstacles.length) {
    if (options.x === undefined) candidates.push({ x: Math.min(...obstacles.map(box => box.x)) - 340, y: start.y }, { x: Math.max(...obstacles.map(box => box.x + box.width)) + 20, y: start.y });
    if (options.y === undefined) candidates.push({ x: start.x, y: Math.min(...obstacles.map(box => box.y)) - 200 }, { x: start.x, y: Math.max(...obstacles.map(box => box.y + box.height)) + 20 });
  }
  candidates.sort((a, b) => (a.x - start.x) ** 2 + (a.y - start.y) ** 2 - (b.x - start.x) ** 2 - (b.y - start.y) ** 2 || a.y - b.y || a.x - b.x);
  const position = candidates.find(fits);
  if (!position) throw new Error('No finite clear position is available for the new container.');
  return position;
}

/** Packing is explicit containment. Roots must be siblings to avoid silently changing inherited context. */
export function groupSelection(project: MeridianProject, ids: readonly string[], options: { title?: string; x?: number; y?: number } = {}): { project: MeridianProject; rootId: string } {
  let rootId = '';
  const result = checkedChange(project, draft => {
    const selection = graph(draft, ids), roots = selection.rootIds.map(id => draft.nodes.find(node => node.id === id)!);
    const parentId = roots[0].parentId;
    if (roots.some(node => node.parentId !== parentId)) throw new Error('Group nodes with the same parent. Move branches into one parent explicitly first.');
    const title = options.title?.trim() || 'New group';
    rootId = crypto.randomUUID();
    const group: MeridianNode = { id: rootId, canvasId: selection.canvasId, parentId, kind: 'container', title, tag: title,
      x: 0, y: 0, width: 320, height: 180, sizeMode: 'auto', containerVariant: 'contained', childLayout: 'contained', childArrangement: 'free', collapsed: false, placement: 'free' };
    const spacing = nodeSpacing(group.style), border = nodeBorderWidth(group), sideInset = spacing.childPadding + border;
    let { x, y } = groupPosition(draft, roots, options, sideInset);
    // This is the creation of a new enclosure, so initialize its frame from the
    // selected composition once. Ordinary reflow must never grow that frame.
    const nav = navigationIndex(draft), boxes = roots.map(root => nav.branchBounds(root));
    const width = Math.max(320, ...boxes.map(box => box.x + box.width - x + sideInset));
    group.width = width;
    const header = nodeContentLayout(draft, group, {}, false, width - 2 * (spacing.padding + border)).headerHeight;
    if (options.y === undefined) y = Math.min(y, ...boxes.map(box => box.y - header - sideInset));
    const height = Math.max(180, ...boxes.map(box => box.y + box.height - y + sideInset));
    Object.assign(group, { x, y, width, height }); draft.nodes.push(group);
    for (const node of roots) { node.parentId = rootId; node.placement = 'free'; }
  });
  return { project: reflowContainedChildren(result, rootId), rootId };
}

function effectiveRules(node: MeridianNode, nav: ReturnType<typeof navigationIndex>): Rules | undefined {
  const result: Rules = {};
  for (const key of ['nodeKinds', 'linkKinds', 'operations'] as const) {
    const lists = [node, ...nav.ancestors(node.id)].map(ancestor => ancestor.rules?.[key]).filter((list): list is NonNullable<typeof list> => !!list);
    if (lists.length) (result as Record<string, string[]>)[key] = lists.reduce<string[]>((allowed, list) => allowed.filter(value => (list as readonly string[]).includes(value)), [...lists[0]]);
  }
  return Object.keys(result).length ? result : undefined;
}

/**
 * Copy only the explicitly selected graph. The caller chooses descendant and saved-reference inclusion.
 * Outside live nodes/canvases never enter the copy; snapshots are observations, not cloned operating systems.
 */
export function createSnippet(project: MeridianProject, ids: readonly string[], options: SnippetOptions): { project: MeridianProject; report: SnippetReport; idMap: Record<string, string> } {
  assertValid(project);
  if (!options || typeof options.includeDescendants !== 'boolean' || typeof options.includeExternalReferences !== 'boolean') throw new Error('Choose descendant and external-reference inclusion explicitly.');
  const selection = graph(project, ids, options.includeDescendants), nav = navigationIndex(project);
  const selected = new Set(ids), included = new Set(selection.nodeIds), full = new Set(graph(project, ids).nodeIds);
  const omitted = [...full].filter(id => !included.has(id));
  const title = options.title?.trim() || `${project.title} · Selection`;
  const copy = newProject(title), canvas = copy.canvases[0]; canvas.title = title; copy.nodes[0].title = title;
  copy.snapshots = Object.create(null);
  const sourceCanvas = project.canvases.find(item => item.id === selection.canvasId)!;
  if (sourceCanvas.style) canvas.style = structuredClone(sourceCanvas.style);
  if (sourceCanvas.snapping) canvas.snapping = structuredClone(sourceCanvas.snapping);
  const idMap: Record<string, string> = Object.create(null);
  const register = (id: string) => idMap[id] ??= crypto.randomUUID();
  for (const id of included) register(id);
  const internalLinks = project.links.filter(link => included.has(link.sourceId) && included.has(link.targetId));
  const crossing = project.links.filter(link => included.has(link.sourceId) !== included.has(link.targetId));
  for (const link of internalLinks) register(link.id);
  const neededAssets = new Set([...project.nodes.filter(node => included.has(node.id)).flatMap(nodeAssetIds), ...sourceCanvas.style?.backgroundAssetId ? [sourceCanvas.style.backgroundAssetId] : []]);
  for (const asset of project.assets) if (neededAssets.has(asset.id)) { register(asset.id); copy.assets.push({ ...structuredClone(asset), id: idMap[asset.id] }); }
  if (canvas.style?.backgroundAssetId && idMap[canvas.style.backgroundAssetId]) canvas.style.backgroundAssetId = idMap[canvas.style.backgroundAssetId];
  const report: SnippetReport = {
    selectedNodes: selected.size, copiedNodes: included.size, copiedLinks: internalLinks.length, copiedAssets: copy.assets.length,
    includedDescendants: included.size - selected.size, omittedDescendants: omitted.length,
    omittedHiddenDescendants: omitted.filter(id => !nav.isNodeVisible(id)).length,
    crossingLinks: crossing.length, reassignedLinkOwners: 0, includedSnapshots: 0,
    externalRequirements: [], missingAssets: [...neededAssets].filter(id => !project.assets.some(asset => asset.id === id)),
  };
  for (const original of project.nodes) if (included.has(original.id)) {
    const node = structuredClone(original); node.id = idMap[original.id]; node.canvasId = canvas.id;
    node.parentId = original.parentId && included.has(original.parentId) ? idMap[original.parentId] : canvas.rootId;
    if (!original.parentId || !included.has(original.parentId)) {
      const rules = effectiveRules(original, nav); if (rules) node.rules = rules;
      node.placement = 'free';
      delete node.cell;
    }
    const addressedCell = original.kind === 'reference' && original.targetId && original.targetCell ? resolveGridCell(project, original.targetId, original.targetCell) : undefined;
    if (node.targetId && included.has(node.targetId) && (!node.targetCell || addressedCell && included.has(addressedCell.id))) node.targetId = idMap[node.targetId];
    if (node.assetId && idMap[node.assetId]) node.assetId = idMap[node.assetId];
    if (node.style?.backgroundAssetId && idMap[node.style.backgroundAssetId]) node.style.backgroundAssetId = idMap[node.style.backgroundAssetId];
    if (node.attachments) node.attachments = node.attachments.map(id => idMap[id] ?? id);
    if (node.contentItems) node.contentItems = node.contentItems.map(item => item.type === 'asset' ? { ...item, id: crypto.randomUUID(), assetId: idMap[item.assetId] ?? item.assetId } : { ...item, id: crypto.randomUUID() });
    if (node.remoteLinks) node.remoteLinks = node.remoteLinks.map(link => ({ ...link, id: register(link.id), targetId: included.has(link.targetId) ? idMap[link.targetId] : link.targetId }));
    const required = new Set(node.dependencyIds ?? []);
    // Excluded sources remain explicitly required; a stored value must not reconnect their missing wire.
    for (const link of crossing) if (isComputedLink(link.kind) && link.targetId === original.id) required.add(link.sourceId);
    if (required.size) node.dependencyIds = [...required].map(id => included.has(id) ? idMap[id] : id);
    copy.nodes.push(node);
  }
  for (const original of internalLinks) {
    const owner = nav.linkParent(original), copiedOwner = owner && included.has(owner.id);
    if (owner && !copiedOwner && owner.id !== sourceCanvas.rootId) report.reassignedLinkOwners++;
    copy.links.push({ ...structuredClone(original), id: idMap[original.id], canvasId: canvas.id,
      sourceId: idMap[original.sourceId], targetId: idMap[original.targetId], parentId: copiedOwner ? idMap[owner.id] : canvas.rootId });
  }

  const requirements = new Map<string, SnippetRequirement>();
  const knownCanvases = new Set(project.canvases.map(item => item.id));
  const queue: { id: string; sourceId: string; wire: boolean; externalContext?: boolean }[] = [];
  for (const original of project.nodes) if (included.has(original.id)) {
    for (const id of original.dependencyIds ?? []) if (!included.has(id)) queue.push({ id, sourceId: original.id, wire: false });
    if ((original.kind === 'reference' || original.kind === 'canvas-link') && original.targetId) {
      const savedCell = original.kind === 'reference' && original.targetCell ? resolveGridCell(project, original.targetId, original.targetCell, { savedOnly: true }) : undefined;
      const liveCell = original.kind === 'reference' && original.targetCell ? resolveGridCell(project, original.targetId, original.targetCell) : undefined;
      const outsideCell = !!original.targetCell && (!liveCell || !included.has(liveCell.id));
      if (!included.has(original.targetId) || outsideCell) queue.push({ id: original.targetId, sourceId: original.id, wire: false, externalContext: outsideCell });
      if (outsideCell && savedCell) queue.push({ id: savedCell.id, sourceId: original.id, wire: false, externalContext: true });
    }
  }
  for (const link of crossing) if (isComputedLink(link.kind) && included.has(link.targetId)) queue.push({ id: link.sourceId, sourceId: link.targetId, wire: true });
  const touchedCanvases = new Set<string>();
  for (let cursor = 0; cursor < queue.length; cursor++) {
    const { id, sourceId, wire, externalContext } = queue[cursor];
    // Included nodes become independent copies. An external observation still
    // depends on the original identities, not those independently editable copies.
    if (included.has(id) && !externalContext) continue;
    const existing = requirements.get(id);
    if (existing) {
      if (!existing.sourceIds.includes(sourceId)) existing.sourceIds.push(sourceId);
      if (wire && !existing.note.includes('wire')) existing.note += ' The crossing input wire is omitted and remains disconnected.';
      continue;
    }
    const live = nav.nodes.get(id), rawSaved = Object.hasOwn(project.snapshots, id) ? project.snapshots[id] : undefined;
    // Never resurrect a deleted local original from its stale saved observation.
    const saved = rawSaved && (live || !knownCanvases.has(rawSaved.node.canvasId)) ? rawSaved : undefined;
    const externalCanvas = project.canvases.find(item => item.id === id);
    const sourceCanvasId = live?.canvasId ?? saved?.node.canvasId ?? externalCanvas?.id;
    if (sourceCanvasId) touchedCanvases.add(sourceCanvasId);
    const status = saved && options.includeExternalReferences ? 'snapshot' : saved || live || externalCanvas ? 'excluded' : 'missing';
    const note = status === 'snapshot' ? 'Saved observation only; the original Work and its wiring are not included.' :
      status === 'excluded' ? 'Outside the selected graph. No original node or Work is copied.' : 'Required source or saved observation is unavailable.';
    requirements.set(id, { id, title: live?.title || saved?.node.title || externalCanvas?.title || id, status, required: true,
      sourceIds: [sourceId], note: note + (wire ? ' The crossing input wire is omitted and remains disconnected.' : '') });
    if (saved && options.includeExternalReferences) {
      const snapshot: SavedSnapshot = structuredClone(saved);
      const pinnedAssets = new Map([snapshot.asset, snapshot.backgroundAsset, ...snapshot.assets ?? []].filter((asset): asset is NonNullable<typeof asset> => !!asset).map(asset => [asset.id, structuredClone(asset)]));
      for (const assetId of nodeAssetIds(snapshot.node)) if (!pinnedAssets.has(assetId)) {
        const asset = project.assets.find(item => item.id === assetId);
        if (asset) pinnedAssets.set(assetId, structuredClone(asset));
        else if (!report.missingAssets.includes(assetId)) report.missingAssets.push(assetId);
      }
      if (snapshot.asset) {
        const originalAsset = snapshot.asset.id;
        snapshot.asset.id = register(originalAsset);
        if (snapshot.node.assetId === originalAsset) snapshot.node.assetId = snapshot.asset.id;
      } else if (snapshot.node.assetId) {
        const asset = project.assets.find(item => item.id === snapshot.node.assetId);
        if (asset) { snapshot.asset = { ...structuredClone(asset), id: register(asset.id) }; snapshot.node.assetId = snapshot.asset.id; }
        else if (!report.missingAssets.includes(snapshot.node.assetId)) report.missingAssets.push(snapshot.node.assetId);
      }
      if (snapshot.backgroundAsset) {
        snapshot.backgroundAsset.id = register(snapshot.backgroundAsset.id);
        snapshot.node.style!.backgroundAssetId = snapshot.backgroundAsset.id;
      } else if (snapshot.node.style?.backgroundAssetId) {
        const asset = project.assets.find(item => item.id === snapshot.node.style!.backgroundAssetId);
        if (asset) { snapshot.backgroundAsset = { ...structuredClone(asset), id: register(asset.id) }; snapshot.node.style.backgroundAssetId = snapshot.backgroundAsset.id; }
        else if (!report.missingAssets.includes(snapshot.node.style.backgroundAssetId)) report.missingAssets.push(snapshot.node.style.backgroundAssetId);
      }
      // Snapshot identity denotes the observed original, and intentionally remains external.
      if (snapshot.node.attachments) snapshot.node.attachments = snapshot.node.attachments.map(id => {
        const asset = project.assets.find(item => item.id === id);
        if (asset) {
          const mapped = register(id);
          if (!copy.assets.some(item => item.id === mapped)) copy.assets.push({ ...structuredClone(asset), id: mapped });
          return mapped;
        }
        if (!report.missingAssets.includes(id)) report.missingAssets.push(id);
        return id;
      });
      const primaryIds = new Set([saved.asset?.id, saved.backgroundAsset?.id]);
      snapshot.assets = [...pinnedAssets.entries()].filter(([originalId]) => !primaryIds.has(originalId)).map(([originalId, asset]) => ({ ...asset, id: register(originalId) }));
      if (!snapshot.assets.length) delete snapshot.assets;
      if (snapshot.node.assetId && idMap[snapshot.node.assetId]) snapshot.node.assetId = idMap[snapshot.node.assetId];
      if (snapshot.node.contentItems) snapshot.node.contentItems = snapshot.node.contentItems.map(item => item.type === 'asset' ? { ...item, id: crypto.randomUUID(), assetId: idMap[item.assetId] ?? item.assetId } : { ...item, id: crypto.randomUUID() });
      copy.snapshots[id] = snapshot;
    }
    // A reference observes its saved revision. Later unsaved declarations on the
    // live original must not replace the requirements recorded in that observation.
    const context = saved?.node ?? live;
    if (context) {
      for (const dependency of context.dependencyIds ?? []) queue.push({ id: dependency, sourceId: context.id, wire: false, externalContext: true });
      if ((context.kind === 'reference' || context.kind === 'canvas-link') && context.targetId) {
        queue.push({ id: context.targetId, sourceId: context.id, wire: false, externalContext: true });
        const cell = context.kind === 'reference' && context.targetCell ? resolveGridCell(project, context.targetId, context.targetCell, { savedOnly: true }) : undefined;
        if (cell) queue.push({ id: cell.id, sourceId: context.id, wire: false, externalContext: true });
      }
      if (live) for (const link of project.links) if (isComputedLink(link.kind) && link.targetId === live.id) queue.push({ id: link.sourceId, sourceId: live.id, wire: true, externalContext: true });
    }
  }
  for (const dependency of project.dependencies) if (touchedCanvases.has(dependency.canvasId)) {
    copy.dependencies.push(structuredClone(dependency));
    requirements.set(dependency.canvasId, { id: dependency.canvasId, title: dependency.title, status: 'missing', required: dependency.required,
      sourceIds: [], note: 'The original Work is not included. Individual saved observations do not contain its operating context.' });
  }
  report.externalRequirements = [...requirements.values()];
  report.includedSnapshots = Object.keys(copy.snapshots).length;
  report.copiedAssets = new Set([...copy.assets.map(asset => asset.id), ...Object.values(copy.snapshots).flatMap(snapshot => [...snapshot.asset ? [snapshot.asset.id] : [], ...snapshot.backgroundAsset ? [snapshot.backgroundAsset.id] : [], ...snapshot.assets?.map(asset => asset.id) ?? []])]).size;
  assertValid(copy);
  return { project: copy, report, idMap };
}
