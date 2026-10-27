import { test, expect, type APIRequestContext, type Page } from '@playwright/test';
import { addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import type { MeridianNode, MeridianProject } from '../src/core/types.ts';

const tile = (page: Page, id: string) => page.locator(`.ms-node[data-node-id="${id}"]`);
const stored = async (request: APIRequestContext, id: string): Promise<MeridianProject> => (await request.get(`/api/projects/${id}`)).json();
const contents = (page: Page, id: string) => page.locator(`[data-gallery-contents="${id}"]`);
async function relations(page: Page, gallery: MeridianNode) {
  await tile(page, gallery.id).locator('.ms-node-title').click();
  await page.getByRole('combobox', { name: 'Side panel', exact: true }).selectOption('relations');
  await expect(contents(page, gallery.id)).toBeVisible();
}
async function fixture(page: Page, request: APIRequestContext, names: string[]) {
  const project = newProject('Gallery Relations workspace'), gallery = addNode(project, project.entryCanvasId, 'container', 'Gallery collection');
  Object.assign(gallery, { x: 120, y: 90, width: 420, height: 360, containerVariant: 'gallery', childLayout: 'contained', childArrangement: 'free', collapsed: false });
  const items = names.map(title => {
    const item = addNode(project, project.entryCanvasId, 'text', title);
    Object.assign(item, { parentId: gallery.id, text: `The ${title.toLowerCase()} passage.`, x: gallery.x + 20, y: gallery.y + 55, width: 300, height: 150 }); return item;
  });
  const response = await request.post('/api/projects', { data: commitSnapshots(project) }); expect(response.ok(), await response.text()).toBeTruthy();
  const saved: MeridianProject = await response.json(); await page.goto(`/?project=${saved.id}`); await relations(page, gallery);
  return { saved, gallery, items };
}

test('empty Gallery offers ordinary child creation without grid controls, and preserves the child on reopen', async ({ page, request }) => {
  const f = await fixture(page, request, []), section = contents(page, f.gallery.id);
  await expect(section).toContainText('This gallery is empty.');
  await expect(page.locator('.inspector').getByLabel('Child pattern', { exact: true })).toHaveCount(0);
  await section.getByRole('button', { name: 'Add gallery object', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'New Node', exact: true }); await expect(dialog).toContainText('Create a child of Gallery collection.');
  await dialog.getByLabel('Type', { exact: true }).selectOption('media'); await dialog.getByLabel('Content', { exact: true }).selectOption('text'); await dialog.getByLabel('Title', { exact: true }).fill('First gallery passage'); await dialog.getByLabel('Text body', { exact: true }).fill('A complete text object in the gallery.');
  await dialog.getByRole('button', { name: 'Create node', exact: true }).click(); await expect(dialog).toHaveCount(0);
  await expect.poll(async () => (await stored(request, f.saved.id)).nodes.filter(node => node.parentId === f.gallery.id).length).toBe(1);
  const child = (await stored(request, f.saved.id)).nodes.find(node => node.parentId === f.gallery.id)!;
  expect(child.kind).toBe('media'); expect(child.contentType).toBe('text'); expect(child.text).toBe('A complete text object in the gallery.');
  await relations(page, f.gallery); await expect(section.locator('.inspector-gallery-position')).toHaveText('1 / 1');
  await expect(section.getByRole('button', { name: 'Previous gallery object', exact: true })).toBeDisabled(); await expect(section.getByRole('button', { name: 'Next gallery object', exact: true })).toBeDisabled();
  await page.reload(); await relations(page, f.gallery); await expect(section.getByRole('button', { name: 'Show First gallery passage in gallery', exact: true })).toBeVisible();
  expect((await stored(request, f.saved.id)).nodes.find(node => node.id === child.id)?.parentId).toBe(f.gallery.id);
});

test('Gallery Relations browsing is transient, ordering saves and undoes, and Inspect opens the actual child content', async ({ page, request }) => {
  const f = await fixture(page, request, ['First object', 'Second object']), section = contents(page, f.gallery.id);
  await expect(section.locator('.inspector-gallery-position')).toHaveText('1 / 2');
  await expect(page.getByRole('button', { name: 'Undo', exact: true })).toBeDisabled();
  await section.getByRole('button', { name: 'Next gallery object', exact: true }).click(); await expect(section.locator('.inspector-gallery-position')).toHaveText('2 / 2');
  await expect(section.locator(`[data-gallery-object="${f.items[1]!.id}"]`)).toHaveClass(/is-current/);
  await expect(section.getByRole('button', { name: 'Next gallery object', exact: true })).toBeDisabled();
  await section.getByRole('button', { name: 'Show First object in gallery', exact: true }).click(); await expect(section.locator('.inspector-gallery-position')).toHaveText('1 / 2');
  const afterBrowsing = await stored(request, f.saved.id); expect(afterBrowsing.revision).toBe(f.saved.revision); expect(afterBrowsing.nodes).toEqual(f.saved.nodes); await expect(page.getByRole('button', { name: 'Undo', exact: true })).toBeDisabled();
  await section.getByRole('button', { name: 'Move Second object earlier', exact: true }).click();
  await expect.poll(async () => (await stored(request, f.saved.id)).nodes.filter(node => node.parentId === f.gallery.id).map(node => node.id)).toEqual([f.items[1]!.id, f.items[0]!.id]);
  expect((await stored(request, f.saved.id)).nodes.filter(node => node.parentId === f.gallery.id).map(node => ({ id: node.id, x: node.x, y: node.y, width: node.width, height: node.height })).sort((a, b) => a.id.localeCompare(b.id))).toEqual(f.items.map(node => ({ id: node.id, x: node.x, y: node.y, width: node.width, height: node.height })).sort((a, b) => a.id.localeCompare(b.id)));
  await page.getByRole('button', { name: 'Undo', exact: true }).click(); await expect.poll(async () => (await stored(request, f.saved.id)).nodes.filter(node => node.parentId === f.gallery.id).map(node => node.id)).toEqual(f.items.map(node => node.id));
  await section.getByRole('button', { name: 'Inspect gallery object: Second object', exact: true }).click();
  await expect(page.getByRole('combobox', { name: 'Side panel', exact: true })).toHaveValue('content'); await expect(page.locator('.inspector').getByLabel('Title', { exact: true })).toHaveValue('Second object'); await expect(page.locator('.inspector').getByLabel('Text body', { exact: true })).toHaveValue('The second object passage.');
  await relations(page, f.gallery); await expect(section.locator('.inspector-gallery-position')).toHaveText('2 / 2');
  await page.reload(); await relations(page, f.gallery); await expect(section.locator('.inspector-gallery-position')).toHaveText('1 / 2');
});
