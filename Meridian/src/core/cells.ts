import type { CellLocation, ChildArrangement, GridSettings, MeridianNode, MeridianProject, Point, SavedSnapshot } from './types.ts';
import { boundedChildInterior, containingFixedInterior, intrinsicContentSize, nodeBorderWidth, nodeSizingMode, nodeSpacing } from './sizing.ts';
import { fixedExpansionTrayContent } from './expansion-tray-frame.ts';

export const GRID_LIMITS = Object.freeze({ axis: 512, cells: 16384, minCell: 36, maxCell: 10000, maxGap: 1000 });
export const CHILD_ARRANGEMENTS: readonly ChildArrangement[] = ['free', 'grid', 'row', 'column', 'radial', 'stack'];
const record = (value: unknown): value is Record<string, unknown> => !!value && typeof value === 'object' && !Array.isArray(value);
const bounded = (value: unknown, min: number, max: number): value is number => typeof value === 'number' && Number.isFinite(value) && value >= min && value <= max;
const axis = (value: unknown): value is number => bounded(value, 1, GRID_LIMITS.axis) && Number.isInteger(value);

export function columnLabel(column: number): string {
  if (!Number.isInteger(column) || column < 1 || column > GRID_LIMITS.axis) throw new Error('Cell columns must be between 1 and 512.');
  let value = column, result = '';
  while (value) { value--; result = String.fromCharCode(65 + value % 26) + result; value = Math.floor(value / 26); }
  return result;
}
export function cellAddress(cell: Pick<CellLocation, 'column' | 'row'>): string;
export function cellAddress(column: number, row: number): string;
export function cellAddress(location: Pick<CellLocation, 'column' | 'row'> | number, row?: number): string {
  const cell = typeof location === 'number' ? { column: location, row: row! } : location;
  if (!axis(cell.column) || !axis(cell.row)) throw new Error('Cell coordinates must be between 1 and 512.');
  return `${cell.column}:${cell.row}`;
}
export function parseCellAddress(value: string): CellLocation | undefined {
  const coordinate = /^([1-9][0-9]*):([1-9][0-9]*)$/.exec(value.trim());
  if (coordinate) {
    const column = Number(coordinate[1]), row = Number(coordinate[2]);
    return axis(column) && axis(row) ? { column, row } : undefined;
  }
  // Legacy spreadsheet aliases stay readable so existing references keep their identity.
  const match = /^([A-Za-z]+)([1-9][0-9]*)$/.exec(value.trim());
  if (!match) return undefined;
  let column = 0;
  for (const letter of match[1]!.toUpperCase()) { column = column * 26 + letter.charCodeAt(0) - 64; if (column > GRID_LIMITS.axis) return undefined; }
  const row = Number(match[2]);
  return axis(column) && axis(row) ? { column, row } : undefined;
}
export function isCellName(value: unknown): value is string {
  return typeof value === 'string' && value === value.trim() && /^[A-Za-z_][A-Za-z0-9_.-]{0,63}$/.test(value) && !/^[A-Za-z]+[1-9][0-9]*$/.test(value);
}
export function isCellReference(value: unknown): value is string {
  return typeof value === 'string' && value === value.trim() && value.length <= 64 && (!!parseCellAddress(value) || isCellName(value));
}
export function isGridSettings(value: unknown): value is GridSettings {
  return record(value) && Object.keys(value).every(key => ['columns', 'rows', 'cellWidth', 'cellHeight', 'gap', 'showHeaders', 'fillOrder', 'windowColumns', 'windowRows', 'scrollDirection'].includes(key)) &&
    axis(value.columns) && axis(value.rows) && value.columns * value.rows <= GRID_LIMITS.cells &&
    bounded(value.cellWidth, GRID_LIMITS.minCell, GRID_LIMITS.maxCell) && bounded(value.cellHeight, GRID_LIMITS.minCell, GRID_LIMITS.maxCell) &&
    (value.gap === undefined || bounded(value.gap, 0, GRID_LIMITS.maxGap)) && (value.showHeaders === undefined || typeof value.showHeaders === 'boolean') &&
    (value.fillOrder === undefined || ['row', 'column'].includes(value.fillOrder as string)) &&
    (value.windowColumns === undefined || axis(value.windowColumns)) &&
    (value.windowRows === undefined || axis(value.windowRows)) &&
    (value.scrollDirection === undefined || ['horizontal', 'vertical'].includes(value.scrollDirection as string));
}
export function isCellLocation(value: unknown): value is CellLocation {
  return record(value) && Object.keys(value).every(key => ['column', 'row', 'name'].includes(key)) && axis(value.column) && axis(value.row) &&
    (value.name === undefined || isCellName(value.name));
}

