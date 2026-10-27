import type { MeridianNode, MeridianProject } from './core/types.ts';
import { nodeContentLayout, type ChildGeometry, type SizingContext } from './core/sizing.ts';
import { contentFrameScale } from './core/content-scale.ts';
import { layoutTextRotations } from './text-rotation.ts';

const properties = ['width', 'height', 'min-width', 'min-height', 'max-width', 'max-height', 'padding', 'margin', 'display', 'flex', 'gap', 'box-sizing',
  'overflow', 'position', 'left', 'top', 'transform', 'transform-origin', 'align-self', 'justify-content', 'align-items'] as const;
type Saved = { element: HTMLElement; declarations: { property: string; value: string; priority: string }[] };
const previous = new WeakMap<HTMLElement, Saved[]>();
const pixel = (value: string) => Number.parseFloat(value) || 0;
const positive = (value: number) => Math.max(Number.MIN_VALUE, value);

export function restoreCanvasLauncherFit(root: HTMLElement): void {
  for (const surface of previous.get(root) ?? []) for (const entry of surface.declarations) {
    if (entry.value) surface.element.style.setProperty(entry.property, entry.value, entry.priority);
    else surface.element.style.removeProperty(entry.property);
  }
  previous.delete(root); delete root.dataset.canvasLauncherFit;
}
function remember(root: HTMLElement, elements: Array<HTMLElement | null>) {
  previous.set(root, elements.filter((item): item is HTMLElement => !!item).map(element => ({ element,
    declarations: properties.map(property => ({ property, value: element.style.getPropertyValue(property), priority: element.style.getPropertyPriority(property) })) })));
}
function copySurface(body: HTMLElement, excluded: HTMLElement[]) {
  let copy = body.querySelector<HTMLElement>(':scope > .ms-canvas-launch-copy');
  if (!copy) {
    copy = document.createElement('div'); copy.className = 'ms-canvas-launch-copy';
    body.append(copy);
  }
  for (const child of [...body.children]) if (child instanceof HTMLElement && child !== copy && !excluded.includes(child) && !child.classList.contains('ms-node-background')) copy.append(child);
  return copy;
}
/** The title and optional lower caption fit separately; neither can resize or
 * move the cube. Native fonts and rich-text ratios remain authored data. */
function textSurface(surface: HTMLElement | null, area: ChildGeometry, baseline: number, fixed: boolean) {
  if (!surface) return;
  const readingWidth = positive(area.width / baseline);
  Object.assign(surface.style, { position: 'absolute', left: '0', top: '0', width: `${readingWidth}px`, height: 'auto', minHeight: '0', maxHeight: 'none',
    padding: '0', margin: '0', boxSizing: 'border-box', overflow: 'visible', flex: 'none', transform: 'none', transformOrigin: '0 0' });
  layoutTextRotations(surface, readingWidth);
  const rotated = [...surface.querySelectorAll<HTMLElement>('.ms-rotated-text-frame')].map(frame => pixel(frame.style.width));
  const width = Math.max(readingWidth, surface.scrollWidth, ...rotated), height = Math.max(.001, surface.offsetHeight, surface.scrollHeight);
  const scale = fixed ? baseline : Math.min(baseline, area.width / width, area.height / height);
  surface.style.width = `${width}px`;
  surface.style.transform = `translate(${area.x + Math.max(0, area.width - width * scale) / 2}px, ${area.y}px) scale(${positive(scale)})`;
}
function doorway(root: HTMLElement, title: HTMLElement | null, copy: HTMLElement, button: HTMLElement,
  width: number, height: number, gap: number, fixed: boolean, fontBaseline = 1) {
  // This footprint depends solely on the safe frame, with sufficient empty SVG
  // view-box area for the complete hover rotation. Titles never enter it.
  const diameter = Math.min(width * .52, height * .4), centerX = width / 2, centerY = height / 2;
  Object.assign(button.style, { position: 'absolute', left: `${centerX - diameter / 2}px`, top: `${centerY - diameter / 2}px`, width: `${diameter}px`, height: `${diameter}px`,
    minWidth: '0', minHeight: '0', margin: '0', flex: 'none', transform: 'none' });
  const titleHeight = positive(centerY - diameter / 2 - gap), copyTop = centerY + diameter / 2 + gap;
  textSurface(title, { x: 0, y: 0, width, height: titleHeight }, fontBaseline, fixed);
  textSurface(copy, { x: 0, y: copyTop, width, height: positive(height - copyTop) }, fontBaseline, fixed);
  root.dataset.canvasLauncherFit = 'true';
}

