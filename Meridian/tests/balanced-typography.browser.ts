import { test, expect, type Page } from '@playwright/test';
import { build } from 'esbuild';
import { readFile } from 'node:fs/promises';
import { addNode, newProject } from '../src/core/model.ts';

let script = '', css = '';
test.beforeAll(async () => {
  script = (await build({ stdin: { contents: `import {renderScene} from './src/scene.ts';window.balancedScene={renderScene};`, resolveDir: process.cwd(), loader: 'ts' }, bundle: true, write: false, format: 'iife', platform: 'browser', target: 'es2022' })).outputFiles[0]!.text;
  css = (await Promise.all(['scene.css', 'grid.css', 'gallery-scene.css', 'window-scene.css', 'canvas-launcher.css'].map(file => readFile(`src/${file}`, 'utf8')))).join('\n');
});

async function mount(page: Page) {
  const project = newProject('Balanced typography'), ids: Record<string, string> = {};
  const add = (key: string, title: string, x: number, y: number) => {
    const node = addNode(project, project.entryCanvasId, 'media', title); Object.assign(node, { x, y }); ids[key] = node.id; return node;
  };
  add('title', 'Zeus', 40, 40);
  add('long', 'System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>', 40, 220);
  const body = add('body', '', 40, 380); body.text = 'Lightning';
  const mixed = add('mixed', 'Olympus', 390, 380); mixed.text = 'Sky';
  const fixed = add('fixed', 'Zeus', 720, 40); Object.assign(fixed, { sizeMode: 'fixed', width: 280, height: 180 });
  const legacy = add('legacy', 'Old title', 1140, 40); delete legacy.style;
  const explicit = add('explicit', 'Right alignment', 750, 380); explicit.style = { ...explicit.style, titleSize: 44, fontSize: 27, textAlign: 'right' };
  await page.setViewportSize({ width: 1900, height: 950 });
  await page.route(/^https?:/, route => route.abort());
  await page.setContent('<style>html,body{margin:0}#scene{width:1900px;height:950px}</style><main id="scene"></main>');
  await page.addStyleTag({ content: css }); await page.addScriptTag({ content: script });
  await page.evaluate(({ project, ids }) => {
    const before = JSON.stringify(project);
    const draw = () => (window as any).balancedScene.renderScene(document.querySelector('#scene'), project, project.entryCanvasId, { fit: false, view: { x: 0, y: 0, scale: 1 }, readonly: true });
    (window as any).balancedFixture = { project, ids, before, draw }; draw();
  }, { project, ids });
  return ids;
}

async function geometry(page: Page, id: string, selector = '.ms-node-title') {
  return page.locator(`.ms-node[data-node-id="${id}"]`).evaluate((card, selector) => {
    const target = card.querySelector<HTMLElement>(selector)!; const c = card.getBoundingClientRect(), s = getComputedStyle(target);
    const r = document.createRange(); r.selectNodeContents(target); const t = r.getBoundingClientRect();
    return { card: { x: c.x, y: c.y, width: c.width, height: c.height }, glyph: { x: t.x, y: t.y, width: t.width, height: t.height }, lines: new Set([...r.getClientRects()].map(rect => Math.round(rect.y))).size, font: parseFloat(s.fontSize), line: parseFloat(s.lineHeight), align: s.textAlign, fit: Number((card as HTMLElement).dataset.contentFit ?? 1), scroll: target.scrollWidth, client: target.clientWidth };
  }, selector);
}

test('new title and body defaults are large, centered and use the65percent reading budget', async ({ page }, info) => {
  const ids = await mount(page);
  for (const [key, selector, font] of [['title', '.ms-node-title', 32], ['body', '.ms-text', 22]] as const) {
    const result = await geometry(page, ids[key]!, selector);
    expect(result.font).toBe(font); expect(result.align).toBe('center'); expect(result.fit).toBeCloseTo(1, 5);
    expect(Math.abs(result.glyph.x + result.glyph.width / 2 - result.card.x - result.card.width / 2)).toBeLessThan(2);
    expect(Math.abs(result.glyph.y + result.glyph.height / 2 - result.card.y - result.card.height / 2)).toBeLessThan(3);
    expect(result.line / (result.card.height - 2)).toBeGreaterThan(.62); expect(result.line / (result.card.height - 2)).toBeLessThan(.67);
  }
  const mixed = await geometry(page, ids.mixed!); expect(mixed.font).toBe(32);
  await page.screenshot({ path: info.outputPath('balanced-node-defaults.png') });
  expect(await page.evaluate(() => JSON.stringify((window as any).balancedFixture.project) === (window as any).balancedFixture.before)).toBe(true);
});

