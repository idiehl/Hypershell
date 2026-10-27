import { test, expect, type APIRequestContext, type Page } from '@playwright/test';
import { addCanvas, addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import type { MeridianProject } from '../src/core/types.ts';
import { inspectorPanel } from './ui-helpers.ts';

const row = (page: Page, id: string) => page.locator(`[data-outline-node="${id}"]`);
const tile = (page: Page, id: string) => page.locator(`.ms-node[data-node-id="${id}"]`);
const search = (page: Page) => page.getByRole('searchbox', { name: 'Search Work nodes', exact: true });
const stored = async (request: APIRequestContext, id: string): Promise<MeridianProject> => (await request.get(`/api/projects/${id}`)).json();

async function fixture(page: Page, request: APIRequestContext, many = 0) {
  const project = newProject('Search refinement'), parent = addNode(project, project.entryCanvasId, 'container', 'Closed archive');
  Object.assign(parent, { x: 100, y: 100, collapsed: true });
  const body = addNode(project, project.entryCanvasId, 'media', 'Hidden account');
  Object.assign(body, { parentId: parent.id, contentType: 'text', text: 'Shared witness account with an uncommon-body-token.' });
  const document = addNode(project, project.entryCanvasId, 'media', 'Source table');
  Object.assign(document, { parentId: parent.id, contentType: 'asset', assetId: 'long-tsv', cell: { column: 2, row: 3, name: 'WitnessRegister' } });
  project.assets.push({ id: 'long-tsv', name: 'Comparative-register.tsv', mime: 'text/tab-separated-values', base64: Buffer.from('Opening\tline\n'.repeat(1000) + 'distant-document-token <img src=x onerror=alert(1)>').toString('base64') });
  const link = addNode(project, project.entryCanvasId, 'media', 'Museum');
  Object.assign(link, { parentId: parent.id, contentType: 'link', hyperlinks: [{ url: 'https://example.org/catalog-link-token', label: 'Artifact catalog' }], remoteLinks: [{ id: 'remote-connection', targetId: 'remote_source_guid', relation: 'generic', label: 'Remote source' }] });
  const elsewhere = addCanvas(project, 'Other sources'), outsideParent = addNode(project, elsewhere.id, 'container', 'Other archive'); outsideParent.collapsed = true;
  const outside = addNode(project, elsewhere.id, 'media', 'External account'); Object.assign(outside, { parentId: outsideParent.id, contentType: 'text', text: 'Shared witness account elsewhere.' });
  for (let at = 0; at < many; at++) { const node = addNode(project, project.entryCanvasId, 'media', `Catalog ${at + 1}`); Object.assign(node, { parentId: parent.id, contentType: 'text', text: 'bulk-search-token' }); }
  const created = await request.post('/api/projects', { data: commitSnapshots(project) }); expect(created.ok()).toBeTruthy();
  await page.goto(`/?project=${project.id}&canvas=${project.entryCanvasId}`); await inspectorPanel(page, 'canvas');
  await expect(search(page)).toBeVisible(); return { project, parent, body, document, link, outside, outsideParent };
}

test('Canvas search finds hidden active text, complete inert documents, cell names, links and type aliases', async ({ page, request }, testInfo) => {
  const f = await fixture(page, request), before = await stored(request, f.project.id);
  await expect(page.getByRole('combobox', { name: 'Search scope', exact: true })).toBeHidden();
  await expect(tile(page, f.body.id)).toHaveCount(0);
  for (const [query, id, source] of [
    ['uncommon-body-token', f.body.id, 'Text'], ['distant-document-token', f.document.id, 'Attachment text'],
    ['Comparative-register.tsv', f.document.id, 'Attachment'], ['2:3', f.document.id, 'Canvas / identity'],
    ['WitnessRegister', f.document.id, 'Canvas / identity'], ['catalog-link-token', f.link.id, 'Hyperlink'],
    ['remote_source_guid', f.link.id, 'Remote link'], ['Media Document', f.document.id, 'Type'],
  ]) {
    await search(page).fill(query); await expect(row(page, id)).toBeVisible(); await expect(row(page, id)).toHaveAttribute('data-search-source', source);
    await expect(row(page, f.parent.id)).toBeVisible();
  }
  await search(page).fill('distant-document-token'); await row(page,f.document.id).locator('[data-outline-more]').click(); await expect(row(page, f.document.id)).toContainText(f.document.id); await expect(row(page, f.document.id)).not.toContainText('<img src=x onerror=alert(1)>');
  await expect(row(page, f.document.id).locator('img')).toHaveCount(0);
  await page.screenshot({ path: testInfo.outputPath('canvas-content-search.png') });
  expect(await stored(request, f.project.id)).toEqual(before);
  await search(page).fill('uncommon-body-token'); await search(page).press('ArrowDown');
  await expect(row(page, f.body.id).locator('.inspector-tree-title')).toBeFocused();
  await search(page).focus(); await search(page).press('Enter'); await expect(tile(page, f.body.id)).toHaveClass(/ms-selected/);
  await expect.poll(async () => (await stored(request, f.project.id)).nodes.find(node => node.id === f.parent.id)?.collapsed).toBe(false);
  await search(page).press('Escape'); await expect(search(page)).toHaveValue('');
  await expect(page.getByRole('combobox', { name: 'Search scope', exact: true })).toBeHidden();
  for (const panel of ['properties', 'appearance', 'content', 'relations', 'links']) { await inspectorPanel(page, panel); await expect(page.getByRole('searchbox')).toHaveCount(0); }
});

test('All canvases is an explicit query scope with context and jump-only external results', async ({ page, request }, testInfo) => {
  const f = await fixture(page, request), before = await stored(request, f.project.id);
  await search(page).fill('Shared witness'); await expect(row(page, f.body.id)).toBeVisible(); await expect(row(page, f.outside.id)).toHaveCount(0);
  const scope = page.getByRole('combobox', { name: 'Search scope', exact: true }); await expect(scope).toHaveValue('canvas'); await scope.selectOption('all');
  await expect(row(page, f.outside.id)).toBeVisible(); await row(page,f.outside.id).locator('[data-outline-more]').click(); await expect(row(page, f.outside.id)).toContainText(f.outside.id); await expect(row(page, f.outsideParent.id)).toBeVisible();
  await expect(row(page, f.outside.id).getByRole('button', { name: /^Delete node:/ })).toHaveCount(0);
  await page.screenshot({ path: testInfo.outputPath('canvas-cross-context-search.png') });
  expect(await stored(request, f.project.id)).toEqual(before);
  await row(page, f.outside.id).getByRole('button', { name: `Jump to Node: ${f.outside.title}`, exact: true }).click();
  await expect(page).toHaveURL(new RegExp(`canvas=${f.outside.canvasId}`)); await expect(tile(page, f.outside.id)).toHaveClass(/ms-selected/);
  expect((await stored(request, f.project.id)).nodes.some(node => node.id === f.outside.id)).toBe(true);
});

test('Canvas search and expanded outlines bound rendered rows with explicit Show more', async ({ page, request }) => {
  const f = await fixture(page, request, 240), before = await stored(request, f.project.id);
  await search(page).fill('bulk-search-token'); await expect(page.locator('.inspector-canvas-tree [data-outline-node]')).toHaveCount(100);
  await expect(page.locator('.inspector-outline-count')).toHaveCount(0);
  await page.getByRole('button', { name: 'Show more canvas nodes', exact: true }).click(); await expect(page.locator('.inspector-canvas-tree [data-outline-node]')).toHaveCount(200);
  await page.getByRole('button', { name: 'Show more canvas nodes', exact: true }).click(); await expect(page.locator('.inspector-canvas-tree [data-outline-node]')).toHaveCount(242);
  await expect(page.getByRole('button', { name: 'Show more canvas nodes', exact: true })).toBeHidden();
  await search(page).press('Escape'); await expect(page.locator('.inspector-canvas-tree [data-outline-node]')).toHaveCount(2);
  await row(page,f.parent.id).locator('.inspector-tree-title').click();
  await expect(page.locator('.inspector-canvas-tree [data-outline-node]')).toHaveCount(100);
  await expect(page.getByRole('button', { name: 'Show more canvas nodes', exact: true })).toBeVisible();
  expect(await stored(request, f.project.id)).toEqual(before);
});
