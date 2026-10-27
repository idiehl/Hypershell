import type { MeridianProject } from './core/types.ts';
import { gallerySelection, galleryStep } from './core/gallery.ts';
import { inspectorNodeType } from './inspector-panels.ts';
import { nodeTag } from './core/node-tag.ts';

type Action = () => void | Promise<void>;
export interface GalleryContentsOptions {
  project: MeridianProject;
  nodeId: string;
  activeItemId?: string;
  /** Browsing is window state. The caller must not persist it as a document edit. */
  onPage: (itemId: string) => void | Promise<void>;
  onInspect: (itemId: string) => void | Promise<void>;
  onAddChild: Action;
  /** Rearranging is an ordinary undoable edit, unlike browsing. */
  onReorder: (itemId: string, direction: -1 | 1) => void | Promise<void>;
}

function element<K extends keyof HTMLElementTagNameMap>(tag: K, className: string, text?: string): HTMLElementTagNameMap[K] {
  const result = document.createElement(tag); result.className = className; if (text !== undefined) result.textContent = text; return result;
}
function button(text: string, label: string, run: Action, className: string): HTMLButtonElement {
  const result = element('button', className, text); result.type = 'button'; result.title = label; result.setAttribute('aria-label', label); result.onclick = () => { void run(); }; return result;
}

/** Ordered gallery children remain real nodes, with their own editable content. */
export function renderGalleryContents(host: HTMLElement, options: GalleryContentsOptions): void {
  const state = gallerySelection(options.project, options.nodeId, options.activeItemId);
  const section = element('section', 'inspector-gallery'); section.dataset.galleryContents = options.nodeId; section.setAttribute('aria-label', 'Gallery objects');
  section.append(element('h3', 'inspector-section-title', `Gallery objects · ${state.items.length}`));
  const hint = element('p', 'inspector-gallery-hint', 'Display one object at a time. Select an object to show it in the gallery; inspect it to edit its content.'); section.append(hint);
  const error = element('p', 'inspector-error'); error.setAttribute('role', 'alert');
  const action = async (run: Action) => { error.textContent = ''; try { await run(); } catch (failure) { error.textContent = failure instanceof Error ? failure.message : String(failure); } };
  if (state.items.length) {
    const pager = element('div', 'inspector-gallery-pager'); pager.setAttribute('role', 'group'); pager.setAttribute('aria-label', 'Browse gallery objects');
    const previous = button('←', 'Previous gallery object', () => action(() => {
      const id = galleryStep(options.project, options.nodeId, options.activeItemId, -1); if (id) return options.onPage(id);
    }), 'quiet inspector-gallery-page');
    previous.disabled = state.index <= 0;
    const position = element('span', 'inspector-gallery-position', `${state.index + 1} / ${state.items.length}`); position.setAttribute('aria-live', 'polite');
    const next = button('→', 'Next gallery object', () => action(() => {
      const id = galleryStep(options.project, options.nodeId, options.activeItemId, 1); if (id) return options.onPage(id);
    }), 'quiet inspector-gallery-page');
    next.disabled = state.index >= state.items.length - 1; pager.append(previous, position, next); section.append(pager);
    const list = element('ol', 'inspector-gallery-list');
    for (let at = 0; at < state.items.length; at++) {
      const item = state.items[at]!, title = nodeTag(item);
      const row = element('li', 'inspector-gallery-object'); row.dataset.galleryObject = item.id; row.classList.toggle('is-current', item.id === state.item?.id);
      const description = element('div', 'inspector-gallery-description');
      const show = button(title, `Show ${title} in gallery`, () => action(() => options.onPage(item.id)), 'quiet inspector-gallery-title'); show.setAttribute('aria-pressed', String(item.id === state.item?.id));
      description.append(show, element('small', 'inspector-gallery-type', `${at + 1} · ${inspectorNodeType(item)}`));
      const actions = element('div', 'inspector-gallery-actions');
      const inspect = button('↗', `Inspect gallery object: ${title}`, () => action(() => options.onInspect(item.id)), 'quiet');
      const earlier = button('↑', `Move ${title} earlier`, () => action(() => options.onReorder(item.id, -1)), 'quiet'); earlier.disabled = at === 0;
      const later = button('↓', `Move ${title} later`, () => action(() => options.onReorder(item.id, 1)), 'quiet'); later.disabled = at === state.items.length - 1;
      actions.append(inspect, earlier, later); row.append(description, actions); list.append(row);
    }
    section.append(list);
  } else section.append(element('p', 'inspector-empty', 'This gallery is empty. Add its first object below, or create a child node directly on the gallery.'));
  section.append(button('Add object', 'Add gallery object', () => action(options.onAddChild), 'secondary full inspector-gallery-add'), error); host.append(section);
}
