import assert from 'node:assert/strict';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { createHash, randomUUID } from 'node:crypto';
import { mkdir, readFile, writeFile, readdir, lstat } from 'node:fs/promises';
import { chromium, expect } from '@playwright/test';
import { addNode, commitSnapshots, newProject } from '../../src/core/model.ts';
import { validateProject } from '../../src/core/rules.ts';
import { navigationIndex } from '../../src/core/navigation.ts';
import { buildPublication, createPublicationPayload, readPublication } from '../../src/publication.ts';

// Only one fresh fixture can be edited on this owned, copied-data host. Existing
// projects, the library, recovery drafts, checkpoints and live bytes stay intact.
const config = JSON.parse(await readFile(process.argv[2], 'utf8'));
const { origin, qaRoot, dataRoot, packageRoot, sourceRoot } = config;
const workspaceRoot = path.resolve(import.meta.dirname, '../..');
assert.equal(workspaceRoot.toLowerCase(), path.resolve('I:/dev/Meridian').toLowerCase());
assert.equal(new URL(origin).origin, 'http://127.0.0.1:5297');
assert.equal(path.resolve(packageRoot).toLowerCase(), path.resolve(workspaceRoot, 'output/embedded-link-package-20261008').toLowerCase());
assert.equal(path.resolve(qaRoot).toLowerCase(), path.resolve(workspaceRoot, 'output/embedded-link-qa-20261008').toLowerCase());
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
  await walk(absoluteRoot);
  return rows.sort((a, b) => a.relativePath.localeCompare(b.relativePath));
}
const protectedSourceFiles = await fileInventory(sourceRoot, true), protectedCopiedFiles = await fileInventory(dataRoot, true);
assert.equal(protectedSourceFiles.length, config.copiedFiles);
assert.deepEqual(protectedCopiedFiles.map(({ relativePath, sha256 }) => ({ relativePath, sha256 })), protectedSourceFiles.map(({ relativePath, sha256 }) => ({ relativePath, sha256 })));
const fixtureId = randomUUID();
const fixtureRoute = route => route === `/api/projects/${fixtureId}` || route === `/api/projects/${fixtureId}/recovery`;
async function json(route, method = 'GET', data, allow404 = false) {
  assert.ok(route.startsWith('/api/') || route === '/__meridian_status');
  if (!['GET', 'HEAD'].includes(method)) {
    assert.ok(route === '/api/projects' && method === 'POST' && data?.id === fixtureId || fixtureRoute(route), 'Only the fresh fixture may be written.');
    fixtureWrites.push(`${method} ${route}`);
  }
  const url = new URL(route, origin);
  assert.equal(url.origin, 'http://127.0.0.1:5297');
  const response = await fetch(url, { method, headers: data === undefined ? {} : { 'Content-Type': 'application/json' }, body: data === undefined ? undefined : JSON.stringify(data) });
  if (allow404 && response.status === 404) return null;
  assert.ok(response.ok, `${method} ${route}: ${response.status} ${await response.clone().text()}`);
  return response.status === 204 ? null : response.json();
}
const stored = () => json(`/api/projects/${fixtureId}`);
const tile = (page, id) => page.locator(`.ms-node[data-node-id="${id}"]`);
const line = (page, id) => page.locator(`.ms-link-line[data-link-id="${id}"]`);
const nodeIn = (document, id) => document.nodes.find(node => node.id === id);
async function saved(page) { await expect(page.locator('.save-state')).toHaveText(/^Saved · r\d+$/); }
async function screenshot(page, name) {
  const filename = path.join(qaRoot, name);
  await page.screenshot({ path: filename }); screenshots.push(filename);
}
async function panel(page, name) {
  if (!(await page.locator('.inspector').isVisible())) await page.getByRole('button', { name: 'Show side panel', exact: true }).click();
  await page.getByRole('combobox', { name: 'Side panel', exact: true }).selectOption(name);
}
async function selectNode(page, id) {
  await panel(page, 'canvas');
  const search = page.getByRole('searchbox', { name: 'Search canvas nodes', exact: true });
  await search.fill(id); await page.locator(`button[data-jump-to-node="${id}"]`).click();
  if (await search.isVisible()) await search.fill('');
}
assert.equal((await json('/__meridian_status')).application, 'atlas-meridian');
const copiedCatalog = await json('/api/projects'), copiedProjects = [];
for (const entry of copiedCatalog) copiedProjects.push({ entry, document: await json(`/api/projects/${entry.id}`), recovery: await json(`/api/projects/${entry.id}/recovery`, 'GET', undefined, true) });
const copiedLibrary = await json('/api/library'), copiedLibraryRecovery = await json('/api/library/recovery', 'GET', undefined, true);
const protectedEnvelopes = { catalog: copiedCatalog, projects: copiedProjects, library: copiedLibrary, libraryRecovery: copiedLibraryRecovery };
await writeFile(path.join(qaRoot, 'copied-content-baselines.json'), JSON.stringify({ envelopes: protectedEnvelopes, sourceFiles: protectedSourceFiles, copiedFiles: protectedCopiedFiles }, null, 2));
record('Recorded every copied project, recovery, library envelope and original file hash using read-only requests.');

