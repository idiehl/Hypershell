import assert from 'node:assert/strict';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { createHash, randomUUID } from 'node:crypto';
import { mkdir, readFile, writeFile, readdir, lstat } from 'node:fs/promises';
import { chromium, expect } from '@playwright/test';
import { addNode, commitSnapshots, newProject } from '../../src/core/model.ts';
import { galleryItems } from '../../src/core/gallery.ts';
import { validateProject } from '../../src/core/rules.ts';
import { buildPublication, createPublicationPayload, readPublication } from '../../src/publication.ts';

// All authored writes are confined to a fresh fixture on the specifically owned
// copied-data host. Live saved canvases and their recovery files are read-only.
const config = JSON.parse(await readFile(process.argv[2], 'utf8'));
const { origin, qaRoot, dataRoot, packageRoot, sourceRoot } = config;
const workspaceRoot = path.resolve(import.meta.dirname, '../..');
assert.equal(workspaceRoot.toLowerCase(), path.resolve('I:/dev/Meridian').toLowerCase());
assert.equal(new URL(origin).origin, 'http://127.0.0.1:5293');
assert.equal(path.resolve(packageRoot).toLowerCase(), path.resolve(workspaceRoot, 'output/background-package-20261008').toLowerCase());
assert.equal(path.resolve(qaRoot).toLowerCase(), path.resolve(workspaceRoot, 'output/background-qa-20261008').toLowerCase());
assert.ok(path.resolve(dataRoot).toLowerCase().startsWith(path.resolve(workspaceRoot, '.test-data').toLowerCase() + path.sep));
assert.notEqual(path.resolve(dataRoot).toLowerCase(), path.resolve(sourceRoot).toLowerCase());
assert.ok(config.ownedHostId > 0, 'A specifically owned isolated host is required.');
await mkdir(qaRoot, { recursive: true });
const digest = bytes => createHash('sha256').update(bytes).digest('hex');
const checks = [], screenshots = [], browserErrors = [], remoteRequests = [];
const record = text => { checks.push(text); process.stdout.write(text + '\n'); };

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
  assert.equal((await lstat(absoluteRoot)).isSymbolicLink(), false); await walk(absoluteRoot); return rows.sort((a, b) => a.relativePath.localeCompare(b.relativePath));
}
const protectedSourceFiles = await fileInventory(sourceRoot, true);
assert.equal(protectedSourceFiles.length, config.copiedFiles);
for (const file of protectedSourceFiles) assert.equal(digest(await readFile(path.join(dataRoot, file.relativePath))), file.sha256, `The copied file differs from the saved original: ${file.relativePath}`);

const fixtureId = randomUUID();
async function json(route, method = 'GET', data, allow404 = false) {
  assert.ok(route.startsWith('/api/') || route === '/__meridian_status');
  // Copying the collection is not authorization to edit it during verification.
  if (!['GET', 'HEAD'].includes(method)) assert.ok(route === '/api/projects' && method === 'POST' || route.startsWith(`/api/projects/${fixtureId}`), 'Only the fresh review fixture may be written.');
  const url = new URL(route, origin); assert.equal(url.origin, 'http://127.0.0.1:5293');
  const response = await fetch(url, { method, headers: data === undefined ? {} : { 'Content-Type': 'application/json' }, body: data === undefined ? undefined : JSON.stringify(data) });
  if (allow404 && response.status === 404) return null;
  assert.ok(response.ok, `${method} ${route}: ${response.status} ${await response.clone().text()}`); return response.json();
}
async function screenshot(page, name) { const filename = path.join(qaRoot, name); await page.screenshot({ path: filename }); screenshots.push(filename); }
const tile = (page, id) => page.locator(`.ms-node[data-node-id="${id}"]`);
const inspector = page => page.locator('.inspector');
async function panel(page, name) {
  await expect(page.locator('.workspace')).toBeVisible();
  if (!(await inspector(page).isVisible())) await page.getByRole('button', { name: 'Show side panel', exact: true }).click();
  await page.getByRole('combobox', { name: 'Side panel', exact: true }).selectOption(name);
}
async function appearance(page, id) {
  if (id) {
    await panel(page, 'canvas'); const search = page.getByRole('searchbox', { name: 'Search canvas nodes', exact: true }); await search.fill(id);
    await page.locator(`button[data-jump-to-node="${id}"]`).click(); if (await search.isVisible()) await search.fill('');
  } else await page.keyboard.press('Escape');
  await panel(page, 'appearance');
  const details = inspector(page).locator('details.appearance-panel');
  if (!(await details.evaluate(element => element.open))) await details.locator('summary').click();
  await expect(inspector(page).getByLabel('Background preset', { exact: true })).toBeVisible();
}
async function saved(page) { await expect(page.locator('.save-state')).toHaveText(/^Saved · r\d+$/); }
async function stored() { return json(`/api/projects/${fixtureId}`); }
async function styleSaved(page, entityId, fields) {
  await expect.poll(async () => {
    const document = await stored(), style = entityId ? document.nodes.find(node => node.id === entityId)?.style : document.canvases[0].style;
    return Object.fromEntries(Object.keys(fields).map(key => [key, style?.[key]]));
  }).toEqual(fields); await saved(page);
}
async function backgroundCSS(layer) {
  return layer.evaluate(element => { const style = getComputedStyle(element); return { image: style.backgroundImage, size: style.backgroundSize, position: style.backgroundPosition, opacity: style.opacity, clip: style.clipPath, transform: style.transform }; });
}

