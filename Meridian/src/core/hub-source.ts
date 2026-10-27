import type { MeridianAsset, MeridianNode, MeridianProject, NodeContentItem } from './types.ts';
import { resolveReferenceTarget } from './cells.ts';
import { isNodeContentItems, nodeContentItems, withNodeContentItems } from './content.ts';

export interface HubView { node: MeridianNode; project: MeridianProject; inherited: boolean }
const equalAsset = (a: MeridianAsset, b: MeridianAsset): boolean => a.name === b.name && a.mime === b.mime && a.base64 === b.base64;

/** A Reference previews its current source or exact saved observation until a
 * local Hub stack is explicitly authored. Its own title and identity stay local.
 * The preview asset overlay never rewrites the loaded project or source bytes. */
export function resolveHubView(project: MeridianProject, node: MeridianNode): HubView {
  if (node.kind !== 'reference' || node.contentItems !== undefined) return { node, project, inherited: false };
  const target = resolveReferenceTarget(project, node);
  if (!target) return { node, project, inherited: false };
  const snapshot = target.saved ? target.snapshot : undefined;
  const pinnedById = new Map<string, MeridianAsset>();
  for (const asset of [snapshot?.asset, snapshot?.backgroundAsset, ...snapshot?.assets ?? []]) if (asset && !pinnedById.has(asset.id)) pinnedById.set(asset.id, asset);
  const pinned = [...pinnedById.values()];
  const pinnedIds = new Set(pinned.map(asset => asset.id));
  const viewProject = pinned.length ? { ...project, assets: [...pinned, ...project.assets.filter(asset => !pinnedIds.has(asset.id))] } : project;
  return { node: { ...node, contentItems: nodeContentItems(target.node) }, project: viewProject, inherited: true };
}

/** Commit a local visual/content override, including only its required media.
 * This function is shallow-immutable so the editor's existing transaction clone
 * remains the only full-project clone. Original source nodes, reference targets,
 * executable fields, observations and existing asset bytes are never modified. */
export function applyHubContent(project: MeridianProject, nodeId: string, items: readonly NodeContentItem[], newAssets: readonly MeridianAsset[] = [], view?: HubView): MeridianProject {
  const node = project.nodes.find(node => node.id === nodeId);
  if (!node) throw new Error('Choose an existing node before changing its Hub content.');
  if (!isNodeContentItems(items)) throw new Error('Node Hub content must use uniquely identified inert text, assets or hyperlinks.');
  const context = view ?? resolveHubView(project, node);
  if (context.node.id !== nodeId || context.project.id !== project.id) throw new Error('The Hub preview belongs to another node or Work.');
  const supplied = new Map<string, MeridianAsset>();
  for (const asset of newAssets) {
    if (!asset || !/^[A-Za-z0-9][A-Za-z0-9_-]{0,95}$/.test(asset.id) || typeof asset.name !== 'string' || !asset.name || asset.name.length > 512 || typeof asset.mime !== 'string' || !asset.mime.includes('/') || asset.mime.length > 128 || /[\u0000-\u001f\u007f]/.test(asset.mime) || typeof asset.base64 !== 'string')
      throw new Error('Hub attachments require valid inert asset metadata.');
    const previous = supplied.get(asset.id);
    if (previous && !equalAsset(previous, asset)) throw new Error('Two uploaded Hub attachments cannot claim one identity with different content.');
    supplied.set(asset.id, asset);
  }
  const previewAssets = new Map<string, MeridianAsset>();
  for (const asset of context.project.assets) if (!previewAssets.has(asset.id)) previewAssets.set(asset.id, asset);
  const assets = [...project.assets], localAssets = new Map(assets.map(asset => [asset.id, asset]));
  const occupied = new Set([project.id, ...project.canvases.map(item => item.id), ...project.nodes.map(item => item.id), ...project.links.map(item => item.id), ...assets.map(item => item.id), ...Object.keys(project.snapshots), ...project.nodes.flatMap(item => item.remoteLinks?.map(link => link.id) ?? [])]);
  const remapped = new Map<string, string>();
  const materialize = (assetId: string): string => {
    const known = remapped.get(assetId); if (known) return known;
    const source = supplied.get(assetId) ?? previewAssets.get(assetId), local = localAssets.get(assetId);
    // Existing missing-media declarations remain repairable. Never invent bytes.
    if (!source) return assetId;
    if (local && equalAsset(local, source)) { remapped.set(assetId, local.id); return local.id; }
    const duplicate = assets.find(asset => equalAsset(asset, source));
    if (duplicate) { remapped.set(assetId, duplicate.id); return duplicate.id; }
    let id = assetId;
    if (occupied.has(id)) do { id = crypto.randomUUID(); } while (occupied.has(id));
    const copy: MeridianAsset = { id, name: source.name, mime: source.mime, base64: source.base64 };
    assets.push(copy); localAssets.set(id, copy); occupied.add(id); remapped.set(assetId, id); return id;
  };
  const localItems = items.map(item => item.type === 'asset' ? { ...item, assetId: materialize(item.assetId) } : structuredClone(item));
  const edited = withNodeContentItems(node, localItems);
  return { ...project, nodes: project.nodes.map(item => item.id === nodeId ? edited : item), assets };
}
