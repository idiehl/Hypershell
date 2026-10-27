import type { MeridianNode } from './types.ts';

export const EXPANSION_TRAY_HEADER = 0;
export const EXPANSION_TRAY_LEGACY_HEADER = 32;
export const EXPANSION_TRAY_PADDING = 5;
export const EXPANSION_TRAY_LEGACY_PADDING = 20;
export const EXPANSION_TRAY_MIN_WIDTH = 11;
export const EXPANSION_TRAY_MIN_HEIGHT = 11;

export const expansionTrayHeader = (source: MeridianNode) => source.expansionTray?.layoutVersion ? EXPANSION_TRAY_HEADER : EXPANSION_TRAY_LEGACY_HEADER;
export const expansionTrayPadding = (source: MeridianNode) => source.expansionTray?.layoutVersion === 3 ? EXPANSION_TRAY_PADDING : EXPANSION_TRAY_LEGACY_PADDING;

/** Saved outer dimensions retain the same fixed chrome at every world scale. */
export function fixedExpansionTrayContent(source: MeridianNode) {
  if (!source.expansionTray?.size || ['canvas', 'container', 'spreadsheet', 'module'].includes(source.kind)) return undefined;
  const { size, offset } = source.expansionTray, padding = expansionTrayPadding(source);
  return { x: source.x + offset.x, y: source.y + offset.y,
    width: Math.max(Number.MIN_VALUE, size.width - 2 * padding),
    height: Math.max(Number.MIN_VALUE, size.height - expansionTrayHeader(source) - 2 * padding) };
}

export function fixedExpansionTrayGallery(source: MeridianNode) {
  const content = fixedExpansionTrayContent(source); if (!content) return undefined;
  const navigationHeight = Math.min(38, content.height / 4), gap = Math.min(5, content.height / 20);
  return { content, page: { ...content, height: content.height - navigationHeight - gap },
    navigation: { x: content.x, y: content.y + content.height - navigationHeight, width: content.width, height: navigationHeight,
      scale: Math.min(1, content.width / 110, navigationHeight / 38) } };
}
