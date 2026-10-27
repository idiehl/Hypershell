import assert from 'node:assert/strict';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { createHash } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { mkdir, readFile, writeFile, readdir, lstat } from 'node:fs/promises';
import { chromium, expect } from '@playwright/test';
import { galleryItems } from '../../src/core/gallery.ts';
import { validateProject } from '../../src/core/rules.ts';
import { buildPublication, createPublicationPayload, readPublication } from '../../src/publication.ts';

// This program reviews the staged package against a separate copy of saved data.
// Its only HTTP write target is the isolated host launched by verify-package.ps1.
const config = JSON.parse(await readFile(process.argv[2], 'utf8'));
const { origin, qaRoot, dataRoot, packageRoot, sourceRoot } = config;
const workspaceRoot = path.resolve(import.meta.dirname, '../..');
const targetId = '005fe68a-bc30-4b11-af1d-4dadec799b1c';
assert.equal(workspaceRoot.toLowerCase(), path.resolve('I:/dev/Meridian').toLowerCase());
assert.equal(new URL(origin).origin, 'http://127.0.0.1:5293');
assert.equal(path.resolve(packageRoot).toLowerCase(), path.resolve(workspaceRoot, 'output/gallery-package-20261008').toLowerCase());
assert.equal(path.resolve(qaRoot).toLowerCase(), path.resolve(workspaceRoot, 'output/gallery-qa-20261008').toLowerCase());
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

async function json(route, method = 'GET', data, allow404 = false) {
  assert.ok(route.startsWith('/api/') || route === '/__meridian_status');
  const url = new URL(route, origin); assert.equal(url.origin, 'http://127.0.0.1:5293');
  const response = await fetch(url, { method, headers: data === undefined ? {} : { 'Content-Type': 'application/json' }, body: data === undefined ? undefined : JSON.stringify(data) });
  if (allow404 && response.status === 404) return null;
  assert.ok(response.ok, `${method} ${route}: ${response.status} ${await response.clone().text()}`); return response.json();
}
async function screenshot(page, name) { const filename = path.join(qaRoot, name); await page.screenshot({ path: filename }); screenshots.push(filename); }
const tile = (page, id) => page.locator(`.ms-node[data-node-id="${id}"]`);
const view = page => page.locator(`.ms-gallery[data-gallery-id="${targetId}"]`);
const inspector = page => page.locator('.inspector');
async function panel(page, name) {
  await expect(page.locator('.workspace')).toBeVisible();
  if (!(await inspector(page).isVisible())) await page.getByRole('button', { name: 'Show side panel', exact: true }).click();
  await page.getByRole('combobox', { name: 'Side panel', exact: true }).selectOption(name);
}
async function saved(page) { await expect(page.locator('.save-state')).toHaveText(/^Saved · r\d+$/); }
async function jump(page, id) {
  await panel(page, 'canvas'); const search = page.getByRole('searchbox', { name: 'Search canvas nodes', exact: true }); await search.fill(id);
  await page.locator(`button[data-jump-to-node="${id}"]`).click(); if (await search.isVisible()) await search.fill('');
}
async function object(page, item, index, total) {
  await expect(view(page).locator('.ms-gallery-item')).toHaveCount(1);
  await expect(view(page).locator('.ms-gallery-item')).toHaveAttribute('data-gallery-item-id', item.id);
  await expect(view(page).locator('.ms-gallery-item-title')).toHaveText(item.title);
  await expect(view(page).locator('.ms-gallery-position')).toHaveText(`${index} of ${total}`);
  await expect.poll(() => view(page).locator('img').evaluate(image => image.complete && image.naturalWidth > 0)).toBe(true);
}

