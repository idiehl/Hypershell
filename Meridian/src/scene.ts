import type { Evaluation, LogicValue, MeridianAsset, MeridianLink, MeridianNode, MeridianProject, Point } from './core/types.ts';
import { GRID_SIZE, resolveSnapping, snapPosition, alignmentGuides, type SnapGuide } from './core/snapping.ts';
import { decodeTextAsset, isCompactContainer, navigationIndex, linkDirection } from './core/navigation.ts';
import { FONT_FAMILIES, isCanvasStyle, isLinkLabel, isLinkStyle, isNodeStyle } from './core/appearance.ts';
import { inputPorts, isComputedLink, operationDefinition, outputValueType } from './core/operations.ts';
import { axisLockedPoint, orthogonalFinish, linkPivotObstacles, draftPivotObstacles, constrainDraftRoute } from './core/link-axis.ts';
import { cellAddress, columnLabel, gridGeometry, parseCsv, resolveReferenceTarget, isOrderedContainer, isEnclosingContainer, isCellChild } from './core/grid.ts';
import { openMediaWindow } from './media-window.ts';
import { nearestBoundary, nodeShape, shapeBounds, shapeAnchor as backdropAnchor, shapePath, shapesOverlap } from './core/geometry.ts';
import { collisionFreeResize, collisionFreeTranslation, collisionNodeShapes, isIntentionalContainment } from './core/collision.ts';
import { isGallery, isEmbeddedGallery, galleryOwner, gallerySelection, galleryStep } from './core/gallery.ts';
import { renderGalleryTile } from './gallery-scene.ts';
import { canvasLauncher, canvasDestinationName, effectivePortalStyle, effectivePortalColor, disposePortalLaunchers } from './canvas-launcher.ts';
import { clampCameraScale, scaleCameraAt, formatCameraZoom } from './core/camera.ts';
import { backgroundPaint, paintBackground, moveCanvasBackground } from './backgrounds.ts';
import { nodeSizingMode, nodeContentLayout, nodeContentBounds, nodeBorderWidth, effectiveNodeBorderWidth, constrainChildGeometry, externalGalleryNavigationBounds } from './core/sizing.ts';
import { createContainedContentPass, fitContainedContent } from './contained-content.ts';
import { setTextRotation } from './text-rotation.ts';
import { renderRichText } from './rich-text-render.ts';
import { RelativeImageResolution } from './relative-image-resolution.ts';
import { canNestNodeBranches, nestingTargetOrder } from './core/nesting.ts';
import { activeNodeContent, hidesOwnContent, projectNodeContent } from './core/content.ts';
import { nodeTag } from './core/node-tag.ts';
import { documentPreviewKind } from './core/document-preview.ts';
import { documentNodeAsset, documentNodeExpanded } from './core/document-node.ts';
import { createDocumentViewer, documentActionButton } from './document-viewer.ts';
import { disposeDocumentReaders, inlineDocumentReader } from './document-node-scene.ts';
import { nodeKindLabel, workLibraryDisplayText } from './core/work-terminology.ts';
import { DEFAULT_CHILD_INSET } from './core/spacing.ts';
import { snapEligibility, isSnapLink, lockedClusterNodeIds, snapContact } from './core/clusters.ts';
import { mountWindowSurfaces, prepareWindowOffsets, windowOffset, windowParents, windowSurface, displayNodeBounds, type WindowOffsets } from './window-scene.ts';
import { mountExpansionTrays, type TrayFrame } from './expansion-tray-scene.ts';
import { previewTrayGeometry } from './tray-geometry-preview.ts';
import { visibleExpansionTrays } from './core/expansion-tray.ts';
import { prepareBackgroundDisplay, markHoverPaint, mountBackgroundDisplay, disposeBackgroundDisplay } from './background-display.ts';
import { clusterEmblem } from './cluster-button.ts';
import { renderSmartLinkRails } from './smart-link-scene.ts';
import { disposeNodeReadability, mountNodeReadability } from './hover-readability.ts';
import { nodeTypography } from './core/sizing.ts';

export interface SceneView { x: number; y: number; scale: number }
export type SceneAuthoringMode = 'select' | 'hand' | 'text' | 'node' | 'link' | 'smart-link' | 'delete' | 'module';
export interface SceneAuthorTarget { nodeId?: string; linkId?: string; segmentIndex?: number; point: Point; clientX: number; clientY: number; shiftKey?: boolean }
export interface SceneOptions {
  selection?: string | null;
  /** Node selection. When supplied this takes precedence over the legacy single selection. */
  selections?: readonly string[];
  selectionMode?: 'pan' | 'region';
  regionIntersection?: 'contained' | 'intersecting';
  evaluation?: Evaluation;
  running?: boolean;
  readonly?: boolean;
  connecting?: boolean;
  fit?: boolean;
  focusNodeId?: string | null;
  focusBranch?: boolean;
  view?: SceneView;
  onSelection?: (id: string | null, anchor?: Point) => void;
  onSelections?: (ids: string[], anchor?: Point) => void;
  /** The host opens an editor on this native text surface without replacing the
   * scene. Pointer defaults remain available for caret and passage selection. */
  onTextEdit?: (nodeId: string, title: boolean, surface: HTMLElement, event: PointerEvent) => void;
  /** A host may restrict editing while preserving ordinary canvas gestures. */
  canEditText?: (nodeId: string, title: boolean) => boolean;
  /** Position locks are evaluated for normalized branch/cluster roots before
   * drawing a movement preview, so locked nodes never appear to drift. */
  canMoveNodes?: (ids: readonly string[]) => boolean;
  /** Module authoring toggles a complete branch without moving or disclosing it. */
  onModuleSelect?: (id: string) => void;
  /** A shared physical edge becomes a rigid cluster when its snap link is locked. */
  onSnapLock?: (sourceId: string, targetId: string, locked: boolean, linkId?: string) => void;
  onMove?: (id: string, x: number, y: number) => void;
  /** Moves the union of the selected branches in one undoable transaction. */
  onMoveSelection?: (ids: string[], dx: number, dy: number) => void;
  /** Reposition a disclosed exterior workspace without moving its source tile. */
  onTrayMove?: (sourceId: string, dx: number, dy: number) => void;
  /** Resize a tray's outer frame and fit its real descendants in one edit. */
  onTrayResize?: (sourceId: string, frame: TrayFrame) => void;
  /** Reparent complete dragged branches as one edit after a valid drop. */
  onNestSelection?: (ids: string[], parentId: string, dx: number, dy: number) => void;
  onResize?: (id: string, width: number, height: number, x?: number, y?: number) => void;
  onToggle?: (id: string, field: 'collapsed' | 'closed', value: boolean) => void;
  onValue?: (id: string, value: boolean | number) => void;
  onCanvasOpen?: (canvasId: string) => void;
  /** Canvas launchers may be bound to a saved canvas in another local collection. */
  onCanvasNodeOpen?: (nodeId: string) => void;
  canvasNodeTitles?: Readonly<Record<string, string>>;
  galleryItems?: Readonly<Record<string, string>>;
  onGalleryPage?: (galleryId: string, itemId: string) => void;
  onGalleryInspect?: (itemId: string) => void;
  windowOffsets?: Readonly<Record<string,Point>>;
  onWindowScroll?: (windowId:string,offset:Point)=>void;
  /** Optional host-owned destination, separate from expanding this node's children. */
  onNodeOpen?: (nodeId: string) => void;
  nodeOpenLabel?: (node: MeridianNode) => string | undefined;
  onAssetOpen?: (assetId: string) => void;
  onCellOpen?: (parentId: string, address: string) => void;
  onLinkSelect?: (id: string, position?: Point, segmentIndex?: number) => void;
  onRouteEdit?: (linkId: string, points: Point[]) => void;
  onViewChange?: (view: SceneView) => void;
  /** A persistent author tool. Simple clicks act; drags and long presses pan. */
  authoringMode?: SceneAuthoringMode;
  /** An unfinished route is view state, never a partially saved graph. */
  linkDraft?: { sourceId: string; points: readonly Point[]; directions?: readonly ('x'|'y'|undefined)[] };
  onAuthorClick?: (target: SceneAuthorTarget) => void;
  onContextMenu?: (target: SceneAuthorTarget) => void;
}

const SVG_NS = 'http://www.w3.org/2000/svg';
const cameras = new WeakMap<HTMLElement, SceneView>();
const activeDrags = new WeakMap<HTMLElement, () => void>();
/** A save acknowledgement must not replace DOM that owns pointer capture. */
export function sceneGestureActive(host: HTMLElement): boolean { return activeDrags.has(host); }
const draftModifierCleanup = new Map<HTMLElement, () => void>();
const imageResolution = new Map<HTMLElement, RelativeImageResolution>();
const galleryItems = new WeakMap<HTMLElement, Map<string, string>>();
const windowPositions=new WeakMap<HTMLElement,WindowOffsets>();
interface PrimaryMediaEntry { assetId: string; mime: string; base64: string; element: HTMLMediaElement; used: boolean; playing: boolean }
const primaryMedia = new WeakMap<HTMLElement, Map<string, PrimaryMediaEntry>>();
const preparedMediaRemount = new WeakSet<HTMLElement>();
/** Call before a host's surrounding editor shell is replaced, then reuse that
 * scene host. The native DOM move must not erase its previous play intent. */
export function prepareSceneMediaForRemount(host: HTMLElement): void {
  for (const entry of primaryMedia.get(host)?.values() ?? []) entry.playing = !entry.element.paused && !entry.element.ended;
  preparedMediaRemount.add(host);
}
function releasePrimaryMedia(entry: PrimaryMediaEntry): void {
  entry.element.pause(); entry.element.removeAttribute('src'); entry.element.load();
}
/** Unchanged primary media retains its real native player through unrelated
 * scene edits. Source changes and disappearing nodes end only their old player. */
function primaryMediaElement(host: HTMLElement, key: string, asset: MeridianAsset, kind: 'video' | 'audio'): HTMLMediaElement {
  const cache = primaryMedia.get(host)!; let entry = cache.get(key);
  if (entry && (entry.assetId !== asset.id || entry.mime !== asset.mime || entry.base64 !== asset.base64 || entry.element.tagName.toLowerCase() !== kind)) {
    releasePrimaryMedia(entry); cache.delete(key); entry = undefined;
  }
  if (!entry) {
    const element = document.createElement(kind); element.controls = true; element.preload = 'metadata';
    element.src = `data:${asset.mime};base64,${asset.base64}`; element.setAttribute('aria-label', asset.name);
    entry = { assetId: asset.id, mime: asset.mime, base64: asset.base64, element, used: true, playing: false }; cache.set(key, entry);
  }
  entry.used = true; entry.element.setAttribute('aria-label', asset.name); return entry.element;
}
let sceneSequence = 0;
// Selection rebuilds scene DOM. Track releases by stable node identity so a second
// click still discloses the same node after the first click populates its inspector.
let lastNodeClick: {id:string;time:number;x:number;y:number} | undefined;
// Only consecutive clicks on the same tile form a disclosure gesture. Sidebar
// controls and buttons interrupt it even though selection recreates the tile DOM.
if (typeof document !== 'undefined') document.addEventListener('pointerdown', event => {
  const target = event.target instanceof Element ? event.target : null;
  if (target?.closest<HTMLElement>('.ms-node')?.dataset.nodeId !== lastNodeClick?.id ||
      target?.closest('button,input,textarea,select,a,video,[contenteditable="true"]')) lastNodeClick = undefined;
}, true);
const clamp = (value: number, low: number, high: number): number => Math.max(low, Math.min(high, value));
const finite = (value: number, fallback = 0): number => Number.isFinite(value) ? value : fallback;
const raster = /^(image\/png|image\/jpeg|image\/gif|image\/webp|image\/avif|image\/bmp)$/i;
const label = (tag: string, className: string, text?: string): HTMLElement => {
  const element = document.createElement(tag);
  element.className = className;
  if (text !== undefined) element.textContent = text;
  return element;
};
const svg = <T extends keyof SVGElementTagNameMap>(tag: T, attributes: Record<string, string> = {}): SVGElementTagNameMap[T] => {
  const element = document.createElementNS(SVG_NS, tag);
  for (const [name, value] of Object.entries(attributes)) element.setAttribute(name, value);
  return element;
};
function button(text: string, title: string, action: (event: MouseEvent) => void): HTMLButtonElement {
  const element = document.createElement('button');
  element.type = 'button'; element.textContent = text; element.title = title; element.setAttribute('aria-label', title);
  element.addEventListener('click', event => { event.stopPropagation(); action(event); });
  return element;
}
/** Intersect a center ray with the shared visual backdrop geometry. */
function shapeAnchor(node: MeridianNode, dx: number, dy: number): Point {
  return backdropAnchor(nodeShape(node), dx, dy);
}
function boundary(node: MeridianNode, point: Point): Point {
  const x = clamp(point.x, 0, 1), y = clamp(point.y, 0, 1);
  const sides = [{ distance: x, x: 0, y }, { distance: 1 - x, x: 1, y }, { distance: y, x, y: 0 }, { distance: 1 - y, x, y: 1 }];
  sides.sort((a, b) => a.distance - b.distance);
  return shapeAnchor(node, (sides[0].x - .5) * node.width, (sides[0].y - .5) * node.height);
}
function aimedAnchor(node: MeridianNode, toward: MeridianNode): Point {
  const dx = toward.x + toward.width / 2 - node.x - node.width / 2;
  const dy = toward.y + toward.height / 2 - node.y - node.height / 2;
  return shapeAnchor(node, dx, dy);
}
function nearestAnchor(node: MeridianNode, event: PointerEvent | MouseEvent, element: HTMLElement): Point {
  const rect = element.getBoundingClientRect();
  // Serialized anchors retain their normalized edge vocabulary. Apply the
  // backdrop intersection once when rendering, rather than twice on new links.
  return boundary({ ...node, x: 0, y: 0, width: 1, height: 1, style: { shape: 'rectangle' } }, { x: (event.clientX - rect.left) / Math.max(1, rect.width), y: (event.clientY - rect.top) / Math.max(1, rect.height) });
}
function valueText(value: LogicValue | undefined): string {
  return typeof value === 'number' && Number.isFinite(value) ? Number(value.toPrecision(12)).toString() : value === true ? 'On' : value === false ? 'Off' : 'Unknown';
}
function openNodeText(node: MeridianNode): void {
  const dialog = document.createElement('dialog'); dialog.className = 'ms-asset-dialog ms-description-dialog'; dialog.setAttribute('aria-label', `${nodeTag(node)} description`);
  const title = label('strong', ''); renderRichText(title, node.title, node.titleRuns);
  const header = label('header', 'ms-dialog-header'); header.append(title, button('×', 'Close description', () => dialog.close()));
  const text = label('div', 'ms-dialog-text'); renderRichText(text, node.text ?? '', node.textRuns);
  dialog.append(header, text); dialog.addEventListener('close', () => dialog.remove(), { once: true });
  document.body.append(dialog); dialog.showModal();
}
function textAsset(asset: MeridianAsset): string {
  try { return decodeTextAsset(asset).slice(0, 3000); }
  catch { return 'This text attachment could not be decoded.'; }
}
function csvPreview(asset:MeridianAsset):HTMLElement {
 const rows=parseCsv(decodeTextAsset(asset),/\.tsv$/i.test(asset.name)?'\t':','),wrap=label('div','ms-csv-preview'),table=document.createElement('table');table.setAttribute('aria-label',asset.name);
 for(const row of rows.slice(0,24)){const tr=document.createElement('tr');for(const value of row.slice(0,12)){const td=document.createElement('td');td.textContent=value;tr.append(td);}table.append(tr);}wrap.append(table);if(rows.length>24)wrap.append(label('small','',`${rows.length} rows in the original attachment`));return wrap;
}

/** Ctrl-click is owned by the image, before the tile can select, move or disclose. */
function imageWindowGesture(image: HTMLImageElement, host: HTMLElement, asset: MeridianAsset, title: string): void {
  imageResolution.get(host)?.register(image);
  image.title = 'Ctrl-click to open image in a new window'; image.tabIndex = 0; image.setAttribute('aria-keyshortcuts', 'Control+Enter');
  const open = () => {
    lastNodeClick = undefined;
    try { openMediaWindow(asset, title); }
    catch (error) {
      const body = image.parentElement; body?.querySelector('.ms-media-error')?.remove();
      const message = label('div', 'ms-node-error ms-media-error', error instanceof Error ? error.message : 'The image window could not open.'); message.setAttribute('role', 'alert'); body?.append(message);
    }
  };
  image.addEventListener('pointerdown', event => {
    if (event.button !== 0 || !event.ctrlKey) return;
    event.preventDefault(); event.stopPropagation(); lastNodeClick = undefined; activeDrags.get(host)?.();
    image.setPointerCapture(event.pointerId); let moved = false;
    const move = (next: PointerEvent) => { if (next.pointerId === event.pointerId) { next.stopPropagation(); moved ||= Math.hypot(next.clientX - event.clientX, next.clientY - event.clientY) > 3; } };
    const cleanup = () => {
      image.removeEventListener('pointermove', move); image.removeEventListener('pointerup', release); image.removeEventListener('pointercancel', cancel); image.removeEventListener('lostpointercapture', cancel);
      window.removeEventListener('blur', cancel); window.removeEventListener('keydown', escape, true);
      if (activeDrags.get(host) === cancel) activeDrags.delete(host);
      if (image.hasPointerCapture(event.pointerId)) image.releasePointerCapture(event.pointerId);
    };
    const release = (next: PointerEvent) => { if (next.pointerId !== event.pointerId) return; next.preventDefault(); next.stopPropagation(); cleanup(); if (!moved && next.ctrlKey && next.button === 0) open(); };
    const cancel = () => { cleanup(); };
    const escape = (next: KeyboardEvent) => { if (next.key === 'Escape') { next.preventDefault(); next.stopPropagation(); cancel(); } };
    activeDrags.set(host, cancel); image.addEventListener('pointermove', move); image.addEventListener('pointerup', release); image.addEventListener('pointercancel', cancel); image.addEventListener('lostpointercapture', cancel);
    window.addEventListener('blur', cancel); window.addEventListener('keydown', escape, true);
  });
  image.addEventListener('click', event => { if (event.ctrlKey) { event.preventDefault(); event.stopPropagation(); } });
  image.addEventListener('keydown', event => { if (event.ctrlKey && event.key === 'Enter') { event.preventDefault(); event.stopPropagation(); activeDrags.get(host)?.(); open(); } });
}

