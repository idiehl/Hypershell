import { displayBounds, navigationIndex } from './navigation.ts';
import type { MeridianAsset, MeridianCanvas, MeridianLink, MeridianNode, MeridianProject, Point } from './types.ts';
import { cloneProject } from './model.ts';
import { isProjectShape, validateChange, validateProject } from './rules.ts';
import { CANVAS_STYLE_KEYS, LINK_STYLE_KEYS, NODE_STYLE_KEYS, isCanvasStyle, isLinkStyle, isNodeStyle, nodeAssetIds } from './appearance.ts';
import { CHILD_ARRANGEMENTS, isCellLocation, isCellReference, isEnclosingContainer, isGridSettings, isOrderedContainer, orderedPattern } from './cells.ts';
import { reflowContainedChildren } from './grid.ts';
import { isGallery } from './gallery.ts';
import { activeNodeContent, hidesOwnContent, isNodeContentItems } from './content.ts';
import { isTextRuns, rebaseTextRuns } from './rich-text.ts';
import { isNodeTag, nodeTag } from './node-tag.ts';
import { isContainerSizes } from './container-size.ts';
import { isRetiredBlockContent } from './retired-content.ts';
import { isExpansionTray } from './expansion-tray-state.ts';
import { valueTypeForLink } from './operations.ts';
import { isSmartLinkSettings } from './smart-link-state.ts';
import { reflowSmartLinks } from './smart-links.ts';

export type AuthoringRef = string;
interface Declaration { key: string; existingId?: string; unset?: string[] }
export type CanvasRecipe = Declaration & Partial<Pick<MeridianCanvas, 'title' | 'snapping' | 'style'>> & { backgroundAsset?: AuthoringRef };
export type NodeRecipe = Declaration & Partial<Omit<MeridianNode, 'id' | 'canvasId' | 'parentId' | 'assetId' | 'targetId' | 'dependencyIds' | 'kind'>> & {
  kind?: Exclude<MeridianNode['kind'], 'canvas'>;
  canvas?: AuthoringRef;
  /** null explicitly returns a node to its canvas root. */
  parent?: AuthoringRef | null;
  asset?: AuthoringRef;
  backgroundAsset?: AuthoringRef;
  target?: AuthoringRef;
  dependencies?: AuthoringRef[];
};
export type AssetRecipe = Declaration & Partial<Omit<MeridianAsset, 'id'>>;
export type LinkRecipe = Declaration & Partial<Omit<MeridianLink, 'id' | 'canvasId' | 'parentId' | 'sourceId' | 'targetId'>> & {
  canvas?: AuthoringRef; source?: AuthoringRef; target?: AuthoringRef;
  /** The node whose expansion reveals this connection; null restores inferred ownership. */
  parent?: AuthoringRef | null;
};
export interface LayoutRecipe {
  kind: 'row' | 'column' | 'grid' | 'radial';
  nodes: AuthoringRef[];
  x?: number; y?: number; gap?: number; columns?: number;
  radius?: number; center?: Point;
  /** Degrees clockwise from the positive x axis; radial layouts default to -90. */
  startAngle?: number;
}
export interface AuthoringRecipe {
  version: 1; title?: string; entryCanvas?: AuthoringRef;
  canvases?: CanvasRecipe[]; nodes?: NodeRecipe[]; assets?: AssetRecipe[]; links?: LinkRecipe[]; layouts?: LayoutRecipe[];
  /** Previously exported aliases. Declaring a bound key edits the same entity. */
  bindings?: Partial<AuthoringAliases>;
  /** Explicit existing branches and links to remove before applying declarations. */
  remove?: { nodes?: AuthoringRef[]; links?: AuthoringRef[] };
  routes?: RouteRecipe[];
}
export interface RouteWaypoint {
  key: string; x: number; y: number; variant?: 'pivot' | 'joint';
  width?: number; height?: number; style?: MeridianNode['style'];
}
/** A semantic connection passing through real routing utilities, in world coordinates. */
export type RouteRecipe = Omit<LinkRecipe, 'points' | 'unset'> & {
  source: AuthoringRef; target: AuthoringRef; via: RouteWaypoint[];
};
export interface AuthoringAliases {
  canvases: Record<string, string>; roots: Record<string, string>; nodes: Record<string, string>;
  assets: Record<string, string>; links: Record<string, string>;
}
export interface AuthoringDiagnostic {
  code: string; message: string; severity: 'warning' | 'error'; path?: string;
  canvasId?: string; nodeId?: string; nodeIds?: string[]; linkId?: string;
}
export interface Bounds { x: number; y: number; width: number; height: number }
export interface CanvasInspection {
  id: string; title: string; nodeCount: number; visibleNodeCount: number; linkCount: number;
  imageCount: number; documentCount: number; containerCount: number; bounds: Bounds | null;
  links: { visual: number; functional: number; numeric: number };
  styles: { styledNodeCount: number; fontSizes: number[]; fontFamilies: string[]; fillColors: string[]; variants: string[] };
  media: { assetIds: string[]; missingAssetNodeIds: string[] };
}
export interface InspectionReport {
  summary: { canvasCount: number; nodeCount: number; linkCount: number; assetCount: number; imageCount: number; styledNodeCount: number; warningCount: number };
  canvases: CanvasInspection[]; warnings: AuthoringDiagnostic[];
}
export interface AuthoringResult { project: MeridianProject; aliases: AuthoringAliases; report: InspectionReport }
export interface AuthoringInventory {
  project: { id: string; title: string; revision: number; entryCanvasId: string };
  aliases: AuthoringAliases;
  canvases: { id: string; title: string; rootId: string; alias: string }[];
  nodes: { id: string; alias: string; canvasId: string; parentId: string | null; tag: string; title: string; kind: MeridianNode['kind'];
    depth: number; childCount: number; visible: boolean; collapsed: boolean; bounds: Bounds; branchBounds: Bounds; text?: string; assetId?: string; targetId?: string; contentItems?: MeridianNode['contentItems']; showTitle?: boolean; smartLink?: MeridianNode['smartLink']; displayPattern?: string; style?: MeridianNode['style'] }[];
  links: { id: string; alias: string; canvasId: string; sourceId: string; targetId: string; parentId?: string; label?: string; kind: MeridianLink['kind']; relation: MeridianLink['relation'] }[];
  assets: { id: string; alias: string; name: string; mime: string; bytes: number }[];
}
export class AuthoringError extends Error {
  diagnostics: AuthoringDiagnostic[];
  constructor(diagnostics: AuthoringDiagnostic[]) {
    super(diagnostics.map(issue => `${issue.path ? `${issue.path}: ` : ''}${issue.message}`).join('\n'));
    this.name = 'AuthoringError'; this.diagnostics = diagnostics;
  }
}

const own = (object: object, key: PropertyKey): boolean => Object.hasOwn(object, key);
const record = (value: unknown): value is Record<string, unknown> => !!value && typeof value === 'object' && !Array.isArray(value);
const reject = (path: string, message: string, code = 'invalid-recipe'): never => { throw new AuthoringError([{ code, message, path, severity: 'error' }]); };
function object(value: unknown, path: string, keys: readonly string[]): asserts value is Record<string, unknown> {
  if (!record(value)) return reject(path, 'Expected an object.');
  for (const key of Object.keys(value)) if (!keys.includes(key)) reject(`${path}.${key}`, 'Unknown field; authoring accepts typed fields only.');
}
function text(value: unknown, path: string, nonempty = false): asserts value is string {
  if (typeof value !== 'string' || nonempty && !value.trim()) reject(path, nonempty ? 'Expected nonempty text.' : 'Expected text.');
}
/** Avoid a repeated quartet regexp: V8 can exhaust its regexp stack on valid multi-MiB assets. */
function canonicalBase64(value: string): boolean {
  const padding = value.endsWith('==') ? 2 : value.endsWith('=') ? 1 : 0;
  if (value.length % 4 !== 0 || /[^A-Za-z0-9+/=]/.test(value)
    || value.indexOf('=') !== (padding ? value.length - padding : -1)) return false;
  // The round trip retains strict rejection of nonzero unused trailing bits.
  try { return btoa(atob(value)) === value; } catch { return false; }
}
function number(value: unknown, path: string, minimum = -1e9, maximum = 1e9): asserts value is number {
  if (typeof value !== 'number' || !Number.isFinite(value) || value < minimum || value > maximum) reject(path, `Expected a finite number between ${minimum} and ${maximum}.`);
}
function boolean(value: unknown, path: string): asserts value is boolean {
  if (typeof value !== 'boolean') reject(path, 'Expected a Boolean.');
}
function point(value: unknown, path: string, normalized = false): asserts value is Point {
  object(value, path, ['x', 'y']);
  number(value.x, `${path}.x`, normalized ? 0 : -1e9, normalized ? 1 : 1e9);
  number(value.y, `${path}.y`, normalized ? 0 : -1e9, normalized ? 1 : 1e9);
}
function snapping(value: unknown, path: string): void {
  object(value, path, ['stickyEdges', 'snapToGrid', 'smartAlign']);
  for (const key of Object.keys(value)) boolean(value[key], `${path}.${key}`);
}
function rules(value: unknown, path: string): void {
  object(value, path, ['nodeKinds', 'linkKinds', 'operations']);
  for (const key of Object.keys(value)) if (!Array.isArray(value[key]) || !(value[key] as unknown[]).every(entry => typeof entry === 'string')) reject(`${path}.${key}`, 'Expected an array of supported names.');
}
const declarationKeys = ['key', 'existingId', 'unset'];
const nodeFields = ['title', 'tag', 'titleRuns', 'showTitle', 'contentItems', 'smartLink', 'x', 'y', 'width', 'height', 'positionLocked', 'sizeMode', 'containerSizes', 'retiredBlockContent', 'contentType', 'childPosition', 'childOffset', 'expansionTray', 'collapsed', 'text', 'textRuns', 'attachments', 'hyperlinks', 'value', 'valueType', 'closed', 'operation', 'exposed', 'rules', 'snapping', 'style', 'canvasTarget', 'containerVariant', 'utilityVariant', 'remoteLinks', 'childLayout', 'placement', 'attachSide', 'grid', 'cell', 'targetCell', 'childArrangement'] as const;
const linkFields = ['kind', 'relation', 'direction', 'targetPort', 'sourceAnchor', 'targetAnchor', 'points', 'label', 'style'] as const;
const nodeKinds = ['container', 'spreadsheet', 'module', 'media', 'text', 'image', 'document', 'input', 'number', 'switch', 'output', 'joint', 'junction', 'reference', 'canvas-link'];

