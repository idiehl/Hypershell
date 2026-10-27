/** A website is separate from local Work navigation and original file downloads. */
export function websiteDestination(href: string, pageUrl: string): string | null {
  if (!href.trim() || href.trimStart().startsWith('#')) return null;
  try {
    const url = new URL(href, pageUrl), page = new URL(pageUrl);
    if (!['http:', 'https:'].includes(url.protocol) || !url.hostname || url.username || url.password) return null;
    // Work windows, history and local application actions retain their own route.
    if (url.origin === page.origin) return null;
    return url.href;
  } catch { return null; }
}

const features = 'popup,width=1120,height=820';
type WebsiteWindow = Window & { __meridianWebsiteBrowser?: boolean; __meridianNativeChrome?: boolean };

/** Reserve inside the click gesture, detach the opener, then navigate the child.
 * A new native host handles remote window requests using an independent WebView.
 * Browsers open a normal popup so sites that prohibit framing remain usable. */
export function openWebsite(url: string): boolean {
  let child: Window | null = null;
  try {
    const parsed = new URL(url);
    if (!['http:', 'https:'].includes(parsed.protocol) || !parsed.hostname || parsed.username || parsed.password) return false;
    const native = window as WebsiteWindow;
    if (native.__meridianWebsiteBrowser === true || native.__meridianNativeChrome === true) {
      // Older loaded desktop hosts retain their direct external-link handler
      // until reopened. Sending about:blank there could replace the Work.
      window.open(parsed.href, '_blank', `${features},noopener,noreferrer`);
      return true;
    }
    // noopener/noreferrer features make window.open return null even on success.
    // Reserve a blank same-origin child instead so a null handle means blocked.
    child = window.open('about:blank', '_blank', features);
    if (!child) return false;
    child.opener = null;
    const referrer = child.document.createElement('meta');
    referrer.name = 'referrer'; referrer.content = 'no-referrer'; child.document.head.append(referrer);
    child.location.replace(parsed.href);
    return true;
  } catch {
    try { child?.close(); } catch { /* The independently closed child needs no cleanup. */ }
    return false;
  }
}

function showBlockedWebsite(url: string): void {
  const existing = document.querySelector<HTMLDialogElement>('dialog[data-website-blocked]');
  if (existing) existing.close();
  const previousFocus = document.activeElement;
  const dialog = document.createElement('dialog'); dialog.dataset.websiteBlocked = 'true';
  dialog.className = 'ms-asset-dialog'; dialog.setAttribute('aria-label', 'Open website');
  Object.assign(dialog.style, { width: 'min(520px,90vw)', boxSizing: 'border-box', padding: '20px' });
  const heading = document.createElement('h2'); heading.textContent = 'Open website'; heading.style.marginTop = '0';
  const message = document.createElement('p'); message.setAttribute('role', 'status');
  message.textContent = 'Your browser blocked the website window. Allow popups for Meridian, then try again.';
  const address = document.createElement('input'); address.type = 'url'; address.readOnly = true; address.value = url;
  address.setAttribute('aria-label', 'Website address'); Object.assign(address.style, { width: '100%', boxSizing: 'border-box' });
  const controls = document.createElement('div'); Object.assign(controls.style, { display: 'flex', justifyContent: 'flex-end', gap: '8px', marginTop: '16px' });
  const close = document.createElement('button'); close.type = 'button'; close.textContent = 'Close'; close.onclick = () => dialog.close();
  const retry = document.createElement('button'); retry.type = 'button'; retry.textContent = 'Open website';
  retry.onclick = () => { if (openWebsite(url)) dialog.close(); else { message.textContent = 'The website window is still blocked. Allow popups for Meridian or copy the address to your browser.'; address.focus(); address.select(); } };
  controls.append(close, retry); dialog.append(heading, message, address, controls); document.body.append(dialog);
  dialog.addEventListener('close', () => { dialog.remove(); if (previousFocus instanceof HTMLElement && previousFocus.isConnected) previousFocus.focus({ preventScroll: true }); }, { once: true });
  dialog.showModal();
}

const boundDocuments = new WeakSet<Document>();
/** One capture listener covers scene, Gallery, Hub and nested document readers,
 * including content mounted later. It does no graph work or background fetches. */
export function bindWebsiteLinks(): void {
  if (boundDocuments.has(document)) return;
  boundDocuments.add(document);
  const activate = (event: MouseEvent) => {
    if (event.defaultPrevented || event.type === 'click' && event.button !== 0 || event.type === 'auxclick' && event.button !== 1) return;
    const anchor = event.composedPath().find(target => target instanceof HTMLAnchorElement);
    if (!(anchor instanceof HTMLAnchorElement) || anchor.hasAttribute('download') || anchor.isContentEditable) return;
    const destination = websiteDestination(anchor.getAttribute('href') ?? '', window.location.href);
    if (!destination) return;
    event.preventDefault(); event.stopPropagation();
    if (!openWebsite(destination)) showBlockedWebsite(destination);
  };
  document.addEventListener('click', activate, true);
  document.addEventListener('auxclick', activate, true);
}
