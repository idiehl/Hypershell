import type { MeridianNode, MeridianProject } from './core/types.ts';
import { isEnclosingContainer, isOrderedContainer } from './core/cells.ts';
import { isGallery, isEmbeddedGallery } from './core/gallery.ts';
import { balancedTextLayout, boundedChildInterior, boundedChildHeaderBounds, galleryObjectLayout, intrinsicContentSize, nodeContentLayout, nodeSizingMode, type ChildGeometry, type SizingContext } from './core/sizing.ts';
import { layoutTextRotations } from './text-rotation.ts';
import { contentFrameScale } from './core/content-scale.ts';
import { fitInlineDocument } from './document-node-scene.ts';
import { fitGalleryCanvasLauncher, fitNodeCanvasLauncher, restoreCanvasLauncherFit } from './launcher-content-fit.ts';

const ownedProperties = ['width', 'height', 'max-width', 'box-sizing', 'flex', 'min-height', 'max-height', 'overflow', 'position', 'left', 'top', 'transform', 'transform-origin', 'align-self'] as const;
type SurfaceState = { element: HTMLElement; declarations: Array<{ property: string; value: string; priority: string }> };
const surfacesByCard = new WeakMap<HTMLElement, SurfaceState[]>();
const pixel = (value: string) => Number.parseFloat(value) || 0;

type TextFit = { scale: number; surfaces: SurfaceState['declarations'][]; cost: number };
type TextFitCache = { entries: Map<string, TextFit>; bytes: number; fontsReady: boolean };
const textFitCaches = new WeakMap<Document, TextFitCache>();
const textFitLimit = 32 * 1024 * 1024, textFitEntries = 4096;
const fitStylesheets = new WeakMap<Document, { text: string; epoch: number }>();
export interface ContainedContentPass { readonly world: HTMLElement; readonly signature: string; readonly dynamicGeometrySelector: string }
const fitStyleProperties = ['font-family', 'font-size', 'font-weight', 'font-style', 'font-stretch', 'font-kerning', 'font-feature-settings', 'font-variation-settings',
  'line-height', 'letter-spacing', 'word-spacing', 'white-space', 'overflow-wrap', 'word-break', 'hyphens', 'text-transform', 'text-indent', 'text-align', 'writing-mode', 'direction', 'tab-size',
  'display', 'box-sizing', 'padding-top', 'padding-right', 'padding-bottom', 'padding-left', 'margin-top', 'margin-right', 'margin-bottom', 'margin-left',
  'border-top-width', 'border-right-width', 'border-bottom-width', 'border-left-width', 'gap', 'align-items', 'justify-content'] as const;
function textFitCache(document: Document): TextFitCache {
  let cache = textFitCaches.get(document); if (cache) return cache;
  cache = { entries: new Map(), bytes: 0, fontsReady: !document.fonts || document.fonts.status === 'loaded' }; textFitCaches.set(document, cache);
  const clear = () => { cache!.entries.clear(); cache!.bytes = 0; cache!.fontsReady = document.fonts.status === 'loaded'; };
  document.fonts?.addEventListener('loading', clear); document.fonts?.addEventListener('loadingdone', clear); document.fonts?.addEventListener('loadingerror', clear);
  if (!cache.fontsReady) void document.fonts?.ready.then(clear); return cache;
}
function surfaceDeclarations(element: HTMLElement): SurfaceState['declarations'] {
  return ownedProperties.map(property => ({ property, value: element.style.getPropertyValue(property), priority: element.style.getPropertyPriority(property) }));
}
/** Remove transient/structural conditions while retaining the selector's
 * ancestor and target constraints. The result is a conservative superset of
 * elements a dynamic rule can affect, including currently inactive hover rules.
 * Unsupported selector syntax opts out at the call site. */
