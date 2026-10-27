import { test, expect, type APIRequestContext, type Page } from '@playwright/test';
import { addCanvas, addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import { normalizeLibraryDocument, type LibraryCanvasDocument, type LibraryCatalogItem } from '../src/core/library.ts';
import type { MeridianProject } from '../src/core/types.ts';
import { inspectorPanel } from './ui-helpers.ts';

const row = (page: Page, id: string) => page.locator(`[data-outline-node="${id}"]`);
const drawer = (page: Page, id: string) => page.locator(`[data-outline-details="${id}"]`);
const more = (page: Page, id: string) => page.locator(`[data-outline-more="${id}"]`);
const tile = (page: Page, id: string) => page.locator(`.ms-node[data-node-id="${id}"]`);
const search = (page: Page) => page.getByRole('searchbox', { name: 'Search Work nodes', exact: true });
const stored = async (request: APIRequestContext, id: string): Promise<MeridianProject> => (await request.get(`/api/projects/${id}`)).json();
const library = async (request: APIRequestContext): Promise<LibraryCanvasDocument> => (await request.get('/api/library')).json();
const camera = async (page: Page) => page.locator('.ms-world').evaluate(el => {
  const style = getComputedStyle(el), matrix = new DOMMatrixReadOnly(style.transform);
  // The shared renderer rebases its floating origin after viewport resizing.
  // Compare the actual camera rather than its equivalent internal transform.
  return { x: matrix.e + parseFloat(style.getPropertyValue('--ms-render-origin-x')) * matrix.a, y: matrix.f + parseFloat(style.getPropertyValue('--ms-render-origin-y')) * matrix.d, scale: matrix.a };
});
const errors = new WeakMap<Page, string[]>();
test.beforeEach(async ({ page }) => { const messages: string[] = []; errors.set(page, messages); page.on('pageerror', error => messages.push(error.message)); });
test.afterEach(async ({ page }) => { expect(errors.get(page) ?? [], 'Sidebar disclosure and tier navigation should not trigger browser exceptions.').toEqual([]); });

async function seed(page: Page, request: APIRequestContext) {
  const project = newProject('Linked research'), entry = project.canvases[0]!, second = addCanvas(project, 'Second canvas');
  entry.title = 'Entry canvas'; project.nodes[0]!.title = entry.title;
  const other = newProject('Remote research'), remote = other.canvases[0]!; remote.title = 'Remote canvas'; other.nodes[0]!.title = remote.title;
  const place = (node: MeridianProject['nodes'][number], x: number, y: number) => Object.assign(node, { x, y, width: 240, height: 130, sizeMode: 'fixed' as const, placement: 'free' as const, childLayout: 'spatial' as const });
  const parent = place(addNode(project, entry.id, 'media', 'A parent'), 100, 100); parent.collapsed = true;
  const child = place(addNode(project, entry.id, 'media', 'A child'), 540, 110); child.parentId = parent.id; child.collapsed = true; child.text = 'Private body content should stay outside the Details drawer.';
  const grandchild = place(addNode(project, entry.id, 'media', 'A grandchild'), 540, 360); grandchild.parentId = child.id;
  const peer = place(addNode(project, entry.id, 'media', 'Independent peer'), 900, 100);
  const local = place(addNode(project, entry.id, 'canvas-link', 'Local doorway'), 540, 610); local.parentId = parent.id; local.targetId = second.rootId;
  const external = place(addNode(project, second.id, 'canvas-link', 'Remote doorway'), 100, 100); external.canvasTarget = { projectId: other.id, canvasId: remote.id };
  const remoteContent = place(addNode(other, remote.id, 'media', 'Remote content'), 450, 100);
  const back = place(addNode(other, remote.id, 'canvas-link', 'Cyclic return'), 100, 100); back.canvasTarget = { projectId: project.id, canvasId: entry.id };
  project.links.push({ id: crypto.randomUUID(), canvasId: entry.id, sourceId: parent.id, targetId: peer.id, kind: 'visual', relation: 'mutual' });
  for (const source of [project, other]) {
    for (const canvas of source.canvases) canvas.snapping = { stickyEdges: false, smartAlign: false, snapToGrid: false };
    const response = await request.post('/api/projects', { data: commitSnapshots(source) }); expect(response.ok(), await response.text()).toBe(true);
  }
  const catalog = await (await request.get('/api/library/catalog')).json() as LibraryCatalogItem[];
  const home = normalizeLibraryDocument(await library(request), catalog);
  const recovery = await request.get('/api/library/recovery');
  if (recovery.ok()) { const pending = await recovery.json(); expect((await request.delete('/api/library/recovery', { data: { baseRevision: pending.baseRevision, savedAt: pending.savedAt } })).ok()).toBe(true); }
  const root = home.project.nodes.find(node => node.id === home.project.canvases.find(canvas => canvas.id === home.project.entryCanvasId)!.rootId)!;
  home.project.nodes = [root]; home.project.canvases = [home.project.canvases.find(canvas => canvas.id === home.project.entryCanvasId)!]; home.project.links = []; home.project.assets = []; home.project.snapshots = {}; home.canvasTargets = {};
  home.discoveredTargets = catalog.flatMap(item => item.canvases.map(canvas => `${item.id}:${canvas.id}`));
  const collection = place(addNode(home.project, root.canvasId, 'media', 'My research collection'), 100, 100); collection.collapsed = false;
  const launch = place(addNode(home.project, root.canvasId, 'canvas-link', 'Enter research'), 450, 100); launch.parentId = collection.id; launch.canvasTarget = { projectId: project.id, canvasId: entry.id }; home.canvasTargets[launch.id] = launch.canvasTarget;
  const saved = await request.put('/api/library', { data: { library: home, expectedRevision: home.revision } }); expect(saved.ok(), await saved.text()).toBe(true);
  await page.goto(`/?project=${project.id}&canvas=${entry.id}`); await inspectorPanel(page, 'canvas'); await expect(row(page, parent.id)).toBeVisible();
  return { project, entry, second, other, remote, parent, child, grandchild, peer, local, external, remoteContent, back, home: await saved.json() as LibraryCanvasDocument, root, collection, launch };
}
async function showTier(page: Page, id: string, tier: number) {
  await search(page).fill(id); await expect(row(page, id)).toBeVisible();
  if (await more(page, id).getAttribute('aria-expanded') !== 'true') await more(page, id).click();
  await expect(drawer(page, id).locator('[data-node-tier]')).toHaveAttribute('data-node-tier', String(tier));
  await expect(drawer(page, id).locator('[data-node-tier]')).toHaveAttribute('aria-label', `Tier ${tier}`);
}
async function openLauncher(page: Page, id: string) {
  await search(page).fill(id); await row(page, id).locator('[data-jump-to-node]').click();
  await tile(page, id).locator('.ms-canvas-open').click(); await expect(page.locator('.workspace')).toBeVisible();
  await inspectorPanel(page, 'canvas');
}

test('Canvas sidebar has compact chrome, exact search wording and square Details buttons', async ({ page, request }, info) => {
  const f = await seed(page, request);
  await expect(search(page)).toHaveAttribute('placeholder', 'Search a GUID or Name');
  await expect(page.locator('.inspector-outline-count')).toHaveCount(0); await expect(page.locator('.save-state')).toHaveCount(0);
  for (const id of [f.entry.rootId, f.parent.id, f.peer.id]) {
    await expect(more(page, id)).toHaveAttribute('title', 'Details'); const bounds = (await more(page, id).boundingBox())!;
    expect(Math.abs(bounds.width - bounds.height)).toBeLessThan(.1);
    await more(page, id).hover(); await expect(more(page, id)).toHaveAttribute('title', 'Details');
  }
  await page.screenshot({ path: info.outputPath('compact-sidebar-and-header.png') });
});

test('solid indentation guides grow with generations and only parents show the expandable symbol', async ({ page, request }, info) => {
  const f = await seed(page, request); await row(page, f.parent.id).locator('.inspector-tree-title').click(); await row(page, f.child.id).locator('.inspector-tree-title').click();
  const guide = async (id: string) => row(page, id).evaluate(el => { const p = getComputedStyle(el, '::before'); return { content: p.content, width: parseFloat(p.width), border: p.borderTopStyle, left: parseFloat(p.left) }; });
  expect((await guide(f.entry.rootId)).content).toBe('none');
  const parent = await guide(f.parent.id), child = await guide(f.child.id), grandchild = await guide(f.grandchild.id);
  expect(parent.border).toBe('solid'); expect(parent.width).toBeGreaterThan(0); expect(child.width).toBeGreaterThan(parent.width); expect(grandchild.width).toBeGreaterThan(child.width);
  expect(parent.left).toBeLessThan(0); expect(child.left).toBeLessThan(parent.left);
  for (const id of [f.entry.rootId, f.parent.id, f.child.id]) await expect(row(page, id).locator(`[data-outline-expandable="${id}"] svg`)).toHaveCount(1);
  await expect(row(page, f.grandchild.id).locator('[data-outline-expandable]')).toHaveCount(0); await expect(row(page, f.peer.id).locator('[data-outline-expandable]')).toHaveCount(0);
  const mark = row(page, f.parent.id).locator('[data-outline-expandable]'); expect(await mark.locator('rect').count()).toBe(2);
  await page.locator('.inspector').screenshot({ path: info.outputPath('generation-guides.png') });
});

test('single-clicking rows selects without camera movement or disclosing canvas content', async ({ page, request }) => {
  const f = await seed(page, request), before = await stored(request, f.project.id), beforeCamera = await camera(page);
  await row(page, f.parent.id).click({ position: { x: 2, y: 8 } }); await expect(tile(page, f.parent.id)).toHaveAttribute('aria-selected', 'true'); await expect(row(page, f.child.id)).toBeVisible();
  expect(await camera(page)).toEqual(beforeCamera); await expect(tile(page, f.child.id)).toHaveCount(0);
  await row(page, f.child.id).locator('.inspector-tree-title').click(); await expect(row(page, f.child.id)).toHaveAttribute('aria-selected', 'true'); await expect(tile(page, f.child.id)).toHaveCount(0);
  expect(await camera(page)).toEqual(beforeCamera); expect(await stored(request, f.project.id)).toEqual(before);
  await row(page, f.peer.id).locator('.inspector-tree-title').click(); await expect(tile(page, f.peer.id)).toHaveAttribute('aria-selected', 'true'); await expect(tile(page, f.parent.id)).toHaveAttribute('aria-selected', 'false');
  await row(page, f.child.id).locator('[data-jump-to-node]').click(); await expect(tile(page, f.child.id)).toHaveAttribute('aria-selected', 'true');
  expect(await camera(page)).not.toEqual(beforeCamera);
});

test('Details remains minimal with counts, tier, GUID and conditional deletion', async ({ page, request }, info) => {
  const f = await seed(page, request); await showTier(page, f.parent.id, 4); const box = drawer(page, f.parent.id);
  await expect(box.locator('[data-subnode-count]')).toHaveAttribute('data-subnode-count', '2'); await expect(box.locator('[data-link-count]')).toHaveAttribute('data-link-count', '1');
  await expect(box).toContainText(f.parent.id); await expect(box).not.toContainText(f.child.title); await expect(box).not.toContainText(f.local.title); await expect(box).not.toContainText(f.peer.title); await expect(box.locator('button')).toHaveCount(1);
  await expect(box.locator('[data-delete-node]')).toBeVisible(); await expect(box.locator('[data-node-tier] svg')).toHaveCount(1);
  const before = await stored(request, f.project.id); await more(page, f.parent.id).click(); await expect(box).toBeHidden(); expect(await stored(request, f.project.id)).toEqual(before);
  await more(page, f.parent.id).click(); await row(page, f.parent.id).screenshot({ path: info.outputPath('minimal-details-with-tier.png') });
});

test('tiers continue from the library through local and external canvas doorways', async ({ page, request }) => {
  const f = await seed(page, request);
  await page.goto('/'); await inspectorPanel(page, 'canvas'); await showTier(page, f.root.id, 1); await showTier(page, f.collection.id, 2); await showTier(page, f.launch.id, 3);
  await openLauncher(page, f.launch.id); await expect(page).toHaveURL(new RegExp(`canvas=${f.entry.id}`)); await showTier(page, f.entry.rootId, 3); await showTier(page, f.parent.id, 4); await showTier(page, f.child.id, 5);
  await openLauncher(page, f.local.id); await expect(page).toHaveURL(new RegExp(`canvas=${f.second.id}`)); await showTier(page, f.second.rootId, 5); await showTier(page, f.external.id, 6);
  await openLauncher(page, f.external.id); await expect(page).toHaveURL(new RegExp(`project=${f.other.id}`)); await showTier(page, f.remote.rootId, 6); await showTier(page, f.remoteContent.id, 7);
});

test('tiers survive direct reload and browser history without growing through a canvas cycle', async ({ page, request }) => {
  const f = await seed(page, request); await showTier(page, f.entry.rootId, 3);
  await openLauncher(page, f.local.id); await showTier(page, f.second.rootId, 5); await openLauncher(page, f.external.id); await showTier(page, f.remote.rootId, 6);
  await page.reload(); await inspectorPanel(page, 'canvas'); await showTier(page, f.remote.rootId, 6);
  await openLauncher(page, f.back.id); await showTier(page, f.entry.rootId, 3);
  await page.goBack(); await inspectorPanel(page, 'canvas'); await showTier(page, f.remote.rootId, 6); await page.goBack(); await inspectorPanel(page, 'canvas'); await showTier(page, f.second.rootId, 5);
  await page.goForward(); await inspectorPanel(page, 'canvas'); await showTier(page, f.remote.rootId, 6);
});

test('narrow sidebar keeps guides, type and both buttons inside the panel with keyboard row selection', async ({ page, request }, info) => {
  const f = await seed(page, request); const divider = page.getByRole('separator', { name: 'Resize side panel', exact: true }); await divider.focus(); await divider.press('Home'); for (let at = 0; at < 3; at++) await divider.press('ArrowRight');
  await expect(divider).toHaveAttribute('aria-valuenow', '260'); await search(page).fill(f.grandchild.id); await expect(row(page, f.grandchild.id)).toBeVisible();
  const item = row(page, f.grandchild.id), title = item.locator('.inspector-tree-title'); await title.focus(); const beforeCamera = await camera(page); await title.press('Enter');
  await expect(item).toHaveAttribute('aria-selected', 'true'); await expect(title).toBeFocused(); expect(await camera(page)).toEqual(beforeCamera);
  const container = (await page.locator('.inspector').boundingBox())!, target = (await item.locator('[data-jump-to-node]').boundingBox())!, detailButton = (await more(page, f.grandchild.id).boundingBox())!;
  expect(target.x).toBeGreaterThan(container.x); expect(detailButton.x + detailButton.width).toBeLessThanOrEqual(container.x + container.width); expect(Math.abs(detailButton.width - detailButton.height)).toBeLessThan(.1);
  await more(page, f.grandchild.id).press('Enter'); await expect(drawer(page, f.grandchild.id)).toBeVisible(); expect(await page.locator('.inspector-panel-body').evaluate(el => el.scrollWidth <= el.clientWidth + 1)).toBe(true);
  await page.locator('.inspector').screenshot({ path: info.outputPath('minimum-width-details.png') });
});
