import type { AttachSide, ContainerSizes, ContainerVariant, GridSettings, LinkDirection, LinkKind, LinkRelation, LinkStyle, MeridianAsset, MeridianNode, MeridianProject, NodeKind, NodeStyle, Operation, Rules, TextRun, UtilityVariant, ValueType } from './types.ts';
import { isContainerSizes, transitionContainerSize } from './container-size.ts';
import { addCanvas, addNode } from './model.ts';
import { validateChange } from './rules.ts';
import { cellAddress, gridCellBounds, gridGeometry, isGridSettings, isOrderedContainer, isEnclosingContainer, isCellChild, orderedPattern } from './cells.ts';
import { arrangeChildren, parseCsv, reflowContainedChildren } from './grid.ts';
import { OPERATION_DEFINITIONS, inputPorts, outputValueType, valueTypeForLink } from './operations.ts';
import { isGallery } from './gallery.ts';
import { revealChildren } from './reveal.ts';
import { isTextRuns, normalizeTextRuns } from './rich-text.ts';
import { isNodeTag, nodeTag } from './node-tag.ts';
import { containingFixedInterior } from './sizing.ts';

export type CreationType = 'canvas' | 'container' | 'joint' | 'text' | 'document' | 'media' | 'block' | 'module' | 'reference' | 'number' | 'input' | 'switch' | 'output' | 'utility' | 'junction';
export type { ContainerVariant } from './types.ts';
export type NodeBehavior = 'inherit' | 'free' | 'align' | 'sticky';
export const CREATION_TYPES: ReadonlyArray<{ value: CreationType; label: string; kind: NodeKind; description: string }> = [
  { value: 'canvas', label: 'Work', kind: 'canvas-link', description: 'A node that opens its own Work workspace.' },
  { value: 'container', label: 'Container', kind: 'container', description: 'Bounded. Reveal children inside the node; show its own content when collapsed.' },
  { value: 'joint', label: 'Operation', kind: 'joint', description: 'Combine Boolean or numeric inputs with an operation.' },
  { value: 'media', label: 'Media', kind: 'media', description: 'Unbounded. Display one text, file, or hyperlink, and reveal children outside this tile.' },
  { value: 'module', label: 'Module', kind: 'module', description: 'Group a reusable system of nodes and connections.' },
  { value: 'reference', label: 'Reference', kind: 'reference', description: 'Refer to another node or an addressable cell.' },
  { value: 'number', label: 'Number', kind: 'number', description: 'Provide a numeric value to the system.' },
  { value: 'input', label: 'Input', kind: 'input', description: 'Provide a Boolean value to the system.' },
  { value: 'switch', label: 'Switch', kind: 'switch', description: 'Open or close the flow of a typed value.' },
  { value: 'output', label: 'Output', kind: 'output', description: 'Display a computed Boolean or numeric result.' },
  { value: 'utility', label: 'Utility', kind: 'junction', description: 'Bend a link with a pivot or combine visual routes at a joint.' },
];
export interface NodeCreationSpec {
  canvasId: string; parentId: string; x: number; y: number; type: CreationType;
  title: string; tag?: string; width: number; height: number; sizeMode?: 'auto' | 'fixed'; style?: NodeStyle;
  positionLocked?: boolean;
  containerSizes?: ContainerSizes;
  titleRuns?: TextRun[]; textRuns?: TextRun[];
  variant?: ContainerVariant; behavior?: NodeBehavior; attachSide?: AttachSide;
  utilityVariant?: UtilityVariant;
  text?: string; asset?: MeridianAsset; contentType?: 'text' | 'asset' | 'link'; hyperlink?: {url:string;label?:string}; grid?: GridSettings; csv?: string;
  operation?: Operation; valueType?: ValueType; value?: boolean | number; closed?: boolean;
  targetId?: string; targetCell?: string; canvasTargetId?: string;
}
export interface LinkCreationSpec {
  canvasId: string; sourceId: string; targetId: string; parentId?: string;
  kind: LinkKind; relation: LinkRelation; direction?: LinkDirection; targetPort?: number;
  label?: string; style?: LinkStyle;
}

