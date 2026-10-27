import { test, expect, type APIRequestContext, type Page } from '@playwright/test';
import { addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import type { MeridianNode, MeridianProject } from '../src/core/types.ts';
import { inspectorPanel } from './ui-helpers.ts';

const tile = (page: Page, id: string) => page.locator(`.ms-node[data-node-id="${id}"]`);
const get = (project: MeridianProject, id: string) => project.nodes.find(node => node.id === id)!;
const stored = async (request: APIRequestContext, id: string): Promise<MeridianProject> => (await request.get(`/api/projects/${id}`)).json();
const failures = new WeakMap<Page, string[]>();
test.beforeEach(async ({ page }) => { const errors: string[] = []; failures.set(page, errors); page.on('pageerror', error => errors.push(error.message)); });
test.afterEach(async ({ page }) => expect(failures.get(page) ?? []).toEqual([]));

function fixture(title: string) {
  const project = newProject(title);
  project.canvases[0]!.snapping = { stickyEdges: false, smartAlign: false, snapToGrid: false };
  return project;
}
function parent(project: MeridianProject, tag: string, x = 80, y = 90, width = 760, height = 340) {
  const node = addNode(project, project.entryCanvasId, 'container', '');
  Object.assign(node, { tag, x, y, width, height, sizeMode: 'fixed', collapsed: false, containerVariant: 'horizontal', childLayout: 'contained', childArrangement: 'row',
    style: { shape: 'rectangle', borderWidth: 1, fill: '#1b3340', border: '#72cbb5', padding: 5 },
    containerSizes: { expanded: { width, height, sizeMode: 'fixed' }, collapsed: { width: 240, height: 140, sizeMode: 'fixed' } } });
  return node;
}
function child(project: MeridianProject, owner: MeridianNode, tag: string) {
  const node = addNode(project, owner.canvasId, 'media', '');
  Object.assign(node, { parentId: owner.id, tag, x: owner.x + 40, y: owner.y + 45, width: 180, height: 80, sizeMode: 'fixed', collapsed: true,
    style: { shape: 'rectangle', fill: '#31564b' } }); return node;
}
async function seed(page: Page, request: APIRequestContext, project: MeridianProject) {
  const response = await request.post('/api/projects', { data: commitSnapshots(project) });
  expect(response.ok(), await response.text()).toBe(true); await page.goto(`/?project=${project.id}`);
  await expect(page.locator('.workspace')).toBeVisible();
}
async function select(page: Page, id: string) {
  await inspectorPanel(page, 'canvas'); const search = page.getByRole('searchbox', { name: 'Search Work nodes', exact: true });
  await search.fill(id); await page.locator(`[data-outline-node="${id}"] .inspector-tree-title`).click(); await search.fill('');
}
async function toggle(page: Page, id: string) {
  await select(page, id); await inspectorPanel(page, 'relations');
  await page.getByRole('button', { name: 'Toggle contents from relations', exact: true }).click();
}
async function box(page: Page, id: string) {
  return tile(page, id).evaluate(element => {
    const style = (element as HTMLElement).style;
    return { x: parseFloat(style.left), y: parseFloat(style.top), width: parseFloat(style.width), height: parseFloat(style.height) };
  });
}
async function fills(page: Page, owner: MeridianNode, ids: string[], inset: number) {
  const frame = await box(page, owner.id), children = await Promise.all(ids.map(id => box(page, id)));
  const cellWidth = (frame.width - 2 * (inset + 1)) / ids.length, cellHeight = frame.height - 2 * (inset + 1);
  for (const [index, bounds] of children.entries()) {
    expect(bounds.x).toBeCloseTo(frame.x + inset + 1 + cellWidth * index, 5);
    expect(bounds.y).toBeCloseTo(frame.y + inset + 1, 5);
    expect(bounds.width).toBeCloseTo(cellWidth, 5); expect(bounds.height).toBeCloseTo(cellHeight, 5);
  }
}

test('default child inset is 20 while authored insets survive and resetting only that inset preserves style', async ({ page, request }, info) => {
  const project = fixture('Child inset defaults and explicit values');
  const defaultParent = parent(project, 'Default inset', 60, 80, 320, 250), five = parent(project, 'Authored five', 420, 80, 320, 250), thirteen = parent(project, 'Authored thirteen', 780, 80, 320, 250);
  five.style!.childPadding = 5; thirteen.style!.childPadding = 13;
  const entries = [defaultParent, five, thirteen].map(owner => child(project, owner, `${owner.tag} child`));
  await seed(page, request, project);
  for (const [index, owner] of [defaultParent, five, thirteen].entries()) await fills(page, owner, [entries[index]!.id], index === 0 ? 20 : index === 1 ? 5 : 13);
  await select(page, defaultParent.id); await inspectorPanel(page, 'appearance');
  await expect(page.locator('.inspector').getByLabel('Child inset', { exact: true })).toHaveValue('20');
  await expect(page.getByRole('button', { name: 'Use default child inset (20 px)', exact: true })).toBeDisabled();
  await select(page, five.id); await inspectorPanel(page, 'appearance');
  await expect(page.locator('.inspector').getByLabel('Child inset', { exact: true })).toHaveValue('5');
  const previous = (await stored(request, project.id)).nodes.find(node => node.id === five.id)!.style!;
  await page.getByRole('button', { name: 'Use default child inset (20 px)', exact: true }).click();
  await expect.poll(async () => get(await stored(request, project.id), five.id).style?.childPadding).toBeUndefined();
  const expected = { ...previous }; delete expected.childPadding;
  expect(get(await stored(request, project.id), five.id).style).toEqual(expected);
  expect(get(await stored(request, project.id), thirteen.id).style?.childPadding).toBe(13);
  await fills(page, five, [entries[1]!.id], 20); await page.reload();
  await fills(page, five, [entries[1]!.id], 20); await fills(page, thirteen, [entries[2]!.id], 13);
  await select(page, five.id); await inspectorPanel(page, 'appearance');
  await expect(page.locator('.inspector').getByLabel('Child inset', { exact: true })).toHaveValue('20');
  await page.locator('.inspector').getByLabel('Child inset', { exact: true }).scrollIntoViewIfNeeded();
  await page.screenshot({ path: info.outputPath('default-and-authored-child-insets.png') });
});

test('double clicking the padded parent rim reliably toggles the parent without toggling its child', async ({ page, request }, info) => {
  const project = fixture('Accessible parent disclosure rim'), owner = parent(project, 'Parent rim'), item = child(project, owner, 'Child stays closed');
  const grandchild = child(project, item, 'Hidden grandchild');
  await seed(page, request, project);
  for (let cycle = 0; cycle < 3; cycle++) {
    const expanded = await tile(page, owner.id).evaluate(element => { const bounds = element.getBoundingClientRect(); return { height: bounds.height, scale: bounds.width / parseFloat((element as HTMLElement).style.width) }; });
    await tile(page, owner.id).dblclick({ position: { x: 15 * expanded.scale, y: expanded.height / 2 } });
    await expect(tile(page, owner.id)).toHaveAttribute('aria-expanded', 'false'); await expect(tile(page, item.id)).toHaveCount(0);
    await tile(page, owner.id).dblclick();
    await expect(tile(page, owner.id)).toHaveAttribute('aria-expanded', 'true'); await expect(tile(page, item.id)).toHaveCount(1);
    await expect(tile(page, item.id)).toHaveAttribute('aria-expanded', 'false'); await expect(tile(page, grandchild.id)).toHaveCount(0);
  }
  await expect.poll(async () => get(await stored(request, project.id), owner.id).collapsed).toBe(false);
  expect(get(await stored(request, project.id), item.id).collapsed).toBe(true);
  await page.screenshot({ path: info.outputPath('accessible-parent-rim.png') });
});

test('nested bounded containers retain authored open and closed frames through allocated cells, repeated toggles and reload', async ({ page, request }, info) => {
  test.setTimeout(90000);
  const project = fixture('Nested authored frame records'), owner = parent(project, 'Ordered parent'), nested = child(project, owner, 'Nested bounded workspace'), peer = child(project, owner, 'Sibling');
  Object.assign(nested, { kind: 'container', containerVariant: 'horizontal', childLayout: 'contained', childArrangement: 'row',
    containerSizes: { expanded: { width: 500, height: 400, sizeMode: 'fixed' }, collapsed: { width: 180, height: 80, sizeMode: 'fixed' } } });
  nested.style!.fill = '#2a405c';
  const detail = child(project, nested, 'Nested detail'), records = structuredClone(nested.containerSizes);
  Object.assign(detail, { title: 'Nested detail', style: { shape: 'rectangle', fill: '#558774' } });
  await seed(page, request, project); const allocation = await box(page, nested.id), outer = await box(page, owner.id);
  for (let cycle = 0; cycle < 6; cycle++) {
    await toggle(page, nested.id); await expect(tile(page, detail.id)).toHaveCount(1);
    expect(await box(page, nested.id)).toEqual(allocation); expect(await box(page, owner.id)).toEqual(outer);
    await fills(page, owner, [nested.id, peer.id], 20);
    await expect.poll(async () => get(await stored(request, project.id), nested.id).containerSizes).toEqual(records);
    await toggle(page, nested.id); await expect(tile(page, detail.id)).toHaveCount(0); expect(await box(page, nested.id)).toEqual(allocation);
    await expect.poll(async () => get(await stored(request, project.id), nested.id).collapsed).toBe(true);
    expect(get(await stored(request, project.id), nested.id).containerSizes).toEqual(records);
  }
  await page.reload(); await expect(tile(page, nested.id)).toHaveAttribute('aria-expanded', 'false'); expect(await box(page, nested.id)).toEqual(allocation);
  await toggle(page, nested.id); await expect(tile(page, detail.id)).toHaveCount(1);
  await expect.poll(async () => get(await stored(request, project.id), nested.id).collapsed).toBe(false);
  expect(get(await stored(request, project.id), nested.id).containerSizes).toEqual(records); expect(await box(page, owner.id)).toEqual(outer);
  await page.screenshot({ path: info.outputPath('nested-bounded-frame-allocation.png') });
});

test('exterior disclosure opens the original branch in a tray while an embedded Container stays inside its cell', async ({ page, request }, info) => {
  const project = fixture('Exterior disclosure tray'), owner = parent(project, 'Ordered owner', 80, 90, 840, 420);
  const exterior = child(project, owner, 'Exterior branch'), bounded = child(project, owner, 'Bounded branch');
  Object.assign(exterior, { containerVariant: 'horizontal', childLayout: 'spatial', childArrangement: 'row' });
  Object.assign(bounded, { kind: 'container', containerVariant: 'horizontal', childLayout: 'contained', childArrangement: 'row',
    containerSizes: { expanded: { width: 500, height: 400, sizeMode: 'fixed' }, collapsed: { width: 180, height: 80, sizeMode: 'fixed' } } });
  const outsideDetail = child(project, exterior, 'Exterior detail'), insideDetail = child(project, bounded, 'Embedded detail');
  await seed(page, request, project); await select(page, exterior.id); await inspectorPanel(page, 'relations');
  const before = await stored(request, project.id), priorFrame = await box(page, owner.id);
  await page.getByRole('button', { name: 'Toggle contents from relations', exact: true }).click();
  const tray = page.locator(`.ms-expansion-tray[data-tray-source-id="${exterior.id}"]`);
  await expect(tray).toHaveCount(1); await expect(tile(page, outsideDetail.id)).toHaveCount(1);
  await expect.poll(async () => get(await stored(request, project.id), exterior.id).collapsed).toBe(false);
  expect((await stored(request, project.id)).nodes.map(node => [node.id, node.parentId])).toEqual(before.nodes.map(node => [node.id, node.parentId]));
  const frame = await tray.evaluate(element => { const style = (element as HTMLElement).style; return { x: parseFloat(style.left), y: parseFloat(style.top), width: parseFloat(style.width), height: parseFloat(style.height) }; });
  expect(frame.x >= priorFrame.x + priorFrame.width || frame.y >= priorFrame.y + priorFrame.height || frame.x + frame.width <= priorFrame.x || frame.y + frame.height <= priorFrame.y).toBe(true);
  expect(await box(page, owner.id)).toEqual(priorFrame); await page.getByRole('button', { name: 'Fit Work', exact: true }).click();
  await page.screenshot({ path: info.outputPath('exterior-disclosure-tray.png') });
  await toggle(page, bounded.id); await expect(tile(page, insideDetail.id)).toHaveCount(1);
  await expect.poll(async () => get(await stored(request, project.id), bounded.id).collapsed).toBe(false);
  expect(get(await stored(request, project.id), exterior.id).collapsed).toBe(false); expect(await box(page, owner.id)).toEqual(priorFrame);
});
