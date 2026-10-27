import type { MeridianAsset, MeridianNode, MeridianProject, Point, Rules, SavedSnapshot } from './types.ts';
import { selectionGraph } from './selection.ts';
import { navigationIndex } from './navigation.ts';
import { nodeAssetIds } from './appearance.ts';
import { resolveGridCell } from './cells.ts';
import { collisionFreeTranslation } from './collision.ts';
import { nodeShape } from './geometry.ts';
import { isNodeIdentifier, validateChange, validateProject } from './rules.ts';
import { nodeTag } from './node-tag.ts';

/** Inert, independently captured graph. Copying never changes the source document. */
export interface NodeClipboard {
  format: 'atlas-meridian-node-clipboard'; version: 1;
  sourceProjectId: string;
  /** One storage canvas with original identities; it is never itself pasted. */
  project: MeridianProject;
  rootIds: string[];
  /** Canvas destinations are retained as destinations, rather than duplicated workspaces. */
  canvasDestinations: Record<string, string>;
}
export interface NodePasteOptions { offset?: Point }
export interface NodePasteResult {
  project: MeridianProject; rootIds: string[]; nodeIds: string[]; linkIds: string[];
  idMap: Record<string, string>; offset: Point;
}

function checked(project: MeridianProject): void {
  const issues = validateProject(project);
  if (issues.length) throw new Error(issues.map(issue => issue.message).join(' '));
}
function effectiveRules(node: MeridianNode, nav: ReturnType<typeof navigationIndex>): Rules | undefined {
  const rules: Rules = {};
  for (const key of ['nodeKinds', 'linkKinds', 'operations'] as const) {
    const limits = [node, ...nav.ancestors(node.id)].map(ancestor => ancestor.rules?.[key]).filter((list): list is NonNullable<typeof list> => !!list);
    if (limits.length) (rules as Record<string, string[]>)[key] = limits.reduce<string[]>((allowed, limit) => allowed.filter(value => (limit as readonly string[]).includes(value)), [...limits[0]]);
  }
  return Object.keys(rules).length ? rules : undefined;
}
function freeze<T>(value: T): T {
  if (value && typeof value === 'object' && !Object.isFrozen(value)) {
    for (const item of Object.values(value)) freeze(item);
    Object.freeze(value);
  }
  return value;
}

