import { test, expect } from '@playwright/test';
import { addNode, commitSnapshots, newProject } from '../src/core/model.ts';

test('Appearance scrolls its last dimension fields fully into the window after resizing', async ({ page, request }, info) => {
  const project = newProject('Appearance scroll bounds');
  const node = addNode(project, project.entryCanvasId, 'text', 'Resizable tile');
  Object.assign(node, { x: 100, y: 100, width: 230, height: 150, text: 'Appearance controls must remain reachable.' });
  expect((await request.post('/api/projects', { data: commitSnapshots(project) })).ok()).toBeTruthy();
  await page.goto(`/?project=${project.id}&canvas=${project.entryCanvasId}`);
  await page.locator(`[data-node-id="${node.id}"] .ms-node-title`).click();
  await page.getByLabel('Side panel', { exact: true }).selectOption('appearance');
  for (const viewport of [{ width: 1500, height: 700 }, { width: 900, height: 540 }, { width: 700, height: 500 }]) {
    await page.setViewportSize(viewport);
    const section = page.locator('.inspector-panel-appearance');
    await section.evaluate(element => { element.scrollTop = 0; });
    await section.hover();
    await page.mouse.wheel(0, 5000);
    await expect.poll(() => section.evaluate(element => element.scrollTop + element.clientHeight - element.scrollHeight)).toBeGreaterThanOrEqual(-1);
    const metrics = await section.evaluate(element => {
      const bounds = element.getBoundingClientRect(), width = element.querySelector<HTMLInputElement>('[aria-label="Width"]')!, height = element.querySelector<HTMLInputElement>('[aria-label="Height"]')!;
      return { scrollTop: element.scrollTop, scrollHeight: element.scrollHeight, clientHeight: element.clientHeight,
        bottom: bounds.bottom, viewportHeight: window.visualViewport?.height ?? window.innerHeight,
        documentScrollTop: document.documentElement.scrollTop,
        widthBottom: width.getBoundingClientRect().bottom, heightBottom: height.getBoundingClientRect().bottom };
    });
    await info.attach(`scroll-bounds-${viewport.width}`, { body: JSON.stringify(metrics), contentType: 'application/json' });
    expect(metrics.bottom).toBeLessThanOrEqual(metrics.viewportHeight + 1);
    expect(metrics.documentScrollTop).toBe(0);
    expect(metrics.widthBottom).toBeLessThanOrEqual(Math.min(metrics.bottom, metrics.viewportHeight) - 6);
    expect(metrics.heightBottom).toBeLessThanOrEqual(Math.min(metrics.bottom, metrics.viewportHeight) - 6);
    await expect(section.getByLabel('Width', { exact: true })).toBeInViewport({ ratio: 1 });
    await expect(section.getByLabel('Height', { exact: true })).toBeInViewport({ ratio: 1 });
    await section.getByLabel('Height', { exact: true }).focus();
    await page.screenshot({ path: info.outputPath(`appearance-bottom-${viewport.width}.png`) });
  }
});
