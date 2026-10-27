import { test, expect, type APIRequestContext, type Locator, type Page } from '@playwright/test';
import { addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import { normalizeContainerLayouts } from '../src/core/grid.ts';
import { expansionTrayBounds } from '../src/core/expansion-tray.ts';
import { EXPANSION_TRAY_PADDING, expansionTrayHeader } from '../src/core/expansion-tray-frame.ts';
import type { ContainerVariant, MeridianNode, MeridianProject } from '../src/core/types.ts';
import { inspectorPanel } from './ui-helpers.ts';
import { nodeTag } from '../src/core/node-tag.ts';

const card = (page: Page, id: string) => page.locator(`.ms-node[data-node-id="${id}"]`);
const tray = (page: Page, id: string) => page.locator(`.ms-expansion-tray[data-tray-source-id="${id}"]`);
const get = (project: MeridianProject, id: string) => project.nodes.find(node => node.id === id)!;
const stored = async (request: APIRequestContext, id: string): Promise<MeridianProject> => (await request.get(`/api/projects/${id}`)).json();
const errors = new WeakMap<Page, string[]>();
test.beforeEach(async ({ page }) => { const list: string[] = []; errors.set(page, list); page.on('pageerror', error => list.push(error.message)); });
test.afterEach(async ({ page }) => expect(errors.get(page) ?? []).toEqual([]));

function fixture(pattern: ContainerVariant = 'horizontal') {
  let project = newProject(`Resizable ${pattern} tray`);
  project.canvases[0]!.snapping = { stickyEdges: false, smartAlign: false, snapToGrid: false };
  const owner = addNode(project, project.entryCanvasId, 'container', '');
  Object.assign(owner, { tag: 'Enclosing parent', x: 60, y: 90, width: 480, height: 260, sizeMode: 'fixed', collapsed: false,
    containerVariant: 'horizontal', childLayout: 'contained', style: { childPadding: 20, shape: 'rectangle' },
    containerSizes: { expanded: { width: 480, height: 260, sizeMode: 'fixed' }, collapsed: { width: 220, height: 100, sizeMode: 'fixed' } } });
  const source = addNode(project, project.entryCanvasId, 'media', 'Original source');
  Object.assign(source, { parentId: owner.id, tag: 'Resizable branch', x: 85, y: 120, width: 190, height: 120,
    sizeMode: 'fixed', collapsed: false, containerVariant: pattern, childPosition: 'auto',
    ...(pattern === 'window' ? { grid: { columns: 2, rows: 2, windowColumns: 1, windowRows: 1, cellWidth: 220, cellHeight: 130, scrollDirection: 'horizontal' } } : {}) });
  const peer = addNode(project, project.entryCanvasId, 'media', 'Peer');
  Object.assign(peer, { parentId: owner.id, x: 340, y: 120, width: 190, height: 120, sizeMode: 'fixed', collapsed: true, childPosition: 'auto' });
  const children = Array.from({ length: pattern === 'window' ? 4 : 2 }, (_, index) => {
    const node = addNode(project, project.entryCanvasId, 'media', `Original child ${index + 1}`);
    Object.assign(node, { parentId: source.id, x: 700 + 240 * index, y: 180, width: 200, height: 130, sizeMode: 'fixed',
      collapsed: true, childPosition: 'auto', containerVariant: 'horizontal', contentType: 'text',
      text: 'Text stays editable and fits its resized node.', style: { fontSize: 18, titleSize: 22, shape: 'rectangle', padding: 5 } });
    return node;
  });
  const hidden = addNode(project, project.entryCanvasId, 'media', 'Hidden descendant');
  Object.assign(hidden, { parentId: children[0]!.id, x: 760, y: 380, width: 140, height: 90, sizeMode: 'fixed', collapsed: true });
  const linkId = crypto.randomUUID(), fanIds = children.map(() => crypto.randomUUID());
  project.links.push({ id: linkId, canvasId: project.entryCanvasId, sourceId: children[0]!.id, targetId: children[1]!.id,
    parentId: source.id, kind: 'visual', relation: 'generic', points: [{ x: 910, y: 160 }, { x: 930, y: 160 }] });
  children.forEach((child, index) => project.links.push({ id: fanIds[index]!, canvasId: project.entryCanvasId, sourceId: source.id,
    targetId: child.id, parentId: source.id, kind: 'visual', relation: 'generic', style: { lineStyle: 'dashed' } }));
  project = normalizeContainerLayouts(project);
  return { project, ownerId: owner.id, sourceId: source.id, peerId: peer.id, childIds: children.map(node => node.id), hiddenId: hidden.id, linkId, fanIds };
}
async function seed(page: Page, request: APIRequestContext, project: MeridianProject) {
  const response = await request.post('/api/projects', { data: commitSnapshots(project) }); expect(response.ok(), await response.text()).toBe(true);
  await page.goto(`/?project=${project.id}`); await expect(page.locator('.workspace')).toBeVisible();
  await page.getByRole('button', { name: 'Fit Work', exact: true }).click();
}
async function geometry(element: Locator) {
  return element.evaluate(surface => { const style = (surface as HTMLElement).style; return { x: parseFloat(style.left), y: parseFloat(style.top), width: parseFloat(style.width), height: parseFloat(style.height) }; });
}
async function resize(page: Page, id: string, edge: string, dx: number, dy: number) {
  const handle = tray(page, id).locator(`[data-tray-resize="${edge}"]`); await expect(handle).toBeVisible();
  const box = (await handle.boundingBox())!;
  await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2); await page.mouse.down();
  await page.mouse.move(box.x + box.width / 2 + dx, box.y + box.height / 2 + dy, { steps: 10 }); await page.mouse.up();
}
async function toggle(page: Page, id: string, projectId: string) {
  const prior = get(await stored(page.request, projectId), id).collapsed;
  await inspectorPanel(page, 'canvas'); const search = page.getByRole('searchbox', { name: 'Search Work nodes', exact: true });
  await search.fill(id); await page.locator(`[data-outline-node="${id}"] .inspector-tree-title`).click(); await search.fill('');
  await inspectorPanel(page, 'relations'); await page.getByRole('button', { name: 'Toggle contents from relations', exact: true }).click();
  await expect.poll(async () => get(await stored(page.request, projectId), id).collapsed).toBe(!prior);
}

