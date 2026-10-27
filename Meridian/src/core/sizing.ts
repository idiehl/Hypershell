import type { MeridianAsset, MeridianNode, MeridianProject, NodeStyle, TextFormat, TextRun } from './types.ts';
import { textSegments } from './rich-text.ts';
import { isGallery, isEmbeddedGallery } from './gallery.ts';
import { DEFAULT_NODE_FONT_SIZE, DEFAULT_NODE_TITLE_SIZE, DEFAULT_TEXT_OCCUPANCY, FONT_FAMILIES } from './appearance.ts';
import { fontFamily } from './fonts.ts';
import { rotatedTextEnvelope, textRotation } from '../text-rotation.ts';
import { isCellChild, orderedCellAxes, gridGeometry } from './cells.ts';
import { activeNodeContent, hidesOwnContent, projectNodeContent } from './content.ts';
import { documentPreviewKind } from './document-preview.ts';
import { nodeShape, shapeBounds, shapeAspectRatio, shapeContentInsets } from './geometry.ts';
import { DEFAULT_CHILD_INSET } from './spacing.ts';
import { fixedExpansionTrayContent, fixedExpansionTrayGallery } from './expansion-tray-frame.ts';

export interface ContentSize { width: number; height: number }
export interface ContentLayout extends ContentSize {
  /** Height of the parent's own header, before any body or child content. */
  headerHeight: number;
  bodyHeight: number;
  contentWidth: number;
  media?: ContentSize;
  /** Unrotated wrapping basis retained when a text block's envelope is wider. */
  rotationReadingWidth?: number;
}
export interface SizingContext {
  /** A view's active gallery object; omission reserves the largest object envelope. */
  galleryItems?: ReadonlyMap<string, string> | Readonly<Record<string, string>>;
}
export interface SizingOptions {
  /** Ordered/Freeform packing keeps an exterior tray separate from its source tile. */
  excludeTrayContents?: boolean;
  children?: (id: string) => readonly MeridianNode[];
  /** A caller-owned graph index avoids repeated linear parent scans. */
  node?: (id: string) => MeridianNode | undefined;
  isVisible?: (id: string) => boolean;
  hasContents?: boolean;
  context?: SizingContext;
}
export interface SizingBounds extends ContentSize { id: string; x: number; y: number }
export interface ChildGeometry extends ContentSize { x: number; y: number }

const finite = (value: number | undefined, fallback: number) => typeof value === 'number' && Number.isFinite(value) ? value : fallback;
const clamp = (value: number, min: number, max: number) => Math.max(min, Math.min(max, value));
const pixel = (value: number) => Math.ceil(finite(value, 36));
const geometryMinimum = (width: number, height = width) => Math.max(Number.MIN_VALUE, Math.min(.001, width / 1024, height / 1024));
const structural = new Set(['container', 'spreadsheet', 'module', 'canvas-link']);
const orderedVariants = new Set(['horizontal', 'vertical', 'grid', 'spreadsheet', 'window']);
function orderedContainer(node: MeridianNode): boolean {
  return node.kind === 'spreadsheet' || !!node.containerVariant && orderedVariants.has(node.containerVariant);
}
function orderedAxes(project: MeridianProject | undefined, node: MeridianNode, children?: readonly MeridianNode[]) {
  const grid = node.grid!;
  const items = children ?? project?.nodes.filter(child => child.parentId === node.id && child.canvasId === node.canvasId);
  let { columns, rows } = items ? orderedCellAxes(node, items) : grid;
  if (node.containerVariant === 'window') { columns = Math.min(columns, grid.windowColumns ?? 3); rows = Math.min(rows, grid.windowRows ?? 3); }
  return { columns, rows };
}

/** Keep non-DOM layout, the editor and publications on the same spacing defaults. */
export function nodeSpacing(style: NodeStyle | undefined) {
  return { padding: Math.max(0, finite(style?.padding, 5)), contentGap: Math.max(0, finite(style?.contentGap, 5)),
    textMediaGap: Math.max(0, finite(style?.textMediaGap, 5)), childPadding: Math.max(0, finite(style?.childPadding, DEFAULT_CHILD_INSET)) };
}
export function nodeBorderWidth(node: MeridianNode): number {
  return node.style?.shape === 'none' || node.kind === 'text' && node.style?.variant === 'label' ? 0 : Math.max(0, finite(node.style?.borderWidth, 1));
}
/** A tiny vector tile needs an interior, even when its authored border is wider
 * than the tile itself. Preserve the requested stroke whenever it fits. */
export function effectiveNodeBorderWidth(node: MeridianNode, frameWidth: number, frameHeight: number): number {
  const physical = shapeBounds(nodeShape(node, { x: 0, y: 0, width: frameWidth, height: frameHeight }));
  const edge = Math.min(physical.width, physical.height), authored = nodeBorderWidth(node);
  return edge < 36 ? Math.min(authored, Math.max(0, edge * .1)) : authored;
}

/** New nodes explicitly use auto. Preserve the proportions of older authored leaf tiles. */
export function nodeSizingMode(node: MeridianNode): 'auto' | 'fixed' {
  if (['canvas', 'joint', 'junction'].includes(node.kind)) return 'fixed';
  return node.sizeMode ?? (structural.has(node.kind) ? 'auto' : 'fixed');
}

/** Typography is an explicit new-node profile, never a migration of older
 * content. Authored sizes and horizontal alignment still take precedence. */
export function nodeTypography(node: MeridianNode): { fontSize: number; titleSize: number; textAlign: 'left' | 'center' | 'right' } {
  const style = node.style, balanced = style?.textLayout === 'balanced';
  return { fontSize: finite(style?.fontSize, balanced ? DEFAULT_NODE_FONT_SIZE : 12),
    titleSize: finite(style?.titleSize, balanced ? DEFAULT_NODE_TITLE_SIZE : 13), textAlign: style?.textAlign ?? 'center' };
}

/** Only a textual tile uses the 65% reading composition. Native players,
 * document readers, controls and expanded workspaces keep their own geometry. */