const viewerJS = await readFile(path.join(packageRoot, 'app/wwwroot/viewer.js'), 'utf8');
const sceneCSS = await readFile(path.join(packageRoot, 'app/wwwroot/scene.css'), 'utf8');
const browser = await chromium.launch(), contexts = [];
async function context(options = {}) {
  const current = await browser.newContext({ viewport: { width: 1600, height: 1000 }, ...options }); contexts.push(current);
  current.setDefaultTimeout(15000); current.setDefaultNavigationTimeout(18000);
  current.on('page', page => page.on('pageerror', error => browserErrors.push(error.message)));
  await current.route('**/*', async route => {
    const request = route.request(), url = request.url();
    if (/^https?:/i.test(url) && new URL(url).origin !== origin) { remoteRequests.push(url); await route.abort(); }
    else if (/^https?:/i.test(url) && !['GET', 'HEAD'].includes(request.method())) {
      if (!fixtureRoute(new URL(url).pathname)) { blockedWrites.push(`${request.method()} ${url}`); await route.abort(); }
      else { fixtureWrites.push(`${request.method()} ${new URL(url).pathname}`); await route.continue(); }
    } else await route.continue();
  });
  return current;
}
const editorContext = await context(), page = await editorContext.newPage();
let fixture, finalProject, parent, first, second, outside, mainCanvas;
const containmentIds = ['enclosed-forward', 'enclosed-reverse'], visibleIds = ['sibling-comparison', 'outside-comparison'];
async function checkScene(surface) {
  await expect(tile(surface, parent.id)).toHaveCount(1);
  await expect(tile(surface, first.id)).toHaveCount(1);
  await expect(tile(surface, second.id)).toHaveCount(1);
  await expect(tile(surface, outside.id)).toHaveCount(1);
  for (const id of containmentIds) await expect(surface.locator(`.meridian-scene [data-link-id="${id}"]`)).toHaveCount(0);
  for (const id of visibleIds) await expect(line(surface, id)).toHaveCount(1);
}
try {
  fixture = newProject('Embedded relationships · isolated package review'); fixture.id = fixtureId;
  mainCanvas = fixture.canvases[0]; mainCanvas.title = 'Containment without duplicate lines';
  parent = addNode(fixture, mainCanvas.id, 'container', 'Embedded collection');
  Object.assign(parent, { x: 100, y: 100, width: 660, height: 420, sizeMode: 'fixed', collapsed: false, containerVariant: 'contained', childLayout: 'contained', text: 'Containment is the visual relationship.', style: { fill: '#1c302d', border: '#426258', textColor: '#dae9e4' } });
  first = addNode(fixture, mainCanvas.id, 'text', 'First embedded object');
  Object.assign(first, { parentId: parent.id, x: 120, y: 220, width: 240, height: 100, sizeMode: 'fixed', text: 'An embedded parent link remains in Links.' });
  second = addNode(fixture, mainCanvas.id, 'text', 'Second embedded object');
  Object.assign(second, { parentId: parent.id, x: 420, y: 320, width: 240, height: 100, sizeMode: 'fixed', text: 'A sibling relationship still draws its line.' });
  outside = addNode(fixture, mainCanvas.id, 'text', 'Another collection');
  Object.assign(outside, { x: 1000, y: 220, width: 240, height: 100, sizeMode: 'fixed', text: 'Connections outside the parent remain visible.' });
  fixture.links.push(
    { id: containmentIds[0], canvasId: mainCanvas.id, parentId: parent.id, sourceId: parent.id, targetId: first.id, kind: 'visual', relation: 'causal', direction: 'forward', label: 'Contained descendant', points: [{ x: 180, y: 180 }] },
    { id: containmentIds[1], canvasId: mainCanvas.id, parentId: parent.id, sourceId: second.id, targetId: parent.id, kind: 'visual', relation: 'chronological', direction: 'both', label: 'Reverse containment' },
    { id: visibleIds[0], canvasId: mainCanvas.id, parentId: parent.id, sourceId: first.id, targetId: second.id, kind: 'visual', relation: 'mutual', label: 'Comparison between siblings' },
    { id: visibleIds[1], canvasId: mainCanvas.id, sourceId: second.id, targetId: outside.id, kind: 'visual', relation: 'generic', label: 'Comparison with outside' },
  );
  assert.deepEqual(validateProject(fixture), []);
  const created = await json('/api/projects', 'POST', commitSnapshots(fixture)); assert.equal(created.id, fixtureId);
  await page.goto(`${origin}/?project=${fixtureId}&canvas=${mainCanvas.id}`);
  await expect(page.locator('.workspace')).toBeVisible(); await saved(page); await checkScene(page);
  await selectNode(page, parent.id); await panel(page, 'links');
  for (const id of containmentIds) await expect(page.locator(`[data-associated-link="${id}"]`)).toContainText('Embedded · no line');
  await page.locator(`[data-associated-link="${containmentIds[0]}"] .inspector-link-title`).click();
  await expect(page.getByRole('combobox', { name: 'Contained in', exact: true })).toHaveValue(parent.id);
  const label = page.getByLabel('Connection label', { exact: true });
  await label.fill('Preserved embedded relationship'); await label.press('Tab');
  await expect.poll(async () => (await stored()).links.find(link => link.id === containmentIds[0]).label).toBe('Preserved embedded relationship');
  await saved(page); await checkScene(page);
  await expect(page.locator(`.meridian-scene [data-link-id="${containmentIds[0]}"]`)).toHaveCount(0);
  await page.getByRole('button', { name: 'Back to associated links', exact: true }).click();
  await expect(page.locator(`[data-associated-link="${containmentIds[0]}"]`)).toContainText('Preserved embedded relationship');
  await screenshot(page, 'package-embedded-links-inspector.png');
  record('Packaged editor omits parent-child shafts, arrows, labels and edit handles in both endpoint directions. Sibling and external links remain visible; embedded links stay listed, editable and automatically saved.');

  await page.reload(); await saved(page); await checkScene(page);
  const reopened = await stored();
  assert.equal(reopened.links.length, 4);
  assert.equal(reopened.links.find(link => link.id === containmentIds[0]).label, 'Preserved embedded relationship');
  const index = navigationIndex(reopened);
  for (const id of containmentIds) { const link = reopened.links.find(link => link.id === id); assert.equal(index.isLinkRevealed(link), true); assert.equal(index.isEmbeddedLink(link), true); assert.equal(index.isLinkVisible(link), false); }
  for (const id of visibleIds) assert.equal(index.isLinkVisible(reopened.links.find(link => link.id === id)), true);
  await selectNode(page, parent.id); await panel(page, 'relations');
  await page.getByRole('button', { name: 'Toggle contents from relations', exact: true }).click();
  await expect(tile(page, first.id)).toHaveCount(0); await expect(tile(page, second.id)).toHaveCount(0);
  for (const id of [...containmentIds, ...visibleIds]) await expect(line(page, id)).toHaveCount(0);
  await page.getByRole('button', { name: 'Toggle contents from relations', exact: true }).click();
  await saved(page); await checkScene(page);
  finalProject = await stored(); assert.equal(finalProject.links.length, 4);
  assert.deepEqual(finalProject.links.find(link => link.id === containmentIds[0]).points, [{ x: 180, y: 180 }]);
  await screenshot(page, 'package-embedded-links-expanded.png');
  record('Reload and collapse/re-expand preserve every link GUID, ownership, route and edited label; only embedded parent-child visual elements are suppressed.');

  const html = buildPublication(finalProject, viewerJS, sceneCSS), offlinePath = path.join(qaRoot, 'package-embedded-links-offline.html');
  await writeFile(offlinePath, html);
  const portable = createPublicationPayload(finalProject).project, imported = readPublication(html);
  assert.deepEqual(portable.links, finalProject.links);
  assert.equal(imported.links.length, finalProject.links.length);
  assert.equal(imported.links.find(link => link.label === 'Preserved embedded relationship').relation, 'causal');
  const offlineContext = await context({ offline: true }), offline = await offlineContext.newPage();
  await offline.goto(pathToFileURL(offlinePath).href); await checkScene(offline);
  const payload = await offline.locator('#meridian-data').textContent();
  await tile(offline, parent.id).press('Enter');
  await expect(offline.getByRole('button', { name: 'Select connection Preserved embedded relationship', exact: true })).toBeVisible();
  await offline.getByRole('button', { name: 'Select connection Preserved embedded relationship', exact: true }).click();
  await expect(offline.locator('.mv-node-context')).toContainText('embedded · no line');
  assert.equal(await offline.locator('#meridian-data').textContent(), payload);
  await screenshot(offline, 'package-embedded-links-offline.png');
  record('Offline publication matches editor suppression, retains sibling/external lines and all authored link metadata, and provides embedded-link inspection without changing its payload.');

  for (const copied of copiedProjects) {
    assert.deepEqual(await json(`/api/projects/${copied.entry.id}`), copied.document, 'An existing copied project changed.');
    assert.deepEqual(await json(`/api/projects/${copied.entry.id}/recovery`, 'GET', undefined, true), copied.recovery, 'An existing copied recovery changed.');
  }
  assert.deepEqual((await json('/api/projects')).filter(entry => entry.id !== fixtureId), copiedCatalog);
  assert.deepEqual(await json('/api/library'), copiedLibrary);
  assert.deepEqual(await json('/api/library/recovery', 'GET', undefined, true), copiedLibraryRecovery);
  const copiedAfter = await fileInventory(dataRoot, true), previousNames = new Set(protectedCopiedFiles.map(file => file.relativePath));
  for (const file of protectedCopiedFiles) assert.equal(copiedAfter.find(current => current.relativePath === file.relativePath)?.sha256, file.sha256, `A protected copied file changed: ${file.relativePath}`);
  const fixturePrefix = path.join('projects', fixtureId) + path.sep;
  for (const file of copiedAfter.filter(current => !previousNames.has(current.relativePath))) assert.ok(file.relativePath.startsWith(fixturePrefix), `An unexpected copied-data file appeared: ${file.relativePath}`);
  assert.deepEqual(await fileInventory(sourceRoot, true), protectedSourceFiles, 'Live saved bytes changed.');
  assert.deepEqual(browserErrors, []); assert.deepEqual(remoteRequests, []); assert.deepEqual(blockedWrites, []);
  const packageHashes = (await fileInventory(path.join(packageRoot, 'app'))).map(({ path, sha256 }) => ({ path, sha256 }));
  await writeFile(path.join(qaRoot, 'package-review.json'), JSON.stringify({ passed: true, origin, isolatedDataRoot: dataRoot, copiedSourceFiles: config.copiedFiles, fixtureProjectId: fixtureId, fixtureCanvasId: mainCanvas.id,
    savedDataUnchanged: true, copiedDataUnchanged: true, protectedSourceFiles, protectedCopiedFiles, protectedEnvelopeSha256: digest(JSON.stringify(protectedEnvelopes)), checks, screenshots, packageHashes, browserErrors, remoteRequests, blockedWrites, fixtureWrites }, null, 2));
  process.stdout.write(JSON.stringify({ checks: checks.length, screenshots, packageFiles: packageHashes.length, browserErrors, remoteRequests, blockedWrites }, null, 2) + '\n');
} catch (error) {
  if (!page.isClosed()) await screenshot(page, 'package-failure.png').catch(() => {});
  const failure = { passed: false, message: error.message, stack: error.stack, checks, screenshots, browserErrors, remoteRequests, blockedWrites, fixtureWrites };
  await writeFile(path.join(qaRoot, 'package-review.json'), JSON.stringify(failure, null, 2));
  await writeFile(path.join(qaRoot, 'package-failure.json'), JSON.stringify(failure, null, 2));
  throw error;
} finally { for (const current of contexts.reverse()) await current.close(); await browser.close(); }