function declarations<T extends Declaration>(value: T[] | undefined, path: string, fields: readonly string[]): T[] {
  if (value === undefined) return [];
  if (!Array.isArray(value) || value.length > 10000) reject(path, 'Expected an array with at most 10000 declarations.');
  const keys = new Set<string>(), ids = new Set<string>();
  value.forEach((item, index) => {
    const location = `${path}[${index}]`; object(item, location, [...declarationKeys, ...fields]);
    text(item.key, `${location}.key`, true);
    if (!/^[A-Za-z][A-Za-z0-9._:/-]{0,119}$/.test(item.key)) reject(`${location}.key`, 'Use a letter followed by at most 119 letters, digits, dots, underscores, colons, slashes or hyphens.');
    if (keys.has(item.key)) reject(`${location}.key`, `Duplicate alias '${item.key}'.`, 'duplicate-alias'); keys.add(item.key);
    if (own(item, 'existingId')) { text(item.existingId, `${location}.existingId`, true); if (ids.has(item.existingId)) reject(`${location}.existingId`, 'The same existing entity cannot be declared twice.'); ids.add(item.existingId); }
  });
  return value;
}
function resolve(ref: unknown, aliases: Record<string, string>, available: Map<string, unknown>, path: string): string {
  text(ref, path, true);
  const id = own(aliases, ref) ? aliases[ref] : ref;
  if (!available.has(id)) reject(path, `Unknown alias or ID '${ref}'.`, 'unresolved-reference');
  return id;
}
function allocate<T extends { id: string }>(items: Declaration[], existing: Map<string, T>, aliases: Record<string, string>, path: string): void {
  for (const item of items) {
    if (item.existingId && !existing.has(item.existingId)) reject(`${path}.${item.key}.existingId`, `Existing ID '${item.existingId}' does not exist.`, 'unresolved-reference');
    aliases[item.key] = item.existingId ?? crypto.randomUUID();
  }
}
function assignFields<T extends object>(target: T, source: object, fields: readonly string[]): void {
  const record = source as Record<string, unknown>;
  for (const key of fields) if (own(source, key)) {
    const old = (target as Record<string, unknown>)[key], value = structuredClone(record[key]);
    (target as Record<string, unknown>)[key] = ['style', 'rules', 'snapping'].includes(key) && old && typeof old === 'object' ? { ...old, ...(value as object) } : value;
  }
}

function readBindings(base: MeridianProject, value: AuthoringRecipe['bindings']): AuthoringAliases {
  const result: AuthoringAliases = { canvases: Object.create(null), roots: Object.create(null), nodes: Object.create(null), assets: Object.create(null), links: Object.create(null) };
  if (value === undefined) return result;
  object(value, 'bindings', Object.keys(result));
  const available = { canvases: base.canvases, roots: base.nodes.filter(node => node.kind === 'canvas'), nodes: base.nodes.filter(node => node.kind !== 'canvas'), assets: base.assets, links: base.links };
  for (const scope of Object.keys(result) as (keyof AuthoringAliases)[]) if (own(value, scope)) {
    const map = value[scope];
    if (!record(map) || Object.keys(map).length > 10000) reject(`bindings.${scope}`, 'Expected at most 10000 alias-to-ID bindings.');
    const ids = new Set(available[scope].map(item => item.id));
    for (const [key, id] of Object.entries(map!)) {
      if (!/^[A-Za-z][A-Za-z0-9._:/-]{0,119}$/.test(key)) reject(`bindings.${scope}.${key}`, 'Invalid alias.');
      text(id, `bindings.${scope}.${key}`, true);
      if (!ids.has(id)) reject(`bindings.${scope}.${key}`, `Bound ${scope} ID '${id}' is unavailable in this project.`, 'stale-binding');
      result[scope][key] = id;
    }
  }
  for (const [key, id] of Object.entries(result.canvases)) {
    const rootId = base.canvases.find(canvas => canvas.id === id)!.rootId;
    if (own(result.roots, key) && result.roots[key] !== rootId) reject(`bindings.roots.${key}`, 'The root binding does not belong to its bound Work.', 'stale-binding');
    result.roots[key] = rootId;
  }
  return result;
}

function bindDeclarations<T extends Declaration>(items: T[], bindings: Record<string, string>, path: string): T[] {
  const used = new Set<string>();
  return items.map(item => {
    const bound = bindings[item.key];
    if (bound && item.existingId && bound !== item.existingId) reject(`${path}.${item.key}.existingId`, 'The explicit ID conflicts with this alias binding.', 'conflicting-binding');
    const existingId = item.existingId ?? bound;
    if (existingId && used.has(existingId)) reject(`${path}.${item.key}`, 'The same existing entity cannot be declared twice.');
    if (existingId) used.add(existingId);
    return existingId ? { ...item, existingId } : item;
  });
}

const nodeUnsetFields = ['titleRuns', 'showTitle', 'contentItems', 'smartLink', 'text', 'textRuns', 'tag', 'positionLocked', 'sizeMode', 'containerSizes', 'retiredBlockContent', 'contentType', 'childPosition', 'childOffset', 'expansionTray', 'collapsed', 'asset', 'backgroundAsset', 'attachments', 'hyperlinks', 'remoteLinks', 'target', 'targetCell', 'canvasTarget', 'dependencies', 'value', 'valueType', 'closed', 'operation', 'exposed', 'rules', 'snapping', 'style', 'containerVariant', 'utilityVariant', 'childLayout', 'placement', 'attachSide', 'grid', 'cell', 'childArrangement'];
const clearNames: Record<string, string> = { asset: 'assetId', target: 'targetId', dependencies: 'dependencyIds' };
function unsetFields(target: object, spec: Declaration, scope: 'canvas' | 'node' | 'link', path: string): void {
  if (!own(spec, 'unset')) return;
  const fields = scope === 'node' ? nodeUnsetFields : scope === 'canvas' ? ['snapping', 'style', 'backgroundAsset'] : ['parent', 'direction', 'targetPort', 'sourceAnchor', 'targetAnchor', 'points', 'label', 'style'];
  const styleKeys: readonly string[] = scope === 'node' ? NODE_STYLE_KEYS : scope === 'canvas' ? CANVAS_STYLE_KEYS : LINK_STYLE_KEYS;
  const allowed = [...fields, ...styleKeys.map(key => `style.${key}`), ...scope !== 'link' ? ['snapping.stickyEdges', 'snapping.snapToGrid', 'snapping.smartAlign'] : [], ...scope === 'node' ? ['rules.nodeKinds', 'rules.linkKinds', 'rules.operations'] : []];
  if (!Array.isArray(spec.unset) || spec.unset.length > 200 || new Set(spec.unset).size !== spec.unset.length) reject(`${path}.unset`, 'Expected at most 200 distinct optional field names.');
  for (const field of spec.unset!) {
    if (typeof field !== 'string' || !allowed.includes(field)) reject(`${path}.unset`, `Cannot unset '${String(field)}'; choose a supported optional field.`);
    const [head, tail] = field.split('.');
    if (spec.unset!.includes(head) && tail) reject(`${path}.unset`, `Choose '${head}' or its subfields, not both.`);
    // A reset followed by a new value is intentional, useful for replacing a style.
    const object = target as Record<string, unknown>;
    if (field === 'backgroundAsset') {
      const style = object.style as Record<string, unknown> | undefined;
      if (style) { delete style.backgroundAssetId; if (!Object.keys(style).length) delete object.style; }
    } else if (tail) {
      const nested = object[head] as Record<string, unknown> | undefined;
      if (nested) { delete nested[tail]; if (!Object.keys(nested).length) delete object[head]; }
    } else delete object[head === 'parent' ? 'parentId' : clearNames[head] ?? head];
  }
}

