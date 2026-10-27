import { interactionTool, workspaceAction } from './ui-helpers.ts';
import { test, expect, type APIRequestContext, type Locator, type Page } from '@playwright/test';
import { readFile, writeFile } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';
import { addCanvas, addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import { buildPublication } from '../src/publication.ts';

async function images(page: Page) {
  await page.goto('/__meridian_status');
  return page.evaluate(() => {
    const canvas = document.createElement('canvas'); canvas.width = 128; canvas.height = 96;
    const context = canvas.getContext('2d')!;
    const paint = (color: string) => { context.fillStyle = color; context.fillRect(0, 0, 128, 96); context.fillStyle = '#faf8ee'; context.fillRect(26, 20, 76, 56); return canvas.toDataURL('image/png').split(',')[1]; };
    return { original: paint('#315d72'), draft: paint('#83593c') };
  });
}
async function fixture(page: Page, request: APIRequestContext) {
  const bytes = await images(page), project = newProject('Image window QA'), home = project.entryCanvasId;
  project.assets.push({ id: 'art', name: 'Illustration.png', mime: 'image/png', base64: bytes.original },
    { id: 'plain', name: 'Source.txt', mime: 'text/plain', base64: Buffer.from('A document keeps its attachment button.').toString('base64') });
  const image = addNode(project, home, 'image', 'An image with children'); Object.assign(image, { x: 50, y: 60, width: 300, height: 270, assetId: 'art', collapsed: true });
  const child = addNode(project, home, 'text', 'Hidden child'); Object.assign(child, { parentId: image.id, x: 60, y: 370 });
  const documentImage = addNode(project, home, 'document', 'Raster document'); Object.assign(documentImage, { x: 410, y: 60, width: 300, height: 270, assetId: 'art' });
  const sourceCanvas = addCanvas(project, 'Originals'), source = addNode(project, sourceCanvas.id, 'image', 'Saved original'); source.assetId = 'art';
  const reference = addNode(project, home, 'reference', 'Pinned illustration'); Object.assign(reference, { x: 770, y: 60, width: 300, height: 270, targetId: source.id });
  const note = addNode(project, home, 'text', 'Ordinary selection'); Object.assign(note, { x: 50, y: 430, width: 300, height: 130, text: 'Ctrl-click still toggles selection here.' });
  const document = addNode(project, home, 'document', 'Source document'); Object.assign(document, { x: 410, y: 430, width: 300, height: 180, assetId: 'plain' });
  const saved = commitSnapshots(project);
  // A current asset can differ from a cross-canvas reference's committed bytes.
  saved.assets.find(asset => asset.id === 'art')!.base64 = bytes.draft;
  expect((await request.post('/api/projects', { data: saved })).ok()).toBeTruthy();
  await page.goto(`/?project=${project.id}`);
  await page.getByRole('button', { name: 'Fit Work', exact: true }).click();
  return { project: saved, bytes, image, child, documentImage, reference, note, document };
}
const card = (page: Page, id: string) => page.locator(`.ms-node[data-node-id="${id}"]`);
async function openImage(page: Page, image: Locator) {
  const opening = page.waitForEvent('popup'); await image.click({ modifiers: ['Control'] });
  const popup = await opening;
  await expect(popup.getByRole('main', { name: 'Image viewer', exact: true })).toBeVisible();
  await expect(popup.locator('.media-window-stage img')).toHaveJSProperty('naturalWidth', 128);
  return popup;
}
async function displayedBytes(popup: Page) {
  return popup.locator('.media-window-stage img').evaluate(async (image: HTMLImageElement) => {
    const bytes = new Uint8Array(await (await fetch(image.currentSrc)).arrayBuffer()); let binary = ''; for (const byte of bytes) binary += String.fromCharCode(byte); return btoa(binary);
  });
}

test('Ctrl-click opens raster image, document and pinned-reference windows without selecting, moving or disclosing', async ({ page, request }, info) => {
  const f = await fixture(page, request);
  for (const node of [f.image, f.documentImage, f.reference]) await expect(card(page, node.id).getByRole('button', { name: 'Open Illustration.png', exact: true })).toHaveCount(0);
  await expect(card(page, f.document.id).getByRole('button', { name: 'Read Source.txt in this node', exact: true })).toBeVisible();
  await card(page, f.note.id).locator('.ms-node-title').click();
  const geometry = await card(page, f.image.id).getAttribute('style');
  const popup = await openImage(page, card(page, f.image.id).locator('img'));
  try {
    expect(new URL(popup.url()).origin).toBe(new URL(page.url()).origin);
    expect(new URL(popup.url()).searchParams.get('window')).toBe('media');
    await expect(popup).toHaveTitle('Illustration.png · Atlas Meridian');
    await expect(popup.getByRole('heading', { name: f.image.title, exact: true })).toBeVisible();
    expect(await displayedBytes(popup)).toBe(f.bytes.draft);
    await expect(popup.locator('.media-window-stage img')).toHaveCSS('object-fit', 'contain');
    await popup.getByRole('button', { name: 'Show image at actual size', exact: true }).click();
    await expect(popup.locator('.media-window-stage')).toHaveClass(/is-original/);
    await popup.getByRole('button', { name: 'Fit image to window', exact: true }).click();
    const downloading = popup.waitForEvent('download'); await popup.getByRole('link', { name: 'Download original', exact: true }).click();
    const download = await downloading, path = info.outputPath('original-image.png'); await download.saveAs(path);
    expect(await readFile(path)).toEqual(Buffer.from(f.bytes.draft, 'base64'));
    await popup.screenshot({ path: info.outputPath('dedicated-image-window.png') });
  } finally { await popup.close(); }
  await expect(card(page, f.note.id)).toHaveAttribute('aria-selected', 'true');
  await expect(card(page, f.image.id)).toHaveAttribute('aria-selected', 'false');
  await expect(card(page, f.child.id)).toHaveCount(0); expect(await card(page, f.image.id).getAttribute('style')).toBe(geometry);
  const documentPopup = await openImage(page, card(page, f.documentImage.id).locator('img'));
  try { expect(await displayedBytes(documentPopup)).toBe(f.bytes.draft); } finally { await documentPopup.close(); }
  const referencePopup = await openImage(page, card(page, f.reference.id).locator('img'));
  try { expect(await displayedBytes(referencePopup)).toBe(f.bytes.original); } finally { await referencePopup.close(); }
  await card(page, f.image.id).locator('img').click(); await expect(card(page, f.image.id)).toHaveAttribute('aria-selected', 'true');
  await card(page, f.note.id).locator('.ms-node-title').click({ modifiers: ['Control'] });
  await expect(card(page, f.image.id)).toHaveAttribute('aria-selected', 'true'); await expect(card(page, f.note.id)).toHaveAttribute('aria-selected', 'true');
  await expect(card(page, f.child.id)).toHaveCount(0);
});

test('a newly attached draft image opens while its automatic save is still pending', async ({ page, request }) => {
  const bytes = await images(page), project = newProject('Unsaved image window QA');
  expect((await request.post('/api/projects', { data: commitSnapshots(project) })).ok()).toBeTruthy();
  await page.goto(`/?project=${project.id}`);
  let release!: () => void, began!: () => void, continued!: () => void, saveIntercepted = false;
  const gate = new Promise<void>(resolve => { release = resolve; }), saving = new Promise<void>(resolve => { began = resolve; });
  const completed = new Promise<void>(resolve => { continued = resolve; });
  await page.route(`**/api/projects/${project.id}`, async route => {
    if (route.request().method() !== 'PUT') { await route.continue(); return; }
    saveIntercepted = true; began(); await gate;
    try { await route.continue(); } finally { continued(); }
  });
  try {
    await page.getByRole('button',{name:'New Node',exact:true}).click();await page.getByRole('menuitem',{name:'Node',exact:true}).click();await page.locator('.canvas-stage').click({position:{x:80,y:50}});
    const dialog=page.getByRole('dialog',{name:'New Node',exact:true});await dialog.getByLabel('Type',{exact:true}).selectOption('media');await dialog.getByLabel('Content',{exact:true}).selectOption('asset');await dialog.getByLabel('Content file',{exact:true}).setInputFiles({ name: 'Draft illustration.png', mimeType: 'image/png', buffer: Buffer.from(bytes.draft, 'base64') });await dialog.getByRole('button',{name:'Create node',exact:true}).click();await interactionTool(page);
    const image = page.locator('.ms-node').getByRole('img', { name: 'Draft illustration.png', exact: true }); await expect(image).toBeVisible();
    await saving;
    const popup = await openImage(page, image);
    try {
      expect(await displayedBytes(popup)).toBe(bytes.draft);
      const saved = await (await request.get(`/api/projects/${project.id}`)).json(); expect(saved.assets).toEqual([]);
      await expect(popup).toHaveTitle('Draft illustration.png · Atlas Meridian');
    } finally { await popup.close(); }
  } finally { release(); if (saveIntercepted) await completed; await page.unroute(`**/api/projects/${project.id}`); }
});

test('offline publication opens a self-contained pinned image window and keeps authored data unchanged', async ({ page, request, browser }, info) => {
  const f = await fixture(page, request);
  f.project.nodes.find(node => node.id === f.reference.id)!.title = 'Illustration <img src=x onerror="window.mediaExecuted=true">';
  const path = info.outputPath('offline-image-window.html');
  await writeFile(path, buildPublication(f.project, await readFile('dist/viewer.js', 'utf8'), await readFile('dist/scene.css', 'utf8')));
  const context = await browser.newContext({ offline: true, viewport: { width: 1500, height: 950 } });
  try {
    const viewer = await context.newPage(); await viewer.goto(pathToFileURL(path).href);
    const authored = await viewer.locator('#meridian-data').textContent();
    const popup = await openImage(viewer, card(viewer, f.reference.id).locator('img'));
    expect(new URL(popup.url()).protocol).toBe('file:'); expect(new URL(popup.url()).hash).toMatch(/^#meridian-media=/);
    await expect(popup.getByRole('heading')).toHaveText(f.project.nodes.find(node => node.id === f.reference.id)!.title);
    expect(await popup.evaluate(() => (window as unknown as { mediaExecuted?: boolean }).mediaExecuted)).toBeUndefined();
    expect(await displayedBytes(popup)).toBe(f.bytes.original);
    await expect(viewer.locator('.mv-selection-count')).toHaveText('0 selected');
    expect(await viewer.locator('#meridian-data').textContent()).toBe(authored);
    await popup.screenshot({ path: info.outputPath('offline-pinned-image-window.png') });
    await popup.close();
  } finally { await context.close(); }
});

test('a sandboxed publication preview opens an app media route with an opaque-opener handshake', async ({ page, request }) => {
  const f = await fixture(page, request);
  await workspaceAction(page,'Preview');
  await page.getByRole('button', { name: 'Open preview', exact: true }).click();
  const preview = page.frameLocator('iframe[title="Published Meridian preview"]');
  const opening = page.waitForEvent('popup');
  await preview.locator(`.ms-node[data-node-id="${f.reference.id}"] img`).click({ modifiers: ['Control'] });
  const popup = await opening;
  try {
    expect(new URL(popup.url()).origin).toBe(new URL(page.url()).origin);
    expect(new URL(popup.url()).searchParams.get('window')).toBe('media');
    expect(new URL(popup.url()).searchParams.get('mediaOpaqueOpener')).toBe('1');
    await expect(popup.locator('.media-window-stage img')).toHaveJSProperty('naturalWidth', 128);
    expect(await displayedBytes(popup)).toBe(f.bytes.original);
  } finally { await popup.close(); }
});
