import type { MeridianNode, MeridianProject, Point } from './core/types.ts';
import { expansionTrayBounds, visibleExpansionTrays, moveExpansionTray, resizeExpansionTray, EXPANSION_TRAY_MIN_WIDTH, EXPANSION_TRAY_MIN_HEIGHT } from './core/expansion-tray.ts';
import type { SizingContext } from './core/sizing.ts';
import { nodeTag } from './core/node-tag.ts';
import { nodeShape, shapeAnchor } from './core/geometry.ts';
import { isEnclosingContainer } from './core/cells.ts';
import { isNodeStyle } from './core/appearance.ts';
import { prepareBackgroundDisplay } from './background-display.ts';

interface Preview { ids: Set<string>; x: number; y: number; project?: MeridianProject }
export interface TrayFrame { x: number; y: number; width: number; height: number }
interface TraySceneOptions {
  project: MeridianProject;
  canvasId: string;
  context: SizingContext;
  world: HTMLElement;
  cards: Map<string, HTMLElement>;
  scale: () => number;
  /** Convert the displayed native surface back to canvas coordinates. */
  elementBounds: (element: HTMLElement) => { x: number; y: number; width: number; height: number };
  descendants: (sourceId: string) => MeridianNode[];
  movable: boolean;
  canMove?: (ids: readonly string[]) => boolean;
  onMove?: (sourceId: string, dx: number, dy: number) => void;
  onResize?: (sourceId: string, frame: TrayFrame) => void;
  onResizePreview?: (project?: MeridianProject) => void;
  onClose?: (sourceId: string) => void;
  onSelect?: (sourceId: string, additive: boolean) => void;
  isSelected?: (sourceId: string) => boolean;
  onPreview: (offset?: Preview) => void;
  beginGesture: (cancel: () => void) => void;
  endGesture: (cancel: () => void) => void;
}
const svg = <K extends keyof SVGElementTagNameMap>(name: K, attrs: Record<string, string> = {}): SVGElementTagNameMap[K] => {
  const element = document.createElementNS('http://www.w3.org/2000/svg', name);
  for (const [key, value] of Object.entries(attrs)) element.setAttribute(key, value);
  return element;
};
// Selection rebuilds the scene. Remember consecutive releases by stable source
// identity so the second click can collapse the same tray in its replacement DOM.
let lastTrayClick: { scope: string; sourceId: string; time: number; x: number; y: number } | undefined;
if (typeof document !== 'undefined') document.addEventListener('pointerdown', event => {
  const target = event.target instanceof Element ? event.target : undefined;
  const background = target?.closest<HTMLElement>('.ms-expansion-tray-backdrop');
  if (background?.dataset.traySourceId !== lastTrayClick?.sourceId || background?.dataset.trayScope !== lastTrayClick?.scope) lastTrayClick = undefined;
}, true);
function displayed(element: HTMLElement): boolean {
  if (getComputedStyle(element).visibility === 'hidden') return false;
  let rect = element.getBoundingClientRect();
  if (!rect.width || !rect.height) return false;
  for (let parent = element.parentElement; parent; parent = parent.parentElement) {
    if (!parent.classList.contains('ms-window-viewport')) continue;
    const clip = parent.getBoundingClientRect();
    const left = Math.max(rect.left, clip.left), top = Math.max(rect.top, clip.top);
    const right = Math.min(rect.right, clip.right), bottom = Math.min(rect.bottom, clip.bottom);
    if (right <= left || bottom <= top) return false;
    rect = new DOMRect(left, top, right - left, bottom - top);
  }
  return true;
}

/** A tray is presentation chrome around the original descendants. It never
 * creates graph nodes, alternate identities, or actual Meridian links. */
