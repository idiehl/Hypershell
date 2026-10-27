import { test, expect, type APIRequestContext, type Page } from '@playwright/test';
import { newProject, addNode, commitSnapshots } from '../src/core/model.ts';
import { arrangeChildren } from '../src/core/grid.ts';
import type { MeridianProject } from '../src/core/types.ts';

async function install(request: APIRequestContext, project: MeridianProject) {
  const response = await request.post('/api/projects', { data: commitSnapshots(project) });
  expect(response.ok()).toBeTruthy(); return response.json() as Promise<MeridianProject>;
}
async function saved(request: APIRequestContext, id: string) { return (await request.get(`/api/projects/${id}`)).json() as Promise<MeridianProject>; }
const tile = (page: Page, id: string) => page.locator(`.ms-node[data-node-id="${id}"]`);
async function beginDrag(page: Page, source: string, target: string) {
  const from = await tile(page, source).locator('.ms-node-title').boundingBox(), into = await tile(page, target).boundingBox();
  expect(from).toBeTruthy(); expect(into).toBeTruthy();
  await page.mouse.move(from!.x + from!.width / 2, from!.y + from!.height / 2); await page.mouse.down();
  await page.mouse.move(into!.x + into!.width / 2, into!.y + into!.height / 2, { steps: 12 });
}

test('drag-to-nest highlights the receiver, moves a complete branch in one undo and retains hierarchy after reopen', async ({ page, request }, info) => {
  const project = newProject('Nesting with pointer feedback'), canvas = project.entryCanvasId;
  const parent = addNode(project, canvas, 'container', 'Receiving container'); Object.assign(parent, { x: 540, y: 120, width: 320, height: 200, sizeMode: 'fixed', collapsed: true, contentType: 'text', text: 'Compressed cover' });
  const branch = addNode(project, canvas, 'media', 'Dragged branch'); Object.assign(branch, { x: 110, y: 180, width: 180, height: 100, sizeMode: 'fixed', text: 'Branch cover', collapsed: true });
  const child = addNode(project, canvas, 'media', 'Hidden descendant'); Object.assign(child, { parentId: branch.id, x: 170, y: 350, text: 'Nested detail', childPosition: 'manual' });
  project.links.push({ id: crypto.randomUUID(), canvasId: canvas, sourceId: branch.id, targetId: child.id, parentId: branch.id, kind: 'visual', relation: 'generic', points: [{ x: 140, y: 315 }] });
  const original = structuredClone(project), installed = await install(request, project);
  await page.goto(`/?project=${installed.id}`); await beginDrag(page, branch.id, parent.id);
  await expect(tile(page, parent.id)).toHaveClass(/ms-drop-receiver/); await expect(page.locator('.ms-nesting-hint')).toBeVisible();
  await expect(page.locator('.ms-nesting-hint')).toHaveAttribute('data-drop-parent', parent.id);
  await page.screenshot({ path: info.outputPath('drag-nesting-feedback.png') }); await page.mouse.up();
  await expect.poll(async () => (await saved(request, installed.id)).nodes.find(node => node.id === branch.id)!.parentId).toBe(parent.id);
  const dropped = await saved(request, installed.id), copy = dropped.nodes.find(node => node.id === branch.id)!;
  expect(copy.childPosition).toBe('auto'); expect(copy.collapsed).toBe(true); expect(dropped.nodes.find(node => node.id === child.id)!.parentId).toBe(branch.id);
  expect(dropped.nodes.find(node => node.id === parent.id)!.collapsed).toBe(false); expect(dropped.nodes.map(node => node.id).sort()).toEqual(original.nodes.map(node => node.id).sort());
  expect(dropped.links.map(link => link.id)).toEqual(original.links.map(link => link.id)); await expect(page.locator('.ms-nesting-hint')).toHaveCount(0);
  await page.locator('.ms-viewport').focus(); await page.keyboard.press('Control+z');
  await expect.poll(async () => (await saved(request, installed.id)).nodes.find(node => node.id === branch.id)!.parentId).toBe(original.canvases[0].rootId);
  expect((await saved(request, installed.id)).nodes.find(node => node.id === parent.id)!.collapsed).toBe(true);
  await page.keyboard.press('Control+y'); await expect.poll(async () => (await saved(request, installed.id)).nodes.find(node => node.id === branch.id)!.parentId).toBe(parent.id);
  await page.reload(); await expect(tile(page, branch.id)).toBeVisible(); await expect(tile(page, child.id)).toHaveCount(0);
});