/** A new child inherits the intersection of every ancestor's permissions. */
export function creationPermissions(project: MeridianProject, parentId: string): Rules {
  const nodes = new Map(project.nodes.map(node => [node.id, node]));
  const result: Rules = {}, visited = new Set<string>(); let parent = nodes.get(parentId);
  while (parent && !visited.has(parent.id)) {
    visited.add(parent.id);
    for (const key of ['nodeKinds', 'linkKinds', 'operations'] as const) {
      const values = parent.rules?.[key]; if (!values) continue;
      const old = result[key] as readonly string[] | undefined;
      // All three vocabularies remain their original union types after intersecting.
      (result as Record<string, unknown>)[key] = values.filter(value => !old || old.includes(value));
    }
    parent = parent.parentId ? nodes.get(parent.parentId) : undefined;
  }
  return result;
}
export function creationTypes(project: MeridianProject, parentId: string) {
  const limits = creationPermissions(project, parentId).nodeKinds;
  return CREATION_TYPES.filter(type => !limits || limits.includes(type.kind) || type.value === 'container' && limits.includes('spreadsheet'));
}
export function creationOperations(project: MeridianProject, parentId: string) {
  const limits = creationPermissions(project, parentId).operations;
  return OPERATION_DEFINITIONS.filter(operation => !limits || limits.includes(operation.operation));
}
function validated(before: MeridianProject, candidate: MeridianProject): void {
  const issues = validateChange(before, candidate);
  if (issues.length) throw new Error([...new Set(issues.map(issue => issue.message))].join(' '));
}
/** Creation is atomic: failed geometry, attachment, rule, or grid checks leave the draft untouched. */
export function createNodeFromSpec(project: MeridianProject, spec: NodeCreationSpec): { project: MeridianProject; nodeId: string; canvasId?: string } {
  if (!['junction','text','document'].includes(spec.type) && !CREATION_TYPES.some(type => type.value === spec.type)) throw new Error('Choose a supported node type.');
  const parent = project.nodes.find(node => node.id === spec.parentId);
  if (!parent || parent.canvasId !== spec.canvasId) throw new Error('Choose a parent in the current Work.');
  if (typeof spec.title !== 'string') throw new Error('The visible title must be text; it may be empty.');
  if (spec.tag !== undefined && !isNodeTag(spec.tag)) throw new Error('The node tag must be text with at most 8000 characters.');
  if (spec.type === 'block' && (spec.title.trim() || spec.text?.trim() || spec.asset || spec.hyperlink || spec.contentType && spec.contentType !== 'text')) throw new Error('Block nodes contain no visible title, text, file, or hyperlink. Give the block a Tag instead.');
  if (spec.titleRuns !== undefined && !isTextRuns(spec.title, spec.titleRuns)
    || spec.textRuns !== undefined && !isTextRuns(spec.text ?? '', spec.textRuns)) throw new Error('Formatted passages need valid text offsets and supported typography.');
  if (![spec.x, spec.y, spec.width, spec.height].every(Number.isFinite) || spec.width <= 0 || spec.height <= 0) throw new Error('Node dimensions must be positive finite numbers.');
  if (spec.sizeMode !== undefined && !['auto','fixed'].includes(spec.sizeMode)) throw new Error('Choose automatic or fixed sizing.');
  if (spec.containerSizes !== undefined && !isContainerSizes(spec.containerSizes)) throw new Error('Container sizes need valid expanded and collapsed frames.');
  if (spec.positionLocked !== undefined && typeof spec.positionLocked !== 'boolean') throw new Error('Position lock must be Boolean.');
  if (spec.utilityVariant !== undefined && (!['utility', 'junction'].includes(spec.type) || !['pivot', 'joint'].includes(spec.utilityVariant))) throw new Error('Choose Pivot or Joint for a Utility node.');
  if (spec.variant !== undefined && !['spatial', 'contained', 'horizontal', 'vertical', 'grid', 'spreadsheet', 'gallery', 'window'].includes(spec.variant)) throw new Error('Choose a supported container variant.');
  const variant = spec.variant === 'spatial' ? 'contained' : spec.variant ?? 'contained';
  if (variant === 'window' && (!spec.grid || spec.grid.windowColumns === undefined || spec.grid.windowRows === undefined)) throw new Error('Define the full grid and visible window dimensions.');
  if (spec.variant === 'gallery' && (spec.grid !== undefined || spec.csv !== undefined)) throw new Error('A gallery displays its child objects one at a time. Choose Grid or Spreadsheet to create addressable cells.');
  if (spec.behavior !== undefined && !['inherit', 'free', 'align', 'sticky'].includes(spec.behavior)) throw new Error('Choose a supported movement behavior.');
  const candidate = structuredClone(project);
  transitionContainerSize(candidate.nodes.find(node => node.id === parent.id)!, true, undefined, { preserveCurrentRecord: !!containingFixedInterior(candidate, parent) });
  const kind = spec.type === 'canvas' ? 'canvas-link' : spec.type === 'utility' ? 'junction' : spec.type === 'container' && spec.variant === 'spreadsheet' ? 'spreadsheet' : spec.type;
  let newCanvasId: string | undefined, targetId = spec.targetId;
  if (spec.type === 'canvas') {
    if (spec.canvasTargetId) {
      const target = candidate.nodes.find(node => node.id === spec.canvasTargetId);
      if (!target || target.kind !== 'canvas' || !candidate.canvases.some(canvas => canvas.rootId === target.id)) throw new Error('Choose an included Work workspace. A saved observation alone cannot open a Work.');
      targetId = target.id;
    } else {
      const canvas = addCanvas(candidate, spec.title.trim() || spec.tag?.trim() || 'Work'); newCanvasId = canvas.id; targetId = canvas.rootId;
    }
  }
  const node = addNode(candidate, spec.canvasId, kind, spec.title.trim());
  if (spec.containerSizes) node.containerSizes = structuredClone(spec.containerSizes);
  node.tag = spec.tag?.trim() || nodeTag(node);
  if (spec.titleRuns !== undefined) {
    const trimOffset = spec.title.length - spec.title.trimStart().length;
    node.titleRuns = normalizeTextRuns(node.title, spec.titleRuns.map(run => ({ ...run, start: run.start - trimOffset, end: run.end - trimOffset })));
  }
  if (spec.textRuns !== undefined) node.textRuns = structuredClone(spec.textRuns);
  Object.assign(node, { parentId: spec.parentId, x: spec.x, y: spec.y, width: spec.width, height: spec.height, childLayout: 'spatial', placement: spec.behavior === 'align' || spec.behavior === 'sticky' ? 'edge' : 'free' });
  const freeformParent = ['contained', 'spatial'].includes(parent.containerVariant ?? '') || parent.containerVariant === undefined && (!parent.childArrangement || parent.childArrangement === 'free' || parent.childArrangement === 'radial');
  const explicitParentEdge = parent.kind !== 'canvas' && !isEnclosingContainer(parent) && !isOrderedContainer(parent) && !isGallery(parent) && freeformParent && (spec.behavior === 'align' || spec.behavior === 'sticky');
  // Legacy radial Freeform parents omitted their variant. Make that existing
  // pattern explicit so reflow preserves manual children alongside auto ones.
  if (explicitParentEdge && parent.containerVariant === undefined && parent.childArrangement === 'radial') candidate.nodes.find(node => node.id === parent.id)!.containerVariant = 'contained';
  // An explicit exterior edge is authored placement. Automatic radial defaults
  // still apply to ordinary children; bounded and ordered owners keep authority.
  if(parent.kind!=='canvas')node.childPosition=explicitParentEdge?'manual':'auto';
  node.sizeMode=spec.sizeMode??(kind==='block'?'fixed':'auto');
  if (kind === 'junction') {
    node.utilityVariant = spec.utilityVariant ?? (spec.type === 'junction' ? 'joint' : 'pivot');
    node.sizeMode = 'fixed';
    node.style = { shape: 'circle', ...node.style };
  }
  if (spec.behavior && spec.behavior !== 'inherit') node.snapping = { stickyEdges: spec.behavior === 'sticky', smartAlign: spec.behavior === 'align', snapToGrid: false };
  if (spec.attachSide) node.attachSide = spec.attachSide;
  if (spec.style) node.style = { ...node.style, ...structuredClone(spec.style) };
  if (spec.style?.shape === 'square' || spec.style?.shape === 'circle') node.height = node.width;
  if (spec.text !== undefined && kind !== 'block') node.text = spec.text;
  if (kind !== 'block') node.contentType=spec.contentType??(spec.asset?'asset':spec.hyperlink?'link':'text');
  if (spec.contentType !== undefined) {
    if (!['text','asset','link'].includes(spec.contentType)) throw new Error('Choose Text, Media or document, or Hyperlink content.');
    if (spec.contentType !== 'text' && spec.text?.trim() || spec.contentType !== 'asset' && spec.asset || spec.contentType !== 'link' && spec.hyperlink) throw new Error('A node displays one content item. Choose its text, file, or hyperlink.');
    node.contentType = spec.contentType;
  }
  if (spec.hyperlink) node.hyperlinks = [structuredClone(spec.hyperlink)];
  if (targetId) node.targetId = targetId;
  if (spec.targetCell) node.targetCell = spec.targetCell;
  if ((kind !== 'canvas-link' && kind !== 'junction') || spec.variant !== undefined) {
    node.collapsed = false;
    node.containerVariant = variant;
    node.childLayout = spec.type === 'container' || spec.type === 'module' ? 'contained' : 'spatial';
    node.childArrangement = variant === 'horizontal' ? 'row' : variant === 'vertical' ? 'column' : ['grid', 'spreadsheet', 'window'].includes(variant) ? 'grid' : variant === 'gallery' ? 'free' : 'radial';
    if (isOrderedContainer(node)) node.grid = structuredClone(spec.grid ?? { columns: variant === 'vertical' ? 1 : 3, rows: variant === 'horizontal' ? 1 : 3, cellWidth: 200, cellHeight: 120, gap: 0, showHeaders: false, fillOrder: 'row' });
  }
  if (spec.type === 'joint') {
    const operations = creationOperations(project, spec.parentId);
    node.operation = spec.operation ?? operations[0]?.operation;
    if (!node.operation) throw new Error('This parent does not allow any joint operations.');
  }
  if (spec.valueType) node.valueType = spec.valueType;
  if (spec.value !== undefined) node.value = spec.value;
  if (spec.closed !== undefined) node.closed = spec.closed;
  if (spec.asset) {
    if (typeof spec.asset.base64 !== 'string' || !/^(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$/.test(spec.asset.base64)) throw new Error('The attachment data is invalid.');
    const padding = spec.asset.base64.endsWith('==') ? 2 : spec.asset.base64.endsWith('=') ? 1 : 0;
    if (spec.asset.base64.length / 4 * 3 - padding > 16 * 1024 * 1024) throw new Error('Attachments must be 16 MiB or smaller.');
    candidate.assets.push(structuredClone(spec.asset)); node.assetId = spec.asset.id;
  }
  if (spec.grid || spec.csv) {
    if (!node.containerVariant || !['horizontal','vertical','grid','spreadsheet','window'].includes(node.containerVariant)) throw new Error('Grid settings require an ordered Display Pattern.');
    const rows = spec.csv ? parseCsv(spec.csv) : undefined;
    node.grid = structuredClone(spec.grid ?? { columns: 3, rows: 3, cellWidth: 200, cellHeight: 100, gap: 16, showHeaders: true });
    if (rows) { node.grid.columns = Math.max(node.grid.columns, ...rows.map(row => row.length)); node.grid.rows = Math.max(node.grid.rows, rows.length); }
    if (!isGridSettings(node.grid)) throw new Error('Grid dimensions must stay within the supported cell limits.');
    node.childArrangement = isOrderedContainer(node) ? orderedPattern(node) : 'grid';
    if (node.childLayout === 'contained') { const bounds = gridGeometry(node,candidate); node.width = Math.max(node.width, bounds.width); node.height = Math.max(node.height, bounds.height); }
    if (rows) for (const [row, values] of rows.entries()) for (const [column, text] of values.entries()) {
      if (!text) continue;
      const child = addNode(candidate, spec.canvasId, 'media', cellAddress(column + 1, row + 1));
      child.parentId = node.id; child.cell = { column: column + 1, row: row + 1 }; child.text = text; child.contentType='text';child.childPosition='auto';child.style = { variant: 'label', fontSize: 14, titleSize: 12 }; child.placement = 'free';
      const bounds = gridCellBounds(node, child.cell,candidate); Object.assign(child, bounds);
    }
  }
  if (isCellChild(node) && parent.grid && !isGallery(parent) && !isOrderedContainer(parent)) {
    let cell: MeridianNode['cell'];
    const occupied = new Set(candidate.nodes.filter(child => child.parentId === parent.id && child.cell).map(child => cellAddress(child.cell!)));
    for (let row = 1; row <= parent.grid.rows && !cell; row++) for (let column = 1; column <= parent.grid.columns; column++) if (!occupied.has(cellAddress(column, row))) { cell = { column, row }; break; }
    if (!cell && parent.containerVariant==='grid'){
      const current=candidate.nodes.find(item=>item.id===parent.id)!;
      current.grid={...current.grid!,rows:current.grid!.rows+1};
      cell={column:1,row:current.grid.rows};
    }
    if (!cell) throw new Error('This grid is full. Increase its rows or columns before adding another child.');
    node.cell = cell;
    if (parent.kind === 'spreadsheet' || parent.childLayout === 'contained') { const bounds = gridCellBounds(parent, cell,candidate); node.x = bounds.x; node.y = bounds.y; }
  }
  if (isCellChild(node) && isOrderedContainer(parent) && parent.grid && ['spreadsheet', 'window'].includes(parent.containerVariant ?? parent.kind)) {
    const childCount = candidate.nodes.filter(child => child.parentId === parent.id && (isCellChild(child) || child.cell)).length;
    if (childCount > parent.grid.columns * parent.grid.rows) throw new Error('This grid is full. Increase its rows or columns before adding another child.');
  }
  if (node.grid && (node.kind === 'spreadsheet' || node.childLayout === 'contained')) { const bounds = gridGeometry(node,candidate); node.width = Math.max(node.width, bounds.width); node.height = Math.max(node.height, bounds.height); }
  if (isOrderedContainer(node) || node.grid && candidate.nodes.some(child => child.parentId === node.id)) Object.assign(candidate, arrangeChildren(candidate, node.id, isOrderedContainer(node) ? orderedPattern(node) : 'grid').project);
  if (isOrderedContainer(parent)) Object.assign(candidate, arrangeChildren(candidate, parent.id, orderedPattern(parent)).project);
  else if(parent.kind!=='canvas'&&!isGallery(parent)) Object.assign(candidate,reflowContainedChildren(candidate,parent.id));
  if (explicitParentEdge) Object.assign(candidate, revealChildren(candidate, parent.id, true, { preserveValidPositions: true }));
  if(['container','spreadsheet','module'].includes(node.kind)&&!spec.csv&&(spec.text?.trim()||spec.asset||spec.hyperlink))transitionContainerSize(node,false,undefined,{preserveCurrentRecord:!!containingFixedInterior(candidate,node)});
  // Initial placement remains free even for a new locked node. Pin its final
  // location only after the destination owner has applied its display pattern.
  if (spec.positionLocked) {
    const created = candidate.nodes.find(item => item.id === node.id)!;
    created.positionLocked = true; created.childPosition = 'manual'; created.placement = 'free';
  }
  validated(project, candidate);
  return { project: candidate, nodeId: node.id, ...(newCanvasId ? { canvasId: newCanvasId } : {}) };
}

export function availableLinkPorts(project: MeridianProject, sourceId: string, targetId: string, kind: LinkKind): Array<{ value: number; label: string }> {
  if (kind === 'visual') return [];
  const source = project.nodes.find(node => node.id === sourceId), target = project.nodes.find(node => node.id === targetId), valueType = valueTypeForLink(kind);
  if (!source || !target || outputValueType(project, source) !== valueType && source.kind !== 'reference') return [];
  const occupied = new Set(project.links.filter(link => link.targetId === targetId && link.kind !== 'visual').map(link => link.targetPort ?? 0));
  return inputPorts(target).flatMap((port, index) => port.type === valueType && !occupied.has(index) ? [{ value: index, label: `${port.name} · ${port.type === 'boolean' ? 'Boolean' : 'Number'}` }] : []);
}
export function createLinkFromSpec(project: MeridianProject, spec: LinkCreationSpec): { project: MeridianProject; linkId: string } {
  const candidate = structuredClone(project), id = crypto.randomUUID();
  if (!spec.sourceId || !spec.targetId) throw new Error('Choose both endpoints for the link.');
  if (spec.sourceId === spec.targetId) throw new Error('Choose a different destination node.');
  if (spec.kind !== 'visual' && spec.direction !== undefined && spec.direction !== 'forward') throw new Error('Computed links pass values from source to destination.');
  candidate.links.push({ id, ...spec }); validated(project, candidate);
  return { project: candidate, linkId: id };
}
