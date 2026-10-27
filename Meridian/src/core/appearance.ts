import type { BackgroundStyle, CanvasStyle, LinkStyle, MeridianAsset, MeridianNode, NodeStyle, PortalStyle } from './types.ts';
import { isFontFamily } from './fonts.ts';
export { FONT_FAMILIES } from './fonts.ts';

export const STYLE_LIMITS = {
  fontSize: { min: 10, max: 72 }, titleSize: { min: 10, max: 96 },
  borderWidth: { min: 0, max: 8 }, radius: { min: 0, max: 48 },
  linkWidth: { min: 1, max: 32 }, linkLabel: { max: 160 },
  backgroundScale: { min: 8, max: 512 }, backgroundOpacity: { min: 0, max: 1 },
  spacing: { min: 0, max: 1000 },
  rotation: { min: -180, max: 180 },
} as const;
export const NODE_SPACING_DEFAULT = 5;
/** New nodes opt in explicitly; missing markers keep older authored Works stable. */
export const DEFAULT_NODE_FONT_SIZE = 22;
export const DEFAULT_NODE_TITLE_SIZE = 32;
export const DEFAULT_TEXT_OCCUPANCY = .65;
export const defaultNewNodeStyle = (): NodeStyle => ({ textLayout: 'balanced', textAlign: 'center', fontSize: DEFAULT_NODE_FONT_SIZE, titleSize: DEFAULT_NODE_TITLE_SIZE });
export const PORTAL_STYLES = [
  { value: 'doorway', label: 'Endless doorway' },
  { value: 'passage', label: 'Luminous passage' },
  { value: 'wormhole', label: 'Curved wormhole' },
  { value: 'vortex', label: 'Spiral vortex' },
] as const satisfies ReadonlyArray<{ value: PortalStyle; label: string }>;
export const BORDER_STYLES = ['solid', 'dashed', 'dotted', 'double'] as const;
export const NODE_SHAPES = [
  { value: 'rectangle', label: 'Rectangle' }, { value: 'square', label: 'Square' },
  { value: 'rounded', label: 'Rounded rectangle' }, { value: 'circle', label: 'Circle' },
  { value: 'ellipse', label: 'Ellipse' }, { value: 'triangle', label: 'Triangle' },
  { value: 'diamond', label: 'Diamond' }, { value: 'pentagon', label: 'Pentagon' },
  { value: 'hexagon', label: 'Hexagon' }, { value: 'octagon', label: 'Octagon' },
  { value: 'none', label: 'No backdrop' },
] as const;
export const BACKGROUND_PATTERNS = ['none', 'dots', 'grid', 'isometric', 'linen', 'contours', 'aurora', 'stars'] as const;
export const BACKGROUND_STYLE_KEYS = ['backgroundPattern', 'backgroundScale', 'backgroundTint', 'backgroundAssetId', 'backgroundFit', 'backgroundOpacity'] as const;
export const BACKGROUND_IMAGE_MIMES = ['image/png', 'image/jpeg', 'image/webp', 'image/gif', 'image/avif', 'image/bmp'] as const;
export const NODE_STYLE_KEYS = ['fill', 'border', 'textColor', 'accent', 'fontFamily', 'fontSize', 'titleSize', 'textLayout', 'textRotation', 'titleRotation', 'contentSizing', 'portalStyle', 'portalColor', 'backgroundDisplay', 'textAlign', 'variant', 'imageFit', 'borderWidth', 'borderStyle', 'radius', 'shape', 'padding', 'contentGap', 'textMediaGap', 'childPadding', ...BACKGROUND_STYLE_KEYS] as const;
export const CANVAS_STYLE_KEYS = ['background', 'grid', 'gridVisible', 'portalStyle', 'portalColor', ...BACKGROUND_STYLE_KEYS] as const;
export const LINK_STYLE_KEYS = ['color', 'width', 'lineStyle', 'dashed'] as const;
export const LINK_LINE_STYLES = ['solid', 'dashed', 'dotted'] as const;
export const linkLineStyle = (style: LinkStyle | undefined, fallback: 'solid' | 'dashed' | 'dotted' = 'solid'): 'solid' | 'dashed' | 'dotted' => style?.lineStyle ?? (style?.dashed === undefined ? fallback : style.dashed ? 'dashed' : 'solid');
export const NODE_STYLE_PRESETS: ReadonlyArray<{ id: string; label: string; style: NodeStyle }> = [
  { id: 'default', label: 'Default card', style: defaultNewNodeStyle() },
  { id: 'paper', label: 'Parchment', style: { fill: '#F3E8CF', border: '#BEA77E', textColor: '#302719', accent: '#996F32', fontFamily: 'serif', fontSize: 15, titleSize: 21, radius: 6 } },
  { id: 'ink', label: 'Midnight', style: { fill: '#1C2340', border: '#657199', textColor: '#E9ECF7', accent: '#B9C6FF', fontFamily: 'sans', fontSize: 14, titleSize: 19, radius: 16 } },
  { id: 'note', label: 'Warm note', style: { fill: '#FFF0B9', border: '#C6A24D', textColor: '#453817', accent: '#9C6618', fontFamily: 'sans', fontSize: 14, titleSize: 18, radius: 3 } },
  { id: 'callout', label: 'Teal callout', style: { fill: '#123A37', border: '#398C7D', textColor: '#D2F3E9', accent: '#79E7C7', fontFamily: 'sans', fontSize: 16, titleSize: 22, radius: 14 } },
  { id: 'label', label: 'Free text', style: { variant: 'label', textColor: '#DFE7F0', fontFamily: 'sans', fontSize: 22, titleSize: 36, borderWidth: 0 } },
];
export const CANVAS_STYLE_PRESETS: ReadonlyArray<{ id: string; label: string; style: CanvasStyle }> = [
  { id: 'default', label: 'Midnight', style: {} },
  { id: 'paper', label: 'Parchment', style: { background: '#E8DDC2', grid: '#C4B89E', gridVisible: true } },
  { id: 'light', label: 'Light', style: { background: '#EFF3F7', grid: '#C8D3DF', gridVisible: true } },
  { id: 'forest', label: 'Deep green', style: { background: '#112D2A', grid: '#2E534C', gridVisible: true } },
];

