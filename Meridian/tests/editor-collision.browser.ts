import { test, expect, type APIRequestContext } from '@playwright/test';
import { addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import { nodeShape, shapesOverlap } from '../src/core/geometry.ts';
import { navigationIndex } from '../src/core/navigation.ts';
import type { MeridianProject } from '../src/core/types.ts';
import { inspectorPanel, selectEditorNode } from './ui-helpers.ts';

async function stored(request: APIRequestContext, id: string): Promise<MeridianProject> {
  return (await request.get(`/api/projects/${id}`)).json();
}
function overlaps(project: MeridianProject, first: string, second: string): boolean {
  const index = navigationIndex(project), a = index.nodes.get(first)!, b = index.nodes.get(second)!;
  return shapesOverlap(nodeShape(a, index.bounds(a)), nodeShape(b, index.bounds(b)));
}

test('legacy peer overlaps are separated on opening and cannot return through undo, redo or a size edit', async ({ page, request }) => {
  const project = newProject('Collision history');
  const first = addNode(project, project.entryCanvasId, 'text', 'First tile');
  const second = addNode(project, project.entryCanvasId, 'text', 'Second tile');
  for (const node of [first, second]) Object.assign(node, { x: 120, y: 160, width: 200, height: 140, text: `${node.title} content` });
  expect(overlaps(project, first.id, second.id)).toBe(true);
  expect((await request.post('/api/projects', { data: commitSnapshots(project) })).ok()).toBeTruthy();
  await page.goto(`/?project=${project.id}&canvas=${project.entryCanvasId}`);
  await expect.poll(async () => overlaps(await stored(request, project.id), first.id, second.id)).toBe(false);
  await page.getByRole('button', { name: 'Undo', exact: true }).click();
  await expect.poll(async () => (await stored(request, project.id)).revision).toBeGreaterThan(2);
  expect(overlaps(await stored(request, project.id), first.id, second.id)).toBe(false);
  await page.getByRole('button', { name: 'Redo', exact: true }).click();
  await selectEditorNode(page, first.id, first.title);
  await inspectorPanel(page, 'appearance');
  const width = page.locator('.inspector').getByLabel('Width', { exact: true });
  await width.fill('600'); await width.press('Tab');
  await expect.poll(async () => (await stored(request, project.id)).nodes.find(node => node.id === first.id)?.width).toBe(600);
  const saved = await stored(request, project.id);
  expect(overlaps(saved, first.id, second.id)).toBe(false);
  for (const node of [first, second]) expect(saved.nodes.find(item => item.id === node.id)?.text).toBe(node.text);
});
