import type { MeridianNode, MeridianProject } from './types.ts';
import { revealChildren } from './reveal.ts';
import { isProjectShape, validateProject } from './rules.ts';

export interface LibraryEntry {
  id: string; kind: 'section' | 'canvas'; title: string; parentId: string | null;
  projectId?: string; canvasId?: string; collapsed: boolean; x: number; y: number;
  /** Automatic placement is the default. Dragging a tile makes its position authored. */
  placement?: 'near' | 'free';
}
export interface CanvasLibrary { schemaVersion: 1; revision: number; entries: LibraryEntry[] }
/** Home uses the same graph as every editable canvas. Version one exists only for migration. */
export interface LibraryCanvasTarget { projectId: string; canvasId?: string }
export interface LibraryCanvasDocument {
  schemaVersion: 2;
  revision: number;
  project: MeridianProject;
  /** Only launchers into separately saved workspaces need this lookup. Local launchers use targetId. */
  canvasTargets: Record<string, LibraryCanvasTarget>;
  /** Discovery history is independent of nodes, so deleting a launcher does not recreate it on reload. */
  discoveredTargets: string[];
}
export type StoredCanvasLibrary = CanvasLibrary | LibraryCanvasDocument;
export interface LibraryCatalogItem {
  id: string; title: string; revision: number; updatedAt: string; entryCanvasId: string;
  canvases: { id: string; rootId: string; title: string; nodeCount: number }[];
  links: { sourceCanvasId: string; targetCanvasId: string }[];
}
const LIBRARY_ID = 'c9f781c1-124a-433e-a1d6-37dd1d8a7580';
const CANVAS_ID = 'c9f781c1-124a-433e-a1d6-37dd1d8a7581';
const ROOT_ID = 'c9f781c1-124a-433e-a1d6-37dd1d8a7582';
const HEADING_ID = 'c9f781c1-124a-433e-a1d6-37dd1d8a7583';
export const emptyLibrary = (): CanvasLibrary => ({ schemaVersion: 1, revision: 0, entries: [] });

/** Stable, cycle-safe descendants, also used for moves and parent choices. */
export function libraryBranch(library: CanvasLibrary, id: string): Set<string> {
  const children = childGroups(library), result = new Set([id]), queue = [id];
  for (let index = 0; index < queue.length; index++) for (const entry of children.get(queue[index]) ?? [])
    if (!result.has(entry.id)) { result.add(entry.id); queue.push(entry.id); }
  return result;
}

/** Discover saved canvases without rewriting a user's existing titles or hierarchy.
 * A breadth-first spanning tree follows authored canvas launchers; cross-links
 * remain in their source canvases instead of duplicating or cycling the library.
 */
