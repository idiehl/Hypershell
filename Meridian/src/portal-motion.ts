/** Web Animations owns travel. JavaScript runs for speed ramps and visible labels,
 * never as a perpetual loop for each idle canvas tile. */
const controllers = new Map<HTMLButtonElement, PortalMotion>();
let intersection: IntersectionObserver | undefined, removal: MutationObserver | undefined;
let preference: MediaQueryList | undefined;

function depthFrames(from: number, to: number): Keyframe[] {
  return Array.from({ length: 25 }, (_, index) => {
    const progress = index / 24;
    return { offset: progress, transform: `scale(${from * Math.pow(to / from, progress)})`,
      opacity: Math.min(1, progress / .18, (1 - progress) / .13) * .86 };
  });
}
function updateAll() { for (const controller of controllers.values()) controller.update(); }
function startShared() {
  if (removal) return;
  preference = matchMedia('(prefers-reduced-motion: reduce)');
  preference.addEventListener('change', updateAll); document.addEventListener('visibilitychange', updateAll);
  intersection = new IntersectionObserver(entries => {
    for (const entry of entries) { const controller = controllers.get(entry.target as HTMLButtonElement); if (controller) { controller.visible = entry.isIntersecting; controller.update(); } }
  }, { threshold: .01 });
  removal = new MutationObserver(records => {
    for (const [button, controller] of controllers) if (!button.isConnected) controller.dispose();
    // Window/tray presentation can hide real cards without detaching them. Only
    // visibility-related mutations refresh those descendants; camera transforms
    // and per-frame screen-space caption coordinates do not scan the scene.
    const targets = new Set<Element>();
    for (const record of records) if (record.type === 'attributes' && record.target instanceof Element) {
      if (record.attributeName !== 'style' || /(?:^|;)\s*(?:display|visibility|content-visibility)\s*:/i.test(`${record.oldValue ?? ''};${record.target.getAttribute('style') ?? ''}`)) targets.add(record.target);
    }
    for (const controller of controllers.values()) if ([...targets].some(target => target === controller.button || target.contains(controller.button))) controller.update();
  });
  removal.observe(document.documentElement, { childList: true, subtree: true, attributes: true, attributeOldValue: true, attributeFilter: ['style','class','hidden'] });
}
function stopShared() {
  if (controllers.size) return;
  intersection?.disconnect(); intersection = undefined; removal?.disconnect(); removal = undefined;
  preference?.removeEventListener('change', updateAll); preference = undefined;
  document.removeEventListener('visibilitychange', updateAll);
}

