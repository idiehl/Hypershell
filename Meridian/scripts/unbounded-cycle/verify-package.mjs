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
assert.equal(path.resolve(packageRoot).toLowerCase(), path.resolve(workspaceRoot, 'output/unbounded-package-20261009').toLowerCase());
assert.equal(path.resolve(qaRoot).toLowerCase(), path.resolve(workspaceRoot, 'output/unbounded-qa-20261009').toLowerCase());
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
const patterns = [], patternNames = ['contained', 'horizontal', 'vertical', 'grid', 'spreadsheet', 'window', 'gallery'];
const picture = { id: randomUUID(), name: 'Local review image.png', mime: 'image/png', base64: 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jKJYAAAAASUVORK5CYII=' };
let receiver, draggable, editable, radialParent, radialChildren, manualChild;
const makeMedia = (canvasId, title, properties = {}) => {
  const node = addNode(fixture, canvasId, 'media', title);
  Object.assign(node, { contentType: 'text', text: title, width: 150, height: 90, sizeMode: 'fixed', childLayout: 'spatial', placement: 'free' }, properties);
  return node;
};
const directContentImage = (page, nodeId) => tile(page, nodeId).locator(':scope > .ms-node-body > .ms-image');
async function openCanvas(canvasId) {
  await page.goto(`${origin}/?project=${fixtureId}&canvas=${canvasId}`); await expect(page.locator('.workspace')).toBeVisible(); await saved(page);
  await page.getByRole('button', { name: 'Hand', exact: true }).click();
  await page.getByRole('button', { name: 'Fit canvas', exact: true }).click();
}
async function selectNode(nodeId) {
  await tile(page, nodeId).locator(':scope > .ms-node-header > .ms-node-title').click();
}
try {
  fixture = newProject('Unbounded nodes and Freeform nesting · isolated package review'); fixture.id = fixtureId; mainCanvas = fixture.canvases[0];
  fixture.assets.push(picture);
  mainCanvas.title = 'Drag to nest and single content';
  mainCanvas.snapping = { stickyEdges: false, smartAlign: false, snapToGrid: false };
  receiver = addNode(fixture, mainCanvas.id, 'container', 'Receive a child');
  Object.assign(receiver, { x: 90, y: 220, width: 300, height: 220, sizeMode: 'fixed', collapsed: true, contentType: 'text', text: 'Release a dragged node over this tile.', containerVariant: 'contained', childLayout: 'contained' });
  draggable = makeMedia(mainCanvas.id, 'Drag this note', { x: 600, y: 220, width: 170, height: 90 });
  editable = makeMedia(mainCanvas.id, 'One chosen content item', { x: 400, y: 70, width: 210, height: 180, contentType: 'asset', assetId: picture.id, text: 'An inactive legacy caption', hyperlinks: [{ url: 'https://example.org/source', label: 'A retained source' }] });
  for (const variant of patternNames) {
    const canvas = addCanvas(fixture, `Bounded and unbounded ${variant === 'contained' ? 'Freeform' : variant}`);
    canvas.snapping = { stickyEdges: false, smartAlign: false, snapToGrid: false };
    const bounded = addNode(fixture, canvas.id, 'container', `Bounded ${variant}`);
    Object.assign(bounded, { x: 70, y: 100, width: 520, height: 380, sizeMode: 'auto', collapsed: false, containerVariant: variant, childLayout: 'contained', contentType: 'asset', assetId: picture.id, text: 'An inactive cover caption', childArrangement: variant === 'contained' ? 'free' : variant === 'horizontal' ? 'row' : variant === 'vertical' ? 'column' : 'grid' });
    const unbounded = makeMedia(canvas.id, `Unbounded ${variant}`, { x: 900, y: 100, width: 200, height: 180, containerVariant: variant, collapsed: false, contentType: 'asset', assetId: picture.id, text: 'An inactive legacy caption' });
    if (['horizontal', 'vertical', 'grid', 'spreadsheet', 'window'].includes(variant)) {
      const grid = { columns: 3, rows: 3, cellWidth: 150, cellHeight: 90, gap: 0, showHeaders: false, fillOrder: 'row', ...(variant === 'window' ? { columns: 2, rows: 4, windowColumns: 2, windowRows: 1, scrollDirection: 'vertical' } : {}) };
      bounded.grid = structuredClone(grid); unbounded.grid = structuredClone(grid);
    }
    const childCount = variant === 'window' ? 6 : 3, boundedChildren = [], unboundedChildren = [];
    for (let at = 0; at < childCount; at++) {
      boundedChildren.push(makeMedia(canvas.id, `Bounded item ${at + 1} · ${variant}`, { parentId: bounded.id, x: 100, y: 100, childPosition: 'auto' }));
      unboundedChildren.push(makeMedia(canvas.id, `Unbounded item ${at + 1} · ${variant}`, { parentId: unbounded.id, x: 100, y: 100, childPosition: 'auto' }));
    }
    fixture.links.push({ id: randomUUID(), canvasId: canvas.id, parentId: bounded.id, sourceId: bounded.id, targetId: boundedChildren[0].id, kind: 'visual', relation: 'generic', direction: 'none' });
    patterns.push({ canvas, variant, bounded, unbounded, boundedChildren, unboundedChildren });
  }
  const radialCanvas = addCanvas(fixture, 'Freeform rings and a stored manual position');
  radialCanvas.snapping = { stickyEdges: false, smartAlign: false, snapToGrid: false };
  radialParent = makeMedia(radialCanvas.id, 'A balanced Freeform parent', { x: 700, y: 600, width: 160, height: 110, containerVariant: 'contained', collapsed: false });
  radialChildren = Array.from({ length: 13 }, (_, at) => makeMedia(radialCanvas.id, `Child ${at + 1}`, { parentId: radialParent.id, width: 90, height: 60, childPosition: 'auto', text: 'Detail' }));
  manualChild = makeMedia(radialCanvas.id, 'Manually arranged', { parentId: radialParent.id, x: 20, y: 20, width: 160, height: 75, childPosition: 'manual' });
  fixture = normalizeContainerLayouts(fixture);
  assert.deepEqual(validateProject(fixture), []);
  const created = await json('/api/projects', 'POST', commitSnapshots(fixture)); assert.equal(created.id, fixtureId);
  record('Prepared a fresh independent fixture with bounded and unbounded versions of every display pattern, radial rings, one manual child and a drag receiver.');

  for (const entry of patterns) {
    await openCanvas(entry.canvas.id);
    await expect(directContentImage(page, entry.bounded.id)).toHaveCount(0);
    await expect(directContentImage(page, entry.unbounded.id)).toHaveCount(1);
    if (entry.variant === 'gallery') {
      await expect(tile(page, entry.unboundedChildren[0].id)).toHaveCount(1);
      await expect(tile(page, entry.unboundedChildren[1].id)).toHaveCount(0);
      const navigation = page.locator(`[data-external-gallery="${entry.unbounded.id}"]`);
      await navigation.getByRole('button', { name: `Next object in ${entry.unbounded.title}`, exact: true }).click();
      await expect(tile(page, entry.unboundedChildren[1].id)).toHaveCount(1);
      await expect(tile(page, entry.unboundedChildren[0].id)).toHaveCount(0);
    } else if (entry.variant === 'window') {
      const surface = page.locator(`.ms-window-viewport[data-window-id="${entry.unbounded.id}"]`);
      await expect(surface).toHaveCount(1);
      const scroll = await surface.evaluate(element => ({ height: element.clientHeight, total: element.scrollHeight }));
      assert.ok(scroll.total > scroll.height, 'An unbounded window should clip a scrollable external child workspace.');
      await surface.evaluate(element => { element.scrollTop = element.scrollHeight; element.dispatchEvent(new Event('scroll')); });
      await expect.poll(() => surface.evaluate(element => element.scrollTop)).toBeGreaterThan(0);
    } else {
      for (const child of entry.unboundedChildren) await expect(tile(page, child.id)).toHaveCount(1);
    }
    const current = await stored(), navigation = navigationIndex(current), body = navigation.bounds(nodeIn(current, entry.unbounded.id));
    for (const child of current.nodes.filter(node => node.parentId === entry.unbounded.id)) {
      const bounds = navigation.bounds(child);
      assert.ok(body.x + body.width <= bounds.x + .01 || bounds.x + bounds.width <= body.x + .01 || body.y + body.height <= bounds.y + .01 || bounds.y + bounds.height <= body.y + .01, `Unbounded ${entry.variant} child overlaps the parent body.`);
    }
    for (const link of current.links.filter(link => link.canvasId === entry.canvas.id)) await expect(page.locator(`.ms-viewport [data-link-id="${link.id}"]`)).toHaveCount(0);
    if (['horizontal', 'vertical', 'grid', 'spreadsheet', 'window'].includes(entry.variant)) {
      const children = current.nodes.filter(node => node.parentId === entry.unbounded.id);
      assert.equal(new Set(children.map(child => `${child.width}:${child.height}`)).size, 1, 'Ordered children have uniform dimensions.');
      assert.equal(new Set(children.map(child => `${child.cell.column}:${child.cell.row}`)).size, children.length, 'Ordered child coordinates are unique.');
    }
    await screenshot(page, `package-pattern-${entry.variant}.png`);
    await tile(page, entry.bounded.id).locator(':scope > .ms-node-header > .ms-node-title').dblclick(); await saved(page);
    await expect(directContentImage(page, entry.bounded.id)).toHaveCount(1);
    for (const child of entry.boundedChildren) await expect(tile(page, child.id)).toHaveCount(0);
    await tile(page, entry.bounded.id).locator(':scope > .ms-node-header > .ms-node-title').dblclick(); await saved(page);
    await expect(directContentImage(page, entry.bounded.id)).toHaveCount(0);
  }
  record('Each actual packaged canvas uses the same bounded/unbounded pattern; bounded parents hide their own cover while expanded, ordered tiles remain uniform and addressable, galleries show one external child and windows scroll an external clipped workspace.');

  await openCanvas(radialCanvas.id);
  const radial = await stored(), center = { x: radialParent.x + radialParent.width / 2, y: radialParent.y + radialParent.height / 2 };
  const relative = id => { const node = nodeIn(radial, id); return { x: node.x + node.width / 2 - center.x, y: node.y + node.height / 2 - center.y }; };
  const first = relative(radialChildren[0].id), second = relative(radialChildren[1].id), third = relative(radialChildren[2].id), outer = relative(radialChildren[12].id);
  assert.ok(first.x > 0 && Math.abs(first.y) < 1);
  assert.ok(second.x > 0 && second.y > 0);
  assert.ok(third.x > 0 && third.y < 0);
  assert.ok(Math.hypot(outer.x, outer.y) > Math.hypot(first.x, first.y) + 1);
  assert.equal(nodeIn(radial, manualChild.id).x, 20); assert.equal(nodeIn(radial, manualChild.id).y, 20);
  await screenshot(page, 'package-freeform-rings.png');
  await tile(page, radialParent.id).locator(':scope > .ms-node-header > .ms-node-title').dblclick(); await saved(page);
  for (const child of [...radialChildren, manualChild]) await expect(tile(page, child.id)).toHaveCount(0);
  await tile(page, radialParent.id).locator(':scope > .ms-node-header > .ms-node-title').dblclick(); await saved(page);
  const radialAgain = await stored();
  for (const child of radialChildren) {
    const before = nodeIn(radial, child.id), after = nodeIn(radialAgain, child.id);
    // Trigonometric coordinates can differ by one floating-point bit after a
    // whole-branch translation; subpixel tolerance still rejects any relocation.
    assert.ok(Math.abs(after.x - before.x) < 1e-7 && Math.abs(after.y - before.y) < 1e-7, 'An automatic child changed its visual ring position.');
  }
  assert.equal(nodeIn(radialAgain, manualChild.id).x, nodeIn(radial, manualChild.id).x);
  assert.equal(nodeIn(radialAgain, manualChild.id).y, nodeIn(radial, manualChild.id).y);
  record('Freeform auto placement follows 3, 4, 2 o’clock, then balanced alternating positions; a thirteenth child occupies a further ring, and manual placement survives collapse/reveal.');

  await openCanvas(mainCanvas.id);
  await selectNode(draggable.id);
  const from = await tile(page, draggable.id).locator(':scope > .ms-node-header > .ms-node-title').boundingBox(), to = await tile(page, receiver.id).boundingBox();
  assert.ok(from && to);
  await page.mouse.move(from.x + from.width / 2, from.y + from.height / 2); await page.mouse.down();
  await page.mouse.move(to.x + to.width / 2, to.y + to.height / 2, { steps: 12 });
  await expect(page.locator(`.ms-nesting-hint[data-drop-parent="${receiver.id}"]`)).toBeVisible();
  await expect(tile(page, receiver.id)).toHaveClass(/ms-drop-receiver/);
  await screenshot(page, 'package-drag-to-nest.png'); await page.mouse.up();
  await expect.poll(async () => nodeIn(await stored(), draggable.id).parentId).toBe(receiver.id); await saved(page);
  const nestedProject = await stored(), nested = nodeIn(nestedProject, draggable.id); assert.equal(nested.childPosition, 'auto');
  assert.equal(nodeIn(nestedProject, receiver.id).collapsed, false);
  assert.equal(nested.id, draggable.id);
  await expect(tile(page, draggable.id)).toHaveCount(1);
  await page.keyboard.press('Control+z');
  await expect.poll(async () => nodeIn(await stored(), draggable.id).parentId).toBe(mainCanvas.rootId); await saved(page);
  await page.keyboard.press('Control+y');
  await expect.poll(async () => nodeIn(await stored(), draggable.id).parentId).toBe(receiver.id); await saved(page);
  record('A real pointer drag highlights the receiving border and shows the cursor nesting symbol; dropping nests the complete branch as one undoable edit and preserves its GUID.');

  await selectNode(editable.id); await panel(page, 'content');
  await expect(directContentImage(page, editable.id)).toHaveCount(1); await expect(tile(page, editable.id).locator('.ms-text')).toHaveCount(0);
  const inspector = page.locator('.inspector'); await inspector.getByLabel('Content', { exact: true }).selectOption('text'); await saved(page);
  const body = inspector.getByLabel('Text body', { exact: true }); await body.fill('One explicit text item replaces the previous active media.'); await body.press('Tab'); await saved(page);
  const textChoice = nodeIn(await stored(), editable.id); assert.equal(textChoice.contentType, 'text'); assert.equal(textChoice.assetId, undefined); assert.equal(textChoice.hyperlinks, undefined);
  await expect(directContentImage(page, editable.id)).toHaveCount(0); await expect(tile(page, editable.id).locator('.ms-text')).toContainText('One explicit text item');
  await inspector.getByLabel('Content', { exact: true }).selectOption('link'); await saved(page);
  await inspector.getByLabel('Hyperlink URL', { exact: true }).fill('https://example.org/one-document'); await inspector.getByLabel('Hyperlink label', { exact: true }).fill('One linked document');
  await inspector.getByRole('button', { name: 'Apply hyperlink', exact: true }).click(); await saved(page);
  const linkChoice = nodeIn(await stored(), editable.id); assert.equal(linkChoice.contentType, 'link'); assert.equal(linkChoice.text, undefined); assert.equal(linkChoice.hyperlinks.length, 1);
  assert.deepEqual(activeNodeContent(linkChoice), { type: 'link', hyperlink: { url: 'https://example.org/one-document', label: 'One linked document' } });
  await screenshot(page, 'package-single-content-inspector.png');
  record('The Content panel selects text, an embedded media/document file or one hyperlink independently of node kind; changing the actual content retains one displayed item and no combined media/text body.');

  finalProject = await stored(); assert.deepEqual(validateProject(finalProject), []);
  const beforeReload = structuredClone(finalProject); await page.reload(); await saved(page); finalProject = await stored(); assert.deepEqual(finalProject, beforeReload);
  assert.equal(nodeIn(finalProject, draggable.id).parentId, receiver.id);
  assert.equal(nodeIn(finalProject, manualChild.id).childPosition, 'manual');
  assert.deepEqual(finalProject.assets, fixture.assets);
  record('Autosave and reopening retain child parentage, identities, manual/automatic placement, one active content item and exact local image bytes.');

  const html = buildPublication(finalProject, viewerJS, sceneCSS), offlinePath = path.join(qaRoot, 'package-unbounded-offline.html'); await writeFile(offlinePath, html);
  const imported = readPublication(html); assert.equal(imported.nodes.length, finalProject.nodes.length);
  assert.equal(imported.nodes.filter(node => node.kind === 'media').length, finalProject.nodes.filter(node => node.kind === 'media').length);
  assert.equal(imported.nodes.find(node => node.title === editable.title).contentType, 'link');
  assert.equal(imported.nodes.find(node => node.title === draggable.title).parentId, imported.nodes.find(node => node.title === receiver.title).id);
  const offlineContext = await context({ offline: true }), offline = await offlineContext.newPage(); await offline.goto(pathToFileURL(offlinePath).href);
  await expect(tile(offline, receiver.id)).toHaveCount(1); await expect(tile(offline, draggable.id)).toHaveCount(1); const payload = await offline.locator('#meridian-data').textContent();
  await tile(offline, receiver.id).locator(':scope > .ms-node-header > .ms-node-title').dblclick(); await expect(tile(offline, draggable.id)).toHaveCount(0);
  await tile(offline, receiver.id).locator(':scope > .ms-node-header > .ms-node-title').dblclick(); await expect(tile(offline, draggable.id)).toHaveCount(1);
  assert.equal(await offline.locator('#meridian-data').textContent(), payload); await screenshot(offline, 'package-unbounded-offline.png');
  record('Portable offline export preserves every bounded/unbounded display pattern, nested hierarchy and active content selector; browsing the published graph leaves the immutable source payload unchanged.');
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

