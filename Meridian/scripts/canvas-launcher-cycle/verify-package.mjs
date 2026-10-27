import assert from 'node:assert/strict';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { createHash, randomUUID } from 'node:crypto';
import { mkdir, readFile, writeFile, readdir, lstat } from 'node:fs/promises';
import { chromium, expect } from '@playwright/test';
import { addCanvas, addNode, commitSnapshots, newProject } from '../../src/core/model.ts';
import { buildPublication } from '../../src/publication.ts';

// Only the owned 5300 host with exact installed backend bits is inspected.
// Storage is fresh and fixture-only. Original saved content is never accessed.
const config = JSON.parse(await readFile(process.argv[2], 'utf8'));
const { origin, qaRoot, dataRoot, packageRoot } = config;
const workspaceRoot = path.resolve(import.meta.dirname, '../..');
assert.equal(workspaceRoot.toLowerCase(), path.resolve('I:/dev/Meridian').toLowerCase());
assert.equal(new URL(origin).origin, 'http://127.0.0.1:5300');
assert.equal(path.resolve(packageRoot).toLowerCase(), path.resolve(workspaceRoot, 'output/canvas-launcher-package-20261010').toLowerCase());
assert.equal(path.resolve(qaRoot).toLowerCase(), path.resolve(workspaceRoot, 'output/canvas-launcher-qa-20261010').toLowerCase());
assert.ok(path.resolve(dataRoot).toLowerCase().startsWith(path.resolve(workspaceRoot, '.test-data').toLowerCase() + path.sep));
assert.equal(config.reviewMode, 'static-assets-on-installed-backend'); assert.equal(config.fixtureOnly, true); assert.equal(config.originalDataAccessed, false);
assert.ok(config.ownedHostId > 0, 'A specifically owned isolated host is required.');
await mkdir(qaRoot, { recursive: true });
const digest = bytes => createHash('sha256').update(bytes).digest('hex');
const checks = [], screenshots = [], launcherMeasurements = [], browserErrors = [], remoteRequests = [], blockedWrites = [], fixtureWrites = [];
const record = message => { checks.push(message); process.stdout.write(message + '\n'); };
async function fileInventory(root, omitHostLock = false) {
  const absoluteRoot = path.resolve(root), rows = [];
  async function walk(directory) {
    for (const item of (await readdir(directory, { withFileTypes: true })).sort((a, b) => a.name.localeCompare(b.name))) {
      const filename = path.resolve(directory, item.name); assert.ok(filename.startsWith(absoluteRoot + path.sep));
      const info = await lstat(filename); assert.equal(info.isSymbolicLink(), false, `Inventory links are not permitted: ${filename}`);
      if (info.isDirectory()) await walk(filename);
      else if (info.isFile() && !(omitHostLock && item.name === '.host.lock')) rows.push({ path: filename, relativePath: path.relative(absoluteRoot, filename), sha256: digest(await readFile(filename)) });
    }
  }
  assert.equal((await lstat(absoluteRoot)).isSymbolicLink(), false);
  await walk(absoluteRoot); return rows.sort((a, b) => a.relativePath.localeCompare(b.relativePath));
}
const initialIsolatedFiles = await fileInventory(dataRoot, true);
const fixtureId = randomUUID();
async function json(route, method = 'GET', data, allow404 = false) {
  assert.ok(route.startsWith('/api/') || route === '/__meridian_status');
  if (!['GET', 'HEAD'].includes(method)) {
    assert.ok(route === '/api/projects' && method === 'POST' && data?.id === fixtureId, 'Only creation of the fresh launcher fixture may write data.');
    fixtureWrites.push(`${method} ${route}`);
  }
  const url = new URL(route, origin); assert.equal(url.origin, 'http://127.0.0.1:5300');
  const response = await fetch(url, { method, headers: data === undefined ? {} : { 'Content-Type': 'application/json' }, body: data === undefined ? undefined : JSON.stringify(data) });
  if (allow404 && response.status === 404) return null;
  assert.ok(response.ok, `${method} ${route}: ${response.status} ${await response.clone().text()}`);
  return response.status === 204 ? null : response.json();
}
assert.equal((await json('/__meridian_status')).application, 'atlas-meridian');
const initialCatalog = await json('/api/projects'); assert.deepEqual(initialCatalog, []);
record('Started fresh fixture-only storage with the exact installed backend; original saved content was never accessed.');
const fixture = newProject('Canvas launcher · isolated production package review'); fixture.id = fixtureId;
const destination = addCanvas(fixture, 'Launcher destination');
const launcher = addNode(fixture, fixture.entryCanvasId, 'canvas-link', 'A compact canvas launcher');
Object.assign(launcher, { x: 120, y: 130, width: 260, height: 100, targetId: destination.id, placement: 'free' });
for (const canvas of fixture.canvases) canvas.snapping = { stickyEdges: false, smartAlign: false, snapToGrid: false };
const gallery = addNode(fixture, fixture.entryCanvasId, 'container', 'A canvas gallery');
Object.assign(gallery, { x: 500, y: 130, width: 320, height: 180, containerVariant: 'gallery', childLayout: 'contained', collapsed: false, placement: 'free' });
const galleryLauncher = addNode(fixture, fixture.entryCanvasId, 'canvas-link', destination.title);
Object.assign(galleryLauncher, { parentId: gallery.id, x: 510, y: 175, width: 280, height: 100, targetId: destination.rootId });
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
async function screenshot(page, name) { const filename = path.join(qaRoot, name); await page.screenshot({ path: filename }); screenshots.push(filename); }
async function launcherStyle(button) {
  return button.evaluate(element => {
    const box = getComputedStyle(element), spiral = getComputedStyle(element.querySelector('.ms-canvas-spiral')), core = getComputedStyle(element.querySelector('.ms-canvas-core'));
    return { width: parseFloat(box.width), height: parseFloat(box.height), text: element.textContent.trim(), accessibleName: element.getAttribute('aria-label'),
      spiralStroke: spiral.stroke, coreFill: core.fill, spiralAnimation: spiral.animationName, spiralDuration: spiral.animationDuration, spiralPlayState: spiral.animationPlayState,
      coreAnimation: core.animationName, coreDuration: core.animationDuration, corePlayState: core.animationPlayState };
  });
}
async function verifyLauncher(button, name, reduced = false) {
  await expect(button).toBeVisible(); await expect(button.locator('svg')).toHaveCount(1);
  await expect(button.locator('.ms-canvas-spiral')).toHaveCount(1); await expect(button.locator('.ms-canvas-core')).toHaveCount(1);
  const normal = await launcherStyle(button); launcherMeasurements.push({ name, state: 'normal', ...normal });
  assert.equal(normal.width, 28); assert.equal(normal.height, 28); assert.equal(normal.text, '');
  assert.ok(normal.accessibleName?.startsWith('Open '), 'The icon has a meaningful accessible destination name.');
  assert.equal(normal.spiralStroke, 'rgb(255, 255, 255)');
  assert.equal(normal.coreFill, 'rgb(114, 230, 160)');
  assert.equal(normal.spiralAnimation, 'none'); assert.equal(normal.coreAnimation, 'none');
  await button.hover(); const hover = await launcherStyle(button); launcherMeasurements.push({ name, state: 'hover', ...hover });
  if (reduced) { assert.equal(hover.spiralAnimation, 'none'); assert.equal(hover.coreAnimation, 'none'); }
  else { assert.equal(hover.spiralAnimation, 'ms-canvas-spin'); assert.equal(hover.spiralDuration, '8s'); assert.equal(hover.spiralPlayState, 'running'); assert.equal(hover.coreAnimation, 'ms-canvas-pulse'); assert.equal(hover.coreDuration, '2.4s'); }
  return { normal, hover };
}
const editorContext = await context(), page = await editorContext.newPage();
try {
  await page.goto(`${origin}/?project=${fixtureId}&canvas=${fixture.entryCanvasId}`); await expect(page.locator('.workspace')).toBeVisible();
  await page.getByRole('button', { name: 'Reset view', exact: true }).click();
  const editorButton = page.locator(`.ms-node[data-node-id="${launcher.id}"] .ms-canvas-open`);
  const editorAppearance = await verifyLauncher(editorButton, 'editor fixture');
  await page.mouse.move(10, 10); const galleryButton = page.locator(`.ms-gallery[data-gallery-id="${gallery.id}"] .ms-canvas-open`);
  await verifyLauncher(galleryButton, 'editor gallery'); await screenshot(page, 'package-gallery-launcher-hover.png');
  await page.mouse.move(10, 10); await editorButton.hover();
  await screenshot(page, 'package-editor-launcher-hover.png'); await editorButton.click();
  await expect(page).toHaveURL(new RegExp(`canvas=${destination.id}`));
  assert.deepEqual(await json(`/api/projects/${fixtureId}`), baseline);
  record('The editor launcher remains 28px, animates on hover and navigates to its canvas in the current window without modifying its document.');
  const viewerJS = await readFile(path.join(packageRoot, 'app/wwwroot/viewer.js'), 'utf8'), sceneCSS = await readFile(path.join(packageRoot, 'app/wwwroot/scene.css'), 'utf8');
  const offlinePath = path.join(qaRoot, 'package-canvas-launcher-offline.html'); await writeFile(offlinePath, buildPublication(baseline, viewerJS, sceneCSS));
  const viewerContext = await context({ offline: true }), viewer = await viewerContext.newPage(); await viewer.goto(pathToFileURL(offlinePath).href);
  const payload = await viewer.locator('#meridian-data').textContent(), viewerButton = viewer.locator(`.ms-node[data-node-id="${launcher.id}"] .ms-canvas-open`);
  const viewerAppearance = await verifyLauncher(viewerButton, 'portable offline viewer');
  for (const key of ['width', 'height', 'spiralStroke', 'coreFill', 'spiralAnimation', 'spiralDuration', 'coreAnimation', 'coreDuration']) assert.equal(viewerAppearance.hover[key], editorAppearance.hover[key], `Editor/viewer launcher consistency: ${key}`);
  await viewer.mouse.move(10, 10); await verifyLauncher(viewer.locator(`.ms-gallery[data-gallery-id="${gallery.id}"] .ms-canvas-open`), 'portable offline gallery');
  await viewer.mouse.move(10, 10); await viewerButton.hover();
  await screenshot(viewer, 'package-viewer-launcher-hover.png'); await viewerButton.click();
  await expect(viewer.getByRole('combobox', { name: 'Choose canvas', exact: true })).toHaveValue(destination.id);
  assert.equal(await viewer.locator('#meridian-data').textContent(), payload);
  record('The portable offline viewer shares the editor launcher geometry, colors and animation; launching preserves the publication source payload.');
  const reducedContext = await context({ reducedMotion: 'reduce' }), reduced = await reducedContext.newPage();
  await reduced.goto(`${origin}/?project=${fixtureId}&canvas=${fixture.entryCanvasId}`);
  await verifyLauncher(reduced.locator(`.ms-node[data-node-id="${launcher.id}"] .ms-canvas-open`), 'reduced-motion editor', true); await screenshot(reduced, 'package-launcher-reduced-motion.png');
  record('Reduced motion suppresses the launcher animation while retaining the compact icon and accessible control.');
  assert.deepEqual(await json(`/api/projects/${fixtureId}`), baseline);
  assert.deepEqual((await json('/api/projects')).filter(entry => entry.id !== fixtureId), initialCatalog);
  assert.equal(await json(`/api/projects/${fixtureId}/recovery`, 'GET', undefined, true), null);
  const isolatedFiles = await fileInventory(dataRoot, true);
  for (const file of initialIsolatedFiles) assert.equal(isolatedFiles.find(current => current.relativePath === file.relativePath)?.sha256, file.sha256, `Initial owned file: ${file.relativePath}`);
  assert.deepEqual(browserErrors, []); assert.deepEqual(remoteRequests, []); assert.deepEqual(blockedWrites, []);
  record('The independent fixture and offline payload remain unchanged; original saved content was not accessed; browser errors, remote requests and blocked writes are empty.');
  const packageHashes = (await fileInventory(path.join(packageRoot, 'app'))).map(({ path, sha256 }) => ({ path, sha256 }));
  await writeFile(path.join(qaRoot, 'package-review.json'), JSON.stringify({ passed: true, reviewMode: config.reviewMode, fixtureOnly: true, originalDataAccessed: false, backendBitsMatchedInstalled: true, origin, isolatedDataRoot: dataRoot, fixtureProjectId: fixtureId, fixtureCanvasId: fixture.entryCanvasId,
    fixtureUnchanged: true, isolatedFiles, checks, screenshots, launcherMeasurements, packageHashes, browserErrors, remoteRequests, blockedWrites, fixtureWrites }, null, 2));
  process.stdout.write(JSON.stringify({ checks: checks.length, screenshots, packageFiles: packageHashes.length, browserErrors, remoteRequests, blockedWrites }, null, 2) + '\n');
} catch (error) {
  if (!page.isClosed()) await screenshot(page, 'package-failure.png').catch(() => {});
  const failure = { passed: false, message: error.message, stack: error.stack, checks, screenshots, launcherMeasurements, browserErrors, remoteRequests, blockedWrites, fixtureWrites };
  await writeFile(path.join(qaRoot, 'package-review.json'), JSON.stringify(failure, null, 2)); await writeFile(path.join(qaRoot, 'package-failure.json'), JSON.stringify(failure, null, 2)); throw error;
} finally { for (const current of contexts.reverse()) await current.close(); await browser.close(); }