export function balancedTextLayout(project: MeridianProject | undefined, original: MeridianNode): boolean {
  if (original.style?.textLayout !== 'balanced' || hidesOwnContent(original) || isGallery(original)
      || ['canvas', 'block', 'canvas-link', 'joint', 'junction', 'input', 'number', 'switch', 'output'].includes(original.kind)) return false;
  const display = projectNodeContent(resolveDisplay(project, original).node), content = activeNodeContent(display);
  return content.type !== 'asset' && !!(original.showTitle !== false && original.title || content.text || content.hyperlink);
}

interface TextMetrics { fontSize: number; family: NodeStyle['fontFamily']; bold?: boolean; italic?: boolean; weight?: number }
const nativeTextWidths = new Map<string, number>();
let textCanvas: CanvasRenderingContext2D | null | undefined;
function nativeTextAdvance(text: string, metrics: TextMetrics): number | undefined {
  if (typeof document === 'undefined') return undefined;
  if (textCanvas === undefined) {
    textCanvas = document.createElement('canvas').getContext('2d');
    // Current choices are local system fonts; clear cached observations if a
    // future font loads after the first measurement in this same document.
    document.fonts?.addEventListener('loadingdone', () => nativeTextWidths.clear());
  }
  if (!textCanvas) return undefined;
  const family = FONT_FAMILIES.find(font => font.value === (metrics.family ?? 'sans'))!.css;
  const font = `${metrics.italic ? 'italic ' : ''}${metrics.weight ?? (metrics.bold ? 600 : 400)} ${metrics.fontSize}px ${family}`, key = `${font}\n${text}`;
  const cached = nativeTextWidths.get(key); if (cached !== undefined) return cached;
  textCanvas.font = font;
  const width = textCanvas.measureText(text).width;
  if (!Number.isFinite(width)) return undefined;
  if (text.length <= 256) {
    if (nativeTextWidths.size > 8192) nativeTextWidths.clear();
    nativeTextWidths.set(key, width);
  }
  return width;
}
/** Editor and offline viewer measure their actual shared typography. Non-DOM
 * authoring uses a stable estimate until the scene can observe local fonts. */
