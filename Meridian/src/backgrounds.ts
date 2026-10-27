import type { BackgroundPattern, BackgroundStyle, MeridianAsset } from './core/types.ts';
import { isBackgroundAsset, isHexColor } from './core/appearance.ts';

export interface BackgroundPaint {
  image: string; size: string; position: string; repeat: string; opacity: number;
  pattern: BackgroundPattern | 'image'; scale: number;
}
const validImages = new WeakMap<MeridianAsset, { mime: string; bytes: string; valid: boolean }>();
function raster(asset: MeridianAsset | undefined): asset is MeridianAsset {
  if (!asset) return false;
  const cached = validImages.get(asset);
  if (cached?.mime === asset.mime && cached.bytes === asset.base64) return cached.valid;
  const valid = isBackgroundAsset(asset);
  validImages.set(asset, { mime: asset.mime, bytes: asset.base64, valid }); return valid;
}
const svgImage = (body: string, width = 100, height = width): string =>
  `url("data:image/svg+xml,${encodeURIComponent(`<svg xmlns="http://www.w3.org/2000/svg" width="${width}" height="${height}" viewBox="0 0 ${width} ${height}">${body}</svg>`)}")`;

/** Only our own geometry or verified embedded raster bytes become CSS images. */
export function backgroundPaint(style: BackgroundStyle, assets: readonly MeridianAsset[] = [], defaultPattern: BackgroundPattern = 'none', defaultTint = '#4B6575'): BackgroundPaint {
  const scale = typeof style.backgroundScale === 'number' && Number.isFinite(style.backgroundScale) ? Math.max(8, Math.min(512, style.backgroundScale)) : 24;
  const opacity = typeof style.backgroundOpacity === 'number' && Number.isFinite(style.backgroundOpacity) ? Math.max(0, Math.min(1, style.backgroundOpacity)) : 1;
  const tint = isHexColor(style.backgroundTint) ? style.backgroundTint : isHexColor(defaultTint) ? defaultTint : '#4B6575';
  const asset = assets.find(item => item.id === style.backgroundAssetId);
  const tile = `${scale}px ${scale}px`;
  const paint: BackgroundPaint = { image: 'none', size: tile, position: '0px 0px', repeat: 'repeat', opacity, pattern: style.backgroundPattern ?? defaultPattern, scale };
  if (raster(asset)) return { ...paint, pattern: 'image', image: `url("data:${asset.mime};base64,${asset.base64}")`,
    size: style.backgroundFit === 'tile' ? `${scale}px auto` : style.backgroundFit === 'contain' ? 'contain' : 'cover',
    repeat: style.backgroundFit === 'tile' ? 'repeat' : 'no-repeat', position: style.backgroundFit === 'tile' ? '0px 0px' : 'center' };
  switch (paint.pattern) {
    case 'dots': paint.image = `radial-gradient(circle,${tint} 1px,transparent 1px)`; paint.position = `${-scale / 2}px ${-scale / 2}px`; break;
    case 'grid': paint.image = `linear-gradient(to right,${tint} 1px,transparent 1px),linear-gradient(to bottom,${tint} 1px,transparent 1px)`; break;
    case 'isometric': paint.image = svgImage(`<path d="M0 0H100M0 28.8675H100M0 57.735H100M-50 0L50 57.735M0 0L100 57.735M50 0L150 57.735M-50 57.735L50 0M0 57.735L100 0M50 57.735L150 0" fill="none" stroke="${tint}" stroke-width=".65"/>`,100,57.735); paint.size=`${scale}px ${scale * .57735}px`; break;
    case 'linen': paint.image = svgImage(`<path d="M0 8H48M0 20H48M0 32H48M0 44H48M8 0V48M20 0V48M32 0V48M44 0V48" stroke="${tint}" stroke-opacity=".36" stroke-width=".7"/><path d="M0 10H48M0 34H48M10 0V48M34 0V48" stroke="${tint}" stroke-opacity=".13" stroke-width="2"/>`,48); break;
    case 'contours': paint.image = svgImage(`<g fill="none" stroke="${tint}" stroke-width=".8" opacity=".65">${Array.from({length:13},(_,index)=>{const y=index*12.5-25;return `<path d="M0 ${y}C25 ${y-18} 75 ${y+18} 100 ${y}"/>`;}).join('')}</g>`); break;
    case 'aurora': {
      const definitions=`<defs>${[[tint,'.44'],['#7195AC','.22'],['#9577BE','.19']].map(([color,alpha],i)=>`<radialGradient id="wash${i}"><stop stop-color="${color}" stop-opacity="${alpha}"/><stop offset="1" stop-color="${color}" stop-opacity="0"/></radialGradient>`).join('')}</defs>`;
      // Wrapped copies give the wash continuous edges at every repeated tile.
      const clouds=[{x:20,y:35,r:45},{x:80,y:80,r:50},{x:60,y:20,r:60}].flatMap((cloud,i)=>[-100,0,100].flatMap(x=>[-100,0,100].map(y=>`<circle cx="${cloud.x+x}" cy="${cloud.y+y}" r="${cloud.r}" fill="url(#wash${i})"/>`))).join('');
      paint.image=svgImage(definitions+clouds); break;
    }
    case 'stars': paint.image = svgImage(`<g fill="${tint}"><circle cx="9" cy="16" r="1.1"/><circle cx="31" cy="83" r=".8"/><circle cx="72" cy="34" r="1.2"/><circle cx="90" cy="92" r=".7"/><circle cx="53" cy="61" r=".55"/><circle cx="14" cy="58" r=".5"/><circle cx="57" cy="7" r=".6"/></g><path d="M42 26H48M45 23V29M78 74H84M81 71V77" stroke="${tint}" stroke-width=".8"/>`); break;
    default: paint.pattern = 'none';
  }
  return paint;
}

export function paintBackground(element: HTMLElement, paint: BackgroundPaint): void {
  element.dataset.backgroundPattern = paint.pattern;
  Object.assign(element.style, { backgroundImage: paint.image, backgroundSize: paint.size, backgroundPosition: paint.position, backgroundRepeat: paint.repeat, opacity: String(paint.opacity) });
}

/** Repeating backgrounds share the canvas origin; single photographs fill the viewport. */
export function moveCanvasBackground(element: HTMLElement, paint: BackgroundPaint, view: { x: number; y: number; scale: number }): void {
  if (paint.pattern === 'image' && paint.repeat === 'no-repeat') return;
  const size = paint.scale * view.scale, offset = paint.pattern === 'dots' ? size / 2 : 0;
  const height = parseFloat(paint.size.split(' ')[1]) / paint.scale * size;
  element.style.backgroundSize = paint.pattern === 'image' ? `${size}px auto` : `${size}px ${height}px`;
  element.style.backgroundPosition = `${view.x - offset}px ${view.y - offset}px`;
  // Subpixel tiles have no useful visual detail and can alias into a solid wash.
  // Fade them out at overview scales, preserving their exact world coordinates.
  element.style.opacity = String(paint.opacity * Math.min(1, Math.max(0, (size - 1) / 3)) *
    (['dots', 'grid', 'isometric'].includes(paint.pattern) ? Math.min(1, Math.max(.18, view.scale)) : 1));
}
