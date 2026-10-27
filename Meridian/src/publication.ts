import type { MeridianAsset, MeridianLink, MeridianNode, MeridianProject, PublicationPayload, SavedSnapshot } from './core/types.ts';
import { forkProject } from './core/model.ts';
import { isProjectShape, validateProject } from './core/rules.ts';
import { CANVAS_STYLE_KEYS, LINK_STYLE_KEYS, NODE_STYLE_KEYS, nodeAssetIds } from './core/appearance.ts';
import { summarizeDependencies } from './core/dependencies.ts';
import { resolveReferenceTarget } from './core/cells.ts';
import { TEXT_FORMAT_KEYS } from './core/rich-text.ts';

const clone = <T>(value: T): T => JSON.parse(JSON.stringify(value)) as T;
const nodeFields = ['id', 'canvasId', 'parentId', 'kind', 'title', 'tag', 'titleRuns', 'showTitle', 'contentItems', 'smartLink', 'x', 'y', 'width', 'height', 'positionLocked', 'sizeMode', 'containerSizes', 'cluster', 'retiredBlockContent', 'contentType', 'childPosition', 'childOffset', 'expansionTray', 'collapsed', 'text', 'textRuns', 'assetId', 'attachments', 'hyperlinks', 'value', 'valueType', 'closed', 'operation', 'targetId', 'canvasTarget', 'containerVariant', 'utilityVariant', 'exposed', 'rules', 'snapping', 'style', 'dependencyIds', 'remoteLinks', 'childLayout', 'placement', 'attachSide', 'grid', 'cell', 'targetCell', 'childArrangement'] as const;
const snappingFields = ['stickyEdges', 'snapToGrid', 'smartAlign'] as const;
const linkFields = ['id', 'canvasId', 'parentId', 'sourceId', 'targetId', 'kind', 'relation', 'direction', 'targetPort', 'sourceAnchor', 'targetAnchor', 'points', 'label', 'style', 'snap'] as const;
function pick<T extends object>(value: T, fields: readonly (keyof T)[]): T {
  return Object.fromEntries(fields.filter(field => value[field] !== undefined).map(field => [field, clone(value[field])])) as T;
}
function cleanNode(node: MeridianNode): MeridianNode {
  const result = pick(node, nodeFields);
  if (result.rules) result.rules = pick(result.rules, ['nodeKinds', 'linkKinds', 'operations']);
  if (result.snapping) result.snapping = pick(result.snapping, snappingFields);
  if (result.style) result.style = pick(result.style, NODE_STYLE_KEYS);
  if (result.canvasTarget) result.canvasTarget = pick(result.canvasTarget, ['projectId', 'canvasId']);
  if (result.cluster) result.cluster = pick(result.cluster, ['buttonShape']);
  if (result.smartLink) result.smartLink = pick(result.smartLink, ['direction', 'gap', 'resolvedDirection']);
  if (result.childOffset) result.childOffset = pick(result.childOffset, ['x', 'y']);
  if (result.expansionTray) result.expansionTray = { offset: pick(result.expansionTray.offset, ['x', 'y']),
    ...(result.expansionTray.layoutVersion !== undefined ? { layoutVersion: result.expansionTray.layoutVersion } : {}),
    ...(result.expansionTray.size ? { size: pick(result.expansionTray.size, ['width', 'height']) } : {}) };
  if (result.containerSizes) { result.containerSizes = pick(result.containerSizes, ['expanded', 'collapsed']); for (const mode of ['expanded', 'collapsed'] as const) if (result.containerSizes[mode]) result.containerSizes[mode] = pick(result.containerSizes[mode], ['width', 'height', 'sizeMode']); }
  if (result.retiredBlockContent) {
    result.retiredBlockContent = pick(result.retiredBlockContent, ['title', 'text', 'contentType', 'titleRuns', 'textRuns']);
    for (const field of ['titleRuns', 'textRuns'] as const) if (result.retiredBlockContent[field]) result.retiredBlockContent[field] = result.retiredBlockContent[field].map(run => ({ start: run.start, end: run.end, style: pick(run.style, TEXT_FORMAT_KEYS) }));
  }
  if (result.grid) result.grid = pick(result.grid, ['columns', 'rows', 'cellWidth', 'cellHeight', 'gap', 'showHeaders', 'fillOrder', 'windowColumns', 'windowRows', 'scrollDirection']);
  if (result.cell) result.cell = pick(result.cell, ['column', 'row', 'name']);
  if (result.remoteLinks) result.remoteLinks = result.remoteLinks.map(link => pick(link, ['id', 'targetId', 'relation', 'label']));
  if (result.hyperlinks) result.hyperlinks = result.hyperlinks.map(link => pick(link, ['url', 'label']));
  for (const field of ['titleRuns', 'textRuns'] as const) if (result[field]) result[field] = result[field].map(run => ({ start: run.start, end: run.end, style: pick(run.style, TEXT_FORMAT_KEYS) }));
  if (result.contentItems) result.contentItems = result.contentItems.map(item => item.type === 'asset' ? pick(item, ['id', 'type', 'assetId']) : item.type === 'link' ? pick(item, ['id', 'type', 'url', 'label']) : { ...pick(item, ['id', 'type', 'text']), ...(item.textRuns ? { textRuns: item.textRuns.map(run => ({ start: run.start, end: run.end, style: pick(run.style, TEXT_FORMAT_KEYS) })) } : {}) });
  return result;
}
function cleanAsset(asset: MeridianAsset): MeridianAsset { return pick(asset, ['id', 'name', 'mime', 'base64']); }
function cleanSnapshot(snapshot: SavedSnapshot): SavedSnapshot {
  return { node: cleanNode(snapshot.node), value: snapshot.value, savedAt: snapshot.savedAt, ...(snapshot.asset ? { asset: cleanAsset(snapshot.asset) } : {}), ...(snapshot.backgroundAsset ? { backgroundAsset: cleanAsset(snapshot.backgroundAsset) } : {}), ...(snapshot.assets ? { assets: snapshot.assets.map(cleanAsset) } : {}) };
}
function cleanLink(link: MeridianLink): MeridianLink {
  const result = pick(link, linkFields);
  if (result.style) result.style = pick(result.style, LINK_STYLE_KEYS);
  if (result.snap) result.snap = pick(result.snap, ['locked']);
  if (result.points) result.points = result.points.map(point => ({ x: point.x, y: point.y }));
  if (result.sourceAnchor) result.sourceAnchor = { x: result.sourceAnchor.x, y: result.sourceAnchor.y };
  if (result.targetAnchor) result.targetAnchor = { x: result.targetAnchor.x, y: result.targetAnchor.y };
  return result;
}

