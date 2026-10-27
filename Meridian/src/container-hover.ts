import type { MeridianNode } from './core/types.ts';
import { nodeShape, shapeBounds } from './core/geometry.ts';
import { nodeTag } from './core/node-tag.ts';
import { FONT_FAMILIES } from './core/appearance.ts';

interface Rect { left: number; top: number; right: number; bottom: number; width: number; height: number }
interface HoverOptions {
  viewport: HTMLElement;
  world: HTMLElement;
  cards: Map<string, HTMLElement>;
  nodes: Map<string, MeridianNode>;
  children: Map<string, MeridianNode[]>;
  ancestors: (id: string) => MeridianNode[];
  rootId?: string;
  enabled: () => boolean;
  onToggle: (id: string, collapsed: boolean) => void;
  /** The original trusted pointer event enters the same gesture as the real
   * card/backdrop. That gesture owns capture, cancellation and one commit. */
  onHandlePointerDown?: (id: string, kind: 'node' | 'tray', event: PointerEvent, onClick: () => void) => void;
}
interface Surface { id: string; kind: 'node' | 'tray'; element: HTMLElement; node: MeridianNode }
const HEIGHT = 28;
const contains = (rect: Rect, x: number, y: number) => x >= rect.left && x <= rect.right && y >= rect.top && y <= rect.bottom;

/** Disclosure chrome is in screen space and owns no content/layout rectangle.
 * Descendants are flat scene siblings, so DOM :hover cannot identify their
 * enclosure. Resolve their presentation ancestry instead, stopping at trays. */
