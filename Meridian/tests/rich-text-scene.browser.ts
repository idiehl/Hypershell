import { test, expect, type Page, type Locator } from '@playwright/test';
import { build } from 'esbuild';
import { readFile, writeFile } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';
import { addCanvas, addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import { buildPublication } from '../src/publication.ts';
import type { MeridianNode } from '../src/core/types.ts';

let script: string, css: string;
test.beforeAll(async () => {
  css = (await Promise.all(['scene.css', 'grid.css', 'gallery-scene.css', 'window-scene.css', 'canvas-launcher.css']
    .map(name => readFile(`src/${name}`, 'utf8')))).join('\n');
  script = (await build({ stdin: { contents: `import {renderScene} from './src/scene.ts';window.richHarness={renderScene};`,
    resolveDir: process.cwd(), loader: 'ts' }, bundle: true, write: false, format: 'iife', platform: 'browser', target: 'es2022' })).outputFiles[0]!.text;
});
function fixture() {
  const project = newProject('Native rich text'), node = addNode(project, project.entryCanvasId, 'media', 'A formatted title');
  Object.assign(node, { x: 40, y: 30, width: 390, height: 300, sizeMode: 'fixed', contentType: 'text',
    text: 'Words of many sizes can share a paragraph.\nA new line has italic words and underlined passages.\n<em>Literal markup</em> remains harmless.',
    style: { fontFamily: 'calibri', titleSize: 24, fontSize: 17, shape: 'rectangle' },
    titleRuns: [{ start: 2, end: 11, style: { fontFamily: 'cambria', fontSize: 34, bold: false, color: '#FFDD88' } }],
    textRuns: [{ start: 0, end: 5, style: { fontFamily: 'georgia', fontSize: 40, bold: true, color: '#78E8D0' } },
      { start: 56, end: 62, style: { italic: true, underline: true, color: '#E7AAFF' } },
      { start: 73, end: 94, style: { fontFamily: 'consolas', fontSize: 13, underline: true } }] });
  return { project, node };
}
async function mount(page: Page, source = fixture()) {
  await page.setContent('<style>html,body{margin:0}#scene{width:1400px;height:850px}</style><div id="scene"></div>');
  await page.addStyleTag({ content: css }); await page.addScriptTag({ content: script });
  await page.evaluate(({ project, node }) => {
    const state: any = { project, node };
    state.draw = (width = node.width, height = node.height, scale = 1) => {
      Object.assign(node, { width, height });
      (window as any).richHarness.renderScene(document.querySelector('#scene'), project, project.entryCanvasId,
        { readonly: true, fit: false, view: { x: 0, y: 0, scale }, selection: node.id });
    };
    (window as any).richTest = state; state.draw();
  }, source); return source;
}
const tile = (page: Page, id: string) => page.locator(`.ms-node[data-node-id="${id}"]`);
async function inside(card: Locator) {
  const result = await card.evaluate(card => {
    const bounds = card.getBoundingClientRect();
    return { bounds: { x: bounds.x, y: bounds.y, right: bounds.right, bottom: bounds.bottom },
      lines: [...card.querySelectorAll('.ms-node-title,.ms-text,.ms-text-preview,.ms-gallery-item-title,.ms-gallery-text')]
        .filter(item => item.textContent?.trim()).flatMap(item => {
          const range = document.createRange(); range.selectNodeContents(item);
          return [...range.getClientRects()].filter(rect => rect.width > 0 && rect.height > 0)
            .map(rect => ({ x: rect.x, y: rect.y, right: rect.right, bottom: rect.bottom }));
        }) };
  });
  expect(result.lines.length).toBeGreaterThan(0);
  for (const line of result.lines) {
    expect(line.x).toBeGreaterThanOrEqual(result.bounds.x - 1); expect(line.y).toBeGreaterThanOrEqual(result.bounds.y - 1);
    expect(line.right).toBeLessThanOrEqual(result.bounds.right + 1); expect(line.bottom).toBeLessThanOrEqual(result.bounds.bottom + 1);
  }
}
test('native passages retain independent formatting, exact literal text and selectable glyphs', async ({ page }, info) => {
  const source = await mount(page), card = tile(page, source.node.id), body = card.locator('.ms-text');
  await expect(body).toHaveText(source.node.text!); await expect(body.locator('em')).toHaveCount(0);
  await expect(body.locator('.ms-text-run').first()).toHaveCSS('font-size', '40px');
  await expect(body.locator('.ms-text-run').first()).toHaveCSS('font-weight', '700');
  await expect(body.locator('.ms-text-run').first()).toHaveCSS('color', 'rgb(120, 232, 208)');
  expect(await body.locator('.ms-text-run').first().evaluate(span => getComputedStyle(span).fontFamily)).toContain('Georgia');
  const italic = body.locator('.ms-text-run').filter({ hasText: source.node.text!.slice(56, 62) });
  await expect(italic).toHaveCSS('font-style', 'italic'); expect(await italic.evaluate(span => getComputedStyle(span).textDecorationLine)).toBe('underline');
  const title = card.locator('.ms-node-title .ms-text-run').filter({ hasText: 'formatted' }); await expect(title).toHaveCSS('font-weight', '400');
  await inside(card); await page.screenshot({ path: info.outputPath('native-formatted-passages.png') });
});
test('mixed-size paragraphs fit automatic, shaped, rotated and microscopic frames without flattening formats', async ({ page }, info) => {
  const source = await mount(page), card = tile(page, source.node.id);
  for (const sizeMode of ['auto', 'fixed']) for (const shape of ['rectangle', 'diamond', 'hexagon', 'circle']) {
    for (const [width, height, scale, rotation] of [[390, 300, 1, 0], [260, 170, 1, 45], [1, .7, 380, 90]]) {
      await page.evaluate(({ sizeMode, shape, width, height, scale, rotation }) => {
        const state = (window as any).richTest; state.node.sizeMode = sizeMode;
        Object.assign(state.node.style, { shape, textRotation: rotation, titleRotation: -rotation }); state.draw(width, height, scale);
      }, { sizeMode, shape, width, height, scale, rotation });
      await inside(card); await expect(card.locator('.ms-text .ms-text-run').first()).toHaveCSS('font-size', '40px');
      await expect(card.locator('.ms-text')).toHaveText(source.node.text!);
    }
  }
  await page.screenshot({ path: info.outputPath('microscopic-formatted-quarter-turns.png') });
});
test('Gallery and local/pinned references display the source passages while document previews reject unrelated ranges', async ({ page }, info) => {
  const source = fixture(), canvas = addCanvas(source.project, 'Remote source');
  const local = addNode(source.project, source.project.entryCanvasId, 'reference', 'Local mirror');
  Object.assign(local, { targetId: source.node.id, x: 500, y: 40, width: 330, height: 270, sizeMode: 'fixed' });
  const gallery = addNode(source.project, source.project.entryCanvasId, 'container', 'Reading collection');
  Object.assign(gallery, { containerVariant: 'gallery', childLayout: 'contained', collapsed: false, x: 860, y: 40, width: 390, height: 370, sizeMode: 'fixed' });
  const object = { ...structuredClone(source.node), id: 'saved-object', canvasId: canvas.id, parentId: null, x: 865, y: 90 };
  source.project.nodes.push(object);
  const pinned = addNode(source.project, source.project.entryCanvasId, 'reference', 'Pinned mirror');
  Object.assign(pinned, { targetId: object.id, x: 500, y: 350, width: 330, height: 270, sizeMode: 'fixed' });
  source.project = commitSnapshots(source.project);
  const galleryCopy = { ...structuredClone(object), id: 'gallery-current', parentId: gallery.id, canvasId: source.project.entryCanvasId }; source.project.nodes.push(galleryCopy);
  const document = addNode(source.project, source.project.entryCanvasId, 'media', 'Literal document');
  Object.assign(document, { x: 40, y: 440, width: 390, height: 220, contentType: 'asset', assetId: 'document', text: source.node.text, textRuns: source.node.textRuns });
  source.project.assets.push({ id: 'document', name: 'plain.txt', mime: 'text/plain', base64: Buffer.from('A different document has no inherited formatting.').toString('base64') });
  await mount(page, source);
  for (const node of [local, pinned]) {
    const card = tile(page, node.id); await expect(card.locator('.ms-text')).toHaveText(source.node.text!);
    await expect(card.locator('.ms-text .ms-text-run').first()).toHaveCSS('font-size', '40px'); await inside(card);
  }
  const galleryCard = tile(page, gallery.id); await expect(galleryCard.locator('.ms-gallery-text .ms-text-run').first()).toHaveCSS('font-size', '40px'); await inside(galleryCard);
  expect(40 * Number(await galleryCard.getAttribute('data-content-fit'))).toBeGreaterThanOrEqual(30);
  await expect(tile(page, document.id).locator('.ms-text-preview .ms-text-run')).toHaveCount(0);
  await page.screenshot({ path: info.outputPath('formatted-gallery-and-references.png') });
});
test('offline publication preserves native passage formatting and plain legacy blocks retain their original DOM', async ({ page, browser }, info) => {
  const source = fixture(), plain = addNode(source.project, source.project.entryCanvasId, 'media', 'Legacy title');
  Object.assign(plain, { x: 490, y: 50, width: 320, height: 180, contentType: 'text', text: 'A legacy text block retains plain DOM.', sizeMode: 'fixed' });
  await mount(page, source); await expect(tile(page, plain.id).locator('.ms-text .ms-text-run')).toHaveCount(0);
  expect(await tile(page, plain.id).locator('.ms-text').evaluate(element => element.childNodes.length)).toBe(1);
  const viewerCode = (await build({ entryPoints: ['src/viewer.ts'], bundle: true, write: false, format: 'iife', target: 'es2022' })).outputFiles[0]!.text;
  const filename = info.outputPath('rich-text-offline.html'); await writeFile(filename, buildPublication(commitSnapshots(source.project), viewerCode, css));
  const context = await browser.newContext({ offline: true, viewport: { width: 1400, height: 850 } });
  try {
    const viewer = await context.newPage(); await viewer.goto(pathToFileURL(filename).href);
    await expect(tile(viewer, source.node.id).locator('.ms-text .ms-text-run').first()).toHaveCSS('font-size', '40px');
    await expect(tile(viewer, source.node.id).locator('.ms-text')).toHaveText(source.node.text!); await inside(tile(viewer, source.node.id));
    await viewer.screenshot({ path: info.outputPath('offline-formatted-text.png') });
  } finally { await context.close(); }
});
