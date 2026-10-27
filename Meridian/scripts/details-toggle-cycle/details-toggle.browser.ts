import { test, expect } from '@playwright/test';
import { writeFile } from 'node:fs/promises';
import { newProject, addNode, commitSnapshots } from '../../src/core/model.ts';

test('Details controls center the plus at desktop scaling and keep disclosure independent', async ({ browser, request }, info) => {
  const project = newProject('Details toggle visual review'), canvas = project.canvases[0]!;
  const parent = addNode(project, canvas.id, 'media', 'My canvases'); Object.assign(parent, { x: 100, y: 100, width: 260, height: 160, sizeMode: 'fixed', collapsed: true });
  const child = addNode(project, canvas.id, 'media', 'Nested canvas'); Object.assign(child, { parentId: parent.id, x: 500, y: 500, width: 200, height: 120, sizeMode: 'fixed' });
  const response = await request.post('/api/projects', { data: commitSnapshots(project) }); expect(response.ok(), await response.text()).toBe(true);
  const original = await (await request.get(`/api/projects/${project.id}`)).json();
  const measurements: unknown[] = [];
  for (const scale of [1, 1.25, 1.5]) {
    const context = await browser.newContext({ baseURL: info.project.use.baseURL as string, viewport: { width: 1500, height: 950 }, deviceScaleFactor: scale });
    const page = await context.newPage(), errors: string[] = []; page.on('pageerror', error => errors.push(error.message));
    try {
      await page.goto(`/?project=${project.id}&canvas=${canvas.id}`); await page.getByRole('combobox', { name: 'Side panel', exact: true }).selectOption('canvas');
      const divider = page.getByRole('separator', { name: 'Resize side panel', exact: true }); await divider.focus(); await divider.press('Home'); for (let at = 0; at < 3; at++) await divider.press('ArrowRight');
      await expect(divider).toHaveAttribute('aria-valuenow', '260');
      const row = page.locator(`[data-outline-node="${parent.id}"]`), button = row.locator('[data-outline-more]'), drawer = row.locator('[data-outline-details]');
      const measure = async (state: string) => {
        const value = await button.evaluate(button => {
          const b = button.getBoundingClientRect(), style = getComputedStyle(button), path = button.querySelector('svg path')! as SVGGraphicsElement;
          const ink = path.getBBox(), center = new DOMPoint(ink.x + ink.width / 2, ink.y + ink.height / 2).matrixTransform(path.getScreenCTM()!);
          return { width: b.width, height: b.height, offsetX: center.x - b.x - b.width / 2, offsetY: center.y - b.y - b.height / 2, border: style.borderWidth, radius: style.borderRadius, background: style.backgroundColor, fontGlyph: button.textContent?.trim(), expanded: button.getAttribute('aria-expanded'), focusVisible: button.matches(':focus-visible') };
        });
        expect(value.width).toBe(24); expect(value.height).toBe(24); expect(Math.abs(value.offsetX)).toBeLessThan(.1); expect(Math.abs(value.offsetY)).toBeLessThan(.1); expect(value.border).toBe('0px'); expect(value.fontGlyph).toBe('');
        measurements.push({ scale, state, ...value }); await row.screenshot({ path: info.outputPath(`details-${scale * 100}-${state}.png`) });
        return value;
      };
      await page.mouse.move(0, 0); await expect(button).toHaveAttribute('title', 'Details'); await expect(button).toHaveAttribute('aria-expanded', 'false');
      const collapsed = await measure('collapsed'); expect(collapsed.background).toBe('rgba(0, 0, 0, 0)');
      await button.hover(); const hover = await measure('hover'); expect(hover.radius).toBe('50%');
      await button.click(); await expect(drawer).toBeVisible(); await expect(row).toHaveAttribute('aria-expanded', 'false');
      await page.mouse.move(0, 0); const expanded = await measure('expanded'); expect(expanded.radius).toBe('50%');
      await row.locator('[data-jump-to-node]').focus(); await page.keyboard.press('Tab'); await expect(button).toBeFocused();
      const focused = await measure('focused'); expect(focused.focusVisible).toBe(true);
      await page.keyboard.press('Enter'); await expect(drawer).toBeHidden(); await expect(row).toHaveAttribute('aria-expanded', 'false');
      await row.locator('.inspector-tree-title').click(); await expect(page.locator(`[data-outline-node="${child.id}"]`)).toBeVisible(); await expect(page.locator(`.ms-node[data-node-id="${parent.id}"]`)).toHaveAttribute('aria-selected', 'true');
      await expect(page.locator(`.ms-node[data-node-id="${child.id}"]`)).toHaveCount(0); await expect(button).toHaveAttribute('aria-expanded', 'false');
      expect(await page.locator('.inspector-panel-body').evaluate(el => el.scrollWidth <= el.clientWidth + 1)).toBe(true); expect(errors).toEqual([]);
    } finally { await context.close(); }
  }
  expect(await (await request.get(`/api/projects/${project.id}`)).json()).toEqual(original);
  await writeFile(info.outputPath('details-control-measurements.json'), JSON.stringify(measurements, null, 2));
});