export function reconcileLibrary(source: CanvasLibrary, catalog: LibraryCatalogItem[]): CanvasLibrary {
  const library = structuredClone(source);
  let section = library.entries.find(e => e.kind === 'section' && e.parentId === null);
  if (!section) { section = { id: crypto.randomUUID(), kind: 'section', title: 'My Works', parentId: null, collapsed: false, x: 80, y: 210 }; library.entries.push(section); }
  const originalIds = new Set(source.entries.map(entry => entry.id));
  for (const item of catalog) {
    let root = library.entries.find(e => e.kind === 'canvas' && e.projectId === item.id && (!e.canvasId || e.canvasId === item.entryCanvasId));
    if (!root) { root = { id: crypto.randomUUID(), kind: 'canvas', title: item.title, parentId: section.id, projectId: item.id, canvasId: item.entryCanvasId, collapsed: true, x: 0, y: 0 }; library.entries.push(root); }
    const existing = new Map(library.entries.filter(e => e.projectId === item.id && e.canvasId).map(e => [e.canvasId!, e]));
    existing.set(item.entryCanvasId, root);
    const canvases = new Map(item.canvases.map(c => [c.id, c]));
    const queue = [item.entryCanvasId], seen = new Set(queue), links = new Map<string, string[]>();
    for (const link of item.links) {
      const targets = links.get(link.sourceCanvasId) ?? []; targets.push(link.targetCanvasId); links.set(link.sourceCanvasId, targets);
    }
    const discover = (canvasId: string, parent: LibraryEntry) => {
      const canvas = canvases.get(canvasId); if (!canvas || seen.has(canvasId)) return;
      seen.add(canvasId); queue.push(canvasId);
      if (!existing.has(canvasId)) {
        const entry: LibraryEntry = { id: crypto.randomUUID(), kind: 'canvas', title: canvas.title, parentId: parent.id, projectId: item.id, canvasId, collapsed: true, x: 0, y: 0 };
        library.entries.push(entry); existing.set(canvasId, entry);
      }
    };
    let cursor = 0;
    const traverse = () => {
      while (cursor < queue.length) {
        const id = queue[cursor++];
        for (const target of links.get(id) ?? []) discover(target, existing.get(id) ?? root);
      }
    };
    traverse();
    // Each disconnected component gets one root below the workspace; its own
    // launcher relationships still form a breadth-first tree.
    for (const canvas of item.canvases) { discover(canvas.id, root); traverse(); }
  }
  if (library.entries.length === source.entries.length) return library;
  if (!source.entries.length) return arrangeLibrary(library);
  return placeDiscoveredEntries(library, originalIds);
}

const TILE_WIDTH = 336, TILE_HEIGHT = 180, GAP = 36;

function childGroups(library: CanvasLibrary): Map<string | null, LibraryEntry[]> {
  const groups = new Map<string | null, LibraryEntry[]>();
  for (const entry of library.entries) { const children = groups.get(entry.parentId) ?? []; children.push(entry); groups.set(entry.parentId, children); }
  return groups;
}
function hierarchy(library: CanvasLibrary) {
  const children = childGroups(library), byId = new Map(library.entries.map(entry => [entry.id, entry]));
  if (byId.size !== library.entries.length) throw new Error('Library node identifiers must be unique.');
  for (const entry of library.entries) if (entry.parentId !== null && !byId.has(entry.parentId)) throw new Error('A library parent does not exist.');
  const order = [...(children.get(null) ?? [])];
  for (let index = 0; index < order.length; index++) order.push(...children.get(order[index].id) ?? []);
  if (order.length !== library.entries.length) throw new Error('A library node cannot contain itself.');
  return { children, byId, order };
}

/** Catalog discovery only positions newly created branches. Existing geometry is authored data. */
function placeDiscoveredEntries(library: CanvasLibrary, originalIds: Set<string>): CanvasLibrary {
  const plan = hierarchy(library);
  for (const entry of plan.order) {
    if (originalIds.has(entry.id)) continue;
    const parent = entry.parentId ? plan.byId.get(entry.parentId) : undefined;
    const siblings = plan.children.get(entry.parentId) ?? [];
    entry.x = parent ? parent.x + TILE_WIDTH + GAP : 80;
    entry.y = parent ? parent.y : 210;
    for (const sibling of siblings) {
      if (sibling === entry) break;
      entry.y = Math.max(entry.y, sibling.y + TILE_HEIGHT + GAP);
    }
  }
  return library;
}

/** Only the explicit Arrange action reflows the whole visible canvas. Disclosure never calls it. */
export function arrangeLibrary(source: CanvasLibrary): CanvasLibrary {
  const library = structuredClone(source), plan = hierarchy(library), hidden = new Set<string>();
  let visibleIndex = 0;
  for (const entry of plan.order) {
    const parent = entry.parentId ? plan.byId.get(entry.parentId) : undefined;
    const invisible = parent && (parent.collapsed || hidden.has(parent.id));
    if (invisible) {
      hidden.add(entry.id);
      const siblings = plan.children.get(entry.parentId)!;
      const index = siblings.indexOf(entry);
      entry.x = parent.x + TILE_WIDTH + GAP + (index % 3) * (TILE_WIDTH + GAP);
      entry.y = parent.y + Math.floor(index / 3) * (TILE_HEIGHT + GAP);
    } else {
      entry.x = 80 + (visibleIndex % 3) * (TILE_WIDTH + GAP);
      entry.y = 210 + Math.floor(visibleIndex / 3) * (TILE_HEIGHT + GAP);
      visibleIndex++;
    }
    delete entry.placement;
  }
  return library;
}