export const isHexColor = (value: unknown): value is string => typeof value === 'string' && /^#[0-9a-f]{6}$/i.test(value);
const record = (value: unknown): value is Record<string, unknown> => !!value && typeof value === 'object' && !Array.isArray(value);
const keys = (value: Record<string, unknown>, allowed: readonly string[]) => Object.keys(value).every(key => allowed.includes(key));
const optional = (value: Record<string, unknown>, key: string, test: (value: unknown) => boolean) => value[key] === undefined || test(value[key]);
const range = (minimum: number, maximum: number) => (value: unknown) => typeof value === 'number' && Number.isFinite(value) && value >= minimum && value <= maximum;
const choice = (values: readonly string[]) => (value: unknown) => typeof value === 'string' && values.includes(value);
export const isBackgroundAssetIdentifier = (value: unknown): value is string => typeof value === 'string' && /^[A-Za-z0-9][A-Za-z0-9_-]{0,95}$/.test(value);
const backgroundSettings = (value: Record<string, unknown>): boolean =>
  optional(value, 'backgroundPattern', choice(BACKGROUND_PATTERNS))
  && optional(value, 'backgroundTint', isHexColor)
  && optional(value, 'backgroundAssetId', isBackgroundAssetIdentifier)
  && optional(value, 'backgroundFit', choice(['cover', 'contain', 'tile']))
  && optional(value, 'backgroundScale', range(STYLE_LIMITS.backgroundScale.min, STYLE_LIMITS.backgroundScale.max))
  && optional(value, 'backgroundOpacity', range(0, 1));

const backgroundAssetChecks = new WeakMap<MeridianAsset, { mime: string; base64: string; valid: boolean }>();
/** Header-only decode keeps repeated canvas paints cheap even for large embedded images. */
export function isBackgroundAsset(asset: MeridianAsset | undefined): asset is MeridianAsset {
  if (!asset) return false;
  const cached = backgroundAssetChecks.get(asset);
  if (cached && cached.mime === asset.mime && cached.base64 === asset.base64) return cached.valid;
  const valid = checkBackgroundAsset(asset);
  backgroundAssetChecks.set(asset, { mime: asset.mime, base64: asset.base64, valid });
  return valid;
}
function checkBackgroundAsset(asset: MeridianAsset): boolean {
  const encoded = asset.base64;
  const padding = encoded.endsWith('==') ? 2 : encoded.endsWith('=') ? 1 : 0;
  if (!BACKGROUND_IMAGE_MIMES.includes(asset.mime as typeof BACKGROUND_IMAGE_MIMES[number])
    || encoded.length > Math.ceil(16 * 1024 * 1024 / 3) * 4 || encoded.length % 4 !== 0
    || encoded.length / 4 * 3 - padding > 16 * 1024 * 1024
    || /[^A-Za-z0-9+/=]/.test(encoded) || encoded.indexOf('=') !== (padding ? encoded.length - padding : -1)) return false;
  let bytes: string;
  try {
    bytes = atob(encoded.slice(0, 128));
    if (padding && btoa(atob(encoded.slice(-4))) !== encoded.slice(-4)) return false;
  } catch { return false; }
  const starts = (...values: number[]) => values.every((value, index) => bytes.charCodeAt(index) === value);
  switch (asset.mime) {
    case 'image/png': return starts(137, 80, 78, 71, 13, 10, 26, 10);
    case 'image/jpeg': return starts(255, 216, 255);
    case 'image/gif': return bytes.startsWith('GIF87a') || bytes.startsWith('GIF89a');
    case 'image/webp': return bytes.startsWith('RIFF') && bytes.slice(8, 12) === 'WEBP';
    case 'image/bmp': return bytes.startsWith('BM');
    case 'image/avif': return bytes.slice(4, 8) === 'ftyp' && /avif|avis/.test(bytes.slice(8, 64));
    default: return false;
  }
}

