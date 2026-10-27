import type { ContainerSize, ContainerSizes, MeridianNode } from './types.ts';

const record = (value: unknown): value is Record<string, unknown> => !!value && typeof value === 'object' && !Array.isArray(value);
export function isContainerSize(value: unknown): value is ContainerSize {
  return record(value) && Object.keys(value).every(key => ['width', 'height', 'sizeMode'].includes(key))
    && ['width', 'height'].every(key => typeof value[key] === 'number' && Number.isFinite(value[key]) && (value[key] as number) > 0 && (value[key] as number) <= 1e9)
    && ['auto', 'fixed'].includes(value.sizeMode as string);
}
export function isContainerSizes(value: unknown): value is ContainerSizes {
  return record(value) && Object.keys(value).every(key => ['expanded', 'collapsed'].includes(key))
    && (value.expanded === undefined || isContainerSize(value.expanded)) && (value.collapsed === undefined || isContainerSize(value.collapsed));
}
export function isSizeRememberingContainer(node: Pick<MeridianNode, 'kind'>): boolean {
  return ['container', 'spreadsheet', 'module'].includes(node.kind);
}
function activeSize(node: MeridianNode): ContainerSize {
  return { width: node.width, height: node.height, sizeMode: node.sizeMode ?? 'auto' };
}

/** Mutates only an editing candidate. Reading/rendering a document never creates mode records. */
export function rememberContainerSize(node: MeridianNode, geometry?: ContainerSize, options: { preserveExisting?: boolean } = {}): void {
  if (!isSizeRememberingContainer(node)) return;
  const mode = node.collapsed ? 'collapsed' : 'expanded';
  // Parent allocation is presentation geometry. It may seed a new mode, but
  // must not replace the frame the user authored for that mode.
  if (options.preserveExisting && node.containerSizes?.[mode]) return;
  const size = geometry ?? activeSize(node);
  if (!isContainerSize(size)) throw new Error('Container frame dimensions must be positive finite numbers and use automatic or fixed sizing.');
  node.containerSizes = { ...node.containerSizes, [mode]: { ...size } };
}

/** Capture the mode being left and restore the destination frame atomically in a cloned candidate.
 * A first transition may seed its other frame; later transitions always honor the stored dimensions. */
export function transitionContainerSize(node: MeridianNode, expanded: boolean, seed?: ContainerSize, options: { preserveCurrentRecord?: boolean } = {}): void {
  if (!isSizeRememberingContainer(node)) { node.collapsed = !expanded; return; }
  if ((node.collapsed !== true) === expanded) return;
  if (node.containerSizes && !isContainerSizes(node.containerSizes)) throw new Error('Container frame records are malformed.');
  if (seed && !isContainerSize(seed)) throw new Error('Container frame dimensions must be positive finite numbers and use automatic or fixed sizing.');
  rememberContainerSize(node, undefined, { preserveExisting: options.preserveCurrentRecord });
  const next = node.containerSizes?.[expanded ? 'expanded' : 'collapsed'] ?? seed ?? activeSize(node);
  node.collapsed = !expanded; node.width = next.width; node.height = next.height; node.sizeMode = next.sizeMode;
  rememberContainerSize(node);
}
