import { test, expect, type APIRequestContext, type Page } from '@playwright/test';
import { readFile, writeFile } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';
import { addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import { reflowContainedChildren } from '../src/core/grid.ts';
import { buildPublication } from '../src/publication.ts';
import type { MeridianProject } from '../src/core/types.ts';
import { interactionTool } from './ui-helpers.ts';

const tile = (page: Page, id: string) => page.locator(`.ms-node[data-node-id="${id}"]`);
const saved = async (request: APIRequestContext, id: string): Promise<MeridianProject> => (await request.get(`/api/projects/${id}`)).json();
async function fixture(page: Page, request: APIRequestContext) {
  const project = newProject('Selection, constrained links, and appearance'), canvasId = project.entryCanvasId;
  project.canvases[0]!.snapping = { stickyEdges: false, smartAlign: false, snapToGrid: false };
  const source = addNode(project, canvasId, 'media', 'Source'), target = addNode(project, canvasId, 'media', 'Destination'), peer = addNode(project, canvasId, 'media', 'Peer');
  for (const [node, x, y] of [[source, 100, 100], [target, 650, 400], [peer, 650, 100]] as const)
    Object.assign(node, { x, y, width: 180, height: 100, sizeMode: 'fixed', contentType: 'text', text: `${node.title} content`, style: { shape: 'rounded' } });
  expect((await request.post('/api/projects', { data: commitSnapshots(project) })).ok()).toBe(true);
  await page.goto(`/?project=${project.id}`); await expect(tile(page, source.id)).toBeVisible(); return { project, source, target, peer };
}
async function worldPoint(page: Page, x: number, y: number) {
  return page.locator('.ms-viewport').evaluate((viewport, point) => {
    const world = viewport.querySelector('.ms-world')!, rect = viewport.getBoundingClientRect();
    const screen = new DOMPoint(point.x, point.y).matrixTransform(new DOMMatrix(getComputedStyle(world).transform));
    return { x: rect.left + screen.x, y: rect.top + screen.y };
  }, { x, y });
}
async function assertAxisSegments(page: Page) {
  const segments = await page.locator('.ms-link-line').evaluateAll(paths => paths.map(element => {
    const line = element as SVGPathElement, length = line.getTotalLength();
    return Array.from({ length: 17 }, (_, index) => { const point = line.getPointAtLength(length * index / 16); return { x: point.x, y: point.y }; });
  }));
  expect(segments).toHaveLength(3);
  for (const points of segments) {
    const xs = points.map(point => point.x), ys = points.map(point => point.y);
    expect(Math.min(Math.max(...xs) - Math.min(...xs), Math.max(...ys) - Math.min(...ys))).toBeLessThan(.05);
    expect(Math.max(Math.max(...xs) - Math.min(...xs), Math.max(...ys) - Math.min(...ys))).toBeGreaterThan(10);
  }
}
function assertAxisPolyline(path: string | null) {
  expect(path).not.toBeNull(); expect(path).toMatch(/^[ML\dEe+\-.,\s]+$/);
  const numbers = path!.match(/[-+]?(?:\d*\.)?\d+(?:e[-+]?\d+)?/gi)!.map(Number);
  expect(numbers.length).toBeGreaterThanOrEqual(4);
  for (let at = 2; at < numbers.length; at += 2)
    expect(Math.min(Math.abs(numbers[at]! - numbers[at - 2]!), Math.abs(numbers[at + 1]! - numbers[at - 1]!))).toBeLessThan(.05);
}
async function displayCenter(page: Page, id: string) {
  return page.evaluate(id => {
    const card = document.querySelector(`.ms-node[data-node-id="${id}"]`)!, box = card.getBoundingClientRect(), viewport = document.querySelector('.ms-viewport')!.getBoundingClientRect();
    const matrix = new DOMMatrix(getComputedStyle(document.querySelector('.ms-world')!).transform).inverse();
    const point = new DOMPoint(box.x + box.width / 2 - viewport.x, box.y + box.height / 2 - viewport.y).matrixTransform(matrix);
    return { x: point.x, y: point.y };
  }, id);
}

test('Single select is the default, background dragging pans, and the Hand dropdown selects an area', async ({ page, request }, info) => {
  const f = await fixture(page, request), baseline = await saved(request, f.project.id);
  await expect(page.getByRole('button', { name: 'Single select', exact: true })).toHaveAttribute('aria-pressed', 'true');
  await tile(page, f.source.id).locator('.ms-node-title').click(); await tile(page, f.peer.id).locator('.ms-node-title').click();
  await expect(tile(page, f.source.id)).toHaveAttribute('aria-selected', 'false'); await expect(tile(page, f.peer.id)).toHaveAttribute('aria-selected', 'true');
  const before = await page.locator('.ms-world').getAttribute('style'), point = await worldPoint(page, 380, 570);
  await page.mouse.move(point.x, point.y); await page.mouse.down(); await page.mouse.move(point.x + 50, point.y + 35, { steps: 6 }); await page.mouse.up();
  expect(await page.locator('.ms-world').getAttribute('style')).not.toBe(before); await expect(page.getByRole('dialog')).toHaveCount(0);
  expect((await saved(request, f.project.id)).nodes).toEqual(baseline.nodes);
  await interactionTool(page, 'Hand'); await expect(page.getByRole('button', { name: 'Hand', exact: true })).toHaveAttribute('aria-pressed', 'true');
  const source = (await tile(page, f.source.id).boundingBox())!, peer = (await tile(page, f.peer.id).boundingBox())!;
  const camera = await page.locator('.ms-world').getAttribute('style');
  await page.mouse.move(source.x - 8, source.y - 8); await page.mouse.down(); await page.mouse.move(peer.x + peer.width + 8, peer.y + peer.height + 8, { steps: 8 }); await page.mouse.up();
  await expect(tile(page, f.source.id)).toHaveAttribute('aria-selected', 'true'); await expect(tile(page, f.peer.id)).toHaveAttribute('aria-selected', 'true');
  await expect(tile(page, f.target.id)).toHaveAttribute('aria-selected', 'false'); expect(await page.locator('.ms-world').getAttribute('style')).toBe(camera);
  await interactionTool(page); await expect(page.getByRole('button', { name: 'Single select', exact: true })).toHaveAttribute('aria-pressed', 'true');
  await page.screenshot({ path: info.outputPath('selection-tools.png') });
});

test('holding Shift constrains both the click-created pivot and the finished link to canvas axes', async ({ page, request, browser }, info) => {
  const f = await fixture(page, request);
  await page.getByRole('button', { name: 'New Node', exact: true }).click(); await page.getByRole('menuitem', { name: 'Link', exact: true }).click();
  await tile(page, f.source.id).locator('.ms-node-title').click(); await page.keyboard.down('Shift');
  try {
    const point = await worldPoint(page, 430, 250); await page.mouse.move(point.x, point.y); await page.mouse.click(point.x, point.y);
    const draft = page.locator('.ms-link-draft-pivot'); await expect(draft).toHaveCount(1);
    expect(Number(await draft.getAttribute('cx'))).toBeCloseTo(430, 2); expect(Number(await draft.getAttribute('cy'))).toBeCloseTo(150, 2);
    expect((await saved(request, f.project.id)).links).toHaveLength(0);
    await tile(page, f.target.id).locator('.ms-node-title').click();
  } finally { await page.keyboard.up('Shift'); }
  await expect.poll(async () => (await saved(request, f.project.id)).links.length).toBe(3);
  const current = await saved(request, f.project.id), pivots = current.nodes.filter(node => node.kind === 'junction');
  expect(pivots).toHaveLength(2); for (const pivot of pivots) expect(pivot.utilityVariant).toBe('pivot');
  await assertAxisSegments(page); await page.screenshot({ path: info.outputPath('shift-axis-route.png') });
  await page.reload(); await assertAxisSegments(page);
  const filename = info.outputPath('shift-axis-route.html'); await writeFile(filename, buildPublication(current, await readFile('dist/viewer.js', 'utf8'), await readFile('dist/scene.css', 'utf8')));
  const context = await browser.newContext({ offline: true });
  try { const viewer = await context.newPage(); await viewer.goto(pathToFileURL(filename).href); await assertAxisSegments(viewer); }
  finally { await context.close(); }
});

test('text defaults to Center, explicit Left persists offline, and thick borders share the selection frame', async ({ page, request, browser }, info) => {
  const f = await fixture(page, request); await tile(page, f.source.id).locator('.ms-node-title').click();
  await page.getByLabel('Side panel', { exact: true }).selectOption('appearance');
  await expect(page.getByLabel('Text alignment', { exact: true })).toHaveValue('center');
  await expect(tile(page, f.source.id).locator('.ms-node-title')).toHaveCSS('text-align', 'center'); await expect(tile(page, f.source.id).locator('.ms-text')).toHaveCSS('text-align', 'center');
  await page.getByLabel('Text alignment', { exact: true }).selectOption('left');
  for (const width of ['4', '8']) {
    const field = page.getByLabel('Border width', { exact: true }); await field.fill(width); await field.press('Tab');
    await expect.poll(async () => (await saved(request, f.project.id)).nodes.find(node => node.id === f.source.id)?.style?.borderWidth).toBe(Number(width));
    const { frame, outline } = await tile(page, f.source.id).evaluate(element => {
      const coordinates = (box: DOMRect) => ({ x: box.x, y: box.y, width: box.width, height: box.height });
      return { frame: coordinates(element.getBoundingClientRect()), outline: coordinates(element.querySelector(':scope > .ms-node-outline')!.getBoundingClientRect()) };
    });
    for (const coordinate of ['x', 'y', 'width', 'height'] as const) expect(outline[coordinate]).toBeCloseTo(frame[coordinate], 1);
    await expect(tile(page, f.source.id).locator(':scope > .ms-node-outline')).toHaveCount(1);
    expect(await tile(page, f.source.id).evaluate(element => getComputedStyle(element).outlineStyle)).toBe('none');
  }
  await expect(tile(page, f.source.id).locator('.ms-node-title')).toHaveCSS('text-align', 'left'); await expect(tile(page, f.source.id).locator('.ms-text')).toHaveCSS('text-align', 'left');
  await page.screenshot({ path: info.outputPath('thick-border-single-outline.png') }); await page.reload();
  await expect(tile(page, f.source.id).locator('.ms-text')).toHaveCSS('text-align', 'left'); const current = await saved(request, f.project.id);
  expect(current.nodes.find(node => node.id === f.source.id)?.style).toMatchObject({ textAlign: 'left', borderWidth: 8 });
  const filename = info.outputPath('appearance-offline.html'); await writeFile(filename, buildPublication(current, await readFile('dist/viewer.js', 'utf8'), await readFile('dist/scene.css', 'utf8')));
  const context = await browser.newContext({ offline: true });
  try { const viewer = await context.newPage(); await viewer.goto(pathToFileURL(filename).href); await expect(tile(viewer, f.source.id).locator('.ms-text')).toHaveCSS('text-align', 'left'); await expect(tile(viewer, f.peer.id).locator('.ms-text')).toHaveCSS('text-align', 'center'); }
  finally { await context.close(); }
});

test('a Shift route inside expanded bounded Freeform keeps its pivots enclosed and its preview directions intact', async ({ page, request }, info) => {
  const project = newProject('Bounded axis route'), canvasId = project.entryCanvasId;
  project.canvases[0]!.snapping = { stickyEdges: false, smartAlign: false, snapToGrid: false };
  const parent = addNode(project, canvasId, 'container', 'Route workspace');
  Object.assign(parent, { x: 100, y: 100, width: 760, height: 400, sizeMode: 'fixed', containerVariant: 'contained', childLayout: 'contained', collapsed: false, style: { shape: 'rectangle', childPadding: 5 } });
  const source = addNode(project, canvasId, 'media', 'Enclosed source'), target = addNode(project, canvasId, 'media', 'Enclosed destination');
  Object.assign(source, { parentId: parent.id, x: 150, y: 200, width: 160, height: 90, sizeMode: 'fixed', childPosition: 'manual', contentType: 'text', text: 'Source content' });
  Object.assign(target, { parentId: parent.id, x: 650, y: 350, width: 160, height: 90, sizeMode: 'fixed', childPosition: 'manual', contentType: 'text', text: 'Destination content' });
  expect((await request.post('/api/projects', { data: commitSnapshots(project) })).ok()).toBe(true); await page.goto(`/?project=${project.id}`);
  await expect(tile(page, source.id)).toBeVisible(); const sourceCenter = await displayCenter(page, source.id);
  await page.getByRole('button', { name: 'New Node', exact: true }).click(); await page.getByRole('menuitem', { name: 'Link', exact: true }).click();
  await tile(page, source.id).locator('.ms-node-title').click(); await page.keyboard.down('Shift');
  try {
    const point = await worldPoint(page, 550, sourceCenter.y + 45); await page.mouse.move(point.x, point.y); await page.mouse.click(point.x, point.y);
    const pivot = page.locator('.ms-link-draft-pivot'); await expect(pivot).toHaveCount(1);
    expect(Number(await pivot.getAttribute('cx'))).toBeCloseTo(550, 2); expect(Number(await pivot.getAttribute('cy'))).toBeCloseTo(sourceCenter.y, 2);
    const destination = (await tile(page, target.id).locator('.ms-node-title').boundingBox())!; await page.mouse.move(destination.x + destination.width / 2, destination.y + destination.height / 2);
    assertAxisPolyline(await page.locator('.ms-link-draft-line').getAttribute('d'));
    await tile(page, target.id).locator('.ms-node-title').click();
  } finally { await page.keyboard.up('Shift'); }
  await expect.poll(async () => (await saved(request, project.id)).links.length).toBe(3); await assertAxisSegments(page);
  const current = await saved(request, project.id), pivots = current.nodes.filter(node => node.kind === 'junction'); expect(pivots).toHaveLength(2);
  for (const pivot of pivots) {
    expect(pivot.parentId).toBe(parent.id); expect(pivot.utilityVariant).toBe('pivot');
    expect(pivot.x).toBeGreaterThanOrEqual(parent.x + 5); expect(pivot.y).toBeGreaterThanOrEqual(parent.y + 5);
    expect(pivot.x + pivot.width).toBeLessThanOrEqual(parent.x + parent.width - 5); expect(pivot.y + pivot.height).toBeLessThanOrEqual(parent.y + parent.height - 5);
  }
  expect(pivots[0]!.x + pivots[0]!.width / 2).toBeCloseTo(550, 2); expect(pivots[0]!.y + pivots[0]!.height / 2).toBeCloseTo(sourceCenter.y, 2);
  expect(current.nodes.find(node => node.id === parent.id)).toMatchObject({ width: 760, height: 400, collapsed: false });
  await page.screenshot({ path: info.outputPath('bounded-axis-route.png') }); await page.reload(); await assertAxisSegments(page);
});

test('a Shift route in a scrolled Window agrees with its preview and stores logical utility coordinates', async ({ page, request }, info) => {
  let project = newProject('Scrolled Window axis route'); const canvasId = project.entryCanvasId;
  project.canvases[0]!.snapping = { stickyEdges: false, smartAlign: false, snapToGrid: false };
  const parent = addNode(project, canvasId, 'container', 'Scroll route workspace');
  Object.assign(parent, { x: 100, y: 100, sizeMode: 'auto', containerVariant: 'window', collapsed: false, childLayout: 'contained', style: { shape: 'rectangle' },
    grid: { columns: 3, rows: 4, cellWidth: 180, cellHeight: 100, windowColumns: 3, windowRows: 2, scrollDirection: 'vertical' } });
  const source = addNode(project, canvasId, 'media', 'Row two source'), target = addNode(project, canvasId, 'media', 'Row three destination');
  Object.assign(source, { parentId: parent.id, sizeMode: 'fixed', contentType: 'text', text: 'Source in 1:2', cell: { column: 1, row: 2 } });
  Object.assign(target, { parentId: parent.id, sizeMode: 'fixed', contentType: 'text', text: 'Destination in 3:3', cell: { column: 3, row: 3 } });
  project = reflowContainedChildren(project, parent.id);
  expect((await request.post('/api/projects', { data: commitSnapshots(project) })).ok()).toBe(true); await page.goto(`/?project=${project.id}`);
  const pane = page.locator(`.ms-window-viewport[data-window-id="${parent.id}"]`); await expect(pane).toBeVisible();
  await pane.evaluate(element => { element.scrollTop = 100; }); await expect.poll(() => pane.evaluate(element => element.scrollTop)).toBe(100);
  const sourceCenter = await displayCenter(page, source.id), destinationCenter = await displayCenter(page, target.id), clickX = sourceCenter.x + 180;
  expect(destinationCenter.y).toBeGreaterThan(sourceCenter.y); const camera = await page.locator('.ms-world').getAttribute('style');
  await page.getByRole('button', { name: 'New Node', exact: true }).click(); await page.getByRole('menuitem', { name: 'Link', exact: true }).click();
  await tile(page, source.id).locator('.ms-node-title').click(); await page.keyboard.down('Shift');
  try {
    const point = await worldPoint(page, clickX, sourceCenter.y + 20); await page.mouse.move(point.x, point.y); await page.mouse.click(point.x, point.y);
    const pivot = page.locator('.ms-link-draft-pivot'); await expect(pivot).toHaveCount(1);
    expect(Number(await pivot.getAttribute('cx'))).toBeCloseTo(clickX, 2); expect(Number(await pivot.getAttribute('cy'))).toBeCloseTo(sourceCenter.y, 2);
    const destination = (await tile(page, target.id).locator('.ms-node-title').boundingBox())!; await page.mouse.move(destination.x + destination.width / 2, destination.y + destination.height / 2);
    assertAxisPolyline(await page.locator('.ms-link-draft-line').getAttribute('d'));
    await page.screenshot({ path: info.outputPath('scrolled-window-axis-preview.png') }); await tile(page, target.id).locator('.ms-node-title').click();
  } finally { await page.keyboard.up('Shift'); }
  await expect.poll(async () => (await saved(request, project.id)).links.length).toBe(3); await assertAxisSegments(page);
  expect(await page.locator('.ms-world').getAttribute('style')).toBe(camera); await expect.poll(() => pane.evaluate(element => element.scrollTop)).toBe(100);
  const current = await saved(request, project.id), pivots = current.nodes.filter(node => node.kind === 'junction'); expect(pivots).toHaveLength(2);
  for (const pivot of pivots) { expect(pivot.parentId).toBe(parent.id); expect(pivot.utilityVariant).toBe('pivot'); expect(pivot.cell).toBeUndefined(); }
  const first = pivots[0]!; expect(first.x + first.width / 2).toBeCloseTo(clickX, 2); expect(first.y + first.height / 2).toBeCloseTo(sourceCenter.y + 100, 2);
  expect((await displayCenter(page, first.id)).y).toBeCloseTo(sourceCenter.y, 2);
  expect(current.nodes.find(node => node.id === source.id)?.cell).toEqual({ column: 1, row: 2 }); expect(current.nodes.find(node => node.id === target.id)?.cell).toEqual({ column: 3, row: 3 });
  await page.screenshot({ path: info.outputPath('scrolled-window-axis-route.png') }); await page.reload(); await expect(pane).toBeVisible();
  await pane.evaluate(element => { element.scrollTop = 100; }); await expect.poll(() => pane.evaluate(element => element.scrollTop)).toBe(100); await assertAxisSegments(page);
  expect((await saved(request, project.id)).nodes.filter(node => node.kind === 'junction').map(node => ({ id: node.id, x: node.x, y: node.y })))
    .toEqual(pivots.map(node => ({ id: node.id, x: node.x, y: node.y })));
});
