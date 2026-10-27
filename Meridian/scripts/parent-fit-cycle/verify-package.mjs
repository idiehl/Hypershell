import assert from 'node:assert/strict';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { createHash, randomUUID } from 'node:crypto';
import { mkdir, readFile, writeFile, readdir, lstat } from 'node:fs/promises';
import { chromium, expect } from '@playwright/test';
import { addCanvas, addNode, commitSnapshots, newProject } from '../../src/core/model.ts';
import { validateProject } from '../../src/core/rules.ts';
import { normalizeContainerLayouts } from '../../src/core/grid.ts';
import { activeNodeContent } from '../../src/core/content.ts';
import { navigationIndex } from '../../src/core/navigation.ts';
import { buildPublication, createPublicationPayload, readPublication } from '../../src/publication.ts';

// Only a fresh fixture may be written on this specifically owned copied-data
// host. Existing projects, library, recoveries, checkpoints and live storage
// remain protected by request guards, complete API baselines and file hashes.
const config = JSON.parse(await readFile(process.argv[2], 'utf8'));
const { origin, qaRoot, dataRoot, packageRoot, sourceRoot } = config;
const workspaceRoot = path.resolve(import.meta.dirname, '../..');
assert.equal(workspaceRoot.toLowerCase(), path.resolve('I:/dev/Meridian').toLowerCase());
assert.equal(new URL(origin).origin, 'http://127.0.0.1:5300');
assert.equal(path.resolve(packageRoot).toLowerCase(), path.resolve(workspaceRoot, 'output/parent-fit-package-20261009').toLowerCase());
assert.equal(path.resolve(qaRoot).toLowerCase(), path.resolve(workspaceRoot, 'output/parent-fit-qa-20261009').toLowerCase());
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
let fixture, finalProject, mainCanvas;
const collections = [];
function makeMedia(canvasId, title, properties = {}) {
  const node = addNode(fixture, canvasId, 'media', title);
  Object.assign(node, { width: 440, height: 180, sizeMode: 'fixed', contentType: 'text', text: `${title} source content`, childPosition: 'auto' }, properties);
  return node;
}
async function dimensions(view, id) {
  return tile(view, id).evaluate(element => {
    const style = element.style;
    return { x: parseFloat(style.left), y: parseFloat(style.top), width: parseFloat(style.width), height: parseFloat(style.height) };
  });
}
async function insideWall(view, parent, children, { overlap = false } = {}) {
  const box = await tile(view, parent.id).boundingBox(), geometry = await dimensions(view, parent.id);
  assert.ok(box); const scale = box.width / geometry.width, inset = ((parent.style?.childPadding ?? 5) + (parent.style?.borderWidth ?? 1)) * scale;
  const header = await tile(view, parent.id).locator(':scope > .ms-node-header').boundingBox(), boxes = [];
  for (const child of children) {
    const frame = await tile(view, child.id).boundingBox(); assert.ok(frame, `Visible ${child.title}`);
    assert.ok(frame.x >= box.x + inset - 1.5, `${child.title}: left wall`);
    assert.ok(frame.y >= header.y + header.height - 1.5, `${child.title}: parent header`);
    assert.ok(frame.x + frame.width <= box.x + box.width - inset + 1.5, `${child.title}: right wall`);
    assert.ok(frame.y + frame.height <= box.y + box.height - inset + 1.5, `${child.title}: bottom wall`); boxes.push(frame);
  }
  if (!overlap) for (let at = 0; at < boxes.length; at++) for (const other of boxes.slice(at + 1)) {
    const frame = boxes[at]; assert.ok(frame.x + frame.width <= other.x + 1.5 || other.x + other.width <= frame.x + 1.5 || frame.y + frame.height <= other.y + 1.5 || other.y + other.height <= frame.y + 1.5, 'Fitted siblings do not overlap.');
  }
}
async function openCanvas(canvasId) {
  await page.goto(`${origin}/?project=${fixtureId}&canvas=${canvasId}`); await expect(page.locator('.workspace')).toBeVisible(); await saved(page);
  await page.getByRole('button', { name: 'Hand', exact: true }).click();
}
async function selectNode(nodeId) { await tile(page, nodeId).locator(':scope > .ms-node-header > .ms-node-title').click(); }
try {
  fixture = newProject('Meridian parent fit · isolated package review'); fixture.id = fixtureId; mainCanvas = fixture.canvases[0];
  mainCanvas.title = 'Manual child walls'; mainCanvas.snapping = { stickyEdges: false, smartAlign: false, snapToGrid: false };
  const freeform = addNode(fixture, mainCanvas.id, 'container', 'Manual parent');
  Object.assign(freeform, { x: 100, y: 100, width: 430, height: 330, sizeMode: 'fixed', collapsed: false, containerVariant: 'contained', childLayout: 'contained', style: { shape: 'rectangle', childPadding: 9, borderWidth: 2 } });
  const child = makeMedia(mainCanvas.id, 'Manual child', { parentId: freeform.id, x: 160, y: 175, width: 150, height: 90, childPosition: 'manual' });
  for (const variant of ['horizontal', 'vertical', 'grid', 'spreadsheet', 'window']) {
    const canvas = addCanvas(fixture, `Fixed ${variant}`); canvas.snapping = { stickyEdges: false, smartAlign: false, snapToGrid: false };
    const parent = addNode(fixture, canvas.id, 'container', `Fixed ${variant}`);
    Object.assign(parent, { x: 100, y: 100, width: 360, height: 260, sizeMode: 'fixed', collapsed: true, containerVariant: variant, childLayout: 'contained',
      style: { shape: 'rectangle', childPadding: 9, borderWidth: 2 }, grid: { columns: 2, rows: variant === 'window' ? 3 : 2, cellWidth: 440, cellHeight: 180, gap: 0, showHeaders: false, fillOrder: 'row', windowColumns: 2, windowRows: 1, scrollDirection: 'vertical' } });
    const children = Array.from({ length: variant === 'window' ? 6 : 4 }, (_, at) => makeMedia(canvas.id, `Child ${at + 1}`, { parentId: parent.id, x: -200 + at * 470, y: 470 }));
    collections.push({ canvas, parent, children, variant });
  }
  const nestedCanvas = addCanvas(fixture, 'Nested parent fit'), outer = addNode(fixture, nestedCanvas.id, 'container', 'Outer');
  Object.assign(outer, { x: 100, y: 100, width: 440, height: 350, sizeMode: 'fixed', collapsed: true, containerVariant: 'contained', childLayout: 'contained' });
  const inner = addNode(fixture, nestedCanvas.id, 'container', 'Inner');
  Object.assign(inner, { parentId: outer.id, width: 720, height: 520, sizeMode: 'fixed', collapsed: false, containerVariant: 'grid', childLayout: 'contained', childPosition: 'auto', grid: { columns: 2, rows: 2, cellWidth: 540, cellHeight: 250, gap: 0, showHeaders: false, fillOrder: 'row' } });
  const leaves = Array.from({ length: 4 }, (_, at) => makeMedia(nestedCanvas.id, `Detail ${at + 1}`, { parentId: inner.id, width: 540, height: 250 }));
  const squareCanvas = addCanvas(fixture, 'Square parent actual edges'), squareParent = addNode(fixture, squareCanvas.id, 'container', 'Square collection');
  Object.assign(squareParent, { x: 100, y: 100, width: 756, height: 502, sizeMode: 'fixed', collapsed: true, containerVariant: 'contained', childLayout: 'contained', style: { shape: 'square', childPadding: 5, borderWidth: 2 } });
  const squareChildren = Array.from({ length: 4 }, (_, at) => makeMedia(squareCanvas.id, `Square item ${at + 1}`, { parentId: squareParent.id, x: 30 + at % 2 * 440, y: 300 + Math.floor(at / 2) * 180, width: 440, height: 120, childPosition: 'manual' }));
  const galleryCanvas = addCanvas(fixture, 'Automatic Gallery remains tight'), gallery = addNode(fixture, galleryCanvas.id, 'container', 'Tight Gallery');
  Object.assign(gallery, { x: 100, y: 100, width: 850, height: 900, sizeMode: 'auto', collapsed: false, containerVariant: 'gallery', childLayout: 'contained' });
  const galleryObjects = ['Short object', 'Second object'].map(title => makeMedia(galleryCanvas.id, title, { parentId: gallery.id, width: 180, height: 100, text: title }));
  const initialIds = fixture.nodes.map(node => node.id).sort(), originalContents = new Map(fixture.nodes.map(node => [node.id, node.text]));
  fixture = await json('/api/projects', 'POST', commitSnapshots(fixture)); assert.deepEqual(validateProject(fixture), []);
  record('Created one independent fixture containing fixed ordered, manual Freeform, nested and automatic Gallery containers; original graph contents remain protected.');
  for (const collection of collections) {
    const { parent, children, canvas, variant } = collection;
    await openCanvas(canvas.id); await selectNode(parent.id); await panel(page, 'relations');
    await page.getByRole('button', { name: 'Toggle contents from relations', exact: true }).click(); await saved(page);
    assert.equal((await dimensions(page, parent.id)).width, parent.width); assert.equal((await dimensions(page, parent.id)).height, parent.height);
    if (variant === 'window') {
      const viewport = tile(page, parent.id).locator('.ms-window-viewport'); await expect(viewport).toBeVisible();
      const surface = await viewport.boundingBox(), wall = await tile(page, parent.id).boundingBox();
      assert.ok(surface.x >= wall.x && surface.y >= wall.y && surface.x + surface.width <= wall.x + wall.width + 1 && surface.y + surface.height <= wall.y + wall.height + 1);
      await viewport.evaluate(element => { element.scrollTop = element.scrollHeight; }); await expect.poll(() => viewport.evaluate(element => element.scrollTop)).toBeGreaterThan(0);
    } else {
      await insideWall(page, parent, children);
      const sizes = await Promise.all(children.map(node => dimensions(page, node.id)));
      assert.ok(sizes[0].width < 440); for (const size of sizes.slice(1)) { assert.equal(size.width, sizes[0].width); assert.equal(size.height, sizes[0].height); }
    }
    assert.equal(nodeIn(await stored(), parent.id).width, parent.width); assert.equal(nodeIn(await stored(), parent.id).height, parent.height);
    await page.reload(); await saved(page); assert.equal((await dimensions(page, parent.id)).width, parent.width); assert.equal((await dimensions(page, parent.id)).height, parent.height);
    if (variant !== 'window') await insideWall(page, parent, children);
    if (variant === 'grid') await screenshot(page, 'package-fixed-grid-fit.png');
  }
  record('Production Horizontal, Vertical, Grid and Spreadsheet shrink equal child tiles to fixed parents; Window keeps its scroll surface inside the fixed parent and retains its full address space.');
  await openCanvas(mainCanvas.id); await selectNode(child.id); await panel(page, 'appearance');
  for (const label of ['Width', 'Height']) { const field = page.locator('.inspector').getByLabel(label, { exact: true }); await field.fill('999'); await field.press('Tab'); await insideWall(page, freeform, [child]); }
  for (const [label, value] of [['Width', '130'], ['Height', '85']]) { const field = page.locator('.inspector').getByLabel(label, { exact: true }); await field.fill(value); await field.press('Tab'); }
  await page.getByRole('button', { name: 'Fit selected branch', exact: true }).click();
  for (const side of ['right', 'bottom', 'left', 'top']) {
    const beforeGeometry = await dimensions(page, child.id);
    const before = await tile(page, child.id).boundingBox(), wall = await tile(page, freeform.id).boundingBox();
    const point = { x: side === 'left' ? before.x + 3 : side === 'right' ? before.x + before.width - 3 : before.x + before.width / 2,
      y: side === 'top' ? before.y + 3 : side === 'bottom' ? before.y + before.height - 3 : before.y + before.height / 2 };
    await page.mouse.move(point.x, point.y); await page.mouse.down();
    await page.mouse.move(side === 'left' ? wall.x - 80 : side === 'right' ? wall.x + wall.width + 80 : point.x,
      side === 'top' ? wall.y - 80 : side === 'bottom' ? wall.y + wall.height + 80 : point.y, { steps: 12 });
    await insideWall(page, freeform, [child]); await page.mouse.up(); await insideWall(page, freeform, [child]);
    const afterGeometry = await dimensions(page, child.id);
    assert.ok((side === 'left' || side === 'right' ? afterGeometry.width : afterGeometry.height) > (side === 'left' || side === 'right' ? beforeGeometry.width : beforeGeometry.height) + .5, `${side} drag actually resizes the child.`);
    await expect.poll(async () => nodeIn(await stored(), child.id).width).toBeCloseTo(afterGeometry.width, 2);
    await expect.poll(async () => nodeIn(await stored(), child.id).height).toBeCloseTo(afterGeometry.height, 2);
  }
  await saved(page); assert.equal(nodeIn(await stored(), freeform.id).width, freeform.width); assert.equal(nodeIn(await stored(), freeform.id).height, freeform.height);
  await screenshot(page, 'package-manual-wall.png'); await page.reload(); await saved(page); await insideWall(page, freeform, [child]);
  record('Appearance Width/Height and pointer resizing at all four Freeform child edges hit the expanded parent wall during the preview and after autosave/reopening.');
  const grid = collections.find(collection => collection.variant === 'grid'); await openCanvas(grid.canvas.id); await selectNode(grid.parent.id); await panel(page, 'appearance');
  for (const [label, value] of [['Width', '240'], ['Height', '200']]) { const field = page.locator('.inspector').getByLabel(label, { exact: true }); await field.fill(value); await field.press('Tab'); }
  await saved(page); await insideWall(page, { ...grid.parent, width: 240, height: 200 }, grid.children);
  assert.equal(nodeIn(await stored(), grid.parent.id).width, 240); assert.equal(nodeIn(await stored(), grid.parent.id).height, 200);
  record('Resizing an existing fixed parent reflows and refits its children without growing the parent or changing graph identities.');
  await openCanvas(nestedCanvas.id); await selectNode(outer.id); await panel(page, 'relations');
  await page.getByRole('button', { name: 'Toggle contents from relations', exact: true }).click(); await saved(page);
  await insideWall(page, outer, [inner]); await insideWall(page, inner, leaves); assert.equal((await dimensions(page, outer.id)).width, outer.width); assert.equal((await dimensions(page, outer.id)).height, outer.height);
  const nestedDimensions = new Map(await Promise.all([outer, inner, ...leaves].map(async node => [node.id, await dimensions(page, node.id)])));
  await screenshot(page, 'package-nested-fit.png'); record('Nested expanded fixed containers fit recursively, keeping both levels inside their owners.');
  await openCanvas(squareCanvas.id); await selectNode(squareParent.id); await panel(page, 'relations');
  await page.getByRole('button', { name: 'Toggle contents from relations', exact: true }).click(); await saved(page); await insideWall(page, squareParent, squareChildren);
  const squareGeometry = await dimensions(page, squareParent.id), squareFrame = await tile(page, squareParent.id).boundingBox(), squareScale = squareFrame.width / squareGeometry.width;
  const squareSide = Math.min(squareGeometry.width, squareGeometry.height), squareLeft = squareFrame.x + (squareGeometry.width - squareSide) / 2 * squareScale, squareTop = squareFrame.y + (squareGeometry.height - squareSide) / 2 * squareScale;
  for (const object of squareChildren) {
    const box = await tile(page, object.id).boundingBox();
    assert.ok(box.x >= squareLeft + 7 * squareScale - 1.5 && box.x + box.width <= squareLeft + (squareSide - 7) * squareScale + 1.5, 'Square actual side walls.');
    assert.ok(box.y >= squareTop + 7 * squareScale - 1.5 && box.y + box.height <= squareTop + (squareSide - 7) * squareScale + 1.5, 'Square actual upper/lower walls.');
  }
  await expect.poll(async () => nodeIn(await stored(), squareParent.id).collapsed).toBe(false);
  assert.equal(nodeIn(await stored(), squareParent.id).width, 756); assert.equal(nodeIn(await stored(), squareParent.id).height, 502);
  await screenshot(page, 'package-square-parent-fit.png'); record('The screenshot case keeps four escaped children inside a square backdrop with 756 by 502 fixed dimensions; the visible shape edges define the walls.');
  await openCanvas(galleryCanvas.id); const galleryView = page.locator(`.ms-gallery[data-gallery-id="${gallery.id}"]`);
  await expect(galleryView.locator('.ms-gallery-item')).toHaveAttribute('data-gallery-item-id', galleryObjects[0].id);
  assert.ok((await dimensions(page, gallery.id)).height < 350); await galleryView.getByRole('button', { name: `Next object in ${gallery.title}`, exact: true }).click();
  await expect(galleryView.locator('.ms-gallery-item')).toHaveAttribute('data-gallery-item-id', galleryObjects[1].id); assert.ok((await dimensions(page, gallery.id)).height < 350);
  record('Automatic Gallery retains its tight single-object frame and paging behavior.');
  finalProject = await stored(); assert.deepEqual(validateProject(finalProject), []); assert.deepEqual(finalProject.nodes.map(node => node.id).sort(), initialIds);
  for (const node of finalProject.nodes) assert.equal(node.text, originalContents.get(node.id), `Content preserved: ${node.id}`);
  const html = buildPublication(finalProject, viewerJS, sceneCSS), offlinePath = path.join(qaRoot, 'package-parent-fit-offline.html'); await writeFile(offlinePath, html);
  assert.equal(readPublication(html).nodes.length, finalProject.nodes.length);
  const offlineContext = await context({ offline: true }), offline = await offlineContext.newPage(); await offline.goto(pathToFileURL(offlinePath).href);
  const payload = await offline.locator('#meridian-data').textContent(); await offline.getByRole('combobox', { name: 'Choose canvas', exact: true }).selectOption(nestedCanvas.id);
  await insideWall(offline, outer, [inner]); await insideWall(offline, inner, leaves);
  for (const [id, geometry] of nestedDimensions) assert.deepEqual(await dimensions(offline, id), geometry);
  await tile(offline, outer.id).locator(':scope > .ms-node-header > .ms-node-title').dblclick(); await expect(tile(offline, inner.id)).toHaveCount(0);
  await tile(offline, outer.id).locator(':scope > .ms-node-header > .ms-node-title').dblclick(); await insideWall(offline, outer, [inner]); await insideWall(offline, inner, leaves);
  assert.equal(await offline.locator('#meridian-data').textContent(), payload); await screenshot(offline, 'package-parent-fit-offline.png');
  record('Portable offline export shares the fitted nested geometry, supports collapse/reveal and leaves the authored source payload immutable.');
  for (const copied of copiedProjects) { assert.deepEqual(await json(`/api/projects/${copied.entry.id}`), copied.document); assert.deepEqual(await json(`/api/projects/${copied.entry.id}/recovery`, 'GET', undefined, true), copied.recovery); }
  assert.deepEqual((await json('/api/projects')).filter(entry => entry.id !== fixtureId), copiedCatalog);
  assert.deepEqual(await json('/api/library'), copiedLibrary); assert.deepEqual(await json('/api/library/recovery', 'GET', undefined, true), copiedLibraryRecovery);
  const copiedAfter = await fileInventory(dataRoot, true), previousNames = new Set(protectedCopiedFiles.map(file => file.relativePath));
  for (const file of protectedCopiedFiles) assert.equal(copiedAfter.find(current => current.relativePath === file.relativePath)?.sha256, file.sha256, `Protected copied file: ${file.relativePath}`);
  const fixturePrefix = path.join('projects', fixtureId) + path.sep;
  for (const file of copiedAfter.filter(current => !previousNames.has(current.relativePath))) assert.ok(file.relativePath.startsWith(fixturePrefix), `Unexpected copied-data file: ${file.relativePath}`);
  assert.deepEqual(await fileInventory(sourceRoot, true), protectedSourceFiles); assert.deepEqual(browserErrors, []); assert.deepEqual(remoteRequests, []); assert.deepEqual(blockedWrites, []);
  const packageHashes = (await fileInventory(path.join(packageRoot, 'app'))).map(({ path, sha256 }) => ({ path, sha256 }));
  await writeFile(path.join(qaRoot, 'package-review.json'), JSON.stringify({ passed: true, origin, isolatedDataRoot: dataRoot, copiedSourceFiles: config.copiedFiles, fixtureProjectId: fixtureId, fixtureCanvasId: mainCanvas.id,
    savedDataUnchanged: true, copiedDataUnchanged: true, protectedSourceFiles, protectedCopiedFiles, protectedEnvelopeSha256: digest(JSON.stringify(protectedEnvelopes)), checks, screenshots, packageHashes, browserErrors, remoteRequests, blockedWrites, fixtureWrites }, null, 2));
  process.stdout.write(JSON.stringify({ checks: checks.length, screenshots, packageFiles: packageHashes.length, browserErrors, remoteRequests, blockedWrites }, null, 2) + '\n');
} catch (error) {
  if (!page.isClosed()) await screenshot(page, 'package-failure.png').catch(() => {});
  const failure = { passed: false, message: error.message, stack: error.stack, checks, screenshots, browserErrors, remoteRequests, blockedWrites, fixtureWrites };
  await writeFile(path.join(qaRoot, 'package-review.json'), JSON.stringify(failure, null, 2)); await writeFile(path.join(qaRoot, 'package-failure.json'), JSON.stringify(failure, null, 2)); throw error;
} finally { for (const current of contexts.reverse()) await current.close(); await browser.close(); }
