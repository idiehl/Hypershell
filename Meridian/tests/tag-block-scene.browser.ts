import { test, expect, type Page, type APIRequestContext } from '@playwright/test';
import { addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import type { MeridianProject } from '../src/core/types.ts';
import { inspectorPanel } from './ui-helpers.ts';

const tile = (page: Page, id: string) => page.locator(`.ms-node[data-node-id="${id}"]`);
const stored = async (request: APIRequestContext, id: string): Promise<MeridianProject> => (await request.get(`/api/projects/${id}`)).json();
async function install(page: Page, request: APIRequestContext, project: MeridianProject) {
  const response = await request.post('/api/projects', { data: commitSnapshots(project) });
  expect(response.ok(), await response.text()).toBe(true); await page.goto(`/?project=${project.id}`);
}
async function target(page: Page, id: string) {
  await inspectorPanel(page, 'canvas'); const search = page.getByRole('searchbox', { name: 'Search Work nodes', exact: true });
  await search.fill(id); await page.locator(`[data-jump-to-node="${id}"]`).click(); await search.fill('');
}

test('a migrated blank Block remains visually empty, reveals children and moves its whole unbounded branch', async ({ page, request }, info) => {
  const project = newProject('Block graph behavior'), canvas = project.canvases[0]!;
  canvas.snapping = { stickyEdges: false, smartAlign: false, snapToGrid: false };
  const block = addNode(project, canvas.id, 'block', '');
  Object.assign(block, { tag: 'A structural branch identity', x: 100, y: 140, width: 240, height: 150, collapsed: true, sizeMode: 'fixed', style: { shape: 'rectangle', fill: '#315e52' } });
  const child = addNode(project, canvas.id, 'media', 'A revealed passage');
  Object.assign(child, { parentId: block.id, childPosition: 'manual', x: 420, y: 140, width: 240, height: 150, sizeMode: 'fixed', text: 'Content belongs to the child, never to the blank organizational block.' });
  await install(page, request, project); await target(page, block.id);
  const parent = tile(page, block.id); await expect(parent).toHaveText(''); await expect(parent).toHaveAttribute('aria-label', /A structural branch identity/);
  await expect(parent).toHaveClass(/ms-node-media/);
  // A retired Block migrates to ordinary Media; empty structural wrappers are
  // allowed, but no visible title, content placeholder or editing target is.
  await expect(parent.locator('.ms-node-title,.ms-text,.ms-text-preview,.ms-image,.ms-video,.ms-file-placeholder,[data-editable-node-id]')).toHaveCount(0);
  await expect(tile(page, child.id)).toHaveCount(0);
  const collapsedBounds = (await parent.boundingBox())!;
  await parent.dblclick({ position: { x: collapsedBounds.width / 2, y: collapsedBounds.height / 2 } }); await expect(tile(page, child.id)).toBeVisible();
  await expect(parent).toHaveText(''); await expect(parent).toHaveAttribute('aria-expanded', 'true');
  await expect.poll(async () => (await stored(request, project.id)).nodes.find(node => node.id === block.id)?.collapsed).toBe(false);
  const before = await stored(request, project.id), bounds = (await parent.boundingBox())!;
  await page.mouse.move(bounds.x + bounds.width / 2, bounds.y + bounds.height / 2); await page.mouse.down();
  await page.mouse.move(bounds.x + bounds.width / 2 + 48, bounds.y + bounds.height / 2 + 32, { steps: 8 }); await page.mouse.up();
  await expect.poll(async () => (await stored(request, project.id)).nodes.find(node => node.id === block.id)?.x).not.toBe(before.nodes.find(node => node.id === block.id)!.x);
  const after = await stored(request, project.id), originalParent = before.nodes.find(node => node.id === block.id)!, movedParent = after.nodes.find(node => node.id === block.id)!, originalChild = before.nodes.find(node => node.id === child.id)!, movedChild = after.nodes.find(node => node.id === child.id)!;
  expect(movedParent.kind).toBe('media'); expect(after.nodes.map(node => node.id)).toEqual(project.nodes.map(node => node.id));
  expect(movedChild.x - originalChild.x).toBeCloseTo(movedParent.x - originalParent.x, 5);
  expect(movedChild.y - originalChild.y).toBeCloseTo(movedParent.y - originalParent.y, 5);
  await page.screenshot({ path: info.outputPath('blank-block-with-revealed-child.png') });
});

test('Gallery pages preserve blank Media and Block objects without identity or placeholder content', async ({ page, request }, info) => {
  const project = newProject('Blank gallery objects'), canvas = project.canvases[0]!;
  canvas.snapping = { stickyEdges: false, smartAlign: false, snapToGrid: false };
  const gallery = addNode(project, canvas.id, 'container', '');
  Object.assign(gallery, { tag: 'A private gallery identity', x: 100, y: 100, width: 340, height: 220, sizeMode: 'fixed', collapsed: false, containerVariant: 'gallery', childLayout: 'contained' });
  const empty = addNode(project, canvas.id, 'media', ''); Object.assign(empty, { tag: 'Empty media identity', parentId: gallery.id, text: '', width: 200, height: 120, sizeMode: 'fixed' });
  const block = addNode(project, canvas.id, 'block', ''); Object.assign(block, { tag: 'Dormant Block identity', parentId: gallery.id, title: 'Dormant old title', text: 'Dormant old body', width: 200, height: 120, sizeMode: 'fixed' });
  await install(page, request, project); await target(page, gallery.id);
  const parent = tile(page, gallery.id), pageItem = parent.locator('.ms-gallery-item');
  await expect(parent.locator(':scope > .ms-node-header')).toHaveCount(0);
  await expect(pageItem).toHaveAttribute('data-gallery-item-id', empty.id); await expect(pageItem).toHaveText('');
  await expect(pageItem.locator('.ms-gallery-item-title')).toHaveCount(0); await expect(parent.locator('.ms-gallery-position')).toHaveCount(0);
  await parent.getByRole('button', { name: 'Next object in A private gallery identity', exact: true }).click();
  await expect(pageItem).toHaveAttribute('data-gallery-item-id', block.id); await expect(pageItem).toHaveText('');
  await expect(parent).not.toContainText('identity'); await expect(parent).not.toContainText('Dormant'); await expect(parent.locator('.ms-gallery-position')).toHaveCount(0);
  await expect(pageItem.locator('.ms-gallery-item-title,.ms-text,.ms-image,.ms-video')).toHaveCount(0);
  await page.screenshot({ path: info.outputPath('blank-block-gallery-page.png') });
});
