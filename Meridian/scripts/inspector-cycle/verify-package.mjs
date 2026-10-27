import assert from 'node:assert/strict';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { createHash } from 'node:crypto';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { chromium, expect } from '@playwright/test';
import { newProject, addNode, commitSnapshots } from '../../src/core/model.ts';
import { nodeShape, shapesOverlap, shapeContact, shapePath } from '../../src/core/geometry.ts';
import { resolveSnapping } from '../../src/core/snapping.ts';
import { gridCellBounds } from '../../src/core/cells.ts';
import { buildPublication, createPublicationPayload, readPublication } from '../../src/publication.ts';

const config = JSON.parse(await readFile(process.argv[2], 'utf8'));
const { origin, qaRoot, dataRoot, packageRoot } = config;
assert.equal(new URL(origin).port, '5293');
assert.equal(path.resolve(packageRoot).toLowerCase(), path.resolve('output/inspector-package').toLowerCase());
assert.ok(path.resolve(dataRoot).toLowerCase().startsWith(path.resolve('.test-data').toLowerCase() + path.sep));
assert.ok(!path.resolve(dataRoot).toLowerCase().startsWith(path.resolve(config.sourceRoot).toLowerCase()));
await mkdir(qaRoot, { recursive: true });
const digest = bytes => createHash('sha256').update(bytes).digest('hex');
const checks = [], screenshots = [], browserErrors = [];
const record = text => { checks.push(text); process.stdout.write(text + '\n'); };
async function json(route, method = 'GET', data) {
  const response = await fetch(origin + route, { method, headers: data === undefined ? {} : { 'Content-Type': 'application/json' }, body: data === undefined ? undefined : JSON.stringify(data) });
  assert.ok(response.ok, `${method} ${route}: ${response.status} ${await response.clone().text()}`);
  return response.json();
}
async function screenshot(page, name) { const filename = path.join(qaRoot, name); await page.screenshot({ path: filename }); screenshots.push(filename); }
const tile = (page, id) => page.locator(`.ms-node[data-node-id="${id}"]`);
const inspector = page => page.locator('.inspector');
async function panel(page, name) {
  if (!(await inspector(page).isVisible())) await page.getByRole('button', { name: 'Toggle side panel', exact: true }).click();
  await page.getByLabel('Side panel', { exact: true }).selectOption(name);
  await expect(page.getByLabel('Side panel', { exact: true })).toHaveValue(name);
  if (name !== 'canvas') await expect(page.getByRole('searchbox')).toHaveCount(0);
}
async function jump(page, id) {
  await panel(page, 'canvas');
  const search = page.getByRole('searchbox', { name: 'Search canvas nodes', exact: true }); await search.fill(id);
  const action = page.locator(`button[data-jump-to-node="${id}"]`); await expect(action).toHaveCount(1); await action.click();
  await expect(tile(page, id)).toHaveAttribute('aria-selected', 'true'); await search.fill('');
}
async function saved(page) { await expect(page.locator('.save-state')).toHaveText(/^Saved · r\d+$/); }
async function editedNode(projectId, id, predicate) {
  await expect.poll(async () => predicate((await json(`/api/projects/${projectId}`)).nodes.find(node => node.id === id))).toBe(true);
  return (await json(`/api/projects/${projectId}`)).nodes.find(node => node.id === id);
}
async function pathClick(page, linkId) {
  const path = page.locator(`.ms-link-hit[data-link-id="${linkId}"]`); await expect(path).toHaveCount(1);
  const point = await path.evaluate(path => {
    const length = path.getTotalLength();
    for (let index = 1; index < 40; index++) {
      const local = path.getPointAtLength(length * index / 40), screen = new DOMPoint(local.x, local.y).matrixTransform(path.getScreenCTM());
      const element = document.elementFromPoint(screen.x, screen.y);
      if (element?.closest('.ms-link-hit')?.getAttribute('data-link-id') === path.getAttribute('data-link-id')) return { x: screen.x, y: screen.y };
    }
    return null;
  });
  assert.ok(point, `The exterior route must have a clickable unobstructed stroke: ${linkId}`);
  await page.mouse.click(point.x, point.y);
}