/** Revealing a library node uses the same local placement engine as ordinary canvases. */
export function revealLibraryChildren(source: CanvasLibrary, id: string, expanded: boolean, preserveValidPositions = false): CanvasLibrary {
  if (!source.entries.some(entry => entry.id === id)) throw new Error('The library node is missing.');
  let scene = revealChildren(libraryScene(source, []), id, expanded, { preserveValidPositions });
  if (expanded) {
    const children = childGroups(source), pending = [...children.get(id) ?? []];
    for (let index = 0; index < pending.length; index++) {
      const entry = pending[index];
      if (!entry.collapsed && children.has(entry.id)) { scene = revealChildren(scene, entry.id, true, { preserveValidPositions }); pending.push(...children.get(entry.id)!); }
    }
  }
  const nodes = new Map(scene.nodes.map(node => [node.id, node]));
  return { ...structuredClone(source), entries: source.entries.map(entry => {
    const node = nodes.get(entry.id)!;
    return { ...entry, x: node.x, y: node.y, collapsed: !!node.collapsed };
  }) };
}

export function moveLibraryBranch(source: CanvasLibrary, id: string, x: number, y: number): CanvasLibrary {
  if (!Number.isFinite(x) || !Number.isFinite(y)) throw new Error('Node position must be finite.');
  const library = structuredClone(source), entry = library.entries.find(e => e.id === id);
  if (!entry) throw new Error('The library node is missing.');
  const branch = libraryBranch(library, id), dx = x - entry.x, dy = y - entry.y;
  for (const child of library.entries) if (branch.has(child.id)) {
    child.x += dx; child.y += dy;
    if (Math.abs(child.x) > 1e9 || Math.abs(child.y) > 1e9) throw new Error('Node position must stay within the Work coordinate limits.');
  }
  entry.placement = 'free';
  return library;
}

export function reparentLibraryEntry(source: CanvasLibrary, id: string, parentId: string | null): CanvasLibrary {
  const library = structuredClone(source), entry = library.entries.find(e => e.id === id);
  if (!entry || parentId && !library.entries.some(e => e.id === parentId)) throw new Error('Choose an existing library node.');
  if (parentId && libraryBranch(library, id).has(parentId)) throw new Error('A node cannot move inside its own contents.');
  entry.parentId = parentId;
  if (parentId) library.entries.find(e => e.id === parentId)!.collapsed = false;
  return parentId ? revealLibraryChildren(library, parentId, true) : library;
}

