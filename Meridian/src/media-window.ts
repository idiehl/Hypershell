import type { MeridianAsset } from './core/types.ts';

const RASTER = /^image\/(png|jpeg|gif|webp|avif|bmp)$/i;
const CHANNEL = /^[a-zA-Z0-9-]{16,80}$/;
const MESSAGE = 'atlas-meridian-media-v1';
interface MediaPayload { asset: MeridianAsset; title: string }
const isRecord = (value: unknown): value is Record<string, unknown> => !!value && typeof value === 'object' && !Array.isArray(value);

function isPayload(value: unknown): value is MediaPayload {
  if (!isRecord(value) || !isRecord(value.asset)) return false;
  const asset = value.asset;
  return typeof value.title === 'string' && typeof asset.id === 'string' && typeof asset.name === 'string' &&
    typeof asset.mime === 'string' && RASTER.test(asset.mime) && typeof asset.base64 === 'string' &&
    asset.base64.length > 0 && asset.base64.length % 4 === 0 && /^[A-Za-z0-9+/]*={0,2}$/.test(asset.base64);
}

/** Reserves a trusted app window inside the gesture, then transfers this exact draft asset. */
export function openMediaWindow(asset: MeridianAsset, title = asset.name): void {
  const payload: MediaPayload = { asset: { ...asset }, title };
  if (!isPayload(payload)) throw new Error('This image attachment cannot be opened.');
  const channel = crypto.randomUUID();
  const publication = !!document.getElementById('meridian-data');
  const embeddedOrigin = new URL(window.location.href).origin;
  const loopbackBlob = window.location.protocol === 'blob:' && /^https?:/.test(embeddedOrigin) &&
    /^(localhost|127(?:\.\d{1,3}){3}|\[::1\])$/i.test(new URL(embeddedOrigin).hostname);
  // Reopening the publication itself keeps file:// and blob: previews self-contained.
  // The editor uses a real loopback URL so MyNet can create its managed child WebView.
  const reopenPublication = publication && !loopbackBlob;
  const destination = reopenPublication ? new URL(window.location.href) : new URL('/', window.location.origin);
  if (reopenPublication) destination.hash = `meridian-media=${channel}`;
  else {
    destination.searchParams.set('window', 'media'); destination.searchParams.set('mediaChannel', channel);
    if (window.origin === 'null') destination.searchParams.set('mediaOpaqueOpener', '1');
  }
  // Sandboxed publication previews have an opaque opener origin. Their child may
  // inherit that sandbox, or a managed WebView may host the trusted app origin.
  const childOrigins = new Set([destination.origin]);
  if (window.origin === 'null') childOrigins.add('null');
  let childOrigin: string | undefined;
  let child: Window | null = null;
  let timeout: ReturnType<typeof setTimeout> | undefined, closed: ReturnType<typeof setInterval> | undefined;
  const cleanup = () => { window.removeEventListener('message', receive); clearTimeout(timeout); clearInterval(closed); };
  const receive = (event: MessageEvent) => {
    if (event.source !== child || !childOrigins.has(event.origin) || childOrigin !== undefined && event.origin !== childOrigin || !isRecord(event.data) || event.data.format !== MESSAGE || event.data.channel !== channel) return;
    if (event.data.kind === 'ready') {
      childOrigin = event.origin;
      child?.postMessage({ format: MESSAGE, channel, kind: 'image', payload }, childOrigin === 'null' ? '*' : childOrigin);
    }
    else if (event.data.kind === 'received') cleanup();
  };
  window.addEventListener('message', receive);
  try { child = window.open(destination.href, '_blank', 'popup,width=1080,height=800'); }
  catch (error) { cleanup(); throw error; }
  if (!child) { cleanup(); throw new Error('Allow Meridian to open windows, then Ctrl-click the image again.'); }
  timeout = setTimeout(cleanup, 45000);
  closed = setInterval(() => { if (child?.closed) cleanup(); }, 500);
}

const CSS = `
html:has(body.meridian-media-window){height:100%;background:#10151b}
body.meridian-media-window{margin:0!important;height:100dvh;overflow:hidden;background:#10151b;color:#e2eaf2;font:14px/1.45 Inter,Segoe UI,system-ui,sans-serif}
.media-window{height:100%;display:flex;flex-direction:column;min-height:0}
.media-window-header{display:flex;align-items:center;gap:18px;flex:none;padding:14px 20px;border-bottom:1px solid #2a3947;background:#141e28;flex-wrap:wrap}
.media-window-heading{flex:1;min-width:160px}.media-window-heading h1{font-family:inherit;font-size:16px;font-weight:600;line-height:1.4;margin:0;overflow-wrap:anywhere}.media-window-heading p{font-size:12px;color:#9fb0bf;margin:3px 0 0;overflow-wrap:anywhere}
.media-window-controls{display:flex;gap:8px;align-items:center}.media-window button,.media-window a{font:inherit;text-decoration:none;color:#dce9f1;background:#203141;border:1px solid #405366;border-radius:6px;padding:7px 12px;cursor:pointer}
.media-window button:hover,.media-window a:hover{background:#2b4459}.media-window button:focus-visible,.media-window a:focus-visible{outline:2px solid #79dcc3;outline-offset:3px}
.media-window button:disabled{opacity:.5;cursor:default}.media-window a[hidden]{display:none}
.media-window-stage{flex:1;min-height:0;min-width:0;overflow:auto;display:grid;place-items:center;padding:18px;box-sizing:border-box;background:radial-gradient(ellipse at center,#1d2a35,#10151b)}
.media-window-stage img{display:block;max-width:100%;max-height:100%;width:100%;height:100%;object-fit:contain;min-height:0;min-width:0}
.media-window-stage.is-original{display:block}.media-window-stage.is-original img{width:auto;height:auto;max-width:none;max-height:none;margin:auto}
.media-window-status{padding:9px 20px;color:#9eb2c3;font-size:12px;border-top:1px solid #263542;min-height:18px;flex:none}
@media(max-width:580px){.media-window-header{padding:12px;gap:10px}.media-window-stage{padding:8px}.media-window-controls{flex-wrap:wrap}.media-window-status{padding:8px 12px}}
`;