function removeEntities(project: MeridianProject, value: AuthoringRecipe['remove'], aliases: AuthoringAliases, editedLinks: Set<string>): Set<string> {
  if (value === undefined) return new Set();
  object(value, 'remove', ['nodes', 'links']);
  const nodes = new Map(project.nodes.map(node => [node.id, node])), links = new Map(project.links.map(link => [link.id, link]));
  const refs = (scope: 'nodes' | 'links', available: Map<string, unknown>): string[] => {
    if (!own(value, scope)) return [];
    const list = value[scope];
    if (!Array.isArray(list) || list.length > 10000) reject(`remove.${scope}`, 'Expected at most 10000 existing aliases or IDs.');
    const ids = list!.map((ref, i) => resolve(ref, aliases[scope], available, `remove.${scope}[${i}]`));
    if (new Set(ids).size !== ids.length) reject(`remove.${scope}`, 'The same entity cannot be removed twice.');
    return ids;
  };
  const requested = refs('nodes', nodes), removed = new Set<string>();
  for (const id of requested) {
    if (nodes.get(id)!.kind === 'canvas') reject('remove.nodes', 'Work roots cannot be removed by a recipe.');
    for (const descendant of descendants(project, id)) removed.add(descendant);
  }
  const explicitLinks = new Set(refs('links', links));
  const nav = navigationIndex(project);
  project.links = project.links.filter(link => !explicitLinks.has(link.id) && (editedLinks.has(link.id) || !removed.has(link.sourceId) && !removed.has(link.targetId) && !removed.has(nav.linkParent(link)?.id ?? '')));
  project.nodes = project.nodes.filter(node => !removed.has(node.id));
  for (const id of removed) delete project.snapshots[id];
  return removed;
}

function prepareRoutes(value: AuthoringRecipe['routes'], nodes: NodeRecipe[], links: LinkRecipe[], aliases: AuthoringAliases): RouteRecipe[] {
  const routes = bindDeclarations(declarations(value, 'routes', ['canvas', 'parent', 'source', 'target', 'via', ...linkFields.filter(key => key !== 'points')]), aliases.links, 'routes');
  const nodeKeys = new Set(nodes.map(spec => spec.key)), linkKeys = new Set(links.map(spec => spec.key));
  const nodeIds = new Set(nodes.flatMap(spec => spec.existingId ? [spec.existingId] : [])), linkIds = new Set(links.flatMap(spec => spec.existingId ? [spec.existingId] : []));
  for (const route of routes) {
    const path = `routes.${route.key}`;
    if (own(route, 'unset')) reject(`${path}.unset`, 'Routes describe a complete sequence of waypoints; unset fields on individual links instead.');
    text(route.source, `${path}.source`, true); text(route.target, `${path}.target`, true);
    if (!Array.isArray(route.via) || route.via.length > 1000) reject(`${path}.via`, 'Expected at most 1000 routing utilities.');
    for (const [index, waypoint] of route.via.entries()) {
      const at = `${path}.via[${index}]`;
      object(waypoint, at, ['key', 'x', 'y', 'variant', 'width', 'height', 'style']);
      text(waypoint.key, `${at}.key`, true);
      if (!/^[A-Za-z][A-Za-z0-9._:/-]{0,119}$/.test(waypoint.key) || nodeKeys.has(waypoint.key)) reject(`${at}.key`, 'Choose a valid unique node alias for this waypoint.', 'duplicate-alias');
      nodeKeys.add(waypoint.key);
      number(waypoint.x, `${at}.x`); number(waypoint.y, `${at}.y`);
      for (const key of ['width', 'height'] as const) if (own(waypoint, key)) number(waypoint[key], `${at}.${key}`, Number.EPSILON);
      if (own(waypoint, 'variant') && !['joint', 'pivot'].includes(waypoint.variant!)) reject(`${at}.variant`, 'Choose Joint or Pivot.');
      if (own(waypoint, 'style') && !isNodeStyle(waypoint.style)) reject(`${at}.style`, 'Invalid routing utility appearance.');
      if (aliases.nodes[waypoint.key] && nodeIds.has(aliases.nodes[waypoint.key])) reject(`${at}.key`, 'The same existing routing utility cannot be declared twice.');
      aliases.nodes[waypoint.key] ??= crypto.randomUUID();
      nodeIds.add(aliases.nodes[waypoint.key]);
    }
    const keys = [route.key, ...route.via.map((_, index) => `${route.key}/segment/${index + 1}`)];
    for (const key of keys) {
      if (key.length > 120 || linkKeys.has(key)) reject(path, `Route segment alias '${key}' conflicts or exceeds 120 characters.`, 'duplicate-alias');
      linkKeys.add(key);
      aliases.links[key] ??= key === route.key && route.existingId ? route.existingId : crypto.randomUUID();
      if (linkIds.has(aliases.links[key])) reject(path, 'The same existing route segment cannot be declared twice.');
      linkIds.add(aliases.links[key]);
    }
  }
  return routes;
}

function materializeRoutes(project: MeridianProject, routes: RouteRecipe[], aliases: AuthoringAliases,
  nodes: Map<string, MeridianNode>, links: Map<string, MeridianLink>, canvases: Map<string, MeridianCanvas>, specs: LinkRecipe[], removedNodes: Set<string>, removedLinks: Set<string>): void {
  for (const route of routes) {
    const path = `routes.${route.key}`;
    const oldTerminal = links.get(aliases.links[route.key]);
    const oldFirst = links.get(aliases.links[`${route.key}/segment/1`]) ?? oldTerminal;
    const expectedSegments = new Set([route.key, ...route.via.map((_, index) => `${route.key}/segment/${index + 1}`)]);
    for (const [key, id] of Object.entries(aliases.links)) if (key.startsWith(`${route.key}/segment/`) && !expectedSegments.has(key) && links.has(id))
      reject(path, `Obsolete route segment '${key}' is still present. Include it in remove.links when shortening this route.`, 'obsolete-route-segment');
    const sourceId = resolve(route.source, aliases.nodes, nodes, `${path}.source`), targetId = resolve(route.target, aliases.nodes, nodes, `${path}.target`);
    const canvasId = route.canvas ? resolve(route.canvas, aliases.canvases, canvases, `${path}.canvas`) : oldTerminal?.canvasId ?? nodes.get(sourceId)!.canvasId;
    const direct: MeridianLink = { id: aliases.links[route.key], canvasId, sourceId, targetId, kind: route.kind ?? oldTerminal?.kind ?? 'visual', relation: route.relation ?? oldTerminal?.relation ?? 'causal' };
    const owner = route.parent === undefined ? oldTerminal?.parentId ?? navigationIndex(project).linkParent(direct)?.id : route.parent === null ? navigationIndex(project).linkParent(direct)?.id : resolve(route.parent, { ...aliases.roots, ...aliases.nodes }, nodes, `${path}.parent`);
    if (!owner) reject(path, 'The route needs an owner in its Work.');
    const valueType = valueTypeForLink(direct.kind);
    const direction = route.direction ?? oldTerminal?.direction, style = { ...oldTerminal?.style, ...route.style };
    const sourceAnchor = route.sourceAnchor ?? oldFirst?.sourceAnchor, targetAnchor = route.targetAnchor ?? oldTerminal?.targetAnchor;
    for (const waypoint of route.via) {
      const id = aliases.nodes[waypoint.key], old = nodes.get(id);
      if (old && old.kind !== 'junction') reject(`${path}.via.${waypoint.key}`, 'A bound waypoint must name an existing routing utility.');
      // A binding whose entity was removed must not silently create a replacement.
      if (removedNodes.has(id)) reject(`${path}.via.${waypoint.key}`, 'The bound waypoint was removed by this recipe.');
      const width = waypoint.width ?? old?.width ?? 12, height = waypoint.height ?? old?.height ?? 12;
      const node: MeridianNode = old ?? { id, canvasId, parentId: owner!, kind: 'junction', title: '', tag: waypoint.key, x: 0, y: 0, width, height };
      Object.assign(node, { canvasId, parentId: owner, utilityVariant: waypoint.variant ?? old?.utilityVariant ?? 'pivot', x: waypoint.x - width / 2, y: waypoint.y - height / 2,
        width, height, sizeMode: 'fixed', childPosition: 'manual', placement: 'free', snapping: { stickyEdges: false, snapToGrid: false, smartAlign: false },
        style: { shape: 'circle', fill: style.color ?? '#78DDC1', ...old?.style, ...structuredClone(waypoint.style ?? {}) } });
      if (valueType) node.valueType = valueType; else delete node.valueType;
      if (!old) { project.nodes.push(node); nodes.set(id, node); }
    }
    const chain = [route.source, ...route.via.map(point => point.key), route.target];
    for (let index = 0; index < chain.length - 1; index++) {
      const terminal = index === chain.length - 2, key = terminal ? route.key : `${route.key}/segment/${index + 1}`;
      if (removedLinks.has(aliases.links[key])) reject(`${path}.via`, 'A bound route segment was removed by this recipe.');
      const existingId = links.has(aliases.links[key]) ? aliases.links[key] : undefined;
      if (route.existingId && terminal && !existingId) reject(`${path}.existingId`, 'The route endpoint segment is unavailable.');
      const spec: LinkRecipe = { key, ...(existingId ? { existingId } : {}), canvas: canvasId, parent: owner, source: chain[index], target: chain[index + 1], kind: direct.kind, relation: direct.relation,
        ...(direction ? { direction } : {}), ...(Object.keys(style).length ? { style: structuredClone(style) } : {}),
        ...(terminal && own(route, 'label') ? { label: route.label } : {}), ...(terminal && (own(route, 'targetPort') || oldTerminal?.targetPort !== undefined) ? { targetPort: route.targetPort ?? oldTerminal?.targetPort } : valueType ? { targetPort: 0 } : {}),
        ...(index === 0 && sourceAnchor ? { sourceAnchor } : {}), ...(terminal && targetAnchor ? { targetAnchor } : {}),
        unset: ['points', 'sourceAnchor', 'targetAnchor', ...!terminal ? ['label'] : []] };
      specs.push(spec);
    }
  }
}

