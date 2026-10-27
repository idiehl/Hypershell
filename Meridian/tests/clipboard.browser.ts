import { test, expect, type APIRequestContext, type Page } from '@playwright/test';
import { addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import { normalizeLibraryDocument, type LibraryCanvasDocument, type LibraryCatalogItem } from '../src/core/library.ts';
import type { MeridianNode, MeridianProject } from '../src/core/types.ts';

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
const PNG = 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLbtAAAAABJRU5ErkJggg==';
const tile = (page: Page, id: string) => page.locator(`#scene .ms-node[data-node-id="${id}"]`);
const stored = async (request: APIRequestContext, id: string): Promise<MeridianProject> => (await request.get(`/api/projects/${id}`)).json();
const library = async (request: APIRequestContext): Promise<LibraryCanvasDocument> => (await request.get('/api/library')).json();
const selection = (page: Page) => page.locator('#scene .ms-node[aria-selected="true"]').evaluateAll(nodes => nodes.map(node => (node as HTMLElement).dataset.nodeId).sort());
const newNodes = (before: MeridianProject, after: MeridianProject) => after.nodes.filter(node => !before.nodes.some(original => original.id === node.id));
async function geometry(page: Page, id: string) {
  return tile(page, id).evaluate(node => {
    const style = (node as HTMLElement).style;
    return { x: parseFloat(style.left), y: parseFloat(style.top), width: parseFloat(style.width), height: parseFloat(style.height) };
  });
}
function intersects(a: { x: number; y: number; width: number; height: number }, b: { x: number; y: number; width: number; height: number }) {
  return a.x < b.x + b.width - .01 && a.x + a.width > b.x + .01 && a.y < b.y + b.height - .01 && a.y + a.height > b.y + .01;
}
function fixture(title: string) {
  const project = newProject(title), canvasId = project.entryCanvasId;
  project.canvases[0]!.snapping = { stickyEdges: false, smartAlign: false, snapToGrid: false };
  const add = (kind: MeridianNode['kind'], name: string, x: number, y: number, width = 200, height = 120) => {
    const node = addNode(project, canvasId, kind, name);
    Object.assign(node, { x, y, width, height, sizeMode: 'fixed', placement: 'free', childLayout: 'spatial' });
    return node;
  };
  return { project, canvasId, add };
}
async function open(page: Page, request: APIRequestContext, project: MeridianProject) {
  const response = await request.post('/api/projects', { data: commitSnapshots(project) });
  expect(response.ok(), await response.text()).toBe(true);
  await page.goto(`/?project=${project.id}`);
  await expect(page.locator('.workspace')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Single select', exact: true })).toHaveAttribute('aria-pressed', 'true');
}
async function select(page: Page, node: MeridianNode, modifier = false) {
  const title = tile(page, node.id).locator('.ms-node-title');
  await (await title.isVisible() ? title : tile(page, node.id)).click(modifier ? { modifiers: ['Control'] } : {});
}
async function mode(page: Page, value: 'Node' | 'Link') {
  await page.getByRole('button', { name: /^New (?:Node|Link)$/ }).click();
  await page.getByRole('menuitem', { name: value, exact: true }).click();
}
async function pasted(page: Page, request: APIRequestContext, before: MeridianProject, count: number) {
  await page.keyboard.press('Control+v');
  await expect.poll(async () => (await stored(request, before.id)).nodes.length).toBe(before.nodes.length + count);
  await expect(page.locator('.save-state')).toHaveText(/^Saved · r\d+$/);
  return stored(request, before.id);
}

test('Ctrl+C and Ctrl+V copy complete media content and appearance to a fresh nearby node without overlap', async ({ page, request }, info) => {
  const f = fixture('Clipboard media preservation'), source = f.add('image', 'Source image', 100, 120, 250, 250), obstacle = f.add('text', 'Nearby obstacle', 390, 120, 240, 200);
  obstacle.text = 'Keep this authored node in place.';
  const imageId = crypto.randomUUID(), notesId = crypto.randomUUID(), backgroundId = crypto.randomUUID();
  f.project.assets.push({ id: imageId, name: 'Illustration.png', mime: 'image/png', base64: PNG }, { id: notesId, name: 'Source.txt', mime: 'text/plain', base64: Buffer.from('A supplemental source attachment.').toString('base64') }, { id: backgroundId, name: 'Backdrop.png', mime: 'image/png', base64: PNG });
  Object.assign(source, { text: 'A caption retained by the copy.', assetId: imageId, attachments: [notesId], hyperlinks: [{ url: 'https://example.org/source', label: 'Source record' }], style: { shape: 'rounded', fill: '#173d3b', border: '#df9e62', textColor: '#f0efe4', borderWidth: 3, borderStyle: 'dotted', radius: 18, padding: 9, contentGap: 7, fontFamily: 'serif', fontSize: 14, titleSize: 20, backgroundAssetId: backgroundId, backgroundFit: 'cover', backgroundOpacity: .15 } });
  await open(page, request, f.project); await select(page, source);
  const baseline = await stored(request, f.project.id), sourceGeometry = await geometry(page, source.id), obstacleGeometry = await geometry(page, obstacle.id);
  await page.keyboard.press('Control+c'); expect(await stored(request, f.project.id)).toEqual(baseline);
  await mode(page, 'Node');
  const after = await pasted(page, request, baseline, 1), copy = newNodes(baseline, after)[0]!;
  expect(copy.id).toMatch(UUID); expect(copy.id).not.toBe(source.id);
  expect(copy).toMatchObject({ canvasId: source.canvasId, parentId: source.parentId, kind: source.kind, title: source.title, text: source.text, hyperlinks: source.hyperlinks, style: source.style, sizeMode: source.sizeMode });
  expect(after.assets.find(asset => asset.id === copy.assetId)?.base64).toBe(PNG);
  expect(after.assets.find(asset => asset.id === copy.attachments?.[0])?.base64).toBe(f.project.assets.find(asset => asset.id === notesId)!.base64);
  expect(after.assets.find(asset => asset.id === copy.style?.backgroundAssetId)?.base64).toBe(PNG);
  const copyGeometry = await geometry(page, copy.id);
  expect(intersects(copyGeometry, sourceGeometry)).toBe(false); expect(intersects(copyGeometry, obstacleGeometry)).toBe(false);
  expect(Math.hypot(copyGeometry.x - sourceGeometry.x, copyGeometry.y - sourceGeometry.y)).toBeLessThan(1000);
  expect(await selection(page)).toEqual([copy.id]);
  await expect(page.getByRole('button', { name: 'Single select', exact: true })).toHaveAttribute('aria-pressed', 'true');
  await expect(page.getByRole('dialog')).toHaveCount(0);
  await expect(tile(page, copy.id).locator('img.ms-image')).toHaveCount(1);
  await page.screenshot({ path: info.outputPath('clipboard-media-copy.png') });
});

test('copying a collapsed container carries hidden descendants and internal styled links with remapped identities', async ({ page, request }, info) => {
  const f = fixture('Clipboard complete branch'), parent = f.add('container', 'Collapsed collection', 100, 110, 340, 260), external = f.add('text', 'Outside collection', 650, 120);
  Object.assign(parent, { containerVariant: 'contained', childLayout: 'contained', childArrangement: 'free', collapsed: true, text: 'The hidden graph travels with this collection.' });
  const first = f.add('text', 'Hidden passage', 120, 175, 140, 100), second = f.add('reference', 'Passage reference', 285, 175, 140, 100);
  Object.assign(first, { parentId: parent.id, text: 'A private section of the copied branch.' }); Object.assign(second, { parentId: parent.id, targetId: first.id });
  const internal = { id: crypto.randomUUID(), canvasId: f.canvasId, parentId: parent.id, sourceId: first.id, targetId: second.id, kind: 'visual' as const, relation: 'mutual' as const, direction: 'both' as const, label: 'Compare inside', points: [{ x: 268, y: 300 }], style: { color: '#dc6b97', width: 7, lineStyle: 'dotted' as const } };
  const crossing = { id: crypto.randomUUID(), canvasId: f.canvasId, parentId: parent.id, sourceId: first.id, targetId: external.id, kind: 'visual' as const, relation: 'causal' as const };
  f.project.links.push(internal, crossing);
  await open(page, request, f.project); await select(page, parent); await expect(tile(page, first.id)).toHaveCount(0);
  const baseline = await stored(request, f.project.id); await page.keyboard.press('Control+c'); const after = await pasted(page, request, baseline, 3);
  const copies = newNodes(baseline, after), copiedParent = copies.find(node => node.kind === 'container')!, copiedFirst = copies.find(node => node.title === first.title)!, copiedSecond = copies.find(node => node.title === second.title)!;
  expect(copies).toHaveLength(3); for (const node of copies) expect(node.id).toMatch(UUID);
  expect(copiedParent.collapsed).toBe(true); expect(copiedFirst.parentId).toBe(copiedParent.id); expect(copiedSecond.parentId).toBe(copiedParent.id); expect(copiedSecond.targetId).toBe(copiedFirst.id); expect(copiedFirst.text).toBe(first.text);
  expect(await selection(page)).toEqual([copiedParent.id]);
  await expect(tile(page, copiedFirst.id)).toHaveCount(0);
  const addedLinks = after.links.filter(link => !baseline.links.some(original => original.id === link.id));
  expect(addedLinks).toHaveLength(1); expect(addedLinks[0]).toMatchObject({ parentId: copiedParent.id, sourceId: copiedFirst.id, targetId: copiedSecond.id, kind: internal.kind, relation: internal.relation, direction: internal.direction, label: internal.label, style: internal.style }); expect(addedLinks[0]!.id).toMatch(UUID);
  expect(after.links.find(link => link.id === crossing.id)).toEqual(baseline.links.find(link => link.id === crossing.id));
  expect(after.links.filter(link => link.targetId === external.id)).toHaveLength(1);
  await tile(page, copiedParent.id).locator('.ms-node-title').dblclick();
  await expect(tile(page, copiedFirst.id)).toBeVisible(); await expect(tile(page, copiedSecond.id)).toBeVisible(); await expect(tile(page, first.id)).toHaveCount(0);
  await page.screenshot({ path: info.outputPath('clipboard-container-contents.png') });
});

test('multi-selection and repeated paste preserve the group and its link while every pasted node receives a new GUID', async ({ page, request }) => {
  const f = fixture('Clipboard selected graph'), first = f.add('text', 'First note', 100, 100, 180, 100), second = f.add('text', 'Second note', 370, 100, 180, 100);
  first.text = 'First selected body.'; second.text = 'Second selected body.';
  f.project.links.push({ id: crypto.randomUUID(), canvasId: f.canvasId, sourceId: first.id, targetId: second.id, kind: 'visual', relation: 'chronological', direction: 'forward', style: { color: '#83c7dc', width: 4, lineStyle: 'solid' } });
  await open(page, request, f.project); await select(page, first); await select(page, second, true);
  expect(await selection(page)).toEqual([first.id, second.id].sort());
  const baseline = await stored(request, f.project.id), originalFirst = await geometry(page, first.id), originalSecond = await geometry(page, second.id);
  await page.keyboard.press('Control+c'); const once = await pasted(page, request, baseline, 2), firstCopies = newNodes(baseline, once);
  expect(await selection(page)).toEqual(firstCopies.map(node => node.id).sort());
  const twice = await pasted(page, request, once, 2), secondCopies = newNodes(once, twice);
  expect(await selection(page)).toEqual(secondCopies.map(node => node.id).sort());
  const allCopies = [...firstCopies, ...secondCopies]; expect(new Set(allCopies.map(node => node.id)).size).toBe(4); for (const copy of allCopies) expect(copy.id).toMatch(UUID);
  for (const group of [firstCopies, secondCopies]) {
    const a = group.find(node => node.title === first.title)!, b = group.find(node => node.title === second.title)!;
    expect(b.x - a.x).toBeCloseTo(originalSecond.x - originalFirst.x); expect(b.y - a.y).toBeCloseTo(originalSecond.y - originalFirst.y);
    expect(twice.links.filter(link => link.sourceId === a.id && link.targetId === b.id)).toHaveLength(1);
  }
  expect(twice.links).toHaveLength(3);
  const ordinary = twice.nodes.filter(node => node.kind !== 'canvas');
  for (let index = 0; index < ordinary.length; index++) for (const peer of ordinary.slice(index + 1)) expect(intersects(ordinary[index]!, peer)).toBe(false);
});

test('paste is one undoable autosaved edit and redo restores the same copied identity after reopen', async ({ page, request }) => {
  const f = fixture('Clipboard history'), source = f.add('text', 'Undoable note', 150, 150); source.text = 'Copied text remains unchanged through history.';
  await open(page, request, f.project); await select(page, source); const baseline = await stored(request, f.project.id);
  await page.keyboard.press('Control+c'); await expect(page.getByRole('button', { name: 'Undo', exact: true })).toBeDisabled();
  const after = await pasted(page, request, baseline, 1), copy = newNodes(baseline, after)[0]!;
  await tile(page, copy.id).focus(); await page.keyboard.press('Control+z');
  await expect(tile(page, copy.id)).toHaveCount(0); await expect.poll(async () => (await stored(request, f.project.id)).nodes.length).toBe(baseline.nodes.length);
  await expect(page.getByRole('button', { name: 'Undo', exact: true })).toBeDisabled();
  await page.keyboard.press('Control+Shift+z'); await expect(tile(page, copy.id)).toBeVisible();
  await expect.poll(async () => (await stored(request, f.project.id)).nodes.find(node => node.id === copy.id)?.text).toBe(source.text);
  await expect(page.locator('.save-state')).toHaveText(/^Saved · r\d+$/); await page.reload();
  await expect(tile(page, copy.id)).toBeVisible(); await select(page, copy); await page.getByLabel('Side panel', { exact: true }).selectOption('properties');
  await expect(page.locator('.inspector').getByLabel('GUID', { exact: true })).toHaveValue(copy.id);
  expect((await stored(request, f.project.id)).nodes.filter(node => node.title === source.title)).toHaveLength(2);
});

test('focused text fields keep native Ctrl+C and Ctrl+V without duplicating the selected node', async ({ page, request, context }) => {
  // Browser clipboard permissions affect native field paste; the node shortcuts
  // must still yield to the focused input rather than pasting the graph.
  await context.grantPermissions(['clipboard-read', 'clipboard-write']);
  const f = fixture('Clipboard text editing'), source = f.add('text', 'Native text editing', 120, 120); source.text = 'Ordinary body text.';
  await open(page, request, f.project); await select(page, source); await page.keyboard.press('Control+c');
  await page.getByLabel('Side panel', { exact: true }).selectOption('content');
  const title = page.locator('.inspector').getByLabel('Title', { exact: true }); await title.focus(); await title.press('Control+a'); await title.press('Control+c');
  await title.fill(''); await title.press('Control+v'); await expect(title).toHaveValue(source.title); await title.press('Tab');
  await expect(page.getByRole('dialog')).toHaveCount(0); expect((await stored(request, f.project.id)).nodes).toHaveLength(f.project.nodes.length);
  const text = page.locator('.inspector').getByLabel('Text body', { exact: true }); await text.focus(); await text.press('Control+a'); await text.press('Control+c'); await text.fill(''); await text.press('Control+v');
  await expect(text).toHaveValue(source.text); expect((await stored(request, f.project.id)).nodes).toHaveLength(f.project.nodes.length);
});

test('Utility pivots preserve compact shape, size and subtype and do not duplicate links to unselected nodes', async ({ page, request }) => {
  const f = fixture('Clipboard routing utility'), source = f.add('text', 'Route source', 100, 120), pivot = f.add('junction', 'Route pivot', 410, 210, 36, 6), target = f.add('text', 'Route destination', 650, 120);
  Object.assign(pivot, { utilityVariant: 'pivot', snapping: { stickyEdges: false, smartAlign: false, snapToGrid: false }, style: { shape: 'rectangle', fill: '#de793e', border: '#ffe4bc', borderWidth: 2 } });
  f.project.links.push({ id: crypto.randomUUID(), canvasId: f.canvasId, sourceId: source.id, targetId: pivot.id, kind: 'visual', relation: 'generic' }, { id: crypto.randomUUID(), canvasId: f.canvasId, sourceId: pivot.id, targetId: target.id, kind: 'visual', relation: 'generic' });
  await open(page, request, f.project); await select(page, pivot); const before = await stored(request, f.project.id);
  await page.keyboard.press('Control+c'); const after = await pasted(page, request, before, 1), copy = newNodes(before, after)[0]!;
  expect(copy).toMatchObject({ kind: 'junction', utilityVariant: 'pivot', width: 36, height: 6, sizeMode: 'fixed', style: pivot.style, snapping: pivot.snapping });
  expect(after.links).toEqual(before.links); await expect(tile(page, copy.id)).toHaveAttribute('data-shape', 'rectangle');
  await page.getByLabel('Side panel', { exact: true }).selectOption('properties'); await expect(page.locator('.inspector').getByLabel('Type', { exact: true })).toHaveValue('utility'); await expect(page.locator('.inspector').getByLabel('Utility role', { exact: true })).toHaveValue('pivot');
});

test('Canvas library uses the same node shortcuts and copied launchers preserve their destination', async ({ page, request }) => {
  const destination = fixture('Clipboard library destination'); destination.add('text', 'Canvas body', 100, 100);
  const created = await request.post('/api/projects', { data: commitSnapshots(destination.project) }); expect(created.ok()).toBe(true);
  const catalog = await (await request.get('/api/library/catalog')).json() as LibraryCatalogItem[];
  // Fresh test storage initially returns the legacy empty-library envelope.
  // Use the ordinary migration contract before constructing a v2 graph fixture.
  const current = normalizeLibraryDocument(await library(request), catalog);
  const recovery = await request.get('/api/library/recovery');
  if (recovery.ok()) { const pending = await recovery.json(); expect((await request.delete('/api/library/recovery', { data: { baseRevision: pending.baseRevision, savedAt: pending.savedAt } })).ok()).toBe(true); }
  // This is only the isolated test library. Prevent incidental catalog discovery
  // from crowding the authored fixture or changing its pasted-node count.
  const root = current.project.nodes.find(node => node.id === current.project.canvases.find(canvas => canvas.id === current.project.entryCanvasId)!.rootId)!;
  current.project.nodes = [root]; current.project.links = []; current.project.assets = []; current.project.snapshots = {}; current.canvasTargets = {};
  current.discoveredTargets = catalog.flatMap(item => item.canvases.map(canvas => `${item.id}:${canvas.id}`));
  const launcher = addNode(current.project, current.project.entryCanvasId, 'canvas-link', 'Copy this canvas launcher');
  Object.assign(launcher, { x: 120, y: 120, width: 250, height: 110, sizeMode: 'fixed', placement: 'free', canvasTarget: { projectId: destination.project.id, canvasId: destination.project.entryCanvasId } });
  current.canvasTargets[launcher.id] = launcher.canvasTarget!;
  const response = await request.put('/api/library', { data: { library: current, expectedRevision: current.revision } }); expect(response.ok(), await response.text()).toBe(true);
  await page.goto('/'); await expect(tile(page, launcher.id)).toBeVisible(); await select(page, launcher);
  const before = await library(request), destinationBefore = await stored(request, destination.project.id);
  await page.keyboard.press('Control+c'); await page.keyboard.press('Control+v');
  await expect.poll(async () => (await library(request)).project.nodes.length).toBe(before.project.nodes.length + 1);
  await expect(page.locator('.save-state')).toHaveText(/^Saved · r\d+$/);
  const after = await library(request), copy = newNodes(before.project, after.project)[0]!;
  expect(copy.id).toMatch(UUID); expect(copy.kind).toBe('canvas-link'); expect(copy.canvasTarget).toEqual(launcher.canvasTarget); expect(after.canvasTargets[copy.id]).toEqual(launcher.canvasTarget);
  expect(new URL(page.url()).searchParams.get('project')).toBeNull(); expect(await selection(page)).toEqual([copy.id]);
  await page.reload(); await expect(tile(page, copy.id)).toBeVisible(); expect((await library(request)).canvasTargets[copy.id]).toEqual(launcher.canvasTarget);
  expect(await stored(request, destination.project.id)).toEqual(destinationBefore);
});