test('cancelling a nesting drag clears feedback and inherited restrictions never advertise an invalid drop', async ({ page, request }) => {
  const project = newProject('Safe nesting cancellation'), canvas = project.entryCanvasId;
  const parent = addNode(project, canvas, 'media', 'Media parent'); Object.assign(parent, { x: 600, y: 140, width: 220, height: 150, sizeMode: 'fixed', text: 'Unbounded contents' });
  const source = addNode(project, canvas, 'media', 'Movable media'); Object.assign(source, { x: 120, y: 160, width: 190, height: 100, sizeMode: 'fixed', text: 'Preserve this location' });
  const restricted = addNode(project, canvas, 'container', 'Numbers only'); Object.assign(restricted, { x: 650, y: 430, width: 230, height: 150, sizeMode: 'fixed', rules: { nodeKinds: ['number'] }, collapsed: true });
  const installed = await install(request, project); await page.goto(`/?project=${installed.id}`);
  await beginDrag(page, source.id, parent.id); await expect(tile(page, parent.id)).toHaveClass(/ms-drop-receiver/);
  await page.keyboard.press('Escape'); await page.mouse.up(); await expect(page.locator('.ms-nesting-hint')).toHaveCount(0); await expect(tile(page, parent.id)).not.toHaveClass(/ms-drop-receiver/);
  const after = await saved(request, installed.id); expect(after.nodes.find(node => node.id === source.id)).toEqual(installed.nodes.find(node => node.id === source.id));
  await beginDrag(page, source.id, restricted.id); await expect(tile(page, restricted.id)).not.toHaveClass(/ms-drop-receiver/); await expect(page.locator('.ms-nesting-hint')).toBeHidden();
  await page.keyboard.press('Escape'); await page.mouse.up(); expect((await saved(request, installed.id)).nodes.find(node => node.id === source.id)!.parentId).toBe(project.canvases[0].rootId);
});

test('an ordered child can be dragged into another parent while an empty-space release preserves its cell', async ({ page, request }) => {
  let project = newProject('Move an addressed child'), canvas = project.entryCanvasId;
  const grid = addNode(project, canvas, 'container', 'Ordered source'); Object.assign(grid, { x: 80, y: 120, containerVariant: 'horizontal', collapsed: false });
  const source = addNode(project, canvas, 'media', 'Addressed child'); Object.assign(source, { parentId: grid.id, text: 'Retain this content', childPosition: 'auto' });
  project = arrangeChildren(project, grid.id, 'row').project;
  const receiver = addNode(project, canvas, 'media', 'New parent'); Object.assign(receiver, { x: 650, y: 120, width: 210, height: 140, sizeMode: 'fixed', text: 'Unbounded parent' });
  const installed = await install(request, project); await page.goto(`/?project=${installed.id}`);
  const baseline = await saved(request, installed.id), from = await tile(page, source.id).locator('.ms-node-title').boundingBox();
  await page.mouse.move(from!.x + from!.width / 2, from!.y + from!.height / 2); await page.mouse.down();
  await page.mouse.move(from!.x + from!.width / 2, from!.y + 180, { steps: 8 }); await page.mouse.up();
  expect((await saved(request, installed.id)).nodes.find(node => node.id === source.id)).toEqual(baseline.nodes.find(node => node.id === source.id));
  await beginDrag(page, source.id, receiver.id); await expect(tile(page, receiver.id)).toHaveClass(/ms-drop-receiver/); await page.mouse.up();
  await expect.poll(async () => (await saved(request, installed.id)).nodes.find(node => node.id === source.id)!.parentId).toBe(receiver.id);
  const moved = (await saved(request, installed.id)).nodes.find(node => node.id === source.id)!; expect(moved.cell).toBeUndefined(); expect(moved.text).toBe('Retain this content');
  await page.locator('.ms-viewport').focus(); await page.keyboard.press('Control+z');
  await expect.poll(async () => (await saved(request, installed.id)).nodes.find(node => node.id === source.id)!.cell).toEqual(baseline.nodes.find(node => node.id === source.id)!.cell);
});

