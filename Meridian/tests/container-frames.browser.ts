import { test, expect, type APIRequestContext, type Page } from '@playwright/test';
import { readFile } from 'node:fs/promises';
import { addNode, commitSnapshots, forkProject, newProject } from '../src/core/model.ts';
import type { ContainerVariant, MeridianNode, MeridianProject, StoredModule } from '../src/core/types.ts';
import { buildPublication, readPublication } from '../src/publication.ts';
import { inspectorPanel } from './ui-helpers.ts';

const tile = (page: Page, id: string) => page.locator(`.ms-node[data-node-id="${id}"]`);
const row = (page: Page, id: string) => page.locator(`[data-outline-node="${id}"]`);
const stored = async (request: APIRequestContext, id: string): Promise<MeridianProject> => (await request.get(`/api/projects/${id}`)).json();
const node = (project: MeridianProject, id: string) => project.nodes.find(node => node.id === id)!;
const errors = new WeakMap<Page, string[]>();
test.beforeEach(async ({ page }) => { const messages: string[] = []; errors.set(page, messages); page.on('pageerror', error => messages.push(error.message)); });
test.afterEach(async ({ page }) => expect(errors.get(page) ?? []).toEqual([]));
async function selectNode(page: Page, id: string) {
  await inspectorPanel(page, 'canvas'); const search = page.getByRole('searchbox', { name: 'Search Work nodes', exact: true }); await search.fill(id); await row(page, id).locator('.inspector-tree-title').click(); await search.fill('');
}
async function seed(page: Page, request: APIRequestContext, project: MeridianProject) {
  const response = await request.post('/api/projects', { data: commitSnapshots(project) }); expect(response.ok(), await response.text()).toBe(true); await page.goto(`/?project=${project.id}`); await expect(page.locator('.workspace')).toBeVisible();
}
function projectFixture(title: string) {
  const project = newProject(title); project.canvases[0]!.snapping = { stickyEdges: false, smartAlign: false, snapToGrid: false }; return project;
}
function parentNode(project: MeridianProject, variant: ContainerVariant = 'horizontal', width = 600, height = 420) {
  const parent = addNode(project, project.entryCanvasId, 'container', '');
  Object.assign(parent, { tag: `${variant} frame`, x: 80, y: 90, width, height, sizeMode: 'fixed', collapsed: false, containerVariant: variant, childLayout: 'contained', childArrangement: variant === 'horizontal' ? 'row' : variant === 'vertical' ? 'column' : variant === 'contained' || variant === 'gallery' ? 'free' : 'grid', style: { shape: 'rectangle', borderWidth: 1, padding: 5, childPadding: 5 },
    containerSizes: { expanded: { width, height, sizeMode: 'fixed' }, collapsed: { width: 260, height: 160, sizeMode: 'fixed' } },
    grid: { columns: variant === 'window' ? 4 : 2, rows: 2, windowColumns: 2, windowRows: 2, cellWidth: 900, cellHeight: 800, gap: 0, fillOrder: 'row', scrollDirection: 'vertical' } }); return parent;
}
function children(project: MeridianProject, parent: MeridianNode, count = 4) {
  return Array.from({ length: count }, (_, at) => { const child = addNode(project, parent.canvasId, 'media', ''); Object.assign(child, { tag: `Object ${at + 1}`, parentId: parent.id, x: parent.x + 6 + at * 910, y: parent.y + 6, width: 900, height: 800, sizeMode: 'fixed', childPosition: 'auto', text: '', style: { shape: 'rectangle', fill: at % 2 ? '#294f58' : '#365d51' } }); return child; });
}
async function geometry(page: Page, id: string) { return tile(page, id).evaluate(el => { const style = (el as HTMLElement).style; return { x: parseFloat(style.left), y: parseFloat(style.top), width: parseFloat(style.width), height: parseFloat(style.height) }; }); }
async function dimensions(page: Page, id: string, width: number, height: number) {
  await selectNode(page, id); await inspectorPanel(page, 'appearance');
  for (const [label, value] of [['Width', width], ['Height', height]] as const) { const input = page.locator('.inspector').getByLabel(label, { exact: true }); await input.fill(String(value)); await input.press('Tab'); }
}
async function toggle(page: Page, id: string) { await selectNode(page, id); await inspectorPanel(page, 'relations'); await page.getByRole('button', { name: 'Toggle contents from relations', exact: true }).click(); }
async function drag(page: Page, id: string, dx: number, dy: number) { const rect = (await tile(page, id).boundingBox())!; await page.mouse.move(rect.x + rect.width / 2, rect.y + rect.height / 2); await page.mouse.down(); await page.mouse.move(rect.x + rect.width / 2 + dx, rect.y + rect.height / 2 + dy, { steps: 5 }); await page.mouse.up(); }
async function filled(page: Page, parent: MeridianNode, childIds: string[], variant: ContainerVariant) {
  const box = (await tile(page, parent.id).boundingBox())!, world = await geometry(page, parent.id), scale = box.width / world.width, inner = { x: box.x + 6 * scale, y: box.y + 6 * scale, width: (world.width - 12) * scale, height: (world.height - 12) * scale };
  const close = (actual: number, expected: number) => expect(Math.abs(actual - expected) / scale).toBeLessThan(1.1);
  if (variant === 'gallery') {
    const item = (await tile(page, parent.id).locator('.ms-gallery-item').boundingBox())!, nav = (await tile(page, parent.id).locator('.ms-gallery-navigation').boundingBox())!;
    close(item.x, inner.x); close(item.y, inner.y); close(item.width, inner.width); close(item.y + item.height + 5 * scale, nav.y); close(nav.y + nav.height, inner.y + inner.height); return;
  }
  if (variant === 'window') {
    const pane = (await tile(page, parent.id).locator('.ms-window-viewport').boundingBox())!; close(pane.x, inner.x); close(pane.y, inner.y); close(pane.width, inner.width); close(pane.height, inner.height);
    for (const id of childIds) { const child = (await tile(page, id).boundingBox())!; close(child.width, inner.width / 2); close(child.height, inner.height / 2); } return;
  }
  const columns = variant === 'horizontal' ? childIds.length : variant === 'vertical' ? 1 : Math.ceil(Math.sqrt(childIds.length)), rows = variant === 'vertical' ? childIds.length : variant === 'horizontal' ? 1 : Math.ceil(Math.sqrt(childIds.length));
  for (const [at, id] of childIds.entries()) { const child = (await tile(page, id).boundingBox())!; close(child.width, inner.width / columns); close(child.height, inner.height / rows); close(child.x, inner.x + at % columns * inner.width / columns); close(child.y, inner.y + Math.floor(at / columns) * inner.height / rows); }
}

