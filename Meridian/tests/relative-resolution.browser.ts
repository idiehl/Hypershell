import { test, expect, type APIRequestContext, type Locator, type Page } from '@playwright/test';
import { createHash } from 'node:crypto';
import { readFile, writeFile } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';
import { addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import type { MeridianAsset, MeridianNode, MeridianProject } from '../src/core/types.ts';
import { buildPublication } from '../src/publication.ts';
import { inspectorPanel, interactionTool, selectEditorNode } from './ui-helpers.ts';

const sourceWidth = 1024, sourceHeight = 768;
const tile = (page: Page, id: string) => page.locator(`.ms-node[data-node-id="${id}"]`);
const stored = async (request: APIRequestContext, id: string): Promise<MeridianProject> => (await request.get(`/api/projects/${id}`)).json();
const fingerprints = (assets: readonly MeridianAsset[]) => assets.map(asset => ({ id: asset.id, name: asset.name, mime: asset.mime,
  bytes: Buffer.from(asset.base64, 'base64').length, hash: createHash('sha256').update(Buffer.from(asset.base64, 'base64')).digest('hex') })).sort((a, b) => a.id.localeCompare(b.id));

async function makeSource(page: Page): Promise<MeridianAsset> {
  await page.setContent('<main>Image resolution fixture</main>');
  const base64 = await page.evaluate(({ width, height }) => {
    const canvas = document.createElement('canvas'); canvas.width = width; canvas.height = height;
    const context = canvas.getContext('2d')!;
    context.fillStyle = '#eef4f4'; context.fillRect(0, 0, width, height);
    for (let x = 0; x < width; x++) {
      context.fillStyle = x % 2 ? '#151b24' : '#ffffff'; context.fillRect(x, 0, 1, height / 2);
      context.fillStyle = `hsl(${x % 360}, 75%, 48%)`; context.fillRect(x, height / 2, 1, height / 2);
    }
    context.fillStyle = '#ffffff'; context.fillRect(80, 280, 864, 200);
    context.fillStyle = '#102b28'; context.font = 'bold 92px sans-serif'; context.textAlign = 'center'; context.fillText('SOURCE 1024', width / 2, 405);
    return canvas.toDataURL('image/png').split(',')[1]!;
  }, { width: sourceWidth, height: sourceHeight });
  return { id: crypto.randomUUID(), name: 'Source detail.png', mime: 'image/png', base64 };
}

async function install(page: Page, request: APIRequestContext, project: MeridianProject) {
  project.canvases[0]!.snapping = { stickyEdges: false, smartAlign: false, snapToGrid: false };
  const response = await request.post('/api/projects', { data: commitSnapshots(project) });
  expect(response.ok(), await response.text()).toBeTruthy(); const saved: MeridianProject = await response.json();
  await page.goto(`/?project=${saved.id}`); await expect(page.locator('.save-state')).toHaveText(`Saved · r${saved.revision}`);
  await interactionTool(page); return saved;
}

async function original(image: Locator, asset: MeridianAsset) {
  await expect.poll(() => image.evaluate(element => (element as HTMLImageElement).naturalWidth)).toBe(sourceWidth);
  expect(await image.evaluate(element => ({ width: (element as HTMLImageElement).naturalWidth, height: (element as HTMLImageElement).naturalHeight,
    currentSrc: (element as HTMLImageElement).currentSrc }))).toEqual({ width: sourceWidth, height: sourceHeight, currentSrc: `data:${asset.mime};base64,${asset.base64}` });
}

async function settledPaint(image: Locator, asset: MeridianAsset) {
  await expect(image).toHaveClass(/ms-resolution-source/);
  await expect.poll(() => image.evaluate((element, source) => {
    const frame = element.getBoundingClientRect();
    return [...document.querySelectorAll<HTMLImageElement>('.ms-resolution-image')].some(paint => {
      const rendered = paint.getBoundingClientRect();
      return paint.currentSrc === source && paint.naturalWidth === 1024 && paint.naturalHeight === 768 && getComputedStyle(paint).display !== 'none' &&
        ['x', 'y', 'width', 'height'].every(key => Math.abs((frame[key as keyof DOMRect] as number) - (rendered[key as keyof DOMRect] as number)) < .5);
    });
  }, `data:${asset.mime};base64,${asset.base64}`)).toBe(true);
}

async function geometry(page: Page, id: string) {
  return tile(page, id).evaluate(element => { const node = element as HTMLElement; return {
    x: parseFloat(node.style.left), y: parseFloat(node.style.top), width: parseFloat(node.style.width), height: parseFloat(node.style.height) }; });
}

async function camera(page: Page) {
  return page.locator('.ms-world').evaluate(element => {
    const world = element as HTMLElement, viewport = world.parentElement!, scale = parseFloat(world.style.getPropertyValue('--ms-camera-scale'));
    const originX = -parseFloat(world.style.getPropertyValue('--ms-render-origin-x')), originY = -parseFloat(world.style.getPropertyValue('--ms-render-origin-y'));
    return { x: viewport.clientWidth / 2 - originX * scale, y: viewport.clientHeight / 2 - originY * scale, scale };
  });
}

function picture(project: MeridianProject, asset: MeridianAsset, title: string, properties: Partial<MeridianNode> = {}) {
  const node = addNode(project, project.entryCanvasId, 'media', title);
  Object.assign(node, { width: 220, height: 180, sizeMode: 'fixed', contentType: 'asset', assetId: asset.id,
    style: { padding: 0, contentGap: 0, borderWidth: 0, imageFit: 'contain' }, ...properties });
  return node;
}

test('image sources retain full resolution through tiny-frame focus, node resizing, camera zoom and editor reload', async ({ page, request }, info) => {
  const asset = await makeSource(page), project = newProject('Relative image detail'); project.assets.push(asset);
  const node = picture(project, asset, 'Fine detail', { x: 200000, y: 100000, width: 3, height: 3 });
  const initial = await install(page, request, project), expectedAssets = fingerprints(initial.assets);
  await selectEditorNode(page, node.id, node.title); await inspectorPanel(page, 'appearance');
  const image = tile(page, node.id).locator('img.ms-image'); await original(image, asset);
  const beforeCamera = await camera(page), beforeFrame = (await image.boundingBox())!;
  expect(beforeCamera.scale).toBeGreaterThan(100); expect(beforeFrame.width).toBeGreaterThan(100);
  for (const [label, value] of [['Width', '8'], ['Height', '6']]) {
    const field = page.locator('.inspector').getByLabel(label, { exact: true }); await field.fill(value); await field.press('Tab');
  }
  await expect.poll(async () => { const saved = await stored(request, initial.id), current = saved.nodes.find(item => item.id === node.id)!; return [current.width, current.height]; }).toEqual([8, 6]);
  await original(image, asset); expect(await camera(page)).toEqual(beforeCamera);
  const resized = (await image.boundingBox())!; expect(resized.width).toBeGreaterThan(beforeFrame.width * 1.5);
  const authored = await geometry(page, node.id), beforeZoom = await stored(request, initial.id);
  await page.getByRole('button', { name: 'Zoom out', exact: true }).click();
  const zoomed = (await image.boundingBox())!; expect(zoomed.width).toBeCloseTo(resized.width * .8, 1);
  await page.getByRole('button', { name: 'Zoom in', exact: true }).click();
  await original(image, asset); expect(await geometry(page, node.id)).toEqual(authored);
  const afterZoom = await stored(request, initial.id); expect(afterZoom).toEqual(beforeZoom); expect(fingerprints(afterZoom.assets)).toEqual(expectedAssets);
  await page.getByRole('button', { name: 'Fit selected branch', exact: true }).click(); await settledPaint(image, asset);
  await page.screenshot({ path: info.outputPath('resized-source-detail.png') });
  await page.reload(); await selectEditorNode(page, node.id, node.title); await original(image, asset);
  expect(await geometry(page, node.id)).toEqual(authored); expect(fingerprints((await stored(request, initial.id)).assets)).toEqual(expectedAssets);
});

test('bounded cover disclosure and nested Gallery paging preserve source pixels, frame geometry and camera', async ({ page, request }, info) => {
  const asset = await makeSource(page), project = newProject('Image disclosure and paging'); project.assets.push(asset);
  const parent = addNode(project, project.entryCanvasId, 'container', 'Image collection');
  Object.assign(parent, { x: 60, y: 60, width: 540, height: 450, sizeMode: 'fixed', collapsed: true, containerVariant: 'vertical', childLayout: 'contained',
    contentType: 'asset', assetId: asset.id, grid: { columns: 1, rows: 1, cellWidth: 520, cellHeight: 380, gap: 0, showHeaders: false } });
  const child = picture(project, asset, 'Internal detail', { parentId: parent.id, childPosition: 'auto' });
  const gallery = addNode(project, project.entryCanvasId, 'container', 'Gallery collection');
  Object.assign(gallery, { x: 740, y: 80, width: 300, height: 380, sizeMode: 'fixed', collapsed: false, containerVariant: 'gallery' });
  const first = picture(project, asset, 'First view', { parentId: gallery.id });
  const nested = addNode(project, project.entryCanvasId, 'container', 'Nested images');
  Object.assign(nested, { parentId: gallery.id, width: 300, height: 380, sizeMode: 'fixed', collapsed: false, containerVariant: 'gallery' });
  const second = picture(project, asset, 'Nested first view', { parentId: nested.id }), third = picture(project, asset, 'Nested second view', { parentId: nested.id });
  const initial = await install(page, request, project), expectedAssets = fingerprints(initial.assets);
  await selectEditorNode(page, parent.id, parent.title); const parentGeometry = await geometry(page, parent.id), before = await camera(page);
  await original(tile(page, parent.id).locator('img.ms-image'), asset);
  await tile(page, parent.id).locator(':scope > .ms-node-header > .ms-node-title').dblclick();
  await expect(tile(page, parent.id).locator(':scope > .ms-node-body img.ms-image')).toHaveCount(0);
  await original(tile(page, child.id).locator('img.ms-image'), asset);
  expect(await geometry(page, parent.id)).toEqual({ x: parent.x, y: parent.y, width: parent.width, height: parent.height });
  expect(await camera(page)).toEqual(before);
  const frame = (await tile(page, parent.id).boundingBox())!, internal = (await tile(page, child.id).locator('img.ms-image').boundingBox())!;
  expect(internal.x).toBeGreaterThanOrEqual(frame.x - 1); expect(internal.y).toBeGreaterThanOrEqual(frame.y - 1);
  expect(internal.x + internal.width).toBeLessThanOrEqual(frame.x + frame.width + 1); expect(internal.y + internal.height).toBeLessThanOrEqual(frame.y + frame.height + 1);
  await tile(page, parent.id).locator(':scope > .ms-node-header > .ms-node-title').dblclick(); await original(tile(page, parent.id).locator('img.ms-image'), asset);
  expect(await geometry(page, parent.id)).toEqual(parentGeometry);
  await expect(page.locator('.save-state')).toHaveText(/^Saved · r\d+$/);
  await selectEditorNode(page, gallery.id, gallery.title); const galleryGeometry = await geometry(page, gallery.id), galleryCamera = await camera(page);
  await original(tile(page, gallery.id).locator(`[data-gallery-item-id="${first.id}"] img.ms-image`), asset);
  const beforePaging = await stored(request, initial.id);
  await page.getByRole('button', { name: `Next object in ${gallery.title}`, exact: true }).click();
  await original(tile(page, gallery.id).locator(`[data-gallery-item-id="${second.id}"] img.ms-image`), asset);
  await page.getByRole('button', { name: `Next object in ${nested.title}`, exact: true }).click();
  await original(tile(page, gallery.id).locator(`[data-gallery-item-id="${third.id}"] img.ms-image`), asset);
  await page.getByRole('button', { name: `Previous object in ${nested.title}`, exact: true }).click();
  await original(tile(page, gallery.id).locator(`[data-gallery-item-id="${second.id}"] img.ms-image`), asset);
  expect(await geometry(page, gallery.id)).toEqual(galleryGeometry); expect(await camera(page)).toEqual(galleryCamera);
  await page.getByRole('button', { name: `Previous object in ${gallery.title}`, exact: true }).click();
  await original(tile(page, gallery.id).locator(`[data-gallery-item-id="${first.id}"] img.ms-image`), asset);
  expect(await stored(request, initial.id)).toEqual(beforePaging); expect(fingerprints(beforePaging.assets)).toEqual(expectedAssets);
  await settledPaint(tile(page, gallery.id).locator(`[data-gallery-item-id="${first.id}"] img.ms-image`), asset);
  await page.screenshot({ path: info.outputPath('gallery-original-source.png') });
});

test('Window browsing, reload and offline publication retain original image files at high zoom and in media windows', async ({ page, request, browser }, info) => {
  const asset = await makeSource(page), project = newProject('Portable image resolution'); project.assets.push(asset);
  const parent = addNode(project, project.entryCanvasId, 'container', 'Image window');
  Object.assign(parent, { x: 70, y: 70, sizeMode: 'auto', collapsed: false, containerVariant: 'window', childLayout: 'contained',
    grid: { columns: 1, rows: 2, cellWidth: 300, cellHeight: 260, gap: 0, showHeaders: false, windowColumns: 1, windowRows: 1, scrollDirection: 'vertical' } });
  const first = picture(project, asset, 'First row', { parentId: parent.id, childPosition: 'auto', cell: { column: 1, row: 1 } });
  const second = picture(project, asset, 'Second row', { parentId: parent.id, childPosition: 'auto', cell: { column: 1, row: 2 } });
  const initial = await install(page, request, project), expectedAssets = fingerprints(initial.assets);
  const pane = page.locator(`.ms-window-viewport[data-window-id="${parent.id}"]`); await expect(pane).toBeVisible();
  await original(tile(page, first.id).locator('img.ms-image'), asset);
  const before = await camera(page), expectedGeometry = await Promise.all([parent, first, second].map(async node => [node.id, await geometry(page, node.id)] as const));
  await pane.evaluate(element => { element.scrollTop = element.scrollHeight; }); await expect.poll(() => pane.evaluate(element => element.scrollTop)).toBeGreaterThan(0);
  await original(tile(page, second.id).locator('img.ms-image'), asset); expect(await camera(page)).toEqual(before);
  await page.getByRole('button', { name: 'Zoom in', exact: true }).click(); await original(tile(page, second.id).locator('img.ms-image'), asset);
  for (const [id, bounds] of expectedGeometry) expect(await geometry(page, id)).toEqual(bounds);
  expect(await stored(request, initial.id)).toEqual(initial);
  await page.reload(); await original(tile(page, first.id).locator('img.ms-image'), asset);
  const saved = await stored(request, initial.id); expect(fingerprints(saved.assets)).toEqual(expectedAssets);
  const filename = info.outputPath('original-image-offline.html');
  await writeFile(filename, buildPublication(saved, await readFile('dist/viewer.js', 'utf8'), await readFile('dist/scene.css', 'utf8')));
  const context = await browser.newContext({ offline: true, deviceScaleFactor: 2, viewport: { width: 1500, height: 950 } });
  try {
    const viewer = await context.newPage(), requests: string[] = []; context.on('request', request => { if (/^https?:/.test(request.url())) requests.push(request.url()); });
    await viewer.goto(pathToFileURL(filename).href); const data = (await viewer.locator('#meridian-data').textContent())!, payload = JSON.parse(data);
    expect(fingerprints(payload.project.assets)).toEqual(expectedAssets);
    const image = tile(viewer, first.id).locator('img.ms-image'); await original(image, asset);
    for (const [id, bounds] of expectedGeometry) expect(await geometry(viewer, id)).toEqual(bounds);
    for (let at = 0; at < 5; at++) await viewer.getByRole('button', { name: 'Zoom in', exact: true }).click();
    await original(image, asset); expect(await viewer.locator('#meridian-data').textContent()).toBe(data);
    await viewer.getByRole('button', { name: 'Fit Work', exact: true }).click(); await settledPaint(image, asset);
    await viewer.screenshot({ path: info.outputPath('offline-original-image.png') });
    const [popup] = await Promise.all([context.waitForEvent('page'), image.click({ modifiers: ['Control'] })]);
    const opened = popup.getByRole('img', { name: asset.name, exact: true });
    await expect.poll(() => opened.evaluate(element => (element as HTMLImageElement).naturalWidth)).toBe(sourceWidth);
    expect(await opened.evaluate(element => ({ width: (element as HTMLImageElement).naturalWidth, height: (element as HTMLImageElement).naturalHeight })))
      .toEqual({ width: sourceWidth, height: sourceHeight });
    await expect(popup.getByRole('status')).toContainText('1024 × 768 pixels');
    const originalBytes = await popup.locator('a[download]').evaluate(async element => {
      const response = await fetch((element as HTMLAnchorElement).href); return [...new Uint8Array(await response.arrayBuffer())];
    });
    expect(createHash('sha256').update(Buffer.from(originalBytes)).digest('hex')).toBe(expectedAssets[0]!.hash);
    await popup.close();
    expect(await viewer.locator('#meridian-data').textContent()).toBe(data); expect(requests).toEqual([]);
  } finally { await context.close(); }
});