export function fitNodeCanvasLauncher(card: HTMLElement, project: MeridianProject, node: MeridianNode, bounds: ChildGeometry, context: SizingContext, intrinsicFontBaseline?: number): number | undefined {
  const body = card.querySelector<HTMLElement>(':scope > .ms-node-body'), button = body?.querySelector<HTMLElement>(':scope > .ms-canvas-open');
  if (!body || !button) return;
  const title = card.querySelector<HTMLElement>(':scope > .ms-node-header'), copy = copySurface(body, [button]);
  const media = [...copy.querySelectorAll<HTMLElement>('.ms-image,.ms-video')];
  remember(card, [body, title, copy, button, ...media]);
  const style = getComputedStyle(card), fixed = node.style?.contentSizing === 'fixed';
  const baseline = contentFrameScale(bounds.width, bounds.height), padding = node.style?.padding ?? 5;
  const fontBaseline = fixed ? 1 : intrinsicFontBaseline ?? baseline;
  const left = pixel(style.paddingLeft) + padding * baseline, top = pixel(style.paddingTop) + padding * baseline;
  const physicalWidth = positive(bounds.width - pixel(style.borderLeftWidth) - pixel(style.borderRightWidth) - pixel(style.paddingLeft) - pixel(style.paddingRight) - 2 * padding * baseline);
  const physicalHeight = positive(bounds.height - pixel(style.borderTopWidth) - pixel(style.borderBottomWidth) - pixel(style.paddingTop) - pixel(style.paddingBottom) - 2 * padding * baseline);
  const width = physicalWidth / baseline, height = physicalHeight / baseline;
  const envelope = media.length ? nodeContentLayout(project, node, context, false, width).media : undefined;
  if (envelope) for (const item of media) {
    Object.assign(item.style, { width: `${envelope.width}px`, height: `${envelope.height}px`, maxWidth: '100%', maxHeight: 'none', flex: 'none' });
  }
  Object.assign(body.style, { position: 'absolute', left: '0', top: '0', width: `${width}px`, height: `${height}px`, padding: '0',
    overflow: 'visible', display: 'block', flex: 'none', transform: `translate(${left}px, ${top}px) scale(${baseline})`, transformOrigin: '0 0' });
  doorway(card, null, copy, button, width, height, node.style?.contentGap ?? 5, fixed, fontBaseline / baseline);
  // The title is a sibling of the body, so its transform includes the same
  // frame baseline explicitly instead of inheriting the body's transform.
  textSurface(title, { x: left, y: top, width: physicalWidth, height: positive((height / 2 - Math.min(width * .52, height * .4) / 2 - (node.style?.contentGap ?? 5)) * baseline) }, fontBaseline, fixed);
  card.dataset.contentFit = String(fontBaseline);
  return fontBaseline;
}

export function fitGalleryCanvasLauncher(root: HTMLElement, width: number, height: number, gap: number, fixed = false): boolean {
  restoreCanvasLauncherFit(root);
  const button = root.querySelector<HTMLElement>(':scope > .ms-canvas-open'); if (!button) return false;
  const title = root.querySelector<HTMLElement>(':scope > .ms-gallery-item-title'), copy = copySurface(root, [button, ...(title ? [title] : [])]);
  remember(root, [root, title, copy, button]);
  const baseline = contentFrameScale(width, height), logicalWidth = width / baseline, logicalHeight = height / baseline;
  Object.assign(root.style, { position: 'absolute', left: '0', top: '0', width: `${logicalWidth}px`, height: `${logicalHeight}px`,
    display: 'block', overflow: 'visible', padding: '0', transform: `scale(${baseline})`, transformOrigin: '0 0' });
  doorway(root, title, copy, button, logicalWidth, logicalHeight, gap, fixed, fixed ? 1 / baseline : 1);
  return true;
}
