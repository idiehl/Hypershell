import type { CellLocation, ChildArrangement, MeridianNode, MeridianProject, Point } from './types.ts';
import { navigationIndex } from './navigation.ts';
import { validateChange } from './rules.ts';
import { GRID_LIMITS, CHILD_ARRANGEMENTS, cellAddress, gridGeometry, gridCellBounds, isGridSettings, isOrderedContainer, isEnclosingContainer, isCellChild, orderedCellAxes, orderedPattern } from './cells.ts';
import { isGallery, isEmbeddedGallery } from './gallery.ts';
import { boundedChildInterior, containingFixedInterior, intrinsicContentSize, nodeBorderWidth, nodeSizingMode, nodeSpacing } from './sizing.ts';
import { shapeAspectRatio, shapeContentInsets } from './geometry.ts';
import { collisionFreeTranslation } from './collision.ts';
import { rememberContainerSize } from './container-size.ts';
import { nodeTag } from './node-tag.ts';
import { prepareExpansionTrays, placeExpansionTrays, repairExpansionTrayCollisions, fitFixedExpansionTray, isExpansionTraySource, migrateExpansionTrayMetrics, fitGalleryItemsToSlot } from './expansion-tray.ts';
export * from './cells.ts';
const bounded = (value: unknown, min: number, max: number): value is number => typeof value === 'number' && Number.isFinite(value) && value >= min && value <= max;
const axis = (value: unknown): value is number => bounded(value, 1, GRID_LIMITS.axis) && Number.isInteger(value);

export interface ChildArrangementOptions { gap?: number; columns?: number; fitParent?: boolean; /** Disclosure can reserve an expanded child instead of shrinking it into the old cell. */ growForDisclosure?: boolean }
interface BranchBox { id: string; x: number; y: number; width: number; height: number; offsetX: number; offsetY: number }
export const FREEFORM_CLOCK_ORDER = Object.freeze([3, 4, 2, 5, 1, 6, 12, 7, 11, 8, 10, 9]);
const intersects = (a: { x: number; y: number; width: number; height: number }, b: { x: number; y: number; width: number; height: number }) => {
  const tolerance = Math.min(.001, a.width * 1e-6, a.height * 1e-6, b.width * 1e-6, b.height * 1e-6);
  return a.x < b.x + b.width - tolerance && a.x + a.width > b.x + tolerance && a.y < b.y + b.height - tolerance && a.y + a.height > b.y + tolerance;
};

/** A branch reserves every locally revealed tile, even while an outer owner is collapsed. */
function branchBox(index: ReturnType<typeof navigationIndex>, root: MeridianNode, measureOutsideCell = false): BranchBox {
  // A newly selected ordered subtype can still carry its old grid metadata.
  // Measure the child's composition before that metadata constrains its tile,
  // otherwise an automatic Grid inherits stale cell dimensions indefinitely.
  const box = index.packingBranchBounds(measureOutsideCell ? { ...root, parentId: null } : root);
  return { ...box, offsetX: root.x - box.x, offsetY: root.y - box.y };
}

function applyPositions(project: MeridianProject, candidate: MeridianProject, index: ReturnType<typeof navigationIndex>, children: MeridianNode[], positions: Map<string, Point>, validate = true): string[] {
  const movements = new Map<string, Point>(), movedRootIds: string[] = [];
  for (const child of children) {
    const at = positions.get(child.id); if (!at) continue;
    const tolerance = Math.min(1e-7, child.width * 1e-6, child.height * 1e-6);
    const delta = { x: Math.abs(at.x - child.x) < tolerance ? 0 : at.x - child.x, y: Math.abs(at.y - child.y) < tolerance ? 0 : at.y - child.y };
    if (!delta.x) at.x = child.x; if (!delta.y) at.y = child.y;
    if (delta.x || delta.y) movedRootIds.push(child.id);
    movements.set(child.id, delta);
    for (const descendant of index.descendants(child.id)) movements.set(descendant.id, delta);
  }
  for (const node of candidate.nodes) {
    const delta = movements.get(node.id); if (!delta) continue;
    const at = positions.get(node.id);
    node.x = at?.x ?? node.x + delta.x; node.y = at?.y ?? node.y + delta.y;
    if (positions.has(node.id) && (delta.x || delta.y)) node.placement = 'free';
    if (![node.x, node.y, node.x + node.width, node.y + node.height].every(Number.isFinite)) throw new Error('Arrangement geometry must stay finite.');
  }
  for (const link of candidate.links) {
    const source = movements.get(link.sourceId), target = movements.get(link.targetId);
    if (source && target && source.x === target.x && source.y === target.y && link.points)
      link.points = link.points.map(point => ({ x: point.x + source.x, y: point.y + source.y }));
  }
  if (validate) {
    const issues = validateChange(project, candidate);
    if (issues.length) throw new Error(issues.map(issue => issue.message).join(' '));
  }
  return movedRootIds;
}

function scalingBranch(candidate: MeridianProject, index: ReturnType<typeof navigationIndex>, root: MeridianNode): MeridianNode[] {
  const nodes = [root, ...index.descendants(root.id).map(node => candidate.nodes.find(item => item.id === node.id)!)];
  return nodes.filter(node => node.id === root.id || !index.ancestors(node.id).slice(0, index.ancestors(node.id).findIndex(parent => parent.id === root.id) + 1).some(isExpansionTraySource));
}
function followScaledTraySources(candidate: MeridianProject, index: ReturnType<typeof navigationIndex>, branch: MeridianNode[], before: ReadonlyMap<string, Point>): void {
  for (const source of branch.filter(isExpansionTraySource)) {
    const position = before.get(source.id)!;
    const dx = source.x - position.x, dy = source.y - position.y;
    if (!dx && !dy) continue;
    const ids = new Set(index.descendants(source.id).map(node => node.id));
    for (const node of candidate.nodes) if (ids.has(node.id)) { node.x += dx; node.y += dy; }
    ids.add(source.id);
    for (const link of candidate.links) if (ids.has(link.sourceId) && ids.has(link.targetId) && link.points)
      link.points = link.points.map(point => ({ x: point.x + dx, y: point.y + dy }));
  }
}

