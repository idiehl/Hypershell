import type { MeridianProject as Project, MeridianCanvas as Canvas, MeridianNode as Node, NodeKind, SavedSnapshot } from './types.ts';
import { evaluate } from './engine.ts';
import { nodeTag } from './node-tag.ts';
import { activeNodeContent } from './content.ts';
import { defaultNewNodeStyle, nodeAssetIds } from './appearance.ts';

const id = (): string => crypto.randomUUID();

export function newProject(title = 'Untitled project'): Project {
  const project: Project = {
    schemaVersion: 1, id: id(), title, revision: 0, entryCanvasId: '',
    canvases: [], nodes: [], links: [], assets: [], snapshots: {}, dependencies: [],
  };
  addCanvas(project, 'Workspace');
  return project;
}

export function addCanvas(project: Project, title: string): Canvas {
  const canvas: Canvas = { id: id(), rootId: id(), title };
  project.canvases.push(canvas);
  project.nodes.push({
    id: canvas.rootId, canvasId: canvas.id, parentId: null, kind: 'canvas', title, tag: title,
    x: 0, y: 0, width: 1200, height: 800, sizeMode: 'fixed',
  });
  if (!project.entryCanvasId) project.entryCanvasId = canvas.id;
  return canvas;
}

export function addNode(project: Project, canvasId: string, kind: NodeKind, title?: string): Node {
  const canvas = project.canvases.find(item => item.id === canvasId);
  if (!canvas) throw new Error('Cannot add a node to a missing Work.');
  if (kind === 'canvas') throw new Error('Create Work roots with addCanvas.');
  const node: Node = {
    id: id(), canvasId, parentId: canvas.rootId, kind,
    title: title ?? (kind === 'block' ? '' : kind === 'canvas-link' ? 'Work' : kind.charAt(0).toUpperCase() + kind.slice(1)),
    x: 100, y: 100, width: kind === 'container' || kind === 'module' ? 480 : 200,
    height: kind === 'container' || kind === 'module' ? 300 : 120,
    sizeMode: kind === 'joint' || kind === 'junction' ? 'fixed' : 'auto',
    ...(!['joint', 'junction', 'block'].includes(kind) ? { style: defaultNewNodeStyle() } : {}),
  };
  if (kind === 'input') { node.value = false; node.exposed = true; }
  if (kind === 'number') { node.value = 0; node.exposed = true; }
  if (kind === 'switch') { node.closed = true; node.exposed = true; }
  if (kind === 'joint') node.operation = 'AND';
  if (kind === 'junction') { node.width = 44; node.height = 44; }
  if (kind === 'text') node.text = '';
  if (kind === 'media') { node.contentType = 'text'; node.text = ''; node.containerVariant = 'contained'; node.childLayout = 'spatial'; node.childArrangement = 'radial'; }
  if (kind === 'block') { node.sizeMode = 'fixed'; node.containerVariant = 'contained'; node.childLayout = 'spatial'; node.childArrangement = 'radial'; }
  if (kind === 'container' || kind === 'module') { node.containerVariant = 'contained'; node.childLayout = 'contained'; node.childArrangement = 'free'; }
  if (kind === 'spreadsheet') {
    node.containerVariant = 'spreadsheet';
    node.grid = { columns: 3, rows: 3, cellWidth: 200, cellHeight: 100, gap: 0, showHeaders: true };
    node.childLayout = 'contained'; node.childArrangement = 'grid'; node.collapsed = false;
    node.width = 666; node.height = 402;
  }
  node.tag = nodeTag(node);
  project.nodes.push(node);
  return node;
}

export function cloneProject(project: Project): Project {
  return structuredClone(project);
}

