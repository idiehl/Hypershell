import { test, expect, type Page } from '@playwright/test';
import { build } from 'esbuild';
import { readFile } from 'node:fs/promises';
import { addNode, newProject } from '../src/core/model.ts';
import type { MeridianNode } from '../src/core/types.ts';

let script: string, css: string;
test.beforeAll(async () => {
  css = (await Promise.all(['scene.css', 'grid.css', 'gallery-scene.css', 'window-scene.css', 'canvas-launcher.css']
    .map(name => readFile(`src/${name}`, 'utf8')))).join('\n');
  script = (await build({ stdin: { contents: `import {renderScene} from './src/scene.ts';window.scalingHarness={renderScene};`,
    resolveDir: process.cwd(), loader: 'ts' }, bundle: true, write: false, format: 'iife', platform: 'browser', target: 'es2022' })).outputFiles[0]!.text;
});

async function mount(page: Page, overrides: Partial<MeridianNode> = {}) {
  const project = newProject('Vector content scaling'), node = addNode(project, project.entryCanvasId, 'media', 'A complete title at every scale');
  Object.assign(node, { x: 50, y: 50, width: 240, height: 110, sizeMode: 'fixed', contentType: 'text',
    text: 'A paragraph stays fully visible instead of being clipped when its bounding tile gets smaller or larger.',
    style: { titleSize: 24, fontSize: 16 }, ...overrides });
  await page.setContent('<style>html,body{margin:0}#scene{width:1400px;height:850px}</style><div id="scene"></div>');
  await page.addStyleTag({ content: css }); await page.addScriptTag({ content: script });
  await page.evaluate(({ project, node }) => {
    const state: any = { project, node, clicks: 0, selected: null };
    state.draw = (width = node.width, height = node.height, scale = 1) => {
      Object.assign(node, { width, height });
      (window as any).scalingHarness.renderScene(document.querySelector('#scene'), project, project.entryCanvasId,
        { readonly: true, fit: false, view: { x: 0, y: 0, scale }, selection: node.id,
          onSelection: (id: string | null) => state.selected = id, onCanvasOpen: () => state.clicks++ });
    };
    (window as any).scalingTest = state; state.draw();
  }, { project, node });
  return { project, node };
}

async function textInside(page: Page, id: string) {
  const results = await page.locator(`.ms-node[data-node-id="${id}"]`).evaluate(card => {
    const outer = card.getBoundingClientRect();
    return [...card.querySelectorAll('.ms-node-title,.ms-text,.ms-gallery-item-title,.ms-gallery-text')].flatMap(element => {
      const range = document.createRange(); range.selectNodeContents(element);
      return [...range.getClientRects()].map(rect => ({ left: rect.left - outer.left, top: rect.top - outer.top,
        right: rect.right - outer.left, bottom: rect.bottom - outer.top, width: outer.width, height: outer.height }));
    });
  });
  expect(results.length).toBeGreaterThan(0);
  for (const rect of results) {
    expect(rect.left).toBeGreaterThanOrEqual(-.6); expect(rect.top).toBeGreaterThanOrEqual(-.6);
    expect(rect.right).toBeLessThanOrEqual(rect.width + .6); expect(rect.bottom).toBeLessThanOrEqual(rect.height + .6);
  }
}

test('text grows and shrinks with fixed frames while preserving all text and authored font proportions', async ({ page }, info) => {
  const { node } = await mount(page), card = page.locator(`.ms-node[data-node-id="${node.id}"]`);
  const scales: number[] = [];
  for (const [width, height] of [[90, 42], [280, 125], [880, 390], [90, 42]]) {
    await page.evaluate(([width, height]) => (window as any).scalingTest.draw(width, height), [width, height]);
    await textInside(page, node.id); scales.push(Number(await card.getAttribute('data-content-fit')));
    expect(await card.locator('.ms-node-title').evaluate(element => getComputedStyle(element).fontSize)).toBe('24px');
    expect(await card.locator('.ms-text').evaluate(element => getComputedStyle(element).fontSize)).toBe('16px');
    await expect(card.locator('.ms-text')).toHaveText(node.text!);
  }
  expect(scales[0]).toBeLessThan(scales[1]!); expect(scales[1]).toBeLessThan(scales[2]!);
  expect(scales[2]).toBeGreaterThan(1); expect(scales[3]).toBe(scales[0]);
  await page.screenshot({ path: info.outputPath('scaled-reading-tile.png') });
});

