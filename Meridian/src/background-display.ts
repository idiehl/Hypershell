import type { MeridianNode } from './core/types.ts';

/** Only the decorative paint participates. Geometry and content remain native. */
export function prepareBackgroundDisplay(surface: HTMLElement, node: MeridianNode, kind: 'node'|'page'|'tray' = 'node'): void {
  if (node.style?.backgroundDisplay !== 'hover') return;
  surface.dataset.backgroundDisplay = 'hover'; surface.dataset.backgroundOwner = node.id;surface.dataset.backgroundSurface=kind;
  if (kind !== 'tray' && (node.style.shape === 'none' || kind === 'node' && surface.classList.contains('ms-label') || kind === 'page' && node.style.variant === 'label')) return;
  if (kind === 'node' && surface.dataset.shape) return; // Existing clipped shape fill.
  const fill = document.createElement('div'); fill.className = 'ms-node-hover-fill'; fill.setAttribute('aria-hidden','true');
  if (kind === 'page') { fill.classList.add('ms-node-background'); fill.style.background = node.style.fill ?? 'transparent'; }
  surface.prepend(fill);
}

export function markHoverPaint(paint: HTMLElement, opacity: number): void {
  paint.style.setProperty('--ms-background-authored-opacity', String(opacity));
}

const mounts = new Map<HTMLElement, { dispose: () => void }>();
const lastPointers = new WeakMap<HTMLElement, {x:number;y:number}>();
let removals: MutationObserver | undefined;
export function disposeBackgroundDisplay(host: HTMLElement): void { mounts.get(host)?.dispose(); }

/** Flat canvas descendants are presentation siblings, so DOM :hover alone cannot
 * reveal their physical parent. Resolve real ancestry and bounds without a RAF. */
export function mountBackgroundDisplay(options: { host: HTMLElement; viewport: HTMLElement; world: HTMLElement;
  ancestors: (id: string) => MeridianNode[] }): { update: () => void; dispose: () => void } {
  const { host, viewport, world, ancestors } = options;
  disposeBackgroundDisplay(host);
  const abort = new AbortController(), signal = abort.signal;
  let pointer: { x:number; y:number } | undefined=lastPointers.get(host);
  const targetId = (target: Element) => {
    const element = target.closest<HTMLElement>('[data-editable-node-id],[data-gallery-item-id],[data-node-id],[data-tray-source-id],[data-external-gallery],[data-external-window],[data-window-id],.ms-container-hover-bar');
    return element?.dataset.editableNodeId ?? element?.dataset.galleryItemId ?? element?.dataset.nodeId ?? element?.dataset.traySourceId
      ?? element?.dataset.externalGallery ?? element?.dataset.externalWindow ?? element?.dataset.windowId ?? element?.dataset.containerHoverOwner;
  };
  const contains = (surface: HTMLElement, point: {x:number;y:number}) => {
    if (!surface.isConnected || getComputedStyle(surface).visibility === 'hidden' || !surface.getClientRects().length) return false;
    const box = surface.getBoundingClientRect();
    if (point.x < box.left || point.x > box.right || point.y < box.top || point.y > box.bottom) return false;
    for (let parent = surface.parentElement; parent; parent=parent.parentElement) if (parent===viewport || parent.classList.contains('ms-window-viewport')) {
      const clip=parent.getBoundingClientRect(); if(point.x<clip.left||point.x>clip.right||point.y<clip.top||point.y>clip.bottom)return false;
    }
    return true;
  };
  const activeFor = (target: Element | null, point: {x:number;y:number}|undefined, surfaces: HTMLElement[]) => {
    const active = new Set<HTMLElement>();
    if(!target || !viewport.contains(target))return active;
    const id=targetId(target);if(!id)return active;
    const ids=new Set([id,...ancestors(id).map(node=>node.id)]);
    const rect=target.getBoundingClientRect(),at=point??{x:rect.left+rect.width/2,y:rect.top+rect.height/2};
    for(const surface of surfaces)if(ids.has(surface.dataset.backgroundOwner!) &&
      (contains(surface,at)||target.classList.contains('ms-container-hover-bar')&&surface.dataset.backgroundOwner===id&&
        surface.dataset.backgroundSurface===((target as HTMLElement).dataset.hoverSurface==='tray'?'tray':'node')))active.add(surface);
    return active;
  };
  const update=()=>{
    if(!host.isConnected)return;
    const surfaces=[...world.querySelectorAll<HTMLElement>('[data-background-display="hover"]')];
    if(!surfaces.length)return;
    const hovered=activeFor(pointer?document.elementFromPoint(pointer.x,pointer.y):null,pointer,surfaces);
    const focused=activeFor(document.activeElement,undefined,surfaces);
    for(const surface of surfaces){const active=hovered.has(surface)||focused.has(surface);if(surface.dataset.backgroundActive!==String(active))surface.dataset.backgroundActive=String(active);}
  };
  viewport.addEventListener('pointermove',event=>{pointer={x:event.clientX,y:event.clientY};lastPointers.set(host,pointer);update();},{signal,capture:true});
  viewport.addEventListener('pointerleave',()=>{pointer=undefined;lastPointers.delete(host);update();},{signal});
  viewport.addEventListener('focusin',update,{signal});
  viewport.addEventListener('focusout',()=>queueMicrotask(()=>{if(!signal.aborted)update();}),{signal});
  const dispose=()=>{abort.abort();mounts.delete(host);if(!mounts.size){removals?.disconnect();removals=undefined;}};
  mounts.set(host,{dispose});
  if(!removals){removals=new MutationObserver(()=>{for(const [element,mount]of mounts)if(!element.isConnected)mount.dispose();});removals.observe(document.documentElement,{childList:true,subtree:true});}
  update();return{update,dispose};
}