const originalLibraryBytes = await readFile(path.join(dataRoot, 'library.json'));
const originalLibrary = JSON.parse(originalLibraryBytes);
const catalog = (await json('/api/projects')).sort((a, b) => Number(/Visual atlas/i.test(b.title)) - Number(/Visual atlas/i.test(a.title)));
let mythology, collection, zeus;
for (const candidate of catalog) {
  const project = await json(`/api/projects/${candidate.id}`), found = project.nodes.find(node => node.id === '005fe68a-bc30-4b11-af1d-4dadec799b1c');
  if (found) { mythology = project; collection = candidate; zeus = found; break; }
}
assert.ok(zeus, 'The copied actual collection must contain the requested Zeus GUID.');
const zeusSelfLinks = mythology.links.filter(link => link.sourceId === zeus.id && link.targetId === zeus.id);
assert.equal(zeusSelfLinks.length, 2, 'The reviewed Zeus node should retain the two legacy self relationships.');
const originalImage = mythology.assets.find(asset => asset.id === zeus.assetId);
assert.ok(originalImage?.mime.startsWith('image/'), 'The actual Zeus attachment must remain available.');

const browser = await chromium.launch();
const context = await browser.newContext({ viewport: { width: 1700, height: 1080 } });
context.on('page', page => page.on('pageerror', error => browserErrors.push(error.message)));
const page = await context.newPage();
let offlineContext;
try {
  await page.goto(origin + '/'); await expect(page.getByRole('button', { name: 'New Node', exact: true })).toBeVisible(); await saved(page);
  const home = await json('/api/library'); assert.equal(home.schemaVersion, 2);
  const previousNodes = originalLibrary.schemaVersion === 2 ? originalLibrary.project.nodes : originalLibrary.entries;
  for (const previous of previousNodes) {
    const current = home.project.nodes.find(node => node.id === previous.id); assert.ok(current, `Preserved home identity ${previous.id}`);
    assert.equal(current.title, previous.title);
    if (originalLibrary.schemaVersion === 2) assert.equal(current.parentId, previous.parentId);
    if (previous.kind === 'canvas' && originalLibrary.schemaVersion === 1) assert.equal(current.canvasTarget.projectId, previous.projectId);
  }
  await expect(page.getByRole('button', { name: /New section|New subsection|New canvas|Node settings/i })).toHaveCount(0);
  await expect(page.locator('.workspace-chevron,.sidebar,.canvas-list')).toHaveCount(0);
  await panel(page, 'canvas'); await page.getByRole('button', { name: 'Fit canvas', exact: true }).click();
  await screenshot(page, 'package-home-desktop.png');
  record('The actual home retains existing node identities and hierarchy and uses the unified inspector, quiet plus button and shared canvas controls.');

  await page.goto(`${origin}/?project=${mythology.id}&canvas=${zeus.canvasId}`); await jump(page, zeus.id); await panel(page, 'links');
  for (const link of zeusSelfLinks) await expect(page.locator(`[data-associated-link="${link.id}"]`)).toContainText('source · destination');
  if (zeus.collapsed) { await tile(page, zeus.id).locator('.ms-node-title').dblclick(); await saved(page); }
  for (const link of zeusSelfLinks) {
    await expect(page.locator(`.ms-link-line[data-link-id="${link.id}"]`)).toHaveCount(1);
    await pathClick(page, link.id); await expect(inspector(page).getByLabel('Connection label', { exact: true })).toBeVisible();
    await inspector(page).getByRole('button', { name: 'Back to associated links', exact: true }).click();
  }
  await expect(tile(page, zeus.id)).not.toContainText(/2 links|hidden connections|⋱/i);
  await screenshot(page, 'package-zeus-links-desktop.png');
  const beforeDelete = await json(`/api/projects/${mythology.id}`);
  await pathClick(page, zeusSelfLinks[0].id); await inspector(page).getByRole('button', { name: 'Remove connection', exact: true }).click();
  await expect.poll(async () => (await json(`/api/projects/${mythology.id}`)).links.some(link => link.id === zeusSelfLinks[0].id)).toBe(false);
  assert.ok((await json(`/api/projects/${mythology.id}`)).links.some(link => link.id === zeusSelfLinks[1].id));
  await page.getByRole('button', { name: 'Undo', exact: true }).click(); await saved(page);
  const undone = await json(`/api/projects/${mythology.id}`); assert.deepEqual(undone.links, beforeDelete.links); assert.deepEqual(undone.nodes, beforeDelete.nodes);
  await jump(page, zeus.id); await panel(page, 'content');
  await expect(inspector(page).getByLabel('Title', { exact: true })).toHaveValue(zeus.title);
  await screenshot(page, 'package-zeus-content-desktop.png');
  const opened = page.waitForEvent('popup'); await tile(page, zeus.id).locator('img').click({ modifiers: ['Control'] });
  const imageWindow = await opened; await expect(imageWindow.getByRole('main', { name: 'Image viewer' })).toBeVisible();
  await expect.poll(() => imageWindow.locator('.media-window-stage img').evaluate(image => image.complete && image.naturalWidth > 0)).toBe(true);
  await screenshot(imageWindow, 'package-zeus-media-window.png'); await imageWindow.close();
  record('Both actual Zeus self-links render as independently selectable exterior loops, appear in Links, and support deletion plus Undo in the isolated copy. The image and Ctrl-click media window remain intact.');

  const fixture = newProject('Production inspector review');
  const source = addNode(fixture, fixture.entryCanvasId, 'container', 'A connected source collection');
  Object.assign(source, { x: 90, y: 100, width: 420, height: 240, text: 'Hierarchy, appearance and content remain independent. This collection owns an image child and a conceptual source relationship.', childLayout: 'spatial', collapsed: false, style: { shape: 'rectangle', fill: '#183A35', border: '#78DDC1', titleSize: 24, fontSize: 17 } });
  const mover = addNode(fixture, fixture.entryCanvasId, 'text', 'A sticky neighboring tile');
  Object.assign(mover, { x: 710, y: 100, width: 190, height: 150, text: 'Drag this tile toward the shaped collection.', style: { shape: 'rectangle' } });
  const media = addNode(fixture, fixture.entryCanvasId, 'image', 'An image inside the collection');
  const imageId = crypto.randomUUID(); fixture.assets.push({ ...originalImage, id: imageId });
  Object.assign(media, { parentId: source.id, x: 100, y: 470, width: 240, height: 330, assetId: imageId, text: 'The original image bytes are preserved.', placement: 'free' });
  const sheet = addNode(fixture, fixture.entryCanvasId, 'spreadsheet', 'An addressable source grid');
  Object.assign(sheet, { x: 680, y: 450, width: 520, height: 250, containerVariant: 'spreadsheet', grid: { columns: 2, rows: 1, cellWidth: 220, cellHeight: 130, gap: 8, showHeaders: true } });
  const cell = addNode(fixture, fixture.entryCanvasId, 'document', 'A1 source passage');
  Object.assign(cell, { parentId: sheet.id, ...gridCellBounds(sheet, { column: 1, row: 1 }), cell: { column: 1, row: 1, name: 'source_passage' }, text: 'Addressable contents keep their original identities.' });
  const visual = { id: crypto.randomUUID(), canvasId: fixture.entryCanvasId, sourceId: source.id, targetId: mover.id, parentId: fixture.canvases[0].rootId, kind: 'visual', relation: 'generic', direction: 'none', label: 'Related reading' };
  fixture.links.push(visual); assert.equal(resolveSnapping(fixture.canvases[0], mover).stickyEdges, true);
  await json('/api/projects', 'POST', commitSnapshots(fixture)); await page.goto(`${origin}/?project=${fixture.id}`);
  await jump(page, source.id); await panel(page, 'properties');
  await expect(inspector(page).getByLabel('Type', { exact: true })).toHaveValue('container');
  await expect(inspector(page).getByLabel('Title', { exact: true })).toHaveCount(0);
  const customGuid = 'production_source_collection';
  await inspector(page).getByLabel('GUID', { exact: true }).fill(customGuid); await page.getByRole('button', { name: 'Apply GUID', exact: true }).click();
  await editedNode(fixture.id, customGuid, node => !!node); await saved(page);
  let current = await json(`/api/projects/${fixture.id}`);
  assert.equal(current.nodes.find(node => node.id === media.id).parentId, customGuid); assert.equal(current.links[0].sourceId, customGuid);
  assert.ok(!Object.hasOwn(current.snapshots, source.id));
  await inspector(page).getByLabel('Alignment behavior', { exact: true }).selectOption('sticky');
  await editedNode(fixture.id, customGuid, node => node?.snapping?.stickyEdges === true); await saved(page);
  await screenshot(page, 'package-properties-desktop.png');
  record('Properties is limited to type, subtype, alignment and GUID. Applying a custom GUID atomically preserves child ownership, link endpoints and saved observations; Sticky remains the default.');

  await panel(page, 'appearance'); await inspector(page).getByLabel('Shape', { exact: true }).selectOption('hexagon');
  await editedNode(fixture.id, customGuid, node => node?.style?.shape === 'hexagon'); await saved(page);
  const shaped = (await json(`/api/projects/${fixture.id}`)).nodes.find(node => node.id === customGuid);
  await expect(tile(page, customGuid)).toHaveAttribute('data-shape', 'hexagon');
  const outline = tile(page, customGuid).locator('.ms-node-outline path');
  assert.equal(await outline.getAttribute('d'), shapePath(nodeShape(shaped, { x: 0, y: 0, width: shaped.width, height: shaped.height })));
  await expect(page.locator('.ms-resize').filter({ hasText: '⋱' })).toHaveCount(0);
  await screenshot(page, 'package-appearance-desktop.png');

  await panel(page, 'content');
  const supplementalBytes = Buffer.from([0, 1, 2, 255, 13, 10, 65, 66, 67]);
  await inspector(page).getByLabel('Add attachment', { exact: true }).setInputFiles({ name: 'Exact source bytes.txt', mimeType: 'text/plain', buffer: supplementalBytes });
  await editedNode(fixture.id, customGuid, node => node?.attachments?.length === 1); await saved(page);
  await inspector(page).getByLabel('Hyperlink URL', { exact: true }).fill('https://example.test/source?topic=Zeus');
  await inspector(page).getByLabel('Hyperlink label', { exact: true }).fill('A source hyperlink'); await inspector(page).getByRole('button', { name: 'Add hyperlink', exact: true }).click();
  await editedNode(fixture.id, customGuid, node => node?.hyperlinks?.length === 1); await saved(page);
  await expect(inspector(page).getByLabel('Text body', { exact: true })).toHaveValue(source.text);
  await screenshot(page, 'package-content-desktop.png');
  await panel(page, 'relations');
  await expect(inspector(page).getByRole('button', { name: 'Inspect descendant An image inside the collection', exact: true })).toBeVisible();
  await expect(inspector(page)).toContainText('Inherited from ancestors'); await screenshot(page, 'package-relations-desktop.png');
  await jump(page, sheet.id); await panel(page, 'relations');
  await expect(inspector(page).getByLabel('Columns', { exact: true })).toHaveValue('2');
  await expect(inspector(page).getByLabel('Rows', { exact: true })).toHaveValue('1');
  await expect(inspector(page)).toContainText('A1 source passage'); await screenshot(page, 'package-grid-relations-desktop.png');
  record('Appearance follows the real hexagon boundary. Content stores byte-exact supplemental attachments and an inert hyperlink. Relations shows descendants, inherited behavior and grid dimensions without a search field.');

  await jump(page, customGuid); await panel(page, 'links'); const linesBefore = await page.locator('.ms-link-line').count();
  await inspector(page).getByLabel('Target GUID', { exact: true }).fill('remotely_saved_greek_source');
  await page.locator('.inspector-remote-form').getByLabel('Relationship', { exact: true }).selectOption('causal');
  await page.locator('.inspector-remote-form').getByLabel('Label', { exact: true }).fill('Remote conceptual source');
  await page.getByRole('button', { name: 'Add remote link', exact: true }).click();
  await editedNode(fixture.id, customGuid, node => node?.remoteLinks?.length === 1); await saved(page);
  assert.equal(await page.locator('.ms-link-line').count(), linesBefore);
  await expect(page.locator('.inspector-remote-row')).toContainText('External GUID');
  await screenshot(page, 'package-remote-links-desktop.png');

  await page.getByRole('button', { name: 'Fit canvas', exact: true }).click();
  const titleBox = await tile(page, mover.id).locator('.ms-node-title').boundingBox(); assert.ok(titleBox);
  current = await json(`/api/projects/${fixture.id}`);
  const previousMover = current.nodes.find(node => node.id === mover.id), targetSource = current.nodes.find(node => node.id === customGuid);
  const scale = await page.locator('.ms-world').evaluate(node => new DOMMatrix(getComputedStyle(node).transform).a);
  const desired = { x: targetSource.x + targetSource.width / 2 - previousMover.width / 2, y: targetSource.y + targetSource.height / 2 - previousMover.height / 2 };
  const start = { x: titleBox.x + titleBox.width / 2, y: titleBox.y + titleBox.height / 2 };
  await page.mouse.move(start.x, start.y); await page.mouse.down();
  await page.mouse.move(start.x + (desired.x - previousMover.x) * scale, start.y + (desired.y - previousMover.y) * scale, { steps: 10 }); await page.mouse.up();
  await editedNode(fixture.id, mover.id, node => node && (node.x !== previousMover.x || node.y !== previousMover.y)); await saved(page);
  current = await json(`/api/projects/${fixture.id}`);
  const placedMover = current.nodes.find(node => node.id === mover.id), placedSource = current.nodes.find(node => node.id === customGuid);
  assert.equal(shapesOverlap(nodeShape(placedMover), nodeShape(placedSource)), false);
  const contact = shapeContact(nodeShape(placedMover), nodeShape(placedSource), 1);
  assert.ok(contact && Math.hypot(contact.x, contact.y) < .05, 'The collided rectangle should settle against the real shaped edge.');
  await jump(page, customGuid); await panel(page, 'appearance'); await screenshot(page, 'package-shaped-contact-desktop.png');
  record('Dragging a default-sticky tile into the shaped collection resolves to genuine backdrop contact without overlap and saves the resulting geometry.');

  await page.reload(); await jump(page, customGuid); await panel(page, 'links');
  await expect(page.locator('.inspector-remote-row')).toContainText('Remote conceptual source');
  await panel(page, 'content'); await expect(inspector(page).getByRole('link', { name: 'A source hyperlink', exact: true })).toHaveAttribute('href', 'https://example.test/source?topic=Zeus');
  const persisted = await json(`/api/projects/${fixture.id}`), persistedSource = persisted.nodes.find(node => node.id === customGuid);
  const supplemental = persisted.assets.find(asset => asset.id === persistedSource.attachments[0]);
  assert.equal(supplemental.base64, supplementalBytes.toString('base64')); assert.equal(persistedSource.style.shape, 'hexagon');
  assert.equal(persisted.nodes.find(node => node.id === media.id).parentId, customGuid);
  await panel(page, 'canvas'); await screenshot(page, 'package-canvas-outline-desktop.png');
  await page.setViewportSize({ width: 700, height: 880 }); await screenshot(page, 'package-canvas-outline-compact.png');
  await panel(page, 'content'); await screenshot(page, 'package-content-compact.png');

  const exported = await fetch(origin + '/api/export', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(persisted) }); assert.equal(exported.status, 200);
  const nativeBytes = Buffer.from(await exported.arrayBuffer()); await writeFile(path.join(qaRoot, 'inspector-review.meridian'), nativeBytes);
  const nativeForm = new FormData(); nativeForm.append('file', new Blob([nativeBytes]), 'inspector-review.meridian');
  const imported = await fetch(origin + '/api/import', { method: 'POST', body: nativeForm }); assert.equal(imported.status, 200); assert.deepEqual(await imported.json(), persisted);
  const payload = createPublicationPayload(persisted); assert.deepEqual(payload.missing, []);
  assert.equal(payload.project.assets.find(asset => asset.id === supplemental.id).base64, supplemental.base64);
  const viewerJs = await readFile(path.join(packageRoot, 'app/wwwroot/viewer.js'), 'utf8'), css = await readFile(path.join(packageRoot, 'app/wwwroot/scene.css'), 'utf8');
  const html = buildPublication(persisted, viewerJs, css), publicationFile = path.join(qaRoot, 'inspector-review.html'); await writeFile(publicationFile, html);
  const publicationCopy = readPublication(html), copiedSource = publicationCopy.nodes.find(node => node.title === source.title);
  assert.notEqual(copiedSource.id, customGuid); assert.equal(copiedSource.remoteLinks[0].targetId, 'remotely_saved_greek_source');
  assert.equal(publicationCopy.assets.find(asset => asset.id === copiedSource.attachments[0]).base64, supplemental.base64); assert.deepEqual(copiedSource.hyperlinks, persistedSource.hyperlinks);
  offlineContext = await browser.newContext({ viewport: { width: 1500, height: 950 }, offline: true });
  const offline = await offlineContext.newPage(), remoteRequests = [];
  offline.on('pageerror', error => browserErrors.push(error.message)); offline.on('request', request => { if (/^https?:/.test(request.url())) remoteRequests.push(request.url()); });
  await offline.goto(pathToFileURL(publicationFile).href); await expect(tile(offline, customGuid)).toHaveAttribute('data-shape', 'hexagon');
  assert.deepEqual(remoteRequests, []); await screenshot(offline, 'package-publication-offline.png');
  record('Save/reload and native export/import preserve custom GUIDs, conceptual links, hierarchy, grid addresses, shape/contact geometry, attachments and hyperlinks. The standalone publication renders offline and independent import retains exact attachment bytes.');

  assert.deepEqual(browserErrors, []);
  const packageHashes = [];
  for (const relative of ['app/Meridian.Host.exe', 'app/Meridian.Host.dll', 'app/wwwroot/editor.js', 'app/wwwroot/viewer.js', 'app/wwwroot/app.css', 'app/wwwroot/scene.css']) {
    const filename = path.join(packageRoot, relative); packageHashes.push({ path: filename, sha256: digest(await readFile(filename)) });
  }
  await writeFile(path.join(qaRoot, 'package-review.json'), JSON.stringify({ passed: true, origin, isolatedDataRoot: dataRoot, copiedSourceFiles: config.copiedFiles,
    sourceCollection: { id: collection.id, title: collection.title, revision: mythology.revision, nodeId: zeus.id, selfLinkIds: zeusSelfLinks.map(link => link.id) },
    fixtureProjectId: fixture.id, customNodeGuid: customGuid, checks, screenshots, packageHashes, browserErrors }, null, 2));
  process.stdout.write(JSON.stringify({ checks: checks.length, screenshots, browserErrors }, null, 2) + '\n');
} catch (error) {
  if (!page.isClosed()) await screenshot(page, 'package-failure.png').catch(() => {});
  const failure = { passed: false, message: error.message, stack: error.stack, checks, screenshots, browserErrors };
  await writeFile(path.join(qaRoot, 'package-review.json'), JSON.stringify(failure, null, 2));
  await writeFile(path.join(qaRoot, 'package-failure.json'), JSON.stringify(failure, null, 2)); throw error;
} finally { await offlineContext?.close(); await context.close(); await browser.close(); }