test('small regular silhouettes fit complete content inside their safe area', async ({ page }) => {
  const { node } = await mount(page);
  for (const shape of ['ellipse', 'diamond', 'hexagon']) {
    await page.evaluate(shape => {
      const state = (window as any).scalingTest; state.node.style.shape = shape; state.draw(140, 64);
    }, shape);
    await textInside(page, node.id);
    const card = page.locator(`.ms-node[data-node-id="${node.id}"]`);
    expect(Number(await card.getAttribute('data-content-fit'))).toBeGreaterThan(0);
    expect(Number(await card.getAttribute('data-content-fit'))).toBeLessThan(1);
    await expect(card.locator('.ms-node-outline path')).toHaveCount(1);
  }
});

test('fixed typography is an explicit opt out and toggling back restores automatic fitting', async ({ page }) => {
  const { node } = await mount(page), card = page.locator(`.ms-node[data-node-id="${node.id}"]`);
  await page.evaluate(() => { const state = (window as any).scalingTest; state.node.style.contentSizing = 'fixed'; state.draw(90, 42); });
  await expect(card).not.toHaveAttribute('data-content-fit');
  expect(await card.locator('.ms-node-title').evaluate(element => getComputedStyle(element).fontSize)).toBe('24px');
  await page.evaluate(() => { const state = (window as any).scalingTest; delete state.node.style.contentSizing; state.draw(90, 42); });
  await textInside(page, node.id); expect(Number(await card.getAttribute('data-content-fit'))).toBeLessThan(1);
});

test('an expanded Unbounded Media Window fits its complete cover independently of its exterior surface', async ({ page }, info) => {
  const { node } = await mount(page, { title: 'Media window cover', width: 220, height: 100, collapsed: false,
    containerVariant: 'window', childLayout: 'spatial',
    grid: { columns: 4, rows: 4, cellWidth: 70, cellHeight: 45, gap: 0, showHeaders: false,
      fillOrder: 'row', windowColumns: 3, windowRows: 2, scrollDirection: 'vertical' } });
  const card = page.locator(`.ms-node[data-node-id="${node.id}"]`), cover = card.locator('.ms-node-body');
  await textInside(page, node.id);
  expect(await cover.evaluate(element => (element as HTMLElement).style.transform)).toContain('scale(');
  const title = (await card.locator('.ms-node-title').boundingBox())!, text = (await card.locator('.ms-text').boundingBox())!;
  expect(text.y).toBeGreaterThanOrEqual(title.y + title.height - .6);
  const viewport = page.locator(`[data-external-window="${node.id}"]`);
  await expect(viewport).toBeVisible();
  const frame = (await card.boundingBox())!, exterior = (await viewport.boundingBox())!;
  expect(exterior.y).toBeGreaterThanOrEqual(frame.y + frame.height);
  expect(exterior.width).toBeGreaterThan(0); expect(exterior.height).toBeGreaterThan(0);
  expect(await viewport.evaluate(element => (element as HTMLElement).style.transform)).toBe('');
  await page.screenshot({ path: info.outputPath('unbounded-window-cover-fit.png') });
});