function potentialGeometrySelector(selector: string): string {
  let result = '', compound = false;
  for (let at = 0; at < selector.length;) {
    const character = selector[at]!;
    if (character === '\\') { result += selector.slice(at, at + 2); at += 2; compound = true; continue; }
    if (character === '[') {
      const start = at++; let quote = '';
      for (; at < selector.length; at++) {
        const next = selector[at]!;
        if (next === '\\') { at++; continue; }
        if (quote) { if (next === quote) quote = ''; continue; }
        if (next === '"' || next === "'") { quote = next; continue; }
        if (next === ']') { at++; break; }
      }
      result += selector.slice(start, at); compound = true; continue;
    }
    if (character === ':') {
      const start = at++; if (selector[at] === ':') at++;
      const nameStart = at; while (at < selector.length && /[\w-]/.test(selector[at]!)) at++;
      const name = selector.slice(nameStart, at).toLowerCase();
      if (selector[at] === '(') {
        const contents = ++at; let depth = 1, quote = '';
        for (; at < selector.length && depth; at++) {
          const next = selector[at]!;
          if (next === '\\') { at++; continue; }
          if (quote) { if (next === quote) quote = ''; continue; }
          if (next === '"' || next === "'") { quote = next; continue; }
          if (next === '(') depth++; else if (next === ')') depth--;
        }
        if (depth) return selector; // Invalid CSS stays conservative via matches().
        if (name === 'is' || name === 'where') { result += `:${name}(${potentialGeometrySelector(selector.slice(contents, at - 1))})`; compound = true; continue; }
      } else if (at === nameStart) { result += selector.slice(start, at); continue; }
      if (!compound) { result += '*'; compound = true; }
      continue;
    }
    result += character; at++;
    if (/[\s>+~,]/.test(character)) compound = false; else compound = true;
  }
  return result;
}
/** The shared scene owns these fresh cards and their stylesheet. Capture the
 * inherited environment once, rather than forcing computed styles on every
 * descendant of every unchanged card. External callers retain the DOM-measured
 * path. Unreadable stylesheets and registered web fonts deliberately opt out. */
