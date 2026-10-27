import { test, expect, type Page, type APIRequestContext } from '@playwright/test';
import { newProject, addNode, commitSnapshots } from '../src/core/model.ts';
import type { MeridianProject } from '../src/core/types.ts';
import { buildPublication, readPublication } from '../src/publication.ts';
import { readFile, writeFile } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';

const tile = (page: Page, id: string) => page.locator(`.ms-node[data-node-id="${id}"]`);
const stored = async (request: APIRequestContext, id: string): Promise<MeridianProject> => (await request.get(`/api/projects/${id}`)).json();
async function png(page: Page, color: string) {
  return Buffer.from(await page.evaluate(color => { const image = document.createElement('canvas'); image.width = 96; image.height = 64; const paint = image.getContext('2d')!; paint.fillStyle = color; paint.fillRect(0, 0, 96, 64); paint.fillStyle = '#d9ede7'; paint.fillRect(9, 9, 25, 25); return image.toDataURL('image/png').split(',')[1]!; }, color), 'base64');
}
async function appearance(page: Page, canvas = false) {
  await page.getByRole('combobox', { name: 'Side panel', exact: true }).selectOption('appearance');
  const panel = page.locator('.appearance-panel');
  if (!(await panel.evaluate(element => (element as HTMLDetailsElement).open))) await panel.locator('summary').click();
  await expect(panel.locator('summary')).toHaveText(canvas ? 'Canvas appearance' : 'Appearance');
  return panel;
}
async function fixture(page: Page, request: APIRequestContext) {
  await page.goto('/'); const original = await png(page, '#395175'), uploaded = await png(page, '#5e8662');
  const project = newProject('Background workspace'), node = addNode(project, project.entryCanvasId, 'image', 'Media with its own backdrop');
  Object.assign(node, { x: 110, y: 110, width: 330, height: 290, assetId: 'original-media', text: 'The primary image and caption stay intact.', style: { shape: 'hexagon', fontFamily: 'serif', fontSize: 15, titleSize: 21, fill: '#18352b', textColor: '#dcefe5', imageFit: 'contain' } });
  const other = addNode(project, project.entryCanvasId, 'text', 'Another node'); Object.assign(other, { x: 600, y: 140, width: 240, height: 200, text: 'Selection guards protect this node.' });
  project.assets.push({ id: 'original-media', name: 'Original media.png', mime: 'image/png', base64: original.toString('base64') });
  const response = await request.post('/api/projects', { data: commitSnapshots(project) }); expect(response.ok(), await response.text()).toBeTruthy();
  await page.goto(`/?project=${project.id}`); await expect(tile(page, node.id)).toBeVisible();
  return { project, node, other, original, uploaded };
}

test('node and canvas share grid scales and decorative presets without replacing typography or primary media', async ({ page, request }, info) => {
  const f = await fixture(page, request); await tile(page, f.node.id).locator('.ms-node-title').click(); await appearance(page);
  const choices = page.getByRole('combobox', { name: 'Background preset', exact: true }); await expect(choices.locator('option')).toHaveCount(14);
  const presets = ['plain', 'dots-fine', 'dots-default', 'dots-medium', 'dots-wide', 'grid-fine', 'grid-medium', 'grid-wide', 'grid-large', 'isometric', 'linen', 'contours', 'aurora', 'stars'];
  for (const id of presets) {
    await choices.selectOption(id);
    await expect(page.locator('.background-preview')).toBeVisible();
    const paint = await page.locator('.background-preview-image').evaluate(element => getComputedStyle(element).backgroundImage);
    if (id === 'plain') expect(paint).toBe('none'); else expect(paint).not.toBe('none');
  }
  await choices.selectOption('grid-large'); await expect(page.getByLabel('Background size', { exact: true })).toHaveValue('128');
  await page.getByLabel('Background size', { exact: true }).fill('96'); await page.getByLabel('Background size', { exact: true }).press('Tab');
  await expect.poll(async () => (await stored(request, f.project.id)).nodes.find(node => node.id === f.node.id)?.style?.backgroundScale).toBe(96);
  const savedNode = (await stored(request, f.project.id)).nodes.find(node => node.id === f.node.id)!;
  expect(savedNode.style).toMatchObject({ ...f.node.style, backgroundPattern: 'grid', backgroundScale: 96 }); expect(savedNode.assetId).toBe(f.node.assetId);
  expect(savedNode.text).toBe(f.node.text); await expect(tile(page, f.node.id).locator('.ms-node-background')).toHaveCSS('background-size', '96px 96px, 96px 96px');
  await page.keyboard.press('Escape'); await appearance(page, true);
  await page.getByLabel('Background preset', { exact: true }).selectOption('dots-medium');
  await expect(page.locator('.ms-canvas-background')).toHaveCSS('background-size', '32px 32px');
  await page.getByLabel('Background preset', { exact: true }).selectOption('contours');
  await expect.poll(async () => (await stored(request, f.project.id)).canvases[0]!.style?.backgroundPattern).toBe('contours');
  await page.reload(); await expect(page.locator('.ms-canvas-background')).toHaveCSS('background-size', '160px 160px');
  await page.screenshot({ path: info.outputPath('canvas-contours-node-grid.png') });
});