/** Shrink a whole free composition uniformly into its authored frame. Relative
 * positions, descendant identity and internal routing stay with each branch;
 * fitting changes geometry, never authored content or typography. */
function fitFixedComposition(candidate: MeridianProject, parent: MeridianNode, index: ReturnType<typeof navigationIndex>, children: MeridianNode[], boxes: BranchBox[], positions: Map<string, Point>): void {
  if (!isEnclosingContainer(parent) || !boxes.length) return;
  const interior = boundedChildInterior(candidate, parent, {}, { x: parent.x, y: parent.y, width: parent.width, height: parent.height });
  const tolerance = Math.min(.001, interior.width * 1e-6, interior.height * 1e-6);
  const proposed = boxes.map(box => { const at = positions.get(box.id)!; return { ...box, x: at.x - box.offsetX, y: at.y - box.offsetY }; });
  const left = Math.min(...proposed.map(box => box.x)), top = Math.min(...proposed.map(box => box.y));
  const right = Math.max(...proposed.map(box => box.x + box.width)), bottom = Math.max(...proposed.map(box => box.y + box.height));
  if (left >= interior.x - tolerance && top >= interior.y - tolerance && right <= interior.x + interior.width + tolerance && bottom <= interior.y + interior.height + tolerance) return;
  const fitsInterior = (box: BranchBox) => box.x >= interior.x - tolerance && box.y >= interior.y - tolerance && box.x + box.width <= interior.x + interior.width + tolerance && box.y + box.height <= interior.y + interior.height + tolerance;
  const anchored = proposed.filter((box, at) => children[at].childPosition !== 'auto' && fitsInterior(box));
  if (anchored.length && anchored.length < proposed.length) {
    // An automatically revealed branch must not squeeze an existing, valid
    // manual composition merely because the new radial proposal was exterior.
    const anchoredIds = new Set(anchored.map(box => box.id)), obstacles = [...anchored];
    for (const box of proposed) {
      if (anchoredIds.has(box.id)) continue;
      const original = children.find(child => child.id === box.id)!, root = candidate.nodes.find(node => node.id === box.id)!;
      let scale = Math.min(1, interior.width / box.width, interior.height / box.height), destination: Point | undefined;
      for (let attempt = 0; attempt < 48 && !destination; attempt++, scale *= .8) {
        const width = Math.max(Number.MIN_VALUE, box.width * scale), height = Math.max(Number.MIN_VALUE, box.height * scale), maxX = interior.x + interior.width - width, maxY = interior.y + interior.height - height;
        const xs = [Math.max(interior.x, Math.min(box.x, maxX)), interior.x, maxX, ...obstacles.flatMap(other => [other.x + other.width, other.x - width])];
        const ys = [Math.max(interior.y, Math.min(box.y, maxY)), interior.y, maxY, ...obstacles.flatMap(other => [other.y + other.height, other.y - height])];
        const choices = xs.flatMap(x => ys.map(y => ({ x, y, width, height }))).filter(choice => choice.x >= interior.x - tolerance && choice.y >= interior.y - tolerance && choice.x + width <= interior.x + interior.width + tolerance && choice.y + height <= interior.y + interior.height + tolerance && !obstacles.some(other => intersects(choice, other)));
        choices.sort((a, b) => Math.hypot(a.x - box.x, a.y - box.y) - Math.hypot(b.x - box.x, b.y - box.y));
        if (choices[0]) { destination = choices[0]; obstacles.push({ ...box, ...choices[0] }); break; }
      }
      if (!destination) throw new Error('No free space remains inside this container. Resize the parent or move a child to make room.');
      positions.set(root.id, { x: destination.x + box.offsetX * scale, y: destination.y + box.offsetY * scale });
      if (scale >= 1) continue;
      const branch = scalingBranch(candidate, index, root), ids = new Set(branch.map(node => node.id)), trayBefore = new Map(branch.filter(node => node.expansionTray).map(node => [node.id, { x: node.x, y: node.y }]));
      for (const node of branch) {
        const effective = index.bounds(index.nodes.get(node.id)!);
        node.x = original.x + (node.x - original.x) * scale; node.y = original.y + (node.y - original.y) * scale;
        node.width = Math.max(Number.MIN_VALUE, effective.width * scale); node.height = Math.max(Number.MIN_VALUE, effective.height * scale); node.sizeMode = 'fixed';
        if (node.containerSizes) rememberContainerSize(node, undefined, { preserveExisting: true });
      }
      followScaledTraySources(candidate, index, branch, trayBefore);
      for (const link of candidate.links) if (ids.has(link.sourceId) && ids.has(link.targetId) && link.points)
        link.points = link.points.map(point => ({ x: original.x + (point.x - original.x) * scale, y: original.y + (point.y - original.y) * scale }));
    }
    return;
  }
  const scale = Math.min(1, interior.width / Math.max(Number.MIN_VALUE, right - left), interior.height / Math.max(Number.MIN_VALUE, bottom - top));
  const x = Math.max(interior.x, Math.min(left, interior.x + interior.width - (right - left) * scale));
  const y = Math.max(interior.y, Math.min(top, interior.y + interior.height - (bottom - top) * scale));
  for (const [at, original] of children.entries()) {
    const root = candidate.nodes.find(node => node.id === original.id)!, box = proposed[at]!;
    positions.set(root.id, { x: x + (box.x - left + box.offsetX) * scale, y: y + (box.y - top + box.offsetY) * scale });
    if (scale >= 1) continue;
    const branch = scalingBranch(candidate, index, root), ids = new Set(branch.map(node => node.id)), trayBefore = new Map(branch.filter(node => node.expansionTray).map(node => [node.id, { x: node.x, y: node.y }]));
    const exteriorWindow = !isEnclosingContainer(root) && root.containerVariant === 'window' ? gridGeometry({ ...root, parentId: null }, candidate) : undefined;
    const sizes = new Map(branch.map(node => [node.id, exteriorWindow && node.parentId === root.id && isCellChild(node)
      ? { width: exteriorWindow.cellWidth, height: exteriorWindow.cellHeight } : index.bounds(index.nodes.get(node.id)!)]));
    for (const node of branch) {
      node.x = original.x + (node.x - original.x) * scale; node.y = original.y + (node.y - original.y) * scale;
      const effective = sizes.get(node.id)!;
      node.width = Math.max(Number.MIN_VALUE, effective.width * scale); node.height = Math.max(Number.MIN_VALUE, effective.height * scale); node.sizeMode = 'fixed';
      if (node.containerSizes) rememberContainerSize(node, undefined, { preserveExisting: true });
    }
    followScaledTraySources(candidate, index, branch, trayBefore);
    for (const link of candidate.links) if (ids.has(link.sourceId) && ids.has(link.targetId) && link.points)
      link.points = link.points.map(point => ({ x: original.x + (point.x - original.x) * scale, y: original.y + (point.y - original.y) * scale }));
  }
}