export function createContainedContentPass(world: HTMLElement): ContainedContentPass | undefined {
  const document = world.ownerDocument;
  if (!world.isConnected || document.fonts?.status === 'loading' || document.fonts?.size) return;
  let css: string; const dynamicGeometrySelectors = new Set<string>();
  try {
    const sheets = [...document.styleSheets, ...document.adoptedStyleSheets];
    const geometryProperty = /^(font|line-height|letter-spacing|word-spacing|white-space|overflow-wrap|word-break|hyphens|text-(?:indent|align|transform|orientation|combine|size|wrap)|writing-mode|direction|tab-size|display|box-sizing|padding|margin|border(?:-(?:top|right|bottom|left|block|inline)(?:-(?:start|end))?)?(?:-width)?$|border.*width|gap|align-|justify-|width|height|min-|max-|flex|grid|position|inset|top|right|bottom|left|transform|zoom|--)/;
    const rules = (items: CSSRuleList): unknown[] => [...items].map(rule => {
      // Only rules capable of affecting this card require measured styles.
      // UI-only spans elsewhere in the editor must not disable the entire pass.
      if (rule instanceof CSSStyleRule && /[+~]|:(?:hover|focus|active|visited|checked|nth-|first-|last-|only-)/.test(rule.selectorText) &&
          [...rule.style].some(property => geometryProperty.test(property))) dynamicGeometrySelectors.add(potentialGeometrySelector(rule.selectorText));
      if (rule.cssText.startsWith('@container')) throw new Error('Container-dependent fit styles');
      if (rule instanceof CSSImportRule && rule.styleSheet) return [rule.cssText, read(rule.styleSheet)];
      if (rule instanceof CSSMediaRule) return [rule.conditionText, matchMedia(rule.conditionText).matches, rules(rule.cssRules)];
      if ('cssRules' in rule) return [rule.cssText, rules((rule as CSSGroupingRule).cssRules)];
      return rule.cssText;
    });
    const read = (sheet: CSSStyleSheet): unknown => [sheet.disabled, sheet.media.mediaText, !sheet.media.mediaText || matchMedia(sheet.media.mediaText).matches, rules(sheet.cssRules)];
    css = JSON.stringify(sheets.map(read));
  } catch { return; }
  let stylesheet = fitStylesheets.get(document);
  if (!stylesheet || stylesheet.text !== css) { stylesheet = { text: css, epoch: (stylesheet?.epoch ?? 0) + 1 }; fitStylesheets.set(document, stylesheet); }
  const ancestors: unknown[] = [];
  for (let element: HTMLElement | null = world.parentElement; element; element = element.parentElement) {
    ancestors.push([element.tagName, [...element.attributes].map(attribute => [attribute.name, attribute.value])]);
  }
  const style = getComputedStyle(world), custom = [...style].filter(property => property.startsWith('--')).map(property => [property, style.getPropertyValue(property)]);
  return { world, dynamicGeometrySelector: [...dynamicGeometrySelectors].join(','), signature: JSON.stringify([stylesheet.epoch, ancestors, fitStyleProperties.map(property => style.getPropertyValue(property)), custom,
    window.innerWidth, window.innerHeight, window.devicePixelRatio]) };
}
function simpleTextMarkup(node: MeridianNode, surfaces: HTMLElement[]): { elements: HTMLElement[]; html: string[] } | undefined {
  if (!['media', 'text'].includes(node.kind) || node.containerVariant && !['contained', 'spatial'].includes(node.containerVariant) || node.assetId || node.targetId || node.attachments?.length || node.contentType && !['text', 'link'].includes(node.contentType) ||
      node.style?.titleRotation || node.style?.textRotation || !surfaces.length) return;
  const elements = surfaces.flatMap(surface => [surface, ...surface.querySelectorAll<HTMLElement>('*')]);
  if (elements.length > 24 || elements.some(element => !['HEADER', 'DIV', 'SPAN', 'BR', 'B', 'STRONG', 'I', 'EM', 'U'].includes(element.tagName) && !(element.tagName === 'A' && element.classList.contains('ms-content-link')) ||
      element.hasAttribute('data-text-rotation') || element.matches('[contenteditable="true"],.ms-gallery,.ms-file-placeholder,.mdv-viewer,.ms-canvas-open'))) return;
  const html = surfaces.map(surface => surface.outerHTML.replace(/ style=""/g, '')); if (html.reduce((total, value) => total + value.length, 0) > 32768) return;
  return { elements, html };
}
function passTextFitKey(pass: ContainedContentPass | undefined, card: HTMLElement, node: MeridianNode, bounds: ChildGeometry, surfaces: HTMLElement[], intrinsic: boolean): { cache: TextFitCache; key: string } | undefined {
  if (!pass || card.parentElement !== pass.world || card.ownerDocument.fonts?.status === 'loading' || card.ownerDocument.fonts?.size) return;
  const markup = simpleTextMarkup(node, surfaces); if (!markup) return;
  try { if (pass.dynamicGeometrySelector && (card.matches(pass.dynamicGeometrySelector) || markup.elements.some(element => element.matches(pass.dynamicGeometrySelector)))) return; }
  catch { return; }
  const cache = textFitCache(card.ownerDocument); if (!cache.fontsReady) return;
  const attributes = [...card.attributes].filter(attribute => attribute.name !== 'style').map(attribute => [attribute.name, attribute.value]);
  const styles = [...card.style].filter(property => !['left', 'top'].includes(property)).map(property => [property, card.style.getPropertyValue(property), card.style.getPropertyPriority(property)]);
  const key = JSON.stringify(['scene', pass.signature, bounds.width, bounds.height, node.width, node.height, node.sizeMode, node.collapsed, node.style, intrinsic, attributes, styles, markup.html]);
  return key.length * 2 <= textFitLimit / 8 ? { cache, key } : undefined;
}
function simpleTextFitKey(card: HTMLElement, node: MeridianNode, bounds: ChildGeometry, surfaces: HTMLElement[], intrinsic: boolean, frameStyle: CSSStyleDeclaration): { cache: TextFitCache; key: string } | undefined {
  const markup = simpleTextMarkup(node, surfaces); if (!markup) return;
  const { elements, html } = markup;
  const cache = textFitCache(card.ownerDocument); if (!cache.fontsReady || card.ownerDocument.fonts?.status === 'loading') return;
  const customFamilies = new Set<string>(); card.ownerDocument.fonts?.forEach(face => customFamilies.add(face.family.replace(/["']/g, '').toLowerCase()));
  const styles = [frameStyle, ...elements.map(element => getComputedStyle(element))];
  if (customFamilies.size && styles.some(style => style.fontFamily.split(',').some(family => customFamilies.has(family.trim().replace(/["']/g, '').toLowerCase())))) return;
  const key = JSON.stringify([bounds.width, bounds.height, node.width, node.height, node.kind, node.sizeMode, node.collapsed, node.style, intrinsic, html,
    styles.map(style => fitStyleProperties.map(property => style.getPropertyValue(property)))]);
  return key.length * 2 <= textFitLimit / 8 ? { cache, key } : undefined;
}
function applyTextFit(card: HTMLElement, surfaces: HTMLElement[], cached: { cache: TextFitCache; key: string } | undefined): number | undefined {
  const fit = cached?.cache.entries.get(cached.key); if (!cached || !fit) return;
  cached.cache.entries.delete(cached.key); cached.cache.entries.set(cached.key, fit);
  surfacesByCard.set(card, surfaces.map(element => ({ element, declarations: surfaceDeclarations(element) })));
  surfaces.forEach((surface, index) => { for (const declaration of fit.surfaces[index]!) {
    if (declaration.value) surface.style.setProperty(declaration.property, declaration.value, declaration.priority);
    else surface.style.removeProperty(declaration.property);
  } });
  card.dataset.contentFit = String(fit.scale); return fit.scale;
}
function rememberTextFit(cache: TextFitCache, key: string, scale: number, surfaces: HTMLElement[], readingWidth: number, transforms: string[]): void {
  // CSSOM serialization rounds long subpixel values. Retain the authored fit
  // inputs so a reused measurement remains exact even at extreme camera zoom.
  const declarations = surfaces.map((surface, index) => surfaceDeclarations(surface).map(declaration => ({ ...declaration,
    value: declaration.property === 'width' ? `${readingWidth}px` : declaration.property === 'transform' ? transforms[index]! : declaration.value })));
  const cost = key.length * 2 + JSON.stringify(declarations).length * 2 + 128;
  if (cost > textFitLimit) return;
  const prior = cache.entries.get(key); if (prior) { cache.bytes -= prior.cost; cache.entries.delete(key); }
  while (cache.entries.size && (cache.entries.size >= textFitEntries || cache.bytes + cost > textFitLimit)) {
    const oldest = cache.entries.keys().next().value!; cache.bytes -= cache.entries.get(oldest)!.cost; cache.entries.delete(oldest);
  }
  cache.entries.set(key, { scale, surfaces: declarations, cost }); cache.bytes += cost;
}

function restoreSurfaces(card: HTMLElement) {
  for (const surface of surfacesByCard.get(card) ?? []) for (const declaration of surface.declarations) {
    if (declaration.value) surface.element.style.setProperty(declaration.property, declaration.value, declaration.priority);
    else surface.element.style.removeProperty(declaration.property);
  }
  surfacesByCard.delete(card); delete card.dataset.contentFit; delete card.dataset.rotationFit; card.classList.remove('ms-content-fitting');
}

/** Intrinsic sizing has already measured the authored fonts. Applying a second
 * frame ratio would make short labels and narrow rotated strips microscopic.
 * Allocated slots and resize previews still use the common reading frame. */
function intrinsicAutomaticFrame(project: MeridianProject, node: MeridianNode, bounds: ChildGeometry, context: SizingContext): boolean {
  if (nodeSizingMode(node) !== 'auto' || !node.collapsed && project.nodes.some(child => child.parentId === node.id)) return false;
  const parent = node.parentId && project.nodes.find(candidate => candidate.id === node.parentId);
  if (parent && !parent.collapsed && (isOrderedContainer(parent) || isGallery(parent))) return false;
  const intrinsic = intrinsicContentSize(project, node, context);
  return (['width', 'height'] as const).every(axis => Math.abs(bounds[axis] - intrinsic[axis]) <= Math.max(1e-8, Math.abs(intrinsic[axis]) * 1e-9));
}

/** A Gallery page is a viewport, not a postcard scaled together with its title.
 * Its object frame fills both axes while raster/video pixels retain their aspect. */
function fitGalleryViewport(card: HTMLElement, project: MeridianProject, node: MeridianNode, bounds: ChildGeometry, context: SizingContext): void {
  const body = card.querySelector<HTMLElement>(':scope > .ms-node-body'); if (!body) return;
  const interior = boundedChildInterior(project, node, { context }, bounds), style = getComputedStyle(card);
  const originX = pixel(style.borderLeftWidth), originY = pixel(style.borderTopWidth);
  body.classList.add('ms-gallery-viewport');
  Object.assign(body.style, { position: 'absolute', left: `${interior.x - bounds.x - originX}px`, top: `${interior.y - bounds.y - originY}px`, width: `${interior.width}px`, height: `${interior.height}px`, padding: '0', transform: 'none', overflow: 'hidden', display: 'flex', flex: 'none', boxSizing: 'border-box' });
  const galleries = [...body.querySelectorAll<HTMLElement>('.ms-gallery')];
  for (const gallery of galleries) {
    Object.assign(gallery.style, { width: '100%', height: '100%', display: 'flex', flex: '1 1 0', minHeight: '0', overflow: 'hidden' });
    const page = gallery.querySelector<HTMLElement>(':scope > .ms-gallery-item,:scope > .ms-gallery-empty'); if (!page) continue;
    Object.assign(page.style, { width: '100%', height: '0', flex: '1 1 0', minHeight: '0', maxHeight: 'none', overflow: 'hidden', position: 'relative' });
    const navigation = gallery.querySelector<HTMLElement>(':scope > .ms-gallery-navigation');
    if (navigation) { Object.assign(navigation.style, { height: '27px', minHeight: '0', flex: '0 0 27px', overflow: 'hidden' }); }
    const media = [...page.querySelectorAll<HTMLElement>(':scope > .ms-image,:scope > .ms-video')];
    if (media.length) {
      for (const item of media) Object.assign(item.style, { width: '100%', height: '0', minHeight: '0', maxHeight: 'none', maxWidth: '100%', flex: '1 1 0', objectFit: 'contain' });
      continue;
    }
    if (page.querySelector(':scope > .ms-gallery')) continue;
    let composition = page.querySelector<HTMLElement>(':scope > .ms-gallery-object-content');
    if (!composition) {
      composition = document.createElement('div'); composition.className = 'ms-gallery-object-content';
      for (const child of [...page.children]) if (!child.classList.contains('ms-node-background')) composition.append(child);
      page.append(composition);
    }
    const width = Math.max(.001, page.clientWidth), height = Math.max(.001, page.clientHeight);
    if (fitGalleryCanvasLauncher(composition, width, height, node.style?.contentGap ?? 5, node.style?.contentSizing === 'fixed')) continue;
    const baseline = contentFrameScale(width, height), readingWidth = Math.max(1, width / baseline);
    Object.assign(composition.style, { position: 'absolute', left: '0', top: '0', width: `${readingWidth}px`, height: 'auto', transform: 'none', transformOrigin: '0 0' });
    layoutTextRotations(composition, readingWidth);
    const naturalWidth = Math.max(1, readingWidth, composition.scrollWidth), naturalHeight = Math.max(1, composition.scrollHeight, composition.offsetHeight);
    const scale = Math.min(baseline, width / naturalWidth, height / naturalHeight);
    composition.style.transform = `translate(${Math.max(0, width - naturalWidth * scale) / 2}px, ${Math.max(0, height - naturalHeight * scale) / 2}px) scale(${scale})`;
  }
  card.dataset.galleryViewport = 'true';
}

/** Fit each node's natural reading composition to its available frame. The card, selection outline,
 * backdrop, world-space descendants and authored typography never scale here.
 * Native transforms preserve vector glyphs, inputs, media and hyperlink hit
 * testing at every camera zoom. Tiny fonts never quantize into a CSS pixel.
 * Repeated preview calls first restore the original declarations. */
export function fitContainedContent(card: HTMLElement, project: MeridianProject, node: MeridianNode,
  bounds: ChildGeometry, context: SizingContext = {}, pass?: ContainedContentPass): number {
  restoreCanvasLauncherFit(card);
  restoreSurfaces(card);
  if (fitInlineDocument(card,bounds)) return 1;
  // Unbounded Media Windows keep their own reading tile; their scrolling
  // surface is a separate world-space object outside this card.
  const galleryViewport = !node.collapsed && isEmbeddedGallery(node);
  const workspace = !node.collapsed && isEnclosingContainer(node);
  const balanced = balancedTextLayout(project, node);
  const intrinsic = !workspace && intrinsicAutomaticFrame(project, node, bounds, context);
  if (card.isConnected && !workspace && !['junction', 'canvas'].includes(node.kind)) {
    const launcherFit = fitNodeCanvasLauncher(card, project, node, bounds, context, intrinsic ? 1 : undefined);
    if (launcherFit !== undefined) return launcherFit;
  }
  if (card.isConnected && node.style?.contentSizing === 'fixed') layoutTextRotations(card);
  if (!card.isConnected || node.style?.contentSizing === 'fixed' || ['junction', 'canvas'].includes(node.kind)) {
    if (card.isConnected && galleryViewport) fitGalleryViewport(card, project, node, bounds, context);
    return 1;
  }

  const surfaces = [...card.children].filter((element): element is HTMLElement => element instanceof HTMLElement &&
    (element.classList.contains('ms-node-header') || !workspace && element.classList.contains('ms-node-body')));
  if (!surfaces.length) { if (galleryViewport) fitGalleryViewport(card, project, node, bounds, context); return 1; }
  card.classList.add('ms-content-fitting');
  const preparedText = !workspace && !galleryViewport ? passTextFitKey(pass, card, node, bounds, surfaces, intrinsic) : undefined;
  const preparedScale = applyTextFit(card, surfaces, preparedText); if (preparedScale !== undefined) return preparedScale;
  const frameStyle = getComputedStyle(card), insetX = pixel(frameStyle.paddingLeft) + pixel(frameStyle.paddingRight) +
    pixel(frameStyle.borderLeftWidth) + pixel(frameStyle.borderRightWidth);
  const insetY = pixel(frameStyle.paddingTop) + pixel(frameStyle.paddingBottom) +
    pixel(frameStyle.borderTopWidth) + pixel(frameStyle.borderBottomWidth);
  const headerSlot = workspace ? boundedChildHeaderBounds(project, node, { context }, bounds) : undefined;
  const availableWidth = headerSlot ? Math.max(Number.MIN_VALUE, headerSlot.width) : Math.max(.001, bounds.width - insetX);
  const baseline = intrinsic || balanced ? 1 : contentFrameScale(bounds.width, bounds.height);
  // Equal frames start at equal authored font scales. A short label never
  // enlarges just because its natural text width is smaller than a sibling's.
  // The logical reading width follows the frame, retaining vector subpixels.
  const readingWidth = Math.max(1, availableWidth / baseline);
  const cachedText = preparedText ?? (!workspace && !galleryViewport ? simpleTextFitKey(card, node, bounds, surfaces, intrinsic, frameStyle) : undefined);
  if (!preparedText) { const cachedScale = applyTextFit(card, surfaces, cachedText); if (cachedScale !== undefined) return cachedScale; }
  const natural = intrinsic ? nodeContentLayout(project, node, context) : nodeContentLayout(project,
    { ...node, width: Math.max(300, node.width), ...(workspace ? { grid: undefined } : {}) }, context,
    false, Math.max(.001, readingWidth - 2 * (node.style?.padding ?? 5)));
  // Expanded structural children keep their world-space coordinates; only the
  // title fits its reserved header slot, never the workspace beneath it.
  const availableHeight = headerSlot ? Math.max(Number.MIN_VALUE, headerSlot.height) : Math.max(.001, bounds.height - insetY);
  const saved = surfaces.map(element => ({ element, declarations: ownedProperties.map(property => ({ property,
    value: element.style.getPropertyValue(property), priority: element.style.getPropertyPriority(property) })) }));
  surfacesByCard.set(card, saved);
  for (const surface of surfaces) {
    surface.style.width = `${readingWidth}px`;
    surface.style.boxSizing = 'border-box';
    surface.style.flex = '0 0 auto'; surface.style.minHeight = '0'; surface.style.maxHeight = 'none';
    surface.style.alignSelf = node.style?.textAlign === 'left' ? 'flex-start' : node.style?.textAlign === 'right' ? 'flex-end' : 'center';
    surface.style.overflow = 'visible'; surface.style.height = 'auto'; surface.style.position = 'absolute';
    surface.style.transform = 'none'; surface.style.transformOrigin = '0 0';
  }
  for (const surface of surfaces) {
    for (const text of surface.querySelectorAll<HTMLElement>('[data-text-rotation]')) {
      const itemElement = text.closest<HTMLElement>('.ms-gallery-item'), galleryElement = text.closest<HTMLElement>('.ms-gallery');
      const parent = galleryElement && project.nodes.find(candidate => candidate.id === galleryElement.dataset.galleryId);
      const item = itemElement && project.nodes.find(candidate => candidate.id === itemElement.dataset.galleryItemId);
      const layout = parent && item ? galleryObjectLayout(project, parent, item, context,
        Math.max(1, readingWidth - 2 * (node.style?.padding ?? 5))) : natural;
      text.dataset.rotationReadingWidth = String(layout.rotationReadingWidth ?? layout.contentWidth);
    }
    layoutTextRotations(surface, readingWidth);
  }
  // A fixed tile may decode its picture after the scene's first layout. Use the
  // same inert header-derived media envelope as automatic tiles, rather than
  // letting that later decode silently invalidate the containment measurement.
  for (const surface of surfaces) for (const media of surface.querySelectorAll<HTMLElement>('.ms-image,.ms-video')) {
    const itemElement = media.closest<HTMLElement>('.ms-gallery-item'), galleryElement = media.closest<HTMLElement>('.ms-gallery');
    const parent = galleryElement && project.nodes.find(candidate => candidate.id === galleryElement.dataset.galleryId);
    const item = itemElement && project.nodes.find(candidate => candidate.id === itemElement.dataset.galleryItemId);
    const envelope = parent && item ? galleryObjectLayout(project, parent, item, context,
      Math.max(1, readingWidth - 2 * (node.style?.padding ?? 5))).media : natural.media;
    if (!envelope) continue;
    saved.push({ element: media, declarations: ownedProperties.map(property => ({ property,
      value: media.style.getPropertyValue(property), priority: media.style.getPropertyPriority(property) })) });
    media.style.width = `${envelope.width}px`; media.style.height = `${envelope.height}px`;
    media.style.maxWidth = '100%'; media.style.maxHeight = 'none'; media.style.flex = '0 0 auto';
  }
  // offset measurements are local CSS pixels, independent of the canvas camera.
  // Explicit image envelopes in the shared scene are included even before load.
  const measure = () => {
    const rotatedWidths = surfaces.flatMap(surface => [...surface.querySelectorAll<HTMLElement>('.ms-rotated-text-frame')].map(frame => {
      let width = Number.parseFloat(frame.style.width) || frame.offsetWidth;
      for (let parent = frame.parentElement; parent; parent = parent.parentElement) {
        const style = getComputedStyle(parent); width += pixel(style.paddingLeft) + pixel(style.paddingRight);
        if (parent === surface) break;
      }
      return width;
    }));
    const width = Math.max(readingWidth, ...surfaces.map(surface => surface.scrollWidth), ...rotatedWidths);
    // Centered children wider than their first wrapping basis have negative
    // overflow that scrollWidth cannot observe. Give the composition its full
    // reserved envelope before applying the single outer scale/translation.
    if (rotatedWidths.length) for (const surface of surfaces) surface.style.width = `${width}px`;
    const heights = surfaces.map(surface => Math.max(surface.offsetHeight, surface.scrollHeight));
    const height = heights.reduce((height, value) => height + value, 0);
    return { width, heights, scale: Math.min(baseline, availableWidth / Math.max(.001, width), availableHeight / Math.max(.001, height)) };
  };
  let measured = measure();
  const rotatedTexts = surfaces.flatMap(surface => [...surface.querySelectorAll<HTMLElement>('[data-text-rotation]')]);
  if (!workspace && nodeSizingMode(node) === 'fixed' && rotatedTexts.length) {
    // Long quarter-turned reading columns stacked in a squat frame waste most
    // of its width. Choose a wrapping composition that gives its native glyphs
    // the largest common fitted scale. A small bounded candidate set uses the same
    // ordinary reading units at every physical size/camera zoom; microscopic
    // frames never become one-character wrapping columns. No authored font,
    // dimensions, content, media or automatic node sizing are changed.
    const bases = rotatedTexts.map(text => Number(text.dataset.rotationReadingWidth) || natural.contentWidth);
    rotatedTexts.forEach((text, index) => text.dataset.rotationWordLimit = String(bases[index]));
    const layout = (factor: number | readonly number[]) => {
      rotatedTexts.forEach((text, index) => text.dataset.rotationReadingWidth = String(bases[index]! * (typeof factor === 'number' ? factor : factor[index]!)));
      for (const surface of surfaces) {
        surface.style.width = `${readingWidth}px`;
        layoutTextRotations(surface, readingWidth);
      }
      return measure();
    };
    let best = 1;
    for (const factor of [.8, .65, .5, .35, .25, .18, 1.3]) {
      const candidate = layout(factor);
      if (candidate.scale > measured.scale * 1.001) { measured = candidate; best = factor; }
    }
    const factors = bases.map(() => best);
    // The title and paragraph can have different optimal line breaks. Four
    // neighboring observations refine those two roles independently, without
    // an exponential search across blocks or per-character native reflows.
    for (const titleRole of [true, false]) {
      const selected = rotatedTexts.map(text => text.classList.contains('ms-node-title') || text.classList.contains('ms-gallery-item-title'));
      if (!selected.includes(titleRole)) continue;
      const before = [...factors];
      for (const step of [.8, 1.2]) {
        const next = before.map((factor, index) => selected[index] === titleRole ? factor * step : factor);
        const candidate = layout(next);
        if (candidate.scale > measured.scale * 1.001) { measured = candidate; factors.splice(0, factors.length, ...next); }
      }
    }
    measured = layout(factors); card.dataset.rotationFit = String(best);
  }
  const { width: measuredWidth, heights, scale } = measured;
  if (!Number.isFinite(scale) || scale <= 0) { restoreSurfaces(card); return 1; }
  const horizontalSpace = Math.max(0, availableWidth - measuredWidth * scale);
  const left = (headerSlot ? headerSlot.x - bounds.x - pixel(frameStyle.borderLeftWidth) : pixel(frameStyle.paddingLeft)) +
    (node.style?.textAlign === 'left' ? 0 : node.style?.textAlign === 'right' ? horizontalSpace : horizontalSpace / 2);
  let top = headerSlot ? headerSlot.y - bounds.y - pixel(frameStyle.borderTopWidth) : pixel(frameStyle.paddingTop);
  if (balanced && !headerSlot) top += Math.max(0, availableHeight - heights.reduce((total, height) => total + height, 0) * scale) / 2;
  const transforms: string[] = [];
  surfaces.forEach((surface, index) => {
    // Keep subpixel placement in the transform matrix too. CSS layout positions
    // otherwise quantize before the camera magnifies microscopic frames.
    surface.style.left = '0'; surface.style.top = '0';
    const transform = `translate(${left}px, ${top}px) scale(${scale})`; transforms.push(transform);
    surface.style.transform = transform; top += heights[index]! * scale;
  });
  card.dataset.contentFit = String(scale);
  if (cachedText) rememberTextFit(cachedText.cache, cachedText.key, scale, surfaces, readingWidth, transforms);
  if (galleryViewport) fitGalleryViewport(card, project, node, bounds, context);
  return scale;
}