/** Call before editor/viewer boot. Returns true when this document is a media child. */
export function bootMediaWindow(): boolean {
  const url = new URL(window.location.href);
  const hashChannel = /^#meridian-media=([a-zA-Z0-9-]+)$/.exec(url.hash)?.[1];
  if (url.searchParams.get('window') !== 'media' && !hashChannel) return false;
  const channel = hashChannel ?? url.searchParams.get('mediaChannel') ?? '';
  const opener = window.opener as Window | null;
  const openerOrigin = url.searchParams.get('mediaOpaqueOpener') === '1' ? 'null' : window.origin;
  const targetOrigin = openerOrigin === 'null' ? '*' : openerOrigin;
  const style = document.createElement('style'); style.textContent = CSS; document.head.append(style);
  document.body.className = 'meridian-media-window'; document.title = 'Image · Atlas Meridian';
  const main = document.createElement('main'); main.className = 'media-window'; main.setAttribute('aria-label', 'Image viewer');
  const header = document.createElement('header'); header.className = 'media-window-header';
  const heading = document.createElement('div'); heading.className = 'media-window-heading';
  const title = document.createElement('h1'); title.textContent = 'Opening image…';
  const name = document.createElement('p'); heading.append(title, name);
  const controls = document.createElement('div'); controls.className = 'media-window-controls';
  const fit = document.createElement('button'); fit.type = 'button'; fit.textContent = 'Actual size'; fit.disabled = true; fit.setAttribute('aria-label', 'Show image at actual size');
  const download = document.createElement('a'); download.textContent = 'Download original'; download.hidden = true;
  controls.append(fit, download); header.append(heading, controls);
  const stage = document.createElement('div'); stage.className = 'media-window-stage';
  const status = document.createElement('div'); status.className = 'media-window-status'; status.setAttribute('role', 'status'); status.textContent = 'Waiting for the image from your Work…';
  main.append(header, stage, status); document.body.replaceChildren(main);
  if (!opener || !CHANNEL.test(channel)) { title.textContent = 'Image unavailable'; status.textContent = 'Open this image again with Ctrl-click from its Work.'; return true; }
  let imageUrl: string | undefined, ready: ReturnType<typeof setInterval> | undefined, timeout: ReturnType<typeof setTimeout> | undefined;
  const stop = () => { window.removeEventListener('message', receive); clearInterval(ready); clearTimeout(timeout); };
  const receive = (event: MessageEvent) => {
    if (event.source !== opener || event.origin !== openerOrigin || !isRecord(event.data) || event.data.format !== MESSAGE || event.data.channel !== channel || event.data.kind !== 'image' || !isPayload(event.data.payload)) return;
    const payload = event.data.payload;
    try {
      const bytes = Uint8Array.from(atob(payload.asset.base64), character => character.charCodeAt(0));
      imageUrl = URL.createObjectURL(new Blob([bytes], { type: payload.asset.mime }));
      const image = document.createElement('img'); image.alt = payload.asset.name; image.draggable = false;
      image.addEventListener('load', () => { status.textContent = `${image.naturalWidth} × ${image.naturalHeight} pixels · ${new Intl.NumberFormat().format(bytes.byteLength)} bytes`; fit.disabled = false; });
      image.addEventListener('error', () => { status.textContent = 'This browser could not display the image. Its original file is available to download.'; });
      image.src = imageUrl; stage.replaceChildren(image);
      title.textContent = payload.title || payload.asset.name; name.textContent = payload.asset.name;
      document.title = `${payload.asset.name} · Atlas Meridian`;
      download.href = imageUrl; download.download = payload.asset.name.replace(/[\\/:*?"<>|]/g, '_') || 'image'; download.hidden = false;
      fit.onclick = () => { const original = stage.classList.toggle('is-original'); fit.textContent = original ? 'Fit image' : 'Actual size'; fit.setAttribute('aria-label', original ? 'Fit image to window' : 'Show image at actual size'); };
      opener.postMessage({ format: MESSAGE, channel, kind: 'received' }, targetOrigin);
      stop();
      // The transfer is complete; the media window no longer needs opener privileges.
      window.opener = null;
    } catch { status.textContent = 'The image could not be decoded. Open it again from its Work.'; stop(); }
  };
  window.addEventListener('message', receive);
  const announce = () => {
    if (opener.closed) { stop(); status.textContent = 'The Work closed before sending the image. Open the image from its Work again.'; return; }
    opener.postMessage({ format: MESSAGE, channel, kind: 'ready' }, targetOrigin);
  };
  ready = setInterval(announce, 250); timeout = setTimeout(() => { stop(); status.textContent = 'The Work did not send the image. Open the image from its Work again.'; }, 45000);
  window.addEventListener('pagehide', () => { stop(); if (imageUrl) URL.revokeObjectURL(imageUrl); }, { once: true });
  announce(); return true;
}