/** Copy whole selected branches, deduplicating selected descendants and excluding crossing wires. */
export function copyNodes(project: MeridianProject, ids: readonly string[]): NodeClipboard {
  const graph = selectionGraph(project, ids), nav = navigationIndex(project);
  const included = new Set(graph.nodeIds), roots = new Set(graph.rootIds);
  const sourceCanvas = project.canvases.find(canvas => canvas.id === graph.canvasId)!;
  const canvas = { id: sourceCanvas.id, rootId: sourceCanvas.rootId, title: sourceCanvas.title };
  const root: MeridianNode = { id: canvas.rootId, canvasId: canvas.id, parentId: null, kind: 'canvas', title: canvas.title, tag: canvas.title, x: 0, y: 0, width: 1200, height: 800, sizeMode: 'fixed' };
  const nodes = project.nodes.filter(node => included.has(node.id)).map(original => {
    const node = structuredClone(original);
    if (roots.has(node.id)) {
      node.parentId = canvas.rootId; delete node.cell; node.placement = 'free';
      const rules = effectiveRules(original, nav);
      if (rules) node.rules = rules;
    }
    return node;
  });
  const links = project.links.filter(link => included.has(link.sourceId) && included.has(link.targetId)).map(original => {
    const link = structuredClone(original);
    if (link.parentId && !included.has(link.parentId)) link.parentId = canvas.rootId;
    return link;
  });
  // Preserve available saved observations for outside references. The original graph
  // remains external: no boundary links or external live nodes become editable copies.
  const snapshots: Record<string, SavedSnapshot> = Object.create(null);
  const pending = nodes.flatMap(node => [...node.dependencyIds ?? [], ...node.targetId && !included.has(node.targetId) ? [node.targetId] : []]);
  const visited = new Set<string>(), knownCanvases = new Set(project.canvases.map(item => item.id));
  for (const node of nodes) if (node.kind === 'reference' && node.targetId && node.targetCell && !included.has(node.targetId)) {
    const cell = resolveGridCell(project, node.targetId, node.targetCell, { savedOnly: true });
    if (cell) pending.push(cell.id);
  }
  for (let cursor = 0; cursor < pending.length; cursor++) {
    const id = pending[cursor]; if (visited.has(id)) continue; visited.add(id);
    const saved = Object.hasOwn(project.snapshots, id) ? project.snapshots[id] : undefined;
    if (!saved || !nav.nodes.has(id) && knownCanvases.has(saved.node.canvasId)) continue;
    snapshots[id] = structuredClone(saved);
    pending.push(...saved.node.dependencyIds ?? []);
    if (saved.node.targetId) pending.push(saved.node.targetId);
    if (saved.node.kind === 'reference' && saved.node.targetId && saved.node.targetCell) {
      const cell = resolveGridCell(project, saved.node.targetId, saved.node.targetCell, { savedOnly: true }); if (cell) pending.push(cell.id);
    }
  }
  const usedAssets = new Set([...nodes, ...Object.values(snapshots).map(snapshot => snapshot.node)].flatMap(nodeAssetIds));
  const assets = project.assets.filter(asset => usedAssets.has(asset.id)).map(asset => structuredClone(asset));
  for (const snapshot of Object.values(snapshots)) for (const asset of [snapshot.asset, snapshot.backgroundAsset, ...snapshot.assets ?? []]) if (asset && !assets.some(item => item.id === asset.id)) assets.push(structuredClone(asset));
  const touched = new Set(Object.values(snapshots).map(snapshot => snapshot.node.canvasId));
  const captured: MeridianProject = { schemaVersion: 1, id: project.id, title: project.title, revision: project.revision, entryCanvasId: canvas.id,
    canvases: [canvas], nodes: [root, ...nodes], links, assets, snapshots,
    dependencies: project.dependencies.filter(dependency => touched.has(dependency.canvasId)).map(dependency => structuredClone(dependency)) };
  checked(captured);
  const canvasDestinations: Record<string, string> = Object.create(null);
  for (const destination of project.canvases) { canvasDestinations[destination.id] = destination.id; canvasDestinations[destination.rootId] = destination.id; }
  return freeze({ format: 'atlas-meridian-node-clipboard', version: 1, sourceProjectId: project.id, project: captured, rootIds: [...graph.rootIds], canvasDestinations });
}

function assetEqual(a: MeridianAsset, b: MeridianAsset): boolean { return a.name === b.name && a.mime === b.mime && a.base64 === b.base64; }
function validClipboard(clipboard: NodeClipboard): void {
  if (!clipboard || clipboard.format !== 'atlas-meridian-node-clipboard' || clipboard.version !== 1 || !isNodeIdentifier(clipboard.sourceProjectId)) throw new Error('Clipboard does not contain Meridian nodes.');
  checked(clipboard.project);
  if (clipboard.sourceProjectId !== clipboard.project.id) throw new Error('Clipboard source identity is malformed.');
  if (clipboard.project.canvases.length !== 1 || !Array.isArray(clipboard.rootIds) || !clipboard.rootIds.length || new Set(clipboard.rootIds).size !== clipboard.rootIds.length) throw new Error('Clipboard selection is malformed.');
  const canvasRoot = clipboard.project.canvases[0].rootId;
  const roots = clipboard.project.nodes.filter(node => node.parentId === canvasRoot);
  if (roots.length !== clipboard.rootIds.length || roots.some(node => !clipboard.rootIds.includes(node.id))) throw new Error('Clipboard selection is malformed.');
  if (!clipboard.canvasDestinations || typeof clipboard.canvasDestinations !== 'object' || Array.isArray(clipboard.canvasDestinations) || Object.entries(clipboard.canvasDestinations).some(([id, canvasId]) => !isNodeIdentifier(id) || !isNodeIdentifier(canvasId))) throw new Error('Clipboard Work destinations are malformed.');
}

