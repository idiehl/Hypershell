import { workspaceAction } from './ui-helpers.ts';
import { test, expect, type Page } from '@playwright/test';
import { readFile, writeFile } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';
import { newProject, addNode, commitSnapshots } from '../src/core/model.ts';
import { buildPublication } from '../src/publication.ts';
import type { MeridianProject, PublicationPayload } from '../src/core/types.ts';

const card = (page: Page, id: string) => page.locator(`.ms-node[data-node-id="${id}"]`);
const title = (page: Page, id: string) => card(page, id).locator('.ms-node-title');
const rawPayload = (html: string): PublicationPayload => JSON.parse(html.match(/<script type="application\/json" id="meridian-data">([\s\S]*?)<\/script>/)![1]);

function fixture() {
  const project = newProject('A reusable research selection'), canvas = project.entryCanvasId;
  const parent = addNode(project, canvas, 'text', 'Research folder');
  Object.assign(parent, { x: 100, y: 80, width: 260, height: 160, childLayout: 'spatial', collapsed: true, text: 'Sources stay inside their ordinary parent.' });
  const child = addNode(project, canvas, 'text', 'Hidden evidence');
  Object.assign(child, { parentId: parent.id, x: 100, y: 600, width: 230, height: 140, placement: 'near', text: 'This supporting account is hidden until the parent expands.' });
  const source = addNode(project, canvas, 'document', 'Complete source');
  Object.assign(source, { x: 430, y: 80, width: 230, height: 160, text: 'The attached text travels with the selection.', assetId: crypto.randomUUID() });
  project.assets.push({ id: source.assetId!, name: 'source.txt', mime: 'text/plain', base64: Buffer.from('A complete included source.\nSecond paragraph.').toString('base64') });
  const reference = addNode(project, canvas, 'reference', 'Reference to this source');
  Object.assign(reference, { x: 740, y: 80, width: 230, height: 160, targetId: source.id });
  const number = addNode(project, canvas, 'number', 'Authored value');
  Object.assign(number, { x: 100, y: 380, width: 230, height: 150, value: 4 });
  const external = addNode(project, canvas, 'reference', 'Saved outside observation');
  Object.assign(external, { x: 430, y: 380, width: 230, height: 150, targetId: crypto.randomUUID() });
  const outsideCanvas = crypto.randomUUID();
  project.snapshots[external.targetId!] = { savedAt: '2026-10-05T12:00:00Z', value: 9,
    node: { id: external.targetId!, canvasId: outsideCanvas, parentId: crypto.randomUUID(), kind: 'number', title: 'Outside original', x: 0, y: 0, width: 200, height: 120, value: 9 } };
  project.dependencies.push({ canvasId: outsideCanvas, title: 'Outside context', required: true });
  const outside = addNode(project, canvas, 'text', 'Outside the selection');
  Object.assign(outside, { x: 740, y: 380, width: 230, height: 150, text: 'This neighbor must not be silently copied.' });
  project.links.push(
    { id: crypto.randomUUID(), canvasId: canvas, parentId: parent.id, sourceId: parent.id, targetId: child.id, kind: 'visual', relation: 'generic', label: 'Evidence inside' },
    { id: crypto.randomUUID(), canvasId: canvas, sourceId: source.id, targetId: reference.id, kind: 'visual', relation: 'mutual', label: 'Same original' },
    { id: crypto.randomUUID(), canvasId: canvas, sourceId: source.id, targetId: outside.id, kind: 'visual', relation: 'causal', label: 'Crosses the selection' },
  );
  return { project: commitSnapshots(project), parent, child, source, reference, number, external, outside };
}

