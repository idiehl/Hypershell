import type { MeridianAsset } from './core/types.ts';
import { createDocumentViewer, documentActionButton, type DocumentViewerState } from './document-viewer.ts';

const readers = new Map<HTMLElement, Set<() => void>>();
const readingStates = new Map<string, DocumentViewerState>();
const activeReaders = new Map<string, () => DocumentViewerState>();
export function disposeDocumentReaders(host: HTMLElement): void {
  for (const [owner, cleanups] of readers) if (owner === host || !owner.isConnected) {
    for (const cleanup of cleanups) cleanup(); readers.delete(owner);
  }
}
export function inlineDocumentReader(host: HTMLElement, asset: MeridianAsset, close: () => void, readingView?: () => void, key = asset.id): HTMLElement {
  const stage = document.createElement('div'); stage.className = 'ms-document-stage';
  const frame = document.createElement('div'); frame.className = 'ms-document-reading-frame';
  const state = activeReaders.get(key)?.() ?? readingStates.get(key);
  const reader = createDocumentViewer(asset, { onClose: close, state });
  activeReaders.set(key, reader.captureState);
  if (readingView) {
    const open = documentActionButton('reading-view', 'Open larger document reading view', readingView);
    reader.element.querySelector('.mdv-toolbar')?.append(open);
  }
  frame.append(reader.element); stage.append(frame);
  const owned = readers.get(host) ?? new Set();
  owned.add(() => {
    if (activeReaders.get(key) === reader.captureState) {
      readingStates.delete(key); readingStates.set(key, reader.captureState()); activeReaders.delete(key);
      if (readingStates.size > 256) readingStates.delete(readingStates.keys().next().value!);
    }
    reader.dispose();
  }); readers.set(host, owned);
  return stage;
}
/** Reading controls follow the frame's scale, rather than the attachment's length. */
export function fitInlineDocument(card: HTMLElement, bounds: { width: number; height: number }): boolean {
  const stage = card.querySelector<HTMLElement>(':scope > .ms-node-body > .ms-document-stage');
  if (!stage) return false;
  const body = stage.parentElement!;
  const unit = Math.max(Number.MIN_VALUE, bounds.width / 640);
  const header = card.querySelector<HTMLElement>(':scope > .ms-node-header');
  if (header) {
    Object.assign(header.style, { boxSizing: 'border-box', height: `${32 * unit}px`, flex: `0 0 ${32 * unit}px`, minHeight: '0', maxHeight: 'none', padding: `${4 * unit}px ${6 * unit}px`, overflow: 'hidden' });
    for (const text of header.querySelectorAll<HTMLElement>('.ms-node-title,.ms-node-title *')) text.style.fontSize = `${18 * unit}px`;
    const title = header.querySelector<HTMLElement>('.ms-node-title'); if (title) Object.assign(title.style, { whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis', lineHeight: '1.25' });
  }
  Object.assign(body.style, { boxSizing: 'border-box', width: '100%', flex: '1 1 0', minWidth: '0', minHeight: '0', padding: '0', overflow: 'hidden', position: 'relative' });
  // clientWidth/clientHeight round sub-pixel world frames to zero. Observe the
  // transformed frame, then remove the scene camera scale before fitting UI.
  const world = card.closest<HTMLElement>('.ms-world');
  const declaredCamera = world ? Number(getComputedStyle(world).getPropertyValue('--ms-camera-scale')) : 0;
  const camera = declaredCamera > 0 ? declaredCamera : card.getBoundingClientRect().width / (Number.parseFloat(getComputedStyle(card).width) || bounds.width);
  const logical = (surface: HTMLElement) => {
    const box = surface.getBoundingClientRect();
    return { width: camera > 0 ? box.width / camera : 0, height: camera > 0 ? box.height / camera : 0 };
  };
  body.style.padding = `${6 * unit}px`; body.style.gap = '0';
  const { width, height } = logical(stage), scale = width / 640;
  if (scale > 0 && height > 0) {
    const frame = stage.firstElementChild as HTMLElement;
    Object.assign(frame.style, { width: '640px', height: `${height / scale}px`, transform: `scale(${scale})` });
  }
  card.dataset.contentFit = 'document'; return true;
}
