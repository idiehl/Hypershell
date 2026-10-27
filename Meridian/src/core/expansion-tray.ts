import type { MeridianNode, MeridianProject } from './types.ts';
import { navigationIndex } from './navigation.ts';
import { isEnclosingContainer, isOrderedContainer, gridGeometry } from './cells.ts';
import { isGallery } from './gallery.ts';
import { externalGalleryNavigationBounds, type SizingContext } from './sizing.ts';
import { collisionFreeResize, collisionFreeTranslation } from './collision.ts';
import { validateChange } from './rules.ts';
import { reflowContainedChildren } from './grid.ts';
import { EXPANSION_TRAY_HEADER, EXPANSION_TRAY_LEGACY_HEADER, EXPANSION_TRAY_PADDING, EXPANSION_TRAY_LEGACY_PADDING, EXPANSION_TRAY_MIN_WIDTH, EXPANSION_TRAY_MIN_HEIGHT, expansionTrayHeader, expansionTrayPadding, fixedExpansionTrayContent, fixedExpansionTrayGallery } from './expansion-tray-frame.ts';
export { EXPANSION_TRAY_HEADER, EXPANSION_TRAY_LEGACY_HEADER, EXPANSION_TRAY_PADDING, EXPANSION_TRAY_LEGACY_PADDING, EXPANSION_TRAY_MIN_WIDTH, EXPANSION_TRAY_MIN_HEIGHT } from './expansion-tray-frame.ts';

