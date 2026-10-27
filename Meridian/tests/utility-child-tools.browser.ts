import { test, expect, type APIRequestContext, type Page } from '@playwright/test';
import { addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import { arrangeChildren } from '../src/core/grid.ts';
import type { MeridianProject } from '../src/core/types.ts';

const stored = async (request: APIRequestContext, id: string): Promise<MeridianProject> => (await request.get(`/api/projects/${id}`)).json();
const tile = (page: Page, id: string) => page.locator(`.ms-node[data-node-id="${id}"]`);

for (const ordered of [true, false]) test(`bulk child appearance and ${ordered ? 'ordered cell dimensions' : 'free dimensions and cell fitting'} preserve routing utilities`, async ({ page, request }) => {
  let project = newProject(`Utility bulk protection ${ordered ? 'ordered' : 'enclosed'}`);
  const parent = addNode(project, project.entryCanvasId, 'container', 'Editable collection');
  Object.assign(parent, { x: 120, y: 100, width: 620, height: 400, sizeMode: 'fixed', collapsed: false, childLayout: 'contained', containerVariant: ordered ? 'spreadsheet' : 'contained', childArrangement: ordered ? 'grid' : 'free', grid: { columns: 2, rows: 1, cellWidth: 220, cellHeight: 150, gap: 0, showHeaders: false } });
  const first = addNode(project, parent.canvasId, 'text', 'First entry'), second = addNode(project, parent.canvasId, 'text', 'Second entry');
  for (const [index, child] of [first, second].entries()) Object.assign(child, { parentId: parent.id, x: 130 + index * 250, y: 180, width: 180, height: 120, sizeMode: 'fixed', text: 'Preserve this ordinary content.', style: { shape: 'rectangle', fill: '#123A37' } });
  if (ordered) project = arrangeChildren(project, parent.id, 'grid').project;
  const pivot = addNode(project, parent.canvasId, 'junction', 'Protected pivot'), joint = addNode(project, parent.canvasId, 'junction', 'Protected joint');
  // The ordered row begins at y=128. Keep utilities clear of those tile edges,
  // so this tests bulk editing rather than the editor's initial collision repair.
  Object.assign(pivot, { parentId: parent.id, utilityVariant: 'pivot', x: 154, y: 108, width: 12, height: 12, sizeMode: 'fixed', style: { shape: 'circle', fill: '#cc8844', border: '#664422', borderWidth: 1 }, snapping: { stickyEdges: false, snapToGrid: false, smartAlign: false }, placement: 'free' });
  Object.assign(joint, { parentId: parent.id, utilityVariant: 'joint', x: 300, y: 114, width: 32, height: 6, sizeMode: 'fixed', style: { shape: 'rectangle', fill: '#4488cc', border: '#224466', borderWidth: 0 }, snapping: { stickyEdges: false, snapToGrid: false, smartAlign: false }, placement: 'free' });
  project.links.push({ id: crypto.randomUUID(), canvasId: parent.canvasId, parentId: parent.id, sourceId: first.id, targetId: pivot.id, kind: 'visual', relation: 'generic', direction: 'none' },
    { id: crypto.randomUUID(), canvasId: parent.canvasId, parentId: parent.id, sourceId: pivot.id, targetId: joint.id, kind: 'visual', relation: 'generic', direction: 'none' },
    { id: crypto.randomUUID(), canvasId: parent.canvasId, parentId: parent.id, sourceId: joint.id, targetId: second.id, kind: 'visual', relation: 'generic', direction: 'none' });
  expect((await request.post('/api/projects', { data: commitSnapshots(project) })).ok()).toBeTruthy();
  await page.goto(`/?project=${project.id}&canvas=${project.entryCanvasId}`);
  await tile(page, parent.id).locator('.ms-node-title').click();
  await page.getByRole('combobox', { name: 'Side panel', exact: true }).selectOption('relations');
  const tools = page.locator('.child-editing-tools'); await tools.locator('summary').click();
  const baseline = await stored(request, project.id), utilities = baseline.nodes.filter(node => node.id === pivot.id || node.id === joint.id);
  const preserved = async () => expect((await stored(request, project.id)).nodes.filter(node => node.id === pivot.id || node.id === joint.id)).toEqual(utilities);
  await tools.getByLabel('Child style preset', { exact: true }).selectOption('ink');
  await tools.getByLabel('Child shape', { exact: true }).selectOption('hexagon');
  await tools.getByRole('button', { name: 'Apply child appearance', exact: true }).click();
  await expect.poll(async () => (await stored(request, project.id)).nodes.find(node => node.id === first.id)?.style?.shape).toBe('hexagon'); await preserved();
  await tools.getByLabel('Child width', { exact: true }).fill('240'); await tools.getByLabel('Child height', { exact: true }).fill('170');
  await tools.getByRole('button', { name: 'Apply child dimensions', exact: true }).click();
  await expect.poll(async () => (await stored(request, project.id)).nodes.find(node => node.id === first.id)?.width).toBe(240); await preserved();
  if (!ordered) {
    await tools.getByRole('button', { name: 'Fit child tiles to cells', exact: true }).click();
    await expect.poll(async () => (await stored(request, project.id)).nodes.find(node => node.id === first.id)?.width).toBe(220); await preserved();
  }
  await page.getByRole('button', { name: 'Undo', exact: true }).click();
  await expect.poll(async () => (await stored(request, project.id)).nodes.find(node => node.id === first.id)?.width).toBe(ordered ? 220 : 240); await preserved();
  await page.reload(); await expect(tile(page, pivot.id)).toBeVisible(); await expect(tile(page, joint.id)).toBeVisible(); await preserved();
});
