import { test, expect, type APIRequestContext, type Page } from '@playwright/test';
import { addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import type { MeridianNode, MeridianProject } from '../src/core/types.ts';

const card = (page: Page, id: string) => page.locator(`.ms-node[data-node-id="${id}"]`);
const camera = (page: Page) => page.locator('.ms-world').evaluate(element => (element as HTMLElement).style.transform);
const node = (project: MeridianProject, id: string) => project.nodes.find(candidate => candidate.id === id)!;
const geometry = (page: Page, id: string) => card(page, id).evaluate(element => ({
  x: Number.parseFloat((element as HTMLElement).style.left), y: Number.parseFloat((element as HTMLElement).style.top),
  width: Number.parseFloat((element as HTMLElement).style.width), height: Number.parseFloat((element as HTMLElement).style.height),
}));

async function read(request: APIRequestContext, id: string): Promise<MeridianProject> {
  const response = await request.get(`/api/projects/${id}`); expect(response.ok()).toBeTruthy(); return response.json();
}
async function seed(page: Page, request: APIRequestContext, project: MeridianProject) {
  const response = await request.post('/api/projects', { data: commitSnapshots(project) }); expect(response.ok()).toBeTruthy();
  const saved = await response.json() as MeridianProject;
  await page.goto(`/?project=${saved.id}`);
  await expect(page.locator('.save-state')).toHaveText(`Saved · r${saved.revision}`);
  await page.getByRole('button', { name: 'Fit Work', exact: true }).click(); return saved;
}
async function select(page: Page, ids: string[]) {
  for (const [index, id] of ids.entries()) await card(page, id).locator('.ms-node-title').click(index ? { modifiers: ['Shift'] } : {});
  await expect(page.locator('.selection-count')).toHaveText(`${ids.length} selected`);
}
async function arrange(page: Page) {
  await page.locator('.selection-bar').getByRole('button', { name: 'Arrange selected nodes', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'Arrange selected nodes', exact: true }); await expect(dialog).toBeVisible(); return dialog;
}
async function committed(page: Page, request: APIRequestContext, id: string) {
  await expect.poll(async () => (await read(request,id)).revision).toBeGreaterThan(1);
  await expect(page.locator('.save-state')).toHaveText(/^Saved · r\d+$/);
  return read(request,id);
}
async function save(page: Page, request: APIRequestContext, id: string) {
  await page.keyboard.press('Control+s');
  await expect(page.locator('.save-state')).toHaveText(/^Saved · r\d+$/); return read(request, id);
}
function fixture(title: string) {
  const project = newProject(title), canvasId = project.entryCanvasId;
  project.canvases[0]!.snapping = { stickyEdges: false, smartAlign: false, snapToGrid: false };
  const make = (title: string, x: number, y: number, width: number, height = 145, parent?: MeridianNode) => {
    const result = addNode(project, canvasId, 'text', title);
    Object.assign(result, { sizeMode: 'fixed', x, y, width, height, text: `${title}: preserve this authored content.`, childLayout: 'spatial', placement: 'free' });
    if (parent) result.parentId = parent.id; return result;
  };
  const link = (source: MeridianNode, target: MeridianNode, x: number, y: number, parentId = project.canvases[0]!.rootId) => {
    const result = { id: crypto.randomUUID(), canvasId, parentId, sourceId: source.id, targetId: target.id,
      kind: 'visual' as const, relation: 'generic' as const, direction: 'none' as const, points: [{ x, y }] };
    project.links.push(result); return result;
  };
  return { project, make, link };
}

test('equal outer-edge spacing preserves hidden branches and routes, is one undo step, and survives save/reopen', async ({ page, request }, info) => {
  const { project, make, link } = fixture('Arrange unequal tiles');
  const left = make('Narrow tile', 100, 180, 180), middle = make('Wide branch', 350, 180, 320), right = make('Last tile', 950, 180, 230);
  middle.collapsed = true;
  const hidden = make('Hidden account', 390, 430, 190, 130, middle), internal = link(middle, hidden, 445, 390, middle.id), crossing = link(left, middle, 320, 440);
  const untouched = make('Unselected detail', 700, 500, 210);
  const baseline = await seed(page, request, project); await select(page, [right.id, left.id, middle.id]);
  const initialCamera = await camera(page), address = page.url();
  const dialog = await arrange(page); await page.screenshot({ path: info.outputPath('arrange-wide.png') });
  await dialog.getByRole('button', { name: 'Space evenly horizontally', exact: true }).click(); await expect(dialog).toHaveCount(0);
  await expect(page.locator('.selection-bar').getByRole('button', { name: 'Arrange selected nodes', exact: true })).toBeFocused();
  const boxes = await Promise.all([left, middle, right].map(item => geometry(page, item.id)));
  const firstGap = boxes[1]!.x - boxes[0]!.x - boxes[0]!.width, lastGap = boxes[2]!.x - boxes[1]!.x - boxes[1]!.width;
  expect(firstGap).toBeCloseTo(lastGap, 6); expect(firstGap).toBeCloseTo(175, 6);
  expect(boxes[0]!.x).toBe(left.x); expect(boxes[2]!.x + boxes[2]!.width).toBe(right.x + right.width);
  expect(await camera(page)).toBe(initialCamera); expect(page.url()).toBe(address);
  await expect(card(page, middle.id)).toHaveAttribute('aria-expanded', 'false'); await expect(card(page, hidden.id)).toHaveCount(0);
  const draft = await committed(page, request, project.id), dx = node(draft, middle.id).x - middle.x;
  expect(dx).toBe(105); expect(node(draft, hidden.id).x).toBe(hidden.x + dx);
  expect(node(draft, untouched.id)).toEqual(node(baseline, untouched.id));
  expect(draft.links.find(item => item.id === internal.id)?.points).toEqual([{ x: 445 + dx, y: 390 }]);
  expect(draft.links.find(item => item.id === crossing.id)).toEqual(crossing);
  expect(draft.revision).toBe(baseline.revision+1);
  for(const id of [middle.id,hidden.id,untouched.id])expect(draft.snapshots[id]!.node).toEqual(node(draft,id));
  expect(await read(request, project.id)).toEqual(draft);
  await page.getByRole('button', { name: 'Undo', exact: true }).click();
  expect((await geometry(page, middle.id)).x).toBe(middle.x); await expect(page.getByRole('button', { name: 'Undo', exact: true })).toBeDisabled();
  await page.getByRole('button', { name: 'Redo', exact: true }).click(); expect((await geometry(page, middle.id)).x).toBe(455);
  const saved = await save(page, request, project.id); expect(node(saved, hidden.id).x).toBe(495);
  await page.reload(); expect((await geometry(page, middle.id)).x).toBe(455);
  await expect(card(page, middle.id)).toHaveAttribute('aria-expanded', 'false'); await expect(card(page, hidden.id)).toHaveCount(0);
});

test('selected ancestors normalize visible children so a branch and its hidden grandchildren move exactly once', async ({ page, request }) => {
  const { project, make, link } = fixture('Arrange complete branches once');
  const parent = make('Expanded parent', 520, 140, 260), child = make('Visible child', 550, 420, 140, 135, parent);
  parent.collapsed = false; child.collapsed = true;
  const grandchild = make('Hidden grandchild', 575, 610, 130, 100, child);
  // Independent branches remain in disjoint rows when their left edges align.
  const left = make('Left anchor', 100, 0, 170, 120), right = make('Right branch', 950, 760, 180, 120);
  const internal = link(parent, child, 900, 355, parent.id), hiddenInternal = link(child, grandchild, 600, 590, child.id), crossing = link(left, parent, 450, 345);
  const baseline = await seed(page, request, project); await select(page, [parent.id, child.id, left.id, right.id]);
  const initialCamera = await camera(page), dialog = await arrange(page);
  await dialog.getByRole('button', { name: 'Align left edges', exact: true }).click();
  await expect(page.locator('.selection-bar').getByRole('button', { name: 'Arrange selected nodes', exact: true })).toBeFocused();
  const draft = await committed(page, request, project.id), dx = left.x - parent.x;
  expect(node(draft, parent.id).x).toBe(left.x); expect(node(draft, child.id).x).toBe(child.x + dx); expect(node(draft, grandchild.id).x).toBe(grandchild.x + dx);
  expect(node(draft, child.id).x - node(draft, parent.id).x).toBe(child.x - parent.x);
  expect(draft.links.find(item => item.id === internal.id)?.points).toEqual([{ x: 900 + dx, y: 355 }]);
  expect(draft.links.find(item => item.id === hiddenInternal.id)?.points).toEqual([{ x: 600 + dx, y: 590 }]);
  expect(draft.links.find(item => item.id === crossing.id)).toEqual(crossing);
  for (const before of [parent, child, grandchild]) {
    const after = node(draft, before.id); expect(after.parentId).toBe(before.parentId); expect(after.collapsed).toBe(before.collapsed); expect(after.text).toBe(before.text);
  }
  expect(draft.revision).toBe(baseline.revision+1);
  for(const id of [parent.id,child.id,grandchild.id])expect(draft.snapshots[id]!.node).toEqual(node(draft,id));
  expect(await camera(page)).toBe(initialCamera);
  await expect(card(page, parent.id)).toHaveAttribute('aria-expanded', 'true'); await expect(card(page, child.id)).toHaveAttribute('aria-expanded', 'false'); await expect(card(page, grandchild.id)).toHaveCount(0);
  await page.getByRole('button', { name: 'Undo', exact: true }).click();
  expect((await geometry(page, parent.id)).x).toBe(parent.x); expect((await geometry(page, child.id)).x).toBe(child.x);
  await expect(page.getByRole('button', { name: 'Undo', exact: true })).toBeDisabled();
});

test('cancel and an already-aligned action preserve history, recovery and the saved project', async ({ page, request }) => {
  const { project, make } = fixture('No incidental arrangement edits');
  const first = make('Aligned first', 180, 130, 240), second = make('Aligned second', 180, 330, 180);
  const baseline = await seed(page, request, project); await select(page, [first.id, second.id]);
  const initialCamera = await camera(page); let recoveryWrites = 0;
  page.on('request', request => { if (request.method() === 'PUT' && request.url().endsWith(`/api/projects/${project.id}/recovery`)) recoveryWrites++; });
  let dialog = await arrange(page); await dialog.getByRole('button', { name: 'Cancel', exact: true }).click(); await expect(dialog).toHaveCount(0);
  await expect(page.locator('.selection-bar').getByRole('button', { name: 'Arrange selected nodes', exact: true })).toBeFocused();
  dialog = await arrange(page); await dialog.getByRole('button', { name: 'Align left edges', exact: true }).click(); await expect(dialog).toHaveCount(0);
  await expect(page.locator('.selection-bar').getByRole('button', { name: 'Arrange selected nodes', exact: true })).toBeFocused();
  await expect(page.locator('.save-state')).toHaveText(`Saved · r${baseline.revision}`);
  await expect(page.getByRole('button', { name: 'Undo', exact: true })).toBeDisabled(); await expect(page.getByRole('button', { name: 'Redo', exact: true })).toBeDisabled();
  // Recovery normally runs after 700 ms. Neither interaction should schedule it.
  await page.waitForTimeout(1000); expect(recoveryWrites).toBe(0); expect((await request.get(`/api/projects/${project.id}/recovery`)).status()).toBe(404);
  expect(await read(request, project.id)).toEqual(baseline); expect(await camera(page)).toBe(initialCamera);
});

test('spacing counts independent branches and every arrangement control fits a compact window', async ({ page, request }, info) => {
  const { project, make } = fixture('Compact branch arrangement');
  const parent = make('Parent branch', 160, 120, 250), child = make('Selected child', 190, 390, 150, 125, parent), sibling = make('Other branch', 680, 180, 210);
  parent.collapsed = false;
  const baseline = await seed(page, request, project); await select(page, [parent.id, child.id, sibling.id]);
  const dialog = await arrange(page);
  for (const name of ['Space evenly horizontally', 'Space evenly vertically']) await expect(dialog.getByRole('button', { name, exact: true })).toBeDisabled();
  for (const name of ['Align left edges', 'Align horizontal centers', 'Align right edges', 'Align top edges', 'Align vertical centers', 'Align bottom edges']) await expect(dialog.getByRole('button', { name, exact: true })).toBeEnabled();
  await page.setViewportSize({ width: 390, height: 844 });
  await expect(dialog).toBeVisible(); const bounds = await dialog.boundingBox(); expect(bounds).not.toBeNull();
  expect(bounds!.x).toBeGreaterThanOrEqual(0); expect(bounds!.x + bounds!.width).toBeLessThanOrEqual(391);
  expect(await dialog.evaluate(element => element.scrollWidth > element.clientWidth + 1)).toBe(false);
  await page.screenshot({ path: info.outputPath('arrange-compact.png') });
  await dialog.getByRole('button', { name: 'Cancel', exact: true }).scrollIntoViewIfNeeded(); await page.keyboard.press('Escape'); await expect(dialog).toHaveCount(0);
  await page.setViewportSize({ width: 1500, height: 950 });
  expect(await read(request, project.id)).toEqual(baseline); await expect(page.getByRole('button', { name: 'Undo', exact: true })).toBeDisabled();
});
