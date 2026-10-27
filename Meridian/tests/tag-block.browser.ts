import { test, expect, type Page, type APIRequestContext } from '@playwright/test';
import { readFile } from 'node:fs/promises';
import { addNode, commitSnapshots, forkProject, newProject } from '../src/core/model.ts';
import type { MeridianProject, StoredModule } from '../src/core/types.ts';
import { buildPublication, readPublication } from '../src/publication.ts';
import { inspectorPanel } from './ui-helpers.ts';

const tile = (page: Page, id: string) => page.locator(`.ms-node[data-node-id="${id}"]`);
const row = (page: Page, id: string) => page.locator(`[data-outline-node="${id}"]`);
const stored = async (request: APIRequestContext, id: string): Promise<MeridianProject> => (await request.get(`/api/projects/${id}`)).json();
const search = (page: Page) => page.getByRole('searchbox', { name: 'Search Work nodes', exact: true });
const errors = new WeakMap<Page, string[]>();
test.beforeEach(async ({ page }) => { const messages: string[] = []; errors.set(page, messages); page.on('pageerror', error => messages.push(error.message)); });
test.afterEach(async ({ page }) => expect(errors.get(page) ?? []).toEqual([]));
async function selectNode(page: Page, id: string) {
  await inspectorPanel(page, 'canvas'); await search(page).fill(id); await row(page, id).locator('[data-jump-to-node]').click(); await search(page).fill('');
}
function fixture() {
  const project = newProject('Tags and blank blocks'), canvas = project.canvases[0]!; canvas.snapping = { stickyEdges: false, smartAlign: false, snapToGrid: false };
  const legacy = addNode(project, canvas.id, 'media', 'Legacy Beacon'); delete legacy.tag; Object.assign(legacy, { x: 80, y: 90, width: 230, height: 140, sizeMode: 'fixed', text: '' });
  const media = addNode(project, canvas.id, 'media', 'Visible media heading'); Object.assign(media, { tag: 'Research identity', x: 430, y: 90, width: 260, height: 160, sizeMode: 'fixed', contentType: 'text', text: 'Dormant source text retained during type conversion.' });
  const block = addNode(project, canvas.id, 'block', ''); Object.assign(block, { tag: 'Blank architectural block', x: 800, y: 90, width: 180, height: 130, sizeMode: 'fixed', style: { shape: 'hexagon', fill: '#315e52' } });
  return { project, canvas, legacy, media, block };
}
async function open(page: Page, request: APIRequestContext) {
  const f = fixture(), response = await request.post('/api/projects', { data: commitSnapshots(f.project) }); expect(response.ok(), await response.text()).toBe(true);
  await page.goto(`/?project=${f.project.id}`); await expect(tile(page, f.legacy.id)).toBeVisible(); return f;
}

