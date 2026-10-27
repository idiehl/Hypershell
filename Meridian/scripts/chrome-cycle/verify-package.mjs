import assert from 'node:assert/strict';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { createHash } from 'node:crypto';
import { mkdir, readFile, writeFile, readdir, lstat } from 'node:fs/promises';
import { chromium, expect } from '@playwright/test';
import { newProject, addCanvas, addNode, commitSnapshots, forkProject } from '../../src/core/model.ts';
import { buildPublication, createPublicationPayload, readPublication } from '../../src/publication.ts';

const config = JSON.parse(await readFile(process.argv[2], 'utf8'));
const { origin, qaRoot, dataRoot, packageRoot, sourceRoot } = config;
const workspaceRoot = path.resolve(import.meta.dirname, '../..');
assert.equal(workspaceRoot.toLowerCase(), path.resolve('I:/dev/Meridian').toLowerCase());
assert.equal(new URL(origin).origin, 'http://127.0.0.1:5293');
assert.equal(path.resolve(packageRoot).toLowerCase(), path.resolve(workspaceRoot, 'output/chrome-package-20261008').toLowerCase());
assert.equal(path.resolve(qaRoot).toLowerCase(), path.resolve(workspaceRoot, 'output/chrome-qa-20261008').toLowerCase());
assert.ok(path.resolve(dataRoot).toLowerCase().startsWith(path.resolve(workspaceRoot, '.test-data').toLowerCase() + path.sep));
assert.notEqual(path.resolve(dataRoot).toLowerCase(), path.resolve(sourceRoot).toLowerCase());
await mkdir(qaRoot, { recursive: true });
const digest = bytes => createHash('sha256').update(bytes).digest('hex');
const checks = [], screenshots = [], browserErrors = [], remoteRequests = [];
const record = text => { checks.push(text); process.stdout.write(text + '\n'); };

async function fileInventory(root, omitHostLock = false) {
  const absoluteRoot = path.resolve(root), rows = [];
  async function walk(directory) {
    const items = await readdir(directory, { withFileTypes: true });
    for (const item of items.sort((a, b) => a.name.localeCompare(b.name))) {
      const filename = path.resolve(directory, item.name);
      assert.ok(filename.startsWith(absoluteRoot + path.sep), 'An inventory file escaped its directory.');
      const info = await lstat(filename); assert.equal(info.isSymbolicLink(), false, `Inventory links are not permitted: ${filename}`);
      if (info.isDirectory()) await walk(filename);
      else if (info.isFile() && !(omitHostLock && item.name === '.host.lock')) rows.push({ path: filename, relativePath: path.relative(absoluteRoot, filename), sha256: digest(await readFile(filename)) });
    }
  }
  assert.equal((await lstat(absoluteRoot)).isSymbolicLink(), false); await walk(absoluteRoot);
  return rows.sort((a, b) => a.relativePath.localeCompare(b.relativePath));
}
const protectedSourceFiles = await fileInventory(sourceRoot, true);
assert.equal(protectedSourceFiles.length, config.copiedFiles);
for (const file of protectedSourceFiles) assert.equal(digest(await readFile(path.join(dataRoot, file.relativePath))), file.sha256, `The isolated copy differs from the saved original: ${file.relativePath}`);

