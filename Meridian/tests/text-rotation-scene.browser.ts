import { test, expect, type Page } from '@playwright/test';
import { build } from 'esbuild';
import { readFile } from 'node:fs/promises';
import { addNode, newProject } from '../src/core/model.ts';
import type { MeridianNode } from '../src/core/types.ts';

let script: string, css: string;
test.beforeAll(async () => {
  css = (await Promise.all(['scene.css', 'grid.css', 'gallery-scene.css', 'window-scene.css', 'canvas-launcher.css']
    .map(name => readFile(`src/${name}`, 'utf8')))).join('\n');
  script = (await build({ stdin: { contents: `import {renderScene} from './src/scene.ts';window.rotationHarness={renderScene};`,
    resolveDir: process.cwd(), loader: 'ts' }, bundle: true, write: false, format: 'iife', platform: 'browser', target: 'es2022' })).outputFiles[0]!.text;
});

async function mount(page: Page, overrides: Partial<MeridianNode> = {}) {
  const project = newProject('Native text rotation'), node = addNode(project, project.entryCanvasId, 'media', 'A complete rotated title');
  Object.assign(node, { x: 60, y: 40, width: 260, height: 130, sizeMode: 'fixed', contentType: 'text',
    text: 'Rotated text stays readable, selectable and inside its bounding silhouette at every canvas scale.',
    style: { titleSize: 24, fontSize: 16, fontFamily: 'calibri', titleRotation: 45, textRotation: -90 }, ...overrides });
  await page.setContent('<style>html,body{margin:0}#scene{width:1400px;height:850px}</style><div id="scene"></div>');
  await page.addStyleTag({ content: css }); await page.addScriptTag({ content: script });
  await page.evaluate(({ project, node }) => {
    const state: any = { project, node };
    state.draw = (width = node.width, height = node.height, scale = 1) => {
      Object.assign(node, { width, height });
      (window as any).rotationHarness.renderScene(document.querySelector('#scene'), project, project.entryCanvasId,
        { readonly: true, fit: false, view: { x: 0, y: 0, scale }, selection: node.id });
    };
    (window as any).rotationTest = state; state.draw();
  }, { project, node });
  return node;
}

async function textInside(page: Page, id: string) {
  const results = await page.locator(`.ms-node[data-node-id="${id}"]`).evaluate(card => {
    const outer = card.getBoundingClientRect();
    return [...card.querySelectorAll('[data-text-rotation]')].flatMap(element => {
      const range = document.createRange(); range.selectNodeContents(element);
      return [...range.getClientRects()].map(rect => ({ left: rect.left - outer.left, top: rect.top - outer.top,
        right: rect.right - outer.left, bottom: rect.bottom - outer.top, width: outer.width, height: outer.height }));
    });
  });
  expect(results.length).toBeGreaterThan(0);
  for (const rect of results) {
    expect(rect.left).toBeGreaterThanOrEqual(-.7); expect(rect.top).toBeGreaterThanOrEqual(-.7);
    expect(rect.right).toBeLessThanOrEqual(rect.width + .7); expect(rect.bottom).toBeLessThanOrEqual(rect.height + .7);
  }
}

test('independent title and body rotations reserve real layout and fit regular and compact silhouettes', async ({ page }, info) => {
  const node = await mount(page), card = page.locator(`.ms-node[data-node-id="${node.id}"]`);
  for (const shape of ['rectangle', 'diamond', 'hexagon', 'circle']) {
    for (const [width, height, scale] of [[260, 130, 1], [36, 22, 7], [1, .6, 320]]) {
      await page.evaluate(({ shape, width, height, scale }) => {
        const state = (window as any).rotationTest; state.node.style.shape = shape; state.draw(width, height, scale);
      }, { shape, width, height, scale });
      await textInside(page, node.id);
      await expect(card.locator('.ms-node-title')).toHaveCSS('font-size', '24px');
      await expect(card.locator('.ms-text')).toHaveCSS('font-size', '16px');
      await expect(card.locator('.ms-text')).toHaveText(node.text!);
      expect(await card.locator('.ms-node-title').evaluate(element => (element as HTMLElement).style.transform)).toBe('rotate(45deg)');
      expect(await card.locator('.ms-text').evaluate(element => (element as HTMLElement).style.transform)).toBe('rotate(-90deg)');
    }
  }
  await page.screenshot({ path: info.outputPath('microscopic-native-rotated-text.png') });
});

