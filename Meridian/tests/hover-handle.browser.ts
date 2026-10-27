import { test, expect, type APIRequestContext, type Locator, type Page } from '@playwright/test';
import { addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import { normalizeContainerLayouts } from '../src/core/grid.ts';
import type { MeridianProject } from '../src/core/types.ts';
import { inspectorPanel } from './ui-helpers.ts';

const card = (page: Page, id: string) => page.locator(`.ms-node[data-node-id="${id}"]`);
const tray = (page: Page, id: string) => page.locator(`.ms-expansion-tray[data-tray-source-id="${id}"]`);
const stored = async (request: APIRequestContext, id: string): Promise<MeridianProject> => (await request.get(`/api/projects/${id}`)).json();
const node = (project: MeridianProject, id: string) => project.nodes.find(item => item.id === id)!;
const positions = (project: MeridianProject) => project.nodes.map(item => ({ id: item.id, x: item.x, y: item.y }));
const geometry = (element: Locator) => element.evaluate(element => {
  const style = (element as HTMLElement).style;
  return { x: parseFloat(style.left), y: parseFloat(style.top), width: parseFloat(style.width), height: parseFloat(style.height) };
});
// Disclosure name strips are intentionally absent. This saved-editor regression
// retains the original branch/route/tray transaction assertions with ordinary hits.
async function drag(page: Page, hoverId: string, ownerId: string, dx: number, dy: number) {
  const owner = card(page, ownerId), box = (await owner.boundingBox())!;
  let at = { x: box.x + box.width / 2, y: box.y + 20 };
  if (ownerId !== hoverId && !(await owner.getAttribute('class'))?.includes('ms-node-container')) {
    const backdrop = page.locator(`.ms-expansion-tray-backdrop[data-tray-source-id="${ownerId}"]`), rim = (await backdrop.boundingBox())!;
    at = { x: rim.x + 4, y: rim.y + rim.height / 2 };
  }
  await page.mouse.move(at.x, at.y); await page.mouse.down(); await page.mouse.move(at.x + dx, at.y + dy, { steps: 8 }); await page.mouse.up();
}

test('real editor node and tray drags make single undoable edits and preserve original branches and tray geometry through reload', async ({ page, request }, info) => {
  test.setTimeout(90000); const errors: string[] = []; page.on('pageerror', error => errors.push(error.message));
  let project = newProject('Hover drag handle integration'); const canvasId = project.entryCanvasId;
  project.canvases[0]!.snapping = { stickyEdges: false, smartAlign: false, snapToGrid: false };
  const owner = addNode(project, canvasId, 'container', '');
  Object.assign(owner, { tag: 'Handle parent', x: 60, y: 90, width: 480, height: 260, sizeMode: 'fixed', collapsed: false, containerVariant: 'contained', childLayout: 'contained', childArrangement: 'free', style: { shape: 'rectangle', childPadding: 5, fontFamily: 'georgia' } });
  const source = addNode(project, canvasId, 'media', 'Original source');
  Object.assign(source, { tag: 'Handle tray source', parentId: owner.id, x: 80, y: 120, width: 200, height: 190, sizeMode: 'fixed', collapsed: false, containerVariant: 'horizontal', childPosition: 'manual' });
  const peer = addNode(project, canvasId, 'media', 'Peer');
  Object.assign(peer, { parentId: owner.id, x: 310, y: 120, width: 200, height: 190, sizeMode: 'fixed', collapsed: true, childPosition: 'manual' });
  const children = Array.from({ length: 2 }, (_, index) => {
    const item = addNode(project, canvasId, 'media', `Real child ${index + 1}`);
    Object.assign(item, { parentId: source.id, x: 700 + index * 220, y: 180, width: 220, height: 130, sizeMode: 'fixed', collapsed: true, childPosition: 'auto', style: { shape: 'rectangle', fontFamily: 'consolas' } }); return item;
  });
  const linkId = crypto.randomUUID();
  project.links.push({ id: linkId, canvasId, parentId: source.id, sourceId: children[0]!.id, targetId: children[1]!.id, kind: 'visual', relation: 'generic', points: [{ x: 910, y: 160 }] });
  project = normalizeContainerLayouts(project);
  const created = await request.post('/api/projects', { data: commitSnapshots(project) }); expect(created.ok(), await created.text()).toBe(true);
  await page.goto(`/?project=${project.id}`); await expect(tray(page, source.id)).toBeVisible();
  // Save the native-font layout before measuring movement, just as an ordinary
  // user edit does; subsequent comparisons isolate the handle transaction.
  await inspectorPanel(page, 'canvas'); const search = page.getByRole('searchbox', { name: 'Search Work nodes', exact: true });
  await search.fill(owner.id); await page.locator(`[data-outline-node="${owner.id}"] .inspector-tree-title`).click(); await search.fill('');
  await inspectorPanel(page, 'properties'); const tag = page.locator('.inspector').getByLabel('Tag', { exact: true });
  await tag.fill('Handle parent baseline'); await tag.press('Tab');
  await expect.poll(async () => node(await stored(request, project.id), owner.id).tag).toBe('Handle parent baseline');
  await page.getByRole('button', { name: 'Fit Work', exact: true }).click();
  const before = await stored(request, project.id), beforeTray = await geometry(tray(page, source.id));
  await drag(page, source.id, owner.id, 36, 24);
  await expect.poll(async () => (await stored(request, project.id)).revision).toBe(before.revision + 1);
  const moved = await stored(request, project.id), dx = node(moved, owner.id).x - node(before, owner.id).x, dy = node(moved, owner.id).y - node(before, owner.id).y;
  expect(dx).not.toBe(0); expect(dy).not.toBe(0);
  for (const id of [source.id, peer.id, ...children.map(item => item.id)]) {
    expect(node(moved, id).x - node(before, id).x).toBeCloseTo(dx, 5); expect(node(moved, id).y - node(before, id).y).toBeCloseTo(dy, 5);
  }
  expect(moved.links.find(item => item.id === linkId)!.points).toEqual(before.links.find(item => item.id === linkId)!.points!.map(point => ({ x: point.x + dx, y: point.y + dy })));
  expect(node(moved, source.id).expansionTray).toEqual(node(before, source.id).expansionTray);
  const movedTray = await geometry(tray(page, source.id)); expect(movedTray.x - beforeTray.x).toBeCloseTo(dx, 4); expect(movedTray.y - beforeTray.y).toBeCloseTo(dy, 4);
  await page.locator('.ms-viewport').focus(); await page.keyboard.press('Control+z');
  await expect.poll(async () => positions(await stored(request, project.id))).toEqual(positions(before));
  await page.keyboard.press('Control+x'); await expect.poll(async () => positions(await stored(request, project.id))).toEqual(positions(moved));
  const trayBefore = await stored(request, project.id);
  await page.keyboard.down('Alt'); await drag(page, children[0]!.id, source.id, 40, 24); await page.keyboard.up('Alt');
  await expect.poll(async () => (await stored(request, project.id)).revision).toBe(trayBefore.revision + 1);
  const trayMoved = await stored(request, project.id), oldSource = node(trayBefore, source.id), newSource = node(trayMoved, source.id);
  expect(newSource.x).toBe(oldSource.x); expect(newSource.y).toBe(oldSource.y); expect(node(trayMoved, owner.id)).toEqual(node(trayBefore, owner.id));
  const tx = newSource.expansionTray!.offset.x - oldSource.expansionTray!.offset.x, ty = newSource.expansionTray!.offset.y - oldSource.expansionTray!.offset.y;
  expect(tx).toBeGreaterThan(0); expect(ty).toBeGreaterThan(0);
  for (const item of children) { expect(node(trayMoved, item.id).x - node(trayBefore, item.id).x).toBeCloseTo(tx, 5); expect(node(trayMoved, item.id).y - node(trayBefore, item.id).y).toBeCloseTo(ty, 5); }
  const savedFrame = await geometry(tray(page, source.id));
  await page.reload(); await expect(tray(page, source.id)).toBeVisible(); expect(await geometry(tray(page, source.id))).toEqual(savedFrame);
  expect(node(await stored(request, project.id), source.id).expansionTray).toEqual(newSource.expansionTray);
  expect((await stored(request, project.id)).nodes.map(item => [item.id, item.parentId])).toEqual(before.nodes.map(item => [item.id, item.parentId]));
  await page.getByRole('button', { name: 'Fit Work', exact: true }).click(); await card(page, children[0]!.id).hover();
  await page.screenshot({ path: info.outputPath('saved-tray-and-container-drag-handles.png') });
  await card(page, source.id).locator('.ms-node-title').dblclick(); await expect(tray(page, source.id)).toHaveCount(0);
  await expect.poll(async () => node(await stored(request, project.id), source.id).collapsed).toBe(true);
  expect(node(await stored(request, project.id), owner.id).collapsed).toBe(false); expect(errors).toEqual([]);
});
