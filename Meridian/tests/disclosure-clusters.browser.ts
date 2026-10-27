import { test, expect, type APIRequestContext, type Page } from '@playwright/test';
import { addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import type { MeridianProject } from '../src/core/types.ts';

const card = (page: Page, id: string) => page.locator(`.ms-node[data-node-id="${id}"]`);
const read = async (request: APIRequestContext, id: string) => await (await request.get(`/api/projects/${id}`)).json() as MeridianProject;

function fixture() {
  const project = newProject('Collapsible Clusters browser QA'), canvasId = project.entryCanvasId;
  project.canvases[0]!.snapping = { stickyEdges: false, smartAlign: false, snapToGrid: false };
  const first = addNode(project, canvasId, 'text', 'First account');
  Object.assign(first, { x: 100, y: 260, width: 220, height: 145, sizeMode: 'fixed', placement: 'free', childLayout: 'spatial', collapsed: true, text: 'Keep this account and its hidden detail intact.' });
  const detail = addNode(project, canvasId, 'text', 'Hidden detail');
  Object.assign(detail, { parentId: first.id, x: 110, y: 470, width: 190, height: 120, sizeMode: 'fixed', placement: 'free', text: 'This original child remains collapsed with its parent.' });
  const second = addNode(project, canvasId, 'text', 'Second account');
  Object.assign(second, { x: 450, y: 260, width: 250, height: 145, sizeMode: 'fixed', placement: 'free', text: 'Its edges do not touch the first account.' });
  const outside = addNode(project, canvasId, 'text', 'Outside account');
  Object.assign(outside, { x: 820, y: 90, width: 230, height: 145, sizeMode: 'fixed', placement: 'free' });
  project.links.push(
    { id: crypto.randomUUID(), canvasId, sourceId: first.id, targetId: second.id, kind: 'visual', relation: 'generic', direction: 'none', label: 'Internal relationship', points: [{ x: 385, y: 470 }] },
    { id: crypto.randomUUID(), canvasId, sourceId: second.id, targetId: outside.id, kind: 'visual', relation: 'generic', direction: 'none', label: 'External relationship', points: [{ x: 770, y: 170 }] },
  );
  return { project: commitSnapshots(project), first, detail, second, outside };
}

async function seed(page: Page, request: APIRequestContext, project: MeridianProject) {
  const response = await request.post('/api/projects', { data: project }); expect(response.ok()).toBeTruthy();
  await page.goto(`/?project=${project.id}`);
  await expect(card(page, project.nodes.find(node => node.kind === 'text')!.id)).toBeVisible();
}
async function selectMembers(page: Page, firstId: string, secondId: string) {
  await card(page, firstId).click({ position: { x: 2, y: 20 } });
  await card(page, secondId).click({ position: { x: 2, y: 20 }, modifiers: ['Shift'] });
  await expect(page.locator('.ms-node[aria-selected="true"]')).toHaveCount(2);
}
async function saved(page: Page, request: APIRequestContext, id: string, testState: (project: MeridianProject) => boolean) {
  const previous = await page.$('.toast');
  await page.keyboard.press('Control+s');
  await page.waitForFunction(prior => {
    const current = document.querySelector('.toast');
    return current !== prior && /^All changes are saved · r\d+\.$/.test(current?.textContent ?? '');
  }, previous);
  await previous?.dispose();
  const project = await read(request, id); expect(testState(project)).toBe(true); return project;
}

test('spaced branches collapse to one named button, preserve child disclosure and links, and remember button appearance after reopen', async ({ page, request }, info) => {
  const errors: string[] = []; page.on('pageerror', error => errors.push(error.message));
  const { project, first, detail, second, outside } = fixture(); await seed(page, request, project);
  await selectMembers(page, first.id, second.id);
  await page.locator('.canvas-toolbar').getByRole('button', { name: 'Create Cluster', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'Create Cluster', exact: true });
  const name = 'Two connected accounts'; await dialog.getByLabel('Name', { exact: true }).fill(name);
  await dialog.getByLabel('Collapsed button shape', { exact: true }).selectOption('circle');
  await dialog.getByRole('button', { name: 'Create Cluster', exact: true }).click(); await expect(dialog).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Collapse Cluster', exact: true })).toBeVisible();
  const created = await saved(page, request, project.id, state => state.nodes.some(node => node.cluster && node.tag === name));
  const cluster = created.nodes.find(node => node.cluster && node.tag === name)!;
  expect(cluster.cluster).toEqual({ buttonShape: 'circle' }); expect(cluster.collapsed).toBe(false);
  for (const original of [first, second]) {
    const member = created.nodes.find(node => node.id === original.id)!;
    expect(member.parentId).toBe(cluster.id); expect(member.x).toBe(original.x); expect(member.y).toBe(original.y);
    expect(member.title).toBe(original.title); expect(member.text).toBe(original.text);
  }
  expect(created.nodes.find(node => node.id === detail.id)?.parentId).toBe(first.id);
  expect(created.links.map(link => ({ id: link.id, sourceId: link.sourceId, targetId: link.targetId, points: link.points })))
    .toEqual(project.links.map(link => ({ id: link.id, sourceId: link.sourceId, targetId: link.targetId, points: link.points })));
  await page.getByRole('button', { name: 'Collapse Cluster', exact: true }).click();
  await expect(card(page, cluster.id)).toHaveAttribute('aria-expanded', 'false');
  await expect(card(page, first.id)).toHaveCount(0); await expect(card(page, second.id)).toHaveCount(0);
  await expect(card(page, outside.id)).toHaveCount(1);
  const expand = page.getByRole('button', { name: `Expand Cluster: ${name}`, exact: true });
  await expand.hover(); await expect(card(page, cluster.id).getByText(name, { exact: true })).toBeVisible();
  await expect(card(page, cluster.id).locator('.ms-cluster-name')).toHaveCSS('opacity', '1');
  await page.screenshot({ path: info.outputPath('collapsed-circle-cluster.png') });
  await expand.click(); await expect(card(page, first.id)).toHaveAttribute('aria-expanded', 'false');
  await expect(card(page, second.id)).toHaveCount(1); await expect(card(page, detail.id)).toHaveCount(0);
  await page.getByRole('combobox', { name: 'Side panel', exact: true }).selectOption('appearance');
  await page.getByLabel('Collapsed button shape', { exact: true }).selectOption('square');
  await page.getByRole('combobox', { name: 'Side panel', exact: true }).selectOption('properties');
  await page.getByRole('button', { name: 'Collapse Cluster', exact: true }).click();
  const final = await saved(page, request, project.id, state => {
    const node = state.nodes.find(node => node.id === cluster.id); return node?.collapsed === true && node.cluster?.buttonShape === 'square';
  });
  expect(final.nodes.find(node => node.id === first.id)?.collapsed).toBe(true);
  await page.reload(); await expect(card(page, cluster.id)).toHaveAttribute('aria-expanded', 'false');
  await page.getByRole('button', { name: `Expand Cluster: ${name}`, exact: true }).click();
  await expect(card(page, first.id)).toHaveCount(1); await expect(card(page, second.id)).toHaveCount(1);
  await expect(card(page, detail.id)).toHaveCount(0); expect(errors).toEqual([]);
});

test('Cluster creation through the selection context menu is one undo step and retains original identities', async ({ page, request }, info) => {
  const { project, first, second } = fixture(); await seed(page, request, project); await selectMembers(page, first.id, second.id);
  await card(page, first.id).click({ button: 'right', position: { x: 2, y: 20 } });
  await page.getByRole('menuitem', { name: 'Create Cluster', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'Create Cluster', exact: true });
  await dialog.getByLabel('Name', { exact: true }).fill('Undoable Cluster');
  await dialog.getByRole('button', { name: 'Create Cluster', exact: true }).click();
  const grouped = await saved(page, request, project.id, state => state.nodes.some(node => !!node.cluster));
  const cluster = grouped.nodes.find(node => node.cluster)!; expect(cluster.cluster?.buttonShape).toBe('square');
  await page.getByRole('button', { name: 'Undo', exact: true }).click();
  await expect(card(page, cluster.id)).toHaveCount(0);
  const undone = await saved(page, request, project.id, state => !state.nodes.some(node => !!node.cluster));
  expect(undone.nodes.map(node => node.id)).toEqual(project.nodes.map(node => node.id));
  expect(undone.nodes.find(node => node.id === first.id)?.parentId).toBe(first.parentId);
  expect(undone.nodes.find(node => node.id === second.id)?.parentId).toBe(second.parentId);
  expect(undone.links).toEqual(project.links);
  await page.getByRole('button', { name: 'Redo', exact: true }).click();
  await expect(card(page, cluster.id)).toHaveCount(1);
  await card(page, cluster.id).focus(); await page.keyboard.press('Space');
  await page.getByRole('combobox', { name: 'Side panel', exact: true }).selectOption('properties');
  await expect(page.getByRole('combobox', { name: 'Type', exact: true })).toBeDisabled();
  await expect(page.getByRole('combobox', { name: 'Display Pattern', exact: true })).toBeDisabled();
  await page.screenshot({ path: info.outputPath('expanded-cluster-after-redo.png') });
});