/** Shared DOM/SVG renderer. Author data is always assigned as text, never HTML. */
export function renderScene(host: HTMLElement, project: MeridianProject, canvasId: string, options: SceneOptions = {}): void {
  disposeNodeReadability(host);
  let media = primaryMedia.get(host); if (!media) { media = new Map(); primaryMedia.set(host, media); }
  const prepared = preparedMediaRemount.delete(host);
  for (const entry of media.values()) { entry.used = false; if (!prepared) entry.playing = !entry.element.paused && !entry.element.ended; }
  disposeDocumentReaders(host);
  disposeBackgroundDisplay(host);
  disposePortalLaunchers(host);
  activeDrags.get(host)?.();
  for (const [previous, resolution] of imageResolution) if (!previous.isConnected) { resolution.dispose(); imageResolution.delete(previous); }
  imageResolution.get(host)?.dispose(); imageResolution.delete(host);
  for(const [previous,cleanup] of draftModifierCleanup)if(!previous.isConnected)cleanup();
  draftModifierCleanup.get(host)?.();
  const previousView = cameras.get(host);
  const view: SceneView = { ...(options.view ?? previousView ?? { x: 44, y: 44, scale: 1 }) };
  view.x = finite(view.x); view.y = finite(view.y); view.scale = clampCameraScale(finite(view.scale, 1));
  cameras.set(host, view);
  host.replaceChildren(); host.classList.add('meridian-scene');
  const viewport = label('div', `ms-viewport${options.connecting ? ' ms-connecting' : ''}${options.selectionMode === 'region' ? ' ms-region-mode' : ''}`);
  const authoringMode = options.readonly ? 'select' : options.authoringMode ?? 'select';
  const moduleMode = authoringMode === 'module', textMode = authoringMode === 'text';
  const navigating = authoringMode === 'select' || authoringMode === 'hand' || moduleMode;
  viewport.dataset.authoringMode = authoringMode; viewport.dataset.readonly = String(!!options.readonly);
  viewport.tabIndex = 0; viewport.setAttribute('aria-label', options.selectionMode === 'region'
    ? 'Work. Drag the background to select nodes. Shift adds to the selection. Use the mouse wheel to zoom.'
    : 'Work. Drag the background to pan. Shift-drag selects nodes. Use the mouse wheel to zoom.');
  if (!navigating && !textMode) viewport.setAttribute('aria-label', `Work. ${authoringMode === 'node' ? 'Click to create a node' : authoringMode === 'link' ? 'Click a node to create a link' : 'Click an object to delete it'}. Drag or hold to pan. Press Enter to use the active tool, or Shift F10 for Work actions.`);
  if (textMode) viewport.setAttribute('aria-label', 'Work. Click a node title or passage to edit text. Press Escape for Single Select.');
  if (moduleMode) viewport.setAttribute('aria-label', 'Work. Click a node to include or exclude its branch from the module. Drag the background to pan.');
  const world = label('div', 'ms-world');
  let readability:ReturnType<typeof mountNodeReadability>|undefined;
  const smartLayer=svg('svg',{class:'ms-links ms-smart-links',width:'1',height:'1','aria-label':'Smart Links'});
  const linksLayer = svg('svg', { class: 'ms-links', width: '1', height: '1', 'aria-label': 'Work connections' });
  const endpointsLayer = svg('svg', { class: 'ms-link-endpoints', width: '1', height: '1', 'aria-label': 'Connection endpoints' });
  const guidesLayer = svg('svg', { class: 'ms-snap-guides', width: '1', height: '1', 'aria-hidden': 'true' });
  const defs = svg('defs'); const markerPrefix = `meridian-arrow-${++sceneSequence}`;
  for (const [name, color] of [['quiet', '#7793ad'], ['number', '#b69af7'], ['on', '#57dbc4'], ['off', '#658093'], ['unknown', '#e6b969'], ['selected', '#f7e3a0']]) {
    const marker = svg('marker', { id: `${markerPrefix}-${name}`, viewBox: '0 0 10 10', refX: '9', refY: '5', markerWidth: '14', markerHeight: '14', orient: 'auto-start-reverse', markerUnits: 'userSpaceOnUse' });
    marker.append(svg('path', { d: 'M 0 0 L 10 5 L 0 10 z', fill: color })); defs.append(marker);
  }
  linksLayer.append(defs); world.append(linksLayer,smartLayer, guidesLayer, endpointsLayer); viewport.append(world); host.append(viewport);
  let moduleOutlineResize: ResizeObserver | undefined;
  const resolution = new RelativeImageResolution(viewport, world, () => {
    moduleOutlineResize?.disconnect(); moduleOutlineResize = undefined;
    if (imageResolution.get(host) === resolution) imageResolution.delete(host);
  });
  imageResolution.set(host, resolution);
  type ShaftHit = { point: Point; visiblePoint: Point; segmentIndex: number };
  const linkHitTargets = new Map<string, (point: Point) => ShaftHit>();
  const visibleShafts = new Map<string, { hit: (point: Point) => ShaftHit; visibleAt: (point: Point) => boolean; width: number }>();
  const windowShaftAt = (target: Element | null, point: Point): { id: string; hit: ShaftHit } | undefined => {
    const pane = target?.closest<HTMLElement>('.ms-window-viewport'), ownerId = pane?.dataset.windowId;
    const directNode = target?.closest<HTMLElement>('.ms-node')?.dataset.nodeId;
    if (!ownerId || directNode && directNode !== ownerId || target?.closest('button,input,textarea,select,a,video,audio')) return;
    // A native scroll pane has a transparent surface above world-space SVG
    // shafts. Resolve only a real, revealed, clipped line at this point, while
    // preserving ordinary empty-cell and child-node interactions around it.
    let result: { id: string; hit: ShaftHit } | undefined, closest = Infinity;
    for (const [id, shaft] of visibleShafts) {
      if (!shaft.visibleAt(point)) continue;
      const hit = shaft.hit(point), distance = Math.hypot(point.x - hit.visiblePoint.x, point.y - hit.visiblePoint.y);
      if (!shaft.visibleAt(hit.visiblePoint)) continue;
      if (distance <= Math.max(shaft.width / 2 + 4, 8 / view.scale) && distance < closest) { closest = distance; result = { id, hit }; }
    }
    return result;
  };
  const authorTarget = (event: MouseEvent | PointerEvent): SceneAuthorTarget => {
    const target = event.target instanceof Element ? event.target : null, rect = viewport.getBoundingClientRect();
    const point = { x: (event.clientX - rect.left - view.x) / view.scale, y: (event.clientY - rect.top - view.y) / view.scale };
    const directLink = target?.closest<SVGElement>('[data-link-id]')?.dataset.linkId, fallback = directLink ? undefined : windowShaftAt(target, point);
    const linkId = directLink ?? fallback?.id, linkHit = fallback?.hit ?? (linkId && linkHitTargets.get(linkId)?.(point));
    const card=target?.closest<HTMLElement>('[data-node-id]'),node=card&&project.nodes.find(node=>node.id===card.dataset.nodeId);
    const textOwner = target?.closest<HTMLElement>('[data-editable-node-id]')?.dataset.editableNodeId;
    // Empty child-workspace space is canvas space while tracing a path. Keep
    // the parent title available as its endpoint, and preserve ordinary blank
    // cell selection and child creation in every other tool.
    const pane=target?.closest<HTMLElement>('.ms-window-viewport');
    const blankWindow=!!pane&&(!card||card.dataset.nodeId===pane.dataset.windowId);
    const cardHeader=card?.querySelector(':scope > .ms-node-header');
    const blankInterior=authoringMode==='link'&&!!options.linkDraft&&(blankWindow||node&&!node.collapsed&&isEnclosingContainer(node)&&!isGallery(node)
      &&!target?.closest('.ms-node-header,.ms-node-body')&&(!cardHeader||event.clientY>cardHeader.getBoundingClientRect().bottom));
    return {
      nodeId: fallback||blankInterior ? undefined : textOwner ?? card?.dataset.nodeId ?? target?.closest<HTMLElement>('[data-tray-source-id]')?.dataset.traySourceId ?? target?.closest<HTMLElement>('[data-external-window]')?.dataset.externalWindow,
      linkId, ...(linkHit ? { point: linkHit.point, segmentIndex: linkHit.segmentIndex } : { point }),
      clientX: event.clientX, clientY: event.clientY, shiftKey: event.shiftKey,
    };
  };
  const targetAt = (point: Point, identity: { nodeId?: string; linkId?: string; segmentIndex?: number } = {}): SceneAuthorTarget => {
    const rect = viewport.getBoundingClientRect();
    return { ...identity, point, clientX: rect.left + view.x + point.x * view.scale, clientY: rect.top + view.y + point.y * view.scale };
  };
  const menuKey = (event: KeyboardEvent) => event.key === 'ContextMenu' || event.shiftKey && event.key === 'F10';
  const editableTextTarget = (target: Element | null): HTMLElement | undefined => {
    if (!options.onTextEdit || options.readonly || options.connecting || !textMode) return;
    const surface = target?.closest<HTMLElement>('[data-editable-node-id][data-editable-text]');
    const id = surface?.dataset.editableNodeId;
    if (!id || options.canEditText?.(id, surface.dataset.editableText === 'title') === false) return;
    return surface;
  };
  const prepareTextSurface = (surface: HTMLElement, id: string, title: boolean) => {
    if (!options.onTextEdit || options.readonly || !textMode) return;
    surface.dataset.editableNodeId = id; surface.dataset.editableText = title ? 'title' : 'body';
  };
  const nativeInteraction = (event: MouseEvent | PointerEvent): boolean => {
    const target = event.target instanceof Element ? event.target : null;
    if (target?.closest('.mdv-viewer,button,input,textarea,select,a,video,audio,[contenteditable="true"]')) return true;
    if (!event.ctrlKey && !event.metaKey && !event.shiftKey && editableTextTarget(target)) return true;
    // Ctrl-click belongs to the raster image's media-window gesture in every mode.
    if (event.ctrlKey && target?.closest('.ms-image')) return true;
    const scroll = target?.closest<HTMLElement>('.ms-text,.ms-text-preview,.ms-caption,.ms-csv-preview');
    if (!scroll) return false;
    const rect = scroll.getBoundingClientRect(), scale = Math.max(1e-9, rect.width / Math.max(1, scroll.offsetWidth));
    const gutterX = (scroll.offsetWidth - scroll.clientWidth) * scale, gutterY = (scroll.offsetHeight - scroll.clientHeight) * scale;
    return gutterX > 0 && event.clientX >= rect.right - gutterX || gutterY > 0 && event.clientY >= rect.bottom - gutterY;
  };
  viewport.addEventListener('contextmenu', event => {
    if (!options.onContextMenu || options.readonly) return;
    event.preventDefault(); event.stopPropagation(); lastNodeClick = undefined;
    activeDrags.get(host)?.(); options.onContextMenu(authorTarget(event));
  }, true);
  viewport.addEventListener('pointerdown', event => {
    if (event.button !== 0) return;
    if (textMode) {
      const target = event.target instanceof Element ? event.target : null;
      // Text owns its native caret/selection gesture. Other objects cannot
      // create, delete, move, navigate or change values while this tool is active.
      if (editableTextTarget(target) || target?.closest('[contenteditable="true"]')) return;
      if (target?.closest('.ms-node') && !target.closest('button,input,textarea,select,a,video,audio')) return;
      event.preventDefault(); event.stopPropagation(); lastNodeClick = undefined; return;
    }
    const moduleTarget = moduleMode ? authorTarget(event) : undefined;
    if (moduleMode ? !moduleTarget?.nodeId || event.ctrlKey && (event.target as Element).closest('.ms-image') : navigating || nativeInteraction(event)) return;
    activeDrags.get(host)?.(); lastNodeClick = undefined;
    event.preventDefault(); event.stopPropagation(); viewport.focus({ preventScroll: true });
    // Focusing can commit a pending inspector field and synchronously replace
    // the scene. Never capture a pointer or create against that detached view.
    if (!viewport.isConnected) return;
    const target = authorTarget(event), started = performance.now();
    const startX = event.clientX, startY = event.clientY, originalX = view.x, originalY = view.y;
    let dragged = false;
    viewport.setPointerCapture(event.pointerId);
    const move = (next: PointerEvent) => {
      if (next.pointerId !== event.pointerId) return;
      next.stopPropagation(); const dx = next.clientX - startX, dy = next.clientY - startY;
      dragged ||= Math.hypot(dx, dy) >= 5;
      if (!dragged) return;
      viewport.classList.add('ms-panning'); view.x = originalX + dx; view.y = originalY + dy; updateView();
    };
    const cleanup = () => {
      viewport.removeEventListener('pointermove', move); viewport.removeEventListener('pointerup', end);
      viewport.removeEventListener('pointercancel', cancel); viewport.removeEventListener('lostpointercapture', cancel);
      window.removeEventListener('blur', cancel); window.removeEventListener('keydown', escape, true);
      viewport.classList.remove('ms-panning');
      if (activeDrags.get(host) === cancel) activeDrags.delete(host);
      if (viewport.hasPointerCapture(event.pointerId)) viewport.releasePointerCapture(event.pointerId);
    };
    const end = (next: PointerEvent) => {
      if (next.pointerId !== event.pointerId) return;
      next.preventDefault(); next.stopPropagation(); cleanup();
      if (!dragged && performance.now() - started < 350) {
        if (moduleMode && target.nodeId) options.onModuleSelect?.(target.nodeId);
        else options.onAuthorClick?.({...target,shiftKey:next.shiftKey});
      }
    };
    const cancel = () => { cleanup(); if (dragged) { view.x = originalX; view.y = originalY; updateView(); } };
    const escape = (next: KeyboardEvent) => { if (next.key === 'Escape') { next.preventDefault(); next.stopPropagation(); cancel(); } };
    activeDrags.set(host, cancel);
    viewport.addEventListener('pointermove', move); viewport.addEventListener('pointerup', end);
    viewport.addEventListener('pointercancel', cancel); viewport.addEventListener('lostpointercapture', cancel);
    window.addEventListener('blur', cancel); window.addEventListener('keydown', escape, true);
  }, true);
  viewport.addEventListener('pointerdown', event => {
    if (!navigating || moduleMode || event.button !== 0 || nativeInteraction(event)) return;
    const rawTarget = event.target instanceof Element ? event.target : null;
    if (rawTarget?.closest('[data-link-id]')) return;
    const target = authorTarget(event); if (!target.linkId) return;
    activeDrags.get(host)?.(); lastNodeClick = undefined; event.preventDefault(); event.stopPropagation();
    viewport.focus({ preventScroll: true }); if (!viewport.isConnected) return;
    viewport.setPointerCapture(event.pointerId);
    const started = performance.now(), startX = event.clientX, startY = event.clientY, originalX = view.x, originalY = view.y;
    let dragged = false;
    const move = (next: PointerEvent) => {
      if (next.pointerId !== event.pointerId) return;
      const dx = next.clientX - startX, dy = next.clientY - startY; dragged ||= Math.hypot(dx, dy) >= 5;
      if (dragged) { viewport.classList.add('ms-panning'); view.x = originalX + dx; view.y = originalY + dy; updateView(); }
    };
    const cleanup = () => {
      viewport.removeEventListener('pointermove', move); viewport.removeEventListener('pointerup', end); viewport.removeEventListener('pointercancel', cancel); viewport.removeEventListener('lostpointercapture', cancel);
      window.removeEventListener('blur', cancel); window.removeEventListener('keydown', escape, true); viewport.classList.remove('ms-panning');
      if (activeDrags.get(host) === cancel) activeDrags.delete(host);
      if (viewport.hasPointerCapture(event.pointerId)) viewport.releasePointerCapture(event.pointerId);
    };
    const end = (next: PointerEvent) => { if (next.pointerId !== event.pointerId) return; next.preventDefault(); next.stopPropagation(); cleanup(); if (!dragged && performance.now() - started < 350) options.onLinkSelect?.(target.linkId!, target.point, target.segmentIndex); };
    const cancel = () => { cleanup(); if (dragged) { view.x = originalX; view.y = originalY; updateView(); } };
    const escape = (next: KeyboardEvent) => { if (next.key === 'Escape') { next.preventDefault(); next.stopPropagation(); cancel(); } };
    activeDrags.set(host, cancel); viewport.addEventListener('pointermove', move); viewport.addEventListener('pointerup', end); viewport.addEventListener('pointercancel', cancel); viewport.addEventListener('lostpointercapture', cancel);
    window.addEventListener('blur', cancel); window.addEventListener('keydown', escape, true);
  }, true);
  for (const name of ['click', 'dblclick'] as const) viewport.addEventListener(name, event => {
    if (textMode && !editableTextTarget(event.target as Element) && !(event.target as Element).closest('[contenteditable="true"]') || !navigating && !nativeInteraction(event) || moduleMode && authorTarget(event).nodeId && !(event.ctrlKey && (event.target as Element).closest('.ms-image'))) { event.preventDefault(); event.stopPropagation(); }
  }, true);
  viewport.addEventListener('keydown', event => {
    if (event.target !== viewport) return;
    const target = targetAt({ x: (viewport.clientWidth / 2 - view.x) / view.scale, y: (viewport.clientHeight / 2 - view.y) / view.scale });
    if (menuKey(event) && options.onContextMenu && !options.readonly) { event.preventDefault(); event.stopPropagation(); options.onContextMenu(target); }
    else if (!navigating && !textMode && (event.key === 'Enter' || event.key === ' ')) { event.preventDefault(); event.stopPropagation(); options.onAuthorClick?.({...target,shiftKey:event.shiftKey}); }
  });
  const localGalleryItems=galleryItems.get(host)??new Map<string,string>(); galleryItems.set(host,localGalleryItems);
  // An explicit host map also represents Reset when empty. Standalone scene
  // callers that omit it retain the renderer's local browsing state.
  const sizingContext={galleryItems:options.galleryItems??Object.fromEntries(localGalleryItems)};
  const navigation = navigationIndex(project,sizingContext);
  renderSmartLinkRails(smartLayer,project,canvasId,{selectedId:options.selection,navigating,navigation,context:sizingContext,
    onSelect:id=>options.onSelection?.(id),
    onActivate:(id,event)=>{const node=navigation.nodes.get(id)!;options.onAuthorClick?.({...targetAt({x:node.x,y:node.y},{nodeId:id}),shiftKey:event.shiftKey});}});
  const windowOffsets=options.windowOffsets===undefined?windowPositions.get(host)??new Map<string,Point>():new Map(Object.entries(options.windowOffsets));windowPositions.set(host,windowOffsets);
  prepareWindowOffsets(project,navigation,windowOffsets,options.focusNodeId);
  for(const [id,at]of windowOffsets)options.onWindowScroll?.(id,at);
  const nodesById = navigation.nodes;
  // Route geometry is recalculated during pointer movement. Index the authored
  // order once rather than scanning every link for each route on every frame.
  const canvasLinks: MeridianLink[] = [];
  const pairCounts = new Map<string, Map<string, number>>();
  const linkOrdinals = new Map<string, number>();
  const incomingPorts = new Map<string, Map<number, MeridianLink>>();
  for (const link of project.links) {
    if (link.canvasId === canvasId) canvasLinks.push(link);
    const targets = pairCounts.get(link.sourceId) ?? new Map<string, number>();
    const ordinal = targets.get(link.targetId) ?? 0;
    linkOrdinals.set(link.id, ordinal); targets.set(link.targetId, ordinal + 1); pairCounts.set(link.sourceId, targets);
    if (isComputedLink(link.kind)) {
      const ports = incomingPorts.get(link.targetId) ?? new Map<number, MeridianLink>(), port = link.targetPort ?? 0;
      if (!ports.has(port)) ports.set(port, link);
      incomingPorts.set(link.targetId, ports);
    }
  }
  const canvas = project.canvases.find(item => item.id === canvasId);
  const canvasStyle = isCanvasStyle(canvas?.style) ? canvas?.style ?? {} : {};
  host.style.backgroundColor = canvasStyle.background ?? '';
  viewport.style.backgroundImage = 'none';
  const canvasBackground = label('div', 'ms-canvas-background'); canvasBackground.setAttribute('aria-hidden', 'true');
  const canvasPaint = backgroundPaint({ ...canvasStyle, ...(canvasStyle.gridVisible === false && !canvasStyle.backgroundAssetId ? { backgroundPattern: 'none' } : {}) }, project.assets, 'dots', canvasStyle.grid ?? '#293A4C');
  paintBackground(canvasBackground, canvasPaint); viewport.prepend(canvasBackground);
  const isRoot = (node: MeridianNode) => node.id === canvas?.rootId;
  const proxyNode = (node: MeridianNode): MeridianNode => navigation.visibleNode(node.id) ?? node;
  const canvasNodes = project.nodes.filter(node => node.canvasId === canvasId);
  const visible = canvasNodes.filter(node => proxyNode(node).id === node.id);
  const cards = new Map<string, HTMLElement>();
  const outlinePreviews = new Map<string, (width: number, height: number) => void>();
  let trayScene: ReturnType<typeof mountExpansionTrays> | undefined;
  let backgroundDisplay: ReturnType<typeof mountBackgroundDisplay> | undefined;
  let trayGeometryPreview: MeridianProject | undefined;
  let trayPreviewIndex: ReturnType<typeof navigationIndex> | undefined;
  let trayPreviewLinks: Map<string, MeridianLink> | undefined;
  let trayPreviewWindowOffsets: WindowOffsets | undefined;
  const geometryProject = () => trayGeometryPreview ?? project;
  const geometryNode = (node: MeridianNode) => trayPreviewIndex?.nodes.get(node.id) ?? node;
  let selectedIds = new Set((options.selections ?? (options.selection ? [options.selection] : []))
    .filter(id => nodesById.get(id)?.canvasId === canvasId && (moduleMode || navigation.isNodeVisible(id) || galleryOwner(project,id))));
  // Card content stays clipped, but that clip must not erase the outer half of
  // a selection stroke. Module outlines paint the identical silhouette in
  // screen space, independently of native border width or the camera scale.
  const moduleOutlineLayer = moduleMode ? svg('svg', { class: 'ms-module-selection-layer', 'aria-hidden': 'true' }) : undefined;
  const moduleOutlineDefs = svg('defs');
  const moduleOutlinePaint = new Map<string, { group: SVGGElement; path: SVGPathElement; clip: SVGRectElement }>();
  let moduleOutlineSequence = 0;
  if (moduleOutlineLayer) {
    moduleOutlineLayer.append(moduleOutlineDefs); viewport.append(moduleOutlineLayer);
    moduleOutlineResize = new ResizeObserver(() => refreshModuleOutlines()); moduleOutlineResize.observe(viewport);
  }
  function refreshModuleOutlines(): void {
    if (!moduleOutlineLayer) return;
    const viewportBounds = viewport.getBoundingClientRect();
    for (const [id, paint] of moduleOutlinePaint) if (!cards.get(id)?.classList.contains('ms-module-selected')) {
      paint.group.remove(); paint.clip.parentElement?.remove(); moduleOutlinePaint.delete(id);
    }
    for (const [id, card] of cards) {
      if (!card.classList.contains('ms-module-selected')) continue;
      const source = card.querySelector<SVGSVGElement>(':scope > .ms-node-outline');
      const sourcePath = source?.querySelector('path');
      if (!source || !sourcePath) continue;
      let paint = moduleOutlinePaint.get(id);
      if (!paint) {
        const clipId = `${markerPrefix}-module-${++moduleOutlineSequence}`;
        const clipPath = svg('clipPath', { id: clipId, clipPathUnits: 'userSpaceOnUse' }), clip = svg('rect'); clipPath.append(clip); moduleOutlineDefs.append(clipPath);
        const group = svg('g', { class: 'ms-module-outline', 'data-node-id': id, 'clip-path': `url(#${clipId})` });
        const path = svg('path', { 'vector-effect': 'non-scaling-stroke' }); group.append(path); moduleOutlineLayer.append(group);
        paint = { group, path, clip }; moduleOutlinePaint.set(id, paint);
      }
      const bounds = card.getBoundingClientRect(), frame = source.viewBox.baseVal;
      let left = viewportBounds.left, top = viewportBounds.top, right = viewportBounds.right, bottom = viewportBounds.bottom;
      // Window cells keep their enclosing scrolling aperture. The tile's own
      // overflow is deliberately excluded: only its content belongs to it.
      for (let parent = card.parentElement; parent && parent !== viewport; parent = parent.parentElement) if (parent.classList.contains('ms-window-viewport')) {
        const clip = parent.getBoundingClientRect(); left = Math.max(left, clip.left); top = Math.max(top, clip.top); right = Math.min(right, clip.right); bottom = Math.min(bottom, clip.bottom);
      }
      paint.group.style.display = bounds.width > 0 && bounds.height > 0 && right > left && bottom > top && getComputedStyle(card).visibility !== 'hidden' && (trayScene?.isNodeVisible(id)??true) ? '' : 'none';
      paint.path.setAttribute('d', sourcePath.getAttribute('d') ?? '');
      paint.path.setAttribute('transform', `translate(${bounds.left - viewportBounds.left} ${bounds.top - viewportBounds.top}) scale(${bounds.width / Math.max(.000001, frame.width)} ${bounds.height / Math.max(.000001, frame.height)})`);
      for (const [name, value] of Object.entries({ x: left - viewportBounds.left, y: top - viewportBounds.top, width: Math.max(0, right - left), height: Math.max(0, bottom - top) })) paint.clip.setAttribute(name, String(value));
      paint.path.setAttribute('stroke-dasharray', card.dataset.shape === 'none' || card.classList.contains('ms-label') ? '4 3' : 'none');
    }
  }
  const highlightSelection = (ids: ReadonlySet<string>) => {
    for (const [id, card] of cards) {
      const selected=ids.has(id)||isGallery(nodesById.get(id))&&[...ids].some(child=>navigation.visibleNode(child)?.id===id);
      card.classList.toggle('ms-selected', selected); card.setAttribute('aria-selected', String(selected));
      card.classList.toggle('ms-module-selected', moduleMode && selected);
    }
    refreshModuleOutlines();
    trayScene?.update(undefined,trayGeometryPreview);
  };
  const selectNodes = (ids: readonly string[], anchor?: Point) => {
    selectedIds = new Set(ids); highlightSelection(selectedIds);
    if (options.onSelections && !options.connecting) options.onSelections([...selectedIds], anchor);
    else options.onSelection?.(ids.at(-1) ?? null, anchor);
  };
  const toggleSelection = (id: string, anchor?: Point) => {
    const ids = new Set(selectedIds); if (ids.has(id)) ids.delete(id); else ids.add(id);
    selectNodes([...ids], anchor);
  };
  // A child selected alongside an ancestor is already part of that ancestor's
  // movable branch. Normalize before both preview and the single commit callback.
  const selectionRoots = (ids: readonly string[]) => {
    const chosen = new Set(lockedClusterNodeIds(project, canvasId, ids).map(id=>navigation.visibleNode(id)?.id??id).filter(id => nodesById.has(id) && id !== canvas?.rootId));
    return [...chosen].filter(id => {
      const seen = new Set<string>(); let parentId = nodesById.get(id)?.parentId;
      while (parentId && !seen.has(parentId)) {
        if (chosen.has(parentId)) return false;
        seen.add(parentId); parentId = nodesById.get(parentId)?.parentId;
      }
      return true;
    });
  };
  const moveSelectionSafely = (ids: readonly string[], dx: number, dy: number) => {
    const roots = selectionRoots(ids);
    if (options.canMoveNodes?.(roots) === false) return;
    const movingIds = new Set(roots.flatMap(id => [id, ...navigation.descendants(id).map(node => node.id)]));
    const utilitySelection = roots.length > 0 && roots.every(id => nodesById.get(id)?.kind === 'junction');
    const physical = (node: MeridianNode) => utilitySelection ? windowNodeVisible(node) : !windowParents(navigation,node.id).length && (trayScene?.isNodeVisible(node.id) ?? true);
    const geometry = (node: MeridianNode) => nodeShape(node, utilitySelection ? screenRect(node) : navigation.bounds(node));
    const moving = visible.filter(node => !isRoot(node) && movingIds.has(node.id) && physical(node)).map(geometry);
    const obstacles = visible.filter(node => !isRoot(node) && !movingIds.has(node.id) && physical(node)).map(geometry);
    const free = collisionFreeTranslation(moving, obstacles, { x: dx, y: dy }, (a, b) => isIntentionalContainment(navigation, a.id!, b.id!));
    if (!free.x && !free.y) return;
    if (options.onMoveSelection) options.onMoveSelection(roots, free.x, free.y);
    else for (const id of roots) { const node = nodesById.get(id)!; options.onMove?.(id, node.x + free.x, node.y + free.y); }
  };
  const children = new Map<string, string[]>();
  for (const node of canvasNodes) if (node.parentId) {
    const siblings = children.get(node.parentId) ?? [];
    siblings.push(node.id); children.set(node.parentId, siblings);
  }
  const renderedRect = (node: MeridianNode) => (trayPreviewIndex ?? navigation).bounds(geometryNode(node));
  const screenRect=(node:MeridianNode)=>displayNodeBounds(trayPreviewIndex??navigation,geometryNode(node),windowOffsets);
  const windowNodeVisible=(node:MeridianNode)=>{const bounds=screenRect(node);return (trayScene?.isNodeVisible(node.id)??true)&&windowParents(navigation,node.id).every(parent=>{const surface=windowSurface(geometryNode(parent),geometryProject()),offset=windowOffset(navigation,parent.id,windowOffsets),left=surface.geometry.left-offset.x,top=surface.geometry.top-offset.y;return bounds.x+ bounds.width>left&&bounds.x<left+surface.width&&bounds.y+bounds.height>top&&bounds.y<top+surface.height;});};
  // This control lives in screen space, so a microscopic seam is as easy to
  // lock as a large one. Contact and hit eligibility use the real silhouettes.
  const snapLock = document.createElement('button'); snapLock.type = 'button'; snapLock.className = 'ms-snap-lock'; snapLock.hidden = true;
  const canSnap = snapEligibility(project);
  const lockIcon = svg('svg', { viewBox: '0 0 24 24', 'aria-hidden': 'true' });
  const lockShackle = svg('path', { d: 'M8 10V7a4 4 0 0 1 8 0v3' });
  lockIcon.append(lockShackle, svg('rect', { x: '5', y: '10', width: '14', height: '11', rx: '3' }), svg('path', { d: 'M12 14v3' })); snapLock.append(lockIcon); viewport.append(snapLock);
  let hoveredSnap: { sourceId: string; targetId: string; point: Point; link?: MeridianLink } | undefined;
  const hideSnapLock = () => { hoveredSnap = undefined; snapLock.hidden = true; };
  function refreshSnapLock(): void {
    if (!hoveredSnap) return;
    const { sourceId, targetId, point, link } = hoveredSnap, locked = link?.snap?.locked === true;
    snapLock.dataset.sourceId = sourceId; snapLock.dataset.targetId = targetId;
    if (link) snapLock.dataset.linkId = link.id; else delete snapLock.dataset.linkId;
    snapLock.setAttribute('aria-pressed', String(locked)); snapLock.title = locked ? 'Unlock cluster edge' : 'Lock nodes together'; snapLock.setAttribute('aria-label', snapLock.title);
    lockShackle.setAttribute('d', locked ? 'M8 10V7a4 4 0 0 1 8 0v3' : 'M8 10V7a4 4 0 0 1 8 0');
    snapLock.style.left = `${view.x + point.x * view.scale}px`; snapLock.style.top = `${view.y + point.y * view.scale}px`; snapLock.hidden = false;
  }
  snapLock.addEventListener('pointerdown', event => { event.stopPropagation(); lastNodeClick = undefined; });
  snapLock.addEventListener('click', event => { event.preventDefault(); event.stopPropagation(); if (hoveredSnap) { const { sourceId, targetId, link } = hoveredSnap; options.onSnapLock?.(sourceId, targetId, link?.snap?.locked !== true, link?.id); } });
  viewport.addEventListener('pointerleave', () => { if (document.activeElement !== snapLock) hideSnapLock(); });
  viewport.addEventListener('pointermove', event => {
    if (!options.onSnapLock || options.readonly || options.connecting || !navigating || moduleMode || activeDrags.has(host)) { hideSnapLock(); return; }
    if ((event.target as Element).closest('.ms-snap-lock')) return;
    const rect = viewport.getBoundingClientRect(), point = { x: (event.clientX - rect.left - view.x) / view.scale, y: (event.clientY - rect.top - view.y) / view.scale }, tolerance = 9 / view.scale;
    const nearby = visible.filter(node => {
      if (isRoot(node) || !windowNodeVisible(node)) return false;
      const bounds = shapeBounds(nodeShape(node, screenRect(node)));
      return point.x >= bounds.x - tolerance && point.x <= bounds.x + bounds.width + tolerance && point.y >= bounds.y - tolerance && point.y <= bounds.y + bounds.height + tolerance;
    }).map(node => ({ node, shape: nodeShape(node, screenRect(node)) })).map(entry => ({ ...entry, edge: nearestBoundary(entry.shape, point) })).filter(entry => entry.edge.distance <= tolerance);
    let nearest: typeof hoveredSnap, distance = Infinity;
    for (let first = 0; first < nearby.length; first++) for (let second = first + 1; second < nearby.length; second++) {
      const a = nearby[first], b = nearby[second]; if (!canSnap(a.node.id, b.node.id) || !snapContact(a.shape, b.shape)) continue;
      const link = canvasLinks.find(link => isSnapLink(link) && (link.sourceId === a.node.id && link.targetId === b.node.id || link.sourceId === b.node.id && link.targetId === a.node.id));
      if (!link && (!resolveSnapping(canvas, a.node).stickyEdges || !resolveSnapping(canvas, b.node).stickyEdges)) continue;
      const seam = { x: (a.edge.point.x + b.edge.point.x) / 2, y: (a.edge.point.y + b.edge.point.y) / 2 };
      // Window scroll panes may clip the seam even when a node is partly visible.
      const clipped = [a.node, b.node].some(node => windowParents(navigation, node.id).some(parent => { const surface = windowSurface(parent, project), offset = windowOffset(navigation, parent.id, windowOffsets), left = surface.geometry.left - offset.x, top = surface.geometry.top - offset.y; return seam.x < left || seam.x > left + surface.width || seam.y < top || seam.y > top + surface.height; }));
      const gap = a.edge.distance + b.edge.distance; if (!clipped && gap < distance) { distance = gap; nearest = { sourceId: a.node.id, targetId: b.node.id, point: seam, link }; }
    }
    hoveredSnap = nearest; if (nearest) refreshSnapLock(); else hideSnapLock();
  });
  const draftGroup = options.linkDraft ? svg('g', { class: 'ms-link-draft', 'aria-hidden': 'true' }) : undefined;
  const draftPath = draftGroup ? svg('path', { class: 'ms-link-draft-line' }) : undefined;
  let draftCursor: SceneAuthorTarget | undefined, draftShift = false;
  const draftPivots: SVGCircleElement[] = [];
  if (draftGroup && draftPath) {
    draftGroup.append(draftPath);
    for (const point of options.linkDraft!.points) {const circle=svg('circle', { cx: String(point.x), cy: String(point.y), r: '5', class: 'ms-link-draft-pivot' });draftPivots.push(circle);draftGroup.append(circle);}
    linksLayer.append(draftGroup);
  }
  const updateDraft = () => {
    const draft = options.linkDraft, source = draft && nodesById.get(draft.sourceId);
    if (!draft || !source || !draftPath || !navigation.isNodeVisible(source.id)) return;
    const rect = viewport.getBoundingClientRect(), box = { ...source, ...screenRect(source) };
    let end = draftCursor ? { x: (draftCursor.clientX - rect.left - view.x) / view.scale, y: (draftCursor.clientY - rect.top - view.y) / view.scale }
      : draft.points.at(-1) ?? { x: box.x + box.width / 2, y: box.y + box.height / 2 };
    const sourceCenter={ x: box.x + box.width / 2, y: box.y + box.height / 2 };
    const destination=draftCursor?.nodeId?nodesById.get(draftCursor.nodeId):undefined;
    const destinationBox=destination?screenRect(destination):undefined;
    if(destinationBox)end={x:destinationBox.x+destinationBox.width/2,y:destinationBox.y+destinationBox.height/2};
    const peerObstacles=linkPivotObstacles(project,canvasId,source.id,destination?.id,screenRect);
    const resolved=constrainDraftRoute(sourceCenter,draft.points,draft.directions??[],peerObstacles);
    const origin=resolved.at(-1)??sourceCenter;
    for(const [index,circle]of draftPivots.entries()){circle.setAttribute('cx',String(resolved[index]!.x));circle.setAttribute('cy',String(resolved[index]!.y));}
    const obstacles=[...peerObstacles,...draftPivotObstacles(resolved)];
    const finish=draftShift&&(destination||draftCursor?.linkId)?orthogonalFinish(origin,end,obstacles):[];
    if(draftShift&&!destination&&!draftCursor?.linkId)end=axisLockedPoint(origin,end,obstacles);
    if(destination&&destinationBox){const previous=finish.at(-1)??origin;end=shapeAnchor({...destination,...destinationBox},previous.x-end.x,previous.y-end.y);}
    const route=[...resolved,...finish,end],toward=route[0]??end;
    const start = source.kind === 'junction' ? { x: box.x + box.width / 2, y: box.y + box.height / 2 }
      : shapeAnchor(box, toward.x - box.x - box.width / 2, toward.y - box.y - box.height / 2);
    draftPath.setAttribute('d', `M${start.x},${start.y} ${route.map(point => `L${finite(point.x)},${finite(point.y)}`).join(' ')}`);
  };
  if (draftGroup) {
    viewport.addEventListener('pointermove', event => { draftCursor=authorTarget(event);draftShift=event.shiftKey;updateDraft(); });
    const modifier=(event:KeyboardEvent)=>{if(event.key==='Shift'){draftShift=event.type==='keydown';updateDraft();}};
    const blur=()=>{draftShift=false;updateDraft();};
    window.addEventListener('keydown',modifier);window.addEventListener('keyup',modifier);window.addEventListener('blur',blur);
    draftModifierCleanup.set(host,()=>{window.removeEventListener('keydown',modifier);window.removeEventListener('keyup',modifier);window.removeEventListener('blur',blur);draftModifierCleanup.delete(host);});
  }
  const drawGuides = (guides: SnapGuide[]) => {
    guidesLayer.replaceChildren(...guides.map(guide => svg('line', {
      class: `ms-snap-guide ms-snap-guide-${guide.kind}`, 'data-axis': guide.axis,
      x1: String(guide.fromPoint?.x ?? (guide.axis === 'x' ? guide.position : guide.from)), y1: String(guide.fromPoint?.y ?? (guide.axis === 'y' ? guide.position : guide.from)),
      x2: String(guide.toPoint?.x ?? (guide.axis === 'x' ? guide.position : guide.to)), y2: String(guide.toPoint?.y ?? (guide.axis === 'y' ? guide.position : guide.to)),
      'vector-effect': 'non-scaling-stroke',
    })));
  };
  let renderOrigin: { x: number; y: number } | undefined;
  function updateView(): void {
    world.style.setProperty('--ms-camera-scale', String(view.scale));
    // Rebase only the rendered coordinate plane around the camera. Large
    // world coordinates magnified into tens of millions of screen pixels can
    // paint correctly while missing the browser's native hit-testing range.
    // Independent translation preserves authored positions and drag transforms;
    // Window cells already have local coordinates inside their direct surface.
    const centerX = viewport.clientWidth / 2, centerY = viewport.clientHeight / 2;
    // Ordinary pan/zoom changes only the world transform. Rewriting inherited
    // origin variables on every pointer move invalidates every card's styles.
    // Rebase after a bounded screen displacement, retaining native hit testing
    // even when the authored coordinates are millions of world units away.
    if (!renderOrigin || Math.abs(view.x + renderOrigin.x * view.scale - centerX) > 4096 ||
        Math.abs(view.y + renderOrigin.y * view.scale - centerY) > 4096) {
      renderOrigin = { x: (centerX - view.x) / view.scale, y: (centerY - view.y) / view.scale };
      world.style.setProperty('--ms-render-origin-x', `${-renderOrigin.x}px`);
      world.style.setProperty('--ms-render-origin-y', `${-renderOrigin.y}px`);
    }
    world.style.transform = `translate(${view.x + renderOrigin.x * view.scale}px, ${view.y + renderOrigin.y * view.scale}px) scale(${view.scale})`;
    // Each radial tile places its dot at the center: shift half a tile to model zero.
    const gridSize = GRID_SIZE * view.scale;
    viewport.style.backgroundPosition = `${view.x - gridSize / 2}px ${view.y - gridSize / 2}px`;
    viewport.style.backgroundSize = `${gridSize}px ${gridSize}px`;
    moveCanvasBackground(canvasBackground, canvasPaint, view);
    updateDraft();
    refreshSnapLock();
    refreshModuleOutlines();
    trayScene?.update(undefined,trayGeometryPreview);
    backgroundDisplay?.update();
    readability?.refresh();
    cameras.set(host, { ...view }); options.onViewChange?.({ ...view });
    zoomText.textContent = formatCameraZoom(view.scale);
  }
  const cameraBar = label('div', 'ms-camera');
  const zoomText = label('span', 'ms-zoom');
  const changeZoom = (factor: number) => {
    const cx = viewport.clientWidth / 2, cy = viewport.clientHeight / 2;
    Object.assign(view, scaleCameraAt(view, { x: cx, y: cy }, factor)); updateView();
  };
  cameraBar.append(button('−', 'Zoom out', () => changeZoom(0.8)), zoomText, button('+', 'Zoom in', () => changeZoom(1.25)), button('⌖', 'Fit Work', () => fitCanvas()));
  if (options.selection && nodesById.has(options.selection)) cameraBar.append(button('⊡', 'Fit selected branch', () => fitCanvas(options.selection!, true)));
  host.append(cameraBar);
  function fitCanvas(nodeId?: string, branch = false): void {
    if(nodeId)nodeId=navigation.visibleNode(nodeId)?.id??nodeId;
    const ids = nodeId ? new Set([nodeId, ...(branch || nodeId === canvas?.rootId ? navigation.descendants(nodeId).map(node => node.id) : [])]) : null;
    const content = visible.filter(node => (!isRoot(node) || node.collapsed) && (!ids || ids.has(node.id)) && (!windowParents(navigation,node.id).length||node.id===nodeId)&&windowNodeVisible(node)).map(screenRect);
    content.push(...visibleExpansionTrays(project,sizingContext).filter(tray=>nodesById.get(tray.sourceId)?.canvasId===canvasId&&(!ids||branch&&ids.has(tray.sourceId))&&windowNodeVisible(nodesById.get(tray.sourceId)!)).map(tray=>({id:tray.sourceId,x:tray.x,y:tray.y,width:tray.width,height:tray.height})));
    if (!content.length) { view.x = 44; view.y = 44; view.scale = 1; updateView(); return; }
    const minX = Math.min(...content.map(node => node.x)), minY = Math.min(...content.map(node => node.y));
    const maxX = Math.max(...content.map(node => node.x + node.width)), maxY = Math.max(...content.map(node => node.y + node.height));
    view.scale = clampCameraScale(Math.min(Math.max(1, viewport.clientWidth - 100) / Math.max(.001, maxX - minX), Math.max(1, viewport.clientHeight - 100) / Math.max(.001, maxY - minY)));
    view.x = (viewport.clientWidth - (maxX - minX) * view.scale) / 2 - minX * view.scale;
    view.y = (viewport.clientHeight - (maxY - minY) * view.scale) / 2 - minY * view.scale; updateView();
  }
  type DragOffset = { ids: Set<string>; x: number; y: number; project?: MeridianProject };
  const routeGeometry = (link: MeridianLink, offset?: DragOffset): { start: Point; end: Point; source: MeridianNode; target: MeridianNode } | null => {
    if (!navigation.isLinkVisible(link)) return null;
    const originalSource = nodesById.get(link.sourceId), originalTarget = nodesById.get(link.targetId);
    if (!originalSource || !originalTarget) return null;
    const source = originalSource, target = originalTarget;
    const effective = (node: MeridianNode) => {const bounds=screenRect(node);return { ...node, ...bounds, x: bounds.x + (offset?.ids.has(node.id) ? offset.x : 0), y: bounds.y + (offset?.ids.has(node.id) ? offset.y : 0) };};
    const effectiveSource = effective(source), effectiveTarget = effective(target);
    const sourceIsPoint = source.kind === 'junction';
    const targetIsPoint = target.kind === 'junction';
    const ports = inputPorts(originalTarget);
    const targetAnchor = link.targetAnchor ?? (isComputedLink(link.kind) && ports.length ? { x: 0, y: ((link.targetPort ?? 0) + 1) / (ports.length + 1) } : undefined);
    return {
      source:effectiveSource, target:effectiveTarget,
      start: sourceIsPoint ? { x: effectiveSource.x + effectiveSource.width / 2, y: effectiveSource.y + effectiveSource.height / 2 } : source.id === originalSource.id && link.sourceAnchor ? boundary(effectiveSource, link.sourceAnchor) : aimedAnchor(effectiveSource, effectiveTarget),
      end: targetIsPoint ? { x: effectiveTarget.x + effectiveTarget.width / 2, y: effectiveTarget.y + effectiveTarget.height / 2 } : target.id === originalTarget.id && targetAnchor ? boundary(effectiveTarget, targetAnchor) : aimedAnchor(effectiveTarget, effectiveSource),
    };
  };
  const routePaths = (link: MeridianLink, offset?: DragOffset, routePoints = trayPreviewLinks?.get(link.id)?.points ?? link.points ?? []): string[] => {
    const geometry = routeGeometry(link, offset); if (!geometry) return [];
    const { start, end } = geometry;
    const movePoints = offset && offset.ids.has(link.sourceId) && offset.ids.has(link.targetId);
    const commonWindow=windowParents(navigation,link.sourceId).find(parent=>windowParents(navigation,link.targetId).some(other=>other.id===parent.id));
    const scrollX=commonWindow?(windowOffset(navigation,commonWindow.id,windowOffsets).x+(windowOffsets.get(commonWindow.id)?.x??0)):0,scrollY=commonWindow?(windowOffset(navigation,commonWindow.id,windowOffsets).y+(windowOffsets.get(commonWindow.id)?.y??0)):0;
    const points = routePoints.map(point => ({ x: point.x-scrollX + (movePoints ? offset.x : 0), y: point.y-scrollY + (movePoints ? offset.y : 0) }));
    const loopSize = 65 + (linkOrdinals.get(link.id) ?? 0) * 24;
    const node = geometry.source, center = { x: node.x + node.width / 2, y: node.y + node.height / 2 };
    const vx = start.x - center.x, vy = start.y - center.y, length = Math.hypot(vx, vy) || 1, nx = vx / length, ny = vy / length;
    const ordinaryPath = points.length ? `M${start.x},${start.y} ${points.map(point => `L${finite(point.x)},${finite(point.y)}`).join(' ')} L${end.x},${end.y}`
      : link.sourceId === link.targetId ? `M${start.x},${start.y} C${start.x + nx * loopSize - ny * loopSize},${start.y + ny * loopSize + nx * loopSize} ${end.x + nx * loopSize + ny * loopSize},${end.y + ny * loopSize - nx * loopSize} ${end.x},${end.y}`
      : `M${start.x},${start.y} L${end.x},${end.y}`;
    const paths: string[] = [];
    if (link.kind === 'visual' && link.relation === 'cyclical' && link.direction === undefined && !points.length) {
      const dx = end.x - start.x, dy = end.y - start.y, length = Math.max(1, Math.hypot(dx, dy));
      const mid = { x: (start.x + end.x) / 2, y: (start.y + end.y) / 2 };
      const offset = 25;
      paths.push(`M${start.x},${start.y} Q${mid.x - dy / length * offset},${mid.y + dx / length * offset} ${end.x},${end.y}`);
      paths.push(`M${end.x},${end.y} Q${mid.x + dy / length * offset},${mid.y - dx / length * offset} ${start.x},${start.y}`);
    } else {
      paths.push(ordinaryPath);
      if (link.kind === 'visual' && link.relation === 'cyclical' && link.direction === undefined) paths.push(`M${end.x},${end.y} ${[...points].reverse().map(point => `L${finite(point.x) + 9},${finite(point.y) + 9}`).join(' ')} L${start.x},${start.y}`);
    }
    return paths;
  };
  type RoutePreview = (offset?: DragOffset) => void;
  const routePreviews: RoutePreview[] = [];
  const routePreviewsByNode = new Map<string, Set<RoutePreview>>();
  const addRoutePreview = (link: MeridianLink, update: RoutePreview) => {
    routePreviews.push(update);
    for (const id of new Set([link.sourceId, link.targetId])) {
      const updates = routePreviewsByNode.get(id) ?? new Set<RoutePreview>(); updates.add(update); routePreviewsByNode.set(id, updates);
    }
  };
  const windowClips=new Map<string,{id:string;rect:SVGRectElement}>();
  const refreshWindowClips=(drag?:DragOffset)=>{for(const [id,clip]of windowClips){
    let left=-Infinity,top=-Infinity,right=Infinity,bottom=Infinity;
    for(const parent of [nodesById.get(id)!,...windowParents(navigation,id)]){
      const surface=windowSurface(geometryNode(parent),geometryProject()),offset=windowOffset(navigation,parent.id,windowOffsets);
      const x=surface.geometry.left-offset.x+(drag?.ids.has(parent.id)?drag.x:0),y=surface.geometry.top-offset.y+(drag?.ids.has(parent.id)?drag.y:0);
      left=Math.max(left,x);top=Math.max(top,y);right=Math.min(right,x+surface.width);bottom=Math.min(bottom,y+surface.height);
    }
    clip.rect.setAttribute('x',String(left));clip.rect.setAttribute('y',String(top));clip.rect.setAttribute('width',String(Math.max(0,right-left)));clip.rect.setAttribute('height',String(Math.max(0,bottom-top)));
  }};
  const updateRoutes = (offset?: DragOffset) => {
    trayScene?.update(offset,offset?.project??trayGeometryPreview);
    backgroundDisplay?.update();
    refreshWindowClips(offset);
    if (!offset) { for (const update of routePreviews) update(); return; }
    // The moving IDs include the complete branch. Its internal Window routes,
    // crossing links and selected bend handles therefore refresh together,
    // while unrelated SVG routes remain untouched during a drag.
    const updates = new Set<RoutePreview>();
    for (const id of offset.ids) for (const update of routePreviewsByNode.get(id) ?? []) updates.add(update);
    for (const update of updates) update(offset);
  };
  const clipForWindow=(parent:MeridianNode)=>{let clip=windowClips.get(parent.id);if(!clip){const id=`${markerPrefix}-window-${windowClips.size}`,path=svg('clipPath',{id,clipPathUnits:'userSpaceOnUse'}),surface=windowSurface(parent,project),rect=svg('rect',{x:String(surface.geometry.left),y:String(surface.geometry.top),width:String(surface.width),height:String(surface.height)});path.append(rect);defs.append(path);clip={id,rect};windowClips.set(parent.id,clip);}return `url(#${clip.id})`;};
  for (const link of canvasLinks) {
    // Edge contact is the visible relationship; a snap never paints a redundant shaft.
    if (isSnapLink(link)) continue;
    const geometry = routeGeometry(link); if (!geometry) continue;
    const points = link.points ?? [];
    const commonWindow=windowParents(navigation,link.sourceId).find(parent=>windowParents(navigation,link.targetId).some(other=>other.id===parent.id));
    const clip=commonWindow?clipForWindow(commonWindow):undefined;
    const routeScroll=()=>{if(!commonWindow)return{x:0,y:0};const ancestor=windowOffset(navigation,commonWindow.id,windowOffsets),own=windowOffsets.get(commonWindow.id);return{x:ancestor.x+(own?.x??0),y:ancestor.y+(own?.y??0)};};
    const moduleSelected = moduleMode && selectedIds.has(link.sourceId) && selectedIds.has(link.targetId);
    const selected = options.selection === link.id || moduleSelected;
    const evaluated = options.running && isComputedLink(link.kind) ? options.evaluation?.values[link.sourceId] : undefined;
    const tone = selected ? 'selected' : link.kind === 'visual' ? 'quiet' : !options.running ? link.kind === 'numeric' ? 'number' : 'quiet' : typeof evaluated === 'number' ? 'number' : evaluated === true ? 'on' : evaluated === false ? 'off' : 'unknown';
    const style = isLinkStyle(link.style) ? link.style ?? {} : {};
    const customColor = style.color;
    const markerColor = moduleSelected ? '#8EF1D6' : customColor;
    const markerSize = clamp((style.width ?? (selected ? 3 : 2)) * 7, 10, 28);
    let marker = `url(#${markerPrefix}-${tone})`;
    if (markerColor || markerSize !== 14) {
      const id = `${markerPrefix}-styled-${defs.childElementCount}`;
      const colors: Record<string, string> = { quiet: '#7793AD', number: '#B69AF7', on: '#57DBC4', off: '#658093', unknown: '#E6B969', selected: '#F7E3A0' };
      const customMarker = svg('marker', { id, viewBox: '0 0 10 10', refX: '9', refY: '5', markerWidth: String(markerSize), markerHeight: String(markerSize), orient: 'auto-start-reverse', markerUnits: 'userSpaceOnUse' });
      customMarker.append(svg('path', { d: 'M 0 0 L 10 5 L 0 10 z', fill: markerColor ?? colors[tone]! })); defs.append(customMarker); marker = `url(#${id})`;
    }
    const linkLabel = isLinkLabel(link.label) ? link.label?.trim() : undefined;
    // Author tools insert at the actual shaft and preserve the correct waypoint
    // partition. Window panes convert the visible hit back to stored coordinates.
    linkHitTargets.set(link.id, point => {
      const current = routeGeometry(link) ?? geometry, scroll = routeScroll();
      const route = [current.start, ...points.map(entry => ({ x: entry.x - scroll.x, y: entry.y - scroll.y })), current.end];
      let best = Infinity, nearest = { ...point }, segmentIndex = 0;
      for (let index = 0; index < route.length - 1; index++) {
        const a = route[index]!, b = route[index + 1]!, dx = b.x - a.x, dy = b.y - a.y;
        const fraction = clamp(((point.x - a.x) * dx + (point.y - a.y) * dy) / Math.max(Number.EPSILON, dx * dx + dy * dy), 0, 1);
        const candidate = { x: a.x + fraction * dx, y: a.y + fraction * dy }, distance = Math.hypot(point.x - candidate.x, point.y - candidate.y);
        if (distance < best) { best = distance; nearest = candidate; segmentIndex = index; }
      }
      // Legacy cyclical/self routes can be curves. Pick their visible shaft,
      // rather than the invisible center chord, without changing persisted data.
      if (!points.length && (link.sourceId === link.targetId || link.relation === 'cyclical' && link.direction === undefined)) {
        best = Infinity;
        for (const { path } of pathElements) {
          const length = path.getTotalLength(), steps = Math.max(24, Math.min(256, Math.ceil(length / 3)));
          let nearestAt = 0;
          for (let at = 0; at <= steps; at++) {
            const candidate = path.getPointAtLength(length * at / steps), distance = Math.hypot(point.x - candidate.x, point.y - candidate.y);
            if (distance < best) { best = distance; nearestAt = length * at / steps; nearest = { x: candidate.x, y: candidate.y }; }
          }
          let span = length / steps;
          for (let pass = 0; pass < 4; pass++) {
            for (const at of [Math.max(0, nearestAt - span), Math.min(length, nearestAt + span)]) {
              const candidate = path.getPointAtLength(at), distance = Math.hypot(point.x - candidate.x, point.y - candidate.y);
              if (distance < best) { best = distance; nearestAt = at; nearest = { x: candidate.x, y: candidate.y }; }
            }
            span /= 2;
          }
        }
      }
      return { point: { x: nearest.x + scroll.x, y: nearest.y + scroll.y }, visiblePoint: nearest, segmentIndex };
    });
    const selectLink = (event: Event) => {
      if (textMode) return;
      event.stopPropagation();
      if (moduleMode) return;
      if (!navigating) {
        const at = linkHitTargets.get(link.id)!({ x: (geometry.start.x + geometry.end.x) / 2, y: (geometry.start.y + geometry.end.y) / 2 });
        options.onAuthorClick?.(targetAt(at.point, { linkId: link.id, segmentIndex: at.segmentIndex })); return;
      }
      if (selected && !options.connecting) return;
      let position: Point | undefined, segmentIndex: number | undefined;
      if (event instanceof MouseEvent) {
        const rect = viewport.getBoundingClientRect(); position = { x: (event.clientX - rect.left - view.x) / view.scale, y: (event.clientY - rect.top - view.y) / view.scale };
        const currentGeometry=routeGeometry(link)??geometry,scroll=routeScroll();
        const route = [currentGeometry.start, ...points.map(point=>({x:point.x-scroll.x,y:point.y-scroll.y})), currentGeometry.end]; let best = Infinity;
        for (let index = 0; index < route.length - 1; index++) {
          const a = route[index], b = route[index + 1], dx = b.x - a.x, dy = b.y - a.y;
          const fraction = clamp(((position.x - a.x) * dx + (position.y - a.y) * dy) / Math.max(Number.EPSILON, dx * dx + dy * dy), 0, 1);
          const distance = Math.hypot(position.x - a.x - fraction * dx, position.y - a.y - fraction * dy);
          if (distance < best) { best = distance; segmentIndex = index; }
        }
        position={x:position.x+scroll.x,y:position.y+scroll.y};
      }
      options.onLinkSelect?.(link.id, position, segmentIndex);
    };
    const pathElements: Array<{ hit: SVGPathElement; path: SVGPathElement }> = [];
    for (const pathData of routePaths(link)) {
      const group = svg('g', { class: `ms-link ms-link-${tone} ms-link-${link.kind}${selected ? ' ms-link-selected' : ''}${moduleSelected ? ' ms-module-link-selected' : ''}`, 'data-link-id': link.id });
      if(clip)group.setAttribute('clip-path',clip);
      const destinationPort = isComputedLink(link.kind) ? inputPorts(nodesById.get(link.targetId)!)[link.targetPort ?? 0] : undefined;
      const hit = svg('path', { d: pathData, class: 'ms-link-hit', tabindex: '0', role: 'button', 'aria-label': `${linkLabel ? `${linkLabel}. ` : ''}${link.kind} ${link.relation} link from ${geometry.source.title} to ${geometry.target.title}${destinationPort ? `, input ${destinationPort.name}` : ''}`, 'data-link-id': link.id });
      hit.style.strokeWidth = String(Math.max(16, (style.width ?? 2) + 8));
      const path = svg('path', { d: pathData, class: 'ms-link-line', 'data-link-id': link.id });
      const direction = linkDirection(link);
      if ((direction === 'forward' || direction === 'both') && geometry.target.kind !== 'junction') path.setAttribute('marker-end', marker);
      if ((direction === 'reverse' || direction === 'both' && !(link.relation === 'cyclical' && link.direction === undefined)) && geometry.source.kind !== 'junction') path.setAttribute('marker-start', marker);
      if (customColor) { path.style.stroke = customColor; path.style.opacity = '1'; }
      if (style.width !== undefined) path.style.strokeWidth = String(style.width);
      if (style.lineStyle === 'dotted') { path.style.strokeDasharray = `0 ${Math.max(4, (style.width ?? 2) * 3)}`; path.style.strokeLinecap = 'round'; }
      else if (style.lineStyle === 'solid') path.style.strokeDasharray = 'none';
      else if (style.lineStyle === 'dashed') path.style.strokeDasharray = '7 5';
      else if (style.dashed !== undefined) path.style.strokeDasharray = style.dashed ? '7 5' : 'none';
      else if(link.kind==='visual'&&link.relation==='chronological')path.style.strokeDasharray='9 4';
      const title = svg('title'); title.textContent = `${link.kind} · ${link.relation}`; group.append(title, hit, path);
      hit.addEventListener('click', selectLink);
      hit.addEventListener('keydown', event => {
        if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); selectLink(event); }
        else if (menuKey(event) && options.onContextMenu && !options.readonly) {
          event.preventDefault(); event.stopPropagation(); options.onContextMenu(targetAt({ x: (geometry.start.x + geometry.end.x) / 2, y: (geometry.start.y + geometry.end.y) / 2 }, { linkId: link.id }));
        }
      });
      hit.addEventListener('dblclick', event => {
        if (options.readonly || options.connecting || moduleMode || textMode || !options.onRouteEdit) return;
        event.stopPropagation(); const rect = viewport.getBoundingClientRect(),scroll=routeScroll();
        options.onRouteEdit(link.id, [...points, { x: (event.clientX - rect.left - view.x) / view.scale+scroll.x, y: (event.clientY - rect.top - view.y) / view.scale+scroll.y }]);
      });
      linksLayer.append(group);
      pathElements.push({ hit, path });
    }
    // Markers are not part of an SVG path's stroke hit region. Give each arrow
    // endpoint its own selectable target above the tile border, including legacy
    // zero-length/self connections. Shafts remain behind tile contents.
    const endpointHits: Array<{ element: SVGCircleElement; side: 'start' | 'end' }> = [];
    const direction = linkDirection(link);
    for (const side of (direction === 'both' ? ['start', 'end'] : direction === 'reverse' ? ['start'] : direction === 'forward' ? ['end'] : []) as Array<'start' | 'end'>) {
      if ((side === 'start' ? geometry.source : geometry.target).kind === 'junction') continue;
      const point = geometry[side];
      const hit = svg('circle', { cx: String(point.x), cy: String(point.y), r: '11', class: 'ms-arrow-hit', 'data-link-id': link.id, 'aria-label': `${linkLabel ? `${linkLabel}. ` : ''}${link.kind} ${link.relation} link from ${geometry.source.title} to ${geometry.target.title}` });
      if(clip)hit.setAttribute('clip-path',clip);
      hit.addEventListener('click', selectLink); endpointsLayer.append(hit); endpointHits.push({ element: hit, side });
    }
    let labelGroup: SVGGElement | undefined;
    if (linkLabel) {
      const colors: Record<string, string> = { quiet: '#7793AD', number: '#B69AF7', on: '#57DBC4', off: '#658093', unknown: '#E6B969', selected: '#F7E3A0' };
      labelGroup = svg('g', { class: 'ms-link-label-group', 'data-link-id': link.id });
      const background = svg('rect', { rx: '4', fill: canvasStyle.background ?? '#0B1420', stroke: customColor ?? colors[tone] });
      const text = svg('text', { class: 'ms-link-label', 'text-anchor': 'middle', 'dominant-baseline': 'central', fill: customColor ?? colors[tone] });
      text.textContent = linkLabel;
      labelGroup.append(background, text);
      if(clip){const clippedLabel=svg('g',{'clip-path':clip});clippedLabel.append(labelGroup);linksLayer.append(clippedLabel);}else linksLayer.append(labelGroup);
      const bounds = text.getBBox();
      background.setAttribute('x', String(bounds.x - 6)); background.setAttribute('y', String(bounds.y - 3));
      background.setAttribute('width', String(bounds.width + 12)); background.setAttribute('height', String(bounds.height + 6));
      labelGroup.addEventListener('click', selectLink);
    }
    const updateRoute = (offset?: DragOffset, routePoints?: Point[]) => {
      const shown=windowNodeVisible(nodesById.get(link.sourceId)!)&&windowNodeVisible(nodesById.get(link.targetId)!);
      for(const pair of pathElements){pair.path.parentElement!.style.display=shown?'':'none';}for(const endpoint of endpointHits)endpoint.element.style.display=shown?'':'none';if(labelGroup)labelGroup.style.display=shown?'':'none';
      routePaths(link, offset, routePoints).forEach((pathData, index) => {
        pathElements[index]?.hit.setAttribute('d', pathData); pathElements[index]?.path.setAttribute('d', pathData);
      });
      const endpoints = routeGeometry(link, offset);
      if (endpoints) for (const hit of endpointHits) {
        const cyclicalStart = hit.side === 'start' && link.relation === 'cyclical' && link.direction === undefined && pathElements.length > 1;
        const shaft = (cyclicalStart ? pathElements.at(-1) : pathElements[0])?.path;
        const point = endpoints[hit.side], length = shaft?.getTotalLength() ?? 0;
        const nearby = shaft?.getPointAtLength(hit.side === 'end' || cyclicalStart ? Math.max(0, length - 2) : Math.min(length, 2));
        const dx = point.x - (nearby?.x ?? point.x), dy = point.y - (nearby?.y ?? point.y), divisor = Math.hypot(dx, dy) || 1;
        // Center the target on the bounded arrowhead itself, keeping wide-link
        // selection useful without a huge invisible circle over a node's body.
        hit.element.setAttribute('cx', String(point.x - dx / divisor * markerSize * .4)); hit.element.setAttribute('cy', String(point.y - dy / divisor * markerSize * .4));
        hit.element.setAttribute('r', String(Math.max(11, markerSize * .75 + 1)));
      }
      const path = pathElements[0]?.path;
      if (labelGroup && path) {
        const point = path.getPointAtLength(path.getTotalLength() / 2);
        labelGroup.setAttribute('transform', `translate(${point.x},${point.y})`);
      }
    };
    updateRoute(); addRoutePreview(link, offset => updateRoute(offset));
    visibleShafts.set(link.id, {
      hit: linkHitTargets.get(link.id)!, width: style.width ?? (selected ? 3 : 2),
      visibleAt: point => {
        if (!pathElements.some(pair => pair.path.parentElement!.style.display !== 'none')) return false;
        if (!commonWindow) return true;
        const rect = windowClips.get(commonWindow.id)?.rect; if (!rect) return false;
        const x = Number(rect.getAttribute('x')), y = Number(rect.getAttribute('y')), width = Number(rect.getAttribute('width')), height = Number(rect.getAttribute('height'));
        return point.x >= x && point.x <= x + width && point.y >= y && point.y <= y + height;
      },
    });
    if (selected && !options.readonly && navigating && !moduleMode) points.forEach((point, index) => {
      const scroll=routeScroll();
      const handle = svg('circle', { cx: String(point.x-scroll.x), cy: String(point.y-scroll.y), r: '6', class: 'ms-route-handle', tabindex: '0', role: 'button', 'aria-label': `Route point ${index + 1}. Drag to move, Delete to remove.` });
      if(clip)handle.setAttribute('clip-path',clip);
      handle.addEventListener('pointerdown', event => {
        if (event.button !== 0) return;
        activeDrags.get(host)?.();
        event.stopPropagation(); event.preventDefault(); handle.setPointerCapture(event.pointerId);
        const rect = viewport.getBoundingClientRect(); let current = { ...point };
        const move = (next: PointerEvent) => { if (next.pointerId !== event.pointerId) return; const scroll=routeScroll();current = { x: (next.clientX - rect.left - view.x) / view.scale+scroll.x, y: (next.clientY - rect.top - view.y) / view.scale+scroll.y }; handle.setAttribute('cx', String(current.x-scroll.x)); handle.setAttribute('cy', String(current.y-scroll.y)); updateRoute(undefined, points.map((entry, i) => i === index ? current : entry)); };
        const cleanup = () => {
          handle.removeEventListener('pointermove', move); handle.removeEventListener('pointerup', end); handle.removeEventListener('pointercancel', cancel); handle.removeEventListener('lostpointercapture', cancel);
          window.removeEventListener('keydown', escape, true); window.removeEventListener('blur', cancel);
          if (activeDrags.get(host) === cancel) activeDrags.delete(host);
          if (handle.hasPointerCapture(event.pointerId)) handle.releasePointerCapture(event.pointerId);
        };
        const end = (next: PointerEvent) => { if (next.pointerId !== event.pointerId) return; cleanup(); options.onRouteEdit?.(link.id, points.map((entry, i) => i === index ? current : entry)); };
        const cancel = () => { cleanup();const scroll=routeScroll(); handle.setAttribute('cx', String(point.x-scroll.x)); handle.setAttribute('cy', String(point.y-scroll.y)); updateRoute(); };
        const escape = (next: KeyboardEvent) => { if (next.key === 'Escape') { next.preventDefault(); next.stopPropagation(); cancel(); } };
        activeDrags.set(host, cancel);
        handle.addEventListener('pointermove', move); handle.addEventListener('pointerup', end); handle.addEventListener('pointercancel', cancel); handle.addEventListener('lostpointercapture', cancel);
        window.addEventListener('keydown', escape, true); window.addEventListener('blur', cancel);
      });
      handle.addEventListener('keydown', event => { if (event.key === 'Delete' || event.key === 'Backspace') { event.preventDefault(); event.stopPropagation(); options.onRouteEdit?.(link.id, points.filter((_, i) => i !== index)); } });
      linksLayer.append(handle);
      addRoutePreview(link, offset => {
        const movePoint = offset && offset.ids.has(link.sourceId) && offset.ids.has(link.targetId);
        const scroll=routeScroll();
        handle.style.display=windowNodeVisible(nodesById.get(link.sourceId)!)&&windowNodeVisible(nodesById.get(link.targetId)!)?'':'none';
        const effectivePoint=trayPreviewLinks?.get(link.id)?.points?.[index]??point;
        handle.setAttribute('cx', String(effectivePoint.x-scroll.x + (movePoint ? offset.x : 0)));
        handle.setAttribute('cy', String(effectivePoint.y-scroll.y + (movePoint ? offset.y : 0)));
      });
    });
  }
  // Containers precede leaf cards in the DOM so nested content remains directly selectable.
  const depth = (node: MeridianNode) => { let count = 0, current = node; const seen = new Set<string>(); while (current.parentId && !seen.has(current.id)) { seen.add(current.id); const parent = nodesById.get(current.parentId); if (!parent) break; count++; current = parent; } return count; };
  visible.sort((a, b) => depth(a) - depth(b));
  const contentPass = createContainedContentPass(world);
  for (const node of visible) {
    if (isRoot(node) && !node.collapsed) continue;
    const isReference = node.kind === 'reference', block = node.kind === 'block', identity = workLibraryDisplayText(project.id, node.id, nodeTag(node));
    const referenceTarget = isReference ? resolveReferenceTarget(project,node) : undefined;
    const snapshot = referenceTarget?.snapshot;
    const sourceNode = referenceTarget?.node;
    // An explicit Hub stack belongs to this tile; legacy References continue
    // projecting their source while retaining their own title and identity.
    const authoredDisplayNode = isReference && node.contentItems !== undefined ? node : sourceNode ?? node;
    const activeContent = activeNodeContent(authoredDisplayNode), displayNode = { ...projectNodeContent(authoredDisplayNode),
      text: activeContent.text, textRuns: activeContent.textRuns, assetId: activeContent.assetId, hyperlinks: activeContent.hyperlink ? [activeContent.hyperlink] : undefined };
    const style = isNodeStyle(node.style) ? node.style ?? {} : {};
    const childCount = navigation.children.get(node.id)?.length ?? 0;
    const cellChildCount = navigation.children.get(node.id)?.filter(child => isCellChild(child) || child.cell).length ?? 0;
    const linkCount = navigation.linksByParent.get(node.id)?.length ?? 0;
    const routePoint = node.kind === 'junction';
    const hasChildren = childCount > 0 || !routePoint && linkCount > 0;
    const docAsset = documentNodeAsset(project, node), docOpen = !!docAsset && documentNodeExpanded(node);
    const canDisclose = !!docAsset || hasChildren || isEnclosingContainer(node);
    const contentsLabel = [childCount ? `${childCount} child node${childCount === 1 ? '' : 's'}` : '', linkCount ? `${linkCount} connection${linkCount === 1 ? '' : 's'}` : ''].filter(Boolean).join(', ');
    const compact = isCompactContainer(node, hasChildren);
    const clusterButton = !!node.cluster && !!node.collapsed;
    const labelMode = !clusterButton && style.variant === 'label' && activeContent.type === 'text';
    const savedAssets = referenceTarget?.saved && node.contentItems === undefined && snapshot ? [snapshot.asset, ...snapshot.assets ?? []] : [];
    const asset = !activeContent.assetId ? undefined : savedAssets.find(item => item?.id === activeContent.assetId) ?? project.assets.find(item => item.id === activeContent.assetId) ?? docAsset;
    const evaluated = options.evaluation?.values[node.id];
    const signalType = outputValueType(project, node), operation = node.kind === 'joint' ? operationDefinition(node.operation, node.valueType) : undefined;
    const card = label('article', `ms-node ms-node-${node.kind}${routePoint ? ' ms-utility' : ''}${node.collapsed ? ' ms-collapsed' : ''}${hasChildren ? ' ms-has-children' : ''}${compact ? ' ms-compact' : ''}${style.titleSize !== undefined ? ' ms-styled-title' : ''}${selectedIds.has(node.id) ? ' ms-selected' : ''}${isReference ? ' ms-reference' : ''}${labelMode ? ' ms-label' : ''}${style.textColor ? ' ms-custom-text' : ''}${style.fill ? ' ms-custom-fill' : ''}${style.accent ? ' ms-accented' : ''}`);
    cards.set(node.id, card);
    if (docAsset) card.classList.add('ms-document-node');
    if (docOpen) card.classList.add('ms-document-open');
    if (node.cluster) card.classList.add('ms-disclosure-cluster');
    if (clusterButton) {
      card.classList.add('ms-cluster-button'); card.dataset.shape = node.cluster!.buttonShape;
      card.setAttribute('role', 'button');
    }
    if (moduleMode && selectedIds.has(node.id)) card.classList.add('ms-module-selected');
    const automatic=nodeSizingMode(node)==='auto'; if(automatic)card.classList.add('ms-auto-size');
    if(node.smartLink)card.classList.add('ms-smart-link-owner');
    if(style.textLayout==='balanced')card.classList.add('ms-balanced-text');
    if(node.childLayout==='spatial'&&!isGallery(node))card.classList.add('ms-spatial-parent');
    if(isGallery(node)){card.classList.add('ms-gallery-parent');if([...selectedIds].some(id=>navigation.visibleNode(id)?.id===node.id))card.classList.add('ms-selected');}
    card.dataset.nodeId = node.id; card.tabIndex = 0; card.setAttribute('aria-label', `${identity}, ${nodeKindLabel(node.kind)}${hasChildren ? `, ${contentsLabel}` : ''}${isReference ? ', read-only reference' : ''}`);
    if (clusterButton) card.setAttribute('aria-label', `Expand Cluster: ${identity}`);
    if (routePoint) { card.dataset.shape = nodeShape(node).shape; card.dataset.utilityVariant = node.utilityVariant ?? 'joint'; }
    else if (!clusterButton && style.shape) card.dataset.shape = style.shape;
    if (signalType) card.dataset.valueType = signalType;
    if (routePoint) card.title = `${identity} · ${node.utilityVariant ?? 'joint'}${signalType ? ` · ${signalType} · ${options.running ? valueText(evaluated) : 'Stopped'}` : ''}`;
    card.setAttribute('aria-selected', String(card.classList.contains('ms-selected')));
    const bounds = renderedRect(node);
    Object.assign(card.style, { left: `${bounds.x}px`, top: `${bounds.y}px`, width: `${bounds.width}px`, height: `${bounds.height}px` });
    const outline = svg('svg', { class: 'ms-node-outline', viewBox: `0 0 ${bounds.width} ${bounds.height}`, 'aria-hidden': 'true' });
    const outlinePath = svg('path', { d: shapePath(nodeShape(node, { ...bounds, x: 0, y: 0 })), 'vector-effect': 'non-scaling-stroke' });
    outline.append(outlinePath); card.append(outline);
    if((style.borderStyle&&style.borderStyle!=='solid'||style.backgroundDisplay==='hover'&&card.dataset.shape)&&style.shape!=='none'&&!labelMode){
      card.classList.add('ms-pattern-border');const border=svg('svg',{class:'ms-node-border',viewBox:`0 0 ${bounds.width} ${bounds.height}`,'aria-hidden':'true'}),width=effectiveNodeBorderWidth(node,bounds.width,bounds.height);
      const inset=width/2,path=svg('path',{d:shapePath(nodeShape(node,{x:inset,y:inset,width:Math.max(1,bounds.width-width),height:Math.max(1,bounds.height-width)})),'stroke-width':String(style.borderStyle==='double'?width/3:width)});
      if(style.borderStyle==='dashed')path.setAttribute('stroke-dasharray',`${width*4} ${width*3}`);if(style.borderStyle==='dotted'){path.setAttribute('stroke-dasharray',`0 ${Math.max(2,width*3)}`);path.setAttribute('stroke-linecap','round');}border.append(path);
      if(style.borderStyle==='double')border.append(svg('path',{d:shapePath(nodeShape(node,{x:width,y:width,width:Math.max(1,bounds.width-width*2),height:Math.max(1,bounds.height-width*2)})),'stroke-width':String(width/3)}));card.append(border);
    }
    const surface = svg('svg', { class: 'ms-node-hit-surface', viewBox: `0 0 ${bounds.width} ${bounds.height}`, 'aria-hidden': 'true' });
    const surfacePath = svg('path', { d: shapePath(nodeShape(node, { ...bounds, x: 0, y: 0 })), 'vector-effect': 'non-scaling-stroke' });
    surface.append(surfacePath); card.append(surface);
    const refreshOutline = (width: number, height: number) => {
      const frame={x:0,y:0,width,height},physical=shapeBounds(nodeShape(node,frame)),path=shapePath(nodeShape(node,frame));
      outline.setAttribute('viewBox',`0 0 ${width} ${height}`);surface.setAttribute('viewBox',`0 0 ${width} ${height}`);outlinePath.setAttribute('d',path);surfacePath.setAttribute('d',path);
      card.style.setProperty('--ms-shape-inset-x',`${physical.x}px`);card.style.setProperty('--ms-shape-inset-y',`${physical.y}px`);
      const renderedBorder=effectiveNodeBorderWidth(node,width,height);
      card.style.setProperty('--ms-border-width',`${renderedBorder}px`);
      // Native CSS borders round positive subpixel widths to a device pixel and
      // can make a 1px tile physically wider than its saved frame. SVG preserves
      // the authored silhouette and proportional stroke at microscopic scales.
      const vectorBorder=!card.dataset.shape&&!routePoint&&!labelMode&&renderedBorder>0&&renderedBorder<1;
      card.classList.toggle('ms-vector-border',vectorBorder);
      let proportional=card.querySelector<SVGSVGElement>(':scope > .ms-proportional-border');
      if(!vectorBorder&&proportional){proportional.remove();proportional=null;}
      if(vectorBorder&&!card.querySelector(':scope > .ms-node-border')){
        proportional=svg('svg',{class:'ms-node-border ms-proportional-border','aria-hidden':'true'});
        proportional.append(svg('path'));card.append(proportional);
      }
      const cssBorder=card.dataset.shape||routePoint||labelMode||vectorBorder?0:renderedBorder;
      card.style.setProperty('--ms-overlay-border-inset',`${cssBorder}px`);
      if(!routePoint&&!labelMode&&style.shape!=='none'){
        // Shape insets stay physical; the inner content's authored padding is
        // part of its fitted composition and contracts with tiny tiles.
        const safe=nodeContentBounds({...node,style:{...style,padding:0}},frame),backdrop=nodeShape(node,frame);
        const corner=backdrop.shape==='rounded'&&Math.min(physical.width,physical.height)<36
          ? Math.min(backdrop.radius??11,physical.width/2,physical.height/2)*(1-Math.SQRT1_2):0;
        const left=Math.max(0,safe.x-cssBorder+corner),top=Math.max(0,safe.y-cssBorder+corner);
        const right=Math.max(0,width-safe.x-safe.width-cssBorder+corner),bottom=Math.max(0,height-safe.y-safe.height-cssBorder+corner);
        card.style.padding=`${top}px ${right}px ${bottom}px ${left}px`;
      }
      const patterned=card.querySelector<SVGSVGElement>(':scope > .ms-node-border');
      if(patterned){patterned.setAttribute('viewBox',`0 0 ${width} ${height}`);[...patterned.querySelectorAll('path')].forEach((item,index)=>{const inset=renderedBorder*(index===0?.5:1);item.setAttribute('d',shapePath(nodeShape(node,{x:inset,y:inset,width:Math.max(.001,width-2*inset),height:Math.max(.001,height-2*inset)})));item.setAttribute('stroke-width',String(style.borderStyle==='double'?renderedBorder/3:renderedBorder));if(style.borderStyle==='dashed')item.setAttribute('stroke-dasharray',`${renderedBorder*4} ${renderedBorder*3}`);if(style.borderStyle==='dotted')item.setAttribute('stroke-dasharray',`0 ${Math.max(.001,renderedBorder*3)}`);});}
    };
    refreshOutline(bounds.width,bounds.height); outlinePreviews.set(node.id,refreshOutline);
    const properties = { fill: '--ms-fill', border: '--ms-border', textColor: '--ms-text-color', accent: '--ms-accent', textAlign: '--ms-text-align' } as const;
    for (const [key, property] of Object.entries(properties)) {
      const value = style[key as keyof typeof properties]; if (value !== undefined) card.style.setProperty(property, value);
    }
    if (routePoint) {
      // The utility silhouette is its actual node backdrop. The same dimensions
      // drive collision, selection, routing and appearance instead of a fixed
      // ornament floating inside a larger invisible tile.
      card.style.setProperty('--ms-fill', style.fill ?? (signalType === 'number' ? '#C4A9FF' : signalType === 'boolean' ? '#78DFC8' : '#A6BBCD'));
      card.style.setProperty('--ms-border', style.border ?? '#172334');
    }
    if (style.fontFamily) card.style.setProperty('--ms-font', FONT_FAMILIES.find(font => font.value === style.fontFamily)!.css);
    if (style.fontSize !== undefined) card.style.setProperty('--ms-font-size', `${style.fontSize}px`);
    if (style.titleSize !== undefined) card.style.setProperty('--ms-title-size', `${style.titleSize}px`);
    if(style.textLayout==='balanced'){const typography=nodeTypography(displayNode);card.style.setProperty('--ms-font-size',`${typography.fontSize}px`);card.style.setProperty('--ms-title-size',`${typography.titleSize}px`);card.style.setProperty('--ms-text-align',style.textAlign??'center');}
    for(const [key,property]of [['padding','--ms-padding'],['contentGap','--ms-content-gap'],['textMediaGap','--ms-text-media-gap'],['childPadding','--ms-child-padding']]as const)card.style.setProperty(property,`${style[key]??(key==='childPadding'?DEFAULT_CHILD_INSET:5)}px`);
    if(style.borderStyle)card.style.setProperty('--ms-border-style',style.borderStyle);
    if (style.radius !== undefined) card.style.setProperty('--ms-radius', `${style.radius}px`);
    const ownBackground = ['backgroundPattern','backgroundAssetId'].some(key => key in style);
    const backdropStyle = ownBackground ? style : isNodeStyle(sourceNode?.style) ? sourceNode?.style ?? style : style;
    const backdropAssets = !ownBackground && referenceTarget?.saved && snapshot?.backgroundAsset ? [snapshot.backgroundAsset, ...project.assets] : project.assets;
    const backdropPaint = backgroundPaint(backdropStyle, backdropAssets);
    if (backdropPaint.image !== 'none' && !routePoint) {
      const backdrop = label('div', 'ms-node-background'); backdrop.setAttribute('aria-hidden', 'true');
      paintBackground(backdrop, backdropPaint); card.append(backdrop);
      markHoverPaint(backdrop,backdropPaint.opacity);
    }
    prepareBackgroundDisplay(card,node);
    const header = label('header', 'ms-node-header');
    const visibleTitle = node.showTitle === false ? '' : workLibraryDisplayText(project.id, node.id, node.title);
    const title = label('span', 'ms-node-title'); renderRichText(title, block ? '' : visibleTitle, block || visibleTitle !== node.title ? undefined : node.titleRuns); title.title = visibleTitle;
    if (!block) prepareTextSurface(title, node.id, true);
    setTextRotation(title, style.titleRotation);
    const kind = label('span', 'ms-node-kind', isReference ? '↗ reference' : node.kind === 'module' ? 'MODULE' : node.kind === 'input' ? 'IN' : node.kind === 'number' ? '# NUMBER' : node.kind === 'output' ? signalType === 'number' ? '# OUT' : 'OUT' : node.kind === 'joint' ? operation?.label ?? 'JOINT' : node.kind === 'switch' ? 'SW' : node.kind === 'junction' ? '●' : '');
    header.append(title, kind);
    if (!hidesOwnContent(node) && !automatic && !isGallery(node) && node.childLayout !== 'spatial' && childCount > 0 && !node.collapsed && displayNode.text && !['image', 'document'].includes(displayNode.kind)) {
      const read = button('≡', `Read description of ${identity}`, () => openNodeText({ ...node, text: displayNode.text, textRuns: displayNode.textRuns })); read.className = 'ms-read-description'; header.append(read);
    }
    if (!block && !clusterButton && visibleTitle) card.append(header);
    if(!isGallery(node) && node.containerVariant!=='window' && node.grid && !node.collapsed && ((node.childArrangement??'grid')==='grid'||isOrderedContainer(node)) && !(automatic&&node.containerVariant==='grid'&&cellChildCount===0)) {
      const geometry=gridGeometry(node,project), grid=label('div','ms-cell-grid');grid.setAttribute('aria-label',`${identity} cells`);
      const enclosing = isEnclosingContainer(node);
      const positioned=(e:HTMLElement,x:number,y:number,width:number,height:number)=>{Object.assign(e.style,{left:`${x-(enclosing?node.x:0)}px`,top:`${y-(enclosing?node.y:0)}px`,width:`${width}px`,height:`${height}px`});grid.append(e);};
      if(!isOrderedContainer(node)&&node.grid.showHeaders!==false){for(let column=1;column<=geometry.columns;column++)positioned(label('span','ms-grid-header',String(column)),geometry.left+(column-1)*(geometry.cellWidth+geometry.gap),geometry.top-geometry.headerHeight,geometry.cellWidth,geometry.headerHeight);for(let row=1;row<=geometry.rows;row++)positioned(label('span','ms-grid-header',String(row)),geometry.left-geometry.headerWidth,geometry.top+(row-1)*(geometry.cellHeight+geometry.gap),geometry.headerWidth,geometry.cellHeight);}
      const occupied=new Set((navigation.children.get(node.id)??[]).filter(n=>n.cell).map(n=>cellAddress(n.cell!)));
      // Sparse grids use a lightweight patterned surface; empty-cell controls are
      // bounded so a large authored address space never creates 16k DOM buttons.
      const cells=Math.min(geometry.rows*geometry.columns,1600);
      if(node.kind==='spreadsheet'||node.containerVariant==='spreadsheet')for(let at=0;at<cells;at++){const column=at%geometry.columns+1,row=Math.floor(at/geometry.columns)+1,address=cellAddress({column,row});if(occupied.has(address))continue;const empty=options.onCellOpen&&!options.readonly&&!options.connecting?button('+',`Add cell ${address} in ${identity}`,()=>options.onCellOpen?.(node.id,address)):label('span','');empty.className='ms-empty-cell';empty.dataset.cellAddress=address;positioned(empty,geometry.left+(column-1)*(geometry.cellWidth+geometry.gap),geometry.top+(row-1)*(geometry.cellHeight+geometry.gap),geometry.cellWidth,geometry.cellHeight);}
      (enclosing ? card : world).append(grid); if(enclosing)card.classList.add('ms-grid-parent');else grid.dataset.externalGrid = node.id;
    }
    const body = label('div', 'ms-node-body');
    if (block) {
      // Blocks retain their backdrop and graph behavior, never their dormant
      // title/content left over from a reversible type conversion.
    } else if (clusterButton) {
      card.append(clusterEmblem(identity));
    } else if (isEmbeddedGallery(displayNode) && !node.collapsed && !referenceTarget?.saved) {
      let localItems=galleryItems.get(host);if(!localItems){localItems=new Map();galleryItems.set(host,localItems);}
      renderGalleryTile(body,project,displayNode,{frameWidth:bounds.width,activeItems:options.galleryItems,localItems,onPage:options.onGalleryPage??(()=>renderScene(host,project,canvasId,{...options,view})),onInspect:options.onGalleryInspect,onReveal:options.onToggle?id=>options.onToggle?.(id,'collapsed',false):undefined,
        canvasPortalStyle:canvas?.style?.portalStyle,canvasPortalColor:canvas?.style?.portalColor,canvasNodeTitles:options.canvasNodeTitles,
        onAssetOpen:options.onAssetOpen,onCanvasOpen:item=>{if(options.onCanvasNodeOpen)options.onCanvasNodeOpen(item.id);else{const original=item.kind==='reference'?resolveReferenceTarget(project,item)?.node:item;const target=project.canvases.find(canvas=>canvas.id===original?.targetId||canvas.rootId===original?.targetId)?.id;if(target)options.onCanvasOpen?.(target);}},
        prepareText:!isReference?(surface,id,title)=>prepareTextSurface(surface,id,title):undefined,
        imageGesture:(image,asset,title)=>imageWindowGesture(image,host,asset,title)});
      card.append(body);
    } else {
      if (isReference) {
        const referenceStatus = label('div', 'ms-reference-status', sourceNode ? !referenceTarget?.saved ? `Linked reference${node.targetCell?' · '+node.targetCell:''}` : `Pinned · ${project.canvases.find(item => item.id === sourceNode.canvasId)?.title ?? 'external Work'}${node.targetCell?' · '+node.targetCell:''}` : 'Missing reference');
        body.append(referenceStatus);
        if(referenceTarget?.saved&&isGallery(displayNode))body.append(label('p','ms-reference-status','This saved reference includes the gallery summary. Open the original context to browse its collection.'));
        if (sourceNode && signalType) body.append(label('div', `ms-reference-value${signalType === 'number' ? ' ms-number-readout' : ''}`, options.running ? valueText(evaluated) : 'Stopped'));
      }
      const isMedia = activeContent.type === 'asset';
      if (!hidesOwnContent(node) && activeContent.type === 'text' && displayNode.text) {
        const text = label('div', 'ms-text'); renderRichText(text, workLibraryDisplayText(project.id, node.id, displayNode.text), workLibraryDisplayText(project.id, node.id, displayNode.text) === displayNode.text ? displayNode.textRuns : undefined); body.append(text);
        if (!isReference || node.contentItems !== undefined) prepareTextSurface(text, node.id, false);
      }
      if (!hidesOwnContent(node) && activeContent.hyperlink) {
        const anchor = document.createElement('a'); anchor.className = 'ms-content-link'; anchor.href = activeContent.hyperlink.url;
        anchor.textContent = activeContent.hyperlink.label || activeContent.hyperlink.url; anchor.target = '_blank'; anchor.rel = 'noopener noreferrer'; body.append(anchor);
      }
      if (isMedia && !hidesOwnContent(node)) {
        if (asset && raster.test(asset.mime)) {
          const image = document.createElement('img'); image.src = `data:${asset.mime};base64,${asset.base64}`; image.alt = asset.name; image.draggable = false; image.className = `ms-image${style.imageFit === 'cover' ? ' ms-image-cover' : ''}`;if(automatic){const media=nodeContentLayout(project,node,sizingContext).media;if(media)Object.assign(image.style,{width:`${media.width}px`,height:`${media.height}px`});} imageWindowGesture(image, host, asset, node.title); body.append(image);
        } else if(asset && /^video\/(mp4|webm|ogg)$/i.test(asset.mime)){const video=primaryMediaElement(host,`${project.id}/${node.id}`,asset,'video');video.className='ms-video';video.style.width='';video.style.height='';if(automatic){const media=nodeContentLayout(project,node,sizingContext).media;if(media)Object.assign(video.style,{width:`${media.width}px`,height:`${media.height}px`});}body.append(video);}
        else if(asset && /^audio\/(mpeg|mp3|ogg|wav|wave|x-wav|webm|aac|mp4)$/i.test(asset.mime)){const audio=primaryMediaElement(host,`${project.id}/${node.id}`,asset,'audio');audio.className='ms-audio';Object.assign(audio.style,{width:'100%',maxWidth:'100%',minWidth:'0'});body.append(audio);}
        else if (asset && documentPreviewKind(asset)) {
          if (docOpen) body.append(inlineDocumentReader(host, asset, () => options.onToggle?.(node.id, 'collapsed', true), () => options.onAssetOpen?.(asset.id), `${project.id}/${node.id}/${asset.id}`));
          // The authored title identifies a compact document. Keep file details in the reader and labeled controls.
        }
        else if (asset) body.append(label('div', 'ms-file-placeholder', `Document · ${asset.name}`));
        else if (activeContent.assetId) body.append(label('div', 'ms-file-placeholder', 'Attachment unavailable.'));
        if (asset && !raster.test(asset.mime) && !options.connecting && !docOpen) {
          if (documentPreviewKind(asset)) {
            const actions = label('div', 'ms-document-actions');
            actions.append(documentActionButton('read', `Read ${asset.name} in this node`, () => {
              if (docAsset && options.onToggle) options.onToggle(node.id, 'collapsed', false);
              else options.onAssetOpen?.(asset.id);
            }));
            const reading = documentActionButton('reading-view', `Read ${asset.name} in a larger view`, () => options.onAssetOpen?.(asset.id));
            reading.classList.add('ms-document-reading-open'); actions.append(reading); body.append(actions);
          } else body.append(button('Open attachment ↗', `Open ${asset.name}`, () => options.onAssetOpen?.(asset.id)));
        }
      }
      if (displayNode.kind === 'canvas-link' && (displayNode.targetId || options.canvasNodeTitles?.[node.id] || displayNode.canvasTarget && options.onCanvasNodeOpen) && !options.connecting) {
        const targetId = project.canvases.find(canvas => canvas.rootId === displayNode.targetId || canvas.id === displayNode.targetId)?.id ?? displayNode.targetId;
        const destinationName = canvasDestinationName(project,node,displayNode,options.canvasNodeTitles);
        const open = canvasLauncher(`Open ${destinationName}`, () => {
          if (options.onCanvasNodeOpen) options.onCanvasNodeOpen(node.id);
          else if (targetId) options.onCanvasOpen?.(targetId);
        }, {destinationName,style:effectivePortalStyle(node,displayNode,canvas?.style?.portalStyle),color:effectivePortalColor(node,displayNode,canvas?.style?.portalColor)});
        body.append(open);
      }
      const openLabel = options.onNodeOpen && !options.connecting ? options.nodeOpenLabel?.(node) : undefined;
      if (openLabel) {
        const open = canvasLauncher(openLabel, () => options.onNodeOpen?.(node.id), {style:effectivePortalStyle(node,displayNode,canvas?.style?.portalStyle),color:effectivePortalColor(node,displayNode,canvas?.style?.portalColor)});
        open.classList.add('ms-node-open'); body.append(open);

      }
      if ((node.kind === 'input' || node.kind === 'switch') && !options.connecting) {
        const enabled = !options.readonly || node.exposed === true;
        const active = node.kind === 'input' ? node.value === true : node.closed !== false;
        const control = button(active ? node.kind === 'switch' ? 'Closed' : 'On' : node.kind === 'switch' ? 'Open' : 'Off', `${identity}: ${node.kind === 'switch' ? 'toggle switch' : 'toggle input'}`, () => node.kind === 'input' ? options.onValue?.(node.id, !active) : options.onToggle?.(node.id, 'closed', !active));
        control.className = `ms-value-control${active ? ' is-on' : ''}`; control.setAttribute('role', 'switch'); control.setAttribute('aria-checked', String(active)); control.disabled = !enabled; body.append(control);
        if (options.readonly && !enabled) body.append(label('span', 'ms-control-note', 'Fixed by author'));
      }
      if (node.kind === 'number' && !options.connecting) {
        const control = document.createElement('input'); control.type = 'number'; control.step = 'any'; control.className = 'ms-number-control'; control.value = typeof node.value === 'number' && Number.isFinite(node.value) ? String(node.value) : '';
        control.setAttribute('aria-label', `${identity}: value`); control.title = 'Set a finite number. Press Enter or leave the field to apply.';
        control.disabled = options.readonly === true && node.exposed !== true;
        control.addEventListener('input', () => control.setCustomValidity(''));
        control.addEventListener('change', () => {
          const value = control.value.trim() ? control.valueAsNumber : NaN;
          if (!Number.isFinite(value)) { control.setCustomValidity('Enter a finite number.'); control.reportValidity(); return; }
          if (value !== node.value) options.onValue?.(node.id, value);
        });
        control.addEventListener('keydown', event => { if (event.key === 'Enter') { event.preventDefault(); control.blur(); } });
        body.append(control);
        if (options.readonly && !node.exposed) { control.title = 'Fixed by author'; kind.title = 'Number · fixed by author'; }
      }
      if (operation) {
        const mark = label('span', `ms-operation-mark ms-operation-${operation.family.toLowerCase()}`, operation.symbol); mark.setAttribute('aria-hidden', 'true'); mark.title = `${operation.label} · ${operation.inputs.map(port => port.type).join(', ')} → ${operation.output}`; body.append(mark);
      }
      if (!routePoint && ['input', 'number', 'output', 'switch', 'joint'].includes(node.kind)) {
        const value = label('span', `ms-state${signalType === 'number' ? ' ms-numeric-state' : ''}${node.kind === 'output' && signalType === 'number' ? ' ms-number-readout' : ''}${options.running && evaluated === true ? ' is-on' : ''}${options.running && typeof evaluated === 'number' ? ' is-number' : ''}${options.running && evaluated == null ? ' is-unknown' : ''}`, options.running ? valueText(evaluated) : 'Stopped');
        if (typeof evaluated === 'number') value.title = String(evaluated);
        value.setAttribute('aria-label', `${identity} signal: ${value.textContent}`); body.append(value);
      }
      if (options.evaluation?.errors[node.id]) { const error = label('span', 'ms-node-error', options.evaluation.errors[node.id]); error.title = options.evaluation.errors[node.id]; body.append(error); }
      if(displayNode.kind==='spreadsheet'&&asset&&!options.connecting)body.append(button('Open original spreadsheet ↗',`Open ${asset.name}`,()=>options.onAssetOpen?.(asset.id)));
      card.append(body);
    }
    // Rotate complete text blocks independently; Gallery items carry their own
    // inherited/overridden settings from the common Gallery renderer.
    for (const text of body.querySelectorAll<HTMLElement>('.ms-text,.ms-text-preview,.ms-content-link')) {
      if (!text.closest('.ms-gallery')) setTextRotation(text, style.textRotation);
    }
    if (!routePoint && !compact) for (const [index, port] of inputPorts(node).entries()) {
      const incoming = incomingPorts.get(node.id)?.get(index);
      const anchor = incoming?.targetAnchor ?? { x: 0, y: (index + 1) / (inputPorts(node).length + 1) };
      const position = boundary({ ...node, ...bounds, x: 0, y: 0 }, anchor), normalized = { x: position.x / bounds.width, y: position.y / bounds.height };
      const tag = label('span', `ms-port ms-port-${port.type}${normalized.x > 0.5 ? ' ms-port-right' : ''}`);
      Object.assign(tag.style, { left: `${normalized.x * 100}%`, top: `${normalized.y * 100}%` }); tag.title = `${identity} · input ${port.name} (${port.type})`;
      tag.setAttribute('aria-label', tag.title); tag.append(label('span', 'ms-port-label', port.name)); card.append(tag);
    }
    let beginResize: ((event: PointerEvent) => boolean) | undefined;
    if (!options.readonly && options.onResize && !options.connecting && authoringMode === 'select' && node.kind !== 'joint' && !routePoint && (!compact || isEnclosingContainer(node)) && !isOrderedContainer(nodesById.get(node.parentId??'')??node)&&!windowParents(navigation,node.id).length) {
      const backdrop = nodeShape(node, bounds), edgeHit = (event: PointerEvent) => {
        const cardBounds = card.getBoundingClientRect();
        return nearestBoundary({ ...backdrop, x: 0, y: 0 }, { x: (event.clientX - cardBounds.left) / view.scale, y: (event.clientY - cardBounds.top) / view.scale });
      };
      // Keep a dependable screen-pixel target at every camera scale, without
      // allowing a tiny node's resize band to consume its entire text surface.
      const physical = shapeBounds(backdrop), edgeTolerance = () => Math.min(10 / view.scale, Math.min(physical.width, physical.height) / 4);
      const edgeTarget = (event: PointerEvent) => {
        const target = event.target instanceof Element ? event.target : null;
        return target?.closest('.ms-node') === card && !target.closest('.mdv-viewer,button,input,textarea,select,a,video,audio');
      };
      const edgeCursor = (normal: Point) => Math.abs(normal.x) < .3 ? 'ns-resize' : Math.abs(normal.y) < .3 ? 'ew-resize' : normal.x * normal.y > 0 ? 'nwse-resize' : 'nesw-resize';
      card.addEventListener('pointermove', event => {
        const edge = edgeHit(event), anchoredEdge = (edge.normal.x < -.3 || edge.normal.y < -.3) && options.canMoveNodes?.(selectionRoots([node.id])) === false;
        const cursor = edgeTarget(event) && !anchoredEdge && selectedIds.has(node.id) && edge.distance <= edgeTolerance() ? edgeCursor(edge.normal) : '';
        card.style.cursor = cursor;
        if (cursor) { card.dataset.resizeCursor = cursor; card.style.setProperty('--ms-edge-cursor', cursor); }
        else { delete card.dataset.resizeCursor; card.style.removeProperty('--ms-edge-cursor'); }
      });
      card.addEventListener('pointerleave', () => { card.style.cursor = ''; delete card.dataset.resizeCursor; card.style.removeProperty('--ms-edge-cursor'); });
      beginResize = event => {
        const edge = edgeHit(event);
        if (event.button !== 0 || !edgeTarget(event) || !selectedIds.has(node.id) || edge.distance > edgeTolerance() || event.ctrlKey || event.metaKey || event.shiftKey) return false;
        if ((edge.normal.x < -.3 || edge.normal.y < -.3) && options.canMoveNodes?.(selectionRoots([node.id])) === false) return false;
        activeDrags.get(host)?.();
        event.stopPropagation(); event.preventDefault(); card.setPointerCapture(event.pointerId);
        const descendants = new Set(navigation.descendants(node.id).map(item => item.id));
        const obstacles = visible.filter(item => !isRoot(item) && item.id !== node.id && (trayScene?.isNodeVisible(item.id)??true) && !isIntentionalContainment(navigation, node.id, item.id)).map(item => nodeShape(item, renderedRect(item)));
        const startX = event.clientX, startY = event.clientY; let resized = { ...backdrop }, dragged = false;
        const move = (next: PointerEvent) => {
          if (next.pointerId !== event.pointerId) return;
          const dx = (next.clientX - startX) / view.scale, dy = (next.clientY - startY) / view.scale;
          dragged ||= Math.hypot(next.clientX - startX, next.clientY - startY) > 3;
          if (!dragged) return;
          lastNodeClick = undefined;
          const horizontal = Math.abs(edge.normal.x) >= .3, vertical = Math.abs(edge.normal.y) >= .3;
          const minimum = node.kind === 'junction' || node.kind === 'joint' ? 2 : node.parentId ? .001 : 1;
          const width = horizontal ? Math.max(minimum, bounds.width + (edge.normal.x < 0 ? -dx : dx)) : bounds.width;
          const height = vertical ? Math.max(minimum, bounds.height + (edge.normal.y < 0 ? -dy : dy)) : bounds.height;
          const x = horizontal && edge.normal.x < 0 ? bounds.x + bounds.width - width : bounds.x;
          const y = vertical && edge.normal.y < 0 ? bounds.y + bounds.height - height : bounds.y;
          // Resizing a left/top edge translates the owned branch with the new origin.
          const limited = constrainChildGeometry(project,node,{x,y,width,height},{children:id=>navigation.children.get(id)??[],context:{galleryItems:options.galleryItems}});
          resized = collisionFreeResize(backdrop, geometry => obstacles.map(obstacle => descendants.has(obstacle.id!) ? { ...obstacle, x: obstacle.x + geometry.x - backdrop.x, y: obstacle.y + geometry.y - backdrop.y } : obstacle), { ...backdrop, ...limited });
          Object.assign(card.style, { left: `${resized.x}px`, top: `${resized.y}px`, width: `${resized.width}px`, height: `${resized.height}px` }); refreshOutline(resized.width, resized.height);
          fitContainedContent(card,project,node,resized,sizingContext);
        };
        const cleanup = () => {
          card.removeEventListener('pointermove', move); card.removeEventListener('pointerup', end); card.removeEventListener('pointercancel', cancel); card.removeEventListener('lostpointercapture', cancel);
          window.removeEventListener('keydown', escape, true); window.removeEventListener('blur', cancel);
          if (activeDrags.get(host) === cancel) activeDrags.delete(host);
          if (card.hasPointerCapture(event.pointerId)) card.releasePointerCapture(event.pointerId);
        };
        const restore = () => { Object.assign(card.style, { left: `${bounds.x}px`, top: `${bounds.y}px`, width: `${bounds.width}px`, height: `${bounds.height}px` }); refreshOutline(bounds.width, bounds.height); fitContainedContent(card,project,node,bounds,sizingContext); };
        const end = (next: PointerEvent) => {
          if (next.pointerId !== event.pointerId) return;
          cleanup(); restore();
          if (resized.x !== bounds.x || resized.y !== bounds.y || resized.width !== bounds.width || resized.height !== bounds.height) options.onResize?.(node.id, resized.width, resized.height, resized.x, resized.y);
          else if (!dragged) selectOrDisclose(next, nearestAnchor(node, next, card));
        };
        const cancel = () => { lastNodeClick = undefined; cleanup(); restore(); };
        const escape = (next: KeyboardEvent) => { if (next.key === 'Escape') { next.preventDefault(); next.stopPropagation(); cancel(); } };
        activeDrags.set(host, cancel);
        card.addEventListener('pointermove', move); card.addEventListener('pointerup', end); card.addEventListener('pointercancel', cancel); card.addEventListener('lostpointercapture', cancel);
        window.addEventListener('keydown', escape, true); window.addEventListener('blur', cancel);
        return true;
      };
      // Capture before descendants: a native contenteditable surface otherwise
      // places a caret and starts passage selection before the card sees it.
      // Away from this narrow edge band the existing native text gesture wins.
      card.addEventListener('pointerdown', event => { beginResize?.(event); }, true);
    }
    const disclose = (collapsed = docAsset ? docOpen : !node.collapsed) => {
      if (!canDisclose || options.connecting || !options.onToggle) return;
      lastNodeClick = undefined;
      options.onToggle(node.id,'collapsed',collapsed);
      const scope = host.isConnected ? host : document;
      [...scope.querySelectorAll<HTMLElement>('.ms-node')].find(element => element.dataset.nodeId === node.id)?.focus({ preventScroll: true });
    };
    let clickedGalleryItem:string|undefined;
    const selectOrDisclose = (event:PointerEvent,anchor?:Point) => {
      if (!options.connecting && (event.ctrlKey || event.metaKey || event.shiftKey)) {
        lastNodeClick = undefined; toggleSelection(node.id, anchor); return;
      }
      if (clusterButton && !options.connecting) { disclose(false); return; }
      const now=performance.now(),previous=lastNodeClick;
      if (!options.connecting && previous?.id === node.id && now - previous.time < 500 && Math.hypot(event.clientX - previous.x, event.clientY - previous.y) < 7) {
        if (canDisclose) { disclose(); return; }
      }
      lastNodeClick={id:node.id,time:now,x:event.clientX,y:event.clientY};if(clickedGalleryItem&&options.onGalleryInspect)options.onGalleryInspect(clickedGalleryItem);else selectNodes([node.id],anchor);
    };
    if(canDisclose){card.setAttribute('aria-expanded',String(docAsset ? docOpen : !node.collapsed));card.setAttribute('aria-keyshortcuts','Alt+ArrowRight Alt+ArrowLeft');}
    if (clusterButton) card.addEventListener('click', event => {
      // Assistive technologies may activate a role=button with a click instead
      // of pointer events. Native pointer releases already own drag/disclosure.
      if (event.detail !== 0 || !navigating || moduleMode || options.connecting || event.ctrlKey || event.metaKey || event.shiftKey) return;
      event.preventDefault(); event.stopPropagation(); disclose(false);
    });
    card.addEventListener('dblclick',event=>{
      if (textMode || (event.target as Element).closest('[contenteditable="true"]')) return;
      if((event.target as Element).closest('button,input,textarea,select,a,video'))return;
      event.preventDefault();event.stopPropagation();
      // Pointer releases own disclosure across DOM replacements. Native dblclick
      // can also follow a drag that returned to its starting point; do not replay it.
    });
    const beginNodePointer = (event: PointerEvent) => {
      const rawTarget = event.target as Element;
      if (rawTarget.closest('.mdv-viewer')) { event.stopPropagation(); lastNodeClick = undefined; return; }
      // Native editing deliberately owns pointer defaults: capture would move
      // the tile and prevent drag-selection, while selecting via the usual host
      // callback would replace the text DOM before the caret can be placed.
      if (rawTarget.closest('[contenteditable="true"]')) { lastNodeClick = undefined; event.stopPropagation(); return; }
      let editing = event.button === 0 && !event.ctrlKey && !event.metaKey && !event.shiftKey ? editableTextTarget(rawTarget) : undefined;
      if (!editing && textMode && event.button === 0 && !event.ctrlKey && !event.metaKey && !event.shiftKey && !node.title && !block && !routePoint && !hidesOwnContent(node) && !rawTarget.closest('button,input,textarea,select,a,video,audio') && options.onTextEdit && options.canEditText?.(node.id, true) !== false) {
        // Empty titles need no permanent strip. The Text tool creates the
        // canonical surface only when the user explicitly clicks to write.
        header.replaceChildren(title); card.insertBefore(header, card.querySelector(':scope > .ms-node-body'));
        title.style.display = 'block'; title.style.minHeight = '1.25em'; editing = title;
      }
      if (editing) {
        event.stopPropagation(); lastNodeClick = undefined; activeDrags.get(host)?.();
        selectedIds = new Set([editing.dataset.editableNodeId!]); highlightSelection(selectedIds);
        options.onTextEdit?.(editing.dataset.editableNodeId!, editing.dataset.editableText === 'title', editing, event);
        if (!node.title && editing === title) {
          // The original blank-card pointer default would refocus the card
          // after this handler and discard the newly created title's caret.
          event.preventDefault(); title.focus({ preventScroll: true });
          const selection = document.getSelection(); if (selection) { const range = document.createRange(); range.selectNodeContents(title); range.collapse(false); selection.removeAllRanges(); selection.addRange(range); }
        }
        return;
      }
      if (textMode) { event.preventDefault(); event.stopPropagation(); lastNodeClick = undefined; return; }
      if (rawTarget.closest('button,input,textarea,select,a,video')) return;
      event.stopPropagation(); if (event.button !== 0) return;
      // Single Select drags the object from its title and body as well as its
      // backdrop; native text selection is reserved for the Text tool.
      if (!options.readonly) event.preventDefault();
      clickedGalleryItem=rawTarget.closest<HTMLElement>('.ms-gallery-item')?.dataset.galleryItemId;
      const anchor = nearestAnchor(node, event, card);
      const pastThreshold = (next: PointerEvent) => Math.hypot(next.clientX - event.clientX, next.clientY - event.clientY) > 3;
      // All selection waits for release: callers may rebuild the entire scene.
      activeDrags.get(host)?.();
      const orderedChild = !routePoint && isOrderedContainer(nodesById.get(node.parentId ?? '') ?? node) && node.parentId !== canvas?.rootId;
      const groupSelection = options.onMoveSelection && selectedIds.has(node.id) ? [...selectedIds] : [node.id];
      const roots = selectionRoots(groupSelection), movable = options.canMoveNodes?.(roots) !== false;
      if (options.connecting || options.readonly || !movable || event.ctrlKey || event.metaKey || event.shiftKey || !routePoint && (orderedChild && !options.onNestSelection || windowParents(navigation,node.id).length>0)) {
        card.setPointerCapture(event.pointerId);
        let dragged = false;
        const move = (next: PointerEvent) => { if (next.pointerId === event.pointerId) dragged ||= pastThreshold(next); };
        const cleanup = () => {
          card.removeEventListener('pointermove', move); card.removeEventListener('pointerup', select);
          card.removeEventListener('pointercancel', cancel); card.removeEventListener('lostpointercapture', cancel);
          window.removeEventListener('keydown', escape, true); window.removeEventListener('blur', cancel);
          if (activeDrags.get(host) === cleanup) activeDrags.delete(host);
          if (card.hasPointerCapture(event.pointerId)) card.releasePointerCapture(event.pointerId);
        };
        const select = (next: PointerEvent) => {
          if (next.pointerId !== event.pointerId) return;
          cleanup(); if (!dragged) selectOrDisclose(next,anchor); else lastNodeClick = undefined;
        };
        const cancel = () => { lastNodeClick = undefined; cleanup(); };
        const escape = (next: KeyboardEvent) => { if (next.key === 'Escape') { next.preventDefault(); next.stopPropagation(); cancel(); } };
        activeDrags.set(host, cleanup);
        card.addEventListener('pointermove', move); card.addEventListener('pointerup', select);
        card.addEventListener('pointercancel', cancel); card.addEventListener('lostpointercapture', cancel);
        window.addEventListener('keydown', escape, true); window.addEventListener('blur', cancel);
        return;
      }
      card.setPointerCapture(event.pointerId);
      const movingIds = new Set<string>(), pending = [...roots];
      while (pending.length) {
        const id = pending.pop()!; if (movingIds.has(id)) continue;
        movingIds.add(id); pending.push(...(children.get(id) ?? []));
      }
      const candidates = visible.filter(candidate => !isRoot(candidate) && !movingIds.has(candidate.id) && (routePoint ? windowNodeVisible(candidate) : !windowParents(navigation,candidate.id).length&&(trayScene?.isNodeVisible(candidate.id)??true))).flatMap(candidate => collisionNodeShapes(project, candidate, routePoint ? screenRect(candidate) : renderedRect(candidate)));
      // A branch cannot align to its own structural ancestors. Evaluate all
      // selected roots, since one drag can move several independent branches.
      const snapAncestors = new Set(roots.flatMap(id => navigation.ancestors(id).map(parent => parent.id)));
      const snapCandidates = candidates.filter(candidate => !snapAncestors.has(candidate.id!) && !isIntentionalContainment(navigation, node.id, candidate.id!));
      const movingGeometry = visible.filter(candidate => !isRoot(candidate) && movingIds.has(candidate.id) && (routePoint ? windowNodeVisible(candidate) : !windowParents(navigation,candidate.id).length&&(trayScene?.isNodeVisible(candidate.id)??true))).flatMap(candidate => collisionNodeShapes(project, candidate, routePoint ? screenRect(candidate) : renderedRect(candidate)));
      const movingCards = [...movingIds].flatMap(id => { const element = cards.get(id),ancestor=element?.parentElement?.closest<HTMLElement>('.ms-node'); return element&&!(ancestor?.dataset.nodeId&&movingIds.has(ancestor.dataset.nodeId)) ? [element] : []; });
      const settings = resolveSnapping(canvas, node), rect = nodeShape(node, renderedRect(node));
      const startX = event.clientX, startY = event.clientY;
      let lastX = startX, lastY = startY, bypass = event.altKey, moved = false, dragged = false, position = { x: node.x, y: node.y };
      const receivingIds = options.onNestSelection && !routePoint ? nestingTargetOrder(project, roots) : [];
      const receivingChecks = new Map<string, boolean>();
      let receivingId: string | undefined;
      const nestingHint = label('span', 'ms-nesting-hint', '↳');
      nestingHint.setAttribute('role', 'status'); nestingHint.hidden = true; viewport.append(nestingHint);
      const showReceiver = (id?: string) => {
        if (receivingId !== id) {
          if (receivingId) cards.get(receivingId)?.classList.remove('ms-drop-receiver');
          receivingId = id;
          if (id) cards.get(id)?.classList.add('ms-drop-receiver');
        }
        nestingHint.hidden = !id;
        if (id) {
          const receiver = nodesById.get(id), title = `Release to nest inside ${receiver ? nodeTag(receiver) : 'this node'}`;
          nestingHint.title = title; nestingHint.setAttribute('aria-label', title); nestingHint.dataset.dropParent = id;
          const viewportBox = viewport.getBoundingClientRect();
          nestingHint.style.left = `${lastX - viewportBox.left + 16}px`; nestingHint.style.top = `${lastY - viewportBox.top + 14}px`;
        } else delete nestingHint.dataset.dropParent;
      };
      const receiverAtPointer = () => {
        const viewportBox = viewport.getBoundingClientRect();
        const point = { x: (lastX - viewportBox.left - view.x) / view.scale, y: (lastY - viewportBox.top - view.y) / view.scale, width: .001, height: .001 };
        for (const id of receivingIds) {
          const target = nodesById.get(id)!;
          if (!windowNodeVisible(target)) continue;
          if (!shapesOverlap(nodeShape(target, renderedRect(target)), point)) continue;
          if (!receivingChecks.has(id)) receivingChecks.set(id, canNestNodeBranches(project, roots, id));
          if (receivingChecks.get(id)) return id;
        }
        return undefined;
      };
      const preview = () => {
        if (!moved) {
          showReceiver();
          position = { x: node.x, y: node.y };
          for (const element of movingCards) element.style.transform = '';
          updateRoutes();
          guidesLayer.replaceChildren();
          return;
        }
        // Test the unrounded pointer position so capture tolerance stays in screen pixels.
        const raw = { ...rect, x: node.x + (lastX - startX) / view.scale, y: node.y + (lastY - startY) / view.scale };
        const scroll = routePoint ? windowOffset(navigation, node.id, windowOffsets) : { x: 0, y: 0 };
        const screenRaw = { ...raw, x: raw.x - scroll.x, y: raw.y - scroll.y };
        showReceiver(receiverAtPointer());
        const receivingBranch = receivingId ? new Set([receivingId, ...navigation.descendants(receivingId).map(child => child.id)]) : undefined;
        const snapped = bypass || receivingId ? { ...screenRaw, guides: [] } : snapPosition(screenRaw, snapCandidates, settings, { scale: view.scale });
        const result = { ...snapped, x: snapped.x + scroll.x, y: snapped.y + scroll.y };
        // Preserve exact captured edges; round only unconstrained axes as the editor did before.
        const coordinate = (axis: 'x' | 'y') => !bypass && (settings.snapToGrid || result.guides.length) ? result[axis] : Math.round(result[axis]);
        position = { x: coordinate('x'), y: coordinate('y') };
        const free = collisionFreeTranslation(movingGeometry, candidates, { x: position.x - node.x, y: position.y - node.y }, (a, b) => !!receivingBranch?.has(b.id!) || isIntentionalContainment(navigation, a.id!, b.id!));
        position = { x: node.x + free.x, y: node.y + free.y };
        const offset = { ids: movingIds, x: position.x - node.x, y: position.y - node.y };
        for (const element of movingCards) element.style.transform = `translate(${offset.x}px,${offset.y}px)`;
        updateRoutes(offset);
        // Show only guides that still describe the final collision-constrained
        // rectangle. A correction on one axis must not erase a valid other axis.
        drawGuides(bypass || receivingId ? [] : alignmentGuides({ ...rect, x: position.x - scroll.x, y: position.y - scroll.y }, snapCandidates, settings, { scale: view.scale }));
      };
      const move = (next: PointerEvent) => {
        if (next.pointerId !== event.pointerId) return;
        lastX = next.clientX; lastY = next.clientY; bypass = next.altKey;
        moved = pastThreshold(next);
        if(moved){dragged=true;lastNodeClick=undefined;}
        preview();
      };
      const modifier = (next: KeyboardEvent) => {
        if (next.key === 'Escape') { next.preventDefault(); next.stopPropagation(); lastNodeClick = undefined; cleanup(); }
        else if (next.key === 'Alt') { bypass = next.type === 'keydown'; preview(); }
      };
      const cleanup = () => {
        card.removeEventListener('pointermove', move); card.removeEventListener('pointerup', end);
        card.removeEventListener('pointercancel', cancel); card.removeEventListener('lostpointercapture', cancel);
        window.removeEventListener('keydown', modifier, true); window.removeEventListener('keyup', modifier); window.removeEventListener('blur', cleanup);
        for (const element of movingCards) element.style.transform = '';
        updateRoutes();
        guidesLayer.replaceChildren();
        if (receivingId) cards.get(receivingId)?.classList.remove('ms-drop-receiver');
        nestingHint.remove();
        if (activeDrags.get(host) === cleanup) activeDrags.delete(host);
        if (card.hasPointerCapture(event.pointerId)) card.releasePointerCapture(event.pointerId);
      };
      const end = (next: PointerEvent) => {
        if (next.pointerId !== event.pointerId) return;
        const parentId = receivingId;
        cleanup();
        if (dragged) {
          lastNodeClick = undefined;
          if (!selectedIds.has(node.id)) selectNodes([node.id], anchor);
          if (moved && parentId && options.onNestSelection) {
            options.onNestSelection(roots, parentId, position.x - node.x, position.y - node.y);
          } else if (moved && !orderedChild && (position.x !== node.x || position.y !== node.y)) {
            if (options.onMoveSelection) options.onMoveSelection(roots, position.x - node.x, position.y - node.y);
            else for (const id of roots) { const movedNode = nodesById.get(id)!; options.onMove?.(id, movedNode.x + position.x - node.x, movedNode.y + position.y - node.y); }
          }
        }
        else selectOrDisclose(next,anchor);
      };
      const cancel = (next: PointerEvent) => { if (next.pointerId === event.pointerId) {lastNodeClick=undefined;cleanup();} };
      activeDrags.set(host, cleanup);
      card.addEventListener('pointermove', move); card.addEventListener('pointerup', end); card.addEventListener('pointercancel', cancel); card.addEventListener('lostpointercapture', cancel);
      window.addEventListener('keydown', modifier, true); window.addEventListener('keyup', modifier); window.addEventListener('blur', cleanup);
    };
    card.addEventListener('pointerdown', event => beginNodePointer(event));
    card.addEventListener('keydown', event => {
      if (event.target !== card) return;
      if (textMode) return;
      if (moduleMode) {
        if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); event.stopPropagation(); options.onModuleSelect?.(node.id); }
        return;
      }
      const target = targetAt({ x: bounds.x + bounds.width / 2, y: bounds.y + bounds.height / 2 }, { nodeId: node.id });
      if (menuKey(event) && options.onContextMenu && !options.readonly) { event.preventDefault(); event.stopPropagation(); options.onContextMenu(target); return; }
      if (!navigating) {
        if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); event.stopPropagation(); lastNodeClick = undefined; options.onAuthorClick?.(target); }
        return;
      }
      if(event.altKey&&canDisclose&&['ArrowLeft','ArrowRight'].includes(event.key)){event.preventDefault();disclose(event.key==='ArrowLeft');return;}
      if (event.key === 'Enter' || event.key === ' ') {
        event.preventDefault(); lastNodeClick = undefined;
        if (!options.connecting && (event.ctrlKey || event.metaKey || event.shiftKey)) toggleSelection(node.id);
        else if (clusterButton && !options.connecting) disclose(false);
        else selectNodes([node.id]);
      }
      if (!options.readonly && !options.connecting && ['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'].includes(event.key)) {
        event.preventDefault(); const step = event.shiftKey ? 20 : 5;
        const dx = event.key === 'ArrowLeft' ? -step : event.key === 'ArrowRight' ? step : 0;
        const dy = event.key === 'ArrowUp' ? -step : event.key === 'ArrowDown' ? step : 0;
        moveSelectionSafely(selectedIds.has(node.id) ? [...selectedIds] : [node.id], dx, dy);
      }
    });
    world.append(card);
    if (!hidesOwnContent(node) && !isGallery(node) && node.childLayout !== 'spatial' && childCount > 0 && !node.collapsed && displayNode.text && !['image', 'document'].includes(displayNode.kind)) {
      const firstChildY = Math.min(...(navigation.children.get(node.id) ?? []).map(child => child.y));
      const summaryHeight = automatic?nodeContentLayout(project,node,sizingContext).bodyHeight:Math.max(0, firstChildY - node.y - header.offsetHeight - 10);
      body.classList.add('ms-contained-summary'); body.style.maxHeight = `${summaryHeight}px`;
      if (!automatic && summaryHeight < 24) body.style.visibility = 'hidden';
    }
    fitContainedContent(card,project,node,bounds,sizingContext,contentPass);
  }
  for (const parent of visible.filter(node => isGallery(node) && !isEmbeddedGallery(node) && !node.collapsed)) {
    const activeId = (options.galleryItems ?? Object.fromEntries(localGalleryItems))[parent.id];
    const { items, item, index } = gallerySelection(project, parent, activeId); if (!item) continue;
    const bounds = renderedRect(item), controls = label('div', 'ms-external-gallery-navigation'); controls.dataset.externalGallery = parent.id;
    controls.setAttribute('role', 'group'); controls.setAttribute('aria-label', `${nodeTag(parent)} gallery navigation`);
    const navigationBounds=externalGalleryNavigationBounds(project,parent,item,{context:sizingContext,children:id=>navigation.children.get(id)??[]},bounds);
    Object.assign(controls.style, { left: `${navigationBounds.x}px`, top: `${navigationBounds.y}px`, width: `${navigationBounds.width/navigationBounds.scale}px`, height: `${navigationBounds.height/navigationBounds.scale}px`, transform:`scale(${navigationBounds.scale})`, transformOrigin:'top left' });
    const change = (direction: -1 | 1) => {
      const id = galleryStep(project, parent, activeId, direction); if (!id) return;
      localGalleryItems.set(parent.id, id);
      if (options.onGalleryPage) options.onGalleryPage(parent.id, id); else renderScene(host, project, canvasId, { ...options, view });
    };
    const previous = button('←', `Previous object in ${nodeTag(parent)}`, () => change(-1)); previous.disabled = index <= 0;
    const next = button('→', `Next object in ${nodeTag(parent)}`, () => change(1)); next.disabled = index >= items.length - 1;
    const position = label('span', 'ms-gallery-position-sr', `${index + 1} of ${items.length}`); position.setAttribute('aria-live','polite');
    controls.append(previous, position, next); world.append(controls);
  }
  mountWindowSurfaces(project,navigation,cards,windowOffsets,(id,at)=>{if(!trayGeometryPreview)options.onWindowScroll?.(id,at);updateRoutes();refreshModuleOutlines();},()=>view.scale);
  trayScene=mountExpansionTrays({project,canvasId,context:sizingContext,world,cards,scale:()=>view.scale,
    elementBounds:element=>{const rect=element.getBoundingClientRect(),plane=viewport.getBoundingClientRect();return{x:(rect.left-plane.left-view.x)/view.scale,y:(rect.top-plane.top-view.y)/view.scale,width:rect.width/view.scale,height:rect.height/view.scale};},
    descendants:id=>navigation.descendants(id),movable:!options.readonly&&!options.connecting&&authoringMode==='select',canMove:options.canMoveNodes,onMove:options.onTrayMove,onResize:options.onTrayResize,
    onResizePreview:preview=>{
      if(preview&&!trayPreviewWindowOffsets)trayPreviewWindowOffsets=new Map([...windowOffsets].map(([id,at])=>[id,{...at}]));
      trayGeometryPreview=preview;trayPreviewIndex=preview?navigationIndex(preview,sizingContext):undefined;trayPreviewLinks=preview?new Map(preview.links.map(link=>[link.id,link])):undefined;
      if(!preview&&trayPreviewWindowOffsets){windowOffsets.clear();for(const[id,at]of trayPreviewWindowOffsets)windowOffsets.set(id,at);trayPreviewWindowOffsets=undefined;}
      previewTrayGeometry(preview??project,sizingContext,world,cards,outlinePreviews,windowOffsets);updateRoutes();refreshModuleOutlines();
    },
    onClose:!textMode&&options.onToggle?id=>options.onToggle?.(id,'collapsed',true):undefined,onPreview:updateRoutes,
    onSelect:!textMode?(id,additive)=>additive?toggleSelection(id):selectNodes([id]):undefined,isSelected:id=>selectedIds.has(id),
    beginGesture:cancel=>{activeDrags.get(host)?.();lastNodeClick=undefined;activeDrags.set(host,cancel);},
    endGesture:cancel=>{if(activeDrags.get(host)===cancel)activeDrags.delete(host);},});
  backgroundDisplay=mountBackgroundDisplay({host,viewport,world,ancestors:navigation.presentationAncestors});
  updateRoutes();
  viewport.addEventListener('pointerdown', event => {
    if (event.button !== 0 || (event.target !== viewport && event.target !== world && event.target !== linksLayer)) return;
    activeDrags.get(host)?.(); lastNodeClick = undefined;
    event.preventDefault(); viewport.focus({ preventScroll: true });
    if (!viewport.isConnected) return;
    viewport.setPointerCapture(event.pointerId);
    const region = !moduleMode && !options.connecting && (options.selectionMode === 'region' || event.shiftKey);
    const additive = event.shiftKey || event.ctrlKey || event.metaKey;
    const initialIds = new Set(selectedIds), rect = viewport.getBoundingClientRect();
    const marquee = region ? label('div', 'ms-selection-region') : undefined;
    if (marquee) { marquee.setAttribute('aria-hidden', 'true'); viewport.append(marquee); }
    viewport.classList.add(region ? 'ms-selecting-region' : 'ms-panning');
    const startX = event.clientX, startY = event.clientY, originalX = view.x, originalY = view.y;
    let moved = false, regionIds = new Set(additive ? initialIds : []);
    const move = (next: PointerEvent) => {
      if (next.pointerId !== event.pointerId) return;
      const dx = next.clientX - startX, dy = next.clientY - startY; moved ||= Math.hypot(dx, dy) > 3;
      if (!region) { view.x = originalX + dx; view.y = originalY + dy; updateView(); return; }
      const left = Math.min(startX, next.clientX) - rect.left, top = Math.min(startY, next.clientY) - rect.top;
      const width = Math.abs(dx), height = Math.abs(dy);
      Object.assign(marquee!.style, { left: `${left}px`, top: `${top}px`, width: `${width}px`, height: `${height}px` });
      const area = { x: (left - view.x) / view.scale, y: (top - view.y) / view.scale, width: width / view.scale, height: height / view.scale };
      regionIds = new Set(additive ? initialIds : []);
      if (moved) for (const node of visible) {
        if (isRoot(node)) continue;
        if(!windowNodeVisible(node))continue;const bounds = screenRect(node);
        const contained = bounds.x >= area.x && bounds.y >= area.y && bounds.x + bounds.width <= area.x + area.width && bounds.y + bounds.height <= area.y + area.height;
        const intersecting = bounds.x < area.x + area.width && bounds.x + bounds.width > area.x && bounds.y < area.y + area.height && bounds.y + bounds.height > area.y;
        if (options.regionIntersection === 'intersecting' ? intersecting : contained) regionIds.add(node.id);
      }
      highlightSelection(regionIds);
    };
    const cleanup = () => {
      viewport.removeEventListener('pointermove', move); viewport.removeEventListener('pointerup', end);
      viewport.removeEventListener('pointercancel', cancel); viewport.removeEventListener('lostpointercapture', cancel);
      window.removeEventListener('keydown', escape, true); window.removeEventListener('blur', cancel);
      viewport.classList.remove('ms-panning', 'ms-selecting-region'); marquee?.remove(); highlightSelection(selectedIds);
      if (activeDrags.get(host) === cancel) activeDrags.delete(host);
      if (viewport.hasPointerCapture(event.pointerId)) viewport.releasePointerCapture(event.pointerId);
    };
    const end = (next: PointerEvent) => {
      if (next.pointerId !== event.pointerId) return;
      cleanup();
      if (region && moved) selectNodes([...regionIds]);
      else if (!moved && !moduleMode) selectNodes([]);
    };
    const cancel = () => {
      cleanup();
      if (!region) { view.x = originalX; view.y = originalY; updateView(); }
    };
    const escape = (next: KeyboardEvent) => { if (next.key === 'Escape') { next.preventDefault(); next.stopPropagation(); cancel(); } };
    activeDrags.set(host, cancel);
    viewport.addEventListener('pointermove', move); viewport.addEventListener('pointerup', end);
    viewport.addEventListener('pointercancel', cancel); viewport.addEventListener('lostpointercapture', cancel);
    window.addEventListener('keydown', escape, true); window.addEventListener('blur', cancel);
  });
  viewport.addEventListener('wheel', event => {
    if ((event.target as Element).closest('.mdv-viewer,textarea,input,select')) return;
    if (!event.ctrlKey && (event.target as Element).closest('[contenteditable="true"]')) { event.stopPropagation(); return; }
    const text = (event.target as Element).closest<HTMLElement>('.ms-text,.ms-text-preview,.ms-caption');
    if (!event.ctrlKey && text) {
      const style = getComputedStyle(text);
      const scrollable = /auto|scroll/.test(style.overflowY) && text.scrollHeight > text.clientHeight + 1 ||
        /auto|scroll/.test(style.overflowX) && text.scrollWidth > text.clientWidth + 1;
      // Keep native reading scroll, including at either boundary. The same wheel
      // gesture must never jump from a passage into changing the canvas camera.
      if (scrollable) { event.stopPropagation(); return; }
    }
    event.preventDefault(); const rect = viewport.getBoundingClientRect(); const x = event.clientX - rect.left, y = event.clientY - rect.top;
    Object.assign(view, scaleCameraAt(view, { x, y }, Math.exp(-event.deltaY * 0.0013))); updateView();
  }, { passive: false });
  viewport.addEventListener('keydown', event => {
    if (event.target !== viewport) return;
    if (!moduleMode && !textMode && !options.readonly && !options.connecting && options.onMoveSelection && selectedIds.size && ['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'].includes(event.key)) {
      event.preventDefault(); const step = event.shiftKey ? 20 : 5;
      moveSelectionSafely([...selectedIds], event.key === 'ArrowLeft' ? -step : event.key === 'ArrowRight' ? step : 0, event.key === 'ArrowUp' ? -step : event.key === 'ArrowDown' ? step : 0);
      return;
    }
    if (event.key === '+' || event.key === '=') { event.preventDefault(); changeZoom(1.25); }
    else if (event.key === '-') { event.preventDefault(); changeZoom(0.8); }
    else if (event.key === '0') { event.preventDefault(); fitCanvas(); }
  });
  if (options.focusNodeId) fitCanvas(options.focusNodeId, options.focusBranch);
  else if (options.fit) fitCanvas(); else updateView();
  readability=mountNodeReadability({host,viewport,cards,getScale:()=>view.scale,enabled:()=>navigating&&!activeDrags.has(host)&&!viewport.querySelector('[contenteditable="true"]')});
  for (const [key, entry] of media) {
    if (!entry.used) { releasePrimaryMedia(entry); media.delete(key); }
    // Moving an existing native player out of the rebuilt scene may pause it.
    // Restore only its previous play intent; paused players remain paused.
    else if (entry.playing) void entry.element.play().catch(() => {});
  }
}