function refitExpandedChildren(candidate: MeridianProject, children: MeridianNode[]): void {
  for (const child of children) {
    const current = candidate.nodes.find(node => node.id === child.id)!;
    if (current.collapsed || !(isEnclosingContainer(current) || current.containerVariant && containingFixedInterior(candidate, current)) || isEmbeddedGallery(current) || !candidate.nodes.some(node => node.parentId === current.id)) continue;
    const pattern = isOrderedContainer(current) ? orderedPattern(current) : current.containerVariant === 'contained' || current.containerVariant === 'spatial' ? 'free' : current.childArrangement ?? 'free';
    arrangeDraft(candidate, current.id, pattern, {}, { project: candidate, index: navigationIndex(candidate) });
  }
}

/** Arrange complete revealed branch footprints; hidden descendants and internal routes travel with their root. */
export function arrangeChildren(project: MeridianProject, parentId: string, pattern: ChildArrangement, options: ChildArrangementOptions = {}): { project: MeridianProject; movedRootIds: string[] } {
  const prepared = structuredClone(project), migrated = migrateExpansionTrayMetrics(prepared), created = prepareExpansionTrays(prepared);
  const result = arrangeDraft(created.length || migrated.length ? prepared : project, parentId, pattern, options);
  for (const id of new Set([...migrated, ...created])) if (id !== parentId) {
    const source = result.project.nodes.find(node => node.id === id)!;
    Object.assign(result.project, arrangeDraft(result.project, id, isOrderedContainer(source) ? orderedPattern(source) : source.containerVariant === 'contained' || source.containerVariant === 'spatial' ? 'free' : source.childArrangement ?? 'free', {}).project);
  }
  if (created.length) placeExpansionTrays(result.project, created);
  repairExpansionTrayCollisions(result.project);
  return result;
}
interface LayoutDraft { project: MeridianProject; index: ReturnType<typeof navigationIndex> }
function arrangeDraft(project: MeridianProject, parentId: string, pattern: ChildArrangement, options: ChildArrangementOptions, shared?: LayoutDraft): { project: MeridianProject; movedRootIds: string[] } {
  if (!CHILD_ARRANGEMENTS.includes(pattern)) throw new Error('Choose a supported child arrangement.');
  if (options.gap !== undefined && !bounded(options.gap, 0, GRID_LIMITS.maxGap)) throw new Error('Arrangement gap must be between 0 and 1000.');
  if (options.columns !== undefined && !axis(options.columns)) throw new Error('Grid columns must be between 1 and 512.');
  const nav = shared?.index ?? navigationIndex(project), original = nav.nodes.get(parentId);
  if (!original) throw new Error('The parent node does not exist.');
  const allChildren = nav.children.get(parentId) ?? [], children = allChildren.filter(isCellChild), addressedUtilities = allChildren.filter(child => !isCellChild(child) && child.cell);
  const candidate = shared?.project ?? structuredClone(project);
  const ordered = isOrderedContainer(original), explicit = original.kind === 'spreadsheet' || ['spreadsheet', 'window'].includes(original.containerVariant ?? '');
  if ((!ordered || explicit) && original.grid && allChildren.some(child => child.cell && (child.cell.column > original.grid!.columns || child.cell.row > original.grid!.rows))) throw new Error('Cell coordinates must fit the parent grid. Move occupied cells before reducing its rows or columns.');
  if ((!ordered || explicit) && options.columns !== undefined && allChildren.some(child => child.cell && child.cell.column > options.columns!)) throw new Error('Move occupied cells before reducing the grid columns.');
  const parent = candidate.nodes.find(node => node.id === parentId)!;
  if (ordered) pattern = orderedPattern(parent);
  parent.childArrangement = pattern;
  // A compact preview owns independent geometry. Hidden expanded contents are
  // reflowed only after disclosure restores their expanded workspace frame.
  if ((isEnclosingContainer(parent) || parent.expansionTray) && parent.collapsed) return { project: candidate, movedRootIds: [] };
  // A disclosure Cluster is an authored assembly, including deliberately layered
  // members. Reopening it must not repack, fit, or scale that composition.
  if (parent.cluster && pattern === 'free') return { project: candidate, movedRootIds: [] };
  if (isExpansionTraySource(parent) && parent.expansionTray?.size && (isGallery(parent) || !ordered && pattern === 'free')) {
    fitFixedExpansionTray(candidate, parent.id);
    refitExpandedChildren(candidate, children);
    if (!shared) {
      const issues = validateChange(project, candidate); if (issues.length) throw new Error(issues.map(issue => issue.message).join(' '));
    }
    return { project: candidate, movedRootIds: [] };
  }
  if (isGallery(parent)) {
    if (isEnclosingContainer(parent) || !children.length) return { project: candidate, movedRootIds: [] };
    parent.childLayout = 'spatial';
    const parentBox = nav.bounds(parent);
    const ids = new Set(children.flatMap(child => [child.id, ...nav.descendants(child.id).map(node => node.id)]));
    const ancestors = new Set(nav.ancestors(parent.id).filter(isEnclosingContainer).map(node => node.id));
    const obstacles = candidate.nodes.filter(node => node.canvasId === parent.canvasId && node.kind !== 'canvas' && !ids.has(node.id) && !ancestors.has(node.id) && nav.isNodeVisible(node.id)).map(node => nav.bounds(node));
    const slot = { x: parent.expansionTray ? parent.x + parent.expansionTray.offset.x : parentBox.x + parentBox.width + 28,
      y: parent.expansionTray ? parent.y + parent.expansionTray.offset.y : parentBox.y,
      width: Math.max(...children.map(child => child.width)), height: Math.max(...children.map(child => child.height)) };
    const shift = collisionFreeTranslation([slot], obstacles, { x: 0, y: 0 });
    slot.x += shift.x; slot.y += shift.y;
    const movedRootIds = children.filter(child => Math.abs(child.x - slot.x) + Math.abs(child.y - slot.y) > 1e-8).map(child => child.id);
    fitGalleryItemsToSlot(candidate, parent.id, slot);
    for (const id of movedRootIds) candidate.nodes.find(node => node.id === id)!.placement = 'free';
    refitExpandedChildren(candidate, children);
    if (!shared) { const issues = validateChange(project, candidate); if (issues.length) throw new Error(issues.map(issue => issue.message).join(' ')); }
    return { project: candidate, movedRootIds };
  }
  const gap = ordered ? 0 : options.gap ?? (pattern === 'grid' ? parent.grid?.gap ?? 16 : 32);
  // An escaped Media Window needs its complete cover + viewport measured before
  // fitting. Once fitted, use its actual surface: measuring its original grid
  // request again would progressively shrink the same composition on reload.
  const windowNeedsReserve = (child: MeridianNode): boolean => {
    if (!isEnclosingContainer(parent) || isEnclosingContainer(child) || child.containerVariant !== 'window') return false;
    const scope = containingFixedInterior(candidate, child);
    if (!scope) return false;
    const box = nav.branchBounds(child), viewport = gridGeometry(child, candidate);
    return box.x < scope.x - .001 || box.y < scope.y - .001 || box.x + box.width > scope.x + scope.width + .001 ||
      box.y + box.height > scope.y + scope.height + .001 || viewport.cellWidth <= .001 || viewport.cellHeight <= .001;
  };
  const boxes = children.map(child => branchBox(nav, child, ordered || windowNeedsReserve(child))), positions = new Map<string, Point>();
  const contained = isEnclosingContainer(parent), spacing = nodeSpacing(parent.style), border = contained ? nodeBorderWidth(parent) : 0, childPadding = contained ? spacing.childPadding : 0, sideInset = childPadding + border;
  if (['container', 'module'].includes(parent.kind) && parent.containerVariant === 'spatial') parent.containerVariant = 'contained';
  if (['container', 'module', 'spreadsheet'].includes(parent.kind)) parent.childLayout = 'contained';
  else if (parent.kind !== 'canvas') parent.childLayout = 'spatial';
  const own = intrinsicContentSize(candidate, ordered ? { ...parent, collapsed: false, ...(!explicit ? { grid: undefined } : {}) } : parent);
  const fixedInterior = contained ? boundedChildInterior(candidate, parent, {}, { x: parent.x, y: parent.y, width: parent.width, height: parent.height }) : undefined;
  const origin = fixedInterior ? { x: fixedInterior.x, y: fixedInterior.y } : parent.expansionTray ? { x: parent.x + parent.expansionTray.offset.x, y: parent.y + parent.expansionTray.offset.y }
    : contained ? { x: parent.x + sideInset, y: parent.y + own.height - spacing.padding - border + spacing.contentGap } : { x: parent.x, y: parent.y + nav.bounds(parent).height + gap };
  if (ordered) {
    if (!contained) delete parent.childOffset;
    const count = children.length + addressedUtilities.length, axes = orderedCellAxes(parent, allChildren);
    const columns = explicit ? options.columns ?? parent.grid?.columns ?? 3 : axes.columns;
    const rows = explicit ? parent.grid?.rows ?? 3 : axes.rows;
    if (!axis(columns) || !axis(rows) || columns * rows > GRID_LIMITS.cells || count > columns * rows) throw new Error('This ordered container is full or exceeds the supported cell limits.');
    const leafSizes = children.map((child, at) => nav.children.get(child.id)?.length && !child.collapsed ? boxes[at]! : intrinsicContentSize(candidate, child));
    const retainedPixels = contained || explicit || options.growForDisclosure && !!parent.grid;
    let cellWidth = retainedPixels ? parent.grid?.cellWidth ?? 200 : Math.max(GRID_LIMITS.minCell, ...leafSizes.map(box => box.width));
    let cellHeight = retainedPixels ? parent.grid?.cellHeight ?? 100 : Math.max(GRID_LIMITS.minCell, ...leafSizes.map(box => box.height));
    if (options.growForDisclosure && !contained) {
      // Explicit Spreadsheet/Window capacities and automatic equal-sized cells
      // retain their coordinate system while making room for the disclosed tile.
      const expanded = children.map((child, at) => !child.collapsed && isEnclosingContainer(child) && nav.children.get(child.id)?.length ? boxes[at] : undefined)
        .filter((box): box is BranchBox => !!box);
      cellWidth = Math.max(cellWidth, parent.grid?.cellWidth ?? 0, ...expanded.map(box => box.width));
      cellHeight = Math.max(cellHeight, parent.grid?.cellHeight ?? 0, ...expanded.map(box => box.height));
    }
    const shape = children[0]?.style?.shape ?? 'rectangle';
    if (!contained && (shape === 'circle' || shape === 'square')) {
      if (explicit && cellWidth !== cellHeight) throw new Error('Circle and square child shapes require equal cell width and height. Choose equal pixel dimensions or a different child shape.');
      cellWidth = cellHeight = Math.max(cellWidth, cellHeight);
    }
    for (const [at, child] of children.entries()) if (!child.collapsed && (nav.children.get(child.id)?.length ?? 0) > 0 && !(isEnclosingContainer(child) && (isGallery(child) || child.containerVariant === 'window'))) {
      const box = boxes[at]!;
      // Exterior ownership is a semantic conflict with a tightly packed cell.
      // Measuring only visible pixels let fitted exterior rows shrink to the
      // numerical tolerance at a wall and silently disappear instead.
      const exteriorContent = !isEnclosingContainer(child) && !child.expansionTray;
      if (exteriorContent || box.offsetX > .001 || box.offsetY > .001 || explicit && !isEnclosingContainer(child) && (box.width > cellWidth + .001 || box.height > cellHeight + .001))
        throw new Error(`A revealed child branch does not fit its ordered cell ("${nodeTag(child)}"): its children are displayed outside its border. Use a bounded Container to show these children inside their tile, or move this node into a Freeform workspace or onto the Work.`);
    }
    parent.grid = { ...parent.grid, columns, rows, cellWidth, cellHeight, gap: 0, showHeaders: false, fillOrder: parent.grid?.fillOrder ?? 'row' };
    // Cell pixel requests remain portable grid metadata. A bounded frame fits
    // its rendered cells independently, even below the 36px authoring minimum.
    if (!isGridSettings(parent.grid)) throw new Error('This arrangement exceeds the supported grid size.');
    const occupied = new Set([...addressedUtilities, ...explicit ? children.filter(child => child.cell) : []].map(child => cellAddress(child.cell!)));
    let cursor = 0;
    const nextCell = (): CellLocation => {
      let cell: CellLocation;
      do {
        if (cursor >= columns * rows) throw new Error('This ordered container is full. Increase its rows or columns before adding another child.');
        cell = parent.grid!.fillOrder === 'column' ? { column: Math.floor(cursor / rows) + 1, row: cursor % rows + 1 }
          : { column: cursor % columns + 1, row: Math.floor(cursor / columns) + 1 };
        cursor++;
      } while (occupied.has(cellAddress(cell)));
      occupied.add(cellAddress(cell)); return cell;
    };
    for (const [at, originalChild] of children.entries()) {
      const child = candidate.nodes.find(node => node.id === originalChild.id)!;
      child.cell = explicit && child.cell ? child.cell : { ...nextCell(), ...(child.cell?.name ? { name: child.cell.name } : {}) };
      const cell = gridCellBounds(parent, child.cell, candidate);
      child.width = cell.width; child.height = cell.height; child.sizeMode = 'fixed'; child.style = { ...child.style, shape };
      if (child.containerSizes) rememberContainerSize(child, undefined, { preserveExisting: true });
      positions.set(child.id, { x: cell.x, y: cell.y });
    }
    if (!contained && !parent.expansionTray && positions.size) {
      // Keep an entire ordered surface together when a nearby peer occupies the
      // intended location. Cell coordinates and each branch's internal paths stay intact.
      const ids = new Set(children.flatMap(child => [child.id, ...nav.descendants(child.id).map(node => node.id)]));
      const ancestors = new Set(nav.ancestors(parent.id).filter(isEnclosingContainer).map(node => node.id));
      const occupied = candidate.nodes.filter(node => node.canvasId === parent.canvasId && node.kind !== 'canvas' && !ids.has(node.id) && !ancestors.has(node.id) && nav.isNodeVisible(node.id)).map(node => nav.bounds(node));
      const geometry = gridGeometry(parent, candidate);
      const proposed = parent.containerVariant === 'window' ? [{ x: geometry.left, y: geometry.top, width: geometry.visibleColumns * cellWidth, height: geometry.visibleRows * cellHeight }]
        : boxes.map(box => { const at = positions.get(box.id)!; return { ...box, x: at.x - box.offsetX, y: at.y - box.offsetY, width: Math.max(box.width, cellWidth), height: Math.max(box.height, cellHeight) }; });
      const shift = collisionFreeTranslation(proposed, occupied, { x: 0, y: 0 });
      if (shift.x || shift.y) for (const at of positions.values()) { at.x += shift.x; at.y += shift.y; }
      parent.childOffset = shift;
    }
    // The authored parent owns its frame; fitting cells never grows that frame.
    const movedRootIds = applyPositions(project, candidate, nav, children, positions, !shared);
    if (contained || parent.expansionTray?.size) refitExpandedChildren(candidate, children);
    return { project: candidate, movedRootIds };
  }
  if (!children.length) {
    // In particular, a blank intrinsic surface is its stored frame, not a
    // header to which another border/padding envelope should be added.
    return { project: candidate, movedRootIds: [] };
  }
  const freeTolerance = fixedInterior ? Math.min(.001, fixedInterior.width * 1e-6, fixedInterior.height * 1e-6) : 0;
  const retainedFreePositions = contained && (pattern === 'free' || pattern === 'radial') && !!fixedInterior && boxes.every(box =>
    box.x >= fixedInterior.x - freeTolerance && box.y >= fixedInterior.y - freeTolerance && box.x + box.width <= fixedInterior.x + fixedInterior.width + freeTolerance && box.y + box.height <= fixedInterior.y + fixedInterior.height + freeTolerance)
    && !boxes.some((box, at) => boxes.slice(at + 1).some(other => intersects(box, other)));
  const retainedTrayPositions = !!parent.expansionTray && pattern === 'free' && boxes.length > 0 &&
    Math.abs(Math.min(...boxes.map(box => box.x)) - origin.x) < 1e-6 && Math.abs(Math.min(...boxes.map(box => box.y)) - origin.y) < 1e-6 &&
    !boxes.some((box, at) => boxes.slice(at + 1).some(other => intersects(box, other)));
  if (retainedFreePositions || retainedTrayPositions) {
    for (const box of boxes) positions.set(box.id, { x: box.x + box.offsetX, y: box.y + box.offsetY });
  } else if (pattern === 'radial' || pattern === 'free' && children.some(child => child.childPosition === 'auto')) {
    const automatic = pattern === 'radial' ? children : children.filter(child => child.childPosition === 'auto');
    const automaticIds = new Set(automatic.flatMap(child => [child.id, ...nav.descendants(child.id).map(node => node.id)]));
    const ancestors = new Set(nav.ancestors(parent.id).filter(isEnclosingContainer).map(node => node.id));
    const obstacles: Array<{ x: number; y: number; width: number; height: number }> = contained ? boxes.filter(box => !automaticIds.has(box.id))
      : candidate.nodes.filter(node => node.canvasId === parent.canvasId && node.kind !== 'canvas' && !automaticIds.has(node.id) && !ancestors.has(node.id) && nav.isNodeVisible(node.id)).map(node => nav.bounds(node));
    const parentBox = contained ? { ...own, x: parent.x, y: parent.y } : nav.bounds(parent), extent = Math.max(...boxes.map(box => Math.hypot(box.width, box.height))), separation = Math.max(gap, 28);
    const radius = Math.max((Math.hypot(parentBox.width, parentBox.height) + extent) / 2 + separation, (extent + separation) / (2 * Math.sin(Math.PI / 12)));
    const ringStep = extent + separation;
    const rings = Math.max(1, Math.ceil(automatic.length / 12));
    const center = contained || parent.expansionTray ? { x: origin.x + radius + (rings - 1) * ringStep + extent / 2, y: origin.y + radius + (rings - 1) * ringStep + extent / 2 }
      : { x: parent.x + parentBox.width / 2, y: parent.y + parentBox.height / 2 };
    for (const box of boxes) if (!automaticIds.has(box.id)) positions.set(box.id, { x: box.x + box.offsetX, y: box.y + box.offsetY });
    for (const [at, child] of automatic.entries()) {
      const box = boxes.find(box => box.id === child.id)!, angle = (FREEFORM_CLOCK_ORDER[at % 12]! - 3) * Math.PI / 6;
      let placed: { x: number; y: number; width: number; height: number } | undefined;
      for (let extra = 0; extra < 4096; extra++) {
        const distance = radius + (Math.floor(at / 12) + extra) * ringStep;
        const proposed = { x: center.x + Math.cos(angle) * distance - box.width / 2, y: center.y + Math.sin(angle) * distance - box.height / 2, width: box.width, height: box.height };
        if (contained && (proposed.x < origin.x || proposed.y < origin.y)) continue;
        if (!obstacles.some(obstacle => intersects(proposed, obstacle))) { placed = proposed; break; }
      }
      if (!placed) throw new Error('No finite space is available for the radial child layout.');
      positions.set(child.id, { x: placed.x + box.offsetX, y: placed.y + box.offsetY }); obstacles.push(placed);
      candidate.nodes.find(node => node.id === child.id)!.childPosition = 'auto';
    }
  } else if (pattern === 'free') {
    if (!contained) return { project: candidate, movedRootIds: [] };
    // Authored free positions stay intact when usable. Escaped/overlapping branches
    // are packed once, retaining each branch's internal composition and disclosure.
    const shape = shapeContentInsets(parent.style?.shape), growsShapedSurface = nodeSizingMode(parent) !== 'fixed' && (shape.x || shape.top || shape.bottom || shapeAspectRatio(parent.style?.shape) !== undefined);
    const unusable = !growsShapedSurface && boxes.some(box => box.x < origin.x - .001 || box.y < origin.y - .001) ||
      boxes.some((box, at) => boxes.slice(at + 1).some(other => box.x < other.x + other.width && box.x + box.width > other.x && box.y < other.y + other.height && box.y + box.height > other.y));
    if (unusable) {
      const columns = Math.ceil(Math.sqrt(boxes.length));
      let y = origin.y;
      for (let start = 0; start < boxes.length; start += columns) {
        const row = boxes.slice(start, start + columns); let x = origin.x;
        for (const box of row) { positions.set(box.id, { x: x + box.offsetX, y: y + box.offsetY }); x += box.width + gap; }
        y += Math.max(...row.map(box => box.height)) + gap;
      }
    } else for (const box of boxes) positions.set(box.id, { x: box.x + box.offsetX, y: box.y + box.offsetY });
  } else
  if (pattern === 'grid') {
    let columns = Math.max(options.columns ?? parent.grid?.columns ?? Math.ceil(Math.sqrt(children.length)), ...allChildren.map(child => child.cell?.column ?? 1));
    columns = Math.min(GRID_LIMITS.axis, columns);
    const occupied = new Set(allChildren.filter(child => child.cell).map(child => cellAddress(child.cell!)));
    let cursor = 0;
    for (const child of children) if (!child.cell) {
      let cell: CellLocation;
      do { cell = { column: cursor % columns + 1, row: Math.floor(cursor / columns) + 1 }; cursor++; }
      while (occupied.has(cellAddress(cell)));
      const placed = candidate.nodes.find(node => node.id === child.id)!; placed.cell = cell; occupied.add(cellAddress(cell));
    }
    const placed = children.map(child => candidate.nodes.find(node => node.id === child.id)!);
    const spreadsheet = parent.kind === 'spreadsheet' || parent.containerVariant === 'spreadsheet';
    if (!spreadsheet && options.columns === undefined) columns = Math.max(1, ...[...placed, ...addressedUtilities].map(child => child.cell!.column));
    const rows = Math.max(spreadsheet ? parent.grid?.rows ?? 1 : 1, ...[...placed, ...addressedUtilities].map(child => child.cell!.row));
    const retainCellSize = spreadsheet || nodeSizingMode(parent) === 'fixed';
    const cellWidth = Math.max(retainCellSize ? parent.grid?.cellWidth ?? GRID_LIMITS.minCell : GRID_LIMITS.minCell, ...boxes.map(box => box.width));
    const cellHeight = Math.max(retainCellSize ? parent.grid?.cellHeight ?? GRID_LIMITS.minCell : GRID_LIMITS.minCell, ...boxes.map(box => box.height));
    parent.grid = { columns, rows, cellWidth, cellHeight, gap, showHeaders: parent.grid?.showHeaders ?? true };
    if (!isGridSettings(parent.grid)) throw new Error('This arrangement exceeds the supported grid size.');
    for (const [index, child] of placed.entries()) {
      const box = boxes[index];
      const at = contained ? gridCellBounds(parent, child.cell!, candidate) : { x: origin.x + (child.cell!.column - 1) * (cellWidth + gap), y: origin.y + (child.cell!.row - 1) * (cellHeight + gap) };
      positions.set(child.id, { x: at.x + box.offsetX, y: at.y + box.offsetY });
    }
    // Enclosing geometry remains authored, including fractional dimensions.
  } else {
    let cursor = pattern === 'row' ? origin.x : origin.y;
    for (const [index, box] of boxes.entries()) {
      positions.set(box.id, { x: (pattern === 'row' ? cursor : origin.x + (pattern === 'stack' ? index * 16 : 0)) + box.offsetX, y: (pattern === 'row' ? origin.y : cursor) + box.offsetY });
      cursor += (pattern === 'row' ? box.width : box.height) + gap;
    }
  }
  fitFixedComposition(candidate, parent, nav, children, boxes, positions);
  const movedRootIds = applyPositions(project, candidate, nav, children, positions, !shared);
  if (contained) refitExpandedChildren(candidate, children);
  return { project: candidate, movedRootIds };
}