test('gallery media, full captions and navigation form one fitted composition with usable SVG controls', async ({ page }, info) => {
  const { node } = await mount(page, { kind: 'container', containerVariant: 'gallery', collapsed: false, text: undefined, contentType: undefined });
  await page.evaluate(() => {
    const state = (window as any).scalingTest, canvas = document.createElement('canvas'); canvas.width = 320; canvas.height = 480;
    canvas.getContext('2d')!.fillRect(0, 0, 320, 480);
    state.project.assets.push({ id: 'portrait', name: 'Portrait.png', mime: 'image/png', base64: canvas.toDataURL().split(',')[1] });
    state.project.nodes.push({ ...state.node, id: 'gallery-picture', parentId: state.node.id, kind: 'media', collapsed: true,
      containerVariant: undefined, assetId: 'portrait', contentType: 'asset', title: 'An entire portrait', text: undefined });
    state.project.nodes.push({ ...state.node, id: 'gallery-canvas', parentId: state.node.id, kind: 'canvas-link', collapsed: true,
      containerVariant: undefined, targetId: state.project.canvases[0].rootId, title: 'A related canvas', text: undefined });
    state.draw(150, 100);
  });
  const card = page.locator(`.ms-node[data-node-id="${node.id}"]`), image = card.locator('.ms-gallery-image');
  await image.evaluate(async element => { const image = element as HTMLImageElement; if (!image.complete) await image.decode(); });
  await textInside(page, node.id);
  const outer = (await card.boundingBox())!, media = (await image.boundingBox())!, controls = (await card.locator('.ms-gallery-navigation').boundingBox())!;
  // The image element fills the Gallery's common viewport; its raster pixels
  // retain their authored aspect through contain, rather than changing the
  // page frame to match whichever image is currently active.
  const decoded = await image.evaluate(element => ({ width: (element as HTMLImageElement).naturalWidth, height: (element as HTMLImageElement).naturalHeight, fit: getComputedStyle(element).objectFit }));
  expect(decoded.fit).toBe('contain'); expect(decoded.height / decoded.width).toBeCloseTo(1.5, 2);
  expect(media.width).toBeGreaterThan(0); expect(media.height).toBeGreaterThan(0);
  const rasterScale = Math.min(media.width / decoded.width, media.height / decoded.height);
  const painted = { width: decoded.width * rasterScale, height: decoded.height * rasterScale };
  expect(painted.height / painted.width).toBeCloseTo(1.5, 2);
  expect(painted.width).toBeLessThanOrEqual(media.width + .01); expect(painted.height).toBeLessThanOrEqual(media.height + .01);
  expect(media.y + media.height).toBeLessThanOrEqual(outer.y + outer.height + .6);
  expect(controls.y + controls.height).toBeLessThanOrEqual(outer.y + outer.height + .6);
  await card.getByRole('button', { name: `Next object in ${node.title}`, exact: true }).click();
  await expect(card.locator('.ms-canvas-open svg')).toHaveCount(1); await card.locator('.ms-canvas-open').click();
  expect(await page.evaluate(() => (window as any).scalingTest.clicks)).toBe(1);
  await page.screenshot({ path: info.outputPath('scaled-gallery.png') });
});

test('micro tiles preserve their physical frame and vector reading content when the camera magnifies them', async ({ page }, info) => {
  const { node } = await mount(page), card = page.locator(`.ms-node[data-node-id="${node.id}"]`);
  for (const [width, height, camera] of [[4, 3, 100], [1, 1, 300]]) {
    await page.evaluate(([width, height, camera]) => { const state = (window as any).scalingTest;
      state.node.x = 1; state.node.y = 1; state.draw(width, height, camera); }, [width, height, camera]);
    await textInside(page, node.id);
    const scale = Number(await card.getAttribute('data-content-fit'));
    expect(scale).toBeGreaterThan(0); expect(scale).toBeLessThan(.1);
    const frame = (await card.boundingBox())!;
    expect(frame.width).toBeCloseTo(width! * camera!, 1); expect(frame.height).toBeCloseTo(height! * camera!, 1);
    await expect(card.locator('.ms-text')).toBeVisible(); await expect(card.locator('canvas')).toHaveCount(0);
    expect(await page.evaluate(point => document.elementFromPoint(point.x, point.y)?.closest<HTMLElement>('.ms-node')?.dataset.nodeId,
      { x: frame.x + frame.width / 2, y: frame.y + frame.height / 2 })).toBe(node.id);
    const stroke = await card.locator('.ms-node-outline path').evaluate(element => Number.parseFloat(getComputedStyle(element).strokeWidth));
    expect(stroke * camera!).toBeCloseTo(2, 3);
  }
  await page.screenshot({ path: info.outputPath('magnified-micro-tile.png') });
});

