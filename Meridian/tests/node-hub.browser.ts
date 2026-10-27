import { test, expect, type APIRequestContext, type Locator, type Page } from '@playwright/test';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { addCanvas, addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import type { MeridianNode, MeridianProject } from '../src/core/types.ts';
import { buildPublication, readPublication } from '../src/publication.ts';
import { evaluate } from '../src/core/engine.ts';

const png = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLbtAAAAABJRU5ErkJggg==', 'base64');
const markdown = '# Thunderbolt\n\nA supporting account that stays outside the primary graph.\n';
async function saved(request: APIRequestContext, id: string): Promise<MeridianProject> { const response = await request.get(`/api/projects/${id}`); expect(response.ok()).toBe(true); return response.json(); }
async function install(page: Page, request: APIRequestContext, legacy = false) {
  const project = newProject(legacy ? 'Legacy Node Hub' : 'Node Hub workflow');
  const node = addNode(project, project.entryCanvasId, 'media', 'Zeus');
  Object.assign(node, { x: 100, y: 100, tag: 'Zeus', width: 300, height: 180 });
  const peer = addNode(project, project.entryCanvasId, 'media', 'Hera');
  Object.assign(peer, { x: 650, y: 100, tag: 'Hera', width: 260, height: 160, text: 'An independent peer.' });
  if (legacy) {
    project.assets.push({ id: 'legacy-image', name: 'Zeus.png', mime: 'image/png', base64: png.toString('base64') }, { id: 'legacy-account', name: 'Account.md', mime: 'text/markdown', base64: Buffer.from(markdown).toString('base64') });
    Object.assign(node, { contentType: 'asset', assetId: 'legacy-image', text: 'A retained original caption.', attachments: ['legacy-account'], hyperlinks: [{ url: 'https://example.com/zeus', label: 'Account source' }] });
  }
  const response = await request.post('/api/projects', { data: commitSnapshots(project) }); expect(response.ok(), await response.text()).toBe(true);
  const baseline: MeridianProject = await response.json();
  await page.goto(`/?project=${project.id}&canvas=${project.entryCanvasId}`);
  await expect(page.locator('.workspace')).toBeVisible();
  return { project: baseline, node };
}
const tile = (page: Page, node: MeridianNode) => page.locator(`.ms-node[data-node-id="${node.id}"]`);
async function selectNode(page: Page, node: MeridianNode): Promise<Locator> {
  await tile(page, node).click({ position: { x: 12, y: 12 } });
  const hub = page.getByRole('complementary', { name: 'Node Hub', exact: true }); await expect(hub).toBeVisible(); return hub;
}
async function addText(page: Page, hub: Locator, text: string) {
  await hub.getByRole('button', { name: 'Add content', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'Add node content', exact: true });
  await dialog.getByLabel('Content type', { exact: true }).selectOption('text'); await dialog.getByLabel('Text', { exact: true }).fill(text);
  await dialog.getByRole('button', { name: 'Add content', exact: true }).click(); await expect(dialog).toHaveCount(0);
}
async function addFile(page: Page, hub: Locator, type: string, name: string, mimeType: string, buffer: Buffer) {
  await hub.getByRole('button', { name: 'Add content', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'Add node content', exact: true });
  await dialog.getByLabel('Content type', { exact: true }).selectOption(type);
  await dialog.getByLabel('Content file', { exact: true }).setInputFiles({ name, mimeType, buffer });
  await dialog.getByRole('button', { name: 'Add content', exact: true }).click(); await expect(dialog).toHaveCount(0);
}

test('an inherited Reference Hub becomes a local content override without changing its original or observed value', async ({ page, request }) => {
  const project = newProject('Reference Hub isolation');
  const source = addNode(project, project.entryCanvasId, 'number', 'Zeus');
  Object.assign(source, { value: 9, contentItems: [{ id: 'source-text', type: 'text', text: 'Original account' }, { id: 'source-image', type: 'asset', assetId: 'source-asset' }] });
  project.assets.push({ id: 'source-asset', name: 'Zeus.png', mime: 'image/png', base64: png.toString('base64') });
  const consumer = addCanvas(project, 'Observation'), ref = addNode(project, consumer.id, 'reference', 'Observation');
  Object.assign(ref, { targetId: source.id, tag: 'Observation', x: 100, y: 100, width: 300, height: 180 });
  const peer = addNode(project, consumer.id, 'media', 'Athena'); Object.assign(peer, { x: 600, y: 100, width: 260, height: 180 });
  const response = await request.post('/api/projects', { data: commitSnapshots(project) }); expect(response.ok(), await response.text()).toBe(true);
  const baseline: MeridianProject = await response.json();
  await page.goto(`/?project=${project.id}&canvas=${consumer.id}`); await expect(page.locator('.workspace')).toBeVisible();
  const hub = await selectNode(page, ref);
  await expect(hub).toContainText('From original'); await expect(hub.getByLabel('Title', { exact: true })).toHaveValue('Observation');
  await expect(hub.locator('[data-content-item]')).toHaveCount(2);
  expect((await saved(request, project.id)).nodes.find(n => n.id === ref.id)?.contentItems).toBeUndefined();
  await addText(page, hub, 'Local supporting account');
  await expect(hub).not.toContainText('From original'); await expect(hub.locator('[data-content-item]')).toHaveCount(3);
  await expect.poll(async () => (await saved(request, project.id)).nodes.find(n => n.id === ref.id)?.contentItems?.length).toBe(3);
  const after = await saved(request, project.id), own = after.nodes.find(n => n.id === ref.id)!;
  expect(after.nodes.find(n => n.id === source.id)).toEqual(baseline.nodes.find(n => n.id === source.id));
  expect(after.assets).toEqual(baseline.assets); expect(own.targetId).toBe(source.id); expect(own.title).toBe('Observation'); expect(own.tag).toBe('Observation');
  expect(evaluate(after).values[ref.id]).toEqual(evaluate(baseline).values[ref.id]); expect(evaluate(after).values[ref.id]).toBe(9);
});

test('the Work Text tool edits the display item and clicking Hub Add flushes the live passage without reverting it', async ({ page, request }) => {
  const project = newProject('Canonical Text tool');
  const node = addNode(project, project.entryCanvasId, 'media', 'Zeus');
  Object.assign(node, { x: 100, y: 100, width: 320, height: 180, text: 'Dormant legacy passage', contentItems: [{ id: 'canonical-text', type: 'text', text: 'Visible passage' }] });
  const response = await request.post('/api/projects', { data: commitSnapshots(project) }); expect(response.ok(), await response.text()).toBe(true);
  await page.goto(`/?project=${project.id}&canvas=${project.entryCanvasId}`); await expect(page.locator('.workspace')).toBeVisible();
  await page.getByRole('button', { name: 'Text', exact: true }).click();
  const body = page.locator(`[data-editable-node-id="${node.id}"][data-editable-text="body"]`);
  await body.click(); await expect(body).toHaveAttribute('contenteditable', 'true'); await expect(body).toHaveText('Visible passage');
  await body.fill('Updated display passage');
  const hub = page.getByRole('complementary', { name: 'Node Hub', exact: true });
  await hub.getByRole('button', { name: 'Add content', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'Add node content', exact: true }); await expect(dialog).toBeVisible();
  await dialog.getByLabel('Text', { exact: true }).fill('Supporting text'); await dialog.getByRole('button', { name: 'Add content', exact: true }).click();
  await expect.poll(async () => (await saved(request, project.id)).nodes.find(n => n.id === node.id)?.contentItems?.[0]).toMatchObject({ id: 'canonical-text', type: 'text', text: 'Updated display passage' });
  expect((await saved(request, project.id)).nodes.find(n => n.id === node.id)?.text).toBe('Dormant legacy passage');
  await expect(hub.locator('[data-content-item]')).toHaveCount(2);
});

test('actual editor presents a concise family graph with artwork and documents retained in the selected Hub', async ({ page, request }) => {
  const priorRaw = JSON.parse(await readFile('output/mythology-current-rework-20261023/baseline/e35cba82-f644-416a-ac14-12645893ed88.json', 'utf8'));
  const prior: MeridianProject = priorRaw.project ?? priorRaw, original = prior.assets.find(a => a.id === 'b13d952e-7f7c-42d5-b449-20c3816ef565')!;
  const p = newProject('Olympians'); p.canvases[0].title = 'Olympians'; p.canvases[0].style = { background: '#0c1c24', gridVisible: false, backgroundPattern: 'none' };
  const root = p.nodes.find(n => n.id === p.canvases[0].rootId)!; root.title = 'Olympians'; root.tag = 'Olympians';
  p.assets.push({ ...original, id: 'qa-zeus-artwork', name: 'Probable Zeus.jpg' }, { id: 'qa-zeus-account', name: 'Zeus.md', mime: 'text/markdown', base64: Buffer.from('# Zeus\n\nKingship, the thunderbolt, and the protection of guests.\n\n## Artwork\n\nProbable Zeus. The Metropolitan Museum of Art, object 252511. The museum identification remains qualified.\n\n[Collection record](https://www.metmuseum.org/art/collection/search/252511) · [Open Access, CC0](https://www.metmuseum.org/hubs/open-access)\n\nOriginal embedded asset: zeus-met-252511.jpg.').toString('base64') }, { id: 'qa-zeus-family', name: 'Family.csv', mime: 'text/csv', base64: Buffer.from('Figure,Relationship\nHera,Consort\nAthena,Daughter\nApollo,Son').toString('base64') });
  const style = { fill: '#17343c', border: '#608b86', textColor: '#e9f0ed', accent: '#b3d7c6', titleSize: 30, fontSize: 19, padding: 22, radius: 0, textAlign: 'left' as const };
  const zeus = addNode(p, p.entryCanvasId, 'media', 'Zeus'); Object.assign(zeus, { x: 100, y: 100, width: 280, height: 170, sizeMode: 'fixed', style, contentItems: [{ id: 'zeus-primary', type: 'text', text: 'Kingship · thunderbolt' }, { id: 'zeus-artwork', type: 'asset', assetId: 'qa-zeus-artwork' }, { id: 'zeus-account', type: 'asset', assetId: 'qa-zeus-account' }, { id: 'zeus-family', type: 'asset', assetId: 'qa-zeus-family' }] });
  const hera = addNode(p, p.entryCanvasId, 'media', 'Hera'); Object.assign(hera, { x: 100, y: 410, width: 280, height: 170, sizeMode: 'fixed', style, contentItems: [{ id: 'hera-primary', type: 'text', text: 'Marriage · queenship' }] });
  const athena = addNode(p, p.entryCanvasId, 'media', 'Athena'); Object.assign(athena, { x: 560, y: 100, width: 280, height: 170, sizeMode: 'fixed', style, contentItems: [{ id: 'athena-primary', type: 'text', text: 'Wisdom · craft' }] });
  const apollo = addNode(p, p.entryCanvasId, 'media', 'Apollo'); Object.assign(apollo, { x: 560, y: 410, width: 280, height: 170, sizeMode: 'fixed', style, contentItems: [{ id: 'apollo-primary', type: 'text', text: 'Music · prophecy' }] });
  const linkStyle = { color: '#7baca0', width: 2 };
  p.links.push({ id: 'qa-consort', canvasId: p.entryCanvasId, sourceId: zeus.id, targetId: hera.id, kind: 'visual', relation: 'causal', label: 'consort', sourceAnchor: { x: .5, y: 1 }, targetAnchor: { x: .5, y: 0 }, style: linkStyle }, { id: 'qa-daughter', canvasId: p.entryCanvasId, sourceId: zeus.id, targetId: athena.id, kind: 'visual', relation: 'causal', label: 'daughter', sourceAnchor: { x: 1, y: .5 }, targetAnchor: { x: 0, y: .5 }, style: linkStyle }, { id: 'qa-son', canvasId: p.entryCanvasId, sourceId: zeus.id, targetId: apollo.id, kind: 'visual', relation: 'causal', label: 'son', sourceAnchor: { x: 1, y: .8 }, targetAnchor: { x: 0, y: .5 }, points: [{ x: 470, y: 236 }, { x: 470, y: 495 }], style: linkStyle });
  const response = await request.post('/api/projects', { data: commitSnapshots(p) }); expect(response.ok(), await response.text()).toBe(true);
  await page.goto(`/?project=${p.id}&canvas=${p.entryCanvasId}`); await expect(page.locator('.workspace')).toBeVisible();
  const hub = await selectNode(page, zeus), image = hub.locator('img');
  await expect.poll(() => image.evaluate((el: HTMLImageElement) => el.complete && el.naturalWidth > 1)).toBe(true);
  await expect(tile(page, zeus)).toContainText('Kingship · thunderbolt'); await expect(tile(page, zeus).locator('img')).toHaveCount(0);
  const out = 'output/node-hub-cycle-20261024/ui-review'; await mkdir(out, { recursive: true });
  await page.getByRole('combobox', { name: 'Side panel', exact: true }).selectOption('appearance');
  await page.getByRole('button', { name: 'Fit Work', exact: true }).click();
  await page.screenshot({ path: `${out}/node-hub-1500.png` });
  await page.setViewportSize({ width: 2048, height: 1200 }); await page.getByRole('button', { name: 'Fit Work', exact: true }).click();
  await page.screenshot({ path: `${out}/node-hub-2048.png` });
  await writeFile(`${out}/fixture.json`, JSON.stringify(await saved(request, p.id), null, 2));
  await writeFile(`${out}/capture.json`, JSON.stringify({ projectId: p.id, isolatedHost: 'http://127.0.0.1:5317', originalAssetId: original.id, originalAssetName: original.name, byteExactImage: p.assets[0].base64 === original.base64, attributionAsset: 'qa-zeus-account', screenshots: ['node-hub-1500.png', 'node-hub-2048.png'], liveWrites: false }, null, 2));
});

test('selected Hub owns title and ordered content; drag promotes the display item and survives undo and reopen', async ({ page, request }, info) => {
  const f = await install(page, request); await expect(page.locator('.node-hub')).toHaveCount(0);
  const hub = await selectNode(page, f.node);
  await expect(hub.getByLabel('Title', { exact: true })).toHaveValue('Zeus');
  await expect(hub.getByLabel('Show title on Work', { exact: true })).toBeChecked();
  await expect(hub.locator('[data-content-item]')).toHaveCount(0);
  expect(await page.getByRole('combobox', { name: 'Side panel', exact: true }).locator('option').allTextContents()).not.toContain('Content');
  const left = (await page.locator('.inspector').boundingBox())!, center = (await page.locator('.canvas-column').boundingBox())!, right = (await hub.boundingBox())!;
  expect(left.x).toBeCloseTo(0, 0); expect(center.x).toBeGreaterThanOrEqual(left.width - 1); expect(right.x).toBeGreaterThanOrEqual(center.x + center.width - 1);

  await hub.getByLabel('Title', { exact: true }).fill('Zeus the thunderer');
  await addText(page, hub, 'The thunderbolt.'); await expect(tile(page, f.node)).toContainText('The thunderbolt.');
  await expect(hub.getByLabel('Title', { exact: true })).toHaveValue('Zeus the thunderer');
  const firstBounds = (await tile(page, f.node).boundingBox())!;
  await addFile(page, hub, 'image', 'Thunderbolt.png', 'image/png', png);
  await expect(hub.locator('[data-content-item]')).toHaveCount(2);
  const afterSupporting = (await tile(page, f.node).boundingBox())!;
  expect(afterSupporting.width).toBeCloseTo(firstBounds.width, 0); expect(afterSupporting.height).toBeCloseTo(firstBounds.height, 0);
  await expect(tile(page, f.node).locator('img')).toHaveCount(0);
  const imageCard = hub.locator('[data-content-item]').filter({ has: page.locator('.nh-item-name', { hasText: 'Thunderbolt.png' }) });
  await imageCard.getByRole('button', { name: 'Move content', exact: true }).dragTo(hub.locator('[data-content-item]').first(), { targetPosition: { x: 20, y: 15 } });
  await expect(hub.locator('[data-content-item]').first()).toHaveAttribute('data-content-type', 'asset');
  await expect(tile(page, f.node).locator('img')).toHaveCount(1);
  await expect.poll(() => tile(page, f.node).locator('img').evaluate((img: HTMLImageElement) => img.complete && img.naturalWidth > 0)).toBe(true);
  await expect(tile(page, f.node)).not.toContainText('The thunderbolt.');
  await page.getByRole('button', { name: 'Undo', exact: true }).click(); await expect(hub.locator('[data-content-item]').first()).toHaveAttribute('data-content-type', 'text');
  await page.getByRole('button', { name: 'Redo', exact: true }).click(); await expect(hub.locator('[data-content-item]').first()).toHaveAttribute('data-content-type', 'asset');

  await addFile(page, hub, 'markdown', 'Account.md', 'text/markdown', Buffer.from(markdown));
  const account = hub.locator('[data-content-item]').filter({ has: page.locator('.nh-item-name', { hasText: 'Account.md' }) });
  await expect(account.getByRole('region', { name: 'Document viewer: Account.md', exact: true })).toBeVisible();
  await account.getByRole('button', { name: 'Pop out content', exact: true }).click();
  const reader = page.locator('dialog.nh-popout'); await expect(reader).toBeVisible(); await expect(reader).toContainText('A supporting account');
  await reader.getByRole('button', { name: 'Close content viewer', exact: true }).click();
  await hub.getByLabel('Title', { exact: true }).fill('Zeus Olympios'); await hub.getByLabel('Title', { exact: true }).press('Tab');
  await expect(tile(page, f.node).locator('.ms-node-title')).toHaveText('Zeus Olympios');
  await hub.getByLabel('Show title on Work', { exact: true }).uncheck();
  await expect(tile(page, f.node).locator('.ms-node-title')).toHaveCount(0);
  await expect(hub.getByLabel('Title', { exact: true })).toHaveValue('Zeus Olympios');
  await expect.poll(async () => (await saved(request, f.project.id)).nodes.find(n => n.id === f.node.id)?.showTitle).toBe(false);
  const final = await saved(request, f.project.id), finalNode = final.nodes.find(n => n.id === f.node.id)!;
  expect(final.nodes.length).toBe(f.project.nodes.length); expect(final.links).toEqual(f.project.links); expect(finalNode.title).toBe('Zeus Olympios'); expect(finalNode.tag).toBe('Zeus'); expect(finalNode.contentItems).toHaveLength(3);
  const ids = finalNode.contentItems!.map(item => item.id);
  await page.reload(); await expect(page.locator('.workspace')).toBeVisible();
  await selectNode(page, f.node); await expect(hub.getByLabel('Show title on Work', { exact: true })).not.toBeChecked();
  await expect(hub.getByLabel('Title', { exact: true })).toHaveValue('Zeus Olympios');
  expect(await hub.locator('[data-content-item]').evaluateAll(cards => cards.map(card => (card as HTMLElement).dataset.contentItem))).toEqual(ids);
  await expect(tile(page, f.node).locator('img')).toHaveCount(1);
  await page.screenshot({ path: info.outputPath('node-hub-first-slot-and-readers.png') });
  await page.setViewportSize({ width: 2048, height: 1200 });
  await page.screenshot({ path: info.outputPath('node-hub-wide-layout.png') });
  await hub.getByRole('button', { name: 'Clear node selection', exact: true }).click(); await expect(page.locator('.node-hub')).toHaveCount(0);
});

test('legacy mixed content is readable without migration and canonical edits preserve original material', async ({ page, request }) => {
  const f = await install(page, request, true), hub = await selectNode(page, f.node);
  await expect(hub.locator('[data-content-item]')).toHaveCount(4);
  const opened = await saved(request, f.project.id), original = opened.nodes.find(n => n.id === f.node.id)!;
  expect(original.contentItems).toBeUndefined(); expect(opened.assets).toEqual(f.project.assets);
  const text = hub.locator('[data-content-type="text"]');
  await text.getByRole('button', { name: 'Move content up', exact: true }).click();
  await expect(tile(page, f.node)).toContainText('A retained original caption.');
  await expect(tile(page, f.node).locator('img')).toHaveCount(0);
  await expect.poll(async () => (await saved(request, f.project.id)).nodes.find(n => n.id === f.node.id)?.contentItems?.[0]?.type).toBe('text');
  const after = await saved(request, f.project.id), changed = after.nodes.find(n => n.id === f.node.id)!;
  expect(changed.text).toBe(original.text); expect(changed.assetId).toBe(original.assetId); expect(changed.attachments).toEqual(original.attachments); expect(changed.hyperlinks).toEqual(original.hyperlinks); expect(after.assets).toEqual(f.project.assets);
  await hub.locator('[data-content-item]').filter({ has: page.locator('.nh-item-name', { hasText: 'Zeus.png' }) }).getByRole('button', { name: 'Remove content', exact: true }).click();
  await expect(hub.locator('[data-content-item]')).toHaveCount(3); await expect(tile(page, f.node)).toContainText('A retained original caption.');
  await page.getByRole('button', { name: 'Undo', exact: true }).click(); await expect(hub.locator('[data-content-item]')).toHaveCount(4);
  await expect.poll(async () => (await saved(request, f.project.id)).nodes.find(n => n.id === f.node.id)?.contentItems?.length).toBe(4);
  await hub.getByRole('button', { name: 'Add content', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'Add node content', exact: true });
  await dialog.getByLabel('Content type', { exact: true }).selectOption('image');
  await dialog.getByLabel('Content file', { exact: true }).setInputFiles({ name: 'Cancelled.png', mimeType: 'image/png', buffer: png });
  await dialog.getByRole('button', { name: 'Cancel', exact: true }).click();
  await expect(hub.locator('[data-content-item]')).toHaveCount(4);
  expect((await saved(request, f.project.id)).assets).toEqual(after.assets);
});

test('typed table form, editing, supporting links and published readers retain the whole stack without source writeback', async ({ page, request, context }) => {
  const f = await install(page, request), hub = await selectNode(page, f.node);
  await addText(page, hub, 'Zeus');
  await hub.getByRole('button', { name: 'Add content', exact: true }).click();
  let dialog = page.getByRole('dialog', { name: 'Add node content', exact: true });
  const choices = await dialog.getByLabel('Content type', { exact: true }).locator('option').allTextContents();
  for (const required of ['Text', 'Image', 'Video', 'Audio', 'Markdown', 'CSV / TSV table', 'PDF', 'Text / code document', 'Hyperlink', 'File attachment']) expect(choices).toContain(required);
  await dialog.getByLabel('Content type', { exact: true }).selectOption('table'); await dialog.getByLabel('Source', { exact: true }).selectOption('source');
  await dialog.getByLabel('File name', { exact: true }).fill('Family.csv'); await dialog.getByLabel('Table data', { exact: true }).fill('Name,Parent\nAthena,Zeus\nApollo,Zeus');
  await dialog.getByRole('button', { name: 'Add content', exact: true }).click();
  const table = hub.locator('[data-content-item]').filter({ has: page.locator('.nh-item-name', { hasText: 'Family.csv' }) });
  await expect(table.getByRole('region', { name: 'Document viewer: Family.csv', exact: true })).toContainText('Athena');
  await expect(tile(page, f.node)).not.toContainText('Athena');
  await hub.locator('[data-content-item]').first().getByRole('button', { name: 'Edit content', exact: true }).click();
  dialog = page.getByRole('dialog', { name: 'Edit node content', exact: true }); await dialog.getByLabel('Text', { exact: true }).fill('Olympian');
  await dialog.getByRole('button', { name: 'Save content', exact: true }).click(); await expect(tile(page, f.node)).toContainText('Olympian');
  await hub.getByRole('button', { name: 'Add content', exact: true }).click();
  dialog = page.getByRole('dialog', { name: 'Add node content', exact: true }); await dialog.getByLabel('Content type', { exact: true }).selectOption('link');
  await dialog.getByLabel('URL', { exact: true }).fill('https://example.com/mythology'); await dialog.getByLabel('Label', { exact: true }).fill('Tradition'); await dialog.getByRole('button', { name: 'Add content', exact: true }).click();
  await expect.poll(async () => (await saved(request, f.project.id)).nodes.find(n => n.id === f.node.id)?.contentItems?.length).toBe(3);
  await hub.getByLabel('Show title on Work', { exact: true }).uncheck();
  await expect.poll(async () => (await saved(request, f.project.id)).nodes.find(n => n.id === f.node.id)?.showTitle).toBe(false);
  const source = await saved(request, f.project.id), viewerJs = await readFile('dist/viewer.js', 'utf8'), css = await readFile('dist/scene.css', 'utf8');
  const native = await request.post('/api/export', { data: source }); expect(native.ok(), await native.text()).toBe(true);
  const reimport = await request.post('/api/import', { multipart: { file: { name: 'Node Hub.meridian', mimeType: 'application/octet-stream', buffer: await native.body() } } });
  expect(reimport.ok(), await reimport.text()).toBe(true);
  const nativeCopy: MeridianProject = await reimport.json();
  expect(nativeCopy.nodes.find(n => n.id === f.node.id)?.contentItems).toEqual(source.nodes.find(n => n.id === f.node.id)?.contentItems);
  expect(nativeCopy.nodes.find(n => n.id === f.node.id)?.showTitle).toBe(false);
  expect(nativeCopy.assets).toEqual(source.assets);
  const html = buildPublication(source, viewerJs, css);
  const imported = readPublication(html), copied = imported.nodes.find(n => n.title === 'Zeus')!;
  expect(copied.contentItems?.map(item => item.type)).toEqual(['text', 'asset', 'link']);
  expect(copied.showTitle).toBe(false);
  const viewer = await context.newPage(); await viewer.route(/^https?:/, route => route.abort()); await viewer.setContent(html, { waitUntil: 'domcontentloaded' });
  await expect(viewer.locator('.ms-node').filter({ hasText: 'Olympian' })).toBeVisible();
  await viewer.locator(`.ms-node[data-node-id="${f.node.id}"]`).click({ position: { x: 12, y: 12 } });
  const publishedHub = viewer.getByRole('region', { name: 'Node Hub: Zeus', exact: true });
  await expect(publishedHub).toBeVisible(); await expect(publishedHub.locator('[data-content-item-id]')).toHaveCount(3);
  const publishedTable = publishedHub.locator('[data-content-item-id]').filter({ has: viewer.locator('.nh-content-heading strong', { hasText: 'Family.csv' }) });
  await publishedTable.locator(':scope > div:last-child').scrollIntoViewIfNeeded();
  await expect(publishedHub.getByRole('region', { name: 'Document viewer: Family.csv', exact: true })).toContainText('Athena');
  const publishedLink = publishedHub.locator('[data-content-item-id]').filter({ has: viewer.locator('.nh-content-heading strong', { hasText: 'Tradition' }) });
  await publishedLink.locator(':scope > div:last-child').scrollIntoViewIfNeeded();
  await expect(publishedHub.getByRole('link', { name: 'Tradition', exact: true })).toHaveAttribute('href', 'https://example.com/mythology');
  await viewer.close(); expect(await saved(request, f.project.id)).toEqual(source);
});