/** Repair an enclosing node's current pattern without turning a free composition into a saved preset. */
export function reflowContainedChildren(project: MeridianProject, parentId: string): MeridianProject {
  const parent = project.nodes.find(node => node.id === parentId);
  if (!parent || parent.kind === 'canvas' || parent.smartLink) return project;
  return arrangeChildren(project, parentId, isOrderedContainer(parent) ? orderedPattern(parent) : parent.containerVariant === 'contained' || parent.containerVariant === 'spatial' ? 'free' : parent.childArrangement ?? 'free').project;
}

/** Legacy API name retained for disclosure callers. Nested contents now fit the
 * active bounded workspace from the inside out; disclosure never resizes an
 * authored ancestor merely because a newly revealed child used to be larger. */
export function growForNestedDisclosure(project: MeridianProject, nodeId: string): MeridianProject {
  const initial = navigationIndex(project), node = initial.nodes.get(nodeId);
  if (!node || node.collapsed || !isEnclosingContainer(node) || !initial.children.get(nodeId)?.length) return project;
  const ancestors = initial.ancestors(nodeId);
  if (!ancestors.some(parent => !parent.collapsed && isEnclosingContainer(parent))) return project;
  let candidate = structuredClone(project);
  for (const id of [nodeId, ...ancestors.map(parent => parent.id)]) {
    const parent = candidate.nodes.find(item => item.id === id)!;
    if (parent.collapsed || !isEnclosingContainer(parent)) continue;
    // A Gallery's renderer fits its active object into the existing viewport.
    if (isEmbeddedGallery(parent)) continue;
    const pattern = isOrderedContainer(parent) ? orderedPattern(parent) : parent.containerVariant === 'contained' || parent.containerVariant === 'spatial' ? 'free' : parent.childArrangement ?? 'free';
    candidate = arrangeDraft(candidate, id, pattern, {}).project;
  }
  const issues = validateChange(project, candidate);
  if (issues.length) throw new Error(issues.map(issue => issue.message).join(' '));
  return candidate;
}