test('offline viewer snippets expose copy boundaries, remap internal identities and reopen as independent editable canvases', async ({ page, request }, info) => {
  test.setTimeout(90000);
  const f = fixture();
  const created = await request.post('/api/projects', { data: f.project }); expect(created.ok()).toBeTruthy();
  f.project = await created.json() as MeridianProject;
  const html = buildPublication(f.project, await readFile('dist/viewer.js', 'utf8'), await readFile('dist/scene.css', 'utf8'));
  const sourcePath = info.outputPath('source-offline.html'); await writeFile(sourcePath, html);
  const errors: string[] = [], remote: string[] = [];
  page.on('pageerror', error => errors.push(error.message));
  page.on('request', request => { if (/^https?:/.test(request.url())) remote.push(request.url()); });
  await page.route(/^https?:/, route => route.abort()); await page.goto(pathToFileURL(sourcePath).href);
  const unchanged = await page.locator('#meridian-data').textContent();
  await page.getByRole('spinbutton', { name: 'Authored value: value', exact: true }).fill('27');
  await page.getByRole('spinbutton', { name: 'Authored value: value', exact: true }).press('Enter');
  await title(page, f.parent.id).click();
  const viewport = await page.locator('.ms-viewport').boundingBox();
  for (const node of [f.source, f.reference, f.number, f.external]) await title(page, node.id).click({ modifiers: ['Shift'] });
  await expect(page.locator('.mv-selection-count')).toHaveText('5 selected');
  expect(await page.locator('.ms-viewport').boundingBox()).toEqual(viewport);
  await expect(card(page, f.child.id)).toHaveCount(0);
  await page.getByRole('button', { name: 'Download selected nodes', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'Download selection', exact: true });
  await expect(dialog).toContainText('Temporary simulation changes are not copied');
  await expect(dialog.locator('.ms-selection-summary')).toContainText('6 nodes · 2 connections · 1 attachment');
  await expect(dialog.getByLabel('Download External References?', { exact: true })).toBeChecked();
  await dialog.getByLabel('Include contained children, including hidden children', { exact: true }).uncheck();
  await dialog.getByLabel('Download External References?', { exact: true }).uncheck();
  await expect(dialog.locator('.ms-selection-summary')).toContainText('5 nodes · 1 connection · 1 attachment');
  await expect(dialog.locator('.ms-selection-summary')).toContainText('1 omitted (1 hidden)');
  await expect(dialog.locator('.ms-selection-summary')).toContainText('2 connections crossing the selection omitted');
  await dialog.getByLabel('Canvas name', { exact: true }).fill('Selection without descendants');
  const firstEvent = page.waitForEvent('download'); await dialog.getByRole('button', { name: 'Download snippet', exact: true }).click();
  const first = await firstEvent, firstPath = info.outputPath('selection-without-children.meridian.html'); await first.saveAs(firstPath);
  expect(first.suggestedFilename()).toBe('Selection without descendants.meridian.html');
  const small = rawPayload(await readFile(firstPath, 'utf8')).project;
  expect(small.nodes).toHaveLength(6); expect(small.links).toHaveLength(1); expect(small.snapshots).toEqual({});
  expect(small.nodes.some(node => node.title === f.child.title || node.title === f.outside.title)).toBe(false);
  expect(small.nodes.find(node => node.title === f.number.title)?.value).toBe(4);

  await page.getByRole('button', { name: 'Download selected nodes', exact: true }).click();
  await dialog.getByLabel('Canvas name', { exact: true }).fill('Selection with contents');
  const secondEvent = page.waitForEvent('download'); await dialog.getByRole('button', { name: 'Download snippet', exact: true }).click();
  const second = await secondEvent, snippetPath = info.outputPath('selection-with-contents.meridian.html'); await second.saveAs(snippetPath);
  const snippetHtml = await readFile(snippetPath, 'utf8'), payload = rawPayload(snippetHtml), snippet = payload.project;
  expect(snippet.nodes).toHaveLength(7); expect(snippet.links).toHaveLength(2);
  expect(snippet.id).not.toBe(f.project.id); expect(snippet.canvases).toHaveLength(1);
  expect(snippet.nodes.every(node => !f.project.nodes.some(original => original.id === node.id))).toBe(true);
  expect(snippet.links.every(link => !f.project.links.some(original => original.id === link.id))).toBe(true);
  const copiedSource = snippet.nodes.find(node => node.title === f.source.title)!, copiedReference = snippet.nodes.find(node => node.title === f.reference.title)!;
  const copiedParent = snippet.nodes.find(node => node.title === f.parent.title)!, copiedChild = snippet.nodes.find(node => node.title === f.child.title)!;
  expect(copiedReference.targetId).toBe(copiedSource.id); expect(copiedChild.parentId).toBe(copiedParent.id);
  expect(snippet.links.find(link => link.label === 'Evidence inside')?.parentId).toBe(copiedParent.id);
  expect(snippet.nodes.find(node => node.title === f.number.title)?.value).toBe(4);
  expect(copiedParent.collapsed).toBe(true);
  expect(snippet.assets[0].id).not.toBe(f.project.assets[0].id); expect(snippet.assets[0].base64).toBe(f.project.assets[0].base64);
  expect(snippet.snapshots[f.external.targetId!]?.value).toBe(9);
  expect(snippetHtml).not.toContain('<dialog'); expect(snippetHtml).toContain('<title>Selection with contents · Atlas Meridian</title>');
  expect(await page.locator('#meridian-data').textContent()).toBe(unchanged); expect(remote).toEqual([]);
  await page.goto(pathToFileURL(snippetPath).href);
  await expect(page.locator('.mv-title')).toHaveText('Selection with contents');
  await expect(page.getByRole('status')).toHaveText('Stopped');
  await expect(card(page, copiedChild.id)).toHaveCount(0);
  await title(page, copiedParent.id).dblclick(); await expect(card(page, copiedChild.id)).toBeVisible();
  await page.screenshot({ path: info.outputPath('snippet-viewer.png') });

  await page.unroute(/^https?:/); await page.goto('/');
  const windowCount = page.context().pages().length;
  const chooser = page.waitForEvent('filechooser'); await workspaceAction(page,'Import');
  await (await chooser).setFiles(snippetPath);
  await page.waitForURL(url => !!url.searchParams.get('project'));
  await expect(page.locator('.workspace')).toBeVisible();
  await expect(page.locator('[data-current-canvas]')).toContainText('Selection with contents');
  expect(page.context().pages()).toHaveLength(windowCount);
  const importedId = new URL(page.url()).searchParams.get('project')!;
  const imported = await (await request.get(`/api/projects/${importedId}`)).json() as MeridianProject;
  expect(imported.id).not.toBe(snippet.id); expect(imported.nodes).toHaveLength(snippet.nodes.length);
  expect(imported.nodes.every(node => !snippet.nodes.some(old => old.id === node.id))).toBe(true);
  expect((await (await request.get(`/api/projects/${f.project.id}`)).json())).toEqual(f.project);
  expect(errors).toEqual([]);
});

