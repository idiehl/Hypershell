import { isHexColor } from './appearance.ts';
import type { MeridianNode, PortalStyle } from './types.ts';

/** References retain their own presentation and follow the current workspace. */
export function effectivePortalColor(node: MeridianNode, _original: MeridianNode, canvasColor?: string): string | undefined {
  return [node.style?.portalColor, canvasColor].find(isHexColor);
}

/** Representative swatches only; an omitted tint retains the complete original artwork. */
export const PORTAL_SWATCHES: Readonly<Record<PortalStyle, string>> = {
  doorway: '#83cfb3', passage: '#e0bd86', wormhole: '#8ab8e9', vortex: '#82d4b8',
};

function hsl(color: string): [number, number, number] {
  const [r, g, b] = [1, 3, 5].map(offset => parseInt(color.slice(offset, offset + 2), 16) / 255);
  const max = Math.max(r!, g!, b!), min = Math.min(r!, g!, b!), difference = max - min, lightness = (max + min) / 2;
  if (!difference) return [0, 0, lightness];
  const hue = (max === r ? (g! - b!) / difference + (g! < b! ? 6 : 0) : max === g ? (b! - r!) / difference + 2 : (r! - g!) / difference + 4) / 6;
  return [hue, difference / (1 - Math.abs(2 * lightness - 1)), lightness];
}

/**
 * Recolors the artwork's tonal structure instead of flattening every stroke to
 * one solid color. Dark wells, moving rings and bright edges retain depth even
 * for black/white choices, and gradients keep all their authored opacity stops.
 */
export function portalTintColor(source: string, tint: string): string {
  if (!isHexColor(source) || !isHexColor(tint)) return source;
  const [hue, saturation, tintLightness] = hsl(tint), [, sourceSaturation, sourceLightness] = hsl(source);
  const lightness = Math.max(.015, Math.min(.985, sourceLightness * (.72 + .56 * tintLightness)));
  const chroma = (1 - Math.abs(2 * lightness - 1)) * saturation * (.68 + .32 * sourceSaturation);
  const sector = hue * 6, secondary = chroma * (1 - Math.abs(sector % 2 - 1)), floor = lightness - chroma / 2;
  const channels = sector < 1 ? [chroma, secondary, 0] : sector < 2 ? [secondary, chroma, 0]
    : sector < 3 ? [0, chroma, secondary] : sector < 4 ? [0, secondary, chroma]
    : sector < 5 ? [secondary, 0, chroma] : [chroma, 0, secondary];
  return '#' + channels.map(channel => Math.round((channel + floor) * 255).toString(16).padStart(2, '0')).join('');
}