export function getAsset(project: MeridianProject, id: string): MeridianAsset | undefined {
  return project.assets.find(asset => asset.id === id) ?? Object.values(project.snapshots).flatMap(snapshot => [snapshot.asset, snapshot.backgroundAsset, ...snapshot.assets ?? []]).find(asset => asset?.id === id);
}

/** Raster/text previews are inert; verified PDFs use the browser's native viewer. */
export function openAssetPreview(project: MeridianProject, assetId: string): void {
  const asset = getAsset(project, assetId); if (!asset) return;
  const dialog = document.createElement('dialog'); dialog.className = 'ms-asset-dialog';
  dialog.setAttribute('aria-label', `Read ${asset.name}`);
  const top = label('header', 'ms-dialog-header'); top.append(label('strong', '', asset.name), button('×', 'Close attachment', () => dialog.close())); dialog.append(top);
  if (documentPreviewKind(asset)) {
    dialog.classList.add('ms-document-dialog');
    const reader = createDocumentViewer(asset); dialog.append(reader.element);
    dialog.addEventListener('close', () => { reader.dispose(); dialog.remove(); }, { once: true });
    document.body.append(dialog); dialog.showModal(); return;
  }
  let previewUrl: string | undefined;
  if (raster.test(asset.mime)) { const image = document.createElement('img'); image.src = `data:${asset.mime};base64,${asset.base64}`; image.alt = asset.name; dialog.append(image); }
  else if(/^video\/(mp4|webm|ogg)$/i.test(asset.mime)){const video=document.createElement('video');video.controls=true;video.preload='metadata';video.src=`data:${asset.mime};base64,${asset.base64}`;video.className='ms-video-preview';video.setAttribute('aria-label',asset.name);dialog.append(video);}
  else if(/^audio\/(mpeg|mp3|ogg|wav|wave|x-wav|webm|aac|mp4)$/i.test(asset.mime)){const audio=document.createElement('audio');audio.controls=true;audio.preload='metadata';audio.src=`data:${asset.mime};base64,${asset.base64}`;audio.setAttribute('aria-label',asset.name);dialog.append(audio);}
  else if (/^(text\/csv|text\/tab-separated-values)$/i.test(asset.mime)||/\.(csv|tsv)$/i.test(asset.name)) {try{dialog.append(csvPreview(asset));}catch{dialog.append(label('p','','Spreadsheet preview unavailable. Download the original.'));}}
  else if (/^(text\/plain|text\/markdown)$/i.test(asset.mime)) dialog.append(label('pre', 'ms-dialog-text', decodeTextAsset(asset)));
  else if (asset.mime === 'application/pdf') {
    try {
      const bytes = Uint8Array.from(atob(asset.base64), character => character.charCodeAt(0));
      if (new TextDecoder().decode(bytes.slice(0, 5)) !== '%PDF-') throw new Error('The attachment does not contain a recognized PDF header.');
      previewUrl = URL.createObjectURL(new Blob([bytes], { type: 'application/pdf' }));
      const preview = document.createElement('object'); preview.type = 'application/pdf'; preview.data = previewUrl; preview.className = 'ms-pdf-preview'; preview.setAttribute('aria-label', `PDF preview: ${asset.name}`);
      preview.append(label('p', '', 'PDF preview is unavailable in this browser. Download the attachment below to open it.'));
      dialog.append(preview, label('p', 'ms-preview-note', 'If your browser cannot preview this PDF, download the original below.'));
    } catch { dialog.append(label('p', 'ms-preview-note', 'This PDF cannot be previewed. Its original bytes remain available to download.')); }
  }
  else dialog.append(label('p', '', `${asset.mime === 'application/pdf' ? 'PDF document' : 'Attachment'} — download to open in your preferred viewer.`));
  const download = button('Download attachment', `Download ${asset.name}`, () => {
    try {
      const bytes = Uint8Array.from(atob(asset.base64), character => character.charCodeAt(0));
      const url = URL.createObjectURL(new Blob([bytes], { type: 'application/octet-stream' }));
      const anchor = document.createElement('a'); anchor.href = url; anchor.download = asset.name.replace(/[\\/:*?"<>|]/g, '_'); anchor.click(); setTimeout(() => URL.revokeObjectURL(url), 1000);
    } catch { dialog.append(label('p', 'ms-node-error', 'The attachment could not be decoded.')); }
  });
  dialog.append(download); dialog.addEventListener('close', () => { if (previewUrl) URL.revokeObjectURL(previewUrl); dialog.remove(); }, { once: true }); document.body.append(dialog); dialog.showModal();
}