export function mountContainerHover(options: HoverOptions): { update: () => void } {
  const { viewport, world, cards, nodes } = options;
  const surfaces = new Map<string, Surface[]>();
  for (const [id, card] of cards) {
    const node = nodes.get(id);
    // Disclosure belongs to hierarchy, not the bounded Container kind. A text-
    // filled Media cover needs an independent way to open its spatial children.
    if (node && id !== options.rootId && options.children.get(id)?.length) surfaces.set(id, [{ id, node, kind: 'node', element: card }]);
  }
  for (const frame of world.querySelectorAll<HTMLElement>('.ms-expansion-tray[data-tray-source-id]')) {
    const id = frame.dataset.traySourceId!, node = nodes.get(id);
    // The source cover and its exterior tray are separate physical surfaces
    // with the same original node identity. Neither may replace the other.
    if (node) surfaces.set(id, [...(surfaces.get(id) ?? []), { id, node, kind: 'tray', element: frame }]);
  }
  const bar = document.createElement('button'); bar.type = 'button'; bar.className = 'ms-container-hover-bar'; bar.hidden = true;
  viewport.append(bar);
  let active: Surface | undefined, pointer: { x: number; y: number } | undefined;
  let keyboard = false;
  let pointerFocusUntil = 0;
  const rectFor = (surface: Surface): Rect | undefined => {
    const element = surface.element;
    if (!element.isConnected || getComputedStyle(element).visibility === 'hidden') return;
    const outer = element.getBoundingClientRect(); if (!outer.width || !outer.height) return;
    let rect: Rect = outer;
    if (surface.kind === 'node') {
      const width = parseFloat(element.style.width), height = parseFloat(element.style.height);
      if (width > 0 && height > 0) {
        const physical = shapeBounds(nodeShape(surface.node, { x: 0, y: 0, width, height }));
        const left = outer.left + physical.x * outer.width / width, top = outer.top + physical.y * outer.height / height;
        rect = { left, top, width: physical.width * outer.width / width, height: physical.height * outer.height / height,
          right: left + physical.width * outer.width / width, bottom: top + physical.height * outer.height / height };
      }
    }
    let left = rect.left, top = rect.top, right = rect.right, bottom = rect.bottom;
    for (let parent: HTMLElement | null = element.parentElement; parent; parent = parent.parentElement) {
      if (parent !== viewport && !parent.classList.contains('ms-window-viewport')) continue;
      const clip = parent.getBoundingClientRect(); left = Math.max(left, clip.left); top = Math.max(top, clip.top); right = Math.min(right, clip.right); bottom = Math.min(bottom, clip.bottom);
    }
    // Visibility may be clipped, but the attached bar must retain the owner's
    // full physical frame, including the portion outside the camera viewport.
    return right > left && bottom > top ? rect : undefined;
  };
  const hide = () => { active = undefined; bar.hidden = true; delete bar.dataset.containerHoverOwner; delete bar.dataset.hoverSurface; };
  const typography = (surface: Surface) => {
    const source = cards.get(surface.id) ?? [...world.querySelectorAll<HTMLElement>('[data-gallery-item-id]')].find(item => item.dataset.galleryItemId === surface.id) ?? surface.element;
    let base = source;
    let title = source.querySelector<HTMLElement>(':scope > .ms-node-header .ms-node-title');
    // A Gallery tray's visual reading surface is its current original child,
    // whose typography can differ from the compact source cover's typography.
    if (surface.kind === 'tray' && surface.node.containerVariant === 'gallery') {
      const page = (options.children.get(surface.id) ?? []).map(node => cards.get(node.id)).find(card => card && getComputedStyle(card).visibility !== 'hidden');
      base = page ?? source.querySelector<HTMLElement>('.ms-gallery-item') ?? source;
      title = base.querySelector<HTMLElement>(':scope > .ms-node-header .ms-node-title,.ms-gallery-item-title');
    }
    else title ??= source.querySelector<HTMLElement>('.ms-gallery-item-title');
    // Rich text and references may resolve a different family/style at the
    // actual glyph than the frame's base style. Match that displayed title.
    const glyph = title && ([...title.querySelectorAll<HTMLElement>('.ms-text-run')].find(run => run.textContent?.trim()) ?? title);
    const computed = getComputedStyle(glyph ?? base.querySelector<HTMLElement>('.ms-text,.ms-text-preview,.ms-node-body') ?? base);
    return { family: glyph || base !== source ? computed.fontFamily : FONT_FAMILIES.find(font => font.value === surface.node.style?.fontFamily)?.css ?? computed.fontFamily,
      weight: computed.fontWeight, style: computed.fontStyle };
  };
  const paint = () => {
    if (!options.enabled() || !active) { hide(); return; }
    const rect = rectFor(active); if (!rect) { hide(); return; }
    const box = viewport.getBoundingClientRect(), width = rect.width, left = rect.left - box.left;
    // At the viewport's top edge there is no room for an exterior strip. Attach
    // it just inside the same owner edge rather than shifting it sideways or
    // shrinking its width to the remaining visible area.
    const top = rect.top - box.top - (rect.top - box.top >= HEIGHT ? HEIGHT : 0);
    const font = typography(active);
    bar.textContent = nodeTag(active.node); bar.setAttribute('aria-label', `${active.kind === 'node' && active.node.collapsed ? 'Expand' : 'Collapse'} ${nodeTag(active.node)}`);
    bar.setAttribute('aria-expanded', String(!active.node.collapsed));
    bar.dataset.containerHoverOwner = active.id; bar.dataset.hoverSurface = active.kind;
    bar.style.fontFamily = font.family; bar.style.fontWeight = font.weight; bar.style.fontStyle = font.style;
    // Padding and border may not impose an implicit minimum on tiny, deeply
    // scaled frames. The complete strip still occupies exactly the same width.
    bar.style.borderWidth = width < 2 ? '0px' : '1px';
    bar.style.paddingInline = `${Math.min(8, Math.max(0, (width - 2) / 2))}px`;
    bar.style.left = `${left}px`; bar.style.top = `${top}px`; bar.style.width = `${width}px`; bar.hidden = false;
  };
  const targetId = (target: Element): string | undefined => {
    const owner = target.closest<HTMLElement>('[data-editable-node-id],[data-gallery-item-id],.ms-node[data-node-id],.ms-expansion-tray-backdrop,[data-external-window],[data-external-gallery]');
    return owner?.dataset.editableNodeId ?? owner?.dataset.galleryItemId ?? owner?.dataset.nodeId ?? owner?.dataset.traySourceId ?? owner?.dataset.externalWindow ?? owner?.dataset.externalGallery;
  };
  const resolve = (target: Element, point?: { x: number; y: number }): Surface | undefined => {
    const id = targetId(target); if (!id) return;
    // Focusing a source tile must not expose a control above its distant tray.
    const targetBox = target.getBoundingClientRect(), at = point ?? { x: targetBox.left + targetBox.width / 2, y: targetBox.top + targetBox.height / 2 };
    for (const ownerId of [id, ...options.ancestors(id).map(node => node.id)]) {
      const ownCover = ownerId === id && cards.get(id)?.contains(target);
      const candidates = [...(surfaces.get(ownerId) ?? [])].sort((a, b) => Number(ownCover ? a.kind === 'tray' : a.kind === 'node') - Number(ownCover ? b.kind === 'tray' : b.kind === 'node'));
      for (const surface of candidates) {
        const rect = rectFor(surface);
        if (rect && contains(rect, at.x, at.y)) return surface;
      }
    }
  };
  const inCorridor = (point: { x: number; y: number }): boolean => {
    if (!active || bar.hidden) return false;
    const frame = rectFor(active), box = bar.getBoundingClientRect();
    return !!frame && point.x >= box.left - 3 && point.x <= box.right + 3 && point.y >= box.top - 3 && point.y <= Math.max(box.bottom, frame.top + 2);
  };
  viewport.addEventListener('pointermove', event => {
    pointer = { x: event.clientX, y: event.clientY };
    if (!options.enabled()) { hide(); return; }
    const target = event.target instanceof Element ? event.target : undefined;
    if (target === bar) { paint(); return; }
    const surface = target && resolve(target, pointer);
    // A nested bar sits just outside its frame, over its parent's blank rim.
    // Keep the nested target while crossing that short gap to its button.
    if (active && inCorridor(pointer) && (!surface || options.ancestors(active.id).some(node => node.id === surface.id))) paint();
    else if (surface) { keyboard = false; active = surface; paint(); }
    else if (!target?.closest('[data-node-id],[data-gallery-item-id]') && inCorridor(pointer)) paint();
    else if (document.activeElement !== bar) { keyboard = false; hide(); }
  }, true);
  viewport.addEventListener('pointerleave', () => { pointer = undefined; if (document.activeElement !== bar && !keyboard) hide(); });
  viewport.addEventListener('pointerdown', event => {
    if (event.target !== bar) { pointerFocusUntil = performance.now() + 100; keyboard = false; hide(); }
  }, true);
  viewport.addEventListener('focusin', event => {
    if (event.target === bar) { keyboard = true; paint(); return; }
    const target = event.target instanceof Element ? event.target : undefined;
    const surface = target && resolve(target);
    if (surface) { keyboard = performance.now() > pointerFocusUntil; active = surface; paint(); }
  });
  viewport.addEventListener('focusout', () => queueMicrotask(() => {
    if (!viewport.contains(document.activeElement)) { keyboard = false; hide(); }
  }));
  bar.addEventListener('pointerdown', event => {
    event.preventDefault(); event.stopPropagation();
    if (event.button !== 0 || !active || !options.enabled()) return;
    const owner = active;
    options.onHandlePointerDown?.(owner.id, owner.kind, event, () => { keyboard = false; hide(); options.onToggle(owner.id, owner.kind === 'tray' || !owner.node.collapsed); });
  });
  bar.addEventListener('dblclick', event => { event.preventDefault(); event.stopPropagation(); });
  bar.addEventListener('click', event => {
    event.preventDefault(); event.stopPropagation();
    // Pointer releases are completed by the real card/tray gesture; a drag
    // must never replay its following native click as a collapse. Keyboard
    // activation has detail=0 and keeps the ordinary native-button behavior.
    if (event.detail !== 0 && options.onHandlePointerDown) return;
    const owner = active; hide(); keyboard = false;
    if (owner && options.enabled()) options.onToggle(owner.id, owner.kind === 'tray' || !owner.node.collapsed);
  });
  return { update() {
    if (!options.enabled()) { hide(); return; }
    if (active && (keyboard || pointer && (contains(rectFor(active) ?? { left: 1, right: 0, top: 1, bottom: 0, width: 0, height: 0 }, pointer.x, pointer.y) || inCorridor(pointer)))) paint();
    else hide();
  } };
}
