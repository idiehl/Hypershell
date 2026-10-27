import type { LinkDirection, MeridianAsset, MeridianLink, MeridianNode, MeridianProject } from './types.ts';
import { cellAddress, isEnclosingContainer, resolveGridCell, resolveReferenceTarget } from './cells.ts';
import { isGallery, isEmbeddedGallery } from './gallery.ts';
import { sizingBounds, branchSizingBounds, nodeSizingMode, type SizingContext } from './sizing.ts';
import { activeNodeContent, nodeContentItems } from './content.ts';
import { nodeTag } from './node-tag.ts';

/** Display geometry only. Collapsing never rewrites a node's expanded geometry. */
export function isCompactContainer(node: MeridianNode, hasChildren = false): boolean {
  if (nodeSizingMode(node) === 'auto') return node.collapsed === true;
  if (node.childLayout === 'spatial' && !['container', 'module', 'spreadsheet'].includes(node.kind) && !isGallery(node)) return false;
  // Media and executable controls keep their own usable surface when their children hide.
  return node.collapsed === true && (node.kind === 'container' || node.kind === 'spreadsheet' || node.kind === 'module' || node.kind === 'canvas' ||
    hasChildren === true && (node.kind === 'text' || node.kind === 'canvas-link'));
}
/** Standalone geometry; use navigationIndex(project).bounds for child-aware geometry. */
export function displayBounds(node: MeridianNode) { return sizingBounds(undefined, node); }

/** Explicit visual arrows override the relation's legacy presentation, never signal flow. */
export function linkDirection(link: MeridianLink): LinkDirection {
  if (link.kind !== 'visual') return 'forward';
  return link.direction ?? (link.relation === 'generic' ? 'none' : link.relation === 'mutual' || link.relation === 'cyclical' ? 'both' : 'forward');
}