assert.equal((await json('/__meridian_status')).application, 'atlas-meridian');
const catalog = await json('/api/projects');
let collection, originalSaved;
for (const entry of catalog) {
  const document = await json(`/api/projects/${encodeURIComponent(entry.id)}`);
  if (document.nodes.some(node => node.id === targetId)) { collection = entry; originalSaved = document; break; }
}
assert.ok(originalSaved, 'The copied collection must contain the supplied Zeus GUID.');
const recovery = await json(`/api/projects/${originalSaved.id}/recovery`, 'GET', undefined, true);
if (recovery) assert.equal(recovery.baseRevision, originalSaved.revision, 'A stale recovery requires review before composition.');
const base = recovery ? { ...recovery.project, revision: originalSaved.revision } : originalSaved;
assert.equal(base.id, originalSaved.id);
const original = base.nodes.find(node => node.id === targetId), originalImage = base.assets.find(asset => asset.id === original?.assetId);
assert.equal(original?.kind, 'image'); assert.ok(originalImage?.mime.startsWith('image/'));
const inputPath = path.join(qaRoot, 'package-zeus-current-base.json'), candidatePath = path.join(qaRoot, 'package-zeus-candidate.json');
await writeFile(inputPath, JSON.stringify(base, null, 2));
const composeOutput = execFileSync(process.execPath, ['--experimental-strip-types', path.join(workspaceRoot, 'scripts/gallery-cycle/compose-zeus-gallery.mjs'), '--input', inputPath, '--out', candidatePath], { cwd: workspaceRoot, encoding: 'utf8', windowsHide: true, maxBuffer: 1024 * 1024 });
await writeFile(path.join(qaRoot, 'package-composition.stdout.log'), composeOutput);
const candidate = JSON.parse(await readFile(candidatePath, 'utf8')), composition = JSON.parse(await readFile(`${candidatePath}.diff.json`, 'utf8'));
assert.deepEqual(validateProject(candidate), []); assert.equal(candidate.id, base.id); assert.equal(candidate.revision, originalSaved.revision);
const candidateParent = candidate.nodes.find(node => node.id === targetId), candidateItems = galleryItems(candidate, targetId);
assert.equal(candidateParent.containerVariant, 'gallery'); assert.equal(candidateItems.length, 5); assert.deepEqual(candidateItems.map(node => node.id), composition.childIds);
assert.equal(candidateItems[0].assetId, originalImage.id); assert.equal(candidateItems[0].title, original.title); assert.equal(candidateItems[0].text, original.text);
assert.equal(candidate.assets.find(asset => asset.id === originalImage.id).base64, originalImage.base64);
// A token and saved revision are rechecked immediately before the isolated write.
assert.deepEqual(await json(`/api/projects/${base.id}`), originalSaved);
assert.equal((await json(`/api/projects/${base.id}/recovery`, 'GET', undefined, true))?.savedAt ?? null, recovery?.savedAt ?? null);
const checkpointName = 'Before Zeus gallery · package review';
const applied = await json(`/api/projects/${base.id}/authoring`, 'POST', { project: candidate, expectedRevision: originalSaved.revision, expectedRecoverySavedAt: recovery?.savedAt ?? null, checkpointName });
assert.equal(applied.revision, originalSaved.revision + 1);
const checkpoint = (await json(`/api/projects/${base.id}/checkpoints`)).find(item => item.name === checkpointName + ' · saved'); assert.ok(checkpoint);
assert.deepEqual(await json(`/api/projects/${base.id}/checkpoints/${checkpoint.id}`), originalSaved);
if (recovery) {
  const draftCheckpoint = (await json(`/api/projects/${base.id}/checkpoints`)).find(item => item.name === checkpointName + ' · draft'); assert.ok(draftCheckpoint);
  assert.deepEqual(await json(`/api/projects/${base.id}/checkpoints/${draftCheckpoint.id}`), recovery.project);
}
record(`The copied r${originalSaved.revision} collection composes into a five-image Zeus gallery, retains the original parent GUID and exact first image, and checkpoints the complete saved baseline${recovery ? ' and current draft' : ''} before the isolated revision advances.`);