/** Prepare one validated replacement project. No host, filesystem, evaluation of code, or input mutation. */
export function applyRecipe(base: MeridianProject, recipe: AuthoringRecipe): AuthoringResult {
  if (!isProjectShape(base)) reject('base', 'The source project has an unsupported or malformed shape.', 'invalid-project');
  object(recipe, 'recipe', ['version', 'title', 'entryCanvas', 'canvases', 'nodes', 'assets', 'links', 'layouts', 'bindings', 'remove', 'routes']);
  if (recipe.version !== 1) reject('recipe.version', 'Only authoring recipe version 1 is supported.');
  const aliases = readBindings(base, recipe.bindings);
  const canvasSpecs = bindDeclarations(declarations(recipe.canvases, 'canvases', ['title', 'snapping', 'style', 'backgroundAsset']), aliases.canvases, 'canvases');
  const nodeSpecs = bindDeclarations(declarations(recipe.nodes, 'nodes', ['canvas', 'parent', 'kind', 'asset', 'backgroundAsset', 'target', 'dependencies', ...nodeFields]), aliases.nodes, 'nodes');
  const assetSpecs = bindDeclarations(declarations(recipe.assets, 'assets', ['name', 'mime', 'base64']), aliases.assets, 'assets');
  const linkSpecs = bindDeclarations(declarations(recipe.links, 'links', ['canvas', 'parent', 'source', 'target', ...linkFields]), aliases.links, 'links');
  for (const spec of assetSpecs) if (own(spec, 'unset')) reject(`assets.${spec.key}.unset`, 'Asset bytes and metadata cannot be unset.');
  const routeSpecs = prepareRoutes(recipe.routes, nodeSpecs, linkSpecs, aliases);
  if (recipe.layouts !== undefined && (!Array.isArray(recipe.layouts) || recipe.layouts.length > 1000)) reject('layouts', 'Expected at most 1000 layouts.');
  const project = cloneProject(base);
  // Explicitly edited connection identities may be retargeted away from a removed
  // waypoint/branch in this same atomic recipe. Every final endpoint still validates.
  const editedLinks = new Set(linkSpecs.flatMap(spec => spec.existingId ? [spec.existingId] : []));
  for (const route of routeSpecs) for (const key of [route.key, ...route.via.map((_, index) => `${route.key}/segment/${index + 1}`)]) editedLinks.add(aliases.links[key]);
  const removedIds = removeEntities(project, recipe.remove, aliases, editedLinks);
  const deferredPositionLocks = new Set<string>(), previouslyLocked = new Set(base.nodes.filter(node => node.positionLocked).map(node => node.id));
  const canvases = new Map(project.canvases.map(c => [c.id, c])), nodes = new Map(project.nodes.map(n => [n.id, n]));
  const assets = new Map(project.assets.map(a => [a.id, a])), links = new Map(project.links.map(l => [l.id, l]));
  allocate(canvasSpecs, canvases, aliases.canvases, 'canvases'); allocate(nodeSpecs, nodes, aliases.nodes, 'nodes');
  allocate(assetSpecs, assets, aliases.assets, 'assets'); allocate(linkSpecs, links, aliases.links, 'links');
  if (own(recipe, 'title')) { text(recipe.title, 'recipe.title', true); project.title = recipe.title; }
  for (const spec of canvasSpecs) {
    const location = `canvases.${spec.key}`;
    if (own(spec, 'title')) text(spec.title, `${location}.title`, true);
    if (own(spec, 'snapping')) snapping(spec.snapping, `${location}.snapping`);
    if (own(spec, 'style') && (spec.style === undefined || !isCanvasStyle(spec.style))) reject(`${location}.style`, 'Invalid Work appearance.');
    let canvas = canvases.get(aliases.canvases[spec.key]);
    if (!canvas) {
      canvas = { id: aliases.canvases[spec.key], rootId: crypto.randomUUID(), title: spec.title ?? spec.key };
      const root: MeridianNode = { id: canvas.rootId, canvasId: canvas.id, parentId: null, kind: 'canvas', title: canvas.title, tag: canvas.title, x: 0, y: 0, width: 1200, height: 800, sizeMode: 'fixed' };
      project.canvases.push(canvas); project.nodes.push(root); canvases.set(canvas.id, canvas); nodes.set(root.id, root);
    }
    unsetFields(canvas, spec, 'canvas', location);
    assignFields(canvas, spec, ['title', 'snapping', 'style']);
    if (own(spec, 'title')) { const root = nodes.get(canvas.rootId); if (root) {
      if (root.titleRuns) root.titleRuns = rebaseTextRuns(root.title, canvas.title, root.titleRuns);
      root.title = canvas.title;
    } }
    aliases.roots[spec.key] = canvas.rootId;
  }
  // Set explicit asset updates first so deduplication compares their final contents.
  for (const spec of [...assetSpecs.filter(item => item.existingId), ...assetSpecs.filter(item => !item.existingId)]) {
    const location = `assets.${spec.key}`;
    let asset = assets.get(aliases.assets[spec.key]);
    for (const field of ['name', 'mime', 'base64'] as const) if (!asset || own(spec, field)) text(spec[field], `${location}.${field}`, field !== 'base64');
    if (spec.mime !== undefined && (!spec.mime.includes('/') || /[\u0000-\u001f\u007f]/.test(spec.mime))) reject(`${location}.mime`, 'Expected a MIME type without control characters.');
    if (spec.base64 !== undefined && !canonicalBase64(spec.base64)) reject(`${location}.base64`, 'Expected canonical base64 attachment data.');
    // Reuse identical attachments without changing a named existing asset or its consumers.
    if (!asset) {
      asset = project.assets.find(item => item.name === spec.name && item.mime === spec.mime && item.base64 === spec.base64);
      if (asset) aliases.assets[spec.key] = asset.id;
    }
    if (!asset) { asset = { id: aliases.assets[spec.key], name: spec.name!, mime: spec.mime!, base64: spec.base64! }; project.assets.push(asset); assets.set(asset.id, asset); }
    assignFields(asset, spec, ['name', 'mime', 'base64']);
  }
  for (const spec of canvasSpecs) if (own(spec, 'backgroundAsset')) {
    const canvas = canvases.get(aliases.canvases[spec.key])!;
    canvas.style = { ...canvas.style, backgroundAssetId: resolve(spec.backgroundAsset, aliases.assets, assets, `canvases.${spec.key}.backgroundAsset`) };
  }
  // Allocate all nodes before resolving their containment, media, and reference targets.
  for (const spec of nodeSpecs) {
    const location = `nodes.${spec.key}`;
    let node = nodes.get(aliases.nodes[spec.key]);
    if (node?.kind === 'canvas') reject(location, 'Update Work roots through their Work declaration.');
    if (!node || own(spec, 'kind')) if (typeof spec.kind !== 'string' || !nodeKinds.includes(spec.kind)) reject(`${location}.kind`, 'Choose a supported non-root node kind.');
    if (node && spec.kind !== undefined && spec.kind !== node.kind) reject(`${location}.kind`, 'An update cannot change an existing node kind.');
    if (!node) {
      const canvasId = resolve(spec.canvas, aliases.canvases, canvases, `${location}.canvas`);
      node = { id: aliases.nodes[spec.key], canvasId, parentId: canvases.get(canvasId)!.rootId, kind: spec.kind!, title: spec.title ?? (spec.kind === 'block' ? '' : spec.key), x: 100, y: 100, width: 200, height: 120, sizeMode: spec.kind === 'joint' || spec.kind === 'junction' || spec.kind === 'block' ? 'fixed' : 'auto', childPosition: spec.childPosition ?? (own(spec, 'x') || own(spec, 'y') ? 'manual' : 'auto') };
      node.tag = spec.tag ?? nodeTag(node);
      if (node.kind === 'container' || node.kind === 'module') { node.width = 480; node.height = 300; }
      if (node.kind === 'input') { node.value = false; node.exposed = true; }
      if (node.kind === 'number') { node.value = 0; node.exposed = true; }
      if (node.kind === 'junction') { node.width = 12; node.height = 12; node.utilityVariant = 'joint'; node.style = { shape: 'circle' }; }
      if (node.kind === 'switch') { node.closed = true; node.exposed = true; }
      if (node.kind === 'joint') node.operation = 'AND';
      if (node.kind === 'text') node.text = '';
      if (node.kind === 'media') { node.contentType = 'text'; node.text = ''; node.containerVariant = 'contained'; node.childLayout = 'spatial'; node.childArrangement = 'radial'; }
      if (node.kind === 'block') { node.containerVariant = 'contained'; node.childLayout = 'spatial'; node.childArrangement = 'radial'; }
      if (node.kind === 'container' || node.kind === 'module') { node.containerVariant = 'contained'; node.childLayout = 'contained'; node.childArrangement = 'free'; }
      if (node.kind === 'spreadsheet') {
        node.containerVariant = 'spreadsheet';
        node.grid = { columns: 3, rows: 3, cellWidth: 200, cellHeight: 100, gap: 0, showHeaders: true };
        node.childLayout = 'contained'; node.childArrangement = 'grid'; node.collapsed = false; node.width = 666; node.height = 402;
      }
      project.nodes.push(node); nodes.set(node.id, node);
    }
  }
  for (const spec of nodeSpecs) {
    const location = `nodes.${spec.key}`, node = nodes.get(aliases.nodes[spec.key])!;
    unsetFields(node, spec, 'node', location);
    const previousPattern = node.containerVariant === 'spatial' ? 'contained' : node.containerVariant;
    const previousArrangement = node.childArrangement;
    for (const key of ['title', 'text', 'operation'] as const) if (own(spec, key)) text(spec[key], `${location}.${key}`, key === 'operation');
    if (own(spec, 'tag') && !isNodeTag(spec.tag)) reject(`${location}.tag`, 'Expected text with at most 8000 characters.');
    for (const [field, runField] of [['title', 'titleRuns'], ['text', 'textRuns']] as const) {
      const canonical = spec[field] ?? node[field] ?? '';
      if (own(spec, runField) && !isTextRuns(canonical, spec[runField])) reject(`${location}.${runField}`, 'Formatted passages must use sorted nonoverlapping safe text offsets and supported typography.');
      if (own(spec, field) && !own(spec, runField) && node[runField]) node[runField] = rebaseTextRuns(node[field] ?? '', canonical, node[runField]);
    }
    for (const key of ['x', 'y', 'width', 'height'] as const) if (own(spec, key)) number(spec[key], `${location}.${key}`, key === 'width' || key === 'height' ? Number.EPSILON : -1e9);
    if (own(spec, 'sizeMode') && !['auto', 'fixed'].includes(spec.sizeMode!)) reject(`${location}.sizeMode`, 'Expected auto or fixed.');
    if (own(spec, 'containerSizes') && !isContainerSizes(spec.containerSizes)) reject(`${location}.containerSizes`, 'Expected independent expanded/collapsed frames with positive finite dimensions and automatic or fixed sizing.');
    if (own(spec, 'retiredBlockContent') && !isRetiredBlockContent(spec.retiredBlockContent)) reject(`${location}.retiredBlockContent`, 'Expected inert canonical archived text and valid passage formatting.');
    if (own(spec, 'contentType') && !['text', 'asset', 'link'].includes(spec.contentType!)) reject(`${location}.contentType`, 'Choose one primary content type: text, asset or link.');
    if (own(spec, 'childPosition') && !['auto', 'manual'].includes(spec.childPosition!)) reject(`${location}.childPosition`, 'Expected auto or manual child position.');
    if (own(spec, 'childOffset')) point(spec.childOffset, `${location}.childOffset`);
    if (own(spec, 'expansionTray') && !isExpansionTray(spec.expansionTray)) reject(`${location}.expansionTray`, 'Expected finite source-relative x/y offsets and optional outer width > 40 and height > 72, each no greater than 1000000000.');
    if (own(spec, 'utilityVariant') && (node.kind !== 'junction' || !['pivot', 'joint'].includes(spec.utilityVariant!))) reject(`${location}.utilityVariant`, 'Expected Pivot or Joint on a Utility node.');
    if (own(spec, 'smartLink') && (node.kind !== 'junction' || !isSmartLinkSettings(spec.smartLink))) reject(`${location}.smartLink`, 'Smart Links use a Utility node with a supported direction and gap from 8 to 10000.');
    for (const key of ['collapsed', 'closed', 'exposed', 'positionLocked', 'showTitle'] as const) if (own(spec, key)) boolean(spec[key], `${location}.${key}`);
    if (own(spec, 'snapping')) snapping(spec.snapping, `${location}.snapping`);
    if (own(spec, 'rules')) rules(spec.rules, `${location}.rules`);
    if (own(spec, 'style') && (spec.style === undefined || !isNodeStyle(spec.style))) reject(`${location}.style`, 'Invalid node appearance.');
    if (own(spec, 'value') && typeof spec.value !== 'boolean' && !(typeof spec.value === 'number' && Number.isFinite(spec.value))) reject(`${location}.value`, 'Expected a Boolean or finite number.');
    if (own(spec, 'valueType') && !['boolean','number'].includes(spec.valueType!)) reject(`${location}.valueType`, 'Expected boolean or number.');
    if (own(spec, 'childLayout') && !['contained', 'spatial'].includes(spec.childLayout!)) reject(`${location}.childLayout`, 'Expected contained or spatial.');
    if ((node.kind === 'media' || node.kind === 'block') && spec.childLayout === 'contained') reject(`${location}.childLayout`, 'Media and Block nodes reveal children outside their bounds.');
    if (own(spec, 'placement') && !['free', 'near', 'edge'].includes(spec.placement!)) reject(`${location}.placement`, 'Expected free, near or edge.');
    if (own(spec, 'attachSide') && !['auto', 'top', 'right', 'bottom', 'left'].includes(spec.attachSide!)) reject(`${location}.attachSide`, 'Expected auto, top, right, bottom or left.');
    if (own(spec, 'childArrangement') && !CHILD_ARRANGEMENTS.includes(spec.childArrangement!)) reject(`${location}.childArrangement`, 'Choose a supported child arrangement.');
    if (own(spec, 'containerVariant') && !['spatial', 'contained', 'horizontal', 'vertical', 'grid', 'spreadsheet', 'gallery', 'window'].includes(spec.containerVariant!)) reject(`${location}.containerVariant`, 'Choose a supported container variant.');
    if (own(spec, 'grid') && !isGridSettings(spec.grid)) reject(`${location}.grid`, 'Invalid grid settings.');
    if (own(spec, 'cell') && !isCellLocation(spec.cell)) reject(`${location}.cell`, 'Invalid cell coordinates or name.');
    if (own(spec, 'targetCell') && !isCellReference(spec.targetCell)) reject(`${location}.targetCell`, 'Use an X:Y coordinate, legacy A1 address or a cell name.');
    assignFields(node, spec, nodeFields);
    if (spec.positionLocked && !previouslyLocked.has(node.id)) { delete node.positionLocked; deferredPositionLocks.add(node.id); }
    if (!own(spec, 'childPosition') && (own(spec, 'x') || own(spec, 'y'))) node.childPosition = 'manual';
    if (own(spec, 'containerVariant')) {
      if (node.containerVariant === 'spatial') node.containerVariant = 'contained';
      node.childLayout = ['container', 'spreadsheet', 'module'].includes(node.kind) ? 'contained' : 'spatial';
      if (isOrderedContainer(node)) {
        const required = orderedPattern(node);
        if (spec.childArrangement !== undefined && spec.childArrangement !== required) reject(`${location}.childArrangement`, `This ordered display pattern requires '${required}'.`);
        node.childArrangement = required;
      } else if (spec.childArrangement !== undefined) node.childArrangement = spec.childArrangement;
      else if (previousPattern === node.containerVariant && previousArrangement !== undefined) node.childArrangement = previousArrangement;
      else node.childArrangement = isEnclosingContainer(node) || node.containerVariant === 'gallery' ? 'free' : 'radial';
      if (isOrderedContainer(node) && !node.grid && node.containerVariant !== 'window')
        node.grid = { columns: node.containerVariant === 'vertical' ? 1 : 3, rows: node.containerVariant === 'horizontal' ? 1 : 3, cellWidth: 200, cellHeight: 120, gap: 0, showHeaders: false, fillOrder: 'row' };
    }
    // Existing recipes use explicit dimensions to communicate scale. Keep that intent
    // unless the declaration explicitly opts back into content-driven sizing.
    if (!own(spec, 'sizeMode') && (own(spec, 'width') || own(spec, 'height'))) node.sizeMode = 'fixed';
    if (own(spec, 'canvas')) node.canvasId = resolve(spec.canvas, aliases.canvases, canvases, `${location}.canvas`);
    if (own(spec, 'parent')) node.parentId = spec.parent === null ? canvases.get(node.canvasId)!.rootId : resolve(spec.parent, { ...aliases.roots, ...aliases.nodes }, nodes, `${location}.parent`);
    if (own(spec, 'dependencies')) {
      if (!Array.isArray(spec.dependencies) || spec.dependencies.length > 1000) reject(`${location}.dependencies`, 'Expected at most 1000 node aliases or GUIDs.');
      node.dependencyIds = spec.dependencies!.map((ref,index) => { text(ref, `${location}.dependencies[${index}]`, true); return own(aliases.nodes, ref) ? aliases.nodes[ref] : ref; });
    }
    if (own(spec, 'asset')) node.assetId = resolve(spec.asset, aliases.assets, assets, `${location}.asset`);
    if (own(spec, 'contentItems')) {
      if (!Array.isArray(spec.contentItems)) reject(`${location}.contentItems`, 'Expected an ordered array of Node Hub items.');
      const items = spec.contentItems!.map((item, index) => item && typeof item === 'object' && item.type === 'asset' ? { ...item, assetId: resolve(item.assetId, aliases.assets, assets, `${location}.contentItems[${index}].assetId`) } : item);
      if (!isNodeContentItems(items)) reject(`${location}.contentItems`, 'Expected at most 1000 uniquely identified inert text, asset or hyperlink items.');
      node.contentItems = structuredClone(items);
    }
    if (node.kind === 'media' && !own(spec, 'contentType')) {
      if (own(spec, 'asset') || spec.attachments?.length) node.contentType = 'asset';
      else if (own(spec, 'text')) node.contentType = 'text';
      else if (spec.hyperlinks?.length) node.contentType = 'link';
    }
    if (own(spec, 'backgroundAsset')) node.style = { ...node.style, backgroundAssetId: resolve(spec.backgroundAsset, aliases.assets, assets, `${location}.backgroundAsset`) };
    if (Array.isArray(spec.attachments)) node.attachments = spec.attachments.map(id => typeof id === 'string' && own(aliases.assets, id) ? aliases.assets[id] : id);
    if (Array.isArray(spec.remoteLinks)) node.remoteLinks = spec.remoteLinks.map(link => link && typeof link === 'object' && !Array.isArray(link) ? ({ ...link,
      targetId: typeof link.targetId === 'string' && own(aliases.nodes, link.targetId) ? aliases.nodes[link.targetId] : link.targetId }) : link);
    if (own(spec, 'target')) {
      if (node.kind === 'reference') {
        text(spec.target, `${location}.target`, true);
        const id = own(aliases.nodes, spec.target) ? aliases.nodes[spec.target] : spec.target;
        if (!nodes.has(id) && !own(project.snapshots, id)) reject(`${location}.target`, `Unknown node or saved snapshot '${spec.target}'.`, 'unresolved-reference');
        node.targetId = id;
      }
      else if (node.kind === 'canvas-link') {
        text(spec.target, `${location}.target`, true);
        const canvasTarget = own(aliases.canvases, spec.target) ? canvases.get(aliases.canvases[spec.target]) : canvases.get(spec.target);
        node.targetId = canvasTarget?.rootId ?? resolve(spec.target, aliases.nodes, nodes, `${location}.target`);
      } else reject(`${location}.target`, 'Only Reference and Work launcher nodes accept targets.');
    }
  }
  materializeRoutes(project, routeSpecs, aliases, nodes, links, canvases, linkSpecs, removedIds, new Set(base.links.filter(link => !links.has(link.id)).map(link => link.id)));
  for (const spec of linkSpecs) {
    const location = `links.${spec.key}`;
    let link = links.get(aliases.links[spec.key]);
    const sourceId = own(spec, 'source') || !link ? resolve(spec.source, aliases.nodes, nodes, `${location}.source`) : link.sourceId;
    const targetId = own(spec, 'target') || !link ? resolve(spec.target, aliases.nodes, nodes, `${location}.target`) : link.targetId;
    const canvasId = own(spec, 'canvas') ? resolve(spec.canvas, aliases.canvases, canvases, `${location}.canvas`) : link?.canvasId ?? nodes.get(sourceId)!.canvasId;
    if (own(spec, 'kind') && !['visual', 'functional', 'numeric'].includes(spec.kind!)) reject(`${location}.kind`, 'Expected visual, functional or numeric.');
    if (own(spec, 'relation') && !['causal', 'chronological', 'mutual', 'cyclical', 'generic'].includes(spec.relation!)) reject(`${location}.relation`, 'Expected causal, chronological, mutual, cyclical or generic.');
    if (own(spec, 'direction') && !['forward', 'reverse', 'both', 'none'].includes(spec.direction!)) reject(`${location}.direction`, 'Expected forward, reverse, both or none.');
    if (own(spec, 'targetPort')) { number(spec.targetPort, `${location}.targetPort`, 0, 1); if (!Number.isInteger(spec.targetPort)) reject(`${location}.targetPort`, 'Expected an integer input port.'); }
    for (const key of ['sourceAnchor', 'targetAnchor'] as const) if (own(spec, key)) point(spec[key], `${location}.${key}`, true);
    if (own(spec, 'points')) { if (!Array.isArray(spec.points) || spec.points.length > 1000) reject(`${location}.points`, 'Expected at most 1000 route points.'); spec.points!.forEach((p, i) => point(p, `${location}.points[${i}]`)); }
    if (own(spec, 'label')) { text(spec.label, `${location}.label`); if (spec.label.length > 160) reject(`${location}.label`, 'Labels are limited to 160 characters.'); }
    if (own(spec, 'style') && (spec.style === undefined || !isLinkStyle(spec.style))) reject(`${location}.style`, 'Invalid link appearance.');
    if (!link) { link = { id: aliases.links[spec.key], canvasId, sourceId, targetId, kind: 'visual', relation: 'causal' }; project.links.push(link); links.set(link.id, link); }
    unsetFields(link, spec, 'link', location);
    Object.assign(link, { canvasId, sourceId, targetId }); assignFields(link, spec, linkFields);
    if (own(spec, 'parent')) {
      if (spec.parent === null) delete link.parentId;
      else link.parentId = resolve(spec.parent, { ...aliases.roots, ...aliases.nodes }, nodes, `${location}.parent`);
    }
  }
  if (own(recipe, 'entryCanvas')) project.entryCanvasId = resolve(recipe.entryCanvas, aliases.canvases, canvases, 'recipe.entryCanvas');
  for (const [index, layout] of (recipe.layouts ?? []).entries()) applyLayout(project, layout, aliases.nodes, `layouts[${index}]`);
  // Reflow touched display patterns in the same way as the editor. Legacy and
  // manually positioned Freeform children retain their authored geometry.
  // Process nested surfaces first; unrelated documents are never rearranged.
  const touched = new Set<string>();
  for (const spec of nodeSpecs) { const node = nodes.get(aliases.nodes[spec.key])!; touched.add(node.id); if (node.parentId) touched.add(node.parentId); }
  for (const node of base.nodes) if (removedIds.has(node.id) && node.parentId && nodes.has(node.parentId)) touched.add(node.parentId);
  const depth = (node: MeridianNode) => { let count = 0, cursor = node, visited = new Set<string>(); while (cursor.parentId && !visited.has(cursor.id)) { visited.add(cursor.id); const parent = nodes.get(cursor.parentId); if (!parent) break; cursor = parent; count++; } return count; };
  for (const parent of project.nodes.filter(node => node.kind !== 'canvas' && !node.smartLink && touched.has(node.id) && (isOrderedContainer(node) || isGallery(node) || project.nodes.some(child => child.parentId === node.id && child.childPosition === 'auto'))).sort((a,b) => depth(b)-depth(a))) {
    try { Object.assign(project, reflowContainedChildren(project, parent.id)); }
    catch (error) { throw new AuthoringError([{ code: 'invalid-project', severity: 'error', nodeId: parent.id, message: error instanceof Error ? error.message : String(error) }]); }
  }
  for (const id of deferredPositionLocks) {
    const node = project.nodes.find(item => item.id === id)!;
    node.positionLocked = true; node.childPosition = 'manual'; node.placement = 'free';
  }
  for (const workId of new Set([...touched].flatMap(id => { const node = nodes.get(id); return node ? [node.canvasId] : []; }))) reflowSmartLinks(project, workId);
  for (const node of project.nodes) if (node.targetId && removedIds.has(node.targetId) || node.dependencyIds?.some(id => removedIds.has(id)) || node.remoteLinks?.some(link => removedIds.has(link.targetId)))
    reject('remove.nodes', `Removing this branch leaves '${nodeTag(node)}' referring to a removed node. Clear or retarget the surviving reference in this recipe.`, 'dangling-reference');
  const issues = validateChange(base, project);
  if (issues.length) throw new AuthoringError(issues.map(issue => ({ ...issue, severity: 'error', code: 'invalid-project' })));
  const retained = { canvases: new Set(project.canvases.map(item => item.id)), roots: new Set(project.canvases.map(item => item.rootId)), nodes: new Set(project.nodes.map(item => item.id)), assets: new Set(project.assets.map(item => item.id)), links: new Set(project.links.map(item => item.id)) };
  for (const scope of Object.keys(aliases) as (keyof AuthoringAliases)[]) for (const [key, id] of Object.entries(aliases[scope])) if (!retained[scope].has(id)) delete aliases[scope][key];
  return { project, aliases, report: inspectProject(project) };
}

