import { test, expect, type Page, type APIRequestContext } from '@playwright/test';
import { addCanvas, addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import { reflowContainedChildren } from '../src/core/grid.ts';
import { revealChildren } from '../src/core/reveal.ts';
import type { MeridianProject } from '../src/core/types.ts';

const card = (page: Page, id: string) => page.locator(`.ms-node[data-node-id="${id}"]`);
const tray = (page: Page, id: string) => page.locator(`.ms-expansion-tray[data-tray-source-id="${id}"]`);
const stored = async (request: APIRequestContext, id: string): Promise<MeridianProject> => (await request.get(`/api/projects/${id}`)).json();
async function fixture(page: Page, request: APIRequestContext) {
  let project = newProject('Gallery frame and hover integration');
  project.canvases[0]!.snapping = { stickyEdges: false, smartAlign: false, snapToGrid: false };
  const destination = addCanvas(project, 'Destination'), owner = addNode(project, project.entryCanvasId, 'container', '');
  Object.assign(owner, { tag: 'Owner Tag', x: 70, y: 100, width: 520, height: 300, sizeMode: 'fixed', collapsed: false, containerVariant: 'horizontal' });
  const source = addNode(project, project.entryCanvasId, 'canvas-link', 'Collection doorway');
  Object.assign(source, { tag: 'Gallery Tag', parentId: owner.id, targetId: destination.rootId, containerVariant: 'gallery', childLayout: 'spatial', collapsed: true, sizeMode: 'fixed' });
  const items = ['Short', 'A title with\ntwo lines', 'A much longer title with several words that may need more than two lines to fit above the cube'].map((title, index) => {
    const node = addNode(project, project.entryCanvasId, 'canvas-link', title);
    Object.assign(node, { parentId: source.id, targetId: destination.rootId, x: 800, y: 100, width: 200 + index * 40, height: 160 + index * 20, sizeMode: 'auto', collapsed: true,
      style: { shape: 'rectangle', titleSize: 18, fontSize: 18 } }); return node;
  });
  project = revealChildren(reflowContainedChildren(project, owner.id), source.id, true);
  const response = await request.post('/api/projects', { data: commitSnapshots(project) });
  expect(response.ok(), await response.text()).toBe(true); project = await response.json();
  await page.goto(`/?project=${project.id}`); await expect(page.locator('.workspace')).toBeVisible();
  await page.getByRole('button', { name: 'Fit Work', exact: true }).click();
  return { project, owner, source, items };
}

test('exterior Gallery original frames, cubes and tray stay identical while paging through different title lengths in the editor', async ({ page, request }, info) => {
  const f = await fixture(page, request), frames = [], cubes = [], trays = [], navigation = [];
  const controls = page.locator(`[data-external-gallery="${f.source.id}"]`);
  for (let index = 0; index < f.items.length; index++) {
    const item = f.items[index]!; await expect(card(page, item.id)).toBeVisible();
    for (const other of f.items.filter(other => other.id !== item.id)) await expect(card(page, other.id)).toHaveCount(0);
    frames.push((await card(page, item.id).boundingBox())!); cubes.push((await card(page, item.id).locator('.ms-canvas-open').boundingBox())!);
    trays.push((await tray(page, f.source.id).boundingBox())!); navigation.push((await controls.boundingBox())!);
    await expect(controls.getByRole('button')).toHaveCount(2);
    await expect(controls.locator('.ms-gallery-position')).toHaveCount(0);
    const status = controls.locator('.ms-gallery-position-sr');
    expect(await status.evaluate(element => getComputedStyle(element).clip)).not.toBe('auto');
    if (index < f.items.length - 1) await controls.getByRole('button', { name: 'Next object in Gallery Tag', exact: true }).click();
  }
  for (const collection of [frames, trays, navigation]) for (const box of collection) for (const key of ['x', 'y', 'width', 'height'] as const)
    expect(box[key]).toBeCloseTo(collection[0]![key], 3);
  for (const box of cubes) { expect(box.width).toBeCloseTo(cubes[0]!.width, 3); expect(box.height).toBeCloseTo(cubes[0]!.height, 3); }
  expect(await stored(request, f.project.id)).toEqual(f.project);
  await page.screenshot({ path: info.outputPath('uniform-gallery-page-and-cube.png') });
});

test('ordinary double clicks collapse a tray and its enclosing parent without editing their text and restores saved geometry', async ({ page, request }, info) => {
  const f = await fixture(page, request), before = (await tray(page, f.source.id).boundingBox())!;
  await card(page, f.items[0]!.id).hover();
  await expect(page.locator('.ms-container-hover-bar')).toHaveCount(0);
  const rim = page.locator(`.ms-expansion-tray-backdrop[data-tray-source-id="${f.source.id}"]`), rimBox = (await rim.boundingBox())!; await rim.dblclick({ position: { x: 4, y: rimBox.height / 2 } });
  await expect(tray(page, f.source.id)).toHaveCount(0);
  await expect.poll(async () => (await stored(request, f.project.id)).nodes.find(node => node.id === f.source.id)!.collapsed).toBe(true);
  // The source keeps its original identity and title. Its independent text and
  // button regions still leave the normal node backdrop available for disclosure.
  const sourceBox = (await card(page, f.source.id).boundingBox())!;
  await card(page, f.source.id).dblclick({ position: { x: sourceBox.width - 8, y: sourceBox.height / 2 } });
  await expect(tray(page, f.source.id)).toBeVisible();
  const restored = (await tray(page, f.source.id).boundingBox())!;
  for (const key of ['x', 'y', 'width', 'height'] as const) expect(restored[key]).toBeCloseTo(before[key], 2);
  await card(page, f.owner.id).dblclick({ position: { x: 20, y: 20 } }); await expect(card(page, f.source.id)).toHaveCount(0);
  await expect.poll(async () => (await stored(request, f.project.id)).nodes.find(node => node.id === f.owner.id)!.collapsed).toBe(true);
  const saved = await stored(request, f.project.id);
  expect(saved.nodes.find(node => node.id === f.source.id)!.title).toBe(f.source.title);
  expect(saved.nodes.find(node => node.id === f.source.id)!.collapsed).toBe(false);
  expect(saved.nodes.find(node => node.id === f.owner.id)!.collapsed).toBe(true);
  expect(saved.nodes.map(node => node.id)).toEqual(f.project.nodes.map(node => node.id));
  await page.reload(); await expect(card(page, f.source.id)).toHaveCount(0);
  await page.screenshot({ path: info.outputPath('hover-collapse-preserves-original-branch.png') });
});