test('resizing scales original children and routes, is one undoable edit, and restores size and position after ancestor collapse and reload', async ({ page, request }, info) => {
  test.setTimeout(90000); const f = fixture(); await seed(page, request, f.project);
  // Fixture authoring uses fallback text metrics. Save the editor's native-font
  // layout through an ordinary non-layout edit before comparing exact geometry.
  await inspectorPanel(page, 'canvas'); const search = page.getByRole('searchbox', { name: 'Search Work nodes', exact: true });
  await search.fill(f.sourceId); await page.locator(`[data-outline-node="${f.sourceId}"] .inspector-tree-title`).click(); await search.fill('');
  await inspectorPanel(page, 'properties'); const tag = page.locator('.inspector').getByLabel('Tag', { exact: true });
  await tag.fill('Resizable branch baseline'); await tag.press('Tab');
  await expect.poll(async () => get(await stored(request, f.project.id), f.sourceId).tag).toBe('Resizable branch baseline');
  const before = await stored(request, f.project.id), oldFrame = expansionTrayBounds(before, f.sourceId)!;
  const oldRendered = await geometry(tray(page, f.sourceId));
  for (const key of ['x', 'y', 'width', 'height'] as const) expect(oldRendered[key]).toBeCloseTo(oldFrame[key], 2);
  await resize(page, f.sourceId, 'se', 55, 80);
  await expect.poll(async () => get(await stored(request, f.project.id), f.sourceId).expansionTray?.size).toBeDefined();
  const after = await stored(request, f.project.id), frame = await geometry(tray(page, f.sourceId)), state = get(after, f.sourceId).expansionTray!;
  const source = get(after, f.sourceId), side = EXPANSION_TRAY_PADDING, top = side + expansionTrayHeader(source);
  const previousTop = side + expansionTrayHeader(get(before, f.sourceId));
  const exactFrame = { x: source.x + state.offset.x - side, y: source.y + state.offset.y - top, width: state.size!.width, height: state.size!.height };
  expect(frame.width).toBeGreaterThan(oldFrame.width + 10); expect(frame.height).toBeGreaterThan(oldFrame.height + 10);
  expect(frame.x).toBeCloseTo(oldFrame.x, 2); expect(frame.y).toBeCloseTo(oldFrame.y, 2);
  expect(state.size!.width).toBeCloseTo(frame.width, 2); expect(state.size!.height).toBeCloseTo(frame.height, 2);
  expect(get(after, f.ownerId)).toEqual(get(before, f.ownerId)); expect(get(after, f.peerId)).toEqual(get(before, f.peerId));
  expect(get(after, f.sourceId)).toEqual({ ...get(before, f.sourceId), expansionTray: state, grid: source.grid });
  expect({ ...source.grid, cellWidth: get(before, f.sourceId).grid!.cellWidth, cellHeight: get(before, f.sourceId).grid!.cellHeight }).toEqual(get(before, f.sourceId).grid);
  expect(after.nodes.map(node => [node.id, node.parentId])).toEqual(before.nodes.map(node => [node.id, node.parentId]));
  const sx = (exactFrame.width - 2 * side) / (oldFrame.width - 2 * side), sy = (exactFrame.height - side - top) / (oldFrame.height - side - previousTop);
  for (const id of [...f.childIds, f.hiddenId]) {
    expect(get(after, id).width).toBeCloseTo(get(before, id).width * sx, 5);
    expect(get(after, id).height).toBeCloseTo(get(before, id).height * sy, 5);
    expect(get(after, id).style).toEqual(get(before, id).style);
  }
  const points = after.links.find(link => link.id === f.linkId)!.points!, previous = before.links.find(link => link.id === f.linkId)!.points!;
  points.forEach((point, index) => {
    expect(point.x).toBeCloseTo(exactFrame.x + side + (previous[index]!.x - oldFrame.x - side) * sx, 5);
    expect(point.y).toBeCloseTo(exactFrame.y + top + (previous[index]!.y - oldFrame.y - previousTop) * sy, 5);
  });
  await page.locator('.ms-viewport').focus(); await page.keyboard.press('Control+z');
  await expect.poll(async () => get(await stored(request, f.project.id), f.sourceId).expansionTray).toEqual(get(before, f.sourceId).expansionTray);
  await page.keyboard.press('Control+x'); await expect.poll(async () => get(await stored(request, f.project.id), f.sourceId).expansionTray).toEqual(state);
  await toggle(page, f.ownerId, f.project.id); await expect(tray(page, f.sourceId)).toHaveCount(0);
  await page.reload(); await toggle(page, f.ownerId, f.project.id); await expect(tray(page, f.sourceId)).toHaveCount(1);
  expect(await geometry(tray(page, f.sourceId))).toEqual(frame);
  const reopened = await stored(request, f.project.id);
  for (const id of [f.sourceId, ...f.childIds, f.hiddenId]) expect(get(reopened, id)).toEqual(get(after, id));
  expect(reopened.links.find(link => link.id === f.linkId)).toEqual(after.links.find(link => link.id === f.linkId));
  await page.getByRole('button', { name: 'Fit Work', exact: true }).click();
  await page.screenshot({ path: info.outputPath('resized-tray-restored-after-save-reopen.png') });
});