/** The library uses Meridian's real scene renderer and containment tree. No logic runs here. */
export function libraryScene(library: CanvasLibrary, catalog: LibraryCatalogItem[]): MeridianProject {
  const plan = hierarchy(library), byProject = new Map(catalog.map(c => [c.id, c]));
  const nodes: MeridianNode[] = [
    { id: ROOT_ID, canvasId: CANVAS_ID, parentId: null, kind: 'canvas', title: 'Work Library', x: 0, y: 0, width: 1200, height: 800 },
    { id: HEADING_ID, canvasId: CANVAS_ID, parentId: ROOT_ID, kind: 'text', title: 'Your Work Library', text: 'Arrange Works into chapters and sections. Expand a node to explore its contents.', x: 80, y: 45, width: 1070, height: 120,
      style: { variant: 'label', titleSize: 38, fontSize: 16, textColor: '#DAE9E4', fontFamily: 'sans' } },
  ];
  for (const entry of library.entries) {
    const item = entry.projectId ? byProject.get(entry.projectId) : undefined;
    const children = plan.children.get(entry.id) ?? [];
    const canvas = item?.canvases.find(c => c.id === (entry.canvasId ?? item.entryCanvasId));
    const text = entry.kind === 'section'
      ? `${children.length} ${children.length === 1 ? 'item' : 'items'} · ${entry.collapsed ? 'Expand to explore' : 'Works and sections'}`
      : item && canvas ? `${canvas.nodeCount} ${canvas.nodeCount === 1 ? 'node' : 'nodes'}${children.length ? ` · ${children.length} child ${children.length === 1 ? 'Work' : 'Works'}` : ''}` : 'Work unavailable · saved location retained';
    // Version-one entries store only fixed tile positions, not enclosure or
    // child dimensions. Their compatibility scene therefore uses exterior
    // disclosure; the version-two migration below creates real enclosures.
    nodes.push({ id: entry.id, canvasId: CANVAS_ID, parentId: entry.parentId ?? ROOT_ID, kind: 'media', title: entry.title,
      collapsed: entry.collapsed, text, x: entry.x, y: entry.y, width: TILE_WIDTH, height: TILE_HEIGHT,
      childLayout: 'spatial', placement: entry.placement ?? 'near',
      style: { fill: entry.kind === 'section' ? '#142421' : '#1C302D', border: entry.kind === 'section' ? '#385449' : '#426258', accent: entry.kind === 'section' ? '#CFB98B' : '#80CDB7', textColor: '#DAE9E4', titleSize: entry.kind === 'section' ? 22 : 18, fontSize: 13, radius: 14 },
    });
  }
  return { schemaVersion: 1, id: LIBRARY_ID, title: 'Work Library', revision: library.revision, entryCanvasId: CANVAS_ID,
    canvases: [{ id: CANVAS_ID, rootId: ROOT_ID, title: 'Work Library', style: { background: '#101B19', grid: '#243832' }, snapping: { stickyEdges: true, snapToGrid: false, smartAlign: true } }],
    nodes, links: library.entries.filter(entry => entry.parentId).map((entry, index) => ({
      id: `library-relationship-${index}`, canvasId: CANVAS_ID, sourceId: entry.parentId!, targetId: entry.id, kind: 'visual', relation: 'generic',
      style: { color: '#557A68', width: 1.5 },
    })), assets: [], snapshots: {}, dependencies: [] };
}

const SAFE_ID = /^[A-Za-z0-9][A-Za-z0-9_-]{0,95}$/;
const targetKey = (target: LibraryCanvasTarget): string => `${target.projectId}:${target.canvasId ?? '*'}`;
const object = (value: unknown): value is Record<string, unknown> => !!value && typeof value === 'object' && !Array.isArray(value);
const identifier = (value: unknown): value is string => typeof value === 'string' && SAFE_ID.test(value);
const revision = (value: unknown): value is number => typeof value === 'number' && Number.isInteger(value) && value >= 0 && value <= 2147483647;