test('repeated small canvas drags preserve exact empty Container dimensions for fixed and automatic sizing', async ({ page, request }) => {
  const project = projectFixture('No growth while dragging'), fixed = parentNode(project, 'contained', 280, 180), automatic = parentNode(project, 'contained', 280, 180); Object.assign(automatic, { x: 540, sizeMode: 'auto', containerSizes: { expanded: { width: 280, height: 180, sizeMode: 'auto' }, collapsed: { width: 180, height: 100, sizeMode: 'auto' } } });
  await seed(page, request, project);
  for (const parent of [fixed, automatic]) { const initial = await geometry(page, parent.id); for (let at = 0; at < 5; at++) { const before = node(await stored(request, project.id), parent.id); await drag(page, parent.id, 8, 3); await expect.poll(async () => node(await stored(request, project.id), parent.id).x).not.toBe(before.x); const current = await geometry(page, parent.id); expect([current.width, current.height]).toEqual([initial.width, initial.height]); } await toggle(page, parent.id); await expect.poll(async () => node(await stored(request, project.id), parent.id).collapsed).toBe(true); await toggle(page, parent.id); expect((await geometry(page, parent.id)).width).toBe(initial.width); expect((await geometry(page, parent.id)).height).toBe(initial.height); }
});

test('expanded and collapsed frames retain independent Appearance and edge sizes across toggle cycles and reload', async ({ page, request }, info) => {
  const project = projectFixture('Independent disclosure frames'), parent = parentNode(project), items = children(project, parent); parent.title = 'A compact cover'; parent.containerSizes!.collapsed!.sizeMode = 'auto'; await seed(page, request, project); await toggle(page, parent.id); await expect(tile(page, items[0]!.id)).toHaveCount(0);
  const automatic = await geometry(page, parent.id), automaticBounds = (await tile(page, parent.id).boundingBox())!, automaticEdge = { x: automaticBounds.x + automaticBounds.width - 3, y: automaticBounds.y + automaticBounds.height / 2 }; await page.mouse.move(automaticEdge.x, automaticEdge.y); await expect(tile(page, parent.id)).toHaveCSS('cursor', 'ew-resize'); await page.mouse.down(); await page.mouse.move(automaticEdge.x + 20, automaticEdge.y, { steps: 6 }); await page.mouse.up(); await expect.poll(async () => node(await stored(request, project.id), parent.id).containerSizes?.collapsed?.sizeMode).toBe('fixed'); expect((await geometry(page, parent.id)).width).toBeGreaterThan(automatic.width);
  await dimensions(page, parent.id, 280, 170); await expect.poll(async () => node(await stored(request, project.id), parent.id).width).toBe(280);
  const before = await geometry(page, parent.id), bounds = (await tile(page, parent.id).boundingBox())!, edge = { x: bounds.x + bounds.width - 3, y: bounds.y + bounds.height / 2 };
  await page.mouse.move(edge.x, edge.y); await expect(tile(page, parent.id)).toHaveCSS('cursor', 'ew-resize'); await page.mouse.down(); await page.mouse.move(edge.x + 24, edge.y, { steps: 6 }); await page.mouse.up(); await expect.poll(async () => node(await stored(request, project.id), parent.id).width).toBeGreaterThan(before.width);
  const closed = await geometry(page, parent.id); expect(closed.height).toBe(170);
  for (let at = 0; at < 3; at++) { await toggle(page, parent.id); expect((await geometry(page, parent.id)).width).toBe(600); expect((await geometry(page, parent.id)).height).toBe(420); await toggle(page, parent.id); expect([...(Object.values(await geometry(page, parent.id))).slice(2)]).toEqual([closed.width, closed.height]); }
  await expect.poll(async () => node(await stored(request, project.id), parent.id).collapsed).toBe(true); await expect.poll(async () => node(await stored(request, project.id), parent.id).width).toBe(closed.width); await page.reload(); expect((await geometry(page, parent.id)).width).toBe(closed.width); await toggle(page, parent.id); await dimensions(page, parent.id, 700, 500); expect((await geometry(page, parent.id)).width).toBe(700); expect((await geometry(page, parent.id)).height).toBe(500); await expect.poll(async () => node(await stored(request, project.id), parent.id).containerSizes?.expanded).toEqual({ width: 700, height: 500, sizeMode: 'fixed' }); await toggle(page, parent.id); expect((await geometry(page, parent.id)).width).toBe(closed.width);
  await expect.poll(async () => node(await stored(request, project.id), parent.id).containerSizes).toMatchObject({ expanded: { width: 700, height: 500, sizeMode: 'fixed' }, collapsed: { width: closed.width, height: closed.height, sizeMode: 'fixed' } }); await page.screenshot({ path: info.outputPath('independent-closed-frame.png') });
});