test('legacy identities become Tags without rewriting on open, and Title can stay empty after saving', async ({ page, request }, info) => {
  const f = await open(page, request), original = await stored(request, f.project.id);
  expect(original.nodes.find(node => node.id === f.legacy.id)?.tag).toBeUndefined();
  await selectNode(page, f.legacy.id); await inspectorPanel(page, 'properties'); const tag = page.locator('.inspector').getByLabel('Tag', { exact: true }); await expect(tag).toHaveValue('Legacy Beacon');
  await inspectorPanel(page, 'content'); const title = page.locator('.inspector').getByLabel('Title', { exact: true }); await title.fill(''); await title.press('Tab');
  await expect.poll(async () => (await stored(request, f.project.id)).nodes.find(node => node.id === f.legacy.id)?.title).toBe('');
  const saved = await stored(request, f.project.id); expect(saved.nodes.find(node => node.id === f.legacy.id)?.tag).toBe('Legacy Beacon'); expect(saved.snapshots[f.legacy.id]?.node.tag).toBe('Legacy Beacon');
  await expect(tile(page, f.legacy.id).locator('.ms-node-title')).toBeHidden(); await expect(tile(page, f.legacy.id)).not.toContainText('Untitled');
  await inspectorPanel(page, 'canvas'); for (const query of ['Legacy Beacon', f.legacy.id]) { await search(page).fill(query); await expect(row(page, f.legacy.id)).toBeVisible(); await expect(row(page, f.legacy.id).locator('.inspector-tree-name')).toHaveText('Legacy Beacon'); }
  await selectNode(page, f.legacy.id); await inspectorPanel(page, 'properties'); await tag.fill('Archive / Θεός'); await tag.press('Tab');
  await expect.poll(async () => (await stored(request, f.project.id)).nodes.find(node => node.id === f.legacy.id)?.tag).toBe('Archive / Θεός');
  await page.reload(); await selectNode(page, f.legacy.id); await inspectorPanel(page, 'content'); await expect(title).toHaveValue(''); await expect(tile(page, f.legacy.id)).toHaveText('');
  await page.getByRole('button', { name: 'Text', exact: true }).click(); await tile(page, f.legacy.id).click({ position: { x: 100, y: 70 } }); const inline = tile(page, f.legacy.id).locator('.ms-node-title[contenteditable="true"]'); await expect(inline).toBeVisible();
  await page.keyboard.insertText('Temporary visible title'); await inline.press('Control+a'); await page.keyboard.press('Backspace'); await page.keyboard.press('Escape'); await expect(tile(page, f.legacy.id).locator('.ms-node-title')).toBeHidden();
  await expect.poll(async () => (await stored(request, f.project.id)).nodes.find(node => node.id === f.legacy.id)?.title).toBe(''); expect((await stored(request, f.project.id)).nodes.find(node => node.id === f.legacy.id)?.tag).toBe('Archive / Θεός');
  await inspectorPanel(page, 'canvas'); await search(page).fill('Archive / Θεός'); await expect(row(page, f.legacy.id)).toBeVisible(); await page.getByRole('button', { name: 'Reset view', exact: true }).click(); await page.screenshot({ path: info.outputPath('tag-without-visible-title.png') });
  await selectNode(page, f.canvas.rootId); await inspectorPanel(page, 'content'); await title.fill(''); await title.press('Tab');
  await expect.poll(async () => (await stored(request, f.project.id)).nodes.find(node => node.id === f.canvas.rootId)?.title).toBe('');
  const blankRoot = await stored(request, f.project.id); expect(blankRoot.canvases[0]!.title).toBe(f.canvas.title); expect(blankRoot.title).toBe(f.project.title);
  await inspectorPanel(page, 'properties'); await tag.fill('Stable workspace identity'); await tag.press('Tab'); await expect.poll(async () => (await stored(request, f.project.id)).canvases[0]!.title).toBe('Stable workspace identity');
  expect((await stored(request, f.project.id)).nodes.find(node => node.id === f.canvas.rootId)?.title).toBe(''); expect((await stored(request, f.project.id)).title).toBe('Stable workspace identity');
});

test('New Node creates a tagged content-free Block and reversible conversion keeps dormant Media content', async ({ page, request }, info) => {
  const f = await open(page, request); await page.getByRole('button', { name: 'New Node', exact: true }).click(); await page.getByRole('menuitem', { name: 'Node', exact: true }).click();
  await page.locator('.ms-viewport').click({ position: { x: 730, y: 440 } }); const form = page.getByRole('dialog', { name: 'New Node', exact: true }); await expect(form).toBeVisible();
  await form.getByLabel('Type', { exact: true }).selectOption('block'); await form.getByLabel('Tag', { exact: true }).fill('New blank shape'); await expect(form.getByLabel('Title', { exact: true })).toBeHidden();
  await form.getByRole('button', { name: 'Create node', exact: true }).click();
  await expect.poll(async () => (await stored(request, f.project.id)).nodes.some(node => node.kind === 'block' && node.tag === 'New blank shape')).toBe(true);
  const created = (await stored(request, f.project.id)).nodes.find(node => node.kind === 'block' && node.tag === 'New blank shape')!; expect(created.title).toBe(''); await expect(tile(page, created.id)).toHaveText('');
  await page.keyboard.press('Escape');
  await selectNode(page, f.media.id); await inspectorPanel(page, 'properties'); const type = page.locator('.inspector').getByLabel('Type', { exact: true }); await type.selectOption('block');
  await expect.poll(async () => (await stored(request, f.project.id)).nodes.find(node => node.id === f.media.id)?.kind).toBe('block'); await expect(tile(page, f.media.id)).toHaveText('');
  let node = (await stored(request, f.project.id)).nodes.find(node => node.id === f.media.id)!; expect(node.tag).toBe('Research identity'); expect(node.title).toBe(f.media.title); expect(node.text).toBe(f.media.text);
  await inspectorPanel(page, 'content'); await expect(page.locator('.inspector').getByLabel('Title', { exact: true })).toHaveCount(0); await expect(page.locator('.inspector [contenteditable]')).toHaveCount(0);
  await inspectorPanel(page, 'properties'); await type.selectOption('media'); await expect.poll(async () => (await stored(request, f.project.id)).nodes.find(node => node.id === f.media.id)?.kind).toBe('media');
  await expect(tile(page, f.media.id)).toContainText(f.media.text!); node = (await stored(request, f.project.id)).nodes.find(node => node.id === f.media.id)!; expect(node.tag).toBe('Research identity');
  await page.screenshot({ path: info.outputPath('block-and-restored-media.png') });
});

