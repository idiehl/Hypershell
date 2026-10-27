import { test, expect, type APIRequestContext, type Locator, type Page } from '@playwright/test';
import { readFile, writeFile } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';
import { addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import type { MeridianNode, MeridianProject, StoredModule } from '../src/core/types.ts';
import { EXPANSION_TRAY_PADDING } from '../src/core/expansion-tray-frame.ts';
import { buildPublication, readPublication } from '../src/publication.ts';
import { inspectorPanel } from './ui-helpers.ts';

const tile = (page: Page, id: string) => page.locator(`.ms-node[data-node-id="${id}"]`);
const tray = (page: Page, id: string) => page.locator(`.ms-expansion-tray[data-tray-source-id="${id}"]`);
const backdrop = (page: Page, id: string) => page.locator(`.ms-expansion-tray-backdrop[data-tray-source-id="${id}"]`);
const tether = (page: Page, id: string) => page.locator(`.ms-expansion-tether[data-tray-source-id="${id}"]`);
const get = (project: MeridianProject, id: string) => project.nodes.find(node => node.id === id)!;
const stored = async (request: APIRequestContext, id: string): Promise<MeridianProject> => (await request.get(`/api/projects/${id}`)).json();
const failures = new WeakMap<Page, string[]>();
test.beforeEach(async ({ page }) => { const errors: string[] = []; failures.set(page, errors); page.on('pageerror', error => errors.push(error.message)); });
test.afterEach(async ({ page }) => expect(failures.get(page) ?? []).toEqual([]));

function media(project: MeridianProject, owner: MeridianNode, tag: string, x: number, y: number) {
  const node = addNode(project, project.entryCanvasId, 'media', tag);
  Object.assign(node, { parentId: owner.id, tag, x, y, width: 190, height: 120, sizeMode: 'fixed', collapsed: true,
    contentType: 'text', text: `Original ${tag} passage.`, childPosition: 'auto', containerVariant: 'horizontal', childArrangement: 'row',
    style: { shape: 'rectangle', fill: '#244b40', fontSize: 18, titleSize: 20, padding: 8 } });
  return node;
}
function fixture(title: string) {
  const project = newProject(title); project.canvases[0]!.snapping = { stickyEdges: false, smartAlign: false, snapToGrid: false };
  const owner = addNode(project, project.entryCanvasId, 'container', '');
  Object.assign(owner, { tag: 'Ordered owner', x: 60, y: 90, width: 560, height: 310, sizeMode: 'fixed', collapsed: false,
    containerVariant: 'horizontal', childLayout: 'contained', childArrangement: 'row', style: { shape: 'rectangle', fill: '#1e2747', childPadding: 20 },
    containerSizes: { expanded: { width: 560, height: 310, sizeMode: 'fixed' }, collapsed: { width: 250, height: 130, sizeMode: 'fixed' } } });
  const source = media(project, owner, 'Branch A', 85, 120), peer = media(project, owner, 'Peer', 350, 120);
  const first = media(project, source, 'First original', 700, 170), second = media(project, source, 'Second original', 920, 170);
  const hidden = media(project, first, 'Remembered descendant', 700, 340);
  const link = (sourceId: string, targetId: string, label: string, points: Array<{ x: number; y: number }>) => ({ id: crypto.randomUUID(), canvasId: project.entryCanvasId, sourceId, targetId, kind: 'visual' as const, relation: 'generic' as const, label, points });
  const internal = link(first.id, second.id, 'Internal branch path', [{ x: 890, y: 140 }, { x: 920, y: 140 }]);
  const incoming = link(source.id, first.id, 'Source to original', [{ x: 640, y: 150 }]);
  const crossing = link(second.id, peer.id, 'Original to peer', [{ x: 1100, y: 460 }]);
  project.links.push(internal, incoming, crossing);
  return { project, owner, source, peer, first, second, hidden, internal, incoming, crossing };
}
async function seed(page: Page, request: APIRequestContext, project: MeridianProject) {
  const response = await request.post('/api/projects', { data: commitSnapshots(project) }); expect(response.ok(), await response.text()).toBe(true);
  await page.goto(`/?project=${project.id}`); await expect(page.locator('.workspace')).toBeVisible();
}
async function select(page: Page, id: string) {
  await inspectorPanel(page, 'canvas'); const search = page.getByRole('searchbox', { name: 'Search Work nodes', exact: true });
  await search.fill(id); await page.locator(`[data-outline-node="${id}"] .inspector-tree-title`).click(); await search.fill('');
}
async function toggle(page: Page, id: string) {
  const expanded = await tile(page, id).getAttribute('aria-expanded') === 'true';
  await select(page, id); await inspectorPanel(page, 'relations'); await page.getByRole('button', { name: 'Toggle contents from relations', exact: true }).click();
  const projectId = new URL(page.url()).searchParams.get('project')!;
  await expect.poll(async () => get(await stored(page.request, projectId), id).collapsed).toBe(expanded);
}
async function fit(page: Page) { await page.getByRole('button', { name: 'Fit Work', exact: true }).click(); }
async function blankRim(page: Page, id: string) {
  const background = backdrop(page, id), box = (await background.boundingBox())!, frame = await geometry(tray(page, id));
  // Just inside the padded rim, clear of both its edge resize hit target and
  // the real child cards. Coordinates follow the actual camera scale.
  return { x: box.x + box.width / 2, y: box.y + .85 * EXPANSION_TRAY_PADDING * box.height / frame.height };
}
async function geometry(surface: Locator) {
  return surface.evaluate(element => { const style = (element as HTMLElement).style; return { x: parseFloat(style.left), y: parseFloat(style.top), width: parseFloat(style.width), height: parseFloat(style.height) }; });
}
function separate(a: { x: number; y: number; width: number; height: number }, b: { x: number; y: number; width: number; height: number }) {
  return a.x + a.width <= b.x + .01 || b.x + b.width <= a.x + .01 || a.y + a.height <= b.y + .01 || b.y + b.height <= a.y + .01;
}
async function assertOutside(page: Page, sourceId: string, ancestorIds: string[]) {
  const frame = await geometry(tray(page, sourceId));
  for (const id of ancestorIds) expect(separate(frame, await geometry(tile(page, id))), `Tray ${sourceId} outside bounded ancestor ${id}`).toBe(true);
}
const positions = (project: MeridianProject, ids: string[]) => ids.map(id => { const node = get(project, id); return { id, x: node.x, y: node.y }; });

test('exterior expansion shows and edits the original nodes outside the enclosure without adding graph objects', async ({ page, request }, info) => {
  const f = fixture('Original nodes in Expansion Tray'); await seed(page, request, f.project);
  const baseline = await stored(request, f.project.id), ownerBox = await geometry(tile(page, f.owner.id)), sourceBox = await geometry(tile(page, f.source.id));
  await toggle(page, f.source.id); await expect(tray(page, f.source.id)).toHaveCount(1); await fit(page);
  await expect(tile(page, f.first.id)).toBeVisible(); await expect(tile(page, f.second.id)).toBeVisible(); await expect(tile(page, f.hidden.id)).toHaveCount(0);
  await assertOutside(page, f.source.id, [f.owner.id]); expect(await geometry(tile(page, f.owner.id))).toEqual(ownerBox); expect(await geometry(tile(page, f.source.id))).toEqual(sourceBox);
  await expect(tether(page, f.source.id)).toHaveAttribute('d', /^M.+ L/); await expect(tether(page, f.source.id)).not.toHaveAttribute('data-link-id', /.+/);
  const opened = await stored(request, f.project.id);
  expect(opened.nodes.map(node => [node.id, node.parentId])).toEqual(baseline.nodes.map(node => [node.id, node.parentId])); expect(opened.links.map(link => link.id)).toEqual(baseline.links.map(link => link.id));
  const rim = await blankRim(page, f.source.id); await page.mouse.move(rim.x, rim.y);
  await expect(tray(page, f.source.id).locator('.ms-expansion-tray-header,.ms-expansion-tray-close')).toHaveCount(0);
  await expect(tile(page, f.source.id)).toHaveClass(/ms-tray-highlighted/); await expect(tether(page, f.source.id)).toHaveClass(/ms-tray-highlighted/);
  await page.screenshot({ path: info.outputPath('original-source-and-connected-expansion-tray.png') });
  const body = page.locator(`[data-editable-node-id="${f.first.id}"][data-editable-text="body"]`);
  await body.click(); await body.press('Control+a'); await page.keyboard.insertText('Edited original passage in its expansion tray.'); await page.keyboard.press('Escape');
  await expect.poll(async () => get(await stored(request, f.project.id), f.first.id).text).toBe('Edited original passage in its expansion tray.');
  await page.reload(); await expect(tray(page, f.source.id)).toHaveCount(1); await fit(page); await expect(body).toHaveText('Edited original passage in its expansion tray.');
  expect(get(await stored(request, f.project.id), f.first.id).parentId).toBe(f.source.id);
  await page.screenshot({ path: info.outputPath('readable-original-text-after-save-reopen.png') });
});

test('moving a tray is one undoable action and remembers its position and expansion states through ancestor collapse and reload', async ({ page, request }, info) => {
  test.setTimeout(90000);
  const f = fixture('Remembered and undoable Expansion Tray'); await seed(page, request, f.project); await toggle(page, f.source.id); await fit(page);
  const before = await stored(request, f.project.id), ids = [f.first.id, f.second.id, f.hidden.id], oldOffset = get(before, f.source.id).expansionTray!.offset;
  const start = await blankRim(page, f.source.id);
  await page.mouse.move(start.x, start.y); await page.mouse.down();
  await page.mouse.move(start.x + 70, start.y + 90, { steps: 12 }); await page.mouse.up();
  await expect.poll(async () => get(await stored(request, f.project.id), f.source.id).expansionTray!.offset).not.toEqual(oldOffset);
  const moved = await stored(request, f.project.id), newOffset = get(moved, f.source.id).expansionTray!.offset, dx = newOffset.x - oldOffset.x, dy = newOffset.y - oldOffset.y;
  expect(Math.abs(dx) + Math.abs(dy)).toBeGreaterThan(10);
  for (const id of ids) { expect(get(moved, id).x).toBeCloseTo(get(before, id).x + dx, 5); expect(get(moved, id).y).toBeCloseTo(get(before, id).y + dy, 5); }
  expect(get(moved, f.source.id)).toEqual({ ...get(before, f.source.id), expansionTray: { ...get(before, f.source.id).expansionTray, offset: newOffset } });
  expect(get(moved, f.owner.id)).toEqual(get(before, f.owner.id)); expect(get(moved, f.peer.id)).toEqual(get(before, f.peer.id));
  expect(moved.links.find(link => link.id === f.internal.id)!.points).toEqual(before.links.find(link => link.id === f.internal.id)!.points!.map(point => ({ x: point.x + dx, y: point.y + dy })));
  for (const id of [f.incoming.id, f.crossing.id]) expect(moved.links.find(link => link.id === id)!.points).toEqual(before.links.find(link => link.id === id)!.points);
  await page.locator('.ms-viewport').focus(); await page.keyboard.press('Control+z');
  await expect.poll(async () => get(await stored(request, f.project.id), f.source.id).expansionTray!.offset).toEqual(oldOffset);
  expect(positions(await stored(request, f.project.id), ids)).toEqual(positions(before, ids));
  await page.keyboard.press('Control+x'); await expect.poll(async () => get(await stored(request, f.project.id), f.source.id).expansionTray!.offset).toEqual(newOffset);
  expect(positions(await stored(request, f.project.id), ids)).toEqual(positions(moved, ids));
  await toggle(page, f.owner.id); await expect(tray(page, f.source.id)).toHaveCount(0); await expect(tether(page, f.source.id)).toHaveCount(0);
  await expect.poll(async () => get(await stored(request, f.project.id), f.owner.id).collapsed).toBe(true);
  expect(get(await stored(request, f.project.id), f.source.id).collapsed).toBe(false); expect(get(await stored(request, f.project.id), f.first.id).collapsed).toBe(true);
  await page.reload(); await expect(tray(page, f.source.id)).toHaveCount(0); await toggle(page, f.owner.id); await expect(tray(page, f.source.id)).toHaveCount(1);
  await expect.poll(async () => get(await stored(request, f.project.id), f.source.id).expansionTray!.offset).toEqual(newOffset);
  expect(positions(await stored(request, f.project.id), ids)).toEqual(positions(moved, ids));
  await fit(page); const collapse = await blankRim(page, f.source.id);
  await page.mouse.dblclick(collapse.x, collapse.y, { delay: 100 }); await expect(tray(page, f.source.id)).toHaveCount(0);
  await expect.poll(async () => get(await stored(request, f.project.id), f.source.id).collapsed).toBe(true);
  await toggle(page, f.source.id); await expect(tray(page, f.source.id)).toHaveCount(1);
  expect(get(await stored(request, f.project.id), f.source.id).expansionTray!.offset).toEqual(newOffset);
  expect(positions(await stored(request, f.project.id), ids)).toEqual(positions(moved, ids));
  await page.reload(); await expect(tray(page, f.source.id)).toHaveCount(1); await fit(page);
  await page.screenshot({ path: info.outputPath('remembered-tray-position-and-nested-closed-state.png') });
});

test('several trays and a source nested inside a bounded child remain separate and preserve each original content type', async ({ page, request }, info) => {
  test.setTimeout(90000);
  const f = fixture('Multiple and deeply nested Expansion Trays');
  f.peer.contentType = 'link'; f.peer.hyperlinks = [{ url: 'https://example.com/original', label: 'Original source record' }];
  const peerChild = media(f.project, f.peer, 'Peer original child', 900, 650);
  Object.assign(f.second, { kind: 'container', title: '', tag: 'Nested bounded container', containerVariant: 'horizontal', childLayout: 'contained', childArrangement: 'row', width: 420, height: 240,
    containerSizes: { expanded: { width: 420, height: 240, sizeMode: 'fixed' }, collapsed: { width: 190, height: 120, sizeMode: 'fixed' } } });
  const deepSource = media(f.project, f.second, 'Deep source', 950, 200), deepChild = media(f.project, deepSource, 'Deep original child', 1200, 200);
  await seed(page, request, f.project); await toggle(page, f.source.id); await toggle(page, f.peer.id); await toggle(page, f.second.id); await toggle(page, deepSource.id); await fit(page);
  const sources = [f.source.id, f.peer.id, deepSource.id];
  for (const id of sources) { await expect(tray(page, id)).toHaveCount(1); await expect(tether(page, id)).toHaveCount(1); await assertOutside(page, id, id === deepSource.id ? [f.owner.id, f.second.id] : [f.owner.id]); }
  for (let a = 0; a < sources.length; a++) for (let b = a + 1; b < sources.length; b++) expect(separate(await geometry(tray(page, sources[a]!)), await geometry(tray(page, sources[b]!)))).toBe(true);
  for (const node of [f.first, peerChild, deepChild]) await expect(tile(page, node.id)).toHaveCount(1);
  const deepBox = await geometry(tile(page, deepChild.id));
  expect(deepBox.width / deepBox.height).toBeGreaterThan(.5); expect(deepBox.width / deepBox.height).toBeLessThan(3);
  const saved = await stored(request, f.project.id);
  expect(saved.nodes.map(node => [node.id, node.parentId, node.contentType])).toEqual(f.project.nodes.map(node => [node.id, node.parentId, node.contentType]));
  expect(get(saved, f.peer.id).hyperlinks).toEqual(f.peer.hyperlinks); expect(saved.nodes).toHaveLength(f.project.nodes.length);
  await toggle(page, f.owner.id); await expect(page.locator('.ms-expansion-tray')).toHaveCount(0); await toggle(page, f.owner.id); await expect(page.locator('.ms-expansion-tray')).toHaveCount(3);
  for (const id of sources) expect(get(await stored(request, f.project.id), id).expansionTray).toEqual(get(saved, id).expansionTray);
  await fit(page); await page.screenshot({ path: info.outputPath('multiple-original-branches-and-deep-expansion-tray.png') });
});

test('copied and reusable branches retain tray placement and disclosure state in packages and the standalone viewer', async ({ page, request, browser }, info) => {
  test.setTimeout(90000);
  const f = fixture('Portable Expansion Tray'); await seed(page, request, f.project); await toggle(page, f.source.id); await fit(page);
  const opened = await stored(request, f.project.id), expected = get(opened, f.source.id).expansionTray;
  await select(page, f.owner.id); await page.locator('.ms-viewport').focus(); await page.keyboard.press('Control+c'); await page.keyboard.press('Control+v');
  await expect.poll(async () => (await stored(request, f.project.id)).nodes.length).toBe(f.project.nodes.length * 2 - 1);
  const copied = await stored(request, f.project.id), copySource = copied.nodes.find(node => node.tag === f.source.tag && node.id !== f.source.id)!;
  expect(copySource.expansionTray).toEqual(expected); expect(copySource.collapsed).toBe(false); expect(copySource.parentId).not.toBe(f.owner.id);
  expect(copied.nodes.find(node => node.tag === f.first.tag && node.parentId === copySource.id)!.collapsed).toBe(true);
  await page.keyboard.press('Escape'); await page.getByRole('button', { name: 'New Node', exact: true }).click(); await page.getByRole('menuitem', { name: 'Module', exact: true }).click();
  // Use the padded rim so selection does not enter the title or a child.
  await fit(page); await tile(page, f.owner.id).click({ position: { x: 5, y: 35 } });
  const name = `Expansion tray ${f.project.id.slice(0, 8)}`; await page.getByRole('textbox', { name: 'Module name', exact: true }).fill(name); await page.getByRole('button', { name: 'Create From Selection', exact: true }).click();
  let catalog: Array<{ id: string; name: string }> = [];
  await expect.poll(async () => { catalog = await (await request.get('/api/modules')).json(); return catalog.some(module => module.name === name); }).toBe(true);
  const module = await (await request.get(`/api/modules/${catalog.find(module => module.name === name)!.id}`)).json() as StoredModule;
  const moduleSource = module.project.nodes.find(node => node.tag === f.source.tag)!; expect(moduleSource.expansionTray).toEqual(expected); expect(moduleSource.collapsed).toBe(false); expect(moduleSource.id).not.toBe(f.source.id);
  const beforeInsertion = await stored(request, f.project.id), existingIds = new Set(beforeInsertion.nodes.map(node => node.id));
  await page.locator(`.module-library-card[data-module-id="${module.id}"]`).getByRole('button', { name: `Add ${name} to Canvas`, exact: true }).click();
  await expect.poll(async () => (await stored(request, f.project.id)).nodes.length).toBe(beforeInsertion.nodes.length + module.project.nodes.length - 1);
  const inserted = (await stored(request, f.project.id)).nodes.filter(node => !existingIds.has(node.id)), insertedSource = inserted.find(node => node.tag === f.source.tag)!;
  expect(insertedSource.expansionTray).toEqual(expected); expect(insertedSource.collapsed).toBe(false);
  expect(inserted.find(node => node.tag === f.first.tag)!.parentId).toBe(insertedSource.id);
  expect(inserted.every(node => !module.project.nodes.some(original => original.id === node.id))).toBe(true);
  const insertedAncestors: string[] = []; let parentId = insertedSource.parentId;
  while (parentId && inserted.some(node => node.id === parentId)) {
    const parent = inserted.find(node => node.id === parentId)!;
    if (['container', 'spreadsheet', 'module'].includes(parent.kind)) insertedAncestors.push(parent.id);
    parentId = parent.parentId;
  }
  await assertOutside(page, insertedSource.id, insertedAncestors);
  const saved = await stored(request, f.project.id), exported = await request.post('/api/export', { data: saved }); expect(exported.ok(), await exported.text()).toBe(true);
  const imported = await request.post('/api/import', { multipart: { file: { name: 'expansion-trays.meridian', mimeType: 'application/octet-stream', buffer: await exported.body() } } }); expect(imported.ok(), await imported.text()).toBe(true);
  const portable = await imported.json() as MeridianProject; expect(portable).toEqual(saved);
  const html = buildPublication(portable, await readFile('dist/viewer.js', 'utf8'), await readFile('dist/scene.css', 'utf8'));
  const fresh = readPublication(html), freshSource = fresh.nodes.find(node => node.tag === f.source.tag)!; expect(freshSource.expansionTray).toEqual(expected); expect(freshSource.id).not.toBe(f.source.id);
  const filename = info.outputPath('expansion-trays-offline.html'); await writeFile(filename, html, 'utf8');
  const viewer = await browser.newPage();
  try {
    await viewer.goto(pathToFileURL(filename).href); await expect(tray(viewer, f.source.id)).toHaveCount(1);
    await expect(tile(viewer, f.first.id)).toHaveCount(1); await expect(viewer.locator('[contenteditable="true"]')).toHaveCount(0);
    await expect(tray(viewer, f.source.id).locator('.ms-expansion-tray-header,.ms-expansion-tray-close,.ms-expansion-tray-resize')).toHaveCount(0);
    await expect(backdrop(viewer, f.source.id)).not.toHaveClass(/ms-tray-draggable/);
    await viewer.screenshot({ path: info.outputPath('standalone-original-nodes-and-tray.png') });
  } finally { await viewer.close(); }
});
