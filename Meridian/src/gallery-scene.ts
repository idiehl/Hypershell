import type { MeridianAsset, MeridianNode, MeridianProject, PortalStyle } from './core/types.ts';
import { galleryItems, gallerySelection, galleryStep, isGallery } from './core/gallery.ts';
import { decodeTextAsset } from './core/navigation.ts';
import { resolveReferenceTarget } from './core/cells.ts';
import { FONT_FAMILIES, isNodeStyle } from './core/appearance.ts';
import { backgroundPaint, paintBackground } from './backgrounds.ts';
import { nodeSizingMode, galleryObjectLayout, nodeInnerContentWidth, sizingBounds } from './core/sizing.ts';
import { activeNodeContent, projectNodeContent } from './core/content.ts';
import { nodeTag } from './core/node-tag.ts';
import { canvasLauncher, canvasDestinationName, effectivePortalStyle, effectivePortalColor, disposePortalLaunchers } from './canvas-launcher.ts';
import { setTextRotation } from './text-rotation.ts';
import { renderRichText } from './rich-text-render.ts';
import { documentPreviewKind } from './core/document-preview.ts';
import { prepareBackgroundDisplay, markHoverPaint } from './background-display.ts';

export interface GalleryTileOptions {
  activeItems?: Readonly<Record<string, string>>;
  onPage?: (galleryId: string, itemId: string) => void;
  onInspect?: (itemId: string) => void;
  onAssetOpen?: (assetId: string) => void;
  onCanvasOpen?: (item: MeridianNode) => void;
  canvasPortalStyle?: PortalStyle;
  canvasPortalColor?: string;
  canvasNodeTitles?: Readonly<Record<string,string>>;
  onReveal?: (galleryId: string) => void;
  /** Canonical child content can be edited in place; references and decoded
   * attachment previews retain their original read-only source boundary. */
  prepareText?: (surface: HTMLElement, nodeId: string, title: boolean) => void;
  imageGesture: (image: HTMLImageElement, asset: MeridianAsset, title: string) => void;
  /** Standalone scene users also browse without modifying their document. */
  localItems: Map<string, string>;
  /** Outer standalone frame width, or the available page width for nesting. */
  frameWidth?: number;
}
const raster = /^image\/(png|jpeg|gif|webp|avif|bmp)$/i;
function element(tag: string, className: string, text?: string): HTMLElement {
  const result = document.createElement(tag); result.className = className;
  if (text !== undefined) result.textContent = text;
  return result;
}
function control(text: string, title: string, action: () => void): HTMLButtonElement {
  const result = document.createElement('button'); result.type = 'button'; result.textContent = text;
  result.title = title; result.setAttribute('aria-label', title);
  result.addEventListener('click', event => { event.stopPropagation(); action(); });
  return result;
}