/** Cells are ordinary immediate child nodes, including document or nested spreadsheet nodes. */
export function resolveGridCell(project: MeridianProject, parentId: string, address: string, options: { savedOnly?: boolean } = {}): MeridianNode | undefined {
  if (!isCellReference(address)) return undefined;
  const parent = options.savedOnly ? project.snapshots[parentId]?.node : project.nodes.find(node => node.id === parentId);
  if (!parent) return undefined;
  const children = options.savedOnly ? Object.values(project.snapshots).map(snapshot => snapshot.node) : project.nodes;
  const coordinate = parseCellAddress(address), name = address.toLocaleLowerCase();
  return children.find(node => node.parentId === parentId && node.canvasId === parent.canvasId && node.cell &&
    (coordinate ? node.cell.column === coordinate.column && node.cell.row === coordinate.row : node.cell.name?.toLocaleLowerCase() === name));
}
export interface ReferenceTarget { node: MeridianNode; snapshot?: SavedSnapshot; saved: boolean }
/** External cell identity, content and value all come from the same committed snapshot set. */
export function resolveReferenceTarget(project: MeridianProject, reference: MeridianNode): ReferenceTarget | undefined {
  if (reference.kind !== 'reference' || !reference.targetId) return undefined;
  const parent = project.nodes.find(node => node.id === reference.targetId);
  if (parent?.canvasId === reference.canvasId) {
    const node = reference.targetCell ? resolveGridCell(project, parent.id, reference.targetCell) : parent;
    return node ? { node, saved: false } : undefined;
  }
  const parentSnapshot = Object.hasOwn(project.snapshots, reference.targetId) ? project.snapshots[reference.targetId] : undefined;
  if (!parentSnapshot || parentSnapshot.node.id !== reference.targetId || parentSnapshot.node.canvasId === reference.canvasId) return undefined;
  const node = reference.targetCell ? resolveGridCell(project, reference.targetId, reference.targetCell, { savedOnly: true }) : parentSnapshot.node;
  const snapshot = node && Object.hasOwn(project.snapshots, node.id) ? project.snapshots[node.id] : undefined;
  return snapshot && snapshot.node.id === node?.id ? { node: snapshot.node, snapshot, saved: true } : undefined;
}

/** Galleries are ordered collections too, but embed one object rather than plotting cell tiles. */
export function isOrderedContainer(parent: MeridianNode): boolean {
  return parent.kind === 'spreadsheet' || ['horizontal', 'vertical', 'grid', 'spreadsheet', 'window'].includes(parent.containerVariant ?? '');
}
/** Routing utilities overlay their owner's surface; they never consume a child cell. */
export function isCellChild(node: MeridianNode): boolean { return node.kind !== 'junction'; }
/** Older addressed utilities keep their aliases and reserve their former coordinates. */
export function orderedCellAxes(parent: MeridianNode, children: readonly MeridianNode[]) {
  if (parent.kind === 'spreadsheet' || ['spreadsheet', 'window'].includes(parent.containerVariant ?? ''))
    return { columns: parent.grid?.columns ?? 3, rows: parent.grid?.rows ?? 3 };
  const addressedUtilities = children.filter(child => !isCellChild(child) && child.cell);
  const count = children.filter(isCellChild).length + addressedUtilities.length;
  const reservedColumns = Math.max(1, ...addressedUtilities.map(child => child.cell!.column));
  const reservedRows = Math.max(1, ...addressedUtilities.map(child => child.cell!.row));
  const side = Math.max(1, Math.ceil(Math.sqrt(count)), reservedColumns, reservedRows);
  return {
    columns: parent.containerVariant === 'horizontal' ? Math.max(1, count, reservedColumns) : parent.containerVariant === 'vertical' ? reservedColumns : side,
    rows: parent.containerVariant === 'vertical' ? Math.max(1, count, reservedRows) : parent.containerVariant === 'horizontal' ? reservedRows : side,
  };
}
/** Formal containers enclose their children; spatial disclosure remains available to ordinary nodes. */
export function isEnclosingContainer(parent: MeridianNode): boolean {
  return ['container', 'spreadsheet', 'module'].includes(parent.kind);
}
export function orderedPattern(parent: MeridianNode): ChildArrangement {
  return parent.containerVariant === 'horizontal' ? 'row' : parent.containerVariant === 'vertical' ? 'column' : 'grid';
}

