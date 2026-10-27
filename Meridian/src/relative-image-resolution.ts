/** Raster content is painted at its final screen size, independently of the
 * world-space tile and any fitted Gallery/Window transforms around it. The
 * original IMG still owns layout, accessible content and pointer/keyboard
 * interaction. Both surfaces use the identical original source; no thumbnail,
 * canvas copy or resized asset is ever generated. */
export class RelativeImageResolution {
  private readonly layer = document.createElement('div');
  private readonly entries = new Map<HTMLImageElement, { paint: HTMLImageElement; loaded: () => void }>();
  private readonly resize: ResizeObserver;
  private readonly mutations: MutationObserver;
  private frame = 0;
  private disposed = false;
  private readonly changed = () => this.schedule();

  constructor(private readonly viewport: HTMLElement, world: HTMLElement, private readonly onDispose?: () => void) {
    this.layer.className = 'ms-image-resolution-layer';
    this.layer.setAttribute('aria-hidden', 'true');
    viewport.append(this.layer);
    this.resize = new ResizeObserver(this.changed); this.resize.observe(viewport);
    this.mutations = new MutationObserver(this.changed);
    this.mutations.observe(world, { subtree: true, childList: true, attributes: true,
      attributeFilter: ['style', 'class', 'src', 'hidden'] });
    // A host may hide a scene without resizing it, or discard its final scene
    // without rendering another. Observe that outer lifecycle as well as the
    // world, so re-showing restores paint and removal releases every listener.
    for (let parent: HTMLElement | null = viewport; parent; parent = parent.parentElement)
      this.mutations.observe(parent, { childList: true, attributes: true, attributeFilter: ['style', 'class', 'hidden'] });
    // Scroll offsets do not resize an image or change the camera transform.
    viewport.addEventListener('scroll', this.changed, true);
    viewport.addEventListener('focusin', this.changed); viewport.addEventListener('focusout', this.changed);
    window.addEventListener('resize', this.changed);
  }

  register(source: HTMLImageElement): void {
    if (this.disposed || this.entries.has(source)) return;
    const paint = new Image(); paint.className = 'ms-resolution-image';
    paint.alt = ''; paint.draggable = false; paint.decoding = 'async';
    paint.setAttribute('aria-hidden', 'true'); paint.style.display = 'none';
    const loaded = this.changed;
    source.addEventListener('load', loaded); source.addEventListener('error', loaded);
    paint.addEventListener('load', loaded); paint.addEventListener('error', loaded);
    paint.src = source.currentSrc || source.src;
    this.entries.set(source, { paint, loaded }); this.layer.append(paint);
    this.resize.observe(source); this.schedule();
  }

  private schedule(): void {
    if (this.disposed || this.frame) return;
    this.frame = requestAnimationFrame(() => { this.frame = 0; this.refresh(); });
  }

  private remove(source: HTMLImageElement): void {
    const entry = this.entries.get(source); if (!entry) return;
    source.classList.remove('ms-resolution-source'); this.resize.unobserve(source);
    source.removeEventListener('load', entry.loaded); source.removeEventListener('error', entry.loaded);
    entry.paint.removeEventListener('load', entry.loaded); entry.paint.removeEventListener('error', entry.loaded);
    entry.paint.remove(); this.entries.delete(source);
  }

  private refresh(): void {
    if (!this.viewport.isConnected) { this.dispose(); return; }
    const viewport = this.viewport.getBoundingClientRect();
    // Read every source before writing paint geometry, so a camera gesture
    // needs one layout observation rather than alternating read/write flushes.
    const frames = [...this.entries].map(([source, entry]) => {
      if (!source.isConnected || !this.viewport.contains(source)) { this.remove(source); return; }
      const rect = source.getBoundingClientRect(), style = getComputedStyle(source);
      let left = Math.max(rect.left, viewport.left), top = Math.max(rect.top, viewport.top);
      let right = Math.min(rect.right, viewport.right), bottom = Math.min(rect.bottom, viewport.bottom);
      for (let parent = source.parentElement; parent && parent !== this.viewport; parent = parent.parentElement) {
        const overflow = getComputedStyle(parent);
        if (!['hidden', 'clip', 'auto', 'scroll'].some(value => overflow.overflowX === value || overflow.overflowY === value)) continue;
        const clip = parent.getBoundingClientRect();
        if (overflow.overflowX !== 'visible') { left = Math.max(left, clip.left); right = Math.min(right, clip.right); }
        if (overflow.overflowY !== 'visible') { top = Math.max(top, clip.top); bottom = Math.min(bottom, clip.bottom); }
      }
      const localWidth = parseFloat(style.width), localHeight = parseFloat(style.height);
      const scaleX = rect.width / Math.max(.001, localWidth || rect.width), scaleY = rect.height / Math.max(.001, localHeight || rect.height);
      const radius = (value: string) => value.includes('%') ? value : value.split(/\s+/).map((part, index) =>
        `${(parseFloat(part) || 0) * (index ? scaleY : scaleX)}px`).join(' ');
      const url = source.currentSrc || source.src;
      const visible = style.visibility !== 'hidden' && rect.width > 0 && rect.height > 0 && right > left && bottom > top;
      return { source, ...entry, rect, url, visible, objectFit: style.objectFit, objectPosition: style.objectPosition,
        background: style.backgroundColor, radii: [style.borderTopLeftRadius, style.borderTopRightRadius,
          style.borderBottomRightRadius, style.borderBottomLeftRadius].map(radius),
        focus: source.matches(':focus-visible'),
        clip: `inset(${Math.max(0, top - rect.top)}px ${Math.max(0, rect.right - right)}px ${Math.max(0, rect.bottom - bottom)}px ${Math.max(0, left - rect.left)}px)` };
    });
    for (const frame of frames) {
      if (!frame) continue;
      const { source, paint } = frame;
      if (paint.src !== frame.url) paint.src = frame.url;
      // Keep the native source visible until its full source paint surface has
      // decoded. A failed image also retains the browser's original alt/error.
      const ready = paint.complete && paint.naturalWidth > 0 && source.complete && source.naturalWidth > 0;
      source.classList.toggle('ms-resolution-source', ready);
      paint.style.display = ready && frame.visible ? 'block' : 'none';
      if (!ready || !frame.visible) continue;
      Object.assign(paint.style, { left: `${frame.rect.left - viewport.left}px`, top: `${frame.rect.top - viewport.top}px`,
        width: `${frame.rect.width}px`, height: `${frame.rect.height}px`, objectFit: frame.objectFit,
        objectPosition: frame.objectPosition, backgroundColor: frame.background, clipPath: frame.clip,
        borderTopLeftRadius: frame.radii[0], borderTopRightRadius: frame.radii[1],
        borderBottomRightRadius: frame.radii[2], borderBottomLeftRadius: frame.radii[3],
        outline: frame.focus ? '2px solid #78e8d0' : 'none', outlineOffset: '-2px' });
    }
  }

  dispose(): void {
    if (this.disposed) return;
    this.disposed = true; if (this.frame) cancelAnimationFrame(this.frame); this.frame = 0;
    this.resize.disconnect(); this.mutations.disconnect();
    this.viewport.removeEventListener('scroll', this.changed, true);
    this.viewport.removeEventListener('focusin', this.changed); this.viewport.removeEventListener('focusout', this.changed);
    window.removeEventListener('resize', this.changed);
    for (const source of this.entries.keys()) this.remove(source);
    this.layer.remove(); this.onDispose?.();
  }
}
