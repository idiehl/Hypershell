import assert from 'node:assert/strict';
import path from 'node:path';
import { createHash, randomUUID } from 'node:crypto';
import { mkdir, readFile, writeFile, readdir, lstat } from 'node:fs/promises';
import { chromium, expect } from '@playwright/test';
import { addNode, commitSnapshots, newProject } from '../../src/core/model.ts';

// Review only the owned package host. Original saved content and its copied
// projects/library are read-only; one new toolbar fixture is the only write.
const config = JSON.parse(await readFile(process.argv[2], 'utf8'));
const { origin, qaRoot, dataRoot, packageRoot, sourceRoot } = config;
const workspaceRoot = path.resolve(import.meta.dirname, '../..');
assert.equal(workspaceRoot.toLowerCase(), path.resolve('I:/dev/Meridian').toLowerCase());
assert.equal(new URL(origin).origin, 'http://127.0.0.1:5300');
assert.equal(path.resolve(packageRoot).toLowerCase(), path.resolve(workspaceRoot, 'output/toolbar-layout-package-20261009').toLowerCase());
assert.equal(path.resolve(qaRoot).toLowerCase(), path.resolve(workspaceRoot, 'output/toolbar-layout-qa-20261009').toLowerCase());
assert.ok(path.resolve(dataRoot).toLowerCase().startsWith(path.resolve(workspaceRoot, '.test-data').toLowerCase() + path.sep));
assert.notEqual(path.resolve(dataRoot).toLowerCase(), path.resolve(sourceRoot).toLowerCase());
assert.ok(config.ownedHostId > 0, 'A specifically owned isolated host is required.');
await mkdir(qaRoot, { recursive: true });
const digest = bytes => createHash('sha256').update(bytes).digest('hex');
const checks = [], screenshots = [], browserErrors = [], remoteRequests = [], blockedWrites = [], fixtureWrites = [];
const record = message => { checks.push(message); process.stdout.write(message + '\n'); };
async function fileInventory(root, omitHostLock = false) {
  const absoluteRoot = path.resolve(root), rows = [];
  async function walk(directory) {
    for (const item of (await readdir(directory, { withFileTypes: true })).sort((a, b) => a.name.localeCompare(b.name))) {
      const filename = path.resolve(directory, item.name);
      assert.ok(filename.startsWith(absoluteRoot + path.sep));
      const info = await lstat(filename);
      assert.equal(info.isSymbolicLink(), false, `Inventory links are not permitted: ${filename}`);
      if (info.isDirectory()) await walk(filename);
      else if (info.isFile() && !(omitHostLock && item.name === '.host.lock')) rows.push({ path: filename, relativePath: path.relative(absoluteRoot, filename), sha256: digest(await readFile(filename)) });
    }
  }
  assert.equal((await lstat(absoluteRoot)).isSymbolicLink(), false);
  await walk(absoluteRoot); return rows.sort((a, b) => a.relativePath.localeCompare(b.relativePath));
}
const protectedSourceFiles = await fileInventory(sourceRoot, true), protectedCopiedFiles = await fileInventory(dataRoot, true);
assert.equal(protectedSourceFiles.length, config.copiedFiles);
assert.deepEqual(protectedCopiedFiles.map(({ relativePath, sha256 }) => ({ relativePath, sha256 })), protectedSourceFiles.map(({ relativePath, sha256 }) => ({ relativePath, sha256 })));
const fixtureId = randomUUID();
async function json(route, method = 'GET', data, allow404 = false) {
  assert.ok(route.startsWith('/api/') || route === '/__meridian_status');
  if (!['GET', 'HEAD'].includes(method)) {
    assert.ok(route === '/api/projects' && method === 'POST' && data?.id === fixtureId, 'Only creation of the fresh toolbar fixture may write data.');
    fixtureWrites.push(`${method} ${route}`);
  }
  const url = new URL(route, origin); assert.equal(url.origin, 'http://127.0.0.1:5300');
  const response = await fetch(url, { method, headers: data === undefined ? {} : { 'Content-Type': 'application/json' }, body: data === undefined ? undefined : JSON.stringify(data) });
  if (allow404 && response.status === 404) return null;
  assert.ok(response.ok, `${method} ${route}: ${response.status} ${await response.clone().text()}`);
  return response.status === 204 ? null : response.json();
}
assert.equal((await json('/__meridian_status')).application, 'atlas-meridian');
const copiedCatalog = await json('/api/projects'), copiedProjects = [];
for (const entry of copiedCatalog) copiedProjects.push({ entry, document: await json(`/api/projects/${entry.id}`), recovery: await json(`/api/projects/${entry.id}/recovery`, 'GET', undefined, true) });
const copiedLibrary = await json('/api/library'), copiedLibraryRecovery = await json('/api/library/recovery', 'GET', undefined, true);
const protectedEnvelopes = { catalog: copiedCatalog, projects: copiedProjects, library: copiedLibrary, libraryRecovery: copiedLibraryRecovery };
await writeFile(path.join(qaRoot, 'copied-content-baselines.json'), JSON.stringify({ envelopes: protectedEnvelopes, sourceFiles: protectedSourceFiles, copiedFiles: protectedCopiedFiles }, null, 2));
record('Recorded all copied project/library documents, recoveries and original saved-file hashes through read-only requests.');
const fixture = newProject('Toolbar layout · isolated production package review'); fixture.id = fixtureId;
const note = addNode(fixture, fixture.entryCanvasId, 'media', 'A toolbar fixture');
Object.assign(note, { x: 120, y: 130, width: 260, height: 100, text: 'Content stays intact.', contentType: 'text', placement: 'free' });
const baseline = await json('/api/projects', 'POST', commitSnapshots(fixture));
const browser = await chromium.launch(), contexts = [];
async function context(options = {}) {
  const current = await browser.newContext({ viewport: { width: 1500, height: 950 }, ...options }); contexts.push(current);
  current.setDefaultTimeout(15000); current.setDefaultNavigationTimeout(18000);
  current.on('page', page => page.on('pageerror', error => browserErrors.push(error.message)));
  await current.route('**/*', async route => {
    const request = route.request(), url = request.url();
    if (/^https?:/i.test(url) && new URL(url).origin !== origin) { remoteRequests.push(url); await route.abort(); }
    else if (/^https?:/i.test(url) && !['GET', 'HEAD'].includes(request.method())) { blockedWrites.push(`${request.method()} ${url}`); await route.abort(); }
    else await route.continue();
  });
  return current;
}
async function open(page) {
  await page.goto(`${origin}/?project=${fixtureId}&canvas=${fixture.entryCanvasId}`);
  await expect(page.locator('.workspace')).toBeVisible(); await expect(page.locator('.save-state')).toHaveText(`Saved · r${baseline.revision}`);
}
async function screenshot(page, name) { const filename = path.join(qaRoot, name); await page.screenshot({ path: filename }); screenshots.push(filename); }
async function closeDialog(page, name) { const dialog = page.getByRole('dialog', { name, exact: true }); await expect(dialog).toBeVisible(); await dialog.getByRole('button', { name: 'Close dialog', exact: true }).click(); }
const editorContext = await context(), page = await editorContext.newPage();
try {
  await open(page);
  for (const width of [1500, 760]) {
    await page.setViewportSize({ width, height: 950 });
    const tools = page.getByRole('group', { name: 'Canvas tools', exact: true });
    assert.deepEqual(await tools.getByRole('button').evaluateAll(buttons => buttons.map(button => button.getAttribute('aria-label'))), ['New Node', 'Delete objects', 'Single select', 'Select area', 'Reset view']);
    assert.deepEqual(await page.locator('.history-tools button').evaluateAll(buttons => buttons.map(button => button.getAttribute('aria-label'))), ['Undo', 'Redo']);
    const layout = await page.locator('.canvas-toolbar').evaluate(toolbar => {
      const tools = toolbar.querySelector('.workspace-tools'), history = toolbar.querySelector('.history-tools');
      const a = tools.getBoundingClientRect(), b = history.getBoundingClientRect(), bar = toolbar.getBoundingClientRect();
      return { offset: a.left - bar.left, gap: b.left - a.right, row: Math.abs(a.top + a.height / 2 - b.top - b.height / 2), historyPadding: parseFloat(getComputedStyle(history).paddingLeft), toolsBorder: getComputedStyle(tools).borderLeftWidth,
        historyBorder: getComputedStyle(history).borderLeftWidth, dividers: [...toolbar.children].filter(child => parseFloat(getComputedStyle(child).borderLeftWidth) > 0).length };
    });
    assert.ok(layout.offset <= 16 && layout.gap >= 0 && layout.gap <= 18 && layout.row <= 3 && layout.historyPadding >= 7, `Left toolbar alignment at ${width}px: ${JSON.stringify(layout)}`);
    assert.equal(layout.toolsBorder, '0px'); assert.equal(layout.historyBorder, '1px'); assert.equal(layout.dividers, 1);
    await expect(page.getByRole('button', { name: 'Canvas panel', exact: true })).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'More canvas tools', exact: true })).toHaveCount(0);
    await expect(page.locator('.canvas-settings-tools')).toHaveCount(0);
    await screenshot(page, `package-toolbar-${width}.png`);
  }
  record('Wide and compact production layouts show all five canvas controls followed by one divider and adjacent Undo/Redo; removed search/overflow buttons are absent.');
  await page.setViewportSize({ width: 1500, height: 950 });
  const reset = page.getByRole('group', { name: 'Canvas tools', exact: true }).getByRole('button', { name: 'Reset view', exact: true });
  await reset.click(); await expect(page.locator('.ms-world')).toHaveCSS('transform', 'matrix(1, 0, 0, 1, 45, 45)');
  await page.getByRole('button', { name: 'Zoom in', exact: true }).click(); await expect(page.locator('.ms-zoom')).toHaveText('125%');
  await reset.click(); await expect(page.locator('.ms-zoom')).toHaveText('100%'); await expect(page.locator('.ms-world')).toHaveCSS('transform', 'matrix(1, 0, 0, 1, 45, 45)');
  record('Reset view works from its final canvas-tool position and restores the camera without changing content.');
  const file = page.getByRole('button', { name: 'File', exact: true }), tools = page.getByRole('button', { name: 'Tools', exact: true });
  await expect(file).toHaveCSS('color', 'rgb(255, 255, 255)'); await expect(tools).toHaveCSS('color', 'rgb(255, 255, 255)');
  const fileBox = await file.boundingBox(), toolsBox = await tools.boundingBox(); assert.ok(toolsBox.x >= fileBox.x + fileBox.width);
  await tools.click(); const menu = page.getByRole('menu', { name: 'Tools', exact: true });
  assert.deepEqual(await menu.getByRole('menuitem').allTextContents(), ['Modules', 'Composition tools']);
  await screenshot(page, 'package-tools-menu.png'); await page.keyboard.press('Escape');
  for (const [label, dialog] of [['Modules', 'Module library'], ['Composition tools', 'Composition tools']]) {
    await tools.click(); await page.getByRole('menuitem', { name: label, exact: true }).click(); await closeDialog(page, dialog);
  }
  record('The white File and Tools menus sit together; Tools contains exactly Modules and Composition tools and both original dialogs open.');
  await page.getByRole('button', { name: 'Close side panel', exact: true }).click();
  const show = page.getByRole('button', { name: 'Show side panel', exact: true }); await expect(show).toBeVisible();
  await expect(page.locator('.canvas-panel-tools').getByRole('button')).toHaveCount(1);
  const showBox = await show.boundingBox(), bar = await page.locator('.canvas-toolbar').boundingBox(); assert.ok(bar.x + bar.width - showBox.x - showBox.width <= 20);
  await show.click(); await expect(page.getByRole('combobox', { name: 'Side panel', exact: true })).toBeFocused();
  await page.getByRole('combobox', { name: 'Side panel', exact: true }).selectOption('appearance');
  await page.keyboard.press('Control+f'); await expect(page.getByRole('searchbox', { name: 'Search canvas nodes', exact: true })).toBeFocused();
  await expect(page.getByRole('combobox', { name: 'Side panel', exact: true })).toHaveValue('canvas');
  record('The retained right arrow reopens the panel; canvas search remains available through Ctrl+F.');
  const nativeContext = await context(); await nativeContext.addInitScript(() => { window.__meridianNativeChrome = true; });
  const nativePage = await nativeContext.newPage(); await open(nativePage); await expect(nativePage.locator('.application-menubar')).toHaveCount(0);
  const dispatch = command => nativePage.evaluate(command => window.dispatchEvent(new CustomEvent('meridian:tool-command', { detail: { command } })), command);
  for (const command of ['inspect', 'delete', 'constructor']) await dispatch(command);
  await expect(nativePage.getByRole('dialog')).toHaveCount(0);
  for (const [command, dialog] of [['modules', 'Module library'], ['composition', 'Composition tools']]) { await dispatch(command); await closeDialog(nativePage, dialog); }
  await screenshot(nativePage, 'package-native-chrome-bridge.png');
  record('Native chrome suppresses duplicate web menus; strict native Tools commands open both dialogs and ignore unsupported actions.');
  assert.deepEqual(await json(`/api/projects/${fixtureId}`), baseline);
  for (const copied of copiedProjects) { assert.deepEqual(await json(`/api/projects/${copied.entry.id}`), copied.document); assert.deepEqual(await json(`/api/projects/${copied.entry.id}/recovery`, 'GET', undefined, true), copied.recovery); }
  assert.deepEqual((await json('/api/projects')).filter(entry => entry.id !== fixtureId), copiedCatalog);
  assert.deepEqual(await json('/api/library'), copiedLibrary); assert.deepEqual(await json('/api/library/recovery', 'GET', undefined, true), copiedLibraryRecovery);
  const copiedAfter = await fileInventory(dataRoot, true), previousNames = new Set(protectedCopiedFiles.map(file => file.relativePath));
  for (const file of protectedCopiedFiles) assert.equal(copiedAfter.find(current => current.relativePath === file.relativePath)?.sha256, file.sha256, `Protected copied file: ${file.relativePath}`);
  const fixturePrefix = path.join('projects', fixtureId) + path.sep;
  for (const file of copiedAfter.filter(current => !previousNames.has(current.relativePath))) assert.ok(file.relativePath.startsWith(fixturePrefix), `Unexpected copied-data file: ${file.relativePath}`);
  assert.deepEqual(await fileInventory(sourceRoot, true), protectedSourceFiles);
  assert.deepEqual(browserErrors, []); assert.deepEqual(remoteRequests, []); assert.deepEqual(blockedWrites, []);
  record('Fixture, every copied canvas/library/recovery and original saved files remain unchanged; browser errors, remote requests and blocked writes are empty.');
  const packageHashes = (await fileInventory(path.join(packageRoot, 'app'))).map(({ path, sha256 }) => ({ path, sha256 }));
  await writeFile(path.join(qaRoot, 'package-review.json'), JSON.stringify({ passed: true, origin, isolatedDataRoot: dataRoot, copiedSourceFiles: config.copiedFiles, fixtureProjectId: fixtureId, fixtureCanvasId: fixture.entryCanvasId,
    savedDataUnchanged: true, copiedDataUnchanged: true, protectedSourceFiles, protectedCopiedFiles, protectedEnvelopeSha256: digest(JSON.stringify(protectedEnvelopes)), checks, screenshots, packageHashes, browserErrors, remoteRequests, blockedWrites, fixtureWrites }, null, 2));
  process.stdout.write(JSON.stringify({ checks: checks.length, screenshots, packageFiles: packageHashes.length, browserErrors, remoteRequests, blockedWrites }, null, 2) + '\n');
} catch (error) {
  if (!page.isClosed()) await screenshot(page, 'package-failure.png').catch(() => {});
  const failure = { passed: false, message: error.message, stack: error.stack, checks, screenshots, browserErrors, remoteRequests, blockedWrites, fixtureWrites };
  await writeFile(path.join(qaRoot, 'package-review.json'), JSON.stringify(failure, null, 2)); await writeFile(path.join(qaRoot, 'package-failure.json'), JSON.stringify(failure, null, 2)); throw error;
} finally { for (const current of contexts.reverse()) await current.close(); await browser.close(); }
