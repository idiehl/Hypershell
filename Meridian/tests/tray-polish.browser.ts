import { test, expect, type APIRequestContext, type Page } from '@playwright/test';
import { addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import { expansionTrayBounds } from '../src/core/expansion-tray.ts';
import type { MeridianNode, MeridianProject } from '../src/core/types.ts';
import { inspectorPanel } from './ui-helpers.ts';

const card = (page: Page, id: string) => page.locator(`.ms-node[data-node-id="${id}"]`);
const tray = (page: Page, id: string) => page.locator(`.ms-expansion-tray[data-tray-source-id="${id}"]`);
const stored = async (request: APIRequestContext, id: string): Promise<MeridianProject> => (await request.get(`/api/projects/${id}`)).json();
async function seed(page: Page, request: APIRequestContext, project: MeridianProject) {
  const response = await request.post('/api/projects', { data: commitSnapshots(project) }); expect(response.ok(), await response.text()).toBe(true);
  await page.goto(`/?project=${project.id}`); await expect(page.locator('.workspace')).toBeVisible();
  await page.getByRole('button', { name: 'Fit Work', exact: true }).click();
}
async function select(page: Page, id: string) {
  await inspectorPanel(page, 'canvas'); const search = page.getByRole('searchbox', { name: 'Search Work nodes', exact: true });
  await search.fill(id); await page.locator(`[data-outline-node="${id}"] .inspector-tree-title`).click(); await search.fill('');
}
async function toggle(page: Page, id: string) {
  await select(page, id); await inspectorPanel(page, 'relations');
  await page.getByRole('button', { name: 'Toggle contents from relations', exact: true }).click();
}
function fixed(project: MeridianProject, parent: MeridianNode, kind: MeridianNode['kind'], title: string, x: number, y: number, width: number, height: number) {
  const node = addNode(project, parent.canvasId, kind, title);
  Object.assign(node, { parentId: parent.id, x, y, width, height, sizeMode: 'fixed', childPosition: 'auto', collapsed: true,
    style: { shape: 'rectangle', childPadding: 20 }, containerVariant: 'horizontal' }); return node;
}

test('plain tray preserves an older authored outer frame and paints its source connector over an opaque enclosing container', async ({ page, request }, info) => {
  const failures: string[] = []; page.on('pageerror', error => failures.push(error.message));
  const project = newProject('Plain tray migration and foreground connector'), root = project.nodes[0]!;
  const owner = fixed(project, root, 'container', '', 100, 100, 500, 360);
  // Preserve this older composition's explicitly authored 20px inset; compact
  // default reallocation is covered separately from fixed tray migration.
  Object.assign(owner, { collapsed: false, childLayout: 'contained', style: { fill: '#302044', border: '#B7B7EF', shape: 'rectangle', childPadding: 20 } });
  const source = fixed(project, owner, 'media', 'Source label stays on source', 121, 121, 458, 318);
  Object.assign(source, { tag: 'Original owner', collapsed: false, expansionTray: { offset: { x: 579, y: 31 }, size: { width: 380, height: 300 } },
    style: { fill: '#183B32', border: '#71D0AE', borderWidth: 3, radius: 11, shape: 'rectangle' } });
  const child = fixed(project, source, 'media', 'Original content', 700, 152, 340, 228);
  const before = expansionTrayBounds(project, source.id)!; await seed(page, request, project);
  await expect(tray(page, source.id)).toBeVisible();
  await expect(page.locator('.ms-expansion-tray-header,.ms-expansion-tray-close,.ms-expansion-tray-badge,.ms-expansion-tray-title')).toHaveCount(0);
  const frame = await tray(page, source.id).evaluate(element => { const style = (element as HTMLElement).style; return { x: parseFloat(style.left), y: parseFloat(style.top), width: parseFloat(style.width), height: parseFloat(style.height) }; });
  for (const key of ['x', 'y', 'width', 'height'] as const) expect(frame[key]).toBeCloseTo(before[key], 2);
  const backdrop = page.locator(`.ms-expansion-tray-backdrop[data-tray-source-id="${source.id}"]`);
  await expect(backdrop).toHaveCSS('background-color', 'rgb(24, 59, 50)'); await expect(tray(page, source.id)).toHaveCSS('border-top-color', 'rgb(113, 208, 174)');
  await expect(tray(page, source.id)).toHaveCSS('border-top-width', '3px'); await expect(tray(page, source.id)).toHaveCSS('border-top-left-radius', '0px');
  const layers = await page.evaluate(({ ownerId, sourceId }) => {
    const enclosure = document.querySelector<HTMLElement>(`.ms-node[data-node-id="${ownerId}"]`)!;
    const path = document.querySelector<SVGPathElement>(`.ms-expansion-tether[data-tray-source-id="${sourceId}"]`)!;
    const source = document.querySelector<HTMLElement>(`.ms-node[data-node-id="${sourceId}"]`)!;
    const start = path.getPointAtLength(0), style = source.style;
    return { containerZ: parseFloat(getComputedStyle(enclosure).zIndex) || 0, connectorZ: parseFloat(getComputedStyle(path.parentElement!).zIndex) || 0,
      start: { x: start.x, y: start.y }, source: { x: parseFloat(style.left), y: parseFloat(style.top), width: parseFloat(style.width), height: parseFloat(style.height) } };
  }, { ownerId: owner.id, sourceId: source.id });
  expect(layers.connectorZ).toBeGreaterThan(layers.containerZ);
  expect(Math.min(Math.abs(layers.start.x - layers.source.x), Math.abs(layers.start.x - layers.source.x - layers.source.width),
    Math.abs(layers.start.y - layers.source.y), Math.abs(layers.start.y - layers.source.y - layers.source.height))).toBeLessThan(.02);
  await expect(card(page, child.id)).toHaveCount(1); expect(failures).toEqual([]);
  await page.screenshot({ path: info.outputPath('plain-tray-visible-source-connector.png') });
});

test('sixteen successive bounded generations inside a tray remain expandable and restore their disclosure after reload', async ({ page, request }, info) => {
  test.setTimeout(120000); const failures: string[] = []; page.on('pageerror', error => failures.push(error.message));
  const project = newProject('Deeply nested bounded generations'), root = project.nodes[0]!;
  const owner = fixed(project, root, 'container', '', 60, 70, 600, 420); Object.assign(owner, { collapsed: false, childLayout: 'contained' });
  const source = fixed(project, owner, 'media', 'Source', 100, 100, 180, 100); source.collapsed = false;
  const chain: string[] = []; let parent = source;
  for (let tier = 0; tier < 16; tier++) {
    const node = fixed(project, parent, 'container', `Generation ${tier + 1}`, 800, 100, 500, 360);
    node.childLayout = 'contained'; node.containerSizes = { expanded: { width: 500, height: 360, sizeMode: 'fixed' }, collapsed: { width: 180, height: 100, sizeMode: 'fixed' } };
    chain.push(node.id); parent = node;
  }
  const leaf = fixed(project, parent, 'media', 'Deep original content', 850, 150, 200, 120); leaf.text = 'Still the original node.';
  await seed(page, request, project);
  for (const [index, id] of chain.entries()) {
    await toggle(page, id);
    await expect.poll(async () => (await stored(request, project.id)).nodes.find(node => node.id === id)!.collapsed).toBe(false);
    const next = chain[index + 1] ?? leaf.id; await expect(card(page, next)).toHaveCount(1);
    const dimensions = await card(page, next).evaluate(element => { const style = (element as HTMLElement).style; return { width: parseFloat(style.width), height: parseFloat(style.height) }; });
    expect(dimensions.width).toBeGreaterThan(.01); expect(dimensions.height).toBeGreaterThan(.01);
  }
  const expanded = await stored(request, project.id);
  expect(expanded.nodes.map(node => node.id)).toEqual(project.nodes.map(node => node.id));
  expect(expanded.nodes.find(node => node.id === leaf.id)!.parentId).toBe(chain.at(-1));
  await page.reload(); await expect(card(page, leaf.id)).toHaveCount(1);
  const restored = await stored(request, project.id);
  for (const id of chain) expect(restored.nodes.find(node => node.id === id)!.collapsed).toBe(false);
  for (const [index, id] of chain.entries()) {
    const title = (await card(page, id).locator(':scope > .ms-node-header').boundingBox())!;
    const next = (await card(page, chain[index + 1] ?? leaf.id).boundingBox())!;
    expect(title.y + title.height, `Generation ${index + 1} title stays above its child workspace`).toBeLessThanOrEqual(next.y + .5);
  }
  await page.screenshot({ path: info.outputPath('sixteen-generations-overview.png') });
  await select(page, leaf.id); await inspectorPanel(page, 'canvas');
  await page.getByRole('searchbox', { name: 'Search Work nodes', exact: true }).fill(leaf.id);
  await page.locator(`[data-outline-node="${leaf.id}"]`).getByRole('button', { name: /^Jump to Node:/ }).click();
  const focused = (await card(page, leaf.id).boundingBox())!, viewport = (await page.locator('.ms-viewport').boundingBox())!;
  expect(focused.width).toBeGreaterThan(100); expect(focused.height).toBeGreaterThan(100);
  expect(Math.abs(focused.x + focused.width / 2 - viewport.x - viewport.width / 2)).toBeLessThan(3);
  expect(Math.abs(focused.y + focused.height / 2 - viewport.y - viewport.height / 2)).toBeLessThan(3);
  expect(failures).toEqual([]); await page.screenshot({ path: info.outputPath('sixteen-revealed-container-generations.png') });
});