export const backgroundAssetId = (style: BackgroundStyle | undefined): string | undefined => style?.backgroundAssetId;
export const nodeAssetIds = (node: MeridianNode): string[] => [...new Set([
  ...node.assetId ? [node.assetId] : [], ...node.attachments ?? [], ...node.contentItems?.flatMap(item => item.type === 'asset' ? [item.assetId] : []) ?? [], ...node.style?.backgroundAssetId ? [node.style.backgroundAssetId] : [],
])];

/** Undefined preserves the historical defaults; present style objects have a bounded vocabulary. */
export function isNodeStyle(value: unknown): value is NodeStyle | undefined {
  if (value === undefined) return true;
  if (!record(value) || !keys(value, NODE_STYLE_KEYS)) return false;
  if (!backgroundSettings(value) || !['fill', 'border', 'textColor', 'accent'].every(key => optional(value, key, isHexColor))) return false;
  return optional(value, 'fontFamily', isFontFamily)
    && optional(value, 'textLayout', choice(['balanced']))
    && optional(value, 'contentSizing', choice(['fit', 'fixed']))
    && optional(value, 'portalStyle', choice(PORTAL_STYLES.map(style => style.value)))
    && optional(value, 'portalColor', isHexColor)
    && optional(value, 'backgroundDisplay', choice(['always', 'hover']))
    && optional(value, 'textAlign', choice(['left', 'center', 'right']))
    && optional(value, 'variant', choice(['card', 'label']))
    && optional(value, 'shape', choice(NODE_SHAPES.map(shape => shape.value)))
    && optional(value, 'imageFit', choice(['contain', 'cover']))
    && optional(value, 'fontSize', range(STYLE_LIMITS.fontSize.min, STYLE_LIMITS.fontSize.max))
    && optional(value, 'titleSize', range(STYLE_LIMITS.titleSize.min, STYLE_LIMITS.titleSize.max))
    && ['textRotation', 'titleRotation'].every(key => optional(value, key, range(STYLE_LIMITS.rotation.min, STYLE_LIMITS.rotation.max)))
    && optional(value, 'borderWidth', range(STYLE_LIMITS.borderWidth.min, STYLE_LIMITS.borderWidth.max))
    && optional(value, 'borderStyle', choice(BORDER_STYLES))
    && ['padding', 'contentGap', 'textMediaGap', 'childPadding'].every(key => optional(value, key, range(STYLE_LIMITS.spacing.min, STYLE_LIMITS.spacing.max)))
    && optional(value, 'radius', range(STYLE_LIMITS.radius.min, STYLE_LIMITS.radius.max));
}
export function isCanvasStyle(value: unknown): value is CanvasStyle | undefined {
  return value === undefined || record(value) && keys(value, CANVAS_STYLE_KEYS) && backgroundSettings(value)
    && ['background', 'grid'].every(key => optional(value, key, isHexColor))
    && optional(value, 'portalStyle', choice(PORTAL_STYLES.map(style => style.value)))
    && optional(value, 'portalColor', isHexColor)
    && optional(value, 'gridVisible', flag => typeof flag === 'boolean');
}
export function isLinkStyle(value: unknown): value is LinkStyle | undefined {
  return value === undefined || record(value) && keys(value, LINK_STYLE_KEYS)
    && optional(value, 'color', isHexColor)
    && optional(value, 'width', range(STYLE_LIMITS.linkWidth.min, STYLE_LIMITS.linkWidth.max))
    && optional(value, 'lineStyle', choice(LINK_LINE_STYLES))
    && optional(value, 'dashed', flag => typeof flag === 'boolean');
}
export const isLinkLabel = (value: unknown): value is string | undefined => value === undefined || typeof value === 'string' && value.length <= STYLE_LIMITS.linkLabel.max && !/[\u0000-\u0008\u000b\u000c\u000e-\u001f\u007f-\u009f]/.test(value);