async function json(route, method = 'GET', data) {
  const response = await fetch(origin + route, { method, headers: data === undefined ? {} : { 'Content-Type': 'application/json' }, body: data === undefined ? undefined : JSON.stringify(data) });
  assert.ok(response.ok, `${method} ${route}: ${response.status} ${await response.clone().text()}`);
  return response.json();
}
async function screenshot(page, name) { const filename = path.join(qaRoot, name); await page.screenshot({ path: filename }); screenshots.push(filename); }
const tile = (page, id) => page.locator(`.ms-node[data-node-id="${id}"]`);
const inspector = page => page.locator('.inspector');
async function panel(page, name) {
  await expect(page.locator('.workspace')).toBeVisible();
  if (!(await inspector(page).isVisible())) await page.getByRole('button', { name: 'Show side panel', exact: true }).click();
  await page.getByLabel('Side panel', { exact: true }).selectOption(name);
  await expect(page.getByLabel('Side panel', { exact: true })).toHaveValue(name);
}
async function jump(page, id) {
  await panel(page, 'canvas');
  const search = page.getByRole('searchbox', { name: 'Search canvas nodes', exact: true }); await search.fill(id);
  const action = page.locator(`button[data-jump-to-node="${id}"]`); await expect(action).toHaveCount(1); await action.click();
  await expect(tile(page, id)).toHaveAttribute('aria-selected', 'true');
  if (await search.isVisible()) await search.fill('');
}
async function saved(page) { await expect(page.locator('.save-state')).toHaveText(/^Saved · r\d+$/); }
async function canvasReady(page, title) {
  await expect(page.locator('.workspace')).toBeVisible();
  await expect(page.locator('.canvas-status > span').first()).toContainText(title);
}
async function fileAction(page, name) { await page.getByRole('button', { name: 'File', exact: true }).click(); await page.getByRole('menuitem', { name, exact: true }).click(); }
async function nativeFile(page, command) { await page.evaluate(command => window.dispatchEvent(new CustomEvent('meridian:file-command', { detail: { command } })), command); }
async function navigateHistory(page, direction) { await page.evaluate(direction => window.dispatchEvent(new CustomEvent('meridian:navigate', { detail: { direction } })), direction); }
async function openSelectedCanvas(page) { await panel(page, 'content'); await inspector(page).getByRole('button', { name: 'Open canvas', exact: true }).click(); }
async function appearanceBottom(page, name) {
  await panel(page, 'appearance');
  const body = page.locator('.inspector-panel-body'), bounds = await body.boundingBox(); assert.ok(bounds);
  await page.mouse.move(bounds.x + bounds.width / 2, bounds.y + bounds.height / 2); await page.mouse.wheel(0, 10000);
  await expect.poll(() => body.evaluate(node => node.scrollTop + node.clientHeight >= node.scrollHeight - 2)).toBe(true);
  for (const label of ['Width', 'Height']) {
    const control = inspector(page).getByLabel(label, { exact: true }); await expect(control).toBeInViewport({ ratio: 1 });
    const controlBounds = await control.boundingBox(); assert.ok(controlBounds);
    assert.ok(controlBounds.y + controlBounds.height <= Math.min(bounds.y + bounds.height, page.viewportSize().height) - 5, `${label} must be fully visible above the content boundary.`);
  }
  await inspector(page).getByLabel('Height', { exact: true }).focus();
  await expect(inspector(page).getByLabel('Height', { exact: true })).toBeFocused(); await screenshot(page, name);
}

const originalLibraryBytes = await readFile(path.join(dataRoot, 'library.json')), originalLibrary = JSON.parse(originalLibraryBytes);
const catalog = await json('/api/projects');
let mythology, collection, zeus;
for (const candidate of catalog) {
  const document = await json(`/api/projects/${candidate.id}`), found = document.nodes.find(node => node.id === '005fe68a-bc30-4b11-af1d-4dadec799b1c');
  if (found) { mythology = document; collection = candidate; zeus = found; break; }
}
assert.ok(zeus, 'The actual copied collection must contain the supplied Zeus GUID.');
const originalImage = mythology.assets.find(asset => asset.id === zeus.assetId); assert.ok(originalImage?.mime.startsWith('image/'));
const actualContent = document => ({ id: document.id, entryCanvasId: document.entryCanvasId,
  nodes: document.nodes.map(({ x, y, width, height, collapsed, ...content }) => content).sort((a, b) => a.id.localeCompare(b.id)),
  links: document.links, assets: document.assets, dependencies: document.dependencies });
const actualContentBefore = actualContent(mythology);

