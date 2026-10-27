import type { MeridianNode, MeridianProject, PortalStyle } from './core/types.ts';
import { nodeTag } from './core/node-tag.ts';
import { architecturalPortal } from './portal-architectural.ts';
import { organicPortal } from './portal-organic.ts';
import { mountPortalMotion } from './portal-motion.ts';
import { isHexColor } from './core/appearance.ts';
import { portalTintColor } from './core/portal-color.ts';

export { disposePortalLaunchers } from './portal-motion.ts';
export { effectivePortalColor } from './core/portal-color.ts';
let sequence = 0;
const styles: readonly PortalStyle[] = ['doorway', 'passage', 'wormhole', 'vortex'];

/** The current appearance follows its workspace unless explicitly restyled. */
export function effectivePortalStyle(node: MeridianNode, _original: MeridianNode, canvasStyle?: PortalStyle): PortalStyle {
  return [node.style?.portalStyle, canvasStyle].find(style => style && styles.includes(style)) ?? 'doorway';
}

/** The destination's identity is independent of a cover's title or navigation Tag. */
export function canvasDestinationName(project: MeridianProject, node: MeridianNode, original: MeridianNode,
  knownTitles?: Readonly<Record<string, string>>): string {
  const target = original.targetId;
  const local = project.canvases.find(canvas => canvas.id === target || canvas.rootId === target);
  const external = original.canvasTarget;
  const knownLocal = external?.projectId === project.id && external.canvasId
    ? project.canvases.find(canvas => canvas.id === external.canvasId) : undefined;
  return local?.title || knownLocal?.title || knownTitles?.[original.id] || knownTitles?.[node.id]
    || original.title.trim() || node.title.trim() || nodeTag(original) || 'Linked Work';
}

/** Shared vector portal for ordinary tiles, references, Galleries and offline viewers. */
export function canvasLauncher(title: string, open: () => void, options: { style?: PortalStyle; color?: string; destinationName?: string } = {}): HTMLButtonElement {
  const button = document.createElement('button');
  button.type = 'button'; button.className = 'ms-canvas-open';
  const name = options.destinationName ?? title.replace(/^Open\s+/i, '');
  const accessible = options.destinationName ? `Open ${name}` : title;
  button.setAttribute('aria-label', accessible);
  const kind = options.style && styles.includes(options.style) ? options.style : 'doorway';
  // The screen-space destination caption is the only visual tooltip. A native
  // title would add a delayed second label below the same portal in WebView.
  button.dataset.portalStyle = kind;
  const approach = document.createElement('span'); approach.className = 'ms-portal-approach'; approach.setAttribute('aria-hidden', 'true');
  const prefix = `meridian-portal-${++sequence}`;
  const art = kind === 'doorway' || kind === 'passage' ? architecturalPortal(kind, prefix) : organicPortal(kind, prefix);
  if (isHexColor(options.color)) {
    button.dataset.portalColor = options.color.toLowerCase();
    button.style.setProperty('--ms-portal-color', options.color);
    button.style.setProperty('--ms-portal-glow', `${options.color}44`);
    for (const paint of art.querySelectorAll('[fill],[stroke],[stop-color]')) {
      for (const attribute of ['fill', 'stroke', 'stop-color']) {
        const color = paint.getAttribute(attribute);
        if (color && isHexColor(color)) paint.setAttribute(attribute, portalTintColor(color, options.color));
      }
    }
  }
  approach.append(art);
  button.append(approach);
  button.addEventListener('click', event => { event.stopPropagation(); open(); });
  mountPortalMotion(button, name);
  return button;
}
