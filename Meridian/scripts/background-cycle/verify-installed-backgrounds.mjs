import assert from 'node:assert/strict';
import path from 'node:path';
import { createHash } from 'node:crypto';
import { readFile, writeFile } from 'node:fs/promises';
import { chromium, expect } from '@playwright/test';

// This final installed-app check performs no authored or persistence writes.
// A new browser context does not share the user's native WebView session.
const workspaceRoot = path.resolve(import.meta.dirname, '../..');
assert.equal(workspaceRoot.toLowerCase(), path.resolve('I:/dev/Meridian').toLowerCase());
const origin = 'http://127.0.0.1:5240', qaRoot = path.join(workspaceRoot, 'output/background-qa-20261008');
const reviewed = JSON.parse(await readFile(path.join(qaRoot, 'package-review.json'), 'utf8'));
const installed = JSON.parse(await readFile(path.join(qaRoot, 'package-install.json'), 'utf8'));
assert.equal(reviewed.passed, true); assert.equal(installed.windowsClosed, false); assert.equal(installed.savedContentUnchangedAfterStartup, true);
const hash = bytes => createHash('sha256').update(bytes).digest('hex');
for (const file of reviewed.packageHashes) {
  const relative = path.relative(path.join(workspaceRoot, 'output/background-package-20261008/app'), file.path);
  assert.ok(relative && !relative.startsWith('..') && !path.isAbsolute(relative));
  assert.equal(hash(await readFile(path.join(installed.destination, relative))), file.sha256, `Installed bytes differ from the reviewed package: ${relative}`);
}
async function json(route, allow404 = false) {
  assert.ok(route.startsWith('/api/') || route === '/__meridian_status');
  const response = await fetch(new URL(route, origin));
  if (allow404 && response.status === 404) return null;
  assert.ok(response.ok, `${route}: ${response.status}`); return response.json();
}
assert.equal((await json('/__meridian_status')).application, 'atlas-meridian');
const documents = [];
for (const entry of await json('/api/projects')) documents.push({ entry, document: await json(`/api/projects/${entry.id}`), recovery: await json(`/api/projects/${entry.id}/recovery`, true) });
const zeusId = '005fe68a-bc30-4b11-af1d-4dadec799b1c', collection = documents.find(row => row.document.nodes.some(node => node.id === zeusId));
assert.ok(collection); const zeus = collection.document.nodes.find(node => node.id === zeusId); assert.equal(zeus.containerVariant, 'gallery');
const browser = await chromium.launch(), context = await browser.newContext({ viewport: { width: 1600, height: 1000 } }), page = await context.newPage();
const browserErrors = [], blockedWrites = [], remoteRequests = [], screenshots = [];
page.on('pageerror', error => browserErrors.push(error.message));
await context.route('**/*', async route => {
  const request = route.request(), url = request.url();
  if (!['GET', 'HEAD'].includes(request.method())) { blockedWrites.push(`${request.method()} ${url}`); await route.abort(); }
  else if (/^https?:/i.test(url) && new URL(url).origin !== origin) { remoteRequests.push(url); await route.abort(); }
  else await route.continue();
});
async function appearance() {
  const inspector = page.locator('.inspector');
  if (!(await inspector.isVisible())) await page.getByRole('button', { name: 'Show side panel', exact: true }).click();
  await page.getByRole('combobox', { name: 'Side panel', exact: true }).selectOption('appearance');
  const details = inspector.locator('details.appearance-panel'); if (!(await details.evaluate(element => element.open))) await details.locator('summary').click();
  const choices = await page.getByLabel('Background preset', { exact: true }).locator('option').evaluateAll(options => options.map(option => option.value));
  assert.deepEqual(choices, ['plain', 'dots-fine', 'dots-default', 'dots-medium', 'dots-wide', 'grid-fine', 'grid-medium', 'grid-wide', 'grid-large', 'isometric', 'linen', 'contours', 'aurora', 'stars']);
  await expect(page.getByLabel('Upload background image', { exact: true })).toHaveAttribute('type', 'file');
  await expect(page.locator('.background-preview')).toBeVisible();
}
try {
  await page.goto(origin); await expect(page.locator('.workspace')).toBeVisible(); await appearance();
  const canvasShot = path.join(qaRoot, 'installed-library-background-controls.png'); await page.screenshot({ path: canvasShot }); screenshots.push(canvasShot);
  await page.goto(`${origin}/?project=${collection.document.id}&canvas=${zeus.canvasId}`); await expect(page.locator('.workspace')).toBeVisible();
  await page.getByRole('combobox', { name: 'Side panel', exact: true }).selectOption('canvas'); await page.getByRole('searchbox', { name: 'Search canvas nodes', exact: true }).fill(zeusId);
  await page.locator(`button[data-jump-to-node="${zeusId}"]`).click(); await appearance();
  await expect(page.locator(`.ms-gallery[data-gallery-id="${zeusId}"] .ms-gallery-position`)).toHaveText('1 of 5');
  const nodeShot = path.join(qaRoot, 'installed-zeus-background-controls.png'); await page.screenshot({ path: nodeShot }); screenshots.push(nodeShot);
  for (const row of documents) { assert.deepEqual(await json(`/api/projects/${row.entry.id}`), row.document); assert.deepEqual(await json(`/api/projects/${row.entry.id}/recovery`, true), row.recovery); }
  assert.deepEqual(browserErrors, []); assert.deepEqual(blockedWrites, []); assert.deepEqual(remoteRequests, []);
  const report = { passed: true, origin, processId: installed.processId, verifiedPackageFiles: reviewed.packageHashes.length, backgroundOptions: 14, libraryCanvasControls: true, zeusNodeControls: true, existingGalleryObjects: 5, savedProjectsUnchanged: true, recoveriesUnchanged: true, browserErrors, blockedWrites, remoteRequests, screenshots };
  await writeFile(path.join(qaRoot, 'installed-review.json'), JSON.stringify(report, null, 2)); process.stdout.write(JSON.stringify(report, null, 2) + '\n');
} finally { await context.close(); await browser.close(); }