test('blank Container headers consume no space and Tag/Block survive package, publication and fork', async ({ page, request }, info) => {
  const f = fixture(), parent = addNode(f.project, f.canvas.id, 'container', ''); Object.assign(parent, { tag: 'Tag-only enclosure', x: 80, y: 400, width: 320, height: 220, sizeMode: 'fixed', collapsed: false, containerVariant: 'vertical', childArrangement: 'column', childLayout: 'contained' });
  const child = addNode(f.project, f.canvas.id, 'block', ''); Object.assign(child, { tag: 'Embedded blank cell', parentId: parent.id, x: 86, y: 406, width: 200, height: 100, sizeMode: 'fixed' });
  const response = await request.post('/api/projects', { data: commitSnapshots(f.project) }); expect(response.ok(), await response.text()).toBe(true); await page.goto(`/?project=${f.project.id}`);
  await expect(tile(page, parent.id).locator('.ms-node-header')).toBeHidden(); await expect(tile(page, parent.id)).not.toContainText(parent.tag!);
  const geometry = await page.evaluate(({ parent, child }) => { const a = document.querySelector<HTMLElement>(`.ms-node[data-node-id="${parent}"]`)!, b = document.querySelector<HTMLElement>(`.ms-node[data-node-id="${child}"]`)!; return { parentY: parseFloat(a.style.top), childY: parseFloat(b.style.top) }; }, { parent: parent.id, child: child.id }); expect(geometry.childY - geometry.parentY).toBeLessThan(12);
  const saved = await stored(request, f.project.id), exported = await request.post('/api/export', { data: saved }); expect(exported.ok(), await exported.text()).toBe(true);
  const imported = await request.post('/api/import', { multipart: { file: { name: 'tags-and-blocks.meridian', mimeType: 'application/octet-stream', buffer: await exported.body() } } }); expect(imported.ok(), await imported.text()).toBe(true); const portable = await imported.json() as MeridianProject;
  expect(portable.nodes.find(node => node.id === f.block.id)).toMatchObject({ kind: 'block', tag: f.block.tag, title: '' }); expect(portable.snapshots[f.block.id]?.node.tag).toBe(f.block.tag);
  const fork = forkProject(portable); const forkSaved = await request.post('/api/projects', { data: commitSnapshots(fork) }); expect(forkSaved.ok(), await forkSaved.text()).toBe(true); expect(fork.nodes.find(node => node.tag === f.block.tag)?.id).not.toBe(f.block.id);
  const html = buildPublication(portable, await readFile('dist/viewer.js', 'utf8'), await readFile('dist/scene.css', 'utf8')), read = readPublication(html); const importedBlock = read.nodes.find(node => node.tag === f.block.tag); expect(importedBlock).toMatchObject({ tag: f.block.tag, kind: 'block' }); expect(importedBlock?.id).not.toBe(f.block.id);
  await page.route(/^https?:/, route => route.abort()); await page.setContent(html); await expect(tile(page, f.block.id)).toHaveText(''); await expect(tile(page, parent.id).locator('.ms-node-header')).toBeHidden(); await page.screenshot({ path: info.outputPath('offline-blank-blocks.png') });
});

