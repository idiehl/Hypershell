import { test, expect, type APIRequestContext, type Page } from '@playwright/test';
import { readFile, writeFile } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';
import { addCanvas, addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import { PORTAL_STYLES } from '../src/core/appearance.ts';
import type { MeridianProject } from '../src/core/types.ts';
import { buildPublication, readPublication } from '../src/publication.ts';
import { inspectorPanel, workspaceAction } from './ui-helpers.ts';

const stored = async (request: APIRequestContext, id: string): Promise<MeridianProject> => (await request.get(`/api/projects/${id}`)).json();
const node = (project: MeridianProject, id: string) => project.nodes.find(item => item.id === id)!;
const launcher = (page: Page, id: string) => page.locator(`.ms-node[data-node-id="${id}"] .ms-canvas-open`);
const galleryLauncher = (page: Page, id: string) => page.locator(`.ms-gallery[data-gallery-id="${id}"] .ms-canvas-open`);
const choice = (page: Page) => page.getByRole('combobox', { name: 'Work portal style', exact: true });
const colorPicker = (page: Page) => page.locator('input[type="color"][aria-label="Work portal color"]');

async function appearance(page: Page, id?: string) {
  // Selecting the outline entry isolates selection from inline text editing and
  // from the canvas launcher's navigation action.
  if (id) {
    await inspectorPanel(page, 'canvas');
    const search = page.getByRole('searchbox', { name: 'Search Work nodes', exact: true });
    await search.fill(id);
    await page.locator(`[data-outline-node="${id}"] .inspector-tree-title`).click();
    await search.fill('');
  } else {
    await page.locator('.ms-viewport').focus(); await page.keyboard.press('Escape');
  }
  await inspectorPanel(page, 'appearance');
  const panel = page.locator('.appearance-panel');
  if (!(await panel.evaluate(element => (element as HTMLDetailsElement).open))) await panel.locator('summary').click();
  return panel;
}

async function seed(page: Page, request: APIRequestContext) {
  const fixture = new URL(test.info().project.use.baseURL!);
  expect(fixture.hostname).toBe('127.0.0.1'); expect(fixture.port).not.toBe('5240');
  const project = newProject('Portal appearance integration'), home = project.canvases[0]!;
  home.title = 'Portal workspace'; node(project, home.rootId).title = home.title;
  for (const canvas of project.canvases) canvas.snapping = { stickyEdges: false, smartAlign: false, snapToGrid: false };
  const destination = addCanvas(project, 'A distant canvas · the actual destination');
  node(project, destination.rootId).tag = 'Destination tag differs from its canvas name';
  const first = addNode(project, home.id, 'canvas-link', 'First visible cover');
  Object.assign(first, { tag: 'First portal handle', x: 70, y: 90, width: 270, height: 210, sizeMode: 'fixed', targetId: destination.rootId, placement: 'free' });
  const second = addNode(project, home.id, 'canvas-link', 'Second visible cover');
  Object.assign(second, { tag: 'Second portal handle', x: 400, y: 90, width: 270, height: 210, sizeMode: 'fixed', targetId: destination.rootId, placement: 'free' });
  const reference = addNode(project, home.id, 'reference', 'Reference cover');
  Object.assign(reference, { tag: 'Reference portal handle', x: 70, y: 380, width: 270, height: 210, sizeMode: 'fixed', targetId: first.id, placement: 'free', style: { fill: '#18352b', fontFamily: 'georgia' } });
  const gallery = addNode(project, home.id, 'container', 'Gallery');
  Object.assign(gallery, { x: 730, y: 90, width: 300, height: 330, sizeMode: 'fixed', containerVariant: 'gallery', childLayout: 'contained', collapsed: false, placement: 'free' });
  const galleryItem = addNode(project, home.id, 'canvas-link', 'Gallery cover');
  Object.assign(galleryItem, { tag: 'Gallery portal handle', parentId: gallery.id, x: 735, y: 95, width: 290, height: 250, targetId: destination.rootId });
  const media = addNode(project, home.id, 'media', 'Ordinary media');
  Object.assign(media, { x: 400, y: 380, width: 270, height: 150, sizeMode: 'fixed', text: 'There is no canvas portal on this node.', placement: 'free' });
  const note = addNode(project, destination.id, 'media', 'The destination contents');
  Object.assign(note, { x: 80, y: 100, width: 240, height: 100, text: 'The existing navigation target is preserved.', sizeMode: 'fixed' });
  const created = await request.post('/api/projects', { data: commitSnapshots(project) });
  expect(created.ok(), await created.text()).toBe(true);
  await page.goto(`/?project=${project.id}&canvas=${home.id}`);
  await expect(launcher(page, first.id)).toBeVisible();
  await page.getByRole('button', { name: 'Fit Work', exact: true }).click();
  return { project, home, destination, first, second, reference, gallery, galleryItem, media, note };
}

test('portal colors support workspace inheritance, per-button and reference overrides, Undo/Redo, reload and offline viewing', async ({ page, request, browser }, info) => {
  test.setTimeout(90000); const f = await seed(page, request);
  // Playwright's native color fill already dispatches input/change. A second
  // synthetic change would create a duplicate edit and test two commits as one.
  const tint = async (color: string) => { await colorPicker(page).fill(color); };
  await appearance(page); await expect(page.getByRole('button', { name: 'Use original colors', exact: true })).toBeDisabled();
  await tint('#c89a53');
  await expect.poll(async () => (await stored(request, f.project.id)).canvases[0]!.style?.portalColor).toBe('#c89a53');
  for (const button of [launcher(page, f.first.id), launcher(page, f.second.id), launcher(page, f.reference.id), galleryLauncher(page, f.gallery.id)]) await expect(button).toHaveAttribute('data-portal-color', '#c89a53');
  await appearance(page, f.first.id); await expect(colorPicker(page)).toHaveValue('#c89a53'); await tint('#d45d78'); await choice(page).selectOption('passage');
  await expect(launcher(page, f.first.id)).toHaveAttribute('data-portal-color', '#d45d78'); await expect(launcher(page, f.reference.id)).toHaveAttribute('data-portal-color', '#c89a53');
  await appearance(page, f.reference.id); await tint('#8c78cc'); await choice(page).selectOption('vortex');
  await appearance(page, f.galleryItem.id); await tint('#549bcd');
  await expect(galleryLauncher(page, f.gallery.id)).toHaveAttribute('data-portal-color', '#549bcd');
  await expect.poll(async () => node(await stored(request, f.project.id), f.galleryItem.id).style?.portalColor).toBe('#549bcd');
  await page.locator('.ms-viewport').focus(); await page.keyboard.press('Control+z'); await expect(galleryLauncher(page, f.gallery.id)).toHaveAttribute('data-portal-color', '#c89a53');
  await page.keyboard.press('Control+x'); await expect(galleryLauncher(page, f.gallery.id)).toHaveAttribute('data-portal-color', '#549bcd');
  // Reload is a persistence check, so finish the real save/recovery cleanup
  // after Undo/Redo rather than racing their newly queued recovery draft.
  await workspaceAction(page, 'Save'); await expect(page.locator('.toast')).toContainText('All changes are saved');
  await expect.poll(async () => (await request.get(`/api/projects/${f.project.id}/recovery`)).status()).toBe(404);
  await page.reload(); await expect(launcher(page, f.first.id)).toHaveAttribute('data-portal-color', '#d45d78'); await expect(launcher(page, f.reference.id)).toHaveAttribute('data-portal-color', '#8c78cc');
  await appearance(page, f.first.id); await page.getByRole('button', { name: 'Use Work color', exact: true }).click();
  await expect(launcher(page, f.first.id)).toHaveAttribute('data-portal-color', '#c89a53'); await expect(launcher(page, f.first.id)).toHaveAttribute('data-portal-style', 'passage');
  await appearance(page); await page.getByRole('button', { name: 'Use original colors', exact: true }).click();
  await expect(launcher(page, f.second.id)).not.toHaveAttribute('data-portal-color'); await expect(launcher(page, f.first.id)).not.toHaveAttribute('data-portal-color');
  await expect(launcher(page, f.reference.id)).toHaveAttribute('data-portal-color', '#8c78cc');
  await expect.poll(async () => (await stored(request, f.project.id)).canvases[0]!.style?.portalColor).toBeUndefined();
  const saved = await stored(request, f.project.id); expect(node(saved, f.reference.id).style).toMatchObject({ fill: '#18352b', fontFamily: 'georgia', portalColor: '#8c78cc', portalStyle: 'vortex' });
  await appearance(page, f.media.id); await expect(colorPicker(page)).toHaveCount(0);
  const file = info.outputPath('portal-colors-offline.html'); await writeFile(file, buildPublication(saved, await readFile('dist/viewer.js', 'utf8'), await readFile('dist/scene.css', 'utf8')));
  const context = await browser.newContext({ offline: true, viewport: { width: 1500, height: 950 } });
  try {
    const viewer = await context.newPage(); await viewer.goto(pathToFileURL(file).href);
    await expect(launcher(viewer, f.first.id)).not.toHaveAttribute('data-portal-color'); await expect(launcher(viewer, f.reference.id)).toHaveAttribute('data-portal-color', '#8c78cc');
    await expect(galleryLauncher(viewer, f.gallery.id)).toHaveAttribute('data-portal-color', '#549bcd');
    await galleryLauncher(viewer, f.gallery.id).hover(); await expect(viewer.locator('.ms-canvas-destination')).toHaveText(f.destination.title);
    await viewer.screenshot({ path: info.outputPath('portal-color-offline.png') });
    await launcher(viewer, f.reference.id).click(); await expect(viewer.locator(`.ms-node[data-node-id="${f.note.id}"]`)).toBeVisible();
  } finally { await context.close(); }
  expect(await stored(request, f.project.id)).toEqual(saved);
});

test('canvas defaults and individual overrides autosave, undo, redo and reopen through the real Appearance panel', async ({ page, request }, info) => {
  test.setTimeout(90000); const f = await seed(page, request), initial = await stored(request, f.project.id);
  await appearance(page); await expect(choice(page)).toHaveValue('doorway'); await expect(choice(page).locator('option')).toHaveCount(4);
  expect(await choice(page).locator('option').allTextContents()).toEqual(PORTAL_STYLES.map(style => style.label));
  for (const target of [launcher(page, f.first.id), launcher(page, f.second.id), launcher(page, f.reference.id), galleryLauncher(page, f.gallery.id)]) await expect(target).toHaveAttribute('data-portal-style', 'doorway');
  for (const style of PORTAL_STYLES) {
    await choice(page).selectOption(style.value);
    await expect.poll(async () => (await stored(request, f.project.id)).canvases.find(canvas => canvas.id === f.home.id)!.style?.portalStyle).toBe(style.value);
    await expect(launcher(page, f.first.id)).toHaveAttribute('data-portal-style', style.value);
    await expect(galleryLauncher(page, f.gallery.id)).toHaveAttribute('data-portal-style', style.value);
  }
  await appearance(page, f.first.id); await expect(choice(page)).toHaveValue(''); await expect(choice(page).locator('option')).toHaveCount(5);
  await choice(page).selectOption('wormhole');
  await expect.poll(async () => node(await stored(request, f.project.id), f.first.id).style?.portalStyle).toBe('wormhole');
  await expect(launcher(page, f.first.id)).toHaveAttribute('data-portal-style', 'wormhole'); await expect(launcher(page, f.second.id)).toHaveAttribute('data-portal-style', 'vortex');
  // A reference's blank choice means the workspace default, even when the
  // canonical source has explicitly chosen a different portal.
  await expect(launcher(page, f.reference.id)).toHaveAttribute('data-portal-style', 'vortex');
  await appearance(page); await choice(page).selectOption('passage');
  await expect.poll(async () => (await stored(request, f.project.id)).canvases[0]!.style?.portalStyle).toBe('passage');
  await expect(launcher(page, f.first.id)).toHaveAttribute('data-portal-style', 'wormhole'); await expect(launcher(page, f.second.id)).toHaveAttribute('data-portal-style', 'passage');
  await expect(launcher(page, f.reference.id)).toHaveAttribute('data-portal-style', 'passage');
  await page.locator('.ms-viewport').focus(); await page.keyboard.press('Control+z');
  await expect(launcher(page, f.second.id)).toHaveAttribute('data-portal-style', 'vortex'); await expect(launcher(page, f.first.id)).toHaveAttribute('data-portal-style', 'wormhole');
  await expect.poll(async () => (await stored(request, f.project.id)).canvases[0]!.style?.portalStyle).toBe('vortex');
  await page.keyboard.press('Control+x'); await expect(launcher(page, f.second.id)).toHaveAttribute('data-portal-style', 'passage');
  await expect.poll(async () => (await stored(request, f.project.id)).canvases[0]!.style?.portalStyle).toBe('passage');
  await page.reload(); await expect(launcher(page, f.first.id)).toHaveAttribute('data-portal-style', 'wormhole'); await expect(launcher(page, f.second.id)).toHaveAttribute('data-portal-style', 'passage');
  const saved = await stored(request, f.project.id);
  expect(saved.nodes.map(item => [item.id, item.parentId, item.targetId, item.title, item.tag])).toEqual(initial.nodes.map(item => [item.id, item.parentId, item.targetId, item.title, item.tag]));
  await page.screenshot({ path: info.outputPath('workspace-default-and-node-override.png') });
});

test('Gallery and reference launchers have independent controls, inheritance can be restored, and plain media has no portal setting', async ({ page, request }, info) => {
  test.setTimeout(90000); const f = await seed(page, request);
  await appearance(page); await choice(page).selectOption('passage');
  await appearance(page, f.reference.id); await expect(choice(page)).toHaveValue(''); await choice(page).selectOption('vortex');
  await expect(launcher(page, f.reference.id)).toHaveAttribute('data-portal-style', 'vortex'); await expect(launcher(page, f.first.id)).toHaveAttribute('data-portal-style', 'passage');
  await expect.poll(async () => node(await stored(request, f.project.id), f.reference.id).style).toMatchObject({ fill: '#18352b', fontFamily: 'georgia', portalStyle: 'vortex' });
  await appearance(page, f.galleryItem.id); await choice(page).selectOption('wormhole');
  await expect(galleryLauncher(page, f.gallery.id)).toHaveAttribute('data-portal-style', 'wormhole');
  await expect.poll(async () => node(await stored(request, f.project.id), f.galleryItem.id).style?.portalStyle).toBe('wormhole');
  await appearance(page, f.reference.id); await choice(page).selectOption('');
  await expect.poll(async () => node(await stored(request, f.project.id), f.reference.id).style?.portalStyle).toBeUndefined();
  await expect(launcher(page, f.reference.id)).toHaveAttribute('data-portal-style', 'passage');
  expect(node(await stored(request, f.project.id), f.reference.id).style).toEqual(f.reference.style);
  await appearance(page, f.media.id); await expect(choice(page)).toHaveCount(0);
  const button = galleryLauncher(page, f.gallery.id); await button.hover();
  await expect(button).toHaveAttribute('aria-label', `Open ${f.destination.title}`);
  await expect(page.locator('.ms-canvas-destination')).toHaveText(f.destination.title);
  await page.screenshot({ path: info.outputPath('gallery-wormhole-destination-name.png') });
  await page.reload(); await expect(galleryLauncher(page, f.gallery.id)).toHaveAttribute('data-portal-style', 'wormhole'); await expect(launcher(page, f.reference.id)).toHaveAttribute('data-portal-style', 'passage');
});

test('offline publication keeps all four portal styles and actual destination names with existing canvas navigation', async ({ page, request, browser }, info) => {
  test.setTimeout(90000); const f = await seed(page, request);
  await appearance(page, f.first.id); await choice(page).selectOption('passage');
  await appearance(page, f.reference.id); await choice(page).selectOption('vortex');
  await appearance(page, f.galleryItem.id); await choice(page).selectOption('wormhole');
  await expect.poll(async () => node(await stored(request, f.project.id), f.galleryItem.id).style?.portalStyle).toBe('wormhole');
  const saved = await stored(request, f.project.id), file = info.outputPath('portal-styles-offline.html');
  await writeFile(file, buildPublication(saved, await readFile('dist/viewer.js', 'utf8'), await readFile('dist/scene.css', 'utf8')));
  const reopened = readPublication(await readFile(file, 'utf8'));
  // Reimport creates an independent document with fresh GUIDs; Tags retain the
  // authored identity needed to compare each portable appearance setting.
  for (const id of [f.first.id, f.reference.id, f.galleryItem.id]) {
    const original = node(saved, id), restored = reopened.nodes.find(item => item.tag === original.tag);
    expect(restored).toBeDefined(); expect(restored!.id).not.toBe(id);
    expect(restored!.style?.portalStyle).toBe(original.style?.portalStyle);
  }
  const context = await browser.newContext({ offline: true, viewport: { width: 1500, height: 950 } });
  try {
    const viewer = await context.newPage(); await viewer.goto(pathToFileURL(file).href);
    await expect(launcher(viewer, f.first.id)).toHaveAttribute('data-portal-style', 'passage');
    await expect(launcher(viewer, f.second.id)).toHaveAttribute('data-portal-style', 'doorway');
    await expect(launcher(viewer, f.reference.id)).toHaveAttribute('data-portal-style', 'vortex');
    await expect(galleryLauncher(viewer, f.gallery.id)).toHaveAttribute('data-portal-style', 'wormhole');
    await galleryLauncher(viewer, f.gallery.id).hover();
    await expect(viewer.locator('.ms-canvas-destination')).toHaveText(f.destination.title);
    const authored = await viewer.locator('#meridian-data').textContent();
    await viewer.screenshot({ path: info.outputPath('four-portal-styles-offline.png') });
    await launcher(viewer, f.reference.id).click(); await expect(viewer.locator(`.ms-node[data-node-id="${f.note.id}"]`)).toBeVisible();
    expect(await viewer.locator('#meridian-data').textContent()).toBe(authored);
  } finally { await context.close(); }
  expect(await stored(request, f.project.id)).toEqual(saved);
});

test('external canvas names use catalog metadata even when the launcher title is empty and its Tag differs', async ({ page, request }, info) => {
  const fixture = new URL(test.info().project.use.baseURL!); expect(fixture.port).not.toBe('5240');
  const destination = newProject('An external project with a different project title');
  destination.canvases[0]!.title = 'The external destination canvas'; node(destination, destination.canvases[0]!.rootId).title = destination.canvases[0]!.title;
  const extra = addCanvas(destination, 'The specific external canvas destination');
  const targetCreated = await request.post('/api/projects', { data: commitSnapshots(destination) }); expect(targetCreated.ok(), await targetCreated.text()).toBe(true);
  const source = newProject('External launcher metadata'), direct = addNode(source, source.entryCanvasId, 'canvas-link', '');
  Object.assign(direct, { tag: 'A user handle that is not the destination', x: 80, y: 100, width: 300, height: 210, sizeMode: 'fixed', placement: 'free', canvasTarget: { projectId: destination.id, canvasId: extra.id } });
  const reference = addNode(source, source.entryCanvasId, 'reference', 'Different reference cover');
  Object.assign(reference, { tag: 'Reference handle', x: 450, y: 100, width: 300, height: 210, sizeMode: 'fixed', placement: 'free', targetId: direct.id });
  const originalId = crypto.randomUUID(), original = { ...direct, id: originalId, canvasId: destination.entryCanvasId, parentId: destination.canvases[0]!.rootId, title: 'A stale saved cover title' };
  source.snapshots[originalId] = { node: original, value: null, savedAt: new Date().toISOString() };
  const pinned = addNode(source, source.entryCanvasId, 'reference', 'Pinned external reference');
  Object.assign(pinned, { x: 820, y: 100, width: 300, height: 210, sizeMode: 'fixed', placement: 'free', targetId: originalId });
  const missingCanvas = addNode(source, source.entryCanvasId, 'canvas-link', 'An obsolete canvas cover title');
  Object.assign(missingCanvas, { x: 80, y: 400, width: 300, height: 210, sizeMode: 'fixed', placement: 'free', canvasTarget: { projectId: destination.id, canvasId: crypto.randomUUID() } });
  const created = await request.post('/api/projects', { data: commitSnapshots(source) }); expect(created.ok(), await created.text()).toBe(true);
  await page.goto(`/?project=${source.id}`);
  for (const id of [direct.id, reference.id, pinned.id]) {
    const button = launcher(page, id); await expect(button).toHaveAttribute('aria-label', `Open ${extra.title}`); await button.hover();
    await expect(page.locator('.ms-canvas-destination')).toHaveText(extra.title);
  }
  await launcher(page, missingCanvas.id).hover(); await expect(page.locator('.ms-canvas-destination')).toHaveText(destination.canvases[0]!.title);
  await page.screenshot({ path: info.outputPath('external-destination-name-from-catalog.png') });
  await launcher(page, direct.id).click(); await page.waitForURL(url => url.searchParams.get('project') === destination.id && url.searchParams.get('canvas') === extra.id);
  expect(node(await stored(request, source.id), direct.id).title).toBe(''); expect(node(await stored(request, source.id), direct.id).tag).toBe(direct.tag);
});
