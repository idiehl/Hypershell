import { test, expect, type APIRequestContext, type Page } from '@playwright/test';
import { readFile, writeFile } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';
import { addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import type { MeridianNode, MeridianProject } from '../src/core/types.ts';
import { buildPublication, readPublication } from '../src/publication.ts';
import { inspectorPanel } from './ui-helpers.ts';

const tile = (page: Page, id: string) => page.locator(`.ms-node[data-node-id="${id}"]`);
const galleryView = (page: Page, id: string) => page.locator(`.ms-gallery[data-gallery-id="${id}"]`);
const stored = async (request: APIRequestContext, id: string): Promise<MeridianProject> => (await request.get(`/api/projects/${id}`)).json();

function pdfBytes(): Buffer {
  const stream = 'BT /F1 14 Tf 30 60 Td (A gallery document) Tj ET\n';
  const objects = [
    '<< /Type /Catalog /Pages 2 0 R >>',
    '<< /Type /Pages /Kids [3 0 R] /Count 1 >>',
    '<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 100] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>',
    '<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>',
    `<< /Length ${Buffer.byteLength(stream)} >>\nstream\n${stream}endstream`,
  ];
  let text = '%PDF-1.4\n'; const offsets = [0];
  for (let at = 0; at < objects.length; at++) { offsets.push(Buffer.byteLength(text)); text += `${at + 1} 0 obj\n${objects[at]}\nendobj\n`; }
  const start = Buffer.byteLength(text); text += `xref\n0 ${objects.length + 1}\n0000000000 65535 f \n`;
  for (const offset of offsets.slice(1)) text += `${String(offset).padStart(10, '0')} 00000 n \n`;
  text += `trailer\n<< /Size ${objects.length + 1} /Root 1 0 R >>\nstartxref\n${start}\n%%EOF\n`; return Buffer.from(text);
}

async function mediaBytes(page: Page): Promise<{ image: string; video: string }> {
  await page.goto('/__meridian_status');
  return page.evaluate(async () => {
    const canvas = document.createElement('canvas'); canvas.width = 128; canvas.height = 96;
    const paint = canvas.getContext('2d')!; paint.fillStyle = '#315d72'; paint.fillRect(0, 0, 128, 96); paint.fillStyle = '#d5e8e1'; paint.fillRect(26, 20, 76, 56);
    const image = canvas.toDataURL('image/png').split(',')[1]!;
    const stream = canvas.captureStream(20), recorder = new MediaRecorder(stream, { mimeType: 'video/webm;codecs=vp8' });
    const chunks: BlobPart[] = [], complete = new Promise<Blob>(resolve => { recorder.onstop = () => resolve(new Blob(chunks, { type: 'video/webm' })); });
    recorder.ondataavailable = event => { if (event.data.size) chunks.push(event.data); }; recorder.start();
    for (let frame = 0; frame < 8; frame++) { paint.fillStyle = '#24495b'; paint.fillRect(0, 0, 128, 96); paint.fillStyle = '#78d9bf'; paint.fillRect(12 + frame * 5, 28, 24, 36); await new Promise(resolve => setTimeout(resolve, 50)); }
    recorder.stop(); const blob = await complete; stream.getTracks().forEach(track => track.stop());
    const bytes = new Uint8Array(await blob.arrayBuffer()); let binary = ''; for (const byte of bytes) binary += String.fromCharCode(byte);
    return { image, video: btoa(binary) };
  });
}

async function fixture(page: Page, request: APIRequestContext) {
  const media = await mediaBytes(page), project = newProject('Gallery media workspace'), canvasId = project.entryCanvasId;
  const gallery = addNode(project, canvasId, 'container', 'A collection of objects');
  Object.assign(gallery, { x: 110, y: 100, width: 440, height: 480, containerVariant: 'gallery', childLayout: 'contained', childArrangement: 'free', collapsed: false, text: 'Read one object at a time.' });
  const image = addNode(project, canvasId, 'image', 'A statue image'); Object.assign(image, { parentId: gallery.id, x: 130, y: 160, width: 260, height: 250, assetId: 'gallery-image', text: 'An image caption with preserved attribution.' });
  const video = addNode(project, canvasId, 'image', 'A moving illustration'); Object.assign(video, { parentId: gallery.id, x: 130, y: 160, width: 260, height: 250, assetId: 'gallery-video', text: 'A silent video object.' });
  const text = addNode(project, canvasId, 'text', 'A complete passage'); Object.assign(text, { parentId: gallery.id, x: 130, y: 160, width: 260, height: 180, text: 'A literal text passage: <script>window.galleryExecuted=true</script>\n<img src=x onerror="window.galleryExecuted=true">\nIts complete ending remains readable.' });
  const pdf = addNode(project, canvasId, 'document', 'A document attachment'); Object.assign(pdf, { parentId: gallery.id, x: 130, y: 160, width: 260, height: 180, assetId: 'gallery-pdf', text: 'Read the preserved source document.' });
  const link = addNode(project, canvasId, 'document', 'A document link'); Object.assign(link, { parentId: gallery.id, x: 130, y: 160, width: 260, height: 180, contentType: 'link', text: 'An external document remains a link.', hyperlinks: [{ url: 'https://example.invalid/source-document', label: 'Read the source document' }] });
  const nested = addNode(project, canvasId, 'text', 'Nested supporting passage'); Object.assign(nested, { parentId: text.id, x: 130, y: 350, text: 'This descendant is available through the node inspector.' });
  const outside = addNode(project, canvasId, 'text', 'Canvas context'); Object.assign(outside, { x: 770, y: 120, width: 280, height: 160, text: 'Ordinary canvas content remains visible.' });
  project.assets.push({ id: 'gallery-image', name: 'Gallery illustration.png', mime: 'image/png', base64: media.image }, { id: 'gallery-video', name: 'Gallery animation.webm', mime: 'video/webm', base64: media.video }, { id: 'gallery-pdf', name: 'Gallery source.pdf', mime: 'application/pdf', base64: pdfBytes().toString('base64') });
  project.links.push({ id: 'gallery-owned-link', canvasId, parentId: gallery.id, sourceId: image.id, targetId: text.id, kind: 'visual', relation: 'causal' }, { id: 'gallery-child-outside-link', canvasId, parentId: project.canvases[0]!.rootId, sourceId: text.id, targetId: outside.id, kind: 'visual', relation: 'generic' });
  const response = await request.post('/api/projects', { data: commitSnapshots(project) }); expect(response.ok(), await response.text()).toBeTruthy();
  const saved: MeridianProject = await response.json(); await page.goto(`/?project=${saved.id}`); await expect(galleryView(page, gallery.id)).toBeVisible();
  return { project: saved, gallery, image, video, text, pdf, link, nested, outside, items: [image, video, text, pdf, link], media };
}

async function expectItem(page: Page, gallery: MeridianNode, item: MeridianNode, index: number, total = 5) {
  const view = galleryView(page, gallery.id); await expect(view.locator('.ms-gallery-item')).toHaveCount(1);
  await expect(view.locator('.ms-gallery-item')).toHaveAttribute('data-gallery-item-id', item.id);
  await expect(view.locator('.ms-gallery-item-title')).toHaveText(item.title);
  await expect(view.locator('.ms-gallery-position')).toHaveCount(0);
  await expect(view.getByRole('button', { name: `Previous object in ${gallery.tag || gallery.title}`, exact: true })).toBeVisible();
  await expect(view.getByRole('button', { name: `Next object in ${gallery.tag || gallery.title}`, exact: true })).toBeVisible();
  const previous = view.getByRole('button', { name: `Previous object in ${gallery.tag || gallery.title}`, exact: true });
  const next = view.getByRole('button', { name: `Next object in ${gallery.tag || gallery.title}`, exact: true });
  if (index === 1) await expect(previous).toBeDisabled(); else await expect(previous).toBeEnabled();
  if (index === total) await expect(next).toBeDisabled(); else await expect(next).toBeEnabled();
}

test('mixed Gallery renders one object at a time with bounded scoped keyboard controls and no clutter or saving', async ({ page, request }, info) => {
  const f = await fixture(page, request), view = galleryView(page, f.gallery.id);
  await expectItem(page, f.gallery, f.image, 1); await expect(view.locator('img')).toHaveJSProperty('naturalWidth', 128);
  await expect(view.getByRole('button', { name: `Previous object in ${f.gallery.title}`, exact: true })).toBeDisabled();
  for (const item of [...f.items, f.nested]) await expect(tile(page, item.id)).toHaveCount(0);
  await expect(tile(page, f.outside.id)).toBeVisible(); await expect(page.locator('.ms-link-line')).toHaveCount(0); await expect(page.getByRole('button', { name: 'Undo', exact: true })).toBeDisabled();
  await view.focus(); await page.keyboard.press('ArrowRight'); await expectItem(page, f.gallery, f.video, 2);
  const video = view.locator('video'); await expect(video).toHaveAttribute('controls', ''); await expect.poll(() => video.evaluate(element => (element as HTMLVideoElement).readyState)).toBeGreaterThanOrEqual(1); expect(await video.evaluate(element => (element as HTMLVideoElement).paused)).toBe(true);
  await video.evaluate(async element => { const player = element as HTMLVideoElement; player.muted = true; await player.play(); }); await expect.poll(() => video.evaluate(element => (element as HTMLVideoElement).currentTime)).toBeGreaterThan(0);
  await view.getByRole('button', { name: `Next object in ${f.gallery.title}`, exact: true }).click(); await expectItem(page, f.gallery, f.text, 3); await expect(view).toContainText(f.text.text!); await expect(view.locator('script, img')).toHaveCount(0);
  expect(await page.evaluate(() => (window as Window & { galleryExecuted?: boolean }).galleryExecuted)).toBeUndefined();
  await view.getByRole('button', { name: `Next object in ${f.gallery.title}`, exact: true }).click(); await expectItem(page, f.gallery, f.pdf, 4); await expect(view.getByRole('button', { name: 'Open Gallery source.pdf', exact: true })).toBeVisible();
  await view.getByRole('button', { name: `Next object in ${f.gallery.title}`, exact: true }).click(); await expectItem(page, f.gallery, f.link, 5); await expect(view.getByRole('link', { name: 'Read the source document', exact: true })).toHaveAttribute('href', 'https://example.invalid/source-document');
  await expect(view.getByRole('button', { name: `Next object in ${f.gallery.title}`, exact: true })).toBeDisabled(); await view.focus(); await page.keyboard.press('ArrowRight'); await expectItem(page, f.gallery, f.link, 5);
  await view.focus(); await page.keyboard.press('ArrowLeft'); await expectItem(page, f.gallery, f.pdf, 4); await expect(view.locator('video, img')).toHaveCount(0);
  expect(await stored(request, f.project.id)).toEqual(f.project); await expect(page.getByRole('button', { name: 'Undo', exact: true })).toBeDisabled(); await page.screenshot({ path: info.outputPath('gallery-document-object.png') });
});

test('GUID jump activates a gallery child and centers its parent, while its own Content editor remains editable', async ({ page, request }) => {
  const f = await fixture(page, request), view = galleryView(page, f.gallery.id);
  await page.keyboard.press('Control+f'); await page.getByRole('searchbox', { name: 'Search Work nodes', exact: true }).fill(f.text.id);
  await page.getByRole('button', { name: `Jump to Node: ${f.text.title}`, exact: true }).click(); await expectItem(page, f.gallery, f.text, 3); await expect(tile(page, f.text.id)).toHaveCount(0);
  const parentBounds = (await tile(page, f.gallery.id).boundingBox())!, viewport = (await page.locator('.ms-viewport').boundingBox())!;
  expect(Math.abs(parentBounds.x + parentBounds.width / 2 - (viewport.x + viewport.width / 2))).toBeLessThan(12); expect(Math.abs(parentBounds.y + parentBounds.height / 2 - (viewport.y + viewport.height / 2))).toBeLessThan(12);
  expect(await stored(request, f.project.id)).toEqual(f.project);
  await expect(view.getByRole('button', { name: /^Inspect current object:/ })).toHaveCount(0);
  const title = view.locator('.ms-gallery-item-title'), editedTitle = `${f.text.title} — edited inline`;
  await title.click(); await expect(title).toHaveAttribute('contenteditable', 'true');
  await expect(page.getByRole('combobox', { name: 'Side panel', exact: true })).toHaveValue('canvas');
  await title.press('Control+End'); await page.keyboard.insertText(' — edited inline'); await title.press('Tab');
  await expect.poll(async () => (await stored(request, f.project.id)).nodes.find(node => node.id === f.text.id)?.title).toBe(editedTitle);
  await inspectorPanel(page, 'content');
  await expect(page.locator('.inspector').getByLabel('Title', { exact: true })).toHaveValue(editedTitle);
  const body = page.locator('.inspector .rich-text-editor').getByRole('textbox', { name: 'Text body', exact: true }); await expect(body).toHaveText(f.text.text!);
  await body.focus(); await page.keyboard.press('ArrowRight'); await expectItem(page, f.gallery, { ...f.text, title: editedTitle }, 3);
  await body.fill('The gallery object has independently editable text.'); await body.press('Tab'); await expect.poll(async () => (await stored(request, f.project.id)).nodes.find(node => node.id === f.text.id)?.text).toBe('The gallery object has independently editable text.');
  await expect(view).toContainText('The gallery object has independently editable text.'); expect((await stored(request, f.project.id)).nodes.find(node => node.id === f.gallery.id)?.text).toBe(f.gallery.text);
});

test('Ctrl-clicking a gallery image retains the original media-window gesture and attachment bytes', async ({ page, request }) => {
  const f = await fixture(page, request), view = galleryView(page, f.gallery.id);
  await tile(page, f.outside.id).locator('.ms-node-title').click(); await expect(view.getByRole('button', { name: 'Open Gallery illustration.png', exact: true })).toHaveCount(0);
  const opening = page.waitForEvent('popup'); await view.locator('img').click({ modifiers: ['Control'] }); const popup = await opening;
  try {
    await expect(popup.getByRole('main', { name: 'Image viewer', exact: true })).toBeVisible(); await expect(popup.locator('.media-window-stage img')).toHaveJSProperty('naturalWidth', 128); await expect(popup.getByRole('heading', { name: f.image.title, exact: true })).toBeVisible();
    expect(await popup.locator('.media-window-stage img').evaluate(async (element: HTMLImageElement) => { const bytes = new Uint8Array(await (await fetch(element.currentSrc)).arrayBuffer()); let binary = ''; for (const byte of bytes) binary += String.fromCharCode(byte); return btoa(binary); })).toBe(f.media.image);
  } finally { await popup.close(); }
  await expect(tile(page, f.outside.id)).toHaveAttribute('aria-selected', 'true'); await expectItem(page, f.gallery, f.image, 1); expect(await stored(request, f.project.id)).toEqual(f.project);
});

test('offline publications share Gallery media, object navigation and search while preserving the authored payload', async ({ page, request, browser }, info) => {
  const f = await fixture(page, request), path = info.outputPath('gallery-offline.html');
  await writeFile(path, buildPublication(f.project, await readFile('dist/viewer.js', 'utf8'), await readFile('dist/scene.css', 'utf8')));
  const context = await browser.newContext({ offline: true, viewport: { width: 1500, height: 950 } });
  try {
    const viewer = await context.newPage(); await viewer.goto(pathToFileURL(path).href); const authored = await viewer.locator('#meridian-data').textContent(), view = galleryView(viewer, f.gallery.id);
    await expectItem(viewer, f.gallery, f.image, 1); await expect(view.locator('img')).toHaveJSProperty('naturalWidth', 128); for (const item of [...f.items, f.nested]) await expect(tile(viewer, item.id)).toHaveCount(0);
    await view.getByRole('button', { name: `Next object in ${f.gallery.title}`, exact: true }).click(); await expectItem(viewer, f.gallery, f.video, 2); await expect.poll(() => view.locator('video').evaluate(element => (element as HTMLVideoElement).readyState)).toBeGreaterThanOrEqual(1);
    await view.focus(); await viewer.keyboard.press('ArrowRight'); await expectItem(viewer, f.gallery, f.text, 3); await expect(view).toContainText(f.text.text!);
    await view.getByRole('button', { name: `Next object in ${f.gallery.title}`, exact: true }).click(); await expectItem(viewer, f.gallery, f.pdf, 4); await view.getByRole('button', { name: 'Open Gallery source.pdf', exact: true }).click(); await expect(viewer.locator('.ms-asset-dialog')).toBeVisible();
    const documentDownload = viewer.waitForEvent('download'); await viewer.locator('.ms-asset-dialog').getByRole('button', { name: 'Download Gallery source.pdf', exact: true }).click(); const original = await documentDownload, pdfPath = info.outputPath('gallery-original.pdf'); await original.saveAs(pdfPath); expect(await readFile(pdfPath)).toEqual(pdfBytes()); await viewer.getByRole('button', { name: 'Close attachment', exact: true }).click();
    await viewer.getByRole('button', { name: 'Explore project', exact: true }).click(); await viewer.getByRole('searchbox', { name: 'Search project', exact: true }).fill(f.link.id); await viewer.getByRole('button', { name: `Go to ${f.link.title} in ${f.project.canvases[0]!.title}`, exact: true }).click(); await expectItem(viewer, f.gallery, f.link, 5); await expect(view.getByRole('link', { name: 'Read the source document', exact: true })).toBeVisible();
    expect(await viewer.locator('#meridian-data').textContent()).toBe(authored); expect(await viewer.evaluate(() => (window as Window & { galleryExecuted?: boolean }).galleryExecuted)).toBeUndefined(); await expect(viewer.locator('.ms-link-line')).toHaveCount(0);
    await viewer.getByRole('button', { name: 'Close project navigation', exact: true }).click(); const downloading = viewer.waitForEvent('download'); await viewer.getByRole('button', { name: 'Download this published canvas for viewing or importing into the Meridian editor', exact: true }).click(); const download = await downloading, copyPath = info.outputPath('gallery-unchanged-copy.html'); await download.saveAs(copyPath);
    const copied = readPublication(await readFile(copyPath, 'utf8')); expect(copied.nodes.find(node => node.title === f.gallery.title)?.containerVariant).toBe('gallery'); expect(copied.assets.map(asset => ({ name: asset.name, mime: asset.mime, base64: asset.base64 }))).toEqual(f.project.assets.map(asset => ({ name: asset.name, mime: asset.mime, base64: asset.base64 }))); expect(copied.nodes.filter(node => node.parentId === copied.nodes.find(node => node.title === f.gallery.title)!.id).map(node => node.title)).toEqual(f.items.map(node => node.title));
    await viewer.reload(); await expectItem(viewer, f.gallery, f.image, 1); await viewer.screenshot({ path: info.outputPath('gallery-offline-first-object.png'), fullPage: true });
  } finally { await context.close(); }
});