/** Upgrade display layouts on an independent draft, preserving saved identities and observations. */
export function normalizeContainerLayouts(project: MeridianProject): MeridianProject {
  let candidate = structuredClone(project);
  for (const parent of candidate.nodes) if (['container', 'module', 'spreadsheet'].includes(parent.kind)) {
    if (parent.containerVariant === 'spatial' || !parent.containerVariant && parent.kind !== 'spreadsheet') parent.containerVariant = 'contained';
    if (!parent.containerVariant && parent.kind === 'spreadsheet') parent.containerVariant = 'spreadsheet';
    parent.childLayout = 'contained';
    if (parent.containerVariant === 'contained') parent.childArrangement = 'free';
  }
  for (const parent of candidate.nodes) if (parent.kind !== 'canvas' && !isEnclosingContainer(parent) && (parent.kind === 'media' || parent.containerVariant)) parent.childLayout = 'spatial';
  migrateExpansionTrayMetrics(candidate);
  const createdTrays = prepareExpansionTrays(candidate);
  const index = navigationIndex(candidate), parents = candidate.nodes.filter(parent => parent.kind !== 'canvas' && (isEnclosingContainer(parent) || parent.containerVariant || parent.childArrangement || index.children.get(parent.id)?.some(child => child.childPosition === 'auto')))
    .sort((a, b) => index.ancestors(b.id).length - index.ancestors(a.id).length).map(parent => parent.id);
  const shared = { project: candidate, index };
  const reflow = (parentId: string) => {
    const parent = index.nodes.get(parentId)!;
    if (parent.kind === 'canvas' || parent.smartLink || isEmbeddedGallery(parent) || !isOrderedContainer(parent) && !index.children.get(parentId)?.length) return;
    // A collapsed fixed Freeform tile carries an independent hidden workspace.
    // Opening a document must not fit that workspace into its compact preview.
    if (parent.collapsed || index.ancestors(parentId).some(ancestor => ancestor.collapsed)) return;
    arrangeDraft(candidate, parentId, isOrderedContainer(parent) ? orderedPattern(parent) : parent.containerVariant === 'contained' || parent.containerVariant === 'spatial' ? 'free' : parent.childArrangement ?? 'free', {}, shared);
  };
  for (const parentId of parents) {
    try { reflow(parentId); }
    catch (error) {
      // Old ordered surfaces allowed revealed branches outside their cells.
      // Keep those compositions readable when opening a saved document, while
      // explicit edits continue to reject unsafe ordered placement atomically.
      if (!(error instanceof Error) || !/revealed child branch does not fit|Circle and square child shapes require equal/.test(error.message)) throw error;
      const parent = candidate.nodes.find(node => node.id === parentId)!;
      parent.kind = parent.kind === 'spreadsheet' ? 'container' : parent.kind;
      parent.containerVariant = 'contained'; parent.childLayout = isEnclosingContainer(parent) ? 'contained' : 'spatial'; parent.childArrangement = 'free';
      reflow(parentId);
    }
  }
  placeExpansionTrays(candidate, createdTrays);
  repairExpansionTrayCollisions(candidate);
  const issues = validateChange(project, candidate);
  if (issues.length) throw new Error(issues.map(issue => issue.message).join(' '));
  return candidate;
}