test('unbounded Gallery pages outside the Media tile and Window scrolls an exterior cell surface without moving the parent', async ({ page, request }, info) => {
  let project = newProject('Exterior browsing patterns'), canvas = project.entryCanvasId;
  const gallery = addNode(project, canvas, 'media', 'Exterior Gallery'); Object.assign(gallery, { x: 80, y: 80, text: 'The parent remains visible', containerVariant: 'gallery', collapsed: false });
  const objects = ['First object', 'Second object'].map(title => { const node = addNode(project, canvas, 'media', title); Object.assign(node, { parentId: gallery.id, text: `${title} passage`, childPosition: 'auto' }); return node; });
  project = arrangeChildren(project, gallery.id, 'free').project;
  const window = addNode(project, canvas, 'media', 'Exterior Window'); Object.assign(window, { x: 650, y: 80, text: 'Window parent content', containerVariant: 'window', collapsed: false, grid: { columns: 4, rows: 2, cellWidth: 150, cellHeight: 100, windowColumns: 2, windowRows: 2, scrollDirection: 'horizontal', fillOrder: 'row' } });
  const cells = Array.from({ length: 8 }, (_, at) => { const node = addNode(project, canvas, 'media', `Cell ${at + 1}`); Object.assign(node, { parentId: window.id, text: `Addressed object ${at + 1}`, childPosition: 'auto' }); return node; });
  project = arrangeChildren(project, window.id, 'grid').project;
  const installed = await install(request, project); await page.goto(`/?project=${installed.id}`);
  await expect(tile(page, gallery.id).locator('.ms-text')).toHaveText('The parent remains visible'); await expect(tile(page, objects[0].id)).toBeVisible(); await expect(tile(page, objects[1].id)).toHaveCount(0);
  const parentBox = await tile(page, gallery.id).boundingBox(), childBox = await tile(page, objects[0].id).boundingBox(); expect(childBox!.x).toBeGreaterThanOrEqual(parentBox!.x + parentBox!.width);
  await page.getByRole('button', { name: 'Next object in Exterior Gallery', exact: true }).click(); await expect(tile(page, objects[1].id)).toBeVisible(); await expect(tile(page, objects[0].id)).toHaveCount(0);
  const pane = page.locator(`[data-external-window="${window.id}"]`); await expect(pane).toBeVisible();
  const a = await tile(page, window.id).boundingBox(), b = await pane.boundingBox(); expect(b!.y).toBeGreaterThanOrEqual(a!.y + a!.height); await expect(tile(page, window.id).locator('.ms-text')).toHaveText('Window parent content');
  await pane.evaluate(element => { element.scrollLeft = element.scrollWidth; }); await expect.poll(() => pane.evaluate(element => element.scrollLeft)).toBeGreaterThan(0);
  await expect(tile(page, cells[3].id)).toBeInViewport(); await page.screenshot({ path: info.outputPath('exterior-gallery-window.png') });
  expect(await saved(request, installed.id)).toEqual(installed); await page.reload(); await expect(tile(page, window.id).locator('.ms-text')).toHaveText('Window parent content');
});