/** A publication owns a portable copy of both its canvases and only their used assets. */
export function createPublicationPayload(project: MeridianProject, options: { includeExternalReferences?: boolean } = {}): PublicationPayload {
  const includeExternal = options.includeExternalReferences ?? true;
  const nodes = project.nodes.map(cleanNode);
  const canvases = project.canvases.map(canvas => ({ id: canvas.id, rootId: canvas.rootId, title: canvas.title, ...(canvas.snapping ? { snapping: pick(canvas.snapping, snappingFields) } : {}), ...(canvas.style ? { style: pick(canvas.style, CANVAS_STYLE_KEYS) } : {}) }));
  const links = project.links.map(cleanLink);
  const availableSnapshots = Object.fromEntries(Object.entries(project.snapshots).map(([id, snapshot]) => [id, cleanSnapshot(snapshot)]));
  const knownNodes = new Set(nodes.map(node => node.id));
  const knownCanvases = new Set(project.canvases.map(canvas => canvas.id));
  const requestedSnapshots = new Set(nodes.filter(node => {
    if (node.kind !== 'reference' || !node.targetId) return false;
    const source = nodes.find(source => source.id === node.targetId);
    const saved = Object.hasOwn(project.snapshots, node.targetId) ? project.snapshots[node.targetId] : undefined;
    return (source?.canvasId ?? saved?.node.canvasId) !== node.canvasId;
  }).map(node => node.targetId!));
  for (const node of project.nodes) if (node.kind === 'reference' && node.targetCell) {
    const target = resolveReferenceTarget(project, node);
    if (target?.saved) requestedSnapshots.add(target.node.id);
  }
  // Inspect the same allowlisted document we package. Source metadata must still be stripped.
  const report = summarizeDependencies({ ...project, nodes, canvases, links, snapshots: availableSnapshots }, { includeExternalReferences: includeExternal });
  for (const dependency of report.snapshots) requestedSnapshots.add(dependency.id);
  const snapshots: Record<string, SavedSnapshot> = Object.create(null) as Record<string, SavedSnapshot>;
  const missing = new Set<string>();
  for (const target of requestedSnapshots) {
    const snapshot = Object.hasOwn(availableSnapshots, target) ? availableSnapshots[target] : undefined;
    if ((includeExternal || knownNodes.has(target)) && snapshot && (!knownCanvases.has(snapshot.node.canvasId) || knownNodes.has(target))) {
      snapshots[target] = snapshot;
    } else missing.add(snapshot?.node.title || nodes.find(node => node.targetId === target)?.title || target);
  }
  for (const node of nodes) {
    if (node.kind === 'reference' && node.targetId && !knownNodes.has(node.targetId) && !Object.hasOwn(snapshots, node.targetId)) missing.add(node.title || node.targetId);
  }
  const dependencies = project.dependencies.filter(dependency => !knownCanvases.has(dependency.canvasId)).map(dependency => ({ canvasId: dependency.canvasId, title: dependency.title, required: dependency.required }));
  for (const dependency of dependencies) {
    // A node snapshot is never evidence that its entire source canvas was bundled.
    if (dependency.required) missing.add(dependency.title);
  }
  for (const dependency of [...report.missing, ...report.excluded]) if (dependency.required) missing.add(dependency.title);
  const usedAssetIds = new Set([...nodes.flatMap(nodeAssetIds), ...canvases.flatMap(canvas => canvas.style?.backgroundAssetId ? [canvas.style.backgroundAssetId] : [])]);
  for (const snapshot of Object.values(snapshots)) if (snapshot.node.assetId && !snapshot.asset) usedAssetIds.add(snapshot.node.assetId);
  for (const snapshot of Object.values(snapshots)) for (const assetId of nodeAssetIds(snapshot.node)) if (![snapshot.asset?.id, snapshot.backgroundAsset?.id, ...snapshot.assets?.map(asset => asset.id) ?? []].includes(assetId)) usedAssetIds.add(assetId);
  for (const snapshot of Object.values(snapshots)) if (snapshot.node.style?.backgroundAssetId && !snapshot.backgroundAsset) usedAssetIds.add(snapshot.node.style.backgroundAssetId);
  const assets = project.assets.filter(asset => usedAssetIds.has(asset.id)).map(cleanAsset);
  for (const id of usedAssetIds) if (!assets.some(asset => asset.id === id)) missing.add(`Asset ${id}`);
  const portable: MeridianProject = {
    schemaVersion: 1, id: project.id, title: project.title, revision: project.revision, entryCanvasId: project.entryCanvasId,
    canvases, nodes, links, assets, snapshots, dependencies,
  };
  return { format: 'atlas-meridian-publication', version: 1, project: portable, publishedAt: new Date().toISOString(), missing: [...missing] };
}

