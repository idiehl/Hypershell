import { test, expect, type APIRequestContext, type Page } from '@playwright/test';
import { readFile, writeFile } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';
import { addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import { reflowContainedChildren } from '../src/core/grid.ts';
import { buildPublication } from '../src/publication.ts';
import type { ContainerVariant, MeridianProject } from '../src/core/types.ts';
import { inspectorPanel } from './ui-helpers.ts';

const tile = (page: Page, id: string) => page.locator(`.ms-node[data-node-id="${id}"]`);
async function selectEditorNode(page: Page, id: string, _title: string) {
  const heading = tile(page, id).locator('> .ms-node-header .ms-node-title'), box = await heading.boundingBox(), stage = await page.locator('.canvas-stage').boundingBox();
  if (box && stage && box.x + box.width / 2 > stage.x && box.x + box.width / 2 < stage.x + stage.width && box.y + box.height / 2 > stage.y && box.y + box.height / 2 < stage.y + stage.height) await heading.click();
  else { await page.keyboard.press('Control+f'); const search = page.getByRole('searchbox', { name: 'Search Work nodes', exact: true }); await search.fill(id);
    const result = page.locator(`[data-outline-node="${id}"]`); await expect(result).toBeVisible(); await result.getByRole('button', { name: /^Jump to Node:/ }).click(); await search.fill(''); }
  await inspectorPanel(page, 'content');
}
const windowView = (page: Page, id: string) => page.locator(`.ms-window-viewport[data-window-id="${id}"]`);
const stored = async (request: APIRequestContext, id: string): Promise<MeridianProject> => (await request.get(`/api/projects/${id}`)).json();
async function seed(page: Page, request: APIRequestContext, project: MeridianProject) {
  const response = await request.post('/api/projects', { data: commitSnapshots(project) });
  expect(response.ok(), await response.text()).toBeTruthy(); const saved: MeridianProject = await response.json();
  await page.goto(`/?project=${saved.id}`); await expect(page.locator('.save-state')).toHaveText(`Saved · r${saved.revision}`); return saved;
}
async function dimensions(page: Page, id: string) { return tile(page, id).evaluate(element => ({ width: parseFloat((element as HTMLElement).style.width), height: parseFloat((element as HTMLElement).style.height) })); }
function collection(variant: ContainerVariant, count: number) {
  let project = newProject('Ordered container interaction QA'); const canvasId = project.entryCanvasId;
  const parent = addNode(project, canvasId, 'container', 'Collection');
  Object.assign(parent, { x: 120, y: 120, containerVariant: variant, childLayout: 'contained', collapsed: false, childArrangement: 'grid', text: 'Choose an object.' });
  parent.grid = { columns: 6, rows: 8, cellWidth: 120, cellHeight: 80, fillOrder: 'row', windowColumns: 3, windowRows: 2, scrollDirection: 'vertical' };
  const children = Array.from({ length: count }, (_, at) => {
    const child = addNode(project, canvasId, 'text', `Entry ${at + 1}`);
    Object.assign(child, { parentId: parent.id, text: `Object ${at + 1}`, cell: { column: at % 6 + 1, row: Math.floor(at / 6) + 1, name: `object_${at + 1}` }, style: { shape: at ? 'diamond' : 'rounded' } }); return child;
  });
  project = reflowContainedChildren(project, parent.id); return { project, parent, children, canvasId };
}
async function openCreation(page: Page) {
  const viewport = (await page.locator('.ms-viewport').boundingBox())!;
  await page.mouse.click(viewport.x + 40, viewport.y + Math.min(500, viewport.height - 40), { button: 'right' });
  await page.getByRole('menuitem', { name: 'Create Node', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'New Node', exact: true }); await expect(dialog).toBeVisible(); return dialog;
}

for (const variant of ['contained', 'horizontal', 'vertical', 'grid', 'spreadsheet', 'window', 'gallery'] as const) {
  test(`creation offers ${variant} with appropriate ordered controls and no Spatial display pattern`, async ({ page, request }) => {
    const saved = await seed(page, request, newProject('Creation options QA')), dialog = await openCreation(page);
    await dialog.getByLabel('Type', { exact: true }).selectOption('container');
    const variants = dialog.getByLabel('Display Pattern', { exact: true }); await expect(variants.locator('option[value="spatial"]')).toHaveCount(0); await variants.selectOption(variant);
    if (variant === 'contained') await expect(variants.locator('option[value="contained"]')).toHaveText('Freeform');
    await dialog.getByLabel('Title', { exact: true }).fill(`Created ${variant}`);
    if (['spreadsheet', 'window'].includes(variant)) {
      await expect(dialog.getByLabel('Columns', { exact: true })).toBeVisible(); await dialog.getByLabel('Columns', { exact: true }).fill('6');
      await dialog.getByLabel('Rows', { exact: true }).fill('8'); await dialog.getByLabel('Cell width', { exact: true }).fill('120'); await dialog.getByLabel('Cell height', { exact: true }).fill('80');
    } else await expect(dialog.getByLabel('Columns', { exact: true })).toBeHidden();
    if (variant === 'window') { await dialog.getByLabel('Window columns', { exact: true }).fill('3'); await dialog.getByLabel('Window rows', { exact: true }).fill('2'); await dialog.getByLabel('Scroll direction', { exact: true }).selectOption('horizontal'); }
    if (['horizontal', 'vertical', 'grid', 'spreadsheet', 'window'].includes(variant)) await dialog.getByLabel('Initial cells (CSV)', { exact: true }).fill('First,Second,Third');
    await dialog.getByRole('button', { name: 'Create node', exact: true }).click();
    await expect.poll(async () => (await stored(request, saved.id)).nodes.find(node => node.title === `Created ${variant}`)?.containerVariant).toBe(variant);
    const current = await stored(request, saved.id), parent = current.nodes.find(node => node.title === `Created ${variant}`)!;
    expect(parent.style?.padding).toBe(5); expect(parent.style?.childPadding).toBe(20);
    if (variant === 'window') { expect(parent.grid).toMatchObject({ columns: 6, rows: 8, cellWidth: 120, cellHeight: 80, windowColumns: 3, windowRows: 2, scrollDirection: 'horizontal' }); await expect(windowView(page, parent.id)).toHaveCount(1); }
    await expect(page.locator('.ms-cell-address')).toHaveCount(0); await expect(page.getByRole('button', { name: /Inspect current object/ })).toHaveCount(0);
  });
}

test('Grid grows from 3×3 to 4×4, fill order moves coordinates and every tile shares its first child shape', async ({ page, request }, info) => {
  const f = collection('grid', 9), saved = await seed(page, request, f.project);
  expect((await stored(request, saved.id)).nodes.find(node => node.id === f.parent.id)?.grid).toMatchObject({ columns: 3, rows: 3 });
  await selectEditorNode(page, f.parent.id, f.parent.title); await tile(page, f.parent.id).locator('.ms-node-title').click({ button: 'right' });
  await page.getByRole('menuitem', { name: 'Create Child Node', exact: true }).click(); const dialog = page.getByRole('dialog', { name: 'New Node', exact: true });
  await dialog.getByLabel('Type', { exact: true }).selectOption('media'); await dialog.getByLabel('Title', { exact: true }).fill('Entry 10');
  await dialog.getByLabel('Content', { exact: true }).selectOption('text'); await dialog.getByLabel('Text body', { exact: true }).fill('Object 10');
  await dialog.getByRole('button', { name: 'Create node', exact: true }).click(); await expect.poll(async () => (await stored(request, saved.id)).nodes.find(node => node.id === f.parent.id)?.grid?.columns).toBe(4);
  await selectEditorNode(page, f.parent.id, f.parent.title); await inspectorPanel(page, 'relations');
  for (const order of ['column', 'row']) {
    await page.locator('.inspector').getByLabel('Fill order', { exact: true }).selectOption(order);
    await expect.poll(async () => (await stored(request, saved.id)).nodes.find(node => node.id === f.children[1]!.id)?.cell).toMatchObject(order === 'column' ? { column: 1, row: 2 } : { column: 2, row: 1 });
    const current = await stored(request, saved.id), children = current.nodes.filter(node => node.parentId === f.parent.id), parent = current.nodes.find(node => node.id === f.parent.id)!;
    expect(parent.grid).toMatchObject({ columns: 4, rows: 4, gap: 0 }); expect(children).toHaveLength(10);
    for (const child of children) { expect(child.width).toBe(parent.grid!.cellWidth); expect(child.height).toBe(parent.grid!.cellHeight); await expect(tile(page, child.id)).toHaveAttribute('data-shape', 'rounded'); }
  }
  await expect(page.locator('.ms-cell-address')).toHaveCount(0); await page.screenshot({ path: info.outputPath('square-grid-10-objects.png') });
});

test('display pattern conversion preserves child GUIDs and names, collapses to own content and exposes categories', async ({ page, request }) => {
  test.setTimeout(90000); const f = collection('grid', 4), saved = await seed(page, request, f.project), identities = saved.nodes.map(node => node.id).sort();
  for (const variant of ['horizontal', 'vertical', 'spreadsheet', 'window', 'gallery', 'contained']) {
    await selectEditorNode(page, f.parent.id, f.parent.title); await inspectorPanel(page, 'properties'); const selector = page.getByRole('combobox', { name: 'Display Pattern', exact: true });
    await expect(selector.locator('optgroup[label="Ordered"]')).toHaveCount(1); await expect(selector.locator('optgroup[label="Unordered"]')).toHaveCount(1); await expect(selector.locator('option[value="spatial"]')).toHaveCount(0);
    await expect(selector.locator('option[value="contained"]')).toHaveText('Freeform');
    await selector.selectOption(variant); await expect.poll(async () => (await stored(request, saved.id)).nodes.find(node => node.id === f.parent.id)?.containerVariant).toBe(variant);
    const current = await stored(request, saved.id); expect(current.nodes.map(node => node.id).sort()).toEqual(identities);
    for (const child of f.children) expect(current.nodes.find(node => node.id === child.id)?.cell?.name).toBe(child.cell!.name);
    if (variant === 'horizontal' || variant === 'vertical') for (const [at, child] of f.children.entries()) expect(current.nodes.find(node => node.id === child.id)?.cell).toMatchObject(variant === 'horizontal' ? { column: at + 1, row: 1 } : { column: 1, row: at + 1 });
    const expanded = await dimensions(page, f.parent.id); await inspectorPanel(page, 'relations'); await page.getByRole('button', { name: 'Toggle contents from relations', exact: true }).click();
    await expect.poll(async () => (await stored(request, saved.id)).nodes.find(node => node.id === f.parent.id)?.collapsed).toBe(true);
    expect((await dimensions(page, f.parent.id)).height).toBeLessThan(expanded.height); for (const child of f.children) await expect(tile(page, child.id)).toHaveCount(0);
    await page.getByRole('button', { name: 'Toggle contents from relations', exact: true }).click(); await expect.poll(async () => (await stored(request, saved.id)).nodes.find(node => node.id === f.parent.id)?.collapsed).toBe(false);
  }
});

test('Window scrolls both axes without moving the camera or saving, selects an exposed child and restores its dimensions after reopen', async ({ page, request }, info) => {
  const f = collection('window', 48), saved = await seed(page, request, f.project), viewport = windowView(page, f.parent.id);
  await expect(viewport).toHaveCSS('overflow-x', 'auto'); await expect(viewport).toHaveCSS('overflow-y', 'auto');
  expect(await viewport.evaluate(element => ({ width: (element as HTMLElement).clientWidth, height: (element as HTMLElement).clientHeight }))).toEqual({ width: 360, height: 160 });
  const camera = await page.locator('.ms-world').evaluate(element => (element as HTMLElement).style.transform), writes: string[] = [];
  page.on('request', request => { if (request.method() !== 'GET' && request.url().includes('/api/')) writes.push(`${request.method()} ${request.url()}`); });
  await viewport.focus(); await viewport.press('ArrowDown'); await expect.poll(() => viewport.evaluate(element => element.scrollTop)).toBe(80);
  await viewport.press('ArrowRight'); await expect.poll(() => viewport.evaluate(element => element.scrollLeft)).toBe(120);
  expect(await page.locator('.ms-world').evaluate(element => (element as HTMLElement).style.transform)).toBe(camera);
  const chosen = f.children[7]!; await tile(page, chosen.id).locator('.ms-node-title').click(); await inspectorPanel(page, 'content'); await expect(page.locator('.inspector').getByLabel('Title', { exact: true })).toHaveValue(chosen.title);
  await page.waitForTimeout(1500); expect(writes).toEqual([]); expect(await stored(request, saved.id)).toEqual(saved);
  const body = page.locator('.inspector').getByLabel('Text body', { exact: true }); await body.fill('Edited inside the scrolling window.'); await body.press('Tab');
  await expect.poll(async () => (await stored(request, saved.id)).nodes.find(node => node.id === chosen.id)?.text).toBe('Edited inside the scrolling window.');
  const current = await stored(request, saved.id); await page.reload(); await expect(windowView(page, f.parent.id)).toHaveCount(1);
  expect((await stored(request, saved.id)).nodes.find(node => node.id === f.parent.id)?.grid).toEqual(current.nodes.find(node => node.id === f.parent.id)?.grid);
  await page.screenshot({ path: info.outputPath('ordered-window-scrolling.png') });
});

test('offline Window uses the same clipped viewport, preserves its authored payload and scrolls horizontally without camera changes', async ({ page, request, browser }, info) => {
  const f = collection('window', 48); f.project.nodes.find(node => node.id === f.parent.id)!.grid!.scrollDirection = 'horizontal'; const saved = await seed(page, request, f.project);
  const path = info.outputPath('ordered-window-offline.html'); await writeFile(path, buildPublication(saved, await readFile('dist/viewer.js', 'utf8'), await readFile('dist/scene.css', 'utf8')));
  const context = await browser.newContext({ offline: true, viewport: { width: 1500, height: 950 } });
  try {
    const viewer = await context.newPage(), requests: string[] = []; viewer.on('request', request => { if (/^https?:/.test(request.url())) requests.push(request.url()); }); await viewer.goto(pathToFileURL(path).href);
    const viewport = windowView(viewer, f.parent.id), authored = await viewer.locator('#meridian-data').textContent(), camera = await viewer.locator('.ms-world').evaluate(element => (element as HTMLElement).style.transform);
    expect(await viewport.evaluate(element => ({ width: (element as HTMLElement).clientWidth, height: (element as HTMLElement).clientHeight }))).toEqual({ width: 360, height: 160 });
    await viewport.hover(); await viewer.mouse.wheel(0, 130); await expect.poll(() => viewport.evaluate(element => element.scrollLeft)).toBeGreaterThan(0);
    await viewport.focus(); await viewport.press('ArrowDown'); await expect.poll(() => viewport.evaluate(element => element.scrollTop)).toBe(80);
    expect(await viewer.locator('.ms-world').evaluate(element => (element as HTMLElement).style.transform)).toBe(camera); expect(await viewer.locator('#meridian-data').textContent()).toBe(authored); expect(requests).toEqual([]);
    await expect(viewer.locator('.ms-cell-address')).toHaveCount(0); await viewer.screenshot({ path: info.outputPath('ordered-window-offline-scrolling.png') });
  } finally { await context.close(); }
});

test('Gallery removes corner addresses, title stripe and redundant inspection control while fitting its navigation bottom', async ({ page, request }, info) => {
  const project = newProject('Gallery presentation QA'), parent = addNode(project, project.entryCanvasId, 'container', 'Zeus · Gallery');
  Object.assign(parent, { x: 100, y: 100, containerVariant: 'gallery', childLayout: 'contained', collapsed: false, width: 400, height: 850 });
  const object = addNode(project, project.entryCanvasId, 'text', 'A source interpretation'); Object.assign(object, { parentId: parent.id, text: 'This object retains its own complete caption and source identity. '.repeat(3), cell: { column: 1, row: 1 } });
  const next = addNode(project, project.entryCanvasId, 'text', 'Another interpretation'); next.parentId = parent.id; next.text = 'A second object.';
  await seed(page, request, project); const view = page.locator(`.ms-gallery[data-gallery-id="${parent.id}"]`);
  await expect(page.getByRole('button', { name: /Inspect current object/ })).toHaveCount(0); await expect(page.locator('.ms-cell-address')).toHaveCount(0);
  await expect(tile(page, parent.id).locator('> .ms-node-header')).toHaveCSS('box-shadow', 'none');
  const remaining = await tile(page, parent.id).evaluate(element => { const frame = element.getBoundingClientRect(), navigation = element.querySelector('.ms-gallery-navigation')!.getBoundingClientRect(); const scale = frame.width / parseFloat((element as HTMLElement).style.width); return (frame.bottom - navigation.bottom) / scale; });
  expect(remaining).toBeGreaterThanOrEqual(4); expect(remaining).toBeLessThanOrEqual(8);
  await view.locator('.ms-gallery-item-title').click(); await inspectorPanel(page, 'content'); await expect(page.locator('.inspector').getByLabel('Title', { exact: true })).toHaveValue(object.title);
  await page.screenshot({ path: info.outputPath('gallery-tight-padding-no-strip.png') });
});
