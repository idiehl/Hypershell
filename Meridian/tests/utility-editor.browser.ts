import { interactionTool } from './ui-helpers.ts';
import { test, expect, type APIRequestContext, type Locator, type Page } from '@playwright/test';
import { readFile, writeFile } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';
import { addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import { arrangeChildren } from '../src/core/grid.ts';
import type { MeridianProject } from '../src/core/types.ts';
import { buildPublication } from '../src/publication.ts';

async function saved(request: APIRequestContext, id: string): Promise<MeridianProject> { return (await request.get(`/api/projects/${id}`)).json(); }
async function install(request: APIRequestContext, project: MeridianProject) {
  const result = await request.post('/api/projects', { data: commitSnapshots(project) }); expect(result.ok(), await result.text()).toBe(true);
}
async function mode(page: Page, value: 'Node' | 'Link') {
  await page.getByRole('button', { name: /^New (?:Node|Link)$/ }).click();
  await page.getByRole('menuitem', { name: value, exact: true }).click();
}
const tile = (page: Page, id: string) => page.locator(`.ms-node[data-node-id="${id}"]`);
const line = (page: Page, id: string) => page.locator(`.ms-link-line[data-link-id="${id}"]`).first();
async function worldClick(page: Page, x: number, y: number) {
  const point = await page.locator('.ms-viewport').evaluate((viewport, point) => {
    const world = viewport.querySelector('.ms-world')!, rect = viewport.getBoundingClientRect();
    const screen = new DOMPoint(point.x, point.y).matrixTransform(new DOMMatrix(getComputedStyle(world).transform));
    return { x: rect.left + screen.x, y: rect.top + screen.y };
  }, { x, y });
  await page.mouse.click(point.x, point.y);
}
async function onLine(page: Page, id: string, fraction = .45) {
  const point = await page.locator(`.ms-link-hit[data-link-id="${id}"]`).first().evaluate((element, fraction) => {
    const path = element as SVGPathElement, local = path.getPointAtLength(path.getTotalLength() * fraction);
    const screen = new DOMPoint(local.x, local.y).matrixTransform(path.getScreenCTM()!);
    return { x: screen.x, y: screen.y };
  }, fraction);
  await page.mouse.click(point.x, point.y);
}
async function change(input: Locator, value: string) { await input.fill(value); await input.press('Tab'); }
function fixture(title: string) {
  const project = newProject(title), canvasId = project.entryCanvasId;
  const source = addNode(project, canvasId, 'text', 'Source'), destination = addNode(project, canvasId, 'text', 'Destination'), branch = addNode(project, canvasId, 'text', 'Branch');
  Object.assign(source, { x: 100, y: 100, width: 160, height: 90, sizeMode: 'fixed', childLayout: 'spatial' });
  Object.assign(destination, { x: 650, y: 100, width: 160, height: 90, sizeMode: 'fixed', childLayout: 'spatial' });
  Object.assign(branch, { x: 100, y: 480, width: 160, height: 90, sizeMode: 'fixed', childLayout: 'spatial' });
  project.canvases[0]!.snapping = { stickyEdges: false, smartAlign: false, snapToGrid: false };
  return { project, source, destination, branch };
}

test('Utility creation exposes Pivot and Joint, compact Dot and Line appearances, and persistent Properties and Appearance', async ({ page, request }, info) => {
  const f = fixture('Utility creation controls'); await install(request, f.project); await page.goto(`/?project=${f.project.id}`); await mode(page, 'Node'); await worldClick(page, 430, 340);
  const dialog = page.getByRole('dialog', { name: 'New Node', exact: true });
  await dialog.getByLabel('Type', { exact: true }).selectOption('utility');
  expect(await dialog.getByLabel('Utility role', { exact: true }).locator('option').allTextContents()).toEqual(['Pivot', 'Joint']);
  await expect(dialog.getByLabel('Width', { exact: true })).toHaveValue('12'); await expect(dialog.getByLabel('Height', { exact: true })).toHaveValue('12');
  await expect(dialog.getByLabel('Content font size', { exact: true })).toBeHidden(); await expect(dialog.getByLabel('Sizing', { exact: true })).toBeHidden();
  await dialog.getByLabel('Utility role', { exact: true }).selectOption('joint'); await dialog.getByLabel('Utility appearance', { exact: true }).selectOption('line');
  await expect(dialog.getByLabel('Width', { exact: true })).toHaveValue('32'); await expect(dialog.getByLabel('Height', { exact: true })).toHaveValue('6');
  await dialog.getByRole('button', { name: 'Create node', exact: true }).click(); await expect(dialog).toHaveCount(0);
  await expect.poll(async () => (await saved(request, f.project.id)).nodes.filter(node => node.kind === 'junction').length).toBe(1);
  const utility = (await saved(request, f.project.id)).nodes.find(node => node.kind === 'junction')!;
  expect(utility).toMatchObject({ title: 'Joint', utilityVariant: 'joint', width: 32, height: 6, sizeMode: 'fixed', style: { shape: 'rectangle' } });
  await interactionTool(page); await tile(page, utility.id).click();
  await page.getByLabel('Side panel', { exact: true }).selectOption('properties');
  await expect(page.getByLabel('Type', { exact: true })).toHaveValue('utility'); await expect(page.getByLabel('Utility role', { exact: true })).toHaveValue('joint');
  await page.getByLabel('Utility role', { exact: true }).selectOption('pivot');
  await page.getByLabel('Side panel', { exact: true }).selectOption('appearance');
  await expect(page.getByLabel('Utility appearance', { exact: true })).toHaveValue('line'); await expect(page.getByLabel('Font', { exact: true })).toHaveCount(0);
  await page.getByLabel('Utility appearance', { exact: true }).selectOption('dot');
  await expect(page.getByLabel('Width', { exact: true })).toHaveValue('12'); await expect(page.getByLabel('Height', { exact: true })).toHaveValue('12');
  await page.getByLabel('Utility appearance', { exact: true }).selectOption('line');
  await expect(page.getByLabel('Width', { exact: true })).toHaveValue('32'); await expect(page.getByLabel('Height', { exact: true })).toHaveValue('6');
  await change(page.getByLabel('Fill color', { exact: true }), '#e78056');
  await change(page.getByLabel('Width', { exact: true }), '40'); await change(page.getByLabel('Height', { exact: true }), '4');
  await expect.poll(async () => (await saved(request, f.project.id)).nodes.find(node => node.id === utility.id)).toMatchObject({ utilityVariant: 'pivot', width: 40, height: 4, style: { fill: '#e78056' } });
  const resized = (await saved(request, f.project.id)).nodes.find(node => node.id === utility.id)!;
  expect(resized.x + resized.width / 2).toBeCloseTo(utility.x + utility.width / 2); expect(resized.y + resized.height / 2).toBeCloseTo(utility.y + utility.height / 2);
  await page.reload(); await tile(page, utility.id).click(); await page.getByLabel('Side panel', { exact: true }).selectOption('properties');
  await expect(page.getByLabel('Utility role', { exact: true })).toHaveValue('pivot'); await expect(page.getByLabel('GUID', { exact: true })).toHaveValue(utility.id);
  await page.screenshot({ path: info.outputPath('utility-properties.png') });
});

test('Link tool previews then commits several draggable pivots together and Escape leaves no orphan utility nodes', async ({ page, request }, info) => {
  const f = fixture('Trace a routed link'); await install(request, f.project); await page.goto(`/?project=${f.project.id}`); await mode(page, 'Link');
  await tile(page, f.source.id).locator('.ms-node-title').click(); await expect(page.getByRole('dialog')).toHaveCount(0);
  await worldClick(page, 360, 340); await worldClick(page, 560, 340);
  expect((await saved(request, f.project.id)).links).toHaveLength(0); expect((await saved(request, f.project.id)).nodes).toHaveLength(4);
  await page.keyboard.press('Escape'); expect((await saved(request, f.project.id)).nodes).toHaveLength(4);
  await expect(page.getByRole('button', { name: 'New Link', exact: true })).toHaveAttribute('aria-pressed', 'true');
  await tile(page, f.source.id).locator('.ms-node-title').click(); await worldClick(page, 360, 340); await worldClick(page, 560, 340); await tile(page, f.destination.id).locator('.ms-node-title').click();
  await expect(page.getByRole('dialog')).toHaveCount(0);
  await expect.poll(async () => (await saved(request, f.project.id)).links.length).toBe(3);
  const completed = await saved(request, f.project.id), pivots = completed.nodes.filter(node => node.kind === 'junction');
  expect(pivots).toHaveLength(2); for (const pivot of pivots) expect(pivot.utilityVariant).toBe('pivot');
  const pivot = pivots[0]!, connected = completed.links.filter(link => link.sourceId === pivot.id || link.targetId === pivot.id), before = await Promise.all(connected.map(link => line(page, link.id).getAttribute('d')));
  await interactionTool(page); const bounds = await tile(page, pivot.id).boundingBox(); expect(bounds).not.toBeNull();
  await page.mouse.move(bounds!.x + bounds!.width / 2, bounds!.y + bounds!.height / 2); await page.mouse.down(); await page.mouse.move(bounds!.x + bounds!.width / 2 + 55, bounds!.y + bounds!.height / 2 + 45, { steps: 8 }); await page.mouse.up();
  await expect.poll(async () => (await saved(request, f.project.id)).nodes.find(node => node.id === pivot.id)?.x).not.toBe(pivot.x);
  const after = await Promise.all(connected.map(link => line(page, link.id).getAttribute('d'))); for (let index = 0; index < before.length; index++) expect(after[index]).not.toBe(before[index]);
  await tile(page, pivot.id).click(); await page.getByLabel('Side panel', { exact: true }).selectOption('properties'); await expect(page.getByLabel('Utility role', { exact: true })).toHaveValue('pivot');
  await page.screenshot({ path: info.outputPath('routed-link-pivots.png') });
});

test('Node tool inserts a Joint on the clicked line and several sources share its outgoing path; link styles persist offline', async ({ page, request, browser }, info) => {
  const f = fixture('Shared link joints'), original = { id: 'shared-route', canvasId: f.project.entryCanvasId, sourceId: f.source.id, targetId: f.destination.id, kind: 'visual' as const, relation: 'causal' as const, direction: 'forward' as const, label: 'Shared route', style: { color: '#6dbbbb', width: 3, lineStyle: 'solid' as const } };
  f.project.links.push(original); await install(request, f.project); await page.goto(`/?project=${f.project.id}`); await mode(page, 'Node'); await onLine(page, original.id, .4);
  const dialog = page.getByRole('dialog', { name: 'New Node', exact: true });
  expect(await dialog.getByLabel('Type', { exact: true }).locator('option').allTextContents()).toEqual(['Utility']); await dialog.getByLabel('Utility role', { exact: true }).selectOption('joint');
  await dialog.getByRole('button', { name: 'Create node', exact: true }).click(); await expect(dialog).toHaveCount(0);
  await expect.poll(async () => (await saved(request, f.project.id)).links.length).toBe(2);
  let current = await saved(request, f.project.id); const joint = current.nodes.find(node => node.utilityVariant === 'joint')!;
  expect(joint).toBeTruthy(); expect(current.links.filter(link => link.sourceId === joint.id)).toHaveLength(1);
  await mode(page, 'Link'); await tile(page, f.branch.id).locator('.ms-node-title').click(); await tile(page, joint.id).click();
  await expect.poll(async () => (await saved(request, f.project.id)).links.length).toBe(3); current = await saved(request, f.project.id);
  expect(current.links.filter(link => link.targetId === joint.id)).toHaveLength(2); const outgoing = current.links.filter(link => link.sourceId === joint.id); expect(outgoing).toHaveLength(1);
  await interactionTool(page); await onLine(page, outgoing[0]!.id);
  await page.getByLabel('Side panel', { exact: true }).selectOption('appearance');
  await change(page.getByLabel('Line color', { exact: true }), '#db6b9c'); await change(page.getByLabel('Line width', { exact: true }), '12'); await page.getByLabel('Line style', { exact: true }).selectOption('dotted');
  await expect.poll(async () => (await saved(request, f.project.id)).links.find(link => link.id === outgoing[0]!.id)?.style).toMatchObject({ color: '#db6b9c', width: 12, lineStyle: 'dotted' });
  await tile(page, f.destination.id).locator('.ms-node-title').click(); await expect(line(page, outgoing[0]!.id)).toHaveCSS('stroke', 'rgb(219, 107, 156)'); await expect(line(page, outgoing[0]!.id)).toHaveCSS('stroke-width', '12px'); await expect(line(page, outgoing[0]!.id)).toHaveCSS('stroke-linecap', 'round');
  await page.reload(); current = await saved(request, f.project.id); expect(current.links.find(link => link.id === outgoing[0]!.id)?.style?.lineStyle).toBe('dotted');
  const output = info.outputPath('utility-links.html'); await writeFile(output, buildPublication(current, await readFile('dist/viewer.js', 'utf8'), await readFile('dist/scene.css', 'utf8')));
  const context = await browser.newContext({ offline: true });
  try { const viewer = await context.newPage(); await viewer.goto(pathToFileURL(output).href); await expect(tile(viewer, joint.id)).toBeVisible(); await expect(line(viewer, outgoing[0]!.id)).toHaveCSS('stroke', 'rgb(219, 107, 156)'); await expect(line(viewer, outgoing[0]!.id)).toHaveCSS('stroke-linecap', 'round'); await viewer.screenshot({ path: info.outputPath('shared-joint-offline.png') }); }
  finally { await context.close(); }
});

test('a Utility inside an ordered Grid keeps independent compact appearance and geometry without changing ordinary child cells', async ({ page, request }, info) => {
  const project = newProject('Utility over an ordered surface'), canvasId = project.entryCanvasId;
  const parent = addNode(project, canvasId, 'container', 'Ordered contents');
  Object.assign(parent, { x: 100, y: 100, width: 400, height: 330, sizeMode: 'fixed', collapsed: false, containerVariant: 'grid', childLayout: 'contained', childArrangement: 'grid', grid: { columns: 2, rows: 2, cellWidth: 150, cellHeight: 100, gap: 0, showHeaders: false, fillOrder: 'row' } });
  const cells = [addNode(project, canvasId, 'text', 'First cell'), addNode(project, canvasId, 'text', 'Second cell')];
  for (const cell of cells) Object.assign(cell, { parentId: parent.id, width: 150, height: 100, sizeMode: 'fixed', childLayout: 'spatial', style: { shape: 'rectangle' } });
  const pivot = addNode(project, canvasId, 'junction', 'Grid route pivot');
  Object.assign(pivot, { parentId: parent.id, utilityVariant: 'pivot', x: 174, y: 284, width: 12, height: 12, sizeMode: 'fixed', placement: 'free', style: { shape: 'circle', fill: '#78ddc1' }, snapping: { stickyEdges: false, smartAlign: false, snapToGrid: false } });
  const arranged = arrangeChildren(project, parent.id, 'grid').project;
  await install(request, arranged); await page.goto(`/?project=${project.id}`); await expect(tile(page, pivot.id)).toBeVisible();
  await tile(page, pivot.id).click(); await page.getByLabel('Side panel', { exact: true }).selectOption('properties');
  await expect(page.getByLabel('Type', { exact: true })).toHaveValue('utility'); await expect(page.getByLabel('Utility role', { exact: true })).toHaveValue('pivot');
  const baseline = await saved(request, project.id);
  const renderedGeometry = async (id: string) => tile(page, id).evaluate(node => ({ x: parseFloat((node as HTMLElement).style.left), y: parseFloat((node as HTMLElement).style.top), width: parseFloat((node as HTMLElement).style.width), height: parseFloat((node as HTMLElement).style.height) }));
  // Opening the browser replaces the fixture's server text-width estimate with
  // real font metrics. Its first edit persists the already-rendered layout.
  // Freeze that visible geometry and every stored non-geometry field before
  // touching the utility, so a routing edit cannot resize or reorder the cells.
  const before = { ...baseline.nodes.find(node => node.id === pivot.id)!, ...await renderedGeometry(pivot.id) };
  const ordinary = await Promise.all(baseline.nodes.filter(node => cells.some(cell => cell.id === node.id)).map(async node => ({ ...node, ...await renderedGeometry(node.id) })));
  expect(before.cell).toBeUndefined(); expect(before.width).toBe(12); expect(before.height).toBe(12);
  await page.getByLabel('Side panel', { exact: true }).selectOption('appearance');
  for (const label of ['Shape', 'Width', 'Height', 'X', 'Y']) await expect(page.getByLabel(label, { exact: true })).toBeEnabled();
  await page.getByLabel('Shape', { exact: true }).selectOption('hexagon'); await change(page.getByLabel('Fill color', { exact: true }), '#e78056');
  await change(page.getByLabel('Width', { exact: true }), '20'); await change(page.getByLabel('Height', { exact: true }), '8');
  await expect.poll(async () => (await saved(request, project.id)).nodes.find(node => node.id === pivot.id)).toMatchObject({ width: 20, height: 8, utilityVariant: 'pivot', style: { shape: 'hexagon', fill: '#e78056' } });
  const after = await saved(request, project.id), resized = after.nodes.find(node => node.id === pivot.id)!;
  expect(resized.x + resized.width / 2).toBeCloseTo(before.x + before.width / 2); expect(resized.y + resized.height / 2).toBeCloseTo(before.y + before.height / 2);
  expect(resized.cell).toBeUndefined(); expect(after.nodes.filter(node => cells.some(cell => cell.id === node.id))).toEqual(ordinary);
  for (const cell of ordinary) expect(await renderedGeometry(cell.id)).toEqual({ x: cell.x, y: cell.y, width: cell.width, height: cell.height });
  await expect(tile(page, pivot.id)).toHaveAttribute('data-shape', 'hexagon'); await expect(tile(page, pivot.id)).toHaveCSS('width', '20px'); await expect(tile(page, pivot.id)).toHaveCSS('height', '8px');
  await page.reload(); await tile(page, pivot.id).click(); await page.getByLabel('Side panel', { exact: true }).selectOption('appearance');
  await expect(page.getByLabel('Shape', { exact: true })).toHaveValue('hexagon'); await expect(page.getByLabel('Width', { exact: true })).toHaveValue('20'); await expect(page.getByLabel('Height', { exact: true })).toHaveValue('8');
  for (const label of ['Shape', 'Width', 'Height', 'X', 'Y']) await expect(page.getByLabel(label, { exact: true })).toBeEnabled();
  expect((await saved(request, project.id)).nodes.filter(node => cells.some(cell => cell.id === node.id))).toEqual(ordinary);
  for (const cell of ordinary) expect(await renderedGeometry(cell.id)).toEqual({ x: cell.x, y: cell.y, width: cell.width, height: cell.height });
  await page.screenshot({ path: info.outputPath('ordered-grid-utility-appearance.png') });
});
