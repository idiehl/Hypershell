import assert from 'node:assert/strict';
import path from 'node:path';
import { createHash } from 'node:crypto';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { chromium, expect } from '@playwright/test';
import { newProject, addNode, commitSnapshots } from '../../src/core/model.ts';

const config = JSON.parse(await readFile(process.argv[2], 'utf8'));
const { origin, qaRoot, dataRoot, packageRoot } = config;
assert.equal(new URL(origin).port, '5293');
assert.ok(!path.resolve(dataRoot).toLowerCase().startsWith(path.resolve(config.sourceRoot).toLowerCase()));
await mkdir(qaRoot, { recursive: true });
const digest = bytes => createHash('sha256').update(bytes).digest('hex');
const checks = [], shots = [], errors = [];
const record = text => { checks.push(text); process.stdout.write(text + '\n'); };
async function json(route, method = 'GET', data) {
  const response = await fetch(origin + route, { method, headers: data === undefined ? {} : { 'Content-Type': 'application/json' }, body: data === undefined ? undefined : JSON.stringify(data) });
  assert.ok(response.ok, `${method} ${route}: ${response.status} ${await response.clone().text()}`);
  return response.json();
}
const screenshot = async (page, name) => { const filename = path.join(qaRoot, name); await page.screenshot({ path: filename }); shots.push(filename); };
async function mode(page, value) {
  if (value === 'Hand') await page.getByRole('button', { name: 'Hand', exact: true }).click();
  else { await page.getByRole('button', { name: 'New Node', exact: true }).click(); await page.getByRole('menuitem', { name: value, exact: true }).click(); }
}
async function openNode(page, location = { x: 70, y: 45 }) {
  await page.locator('.canvas-stage').click({ position: location });
  const dialog = page.getByRole('dialog', { name: 'New Node', exact: true }); await expect(dialog).toBeVisible(); return dialog;
}
async function submit(dialog) { await dialog.getByRole('button', { name: 'Create node', exact: true }).click(); await expect(dialog).toHaveCount(0); }
async function savedNode(projectId, title) {
  await expect.poll(async () => (await json(`/api/projects/${projectId}`)).nodes.some(node => node.title === title && node.kind !== 'canvas')).toBe(true);
  return (await json(`/api/projects/${projectId}`)).nodes.find(node => node.title === title && node.kind !== 'canvas');
}
const tile = (page, id) => page.locator(`.ms-node[data-node-id="${id}"]`);
const sourceLibraryBytes = await readFile(path.join(dataRoot, 'library.json'));
const sourceLibrary = JSON.parse(sourceLibraryBytes);
const catalog = await json('/api/projects');
const myth = catalog.find(item => /Greek Mythology.*Visual atlas/i.test(item.title)) ?? catalog.find(item => /Greek Mythology/i.test(item.title));
assert.ok(myth, 'The copied library should include a mythology collection.');
const mythology = await json(`/api/projects/${myth.id}`);
const zeus = mythology.nodes.find(node => node.id === '005fe68a-bc30-4b11-af1d-4dadec799b1c') ?? mythology.nodes.find(node => node.kind === 'image' && mythology.assets.find(asset => asset.id === node.assetId)?.mime.startsWith('image/'));
assert.ok(zeus, 'The real collection should include an image to review.');
const originalAsset = mythology.assets.find(asset => asset.id === zeus.assetId);
assert.ok(originalAsset);
const browser = await chromium.launch();
const context = await browser.newContext({ viewport: { width: 1700, height: 1080 } });
context.on('page', page => page.on('pageerror', error => errors.push(error.message)));
const page = await context.newPage();
try {
  await page.goto(origin + '/'); await expect(page.getByRole('button', { name: 'New Node', exact: true })).toBeVisible();
  await expect.poll(async () => (await json('/api/library')).schemaVersion).toBe(2);
  const migrated = await json('/api/library');
  assert.ok(!migrated.project.nodes.some(node => ['section', 'subsection'].includes(node.kind)));
  if (sourceLibrary.schemaVersion === 1) {
    for (const entry of sourceLibrary.entries) {
      const node = migrated.project.nodes.find(node => node.id === entry.id); assert.ok(node, entry.title);
      assert.equal(node.kind, entry.kind === 'section' ? 'container' : 'canvas-link');
      assert.equal(node.parentId, entry.parentId ?? migrated.project.canvases[0].rootId);
      assert.equal(node.collapsed, entry.collapsed); assert.equal(node.x, entry.x); assert.equal(node.y, entry.y);
      if (entry.kind === 'canvas') { assert.equal(node.canvasTarget.projectId, entry.projectId); assert.equal(node.targetId, undefined); }
    }
    const backup = await readFile(path.join(dataRoot, 'library.json.bak'));
    assert.equal(digest(backup), digest(sourceLibraryBytes));
    await writeFile(path.join(qaRoot, 'migration-original-v1.json'), backup);
    record('Schema 2 migration preserves original identities, hierarchy, positions, collapsed state and destinations; the exact v1 file is backed up.');
  } else record('Existing schema 2 home opens as an ordinary canvas and retains its saved destinations.');
  const homeExport = await fetch(origin + '/api/export', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(migrated.project) });
  assert.equal(homeExport.status, 200);
  const homeBytes = Buffer.from(await homeExport.arrayBuffer());
  const homeForm = new FormData(); homeForm.append('file', new Blob([homeBytes]), 'home-review.meridian');
  const homeImport = await fetch(origin + '/api/import', { method: 'POST', body: homeForm });
  assert.equal(homeImport.status, 200); assert.deepEqual(await homeImport.json(), migrated.project);
  await writeFile(path.join(qaRoot, 'migrated-home-review.meridian'), homeBytes);
  record('The actual migrated home exports and imports through the same native canvas format without losing external destinations.');
  await expect(page.getByRole('button', { name: /New section|New subsection|New canvas/i })).toHaveCount(0);
  await expect(page.locator('.sidebar,.library-workspace,.canvas-list')).toHaveCount(0);
  await page.getByRole('button', { name: 'Fit canvas', exact: true }).click();
  await screenshot(page, 'package-home-desktop.png');
  await mode(page, 'Node'); let dialog = await openNode(page, { x: 45, y: 35 });
  await dialog.getByLabel('Type', { exact: true }).selectOption('container'); await dialog.getByLabel('Variant', { exact: true }).selectOption('gallery');
  await dialog.getByLabel('Title', { exact: true }).fill('A gallery of connected ideas'); await dialog.getByLabel('Summary', { exact: true }).fill('Create child images and passages in the same canvas.');
  await dialog.getByLabel('Style', { exact: true }).selectOption('paper'); await dialog.getByLabel('Shape', { exact: true }).selectOption('hexagon');
  await screenshot(page, 'package-new-node-desktop.png');
  await dialog.getByRole('button', { name: 'Cancel', exact: true }).click();
  assert.deepEqual((await json('/api/library')).project, migrated.project);
  await page.setViewportSize({ width: 600, height: 850 }); await mode(page, 'Hand'); await page.getByRole('button', { name: 'Fit canvas', exact: true }).click();
  await screenshot(page, 'package-home-compact.png');
  await mode(page, 'Node'); dialog = await openNode(page, { x: 40, y: 35 }); await dialog.getByLabel('Type', { exact: true }).selectOption('text');
  await dialog.getByLabel('Title', { exact: true }).fill('A floating annotation'); await dialog.getByLabel('Text', { exact: true }).fill('Place an idea anywhere on the canvas.'); await dialog.getByLabel('Shape', { exact: true }).selectOption('none');
  await screenshot(page, 'package-new-node-compact.png');
  const bounds = await dialog.boundingBox(); assert.ok(bounds.x >= 0 && bounds.x + bounds.width <= 601); assert.ok(bounds.y >= 0 && bounds.y + bounds.height <= 851);
  await dialog.getByRole('button', { name: 'Cancel', exact: true }).click();
  assert.deepEqual((await json('/api/library')).project, migrated.project);
  record('Home uses the shared Node/Link/Delete/Hand tools. Desktop and compact forms fit the screen, and cancellation leaves saved content unchanged.');

  await page.setViewportSize({ width: 1700, height: 1080 });
  await page.goto(`${origin}/?project=${myth.id}&canvas=${zeus.canvasId}`);
  await expect(page.locator('.canvas-stage')).toBeVisible();
  await page.getByRole('button', { name: 'Search nodes', exact: true }).click();
  await page.getByRole('searchbox', { name: 'Find a node', exact: true }).fill(zeus.id);
  await page.locator(`button[data-navigation-node="${zeus.id}"]`).click(); await page.getByRole('searchbox', { name: 'Find a node', exact: true }).fill('');
  await expect(tile(page, zeus.id).locator('img')).toBeVisible(); await screenshot(page, 'package-mythology-canvas.png');
  const imageOpen = page.waitForEvent('popup'); await tile(page, zeus.id).locator('img').click({ modifiers: ['Control'] }); const imageWindow = await imageOpen;
  await expect(imageWindow.getByRole('main', { name: 'Image viewer' })).toBeVisible(); await expect(imageWindow.locator('.media-window-stage img')).toHaveJSProperty('complete', true);
  assert.ok(await imageWindow.locator('.media-window-stage img').evaluate(image => image.naturalWidth > 0)); await screenshot(imageWindow, 'package-mythology-media.png'); await imageWindow.close();
  record('The real mythology canvas retains its composition, right inspector and Ctrl-click image window in the production package.');

  const fixture = newProject('Production tool review');
  const parent = addNode(fixture, fixture.entryCanvasId, 'text', 'A passage with adjacent media'); Object.assign(parent, { x: 110, y: 140, width: 340, height: 200, text: 'A parent can hold pictures, passages, documents and other nodes. Expand its contents in the same canvas.', childLayout: 'spatial', style: { shape: 'rounded', titleSize: 23, fontSize: 17 } });
  const other = addNode(fixture, fixture.entryCanvasId, 'text', 'A related passage'); Object.assign(other, { x: 890, y: 140, width: 250, height: 190, text: 'This node receives a chronological connection.' });
  const external = addNode(fixture, fixture.entryCanvasId, 'canvas-link', 'Original mythology canvas'); Object.assign(external, { x: 110, y: 460, width: 290, height: 180, canvasTarget: { projectId: myth.id, canvasId: zeus.canvasId } });
  await json('/api/projects', 'POST', commitSnapshots(fixture));
  await page.goto(`${origin}/?project=${fixture.id}&canvas=${fixture.entryCanvasId}`); await mode(page, 'Node');
  await tile(page, parent.id).locator('.ms-node-title').click(); dialog = page.getByRole('dialog', { name: 'New Node', exact: true });
  await dialog.getByLabel('Type', { exact: true }).selectOption('media'); await dialog.getByLabel('Title', { exact: true }).fill('A sticky image child');
  await dialog.getByLabel('Media file', { exact: true }).setInputFiles({ name: originalAsset.name, mimeType: originalAsset.mime, buffer: Buffer.from(originalAsset.base64, 'base64') });
  await dialog.getByLabel('Caption', { exact: true }).fill('A media node created with the shared form.'); await dialog.getByLabel('Shape', { exact: true }).selectOption('hexagon');
  await dialog.getByLabel('Width', { exact: true }).fill('330'); await dialog.getByLabel('Height', { exact: true }).fill('290'); await dialog.getByLabel('Behavior', { exact: true }).selectOption('sticky');
  await dialog.getByLabel('Parent edge', { exact: true }).selectOption('right'); await submit(dialog);
  const media = await savedNode(fixture.id, 'A sticky image child'); assert.equal(media.parentId, parent.id); assert.equal(media.style.shape, 'hexagon'); assert.equal(media.snapping.stickyEdges, true);
  const currentMediaProject = await json(`/api/projects/${fixture.id}`); assert.equal(currentMediaProject.assets.find(asset => asset.id === media.assetId).base64, originalAsset.base64);
  dialog = await openNode(page, { x: 520, y: 560 }); await dialog.getByLabel('Type', { exact: true }).selectOption('container'); await dialog.getByLabel('Variant', { exact: true }).selectOption('gallery');
  await dialog.getByLabel('Title', { exact: true }).fill('An addressable gallery'); await dialog.getByLabel('Columns', { exact: true }).fill('2'); await dialog.getByLabel('Rows', { exact: true }).fill('1');
  await dialog.getByLabel('Cell width', { exact: true }).fill('170'); await dialog.getByLabel('Cell height', { exact: true }).fill('110'); await dialog.getByLabel('Initial cells (CSV)', { exact: true }).fill('Athena,Zeus'); await submit(dialog);
  const gallery = await savedNode(fixture.id, 'An addressable gallery'); assert.equal(gallery.containerVariant, 'gallery'); assert.equal(gallery.childArrangement, 'grid');
  const galleryProject = await json(`/api/projects/${fixture.id}`);
  assert.deepEqual(galleryProject.nodes.filter(node => node.parentId === gallery.id).map(node => ({ title: node.title, text: node.text, cell: node.cell })).sort((a, b) => a.title.localeCompare(b.title)), [
    { title: 'A1', text: 'Athena', cell: { column: 1, row: 1 } }, { title: 'B1', text: 'Zeus', cell: { column: 2, row: 1 } },
  ]);
  await mode(page, 'Link'); await tile(page, parent.id).locator('.ms-node-title').click(); const linkDialog = page.getByRole('dialog', { name: 'New Link', exact: true });
  await linkDialog.getByLabel('Destination', { exact: true }).selectOption(other.id); await linkDialog.getByLabel('Relationship', { exact: true }).selectOption('chronological'); await linkDialog.getByLabel('Label', { exact: true }).fill('Read next');
  await linkDialog.getByLabel('Contained in', { exact: true }).selectOption(fixture.canvases[0].rootId);
  await screenshot(page, 'package-new-link-desktop.png');
  await linkDialog.getByRole('button', { name: 'Create link', exact: true }).click(); await expect(linkDialog).toHaveCount(0);
  await expect.poll(async () => (await json(`/api/projects/${fixture.id}`)).links.some(link => link.label === 'Read next')).toBe(true);
  await mode(page, 'Hand'); await page.getByRole('button', { name: 'Fit canvas', exact: true }).click(); await screenshot(page, 'package-created-node-system.png');
  await page.reload(); await expect(tile(page, media.id)).toHaveAttribute('data-shape', 'hexagon');
  const saved = await json(`/api/projects/${fixture.id}`); assert.equal(saved.nodes.find(node => node.id === gallery.id).containerVariant, 'gallery'); assert.equal(saved.links.find(link => link.label === 'Read next').direction, 'forward');
  assert.deepEqual(saved.nodes.find(node => node.id === external.id).canvasTarget, external.canvasTarget);
  record('Media bytes, shaped child nodes, sticky behavior, Gallery variant, addressed children and chronological links save and reload correctly.');
  const exported = await fetch(origin + '/api/export', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(saved) }); assert.equal(exported.status, 200);
  const bytes = Buffer.from(await exported.arrayBuffer()); await writeFile(path.join(qaRoot, 'production-review.meridian'), bytes);
  const form = new FormData(); form.append('file', new Blob([bytes]), 'review.meridian'); const imported = await fetch(origin + '/api/import', { method: 'POST', body: form }); assert.equal(imported.status, 200); assert.deepEqual(await imported.json(), saved);
  await page.getByRole('button', { name: 'Save a copy', exact: true }).click(); const copyDialog = page.getByRole('dialog', { name: 'Save an independent copy', exact: true });
  await copyDialog.getByLabel('Name', { exact: true }).fill('Production review independent copy'); await copyDialog.getByRole('button', { name: 'Continue', exact: true }).click();
  await expect.poll(async () => (await json('/api/projects')).some(project => project.title === 'Production review independent copy')).toBe(true);
  const copySummary = (await json('/api/projects')).find(project => project.title === 'Production review independent copy'); const copy = await json(`/api/projects/${copySummary.id}`), copiedLauncher = copy.nodes.find(node => node.title === external.title);
  assert.notEqual(copy.id, fixture.id); assert.deepEqual(copiedLauncher.canvasTarget, external.canvasTarget);
  const copyUrl = page.url(); const canvasOpen = page.waitForEvent('popup'); await tile(page, copiedLauncher.id).getByRole('button', { name: 'Open Original mythology canvas', exact: true }).click(); const canvasWindow = await canvasOpen;
  await expect.poll(() => new URL(canvasWindow.url()).searchParams.get('project')).toBe(myth.id); await expect.poll(() => new URL(canvasWindow.url()).searchParams.get('canvas')).toBe(zeus.canvasId); assert.equal(page.url(), copyUrl);
  await expect(canvasWindow.locator('.canvas-stage')).toBeVisible(); await canvasWindow.close();
  record('Native export/import preserves all new fields and media. Save a copy retains the original external destination and opens it in a separate window.');
  assert.deepEqual(errors, []);
  const assets = [];
  for (const relative of ['app/Meridian.Host.exe', 'app/Meridian.Host.dll', 'app/wwwroot/editor.js', 'app/wwwroot/viewer.js', 'app/wwwroot/app.css', 'app/wwwroot/scene.css']) assets.push({ path: path.join(packageRoot, relative), sha256: digest(await readFile(path.join(packageRoot, relative))) });
  await writeFile(path.join(qaRoot, 'package-review.json'), JSON.stringify({ passed: true, origin, isolatedDataRoot: dataRoot, originalLibrarySchema: sourceLibrary.schemaVersion, sourceCollection: { id: myth.id, title: myth.title, revision: mythology.revision, imageNodeId: zeus.id }, checks, screenshots: shots, packageHashes: assets, browserErrors: errors }, null, 2));
  process.stdout.write(JSON.stringify({ checks: checks.length, screenshots: shots, errors }, null, 2) + '\n');
} catch (error) {
  if (!page.isClosed()) await screenshot(page, 'package-failure.png').catch(() => {});
  await writeFile(path.join(qaRoot, 'package-failure.json'), JSON.stringify({ message: error.message, stack: error.stack, checks, screenshots: shots, browserErrors: errors }, null, 2));
  throw error;
} finally { await context.close(); await browser.close(); }