class PortalMotion {
  readonly animations: Animation[] = [];
  visible = true;
  private hovered = false; private focused = false; private rate = 1; private cssVisible = true;
  private frame = 0; private last = 0; private disposed = false;
  private caption?: HTMLElement;
  private listeners = new AbortController();
  constructor(readonly button: HTMLButtonElement, readonly name: string) {
    for (const layer of button.querySelectorAll<SVGElement>('[data-portal-motion]')) {
      const duration = Number(layer.dataset.duration ?? 8) * 1000;
      const frames = layer.dataset.portalMotion === 'rotate'
        ? [{ transform: 'rotate(0deg)' }, { transform: `rotate(${360 * Number(layer.dataset.direction ?? 1)}deg)` }]
        : depthFrames(Number(layer.dataset.fromScale ?? .08), Number(layer.dataset.toScale ?? 1.06));
      const animation = layer.animate(frames, { duration, iterations: Infinity, easing: 'linear', fill: 'both' });
      animation.pause(); animation.currentTime = Number(layer.dataset.delay ?? 0) * duration; this.animations.push(animation);
    }
    const signal = this.listeners.signal;
    button.addEventListener('pointerenter', () => { this.hovered = true; this.update(); }, { signal });
    button.addEventListener('pointerleave', () => { this.hovered = false; this.update(); }, { signal });
    button.addEventListener('focus', () => { this.focused = true; this.update(); }, { signal });
    button.addEventListener('blur', () => { this.focused = false; this.update(); }, { signal });
    button.addEventListener('keydown', event => { if (event.key === 'Escape') { this.hovered = false; button.blur(); this.update(); } }, { signal });
  }
  private get active() { return this.hovered || this.focused; }
  private get stopped() { return !this.button.isConnected || document.hidden || !this.visible || !this.cssVisible || !!preference?.matches; }
  update() {
    if (this.disposed) return;
    const visibility = getComputedStyle(this.button).visibility;
    this.cssVisible = this.button.getClientRects().length > 0 && visibility !== 'hidden' && visibility !== 'collapse';
    this.button.dataset.portalActive = String(this.active);
    this.button.dataset.portalReduced = String(!!preference?.matches);
    this.button.dataset.portalPaused = String(this.stopped);
    for (const animation of this.animations) this.stopped ? animation.pause() : animation.play();
    if (!this.active || !this.visible || !this.cssVisible || document.hidden || !this.button.isConnected) { this.caption?.remove(); this.caption = undefined; }
    else if (!this.caption) {
      this.caption = document.createElement('span'); this.caption.className = 'ms-canvas-portal-tooltip ms-canvas-destination';
      this.caption.textContent = this.name; this.caption.setAttribute('aria-hidden', 'true');
      this.caption.style.fontFamily = getComputedStyle(this.button).fontFamily;
      document.body.append(this.caption);
    }
    // A reduced-motion caption tracks camera movement without animating art.
    if (this.stopped && !this.caption) { cancelAnimationFrame(this.frame); this.frame = 0; this.last = 0; }
    else if (!this.frame) this.frame = requestAnimationFrame(time => this.tick(time));
  }
  private tick(time: number) {
    this.frame = 0; if (this.disposed) return;
    if (!this.button.isConnected) { this.dispose(); return; }
    if (this.caption) {
      const box = this.button.getBoundingClientRect(), caption = this.caption;
      caption.style.left = `${Math.min(innerWidth - 8, Math.max(8, box.x + box.width / 2))}px`;
      caption.style.top = `${Math.max(6, box.top - 9)}px`;
      const width = caption.getBoundingClientRect().width, center = Math.min(innerWidth - width / 2 - 8, Math.max(width / 2 + 8, box.x + box.width / 2));
      caption.style.left = `${center}px`;
      if (box.top < caption.getBoundingClientRect().height + 18) { caption.style.top = `${Math.min(innerHeight - 6, box.bottom + 9)}px`; caption.dataset.below = 'true'; }
      else delete caption.dataset.below;
    }
    if (!this.stopped) {
      const dt = this.last ? Math.min(64, time - this.last) : 16;
      const target = this.active ? 4 : 1; this.rate += (target - this.rate) * (1 - Math.exp(-dt / 130));
      if (Math.abs(target - this.rate) < .003) this.rate = target;
      for (const animation of this.animations) animation.updatePlaybackRate(this.rate);
      this.button.dataset.portalRate = this.rate.toFixed(4);
    }
    this.last = time;
    if (this.caption || !this.stopped && this.rate !== (this.active ? 4 : 1)) this.frame = requestAnimationFrame(next => this.tick(next));
    else this.last = 0;
  }
  dispose() {
    if (this.disposed) return; this.disposed = true;
    cancelAnimationFrame(this.frame); this.frame = 0; this.listeners.abort(); this.caption?.remove(); this.caption = undefined;
    for (const animation of this.animations) animation.cancel();
    intersection?.unobserve(this.button); controllers.delete(this.button); stopShared();
  }
}

export function mountPortalMotion(button: HTMLButtonElement, destinationName: string): void {
  startShared(); const controller = new PortalMotion(button, destinationName); controllers.set(button, controller);
  // Shared renderers build cards before attaching them to the world.
  queueMicrotask(() => { if (!button.isConnected) controller.dispose(); else { intersection?.observe(button); controller.update(); } });
}
/** Deterministic cleanup before scene replacement or Gallery paging. The shared
 * observer also covers a host removing an entire scene without rerendering. */
export function disposePortalLaunchers(root: HTMLElement): void {
  for (const [button, controller] of controllers) if (button === root || root.contains(button)) controller.dispose();
}
