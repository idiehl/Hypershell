import type { MeridianNode, MeridianProject } from './types.ts';

/** A gallery keeps ordinary child identities while presenting one object inside its own tile. */
export function isGallery(node: MeridianNode | undefined): boolean {
  return !!node && node.kind !== 'canvas' && node.containerVariant === 'gallery';
}
/** Only bounded galleries replace their parent tile with the current object's content. */
export function isEmbeddedGallery(node: MeridianNode | undefined): boolean {
  return isGallery(node) && !!node && ['container', 'spreadsheet', 'module'].includes(node.kind);
}

/** Stored node order is the collection order. Nested contents remain children of their own item. */
export function galleryItems(project: MeridianProject, parent: MeridianNode | string): MeridianNode[] {
  const gallery = typeof parent === 'string' ? project.nodes.find(node => node.id === parent) : parent;
  if (!isGallery(gallery)) return [];
  return project.nodes.filter(node => node.parentId === gallery!.id && node.canvasId === gallery!.canvasId && node.kind !== 'canvas');
}

export interface GallerySelection { items: MeridianNode[]; index: number; item?: MeridianNode }

/** Selection belongs to the window/viewer, so browsing never edits or automatically saves a document. */
export function gallerySelection(project: MeridianProject, parent: MeridianNode | string, activeId?: string): GallerySelection {
  const items = galleryItems(project, parent);
  const requested = activeId ? items.findIndex(node => node.id === activeId) : -1;
  const index = items.length ? Math.max(0, requested) : -1;
  return { items, index, ...(index >= 0 ? { item: items[index] } : {}) };
}

/** Previous/Next stop at the ends; removing the current object safely falls back to the first item. */
export function galleryStep(project: MeridianProject, parent: MeridianNode | string, activeId: string | undefined, direction: -1 | 1): string | undefined {
  const selection = gallerySelection(project, parent, activeId);
  if (!selection.items.length) return undefined;
  return selection.items[Math.max(0, Math.min(selection.items.length - 1, selection.index + direction))]!.id;
}

/** The nearest containing gallery owns a descendant's canvas display, even when it is expanded. */
export function galleryOwner(project: MeridianProject, nodeId: string): MeridianNode | undefined {
  const nodes = new Map(project.nodes.map(node => [node.id, node])), seen = new Set<string>([nodeId]);
  let parentId = nodes.get(nodeId)?.parentId;
  while (parentId && !seen.has(parentId)) {
    seen.add(parentId);
    const parent = nodes.get(parentId);
    if (!parent) return undefined;
    if (isEmbeddedGallery(parent)) return parent;
    parentId = parent.parentId;
  }
  return undefined;
}