test('a selected micro tile at distant world coordinates retains a thin outline and clickable content', async ({ page }, info) => {
  const { node } = await mount(page, { x: 200000, y: 100000, width: 3, height: 4, title: 'Detail', text: 'Layers',
    style: { padding: 0, contentGap: 0, borderWidth: 0, fontSize: 13, titleSize: 16, shape: 'rectangle' } });
  await page.evaluate(() => {
    const state = (window as any).scalingTest, node = state.node;
    (window as any).scalingHarness.renderScene(document.querySelector('#scene'), state.project, state.project.entryCanvasId,
      { readonly: true, fit: false, selection: node.id, onSelection: (id: string | null) => state.selected = id,
        view: { x: 437.5 - node.x * 175, y: 75 - node.y * 175, scale: 175 } });
  });
  const card = page.locator(`.ms-node[data-node-id="${node.id}"]`), frame = (await card.boundingBox())!;
  const point = { x: frame.x + frame.width / 2, y: frame.y + frame.height / 2 };
  const stroke = await card.locator('.ms-node-outline path').evaluate(element => Number.parseFloat(getComputedStyle(element).strokeWidth));
  expect(stroke * 175).toBeCloseTo(2, 3);
  for (const selector of ['.ms-node-title', '.ms-text']) {
    const content = (await card.locator(selector).boundingBox())!;
    expect(content.x).toBeGreaterThanOrEqual(frame.x - 2); expect(content.y).toBeGreaterThanOrEqual(frame.y - 2);
    expect(content.x + content.width).toBeLessThanOrEqual(frame.x + frame.width + 2);
    expect(content.y + content.height).toBeLessThanOrEqual(frame.y + frame.height + 2);
  }
  await page.screenshot({ path: info.outputPath('distant-selected-micro-tile.png') });
  expect(await page.evaluate(point => document.elementFromPoint(point.x, point.y)?.closest<HTMLElement>('.ms-node')?.dataset.nodeId, point)).toBe(node.id);
  await page.mouse.click(point.x, point.y); expect(await page.evaluate(() => (window as any).scalingTest.selected)).toBe(node.id);
});

test('render origin preserves distant SVG routes, panning and objects appended after the first camera update', async ({ page }) => {
  const { node } = await mount(page, { x: 200000, y: 100000, width: 20, height: 10, title: 'First', text: 'Layer',
    style: { padding: 0, contentGap: 0, borderWidth: 0, shape: 'rectangle' } });
  await page.evaluate(() => {
    const state = (window as any).scalingTest, node = state.node;
    state.project.nodes.push({ ...node, id: 'distant-second', x: node.x + 45, title: 'Second' });
    state.project.links.push({ id: 'distant-route', canvasId: state.project.entryCanvasId, sourceId: node.id,
      targetId: 'distant-second', kind: 'visual', relation: 'generic', direction: 'none', style: { width: .1, lineStyle: 'solid' } });
    state.links = [];
    (window as any).scalingHarness.renderScene(document.querySelector('#scene'), state.project, state.project.entryCanvasId,
      { readonly: true, fit: false, onLinkSelect: (id: string, point: any) => state.links.push({ id, point }),
        onViewChange: (view: any) => state.view = view, view: { x: 300 - node.x * 10, y: 200 - node.y * 10, scale: 10 } });
    const mark = document.createElement('div'); mark.className = 'test-creation-mark';
    Object.assign(mark.style, { position: 'absolute', left: `${node.x + 80}px`, top: `${node.y + 35}px`,
      width: '5px', height: '5px', background: 'white' });
    document.querySelector('.ms-world')!.append(mark);
  });
  expect(await page.evaluate(() => document.elementFromPoint(625, 250)?.getAttribute('data-link-id'))).toBe('distant-route');
  await page.mouse.click(625, 250);
  const route = await page.evaluate(() => (window as any).scalingTest.links.at(-1));
  expect(route.id).toBe('distant-route'); expect(route.point.x).toBeCloseTo(200032.5); expect(route.point.y).toBeCloseTo(100005);
  const card = page.locator(`.ms-node[data-node-id="${node.id}"]`), mark = page.locator('.test-creation-mark');
  const before = (await card.boundingBox())!, markBefore = (await mark.boundingBox())!;
  expect(markBefore.x).toBeCloseTo(1100); expect(markBefore.y).toBeCloseTo(550);
  await page.mouse.move(50, 700); await page.mouse.down(); await page.mouse.move(100, 730, { steps: 3 }); await page.mouse.up();
  const after = (await card.boundingBox())!, markAfter = (await mark.boundingBox())!;
  expect(after.x - before.x).toBeCloseTo(50); expect(after.y - before.y).toBeCloseTo(30);
  expect(markAfter.x - markBefore.x).toBeCloseTo(50); expect(markAfter.y - markBefore.y).toBeCloseTo(30);
  expect(await page.evaluate(() => ({ x: (window as any).scalingTest.node.x, y: (window as any).scalingTest.node.y }))).toEqual({ x: 200000, y: 100000 });
});
