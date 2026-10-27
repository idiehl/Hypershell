import type { MeridianProject } from './core/types.ts';
import { createRichTextEditor, type RichTextEditor, type RichTextValue } from './rich-text-editor.ts';
import { fitContainedContent } from './contained-content.ts';
import { activeNodeContent, nodeContentItems } from './core/content.ts';
import { applyHubContent, resolveHubView } from './core/hub-source.ts';

export interface CanvasTextEditingCallbacks {
  onChange(nodeId: string, title: boolean, value: RichTextValue): boolean | void;
  onSelect?(nodeId: string): void;
  onFinish?(): void;
  onEscape?(): void;
}
interface Session { editor: RichTextEditor; surface: HTMLElement; nodeId: string; title: boolean }

/** Authoring-only controller. The glyph surface remains in the shared scene;
 * formatting controls use screen pixels outside the zoomed world. */
export function mountCanvasTextEditing(host: HTMLElement, source: MeridianProject | (() => MeridianProject), callbacks: CanvasTextEditingCallbacks) {
  const project = () => typeof source === 'function' ? source() : source;
  const abort = new AbortController();
  let session: Session | undefined, finishing = false, disposed = false;
  let outsideRelease: AbortController | undefined;
  let positionFrame = 0, fitFrame = 0;
  const owns = (target: Node | null) => !!session?.editor.owns(target);
  const flush = (finishComposition = false) => session?.editor.flush(finishComposition);
  const sync = () => {
    const current = session, node = current && project().nodes.find(candidate => candidate.id === current.nodeId);
    if (!current || !node) return;
    const content = activeNodeContent(resolveHubView(project(), node).node);
    current.editor.sync(current.title ? node.title : content.text ?? '', current.title ? node.titleRuns : content.textRuns, node.style);
    scheduleFit();
  };
  const finish = (notify = true) => {
    if (!session || finishing) return;
    finishing = true;
    const current = session;
    try {
      outsideRelease?.abort(); outsideRelease = undefined;
      current.editor.flush(true);
      session = undefined;
      current.editor.dispose(); current.editor.root.remove();
      if (document.activeElement === current.surface) current.surface.blur();
      if (notify) callbacks.onFinish?.();
    } finally { finishing = false; }
  };

  const position = () => {
    positionFrame = 0;
    const current = session; if (!current) return;
    if (!current.surface.isConnected || !host.contains(current.surface)) { finish(); return; }
    const bounds = current.editor.selectionBounds(), toolbar = current.editor.root;
    // A caret is unobstructed. A passage's controls persist while font/size/
    // color inputs own focus, using the editor's saved rich text range.
    toolbar.hidden = !bounds;
    if (!bounds) return;
    const workspace = host.getBoundingClientRect();
    const leftEdge = Math.max(8, workspace.left + 8), rightEdge = Math.min(window.innerWidth - 8, workspace.right - 8);
    const topEdge = Math.max(8, workspace.top + 8), bottomEdge = Math.min(window.innerHeight - 8, workspace.bottom - 8);
    // A selection outside the current viewport does not float over the chrome.
    if (bounds.bottom < topEdge || bounds.top > bottomEdge || bounds.right < leftEdge || bounds.left > rightEdge) { toolbar.hidden = true; return; }
    toolbar.style.maxWidth = `${Math.max(1, rightEdge - leftEdge)}px`;
    const width = toolbar.offsetWidth, height = toolbar.offsetHeight;
    const left = Math.max(leftEdge, Math.min(bounds.left + bounds.width / 2 - width / 2, rightEdge - width));
    const above = bounds.top - height - 7;
    const top = Math.max(topEdge, Math.min(above >= topEdge ? above : bounds.bottom + 7, bottomEdge - height));
    toolbar.style.left = `${left}px`; toolbar.style.top = `${top}px`;
  };
  const schedulePosition = () => { if (!positionFrame && !disposed) positionFrame = requestAnimationFrame(position); };
  const refit = () => {
    fitFrame = 0;
    const current = session; if (!current?.surface.isConnected) return;
    const card = current.surface.closest<HTMLElement>('.ms-node');
    const currentProject = project(), owner = card && currentProject.nodes.find(node => node.id === card.dataset.nodeId);
    if (!card || !owner) { schedulePosition(); return; }
    // Native input paints immediately; the save callback is debounced. Measure
    // the pending text too without mutating the stored project prematurely.
    const value = current.editor.getValue();
    let pending: MeridianProject;
    if (current.title) pending = { ...currentProject, nodes: currentProject.nodes.map(node => node.id === current.nodeId ? { ...node, title: value.text, titleRuns: value.runs } : node) };
    else {
      const hubView = resolveHubView(currentProject, owner), items = nodeContentItems(hubView.node), first = items[0];
      if (first?.type === 'text') { first.text = value.text; if (value.runs.length) first.textRuns = value.runs; else delete first.textRuns; }
      else items.unshift({ id: crypto.randomUUID(), type: 'text', text: value.text, ...(value.runs.length ? { textRuns: value.runs } : {}) });
      pending = applyHubContent(currentProject, owner.id, items, [], hubView);
    }
    const pendingOwner = pending.nodes.find(node => node.id === owner.id)!;
    const galleryItems: Record<string, string> = {};
    for (const gallery of card.querySelectorAll<HTMLElement>('.ms-gallery[data-gallery-id]')) {
      const item = gallery.querySelector<HTMLElement>('.ms-gallery-item[data-gallery-item-id]');
      if (gallery.dataset.galleryId && item?.dataset.galleryItemId) galleryItems[gallery.dataset.galleryId] = item.dataset.galleryItemId;
    }
    fitContainedContent(card, pending, pendingOwner, {
      x: Number.parseFloat(card.style.left) || owner.x, y: Number.parseFloat(card.style.top) || owner.y,
      width: Number.parseFloat(card.style.width) || owner.width, height: Number.parseFloat(card.style.height) || owner.height
    }, { galleryItems });
    schedulePosition();
  };
  const scheduleFit = () => { if (!fitFrame && !disposed) fitFrame = requestAnimationFrame(refit); };
  const start = (surface: HTMLElement, nodeId: string, title: boolean): boolean => {
    if (disposed || !surface.isConnected || !host.contains(surface)) return false;
    if (session?.surface === surface) return true;
    const node = project().nodes.find(candidate => candidate.id === nodeId); if (!node || node.kind === 'block') return false;
    const content = activeNodeContent(resolveHubView(project(), node).node);
    // Switching passages must not replace the just-clicked DOM surface.
    finish(false);
    const editor = createRichTextEditor({
      inlineSurface: surface, label: title ? 'Work node title' : 'Work node text', title, base: node.style,
      text: title ? node.title : content.text ?? '', runs: (title ? node.titleRuns : content.textRuns) ?? [],
      maxLength: title ? 240 : 1_000_000,
      onChange: value => callbacks.onChange(nodeId, title, value),
      onSelectionChange: schedulePosition, onRender: scheduleFit
    });
    editor.root.hidden = true; document.body.append(editor.root);
    session = { editor, surface, nodeId, title }; callbacks.onSelect?.(nodeId);
    schedulePosition(); return true;
  };

  document.addEventListener('keydown', event => {
    if (event.key !== 'Escape' || event.defaultPrevented || event.isComposing || !session || !owns(event.target as Node)) return;
    // An open font chooser consumes its own Escape before this listener.
    event.preventDefault(); event.stopPropagation(); finish(false); callbacks.onEscape?.();
  }, { signal: abort.signal });
  document.addEventListener('pointerdown', event => {
    if (!session || owns(event.target as Node)) return;
    const target = event.target as Element;
    // Scene starts another canonical text surface in this same pointer event.
    if (target.closest?.('[data-editable-text]')) return;
    flush();
    // Let the original canvas/control click complete before replacing its DOM.
    outsideRelease?.abort(); outsideRelease = new AbortController();
    const previous = session, signal = outsideRelease.signal;
    const release = (next: PointerEvent) => {
      if (next.pointerId !== event.pointerId) return;
      outsideRelease?.abort(); outsideRelease = undefined;
      // Pointer-up's native click and the scene's drag transaction finish first.
      // A redraw during the hold would cancel capture or discard the button.
      setTimeout(() => { if (session === previous) finish(); }, 0);
    };
    window.addEventListener('pointerup', release, { capture: true, signal });
    window.addEventListener('pointercancel', release, { capture: true, signal });
  }, { capture: true, signal: abort.signal });
  document.addEventListener('scroll', schedulePosition, { capture: true, signal: abort.signal });
  window.addEventListener('resize', schedulePosition, { signal: abort.signal });
  document.addEventListener('selectionchange', schedulePosition, { signal: abort.signal });
  const observer = new MutationObserver(records => {
    if (session && !session.surface.isConnected) finish();
    else if (records.some(record => record.type === 'childList' || (record.target as Element).classList?.contains('ms-world'))) schedulePosition();
  });
  // Observe topology/camera changes, not fitting attributes on every glyph.
  observer.observe(host, { childList: true, subtree: true, attributes: true, attributeFilter: ['style'] });
  const resize = new ResizeObserver(schedulePosition); resize.observe(host);
  return {
    start, flush, sync, finish, owns,
    get active() { return !!session; },
    get nodeId() { return session?.nodeId; },
    get surface() { return session?.surface; },
    dispose() { finish(false); disposed = true; outsideRelease?.abort(); abort.abort(); observer.disconnect(); resize.disconnect(); cancelAnimationFrame(positionFrame); cancelAnimationFrame(fitFrame); }
  };
}

export type CanvasTextEditing = ReturnType<typeof mountCanvasTextEditing>;