test('every bounded ordered pattern fills both axes and shrinks oversized children without changing its parent', async ({ page, request }, info) => {
  test.setTimeout(90000);
  for (const variant of ['horizontal', 'vertical', 'grid', 'spreadsheet', 'window', 'gallery'] as const) {
    const project = projectFixture(`${variant} authoritative frame`), parent = parentNode(project, variant), items = children(project, parent, variant === 'window' ? 8 : 4);
    if (variant === 'gallery') {
      const base64 = await page.evaluate(() => {
        const canvas = document.createElement('canvas'); canvas.width = 640; canvas.height = 360;
        const drawing = canvas.getContext('2d')!; drawing.fillStyle = '#193e48'; drawing.fillRect(0, 0, 640, 360);
        drawing.strokeStyle = '#7ce0c8'; drawing.lineWidth = 8; drawing.strokeRect(18, 18, 604, 324);
        drawing.fillStyle = '#7ce0c8'; drawing.beginPath(); drawing.arc(320, 180, 115, 0, 2 * Math.PI); drawing.fill();
        drawing.fillStyle = '#193e48'; drawing.font = 'bold 34px sans-serif'; drawing.textAlign = 'center'; drawing.fillText('16 : 9', 320, 192);
        return canvas.toDataURL('image/png').split(',')[1]!;
      });
      project.assets.push({ id: 'gallery-landscape', name: 'landscape-aspect-check.png', mime: 'image/png', base64 }); Object.assign(items[0]!, { contentType: 'asset', assetId: 'gallery-landscape' });
    }
    await seed(page, request, project); await filled(page, parent, items.map(child => child.id), variant); expect((await geometry(page, parent.id)).width).toBe(600); expect((await geometry(page, parent.id)).height).toBe(420);
    await dimensions(page, parent.id, 720, 500); await expect.poll(async () => node(await stored(request, project.id), parent.id).height).toBe(500); await filled(page, parent, items.map(child => child.id), variant); expect(node(await stored(request, project.id), parent.id)).toMatchObject({ width: 720, height: 500 });
    if (variant === 'gallery') {
      const image = tile(page, parent.id).locator('.ms-gallery-item img'); await expect(image).toHaveJSProperty('naturalWidth', 640); await expect(image).toHaveJSProperty('naturalHeight', 360); await expect(image).toHaveCSS('object-fit', 'contain');
      const imageBounds = (await image.boundingBox())!, pageBounds = (await tile(page, parent.id).locator('.ms-gallery-item').boundingBox())!;
      expect(imageBounds.width).toBeLessThanOrEqual(pageBounds.width + 1); expect(imageBounds.height).toBeLessThanOrEqual(pageBounds.height + 1); expect(imageBounds.y + imageBounds.height).toBeLessThanOrEqual(pageBounds.y + pageBounds.height + 1);
    }
    if (variant === 'grid' || variant === 'gallery') await page.screenshot({ path: info.outputPath(`${variant}-fills-inner-frame.png`) });
  }
});