/** Paste one independent graph beside the captured original; document validation is atomic. */
export function pasteNodes(project: MeridianProject, clipboard: NodeClipboard, canvasId: string, options: NodePasteOptions = {}): NodePasteResult {
  checked(project); validClipboard(clipboard);
  const proposed = options.offset ?? { x: 32, y: 32 };
  if (!proposed || !Number.isFinite(proposed.x) || !Number.isFinite(proposed.y)) throw new Error('Paste offset must be finite.');
  const candidate = structuredClone(project), canvas = candidate.canvases.find(canvas => canvas.id === canvasId);
  if (!canvas) throw new Error('The destination Work does not exist.');
  const source = clipboard.project, included = new Set(source.nodes.filter(node => node.kind !== 'canvas').map(node => node.id));
  const roots = new Set(clipboard.rootIds), idMap: Record<string, string> = Object.create(null);
  const used = new Set([candidate.id, ...candidate.nodes, ...candidate.canvases, ...candidate.links, ...candidate.assets].map(item => typeof item === 'string' ? item : item.id));
  for (const node of candidate.nodes) for (const link of node.remoteLinks ?? []) used.add(link.id);
  for (const id of Object.keys(candidate.snapshots)) used.add(id);
  // Fresh graph identities must differ from their originals even when the
  // clipboard is pasted into another document that does not contain them.
  for (const node of source.nodes) {
    used.add(node.id); for (const link of node.remoteLinks ?? []) used.add(link.id);
  }
  for (const link of source.links) used.add(link.id);
  const fresh = (original: string): string => {
    if (Object.hasOwn(idMap, original)) return idMap[original];
    let id: string; do { id = crypto.randomUUID(); } while (used.has(id));
    used.add(id); return idMap[original] = id;
  };
  for (const node of source.nodes) if (included.has(node.id)) {
    fresh(node.id); for (const link of node.remoteLinks ?? []) fresh(link.id);
  }
  for (const link of source.links) fresh(link.id);
  const assetMap: Record<string, string> = Object.create(null);
  for (const asset of source.assets) {
    const existing = candidate.assets.find(item => item.id === asset.id);
    if (existing && assetEqual(existing, asset)) assetMap[asset.id] = asset.id;
    else {
      const id = used.has(asset.id) ? fresh(asset.id) : asset.id;
      used.add(id); assetMap[asset.id] = id; candidate.assets.push({ ...structuredClone(asset), id });
    }
  }
  const mapNode = (id: string) => included.has(id) ? idMap[id] : id;
  const mapAsset = (id: string) => assetMap[id] ?? id;
  const remapAssets = (node: MeridianNode): void => {
    if (node.assetId) node.assetId = mapAsset(node.assetId);
    if (node.contentItems) node.contentItems = node.contentItems.map(item => item.type === 'asset' ? { ...item, id: crypto.randomUUID(), assetId: mapAsset(item.assetId) } : { ...item, id: crypto.randomUUID() });
    if (node.style?.backgroundAssetId) node.style.backgroundAssetId = mapAsset(node.style.backgroundAssetId);
    if (node.attachments) node.attachments = node.attachments.map(mapAsset);
  };
  const inserted = source.nodes.filter(node => included.has(node.id)).map(original => {
    const node = structuredClone(original); node.tag = nodeTag(original); node.id = idMap[original.id]; node.canvasId = canvas.id;
    node.parentId = roots.has(original.id) ? canvas.rootId : mapNode(original.parentId!);
    if (roots.has(original.id)) { delete node.cell; node.placement = 'free'; }
    if (node.targetId) {
      if (node.kind === 'canvas-link' && clipboard.sourceProjectId !== candidate.id && Object.hasOwn(clipboard.canvasDestinations, node.targetId)) {
        node.canvasTarget = { projectId: clipboard.sourceProjectId, canvasId: clipboard.canvasDestinations[node.targetId] }; delete node.targetId;
      } else node.targetId = mapNode(node.targetId);
    }
    if (node.dependencyIds) node.dependencyIds = node.dependencyIds.map(mapNode);
    if (node.remoteLinks) node.remoteLinks = node.remoteLinks.map(link => ({ ...link, id: idMap[link.id], targetId: mapNode(link.targetId) }));
    remapAssets(node); return node;
  });
  const insertedLinks = source.links.map(original => ({ ...structuredClone(original), id: idMap[original.id], canvasId: canvas.id,
    sourceId: idMap[original.sourceId], targetId: idMap[original.targetId],
    ...(original.parentId ? { parentId: included.has(original.parentId) ? idMap[original.parentId] : canvas.rootId } : {}) }));
  for (const [id, captured] of Object.entries(source.snapshots)) {
    // Existing destination observations remain authoritative. Same-project paste
    // must never resurrect a locally deleted original from an old clipboard.
    const live = candidate.nodes.find(node => node.id === id);
    if (clipboard.sourceProjectId !== candidate.id && live && live.canvasId !== captured.node.canvasId) throw new Error('A copied external reference conflicts with a destination node GUID.');
    if (Object.hasOwn(candidate.snapshots, id) || live) continue;
    if (candidate.canvases.some(canvas => canvas.id === captured.node.canvasId)) continue;
    const snapshot = structuredClone(captured); remapAssets(snapshot.node);
    if (snapshot.asset) snapshot.asset.id = mapAsset(snapshot.asset.id);
    if (snapshot.backgroundAsset) snapshot.backgroundAsset.id = mapAsset(snapshot.backgroundAsset.id);
    if (snapshot.assets) snapshot.assets = snapshot.assets.map(asset => ({ ...asset, id: mapAsset(asset.id) }));
    candidate.snapshots[id] = snapshot;
  }
  for (const dependency of source.dependencies) if (!candidate.dependencies.some(item => item.canvasId === dependency.canvasId)) candidate.dependencies.push(structuredClone(dependency));
  candidate.nodes.push(...inserted); candidate.links.push(...insertedLinks);
  const nav = navigationIndex(candidate), insertedIds = new Set(inserted.map(node => node.id));
  const physical = (node: MeridianNode) => nav.isNodeVisible(node.id) && !nav.ancestors(node.id).some(parent => parent.containerVariant === 'window');
  const moving = inserted.filter(physical).map(node => nodeShape(node, nav.bounds(node)));
  const obstacles = candidate.nodes.filter(node => node.canvasId === canvas.id && node.kind !== 'canvas' && !insertedIds.has(node.id) && physical(node)).map(node => nodeShape(node, nav.bounds(node)));
  const offset = collisionFreeTranslation(moving, obstacles, proposed);
  if (!Number.isFinite(offset.x) || !Number.isFinite(offset.y)) throw new Error('No finite clear paste position is available.');
  for (const node of inserted) { node.x += offset.x; node.y += offset.y; }
  for (const link of insertedLinks) if (link.points) link.points = link.points.map(point => ({ x: point.x + offset.x, y: point.y + offset.y }));
  const issues = validateChange(project, candidate);
  if (issues.length) throw new Error(issues.map(issue => issue.message).join(' '));
  return { project: candidate, rootIds: clipboard.rootIds.map(id => idMap[id]), nodeIds: inserted.map(node => node.id), linkIds: insertedLinks.map(link => link.id), idMap, offset };
}