export interface ExpansionTrayBounds {
  sourceId: string; x: number; y: number; width: number; height: number;
  content: { x: number; y: number; width: number; height: number };
}
export function isExpansionTraySource(node: MeridianNode | undefined): boolean {
  return !!node?.expansionTray && !node.smartLink && node.kind !== 'canvas' && !isEnclosingContainer(node);
}
/** Closest tray boundary, excluding the node's own tray: its cover remains in its owner's cell. */
export function expansionTrayOwner(project: MeridianProject, nodeId: string): MeridianNode | undefined {
  return navigationIndex(project).ancestors(nodeId).find(isExpansionTraySource);
}
export function expansionTrayBounds(project: MeridianProject, sourceId: string, context: SizingContext = {}): ExpansionTrayBounds | undefined {
  const nav = navigationIndex(project, context), source = nav.nodes.get(sourceId);
  if (!source || !isExpansionTraySource(source)) return;
  const children = nav.children.get(source.id) ?? [];
  if (!children.length) return;
  const fixed = fixedExpansionTrayContent(source);
  const header = expansionTrayHeader(source), padding = expansionTrayPadding(source);
  if (fixed) return { sourceId, x: fixed.x - padding, y: fixed.y - header - padding,
    width: source.expansionTray!.size!.width, height: source.expansionTray!.size!.height, content: fixed };
  let boxes: Array<{ x: number; y: number; width: number; height: number }>;
  if (source.containerVariant === 'window' && source.grid) {
    const grid = gridGeometry(source, project);
    boxes = [{ x: grid.left, y: grid.top, width: grid.visibleColumns * grid.cellWidth, height: grid.visibleRows * grid.cellHeight }];
  } else {
    // Paging changes visibility, never the collection's common outer frame.
    const exposed = children;
    boxes = exposed.map(child => isGallery(source) ? nav.bounds(child) : nav.packingBranchBounds(child));
    if (isGallery(source)) boxes.push(...exposed.map(child => externalGalleryNavigationBounds(project, source, child, { context })));
  }
  const x = Math.min(...boxes.map(box => box.x)), y = Math.min(...boxes.map(box => box.y));
  const width = Math.max(...boxes.map(box => box.x + box.width)) - x, height = Math.max(...boxes.map(box => box.y + box.height)) - y;
  return { sourceId, x: x - padding, y: y - header - padding,
    width: width + 2 * padding, height: height + header + 2 * padding,
    content: { x, y, width, height } };
}
export function visibleExpansionTrays(project: MeridianProject, context: SizingContext = {}): ExpansionTrayBounds[] {
  const nav = navigationIndex(project, context);
  return project.nodes.filter(source => isExpansionTraySource(source) && !source.collapsed && !nav.ancestors(source.id).some(parent => parent.collapsed))
    .filter(source => {
      // Embedded Gallery items have no separate cover card, but an active item's tray remains visible.
      for (const gallery of nav.ancestors(source.id).filter(isGallery)) {
        const children = nav.children.get(gallery.id) ?? [], active = context.galleryItems;
        const selected = active && typeof (active as ReadonlyMap<string, string>).get === 'function' ? (active as ReadonlyMap<string, string>).get(gallery.id)
          : (active as Readonly<Record<string, string>> | undefined)?.[gallery.id];
        const chosen = children.find(child => child.id === selected) ?? children[0];
        const item = source.parentId === gallery.id ? source : nav.ancestors(source.id).find(parent => parent.parentId === gallery.id);
        if (item?.id !== chosen?.id) return false;
      }
      return true;
    }).flatMap(source => expansionTrayBounds(project, source.id, context) ?? []);
}
/** Initialize only newly disclosed exterior branches. Call on an editing clone. */
export function prepareExpansionTrays(project: MeridianProject): string[] {
  const nav = navigationIndex(project), created: string[] = [];
  for (const node of project.nodes) {
    if (node.kind === 'canvas' || node.smartLink || isEnclosingContainer(node) || node.collapsed || node.expansionTray || !(nav.children.get(node.id)?.length)) continue;
    if (nav.ancestors(node.id).some(parent => parent.collapsed)) continue;
    if (!nav.ancestors(node.id).some(parent => !parent.collapsed && (isEnclosingContainer(parent) || isOrderedContainer(parent) || isGallery(parent)))) continue;
    node.expansionTray = { offset: { x: node.width + 80, y: 0 }, layoutVersion: 3 }; created.push(node.id);
  }
  return created;
}
function translateContents(project: MeridianProject, source: MeridianNode, dx: number, dy: number): void {
  const ids = new Set(navigationIndex(project).descendants(source.id).map(node => node.id));
  for (const node of project.nodes) if (ids.has(node.id)) { node.x += dx; node.y += dy; }
  for (const link of project.links) if (ids.has(link.sourceId) && ids.has(link.targetId) && link.points)
    link.points = link.points.map(point => ({ x: point.x + dx, y: point.y + dy }));
  source.expansionTray!.offset.x += dx; source.expansionTray!.offset.y += dy;
}
/** Place a freshly laid-out tray outside all surrounding bounded workspaces. */
export function placeExpansionTrays(project: MeridianProject, sourceIds: readonly string[]): MeridianProject {
  for (const sourceId of sourceIds) {
    const nav = navigationIndex(project), source = nav.nodes.get(sourceId), frame = expansionTrayBounds(project, sourceId);
    if (!source || !frame) continue;
    const excluded = new Set(nav.descendants(source.id).map(node => node.id));
    const obstacles = project.nodes.filter(node => node.canvasId === source.canvasId && node.kind !== 'canvas' && !excluded.has(node.id) && nav.isNodeVisible(node.id)).map(node => nav.bounds(node));
    obstacles.push(...visibleExpansionTrays(project).filter(tray => tray.sourceId !== sourceId && !excluded.has(tray.sourceId) && nav.nodes.get(tray.sourceId)?.canvasId === source.canvasId).map(tray => ({ id: tray.sourceId, ...tray })));
    const enclosing = nav.ancestors(sourceId).filter(isEnclosingContainer), owner = enclosing.at(-1), boundary = owner ? nav.bounds(owner) : nav.bounds(source);
    const candidates = [
      { x: boundary.x + boundary.width + 40, y: boundary.y },
      { x: boundary.x, y: boundary.y + boundary.height + 40 },
      { x: boundary.x - frame.width - 40, y: boundary.y },
      { x: boundary.x, y: boundary.y - frame.height - 40 },
    ];
    let destination = candidates[0];
    for (const at of candidates) {
      const delta = collisionFreeTranslation([{ ...frame, x: at.x, y: at.y }], obstacles, { x: 0, y: 0 });
      destination = { x: at.x + delta.x, y: at.y + delta.y };
      if (!delta.x && !delta.y) break;
    }
    const dx = destination.x - frame.x, dy = destination.y - frame.y;
    // A Freeform manual composition may start anywhere; bind its canonical layout origin once.
    source.expansionTray!.offset = { x: frame.content.x - source.x, y: frame.content.y - source.y };
    translateContents(project, source, dx, dy);
  }
  return project;
}
/** Cell allocation can move a source relative to its enclosure. Preserve any
 * valid authored tray position; repair only a frame that now intersects a peer
 * or an enclosing workspace, moving its real contents together. */