test('creating ordered children retains parent dimensions and parent-controlled equal fitted cells', async ({ page, request }) => {
  const project = projectFixture('Ordered editing walls'), parent = parentNode(project), items = children(project, parent); await seed(page, request, project); await selectNode(page, parent.id); await tile(page, parent.id).click({ button: 'right', position: { x: 3, y: 25 } }); await page.getByRole('menuitem', { name: 'Create Child Node', exact: true }).click();
  const form = page.getByRole('dialog', { name: 'New Node', exact: true }); await form.getByLabel('Type', { exact: true }).selectOption('media'); await form.getByLabel('Tag', { exact: true }).fill('New fitted child'); await form.getByRole('button', { name: 'Create node', exact: true }).click();
  await expect.poll(async () => (await stored(request, project.id)).nodes.filter(child => child.parentId === parent.id).length).toBe(5); const current = await stored(request, project.id), all = current.nodes.filter(child => child.parentId === parent.id); await filled(page, parent, all.map(child => child.id), 'horizontal');
  await selectNode(page, items[0]!.id); await inspectorPanel(page, 'appearance'); await expect(page.locator('.inspector').getByLabel('Width', { exact: true })).toBeDisabled(); await expect(page.locator('.inspector').getByLabel('Height', { exact: true })).toBeDisabled(); await filled(page, parent, all.map(child => child.id), 'horizontal'); expect(node(await stored(request, project.id), parent.id)).toMatchObject({ width: 600, height: 420 });
});