assert.equal((await json('/__meridian_status')).application, 'atlas-meridian');
const copiedCatalog = await json('/api/projects'), copiedProjects = [];
for (const entry of copiedCatalog) copiedProjects.push({ entry, document: await json(`/api/projects/${encodeURIComponent(entry.id)}`), recovery: await json(`/api/projects/${encodeURIComponent(entry.id)}/recovery`, 'GET', undefined, true) });
const zeusProject = copiedProjects.find(row => row.document.nodes.some(node => node.id === '005fe68a-bc30-4b11-af1d-4dadec799b1c'));
assert.ok(zeusProject, 'The copied collection contains the requested Zeus node.');
const zeus = zeusProject.document.nodes.find(node => node.id === '005fe68a-bc30-4b11-af1d-4dadec799b1c');
assert.equal(zeus.containerVariant, 'gallery'); assert.equal(galleryItems(zeusProject.document, zeus).length, 5);
record('The existing five-object Zeus gallery, all saved collection documents and recovery baselines are recorded before a separate background review fixture is created.');

const fixture = newProject('Background collection · isolated package review'); fixture.id = fixtureId;
const canvas = fixture.canvases[0]; canvas.title = 'Canvas and node background collection';
const patterned = addNode(fixture, canvas.id, 'text', 'Patterned notes');
Object.assign(patterned, { x: 80, y: 90, width: 390, height: 280, text: 'A quiet paper texture sits behind editable content. Image backgrounds stay independent of the node’s content and identity.', style: { shape: 'rounded', fill: '#18342f', textColor: '#edf8ef', fontSize: 16, titleSize: 25, backgroundPattern: 'linen', backgroundScale: 32, backgroundTint: '#749c88', backgroundOpacity: 0.35 } });
const shaped = addNode(fixture, canvas.id, 'text', 'Clipped hexagon');
Object.assign(shaped, { x: 640, y: 90, width: 360, height: 300, text: 'Background imagery follows this hexagon’s backdrop. Selecting the node still outlines its actual edges.', style: { shape: 'hexagon', fill: '#1d2b48', fontSize: 16, titleSize: 24, textAlign: 'center', backgroundPattern: 'stars', backgroundScale: 128, backgroundTint: '#b8cbee', backgroundOpacity: 0.5 } });
const unframed = addNode(fixture, canvas.id, 'text', 'Floating content');
Object.assign(unframed, { x: 80, y: 480, width: 380, height: 110, text: 'No backdrop remains available.', style: { shape: 'none', titleSize: 24, fontSize: 15, backgroundPattern: 'aurora', backgroundScale: 128 } });
assert.deepEqual(validateProject(fixture), []);
const created = await json('/api/projects', 'POST', commitSnapshots(fixture)); assert.equal(created.id, fixtureId);