function htmlText(value: string): string { return value.replace(/[&<>"']/g, character => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[character]!)); }
function inertJson(value: unknown): string { return JSON.stringify(value).replace(/[<>&\u2028\u2029]/g, character => `\\u${character.charCodeAt(0).toString(16).padStart(4, '0')}`); }

/** viewerJs and css are trusted build outputs, never author-supplied source. */
export function buildPublication(project: MeridianProject, viewerJs: string, css: string, options: { includeExternalReferences?: boolean } = {}): string {
  const payload = createPublicationPayload(project, options);
  // Escaping closing tags protects the enclosing raw-text elements even in bundled strings/comments.
  const script = viewerJs.replace(/<\/script/gi, '<\\/script');
  const style = css.replace(/<\/style/gi, '<\\/style');
  return `<!doctype html>\n<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><meta name="referrer" content="no-referrer"><title>${htmlText(project.title)} · Atlas Meridian</title><style>${style}</style></head><body class="meridian-publication"><main id="meridian-viewer" aria-label="Atlas Meridian viewer"></main><script type="application/json" id="meridian-data">${inertJson(payload)}</script><script>${script}</script></body></html>`;
}

function assertProject(value: unknown): asserts value is MeridianProject {
  if (!isProjectShape(value)) throw new Error('The publication project is malformed.');
  const issues = validateProject(value);
  if (issues.length) throw new Error(`The publication project is invalid: ${issues.map(issue => issue.message).join(' ')}`);
}

export function readPublication(html: string): MeridianProject {
  const scripts = html.matchAll(/<script\b([^>]*)>([\s\S]*?)<\/script\s*>/gi);
  let payload: PublicationPayload | undefined;
  for (const match of scripts) {
    if (!/\bid\s*=\s*(["'])meridian-data\1/i.test(match[1]) || !/\btype\s*=\s*(["'])application\/json\1/i.test(match[1])) continue;
    if (payload) throw new Error('The publication has more than one data payload.');
    try { payload = JSON.parse(match[2]) as PublicationPayload; } catch { throw new Error('The publication data could not be read.'); }
  }
  if (!payload || payload.format !== 'atlas-meridian-publication' || payload.version !== 1) throw new Error('This is not a supported Atlas Meridian publication.');
  assertProject(payload.project);
  return forkProject(payload.project);
}