const browser = await chromium.launch();
const context = await browser.newContext({ viewport: { width: 1700, height: 1080 } });
const contexts = [context];
async function protectContext(current) {
  current.setDefaultTimeout(10000); current.setDefaultNavigationTimeout(15000);
  current.on('page', page => page.on('pageerror', error => browserErrors.push(error.message)));
  await current.route('**/*', async route => {
    const requestUrl = route.request().url();
    if (/^https?:/i.test(requestUrl) && new URL(requestUrl).origin !== origin) { remoteRequests.push(requestUrl); await route.abort(); }
    else await route.continue();
  });
}
await protectContext(context);
const page = await context.newPage();
let fixture, duplicateId, importedId;
try {
  await page.goto(origin + '/'); await expect(page.locator('.workspace')).toBeVisible(); await saved(page);
  await expect(page.locator('.app-header .application-title')).toHaveText('Atlas Meridian');
  await expect(page.locator('.app-header .brand-mark img')).toHaveAttribute('src', '/brand/meridian.png');
  assert.equal(digest(await (await fetch(origin + '/brand/meridian.png')).arrayBuffer().then(bytes => Buffer.from(bytes))), digest(await readFile(path.join(workspaceRoot, 'assets/brand/meridian.png'))));
  await expect(page.locator('.app-header .project-heading,.app-header .header-actions,.header-overflow')).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Workspace actions', exact: true })).toHaveCount(0);
  await expect(page.getByRole('button', { name: /New section|New subsection|New canvas/i })).toHaveCount(0);
  const tools = page.getByRole('group', { name: 'Canvas tools', exact: true });
  assert.deepEqual(await tools.locator('button').evaluateAll(buttons => buttons.map(button => button.getAttribute('aria-label'))), ['New Node', 'Delete objects', 'Hand', 'Select area']);
  assert.deepEqual(await tools.locator('button').evaluateAll(buttons => buttons.slice(0, 2).map(button => button.textContent)), ['+', '−']);
  const toolBounds = await tools.boundingBox(), toolbarBounds = await page.locator('.canvas-toolbar').boundingBox(); assert.ok(toolBounds && toolbarBounds && toolBounds.x - toolbarBounds.x < 25);
  await panel(page, 'appearance');
  const close = page.getByRole('button', { name: 'Close side panel', exact: true }), selector = page.getByLabel('Side panel', { exact: true });
  assert.equal(await close.locator('path').getAttribute('d'), 'M4 12h16m-7-7 7 7-7 7');
  const closeBounds = await close.boundingBox(), selectorBounds = await selector.boundingBox(); assert.ok(closeBounds.x + closeBounds.width <= selectorBounds.x);
  await close.click(); await expect(inspector(page)).toBeHidden();
  const reopen = page.getByRole('button', { name: 'Show side panel', exact: true }); assert.equal(await reopen.locator('path').getAttribute('d'), 'M20 12H4m7-7-7 7 7 7');
  await reopen.click(); await expect(selector).toHaveValue('appearance');
  await panel(page, 'canvas'); await page.getByRole('button', { name: 'Fit canvas', exact: true }).click(); await screenshot(page, 'package-home-desktop.png');
  const home = await json('/api/library'); assert.equal(home.schemaVersion, 2);
  for (const node of originalLibrary.project.nodes) { const current = home.project.nodes.find(item => item.id === node.id); assert.ok(current); assert.equal(current.title, node.title); assert.equal(current.parentId, node.parentId); }
  record('The actual library keeps its authored GUIDs and hierarchy; the real mark and one Atlas Meridian header replace the repeated branding. The four tools sit together on the left and the matching panel arrows retain Appearance.');

  const launcher = home.project.nodes.find(node => node.kind === 'canvas-link' && (node.canvasTarget ?? home.canvasTargets[node.id])?.projectId === mythology.id && (node.canvasTarget ?? home.canvasTargets[node.id])?.canvasId === zeus.canvasId);
  assert.ok(launcher, 'The copied library must expose the actual Zeus canvas launcher.');
  const homeCanvasTitle = home.project.canvases.find(canvas => canvas.id === launcher.canvasId).title;
  const actualCanvasTitle = mythology.canvases.find(canvas => canvas.id === zeus.canvasId).title;
  process.stdout.write(`Phase: select actual library launcher ${launcher.id} for project ${mythology.id}, canvas ${zeus.canvasId}.\n`);
  await jump(page, launcher.id); await saved(page); const homeUrl = page.url(), initialPages = context.pages().length;
  let canvasPopups = 0; const countPopup = () => { canvasPopups++; }; page.on('popup', countPopup);
  process.stdout.write(`Phase: open selected canvas from ${homeUrl}.\n`);
  await openSelectedCanvas(page); process.stdout.write(`Phase: canvas open clicked; current address ${page.url()}. Waiting for target address.\n`);
  await page.waitForURL(url => url.searchParams.get('project') === mythology.id && url.searchParams.get('canvas') === zeus.canvasId);
  process.stdout.write(`Phase: target address ready ${page.url()}; reveal Zeus ${zeus.id}.\n`);
  await canvasReady(page, actualCanvasTitle); await jump(page, zeus.id); await saved(page);
  await panel(page, 'content'); await expect(inspector(page).getByLabel('Title', { exact: true })).toHaveValue(zeus.title);
  await expect.poll(() => tile(page, zeus.id).locator('img').evaluate(image => image.complete && image.naturalWidth > 0)).toBe(true);
  await expect(tile(page, zeus.id)).not.toContainText(/2 links|hidden connections|⋱/i); await screenshot(page, 'package-zeus-desktop.png');
  const canvasUrl = page.url(); process.stdout.write(`Phase: native Back from ${canvasUrl} to ${homeUrl}.\n`);
  await navigateHistory(page, 'back'); await page.waitForURL(homeUrl); await canvasReady(page, homeCanvasTitle); await saved(page);
  process.stdout.write(`Phase: Back completed; native Forward to ${canvasUrl}.\n`);
  await navigateHistory(page, 'forward'); await page.waitForURL(canvasUrl); await canvasReady(page, actualCanvasTitle); await saved(page); await jump(page, zeus.id); await saved(page);
  process.stdout.write('Phase: actual-canvas Back and Forward completed.\n');
  assert.equal(canvasPopups, 0); assert.equal(context.pages().length, initialPages); page.off('popup', countPopup);
  await appearanceBottom(page, 'package-appearance-bottom-desktop.png');
  await page.setViewportSize({ width: 700, height: 720 }); await appearanceBottom(page, 'package-appearance-bottom-compact.png');
  await panel(page, 'canvas'); await screenshot(page, 'package-canvas-compact.png'); await page.setViewportSize({ width: 1700, height: 1080 });
  assert.deepEqual(actualContent(await json(`/api/projects/${mythology.id}`)), actualContentBefore);
  record('Opening the real mythology canvas stays in one window. Back and Forward return between the library and canvas without popups; Zeus retains its GUID, content and exact image bytes. Appearance reaches Width and Height at desktop and compact sizes.');

  fixture = newProject('Production chrome review'); fixture.canvases[0].title = 'Chrome editing fixture'; fixture.nodes[0].title = 'Chrome editing fixture';
  const details = addCanvas(fixture, 'Fixture details'), note = addNode(fixture, fixture.entryCanvasId, 'text', 'A working source note');
  Object.assign(note, { x: 100, y: 100, width: 340, height: 240, text: 'Original fixture content.', style: { fontSize: 17 } });
  const media = addNode(fixture, fixture.entryCanvasId, 'image', 'Byte-exact fixture image');
  const assetId = crypto.randomUUID(); fixture.assets.push({ ...originalImage, id: assetId }); Object.assign(media, { x: 510, y: 100, width: 250, height: 350, assetId });
  const localLauncher = addNode(fixture, fixture.entryCanvasId, 'canvas-link', details.title); Object.assign(localLauncher, { x: 100, y: 390, width: 260, height: 150, targetId: details.rootId });
  const detailNote = addNode(fixture, details.id, 'text', 'Details note'); Object.assign(detailNote, { x: 100, y: 100, width: 330, height: 200, text: 'The second canvas has its own history location.' });
  await json('/api/projects', 'POST', commitSnapshots(fixture)); await page.goto(`${origin}/?project=${fixture.id}&canvas=${fixture.entryCanvasId}`);
  await jump(page, note.id); await panel(page, 'content');
  await inspector(page).getByLabel('Text body', { exact: true }).fill('Focused typing included by File commands.');
  await fileAction(page, 'Create checkpoint'); const checkpointName = 'Chrome package checkpoint';
  await page.getByRole('dialog').getByLabel('Name', { exact: true }).fill(checkpointName); await page.getByRole('dialog').getByRole('button', { name: 'Continue', exact: true }).click();
  await expect.poll(async () => (await json(`/api/projects/${fixture.id}/checkpoints`)).some(checkpoint => checkpoint.name === checkpointName)).toBe(true);
  const checkpoint = (await json(`/api/projects/${fixture.id}/checkpoints`)).find(checkpoint => checkpoint.name === checkpointName);
  assert.equal((await json(`/api/projects/${fixture.id}/checkpoints/${checkpoint.id}`)).nodes.find(node => node.id === note.id).text, 'Focused typing included by File commands.');
  await fileAction(page, 'Save'); await saved(page);
  await fileAction(page, 'History'); await expect(page.getByRole('dialog', { name: 'History & checkpoints', exact: true })).toContainText(checkpointName); await page.getByRole('dialog').getByRole('button', { name: 'Close', exact: true }).click();
  await fileAction(page, 'Save as'); const copyTitle = 'Chrome independent named canvas';
  await page.getByRole('dialog').getByLabel('Name', { exact: true }).fill(copyTitle); await page.getByRole('dialog').getByRole('button', { name: 'Continue', exact: true }).click();
  await page.waitForURL(url => !!url.searchParams.get('project') && url.searchParams.get('project') !== fixture.id); await saved(page);
  const copyId = new URL(page.url()).searchParams.get('project'); assert.notEqual(copyId, fixture.id); const copy = await json(`/api/projects/${copyId}`);
  assert.equal(copy.title, copyTitle); assert.equal(copy.canvases.find(canvas => canvas.id === copy.entryCanvasId).title, copyTitle); assert.equal(copy.canvases.length, fixture.canvases.length);
  assert.ok(copy.nodes.every(node => !fixture.nodes.some(original => original.id === node.id))); assert.equal(copy.assets[0].base64, originalImage.base64);
  const copyNote = copy.nodes.find(node => node.title === note.title); await jump(page, copyNote.id); await panel(page, 'content'); await inspector(page).getByLabel('Text body', { exact: true }).fill('Typing included by Duplicate.');
  await fileAction(page, 'Duplicate'); await page.waitForURL(url => !!url.searchParams.get('project') && url.searchParams.get('project') !== copyId); await saved(page);
  duplicateId = new URL(page.url()).searchParams.get('project'); assert.notEqual(duplicateId, copyId);
  const duplicated = await json(`/api/projects/${duplicateId}`); assert.equal(duplicated.nodes.find(node => node.title === note.title).text, 'Typing included by Duplicate.');
  assert.ok(duplicated.nodes.every(node => !copy.nodes.some(original => original.id === node.id))); assert.equal(duplicated.assets[0].base64, originalImage.base64);
  await screenshot(page, 'package-file-duplicate-desktop.png');
  record('File Save, checkpoint, History, Save as and Duplicate use the current graph, commit focused typing, and produce independent GUIDs while preserving every included canvas and exact media bytes.');

  const importDraft = forkProject(duplicated); importDraft.title = 'Chrome imported fixture';
  const importResponse = await fetch(origin + '/api/export', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(importDraft) }); assert.equal(importResponse.status, 200);
  const importBytes = Buffer.from(await importResponse.arrayBuffer()), choosing = page.waitForEvent('filechooser'); await fileAction(page, 'Import');
  await (await choosing).setFiles({ name: 'chrome-fixture.meridian', mimeType: 'application/octet-stream', buffer: importBytes });
  await page.waitForURL(url => url.searchParams.get('project') === importDraft.id); await saved(page); importedId = importDraft.id;
  const imported = await json(`/api/projects/${importedId}`); assert.equal(imported.assets[0].base64, originalImage.base64);
  await fileAction(page, 'Export canvas'); const downloading = page.waitForEvent('download');
  await page.getByRole('dialog', { name: 'Download editable canvas', exact: true }).getByRole('button', { name: 'Download canvas', exact: true }).click();
  const editablePath = path.join(qaRoot, 'chrome-review.meridian'); await (await downloading).saveAs(editablePath);
  const exportForm = new FormData(); exportForm.append('file', new Blob([await readFile(editablePath)]), 'chrome-review.meridian');
  const inspectedExport = await fetch(origin + '/api/import', { method: 'POST', body: exportForm }); assert.equal(inspectedExport.status, 200);
  const exportCopy = await inspectedExport.json(); assert.equal(exportCopy.assets[0].base64, originalImage.base64); assert.deepEqual(exportCopy.nodes, imported.nodes);
  await fileAction(page, 'Preview'); await page.getByRole('dialog', { name: 'Preview published canvas', exact: true }).getByRole('button', { name: 'Open preview', exact: true }).click();
  await expect(page.getByRole('dialog', { name: 'Published preview', exact: true })).toBeVisible();
  await expect(page.frameLocator('.preview-frame').locator('.ms-viewport')).toBeVisible(); await screenshot(page, 'package-preview-desktop.png');
  await page.getByRole('dialog', { name: 'Published preview', exact: true }).getByRole('button', { name: 'Close', exact: true }).click();
  await page.getByRole('button', { name: 'File', exact: true }).click(); await expect(page.getByRole('menuitem', { name: 'Publish', exact: true })).toBeVisible(); await page.keyboard.press('Escape');
  record('File Import replaces the current workspace in the same window, Export round-trips the editable graph and media bytes, and Preview renders the embedded viewer. Publish remains available without publishing anything online.');

  const nativeContext = await browser.newContext({ viewport: { width: 1700, height: 1080 } }); contexts.push(nativeContext); await protectContext(nativeContext);
  await nativeContext.addInitScript(() => { window.__meridianNativeChrome = true; }); const nativePage = await nativeContext.newPage();
  await nativePage.goto(`${origin}/?project=${importedId}&canvas=${imported.entryCanvasId}`); await expect(nativePage.locator('.workspace')).toBeVisible();
  await expect(nativePage.locator('.application-menubar')).toHaveCount(0); await expect(nativePage.getByRole('button', { name: 'File', exact: true })).toHaveCount(0);
  await nativeFile(nativePage, 'export'); await expect(nativePage.getByRole('dialog', { name: 'Download editable canvas', exact: true })).toBeVisible(); await nativePage.getByRole('dialog').getByRole('button', { name: 'Cancel', exact: true }).click();
  await screenshot(nativePage, 'package-native-header-workspace.png');
  record('The native chrome marker removes the duplicate web File/navigation row while native File dispatch still reaches the same export form.');

  const payload = createPublicationPayload(imported); assert.deepEqual(payload.missing, []); assert.equal(payload.project.assets[0].base64, originalImage.base64);
  const viewerJs = await readFile(path.join(packageRoot, 'app/wwwroot/viewer.js'), 'utf8'), css = await readFile(path.join(packageRoot, 'app/wwwroot/scene.css'), 'utf8');
  const html = buildPublication(imported, viewerJs, css), publicationFile = path.join(qaRoot, 'chrome-review.html'); await writeFile(publicationFile, html);
  const publicationCopy = readPublication(html); assert.equal(publicationCopy.assets[0].base64, originalImage.base64); assert.equal(publicationCopy.canvases.length, imported.canvases.length);
  const offlineContext = await browser.newContext({ viewport: { width: 1700, height: 1080 }, offline: true }); contexts.push(offlineContext);
  offlineContext.setDefaultTimeout(10000); offlineContext.setDefaultNavigationTimeout(15000);
  offlineContext.on('page', page => page.on('pageerror', error => browserErrors.push(error.message)));
  const offline = await offlineContext.newPage(); offline.on('request', request => { if (/^https?:/.test(request.url())) remoteRequests.push(request.url()); });
  await offline.goto(pathToFileURL(publicationFile).href); await expect(offline.locator('.ms-viewport')).toBeVisible();
  await expect.poll(() => tile(offline, imported.nodes.find(node => node.kind === 'image').id).locator('img').evaluate(image => image.complete && image.naturalWidth > 0)).toBe(true);
  await screenshot(offline, 'package-publication-offline.png');
  record('The standalone viewer still opens offline with all included canvases and exact Zeus-derived fixture image bytes, with no remote requests.');

  assert.deepEqual(actualContent(await json(`/api/projects/${mythology.id}`)), actualContentBefore);
  assert.deepEqual(await fileInventory(sourceRoot, true), protectedSourceFiles, 'Live saved file paths and bytes must remain unchanged.');
  assert.deepEqual(browserErrors, []); assert.deepEqual(remoteRequests, []);
  const packageHashes = (await fileInventory(path.join(packageRoot, 'app'))).map(({ path, sha256 }) => ({ path, sha256 }));
  await writeFile(path.join(qaRoot, 'package-review.json'), JSON.stringify({ passed: true, origin, isolatedDataRoot: dataRoot, copiedSourceFiles: config.copiedFiles,
    sourceCollection: { id: collection.id, title: collection.title, revision: mythology.revision, nodeId: zeus.id, imageSha256: digest(Buffer.from(originalImage.base64, 'base64')) },
    fixtureProjectId: fixture.id, duplicateProjectId: duplicateId, importedProjectId: importedId, savedDataUnchanged: true, protectedSourceFiles,
    checks, screenshots, packageHashes, browserErrors, remoteRequests }, null, 2));
  process.stdout.write(JSON.stringify({ checks: checks.length, screenshots, packageFiles: packageHashes.length, browserErrors }, null, 2) + '\n');
} catch (error) {
  if (!page.isClosed()) await screenshot(page, 'package-failure.png').catch(() => {});
  const failure = { passed: false, message: error.message, stack: error.stack, checks, screenshots, browserErrors, remoteRequests };
  await writeFile(path.join(qaRoot, 'package-review.json'), JSON.stringify(failure, null, 2)); await writeFile(path.join(qaRoot, 'package-failure.json'), JSON.stringify(failure, null, 2)); throw error;
} finally { for (const current of contexts.reverse()) await current.close(); await browser.close(); }
