import type { MeridianAsset, MeridianNode, MeridianProject, NodeContentItem } from './core/types.ts';
import { nodeContentItems } from './core/content.ts';
import { resolveReferenceTarget } from './core/cells.ts';
import { nodeTag } from './core/node-tag.ts';
import { documentPreviewKind } from './core/document-preview.ts';
import { createDocumentViewer, documentActionButton, type DocumentViewerState } from './document-viewer.ts';
import { renderRichText } from './rich-text-render.ts';

export interface HubContentMount { dispose: () => void }
const raster = /^image\/(png|jpeg|gif|webp|avif|bmp)$/i;
const video = /^video\/(mp4|webm|ogg)$/i;
const audio = /^audio\/(mpeg|mp3|ogg|wav|wave|x-wav|webm|aac|mp4)$/i;
const previewStates = new Map<string, { document?: DocumentViewerState; media?: { time: number; paused: boolean; volume: number; muted: boolean } }>();
function remember(key: string, state: { document?: DocumentViewerState; media?: { time: number; paused: boolean; volume: number; muted: boolean } }): void {
  previewStates.delete(key); previewStates.set(key, state);
  if (previewStates.size > 256) previewStates.delete(previewStates.keys().next().value!);
}
const make = <K extends keyof HTMLElementTagNameMap>(tag: K, className = '', text?: string): HTMLElementTagNameMap[K] => {
  const element = document.createElement(tag); element.className = className; if (text !== undefined) element.textContent = text; return element;
};

/** Saved Reference observations own their bytes even when a newer source asset is loaded. */
export function hubContentAsset(project: MeridianProject, node: MeridianNode, assetId: string): MeridianAsset | undefined {
  const target = node.kind === 'reference' && node.contentItems === undefined ? resolveReferenceTarget(project, node) : undefined;
  if (target?.saved && target.snapshot) {
    const snapshot = target.snapshot;
    const pinned = [snapshot.asset, snapshot.backgroundAsset, ...snapshot.assets ?? []].find(asset => asset?.id === assetId);
    if (pinned) return pinned;
  }
  return project.assets.find(asset => asset.id === assetId) ?? Object.values(project.snapshots)
    .flatMap(snapshot => [snapshot.asset, snapshot.backgroundAsset, ...snapshot.assets ?? []]).find(asset => asset?.id === assetId);
}

export function hubItemLabel(project: MeridianProject, node: MeridianNode, item: NodeContentItem): string {
  return item.type === 'text' ? 'Text' : item.type === 'link' ? item.label || item.url : hubContentAsset(project, node, item.assetId)?.name || 'Attachment unavailable';
}

/** An inert, bounded reader for one Hub item. The owner disposes it before replacement. */
export function mountHubItemPreview(host: HTMLElement, project: MeridianProject, node: MeridianNode, item: NodeContentItem): HubContentMount {
  const key = `${project.id}/${node.id}/${item.id}/${item.type === 'asset' ? item.assetId : item.type}`;
  const state = previewStates.get(key);
  host.classList.add('nh-item-preview');
  Object.assign(host.style, { minWidth: '0', maxHeight: '280px', overflow: 'auto', overflowWrap: 'anywhere' });
  let cleanup = () => {};
  if (item.type === 'text') {
    const text = make('div', 'nh-text-preview'); text.style.whiteSpace = 'pre-wrap'; renderRichText(text, item.text, item.textRuns); host.append(text);
  } else if (item.type === 'link') {
    const link = make('a', 'nh-link-preview', item.label || item.url); link.href = item.url; link.target = '_blank'; link.rel = 'noopener noreferrer'; host.append(link);
  } else {
    const asset = hubContentAsset(project, node, item.assetId);
    if (!asset) host.append(make('p', 'nh-missing', 'Attachment unavailable.'));
    else if (raster.test(asset.mime)) {
      const image = make('img', 'nh-image-preview'); image.src = `data:${asset.mime};base64,${asset.base64}`; image.alt = asset.name; image.draggable = false;
      Object.assign(image.style, { display: 'block', width: '100%', maxHeight: '260px', objectFit: 'contain' }); host.append(image);
    } else if (video.test(asset.mime) || audio.test(asset.mime)) {
      const media = make(video.test(asset.mime) ? 'video' : 'audio', 'nh-media-preview'); media.controls = true; media.preload = 'metadata';
      media.src = `data:${asset.mime};base64,${asset.base64}`; media.setAttribute('aria-label', asset.name);
      Object.assign(media.style, { display: 'block', width: '100%', maxHeight: '260px' });
      const restore = () => {
        if (!state?.media) return;
        try { media.currentTime = Math.max(0, Math.min(Number.isFinite(media.duration) ? media.duration : Infinity, state.media.time)); } catch { /* Unsupported media remains available as original bytes. */ }
        media.volume = state.media.volume; media.muted = state.media.muted;
        if (!state.media.paused) void media.play().catch(() => {});
      };
      media.addEventListener('loadedmetadata', restore, { once: true }); host.append(media);
      cleanup = () => {
        remember(key, { media: { time: Number.isFinite(media.currentTime) ? media.currentTime : 0, paused: media.paused, volume: media.volume, muted: media.muted } });
        media.removeEventListener('loadedmetadata', restore); media.pause(); media.removeAttribute('src'); media.load();
      };
    } else if (documentPreviewKind(asset)) {
      const reader = createDocumentViewer(asset, { state: state?.document });
      Object.assign(reader.element.style, { width: '100%', height: '260px', minHeight: '0', fontSize: '12px' }); host.append(reader.element);
      cleanup = () => { remember(key, { document: reader.captureState() }); reader.dispose(); };
    } else {
      host.append(make('p', 'nh-file-preview', asset.name));
      try {
        const bytes = Uint8Array.from(atob(asset.base64), value => value.charCodeAt(0));
        const url = URL.createObjectURL(new Blob([bytes], { type: 'application/octet-stream' }));
        const download = make('a', 'nh-download', 'Download original'); download.href = url; download.download = asset.name; host.append(download);
        cleanup = () => URL.revokeObjectURL(url);
      } catch { host.append(make('p', 'nh-missing', 'The attachment could not be decoded.')); }
    }
  }
  let disposed = false;
  return { dispose: () => { if (!disposed) { disposed = true; cleanup(); } } };
}

