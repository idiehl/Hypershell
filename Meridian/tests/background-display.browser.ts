import { test, expect, type APIRequestContext, type Page } from '@playwright/test';
import { addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import type { MeridianProject } from '../src/core/types.ts';
import { inspectorPanel } from './ui-helpers.ts';

const stored = async (request: APIRequestContext, id: string): Promise<MeridianProject> => (await request.get(`/api/projects/${id}`)).json();
const card = (page: Page, id: string) => page.locator(`.ms-node[data-node-id="${id}"]`);
const choice = (page: Page) => page.getByRole('combobox', { name: 'Background display', exact: true });
const fillOpacity = (page: Page, id: string) => card(page, id).evaluate(element => element.hasAttribute('data-shape')
  ? getComputedStyle(element, '::after').opacity
  : getComputedStyle(element.querySelector('.ms-node-hover-fill')!).opacity);
async function appearance(page: Page, id?: string) {
  if (id) {
    await inspectorPanel(page, 'canvas');
    const search = page.getByRole('searchbox', { name: 'Search Work nodes', exact: true });
    await search.fill(id); await page.locator(`[data-outline-node="${id}"] .inspector-tree-title`).click(); await search.fill('');
  } else { await page.locator('.ms-viewport').focus(); await page.keyboard.press('Escape'); }
  await inspectorPanel(page, 'appearance');
  const panel = page.locator('.appearance-panel');
  if (!(await panel.evaluate(element => (element as HTMLDetailsElement).open))) await panel.locator('summary').click();
  return panel;
}

test('node-only background display autosaves, undoes, redoes and reopens while retaining a dormant setting for unframed text', async ({ page, request }, info) => {
  test.setTimeout(90000);
  const fixture = new URL(test.info().project.use.baseURL!); expect(fixture.hostname).toBe('127.0.0.1'); expect(fixture.port).not.toBe('5240');
  const project = newProject('Background display Appearance integration'), canvas = project.canvases[0]!;
  canvas.snapping = { stickyEdges: false, smartAlign: false, snapToGrid: false };
  const node = addNode(project, canvas.id, 'media', 'The text stays visible');
  Object.assign(node, { x: 90, y: 110, width: 360, height: 240, sizeMode: 'fixed', placement: 'free', text: 'Only the background appears when you move over this node.', style: { shape: 'rectangle', fill: '#18352b', border: '#5b8c7d', textColor: '#dcefe5', fontFamily: 'georgia', backgroundPattern: 'grid', backgroundScale: 32, backgroundOpacity: .6 } });
  const peer = addNode(project, canvas.id, 'media', 'Unchanged neighbor');
  Object.assign(peer, { x: 560, y: 110, width: 240, height: 150, sizeMode: 'fixed', placement: 'free', text: 'This node keeps its ordinary background.' });
  const created = await request.post('/api/projects', { data: commitSnapshots(project) }); expect(created.ok(), await created.text()).toBe(true);
  await page.goto(`/?project=${project.id}`); await expect(card(page, node.id)).toBeVisible();
  await appearance(page, node.id);
  await expect(choice(page)).toHaveValue('always');
  expect(await choice(page).locator('option').allTextContents()).toEqual(['Always visible', 'Show on Hover']);
  await choice(page).selectOption('hover');
  await expect.poll(async () => (await stored(request, project.id)).nodes.find(item => item.id === node.id)!.style?.backgroundDisplay).toBe('hover');
  const changed = await stored(request, project.id);
  expect(changed.nodes.find(item => item.id === node.id)!.style).toEqual({ ...node.style, backgroundDisplay: 'hover' });
  expect(changed.nodes.find(item => item.id === peer.id)).toEqual(peer);
  await expect(card(page, node.id).locator('.ms-node-title')).toHaveText(node.title);
  await expect(card(page, node.id).locator('.ms-node-body')).toContainText(node.text!);
  const pattern = card(page, node.id).locator(':scope > .ms-node-background');
  await expect(card(page, node.id)).toHaveAttribute('data-background-display', 'hover');
  await page.mouse.move(10, 10); await expect.poll(() => fillOpacity(page, node.id)).toBe('0'); await expect(pattern).toHaveCSS('opacity', '0');
  await card(page, node.id).hover(); await expect.poll(() => fillOpacity(page, node.id)).toBe('1'); await expect(pattern).toHaveCSS('opacity', '0.6');
  await expect(card(page, node.id).locator('.ms-node-title')).toHaveCSS('opacity', '1'); await expect(card(page, node.id).locator('.ms-node-body')).toHaveCSS('opacity', '1');
  await page.screenshot({ path: info.outputPath('hover-background-with-persistent-content.png') });
  await page.locator('.ms-viewport').focus(); await page.keyboard.press('Control+z');
  await expect.poll(async () => (await stored(request, project.id)).nodes.find(item => item.id === node.id)!.style?.backgroundDisplay).toBeUndefined();
  await expect(choice(page)).toHaveValue('always');
  await page.keyboard.press('Control+x');
  await expect.poll(async () => (await stored(request, project.id)).nodes.find(item => item.id === node.id)!.style?.backgroundDisplay).toBe('hover');
  await expect(choice(page)).toHaveValue('hover');
  await page.reload(); await appearance(page, node.id); await expect(choice(page)).toHaveValue('hover');
  await page.mouse.move(10, 10); await expect.poll(() => fillOpacity(page, node.id)).toBe('0'); await expect(pattern).toHaveCSS('opacity', '0');
  await expect(card(page, node.id).locator('.ms-node-title')).toBeVisible();
  await page.getByRole('combobox', { name: 'Shape', exact: true }).selectOption('none');
  await expect.poll(async () => (await stored(request, project.id)).nodes.find(item => item.id === node.id)!.style?.shape).toBe('none');
  await expect(choice(page)).toHaveValue('hover');
  expect((await stored(request, project.id)).nodes.find(item => item.id === node.id)!.style?.backgroundDisplay).toBe('hover');
  await card(page, node.id).hover();
  await expect(card(page, node.id).locator('.ms-node-title')).toBeVisible(); await expect(card(page, node.id).locator('.ms-node-body')).toBeVisible();
  await expect(card(page, node.id).locator('.ms-node-background')).toBeHidden();
  await page.screenshot({ path: info.outputPath('unframed-content-retains-dormant-hover-background.png') });
  await page.getByRole('combobox', { name: 'Shape', exact: true }).selectOption('rectangle');
  await expect.poll(async () => (await stored(request, project.id)).nodes.find(item => item.id === node.id)!.style?.shape).toBe('rectangle');
  await expect(choice(page)).toHaveValue('hover');
  await appearance(page); await expect(choice(page)).toHaveCount(0);
  await expect(page.getByRole('combobox', { name: 'Work portal style', exact: true })).toBeVisible();
  const saved = await stored(request, project.id);
  expect(saved.nodes.find(item => item.id === node.id)!.style).toEqual({ ...node.style, backgroundDisplay: 'hover' });
  expect(saved.nodes.find(item => item.id === peer.id)).toEqual(peer);
  expect(saved.nodes.find(item => item.id === node.id)!.text).toBe(node.text);
});
