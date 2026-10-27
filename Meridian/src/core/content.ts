import type { MeridianNode, NodeContentItem, NodeContentType, NodeHyperlink } from './types.ts';
import { isTextRuns } from './rich-text.ts';

export const MAX_NODE_CONTENT_ITEMS = 1000;
const identifier = (value: unknown): value is string => typeof value === 'string' && /^[A-Za-z0-9][A-Za-z0-9_-]{0,95}$/.test(value);
const hyperlink = (url: unknown): url is string => {
  if (typeof url !== 'string' || url.length > 2048 || url !== url.trim() || /[\u0000-\u0020\u007f]/.test(url)) return false;
  try { const parsed = new URL(url); return ['http:', 'https:', 'mailto:'].includes(parsed.protocol) && (parsed.protocol === 'mailto:' ? !!parsed.pathname : !!parsed.hostname); } catch { return false; }
};
/** Strict inert vocabulary shared by untrusted publication/recipe validation. */
export function isNodeContentItems(value: unknown): value is NodeContentItem[] {
  if (!Array.isArray(value) || value.length > MAX_NODE_CONTENT_ITEMS) return false;
  const seen = new Set<string>();
  return value.every(item => {
    if (!item || typeof item !== 'object' || Array.isArray(item) || !identifier(item.id) || seen.has(item.id)) return false;
    seen.add(item.id);
    const keys = item.type === 'text' ? ['id', 'type', 'text', 'textRuns'] : item.type === 'asset' ? ['id', 'type', 'assetId'] : item.type === 'link' ? ['id', 'type', 'url', 'label'] : [];
    if (!keys.length || Object.keys(item).some(key => !keys.includes(key))) return false;
    if (item.type === 'text') return typeof item.text === 'string' && item.text.length <= 1_000_000 && (item.textRuns === undefined || isTextRuns(item.text, item.textRuns));
    if (item.type === 'asset') return identifier(item.assetId);
    return hyperlink(item.url) && (item.label === undefined || typeof item.label === 'string' && item.label.length <= 160 && !/[\u0000-\u0008\u000b\u000c\u000e-\u001f\u007f-\u009f]/.test(item.label));
  });
}

/** Read-only legacy projection. Opening a Hub must never rewrite an older Work. */
export function nodeContentItems(node: MeridianNode): NodeContentItem[] {
  if (node.contentItems !== undefined) return structuredClone(node.contentItems);
  if (node.kind === 'block') return [];
  const items: NodeContentItem[] = [];
  const assets = [...new Set([node.assetId, ...node.attachments ?? []].filter((id): id is string => !!id))];
  const type = effectiveContentType(node);
  if (node.text || type === 'text' && (assets.length || node.hyperlinks?.length)) items.push({ id: 'legacy-text', type: 'text', text: node.text ?? '', ...(node.textRuns ? { textRuns: structuredClone(node.textRuns) } : {}) });
  assets.forEach((assetId, index) => items.push({ id: `legacy-asset-${index}`, type: 'asset', assetId }));
  node.hyperlinks?.forEach((link, index) => items.push({ id: `legacy-link-${index}`, type: 'link', ...structuredClone(link) }));
  const first = items.findIndex(item => item.type === type);
  if (first > 0) items.unshift(...items.splice(first, 1));
  return items;
}

/** Canonicalize only after an explicit Hub edit; dormant legacy fields remain recoverable. */
export function withNodeContentItems(node: MeridianNode, items: readonly NodeContentItem[]): MeridianNode {
  if (!isNodeContentItems(items)) throw new Error('Node Hub content must contain at most 1000 uniquely identified text, asset or hyperlink items.');
  return { ...node, contentItems: structuredClone(items) };
}

/** Compatibility-only view for renderers that still read legacy primary fields. */
export function projectNodeContent(node: MeridianNode): MeridianNode {
  if (node.contentItems === undefined && node.showTitle !== false) return node;
  const projected = { ...node };
  if (node.showTitle === false) { projected.title = ''; delete projected.titleRuns; }
  if (node.contentItems === undefined) return projected;
  delete projected.text; delete projected.textRuns; delete projected.assetId; delete projected.hyperlinks; delete projected.attachments;
  const first = node.contentItems[0]; projected.contentType = first?.type ?? 'text';
  if (first?.type === 'text') { projected.text = first.text; if (first.textRuns) projected.textRuns = first.textRuns; }
  else if (first?.type === 'asset') projected.assetId = first.assetId;
  else if (first?.type === 'link') projected.hyperlinks = [{ url: first.url, ...(first.label !== undefined ? { label: first.label } : {}) }];
  return projected;
}

/** A saved selection wins. Older mixed-content tiles prefer their primary attachment, then text, then a link. */
export function effectiveContentType(node: MeridianNode): NodeContentType {
  if (node.kind === 'block') return 'text';
  if (node.contentItems !== undefined) return node.contentItems[0]?.type ?? 'text';
  return node.contentType ?? (node.assetId || node.attachments?.length ? 'asset' : node.text ? 'text' : node.hyperlinks?.length ? 'link' : 'text');
}
export interface ActiveNodeContent { type: NodeContentType; text?: string; textRuns?: MeridianNode['textRuns']; assetId?: string; hyperlink?: NodeHyperlink }
/** Reading content never destroys an older title, caption, attachment or source list. */
export function activeNodeContent(node: MeridianNode): ActiveNodeContent {
  if (node.kind === 'block') return { type: 'text', text: '' };
  if (node.contentItems !== undefined) {
    const first = node.contentItems[0];
    if (!first) return { type: 'text', text: '' };
    return first.type === 'text' ? { type: 'text', text: first.text, ...(first.textRuns ? { textRuns: first.textRuns } : {}) } : first.type === 'asset' ? { type: 'asset', assetId: first.assetId } : { type: 'link', hyperlink: { url: first.url, ...(first.label !== undefined ? { label: first.label } : {}) } };
  }
  const type = effectiveContentType(node);
  return type === 'asset' ? { type, assetId: node.assetId ?? node.attachments?.[0] }
    : type === 'link' ? { type, hyperlink: node.hyperlinks?.[0] }
      : { type, text: node.text ?? '', ...(node.textRuns ? { textRuns: node.textRuns } : {}) };
}
/** Bounded parents replace their own content with the revealed child workspace. */
export function hidesOwnContent(node: MeridianNode): boolean {
  return node.kind === 'block' || !node.collapsed && ['container', 'spreadsheet', 'module'].includes(node.kind);
}
