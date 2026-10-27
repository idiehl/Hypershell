import { test, expect, type APIRequestContext, type Page } from '@playwright/test';
import { readFile, writeFile } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';
import { addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import type { MeridianProject, StoredModule } from '../src/core/types.ts';
import { buildPublication } from '../src/publication.ts';

const tile = (page: Page, id: string) => page.locator(`.ms-node[data-node-id="${id}"]`);
const treeItem = (page: Page, id: string) => page.locator(`[data-module-selection-node="${id}"] > .module-selection-row`);
const readProject = async (request: APIRequestContext, id: string) => await (await request.get(`/api/projects/${id}`)).json() as MeridianProject;
const originalPng = 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/l9sAAAAASUVORK5CYII=';

function fixture() {
  const project = newProject('Module multi-selection QA');
  const add = (kind: Parameters<typeof addNode>[2], title: string, x: number, y: number, width: number, height: number) => {
    const node = addNode(project, project.entryCanvasId, kind, title);
    Object.assign(node, { x, y, width, height, sizeMode: 'fixed', childPosition: 'manual' }); return node;
  };
  const parent = add('container', 'Original assembly', 60, 70, 720, 360);
  Object.assign(parent, { collapsed: false, containerVariant: 'contained', childLayout: 'contained', childArrangement: 'free' });
  const photo = add('media', 'Original pixels', 80, 145, 180, 220);
  const locked = add('media', 'Locked neighbor', 260, 145, 180, 220);
  const excluded = add('media', 'Explicitly excluded', 470, 145, 220, 120);
  const outside = add('media', 'Another selected root', 820, 145, 220, 180);
  for (const child of [photo, locked, excluded]) child.parentId = parent.id;
  const asset = { id: crypto.randomUUID(), name: 'original.png', mime: 'image/png', base64: originalPng };
  project.assets.push(asset); photo.contentType = 'asset'; photo.assetId = asset.id;
  photo.style = { fill: '#183d36', border: '#85c5b7', borderWidth: 2, textAlign: 'center' };
  locked.text = 'Snapped edges are stored as relationships.'; outside.text = 'This root is included without reparenting the original.';
  const snap = { id: crypto.randomUUID(), canvasId: project.entryCanvasId, sourceId: photo.id, targetId: locked.id, kind: 'visual' as const, relation: 'generic' as const, direction: 'none' as const, snap: { locked: true } };
  const route = { id: crypto.randomUUID(), canvasId: project.entryCanvasId, sourceId: photo.id, targetId: outside.id, kind: 'visual' as const, relation: 'mutual' as const, points: [{ x: 805, y: 115 }], style: { color: '#eed099', width: 3, lineStyle: 'dotted' as const } };
  const omittedRoute = { id: crypto.randomUUID(), canvasId: project.entryCanvasId, sourceId: photo.id, targetId: excluded.id, kind: 'visual' as const, relation: 'generic' as const };
  project.links.push(snap, route, omittedRoute);
  return { project: commitSnapshots(project), parent, photo, locked, excluded, outside, asset, snap, route, omittedRoute };
}

async function openFixture(page: Page, request: APIRequestContext) {
  const source = fixture(), response = await request.post('/api/projects', { data: source.project }); expect(response.status()).toBe(201);
  const saved = await response.json() as MeridianProject; await page.goto(`/?project=${saved.id}`);
  await expect(tile(page, source.parent.id)).toBeVisible(); return { ...source, saved };
}
async function activate(page: Page) {
  await page.getByRole('button', { name: 'New Node', exact: true }).click();
  await page.getByRole('menuitem', { name: 'Create Module', exact: true }).click();
  await expect(page.getByRole('combobox', { name: 'Side panel', exact: true })).toHaveValue('module');
  await expect(page.getByRole('button', { name: 'Create Module', exact: true })).toHaveAttribute('aria-pressed', 'true');
}
async function pick(page: Page, id: string) {
  await tile(page, id).locator(':scope > .ms-node-header > .ms-node-title').click();
}
async function save(page: Page, request: APIRequestContext, id: string) {
  await page.keyboard.press('Control+s'); await expect(page.locator('.save-state')).toHaveText(/^Saved · r\d+$/); return await readProject(request, id);
}
async function capture(page: Page, request: APIRequestContext, name: string): Promise<StoredModule> {
  await page.getByRole('textbox', { name: 'Module name', exact: true }).fill(name);
  await page.getByRole('button', { name: 'Create From Selection', exact: true }).click();
  let catalog: { id: string; name: string }[] = [];
  await expect.poll(async () => { catalog = await (await request.get('/api/modules')).json(); return catalog.some(item => item.name === name); }).toBe(true);
  const stored = await (await request.get(`/api/modules/${catalog.find(item => item.name === name)!.id}`)).json() as StoredModule;
  await expect(page.locator(`.module-library-card[data-module-id="${stored.id}"]`)).toBeVisible(); return stored;
}

test('Create Module selects complete branches with explicit exclusions, hierarchical checks and internal link highlighting', async ({ page, request }, info) => {
  const errors: string[] = []; page.on('pageerror', error => errors.push(error.message));
  const source = await openFixture(page, request); await activate(page);
  await expect(page.getByRole('button', { name: 'Create From Selection', exact: true })).toBeDisabled();
  await pick(page, source.parent.id);
  for (const node of [source.parent, source.photo, source.locked, source.excluded]) {
    await expect(tile(page, node.id)).toHaveClass(/ms-module-selected/);
    await expect(treeItem(page, node.id).getByRole('checkbox')).toBeChecked();
  }
  await expect(page.locator(`.ms-link[data-link-id="${source.omittedRoute.id}"]`)).toHaveClass(/ms-module-link-selected/);
  expect(await treeItem(page, source.photo.id).locator('..').evaluate(item => item.parentElement?.parentElement?.getAttribute('data-module-selection-node'))).toBe(source.parent.id);
  await treeItem(page, source.excluded.id).getByRole('checkbox').uncheck();
  await expect(tile(page, source.excluded.id)).not.toHaveClass(/ms-module-selected/);
  await expect(page.locator(`.ms-link[data-link-id="${source.omittedRoute.id}"]`)).not.toHaveClass(/ms-module-link-selected/);
  await pick(page, source.outside.id);
  await expect(page.locator(`.ms-link[data-link-id="${source.route.id}"]`)).toHaveClass(/ms-module-link-selected/);
  await expect(page.locator('.module-selection-count')).toHaveText('4 nodes · 2 links');
  await pick(page, source.parent.id); await expect(tile(page, source.photo.id)).not.toHaveClass(/ms-module-selected/);
  await expect(treeItem(page, source.outside.id).getByRole('checkbox')).toBeChecked();
  await pick(page, source.parent.id); await expect(treeItem(page, source.excluded.id).getByRole('checkbox')).not.toBeChecked();
  await page.screenshot({ path: info.outputPath('module-selection-tree.png') });
  const stored = await capture(page, request, `Selected assembly ${source.saved.id.slice(0, 8)}`);
  const content = stored.project.nodes.filter(node => node.kind !== 'canvas' && node.id !== stored.rootId);
  expect(content.map(node => node.title).sort()).toEqual([source.parent, source.photo, source.locked, source.outside].map(node => node.title).sort());
  expect(stored.project.links).toHaveLength(2); expect(stored.project.links.find(link => link.snap)?.snap).toEqual({ locked: true });
  const route = stored.project.links.find(link => !link.snap)!;
  expect(route.points).toEqual(source.route.points); expect(route.style).toEqual(source.route.style);
  expect(stored.project.assets.map(asset => ({ name: asset.name, mime: asset.mime, base64: asset.base64 }))).toEqual([{ name: source.asset.name, mime: source.asset.mime, base64: originalPng }]);
  expect(await readProject(request, source.saved.id)).toEqual(source.saved); expect(errors).toEqual([]);
});

test('Add to Work creates independent identical modules, preserves locked relationships through undo/reload and offline publication', async ({ page, request, browser }, info) => {
  const source = await openFixture(page, request); await activate(page); await pick(page, source.parent.id);
  await treeItem(page, source.excluded.id).getByRole('checkbox').uncheck(); await pick(page, source.outside.id);
  const stored = await capture(page, request, `Portable assembly ${source.saved.id.slice(0, 8)}`);
  const add = page.locator(`.module-library-card[data-module-id="${stored.id}"]`).getByRole('button', { name: `Add ${stored.name} to Canvas`, exact: true });
  await add.click(); await expect(page.locator('.ms-node-module')).toHaveCount(1); const once = await save(page, request, source.saved.id);
  const first = once.nodes.find(node => node.kind === 'module')!; expect(first).toBeTruthy();
  await add.click(); await expect(page.locator('.ms-node-module')).toHaveCount(2); const twice = await save(page, request, source.saved.id);
  const roots = twice.nodes.filter(node => node.kind === 'module'); expect(roots).toHaveLength(2);
  const definitionRoot = stored.project.nodes.find(node => node.id === stored.rootId)!;
  const originalIds = new Set([...stored.project.nodes, ...stored.project.links, ...stored.project.assets].map(item => item.id));
  const allCopyIds: string[] = [];
  for (const root of roots) {
    const descendants = new Set([root.id]);
    for (let changed = true; changed;) { changed = false; for (const node of twice.nodes) if (node.parentId && descendants.has(node.parentId) && !descendants.has(node.id)) { descendants.add(node.id); changed = true; } }
    const copied = twice.nodes.filter(node => descendants.has(node.id)); expect(copied).toHaveLength(5);
    for (const node of copied) {
      const original = stored.project.nodes.find(item => item.title === node.title && item.kind === node.kind)!;
      expect(node.x - root.x).toBeCloseTo(original.x - definitionRoot.x, 6); expect(node.y - root.y).toBeCloseTo(original.y - definitionRoot.y, 6);
      expect(node.width).toBe(original.width); expect(node.height).toBe(original.height); expect(originalIds.has(node.id)).toBe(false); allCopyIds.push(node.id);
    }
    const links = twice.links.filter(link => descendants.has(link.sourceId)); expect(links).toHaveLength(2);
    expect(links.find(link => link.snap)?.snap).toEqual({ locked: true });
    for (const link of links) { expect(descendants.has(link.targetId)).toBe(true); expect(originalIds.has(link.id)).toBe(false); allCopyIds.push(link.id); }
    const image = copied.find(node => node.title === source.photo.title)!;
    const asset = twice.assets.find(item => item.id === image.assetId)!; expect(asset.base64).toBe(originalPng); expect(originalIds.has(asset.id)).toBe(false); allCopyIds.push(asset.id);
  }
  expect(new Set(allCopyIds).size).toBe(allCopyIds.length);
  await page.getByRole('button', { name: 'Undo', exact: true }).click(); const undone = await save(page, request, source.saved.id);
  expect(undone.nodes).toEqual(once.nodes); expect(undone.links).toEqual(once.links); expect(undone.assets).toEqual(once.assets);
  await page.getByRole('button', { name: 'Redo', exact: true }).click(); const redone = await save(page, request, source.saved.id);
  expect(redone.nodes).toEqual(twice.nodes); expect(redone.links).toEqual(twice.links); expect(redone.assets).toEqual(twice.assets);
  await page.reload(); await expect(tile(page, first.id)).toBeVisible(); const reopened = await readProject(request, source.saved.id);
  expect(reopened.nodes).toEqual(twice.nodes); expect(reopened.links).toEqual(twice.links);
  expect(await (await request.get(`/api/modules/${stored.id}`)).json()).toEqual(stored);
  const filename = info.outputPath('module-copies-offline.html');
  await writeFile(filename, buildPublication(reopened, await readFile('dist/viewer.js', 'utf8'), await readFile('dist/scene.css', 'utf8')));
  const context = await browser.newContext({ offline: true, viewport: { width: 1500, height: 950 } });
  try {
    const viewer = await context.newPage(); await viewer.goto(pathToFileURL(filename).href);
    const data = JSON.parse((await viewer.locator('#meridian-data').textContent())!);
    expect(data.project.links.filter((link: { snap?: unknown }) => link.snap)).toHaveLength(3);
    expect(data.project.assets.every((asset: { base64: string }) => asset.base64 === originalPng)).toBe(true);
    await viewer.getByRole('button', { name: 'Fit Work', exact: true }).click();
    for (const root of roots) await expect(tile(viewer, root.id)).toBeVisible();
    await viewer.screenshot({ path: info.outputPath('independent-module-copies-offline.png') });
  } finally { await context.close(); }
});