test('viewer region selection makes partial intersections explicit and never moves authored nodes', async ({ page }, info) => {
  const project = newProject('Choose a region');
  const nodes = [0, 1, 2].map(index => { const node = addNode(project, project.entryCanvasId, 'text', `Region ${index + 1}`); Object.assign(node, { x: 100 + index * 320, y: 120, width: 240, height: 180, text: 'Select this tile without moving it.' }); return node; });
  const html = buildPublication(commitSnapshots(project), await readFile('dist/viewer.js', 'utf8'), await readFile('dist/scene.css', 'utf8'));
  const file = info.outputPath('region-offline.html'); await writeFile(file, html); await page.route(/^https?:/, route => route.abort()); await page.goto(pathToFileURL(file).href);
  const original = await page.locator('#meridian-data').textContent();
  const originalPositions = await page.locator('.ms-node').evaluateAll(elements => elements.map(node => ({ id: (node as HTMLElement).dataset.nodeId, left: (node as HTMLElement).style.left, top: (node as HTMLElement).style.top })));
  await page.getByRole('button', { name: 'Select area', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Select area', exact: true })).toHaveAttribute('aria-pressed', 'true');
  await expect(page.getByRole('combobox', { name: 'Region selection boundary', exact: true })).toHaveValue('contained');
  const a = (await card(page, nodes[0].id).boundingBox())!, b = (await card(page, nodes[1].id).boundingBox())!;
  const drag = async () => { await page.mouse.move(a.x - 15, a.y - 15); await page.mouse.down(); await page.mouse.move(b.x + b.width / 2, b.y + b.height + 15, { steps: 8 }); await page.mouse.up(); };
  await drag(); await expect(page.locator('.mv-selection-count')).toHaveText('1 selected');
  await expect(card(page, nodes[0].id)).toHaveAttribute('aria-selected', 'true'); await expect(card(page, nodes[1].id)).toHaveAttribute('aria-selected', 'false');
  await page.getByRole('combobox', { name: 'Region selection boundary', exact: true }).selectOption('intersecting');
  await drag(); await expect(page.locator('.mv-selection-count')).toHaveText('2 selected');
  await expect(card(page, nodes[1].id)).toHaveAttribute('aria-selected', 'true'); await expect(card(page, nodes[2].id)).toHaveAttribute('aria-selected', 'false');
  await title(page, nodes[2].id).click({ modifiers: ['Control'] }); await expect(page.locator('.mv-selection-count')).toHaveText('3 selected');
  await title(page, nodes[1].id).click({ modifiers: ['Shift'] }); await expect(page.locator('.mv-selection-count')).toHaveText('2 selected');
  await page.screenshot({ path: info.outputPath('region-selection.png') });
  expect(await page.locator('.ms-node').evaluateAll(elements => elements.map(node => ({ id: (node as HTMLElement).dataset.nodeId, left: (node as HTMLElement).style.left, top: (node as HTMLElement).style.top })))).toEqual(originalPositions);
  expect(await page.locator('#meridian-data').textContent()).toBe(original);
  await page.getByRole('button', { name: 'Clear selected nodes', exact: true }).click(); await expect(page.locator('.mv-selection-count')).toHaveText('0 selected');
  await expect(page.getByRole('button', { name: 'Download selected nodes', exact: true })).toBeDisabled();
});