test('Freeform keeps authored child sizes and positions instead of stretching them into cells', async ({ page, request }) => {
  const project = projectFixture('Freeform exception'), parent = parentNode(project, 'contained'), items = children(project, parent, 2); Object.assign(items[0]!, { x: 100, y: 120, width: 90, height: 70, childPosition: 'manual' }); Object.assign(items[1]!, { x: 300, y: 260, width: 170, height: 110, childPosition: 'manual' });
  await seed(page, request, project); const before = await Promise.all(items.map(child => geometry(page, child.id))); await dimensions(page, parent.id, 720, 500); expect(await Promise.all(items.map(child => geometry(page, child.id)))).toEqual(before); expect(before[0]!.width).not.toBe(before[1]!.width);
});

test('nested disclosure fits its current ordered cell without accumulating ancestor growth', async ({ page, request }) => {
  const project = projectFixture('Nested frames stay bounded'), parent = parentNode(project, 'grid'), items = children(project, parent), inner = items[0]!; Object.assign(inner, { kind: 'container', containerVariant: 'grid', childLayout: 'contained', childArrangement: 'grid', collapsed: true, width: 220, height: 130, containerSizes: { collapsed: { width: 220, height: 130, sizeMode: 'fixed' }, expanded: { width: 900, height: 800, sizeMode: 'fixed' } } }); const descendants = children(project, inner, 4);
  await seed(page, request, project); const outer = await geometry(page, parent.id); for (let at = 0; at < 3; at++) { await toggle(page, inner.id); for (const child of descendants) await expect(tile(page, child.id)).toHaveCount(1); await filled(page, parent, items.map(child => child.id), 'grid'); expect(await geometry(page, parent.id)).toEqual(outer); await toggle(page, inner.id); for (const child of descendants) await expect(tile(page, child.id)).toHaveCount(0); expect(await geometry(page, parent.id)).toEqual(outer); }
});

test('frame modes survive clipboard, Module storage, package export, publication and host validation', async ({ page, request }) => {
  const project = projectFixture('Portable frame modes'), parent = parentNode(project, 'vertical'), items = children(project, parent, 2); await seed(page, request, project); await selectNode(page, parent.id); await page.locator('.ms-viewport').focus(); await page.keyboard.press('Control+c'); await page.keyboard.press('Control+v'); await expect.poll(async () => (await stored(request, project.id)).nodes.filter(item => item.kind === 'container').length).toBe(2);
  let saved = await stored(request, project.id); const copy = saved.nodes.find(item => item.kind === 'container' && item.id !== parent.id)!; expect(copy.containerSizes).toEqual(node(saved, parent.id).containerSizes);
  await page.keyboard.press('Escape'); await page.getByRole('button', { name: 'New Node', exact: true }).click(); await page.getByRole('menuitem', { name: 'Module', exact: true }).click(); await tile(page, parent.id).click({ position: { x: 3, y: 35 } }); const name = `Frame modes ${project.id.slice(0, 8)}`; await page.getByRole('textbox', { name: 'Module name', exact: true }).fill(name); await page.getByRole('button', { name: 'Create From Selection', exact: true }).click();
  let catalog: Array<{ id: string; name: string }> = []; await expect.poll(async () => { catalog = await (await request.get('/api/modules')).json(); return catalog.some(item => item.name === name); }).toBe(true); const module = await (await request.get(`/api/modules/${catalog.find(item => item.name === name)!.id}`)).json() as StoredModule; expect(module.project.nodes.find(item => item.kind === 'container' && item.tag === parent.tag)?.containerSizes).toEqual(node(saved, parent.id).containerSizes);
  await page.locator(`.module-library-card[data-module-id="${module.id}"]`).getByRole('button', { name: `Add ${name} to Canvas`, exact: true }).click(); await expect.poll(async () => (await stored(request, project.id)).nodes.filter(item => item.kind === 'container').length).toBe(3); saved = await stored(request, project.id); const inserted = saved.nodes.find(item => item.kind === 'container' && item.id !== parent.id && item.id !== copy.id)!; expect(inserted.containerSizes).toEqual(node(saved, parent.id).containerSizes);
  const exported = await request.post('/api/export', { data: saved }); expect(exported.ok(), await exported.text()).toBe(true); const imported = await request.post('/api/import', { multipart: { file: { name: 'container-modes.meridian', mimeType: 'application/octet-stream', buffer: await exported.body() } } }); expect(imported.ok(), await imported.text()).toBe(true); const portable = await imported.json() as MeridianProject; expect(node(portable, parent.id).containerSizes).toEqual(node(saved, parent.id).containerSizes);
  const read = readPublication(buildPublication(portable, await readFile('dist/viewer.js', 'utf8'), await readFile('dist/scene.css', 'utf8'))); expect(read.nodes.find(item => item.tag === parent.tag)?.containerSizes).toEqual(node(saved, parent.id).containerSizes); expect(forkProject(saved).nodes.find(item => item.tag === parent.tag)?.containerSizes).toEqual(node(saved, parent.id).containerSizes);
  for (const malformed of [{ expanded: { width: 0, height: 20, sizeMode: 'fixed' } }, { expanded: { width: 10, height: 20, sizeMode: 'flexible' } }, { unknown: { width: 10, height: 20, sizeMode: 'fixed' } }]) { const candidate = structuredClone(saved); (node(candidate, parent.id) as unknown as Record<string, unknown>).containerSizes = malformed; const rejected = await request.put(`/api/projects/${saved.id}`, { data: { project: candidate, expectedRevision: saved.revision } }); expect(rejected.status(), await rejected.text()).toBe(400); expect(await stored(request, saved.id)).toEqual(saved); }
  const malformedSnapshot = structuredClone(saved); (malformedSnapshot.snapshots[parent.id]!.node as unknown as Record<string, unknown>).containerSizes = { collapsed: { width: 10, height: 0, sizeMode: 'fixed' } }; expect((await request.put(`/api/projects/${saved.id}`, { data: { project: malformedSnapshot, expectedRevision: saved.revision } })).status()).toBe(400); expect(await stored(request, saved.id)).toEqual(saved);
  expect(items).toHaveLength(2);
});

