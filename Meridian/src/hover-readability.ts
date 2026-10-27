/** Transient screen-space readability for small graph cards. The native card is
 * enlarged; no node copies, new media players, or authored geometry are created. */
export const READABILITY_TARGET_PX = 16;
export const READABILITY_TRIGGER_PX = 14;
const MINIMUM_CONTROL_PX = 48;
const mounts = new Map<HTMLElement, { dispose: () => void }>();
let removals: MutationObserver | undefined;

export interface ReadabilityRect { left: number; top: number; width: number; height: number }
export interface ReadabilityPlacement { factor: number; left: number; top: number; width: number; height: number }
const clamp = (n: number, low: number, high: number) => Math.max(low, Math.min(high, n));
const positive = (n: number) => Number.isFinite(n) && n > 0;

/** Pure sizing contract. The same screen text target applies at every camera
 * zoom; a viewport fit is the only cap, never an arbitrary magnification limit. */
export function readabilityPlacement(rect: ReadabilityRect, viewport: ReadabilityRect,
  textPx?: number, point?: { x: number; y: number }): ReadabilityPlacement | undefined {
  if (![rect.width, rect.height, viewport.width, viewport.height].every(positive)) return;
  let factor: number;
  if (textPx !== undefined && positive(textPx)) {
    if (textPx >= READABILITY_TRIGGER_PX) return;
    factor = READABILITY_TARGET_PX / textPx;
  } else {
    if (Math.min(rect.width, rect.height) >= 32) return;
    factor = MINIMUM_CONTROL_PX / Math.min(rect.width, rect.height);
  }
  const inset = Math.min(12, viewport.width / 20, viewport.height / 20);
  factor = Math.min(factor, (viewport.width - inset * 2) / rect.width, (viewport.height - inset * 2) / rect.height);
  if (!Number.isFinite(factor) || factor <= 1.05) return;
  const width = rect.width * factor, height = rect.height * factor;
  const at = point ?? { x: rect.left + rect.width / 2, y: rect.top + rect.height / 2 };
  const anchorX = clamp((at.x - rect.left) / rect.width, 0, 1), anchorY = clamp((at.y - rect.top) / rect.height, 0, 1);
  return { factor, width, height,
    left: clamp(at.x - width * anchorX, viewport.left + inset, viewport.left + viewport.width - inset - width),
    top: clamp(at.y - height * anchorY, viewport.top + inset, viewport.top + viewport.height - inset - height) };
}

export interface NodeReadabilityOptions {
  host: HTMLElement;
  viewport: HTMLElement;
  cards: ReadonlyMap<string, HTMLElement>;
  getScale: () => number;
  enabled?: () => boolean;
}

interface Active {
  card: HTMLElement;
  saved: Map<string, { value: string; priority: string }>;
  pointer?: { x: number; y: number };
  keyboard: boolean;
}

function localScale(element: HTMLElement): number {
  const style = getComputedStyle(element);
  let factor = 1;
  if (style.transform && style.transform !== 'none') {
    try { const matrix = new DOMMatrixReadOnly(style.transform); factor *= Math.min(Math.hypot(matrix.a, matrix.b), Math.hypot(matrix.c, matrix.d)); } catch { /* Unknown transforms cannot supply a useful scale. */ }
  }
  if (style.scale && style.scale !== 'none') {
    const values = style.scale.split(/\s+/).map(Number).filter(Number.isFinite); if (values.length) factor *= Math.min(...values.slice(0, 2));
  }
  return positive(factor) ? factor : 1;
}

/** Measure only the current card. Contained content has its own fitted transform,
 * so camera scale alone would substantially understate small-label enlargement. */
function textPixels(card: HTMLElement, viewport: HTMLElement, cameraScale: number): number | undefined {
  const surfaces = card.querySelectorAll<HTMLElement>(':scope > .ms-node-header .ms-node-title,:scope > .ms-node-body .ms-text,:scope > .ms-node-body .ms-text-preview,:scope > .ms-node-body .ms-content-link');
  let smallest = Infinity;
  for (const surface of [...surfaces].slice(0, 4)) {
    if (!surface.textContent?.trim() || !surface.getClientRects().length) continue;
    const style = getComputedStyle(surface); if (style.display === 'none' || style.visibility === 'hidden') continue;
    const base = Number.parseFloat(style.fontSize); if (!positive(base)) continue;
    let factor = 1, sawWorld = false;
    for (let element: HTMLElement | null = surface; element && element !== viewport; element = element.parentElement) {
      factor *= localScale(element); if (element.classList.contains('ms-world')) sawWorld = true;
    }
    if (!sawWorld) factor *= positive(cameraScale) ? cameraScale : 1;
    smallest = Math.min(smallest, base * factor);
  }
  return positive(smallest) ? smallest : undefined;
}