/** A read is never a storage mutation: the first successful ordinary autosave commits the migrated graph. */
export function normalizeLibraryDocument(source: unknown, catalog: LibraryCatalogItem[] = []): LibraryCanvasDocument {
  if (!object(source) || !revision(source.revision)) throw new Error('The saved Work Library is invalid.');
  if (source.schemaVersion === 2) {
    const document = source as unknown as LibraryCanvasDocument;
    validateLibraryCanvasDocument(document);
    const normalized = structuredClone(document);
    for (const node of normalized.project.nodes) {
      const target = normalized.canvasTargets[node.id];
      if (node.kind === 'canvas-link' && !node.targetId && !node.canvasTarget && target)
        node.canvasTarget = structuredClone(target);
    }
    return libraryDocumentFromProject(normalized, normalized.project);
  }
  if (source.schemaVersion !== 1 || !Array.isArray(source.entries) || source.entries.length > 10000)
    throw new Error('The saved Work Library has an unsupported format.');
  for (const item of source.entries) {
    if (!object(item) || !identifier(item.id) || !['section', 'canvas'].includes(item.kind as string)
      || typeof item.title !== 'string' || !item.title.trim() || item.title.length > 256
      || !(item.parentId === null || identifier(item.parentId)) || typeof item.collapsed !== 'boolean'
      || !['x', 'y'].every(key => typeof item[key] === 'number' && Number.isFinite(item[key]) && Math.abs(item[key] as number) <= 1e9)
      || item.placement !== undefined && !['near', 'free'].includes(item.placement as string)
      || item.kind === 'canvas' && (!identifier(item.projectId) || item.canvasId !== undefined && !identifier(item.canvasId)))
      throw new Error('The saved Work Library contains an invalid node.');
  }
  const legacy = source as unknown as CanvasLibrary;
  hierarchy(legacy);
  const used = new Set(legacy.entries.map(entry => entry.id));
  const reserve = (preferred: string): string => {
    let candidate = preferred, suffix = 0;
    while (used.has(candidate)) candidate = `${preferred}-${++suffix}`;
    used.add(candidate); return candidate;
  };
  const projectId = reserve(LIBRARY_ID), canvasId = reserve(CANVAS_ID), rootId = reserve(ROOT_ID), headingId = reserve(HEADING_ID);
  const targets: Record<string, LibraryCanvasTarget> = {};
  const byProject = new Map(catalog.map(item => [item.id, item]));
  const nodes: MeridianNode[] = [
    { id: rootId, canvasId, parentId: null, kind: 'canvas', title: 'Work Library', x: 0, y: 0, width: 1200, height: 800 },
    { id: headingId, canvasId, parentId: rootId, kind: 'text', title: 'Your Work Library', text: 'A Work for your ideas and the Works they connect. Expand any node to explore its contents.', x: 80, y: 45, width: 1070, height: 120,
      style: { variant: 'label', titleSize: 38, fontSize: 16, textColor: '#DAE9E4', fontFamily: 'sans' } },
  ];
  for (const entry of legacy.entries) {
    if (entry.kind === 'canvas') {
      const canvasId = entry.canvasId ?? byProject.get(entry.projectId!)?.entryCanvasId;
      targets[entry.id] = { projectId: entry.projectId!, ...(canvasId ? { canvasId } : {}) };
    }
    nodes.push({ id: entry.id, canvasId, parentId: entry.parentId ?? rootId,
      kind: entry.kind === 'canvas' ? 'canvas-link' : 'container', title: entry.title,
      ...(targets[entry.id] ? { canvasTarget: structuredClone(targets[entry.id]) } : {}),
      collapsed: entry.collapsed, x: entry.x, y: entry.y, width: TILE_WIDTH, height: TILE_HEIGHT,
      childLayout: 'spatial', placement: entry.placement ?? 'near',
      style: { fill: '#1C302D', border: '#426258', accent: '#80CDB7', textColor: '#DAE9E4', titleSize: 18, fontSize: 13, radius: 14 } });
  }
  const document: LibraryCanvasDocument = {
    schemaVersion: 2, revision: legacy.revision,
    project: { schemaVersion: 1, id: projectId, title: 'Work Library', revision: legacy.revision, entryCanvasId: canvasId,
      canvases: [{ id: canvasId, rootId, title: 'Work Library', style: { background: '#101B19', grid: '#243832' }, snapping: { stickyEdges: true, snapToGrid: false, smartAlign: true } }],
      nodes, links: legacy.entries.filter(entry => entry.parentId).map((entry, index) => ({
        id: reserve(`library-relationship-${index}`), canvasId, sourceId: entry.parentId!, targetId: entry.id, parentId: entry.parentId!,
        kind: 'visual', relation: 'generic', direction: 'none', style: { color: '#557A68', width: 1.5 } })),
      assets: [], snapshots: {}, dependencies: [] },
    canvasTargets: targets, discoveredTargets: [...new Set(Object.values(targets).map(targetKey))],
  };
  validateLibraryCanvasDocument(document);
  return document;
}