/** A fork owns its entire included graph. Unresolved external IDs remain external. */
export function forkProject(project: Project): Project {
  const fork = cloneProject(project);
  const ids = new Map<string, string>();
  const register = (old: string): void => { if (!ids.has(old)) ids.set(old, id()); };
  register(project.id);
  for (const entity of [...project.canvases, ...project.nodes, ...project.links, ...project.assets]) register(entity.id);
  for (const node of project.nodes) for (const link of node.remoteLinks ?? []) register(link.id);
  for (const [key, snapshot] of Object.entries(project.snapshots)) {
    register(key); register(snapshot.node.id);
    for (const link of snapshot.node.remoteLinks ?? []) register(link.id);
    if (snapshot.asset) register(snapshot.asset.id);
    if (snapshot.backgroundAsset) register(snapshot.backgroundAsset.id);
    for (const asset of snapshot.assets ?? []) register(asset.id);
  }
  const remap = (old: string): string => ids.get(old) ?? old;
  const localNodes = new Set(project.nodes.map(node => node.id));
  const remapNode = (node: Node): void => {
    node.id = remap(node.id); node.canvasId = remap(node.canvasId);
    if (node.parentId) node.parentId = remap(node.parentId);
    if (node.targetId) node.targetId = remap(node.targetId);
    if (node.assetId) node.assetId = remap(node.assetId);
    if (node.style?.backgroundAssetId) node.style.backgroundAssetId = remap(node.style.backgroundAssetId);
    if (node.attachments) node.attachments = node.attachments.map(remap);
    if (node.contentItems) node.contentItems = node.contentItems.map(item => item.type === 'asset' ? { ...item, id: id(), assetId: remap(item.assetId) } : { ...item, id: id() });
    if (node.dependencyIds) node.dependencyIds = node.dependencyIds.map(remap);
    if (node.remoteLinks) node.remoteLinks = node.remoteLinks.map(link => ({ ...link, id: remap(link.id), targetId: localNodes.has(link.targetId) ? remap(link.targetId) : link.targetId }));
  };
  fork.id = remap(fork.id); fork.revision = 0; fork.entryCanvasId = remap(fork.entryCanvasId);
  for (const canvas of fork.canvases) { canvas.id = remap(canvas.id); canvas.rootId = remap(canvas.rootId); if (canvas.style?.backgroundAssetId) canvas.style.backgroundAssetId = remap(canvas.style.backgroundAssetId); }
  for (const node of fork.nodes) remapNode(node);
  for (const link of fork.links) {
    link.id = remap(link.id); link.canvasId = remap(link.canvasId);
    link.sourceId = remap(link.sourceId); link.targetId = remap(link.targetId);
    if (link.parentId) link.parentId = remap(link.parentId);
  }
  for (const asset of fork.assets) asset.id = remap(asset.id);
  const snapshots: Record<string, SavedSnapshot> = Object.create(null);
  for (const [key, snapshot] of Object.entries(fork.snapshots)) {
    remapNode(snapshot.node);
    if (snapshot.asset) snapshot.asset.id = remap(snapshot.asset.id);
    if (snapshot.backgroundAsset) snapshot.backgroundAsset.id = remap(snapshot.backgroundAsset.id);
    for (const asset of snapshot.assets ?? []) asset.id = remap(asset.id);
    snapshots[remap(key)] = snapshot;
  }
  fork.snapshots = snapshots;
  for (const dependency of fork.dependencies) dependency.canvasId = remap(dependency.canvasId);
  return fork;
}

/** Capture an atomic saved revision; the caller controls revision advancement. */
export function commitSnapshots(project: Project): Project {
  const saved = cloneProject(project);
  const evaluation = evaluate(project);
  const savedAt = new Date().toISOString();
  const assetsById = new Map(project.assets.map(asset => [asset.id, asset]));
  saved.snapshots = Object.assign(Object.create(null), saved.snapshots);
  // Preserve intentionally included external snapshots, but remove deleted local originals.
  const localCanvases = new Set(project.canvases.map(canvas => canvas.id));
  const currentNodes = new Set(project.nodes.map(node => node.id));
  for (const [key, snapshot] of Object.entries(saved.snapshots)) {
    if (localCanvases.has(snapshot.node.canvasId) && !currentNodes.has(key)) delete saved.snapshots[key];
  }
  for (const node of project.nodes) {
    const activeAssetId = node.contentItems !== undefined ? activeNodeContent(node).assetId : node.assetId;
    const asset = activeAssetId ? assetsById.get(activeAssetId) : undefined;
    const backgroundAsset = node.style?.backgroundAssetId ? assetsById.get(node.style.backgroundAssetId) : undefined;
    // Preserve old snapshot envelopes until an explicit Hub edit. Canonical Hub
    // stacks capture secondary media so saved external references stay complete.
    const assets = node.contentItems !== undefined ? [...new Set(nodeAssetIds(node))].filter(id => id !== asset?.id && id !== backgroundAsset?.id).flatMap(id => { const item = assetsById.get(id); return item ? [item] : []; }) : [];
    saved.snapshots[node.id] = {
      node: structuredClone(node), value: evaluation.values[node.id] ?? null,
      ...(asset ? { asset: structuredClone(asset) } : {}), ...(backgroundAsset ? { backgroundAsset: structuredClone(backgroundAsset) } : {}), ...(assets.length ? { assets: structuredClone(assets) } : {}), savedAt,
    };
  }
  return saved;
}
