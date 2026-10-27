import { test, expect, type Page } from '@playwright/test';
import { build } from 'esbuild';
import { readFile } from 'node:fs/promises';

let script: string, css: string;
test.beforeAll(async () => {
  css = await readFile('src/workspace-tools.css', 'utf8');
  script = (await build({ stdin: { contents: `import {workspaceTools} from './src/workspace-tools.ts';window.selectionToolHarness={workspaceTools};`,
    resolveDir: process.cwd(), loader: 'ts' }, bundle: true, write: false, format: 'iife', platform: 'browser', target: 'es2022' })).outputFiles[0]!.text;
});

async function mount(page: Page) {
  await page.setContent('<style>body{margin:0;background:#101d25;color:#dce9ee;font:14px system-ui}button{background:transparent;border:0;color:inherit;cursor:pointer}.toolbar{padding:20px}.canvas{height:500px}</style><div class="toolbar"></div><main class="canvas"></main>');
  await page.addStyleTag({ content: css }); await page.addScriptTag({ content: script });
  await page.evaluate(() => {
    const state = { mode: 'select', navigationMode: 'select', changes: [] as string[] };
    const change = (mode: string) => { state.mode = mode; if (mode === 'select' || mode === 'hand') state.navigationMode = mode; state.changes.push(mode); render(); };
    const render = () => document.querySelector('.toolbar')!.replaceChildren((window as any).selectionToolHarness.workspaceTools(state.mode, change, {
      active: state.mode === 'hand', navigationMode: state.navigationMode,
      activate: () => change(state.mode === 'hand' ? 'select' : 'hand'),
    }));
    (window as any).selectionToolState = state; render();
  });
}

test('the default finger tool occupies the third slot and its menu changes the active tool', async ({ page }, info) => {
  await mount(page);
  const group = page.getByRole('group', { name: 'Work tools', exact: true });
  await expect(group.getByRole('button')).toHaveCount(3);
  for (const [index, name] of ['New Node', 'Delete objects', 'Single select'].entries())
    await expect(group.getByRole('button').nth(index)).toHaveAttribute('aria-label', name);
  const select = page.getByRole('button', { name: 'Single select', exact: true });
  await expect(select).toHaveAttribute('aria-pressed', 'true'); await expect(select).toHaveAttribute('aria-haspopup', 'menu');
  const finger = await select.locator('svg path').getAttribute('d');
  await select.click(); await expect(page.getByRole('menu', { name: 'Selection tool' })).toBeVisible();
  expect(await page.evaluate(() => (window as any).selectionToolState.changes)).toEqual([]);
  await page.getByRole('menuitem', { name: 'Select Area', exact: true }).click();
  const hand = page.getByRole('button', { name: 'Select Area', exact: true });
  await expect(hand).toHaveAttribute('aria-pressed', 'true');
  expect(await hand.locator('svg path').getAttribute('d')).not.toBe(finger);
  await hand.click(); await page.getByRole('menuitem', { name: 'Single select', exact: true }).click();
  await expect(select).toHaveAttribute('aria-pressed', 'true');
  expect(await page.evaluate(() => (window as any).selectionToolState.changes)).toEqual(['hand', 'select']);
  await page.screenshot({ path: info.outputPath('single-select-toolbar.png') });
});

test('creation and deletion keep Select Area available in the interaction dropdown', async ({ page }) => {
  await mount(page);
  await page.getByRole('button', { name: 'Single select', exact: true }).click(); await page.getByRole('menuitem', { name: 'Select Area', exact: true }).click();
  await page.getByRole('button', { name: 'New Node', exact: true }).click(); await page.getByRole('menuitem', { name: 'Link', exact: true }).click();
  await expect(page.getByRole('button', { name: 'New Link', exact: true })).toHaveAttribute('aria-pressed', 'true');
  await expect(page.getByRole('button', { name: 'Select Area', exact: true })).toHaveAttribute('aria-pressed', 'false');
  await page.getByRole('button', { name: 'Delete objects', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Delete objects', exact: true })).toHaveAttribute('aria-pressed', 'true');
  await page.getByRole('button', { name: 'Select Area', exact: true }).click(); await page.getByRole('menuitem', { name: 'Select Area', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Select Area', exact: true })).toHaveAttribute('aria-pressed', 'true');
  await page.getByRole('button', { name: 'Select Area', exact: true }).click(); await page.getByRole('menuitem', { name: 'Single select', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Single select', exact: true })).toHaveAttribute('aria-pressed', 'true');
});

test('keyboard selection works and dismissed menus do not change the active mode', async ({ page }) => {
  await mount(page);
  const select = page.getByRole('button', { name: 'Single select', exact: true }); await select.focus(); await page.keyboard.press('Enter');
  await expect(page.getByRole('menuitem', { name: 'Single select', exact: true })).toBeFocused();
  await page.keyboard.press('ArrowDown'); await expect(page.getByRole('menuitem', { name: 'Select Area', exact: true })).toBeFocused();
  await page.keyboard.press('Enter'); await expect(page.getByRole('button', { name: 'Select Area', exact: true })).toHaveAttribute('aria-pressed', 'true');
  await page.getByRole('button', { name: 'Select Area', exact: true }).click(); await page.keyboard.press('Escape');
  await expect(page.getByRole('menu')).toHaveCount(0);
  await page.getByRole('button', { name: 'Select Area', exact: true }).click(); await page.locator('.canvas').click({ position: { x: 100, y: 100 } });
  await expect(page.getByRole('menu')).toHaveCount(0);
  expect(await page.evaluate(() => (window as any).selectionToolState.changes)).toEqual(['hand']);
});