function descendants(project: MeridianProject, id: string): Set<string> {
  const ids = new Set([id]); let changed = true;
  while (changed) { changed = false; for (const node of project.nodes) if (node.parentId && ids.has(node.parentId) && !ids.has(node.id)) { ids.add(node.id); changed = true; } }
  return ids;
}
function bounds(nodes: MeridianNode[], collapsed = false): Bounds | null {
  if (!nodes.length) return null;
  const boxes = collapsed ? nodes.map(displayBounds) : nodes;
  const x = Math.min(...boxes.map(n => n.x)), y = Math.min(...boxes.map(n => n.y));
  return { x, y, width: Math.max(...boxes.map(n => n.x + n.width)) - x, height: Math.max(...boxes.map(n => n.y + n.height)) - y };
}
function moveGroup(project: MeridianProject, ids: Set<string>, dx: number, dy: number): void {
  for (const node of project.nodes) if (ids.has(node.id)) { node.x += dx; node.y += dy; }
  for (const link of project.links) if (ids.has(link.sourceId) && ids.has(link.targetId) && link.points) link.points = link.points.map(point => ({ x: point.x + dx, y: point.y + dy }));
}
function applyLayout(project: MeridianProject, layout: LayoutRecipe, aliases: Record<string, string>, path: string): void {
  object(layout, path, ['kind', 'nodes', 'x', 'y', 'gap', 'columns', 'radius', 'center', 'startAngle']);
  if (!['row', 'column', 'grid', 'radial'].includes(layout.kind)) reject(`${path}.kind`, 'Expected row, column, grid or radial.');
  if (!Array.isArray(layout.nodes) || !layout.nodes.length || layout.nodes.length > 10000) reject(`${path}.nodes`, 'Choose between 1 and 10000 nodes.');
  for (const key of ['x', 'y', 'startAngle'] as const) if (own(layout, key)) number(layout[key], `${path}.${key}`);
  for (const key of ['gap', 'radius'] as const) if (own(layout, key)) number(layout[key], `${path}.${key}`, 0);
  if (own(layout, 'columns')) { number(layout.columns, `${path}.columns`, 1, 10000); if (!Number.isInteger(layout.columns)) reject(`${path}.columns`, 'Expected a positive integer.'); }
  if (own(layout, 'center')) point(layout.center, `${path}.center`);
  const nodes = new Map(project.nodes.map(node => [node.id, node]));
  const selected = layout.nodes.map((ref, i) => nodes.get(resolve(ref, aliases, nodes, `${path}.nodes[${i}]`))!);
  if (new Set(selected.map(node => node.id)).size !== selected.length) reject(`${path}.nodes`, 'A layout cannot select the same node twice.');
  if (selected.some(node => node.kind === 'canvas')) reject(`${path}.nodes`, 'Work roots are not layout items.');
  if (new Set(selected.map(node => node.canvasId)).size !== 1) reject(`${path}.nodes`, 'A layout stays within one Work.');
  const groups = selected.map(node => ({ node, ids: descendants(project, node.id) }));
  for (const group of groups) if (selected.some(node => node.id !== group.node.id && group.ids.has(node.id))) reject(`${path}.nodes`, 'Choose a parent group or its descendants, not both.', 'overlapping-selection');
  const navigation = navigationIndex(project);
  // Gallery items render inside their owning tile. Their retained world coordinates
  // must not enlarge a layout footprint, but they still travel with the whole branch.
  const boxes = groups.map(group => bounds(project.nodes.filter(node => group.ids.has(node.id) &&
    (node.id === group.node.id || !navigation.ancestors(node.id).some(parent => group.ids.has(parent.id) && isGallery(parent)))))!);
  const origin = { x: layout.x ?? Math.min(...boxes.map(box => box.x)), y: layout.y ?? Math.min(...boxes.map(box => box.y)) };
  const gap = layout.gap ?? 32;
  const positions: Point[] = [];
  if (layout.kind === 'radial') {
    const center = layout.center ?? origin, radius = layout.radius ?? 240, start = (layout.startAngle ?? -90) * Math.PI / 180;
    boxes.forEach((box, i) => { const angle = start + 2 * Math.PI * i / boxes.length; positions.push({ x: center.x + Math.cos(angle) * radius - box.width / 2, y: center.y + Math.sin(angle) * radius - box.height / 2 }); });
  } else if (layout.kind === 'grid') {
    const columns = Math.min(layout.columns ?? Math.ceil(Math.sqrt(boxes.length)), boxes.length);
    const widths = Array.from({ length: columns }, (_, column) => Math.max(...boxes.filter((_, i) => i % columns === column).map(box => box.width)));
    const heights = Array.from({ length: Math.ceil(boxes.length / columns) }, (_, row) => Math.max(...boxes.slice(row * columns, (row + 1) * columns).map(box => box.height)));
    boxes.forEach((_, i) => positions.push({ x: origin.x + widths.slice(0, i % columns).reduce((a, b) => a + b + gap, 0), y: origin.y + heights.slice(0, Math.floor(i / columns)).reduce((a, b) => a + b + gap, 0) }));
  } else {
    let cursor = layout.kind === 'row' ? origin.x : origin.y;
    for (const box of boxes) { positions.push({ x: layout.kind === 'row' ? cursor : origin.x, y: layout.kind === 'column' ? cursor : origin.y }); cursor += (layout.kind === 'row' ? box.width : box.height) + gap; }
  }
  groups.forEach((group, i) => { group.node.childPosition = 'manual'; moveGroup(project, group.ids, positions[i].x - boxes[i].x, positions[i].y - boxes[i].y); });
}