export function textAdvance(text: string, metrics: TextMetrics): number {
  const native = nativeTextAdvance(text, metrics); if (native !== undefined) return native;
  const family = fontFamily(metrics.family);
  let advance = 0;
  for (const glyph of text) {
    let factor = family.monospace ? .61
      : /[\t]/.test(glyph) ? 1.2 : /\s/.test(glyph) ? .32
      : /[ilI!|.,:;'`]/.test(glyph) ? .3 : /[mwMW@%&#]/.test(glyph) ? .92
      : /[A-Z]/.test(glyph) ? .69 : /[0-9]/.test(glyph) ? .58
      : /[^\u0000-\u024f]/.test(glyph) ? 1 : .57;
    if (family.category === 'serif') factor *= 1.03;
    advance += metrics.fontSize * factor * (metrics.bold || (metrics.weight ?? 400) >= 600 ? 1.055 : 1);
  }
  return advance;
}
export function wrappedTextHeight(text: string, width: number, metrics: TextMetrics, lineHeight = 1.5): number {
  if (!text) return 0;
  const limit = Math.max(metrics.fontSize, width); let lines = 0;
  for (const paragraph of text.replace(/\r\n?/g, '\n').split('\n')) {
    lines++; let occupied = 0;
    for (const word of paragraph.match(/\s+|\S+/gu) ?? []) {
      const amount = textAdvance(word, metrics);
      // Trailing spaces can hang beyond a pre-wrap reading line; they should
      // never manufacture a blank line or consume the next line's first word.
      if (/^\s+$/u.test(word)) { occupied += amount; continue; }
      if (occupied > 0 && occupied + amount > limit + .01) { lines++; occupied = 0; }
      if (amount <= limit + .01) { occupied += amount; continue; }
      // overflow-wrap:anywhere breaks a token longer than one reading line.
      for (const glyph of word) {
        const amount = textAdvance(glyph, metrics);
        if (occupied > 0 && occupied + amount > limit + .01) { lines++; occupied = 0; }
        occupied += amount;
      }
    }
  }
  return lines * metrics.fontSize * lineHeight;
}

function runMetrics(base: TextMetrics, style: TextFormat): TextMetrics {
  return { ...base, family: style.fontFamily ?? base.family, fontSize: style.fontSize ?? base.fontSize,
    ...(style.bold !== undefined ? { bold: style.bold, weight: style.bold ? 700 : 400 } : {}),
    ...(style.italic !== undefined ? { italic: style.italic } : {}) };
}
const metricFormatting = (runs: readonly TextRun[] | undefined) => !!runs?.some(run =>
  run.style.fontFamily !== undefined || run.style.fontSize !== undefined || run.style.bold !== undefined || run.style.italic !== undefined);
/** Measure a canonical UTF-16 range across its actual native font runs. A word
 * spanning two formats remains one wrapping token rather than acquiring a
 * spurious break at the formatting boundary. */
function richMeasurements(text: string, runs: readonly TextRun[], base: TextMetrics, lineHeight: number) {
  const segments = textSegments(text, runs).map(segment => ({ ...segment, metrics: runMetrics(base, segment.style) }));
  const baseHeight = base.fontSize * lineHeight;
  const measure = (start: number, end: number) => {
    let width = 0, height = baseHeight;
    let low = 0, high = segments.length;
    while (low < high) { const middle = (low + high) >>> 1; if (segments[middle]!.end <= start) low = middle + 1; else high = middle; }
    for (let at = low; at < segments.length && segments[at]!.start < end; at++) {
      const segment = segments[at]!, first = Math.max(start, segment.start), last = Math.min(end, segment.end);
      if (last <= first) continue;
      width += textAdvance(text.slice(first, last), segment.metrics);
      height = Math.max(height, segment.metrics.fontSize * lineHeight);
    }
    return { width, height };
  };
  const paragraphs: Array<{ start: number; end: number }> = []; let start = 0;
  for (const newline of text.matchAll(/\r\n?|\n/g)) { paragraphs.push({ start, end: newline.index! }); start = newline.index! + newline[0].length; }
  paragraphs.push({ start, end: text.length });
  return { measure, paragraphs, baseHeight };
}
export function richTextAdvance(text: string, runs: readonly TextRun[] | undefined, metrics: TextMetrics): number {
  if (!metricFormatting(runs)) return textAdvance(text, metrics);
  return richMeasurements(text, runs!, metrics, 1.5).measure(0, text.length).width;
}
export function longestRichTextLine(text: string, runs: readonly TextRun[] | undefined, metrics: TextMetrics): number {
  if (!metricFormatting(runs)) return Math.max(0, ...text.replace(/\r\n?/g, '\n').split('\n').map(line => textAdvance(line, metrics)));
  const rich = richMeasurements(text, runs!, metrics, 1.5);
  return rich.paragraphs.reduce((width, line) => Math.max(width, rich.measure(line.start, line.end).width), 0);
}
/** Mixed-size inline glyphs contribute to each individual line's height. The
 * inherited text strut still supplies the minimum height of blank/small lines. */
export function wrappedRichTextHeight(text: string, runs: readonly TextRun[] | undefined, width: number, metrics: TextMetrics, lineHeight = 1.5): number {
  if (!metricFormatting(runs)) return wrappedTextHeight(text, width, metrics, lineHeight);
  if (!text) return 0;
  const rich = richMeasurements(text, runs!, metrics, lineHeight), limit = Math.max(metrics.fontSize, width);
  let total = 0;
  for (const paragraph of rich.paragraphs) {
    let occupied = 0, line = rich.baseHeight;
    const finish = () => { total += line; occupied = 0; line = rich.baseHeight; };
    for (const token of text.slice(paragraph.start, paragraph.end).matchAll(/\s+|\S+/gu)) {
      const start = paragraph.start + token.index!, end = start + token[0].length, measured = rich.measure(start, end);
      if (/^\s+$/u.test(token[0])) { occupied += measured.width; line = Math.max(line, measured.height); continue; }
      if (occupied > 0 && occupied + measured.width > limit + .01) finish();
      if (measured.width <= limit + .01) { occupied += measured.width; line = Math.max(line, measured.height); continue; }
      let at = start;
      for (const glyph of token[0]) {
        const next = at + glyph.length, measured = rich.measure(at, next);
        if (occupied > 0 && occupied + measured.width > limit + .01) finish();
        occupied += measured.width; line = Math.max(line, measured.height); at = next;
      }
    }
    total += line;
  }
  return total;
}

const imageSizes = new WeakMap<MeridianAsset, { base64: string; mime: string; size?: ContentSize }>();
/** Read inert image headers only; no decoding, networking or browser-dependent layout. */
export function assetImageSize(asset: MeridianAsset | undefined): ContentSize | undefined {
  if (!asset || !/^image\//i.test(asset.mime)) return undefined;
  const cached = imageSizes.get(asset); if (cached?.base64 === asset.base64 && cached.mime === asset.mime) return cached.size;
  let size: ContentSize | undefined;
  try {
    const encoded = asset.base64.slice(0, 349524), raw = atob(encoded.slice(0, encoded.length - encoded.length % 4));
    const bytes = Uint8Array.from(raw, glyph => glyph.charCodeAt(0)), view = new DataView(bytes.buffer);
    const ascii = (at: number, count: number) => String.fromCharCode(...bytes.subarray(at, at + count));
    if (asset.mime === 'image/png' && bytes.length >= 24 && ascii(1, 3) === 'PNG') size = { width: view.getUint32(16), height: view.getUint32(20) };
    else if (asset.mime === 'image/gif' && bytes.length >= 10 && ascii(0, 3) === 'GIF') size = { width: view.getUint16(6, true), height: view.getUint16(8, true) };
    else if (asset.mime === 'image/bmp' && bytes.length >= 26 && ascii(0, 2) === 'BM') {
      size = view.getUint32(14, true) === 12 ? { width: view.getUint16(18, true), height: view.getUint16(20, true) }
        : { width: Math.abs(view.getInt32(18, true)), height: Math.abs(view.getInt32(22, true)) };
    } else if (asset.mime === 'image/jpeg' && bytes[0] === 255 && bytes[1] === 216) {
      let at = 2;
      while (at + 4 < bytes.length) {
        if (bytes[at] !== 255) break;
        while (bytes[at] === 255) at++;
        const marker = bytes[at++]!;
        if (marker === 217 || marker === 218) break;
        if (marker === 1 || marker >= 208 && marker <= 215) continue;
        const length = view.getUint16(at); if (length < 2 || at + length > bytes.length) break;
        if ([192, 193, 194, 195, 197, 198, 199, 201, 202, 203, 205, 206, 207].includes(marker) && length >= 7) {
          size = { width: view.getUint16(at + 5), height: view.getUint16(at + 3) }; break;
        }
        at += length;
      }
    } else if (asset.mime === 'image/webp' && bytes.length >= 30 && ascii(0, 4) === 'RIFF' && ascii(8, 4) === 'WEBP') {
      const tag = ascii(12, 4);
      if (tag === 'VP8X') size = { width: 1 + bytes[24]! + (bytes[25]! << 8) + (bytes[26]! << 16), height: 1 + bytes[27]! + (bytes[28]! << 8) + (bytes[29]! << 16) };
      else if (tag === 'VP8L' && bytes[20] === 47) size = { width: 1 + bytes[21]! + ((bytes[22]! & 63) << 8), height: 1 + (bytes[22]! >> 6) + (bytes[23]! << 2) + ((bytes[24]! & 15) << 10) };
      else if (tag === 'VP8 ' && bytes[23] === 157 && bytes[24] === 1 && bytes[25] === 42) size = { width: view.getUint16(26, true) & 16383, height: view.getUint16(28, true) & 16383 };
    } else if (asset.mime === 'image/avif') {
      for (let at = 4; at + 16 <= bytes.length; at++) if (ascii(at, 4) === 'ispe') { size = { width: view.getUint32(at + 8), height: view.getUint32(at + 12) }; break; }
    }
    if (size && (![size.width, size.height].every(value => Number.isFinite(value) && value > 0 && value <= 1_000_000))) size = undefined;
  } catch { size = undefined; }
  imageSizes.set(asset, { base64: asset.base64, mime: asset.mime, size }); return size;
}

function resolveDisplay(project: MeridianProject | undefined, node: MeridianNode) {
  if (!project || node.kind !== 'reference' || !node.targetId) { const assetId = activeNodeContent(node).assetId; return { node, asset: assetId ? project?.assets.find(asset => asset.id === assetId) : undefined, reference: false }; }
  if (node.contentItems !== undefined) { const assetId = activeNodeContent(node).assetId; return { node, asset: assetId ? project.assets.find(asset => asset.id === assetId) : undefined, reference: true }; }
  const local = project.nodes.find(candidate => candidate.id === node.targetId && candidate.canvasId === node.canvasId);
  const saved = !local ? project.snapshots[node.targetId] : undefined;
  let display = local ?? saved?.node;
  if (display && node.targetCell) {
    const value = node.targetCell.toUpperCase();
    const candidates = local ? project.nodes : Object.values(project.snapshots).map(snapshot => snapshot.node);
    display = candidates.find(child => child.parentId === display!.id && child.cell && (child.cell.name?.toUpperCase() === value || (() => {
      let column = child.cell!.column, label = ''; while (column > 0) { column--; label = String.fromCharCode(65 + column % 26) + label; column = Math.floor(column / 26); }
      return `${child.cell!.column}:${child.cell!.row}` === value || `${label}${child.cell!.row}` === value;
    })()));
  }
  const snapshot = display && !local ? project.snapshots[display.id] : undefined;
  const active = activeNodeContent(display ?? node);
  return { node: display ?? node, asset: active.type === 'asset' ? [snapshot?.asset, ...snapshot?.assets ?? []].find(asset => asset?.id === active.assetId) ?? project.assets.find(asset => asset.id === active.assetId) : undefined, reference: true };
}
function textFromAsset(asset: MeridianAsset, limit = 3000): string {
  try { return new TextDecoder().decode(Uint8Array.from(atob(asset.base64), glyph => glyph.charCodeAt(0))).slice(0, limit); } catch { return ''; }
}
function csvContentSize(asset: MeridianAsset): ContentSize {
  const source = textFromAsset(asset, 2_000_000), delimiter = /\.tsv$/i.test(asset.name) ? '\t' : ',';
  const rows: string[][] = []; let row: string[] = [], value = '', quoted = false;
  for (let at = 0; at < source.length && rows.length <= 24; at++) {
    const glyph = source[at]!;
    if (glyph === '"') { if (quoted && source[at + 1] === '"') { value += '"'; at++; } else quoted = !quoted; }
    else if (!quoted && glyph === delimiter) { row.push(value); value = ''; }
    else if (!quoted && (glyph === '\r' || glyph === '\n')) { row.push(value); rows.push(row); row = []; value = ''; if (glyph === '\r' && source[at + 1] === '\n') at++; }
    else value += glyph;
  }
  if (rows.length <= 24 && (value || row.length || !rows.length)) { row.push(value); rows.push(row); }
  const displayed = rows.slice(0, 24).map(row => row.slice(0, 12)), columns = Math.max(1, ...displayed.map(row => row.length));
  const metrics = { fontSize: 12, family: 'mono' as const };
  const widths = Array.from({ length: columns }, (_, column) => clamp(Math.max(...displayed.map(row => textAdvance(row[column] ?? '', metrics))) + 19, 85, 360));
  const height = displayed.reduce((height, row) => height + Math.max(17, ...row.map((cell, column) => wrappedTextHeight(cell, widths[column]! - 19, metrics, 1.4))) + 13, 0);
  return { width: widths.reduce((sum, width) => sum + width, 0), height: pixel(height + (rows.length > 24 ? 20 : 0)) };
}
const paddingForShape = shapeContentInsets;
/** Expand an automatic frame enough to hold the specified safe reading area,
 * preserving regular silhouettes instead of stretching them to fit content. */
export function nodeFrameForSafeContent(node: MeridianNode, content: ContentSize): ContentSize {
  const shape = paddingForShape(node.style?.shape), ratio = shapeAspectRatio(node.style?.shape);
  let width = content.width / (1 - 2 * shape.x), height = content.height / (1 - shape.top - shape.bottom);
  if (ratio !== undefined) { height = Math.max(height, width / ratio); width = height * ratio; }
  return { width: pixel(width), height: pixel(height) };
}
/** Actual header/body reading area, including backdrop proportions and the
 * authored border/padding. Shared by layout, content fitting and publications. */
export function nodeContentBounds(node: MeridianNode, frame: ChildGeometry): ChildGeometry {
  const physical = shapeBounds(nodeShape(node, frame)), shape = paddingForShape(node.style?.shape), inset = nodeSpacing(node.style).padding + effectiveNodeBorderWidth(node, frame.width, frame.height);
  const width = physical.width * (1 - 2 * shape.x), height = physical.height * (1 - shape.top - shape.bottom);
  const minimum = geometryMinimum(width, height), horizontal = Math.min(inset, Math.max(0, (width - minimum) / 2)), vertical = Math.min(inset, Math.max(0, (height - minimum) / 2));
  return { x: physical.x + physical.width * shape.x + horizontal, y: physical.y + physical.height * shape.top + vertical,
    width: Math.max(minimum, width - 2 * horizontal), height: Math.max(minimum, height - 2 * vertical) };
}
export function nodeInnerContentWidth(node: MeridianNode, frameWidth: number, frameHeight?: number): number {
  const physical = frameHeight === undefined ? { width: frameWidth } : shapeBounds(nodeShape(node, { x: 0, y: 0, width: frameWidth, height: frameHeight }));
  const border = frameHeight === undefined ? nodeBorderWidth(node) : effectiveNodeBorderWidth(node, frameWidth, frameHeight);
  return Math.max(geometryMinimum(physical.width), physical.width * (1 - 2 * paddingForShape(node.style?.shape).x) - 2 * (nodeSpacing(node.style).padding + border));
}

/** Automatic ordered enclosures reserve a silhouette-safe surface before any
 * child coordinates are assigned; fixed enclosures retain their authored frame. */
export function orderedContainerFrame(project: MeridianProject | undefined, parent: MeridianNode, columns: number, rows: number, headerWidth = 0, headerHeight = 0): ContentSize {
  const grid = parent.grid!, spacing = nodeSpacing(parent.style), border = nodeBorderWidth(parent), gap = orderedContainer(parent) ? 0 : grid.gap ?? 0;
  const width = columns * grid.cellWidth + Math.max(0, columns - 1) * gap + headerWidth + 2 * (spacing.childPadding + border);
  const readingWidth = Math.max(1, width - 2 * (spacing.padding + border));
  const layout = nodeContentLayout(project, { ...parent, collapsed: false, grid: undefined }, {}, false, readingWidth);
  const height = border + layout.headerHeight + spacing.contentGap + headerHeight + rows * grid.cellHeight + Math.max(0, rows - 1) * gap + spacing.childPadding + border;
  return nodeFrameForSafeContent(parent, { width, height });
}

/** The usable child surface follows the actual backdrop, including centered
 * square/circle silhouettes. Header, border and authored child padding reserve
 * space before the collection is fitted. In a small allocated frame those
 * reservations contract together to at most one quarter of each safe axis,
 * leaving a useful surface at every hierarchy depth without changing styles.
 * A supplied frame avoids recursion in
 * ordered geometry and lets pointer previews use their current rendered size. */
function boundedChildAreas(project: MeridianProject | undefined, parent: MeridianNode, options: SizingOptions = {}, frame?: ChildGeometry): { interior: ChildGeometry; header: ChildGeometry } {
  const outer = frame ?? sizingBounds(project, parent, options), physical = shapeBounds(nodeShape(parent, outer));
  const shape = paddingForShape(parent.style?.shape), spacing = nodeSpacing(parent.style), border = effectiveNodeBorderWidth(parent, outer.width, outer.height);
  const side = border + spacing.childPadding, horizontalInset = physical.width * shape.x, safeWidth = physical.width * (1 - 2 * shape.x), safeHeight = physical.height * (1 - shape.top - shape.bottom);
  const minimum = geometryMinimum(safeWidth, safeHeight), horizontalSide = Math.min(side, safeWidth / 8);
  const width = Math.max(minimum, safeWidth - 2 * horizontalSide);
  const readingWidth = Math.max(1, physical.width - 2 * horizontalInset - 2 * (spacing.padding + border));
  const header = nodeContentLayout(project, { ...parent, collapsed: false }, options.context, false, readingWidth).headerHeight;
  const verticalBudget = 2 * side + header, reservationScale = verticalBudget > 0 ? Math.min(1, safeHeight / (4 * verticalBudget)) : 1;
  const verticalSide = side * reservationScale;
  const top = physical.y + physical.height * shape.top + verticalSide + header * reservationScale;
  const bottom = physical.y + physical.height * (1 - shape.bottom) - verticalSide;
  const x = physical.x + horizontalInset + horizontalSide;
  return { interior: { x, y: Math.min(top, bottom - minimum), width, height: Math.max(minimum, bottom - top) },
    header: { x, y: physical.y + physical.height * shape.top + verticalSide, width, height: header * reservationScale } };
}

export function boundedChildInterior(project: MeridianProject | undefined, parent: MeridianNode, options: SizingOptions = {}, frame?: ChildGeometry): ChildGeometry {
  return boundedChildAreas(project, parent, options, frame).interior;
}

/** The title uses the exact reservation that precedes an expanded workspace.
 * Deeply allocated frames contract its complete text composition as a unit. */
export function boundedChildHeaderBounds(project: MeridianProject | undefined, parent: MeridianNode, options: SizingOptions = {}, frame?: ChildGeometry): ChildGeometry {
  return boundedChildAreas(project, parent, options, frame).header;
}

/** An exterior display pattern may live inside a higher bounded workspace.
 * Follow that ownership without turning the Media tile itself into an enclosure. */
export function containingFixedInterior(project: MeridianProject | undefined, node: MeridianNode, options: SizingOptions = {}): ChildGeometry | undefined {
  if (node.expansionTray && !enclosingContainer(node)) return undefined;
  const seen = new Set([node.id]); let parentId = node.parentId;
  while (project && parentId && !seen.has(parentId)) {
    seen.add(parentId); const parent = project.nodes.find(candidate => candidate.id === parentId && candidate.canvasId === node.canvasId);
    if (!parent) break;
    if (parent.expansionTray && !enclosingContainer(parent)) return parent.containerVariant === 'gallery' ? fixedExpansionTrayGallery(parent)?.page : fixedExpansionTrayContent(parent);
    if (!parent.collapsed && enclosingContainer(parent) && !isEmbeddedGallery(parent))
      return boundedChildInterior(project, parent, options, { x: parent.x, y: parent.y, width: parent.width, height: parent.height });
    parentId = parent.parentId;
  }
  return undefined;
}

/** The exterior paging strip participates in a branch's footprint and contracts
 * with an enclosing workspace instead of escaping a scaled gallery slot. */
export function externalGalleryNavigationBounds(project: MeridianProject, parent: MeridianNode, item: MeridianNode, options: SizingOptions = {}, itemFrame?: ChildGeometry): ChildGeometry & { scale: number } {
  const fixed = fixedExpansionTrayGallery(parent); if (fixed) return fixed.navigation;
  const tile = itemFrame ?? sizingBounds(project, item, options), scope = containingFixedInterior(project, parent, options);
  // The page owns the strip's outer width. Its controls scale inside that
  // frame; a minimum button reading width must never enlarge the Gallery.
  const width = tile.width, desiredY = tile.y + tile.height + 5, widthScale = Math.min(1, width / 110);
  if (!scope) return { x: tile.x, y: desiredY, width, height: 38 * widthScale, scale: widthScale };
  const bottom = scope.y + scope.height, minimum = geometryMinimum(width, scope.height), y = clamp(desiredY, scope.y, bottom - minimum);
  const scale = Math.min(widthScale, (bottom - y) / 38);
  return { x: tile.x, y, width, height: 38 * scale, scale };
}

/** A containing parent's inner edge is a wall for every resize handle and for
 * numeric geometry edits. Exterior disclosure never acquires a bounding wall. */
export function constrainChildGeometry(project: MeridianProject, node: MeridianNode, proposed: ChildGeometry, options: SizingOptions = {}): ChildGeometry {
  const parent = node.parentId && project.nodes.find(candidate => candidate.id === node.parentId && candidate.canvasId === node.canvasId);
  const trayInterior = parent && (parent.containerVariant === 'gallery' ? fixedExpansionTrayGallery(parent)?.page : fixedExpansionTrayContent(parent));
  if (!parent || parent.collapsed || !enclosingContainer(parent) && !trayInterior || isEmbeddedGallery(parent)) return { ...proposed };
  if (isCellChild(node) && node.cell && orderedContainer(parent) && parent.grid) {
    const grid = gridGeometry(parent, project);
    return { x: grid.left + (node.cell.column - 1) * (grid.cellWidth + grid.gap), y: grid.top + (node.cell.row - 1) * (grid.cellHeight + grid.gap), width: grid.cellWidth, height: grid.cellHeight };
  }
  const interior = trayInterior || boundedChildInterior(project, parent, options), right = interior.x + interior.width, bottom = interior.y + interior.height;
  const minimum = geometryMinimum(interior.width, interior.height);
  const x = clamp(finite(proposed.x, node.x), interior.x, right - minimum), y = clamp(finite(proposed.y, node.y), interior.y, bottom - minimum);
  const endX = clamp(finite(proposed.x, node.x) + finite(proposed.width, node.width), x + minimum, right);
  const endY = clamp(finite(proposed.y, node.y) + finite(proposed.height, node.height), y + minimum, bottom);
  const width = endX - x, height = endY - y;
  if (node.style?.shape === 'square' || node.style?.shape === 'circle') { const side = Math.min(width, height); return { x, y, width: side, height: side }; }
  return { x, y, width, height };
}

/** Own content only; child layout can reserve this height before placing a contained collection. */
export function nodeContentLayout(project: MeridianProject | undefined, original: MeridianNode, context: SizingContext = {}, galleryObject = false, readingWidth?: number): ContentLayout {
  const display = resolveDisplay(project, original), node = projectNodeContent(display.node), style = original.style ?? {}, shape = paddingForShape(galleryObject ? undefined : style.shape);
  const spacing = nodeSpacing(style), padding = galleryObject ? 0 : spacing.padding;
  const typography = nodeTypography(original), font = { fontSize: typography.fontSize, family: style.fontFamily };
  const titleFont = { ...font, fontSize: galleryObject ? finite(style.fontSize, style.textLayout === 'balanced' ? DEFAULT_NODE_FONT_SIZE : 13) : typography.titleSize, bold: true };
  const border = galleryObject ? 0 : 2 * nodeBorderWidth(original);
  const orderedWorkspace = !galleryObject && enclosingContainer(original) && !original.collapsed && orderedContainer(original) && original.grid;
  // Ordinary text tiles must not scan the whole Work just to discover that
  // they have no grid. Smart layout measures many such nodes in one pass.
  const childCount = orderedWorkspace && ['grid', 'horizontal', 'vertical'].includes(original.containerVariant ?? '')
    ? project?.nodes.filter(child => child.parentId === original.id && child.canvasId === original.canvasId && (isCellChild(child) || child.cell)).length : undefined;
  const fittedGrid = orderedWorkspace && (!['grid', 'horizontal', 'vertical'].includes(original.containerVariant ?? '') || childCount !== 0);
  const gridWidth = fittedGrid ? 2 * (spacing.childPadding + nodeBorderWidth(original)) + orderedAxes(project, original).columns * original.grid!.cellWidth : undefined;
  const active = activeNodeContent(node), block = original.kind === 'block', hidden = block || !galleryObject && hidesOwnContent(original);
  const title = block || original.showTitle === false ? '' : original.title;
  const kind = !title ? '' : original.kind === 'reference' ? '↗ reference' : original.kind === 'module' ? 'MODULE' : original.kind === 'input' ? 'IN' : original.kind === 'number' ? '# NUMBER' : original.kind === 'output' ? original.valueType === 'number' ? '# OUT' : 'OUT' : original.kind === 'switch' ? 'SW' : '';
  const markerWidth = galleryObject || !kind ? 0 : textAdvance(kind, { fontSize: 9, family: style.fontFamily }) + kind.length * .81 + spacing.contentGap;
  const titleDrivenWidth = style.textLayout === 'balanced' && nodeSizingMode(original) === 'auto' && !galleryObject && !style.titleRotation && !!title;
  const titleWidth = titleDrivenWidth ? longestRichTextLine(title, original.titleRuns, titleFont) + markerWidth : undefined;
  const cap = gridWidth ?? (titleWidth !== undefined ? (titleWidth + 2 * padding + border) / (1 - 2 * shape.x) : clamp(finite(original.width, 300), 36, 420));
  const available = readingWidth ?? Math.max(16, cap * (1 - 2 * shape.x) - 2 * padding - border);
  const asset = hidden ? undefined : display.asset, parts: Array<{ text?: string; runs?: readonly TextRun[]; height?: number; width?: number; font?: TextMetrics; lineHeight?: number; maxHeight?: number; media?: boolean; links?: number[] }> = [];
  const bodyText = hidden ? '' : active.text ?? '';
  if (display.reference && !galleryObject) parts.push({ text: display.node === original && original.contentItems === undefined ? 'Missing reference' : 'Linked reference', font: { ...font, fontSize: 10 } });
  const media = asset && /^(image|video)\//i.test(asset.mime);
  let image: ContentSize | undefined;
  if (media) {
    const natural = assetImageSize(asset), ratio = natural ? natural.width / natural.height : /^video\//i.test(asset!.mime) ? 16 / 9 : 4 / 3;
    const mediaWidth = Math.min(available, natural?.width ?? available);
    image = { width: pixel(mediaWidth), height: pixel(mediaWidth / ratio) }; parts.push({ width: image.width, height: image.height, media: true });
  } else if (asset && /^audio\/(mpeg|mp3|ogg|wav|wave|x-wav|webm|aac|mp4)$/i.test(asset.mime)) {
    parts.push({ width: Math.min(available, 300), height: 54 });
  } else if (asset) {
    if (documentPreviewKind(asset) && !galleryObject) {
      // Two 34px icon controls, their 6px gap, and the action row's vertical padding.
      parts.push({ height: 40, width: 74 });
    } else {
      if (documentPreviewKind(asset)) parts.push({ text: `${documentPreviewKind(asset) === 'csv' ? 'Table' : 'Document'} · ${asset.name}`, height: 54 });
      else if (!galleryObject && (/^text\/(csv|tab-separated-values)$/i.test(asset.mime) || /\.(csv|tsv)$/i.test(asset.name))) parts.push(csvContentSize(asset));
      else if (/^text\/(plain|markdown|csv|tab-separated-values)$/i.test(asset.mime)) {
        if (!original.cell || !bodyText) { const text = textFromAsset(asset); parts.push({ text, runs: text === node.text ? node.textRuns : undefined, font: {
          fontSize: finite(style.fontSize, 11), family: style.fontFamily ?? 'mono' }, maxHeight: 360 }); }
      } else parts.push({ text: `${asset.mime === 'application/pdf' ? 'PDF' : 'Document'} · ${asset.name}`, height: 54 });
      parts.push({ height: 30, width: Math.min(available, textAdvance(galleryObject ? 'Open document ↗' : 'Open attachment ↗', { ...font, fontSize: 13 }) + 20) });
    }
  } else if (!hidden && active.assetId) parts.push({ text: 'Attachment unavailable.', height: 54 });
  if (bodyText) parts.push({ text: bodyText, runs: bodyText === node.text ? node.textRuns : undefined, font });
  if (!hidden && active.hyperlink) parts.push({ text: active.hyperlink.label || active.hyperlink.url, font });
  if (node.kind === 'canvas-link') parts.push({ height: 72, width: 72 });
  if (['input', 'switch'].includes(node.kind)) parts.push({ height: 28, width: 56 });
  if (node.kind === 'number') parts.push({ height: 30, width: 120 });
  if (['input', 'switch', 'number', 'output'].includes(node.kind)) parts.push({ height: node.kind === 'output' && node.valueType === 'number' ? 36 : 17, width: 68 });
  if (block) parts.length = 0;
  const longest = (text: string, metrics: TextMetrics, runs?: readonly TextRun[]) => longestRichTextLine(text, runs, metrics);
  // Blank nodes still provide a usable selection, nesting and resize surface.
  // Their identity tag is metadata and contributes no phantom reading content.
  if (!title && !kind && !parts.length) {
    const width = Math.max(1, finite(original.width, 200)), height = Math.max(1, finite(original.height, 120));
    return { width, height, headerHeight: 0, bodyHeight: 0, contentWidth: Math.max(1, width * (1 - 2 * shape.x) - 2 * padding - border) };
  }
  const basis = readingWidth ?? (gridWidth !== undefined ? available : Math.max(16 + markerWidth, Math.min(available, Math.max(longest(title, titleFont, original.titleRuns) + markerWidth, ...parts.map(part => part.width ?? (part.text ? longest(part.text, part.font ?? font, part.runs) : 0))))));
  const bodyRotation = textRotation(style.textRotation), titleRotation = textRotation(style.titleRotation);
  const rotated = (text: string, metrics: TextMetrics, lineHeight: number, rotation: number, width: number, runs?: readonly TextRun[]) => {
    const reading = Math.max(1, Math.min(width, longest(text, metrics, runs)));
    return rotatedTextEnvelope(reading, wrappedRichTextHeight(text, runs, reading, metrics, lineHeight), rotation);
  };
  const rotatedTitle = titleRotation ? rotated(title, titleFont, galleryObject ? 1.35 : 1.25, titleRotation, basis - markerWidth, original.titleRuns) : undefined;
  const rotatedParts = parts.map(part => bodyRotation && part.text && part.height === undefined
    ? rotated(part.text, part.font ?? font, part.lineHeight ?? 1.5, bodyRotation, basis, part.runs) : undefined);
  const contentWidth = titleRotation || bodyRotation ? Math.max(1, readingWidth !== undefined || gridWidth !== undefined ? basis : 0,
    (rotatedTitle?.width ?? Math.min(basis - markerWidth, longest(title, titleFont, original.titleRuns))) + markerWidth,
    ...parts.map((part, at) => rotatedParts[at]?.width ?? part.width ?? (part.text ? Math.min(basis, longest(part.text, part.font ?? font, part.runs)) : 0))) : basis;
  const headerHeight = title ? (rotatedTitle?.height ?? wrappedRichTextHeight(title, original.titleRuns, contentWidth - markerWidth, titleFont, galleryObject ? 1.35 : 1.25)) + padding : 0;
  const itemHeight = (part: typeof parts[number]) => {
    if (part.links) {
      let lines = 1, occupied = 0;
      for (const width of part.links) {
        if (occupied && occupied + spacing.contentGap + width > contentWidth) { lines++; occupied = 0; }
        if (width > contentWidth) { lines += Math.ceil(width / contentWidth) - 1; occupied = width % contentWidth; }
        else occupied += (occupied ? spacing.contentGap : 0) + width;
      }
      return lines * 15 + (lines - 1) * spacing.contentGap;
    }
    return part.height ?? Math.min(part.maxHeight ?? Infinity, wrappedRichTextHeight(part.text ?? '', part.runs, contentWidth, part.font ?? font, part.lineHeight ?? 1.5));
  };
  const bodyHeight = (parts.length ? padding : 0) + parts.reduce((height, part, at) => height + (rotatedParts[at]?.height ?? itemHeight(part)) +
    (at > 0 && parts[at - 1]?.media && part.text ? spacing.textMediaGap : at > 0 || !!title ? spacing.contentGap : padding), 0) + (!parts.length && title ? padding : 0);
  let width = pixel((contentWidth + 2 * padding + border) / (1 - 2 * shape.x));
  // Embedded object measurements retain fractions so nested Gallery frames do
  // not accumulate a spare pixel for each title, paragraph and level.
  const contentHeight = (headerHeight + bodyHeight + border) / (1 - shape.top - shape.bottom);
  let height = galleryObject ? contentHeight : pixel(contentHeight);
  if (!galleryObject && readingWidth === undefined && nodeSizingMode(original) === 'auto' && balancedTextLayout(project, original)) {
    const textComposition = Math.max(0, headerHeight + bodyHeight - 2 * padding);
    height = pixel(Math.max(contentHeight, (textComposition / DEFAULT_TEXT_OCCUPANCY + border) / (1 - shape.top - shape.bottom)));
  }
  const ratio = galleryObject ? undefined : shapeAspectRatio(style.shape);
  if (ratio !== undefined) { height = pixel(Math.max(height, width / ratio)); width = pixel(height * ratio); }
  return { width, height, headerHeight, bodyHeight, contentWidth, ...(titleRotation || bodyRotation ? { rotationReadingWidth: basis } : {}), ...(image ? { media: image } : {}) };
}
export function intrinsicContentSize(project: MeridianProject | undefined, node: MeridianNode, context: SizingContext = {}): ContentSize {
  const { width, height } = nodeContentLayout(project, node, context); return { width, height };
}
/** Embedded objects inherit the collection's typography, but keep their own content identity. */
export function galleryObjectLayout(project: MeridianProject, parent: MeridianNode, item: MeridianNode, context: SizingContext = {}, readingWidth?: number): ContentLayout {
  return nodeContentLayout(project, { ...item, style: { fontFamily: parent.style?.fontFamily, fontSize: parent.style?.fontSize,
    contentGap: parent.style?.contentGap, textMediaGap: parent.style?.textMediaGap,
    textRotation: parent.style?.textRotation, titleRotation: parent.style?.titleRotation, ...item.style } }, context, true, readingWidth);
}

/** Exterior Gallery cards occupy one authored slot. Text and the active page
 * never participate in its dimensions; their content fits this common frame. */
export function exteriorGalleryItemFrame(project: MeridianProject, parent: MeridianNode): ChildGeometry | undefined {
  if (!isGallery(parent) || isEmbeddedGallery(parent) || parent.collapsed) return;
  const fixed = fixedExpansionTrayGallery(parent); if (fixed) return fixed.page;
  const children = project.nodes.filter(child => child.parentId === parent.id && child.canvasId === parent.canvasId);
  if (!children.length) return;
  return { x: parent.expansionTray ? parent.x + parent.expansionTray.offset.x : children[0].x,
    y: parent.expansionTray ? parent.y + parent.expansionTray.offset.y : children[0].y,
    width: Math.max(...children.map(child => child.width)), height: Math.max(...children.map(child => child.height)) };
}

function enclosingContainer(node: MeridianNode): boolean {
  return ['container', 'spreadsheet', 'module'].includes(node.kind);
}
function fixedBounds(node: MeridianNode): SizingBounds {
  const minimum = node.parentId ? Number.MIN_VALUE : node.kind === 'junction' ? 2 : 1;
  return { id: node.id, x: finite(node.x, 0), y: finite(node.y, 0), width: Math.max(minimum, finite(node.width, 180)), height: Math.max(minimum, finite(node.height, 120)) };
}

/** Visible exterior collections are part of a branch's footprint, never part of
 * the Media parent's own tile. Windows reserve only their scrolling viewport;
 * galleries reserve the active slot (or all possible slot sizes outside a view). */
export function branchSizingBounds(project: MeridianProject | undefined, node: MeridianNode, options: SizingOptions = {}, seen = new Set<string>()): SizingBounds {
  const own = sizingBounds(project, node, options, seen);
  if (node.collapsed || seen.has(node.id) || enclosingContainer(node) || options.excludeTrayContents && node.expansionTray) return own;
  const visited = new Set(seen).add(node.id), boxes: Array<ContentSize & { x: number; y: number }> = [own];
  const children = options.children?.(node.id) ?? project?.nodes.filter(child => child.parentId === node.id && child.canvasId === node.canvasId) ?? [];
  if (node.containerVariant === 'window' && node.grid) {
    const grid = gridGeometry(node, project);
    boxes.push({ x: grid.left, y: grid.top, width: grid.visibleColumns * grid.cellWidth, height: grid.visibleRows * grid.cellHeight });
  } else {
    const context = options.context?.galleryItems;
    const chosenId = context && typeof (context as ReadonlyMap<string, string>).get === 'function' ? (context as ReadonlyMap<string, string>).get(node.id)
      : (context as Readonly<Record<string, string>> | undefined)?.[node.id];
    const exposed = isGallery(node) && context ? [children.find(child => child.id === chosenId) ?? children[0]].filter((child): child is MeridianNode => !!child) : children;
    for (const child of exposed) {
      if (options.isVisible && !options.isVisible(child.id)) continue;
      boxes.push(branchSizingBounds(project, child, options, visited));
      if (isGallery(node)) {
        if (project) boxes.push(externalGalleryNavigationBounds(project, node, child, options));
      }
    }
  }
  const x = Math.min(...boxes.map(box => box.x)), y = Math.min(...boxes.map(box => box.y));
  return { id: node.id, x, y, width: Math.max(...boxes.map(box => box.x + box.width)) - x, height: Math.max(...boxes.map(box => box.y + box.height)) - y };
}

/** Effective world geometry; never mutates authored dimensions, positions, children or references. */
export function sizingBounds(project: MeridianProject | undefined, node: MeridianNode, options: SizingOptions = {}, _seen = new Set<string>()): SizingBounds {
  const foundParent = node.parentId ? options.node ? options.node(node.parentId) : project?.nodes.find(parent => parent.id === node.parentId) : undefined;
  const parent = foundParent?.canvasId === node.canvasId ? foundParent : undefined;
  const galleryFrame = project && parent && exteriorGalleryItemFrame(project, parent);
  if (galleryFrame) return { id: node.id, ...galleryFrame };
  // The cell owns its tile's dimensions, including a collapsed nested container.
  // Disclosure cannot leave a differently shaped/sized hole in an ordered surface.
  if (isCellChild(node) && parent && orderedContainer(parent) && parent.grid) {
    const geometry = gridGeometry(parent, project);
    return { ...fixedBounds(node), width: geometry.cellWidth, height: geometry.cellHeight };
  }
  const formal = enclosingContainer(node);
  // The active expanded enclosure is a workspace frame, not intrinsic content.
  // Children fit it; a move/reflow cannot enlarge an authored frame. Fixed
  // collapsed geometry is independently authored and must remain resizable.
  if (nodeSizingMode(node) === 'fixed' || formal && !node.collapsed) return fixedBounds(node);
  const own = intrinsicContentSize(project, node, options.context), bounds: SizingBounds = { id: node.id, x: finite(node.x, 0), y: finite(node.y, 0), ...own };
  if (node.collapsed && formal) {
    // A collapsed nested container still fits the tile assigned by its expanded
    // owner. Its independent hidden workspace dimensions remain recoverable.
    if (parent && !parent.collapsed && enclosingContainer(parent) && !isEmbeddedGallery(parent))
      return { ...bounds, width: Math.min(bounds.width, node.width), height: Math.min(bounds.height, node.height) };
    return bounds;
  }
  // Ordinary nodes keep their own content tile. Their display pattern lives on
  // the surrounding canvas and cannot inflate the parent's backdrop.
  return bounds;
}
