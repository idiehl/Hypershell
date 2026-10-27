import type { MeridianProject } from './core/types.ts';
import { navigationIndex } from './core/navigation.ts';
import { gridGeometry, isEnclosingContainer } from './core/cells.ts';
import { gallerySelection } from './core/gallery.ts';
import { externalGalleryNavigationBounds, nodeSizingMode, type SizingContext } from './core/sizing.ts';
import { fitContainedContent } from './contained-content.ts';
import { windowSurface, type WindowOffsets } from './window-scene.ts';

/** Repaint existing real card surfaces from the exact candidate document used
 * by the resize transaction. No duplicate cards, cloned media, or scaled bitmap
 * layers are introduced by a preview, and native text stays editable afterward. */
export function previewTrayGeometry(project: MeridianProject, context: SizingContext, world: HTMLElement,
  cards: Map<string, HTMLElement>, outlines: Map<string, (width: number, height: number) => void>, offsets: WindowOffsets): void {
  const index = navigationIndex(project, context);
  for (const viewport of world.querySelectorAll<HTMLElement>('.ms-window-viewport[data-window-id]')) {
    const node = index.nodes.get(viewport.dataset.windowId!); if (!node) continue;
    const surface = windowSurface(node, project), enclosing = isEnclosingContainer(node);
    const outerId = viewport.parentElement?.closest<HTMLElement>('.ms-window-viewport')?.dataset.windowId;
    const outer = outerId && index.nodes.get(outerId), origin = outer ? windowSurface(outer, project).geometry : undefined;
    const x = enclosing ? node.x : origin?.left ?? 0, y = enclosing ? node.y : origin?.top ?? 0;
    Object.assign(viewport.style, { left: `${surface.geometry.left - x}px`, top: `${surface.geometry.top - y}px`, width: `${surface.width}px`, height: `${surface.height}px` });
    const content = viewport.querySelector<HTMLElement>(':scope > .ms-window-content');
    if (content) Object.assign(content.style, { width: `${surface.fullWidth}px`, height: `${surface.fullHeight}px` });
  }
  for (const [id, card] of cards) {
    const node = index.nodes.get(id); if (!node) continue;
    const bounds = index.bounds(node), ownerId = card.parentElement?.closest<HTMLElement>('.ms-window-viewport')?.dataset.windowId;
    const owner = ownerId && index.nodes.get(ownerId), origin = owner ? windowSurface(owner, project).geometry : undefined;
    Object.assign(card.style, { left: `${bounds.x - (origin?.left ?? 0)}px`, top: `${bounds.y - (origin?.top ?? 0)}px`, width: `${bounds.width}px`, height: `${bounds.height}px` });
    card.classList.toggle('ms-auto-size', nodeSizingMode(node) === 'auto'); outlines.get(id)?.(bounds.width, bounds.height);
  }
  for (const grid of world.querySelectorAll<HTMLElement>('.ms-cell-grid')) {
    const id = grid.dataset.externalGrid ?? grid.parentElement?.closest<HTMLElement>('.ms-node')?.dataset.nodeId;
    const node = id ? index.nodes.get(id) : undefined; if (!node?.grid) continue;
    const geometry = gridGeometry(node, project), enclosed = isEnclosingContainer(node);
    const originX = enclosed ? node.x : 0, originY = enclosed ? node.y : 0;
    const headers = [...grid.querySelectorAll<HTMLElement>(':scope > .ms-grid-header')];
    for (const [at, header] of headers.entries()) {
      const column = at < geometry.columns ? at : -1, row = at - geometry.columns;
      Object.assign(header.style, column >= 0
        ? { left: `${geometry.left + column * (geometry.cellWidth + geometry.gap) - originX}px`, top: `${geometry.top - geometry.headerHeight - originY}px`, width: `${geometry.cellWidth}px`, height: `${geometry.headerHeight}px` }
        : { left: `${geometry.left - geometry.headerWidth - originX}px`, top: `${geometry.top + row * (geometry.cellHeight + geometry.gap) - originY}px`, width: `${geometry.headerWidth}px`, height: `${geometry.cellHeight}px` });
    }
    for (const cell of grid.querySelectorAll<HTMLElement>('[data-cell-address]')) {
      const [column, row] = cell.dataset.cellAddress!.split(':').map(Number); if (!column || !row) continue;
      Object.assign(cell.style, { left: `${geometry.left + (column - 1) * (geometry.cellWidth + geometry.gap) - originX}px`, top: `${geometry.top + (row - 1) * (geometry.cellHeight + geometry.gap) - originY}px`, width: `${geometry.cellWidth}px`, height: `${geometry.cellHeight}px` });
    }
  }
  for (const controls of world.querySelectorAll<HTMLElement>('[data-external-gallery]')) {
    const parent = index.nodes.get(controls.dataset.externalGallery!); if (!parent) continue;
    const active = context.galleryItems instanceof Map ? context.galleryItems.get(parent.id) : (context.galleryItems as Readonly<Record<string, string>> | undefined)?.[parent.id];
    const { item } = gallerySelection(project, parent, active); if (!item) continue;
    const bounds = externalGalleryNavigationBounds(project, parent, item, { context, children: id => index.children.get(id) ?? [] }, index.bounds(item));
    Object.assign(controls.style, { left: `${bounds.x}px`, top: `${bounds.y}px`, width: `${bounds.width / bounds.scale}px`, height: `${bounds.height / bounds.scale}px`, transform: `scale(${bounds.scale})` });
  }
  // All physical frames are in place before measuring their natural reading
  // compositions, including a bounded Gallery's existing image/video viewport.
  for (const [id, card] of cards) {
    const node = index.nodes.get(id); if (node) fitContainedContent(card, project, node, index.bounds(node), context);
  }
  for (const viewport of world.querySelectorAll<HTMLElement>('.ms-window-viewport[data-window-id]')) {
    const at = offsets.get(viewport.dataset.windowId!); if (at) { viewport.scrollLeft = at.x; viewport.scrollTop = at.y; }
  }
}