const browser = await chromium.launch(), contexts = [];
async function context(options = {}) {
  const current = await browser.newContext({ viewport: { width: 1700, height: 1080 }, ...options }); contexts.push(current);
  current.setDefaultTimeout(12000); current.setDefaultNavigationTimeout(18000);
  current.on('page', page => page.on('pageerror', error => browserErrors.push(error.message)));
  await current.route('**/*', async route => {
    const url = route.request().url();
    if (/^https?:/i.test(url) && new URL(url).origin !== origin) { remoteRequests.push(url); await route.abort(); } else await route.continue();
  }); return current;
}
const editorContext = await context(), page = await editorContext.newPage();
let finalProject;
try {
  await page.goto(`${origin}/?project=${applied.id}&canvas=${candidateParent.canvasId}`); await expect(page.locator('.workspace')).toBeVisible(); await jump(page, targetId); await saved(page);
  await expect(tile(page, targetId)).toHaveAttribute('aria-selected', 'true'); await expect(view(page)).toBeVisible();
  const stableBaseline = await json(`/api/projects/${applied.id}`), items = galleryItems(stableBaseline, targetId), parent = stableBaseline.nodes.find(node => node.id === targetId);
  for (const item of items) await expect(tile(page, item.id)).toHaveCount(0);
  for (const linkId of composition.removedLinkIds) await expect(page.locator(`.ms-link-line[data-link-id="${linkId}"]`)).toHaveCount(0);
  for (const link of stableBaseline.links.filter(link => items.some(item => item.id === link.sourceId || item.id === link.targetId))) await expect(page.locator(`.ms-link-line[data-link-id="${link.id}"]`)).toHaveCount(0);
  await page.getByRole('button', { name: 'Zoom in', exact: true }).click(); await page.getByRole('button', { name: 'Zoom in', exact: true }).click();
  await object(page, items[0], 1, items.length); await expect(view(page).getByRole('button', { name: `Previous object in ${parent.title}`, exact: true })).toBeDisabled(); await screenshot(page, 'package-zeus-first-object.png');
  for (let index = 1; index < items.length; index++) {
    await view(page).getByRole('button', { name: `Next object in ${parent.title}`, exact: true }).click(); await object(page, items[index], index + 1, items.length);
    await expect(view(page).locator('.ms-gallery-text')).toContainText(items[index].text);
    await expect(view(page).getByRole('link', { name: 'Museum catalog', exact: true })).toHaveAttribute('href', items[index].hyperlinks[0].url);
    if (index === 2) await screenshot(page, 'package-zeus-middle-object.png');
  }
  await expect(view(page).getByRole('button', { name: `Next object in ${parent.title}`, exact: true })).toBeDisabled(); await screenshot(page, 'package-zeus-last-object.png');
  await view(page).focus(); await page.keyboard.press('ArrowRight'); await object(page, items.at(-1), 5, 5); await page.keyboard.press('Home'); await object(page, items[0], 1, 5);
  assert.deepEqual(await json(`/api/projects/${applied.id}`), stableBaseline, 'Browsing must not save, reorder, or change the gallery document.');
  record('All five researched images render and page within the one existing Zeus tile. Previous/Next stop at the ends, scoped keyboard controls work, inactive children and their canvas lines remain absent, and paging leaves the saved graph unchanged.');

  const popupEvent = page.waitForEvent('popup'); await view(page).locator('img').click({ modifiers: ['Control'] }); const popup = await popupEvent;
  try {
    await expect(popup.getByRole('main', { name: 'Image viewer', exact: true })).toBeVisible();
    const bytes = await popup.locator('.media-window-stage img').evaluate(async image => { const data = new Uint8Array(await (await fetch(image.currentSrc)).arrayBuffer()); let binary = ''; for (const byte of data) binary += String.fromCharCode(byte); return btoa(binary); }); assert.equal(bytes, originalImage.base64);
    await screenshot(popup, 'package-zeus-dedicated-image-window.png');
  } finally { await popup.close(); }
  await jump(page, items[2].id); await object(page, items[2], 3, 5); await expect(tile(page, items[2].id)).toHaveCount(0);
  await view(page).getByRole('button', { name: `Inspect current object: ${items[2].title}`, exact: true }).click(); await expect(page.getByRole('combobox', { name: 'Side panel', exact: true })).toHaveValue('content');
  await expect(inspector(page).getByLabel('Title', { exact: true })).toHaveValue(items[2].title); await expect(inspector(page).getByLabel('Text body', { exact: true })).toHaveValue(items[2].text);
  record('The actual first image retains Ctrl-click media opening and exact attachment bytes; searching another child GUID displays that object inside the gallery, and Inspect opens its own caption and content.');

  await tile(page, targetId).locator('.ms-node-title').click(); await panel(page, 'relations'); const contents = page.locator(`[data-gallery-contents="${targetId}"]`);
  await expect(contents.locator('[data-gallery-object]')).toHaveCount(5); await expect(inspector(page).getByLabel('Child pattern', { exact: true })).toHaveCount(0);
  const beforeOrder = await json(`/api/projects/${applied.id}`); await contents.getByRole('button', { name: `Move ${items[1].title} earlier`, exact: true }).click();
  await expect.poll(async () => galleryItems(await json(`/api/projects/${applied.id}`), targetId).map(node => node.id)).toEqual([items[1].id, items[0].id, ...items.slice(2).map(node => node.id)]); await saved(page);
  await page.getByRole('button', { name: 'Undo', exact: true }).click(); await expect.poll(async () => galleryItems(await json(`/api/projects/${applied.id}`), targetId).map(node => node.id)).toEqual(items.map(node => node.id)); await saved(page);
  const afterUndo = await json(`/api/projects/${applied.id}`); assert.deepEqual(afterUndo.nodes, beforeOrder.nodes); assert.deepEqual(afterUndo.links, beforeOrder.links); assert.deepEqual(afterUndo.assets, beforeOrder.assets);
  await page.reload(); await jump(page, targetId); await saved(page); await object(page, items[0], 1, 5); await tile(page, targetId).locator('.ms-node-title').click(); await panel(page, 'relations');
  assert.deepEqual(await page.locator(`[data-gallery-contents="${targetId}"] [data-gallery-object]`).evaluateAll(rows => rows.map(row => row.dataset.galleryObject)), items.map(node => node.id)); await screenshot(page, 'package-zeus-relations.png');
  record('Relations lists the five real image child nodes and omits grid layout tools. Reordering automatically saves, Undo restores the same order without changing node geometry or bytes, and reopening retains the authored collection while browsing resets to its first object.');

  finalProject = await json(`/api/projects/${applied.id}`);
  for (let index = 0; index < finalProject.canvases.length; index++) {
    const canvas = finalProject.canvases[index]; await page.goto(`${origin}/?project=${finalProject.id}&canvas=${canvas.id}`); await expect(page.locator('.workspace')).toBeVisible(); await page.getByRole('button', { name: 'Fit canvas', exact: true }).click(); await saved(page); await screenshot(page, `package-canvas-${index + 1}.png`);
  }
  finalProject = await json(`/api/projects/${applied.id}`); assert.deepEqual(finalProject.canvases, base.canvases); assert.equal(finalProject.nodes.filter(node => node.parentId === targetId).length, 5);
  const payload = createPublicationPayload(finalProject); assert.deepEqual(payload.missing, []);
  const html = buildPublication(finalProject, await readFile(path.join(packageRoot, 'app/wwwroot/viewer.js'), 'utf8'), await readFile(path.join(packageRoot, 'app/wwwroot/scene.css'), 'utf8'));
  const publicationPath = path.join(qaRoot, 'package-zeus-gallery.html'); await writeFile(publicationPath, html);
  const imported = readPublication(html), importedParent = imported.nodes.find(node => node.title === parent.title); assert.equal(importedParent.containerVariant, 'gallery'); assert.equal(galleryItems(imported, importedParent).length, 5); assert.equal(imported.assets.find(asset => asset.name === originalImage.name).base64, originalImage.base64);
  const offlineContext = await context({ offline: true }), offline = await offlineContext.newPage(); await offline.goto(pathToFileURL(publicationPath).href); await expect(offline.locator('.ms-viewport')).toBeVisible();
  const authoredPayload = await offline.locator('#meridian-data').textContent(); await offline.getByRole('combobox', { name: 'Choose canvas', exact: true }).selectOption(parent.canvasId);
  await offline.getByRole('button', { name: 'Explore project', exact: true }).click(); await offline.getByRole('searchbox', { name: 'Search project', exact: true }).fill(targetId);
  await offline.getByRole('button', { name: `Go to ${parent.title} in ${finalProject.canvases.find(canvas => canvas.id === parent.canvasId).title}`, exact: true }).click(); await offline.getByRole('button', { name: 'Close project navigation', exact: true }).click();
  await object(offline, items[0], 1, 5); await screenshot(offline, 'package-gallery-offline-first.png');
  for (let index = 1; index < items.length; index++) { await view(offline).getByRole('button', { name: `Next object in ${parent.title}`, exact: true }).click(); await object(offline, items[index], index + 1, 5); }
  await screenshot(offline, 'package-gallery-offline-last.png'); assert.equal(await offline.locator('#meridian-data').textContent(), authoredPayload);
  for (const item of items) await expect(tile(offline, item.id)).toHaveCount(0);
  record('The packaged standalone viewer displays the same five actual images offline, preserves their museum links and exact original bytes, and leaves the publication payload immutable. Every existing canvas has a reviewed package screenshot.');

  assert.deepEqual(await fileInventory(sourceRoot, true), protectedSourceFiles, 'Live saved files must remain unchanged throughout copied-data review.'); assert.deepEqual(browserErrors, []); assert.deepEqual(remoteRequests, []);
  const packageHashes = (await fileInventory(path.join(packageRoot, 'app'))).map(({ path, sha256 }) => ({ path, sha256 }));
  await writeFile(path.join(qaRoot, 'package-review.json'), JSON.stringify({ passed: true, origin, isolatedDataRoot: dataRoot, copiedSourceFiles: config.copiedFiles,
    sourceCollection: { id: collection.id, title: collection.title, revision: originalSaved.revision, nodeId: targetId, imageSha256: digest(Buffer.from(originalImage.base64, 'base64')) },
    galleryProjectId: applied.id, galleryCanvasId: parent.canvasId, galleryNodeId: targetId, galleryChildIds: items.map(node => node.id), galleryObjects: items.length, checkpointId: checkpoint.id, composition,
    savedDataUnchanged: true, protectedSourceFiles, checks, screenshots, packageHashes, browserErrors, remoteRequests }, null, 2));
  process.stdout.write(JSON.stringify({ checks: checks.length, screenshots, packageFiles: packageHashes.length, browserErrors }, null, 2) + '\n');
} catch (error) {
  if (!page.isClosed()) await screenshot(page, 'package-failure.png').catch(() => {});
  const failure = { passed: false, message: error.message, stack: error.stack, checks, screenshots, browserErrors, remoteRequests };
  await writeFile(path.join(qaRoot, 'package-review.json'), JSON.stringify(failure, null, 2)); await writeFile(path.join(qaRoot, 'package-failure.json'), JSON.stringify(failure, null, 2)); throw error;
} finally { for (const current of contexts.reverse()) await current.close(); await browser.close(); }