/** Bounded CSV data only: quoted fields/newlines, never a formula or executable import. */
export function parseCsv(text: string, delimiter: ',' | '\t' = ','): string[][] {
  if (text.length > 2_000_000) throw new Error('CSV attachments are limited to 2 million characters.');
  if (text.charCodeAt(0) === 0xfeff) text = text.slice(1);
  const rows: string[][] = [], row: string[] = []; let value = '', quoted = false, afterQuote = false;
  const field = () => { row.push(value); value = ''; afterQuote = false; if (row.length > GRID_LIMITS.axis) throw new Error('CSV has too many columns.'); };
  const finish = () => { field(); rows.push(row.splice(0)); if (rows.length > GRID_LIMITS.axis || rows.length * Math.max(...rows.map(item => item.length)) > GRID_LIMITS.cells) throw new Error('CSV has too many cells in its rectangular grid.'); };
  for (let index = 0; index < text.length; index++) {
    const character = text[index]!;
    if (quoted) {
      if (character === '"') { if (text[index + 1] === '"') { value += '"'; index++; } else { quoted = false; afterQuote = true; } }
      else value += character;
    } else if (character === '"' && !value && !afterQuote) quoted = true;
    else if (character === delimiter) field();
    else if (character === '\n' || character === '\r') { if (character === '\r' && text[index + 1] === '\n') index++; finish(); }
    else if (afterQuote) { if (!/\s/.test(character)) throw new Error('Unexpected text after a quoted CSV field.'); }
    else value += character;
  }
  if (quoted) throw new Error('CSV contains an unfinished quoted field.');
  if (value || row.length || afterQuote || !rows.length) finish();
  return rows;
}
export function exportCsv(rows: readonly (readonly string[])[]): string {
  return rows.map(row => row.map(value => /[",\r\n]/.test(value) ? `"${value.replace(/"/g, '""')}"` : value).join(',')).join('\r\n');
}
