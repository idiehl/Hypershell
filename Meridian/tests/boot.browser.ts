import { test, expect, type Page } from '@playwright/test';
import { addCanvas, addNode, commitSnapshots, newProject } from '../src/core/model.ts';

async function deferRecovery(page: Page, projectId: string) {
  let release!: () => void, intercepted!: () => void;
  const pending = new Promise<void>(resolve => { intercepted = resolve; });
  const hold = new Promise<void>(resolve => { release = resolve; });
  await page.route(`**/api/projects/${projectId}/recovery`, async route => {
    const response = await route.fetch();
    intercepted();
    await hold;
    await route.fulfill({ response });
  });
  return { pending, release };
}

for (const requested of ['entry', 'inner'] as const) test(`opening the ${requested} canvas preserves the first selection while recovery finishes`, async ({ page, request }, info) => {
  const project = newProject(`Boot selection ${requested}`), inner = addCanvas(project, 'Requested inner canvas');
  const entryNode = addNode(project, project.entryCanvasId, 'text', 'The entry tile');
  const innerNode = addNode(project, inner.id, 'text', 'The requested inner tile');
  for (const node of [entryNode, innerNode]) Object.assign(node, { x: 320, y: 220, width: 340, height: 220, childLayout: 'spatial', text: 'Selecting this tile before the recovery check completes must not lose the selection or move the camera.' });
  const created = await request.post('/api/projects', { data: commitSnapshots(project) }); expect(created.ok()).toBeTruthy();
  const target = requested === 'entry' ? entryNode : innerNode;
  const recovery = await deferRecovery(page, project.id);
  try {
    await page.goto(`/?project=${project.id}&canvas=${target.canvasId}`);
    await recovery.pending;
    const tile = page.locator(`.ms-node[data-node-id="${target.id}"]`);
    // The requested canvas must already be visible before recovery is released.
    await expect(tile).toBeVisible();
    expect(new URL(page.url()).searchParams.get('canvas')).toBe(target.canvasId);
    await page.getByRole('button', { name: 'Fit Work', exact: true }).click();
    await tile.locator('.ms-node-title').click();
    await page.getByLabel('Side panel', { exact: true }).selectOption('content');
    await expect(page.locator('.inspector').getByLabel('Title', { exact: true })).toHaveValue(target.title);
    const camera = await page.locator('.ms-world').evaluate(node => (node as HTMLElement).style.transform), address = page.url();
    recovery.release();
    await page.waitForLoadState('networkidle');
    await expect(tile).toHaveAttribute('aria-selected', 'true');
    await expect(page.locator('.inspector').getByLabel('Title', { exact: true })).toHaveValue(target.title);
    expect(await page.locator('.ms-world').evaluate(node => (node as HTMLElement).style.transform)).toBe(camera);
    expect(page.url()).toBe(address);
  } finally {
    recovery.release();
    if (test.info().status !== test.info().expectedStatus) await page.screenshot({ path: info.outputPath('boot-recovery-failure.png'), fullPage: true });
  }
});
