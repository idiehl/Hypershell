import assert from 'node:assert/strict';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { createHash, randomUUID } from 'node:crypto';
import { mkdir, readFile, writeFile, readdir, lstat } from 'node:fs/promises';
import { chromium, expect } from '@playwright/test';
import { addNode, commitSnapshots, newProject } from '../../src/core/model.ts';
import { validateProject } from '../../src/core/rules.ts';
import { buildPublication, createPublicationPayload, readPublication } from '../../src/publication.ts';

// Only a fresh fixture may be written on this specifically owned copied-data
// host. Existing projects, library, recoveries, checkpoints and live storage
// remain protected by request guards, complete API baselines and file hashes.
const config = JSON.parse(await readFile(process.argv[2], 'utf8'));
const { origin, qaRoot, dataRoot, packageRoot, sourceRoot } = config;
const workspaceRoot = path.resolve(import.meta.dirname, '../..');
assert.equal(workspaceRoot.toLowerCase(), path.resolve('I:/dev/Meridian').toLowerCase());
assert.equal(new URL(origin).origin, 'http://127.0.0.1:5300');
assert.equal(path.resolve(packageRoot).toLowerCase(), path.resolve(workspaceRoot, 'output/clipboard-package-20261008').toLowerCase());
assert.equal(path.resolve(qaRoot).toLowerCase(), path.resolve(workspaceRoot, 'output/clipboard-qa-20261008').toLowerCase());
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
  const url = new URL(route, origin); assert.equal(url.origin, 'http://127.0.0.1:5300');
  const response = await fetch(url, { method, headers: data === undefined ? {} : { 'Content-Type': 'application/json' }, body: data === undefined ? undefined : JSON.stringify(data) });
  if (allow404 && response.status === 404) return null;
  assert.ok(response.ok, `${method} ${route}: ${response.status} ${await response.clone().text()}`);
  return response.status === 204 ? null : response.json();
}
const stored = () => json(`/api/projects/${fixtureId}`);
const tile = (page, id) => page.locator(`.ms-node[data-node-id="${id}"]`);
const nodeIn = (project, id) => project.nodes.find(node => node.id === id);
async function saved(page) { await expect(page.locator('.save-state')).toHaveText(/^Saved · r\d+$/); }
async function screenshot(page, name) { const filename = path.join(qaRoot, name); await page.screenshot({ path: filename }); screenshots.push(filename); }
async function panel(page, name) {
  if (!(await page.locator('.inspector').isVisible())) await page.getByRole('button', { name: 'Show side panel', exact: true }).click();
  await page.getByRole('combobox', { name: 'Side panel', exact: true }).selectOption(name);
}
const ids = project => new Set(project.nodes.map(node => node.id));
const newNodes = (before, after) => after.nodes.filter(node => !ids(before).has(node.id));
const descendants = (project, rootId) => {
  const included = new Set([rootId]);
  for (let previous = -1; previous !== included.size;) { previous = included.size; for (const node of project.nodes) if (included.has(node.parentId)) included.add(node.id); }
  return project.nodes.filter(node => included.has(node.id));
};
function assertNoRootOverlap(project, roots) {
  for (let index = 0; index < roots.length; index++) for (const other of roots.slice(index + 1)) {
    const a = nodeIn(project, roots[index]), b = nodeIn(project, other); assert.ok(a && b);
    assert.ok(a.x + a.width <= b.x || b.x + b.width <= a.x || a.y + a.height <= b.y || b.y + b.height <= a.y, `Pasted roots overlap: ${a.id} and ${b.id}`);
  }
}
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
  const current = await browser.newContext({ viewport: { width: 1600, height: 1000 }, permissions: ['clipboard-read', 'clipboard-write'], ...options }); contexts.push(current);
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
let fixture, finalProject, mainCanvas, collection, text, image, pivot, reference, independentText, independentUtility;
const containerCopies = [], textCopies = [], utilityCopies = [];
function checkCollectionCopy(project, root, original) {
  const contents = descendants(project, root.id), originalContents = descendants(original, collection.id);
  assert.equal(contents.length, originalContents.length); assert.equal(root.kind, 'container'); assert.equal(root.containerVariant, collection.containerVariant);
  assert.deepEqual(root.style, collection.style);
  for (const item of contents) assert.ok(!ids(original).has(item.id), 'Every copied descendant must have its own GUID.');
  const mapping = new Map(originalContents.map(node => [node.id, contents.find(copy => copy.title === node.title)?.id]));
  assert.ok([...mapping.values()].every(Boolean), 'Titles and full descendant collection survived.');
  for (const item of originalContents) {
    const copy = nodeIn(project, mapping.get(item.id));
    assert.equal(copy.kind, item.kind); assert.equal(copy.text, item.text); assert.deepEqual(copy.style, item.style);
    if (item.id !== collection.id) assert.equal(copy.parentId, mapping.get(item.parentId));
    if (item.targetId) assert.equal(copy.targetId, mapping.get(item.targetId));
    if (item.utilityVariant) { assert.equal(copy.utilityVariant, item.utilityVariant); assert.equal(copy.width, item.width); assert.equal(copy.height, item.height); }
    const assetContent = (document, id) => { const asset = document.assets.find(candidate => candidate.id === id); assert.ok(asset); return { name: asset.name, mime: asset.mime, base64: asset.base64 }; };
    if (item.assetId) assert.deepEqual(assetContent(project, copy.assetId), assetContent(original, item.assetId));
    if (item.attachments) assert.deepEqual(copy.attachments.map(id => assetContent(project, id)), item.attachments.map(id => assetContent(original, id)));
  }
  const originalLinks = original.links.filter(link => mapping.has(link.sourceId) && mapping.has(link.targetId));
  const copiedLinks = project.links.filter(link => contents.some(node => node.id === link.sourceId) && contents.some(node => node.id === link.targetId));
  assert.equal(copiedLinks.length, originalLinks.length);
  for (const link of originalLinks) {
    const copy = copiedLinks.find(candidate => candidate.sourceId === mapping.get(link.sourceId) && candidate.targetId === mapping.get(link.targetId));
    assert.ok(copy); assert.notEqual(copy.id, link.id); assert.deepEqual(copy.style, link.style); assert.equal(copy.parentId, mapping.get(link.parentId));
  }
}
try {
  fixture = newProject('Node copy and paste · isolated package review'); fixture.id = fixtureId; mainCanvas = fixture.canvases[0];
  mainCanvas.snapping = { stickyEdges: false, smartAlign: false, snapToGrid: false };
  const attachment = { id: randomUUID(), name: 'Preserved source note.txt', mime: 'text/plain', base64: Buffer.from('An attachment retained in every pasted collection.').toString('base64') };
  const picture = { id: randomUUID(), name: 'Local image.png', mime: 'image/png', base64: 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jKJYAAAAASUVORK5CYII=' };
  fixture.assets.push(attachment, picture);
  collection = addNode(fixture, mainCanvas.id, 'container', 'Collected objects');
  Object.assign(collection, { x: 80, y: 100, width: 440, height: 300, sizeMode: 'fixed', containerVariant: 'contained', childLayout: 'contained', childArrangement: 'free', collapsed: false, placement: 'free', style: { padding: 5, childPadding: 5, fill: '#183c37', border: '#78ddc1' } });
  text = addNode(fixture, mainCanvas.id, 'text', 'Attached passage');
  Object.assign(text, { parentId: collection.id, x: 95, y: 150, width: 190, height: 120, sizeMode: 'fixed', text: 'A copied collection preserves its passage, attachment and internal routes.', attachments: [attachment.id], placement: 'free', style: { fill: '#23463d', textColor: '#d9ece5' } });
  image = addNode(fixture, mainCanvas.id, 'image', 'Embedded picture');
  Object.assign(image, { parentId: collection.id, x: 330, y: 150, width: 175, height: 145, sizeMode: 'fixed', assetId: picture.id, placement: 'free' });
  pivot = addNode(fixture, mainCanvas.id, 'junction', 'Internal pivot');
  Object.assign(pivot, { parentId: collection.id, x: 302, y: 240, width: 12, height: 12, utilityVariant: 'pivot', placement: 'free', style: { shape: 'circle', fill: '#db6b9c', borderWidth: 0 } });
  reference = addNode(fixture, mainCanvas.id, 'reference', 'Internal reference');
  Object.assign(reference, { parentId: collection.id, x: 95, y: 310, width: 190, height: 70, sizeMode: 'fixed', targetId: text.id, placement: 'free' });
  fixture.links.push(...[[text, pivot], [pivot, image]].map(([source, target]) => ({ id: randomUUID(), canvasId: mainCanvas.id, parentId: collection.id, sourceId: source.id, targetId: target.id, kind: 'visual', relation: 'generic', direction: 'none', style: { color: '#db6b9c', width: 4, lineStyle: 'dotted' } })));
  independentText = addNode(fixture, mainCanvas.id, 'text', 'An independent note');
  Object.assign(independentText, { x: 740, y: 100, width: 220, height: 115, sizeMode: 'fixed', placement: 'free', text: 'Copy me nearby with my original content.', style: { shape: 'rounded', fill: '#25364d', titleSize: 17 } });
  independentUtility = addNode(fixture, mainCanvas.id, 'junction', 'An independent joint');
  Object.assign(independentUtility, { x: 740, y: 465, width: 32, height: 6, utilityVariant: 'joint', placement: 'free', style: { shape: 'rectangle', fill: '#e78056', borderWidth: 0 } });
  fixture.links.push({ id: randomUUID(), canvasId: mainCanvas.id, parentId: mainCanvas.rootId, sourceId: independentText.id, targetId: text.id, kind: 'visual', relation: 'generic', direction: 'none' });
  assert.deepEqual(validateProject(fixture), []);
  const created = await json('/api/projects', 'POST', commitSnapshots(fixture)); assert.equal(created.id, fixtureId);
  await page.goto(`${origin}/?project=${fixtureId}&canvas=${mainCanvas.id}`); await expect(page.locator('.workspace')).toBeVisible(); await saved(page);
  await page.getByRole('button', { name: 'Hand', exact: true }).click();

  await tile(page, collection.id).locator('.ms-node-title').click(); await page.keyboard.press('Control+c');
  let before = await stored(); assert.equal(before.nodes.length, fixture.nodes.length, 'Copy should not edit the canvas.');
  for (let count = 0; count < 2; count++) {
    await page.keyboard.press('Control+v');
    await expect.poll(async () => (await stored()).nodes.length).toBe(before.nodes.length + 5); await saved(page);
    const after = await stored(), added = newNodes(before, after), root = added.find(node => node.parentId === mainCanvas.rootId);
    assert.ok(root); assert.equal(added.length, 5); checkCollectionCopy(after, root, fixture); containerCopies.push(root.id);
    assert.equal(after.links.length, before.links.length + 2, 'Only wholly internal links should be copied; the crossing link stays original.');
    assertNoRootOverlap(after, [collection.id, independentText.id, independentUtility.id, ...containerCopies]);
    assert.ok(Math.hypot(root.x - collection.x, root.y - collection.y) < 2000, 'Copy should remain near the source collection.');
    before = after;
  }
  await page.getByRole('button', { name: 'Fit canvas', exact: true }).click(); await screenshot(page, 'package-container-copies.png');
  record('Real Ctrl+C/Ctrl+V copies the selected container, descendants, compact pivot, remapped internal reference, styled links, image and document attachment; repeated pastes have fresh GUIDs and do not overlap nearby roots.');

  await tile(page, independentText.id).locator('.ms-node-title').click(); await page.keyboard.press('Control+c'); before = await stored();
  await page.keyboard.press('Control+v'); await expect.poll(async () => (await stored()).nodes.length).toBe(before.nodes.length + 1); await saved(page);
  let after = await stored(), pastedText = newNodes(before, after)[0]; assert.ok(pastedText); textCopies.push(pastedText.id);
  assert.notEqual(pastedText.id, independentText.id); assert.equal(pastedText.text, independentText.text); assert.deepEqual(pastedText.style, independentText.style);
  assertNoRootOverlap(after, [collection.id, independentText.id, independentUtility.id, ...containerCopies, ...textCopies]);
  await page.keyboard.press('Control+z'); await expect.poll(async () => (await stored()).nodes.length).toBe(before.nodes.length); await saved(page);
  assert.equal(nodeIn(await stored(), pastedText.id), undefined);
  await page.keyboard.press('Control+y'); await expect.poll(async () => (await stored()).nodes.length).toBe(before.nodes.length + 1); await saved(page);
  assert.deepEqual(nodeIn(await stored(), pastedText.id), pastedText);
  record('An ordinary text node pastes as a distinct nearby node with content and appearance intact; undo removes the entire paste and redo restores the same newly assigned GUID.');

  await tile(page, independentUtility.id).click(); await page.keyboard.press('Control+c'); before = await stored();
  await page.keyboard.press('Control+v'); await expect.poll(async () => (await stored()).nodes.length).toBe(before.nodes.length + 1); await saved(page);
  after = await stored(); const pastedUtility = newNodes(before, after)[0]; assert.ok(pastedUtility); utilityCopies.push(pastedUtility.id);
  assert.notEqual(pastedUtility.id, independentUtility.id); assert.equal(pastedUtility.kind, 'junction'); assert.equal(pastedUtility.utilityVariant, 'joint');
  assert.equal(pastedUtility.width, 32); assert.equal(pastedUtility.height, 6); assert.deepEqual(pastedUtility.style, independentUtility.style);
  assertNoRootOverlap(after, [collection.id, independentText.id, independentUtility.id, ...containerCopies, ...textCopies, ...utilityCopies]);
  await page.getByRole('button', { name: 'Fit canvas', exact: true }).click(); await screenshot(page, 'package-text-utility-copies.png');
  record('Utility nodes also copy as independent GUIDs while retaining their sub-type, line shape, compact dimensions and color.');

  await tile(page, independentText.id).locator('.ms-node-title').click(); await panel(page, 'content');
  const title = page.locator('.inspector').getByLabel('Title', { exact: true }); await title.focus();
  before = await stored(); await title.press('Control+a'); await title.press('Control+c'); await title.press('Control+v');
  await expect(title).toHaveValue(independentText.title); await title.press('Tab'); await saved(page);
  after = await stored(); assert.equal(after.nodes.length, before.nodes.length); assert.equal(after.links.length, before.links.length);
  await screenshot(page, 'package-text-field-shortcuts.png');
  record('Ctrl+C and Ctrl+V remain native text editing shortcuts inside an inspector field; they neither copy nor create canvas nodes while typing.');

  finalProject = await stored(); assert.deepEqual(validateProject(finalProject), []);
  const originalNodeIds = ids(fixture); assert.equal(new Set(finalProject.nodes.map(node => node.id)).size, finalProject.nodes.length);
  for (const copyId of [...containerCopies, ...textCopies, ...utilityCopies]) assert.ok(!originalNodeIds.has(copyId));
  const beforeReload = structuredClone(finalProject); await page.reload(); await saved(page); finalProject = await stored(); assert.deepEqual(finalProject, beforeReload);
  for (const copyId of containerCopies) checkCollectionCopy(finalProject, nodeIn(finalProject, copyId), fixture);
  await page.getByRole('button', { name: 'Fit canvas', exact: true }).click(); await screenshot(page, 'package-pasted-graph-reopened.png');
  record('Autosave and reload preserve all pasted identities, descendants, internal links, attached assets, appearance and geometry.');

  const html = buildPublication(finalProject, viewerJS, sceneCSS), offlinePath = path.join(qaRoot, 'package-pasted-graph-offline.html'); await writeFile(offlinePath, html);
  const portable = createPublicationPayload(finalProject).project, imported = readPublication(html);
  assert.deepEqual(portable.links, finalProject.links); assert.equal(imported.nodes.length, finalProject.nodes.length); assert.equal(imported.links.length, finalProject.links.length);
  for (const copyId of containerCopies) {
    const importedRoot = imported.nodes.find(node => node.title === nodeIn(finalProject, copyId).title && node.x === nodeIn(finalProject, copyId).x && node.y === nodeIn(finalProject, copyId).y);
    assert.ok(importedRoot); assert.equal(descendants(imported, importedRoot.id).length, 5);
  }
  const offlineContext = await context({ offline: true }), offline = await offlineContext.newPage(); await offline.goto(pathToFileURL(offlinePath).href);
  for (const id of [...containerCopies, ...textCopies, ...utilityCopies]) await expect(tile(offline, id)).toHaveCount(1);
  const payload = await offline.locator('#meridian-data').textContent(); await tile(offline, textCopies[0]).press('Enter');
  assert.equal(await offline.locator('#meridian-data').textContent(), payload); await screenshot(offline, 'package-pasted-graph-offline.png');
  record('Offline export retains the full pasted graph and local attachments, and published node inspection leaves the immutable payload unchanged.');

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