/** A cycle-safe, shared index for navigation; nothing here mutates the document. */
export function navigationIndex(project: MeridianProject, sizingContext: SizingContext = {}) {
  const nodes = new Map(project.nodes.map(node => [node.id, node]));
  const children = new Map<string, MeridianNode[]>();
  for (const node of project.nodes) if (node.parentId) {
    const siblings = children.get(node.parentId) ?? [];
    siblings.push(node); children.set(node.parentId, siblings);
  }
  const ancestors = (id: string): MeridianNode[] => {
    const result: MeridianNode[] = [], seen = new Set([id]);
    let parentId = nodes.get(id)?.parentId;
    while (parentId && !seen.has(parentId)) {
      seen.add(parentId); const parent = nodes.get(parentId); if (!parent) break;
      result.push(parent); parentId = parent.parentId;
    }
    return result;
  };
  const descendants = (id: string): MeridianNode[] => {
    const result: MeridianNode[] = [], seen = new Set([id]), pending = [...(children.get(id) ?? [])].reverse();
    while (pending.length) {
      const node = pending.pop()!; if (seen.has(node.id)) continue;
      seen.add(node.id); result.push(node); pending.push(...[...(children.get(node.id) ?? [])].reverse());
    }
    return result;
  };
  const presentationAncestors = (id: string): MeridianNode[] => {
    const result: MeridianNode[] = [];
    for (const parent of ancestors(id)) { result.push(parent); if (parent.expansionTray && !isEnclosingContainer(parent)) break; }
    return result;
  };
  const outline = (canvasId: string, visibleOnly = false) => {
    const root = project.canvases.find(canvas => canvas.id === canvasId)?.rootId;
    if (visibleOnly) {
      const rows: Array<{ node: MeridianNode; depth: number }> = [], seen = new Set<string>();
      const pending = root && !nodes.get(root)?.collapsed ? (children.get(root) ?? []).map(node => ({ node, depth: 0 })).reverse() : [];
      while (pending.length) {
        const row = pending.pop()!; if (seen.has(row.node.id) || row.node.canvasId !== canvasId) continue;
        seen.add(row.node.id); if (!isNodeVisible(row.node.id)) continue; rows.push(row);
        if (!row.node.collapsed && !isEmbeddedGallery(row.node)) pending.push(...(children.get(row.node.id) ?? []).map(node => ({ node, depth: row.depth + 1 })).reverse());
      }
      return rows;
    }
    return root ? descendants(root).filter(node => node.canvasId === canvasId).map(node => ({ node, depth: Math.max(0, ancestors(node.id).length - 1) })) : [];
  };
  const original = (id: string): MeridianNode | undefined => {
    const node = nodes.get(id);
    if (!node?.targetId || !['reference', 'canvas-link'].includes(node.kind)) return undefined;
    if (node.kind === 'reference' && node.targetCell) return resolveGridCell(project, node.targetId, node.targetCell);
    return nodes.get(node.targetId) ?? nodes.get(project.canvases.find(canvas => canvas.id === node.targetId)?.rootId ?? '');
  };
  const backlinks = (id: string) => {
    const canvasId = project.canvases.find(canvas => canvas.rootId === id)?.id;
    return project.nodes.filter(node => ['reference', 'canvas-link'].includes(node.kind) && (node.targetId === id || (canvasId && node.targetId === canvasId) || node.kind === 'reference' && node.targetCell && node.targetId && resolveGridCell(project, node.targetId, node.targetCell)?.id === id));
  };
  /** Collapsed branches and embedded gallery objects use their outer visible tile for navigation. */
  const visibleNode = (id: string): MeridianNode | undefined => {
    let visible = nodes.get(id);
    let outsideTray = false;
    for (const parent of ancestors(id)) {
      if (parent.collapsed) { visible = parent; continue; }
      if (parent.expansionTray && !isEnclosingContainer(parent)) outsideTray = true;
      if (isEmbeddedGallery(parent) && !outsideTray) { visible = parent; continue; }
      if (isGallery(parent)) {
        const context = sizingContext.galleryItems;
        const active = context && typeof (context as ReadonlyMap<string, string>).get === 'function' ? (context as ReadonlyMap<string, string>).get(parent.id)
          : (context as Readonly<Record<string, string>> | undefined)?.[parent.id];
        const items = children.get(parent.id) ?? [], chosen = items.find(node => node.id === active) ?? items[0];
        const immediate = visible?.parentId === parent.id ? visible : ancestors(visible?.id ?? '').find(node => node.parentId === parent.id);
        if (immediate?.id !== chosen?.id) visible = parent;
      }
    }
    return visible;
  };
  const isNodeVisible = (id: string): boolean => nodes.has(id) && visibleNode(id)?.id === id;
  const linkParent = (link: MeridianLink): MeridianNode | undefined => {
    if (link.parentId !== undefined) return nodes.get(link.parentId);
    const targetAncestors = new Set([link.targetId, ...ancestors(link.targetId).map(node => node.id)]);
    return [nodes.get(link.sourceId), ...ancestors(link.sourceId)].find(node => node && targetAncestors.has(node.id));
  };
  const linksByParent = new Map<string, MeridianLink[]>();
  for (const link of project.links) {
    const parent = linkParent(link); if (!parent) continue;
    const links = linksByParent.get(parent.id) ?? []; links.push(link); linksByParent.set(parent.id, links);
  }
  const contents = (id: string) => ({ nodes: children.get(id) ?? [], links: linksByParent.get(id) ?? [] });
  /** Disclosure eligibility is separate from whether a connection needs a canvas line. */
  const isLinkRevealed = (link: MeridianLink): boolean => {
    const parent = linkParent(link);
    return !!parent && parent.canvasId === link.canvasId && !parent.collapsed && isNodeVisible(parent.id) &&
      isNodeVisible(link.sourceId) && isNodeVisible(link.targetId);
  };
  const canvasRoots = new Set(project.canvases.map(canvas => canvas.rootId));
  /** Containment already presents this relationship; retain the link only in the document. */
  const isEmbeddedLink = (link: MeridianLink): boolean => {
    const source = nodes.get(link.sourceId), target = nodes.get(link.targetId);
    if (!source || !target || source.id === target.id || source.canvasId !== link.canvasId || target.canvasId !== link.canvasId) return false;
    const trayOwns = (owner: MeridianNode, child: MeridianNode) => !!owner.expansionTray && !isEnclosingContainer(owner) &&
      ancestors(child.id).some(parent => parent.id === owner.id);
    if (trayOwns(source, target) || trayOwns(target, source)) return true;
    const encloses = (container: MeridianNode, child: MeridianNode) => container.kind !== 'canvas' &&
      !canvasRoots.has(container.id) && isEnclosingContainer(container) && presentationAncestors(child.id).some(parent => parent.id === container.id);
    return encloses(source, target) || encloses(target, source);
  };
  const isLinkVisible = (link: MeridianLink): boolean => !link.snap && isLinkRevealed(link) && !isEmbeddedLink(link);
  const bounds = (node: MeridianNode) => sizingBounds(project, node, { node: id => nodes.get(id), children: id => children.get(id) ?? [], hasContents: (linksByParent.get(node.id)?.length ?? 0) > 0, context: sizingContext });
  const branchBounds = (node: MeridianNode) => branchSizingBounds(project, node, { node: id => nodes.get(id), children: id => children.get(id) ?? [], context: sizingContext });
  const packingBranchBounds = (node: MeridianNode) => branchSizingBounds(project, node, { node: id => nodes.get(id), children: id => children.get(id) ?? [], context: sizingContext, excludeTrayContents: true });
  return { nodes, children, ancestors, presentationAncestors, descendants, outline, original, backlinks, visibleNode, isNodeVisible, linkParent, linksByParent, contents, isLinkRevealed, isEmbeddedLink, isLinkVisible, bounds, branchBounds, packingBranchBounds };
}