test('a long automatic title remains a complete single line while explicit geometry and old fonts remain intact', async ({ page }) => {
  const ids = await mount(page), long = await geometry(page, ids.long!);
  expect(long.card.width).toBeGreaterThan(420); expect(long.lines).toBe(1); expect(long.scroll).toBeLessThanOrEqual(long.client + 1);
  const fixed = await geometry(page, ids.fixed!); expect(fixed.card.width).toBe(280); expect(fixed.card.height).toBe(180); expect(fixed.font).toBe(32); expect(fixed.fit).toBe(1);
  expect(Math.abs(fixed.glyph.y + fixed.glyph.height / 2 - fixed.card.y - fixed.card.height / 2)).toBeLessThan(3);
  const older = await geometry(page, ids.legacy!); expect(older.font).toBe(13);
  const explicit = await geometry(page, ids.explicit!); expect(explicit.font).toBe(44); expect(explicit.align).toBe('right');
  const priorWidth = long.card.width;
  await page.evaluate(() => { const f = (window as any).balancedFixture; const node = f.project.nodes.find((n: any) => n.id === f.ids.long); node.showTitle = false; node.text = 'Short'; f.draw(); });
  const hidden = await geometry(page, ids.long!, '.ms-text'); expect(hidden.card.width).toBeLessThan(priorWidth / 3);
});

test('balanced long titles and paragraphs fit their allocated Grid, Window and Gallery surfaces without changing those frames', async ({ page }, info) => {
  const project = newProject('Allocated typography'), ids: Record<string, string> = {};
  for (const [index, pattern] of ['grid', 'window', 'gallery'].entries()) {
    const parent = addNode(project, project.entryCanvasId, 'container', pattern);
    Object.assign(parent, { x: 40 + index * 620, y: 40, width: 540, height: 650, sizeMode: 'fixed', collapsed: false, childLayout: 'contained', containerVariant: pattern, childArrangement: pattern === 'gallery' ? 'free' : 'grid',
      ...(pattern === 'gallery' ? {} : { grid: { columns: 1, rows: 5, cellWidth: 480, cellHeight: 240, windowColumns: 1, windowRows: 2, showHeaders: false } }) });
    const child = addNode(project, project.entryCanvasId, 'media', 'A complete and deliberately long title stays available');
    Object.assign(child, { parentId: parent.id, x: parent.x + 6, y: parent.y + 56, text: 'Every sentence remains part of the native reading surface. '.repeat(5), ...(pattern === 'gallery' ? {} : { cell: { column: 1, row: 1 } }) });
    ids[pattern] = parent.id; ids[`${pattern}-child`] = child.id;
  }
  await page.setViewportSize({ width: 1900, height: 950 }); await page.route(/^https?:/, route => route.abort());
  await page.setContent('<style>html,body{margin:0}#scene{width:1900px;height:950px}</style><main id="scene"></main>');
  await page.addStyleTag({ content: css }); await page.addScriptTag({ content: script });
  await page.evaluate(project => { (window as any).allocatedBefore = JSON.stringify(project); (window as any).allocatedProject = project; (window as any).balancedScene.renderScene(document.querySelector('#scene'), project, project.entryCanvasId, { fit: false, view: { x: 0, y: 0, scale: 1 }, readonly: true }); }, project);
  for (const pattern of ['grid', 'window', 'gallery']) {
    const parent = page.locator(`.ms-node[data-node-id="${ids[pattern]}"]`); await expect(parent).toBeVisible();
    const b = (await parent.boundingBox())!; expect(b.width).toBe(540); expect(b.height).toBe(650);
    const target = pattern === 'gallery' ? parent.locator('.ms-gallery-item').first() : page.locator(`.ms-node[data-node-id="${ids[`${pattern}-child`]}"]`).first();
    await expect(target).toBeVisible();
    const result = await target.evaluate(element => {
      const frame = element.getBoundingClientRect(), boxes = [...element.querySelectorAll('.ms-node-title,.ms-text,.ms-gallery-item-title,.ms-gallery-text')].filter(e => e.textContent?.trim()).map(text => { const range = document.createRange(); range.selectNodeContents(text); const b = range.getBoundingClientRect(); return { text: text.textContent, align: getComputedStyle(text).textAlign, left: b.left, top: b.top, right: b.right, bottom: b.bottom }; });
      return { frame: { left: frame.left, top: frame.top, right: frame.right, bottom: frame.bottom }, boxes };
    });
    expect(result.boxes.length).toBeGreaterThan(1);
    for (const box of result.boxes) { expect(box.align).toBe('center'); expect(box.left).toBeGreaterThanOrEqual(result.frame.left - 2); expect(box.right).toBeLessThanOrEqual(result.frame.right + 2); expect(box.top).toBeGreaterThanOrEqual(result.frame.top - 3); expect(box.bottom).toBeLessThanOrEqual(result.frame.bottom + 3); }
  }
  expect(await page.evaluate(() => JSON.stringify((window as any).allocatedProject) === (window as any).allocatedBefore)).toBe(true);
  await page.screenshot({ path: info.outputPath('balanced-allocated-surfaces.png') });
});