test('automatic rotated frames use their envelope and zero degrees retains the original DOM', async ({ page }, info) => {
  const node = await mount(page, { sizeMode: 'auto', title: 'Rotation', text: 'A vertical reading strip.', width: 380,
    style: { titleSize: 24, fontSize: 18, titleRotation: 90, textRotation: 90, fontFamily: 'cambria' } });
  const card = page.locator(`.ms-node[data-node-id="${node.id}"]`);
  await textInside(page, node.id); const vertical = (await card.boundingBox())!;
  expect(vertical.height).toBeGreaterThan(vertical.width * 2);
  const fonts = await card.evaluate(card => [...card.querySelectorAll<HTMLElement>('.ms-node-title,.ms-text')].map(text => {
    let scale = 1;
    for (let element: Element | null = text; element && element !== card.parentElement; element = element.parentElement) {
      const matrix = new DOMMatrixReadOnly(getComputedStyle(element).transform); scale *= Math.hypot(matrix.a, matrix.b);
    }
    return parseFloat(getComputedStyle(text).fontSize) * scale;
  }));
  // This automatic frame was already measured from its rotated font envelope.
  // It must retain real readable glyph sizes, rather than applying the narrow
  // frame's width ratio a second time and leaving tiny text in an empty strip.
  expect(fonts[0]).toBeGreaterThanOrEqual(24 * .9); expect(fonts[0]).toBeLessThanOrEqual(24.001);
  expect(fonts[1]).toBeGreaterThanOrEqual(18 * .9); expect(fonts[1]).toBeLessThanOrEqual(18.001);
  await page.screenshot({ path: info.outputPath('automatic-vertical-text-strip.png') });
  await page.evaluate(() => {
    const state = (window as any).rotationTest; state.node.style.titleRotation = 0; state.node.style.textRotation = 0; state.draw();
  });
  await expect(card.locator('.ms-rotated-text-frame')).toHaveCount(0);
  const upright = (await card.boundingBox())!; expect(upright.width).toBeGreaterThan(vertical.width);
  expect(upright.height).toBeLessThan(vertical.height);
});

test('fixed rotated reading frames choose a legible composition instead of shrinking long columns into tiny text', async ({ page }, info) => {
  const node = await mount(page, { title: 'Words with room to turn', width: 380, height: 280,
    text: 'Rotation keeps these words readable inside the tile.\nEvery line belongs to one scalable text block.',
    style: { fontSize: 17, titleSize: 22, fontFamily: 'georgia', titleRotation: 90, textRotation: -90 } });
  const card = page.locator(`.ms-node[data-node-id="${node.id}"]`);
  await textInside(page, node.id);
  expect(17 * Number(await card.getAttribute('data-content-fit'))).toBeGreaterThanOrEqual(10);
  expect(22 * Number(await card.getAttribute('data-content-fit'))).toBeGreaterThanOrEqual(13);
  expect(await card.locator('.ms-node-title').evaluate(title => {
    const word = document.createRange(); word.setStart(title.firstChild!, 0); word.setEnd(title.firstChild!, 5);
    return word.getClientRects().length;
  })).toBe(1);
  await page.screenshot({ path: info.outputPath('legible-fixed-quarter-turns.png') });
  await page.evaluate(() => {
    const state = (window as any).rotationTest;
    Object.assign(state.node, { title: 'Text inside a circle', text: 'Readable text follows the available interior, even when its block turns.' });
    Object.assign(state.node.style, { shape: 'circle', fontFamily: 'serif', titleRotation: -30, textRotation: 45 });
    state.draw(250, 250);
  });
  await textInside(page, node.id);
  expect(17 * Number(await card.getAttribute('data-content-fit'))).toBeGreaterThanOrEqual(9.5);
  await page.screenshot({ path: info.outputPath('legible-diagonal-circle-text.png') });
});

