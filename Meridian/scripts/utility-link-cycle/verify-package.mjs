import assert from 'node:assert/strict';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { createHash, randomUUID } from 'node:crypto';
import { mkdir, readFile, writeFile, readdir, lstat } from 'node:fs/promises';
import { chromium, expect } from '@playwright/test';
import { addNode, commitSnapshots, newProject } from '../../src/core/model.ts';
import { validateProject } from '../../src/core/rules.ts';
import { buildPublication, createPublicationPayload, readPublication } from '../../src/publication.ts';

// Review one fresh fixture on a specifically owned, copied-data host. Nothing
// in the existing projects, library, recoveries, checkpoints, or live storage
// is writable through this review's browser and API helpers.
const config = JSON.parse(await readFile(process.argv[2], 'utf8'));
const { origin, qaRoot, dataRoot, packageRoot, sourceRoot } = config;
const workspaceRoot = path.resolve(import.meta.dirname, '../..');
assert.equal(workspaceRoot.toLowerCase(), path.resolve('I:/dev/Meridian').toLowerCase());
assert.equal(new URL(origin).origin, 'http://127.0.0.1:5298');
assert.equal(path.resolve(packageRoot).toLowerCase(), path.resolve(workspaceRoot, 'output/utility-link-package-20261008').toLowerCase());
assert.equal(path.resolve(qaRoot).toLowerCase(), path.resolve(workspaceRoot, 'output/utility-link-qa-20261008').toLowerCase());
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
const fixtureRoute = route => route === `/api/projects/${fixtureId}` || route === `/api/projects/${fixtureId}/recovery`;
async function json(route, method = 'GET', data, allow404 = false) {
  assert.ok(route.startsWith('/api/') || route === '/__meridian_status');
  if (!['GET', 'HEAD'].includes(method)) {
    assert.ok(route === '/api/projects' && method === 'POST' && data?.id === fixtureId || fixtureRoute(route), 'Only the fresh fixture may be written.');
    fixtureWrites.push(`${method} ${route}`);
  }
  const url = new URL(route, origin); assert.equal(url.origin, 'http://127.0.0.1:5298');
  const response = await fetch(url, { method, headers: data === undefined ? {} : { 'Content-Type': 'application/json' }, body: data === undefined ? undefined : JSON.stringify(data) });
  if (allow404 && response.status === 404) return null;
  assert.ok(response.ok, `${method} ${route}: ${response.status} ${await response.clone().text()}`);
  return response.status === 204 ? null : response.json();
}
const stored = () => json(`/api/projects/${fixtureId}`);
const tile = (page, id) => page.locator(`.ms-node[data-node-id="${id}"]`);
const line = (page, id) => page.locator(`.ms-link-line[data-link-id="${id}"]`).first();
const nodeIn = (project, id) => project.nodes.find(node => node.id === id);
async function saved(page) { await expect(page.locator('.save-state')).toHaveText(/^Saved · r\d+$/); }
async function screenshot(page, name) { const filename = path.join(qaRoot, name); await page.screenshot({ path: filename }); screenshots.push(filename); }
async function panel(page, name) {
  if (!(await page.locator('.inspector').isVisible())) await page.getByRole('button', { name: 'Show side panel', exact: true }).click();
  await page.getByRole('combobox', { name: 'Side panel', exact: true }).selectOption(name);
}
async function mode(page, value) {
  await page.getByRole('button', { name: /^New (?:Node|Link)$/ }).click();
  await page.getByRole('menuitem', { name: value, exact: true }).click();
}
async function worldClick(page, x, y) {
  const point = await page.locator('.ms-viewport').evaluate((viewport, point) => {
    const rect = viewport.getBoundingClientRect(), screen = new DOMPoint(point.x, point.y).matrixTransform(new DOMMatrix(getComputedStyle(viewport.querySelector('.ms-world')).transform));
    return { x: rect.left + screen.x, y: rect.top + screen.y };
  }, { x, y });
  await page.mouse.click(point.x, point.y);
}
async function onLine(page, id, fraction = .45) {
  const point = await page.locator(`.ms-link-hit[data-link-id="${id}"]`).first().evaluate((element, fraction) => {
    const at = element.getPointAtLength(element.getTotalLength() * fraction), screen = new DOMPoint(at.x, at.y).matrixTransform(element.getScreenCTM());
    return { x: screen.x, y: screen.y };
  }, fraction);
  await page.mouse.click(point.x, point.y); return point;
}
async function change(input, value) { await input.fill(value); await input.press('Tab'); }
assert.equal((await json('/__meridian_status')).application, 'atlas-meridian');
const copiedCatalog = await json('/api/projects'), copiedProjects = [];
for (const entry of copiedCatalog) copiedProjects.push({ entry, document: await json(`/api/projects/${entry.id}`), recovery: await json(`/api/projects/${entry.id}/recovery`, 'GET', undefined, true) });
const copiedLibrary = await json('/api/library'), copiedLibraryRecovery = await json('/api/library/recovery', 'GET', undefined, true);
const protectedEnvelopes = { catalog: copiedCatalog, projects: copiedProjects, library: copiedLibrary, libraryRecovery: copiedLibraryRecovery };
await writeFile(path.join(qaRoot, 'copied-content-baselines.json'), JSON.stringify({ envelopes: protectedEnvelopes, sourceFiles: protectedSourceFiles, copiedFiles: protectedCopiedFiles }, null, 2));
record('Recorded every copied project, recovery, library envelope and original saved-file hash through read-only requests.');