/** One common child surface occupies the parent; paging never changes its frame. */
export function renderGalleryTile(host: HTMLElement, project: MeridianProject, parent: MeridianNode, options: GalleryTileOptions, depth = 0): void {
  const context = { galleryItems: options.activeItems ?? Object.fromEntries(options.localItems) };
  const frameWidth = options.frameWidth ?? sizingBounds(project, parent, { context }).width;
  const readingWidth = depth ? frameWidth : nodeInnerContentWidth(parent, frameWidth);
  const gallery = element('section', 'ms-gallery'); gallery.dataset.galleryId = parent.id;
  gallery.tabIndex = 0;
  gallery.setAttribute('aria-label', `${nodeTag(parent)} gallery`); gallery.setAttribute('aria-roledescription', 'gallery');
  host.append(gallery);
  const draw = () => {
    disposePortalLaunchers(gallery);
    gallery.replaceChildren();
    const activeId = options.activeItems === undefined ? options.localItems.get(parent.id) : options.activeItems[parent.id];
    const { items, item, index } = gallerySelection(project, parent, activeId);
    if (!item) {
      const empty = element('div', 'ms-gallery-empty ms-text');
      // Type conversion is lossless even before the author promotes existing media into children.
      const active = activeNodeContent(parent); if (active.assetId || active.text || active.hyperlink) renderObject(empty, parent, false);
      gallery.append(empty); return;
    }
    const page = element('div', 'ms-gallery-item'); page.dataset.galleryItemId = item.id;
    const style=isNodeStyle(item.style)?item.style:undefined;
    if(style?.fontFamily){const family=FONT_FAMILIES.find(font=>font.value===style.fontFamily)!.css;page.style.fontFamily=family;page.style.setProperty('--ms-font',family);}
    if(style?.fontSize!==undefined)page.style.setProperty('--ms-font-size',`${style.fontSize}px`);
    if(style?.textColor)page.style.setProperty('--ms-text-color',style.textColor);
    if(style?.accent)page.style.setProperty('--ms-accent',style.accent);
    if(style?.contentGap!==undefined)page.style.setProperty('--ms-content-gap',`${style.contentGap}px`);
    if(style?.textMediaGap!==undefined)page.style.setProperty('--ms-text-media-gap',`${style.textMediaGap}px`);
    page.style.setProperty('--ms-text-align',style?.textAlign??'center');
    page.style.textAlign=style?.textAlign??'center';
    const reference=item.kind==='reference'?resolveReferenceTarget(project,item):undefined;
    const ownBackground=!!style&&['backgroundPattern','backgroundAssetId'].some(key=>key in style);
    const sourceStyle=isNodeStyle(reference?.node?.style)?reference?.node?.style:undefined;
    const backdropStyle=ownBackground?style:sourceStyle??style;
    if(backdropStyle && style?.shape !== 'none' && style?.variant !== 'label') {
      const assets=!ownBackground&&reference?.saved&&reference.snapshot?.backgroundAsset?[reference.snapshot.backgroundAsset,...project.assets]:project.assets;
      const paint=backgroundPaint(backdropStyle, assets);
      if(paint.image!=='none'){const backdrop=element('div','ms-node-background');backdrop.setAttribute('aria-hidden','true');paintBackground(backdrop,paint);markHoverPaint(backdrop,paint.opacity);page.append(backdrop);}
    }
    prepareBackgroundDisplay(page,item,'page');
    page.setAttribute('role', 'group'); page.setAttribute('aria-label', `${index + 1} of ${items.length}: ${nodeTag(item)}`);
    if (item.kind !== 'block' && item.showTitle !== false && item.title) {
      const title = element('strong', 'ms-gallery-item-title'); renderRichText(title, item.title, item.titleRuns);
      options.prepareText?.(title, item.id, true);
      setTextRotation(title, style?.titleRotation ?? parent.style?.titleRotation);
      page.append(title);
    }
    renderObject(page, item, true); gallery.append(page);
    const navigation = element('div', 'ms-gallery-navigation');
    const change = (itemId: string | undefined, focus: 'previous' | 'next') => {
      if (!itemId || itemId === item.id) return;
      options.localItems.set(parent.id, itemId);
      if (options.onPage) options.onPage(parent.id, itemId); else draw();
      // A host may rebuild the scene; restore the same control in its replacement.
      const current = [...document.querySelectorAll<HTMLElement>('.ms-gallery')].find(node => node.dataset.galleryId === parent.id);
      const buttons = current?.querySelectorAll<HTMLButtonElement>('.ms-gallery-navigation button');
      const preferred = buttons?.[focus === 'previous' ? 0 : 1];
      (preferred?.disabled ? buttons?.[focus === 'previous' ? 1 : 0] : preferred)?.focus({ preventScroll: true });
    };
    const previous = control('←', `Previous object in ${nodeTag(parent)}`, () => change(galleryStep(project, parent, activeId, -1), 'previous'));
    const next = control('→', `Next object in ${nodeTag(parent)}`, () => change(galleryStep(project, parent, activeId, 1), 'next'));
    previous.disabled = index <= 0; next.disabled = index >= items.length - 1;
    previous.dataset.galleryControl = 'previous'; next.dataset.galleryControl = 'next';
    navigation.append(previous, next);
    gallery.append(navigation);
    gallery.onkeydown = event => {
      if ((event.target as HTMLElement).closest('video,audio,input,textarea,select,a,[contenteditable="true"]')) return;
      if (event.altKey || event.ctrlKey || event.metaKey) return;
      const target = event.key === 'ArrowLeft' ? galleryStep(project, parent, activeId, -1)
        : event.key === 'ArrowRight' ? galleryStep(project, parent, activeId, 1)
        : event.key === 'Home' ? items[0]?.id : event.key === 'End' ? items.at(-1)?.id : undefined;
      if (target) { event.preventDefault(); event.stopPropagation(); change(target, event.key === 'ArrowLeft' || event.key === 'Home' ? 'previous' : 'next'); }
    };
  };
  const renderObject = (host: HTMLElement, object: MeridianNode, nested: boolean) => {
    if (object.kind === 'block') return;
    const reference = object.kind === 'reference' ? resolveReferenceTarget(project, object) : undefined;
    const original = object.kind === 'reference' && object.contentItems !== undefined ? object : reference?.node ?? object;
    const active = activeNodeContent(original), display = { ...projectNodeContent(original),
      text: active.text, textRuns: active.textRuns, assetId: active.assetId, hyperlinks: active.hyperlink ? [active.hyperlink] : undefined };
    const rotation = object.style?.textRotation ?? parent.style?.textRotation;
    const textBlock = (tag: string, className: string, text: string) => {
      const block = element(tag, className); renderRichText(block, text, text === active.text ? active.textRuns : undefined);
      if (object.kind !== 'reference' && active.type === 'text' && text === active.text) options.prepareText?.(block, object.id, false);
      setTextRotation(block, rotation); return block;
    };
    const savedAssets = reference?.saved && object.contentItems === undefined && reference.snapshot ? [reference.snapshot.asset, ...reference.snapshot.assets ?? []] : [];
    const asset = !active.assetId ? undefined : savedAssets.find(asset => asset?.id === active.assetId) ?? project.assets.find(item => item.id === active.assetId);
    if (isGallery(display) && nested && !reference?.saved) {
      if (!display.collapsed && depth < 16) { renderGalleryTile(host, project, display, { ...options, prepareText: reference ? undefined : options.prepareText, frameWidth: readingWidth }, depth + 1); return; }
      host.append(textBlock('p', 'ms-text ms-gallery-text', display.text || `${galleryItems(project,display).length} objects · ${display.collapsed?'Collapsed gallery':'Open this gallery to explore further.'}`));
      if(display.collapsed&&options.onReveal)host.append(control('Reveal gallery',`Reveal gallery: ${nodeTag(display)}`,()=>options.onReveal?.(display.id)));
      return;
    }
    if (asset && raster.test(asset.mime)) {
      const image = document.createElement('img'); image.src = `data:${asset.mime};base64,${asset.base64}`; image.alt = asset.name;
      image.draggable = false; image.className = 'ms-image ms-gallery-image'; options.imageGesture(image, asset, nodeTag(object)); host.append(image);
      if(nodeSizingMode(parent)==='auto'){const media=galleryObjectLayout(project,parent,object,context,readingWidth).media;if(media)Object.assign(image.style,{width:`${media.width}px`,height:`${media.height}px`,alignSelf:'center'});}
    } else if (asset && /^video\/(mp4|webm|ogg)$/i.test(asset.mime)) {
      const video = document.createElement('video'); video.controls = true; video.preload = 'metadata'; video.className = 'ms-video ms-gallery-video';
      video.src = `data:${asset.mime};base64,${asset.base64}`; video.setAttribute('aria-label', asset.name); host.append(video);
      if(nodeSizingMode(parent)==='auto'){const media=galleryObjectLayout(project,parent,object,context,readingWidth).media;if(media)Object.assign(video.style,{width:`${media.width}px`,height:`${media.height}px`,alignSelf:'center'});}
    } else if (asset && /^audio\/(mpeg|mp3|ogg|wav|wave|x-wav|webm|aac|mp4)$/i.test(asset.mime)) {
      const audio = document.createElement('audio'); audio.controls = true; audio.preload = 'metadata'; audio.className = 'ms-audio ms-gallery-audio';
      audio.src = `data:${asset.mime};base64,${asset.base64}`; audio.setAttribute('aria-label', asset.name); audio.style.width = '100%'; host.append(audio);
    } else if (asset) {
      if (documentPreviewKind(asset)) host.append(element('div', 'ms-file-placeholder', `${documentPreviewKind(asset) === 'csv' ? 'Table' : 'Document'} · ${asset.name}`));
      else if (/^text\/(plain|markdown|csv|tab-separated-values)$/i.test(asset.mime)) host.append(textBlock('pre', 'ms-text-preview ms-gallery-document', decodeTextAsset(asset).slice(0, 3000)));
      else host.append(element('div', 'ms-file-placeholder', `${asset.mime === 'application/pdf' ? 'PDF' : 'Document'} · ${asset.name}`));
      if (options.onAssetOpen) host.append(control(documentPreviewKind(asset) ? 'Read document' : 'Open document ↗', `Open ${asset.name}`, () => options.onAssetOpen?.(asset.id)));
    }
    if (!asset && active.assetId) host.append(element('div', 'ms-file-placeholder', 'Attachment unavailable.'));
    if (display.text) host.append(textBlock('div', asset ? 'ms-text ms-caption ms-gallery-text ms-after-media' : 'ms-text ms-gallery-text', display.text));
    if (display.kind === 'canvas-link' && options.onCanvasOpen) {
      const destinationName=canvasDestinationName(project,object,display,options.canvasNodeTitles);
      host.append(canvasLauncher(`Open ${destinationName}`, () => options.onCanvasOpen?.(object),
        {destinationName,style:effectivePortalStyle(object,display,options.canvasPortalStyle??project.canvases.find(canvas=>canvas.id===parent.canvasId)?.style?.portalStyle),color:effectivePortalColor(object,display,options.canvasPortalColor??project.canvases.find(canvas=>canvas.id===parent.canvasId)?.style?.portalColor)}));
    }
    if (object.kind === 'reference' && !reference?.node) host.append(element('p', 'ms-reference-status', 'Referenced content is unavailable.'));
    if (display.hyperlinks?.length) {
      const links = element('div', 'ms-gallery-links');
      for (const link of display.hyperlinks) {
        const anchor = document.createElement('a'); anchor.textContent = link.label || link.url; anchor.href = link.url;
        setTextRotation(anchor, rotation);
        anchor.target = '_blank'; anchor.rel = 'noopener noreferrer'; links.append(anchor);
      }
      host.append(links);
    }
  };
  draw();
}
