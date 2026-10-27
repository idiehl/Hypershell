import type { MeridianProject } from './core/types.ts';
import { nodeAssetIds } from './core/appearance.ts';

/** A revision acknowledgement normally changes no rendered content. Retain the
 * existing readers/camera in that case, but refresh snapshot consumers and any
 * server-normalized graph, appearance, metadata or attachment change. */
export function saveChangesScene(before: MeridianProject, after: MeridianProject): boolean {
  for (const project of [before, after]) {
    const assets = new Set(project.assets.map(asset => asset.id));
    if (project.nodes.some(node => node.kind === 'reference' || node.remoteLinks?.length ||
        nodeAssetIds(node).some(id => !assets.has(id))) ||
        project.canvases.some(canvas => canvas.style?.backgroundAssetId && !assets.has(canvas.style.backgroundAssetId))) return true;
  }
  const same = (a: unknown, b: unknown): boolean => {
    if (Object.is(a, b)) return true;
    if (!a || !b || typeof a !== 'object' || typeof b !== 'object' || Array.isArray(a) !== Array.isArray(b)) return false;
    const first = a as Record<string, unknown>, second = b as Record<string, unknown>, keys = Object.keys(first);
    return keys.length === Object.keys(second).length && keys.every(key => Object.hasOwn(second, key) && same(first[key], second[key]));
  };
  const keys = new Set([...Object.keys(before), ...Object.keys(after)]);
  keys.delete('revision'); keys.delete('snapshots');
  for (const key of keys) if (!same(before[key as keyof MeridianProject], after[key as keyof MeridianProject])) return true;
  return false;
}

/** Coalesces committed edits while keeping one revision-checked save in flight. */
export interface AutomaticSaveOptions {
  commit: () => Promise<boolean>;
  idleMs?: number;
  maxWaitMs?: number;
  maxActions?: number;
  clock?: {
    setTimeout: (callback: () => void, delay: number) => unknown;
    clearTimeout: (timer: unknown) => void;
  };
}

export function createAutomaticSave(options: AutomaticSaveOptions) {
  const clock = options.clock ?? {
    setTimeout: (callback: () => void, delay: number) => setTimeout(callback, delay),
    clearTimeout: (timer: unknown) => clearTimeout(timer as ReturnType<typeof setTimeout>),
  };
  let idleTimer: unknown, maxTimer: unknown;
  let pending = false, paused = false, actions = 0, generation = 0;
  let running: Promise<boolean> | null = null;

  function clearTimers() {
    if (idleTimer !== undefined) clock.clearTimeout(idleTimer);
    if (maxTimer !== undefined) clock.clearTimeout(maxTimer);
    idleTimer = maxTimer = undefined;
  }
  function pause() { paused = true; clearTimers(); }
  function reset() { generation++; pending = false; paused = false; actions = 0; clearTimers(); }
  async function drain(): Promise<boolean> {
    if (running) return running;
    if (paused) return false;
    clearTimers();
    const ticket = generation;
    const work = async () => {
      while (pending && !paused && ticket === generation) {
        pending = false; actions = 0;
        let saved = false;
        try { saved = await options.commit(); } catch { /* The owner reports and retains its draft. */ }
        if (ticket !== generation) return saved;
        if (!saved) { pending = true; pause(); return false; }
      }
      return !paused;
    };
    running = work();
    try { return await running; }
    finally {
      running = null;
      // A new canvas may have been installed while the old request finished.
      if (pending && !paused && ticket !== generation) void drain();
    }
  }
  function changed() {
    pending = true; actions++;
    if (paused || running) return;
    if (actions >= (options.maxActions ?? 5)) { void drain(); return; }
    if (idleTimer !== undefined) clock.clearTimeout(idleTimer);
    idleTimer = clock.setTimeout(() => { idleTimer = undefined; void drain(); }, options.idleMs ?? 500);
    if (maxTimer === undefined) maxTimer = clock.setTimeout(() => { maxTimer = undefined; void drain(); }, options.maxWaitMs ?? 2000);
  }
  async function flush(retry = false): Promise<boolean> {
    if (retry) paused = false;
    clearTimers();
    // An edit made while saving remains pending and is captured after the
    // response supplies the new revision, never against the old revision.
    if (running) {
      const result = await running;
      if (!result || paused) return false;
    }
    return drain();
  }
  return { changed, flush, pause, reset, get paused() { return paused; }, get pending() { return pending; } };
}