const viewerJS = await readFile(path.join(packageRoot, 'app/wwwroot/viewer.js'), 'utf8'), sceneCSS = await readFile(path.join(packageRoot, 'app/wwwroot/scene.css'), 'utf8');
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
let fixture, finalProject, mainCanvas, source, destination, branchA, branchB, pivotId, jointId, outgoingId;
async function checkScene(surface) {
  for (const node of [source, destination, branchA, branchB]) await expect(tile(surface, node.id)).toHaveCount(1);
  await expect(tile(surface, pivotId)).toHaveCount(1); await expect(tile(surface, jointId)).toHaveCount(1);
  const outgoing = line(surface, outgoingId);
  await expect(outgoing).toHaveCSS('stroke', 'rgb(219, 107, 156)'); await expect(outgoing).toHaveCSS('stroke-width', '12px');
  await expect(outgoing).toHaveCSS('stroke-linecap', 'round'); await expect(outgoing).toHaveCSS('stroke-dasharray', '0px, 36px');
  for (const link of finalProject.links) await expect(line(surface, link.id)).toHaveCount(1);
}
try {
  fixture = newProject('Link utility tools · isolated package review'); fixture.id = fixtureId; mainCanvas = fixture.canvases[0];
  mainCanvas.snapping = { stickyEdges: false, smartAlign: false, snapToGrid: false };
  source = addNode(fixture, mainCanvas.id, 'text', 'One source'); destination = addNode(fixture, mainCanvas.id, 'text', 'Shared destination');
  branchA = addNode(fixture, mainCanvas.id, 'text', 'Another source'); branchB = addNode(fixture, mainCanvas.id, 'text', 'A third source');
  for (const [node, x, y, text] of [[source, 100, 100, 'Trace a route through a turning point.'], [destination, 780, 100, 'Incoming paths share one final link.'], [branchA, 100, 470, 'Meet at an explicit joint.'], [branchB, 780, 470, 'Reuse the same joint and outgoing path.']])
    Object.assign(node, { x, y, width: 180, height: 105, sizeMode: 'fixed', childLayout: 'spatial', text });
  assert.deepEqual(validateProject(fixture), []);
  const created = await json('/api/projects', 'POST', commitSnapshots(fixture)); assert.equal(created.id, fixtureId);
  await page.goto(`${origin}/?project=${fixtureId}&canvas=${mainCanvas.id}`); await expect(page.locator('.workspace')).toBeVisible(); await saved(page);

  await mode(page, 'Link'); await tile(page, source.id).locator('.ms-node-title').click(); await worldClick(page, 430, 320);
  await expect(page.getByRole('dialog')).toHaveCount(0); await expect(page.locator('.ms-link-draft-line')).toHaveCount(1);
  const pending = await stored(); assert.equal(pending.nodes.length, fixture.nodes.length); assert.equal(pending.links.length, 0);
  await screenshot(page, 'package-link-draft.png');
  await page.keyboard.press('Escape'); await expect(page.locator('.ms-link-draft-line')).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'New Link', exact: true })).toHaveAttribute('aria-pressed', 'true');
  assert.equal((await stored()).nodes.length, fixture.nodes.length);
  await tile(page, source.id).locator('.ms-node-title').click(); await worldClick(page, 430, 320); await tile(page, destination.id).locator('.ms-node-title').click();
  await expect.poll(async () => (await stored()).links.length).toBe(2); await saved(page);
  let current = await stored(), pivot = current.nodes.find(node => node.utilityVariant === 'pivot'); assert.ok(pivot); pivotId = pivot.id;
  assert.equal(current.nodes.length, fixture.nodes.length + 1); assert.equal(pivot.width, 12); assert.equal(pivot.height, 12);
  assert.equal(current.links.filter(link => link.sourceId === pivotId || link.targetId === pivotId).length, 2);
  await expect(page.getByRole('dialog')).toHaveCount(0);
  record('Packaged Link tool previews source and empty-space pivots without writing, cancels cleanly on Escape, and commits the finished route and utility as one graph change without a dialog.');

  await page.getByRole('button', { name: 'Hand', exact: true }).click(); await tile(page, pivotId).click(); await panel(page, 'properties');
  await expect(page.getByLabel('Type', { exact: true })).toHaveValue('utility'); await expect(page.getByLabel('Sub-type', { exact: true })).toHaveValue('pivot');
  await expect(page.getByLabel('GUID', { exact: true })).toHaveValue(pivotId); await panel(page, 'appearance');
  await page.getByLabel('Utility appearance', { exact: true }).selectOption('line');
  await expect(page.getByLabel('Width', { exact: true })).toHaveValue('32'); await expect(page.getByLabel('Height', { exact: true })).toHaveValue('6');
  await page.getByLabel('Utility appearance', { exact: true }).selectOption('dot');
  await expect(page.getByLabel('Width', { exact: true })).toHaveValue('12'); await expect(page.getByLabel('Height', { exact: true })).toHaveValue('12');
  await change(page.getByLabel('Fill color', { exact: true }), '#e78056'); await change(page.getByLabel('Width', { exact: true }), '18'); await change(page.getByLabel('Height', { exact: true }), '18');
  await expect.poll(async () => nodeIn(await stored(), pivotId)).toMatchObject({ width: 18, height: 18, style: { fill: '#e78056' } });
  current = await stored(); const resized = nodeIn(current, pivotId);
  assert.ok(Math.abs(resized.x + resized.width / 2 - pivot.x - pivot.width / 2) < .001); assert.ok(Math.abs(resized.y + resized.height / 2 - pivot.y - pivot.height / 2) < .001);
  const connected = current.links.filter(link => link.sourceId === pivotId || link.targetId === pivotId), pathsBefore = await Promise.all(connected.map(link => line(page, link.id).getAttribute('d')));
  const bounds = await tile(page, pivotId).boundingBox(); assert.ok(bounds);
  await page.mouse.move(bounds.x + bounds.width / 2, bounds.y + bounds.height / 2); await page.mouse.down();
  await page.mouse.move(bounds.x + bounds.width / 2 + 60, bounds.y + bounds.height / 2 + 35, { steps: 8 }); await page.mouse.up();
  await expect.poll(async () => nodeIn(await stored(), pivotId)?.x).not.toBe(resized.x); await saved(page);
  const pathsAfter = await Promise.all(connected.map(link => line(page, link.id).getAttribute('d')));
  for (let index = 0; index < pathsBefore.length; index++) assert.notEqual(pathsAfter[index], pathsBefore[index]);
  await screenshot(page, 'package-pivot-appearance.png');
  record('Pivot selection opens ordinary Utility properties and Appearance; Dot/Line choices, custom color and dimensions save while preserving its center, and dragging changes both adjacent link paths.');

  current = await stored(); const pivotOutgoing = current.links.find(link => link.sourceId === pivotId && link.targetId === destination.id); assert.ok(pivotOutgoing);
  await mode(page, 'Node'); const insertionScreen = await onLine(page, pivotOutgoing.id, .55);
  const dialog = page.getByRole('dialog', { name: 'New Node', exact: true }); await expect(dialog).toBeVisible();
  assert.deepEqual(await dialog.getByLabel('Type', { exact: true }).locator('option').allTextContents(), ['Utility']);
  await expect(dialog.getByLabel('Sub-type', { exact: true })).toHaveValue('pivot'); await dialog.getByLabel('Sub-type', { exact: true }).selectOption('joint');
  await dialog.getByRole('button', { name: 'Create node', exact: true }).click(); await expect(dialog).toHaveCount(0);
  await expect.poll(async () => (await stored()).links.length).toBe(3); current = await stored(); const joint = current.nodes.find(node => node.utilityVariant === 'joint'); assert.ok(joint); jointId = joint.id;
  const jointBounds = await tile(page, jointId).boundingBox(); assert.ok(jointBounds);
  assert.ok(Math.abs(jointBounds.x + jointBounds.width / 2 - insertionScreen.x) < 2, 'Joint did not land on the clicked visual line position.');
  assert.ok(Math.abs(jointBounds.y + jointBounds.height / 2 - insertionScreen.y) < 2, 'Joint did not land on the clicked visual line position.');
  await mode(page, 'Link');
  for (const branch of [branchA, branchB]) { await tile(page, branch.id).locator('.ms-node-title').click(); await tile(page, jointId).click(); }
  await expect.poll(async () => (await stored()).links.length).toBe(5); await saved(page); current = await stored();
  assert.equal(current.links.filter(link => link.targetId === jointId).length, 3);
  const outgoing = current.links.filter(link => link.sourceId === jointId); assert.equal(outgoing.length, 1); assert.equal(outgoing[0].targetId, destination.id); outgoingId = outgoing[0].id;
  await page.getByRole('button', { name: 'Hand', exact: true }).click(); await tile(page, jointId).click(); await panel(page, 'properties');
  await expect(page.getByLabel('Type', { exact: true })).toHaveValue('utility'); await expect(page.getByLabel('Sub-type', { exact: true })).toHaveValue('joint');
  await screenshot(page, 'package-joint-properties.png');
  record('Node tool inserts a Pivot/Joint form on the actual clicked line; Joint selection populates properties, and three incoming routes now meet at one utility with exactly one shared outgoing segment.');

  await onLine(page, outgoingId, .65); await panel(page, 'appearance');
  await change(page.getByLabel('Line color', { exact: true }), '#db6b9c'); await change(page.getByLabel('Line width', { exact: true }), '12');
  await page.getByLabel('Line style', { exact: true }).selectOption('solid');
  await expect(line(page, outgoingId)).toHaveCSS('stroke', 'rgb(219, 107, 156)'); await expect(line(page, outgoingId)).toHaveCSS('stroke-width', '12px');
  await expect(line(page, outgoingId)).toHaveCSS('stroke-dasharray', 'none');
  await page.getByLabel('Line style', { exact: true }).selectOption('dotted');
  await expect(line(page, outgoingId)).toHaveCSS('stroke-linecap', 'round'); await expect(line(page, outgoingId)).toHaveCSS('stroke-dasharray', '0px, 36px');
  await expect.poll(async () => (await stored()).links.find(link => link.id === outgoingId)?.style).toMatchObject({ color: '#db6b9c', width: 12, lineStyle: 'dotted' }); await saved(page);
  await screenshot(page, 'package-link-appearance.png');
  record('Selected-link color and 12-pixel thickness update immediately; Solid produces a continuous shaft and Dotted produces round dots, without selection overriding the authored appearance.');

  await page.reload(); await saved(page); finalProject = await stored(); assert.deepEqual(validateProject(finalProject), []); await checkScene(page);
  assert.equal(finalProject.nodes.filter(node => node.kind === 'junction').length, 2); assert.equal(nodeIn(finalProject, pivotId).utilityVariant, 'pivot'); assert.equal(nodeIn(finalProject, jointId).utilityVariant, 'joint');
  assert.equal(finalProject.links.filter(link => link.sourceId === jointId).length, 1); assert.equal(finalProject.links.filter(link => link.targetId === jointId).length, 3);
  await page.getByRole('button', { name: 'Fit canvas', exact: true }).click(); await screenshot(page, 'package-shared-joint-reopened.png');
  record('Automatic saving and reload retain utility GUIDs, sub-types, moved geometry, colors, complete shared link graph and explicit dotted style.');

  const html = buildPublication(finalProject, viewerJS, sceneCSS), offlinePath = path.join(qaRoot, 'package-utility-links-offline.html'); await writeFile(offlinePath, html);
  const portable = createPublicationPayload(finalProject).project, imported = readPublication(html);
  assert.deepEqual(portable.links, finalProject.links); assert.equal(imported.links.length, finalProject.links.length);
  assert.equal(imported.nodes.filter(node => node.utilityVariant === 'pivot').length, 1); assert.equal(imported.nodes.filter(node => node.utilityVariant === 'joint').length, 1);
  const offlineContext = await context({ offline: true }), offline = await offlineContext.newPage(); await offline.goto(pathToFileURL(offlinePath).href); await checkScene(offline);
  const payload = await offline.locator('#meridian-data').textContent(); await tile(offline, jointId).press('Enter');
  await expect(offline.locator('.mv-node-context')).toContainText('Joint'); assert.equal(await offline.locator('#meridian-data').textContent(), payload);
  await screenshot(offline, 'package-utility-links-offline.png');
  record('Offline publication retains Utility sub-types and authored graph, matches route geometry and custom dotted link appearance, and permits utility inspection without mutating its payload.');

  for (const copied of copiedProjects) {
    assert.deepEqual(await json(`/api/projects/${copied.entry.id}`), copied.document, 'An existing copied project changed.');
    assert.deepEqual(await json(`/api/projects/${copied.entry.id}/recovery`, 'GET', undefined, true), copied.recovery, 'An existing copied recovery changed.');
  }
  assert.deepEqual((await json('/api/projects')).filter(entry => entry.id !== fixtureId), copiedCatalog);
  assert.deepEqual(await json('/api/library'), copiedLibrary); assert.deepEqual(await json('/api/library/recovery', 'GET', undefined, true), copiedLibraryRecovery);
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
  await writeFile(path.join(qaRoot, 'package-review.json'), JSON.stringify(failure, null, 2)); await writeFile(path.join(qaRoot, 'package-failure.json'), JSON.stringify(failure, null, 2)); throw error;
} finally { for (const current of contexts.reverse()) await current.close(); await browser.close(); }