test('a tray replaces source-child fan lines with one solid tether and snaps flush to its enclosing container edge', async ({ page, request }, info) => {
  const f = fixture(); await seed(page, request, f.project);
  await expect(page.locator(`.ms-expansion-tether[data-tray-source-id="${f.sourceId}"]`)).toHaveCount(1);
  await expect(page.locator(`.ms-expansion-tether[data-tray-source-id="${f.sourceId}"]`)).toHaveCSS('stroke-dasharray', 'none');
  for (const id of f.fanIds) await expect(page.locator(`.ms-link[data-link-id="${id}"]`)).toHaveCount(0);
  await expect(page.locator(`.ms-link[data-link-id="${f.linkId}"]`)).toHaveCount(1);
  const background = page.locator(`.ms-expansion-tray-backdrop[data-tray-source-id="${f.sourceId}"]`), back = (await background.boundingBox())!;
  const outer = (await card(page, f.ownerId).boundingBox())!, old = (await tray(page, f.sourceId).boundingBox())!;
  const logical = await geometry(tray(page, f.sourceId));
  const start = { x: back.x + back.width / 2, y: back.y + .85 * EXPANSION_TRAY_PADDING * back.height / logical.height };
  await page.mouse.move(start.x, start.y); await page.mouse.down();
  await page.mouse.move(start.x + outer.x + outer.width + 5 - old.x, start.y + outer.y + 20 - old.y, { steps: 12 }); await page.mouse.up();
  const owner = await geometry(card(page, f.ownerId));
  await expect.poll(async () => (await geometry(tray(page, f.sourceId))).x).toBeCloseTo(owner.x + owner.width, 5);
  await expect.poll(async () => get(await stored(request, f.project.id), f.sourceId).expansionTray).not.toEqual(get(f.project, f.sourceId).expansionTray);
  const saved = await stored(request, f.project.id);
  const sourceState = get(saved, f.sourceId).expansionTray;
  expect(f.fanIds.every(id => saved.links.some(link => link.id === id))).toBe(true);
  await page.screenshot({ path: info.outputPath('single-connector-and-snapped-tray.png') });
  await page.reload();
  expect((await geometry(tray(page, f.sourceId))).x).toBeCloseTo(owner.x + owner.width, 5);
  expect(get(await stored(request, f.project.id), f.sourceId).expansionTray).toEqual(sourceState);
});