export function repairExpansionTrayCollisions(project: MeridianProject): MeridianProject {
  for (const initial of visibleExpansionTrays(project)) {
    const nav = navigationIndex(project), source = nav.nodes.get(initial.sourceId)!, frame = expansionTrayBounds(project, source.id)!;
    const excluded = new Set(nav.descendants(source.id).map(node => node.id));
    const obstacles = project.nodes.filter(node => node.canvasId === source.canvasId && node.kind !== 'canvas' && !excluded.has(node.id) && nav.isNodeVisible(node.id)).map(node => nav.bounds(node));
    obstacles.push(...visibleExpansionTrays(project).filter(tray => tray.sourceId !== source.id && !excluded.has(tray.sourceId) && nav.nodes.get(tray.sourceId)?.canvasId === source.canvasId).map(tray => ({ id: tray.sourceId, ...tray })));
    const delta = collisionFreeTranslation([frame], obstacles, { x: 0, y: 0 });
    if (Math.abs(delta.x) > 1e-7 || Math.abs(delta.y) > 1e-7) translateContents(project, source, delta.x, delta.y);
  }
  return project;
}
export interface TrayMoveOptions { snapDistance?: number; bypassSnap?: boolean }
export function moveExpansionTray(project: MeridianProject, sourceId: string, dx: number, dy: number, options: TrayMoveOptions = {}): MeridianProject {
  if (![dx, dy].every(Number.isFinite)) throw new Error('Tray movement must be finite.');
  if (options.snapDistance !== undefined && (!Number.isFinite(options.snapDistance) || options.snapDistance < 0)) throw new Error('Tray snap distance must be a nonnegative finite number.');
  const candidate = structuredClone(project);
  const migrated = migrateExpansionTrayMetrics(candidate);
  for (const id of migrated) Object.assign(candidate, reflowContainedChildren(candidate, id));
  const nav = navigationIndex(candidate), source = nav.nodes.get(sourceId), frame = expansionTrayBounds(candidate, sourceId);
  if (!source || !frame || !isExpansionTraySource(source)) throw new Error('Choose an existing expansion tray.');
  const excluded = new Set(nav.descendants(sourceId).map(node => node.id));
  if (nav.descendants(sourceId).some(node => node.positionLocked)) throw new Error('Unlock the pinned contents before moving this expansion tray.');
  const obstacles = candidate.nodes.filter(node => node.canvasId === source.canvasId && node.kind !== 'canvas' && !excluded.has(node.id) && nav.isNodeVisible(node.id)).map(node => nav.bounds(node));
  obstacles.push(...visibleExpansionTrays(candidate).filter(tray => tray.sourceId !== sourceId && !excluded.has(tray.sourceId) && nav.nodes.get(tray.sourceId)?.canvasId === source.canvasId).map(tray => ({ id: tray.sourceId, ...tray })));
  let proposed = { x: dx, y: dy };
  if (!options.bypassSnap && options.snapDistance) {
    const owner = nav.ancestors(sourceId).filter(isEnclosingContainer).at(-1);
    if (owner) {
      const boundary = nav.bounds(owner), at = { x: frame.x + dx, y: frame.y + dy }, seamX = Math.min(at.x + frame.width, boundary.x + boundary.width) - Math.max(at.x, boundary.x),
        seamY = Math.min(at.y + frame.height, boundary.y + boundary.height) - Math.max(at.y, boundary.y);
      const choices = [
        { x: boundary.x + boundary.width - frame.x, y: dy, distance: Math.abs(at.x - boundary.x - boundary.width), seam: seamY },
        { x: dx, y: boundary.y + boundary.height - frame.y, distance: Math.abs(at.y - boundary.y - boundary.height), seam: seamX },
        { x: boundary.x - frame.width - frame.x, y: dy, distance: Math.abs(at.x + frame.width - boundary.x), seam: seamY },
        { x: dx, y: boundary.y - frame.height - frame.y, distance: Math.abs(at.y + frame.height - boundary.y), seam: seamX },
      ].filter(choice => choice.distance <= options.snapDistance! && choice.seam > 1e-7).sort((a, b) => a.distance - b.distance);
      if (choices[0]) proposed = { x: choices[0].x, y: choices[0].y };
    }
  }
  const delta = collisionFreeTranslation([frame], obstacles, proposed);
  translateContents(candidate, source, delta.x, delta.y);
  const issues = validateChange(project, candidate); if (issues.length) throw new Error(issues.map(issue => issue.message).join(' '));
  return candidate;
}

type Frame = { x: number; y: number; width: number; height: number };
/** Transform one presentation scope; nested trays are detached scopes whose
 * original contents translate with their source rather than being distorted. */