export function mountExpansionTrays(options: TraySceneOptions): { update: (preview?: Preview, geometry?: MeridianProject) => void; isNodeVisible: (id: string) => boolean; beginMove: (id: string, event: PointerEvent, onClick?: () => void) => void } {
  const { project, context, world, cards } = options;
  const clickScope = `${project.id}:${options.canvasId}`;
  const movePointers = new Map<string, (event: PointerEvent, onClick?: () => void) => void>();
  const layer = svg('svg', { class: 'ms-expansion-tethers', width: '1', height: '1', 'aria-hidden': 'true' });
  world.prepend(layer);
  const initial = visibleExpansionTrays(project, context).filter(tray => project.nodes.find(node => node.id === tray.sourceId)?.canvasId === options.canvasId);
  initial.sort((a, b) => options.descendants(a.sourceId).some(node => node.id === b.sourceId) ? -1 : options.descendants(b.sourceId).some(node => node.id === a.sourceId) ? 1 : 0);
  const frames = initial.map(tray => {
    const source = project.nodes.find(node => node.id === tray.sourceId)!, identity = nodeTag(source);
    const sourceSurface = cards.get(source.id) ?? [...world.querySelectorAll<HTMLElement>('[data-gallery-item-id]')].find(item => item.dataset.galleryItemId === source.id);
    if (!sourceSurface) return;
    const sourceCard = sourceSurface.closest<HTMLElement>('.ms-node') ?? sourceSurface;
    const frame = document.createElement('section'); frame.className = 'ms-expansion-tray'; frame.dataset.traySourceId = source.id;
    frame.setAttribute('role', 'region'); frame.setAttribute('aria-label', `${identity} expansion tray`);
    const background = document.createElement('div'); background.className = 'ms-expansion-tray-backdrop'; background.dataset.traySourceId = source.id;
    background.dataset.trayScope = clickScope;
    background.tabIndex = 0; background.setAttribute('role', 'button'); background.setAttribute('aria-label', `${identity} expansion tray background`);
    background.setAttribute('aria-expanded', 'true'); background.setAttribute('aria-keyshortcuts', 'Alt+ArrowLeft');
    const style = isNodeStyle(source.style) ? source.style ?? {} : {};
    // Trays begin as ordinary rectangular enclosures. An explicit rounded
    // shape (or radius without a competing shape) opts into rounded corners.
    const radius = style.shape === 'rounded' ? style.radius ?? 20 : style.shape === undefined ? style.radius ?? 0 : 0;
    for (const element of [frame, background]) {
      element.style.setProperty('--ms-tray-fill', style.fill ?? '#17283e55'); element.style.setProperty('--ms-tray-border', style.border ?? '#314c6c');
      element.style.setProperty('--ms-tray-border-width', `${style.borderWidth ?? 1}px`); element.style.setProperty('--ms-tray-radius', `${radius}px`);
    }
    prepareBackgroundDisplay(background,source,'tray');
    // A normal empty enclosure owns only its blank background; original cards
    // paint later and keep their native hit targets, controls and text editors.
    world.insertBefore(background, [...cards.values()].find(card => card.parentElement === world) ?? null); world.append(frame);
    const tether = svg('path', { class: 'ms-expansion-tether', 'data-tray-source-id': source.id, 'vector-effect': 'non-scaling-stroke' }); layer.append(tether);
    sourceCard.classList.add('ms-tray-source');
    const highlight = (value: boolean) => {
      frame.classList.toggle('ms-tray-highlighted', value); background.classList.toggle('ms-tray-highlighted', value); tether.classList.toggle('ms-tray-highlighted', value); sourceCard.classList.toggle('ms-tray-highlighted', value);
    };
    for (const element of [frame, background, sourceSurface]) {
      element.addEventListener('pointerenter', () => highlight(true)); element.addEventListener('pointerleave', () => highlight(false));
      element.addEventListener('focusin', () => highlight(true)); element.addEventListener('focusout', () => highlight(false));
    }
    const descendants = options.descendants(source.id), ids = new Set(descendants.map(node => node.id));
    const contentElements = (() => {
      const elements = descendants.flatMap(node => cards.get(node.id) ? [cards.get(node.id)!] : []);
      for (const element of world.querySelectorAll<HTMLElement>('[data-external-window],[data-external-gallery],[data-external-grid]')) {
        const owner = element.dataset.externalWindow ?? element.dataset.externalGallery ?? element.dataset.externalGrid;
        if (owner === source.id || owner && ids.has(owner)) elements.push(element);
      }
      return elements.filter(element => !elements.some(other => other !== element && other.contains(element)));
    })();
    const contents = () => contentElements;
    for (const element of contents()) {
      element.addEventListener('pointerenter', () => highlight(true)); element.addEventListener('pointerleave', () => highlight(false));
      element.addEventListener('focusin', () => highlight(true)); element.addEventListener('focusout', () => highlight(false));
    }
    const canMove = () => options.movable && !!options.onMove && options.canMove?.(descendants.map(node => node.id)) !== false;
    if (options.movable && options.onResize) for (const edge of ['n', 'ne', 'e', 'se', 's', 'sw', 'w', 'nw']) {
      const handle = document.createElement('span'); handle.className = 'ms-expansion-tray-resize'; handle.dataset.trayResize = edge;
      handle.setAttribute('aria-hidden', 'true'); handle.title = `Resize expansion tray: ${identity}`; frame.append(handle);
      handle.addEventListener('pointerdown', event => {
        if (event.button !== 0 || options.canMove?.(descendants.map(node => node.id)) === false) return;
        event.preventDefault(); event.stopPropagation();
        const original = expansionTrayBounds(project, source.id, context); if (!original) return;
        const startX = event.clientX, startY = event.clientY;
        let latest: TrayFrame = original, dragged = false;
        const cleanup = () => {
          handle.removeEventListener('pointermove', move); handle.removeEventListener('pointerup', end); handle.removeEventListener('pointercancel', cancel); handle.removeEventListener('lostpointercapture', cancel);
          window.removeEventListener('keydown', escape, true); window.removeEventListener('blur', cancel);
          options.endGesture(cancel); frame.classList.remove('ms-tray-resizing'); options.onResizePreview?.();
          if (handle.hasPointerCapture(event.pointerId)) handle.releasePointerCapture(event.pointerId);
        };
        const cancel = () => cleanup();
        const move = (next: PointerEvent) => {
          if (next.pointerId !== event.pointerId) return;
          next.preventDefault(); next.stopPropagation(); const clientX = next.clientX - startX, clientY = next.clientY - startY;
          dragged ||= Math.hypot(clientX, clientY) > 3; if (!dragged) return;
          const dx = clientX / options.scale(), dy = clientY / options.scale();
          let left = original.x, top = original.y, right = original.x + original.width, bottom = original.y + original.height;
          if (edge.includes('e')) right = Math.max(left + EXPANSION_TRAY_MIN_WIDTH, right + dx);
          if (edge.includes('w')) left = Math.min(right - EXPANSION_TRAY_MIN_WIDTH, left + dx);
          if (edge.includes('s')) bottom = Math.max(top + EXPANSION_TRAY_MIN_HEIGHT, bottom + dy);
          if (edge.includes('n')) top = Math.min(bottom - EXPANSION_TRAY_MIN_HEIGHT, top + dy);
          try {
            const preview = resizeExpansionTray(project, source.id, { x: left, y: top, width: right - left, height: bottom - top }, context);
            latest = expansionTrayBounds(preview, source.id, context)!;
            frame.classList.add('ms-tray-resizing'); options.onResizePreview?.(preview);
          } catch {
            // A collision or pinned composition keeps the last valid preview.
            // It is never converted into a partial edit while the pointer is held.
          }
        };
        const end = (next: PointerEvent) => {
          if (next.pointerId !== event.pointerId) return; next.preventDefault(); next.stopPropagation();
          const result = { x: latest.x, y: latest.y, width: latest.width, height: latest.height };
          cleanup(); if (dragged && (['x', 'y', 'width', 'height'] as const).some(key => Math.abs(result[key] - original[key]) > 1e-7)) options.onResize?.(source.id, result);
        };
        const escape = (next: KeyboardEvent) => { if (next.key === 'Escape') { next.preventDefault(); next.stopPropagation(); cancel(); } };
        lastTrayClick = undefined; options.beginGesture(cancel); background.focus({ preventScroll: true }); if (!handle.isConnected) { cleanup(); return; }
        handle.setPointerCapture(event.pointerId);
        handle.addEventListener('pointermove', move); handle.addEventListener('pointerup', end); handle.addEventListener('pointercancel', cancel); handle.addEventListener('lostpointercapture', cancel);
        window.addEventListener('keydown', escape, true); window.addEventListener('blur', cancel);
      });
    }
    {
      background.addEventListener('dblclick', event => { event.preventDefault(); event.stopPropagation(); });
      if (options.movable && options.onMove) background.classList.add('ms-tray-draggable');
      background.addEventListener('keydown', event => {
        if (event.target !== background) return;
        if (event.altKey && event.key === 'ArrowLeft') { event.preventDefault(); event.stopPropagation(); lastTrayClick = undefined; options.onClose?.(source.id); return; }
        if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); event.stopPropagation(); lastTrayClick = undefined; options.onSelect?.(source.id, event.ctrlKey || event.metaKey || event.shiftKey); return; }
        if (!canMove() || event.ctrlKey || event.metaKey || event.altKey) return;
        const step = event.shiftKey ? 20 : 5;
        const dx = event.key === 'ArrowLeft' ? -step : event.key === 'ArrowRight' ? step : 0;
        const dy = event.key === 'ArrowUp' ? -step : event.key === 'ArrowDown' ? step : 0;
        if (!dx && !dy) return; event.preventDefault(); event.stopPropagation(); options.onMove?.(source.id, dx, dy);
      });
      const beginMove = (event: PointerEvent, handleClick?: () => void) => {
        if (event.button !== 0) return;
        event.preventDefault(); event.stopPropagation();
        const startX = event.clientX, startY = event.clientY;
        let dx = 0, dy = 0, dragged = false, lastX = startX, lastY = startY, bypassSnap = event.altKey;
        let receiver: HTMLElement | undefined;
        const originals = new Map(contents().map(element => [element, element.style.transform]));
        const cleanup = () => {
          background.removeEventListener('pointermove', move); background.removeEventListener('pointerup', end);
          background.removeEventListener('pointercancel', cancel); background.removeEventListener('lostpointercapture', cancel);
          window.removeEventListener('keydown', escape, true); window.removeEventListener('keyup', escape, true); window.removeEventListener('blur', cancel);
          for (const [element, transform] of originals) element.style.transform = transform;
          frame.classList.remove('ms-tray-dragging'); background.classList.remove('ms-tray-dragging'); options.endGesture(cancel); options.onPreview();
          receiver?.classList.remove('ms-tray-snap-receiver');
          if (background.hasPointerCapture(event.pointerId)) background.releasePointerCapture(event.pointerId);
        };
        const cancel = () => cleanup();
        const preview = () => {
          const x = lastX - startX, y = lastY - startY;
          dragged ||= handleClick ? Math.hypot(x, y) >= 4 : Math.hypot(x, y) > 3; if (!dragged) return; lastTrayClick = undefined;
          if (!canMove()) return;
          let candidate: MeridianProject;
          try {
            candidate = moveExpansionTray(project, source.id, x / options.scale(), y / options.scale(), { snapDistance: 12 / options.scale(), bypassSnap });
          } catch { return; }
          const nextSource = candidate.nodes.find(node => node.id === source.id)!;
          dx = nextSource.expansionTray!.offset.x - source.expansionTray!.offset.x; dy = nextSource.expansionTray!.offset.y - source.expansionTray!.offset.y;
          frame.classList.add('ms-tray-dragging'); background.classList.add('ms-tray-dragging');
          for (const [element, transform] of originals) element.style.transform = `translate(${dx}px,${dy}px) ${transform}`.trim();
          options.onPreview({ ids, x: dx, y: dy, project: candidate });
          let owner = project.nodes.find(node => node.id === source.parentId), outer: HTMLElement | undefined;
          const seen = new Set<string>();
          while (owner && !seen.has(owner.id)) { seen.add(owner.id); if (isEnclosingContainer(owner) && cards.has(owner.id)) outer = cards.get(owner.id); owner = project.nodes.find(node => node.id === owner!.parentId); }
          receiver?.classList.remove('ms-tray-snap-receiver'); receiver = undefined;
          if (!bypassSnap && outer) {
            const a = options.elementBounds(outer), b = expansionTrayBounds(candidate, source.id, context)!;
            const seamX = Math.min(a.x + a.width, b.x + b.width) - Math.max(a.x, b.x), seamY = Math.min(a.y + a.height, b.y + b.height) - Math.max(a.y, b.y);
            const touches = seamX > 1e-7 && (Math.abs(a.y + a.height - b.y) < 1e-6 || Math.abs(b.y + b.height - a.y) < 1e-6)
              || seamY > 1e-7 && (Math.abs(a.x + a.width - b.x) < 1e-6 || Math.abs(b.x + b.width - a.x) < 1e-6);
            if (touches) { receiver = outer; receiver.classList.add('ms-tray-snap-receiver'); }
          }
        };
        const move = (next: PointerEvent) => {
          if (next.pointerId !== event.pointerId) return;
          next.stopPropagation(); lastX = next.clientX; lastY = next.clientY; bypassSnap = next.altKey; preview();
        };
        const end = (next: PointerEvent) => {
          if (next.pointerId !== event.pointerId) return; next.preventDefault(); next.stopPropagation();
          cleanup();
          if (dragged) { if (dx || dy) options.onMove?.(source.id, dx, dy); return; }
          if (handleClick) { lastTrayClick = undefined; handleClick(); return; }
          const now = performance.now(), previous = lastTrayClick;
          if (!next.ctrlKey && !next.metaKey && !next.shiftKey && previous?.scope === clickScope && previous.sourceId === source.id
            && now - previous.time < 480 && Math.hypot(next.clientX - previous.x, next.clientY - previous.y) < 6) {
            lastTrayClick = undefined; options.onClose?.(source.id);
          } else {
            lastTrayClick = { scope: clickScope, sourceId: source.id, time: now, x: next.clientX, y: next.clientY };
            options.onSelect?.(source.id, next.ctrlKey || next.metaKey || next.shiftKey);
          }
        };
        const escape = (next: KeyboardEvent) => {
          if (next.key === 'Escape') { next.preventDefault(); next.stopPropagation(); lastTrayClick = undefined; cancel(); }
          else if (next.key === 'Alt') { next.preventDefault(); bypassSnap = next.type === 'keydown'; preview(); }
        };
        options.beginGesture(cancel);
        // Committing a text editor on focus may replace the entire scene.
        background.focus({ preventScroll: true }); if (!background.isConnected) { cleanup(); return; }
        background.setPointerCapture(event.pointerId);
        background.addEventListener('pointermove', move); background.addEventListener('pointerup', end); background.addEventListener('pointercancel', cancel); background.addEventListener('lostpointercapture', cancel);
        window.addEventListener('keydown', escape, true); window.addEventListener('keyup', escape, true); window.addEventListener('blur', cancel);
      };
      background.addEventListener('pointerdown', event => beginMove(event));
      movePointers.set(source.id, beginMove);
    }
    return { source, sourceSurface, sourceCard, frame, background, tether, contents, ids, shown: true };
  }).filter(item => !!item);
  const update = (preview?: Preview, geometry?: MeridianProject) => {
    const geometryProject = geometry ?? (preview ? { ...project, nodes: project.nodes.map(node => preview.ids.has(node.id) ? { ...node, x: node.x + preview.x, y: node.y + preview.y } : node) } : project);
    for (const item of frames) {
      const bounds = expansionTrayBounds(geometryProject, item.source.id, context); if (!bounds) continue;
      const shown = displayed(item.sourceSurface); item.shown = shown;
      item.frame.hidden = !shown; item.background.hidden = !shown; item.tether.style.display = shown ? '' : 'none';
      for (const element of item.contents()) element.style.visibility = shown ? '' : 'hidden';
      if (!shown) continue;
      Object.assign(item.frame.style, { left: `${bounds.x}px`, top: `${bounds.y}px`, width: `${bounds.width}px`, height: `${bounds.height}px` });
      Object.assign(item.background.style, { left: `${bounds.x}px`, top: `${bounds.y}px`, width: `${bounds.width}px`, height: `${bounds.height}px` });
      const selected = options.isSelected?.(item.source.id) ?? false; item.frame.classList.toggle('ms-selected', selected); item.background.setAttribute('aria-pressed', String(selected));
      const sourceBounds = options.elementBounds(item.sourceSurface);
      const target = { x: bounds.x + bounds.width / 2, y: bounds.y + bounds.height / 2 };
      const source = { ...item.source, ...sourceBounds };
      let owner = project.nodes.find(node => node.id === item.source.parentId), enclosure: HTMLElement | undefined;
      const seen = new Set<string>();
      while (owner && !seen.has(owner.id)) {
        seen.add(owner.id); if (isEnclosingContainer(owner) && cards.has(owner.id)) enclosure = cards.get(owner.id);
        owner = project.nodes.find(node => node.id === owner!.parentId);
      }
      const outer = enclosure && options.elementBounds(enclosure);
      // Leave an ordered row through its free rim rather than hiding a straight
      // connector beneath neighboring tiles and making it appear to belong to a peer.
      if (outer && (bounds.x >= outer.x + outer.width || bounds.x + bounds.width <= outer.x)) {
        const below = target.y >= outer.y + outer.height / 2, y = below ? outer.y + outer.height + 16 : outer.y - 16;
        const start = shapeAnchor(nodeShape(source), 0, below ? 1 : -1);
        const right = bounds.x >= outer.x + outer.width, x = right ? bounds.x - 16 : bounds.x + bounds.width + 16;
        const end = { x: right ? bounds.x : bounds.x + bounds.width, y: Math.max(bounds.y + 8, Math.min(bounds.y + bounds.height - 8, y)) };
        item.tether.setAttribute('d', `M${start.x},${start.y} L${start.x},${y} L${x},${y} L${x},${end.y} L${end.x},${end.y}`);
      } else {
        const start = shapeAnchor(nodeShape(source), target.x - sourceBounds.x - sourceBounds.width / 2, target.y - sourceBounds.y - sourceBounds.height / 2);
        const end = shapeAnchor(nodeShape({ ...source, style: { shape: 'rectangle' }, ...bounds }), start.x - target.x, start.y - target.y);
        item.tether.setAttribute('d', `M${start.x},${start.y} L${end.x},${end.y}`);
      }
    }
  };
  update(); return { update, isNodeVisible: id => frames.every(item => !item.ids.has(id) || item.shown), beginMove: (id, event, onClick) => movePointers.get(id)?.(event, onClick) };
}
