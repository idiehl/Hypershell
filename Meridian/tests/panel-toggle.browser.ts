import { test, expect } from '@playwright/test';
import { addNode, commitSnapshots, newProject } from '../src/core/model.ts';

test('paired side panel arrows preserve the selected menu and keyboard access at desktop and compact widths', async ({ page, request }) => {
  const project = newProject('Side panel arrow controls');
  const node = addNode(project, project.entryCanvasId, 'text', 'Panel selection');
  Object.assign(node, { x: 100, y: 100, width: 230, height: 150, text: 'Keep the selected node when the side panel is closed.' });
  expect((await request.post('/api/projects', { data: commitSnapshots(project) })).ok()).toBeTruthy();
  await page.goto(`/?project=${project.id}&canvas=${project.entryCanvasId}`);
  await page.locator(`[data-node-id="${node.id}"] .ms-node-title`).click();
  await page.getByLabel('Side panel', { exact: true }).selectOption('appearance');

  for (const viewport of [{ width: 1500, height: 700 }, { width: 700, height: 500 }]) {
    await page.setViewportSize(viewport);
    const inspector = page.getByRole('complementary', { name: 'Canvas side panel' });
    const close = inspector.getByRole('button', { name: 'Close side panel', exact: true });
    const choice = inspector.getByLabel('Side panel', { exact: true });
    await expect(close).toHaveAttribute('aria-expanded', 'true');
    await expect(close).toHaveAttribute('aria-controls', 'meridian-side-panel');
    await expect(close.locator('svg path')).toHaveAttribute('d', 'M4 12h16m-7-7 7 7-7 7');
    const positions = await close.evaluate(button => ({
      closeRight: button.getBoundingClientRect().right,
      selectLeft: button.parentElement!.querySelector('select')!.getBoundingClientRect().left,
      first: button.parentElement!.firstElementChild === button,
    }));
    expect(positions.first).toBeTruthy();
    expect(positions.closeRight).toBeLessThan(positions.selectLeft);
    await close.focus(); await page.keyboard.press('Enter');
    await expect(inspector).toBeHidden();
    const reopen = page.getByRole('button', { name: 'Show side panel', exact: true });
    await expect(reopen).toBeVisible();
    await expect(reopen).toBeFocused();
    await expect(reopen).toHaveAttribute('aria-expanded', 'false');
    await expect(reopen.locator('svg path')).toHaveAttribute('d', 'M20 12H4m7-7-7 7 7 7');
    await page.keyboard.press('Enter');
    await expect(inspector).toBeVisible();
    await expect(choice).toHaveValue('appearance');
    await expect(choice).toBeFocused();
    await expect(reopen).toHaveCount(0);
    await expect(inspector.getByRole('button', { name: 'Close side panel', exact: true })).toHaveCount(1);
    const body = inspector.locator('.inspector-panel-appearance');
    await body.hover(); await page.mouse.wheel(0, 5000);
    await expect(body.getByLabel('Width', { exact: true })).toBeInViewport({ ratio: 1 });
    await expect(body.getByLabel('Height', { exact: true })).toBeInViewport({ ratio: 1 });
  }
});