for (const pattern of ['window', 'gallery'] as const) test(`resized ${pattern} tray retains its complete content and a stable frame while browsing and reopening`, async ({ page, request }, info) => {
  test.setTimeout(60000); const f = fixture(pattern); await seed(page, request, f.project);
  await resize(page, f.sourceId, 'se', 65, 55);
  await expect.poll(async () => get(await stored(request, f.project.id), f.sourceId).expansionTray?.size).toBeDefined();
  const beforeBrowse = await stored(request, f.project.id), frame = await geometry(tray(page, f.sourceId));
  if (pattern === 'window') {
    const viewport = page.locator(`.ms-window-viewport[data-window-id="${f.sourceId}"]`);
    const extent = await viewport.evaluate(element => ({ width: element.clientWidth, height: element.clientHeight, fullWidth: element.scrollWidth }));
    expect(extent.width).toBeCloseTo(frame.width - 2 * EXPANSION_TRAY_PADDING, 0);
    expect(extent.height).toBeCloseTo(frame.height - 2 * EXPANSION_TRAY_PADDING - expansionTrayHeader(get(beforeBrowse, f.sourceId)), 0); expect(extent.fullWidth).toBeGreaterThan(extent.width);
    await viewport.evaluate(element => { element.scrollLeft = element.scrollWidth; element.dispatchEvent(new Event('scroll')); });
    expect(await viewport.evaluate(element => element.scrollLeft)).toBeGreaterThan(0);
  } else {
    await page.getByRole('button', { name: `Next object in ${nodeTag(get(beforeBrowse, f.sourceId))}`, exact: true }).click();
    expect(await geometry(tray(page, f.sourceId))).toEqual(frame);
    await expect(card(page, f.childIds[1]!)).toBeVisible();
  }
  expect(get(await stored(request, f.project.id), f.sourceId).expansionTray).toEqual(get(beforeBrowse, f.sourceId).expansionTray);
  await toggle(page, f.ownerId, f.project.id); await page.reload(); await toggle(page, f.ownerId, f.project.id);
  expect(await geometry(tray(page, f.sourceId))).toEqual(frame);
  const reopened = await stored(request, f.project.id);
  expect(reopened.nodes.map(node => node.id)).toEqual(beforeBrowse.nodes.map(node => node.id));
  expect(get(reopened, f.sourceId).expansionTray).toEqual(get(beforeBrowse, f.sourceId).expansionTray);
  await page.getByRole('button', { name: 'Fit Work', exact: true }).click();
  await page.screenshot({ path: info.outputPath(`resized-${pattern}-tray.png`) });
});