test('custom background images save, reopen, clip to node shapes and export offline while media remains independent', async ({ page, request, browser }, info) => {
  const f = await fixture(page, request); await tile(page, f.node.id).locator('.ms-node-title').click(); await appearance(page);
  await page.getByLabel('Upload background image', { exact: true }).setInputFiles({ name: 'My node background.png', mimeType: 'image/png', buffer: f.uploaded });
  await expect(page.locator('.background-image-name')).toHaveText('My node background.png'); await expect(page.getByLabel('Background image fit', { exact: true })).toHaveValue('cover');
  await page.getByLabel('Background image fit', { exact: true }).selectOption('tile'); await page.getByLabel('Background size', { exact: true }).fill('80'); await page.getByLabel('Background size', { exact: true }).press('Tab');
  await expect.poll(async () => (await stored(request, f.project.id)).nodes.find(node => node.id === f.node.id)?.style?.backgroundScale).toBe(80);
  const imageLayer = tile(page, f.node.id).locator('.ms-node-background'); await expect(imageLayer).toHaveCSS('background-repeat', 'repeat');
  expect(await imageLayer.evaluate(element => getComputedStyle(element).clipPath)).toContain('polygon(');
  const before = await stored(request, f.project.id), savedNode = before.nodes.find(node => node.id === f.node.id)!;
  expect(savedNode.assetId).toBe('original-media'); expect(savedNode.style).toMatchObject({ ...f.node.style, backgroundFit: 'tile', backgroundScale: 80, backgroundOpacity: .35 });
  expect(before.assets.find(asset => asset.id === savedNode.style!.backgroundAssetId)?.base64).toBe(f.uploaded.toString('base64'));
  await page.keyboard.press('Escape'); await appearance(page, true);
  await page.getByLabel('Upload background image', { exact: true }).setInputFiles({ name: 'My canvas background.png', mimeType: 'image/png', buffer: f.original });
  await expect(page.locator('.background-image-name')).toHaveText('My canvas background.png'); await page.getByLabel('Background image fit', { exact: true }).selectOption('contain');
  await expect.poll(async () => (await stored(request, f.project.id)).canvases[0]!.style?.backgroundFit).toBe('contain');
  const saved = await stored(request, f.project.id); expect(saved.assets).toHaveLength(3); await page.reload();
  await expect(page.locator('.ms-canvas-background')).toHaveCSS('background-size', 'contain'); await expect(tile(page, f.node.id).locator('.ms-node-body img')).toHaveJSProperty('naturalWidth', 96);
  const path = info.outputPath('backgrounds-offline.html'); await writeFile(path, buildPublication(saved, await readFile('dist/viewer.js', 'utf8'), await readFile('dist/scene.css', 'utf8')));
  const context = await browser.newContext({ offline: true, viewport: { width: 1300, height: 800 } });
  try {
    const viewer = await context.newPage(); await viewer.goto(pathToFileURL(path).href); await expect(viewer.locator('.ms-canvas-background')).toHaveCSS('background-size', 'contain');
    await expect(tile(viewer, f.node.id).locator('.ms-node-background')).toHaveCSS('background-size', '80px auto'); await expect(tile(viewer, f.node.id).locator('.ms-node-body img')).toHaveJSProperty('naturalWidth', 96);
    const reopened = readPublication(await readFile(path, 'utf8')); expect(reopened.assets.map(asset => asset.base64).sort()).toEqual(saved.assets.map(asset => asset.base64).sort());
    await viewer.screenshot({ path: info.outputPath('backgrounds-offline.png') });
  } finally { await context.close(); }
  await tile(page, f.node.id).locator('.ms-node-title').click(); await appearance(page); await page.getByRole('button', { name: 'Remove background image', exact: true }).click();
  await expect.poll(async () => (await stored(request, f.project.id)).nodes.find(node => node.id === f.node.id)?.style?.backgroundAssetId).toBeUndefined();
  expect((await stored(request, f.project.id)).nodes.find(node => node.id === f.node.id)?.assetId).toBe('original-media'); await expect(tile(page, f.node.id).locator('.ms-node-body img')).toHaveJSProperty('naturalWidth', 96);
});

