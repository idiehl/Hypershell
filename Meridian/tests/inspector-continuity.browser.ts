import { test, expect, type Page, type APIRequestContext } from '@playwright/test';
import { addCanvas, addNode, commitSnapshots, newProject } from '../src/core/model.ts';

async function seed(page: Page, request: APIRequestContext) {
  const project = newProject('Inspector continuity');
  const original = addNode(project, project.entryCanvasId, 'media', 'Original passage');
  Object.assign(original, { x: 90, y: 90, width: 240, height: 160, contentType: 'text', text: 'A saved source with an independent appearance.' });
  for (let i = 0; i < 18; i++) {
    const node = addNode(project, project.entryCanvasId, 'media', `Entry ${String(i + 1).padStart(2, '0')}`);
    Object.assign(node, { x: 90 + (i % 6) * 330, y: 370 + Math.floor(i / 6) * 230, contentType: 'text', text: 'A separate visible entry.' });
  }
  const other = addCanvas(project, 'Referencing context');
  const parent = addNode(project, other.id, 'media', 'Closed references');
  Object.assign(parent, { x: 100, y: 100, collapsed: true, contentType: 'text', text: 'Saved observations.' });
  const reference = addNode(project, other.id, 'reference', 'Passage elsewhere');
  Object.assign(reference, { parentId: parent.id, targetId: original.id, x: 400, y: 120 });
  const saved = commitSnapshots(project);
  expect((await request.post('/api/projects', { data: saved })).ok()).toBe(true);
  await page.goto(`/?project=${project.id}&canvas=${project.entryCanvasId}`);
  await expect(page.locator('.workspace')).toBeVisible();
  return { project: saved, original, other, parent, reference };
}
async function panel(page: Page, name: string) {
  await page.getByRole('combobox', { name: 'Side panel', exact: true }).selectOption(name);
}

test('Appearance retains field focus and panel position through edits and autosave, and Tab reaches the next field', async ({ page, request }, info) => {
  const f = await seed(page, request);
  await page.locator(`[data-node-id="${f.original.id}"] .ms-node-title`).click();
  await panel(page, 'appearance');
  const body = page.locator('.inspector-panel-appearance'), width = body.getByLabel('Width', { exact: true });
  await width.scrollIntoViewIfNeeded(); await width.focus();
  const before = await body.evaluate(element => element.scrollTop);
  expect(before).toBeGreaterThan(0);
  await width.fill('260'); await width.dispatchEvent('change');
  await expect(width).toBeFocused(); await expect(width).toHaveValue('260');
  expect(Math.abs(await body.evaluate(element => element.scrollTop) - before)).toBeLessThan(3);
  await expect.poll(async () => (await (await request.get(`/api/projects/${f.project.id}`)).json()).nodes.find((node: {id: string}) => node.id === f.original.id).width).toBe(260);
  await expect(width).toBeFocused();
  await width.fill('275'); await page.keyboard.press('Tab');
  await expect(body.getByLabel('Height', { exact: true })).toBeFocused();
  await expect.poll(async () => (await (await request.get(`/api/projects/${f.project.id}`)).json()).nodes.find((node: {id: string}) => node.id === f.original.id).width).toBe(275);
  await body.getByLabel('Height', { exact: true }).fill('85'); await page.keyboard.press('Shift+Tab');
  await expect(width).toBeFocused();
  await page.screenshot({ path: info.outputPath('appearance-continuity.png') });
});