/** Shared world-space geometry for the editor and portable viewer. */
export function gridGeometry(parent: MeridianNode, project?: MeridianProject) {
  let grid = parent.grid ?? { columns: 3, rows: 3, cellWidth: 200, cellHeight: 100, gap: 0, showHeaders: true };
  if (project && ['grid', 'horizontal', 'vertical'].includes(parent.containerVariant ?? '')) {
    const children = project.nodes.filter(child => child.parentId === parent.id && child.canvasId === parent.canvasId);
    grid = { ...grid, ...orderedCellAxes(parent, children) };
  }
  const ordered = isOrderedContainer(parent), enclosed = isEnclosingContainer(parent), spacing = nodeSpacing(parent.style), padding = enclosed ? spacing.childPadding : 0, border = enclosed ? nodeBorderWidth(parent) : 0, sideInset = padding + border;
  const headerWidth = ordered || grid.showHeaders === false ? 0 : 34, headerHeight = ordered || grid.showHeaders === false ? 0 : 26;
  const gap = ordered ? 0 : grid.gap ?? 0;
  const visibleColumns = parent.containerVariant === 'window' ? Math.min(grid.columns, grid.windowColumns ?? 3) : grid.columns;
  const visibleRows = parent.containerVariant === 'window' ? Math.min(grid.rows, grid.windowRows ?? 3) : grid.rows;
  if (enclosed) {
    const interior = boundedChildInterior(project, parent, {}, { x: parent.x, y: parent.y, width: parent.width, height: parent.height });
    const cellWidth = Math.max(Number.MIN_VALUE, (interior.width - headerWidth - (visibleColumns - 1) * gap) / visibleColumns);
    const cellHeight = Math.max(Number.MIN_VALUE, (interior.height - headerHeight - (visibleRows - 1) * gap) / visibleRows);
    return { ...grid, cellWidth, cellHeight, gap, headerWidth, headerHeight, contentHeight: interior.y - parent.y, padding, sideInset, visibleColumns, visibleRows,
      left: interior.x + headerWidth, top: interior.y + headerHeight, width: parent.width, height: parent.height };
  }
  if (parent.expansionTray) {
    const content = fixedExpansionTrayContent(parent), cellWidth = content ? (content.width - (visibleColumns - 1) * gap) / visibleColumns : grid.cellWidth,
      cellHeight = content ? (content.height - (visibleRows - 1) * gap) / visibleRows : grid.cellHeight;
    return { ...grid, cellWidth, cellHeight, gap, headerWidth, headerHeight, contentHeight: 0, padding, sideInset, visibleColumns, visibleRows,
      left: parent.x + parent.expansionTray.offset.x, top: parent.y + parent.expansionTray.offset.y,
      width: visibleColumns * cellWidth + (visibleColumns - 1) * gap,
      height: visibleRows * cellHeight + (visibleRows - 1) * gap };
  }
  const width = 2 * sideInset + headerWidth + visibleColumns * grid.cellWidth + (visibleColumns - 1) * gap;
  // A fitted Media cover scales its content into the authored tile. Reserving
  // its original text height here would consume the exterior Window viewport
  // again every time a higher enclosure normalizes the composition.
  const contentHeight = !enclosed && nodeSizingMode(parent) === 'fixed' ? parent.height
    : intrinsicContentSize(project, ordered && enclosed ? { ...parent, width, collapsed: false } : parent).height;
  const topOffset = enclosed ? contentHeight - spacing.padding - border + spacing.contentGap + headerHeight : Math.max(contentHeight, nodeSizingMode(parent) === 'fixed' ? parent.height : 0) + 28 + headerHeight;
  const scope = ordered && !enclosed ? containingFixedInterior(project, parent) : undefined;
  if (scope) {
    const left = Math.max(scope.x, Math.min(parent.x + (parent.childOffset?.x ?? 0), scope.x + scope.width - .001));
    const top = Math.max(scope.y, Math.min(parent.y + topOffset + (parent.childOffset?.y ?? 0), scope.y + scope.height - .001));
    const firstChild = project?.nodes.find(child => child.parentId === parent.id && isCellChild(child));
    let cellWidth = Math.min(grid.cellWidth, firstChild?.width ?? grid.cellWidth, (scope.x + scope.width - left) / visibleColumns), cellHeight = Math.min(grid.cellHeight, firstChild?.height ?? grid.cellHeight, (scope.y + scope.height - top) / visibleRows);
    const shape = firstChild?.style?.shape;
    if (shape === 'circle' || shape === 'square') cellWidth = cellHeight = Math.min(cellWidth, cellHeight);
    return { ...grid, cellWidth, cellHeight, gap: 0, headerWidth, headerHeight, contentHeight, padding, sideInset, visibleColumns, visibleRows,
      left, top, width: visibleColumns * cellWidth, height: top - parent.y + visibleRows * cellHeight };
  }
  return { ...grid, gap, headerWidth, headerHeight, contentHeight, padding, sideInset, visibleColumns, visibleRows,
    left: parent.x + sideInset + headerWidth + (enclosed ? 0 : parent.childOffset?.x ?? 0), top: parent.y + topOffset + (enclosed ? 0 : parent.childOffset?.y ?? 0),
    width,
    height: topOffset + visibleRows * grid.cellHeight + (visibleRows - 1) * gap + sideInset };
}
export function gridCellBounds(parent: MeridianNode, cell: Pick<CellLocation, 'column' | 'row'>, project?: MeridianProject) {
  const geometry = gridGeometry(parent, project);
  return { x: geometry.left + (cell.column - 1) * (geometry.cellWidth + geometry.gap),
    y: geometry.top + (cell.row - 1) * (geometry.cellHeight + geometry.gap), width: geometry.cellWidth, height: geometry.cellHeight };
}

