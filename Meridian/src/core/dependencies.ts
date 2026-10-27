import type { MeridianNode, MeridianProject } from './types.ts';
import { isProjectShape } from './rules.ts';
import { isComputedLink } from './operations.ts';
import { resolveGridCell, resolveReferenceTarget } from './cells.ts';
import { nodeTag } from './node-tag.ts';

export type DependencyStatus = 'local' | 'snapshot' | 'missing' | 'excluded';
export type DependencyReason = 'declared' | 'reference' | 'functional' | 'canvas';
export interface DependencyItem {
  id: string; title: string; status: DependencyStatus; reasons: DependencyReason[];
  sourceIds: string[]; required: boolean; canvasId?: string; note?: string;
}
export interface DependencySummary {
  entries: DependencyItem[]; local: DependencyItem[]; snapshots: DependencyItem[];
  missing: DependencyItem[]; excluded: DependencyItem[];
}
export interface DependencyOptions { rootIds?: string[]; includeExternalReferences?: boolean }

/** Explain actual available context. Declarations do not fetch, clone or create graph edges. */
export function summarizeDependencies(project: MeridianProject, options: DependencyOptions = {}): DependencySummary {
  if (!isProjectShape(project)) throw new Error('Cannot inspect malformed Work data.');
  const nodes = new Map(project.nodes.map(node => [node.id, node]));
  const canvases = new Map(project.canvases.map(canvas => [canvas.id, canvas]));
  const includeExternal = options.includeExternalReferences ?? true;
  const entries = new Map<string, DependencyItem>();
  const children = new Map<string, string[]>(), incoming = new Map<string, string[]>();
  for (const node of project.nodes) if (node.parentId) {
    const list = children.get(node.parentId) ?? []; list.push(node.id); children.set(node.parentId, list);
  }
  for (const link of project.links) if (isComputedLink(link.kind)) {
    const list = incoming.get(link.targetId) ?? []; list.push(link.sourceId); incoming.set(link.targetId, list);
  }
  const queue: { node: MeridianNode; snapshot: boolean }[] = [];
  const scheduled = new Set<string>();
  const schedule = (node: MeridianNode, snapshot: boolean): void => {
    const key = `${snapshot ? 'saved' : 'live'}:${node.id}`;
    if (!scheduled.has(key)) { scheduled.add(key); queue.push({ node, snapshot }); }
  };
  const snapshotFor = (id: string) => Object.hasOwn(project.snapshots, id) ? project.snapshots[id] : undefined;
  const rank: Record<DependencyStatus, number> = { local: 0, snapshot: 1, excluded: 2, missing: 3 };
  const record = (item: DependencyItem): void => {
    const old = entries.get(item.id);
    if (!old) { entries.set(item.id, item); return; }
    old.required ||= item.required;
    old.reasons = [...new Set([...old.reasons, ...item.reasons])];
    old.sourceIds = [...new Set([...old.sourceIds, ...item.sourceIds])];
    if (rank[item.status] > rank[old.status]) { old.status = item.status; old.note = item.note; }
  };
  const require = (id: string, source: MeridianNode, reason: DependencyReason, savedSource: boolean): void => {
    const live = nodes.get(id), rawSaved = snapshotFor(id), canvas = canvases.get(id);
    // A removed local original must not be resurrected from an obsolete snapshot.
    const saved = rawSaved && (live || !canvases.has(rawSaved.node.canvasId)) ? rawSaved : undefined;
    const pinned = reason === 'reference' && source.kind === 'reference' && (savedSource || live?.canvasId !== source.canvasId);
    let status: DependencyStatus, note: string | undefined;
    if (canvas) status = 'local';
    else if (live && !pinned) status = 'local';
    else if (saved && (includeExternal || live)) {
      status = live ? 'local' : 'snapshot';
      note = live ? 'Uses the saved snapshot from another included Work.' : 'Saved snapshot only; the original Work and its wiring are not included.';
    } else if (saved && !includeExternal) { status = 'excluded'; note = 'The available external snapshot is excluded from this publication.'; }
    else { status = 'missing'; note = live && pinned ? 'The original is present, but this reference across Works needs a saved snapshot.' : 'Required node or saved context is unavailable.'; }
    record({ id, title: live ? nodeTag(live) : saved ? nodeTag(saved.node) : canvas?.title || id, status, reasons: [reason], sourceIds: [source.id], required: true,
      ...(live?.canvasId || saved?.node.canvasId ? { canvasId: live?.canvasId ?? saved?.node.canvasId } : {}), ...(note ? { note } : {}) });
    if (status === 'local' && live) schedule(pinned && saved ? saved.node : live, pinned && !!saved);
    else if ((status === 'snapshot' || status === 'excluded') && saved) schedule(saved.node, true);
  };
  const roots = options.rootIds ?? project.nodes.map(node => node.id);
  const scope = new Set<string>(), rootQueue = [...roots];
  for (let cursor = 0; cursor < rootQueue.length; cursor++) {
    const id = rootQueue[cursor];
    if (scope.has(id)) continue;
    scope.add(id);
    const node = nodes.get(id);
    if (!node) throw new Error(`The dependency inspection root does not exist: ${id}`);
    schedule(node, false); rootQueue.push(...children.get(id) ?? []);
  }
  for (let cursor = 0; cursor < queue.length; cursor++) {
    const { node, snapshot } = queue[cursor];
    for (const id of node.dependencyIds ?? []) require(id, node, 'declared', snapshot);
    if ((node.kind === 'reference' || node.kind === 'canvas-link') && node.targetId) {
      require(node.targetId, node, 'reference', snapshot);
      if (node.kind === 'reference' && node.targetCell) {
        const cell = snapshot ? resolveGridCell(project, node.targetId, node.targetCell, { savedOnly: true }) : resolveReferenceTarget(project, node)?.node;
        if (cell) require(cell.id, node, 'reference', snapshot);
        else record({ id: `${node.targetId}!${node.targetCell}`, title: `${nodeTag(node)} · ${node.targetCell}`, status: 'missing', reasons: ['reference'], sourceIds: [node.id], required: true, note: 'The addressed cell is empty or its saved observation is unavailable.' });
      }
    }
    if (!snapshot) for (const id of incoming.get(node.id) ?? []) require(id, node, 'functional', false);
  }
  const touchedCanvases = new Set([...entries.values()].map(item => item.canvasId));
  for (const dependency of project.dependencies) {
    if (options.rootIds && !touchedCanvases.has(dependency.canvasId)) continue;
    const local = canvases.has(dependency.canvasId);
    const partial = Object.values(project.snapshots).some(snapshot => snapshot.node.canvasId === dependency.canvasId);
    record({ id: dependency.canvasId, title: dependency.title || dependency.canvasId, status: local ? 'local' : 'missing', reasons: ['canvas'], sourceIds: [], required: dependency.required,
      canvasId: dependency.canvasId, ...!local ? { note: partial ? 'Individual saved nodes are available; they do not include this required Work.' : 'This external Work is not included.' } : {} });
  }
  const result = [...entries.values()];
  return { entries: result, local: result.filter(item => item.status === 'local'), snapshots: result.filter(item => item.status === 'snapshot'), missing: result.filter(item => item.status === 'missing'), excluded: result.filter(item => item.status === 'excluded') };
}