test('outline action focus and scroll survive inspection and a hidden-node jump', async ({ page, request }) => {
  const f = await seed(page, request); await panel(page, 'canvas');
  const body = page.locator('.inspector-panel-canvas');
  const row = body.locator('[data-outline-node]').filter({ has: page.getByRole('button', { name: 'Node: Entry 16', exact: true }) });
  const inspect = row.getByRole('button', { name: 'Node: Entry 16', exact: true });
  await inspect.scrollIntoViewIfNeeded(); await inspect.focus();
  const before = await body.evaluate(element => element.scrollTop);
  await page.keyboard.press('Enter'); await expect(inspect).toBeFocused();
  expect(Math.abs(await body.evaluate(element => element.scrollTop) - before)).toBeLessThan(3);
  await page.keyboard.press('ArrowDown');
  await expect(body.getByRole('button', { name: 'Node: Entry 17', exact: true })).toBeFocused();
  await page.goto(`/?project=${f.project.id}&canvas=${f.other.id}`); await page.keyboard.press('Control+f');
  const search = page.getByRole('searchbox', { name: 'Search Work nodes', exact: true }); await search.fill(f.reference.id);
  const jump = page.locator(`[data-outline-node="${f.reference.id}"] [data-jump-to-node]`);
  await jump.focus(); await page.keyboard.press('Enter'); await expect(jump).toBeFocused();
  await expect(page.locator(`[data-node-id="${f.reference.id}"]`)).toHaveCount(1);
});

test('panel positions are separate for nodes and return when revisiting a node or reopening the panel', async ({ page, request }) => {
  const f = await seed(page, request); await page.locator(`[data-node-id="${f.original.id}"] .ms-node-title`).click(); await panel(page, 'appearance');
  const body = page.locator('.inspector-panel-appearance'); await body.getByLabel('Height', { exact: true }).scrollIntoViewIfNeeded();
  const savedPosition = await body.evaluate(element => element.scrollTop); expect(savedPosition).toBeGreaterThan(0);
  await page.getByRole('button', { name: 'Close side panel', exact: true }).click();
  await page.getByRole('button', { name: 'Show side panel', exact: true }).click();
  expect(Math.abs(await body.evaluate(element => element.scrollTop) - savedPosition)).toBeLessThan(3);
  await panel(page, 'canvas');
  await page.getByRole('button', { name: 'Jump to Node: Entry 01', exact: true }).click(); await panel(page, 'appearance');
  expect(await body.evaluate(element => element.scrollTop)).toBe(0);
  await panel(page, 'canvas'); await page.getByRole('button', { name: 'Jump to Node: Original passage', exact: true }).click(); await panel(page, 'appearance');
  expect(Math.abs(await body.evaluate(element => element.scrollTop) - savedPosition)).toBeLessThan(3);
});

test('Relations finds available backlinks and opens hidden referencing and original contexts without copying or writeback', async ({ page, request }, info) => {
  const f = await seed(page, request); await page.locator(`[data-node-id="${f.original.id}"] .ms-node-title`).click(); await panel(page, 'relations');
  const backlinks = page.getByRole('region', { name: 'Where this node is referenced', exact: true });
  await expect(backlinks).toContainText('Referencing context'); await expect(backlinks).toContainText('hidden');
  await backlinks.getByRole('button', { name: 'Jump to reference: Passage elsewhere', exact: true }).click();
  await expect(page).toHaveURL(new RegExp(`canvas=${f.other.id}`));
  await expect(page.locator(`[data-node-id="${f.reference.id}"]`)).toHaveCount(1);
  await page.getByRole('button', { name: 'Open original context', exact: true }).click();
  await expect(page).toHaveURL(new RegExp(`canvas=${f.project.entryCanvasId}`));
  await expect(page.locator(`[data-node-id="${f.original.id}"]`)).toHaveAttribute('aria-selected', 'true');
  const saved = await (await request.get(`/api/projects/${f.project.id}`)).json();
  expect(saved.nodes).toHaveLength(f.project.nodes.length); expect(saved.assets).toEqual(f.project.assets);
  expect(saved.nodes.find((node: {id: string}) => node.id === f.original.id)).toEqual(f.project.nodes.find(node => node.id === f.original.id));
  expect(saved.nodes.find((node: {id: string}) => node.id === f.reference.id).targetId).toBe(f.original.id);
  await page.screenshot({ path: info.outputPath('relations-backlinks.png') });
});
