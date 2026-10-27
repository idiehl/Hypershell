import type { MeridianProject } from './types.ts';

/** A canvas root occupies the same tier as the launcher that opens it. */
export interface CanvasTierAnchor { projectId: string; canvasId?: string; tier: number }
export interface CanvasTierContext { version: 1; anchors: CanvasTierAnchor[] }
export interface CanvasTierLocation { projectId: string; canvasId?: string }
export interface CanvasTierOptions {
  home: CanvasTierLocation;
  /** Navigation metadata is a fallback; the current library hierarchy always wins. */
  anchors?: readonly CanvasTierAnchor[];
}
export interface CanvasTierIndex {
  canvasTier(projectId: string, canvasId: string): number | undefined;
  nodeTier(projectId: string, nodeId: string): number | undefined;
  /** Excludes disconnected fallback roots, which have no known library ancestry. */
  anchors(): CanvasTierAnchor[];
}

const MAX_ANCHORS = 512;
const MAX_TIER = 1_000_000;
const identifier = (value: unknown): value is string => typeof value === 'string' && /^[A-Za-z0-9][A-Za-z0-9_-]{0,95}$/.test(value);
const validTier = (value: unknown): value is number => typeof value === 'number' && Number.isSafeInteger(value) && value >= 1 && value <= MAX_TIER;
const key = (projectId: string, id: string) => JSON.stringify([projectId, id]);

/** Saved context is inert, bounded view metadata, never a document field. */
export function normalizeTierContext(value: unknown): CanvasTierContext {
  const empty: CanvasTierContext = { version: 1, anchors: [] };
  if (!value || typeof value !== 'object' || Array.isArray(value)) return empty;
  const context = value as Record<string, unknown>;
  if (context.version !== 1 || !Array.isArray(context.anchors) || context.anchors.length > MAX_ANCHORS) return empty;
  const anchors = new Map<string, CanvasTierAnchor>();
  for (const candidate of context.anchors) {
    if (!candidate || typeof candidate !== 'object' || Array.isArray(candidate)) continue;
    const anchor = candidate as Record<string, unknown>;
    if (!identifier(anchor.projectId) || anchor.canvasId !== undefined && !identifier(anchor.canvasId) || !validTier(anchor.tier)) continue;
    const normalized: CanvasTierAnchor = { projectId: anchor.projectId, tier: anchor.tier };
    if (anchor.canvasId !== undefined) normalized.canvasId = anchor.canvasId as string;
    const anchorKey = key(normalized.projectId, normalized.canvasId ?? '');
    const existing = anchors.get(anchorKey);
    if (!existing || normalized.tier < existing.tier) anchors.set(anchorKey, normalized);
  }
  return { version: 1, anchors: [...anchors.values()] };
}

export function parseTierContext(value: string | null | undefined): CanvasTierContext {
  if (!value || value.length > 131_072) return { version: 1, anchors: [] };
  try { return normalizeTierContext(JSON.parse(value)); } catch { return { version: 1, anchors: [] }; }
}

/** Preserve the shallowest observed ancestry, including cycles through unloaded workspaces. */
export function rememberCanvasTier(context: unknown, anchor: CanvasTierAnchor): CanvasTierContext {
  const saved = normalizeTierContext(context);
  const normalized = normalizeTierContext({ version: 1, anchors: [anchor] }).anchors[0];
  if (!normalized) return saved;
  const existing = saved.anchors.find(item => item.projectId === normalized.projectId && item.canvasId === normalized.canvasId);
  const filtered = saved.anchors.filter(item => item !== existing);
  return { version: 1, anchors: [...filtered.slice(-(MAX_ANCHORS - 1)), { ...normalized, tier: Math.min(existing?.tier ?? Infinity, normalized.tier) }] };
}

interface Edge { target: string; depth: number }
interface CanvasInfo { projectId: string; canvasId: string; key: string; depths: Map<string, number>; edges: Edge[] }

/**
 * Library-wide, cycle-safe tiers. Containment contributes one tier per generation;
 * crossing a canvas launcher contributes no extra generation. Multiple launchers
 * use the shallowest route from the library so Back/Forward and cycles stay stable.
 * Only supplied projects are inspected; this function performs no I/O or writes.
 */