const contains = (rect: DOMRect, point: { x: number; y: number }) => point.x >= rect.left && point.x <= rect.right && point.y >= rect.top && point.y <= rect.bottom;
const nativeControl = (target: Element | null) => !!target?.closest('button,a,input,textarea,select,video,audio,.mdv-viewer,[contenteditable="true"]');

export function disposeNodeReadability(host: HTMLElement): void { mounts.get(host)?.dispose(); }

export function mountNodeReadability(options: NodeReadabilityOptions): { refresh: () => void; dispose: () => void } {
  const { host, viewport, cards } = options;
  disposeNodeReadability(host);
  const abort = new AbortController(), signal = abort.signal;
  let active: Active | undefined, pointer: { x: number; y: number } | undefined;
  let pressed = false, holdControl = false, suppress: HTMLElement | undefined, pointerFocusUntil = 0;
  const enabled = () => options.enabled?.() !== false && !viewport.classList.contains('ms-panning') && !viewport.classList.contains('ms-region-mode') && !viewport.classList.contains('ms-selecting-region') && (!viewport.dataset.authoringMode || ['select', 'hand'].includes(viewport.dataset.authoringMode));
  const restore = (entry: Active) => {
    for (const [property, previous] of entry.saved) {
      if (previous.value) entry.card.style.setProperty(property, previous.value, previous.priority); else entry.card.style.removeProperty(property);
    }
    entry.card.classList.remove('ms-readability-expanded'); delete entry.card.dataset.readabilityScale;
  };
  const hide = () => { if (active) restore(active); active = undefined; };
  const targetCard = (target: Element | null): HTMLElement | undefined => {
    const card = target?.closest<HTMLElement>('.ms-node[data-node-id]');
    if (!card || cards.get(card.dataset.nodeId!) !== card || !viewport.contains(card)) return;
    // Never zoom an expanded enclosing workspace and all its children together.
    if (card.matches('.ms-utility,.ms-document-open,.ms-grid-parent:not(.ms-collapsed),.ms-node-container:not(.ms-collapsed),.ms-node-spreadsheet:not(.ms-collapsed),.ms-node-module:not(.ms-collapsed),.ms-gallery-parent:not(.ms-collapsed)')) return;
    if (target?.closest('[contenteditable="true"]')) return;
    return card;
  };
  const paint = (card: HTMLElement, keyboard: boolean, at?: { x: number; y: number }) => {
    if (!card.isConnected || !enabled()) { hide(); return; }
    const entry = active?.card === card ? active : { card, saved: new Map(['scale', 'translate', 'transform-origin', 'z-index'].map(property => [property, { value: card.style.getPropertyValue(property), priority: card.style.getPropertyPriority(property) }])), keyboard, pointer: at };
    if (active) restore(active);
    active = undefined;
    const rect = card.getBoundingClientRect(), ownViewport = viewport.getBoundingClientRect();
    // Leaves within a scrolling Window stay inside its local viewport. Their
    // clipping surface must not be moved, enlarged, or made overflow-visible.
    let clip: ReadabilityRect = ownViewport;
    for (let parent = card.parentElement; parent && parent !== viewport; parent = parent.parentElement) {
      if (!parent.classList.contains('ms-window-viewport')) continue;
      const bounds = parent.getBoundingClientRect(), left = Math.max(clip.left, bounds.left), top = Math.max(clip.top, bounds.top);
      clip = { left, top, width: Math.max(0, Math.min(clip.left + clip.width, bounds.right) - left), height: Math.max(0, Math.min(clip.top + clip.height, bounds.bottom) - top) };
    }
    const placement = readabilityPlacement(rect, clip, textPixels(card, viewport, options.getScale()), at);
    if (!placement) return;
    const worldWidth = Number.parseFloat(card.style.width) || card.offsetWidth;
    const worldHeight = Number.parseFloat(card.style.height) || card.offsetHeight;
    const scaleX = positive(worldWidth) ? rect.width / worldWidth : options.getScale(), scaleY = positive(worldHeight) ? rect.height / worldHeight : options.getScale();
    if (!positive(scaleX) || !positive(scaleY)) return;
    // The scene rebases large world coordinates with CSS individual translate.
    // Preserve that computed base even when it came from a stylesheet variable,
    // rather than replacing it with just this temporary screen displacement.
    const computed = getComputedStyle(card);
    const translations = computed.translate === 'none' ? [] : computed.translate.match(/calc\([^)]*\)|[^\s]+/g) ?? [];
    const baseScales = computed.scale === 'none' ? [] : computed.scale.split(/\s+/).map(Number);
    const baseScaleX = positive(baseScales[0]) ? baseScales[0] : 1, baseScaleY = positive(baseScales[1]) ? baseScales[1] : baseScaleX;
    card.style.setProperty('transform-origin', '0 0');
    card.style.setProperty('scale', `${baseScaleX * placement.factor} ${baseScaleY * placement.factor}`);
    card.style.setProperty('translate', `calc(${translations[0] ?? '0px'} + ${(placement.left - rect.left) / scaleX}px) calc(${translations[1] ?? '0px'} + ${(placement.top - rect.top) / scaleY}px)`);
    card.style.setProperty('z-index', '100000'); card.dataset.readabilityScale = String(placement.factor); card.classList.add('ms-readability-expanded');
    entry.keyboard = keyboard; entry.pointer = at; active = entry;
  };
  const refresh = () => {
    if (!active) return;
    if (pressed && holdControl && active.card.isConnected) return;
    const entry = active;
    if (!enabled() || !entry.card.isConnected || pressed || !entry.keyboard && (!pointer || !contains(entry.card.getBoundingClientRect(), pointer))) { hide(); return; }
    paint(entry.card, entry.keyboard, entry.pointer);
  };
  viewport.addEventListener('pointermove', event => {
    pointer = { x: event.clientX, y: event.clientY };
    if (pressed && holdControl) return;
    if (pressed || event.buttons || !enabled()) { hide(); return; }
    const target = event.target instanceof Element ? event.target : null;
    if (active && active.card.isConnected && contains(active.card.getBoundingClientRect(), pointer)) return;
    const card = targetCard(target);
    if (card !== suppress) suppress = undefined;
    if (card && card !== suppress) paint(card, false, pointer); else hide();
  }, { signal, capture: true });
  viewport.addEventListener('pointerleave', () => { pointer = undefined; suppress = undefined; if (!active?.keyboard && !holdControl) hide(); }, { signal });
  viewport.addEventListener('pointerdown', event => {
    pointerFocusUntil = performance.now() + 500; pressed = true;
    const target = event.target instanceof Element ? event.target : null;
    holdControl = !!active && active.card.contains(target) && nativeControl(target);
    if (!holdControl) { suppress = active?.card; hide(); }
  }, { signal, capture: true });
  window.addEventListener('pointerup', () => { pressed = false; holdControl = false; queueMicrotask(refresh); }, { signal });
  window.addEventListener('pointercancel', () => { pressed = false; holdControl = false; hide(); }, { signal });
  viewport.addEventListener('wheel', hide, { signal, capture: true, passive: true });
  viewport.addEventListener('focusin', event => {
    if (performance.now() < pointerFocusUntil) return;
    const card = targetCard(event.target instanceof Element ? event.target : null); if (card && enabled()) paint(card, true);
  }, { signal });
  viewport.addEventListener('focusout', () => queueMicrotask(() => { if (active?.keyboard && !active.card.contains(document.activeElement)) hide(); }), { signal });
  window.addEventListener('keydown', event => { if (event.key === 'Escape') hide(); }, { signal, capture: true });
  window.addEventListener('blur', () => { pressed = false; holdControl = false; hide(); }, { signal });
  window.addEventListener('resize', refresh, { signal });
  const dispose = () => { hide(); abort.abort(); mounts.delete(host); if (!mounts.size) { removals?.disconnect(); removals = undefined; } };
  mounts.set(host, { dispose });
  if (!removals) { removals = new MutationObserver(() => { for (const [element, mount] of mounts) if (!element.isConnected) mount.dispose(); }); removals.observe(document.documentElement, { childList: true, subtree: true }); }
  return { refresh, dispose };
}