const unique = <T>(values: T[]): T[] => [...new Set(values)];
/** Export reusable aliases and a compact authoring inventory without embedding attachment bytes. */
export function inventoryProject(project: MeridianProject): AuthoringInventory {
  if (!isProjectShape(project)) reject('project', 'Cannot inventory a malformed project.', 'invalid-project');
  const aliases: AuthoringAliases = { canvases: Object.create(null), roots: Object.create(null), nodes: Object.create(null), links: Object.create(null), assets: Object.create(null) };
  const nav = navigationIndex(project);
  const add = (scope: keyof AuthoringAliases, id: string, index: number) => { const key = `${scope === 'canvases' ? 'canvas' : scope === 'nodes' ? 'node' : scope === 'links' ? 'link' : 'asset'}-${index + 1}`; aliases[scope][key] = id; return key; };
  const canvases = project.canvases.map((canvas, index) => { const alias = add('canvases', canvas.id, index); aliases.roots[alias] = canvas.rootId; return { id: canvas.id, title: canvas.title, rootId: canvas.rootId, alias }; });
  const nodes = project.nodes.filter(node => node.kind !== 'canvas').map((node, index) => ({
    id: node.id, alias: add('nodes', node.id, index), canvasId: node.canvasId, parentId: node.parentId, tag: nodeTag(node), title: node.title, kind: node.kind,
    depth: nav.ancestors(node.id).length, childCount: nav.contents(node.id).nodes.length, visible: nav.isNodeVisible(node.id), collapsed: !!node.collapsed,
    bounds: { ...nav.bounds(node) }, branchBounds: { ...nav.branchBounds(node) },
    ...(node.text !== undefined ? { text: node.text } : {}), ...(node.assetId ? { assetId: node.assetId } : {}), ...(node.targetId ? { targetId: node.targetId } : {}),
    ...(node.contentItems !== undefined ? { contentItems: structuredClone(node.contentItems) } : {}), ...(node.showTitle !== undefined ? { showTitle: node.showTitle } : {}),
    ...(node.smartLink ? { smartLink: structuredClone(node.smartLink) } : {}),
    ...(node.containerVariant ? { displayPattern: node.containerVariant } : {}), ...(node.style ? { style: structuredClone(node.style) } : {}),
  }));
  const links = project.links.map((link, index) => ({ id: link.id, alias: add('links', link.id, index), canvasId: link.canvasId, sourceId: link.sourceId, targetId: link.targetId,
    ...(link.parentId ? { parentId: link.parentId } : {}), ...(link.label ? { label: link.label } : {}), kind: link.kind, relation: link.relation }));
  const assets = project.assets.map((asset, index) => ({ id: asset.id, alias: add('assets', asset.id, index), name: asset.name, mime: asset.mime, bytes: Math.max(0, Math.floor(asset.base64.length * 3 / 4) - (asset.base64.endsWith('==') ? 2 : asset.base64.endsWith('=') ? 1 : 0)) }));
  return { project: { id: project.id, title: project.title, revision: project.revision, entryCanvasId: project.entryCanvasId }, aliases, canvases, nodes, links, assets };
}
/** Geometry and reading heuristics are advisory. Text overflow is estimated, not browser-measured. */
export function inspectProject(project: MeridianProject): InspectionReport {
  const report: InspectionReport = { summary: { canvasCount: 0, nodeCount: 0, linkCount: 0, assetCount: 0, imageCount: 0, styledNodeCount: 0, warningCount: 0 }, canvases: [], warnings: [] };
  if (!isProjectShape(project)) { report.warnings.push({ code: 'invalid-project', message: 'The project has an unsupported or malformed shape.', severity: 'error' }); report.summary.warningCount = 1; return report; }
  const nodes = new Map(project.nodes.map(node => [node.id, node])), assets = new Map(project.assets.map(asset => [asset.id, asset])), assetIds = new Set(assets.keys());
  const selectedAsset = (node: MeridianNode) => { const id = activeNodeContent(node).assetId; return id ? assets.get(id) : undefined; };
  const imageNode = (node: MeridianNode): boolean => /^image\//i.test(selectedAsset(node)?.mime ?? '') || node.kind === 'image' && node.contentType === undefined;
  const documentNode = (node: MeridianNode): boolean => {
    const asset = selectedAsset(node);
    return !!asset && !/^(?:image|video|audio)\//i.test(asset.mime) || node.kind === 'document' && node.contentType === undefined;
  };
  for (const issue of validateProject(project)) report.warnings.push({ ...issue, code: 'invalid-project', severity: 'error', canvasId: issue.nodeId ? nodes.get(issue.nodeId)?.canvasId : project.links.find(l => l.id === issue.linkId)?.canvasId });
  const warn = (issue: Omit<AuthoringDiagnostic, 'severity'>): void => { if (report.warnings.length < 500) report.warnings.push({ ...issue, severity: 'warning' }); };
  const navigation = navigationIndex(project), visible = (node: MeridianNode): boolean => navigation.isNodeVisible(node.id);
  for (const canvas of project.canvases) {
    const content = project.nodes.filter(node => node.canvasId === canvas.id && node.kind !== 'canvas');
    const shown = content.filter(visible), links = project.links.filter(link => link.canvasId === canvas.id);
    const fonts = content.filter(node => activeNodeContent(node).text);
    const styledNodeCount = content.filter(node => node.style && Object.keys(node.style).length).length;
    const entry: CanvasInspection = {
      id: canvas.id, title: canvas.title, nodeCount: content.length, visibleNodeCount: shown.length, linkCount: links.length,
      imageCount: content.filter(imageNode).length, documentCount: content.filter(documentNode).length,
      containerCount: content.filter(node => node.kind === 'container' || node.kind === 'spreadsheet' || node.kind === 'module').length, bounds: bounds(shown, true),
      links: { visual: links.filter(link => link.kind === 'visual').length, functional: links.filter(link => link.kind === 'functional').length, numeric: links.filter(link => link.kind === 'numeric').length },
      styles: { styledNodeCount, fontSizes: unique(fonts.map(node => node.style?.fontSize ?? 12)).sort((a, b) => a - b), fontFamilies: unique(fonts.map(node => node.style?.fontFamily ?? 'sans')).sort(), fillColors: unique(content.map(node => node.style?.fill ?? '#142132')).sort(), variants: unique(content.map(node => node.style?.variant ?? 'card')).sort() },
      media: { assetIds: unique([...content.flatMap(nodeAssetIds), ...canvas.style?.backgroundAssetId ? [canvas.style.backgroundAssetId] : []]), missingAssetNodeIds: content.filter(node => nodeAssetIds(node).some(id => !assetIds.has(id))).map(node => node.id) },
    };
    report.canvases.push(entry);
    if (canvas.style?.backgroundAssetId && !assetIds.has(canvas.style.backgroundAssetId)) warn({ code: 'missing-media', canvasId: canvas.id, nodeId: canvas.rootId, message: `'${canvas.title}' points to an absent Work background image. Attach the original bytes or choose an included asset.` });
    for (const node of content) {
      const parent = node.parentId ? nodes.get(node.parentId) : undefined;
      if (visible(node) && parent && isEnclosingContainer(parent) && (node.x < parent.x || node.y < parent.y || node.x + node.width > parent.x + parent.width || node.y + node.height > parent.y + parent.height)) warn({ code: 'outside-container', canvasId: canvas.id, nodeId: node.id, nodeIds: [parent.id, node.id], message: `'${node.title}' extends outside '${parent.title}'. Resize the parent or move the child inside its bounds.` });
      if (nodeAssetIds(node).some(id => !assetIds.has(id))) warn({ code: 'missing-media', canvasId: canvas.id, nodeId: node.id, message: `'${node.title}' points to an absent attachment or background image. Attach the original bytes or choose an included asset.` });
      if (node.kind === 'reference' && node.targetId && !nodes.has(node.targetId) && (!own(project.snapshots, node.targetId) || project.snapshots[node.targetId].node.canvasId === node.canvasId)) warn({ code: 'missing-reference', canvasId: canvas.id, nodeId: node.id, message: `'${node.title}' has no available original or usable external snapshot.` });
      if (node.kind === 'reference' && node.targetId && nodes.has(node.targetId) && nodes.get(node.targetId)!.canvasId !== node.canvasId && !own(project.snapshots, node.targetId)) warn({ code: 'unsaved-reference', canvasId: canvas.id, nodeId: node.id, message: `'${node.title}' reads another Work, but its original has no saved snapshot yet. Save the project before relying on the reference value.` });
      if (node.kind === 'canvas-link' && node.targetId && !project.canvases.some(c => c.id === node.targetId || c.rootId === node.targetId)) warn({ code: 'missing-canvas-target', canvasId: canvas.id, nodeId: node.id, message: `'${node.title}' does not open an included Work. Set its target to a Work alias or root ID.` });
      const activeText = activeNodeContent(node).text;
      if (visible(node) && activeText && !hidesOwnContent(node) && node.style?.contentSizing === 'fixed') {
        const fontSize = node.style?.fontSize ?? 12, label = node.style?.variant === 'label';
        const columns = Math.max(1, Math.floor((node.width - (label ? 0 : 24)) / (fontSize * (node.style?.fontFamily === 'mono' ? 0.6 : 0.53))));
        const lines = activeText.split('\n').reduce((count, line) => count + Math.max(1, Math.ceil(line.length / columns)), 0);
        const estimated = Math.ceil(lines * fontSize * 1.45), available = Math.max(0, node.height - (label ? 0 : 64));
        if (estimated > available + 1) warn({ code: 'estimated-text-overflow', canvasId: canvas.id, nodeId: node.id, message: `'${node.title}' needs approximately ${estimated}px of body height; about ${available}px is available. Enlarge the node, shorten the text, or reduce type size, then verify the rendered result.` });
      }
    }
    const siblings = new Map<string | null, MeridianNode[]>();
    for (const node of shown) { const list = siblings.get(node.parentId) ?? []; list.push(node); siblings.set(node.parentId, list); }
    for (const family of siblings.values()) {
      family.sort((a, b) => a.x - b.x || a.id.localeCompare(b.id));
      for (let i = 0; i < family.length && report.warnings.length < 500; i++) for (let j = i + 1; j < family.length && family[j].x < family[i].x + displayBounds(family[i]).width && report.warnings.length < 500; j++) {
        const a = { ...family[i], ...displayBounds(family[i]) }, b = { ...family[j], ...displayBounds(family[j]) };
        const overlapX = Math.min(a.x + a.width, b.x + b.width) - Math.max(a.x, b.x);
        const overlapY = Math.min(a.y + a.height, b.y + b.height) - Math.max(a.y, b.y);
        if (overlapX > 1 && overlapY > 1) warn({ code: 'sibling-overlap', canvasId: canvas.id, nodeIds: [a.id, b.id], message: `'${a.title}' and '${b.title}' overlap by ${Math.round(overlapX)} × ${Math.round(overlapY)} world units. Confirm intentional layering or separate them.` });
      }
    }
    if (fonts.length >= 8 && entry.styles.fontSizes.length === 1 && entry.styles.fontFamilies.length === 1 && entry.styles.variants.length === 1 && entry.styles.fillColors.length === 1) warn({ code: 'uniform-presentation', canvasId: canvas.id, message: `${fonts.length} text-bearing nodes share one type size, family, surface and variant. Consider distinct headings, focal images, labels or grouped regions to establish reading hierarchy.` });
  }
  const content = project.nodes.filter(node => node.kind !== 'canvas');
  report.summary = { canvasCount: project.canvases.length, nodeCount: content.length, linkCount: project.links.length, assetCount: project.assets.length, imageCount: content.filter(imageNode).length, styledNodeCount: report.canvases.reduce((sum, canvas) => sum + canvas.styles.styledNodeCount, 0), warningCount: report.warnings.length };
  return report;
}