const browser = await chromium.launch(), contexts = [];
async function context(options = {}) {
  const current = await browser.newContext({ viewport: { width: 1600, height: 1000 }, ...options }); contexts.push(current);
  current.setDefaultTimeout(12000); current.setDefaultNavigationTimeout(18000);
  current.on('page', page => page.on('pageerror', error => browserErrors.push(error.message)));
  await current.route('**/*', async route => {
    const request = route.request(), url = request.url();
    if (/^https?:/i.test(url) && new URL(url).origin !== origin) { remoteRequests.push(url); await route.abort(); }
    else if (/^https?:/i.test(url) && !['GET', 'HEAD'].includes(request.method()) && !new URL(url).pathname.startsWith(`/api/projects/${fixtureId}`)) { throw new Error(`A browser attempted to write an existing collection: ${request.method()} ${url}`); }
    else await route.continue();
  }); return current;
}
const editorContext = await context(), page = await editorContext.newPage();
let finalProject;
try {
  // A local, deterministic raster fixture proves uploads preserve actual bytes.
  const rasterPage = await editorContext.newPage();
  await rasterPage.setContent('<style>html,body{margin:0}</style><svg xmlns="http://www.w3.org/2000/svg" width="128" height="128"><rect width="128" height="128" fill="#203950"/><path d="M0 64L64 0L128 64L64 128Z" fill="#91c9ae"/><circle cx="64" cy="64" r="22" fill="#e4c680"/></svg>');
  const rasterPath = path.join(qaRoot, 'background-upload-fixture.png'); await rasterPage.locator('svg').screenshot({ path: rasterPath }); await rasterPage.close();
  const rasterBytes = await readFile(rasterPath), rasterBase64 = rasterBytes.toString('base64');
  await page.goto(`${origin}/?project=${fixtureId}&canvas=${canvas.id}`); await expect(page.locator('.workspace')).toBeVisible(); await saved(page);
  await expect(tile(page, patterned.id).locator('.ms-node-background')).toBeVisible(); await expect(tile(page, shaped.id).locator('.ms-node-background')).toBeVisible();
  assert.notEqual((await backgroundCSS(tile(page, shaped.id).locator('.ms-node-background'))).image, 'none');
  await expect(tile(page, unframed.id).locator('.ms-node-background')).toBeHidden();
  await screenshot(page, 'package-initial-backgrounds.png');

  await appearance(page, null);
  const presets = page.getByLabel('Background preset', { exact: true });
  const expectedPresets = ['plain', 'dots-fine', 'dots-default', 'dots-medium', 'dots-wide', 'grid-fine', 'grid-medium', 'grid-wide', 'grid-large', 'isometric', 'linen', 'contours', 'aurora', 'stars'];
  assert.deepEqual(await presets.locator('option').evaluateAll(options => options.map(option => option.value)), expectedPresets);
  for (const preset of ['grid-fine', 'grid-medium', 'grid-wide', 'grid-large', 'isometric', 'contours', 'aurora', 'stars']) {
    await presets.selectOption(preset);
    const [pattern, scale] = ({ 'grid-fine': ['grid', 16], 'grid-medium': ['grid', 32], 'grid-wide': ['grid', 64], 'grid-large': ['grid', 128], isometric: ['isometric', 48], contours: ['contours', 160], aurora: ['aurora', 256], stars: ['stars', 128] })[preset];
    await styleSaved(page, null, { backgroundPattern: pattern, backgroundScale: scale });
    const document = await stored(), rendered = await backgroundCSS(page.locator('.ms-canvas-background'));
    assert.notEqual(rendered.image, 'none'); assert.equal(document.canvases[0].style.backgroundPattern, pattern);
    await expect(page.locator('.ms-canvas-background')).toHaveAttribute('data-background-pattern', pattern);
    if (['grid-fine', 'contours', 'aurora'].includes(preset)) await screenshot(page, `package-canvas-${preset}.png`);
  }
  await presets.selectOption('grid-medium'); await styleSaved(page, null, { backgroundPattern: 'grid', backgroundScale: 32 });
  const scale = (await stored()).canvases[0].style.backgroundScale, beforeZoom = await backgroundCSS(page.locator('.ms-canvas-background'));
  await page.getByRole('button', { name: 'Zoom in', exact: true }).click();
  const afterZoom = await backgroundCSS(page.locator('.ms-canvas-background'));
  assert.ok(beforeZoom.size !== afterZoom.size || beforeZoom.transform !== afterZoom.transform, 'A world background follows canvas zoom.');
  assert.equal((await stored()).canvases[0].style.backgroundScale, scale, 'Camera changes must not rewrite authored scale.');
  record('All fourteen built-in background choices are available, including four line-grid scales and four dot-grid scales. Decorative patterns render in the packaged canvas, and grid scale follows the camera without editing the document.');

  await page.getByLabel('Upload background image', { exact: true }).setInputFiles(rasterPath);
  await expect.poll(async () => (await stored()).canvases[0].style?.backgroundAssetId).toBeTruthy(); await saved(page);
  let document = await stored(), canvasAssetId = document.canvases[0].style.backgroundAssetId;
  assert.equal(document.assets.find(asset => asset.id === canvasAssetId)?.base64, rasterBase64);
  await page.getByLabel('Background image fit', { exact: true }).selectOption('tile'); await styleSaved(page, null, { backgroundFit: 'tile' });
  await page.getByLabel('Background size', { exact: true }).fill('96'); await page.getByLabel('Background size', { exact: true }).press('Tab'); await styleSaved(page, null, { backgroundScale: 96 });
  await page.getByLabel('Background opacity', { exact: true }).fill('0.65'); await page.getByLabel('Background opacity', { exact: true }).press('Tab'); await styleSaved(page, null, { backgroundOpacity: 0.65 });
  assert.ok((await backgroundCSS(page.locator('.ms-canvas-background'))).image.includes('data:image/png;base64,'));
  await screenshot(page, 'package-canvas-uploaded-image.png');

  await appearance(page, shaped.id);
  assert.deepEqual(await page.getByLabel('Background preset', { exact: true }).locator('option').evaluateAll(options => options.map(option => option.value)), expectedPresets, 'Nodes offer the same background collection as canvases.');
  await page.getByLabel('Background preset', { exact: true }).selectOption('grid-wide'); await styleSaved(page, shaped.id, { backgroundPattern: 'grid', backgroundScale: 64 });
  await expect(tile(page, shaped.id).locator('.ms-node-background')).toHaveAttribute('data-background-pattern', 'grid');
  await page.getByLabel('Background preset', { exact: true }).selectOption('aurora'); await styleSaved(page, shaped.id, { backgroundPattern: 'aurora', backgroundScale: 256 });
  await page.getByLabel('Upload background image', { exact: true }).setInputFiles(rasterPath); await expect.poll(async () => (await stored()).nodes.find(node => node.id === shaped.id)?.style?.backgroundAssetId).toBeTruthy(); await saved(page);
  document = await stored(); const nodeAssetId = document.nodes.find(node => node.id === shaped.id).style.backgroundAssetId;
  assert.equal(document.assets.find(asset => asset.id === nodeAssetId)?.base64, rasterBase64);
  await page.getByLabel('Background image fit', { exact: true }).selectOption('cover'); await styleSaved(page, shaped.id, { backgroundFit: 'cover' });
  await page.getByLabel('Background opacity', { exact: true }).fill('0.3'); await page.getByLabel('Background opacity', { exact: true }).press('Tab'); await styleSaved(page, shaped.id, { backgroundOpacity: 0.3 });
  const nodeLayer = await backgroundCSS(tile(page, shaped.id).locator('.ms-node-background'));
  assert.ok(nodeLayer.image.includes('data:image/png;base64,'));
  assert.ok(nodeLayer.clip !== 'none' || await tile(page, shaped.id).evaluate(element => getComputedStyle(element).clipPath !== 'none' || getComputedStyle(element).getPropertyValue('--ms-shape-clip').trim() !== ''), 'The image uses the same hexagon backdrop boundary.');
  await expect(tile(page, shaped.id)).toHaveAttribute('aria-selected', 'true'); await expect(tile(page, shaped.id).locator('.ms-node-title')).toHaveText(shaped.title);
  await screenshot(page, 'package-node-uploaded-image-clipped.png');
  record('Canvas and hexagon node custom PNG uploads preserve exact original bytes as project assets. Tile size, fit and opacity autosave; the node keeps its title, content, shape and selected edge outline.');

  await page.getByRole('button', { name: 'Remove background image', exact: true }).click();
  await expect.poll(async () => Boolean((await stored()).nodes.find(node => node.id === shaped.id)?.style?.backgroundAssetId)).toBe(false); await saved(page);
  await page.getByRole('button', { name: 'Undo', exact: true }).click(); await styleSaved(page, shaped.id, { backgroundAssetId: nodeAssetId });
  await page.reload(); await appearance(page, shaped.id); await saved(page);
  await expect(page.getByLabel('Background image fit', { exact: true })).toHaveValue('cover'); await expect(page.getByLabel('Background opacity', { exact: true })).toHaveValue('0.3');
  assert.ok((await backgroundCSS(tile(page, shaped.id).locator('.ms-node-background'))).image.includes(rasterBase64));
  finalProject = await stored(); assert.equal(finalProject.canvases[0].style.backgroundAssetId, canvasAssetId); assert.equal(finalProject.nodes.find(node => node.id === shaped.id).style.backgroundAssetId, nodeAssetId);
  record('Removing a node background is undoable and leaves the content intact. Reopening restores both uploaded backgrounds and their authored image-fit/opacity settings without external requests.');

  await page.setViewportSize({ width: 1100, height: 720 });
  await page.getByLabel('Background opacity', { exact: true }).scrollIntoViewIfNeeded();
  await expect(page.getByLabel('Background opacity', { exact: true })).toBeInViewport(); await screenshot(page, 'package-background-controls-small-height.png');
  await page.setViewportSize({ width: 1600, height: 1000 });
  const payload = createPublicationPayload(finalProject); assert.deepEqual(payload.missing, []);
  const html = buildPublication(finalProject, await readFile(path.join(packageRoot, 'app/wwwroot/viewer.js'), 'utf8'), await readFile(path.join(packageRoot, 'app/wwwroot/scene.css'), 'utf8'));
  const publicationPath = path.join(qaRoot, 'package-backgrounds-offline.html'); await writeFile(publicationPath, html);
  const imported = readPublication(html), importedNode = imported.nodes.find(node => node.title === shaped.title);
  assert.equal(imported.canvases[0].style.backgroundFit, 'tile'); assert.equal(importedNode.style.backgroundFit, 'cover');
  assert.equal(imported.assets.find(asset => asset.id === imported.canvases[0].style.backgroundAssetId).base64, rasterBase64); assert.equal(imported.assets.find(asset => asset.id === importedNode.style.backgroundAssetId).base64, rasterBase64);
  const offlineContext = await context({ offline: true }), offline = await offlineContext.newPage(); await offline.goto(pathToFileURL(publicationPath).href); await expect(offline.locator('.ms-viewport')).toBeVisible();
  const authoredPayload = await offline.locator('#meridian-data').textContent();
  assert.ok((await backgroundCSS(offline.locator('.ms-canvas-background'))).image.includes(rasterBase64)); assert.ok((await backgroundCSS(tile(offline, shaped.id).locator('.ms-node-background'))).image.includes(rasterBase64));
  await offline.getByRole('button', { name: 'Zoom in', exact: true }).click(); assert.equal(await offline.locator('#meridian-data').textContent(), authoredPayload); await screenshot(offline, 'package-backgrounds-offline.png');
  record('Offline HTML uses the same packaged scene renderer and embedded PNG bytes for canvas and node backgrounds. Import retains both asset-backed styles, and viewer camera changes leave its authored payload immutable.');

  for (const copied of copiedProjects) {
    assert.deepEqual(await json(`/api/projects/${copied.entry.id}`), copied.document, 'An existing copied canvas changed during background QA.');
    assert.deepEqual(await json(`/api/projects/${copied.entry.id}/recovery`, 'GET', undefined, true), copied.recovery, 'An existing copied recovery changed during background QA.');
  }
  assert.deepEqual(await fileInventory(sourceRoot, true), protectedSourceFiles, 'Live saved files must remain unchanged throughout copied-data review.'); assert.deepEqual(browserErrors, []); assert.deepEqual(remoteRequests, []);
  const packageHashes = (await fileInventory(path.join(packageRoot, 'app'))).map(({ path, sha256 }) => ({ path, sha256 }));
  await writeFile(path.join(qaRoot, 'package-review.json'), JSON.stringify({ passed: true, origin, isolatedDataRoot: dataRoot, copiedSourceFiles: config.copiedFiles, fixtureProjectId: fixtureId, fixtureCanvasId: canvas.id, fixtureNodeId: shaped.id,
    sourceCollection: { id: zeusProject.document.id, revision: zeusProject.document.revision, nodeId: zeus.id, galleryObjects: 5 }, customImageSha256: digest(rasterBytes), canvasAssetId, nodeAssetId,
    savedDataUnchanged: true, protectedSourceFiles, checks, screenshots, packageHashes, browserErrors, remoteRequests }, null, 2));
  process.stdout.write(JSON.stringify({ checks: checks.length, screenshots, packageFiles: packageHashes.length, browserErrors }, null, 2) + '\n');
} catch (error) {
  if (!page.isClosed()) await screenshot(page, 'package-failure.png').catch(() => {});
  const failure = { passed: false, message: error.message, stack: error.stack, checks, screenshots, browserErrors, remoteRequests };
  await writeFile(path.join(qaRoot, 'package-review.json'), JSON.stringify(failure, null, 2)); await writeFile(path.join(qaRoot, 'package-failure.json'), JSON.stringify(failure, null, 2)); throw error;
} finally { for (const current of contexts.reverse()) await current.close(); await browser.close(); }