test('background uploads reject invalid files and cannot attach to a changed selection or overwrite unrelated appearance edits', async ({ page, request }) => {
  const f = await fixture(page, request); await tile(page, f.node.id).locator('.ms-node-title').click(); await appearance(page);
  const upload = page.getByLabel('Upload background image', { exact: true });
  await upload.setInputFiles({ name: 'Unsafe.svg', mimeType: 'image/svg+xml', buffer: Buffer.from('<svg xmlns="http://www.w3.org/2000/svg"/>') });
  await expect(page.locator('.background-upload-error')).toContainText('Choose a PNG'); expect((await stored(request, f.project.id)).assets).toHaveLength(1);
  await upload.setInputFiles({ name: 'Corrupt.png', mimeType: 'image/png', buffer: Buffer.from('not an image') }); await expect(page.locator('.background-upload-error')).toContainText('valid image file');
  await upload.setInputFiles({ name: 'Too large.png', mimeType: 'image/png', buffer: Buffer.alloc(16 * 1024 * 1024 + 1) }); await expect(page.locator('.background-upload-error')).toContainText('16 MiB');
  const delay = () => page.evaluate(() => {
    const original = File.prototype.arrayBuffer;
    File.prototype.arrayBuffer = async function () { const file = this; File.prototype.arrayBuffer = original; await new Promise<void>(resolve => { (window as Window & { releaseBackgroundRead?: () => void }).releaseBackgroundRead = resolve; }); return original.call(file); };
  });
  const release = () => page.evaluate(() => (window as Window & { releaseBackgroundRead?: () => void }).releaseBackgroundRead?.());
  await delay(); await upload.setInputFiles({ name: 'Preserved style.png', mimeType: 'image/png', buffer: f.uploaded });
  await page.getByLabel('Body size', { exact: true }).fill('19'); await page.getByLabel('Body size', { exact: true }).press('Tab'); await release();
  await expect(page.locator('.background-image-name')).toHaveText('Preserved style.png');
  await expect.poll(async () => (await stored(request, f.project.id)).nodes.find(node => node.id === f.node.id)?.style?.fontSize).toBe(19);
  const before = await stored(request, f.project.id); await delay(); await page.getByLabel('Upload background image', { exact: true }).setInputFiles({ name: 'Replaced background.png', mimeType: 'image/png', buffer: f.original });
  await page.getByLabel('Background preset', { exact: true }).selectOption('grid-fine'); await release(); await expect(page.getByRole('alert').filter({ hasText: 'background changed' })).toContainText('background changed');
  expect((await stored(request, f.project.id)).assets).toEqual(before.assets);
  await delay(); await page.getByLabel('Upload background image', { exact: true }).setInputFiles({ name: 'Late selection.png', mimeType: 'image/png', buffer: f.original });
  await tile(page, f.other.id).locator('.ms-node-title').click(); await release(); await expect(page.getByRole('alert').filter({ hasText: 'selected canvas or node changed' })).toContainText('selected canvas or node changed');
  expect((await stored(request, f.project.id)).assets).toEqual(before.assets); expect((await stored(request, f.project.id)).nodes.find(node => node.id === f.other.id)?.style?.backgroundAssetId).toBeUndefined();
});