test('clipboard and Module library recreate tagged Blocks with fresh GUIDs', async ({ page, request }) => {
  const f = await open(page, request); await selectNode(page, f.block.id); await page.locator('.ms-viewport').focus(); const before = await stored(request, f.project.id); await page.keyboard.press('Control+c'); await page.keyboard.press('Control+v');
  await expect.poll(async () => (await stored(request, f.project.id)).nodes.filter(node => node.kind === 'block').length).toBe(2); const pasted = (await stored(request, f.project.id)).nodes.find(node => node.kind === 'block' && !before.nodes.some(old => old.id === node.id))!; expect(pasted.tag).toBe(f.block.tag); expect(pasted.title).toBe(''); expect(pasted.id).not.toBe(f.block.id);
  await page.keyboard.press('Escape'); await page.getByRole('button', { name: 'New Node', exact: true }).click(); await page.getByRole('menuitem', { name: 'Create Module', exact: true }).click(); await tile(page, f.block.id).click();
  const name = `Tagged blocks ${f.project.id.slice(0, 8)}`; await page.getByRole('textbox', { name: 'Module name', exact: true }).fill(name); await page.getByRole('button', { name: 'Create From Selection', exact: true }).click();
  let catalog: Array<{ id: string; name: string }> = []; await expect.poll(async () => { catalog = await (await request.get('/api/modules')).json(); return catalog.some(item => item.name === name); }).toBe(true);
  const module = await (await request.get(`/api/modules/${catalog.find(item => item.name === name)!.id}`)).json() as StoredModule; expect(module.project.nodes.some(node => node.kind === 'block' && node.tag === f.block.tag && node.title === '')).toBe(true);
  await page.locator(`.module-library-card[data-module-id="${module.id}"]`).getByRole('button', { name: `Add ${name} to Canvas`, exact: true }).click(); await expect.poll(async () => (await stored(request, f.project.id)).nodes.filter(node => node.kind === 'block').length).toBe(3);
  const after = await stored(request, f.project.id), blocks = after.nodes.filter(node => node.kind === 'block'); expect(new Set(blocks.map(node => node.id)).size).toBe(3); expect(blocks.every(node => node.tag === f.block.tag && node.title === '')).toBe(true);
  await page.reload(); expect((await stored(request, f.project.id)).nodes.filter(node => node.kind === 'block')).toEqual(blocks);
});

test('host rejects malformed and oversized Tags in live nodes and saved snapshots atomically', async ({ request }) => {
  const f = fixture(), create = await request.post('/api/projects', { data: commitSnapshots(f.project) }); expect(create.ok(), await create.text()).toBe(true); const saved = await stored(request, f.project.id);
  for (const [snapshot, invalid] of [[false, 42], [false, 'x'.repeat(8001)], [true, 42], [true, 'x'.repeat(8001)]] as const) {
    const candidate = structuredClone(saved), node = snapshot ? candidate.snapshots[f.block.id]!.node : candidate.nodes.find(node => node.id === f.block.id)!; (node as unknown as Record<string, unknown>).tag = invalid;
    const rejected = await request.put(`/api/projects/${saved.id}`, { data: { project: candidate, expectedRevision: saved.revision } }); expect(rejected.status(), await rejected.text()).toBe(400); expect(await stored(request, saved.id)).toEqual(saved);
  }
  const valid = structuredClone(saved); valid.nodes.find(node => node.id === f.block.id)!.tag = 'x'.repeat(8000); const accepted = await request.put(`/api/projects/${saved.id}`, { data: { project: valid, expectedRevision: saved.revision } }); expect(accepted.ok(), await accepted.text()).toBe(true);
});

test('blank Container interiors still place a link pivot instead of selecting the enclosing node', async ({ page, request }, info) => {
  const f = fixture(), parent = addNode(f.project, f.canvas.id, 'container', ''); Object.assign(parent, { tag: 'Blank routing surface', x: 80, y: 400, width: 350, height: 240, sizeMode: 'fixed', collapsed: false, childLayout: 'contained', childArrangement: 'free' });
  const child = addNode(f.project, f.canvas.id, 'block', ''); Object.assign(child, { tag: 'One enclosed object', parentId: parent.id, x: 86, y: 406, width: 150, height: 90, childPosition: 'manual', sizeMode: 'fixed' });
  const response = await request.post('/api/projects', { data: commitSnapshots(f.project) }); expect(response.ok(), await response.text()).toBe(true); await page.goto(`/?project=${f.project.id}`);
  await page.getByRole('button', { name: 'New Node', exact: true }).click(); await page.getByRole('menuitem', { name: 'Link', exact: true }).click(); await tile(page, f.legacy.id).click();
  await expect(page.locator('.ms-link-draft')).toHaveCount(1); const bounds = (await tile(page, parent.id).boundingBox())!; await page.mouse.click(bounds.x + bounds.width / 2, bounds.y + bounds.height * .85);
  await expect(page.locator('.ms-link-draft-pivot')).toHaveCount(1); expect((await stored(request, f.project.id)).links).toHaveLength(0);
  await tile(page, f.media.id).click(); await expect.poll(async () => (await stored(request, f.project.id)).links.length).toBe(2); expect((await stored(request, f.project.id)).nodes.filter(node => node.utilityVariant === 'pivot')).toHaveLength(1);
  await page.screenshot({ path: info.outputPath('pivot-inside-titleless-container.png') });
});
