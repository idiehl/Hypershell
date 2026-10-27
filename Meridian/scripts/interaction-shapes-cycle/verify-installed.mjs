import assert from 'node:assert/strict';
import path from 'node:path';
import { createHash } from 'node:crypto';
import { readFile, writeFile } from 'node:fs/promises';
import { chromium, expect } from '@playwright/test';
import { navigationIndex } from '../../src/core/navigation.ts';
import { boundedChildInterior } from '../../src/core/sizing.ts';

// Read-only check of the installed package. No original project is edited,
// disclosed, migrated or written by this isolated browser context.
const workspace = path.resolve(import.meta.dirname, '../..');
assert.equal(workspace.toLowerCase(), path.resolve('I:/dev/Meridian').toLowerCase());
const qaRoot = path.join(workspace, 'output/interaction-shapes-qa-20261009');
const review = JSON.parse(await readFile(path.join(qaRoot, 'package-review.json'), 'utf8'));
const installed = JSON.parse(await readFile(path.join(qaRoot, 'package-install.json'), 'utf8'));
assert.equal(review.passed, true); assert.equal(installed.savedContentUnchangedAfterStartup, true);
assert.equal(installed.nativeWindowsUnchanged, true); assert.equal(installed.windowsClosed, false);
const origin = 'http://127.0.0.1:5240', hash = bytes => createHash('sha256').update(bytes).digest('hex');
for (const file of review.packageHashes) {
  const relative = path.relative(path.join(workspace, 'output/interaction-shapes-package-20261009/app'), file.path);
  assert.ok(relative && !relative.startsWith('..') && !path.isAbsolute(relative));
  assert.equal(hash(await readFile(path.join(installed.destination, relative))), file.sha256, `Installed package bytes: ${relative}`);
}
for (const filename of ['editor.js', 'viewer.js', 'app.css', 'scene.css', 'index.html']) {
  const response = await fetch(new URL(`/${filename}`, origin)); assert.ok(response.ok);
  assert.equal(hash(Buffer.from(await response.arrayBuffer())), hash(await readFile(path.join(installed.destination, 'wwwroot', filename))), `Served package bytes: ${filename}`);
}
async function json(route, allow404 = false) {
  assert.ok(route.startsWith('/api/') || route === '/__meridian_status');
  const response = await fetch(new URL(route, origin));
  if (allow404 && response.status === 404) return null;
  assert.ok(response.ok, `${route}: ${response.status}`); return response.json();
}
assert.equal((await json('/__meridian_status')).application, 'atlas-meridian');
const catalog = await json('/api/projects'), library = await json('/api/library'), libraryRecovery = await json('/api/library/recovery', true);
const documents = [];
for (const entry of catalog) documents.push({ id: entry.id, document: await json(`/api/projects/${entry.id}`), recovery: await json(`/api/projects/${entry.id}/recovery`, true) });
const index = navigationIndex(library.project), visibleNodes = index.outline(library.project.entryCanvasId, true);
const groupId = (visibleNodes.find(row => row.node.kind === 'container') ?? visibleNodes[0])?.node.id;
const browser = await chromium.launch(), context = await browser.newContext({ viewport: { width: 1600, height: 1000 } }), page = await context.newPage();
const errors = [], blockedWrites = [], remoteRequests = [];
page.on('pageerror', error => errors.push(error.message));
await context.route('**/*', async route => {
  const request = route.request(), url = request.url();
  if (!['GET', 'HEAD'].includes(request.method())) { blockedWrites.push(`${request.method()} ${url}`); await route.abort(); }
  else if (/^https?:/.test(url) && new URL(url).origin !== origin) { remoteRequests.push(url); await route.abort(); }
  else await route.continue();
});
try {
  await page.goto(origin); await expect(page.locator('.workspace')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Single select', exact: true })).toHaveAttribute('aria-pressed', 'true');
  await page.getByRole('button', { name: 'Single select', exact: true }).click(); await page.getByRole('menuitem', { name: 'Hand', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Hand', exact: true })).toHaveAttribute('aria-pressed', 'true');
  await page.getByRole('button', { name: 'Hand', exact: true }).click(); await page.getByRole('menuitem', { name: 'Single select', exact: true }).click();
  const renderedBounds = await page.locator('.ms-node[data-node-id]').evaluateAll(elements => Object.fromEntries(elements.map(element => [element.getAttribute('data-node-id'), {
    x: parseFloat(element.style.left), y: parseFloat(element.style.top), width: parseFloat(element.style.width), height: parseFloat(element.style.height),
  }])));
  let fittedChildrenVerified = 0;
  for (const parent of library.project.nodes) {
    if (!['container', 'module', 'spreadsheet'].includes(parent.kind) || parent.collapsed || ['gallery', 'window'].includes(parent.containerVariant) || !renderedBounds[parent.id]) continue;
    const interior = boundedChildInterior(library.project, parent, {}, renderedBounds[parent.id]);
    for (const child of library.project.nodes.filter(node => node.parentId === parent.id && renderedBounds[node.id])) {
      const geometry = renderedBounds[child.id];
      assert.ok(geometry.x >= interior.x - 1.5 && geometry.y >= interior.y - 1.5 && geometry.x + geometry.width <= interior.x + interior.width + 1.5 && geometry.y + geometry.height <= interior.y + interior.height + 1.5,
        `Installed visible library child fits the actual parent wall: ${child.title}`);
      fittedChildrenVerified++;
    }
  }
  let bounds = null;
  const embeddedLinks = library.project.links.filter(link => link.canvasId === library.project.entryCanvasId && index.isEmbeddedLink(link));
  for (const link of embeddedLinks) await expect(page.locator(`.ms-viewport [data-link-id="${link.id}"]`)).toHaveCount(0);
  if (groupId) {
    const tile = page.locator(`.ms-node[data-node-id="${groupId}"]`); await expect(tile).toHaveCount(1);
    bounds = await tile.evaluate(element => ({ width: parseFloat(element.style.width), height: parseFloat(element.style.height) }));
    await tile.press('Enter');
    if (!(await page.locator('.inspector').isVisible())) await page.getByRole('button', { name: 'Show side panel', exact: true }).click();
    await page.getByRole('combobox', { name: 'Side panel', exact: true }).selectOption('links');
    for (const link of embeddedLinks.filter(link => link.sourceId === groupId || link.targetId === groupId)) await expect(page.locator(`[data-associated-link="${link.id}"]`)).toHaveCount(1);
  }
  // Exercise discovery controls without revealing hidden branches or changing
  // any authored state in the real library. Fixture-only mutation checks belong
  // to verify-package.mjs on its guarded isolated copied-data host.
  await page.getByRole('combobox', { name: 'Side panel', exact: true }).selectOption('canvas');
  const searchTarget = library.project.nodes.find(node => node.canvasId === library.project.entryCanvasId && node.kind !== 'canvas');
  if (searchTarget) {
    const search = page.getByRole('searchbox', { name: 'Search canvas nodes', exact: true });
    await search.fill(searchTarget.id);
    await expect(page.locator(`[data-outline-node="${searchTarget.id}"][data-search-match="true"]`)).toHaveCount(1);
    await page.getByRole('combobox', { name: 'Search scope', exact: true }).selectOption('all');
    await expect(page.locator(`[data-outline-node="${searchTarget.id}"][data-search-match="true"]`)).toHaveCount(1);
    const searchScreenshot = path.join(qaRoot, 'installed-canvas-search.png'); await page.screenshot({ path: searchScreenshot });
    await search.press('Escape');
  }
  for (const panel of ['properties', 'appearance', 'content', 'relations', 'links']) {
    await page.getByRole('combobox', { name: 'Side panel', exact: true }).selectOption(panel);
    await expect(page.getByRole('searchbox', { name: 'Search canvas nodes', exact: true })).toHaveCount(0);
  }
  await page.getByRole('combobox', { name: 'Side panel', exact: true }).selectOption('canvas');
  const screenshot = path.join(qaRoot, 'installed-canvas-library.png'); await page.screenshot({ path: screenshot });
  await page.waitForTimeout(2200);
  for (const row of documents) {
    assert.deepEqual(await json(`/api/projects/${row.id}`), row.document);
    assert.deepEqual(await json(`/api/projects/${row.id}/recovery`, true), row.recovery);
  }
  assert.deepEqual(await json('/api/projects'), catalog); assert.deepEqual(await json('/api/library'), library);
  assert.deepEqual(await json('/api/library/recovery', true), libraryRecovery);
  assert.deepEqual(errors, []); assert.deepEqual(blockedWrites, []); assert.deepEqual(remoteRequests, []);
  const report = { passed: true, origin, processId: installed.processId, packageFiles: review.packageHashes.length, servedFiles: 5,
    libraryNodeId: groupId, libraryBounds: bounds, fittedChildrenVerified, suppressedEmbeddedLinks: embeddedLinks.length, canvasGuidSearchVerified: !!searchTarget, searchExclusiveToCanvas: true, savedProjectsUnchanged: true, recoveriesUnchanged: true, libraryUnchanged: true,
    browserErrors: errors, blockedWrites, remoteRequests, screenshot };
  await writeFile(path.join(qaRoot, 'installed-review.json'), JSON.stringify(report, null, 2)); process.stdout.write(JSON.stringify(report, null, 2) + '\n');
} finally { await context.close(); await browser.close(); }