export function buildTierIndex(sources: readonly MeridianProject[], options: CanvasTierOptions): CanvasTierIndex {
  const projects = new Map(sources.map(project => [project.id, project]));
  const canvases = new Map<string, CanvasInfo>();
  for (const project of projects.values()) {
    const byId = new Map(project.nodes.map(node => [node.id, node]));
    const children = new Map<string, string[]>();
    for (const node of project.nodes) if (node.parentId && byId.get(node.parentId)?.canvasId === node.canvasId) {
      const group = children.get(node.parentId) ?? []; group.push(node.id); children.set(node.parentId, group);
    }
    for (const canvas of project.canvases) {
      const root = byId.get(canvas.rootId), depths = new Map<string, number>();
      if (root?.canvasId === canvas.id) {
        depths.set(root.id, 0);
        const queue = [root.id];
        for (let index = 0; index < queue.length; index++) {
          const parent = queue[index];
          for (const id of children.get(parent) ?? []) if (!depths.has(id)) {
            depths.set(id, depths.get(parent)! + 1); queue.push(id);
          }
        }
      }
      const canvasKey = key(project.id, canvas.id);
      canvases.set(canvasKey, { projectId: project.id, canvasId: canvas.id, key: canvasKey, depths, edges: [] });
    }
    const localTargets = new Map(project.canvases.flatMap(canvas => [[canvas.id, canvas.id], [canvas.rootId, canvas.id]] as const));
    for (const node of project.nodes) {
      if (node.kind !== 'canvas-link') continue;
      const source = canvases.get(key(project.id, node.canvasId)), depth = source?.depths.get(node.id);
      if (!source || depth === undefined) continue;
      const external = node.canvasTarget;
      const targetProject = external ? projects.get(external.projectId) : project;
      const targetCanvas = external ? external.canvasId ?? targetProject?.entryCanvasId : node.targetId ? localTargets.get(node.targetId) : undefined;
      if (targetProject && targetCanvas && targetProject.canvases.some(canvas => canvas.id === targetCanvas))
        source.edges.push({ target: key(targetProject.id, targetCanvas), depth });
    }
  }

  const distances = new Map<string, number>();
  const provenance = new Set<string>();
  const locationKey = (location: CanvasTierLocation): string | undefined => {
    const project = projects.get(location.projectId), canvasId = location.canvasId ?? project?.entryCanvasId;
    const value = canvasId ? key(location.projectId, canvasId) : undefined;
    return value && canvases.has(value) ? value : undefined;
  };
  // Binary heap keeps deeply nested libraries inexpensive without recursive walks.
  const propagate = (seeds: Array<{ key: string; tier: number }>, immutable: ReadonlySet<string>): void => {
    const heap: Array<{ key: string; tier: number }> = [];
    const push = (value: { key: string; tier: number }) => {
      heap.push(value); let at = heap.length - 1;
      while (at > 0) { const parent = (at - 1) >> 1; if (heap[parent].tier <= value.tier) break; heap[at] = heap[parent]; at = parent; }
      heap[at] = value;
    };
    const pop = () => {
      const first = heap[0], last = heap.pop()!;
      if (heap.length) {
        let at = 0;
        while (at * 2 + 1 < heap.length) {
          let child = at * 2 + 1; if (child + 1 < heap.length && heap[child + 1].tier < heap[child].tier) child++;
          if (heap[child].tier >= last.tier) break; heap[at] = heap[child]; at = child;
        }
        heap[at] = last;
      }
      return first;
    };
    for (const seed of seeds) {
      if (immutable.has(seed.key) || !validTier(seed.tier) || (distances.get(seed.key) ?? Infinity) <= seed.tier) continue;
      distances.set(seed.key, seed.tier); push(seed);
    }
    while (heap.length) {
      const current = pop(); if (distances.get(current.key) !== current.tier) continue;
      for (const edge of canvases.get(current.key)?.edges ?? []) {
        const tier = current.tier + edge.depth;
        if (immutable.has(edge.target) || !validTier(tier) || (distances.get(edge.target) ?? Infinity) <= tier) continue;
        distances.set(edge.target, tier); push({ key: edge.target, tier });
      }
    }
  };
  const home = locationKey(options.home);
  if (home) propagate([{ key: home, tier: 1 }], new Set());
  const libraryReachable = new Set(distances.keys());
  const anchors = normalizeTierContext({ version: 1, anchors: options.anchors ? [...options.anchors].slice(-MAX_ANCHORS) : [] }).anchors;
  propagate(anchors.flatMap(anchor => { const anchorKey = locationKey(anchor); return anchorKey ? [{ key: anchorKey, tier: anchor.tier }] : []; }), libraryReachable);
  for (const canvasKey of distances.keys()) provenance.add(canvasKey);
  // A directly opened, unlisted workspace still has honest local generations.
  // Seed its entry first so internal launchers establish ancestry before orphans.
  const known = new Set(distances.keys());
  for (const project of projects.values()) {
    const entry = locationKey({ projectId: project.id });
    if (entry && !distances.has(entry)) propagate([{ key: entry, tier: 1 }], known);
  }
  const rooted = new Set(distances.keys());
  for (const canvas of canvases.values()) if (!distances.has(canvas.key)) propagate([{ key: canvas.key, tier: 1 }], rooted);

  const nodeTiers = new Map<string, number>();
  for (const canvas of canvases.values()) {
    const base = distances.get(canvas.key);
    if (base === undefined) continue;
    for (const [id, depth] of canvas.depths) nodeTiers.set(key(canvas.projectId, id), base + depth);
  }

  return {
    canvasTier: (projectId, canvasId) => distances.get(key(projectId, canvasId)),
    nodeTier: (projectId, nodeId) => nodeTiers.get(key(projectId, nodeId)),
    anchors: () => [...canvases.values()].filter(canvas => provenance.has(canvas.key)).map(canvas => ({ projectId: canvas.projectId, canvasId: canvas.canvasId, tier: distances.get(canvas.key)! })),
  };
}
