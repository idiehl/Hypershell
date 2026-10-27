import { test, expect, type APIRequestContext, type Page } from '@playwright/test';
import { readFile, writeFile } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';
import { addCanvas, addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import type { MeridianProject } from '../src/core/types.ts';
import { buildPublication } from '../src/publication.ts';
import { inspectorPanel, selectEditorNode } from './ui-helpers.ts';
import { arrangeChildren } from '../src/core/grid.ts';

const tile = (page: Page, id: string) => page.locator(`.ms-node[data-node-id="${id}"]`);
const stored = async (request: APIRequestContext, id: string): Promise<MeridianProject> => (await request.get(`/api/projects/${id}`)).json();
async function seed(page: Page, request: APIRequestContext, project: MeridianProject) {
  const response = await request.post('/api/projects', { data: commitSnapshots(project) });
  expect(response.ok(), await response.text()).toBeTruthy();
  const saved: MeridianProject = await response.json(); await page.goto(`/?project=${saved.id}`);
  await expect(page.locator('.save-state')).toHaveText(`Saved · r${saved.revision}`); return saved;
}
async function geometry(page: Page, id: string) {
  return tile(page, id).evaluate(element => {
    const style = (element as HTMLElement).style;
    return { x: parseFloat(style.left), y: parseFloat(style.top), width: parseFloat(style.width), height: parseFloat(style.height) };
  });
}
async function withinParent(page: Page, parentId: string, childIds: string[]) {
  const parent = (await tile(page, parentId).boundingBox())!;
  for (const id of childIds) {
    const child = (await tile(page, id).boundingBox())!;
    expect(child.x, `${id} left`).toBeGreaterThanOrEqual(parent.x - 2);
    expect(child.y, `${id} top`).toBeGreaterThanOrEqual(parent.y - 2);
    expect(child.x + child.width, `${id} right`).toBeLessThanOrEqual(parent.x + parent.width + 2);
    expect(child.y + child.height, `${id} bottom`).toBeLessThanOrEqual(parent.y + parent.height + 2);
  }
}

test('oversized legacy containers fit their own collapsed content and every enclosing subtype keeps the same children inside', async ({ page, request }, info) => {
  const project = newProject('Container disclosure and conversion QA'), canvasId = project.entryCanvasId;
  const parent = addNode(project, canvasId, 'container', 'My canvases');
  Object.assign(parent, { x: 100, y: 100, width: 1074, height: 892, childLayout: 'spatial', containerVariant: 'spatial', collapsed: true,
    grid: { columns: 3, rows: 3, cellWidth: 200, cellHeight: 120, gap: 0, showHeaders: true } });
  delete parent.sizeMode; // Reproduce a saved tile created before automatic sizing existed.
  const first = addNode(project, canvasId, 'text', 'First collection');
  Object.assign(first, { parentId: parent.id, x: -360, y: 300, text: 'A collection with hidden supporting detail.', collapsed: true, cell: { column: 1, row: 1, name: 'First' } });
  const second = addNode(project, canvasId, 'text', 'Second collection');
  Object.assign(second, { parentId: parent.id, x: 1400, y: 340, text: 'A separate branch retains its own identity.', cell: { column: 2, row: 1, name: 'Second' } });
  const hidden = addNode(project, canvasId, 'text', 'Hidden detail'); Object.assign(hidden, { parentId: first.id, x: -320, y: 500, text: 'Only the child controls its own disclosure.' });
  const saved = await seed(page, request, project), identities = saved.nodes.map(node => node.id).sort();
  await expect(tile(page, first.id)).toHaveCount(0); await expect(tile(page, second.id)).toHaveCount(0);
  const compact = await geometry(page, parent.id); expect(compact.height).toBeLessThan(180); expect(compact.width).toBeLessThan(400);
  await selectEditorNode(page, parent.id, parent.title); await inspectorPanel(page, 'relations');
  await page.getByRole('button', { name: 'Toggle contents from relations', exact: true }).click();
  await expect(tile(page, first.id)).toHaveCount(1); await expect(tile(page, second.id)).toHaveCount(1);
  expect((await geometry(page, parent.id)).height).toBeGreaterThanOrEqual(parent.height); await expect(tile(page, hidden.id)).toHaveCount(0);
  for (const variant of ['contained', 'horizontal', 'vertical', 'grid', 'spreadsheet', 'window', 'gallery', 'contained']) {
    await inspectorPanel(page, 'properties');
    await page.getByRole('combobox', { name: 'Display Pattern', exact: true }).selectOption(variant);
    await expect.poll(async () => (await stored(request, saved.id)).nodes.find(node => node.id === parent.id)?.containerVariant).toBe(variant);
    if (variant === 'gallery') {
      await expect(page.locator(`.ms-gallery[data-gallery-id="${parent.id}"] .ms-gallery-item`)).toHaveCount(1);
      await expect(tile(page, first.id)).toHaveCount(0); await expect(tile(page, second.id)).toHaveCount(0);
      expect((await geometry(page, parent.id)).height).toBeLessThan(350);
    } else {
      await expect(tile(page, first.id)).toHaveCount(1); await expect(tile(page, second.id)).toHaveCount(1);
      await withinParent(page, parent.id, [first.id, second.id]);
      if (variant === 'window') await expect(tile(page, parent.id).locator('.ms-window-viewport')).toHaveCount(1);
      if (variant === 'grid') await expect(tile(page, parent.id).locator('.ms-empty-cell')).toHaveCount(0);
      if (variant === 'spreadsheet') {
        // Spreadsheet capacity is explicit and empty cells remain editable.
        await inspectorPanel(page, 'relations'); const rows = page.getByLabel('Rows', { exact: true }); await rows.fill('3'); await rows.press('Tab');
        await expect.poll(async () => (await stored(request, saved.id)).nodes.find(node => node.id === parent.id)?.grid?.rows).toBe(3);
        expect(await tile(page, parent.id).locator('.ms-empty-cell').count()).toBeGreaterThan(0);
      }
    }
    await expect(tile(page, hidden.id)).toHaveCount(0);
    const current = await stored(request, saved.id);
    expect(current.nodes.map(node => node.id).sort()).toEqual(identities);
    expect(current.nodes.find(node => node.id === first.id)?.cell?.name).toBe(first.cell!.name);
    expect(current.nodes.find(node => node.id === second.id)?.cell?.name).toBe(second.cell!.name);
    expect(current.nodes.find(node => node.id === first.id)?.collapsed).toBe(true);
  }
  await inspectorPanel(page, 'relations'); await page.getByRole('button', { name: 'Toggle contents from relations', exact: true }).click();
  await expect(tile(page, first.id)).toHaveCount(0); expect((await geometry(page, parent.id)).height).toBeLessThan(180);
  await page.getByRole('button', { name: 'Toggle contents from relations', exact: true }).click();
  await withinParent(page, parent.id, [first.id, second.id]); await expect(tile(page, hidden.id)).toHaveCount(0);
  await page.screenshot({ path: info.outputPath('container-fitted-contained-children.png') });
});

test('Gallery fits the current object, keeps the camera still during paging, and renders the same dimensions offline', async ({ page, request, browser }, info) => {
  await page.goto('/__meridian_status');
  const imageBytes = await page.evaluate(() => {
    const canvas = document.createElement('canvas'); canvas.width = 320; canvas.height = 180;
    const context = canvas.getContext('2d')!; context.fillStyle = '#335f68'; context.fillRect(0, 0, 320, 180);
    context.fillStyle = '#8cdbc8'; context.fillRect(36, 25, 248, 130); return canvas.toDataURL('image/png').split(',')[1]!;
  });
  const project = newProject('Gallery minimum frame QA'), canvasId = project.entryCanvasId, destination = addCanvas(project, 'Destination');
  const parent = addNode(project, canvasId, 'container', 'Relevant objects');
  Object.assign(parent, { x: 100, y: 100, width: 1074, height: 892, childLayout: 'contained', containerVariant: 'gallery', collapsed: false });
  const link = addNode(project, canvasId, 'canvas-link', 'A canvas'); Object.assign(link, { parentId: parent.id, targetId: destination.rootId });
  const image = addNode(project, canvasId, 'image', 'A landscape image'); Object.assign(image, { parentId: parent.id, assetId: 'sizing-image', width: 350, text: 'The caption and navigation fit the image.' });
  project.assets.push({ id: 'sizing-image', name: 'landscape.png', mime: 'image/png', base64: imageBytes });
  const saved = await seed(page, request, project), view = page.locator(`.ms-gallery[data-gallery-id="${parent.id}"]`);
  const writes: string[] = []; page.on('request', request => { if (request.method() !== 'GET' && request.url().includes(`/api/projects/${saved.id}`)) writes.push(request.url()); });
  const camera = await page.locator('.ms-world').evaluate(element => (element as HTMLElement).style.transform), short = await geometry(page, parent.id);
  expect(short.height).toBeLessThan(220); await expect(view.locator('.ms-gallery-item')).toHaveAttribute('data-gallery-item-id', link.id);
  await expect(view.locator('.ms-gallery-navigation button')).toHaveCount(2);
  await expect(view.getByRole('button', { name: /^Inspect current object:/ })).toHaveCount(0);
  const bottomPadding = async () => tile(page, parent.id).evaluate(element => {
    const scale = new DOMMatrix(getComputedStyle(document.querySelector('.ms-world')!).transform).a;
    const frame = element.getBoundingClientRect(), navigation = element.querySelector('.ms-gallery-navigation')!.getBoundingClientRect();
    const style = getComputedStyle(element), border = parseFloat(style.borderBottomWidth) || parseFloat(style.getPropertyValue('--ms-border-width')) || 1;
    return (frame.bottom - navigation.bottom) / scale - border;
  });
  expect(await bottomPadding()).toBeGreaterThanOrEqual(4.9); expect(await bottomPadding()).toBeLessThan(6.1);
  const controlsFit = await tile(page, parent.id).evaluate(element => {
    const frame = element.getBoundingClientRect();
    return [...element.querySelectorAll('.ms-gallery-navigation button,.ms-gallery-position')].every(control => {
      const bounds = control.getBoundingClientRect();
      return bounds.left >= frame.left && bounds.right <= frame.right && bounds.bottom <= frame.bottom;
    });
  });
  expect(controlsFit).toBe(true);
  await page.screenshot({ path: info.outputPath('gallery-fitted-canvas-object.png') });
  await view.getByRole('button', { name: `Next object in ${parent.title}`, exact: true }).click();
  await expect(view.locator('.ms-gallery-item')).toHaveAttribute('data-gallery-item-id', image.id); await expect(view.locator('img')).toHaveJSProperty('naturalWidth', 320);
  const imageFrame = await geometry(page, parent.id); expect(imageFrame.height).toBeGreaterThan(short.height + 60); expect(imageFrame.height).toBeLessThan(500);
  expect(await bottomPadding()).toBeGreaterThanOrEqual(4.9); expect(await bottomPadding()).toBeLessThan(6.1);
  expect(await page.locator('.ms-world').evaluate(element => (element as HTMLElement).style.transform)).toBe(camera);
  await expect(tile(page, link.id)).toHaveCount(0); await expect(tile(page, image.id)).toHaveCount(0);
  await page.screenshot({ path: info.outputPath('gallery-fitted-image-object.png') });
  await view.getByRole('button', { name: `Previous object in ${parent.title}`, exact: true }).click(); expect(await geometry(page, parent.id)).toEqual(short);
  await page.waitForTimeout(2200); expect(writes).toEqual([]); expect(await stored(request, saved.id)).toEqual(saved);
  const offlinePath = info.outputPath('container-sizing-offline.html');
  await writeFile(offlinePath, buildPublication(saved, await readFile('dist/viewer.js', 'utf8'), await readFile('dist/scene.css', 'utf8')));
  const context = await browser.newContext({ offline: true, viewport: { width: 1500, height: 950 } });
  try {
    const viewer = await context.newPage(); await viewer.goto(pathToFileURL(offlinePath).href);
    const offlineView = viewer.locator(`.ms-gallery[data-gallery-id="${parent.id}"]`), payload = await viewer.locator('#meridian-data').textContent();
    expect(await geometry(viewer, parent.id)).toEqual(short);
    await offlineView.getByRole('button', { name: `Next object in ${parent.title}`, exact: true }).click();
    await expect(offlineView.locator('img')).toHaveJSProperty('naturalWidth', 320); expect(await geometry(viewer, parent.id)).toEqual(imageFrame);
    expect(await viewer.locator('#meridian-data').textContent()).toBe(payload);
    await viewer.screenshot({ path: info.outputPath('gallery-fitted-offline-image.png') });
  } finally { await context.close(); }
});

test('Automatic typography remains inside its tile and Appearance dimensions or edge resizing create a persistent fixed size', async ({ page, request }, info) => {
  const project = newProject('Automatic and manual dimensions QA');
  const node = addNode(project, project.entryCanvasId, 'text', 'A long title that needs room for every word');
  Object.assign(node, { x: 80, y: 80, width: 300, height: 40, text: 'A readable canvas keeps every part of this passage visible. '.repeat(6),
    style: { fontSize: 24, titleSize: 32 } });
  const saved = await seed(page, request, project);
  const contentFits = async () => {
    const measurements = await tile(page, node.id).evaluate(element => {
      const parent = element.getBoundingClientRect(), title = element.querySelector('.ms-node-title') as HTMLElement, text = element.querySelector('.ms-text') as HTMLElement;
      return [title, text].map(part => { const bounds = part.getBoundingClientRect(); return { horizontal: part.scrollWidth - part.clientWidth, vertical: part.scrollHeight - part.clientHeight,
        inside: bounds.left >= parent.left - 2 && bounds.right <= parent.right + 2 && bounds.top >= parent.top - 2 && bounds.bottom <= parent.bottom + 2 }; });
    });
    for (const part of measurements) { expect(part.horizontal).toBeLessThanOrEqual(2); expect(part.vertical).toBeLessThanOrEqual(2); expect(part.inside).toBe(true); }
  };
  await contentFits(); expect((await geometry(page, node.id)).height).toBeGreaterThan(250);
  await selectEditorNode(page, node.id, node.title); await inspectorPanel(page, 'appearance');
  await expect(page.getByRole('combobox', { name: 'Sizing', exact: true })).toHaveValue('auto');
  for (const [label, value] of [['Width', '640'], ['Height', '500']]) { const field = page.locator('.inspector').getByLabel(label, { exact: true }); await field.fill(value); await field.press('Tab'); }
  await expect(page.getByRole('combobox', { name: 'Sizing', exact: true })).toHaveValue('fixed');
  await expect.poll(async () => (await stored(request, saved.id)).nodes.find(item => item.id === node.id)?.height).toBe(500);
  expect((await stored(request, saved.id)).nodes.find(item => item.id === node.id)?.width).toBe(640);
  await page.getByRole('button', { name: 'Fit selected branch', exact: true }).click();
  await expect(tile(page, node.id)).toHaveAttribute('aria-selected', 'true');
  const beforeDrag = (await tile(page, node.id).boundingBox())!;
  // The hit surface occupies the backdrop interior. Stay inside its real edge
  // rather than clicking the antialiased outer border and panning the background.
  const edge = { x: beforeDrag.x + beforeDrag.width - 4, y: beforeDrag.y + beforeDrag.height / 2 };
  await page.mouse.move(edge.x, edge.y); await expect(tile(page, node.id)).toHaveCSS('cursor', 'ew-resize');
  expect(await page.evaluate(({ x, y }) => document.elementFromPoint(x, y)?.closest('[data-node-id]')?.getAttribute('data-node-id'), edge)).toBe(node.id);
  await page.mouse.down(); await page.mouse.move(edge.x + 40, edge.y, { steps: 10 }); await page.mouse.up();
  await expect.poll(async () => (await stored(request, saved.id)).nodes.find(item => item.id === node.id)?.width ?? 0).toBeGreaterThan(640);
  const fixed = (await stored(request, saved.id)).nodes.find(item => item.id === node.id)!; expect(fixed.sizeMode).toBe('fixed');
  await page.reload(); await expect(tile(page, node.id)).toHaveCount(1);
  const restored = await geometry(page, node.id); expect(restored.width).toBeCloseTo(fixed.width, 2); expect(restored.height).toBeCloseTo(fixed.height, 2);
  await selectEditorNode(page, node.id, node.title); await inspectorPanel(page, 'appearance');
  await page.getByRole('combobox', { name: 'Sizing', exact: true }).selectOption('auto'); await contentFits();
  expect((await geometry(page, node.id)).width).toBeLessThanOrEqual(420);
  await page.screenshot({ path: info.outputPath('automatic-large-typography.png') });
});

test('automatic Grid and Spreadsheet preserve their collapsed cover and hide it above expanded child tiles without coordinate badges', async ({ page, request }, info) => {
  const project = newProject('Grid content reservation QA'), canvasId = project.entryCanvasId;
  const parent = addNode(project, canvasId, 'container', 'A long collection title that must remain readable above its grid');
  Object.assign(parent, { x: 100, y: 100, width: 300, height: 892, childLayout: 'contained', containerVariant: 'grid', childArrangement: 'grid', collapsed: true,
    text: 'The collection retains its cover explanation while collapsed. '.repeat(3), style: { titleSize: 32, fontSize: 24 },
    grid: { columns: 2, rows: 2, cellWidth: 240, cellHeight: 180, gap: 12, showHeaders: true } });
  const child = addNode(project, canvasId, 'text', 'An addressed entry');
  Object.assign(child, { parentId: parent.id, cell: { column: 1, row: 1, name: 'entry' }, width: 240,
    text: 'Every line remains visible without a coordinate footer. '.repeat(2), style: { fontSize: 20 } });
  Object.assign(project, arrangeChildren(project, parent.id, 'grid').project);
  const saved = await seed(page, request, project), identities = saved.nodes.map(node => node.id).sort();
  const cover = () => tile(page, parent.id).locator(':scope > .ms-node-body .ms-text');
  await expect(cover()).toHaveText(parent.text!); await expect(tile(page, child.id)).toHaveCount(0);
  for (const variant of ['grid', 'spreadsheet']) {
    await selectEditorNode(page, parent.id, parent.title);
    if (variant === 'spreadsheet') {
      await inspectorPanel(page, 'properties');
      await page.getByRole('combobox', { name: 'Display Pattern', exact: true }).selectOption(variant);
    }
    await inspectorPanel(page, 'relations');
    await page.getByRole('button', { name: 'Toggle contents from relations', exact: true }).click();
    await expect.poll(async () => (await stored(request, saved.id)).nodes.find(node => node.id === parent.id)?.collapsed).toBe(false);
    await expect(tile(page, child.id)).toHaveCount(1); await expect(cover()).toHaveCount(0);
    await expect(tile(page, parent.id).locator('.ms-contained-summary')).toHaveCount(0);
    const own = await tile(page, parent.id).evaluate(element => {
      const title = element.querySelector('.ms-node-title') as HTMLElement;
      return { clippedTitle: title.scrollHeight > title.clientHeight + 1, titleBottom: title.getBoundingClientRect().bottom };
    });
    expect(own.clippedTitle).toBe(false); expect(own.titleBottom).toBeLessThan((await tile(page, child.id).boundingBox())!.y);
    await expect(tile(page, child.id).locator('.ms-cell-address')).toHaveCount(0);
    await expect(tile(page, parent.id).locator('.ms-grid-header')).toHaveCount(0);
    const fits = await tile(page, child.id).evaluate(element => {
      const text = element.querySelector('.ms-text') as HTMLElement, frame = element.getBoundingClientRect();
      return text.scrollHeight <= text.clientHeight + 1 && text.getBoundingClientRect().bottom <= frame.bottom;
    });
    expect(fits).toBe(true);
    await withinParent(page, parent.id, [child.id]);
    const expanded = await stored(request, saved.id);
    expect(expanded.nodes.map(node => node.id).sort()).toEqual(identities);
    expect(expanded.nodes.find(node => node.id === parent.id)?.text).toBe(parent.text);
    expect(expanded.nodes.find(node => node.id === child.id)?.text).toBe(child.text);
    await page.getByRole('button', { name: 'Toggle contents from relations', exact: true }).click();
    await expect.poll(async () => (await stored(request, saved.id)).nodes.find(node => node.id === parent.id)?.collapsed).toBe(true);
    await expect(tile(page, child.id)).toHaveCount(0); await expect(cover()).toHaveText(parent.text!);
  }
  await page.getByRole('button', { name: 'Fit Work', exact: true }).click();
  await page.screenshot({ path: info.outputPath('automatic-grid-preserved-collapsed-cover.png') });
});

test('empty automatic Gallery and an empty object show their complete empty state inside a fitted frame', async ({ page, request }) => {
  const project = newProject('Gallery empty-state QA'), canvasId = project.entryCanvasId;
  const empty = addNode(project, canvasId, 'container', 'Empty gallery');
  Object.assign(empty, { x: 100, y: 100, containerVariant: 'gallery', childLayout: 'contained', width: 1200, height: 892 });
  const collection = addNode(project, canvasId, 'container', 'One empty object');
  Object.assign(collection, { x: 700, y: 100, containerVariant: 'gallery', childLayout: 'contained', width: 1200, height: 892 });
  const object = addNode(project, canvasId, 'container', 'An empty object'); object.parentId = collection.id;
  await seed(page, request, project);
  for (const [id, selector] of [[empty.id, '.ms-gallery-empty'], [collection.id, '.ms-gallery-item']]) {
    const fits = await tile(page, id).evaluate((element, selector) => {
      const frame = element.getBoundingClientRect(), content = element.querySelector(selector as string)!.getBoundingClientRect();
      return content.bottom <= frame.bottom && content.right <= frame.right;
    }, selector);
    expect(fits).toBe(true); expect((await geometry(page, id)).height).toBeLessThan(300);
  }
});

test('offline viewer repairs legacy Gallery collisions once per canvas like the editor, while paging and Reset preserve the publication and camera', async ({ page, request, browser }, info) => {
  const project = newProject('Portable Gallery collision repair QA'), second = addCanvas(project, 'Another collection');
  const collections = [project.entryCanvasId, second.id].map((canvasId, index) => {
    const parent = addNode(project, canvasId, 'container', `Collection ${index + 1}`);
    Object.assign(parent, { x: 100, y: 100, width: 350, height: 100, containerVariant: 'gallery', childLayout: 'contained' });
    const long = addNode(project, canvasId, 'text', `A complete long object ${index + 1}`);
    Object.assign(long, { parentId: parent.id, width: 350, text: 'A documented explanation that stays fully visible.\n'.repeat(8), style: { fontSize: 18 } });
    const short = addNode(project, canvasId, 'text', `A short object ${index + 1}`); Object.assign(short, { parentId: parent.id, text: 'A concise entry.' });
    const peer = addNode(project, canvasId, 'text', `Independent neighbor ${index + 1}`);
    Object.assign(peer, { x: 200, y: 250, width: 200, height: 120, sizeMode: 'fixed', text: 'This sibling must never cover the gallery controls.' });
    return { canvasId, parent, long, short, peer };
  });
  const response = await request.post('/api/projects', { data: commitSnapshots(project) }); expect(response.ok(), await response.text()).toBeTruthy();
  const authored: MeridianProject = await response.json(), expected = new Map<string, Awaited<ReturnType<typeof geometry>>>();
  for (const collection of collections) {
    await page.goto(`/?project=${authored.id}&canvas=${collection.canvasId}`); await expect(tile(page, collection.parent.id)).toHaveCount(1);
    await expect.poll(async () => { const current = (await stored(request, authored.id)).nodes.find(node => node.id === collection.peer.id)!;
      return current.x !== collection.peer.x || current.y !== collection.peer.y; }).toBe(true);
    expected.set(collection.parent.id, await geometry(page, collection.parent.id)); expected.set(collection.peer.id, await geometry(page, collection.peer.id));
  }
  const offlinePath = info.outputPath('gallery-runtime-collision-repair.html');
  await writeFile(offlinePath, buildPublication(authored, await readFile('dist/viewer.js', 'utf8'), await readFile('dist/scene.css', 'utf8')));
  const context = await browser.newContext({ offline: true, viewport: { width: 1500, height: 950 } });
  try {
    const viewer = await context.newPage(); await viewer.goto(pathToFileURL(offlinePath).href); const original = await viewer.locator('#meridian-data').textContent();
    const parity = async (collection: typeof collections[number]) => {
      for (const id of [collection.parent.id, collection.peer.id]) {
        const actual = await geometry(viewer, id), editor = expected.get(id)!;
        for (const key of ['x', 'y', 'width', 'height'] as const) expect(actual[key]).toBeCloseTo(editor[key], 2);
      }
    };
    const noOverlap = async (collection: typeof collections[number]) => {
      const gallery = await geometry(viewer, collection.parent.id), peer = await geometry(viewer, collection.peer.id);
      const overlapX = Math.min(gallery.x + gallery.width, peer.x + peer.width) - Math.max(gallery.x, peer.x);
      const overlapY = Math.min(gallery.y + gallery.height, peer.y + peer.height) - Math.max(gallery.y, peer.y);
      expect(overlapX <= .01 || overlapY <= .01).toBe(true);
    };
    for (const collection of collections) {
      await viewer.getByRole('combobox', { name: 'Choose Work', exact: true }).selectOption(collection.canvasId); await parity(collection); await noOverlap(collection);
      const camera = await viewer.locator('.ms-world').evaluate(element => (element as HTMLElement).style.transform);
      const peer = await geometry(viewer, collection.peer.id), gallery = viewer.locator(`.ms-gallery[data-gallery-id="${collection.parent.id}"]`);
      await gallery.getByRole('button', { name: `Next object in ${collection.parent.title}`, exact: true }).click();
      await expect(gallery.locator('.ms-gallery-item')).toHaveAttribute('data-gallery-item-id', collection.short.id); await noOverlap(collection);
      expect(await geometry(viewer, collection.peer.id)).toEqual(peer);
      expect(await viewer.locator('.ms-world').evaluate(element => (element as HTMLElement).style.transform)).toBe(camera);
      await viewer.getByRole('button', { name: 'Reset to authored state', exact: true }).click();
      await expect(gallery.locator('.ms-gallery-item')).toHaveAttribute('data-gallery-item-id', collection.long.id); await parity(collection); await noOverlap(collection);
      expect(await viewer.locator('.ms-world').evaluate(element => (element as HTMLElement).style.transform)).toBe(camera);
      expect(await viewer.locator('#meridian-data').textContent()).toBe(original);
    }
    // Reset on the second canvas also discards the first canvas's runtime repair.
    // Visiting it again must normalize it once without changing its remembered view.
    await viewer.getByRole('combobox', { name: 'Choose Work', exact: true }).selectOption(collections[0]!.canvasId);
    await parity(collections[0]!); await noOverlap(collections[0]!);
    expect(JSON.parse(original!).project.nodes.map((node: { id: string }) => node.id)).toEqual(authored.nodes.map(node => node.id));
    // Publication intentionally omits local storage snapshots. Compare the
    // publication to its own original payload, rather than its source envelope.
    expect(JSON.parse((await viewer.locator('#meridian-data').textContent())!).project).toEqual(JSON.parse(original!).project);
    await viewer.screenshot({ path: info.outputPath('gallery-viewer-repaired-neighbor.png') });
  } finally { await context.close(); }
});