function transformTrayContents(project: MeridianProject, source: MeridianNode, from: Frame, to: Frame, scaleModes: boolean, rootIds?: readonly string[]): void {
  const nav = navigationIndex(project), all = nav.descendants(source.id), selected = rootIds ? new Set(rootIds.flatMap(id => [id, ...nav.descendants(id).map(node => node.id)])) : new Set(all.map(node => node.id));
  const sx = to.width / Math.max(Number.MIN_VALUE, from.width), sy = to.height / Math.max(Number.MIN_VALUE, from.height);
  const transform = (point: { x: number; y: number }) => ({ x: to.x + (point.x - from.x) * sx, y: to.y + (point.y - from.y) * sy });
  const nested = all.filter(node => selected.has(node.id) && isExpansionTraySource(node) && !nav.ancestors(node.id).some(parent => parent.id !== source.id && selected.has(parent.id) && isExpansionTraySource(parent)));
  const escaped = new Set(nested.flatMap(node => nav.descendants(node.id).map(child => child.id)));
  const scaled = all.filter(node => selected.has(node.id) && !escaped.has(node.id)), before = new Map(scaled.map(node => [node.id,
    rootIds?.includes(node.id) && isGallery(source) ? { x: node.x, y: node.y, width: node.width, height: node.height } : { ...nav.bounds(node) }]));
  for (const node of scaled) {
    const original = before.get(node.id)!, at = transform(original);
    node.x = at.x; node.y = at.y; node.width = Math.max(Number.MIN_VALUE, original.width * sx); node.height = Math.max(Number.MIN_VALUE, original.height * sy); node.sizeMode = 'fixed';
    if (scaleModes && node.containerSizes) for (const mode of ['expanded', 'collapsed'] as const) {
      const size = node.containerSizes[mode]; if (size) node.containerSizes[mode] = { ...size, width: size.width * sx, height: size.height * sy };
    }
  }
  const translations = new Map<string, { x: number; y: number }>();
  for (const owner of nested) {
    const original = before.get(owner.id)!, delta = { x: owner.x - original.x, y: owner.y - original.y };
    for (const node of nav.descendants(owner.id)) { node.x += delta.x; node.y += delta.y; translations.set(node.id, delta); }
    translations.set(owner.id, delta);
  }
  const scaledIds = new Set(scaled.map(node => node.id));
  for (const link of project.links) if (link.points && selected.has(link.sourceId) && selected.has(link.targetId)) {
    const a = translations.get(link.sourceId), b = translations.get(link.targetId);
    if (a && b && a.x === b.x && a.y === b.y) link.points = link.points.map(point => ({ x: point.x + a.x, y: point.y + a.y }));
    else if (scaledIds.has(link.sourceId) && scaledIds.has(link.targetId)) link.points = link.points.map(transform);
  }
}

/** Allocate the same outer card frame to every exterior Gallery object. Its
 * original subtree and routes follow its own affine change; saved mode frames
 * and authored shapes/typography remain untouched. */
export function fitGalleryItemsToSlot(project: MeridianProject, sourceId: string, slot: Frame): void {
  const source = project.nodes.find(node => node.id === sourceId); if (!source) return;
  for (const child of project.nodes.filter(node => node.parentId === sourceId && node.canvasId === source.canvasId)) {
    const from = { x: child.x, y: child.y, width: child.width, height: child.height };
    if (Math.abs(from.x - slot.x) + Math.abs(from.y - slot.y) + Math.abs(from.width - slot.width) + Math.abs(from.height - slot.height) < 1e-8) continue;
    transformTrayContents(project, source, from, slot, false, [child.id]);
  }
}

/** Fixed legacy frames keep their exact authored outer box as retired header
 * space and the old 20px inset become available to their original contents.
 * Automatic frames tighten their margins around unchanged content. */
export function migrateExpansionTrayMetrics(project: MeridianProject): string[] {
  const migrated: string[] = [];
  for (const source of project.nodes) {
    if (!isExpansionTraySource(source) || source.expansionTray!.layoutVersion === 3) continue;
    const previous = source.expansionTray!, content = fixedExpansionTrayContent(source);
    if (content) {
      const inset = expansionTrayPadding(source) - EXPANSION_TRAY_PADDING, top = expansionTrayHeader(source) + inset;
      const target = { ...content, x: content.x - inset, y: content.y - top, width: content.width + 2 * inset, height: content.height + top + inset };
      transformTrayContents(project, source, content, target, false);
      source.expansionTray = { ...previous, offset: { x: previous.offset.x - inset, y: previous.offset.y - top }, layoutVersion: 3 };
    } else source.expansionTray = { ...previous, layoutVersion: 3 };
    migrated.push(source.id);
  }
  return migrated;
}