/** Validate the normal graph and the small external-location envelope before using untrusted storage. */
export function validateLibraryCanvasDocument(document: LibraryCanvasDocument): void {
  if (!object(document) || document.schemaVersion !== 2 || !revision(document.revision)
    || Object.keys(document).some(key => !['schemaVersion', 'revision', 'project', 'canvasTargets', 'discoveredTargets'].includes(key))
    || !isProjectShape(document.project) || document.project.revision !== document.revision
    || document.project.nodes.length > 10002 || document.project.links.length > 20000)
    throw new Error('The saved Work Library contains an invalid Work or revision.');
  const issues = validateProject(document.project);
  if (issues.length) throw new Error(`The saved Work Library is invalid: ${issues[0].message}`);
  if (!object(document.canvasTargets) || Object.keys(document.canvasTargets).length > 10000
    || !Array.isArray(document.discoveredTargets) || document.discoveredTargets.length > 20000
    || new Set(document.discoveredTargets).size !== document.discoveredTargets.length)
    throw new Error('The saved Work Library contains invalid Work locations.');
  const nodes = new Map(document.project.nodes.map(node => [node.id, node]));
  for (const [id, target] of Object.entries(document.canvasTargets)) {
    if (!identifier(id) || nodes.get(id)?.kind !== 'canvas-link' || !object(target) || !identifier(target.projectId)
      || target.canvasId !== undefined && !identifier(target.canvasId)
      || Object.keys(target).some(key => !['projectId', 'canvasId'].includes(key)))
      throw new Error('An external Work location must belong to an existing Work node.');
    const canonical = nodes.get(id)?.canvasTarget;
    if (canonical && (canonical.projectId !== target.projectId || canonical.canvasId !== target.canvasId))
      throw new Error('The Work location must match its Work node destination.');
  }
  for (const key of document.discoveredTargets) {
    if (typeof key !== 'string' || !/^[A-Za-z0-9][A-Za-z0-9_-]{0,95}:(?:[A-Za-z0-9][A-Za-z0-9_-]{0,95}|\*)$/.test(key))
      throw new Error('The saved Work discovery history contains an invalid location.');
  }
}

/** Preserve the envelope while saving edits made by the ordinary canvas editor. */
export function libraryDocumentFromProject(source: LibraryCanvasDocument, project: MeridianProject): LibraryCanvasDocument {
  const result = structuredClone(source);
  result.project = structuredClone(project); result.revision = project.revision;
  const discovered = new Set(result.discoveredTargets);
  for (const target of Object.values(result.canvasTargets)) discovered.add(targetKey(target));
  result.canvasTargets = {};
  for (const node of result.project.nodes) {
    if (node.kind === 'canvas-link' && node.canvasTarget) {
      result.canvasTargets[node.id] = structuredClone(node.canvasTarget);
      discovered.add(targetKey(node.canvasTarget));
    }
  }
  result.discoveredTargets = [...discovered];
  validateLibraryCanvasDocument(result);
  return result;
}