/** Larger view uses the appropriate same-page trusted viewer and never changes the Work. */
export function openHubItem(project: MeridianProject, node: MeridianNode, item: NodeContentItem): void {
  const dialog = make('dialog', 'ms-asset-dialog nh-popout'); dialog.setAttribute('aria-label', `${nodeTag(node)}: ${hubItemLabel(project, node, item)}`);
  Object.assign(dialog.style, { width: 'min(1120px,94vw)', maxWidth: '94vw', maxHeight: '92dvh', boxSizing: 'border-box' });
  const heading = make('header', 'ms-dialog-header'); heading.append(make('strong', '', hubItemLabel(project, node, item)));
  const close = make('button', '', '×'); close.type = 'button'; close.title = 'Close content viewer'; close.setAttribute('aria-label', 'Close content viewer'); close.onclick = () => dialog.close(); heading.append(close);
  const body = make('div', 'nh-popout-body'); const mounted = mountHubItemPreview(body, project, node, item);
  Object.assign(body.style, { maxHeight: '78dvh', minHeight: '120px' });
  const reader = body.querySelector<HTMLElement>('.mdv-viewer'); if (reader) reader.style.height = 'min(680px,74dvh)';
  const media = body.querySelector<HTMLElement>('img,video'); if (media) media.style.maxHeight = '74dvh';
  dialog.append(heading, body); dialog.addEventListener('close', () => { mounted.dispose(); dialog.remove(); }, { once: true }); document.body.append(dialog); dialog.showModal();
}

/** Published readers expose every item; only the first item belongs to the scene tile. */
export function appendReadonlyNodeHub(host: HTMLElement, project: MeridianProject, node: MeridianNode): HubContentMount {
  const target = node.kind === 'reference' && node.contentItems === undefined ? resolveReferenceTarget(project, node)?.node : undefined;
  const items = nodeContentItems(target ?? node), disposers: Array<() => void> = [];
  const stack = make('section', 'nh-stack'); stack.setAttribute('aria-label', `Node Hub: ${nodeTag(node)}`);
  Object.assign(stack.style, { display: 'grid', gap: '12px', minWidth: '0' });
  const pending = new Map<HTMLElement, NodeContentItem>();
  const mount = (host: HTMLElement, item: NodeContentItem) => { const reader = mountHubItemPreview(host, project, node, item); disposers.push(reader.dispose); };
  // One observer per Hub avoids parsing/loading every offscreen reader in a
  // long content stack. Individual preview mounts remain immediately usable.
  const observer = typeof IntersectionObserver !== 'undefined' ? new IntersectionObserver(entries => {
    for (const entry of entries) if (entry.isIntersecting) {
      const preview = entry.target as HTMLElement, item = pending.get(preview); if (!item) continue;
      pending.delete(preview); observer?.unobserve(preview); mount(preview, item);
    }
  }, { rootMargin: '160px' }) : undefined;
  for (const [index, item] of items.entries()) {
    const section = make('section', 'nh-content-section'); section.dataset.contentItemId = item.id;
    Object.assign(section.style, { border: '1px solid #344b5a', padding: '10px', borderRadius: '6px', minWidth: '0' });
    const header = make('div', 'nh-content-heading'); Object.assign(header.style, { display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '8px', minWidth: '0' });
    const label = make('strong', '', hubItemLabel(project, node, item)); label.title = label.textContent ?? '';
    Object.assign(label.style, { flex: '1 1 0', minWidth: '0', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' });
    header.append(label); if (!index) { const badge = make('small', 'nh-display-slot', 'Display'); Object.assign(badge.style, { flex: '0 0 auto', whiteSpace: 'nowrap' }); header.append(badge); }
    const open = documentActionButton('reading-view', `Open ${hubItemLabel(project, node, item)} in a larger view`, () => openHubItem(project, node, item));
    Object.assign(open.style, { flex: '0 0 30px', width: '30px', minWidth: '30px', maxWidth: '30px', height: '30px', margin: '0', padding: '5px', boxSizing: 'border-box', display: 'inline-flex', alignItems: 'center', justifyContent: 'center', textAlign: 'center' }); header.append(open);
    const preview = make('div'); preview.style.minHeight = item.type === 'asset' ? '100px' : '24px';
    if (observer) { pending.set(preview, item); observer.observe(preview); } else mount(preview, item);
    section.append(header, preview); stack.append(section);
  }
  if (items.length) host.append(stack);
  return { dispose: () => { observer?.disconnect(); pending.clear(); for (const dispose of disposers) dispose(); } };
}
