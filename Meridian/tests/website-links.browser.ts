import { test, expect, type APIRequestContext, type BrowserContext, type Locator, type Page } from '@playwright/test';
import { mkdir, readFile } from 'node:fs/promises';
import { addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import type { MeridianNode, MeridianProject } from '../src/core/types.ts';
import { buildPublication } from '../src/publication.ts';

const SITE = 'https://namespace-docs.test';
const output = 'output/website-browser-cycle-20261025/browser-review';
const tile = (page: Page, node: MeridianNode) => page.locator(`.ms-node[data-node-id="${node.id}"]`);
const pageErrors = new WeakMap<BrowserContext, string[]>();
test.beforeEach(async ({ context }) => {
  const errors: string[] = []; pageErrors.set(context, errors);
  for (const page of context.pages()) page.on('pageerror', error => errors.push(error.message));
  context.on('page', page => page.on('pageerror', error => errors.push(error.message)));
});
test.afterEach(async ({ context }) => expect(pageErrors.get(context) ?? []).toEqual([]));

async function current(request: APIRequestContext, id: string): Promise<MeridianProject> {
  const response = await request.get(`/api/projects/${id}`); expect(response.ok()).toBe(true); return response.json();
}

async function fixture(page: Page, request: APIRequestContext, context: BrowserContext) {
  const siteRequests: string[] = [];
  const siteReferrers: string[] = [];
  await context.route(`${SITE}/**`, route => {
    siteRequests.push(route.request().url());
    siteReferrers.push(route.request().headers().referer ?? '');
    return route.fulfill({ contentType: 'text/html', headers: { 'X-Frame-Options': 'DENY', 'Content-Security-Policy': "frame-ancestors 'none'" }, body: `<!doctype html><title>Namespace reference</title><h1>Namespace reference</h1><p>Ordinary website content</p><a href="${SITE}/next">Next reference</a><script>window.openerPresent=!!window.opener;</script>` });
  });
  const project = newProject('Website links');
  const style = { titleSize: 22, fontSize: 16, padding: 16, textAlign: 'center' as const };
  const link = addNode(project, project.entryCanvasId, 'media', 'System.Text');
  Object.assign(link, { x: 100, y: 100, width: 310, height: 160, sizeMode: 'fixed', style,
    contentItems: [{ id: 'namespace-reference', type: 'link', url: `${SITE}/system.text`, label: 'Namespace reference' }] });
  const supporting = addNode(project, project.entryCanvasId, 'media', 'System.Collections');
  Object.assign(supporting, { x: 530, y: 100, width: 310, height: 160, sizeMode: 'fixed', style,
    contentItems: [{ id: 'collections-label', type: 'text', text: 'Collections' }, { id: 'collections-reference', type: 'link', url: `${SITE}/system.collections`, label: 'Collections reference' }] });
  const document = addNode(project, project.entryCanvasId, 'media', 'Reference');
  Object.assign(document, { x: 100, y: 380, width: 310, height: 180, sizeMode: 'fixed', style,
    contentItems: [{ id: 'reference-label', type: 'text', text: 'Namespace notes' }, { id: 'reference-document', type: 'asset', assetId: 'notes-md' }] });
  project.assets.push({ id: 'notes-md', name: 'Notes.md', mime: 'text/markdown', base64: Buffer.from(`# Namespaces\n\n[Text reference](${SITE}/markdown.text)\n\n${SITE}/markdown.autolink\n`).toString('base64') });
  const response = await request.post('/api/projects', { data: commitSnapshots(project) });
  expect(response.ok(), await response.text()).toBe(true);
  const saved: MeridianProject = await response.json();
  await page.goto(`/?project=${project.id}&canvas=${project.entryCanvasId}`);
  await expect(page.locator('.workspace')).toBeVisible();
  return { project: saved, link, supporting, document, siteRequests, siteReferrers };
}

async function select(page: Page, node: MeridianNode) {
  await tile(page, node).click({ position: { x: 12, y: 12 } });
  const hub = page.getByRole('complementary', { name: 'Node Hub', exact: true }); await expect(hub).toBeVisible(); return hub;
}

async function popupFrom(page: Page, context: BrowserContext, anchor: Locator, path: string, keyboard = false) {
  const address = page.url(), popupPromise = context.waitForEvent('page');
  if (keyboard) { await anchor.focus(); await anchor.press('Enter'); } else await anchor.click();
  const popup = await popupPromise;
  await expect(popup).toHaveURL(`${SITE}${path}`);
  await expect(popup.getByRole('heading', { name: 'Namespace reference' })).toBeVisible();
  expect(await popup.evaluate(() => window.opener === null)).toBe(true);
  expect(page.url()).toBe(address);
  await expect(page.locator('.workspace,#meridian-viewer').first()).toBeVisible();
  return popup;
}

test('a namespace website opens as an isolated popup while the editor and saved Work stay intact', async ({ page, request, context }) => {
  const f = await fixture(page, request, context);
  expect(f.siteRequests).toEqual([]);
  const anchor = tile(page, f.link).getByRole('link', { name: 'Namespace reference', exact: true });
  const popup = await popupFrom(page, context, anchor, '/system.text');
  expect(f.siteRequests).toEqual([`${SITE}/system.text`]);
  expect(f.siteReferrers).toEqual(['']);
  await expect(page.locator('iframe')).toHaveCount(0);
  await mkdir(output, { recursive: true });
  await page.screenshot({ path: `${output}/namespace-work.png` });
  await popup.screenshot({ path: `${output}/website-popup.png` });
  await popup.close();
  expect(await current(request, f.project.id)).toEqual(f.project);
  await page.reload(); await expect(page.locator('.workspace')).toBeVisible();
  const reopened = await popupFrom(page, context, tile(page, f.link).getByRole('link', { name: 'Namespace reference', exact: true }), '/system.text', true);
  await reopened.close(); expect(await current(request, f.project.id)).toEqual(f.project);
});

test('supporting Hub links and links inside a pop-out share the website window behavior', async ({ page, request, context }) => {
  const f = await fixture(page, request, context), hub = await select(page, f.supporting);
  const anchor = hub.getByRole('link', { name: 'Collections reference', exact: true });
  await anchor.scrollIntoViewIfNeeded();
  let popup = await popupFrom(page, context, anchor, '/system.collections'); await popup.close();
  await hub.locator('[data-content-item="collections-reference"]').getByRole('button', { name: 'Pop out content', exact: true }).click();
  const dialog = page.locator('dialog.nh-popout'); await expect(dialog).toBeVisible();
  popup = await popupFrom(page, context, dialog.getByRole('link', { name: 'Collections reference', exact: true }), '/system.collections');
  await popup.close(); await expect(dialog).toBeVisible();
  await dialog.getByRole('button', { name: 'Close content viewer', exact: true }).click();
  expect(await current(request, f.project.id)).toEqual(f.project);
});

test('Markdown reader links bypass stopped bubbling and open their full website without an iframe', async ({ page, request, context }) => {
  const f = await fixture(page, request, context), hub = await select(page, f.document);
  const card = hub.locator('[data-content-item="reference-document"]');
  await card.scrollIntoViewIfNeeded();
  const reader = card.getByRole('region', { name: 'Document viewer: Notes.md', exact: true }); await expect(reader).toBeVisible();
  let popup = await popupFrom(page, context, reader.getByRole('link', { name: 'Text reference', exact: true }), '/markdown.text'); await popup.close();
  await card.getByRole('button', { name: 'Pop out content', exact: true }).click();
  const dialog = page.locator('dialog.nh-popout'); await expect(dialog).toBeVisible();
  popup = await popupFrom(page, context, dialog.getByRole('link', { name: `${SITE}/markdown.autolink`, exact: true }), '/markdown.autolink', true);
  await popup.close(); await expect(dialog).toBeVisible();
  await dialog.getByRole('button', { name: 'Close content viewer', exact: true }).click();
  expect(await current(request, f.project.id)).toEqual(f.project);
});

test('a portable publication retains website popups and never changes its embedded source', async ({ page, request, context }) => {
  const f = await fixture(page, request, context);
  const html = buildPublication(f.project, await readFile('dist/viewer.js', 'utf8'), await readFile('dist/scene.css', 'utf8'));
  const viewer = await context.newPage(); await viewer.setContent(html, { waitUntil: 'domcontentloaded' });
  await expect(viewer.locator('.ms-node')).toHaveCount(3);
  const source = await viewer.locator('#meridian-data').textContent();
  let popup = await popupFrom(viewer, context, tile(viewer, f.link).getByRole('link', { name: 'Namespace reference', exact: true }), '/system.text'); await popup.close();
  await tile(viewer, f.supporting).click({ position: { x: 12, y: 12 } });
  const hub = viewer.getByRole('region', { name: 'Node Hub: System.Collections', exact: true }); await expect(hub).toBeVisible();
  const anchor = hub.getByRole('link', { name: 'Collections reference', exact: true }); await anchor.scrollIntoViewIfNeeded();
  popup = await popupFrom(viewer, context, anchor, '/system.collections', true); await popup.close();
  expect(await viewer.locator('#meridian-data').textContent()).toBe(source);
  expect(await current(request, f.project.id)).toEqual(f.project);
  await viewer.close();
});

test('blocked popup feedback retains the URL, retries within a fresh gesture and restores focus', async ({ page, request, context }) => {
  const f = await fixture(page, request, context);
  await page.evaluate(() => {
    const state = window as Window & { realWebsiteOpen?: typeof window.open };
    state.realWebsiteOpen = window.open.bind(window);
    window.open = () => { throw new DOMException('Popup blocked by the embedding sandbox', 'SecurityError'); };
  });
  const anchor = tile(page, f.link).getByRole('link', { name: 'Namespace reference', exact: true });
  await anchor.focus(); await anchor.press('Enter');
  const dialog = page.getByRole('dialog', { name: 'Open website', exact: true });
  await expect(dialog).toBeVisible(); await expect(dialog).toContainText('blocked');
  await expect(dialog.getByRole('textbox', { name: 'Website address', exact: true })).toHaveValue(`${SITE}/system.text`);
  await mkdir(output, { recursive: true }); await page.screenshot({ path: `${output}/blocked-website-retry.png` });
  expect(f.siteRequests).toEqual([]);
  await page.evaluate(() => { window.open = () => null; });
  await dialog.getByRole('button', { name: 'Open website', exact: true }).click();
  await expect(dialog).toContainText('still blocked');
  await page.evaluate(() => { window.open = (window as Window & { realWebsiteOpen?: typeof window.open }).realWebsiteOpen!; });
  const popupPromise = context.waitForEvent('page');
  await dialog.getByRole('button', { name: 'Open website', exact: true }).click();
  const popup = await popupPromise; await expect(popup).toHaveURL(`${SITE}/system.text`);
  await expect(dialog).toHaveCount(0); await popup.close();
  await expect(anchor).toBeFocused();
  expect(await current(request, f.project.id)).toEqual(f.project);
});

test('local Work routes, fragment links, original downloads and mail links keep their ordinary actions', async ({ page, request, context }) => {
  const f = await fixture(page, request, context);
  await page.evaluate(() => {
    const state = window as Window & { websitePassThrough?: Array<{ id: string; prevented: boolean }>; websiteOpenCalls?: number };
    state.websitePassThrough = []; state.websiteOpenCalls = 0;
    window.open = () => { state.websiteOpenCalls!++; return null; };
    const list = document.createElement('nav'); list.id = 'website-passthrough';
    for (const [id, href, download] of [
      ['local-work', '/?project=another&canvas=work', false],
      ['fragment', '#inside-document', false],
      ['email', 'mailto:reference@example.com', false],
      ['original-file', 'https://namespace-docs.test/file.csv', true],
    ] as const) {
      const anchor = document.createElement('a'); anchor.href = href; anchor.textContent = id;
      if (download) anchor.download = 'original.csv';
      anchor.onclick = event => { state.websitePassThrough!.push({ id, prevented: event.defaultPrevented }); event.preventDefault(); };
      list.append(anchor);
    }
    Object.assign(list.style, { position: 'fixed', top: '0', left: '0', zIndex: '99999', background: 'white', display: 'flex', gap: '20px' }); document.body.append(list);
  });
  for (const name of ['local-work', 'fragment', 'email', 'original-file']) await page.locator('#website-passthrough').getByRole('link', { name, exact: true }).click();
  const result = await page.evaluate(() => {
    const state = window as Window & { websitePassThrough?: Array<{ id: string; prevented: boolean }>; websiteOpenCalls?: number };
    return { records: state.websitePassThrough, opens: state.websiteOpenCalls };
  });
  expect(result.opens).toBe(0);
  expect(result.records).toEqual(['local-work', 'fragment', 'email', 'original-file'].map(id => ({ id, prevented: false })));
  await expect(page.getByRole('dialog', { name: 'Open website', exact: true })).toHaveCount(0);
  expect(await current(request, f.project.id)).toEqual(f.project);
});

test('the native website-browser capability sends a detached remote request without reporting a false popup block', async ({ page, request, context }) => {
  const f = await fixture(page, request, context);
  await page.evaluate(() => {
    const state = window as Window & { __meridianWebsiteBrowser?: boolean; websiteNativeCalls?: unknown[][] };
    state.__meridianWebsiteBrowser = true; state.websiteNativeCalls = [];
    window.open = (...args) => { state.websiteNativeCalls!.push(args); return null; };
  });
  await tile(page, f.link).getByRole('link', { name: 'Namespace reference', exact: true }).click();
  const calls = await page.evaluate(() => (window as Window & { websiteNativeCalls?: unknown[][] }).websiteNativeCalls);
  expect(calls).toEqual([[`${SITE}/system.text`, '_blank', 'popup,width=1120,height=820,noopener,noreferrer']]);
  await expect(page.getByRole('dialog', { name: 'Open website', exact: true })).toHaveCount(0);
  expect(f.siteRequests).toEqual([]); expect(await current(request, f.project.id)).toEqual(f.project);
});

test('an already-open native host routes the website directly without reserving a blank window or navigating away from the Work', async ({ page, request, context }) => {
  const f = await fixture(page, request, context), address = page.url();
  await page.evaluate(() => {
    const state = window as Window & { __meridianNativeChrome?: boolean; __meridianWebsiteBrowser?: boolean; legacyWebsiteCalls?: unknown[][] };
    state.__meridianNativeChrome = true; delete state.__meridianWebsiteBrowser; state.legacyWebsiteCalls = [];
    window.open = (...args) => { state.legacyWebsiteCalls!.push(args); return null; };
  });
  await tile(page, f.link).getByRole('link', { name: 'Namespace reference', exact: true }).click();
  expect(await page.evaluate(() => (window as Window & { legacyWebsiteCalls?: unknown[][] }).legacyWebsiteCalls))
    .toEqual([[`${SITE}/system.text`, '_blank', 'popup,width=1120,height=820,noopener,noreferrer']]);
  expect(page.url()).toBe(address); await expect(page.locator('.workspace')).toBeVisible();
  await expect(page.getByRole('dialog', { name: 'Open website', exact: true })).toHaveCount(0);
  expect(f.siteRequests).toEqual([]); expect(await current(request, f.project.id)).toEqual(f.project);
});