export function decodeTextAsset(asset: MeridianAsset): string {
  try { return new TextDecoder().decode(Uint8Array.from(atob(asset.base64), character => character.charCodeAt(0))); }
  catch { return ''; }
}
export interface SearchResult { nodeId: string; canvasId: string; title: string; canvasTitle: string; snippet: string; source: string }
const fold = (value: string) => value.normalize('NFKD').replace(/\p{M}/gu, '').toLocaleLowerCase();

/** Decode each attachment once per document index, including text beyond card previews. */
export function createProjectSearch(project: MeridianProject): (query: string, limit?: number) => SearchResult[] {
  const assets = new Map(project.assets.map(asset => [asset.id, asset]));
  const canvases = new Map(project.canvases.map(canvas => [canvas.id, canvas.title]));
  const decoded = new Map<MeridianAsset, string>();
  const documents = project.nodes.map(node => {
    const reference = resolveReferenceTarget(project, node), snapshot = reference?.snapshot;
    const source = node.kind === 'reference' ? reference?.node : node;
    const content = source ? activeNodeContent(source) : undefined;
    const sourceAsset = (id: string): MeridianAsset | undefined => reference?.saved ? [snapshot?.asset, snapshot?.backgroundAsset, ...snapshot?.assets ?? []].find(asset => asset?.id === id) : assets.get(id);
    const asset = content?.assetId ? sourceAsset(content.assetId) : undefined;
    if (asset && !decoded.has(asset)) decoded.set(asset, /^(text\/(plain|markdown|csv|tab-separated-values)|application\/(csv|tab-separated-values))(;|$)/i.test(asset.mime) ? decodeTextAsset(asset) : '');
    const kind = node.kind === 'canvas-link' ? 'canvas' : node.kind === 'joint' ? 'operation' : node.kind === 'junction' ? `utility ${node.utilityVariant ?? 'joint'}` : node.kind;
    const media = source && ['media', 'text', 'image', 'document'].includes(source.kind)
      ? `media ${content?.type === 'text' ? 'text' : content?.type === 'link' ? 'hyperlink' : asset ? `${asset.mime.startsWith('image/') ? 'image picture' : asset.mime.startsWith('video/') ? 'video' : asset.mime.startsWith('audio/') ? 'audio' : 'document'} ${asset.mime}` : ''}` : '';
    const pattern = node.containerVariant === 'contained' || node.containerVariant === 'spatial' ? 'freeform' : node.containerVariant ?? '';
    const fields = [
      { source: 'Tag', text: nodeTag(node) },
      { source: 'Title', text: node.title },
      { source: 'Text', text: content?.text ?? '' },
      { source: 'Attachment', text: asset ? `${asset.name} ${asset.mime}` : '' },
      { source: 'Attachment text', text: asset ? decoded.get(asset) ?? '' : '' },
      { source: 'Work / identity', text: `${canvases.get(node.canvasId) ?? ''} ${node.id} ${node.cell ? `${cellAddress(node.cell)} ${node.cell.name ?? ''}` : ''} ${node.targetCell ?? ''}` },
      { source: 'Hyperlink', text: content?.hyperlink ? `${content.hyperlink.label ?? ''} ${content.hyperlink.url}` : '' },
      { source: 'Remote link', text: (node.remoteLinks ?? []).map(link => `${link.targetId} ${link.label ?? ''} ${link.relation}`).join('\n') },
      ...source?.contentItems !== undefined ? nodeContentItems(source).flatMap(item => {
        if (item.type === 'text') return [{ source: 'Hub text', text: item.text }];
        if (item.type === 'link') return [{ source: 'Hub hyperlink', text: `${item.label ?? ''} ${item.url}` }];
        const attached = sourceAsset(item.assetId); if (!attached) return [];
        if (!decoded.has(attached)) decoded.set(attached, /^(text\/(plain|markdown|csv|tab-separated-values)|application\/(csv|tab-separated-values))(;|$)/i.test(attached.mime) ? decodeTextAsset(attached) : '');
        return [{ source: 'Hub attachment', text: `${attached.name} ${attached.mime}` }, { source: 'Hub attachment text', text: decoded.get(attached) ?? '' }];
      }) : [],
      { source: 'Type', text: `${kind} ${media} ${pattern} ${node.kind === 'spreadsheet' ? 'container spreadsheet' : ''}` },
    ].map(field => ({ ...field, folded: fold(field.text) }));
    return { node, fields, identity: fold(node.id) };
  });
  return (query, limit = 100) => {
    const foldedQuery = fold(query.trim()), terms = foldedQuery.split(/\s+/).filter(Boolean); if (!terms.length) return [];
    const matches = documents.flatMap(({ node, fields, identity }) => {
      if (!terms.every(term => fields.some(field => field.folded.includes(term)))) return [];
      const exactIdentity = identity === foldedQuery, exactTag = fields[0]!.folded === foldedQuery, exactTitle = fields[1]!.folded === foldedQuery;
      // Prefer the field explaining the most query terms; preserve the existing
      // title/content/metadata priority when terms are split across equal matches.
      // An exact handle must precede mentions in source packets. Shared packets
      // can contain every GUID and otherwise crowd the actual node out of the cap.
      const field = exactIdentity ? fields[5]! : exactTag ? fields[0]! : exactTitle ? fields[1]! : fields.reduce((best, candidate) => {
        const count = terms.filter(term => candidate.folded.includes(term)).length;
        return count > best.count ? { field: candidate, count } : best;
      }, { field: fields[0]!, count: 0 }).field;
      const at = Math.max(0, field.folded.indexOf(terms.find(term => field.folded.includes(term))!) - 55);
      const snippet = `${at ? '…' : ''}${field.text.slice(at, at + 190).replace(/\s+/g, ' ')}${field.text.length > at + 190 ? '…' : ''}`;
      return [{ nodeId: node.id, canvasId: node.canvasId, title: nodeTag(node), canvasTitle: canvases.get(node.canvasId) ?? 'Work', snippet, source: field.source,
        rank: exactIdentity ? -3 : exactTag ? -2 : exactTitle ? -1 : fields.indexOf(field) }];
    });
    return matches.sort((a, b) => a.rank - b.rank || a.canvasTitle.localeCompare(b.canvasTitle) || a.title.localeCompare(b.title)).slice(0, limit);
  };
}