/** Automatic fixed-tray fitting preserves the user's saved mode frames. */
export function fitFixedExpansionTray(project: MeridianProject, sourceId: string): MeridianProject {
  const nav = navigationIndex(project), source = nav.nodes.get(sourceId), content = source && fixedExpansionTrayContent(source);
  if (!source || !content || source.collapsed) return project;
  const children = nav.children.get(source.id) ?? []; if (!children.length) return project;
  if (isGallery(source)) {
    const page = fixedExpansionTrayGallery(source)!.page;
    fitGalleryItemsToSlot(project, sourceId, page);
    return project;
  }
  if (isOrderedContainer(source)) return project;
  const boxes = children.map(child => nav.packingBranchBounds(child));
  const left = Math.min(...boxes.map(box => box.x)), top = Math.min(...boxes.map(box => box.y));
  const right = Math.max(...boxes.map(box => box.x + box.width)), bottom = Math.max(...boxes.map(box => box.y + box.height));
  if (left >= content.x - 1e-8 && top >= content.y - 1e-8 && right <= content.x + content.width + 1e-8 && bottom <= content.y + content.height + 1e-8) return project;
  transformTrayContents(project, source, { x: left, y: top, width: right - left, height: bottom - top }, content, false);
  return project;
}

/** Resize is one atomic edit of original nodes, their routes and the saved
 * presentation frame. Typography, hierarchy, node identities and source cover stay unchanged. */
export function resizeExpansionTray(project: MeridianProject, sourceId: string, frame: Frame, context: SizingContext = {}): MeridianProject {
  if (![frame.x, frame.y, frame.width, frame.height].every(Number.isFinite) || frame.width <= 0 || frame.height <= 0 || frame.width > 1e9 || frame.height > 1e9)
    throw new Error('Tray dimensions and position must be finite, with positive dimensions no larger than one billion.');
  const candidate = structuredClone(project);
  const migrated = migrateExpansionTrayMetrics(candidate);
  for (const id of migrated) Object.assign(candidate, reflowContainedChildren(candidate, id));
  const nav = navigationIndex(candidate, context), source = nav.nodes.get(sourceId), before = expansionTrayBounds(candidate, sourceId, context);
  if (!source || !before || !isExpansionTraySource(source)) throw new Error('Choose an existing expansion tray.');
  const descendants = nav.descendants(sourceId), ids = new Set(descendants.map(node => node.id));
  if (descendants.some(node => node.positionLocked)) throw new Error('Unlock the pinned contents before resizing this expansion tray.');
  const proposed = { ...frame, width: Math.max(EXPANSION_TRAY_MIN_WIDTH, frame.width), height: Math.max(EXPANSION_TRAY_MIN_HEIGHT, frame.height) };
  const obstacles = candidate.nodes.filter(node => node.canvasId === source.canvasId && node.kind !== 'canvas' && !ids.has(node.id) && nav.isNodeVisible(node.id)).map(node => nav.bounds(node));
  obstacles.push(...visibleExpansionTrays(candidate).filter(tray => tray.sourceId !== sourceId && !ids.has(tray.sourceId) && nav.nodes.get(tray.sourceId)?.canvasId === source.canvasId).map(tray => ({ id: tray.sourceId, ...tray })));
  const actual = collisionFreeResize(before, obstacles, proposed);
  const target = { x: actual.x + EXPANSION_TRAY_PADDING, y: actual.y + EXPANSION_TRAY_HEADER + EXPANSION_TRAY_PADDING,
    width: actual.width - 2 * EXPANSION_TRAY_PADDING, height: actual.height - EXPANSION_TRAY_HEADER - 2 * EXPANSION_TRAY_PADDING };
  transformTrayContents(candidate, source, before.content, target, true);
  source.expansionTray = { ...source.expansionTray!, offset: { x: target.x - source.x, y: target.y - source.y }, size: { width: actual.width, height: actual.height }, layoutVersion: 3 };
  Object.assign(candidate, reflowContainedChildren(candidate, sourceId));
  repairExpansionTrayCollisions(candidate);
  const issues = validateChange(project, candidate); if (issues.length) throw new Error(issues.map(issue => issue.message).join(' '));
  return candidate;
}