/** Discovery adds only never-seen launchers. Titles, positions, nodes, links and intentional deletions remain authored. */
export function reconcileLibraryCanvas(source: LibraryCanvasDocument, catalog: LibraryCatalogItem[]): LibraryCanvasDocument {
  const result = structuredClone(source), project = result.project;
  const home = project.canvases.find(canvas => canvas.id === project.entryCanvasId)!;
  const discovered = new Set(result.discoveredTargets);
  const root = project.nodes.find(node => node.id === home.rootId)!;
  const occupied = project.nodes.filter(node => node.canvasId === home.id && node.kind !== 'canvas');
  const addLauncher = (item: LibraryCatalogItem, canvas: LibraryCatalogItem['canvases'][number], parent: MeridianNode): MeridianNode | undefined => {
    const target = { projectId: item.id, canvasId: canvas.id }, key = targetKey(target);
    if (discovered.has(key) || canvas.id === item.entryCanvasId && discovered.has(`${item.id}:*`)) return undefined;
    let x = parent === root ? 80 : parent.x + parent.width + GAP, y = parent === root ? 210 : parent.y;
    while (occupied.some(node => x < node.x + node.width + GAP && x + TILE_WIDTH + GAP > node.x && y < node.y + node.height + GAP && y + TILE_HEIGHT + GAP > node.y))
      y += TILE_HEIGHT + GAP;
    const node: MeridianNode = { id: crypto.randomUUID(), canvasId: home.id, parentId: parent.id, kind: 'canvas-link', title: canvas.id === item.entryCanvasId ? item.title : canvas.title,
      tag: canvas.id === item.entryCanvasId ? item.title : canvas.title,
      x, y, width: TILE_WIDTH, height: TILE_HEIGHT, collapsed: true, childLayout: 'spatial', placement: 'near',
      style: { fill: '#1C302D', border: '#426258', accent: '#80CDB7', textColor: '#DAE9E4', titleSize: 18, fontSize: 13, radius: 14 } };
    node.canvasTarget = structuredClone(target);
    project.nodes.push(node); occupied.push(node); result.canvasTargets[node.id] = target; discovered.add(key); return node;
  };
  for (const item of catalog) {
    const canvases = new Map(item.canvases.map(canvas => [canvas.id, canvas]));
    const existing = new Map<string, MeridianNode>();
    for (const [id, target] of Object.entries(result.canvasTargets)) {
      if (target.projectId !== item.id) continue;
      const node = project.nodes.find(node => node.id === id); if (!node) continue;
      const canvasId = target.canvasId ?? item.entryCanvasId;
      existing.set(canvasId, node); discovered.add(targetKey(target)); discovered.add(`${item.id}:${canvasId}`);
      if (!target.canvasId) { target.canvasId = canvasId; node.canvasTarget = structuredClone(target); }
    }
    let launcherRoot = existing.get(item.entryCanvasId);
    const entry = canvases.get(item.entryCanvasId);
    if (!launcherRoot && entry) { launcherRoot = addLauncher(item, entry, root); if (launcherRoot) existing.set(entry.id, launcherRoot); }
    const links = new Map<string, string[]>();
    for (const link of item.links) { const children = links.get(link.sourceCanvasId) ?? []; children.push(link.targetCanvasId); links.set(link.sourceCanvasId, children); }
    const visited = new Set<string>(), queue: string[] = [item.entryCanvasId];
    const traverse = () => {
      for (let index = 0; index < queue.length; index++) {
        const canvasId = queue[index]; if (visited.has(canvasId)) continue; visited.add(canvasId);
        const parent = existing.get(canvasId) ?? launcherRoot ?? root;
        for (const childId of links.get(canvasId) ?? []) {
          if (!canvases.has(childId) || visited.has(childId)) continue;
          if (!existing.has(childId)) { const child = addLauncher(item, canvases.get(childId)!, parent); if (child) existing.set(childId, child); }
          queue.push(childId);
        }
      }
    };
    traverse();
    for (const canvas of item.canvases) {
      if (visited.has(canvas.id)) continue;
      if (!existing.has(canvas.id)) { const node = addLauncher(item, canvas, launcherRoot ?? root); if (node) existing.set(canvas.id, node); }
      queue.push(canvas.id); traverse();
    }
  }
  result.discoveredTargets = [...discovered];
  validateLibraryCanvasDocument(result);
  return result;
}