test('Block is absent from creation and Properties while legacy Blocks become blank Media without losing identity or style', async ({ page, request }, info) => {
  const project = projectFixture('Retired Block migration'), legacy = addNode(project, project.entryCanvasId, 'media', ''); Object.assign(legacy, { kind: 'block', tag: 'Retained structural identity', title: 'Dormant old title', text: 'Dormant old body', x: 100, y: 100, width: 260, height: 170, sizeMode: 'fixed', style: { shape: 'hexagon', fill: '#315e52', borderWidth: 2 } }); await seed(page, request, project); await expect(tile(page, legacy.id)).toHaveText(''); await selectNode(page, legacy.id); await inspectorPanel(page, 'properties'); const type = page.locator('.inspector').getByLabel('Type', { exact: true }); await expect(type).toHaveValue('media'); await expect(type.locator('option[value="block"]')).toHaveCount(0);
  const tag = page.locator('.inspector').getByLabel('Tag', { exact: true }); await tag.fill('Retained identity after migration'); await tag.press('Tab'); await expect.poll(async () => node(await stored(request, project.id), legacy.id).kind).toBe('media'); const migrated = node(await stored(request, project.id), legacy.id); expect(migrated).toMatchObject({ id: legacy.id, tag: 'Retained identity after migration', title: '', style: legacy.style }); expect(migrated.retiredBlockContent).toMatchObject({ title: 'Dormant old title', text: 'Dormant old body' });
  await page.reload(); await expect(tile(page, legacy.id)).toHaveText(''); await page.getByRole('button', { name: 'New Node', exact: true }).click(); await page.getByRole('menuitem', { name: 'Node', exact: true }).click(); await page.locator('.ms-viewport').click({ position: { x: 750, y: 400 } }); const form = page.getByRole('dialog', { name: 'New Node', exact: true }); await expect(form.getByLabel('Type', { exact: true }).locator('option[value="block"]')).toHaveCount(0); await form.getByRole('button', { name: 'Cancel', exact: true }).click(); await page.screenshot({ path: info.outputPath('legacy-block-is-blank-media.png') });
});