test('manual typography retains exact font sizes while rotation remains native and selectable', async ({ page }) => {
  const node = await mount(page, { width: 620, height: 950,
    style: { contentSizing: 'fixed', titleRotation: -90, textRotation: 45, fontFamily: 'garamond', titleSize: 20, fontSize: 16 } });
  const card = page.locator(`.ms-node[data-node-id="${node.id}"]`);
  await expect(card).not.toHaveAttribute('data-content-fit');
  await expect(card.locator('.ms-node-title')).toHaveCSS('font-size', '20px');
  await expect(card.locator('.ms-text')).toHaveCSS('font-size', '16px');
  await expect(card.locator('.ms-text')).toHaveCSS('user-select', 'text');
  await textInside(page, node.id);
});

test('Gallery rotations follow each item and leave image geometry, navigation and media content upright', async ({ page }, info) => {
  const node = await mount(page, { kind: 'container', containerVariant: 'gallery', collapsed: false, text: undefined,
    contentType: undefined, width: 240, height: 160,
    style: { titleRotation: 45, textRotation: 90, titleSize: 24, fontSize: 16, fontFamily: 'segoe-script' } });
  await page.evaluate(() => {
    const state = (window as any).rotationTest, canvas = document.createElement('canvas'); canvas.width = 120; canvas.height = 240;
    canvas.getContext('2d')!.fillRect(0, 0, 120, 240);
    state.project.assets.push({ id: 'portrait', name: 'Portrait.png', mime: 'image/png', base64: canvas.toDataURL().split(',')[1] });
    state.project.nodes.push({ ...state.node, id: 'gallery-image', parentId: state.node.id, kind: 'media', collapsed: true,
      containerVariant: undefined, assetId: 'portrait', contentType: 'asset', title: 'Upright portrait', style: { titleRotation: -90 } });
    state.project.nodes.push({ ...state.node, id: 'gallery-text', parentId: state.node.id, kind: 'media', collapsed: true,
      containerVariant: undefined, contentType: 'text', title: 'A text entry', text: 'The entire text entry is rotated independently.',
      style: { textRotation: -45, titleRotation: 0, fontFamily: 'georgia' } });
    state.draw();
  });
  const card = page.locator(`.ms-node[data-node-id="${node.id}"]`), image = card.locator('.ms-gallery-image');
  await image.evaluate(async element => { const image = element as HTMLImageElement; if (!image.complete) await image.decode(); });
  expect(await image.evaluate(element => (element as HTMLElement).style.transform)).toBe('');
  const media = (await image.boundingBox())!, decoded = await image.evaluate(element => ({ width: (element as HTMLImageElement).naturalWidth,
    height: (element as HTMLImageElement).naturalHeight, fit: getComputedStyle(element).objectFit }));
  // A stable Gallery page reserves a common viewport. Its raster pixels keep
  // their aspect inside that viewport; only the text blocks are rotated.
  expect(decoded.fit).toBe('contain'); expect(decoded.height / decoded.width).toBeCloseTo(2, 2);
  expect(media.width).toBeGreaterThan(0); expect(media.height).toBeGreaterThan(0);
  const rasterScale = Math.min(media.width / decoded.width, media.height / decoded.height);
  expect(decoded.width * rasterScale).toBeLessThanOrEqual(media.width + .01);
  expect(decoded.height * rasterScale).toBeLessThanOrEqual(media.height + .01);
  await textInside(page, node.id);
  await card.getByRole('button', { name: `Next object in ${node.title}`, exact: true }).click();
  await textInside(page, node.id);
  expect(await card.locator('.ms-gallery-text').evaluate(element => (element as HTMLElement).style.transform)).toBe('rotate(-45deg)');
  await expect(card.locator('.ms-gallery-item-title')).not.toHaveAttribute('data-text-rotation');
  await page.screenshot({ path: info.outputPath('rotated-gallery-entry.png') });
});
