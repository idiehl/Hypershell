import { test, expect, type Page } from '@playwright/test';
import { build } from 'esbuild';
import { readFile, writeFile } from 'node:fs/promises';
import { addNode, newProject } from '../src/core/model.ts';

let script: string, css: string;
test.beforeAll(async () => {
  css = (await Promise.all(['scene.css', 'grid.css', 'gallery-scene.css', 'window-scene.css', 'canvas-launcher.css']
    .map(name => readFile(`src/${name}`, 'utf8')))).join('\n');
  script = (await build({ stdin: { contents: `import {renderScene} from './src/scene.ts';import {gridCellBounds} from './src/core/cells.ts';import {fitContainedContent} from './src/contained-content.ts';window.resolutionHarness={renderScene,gridCellBounds,fitContainedContent};`,
    resolveDir: process.cwd(), loader: 'ts' }, bundle: true, write: false, format: 'iife', platform: 'browser', target: 'es2022' })).outputFiles[0]!.text;
});

type Fixture = 'ordinary' | 'gallery' | 'window' | 'distant';
async function mount(page: Page) {
  const project = newProject('Relative raster resolution'), node = addNode(project, project.entryCanvasId, 'media', 'Detailed image');
  await page.setContent('<style>html,body{margin:0}#scene{width:1100px;height:1000px}#reference{position:fixed;left:40px;top:80px;width:512px;height:512px}</style><div id="scene"></div>');
  await page.addStyleTag({ content: css }); await page.addScriptTag({ content: script });
  await page.evaluate(({ project, node }) => {
    const source = document.createElement('canvas'); source.width = source.height = 512;
    const context = source.getContext('2d')!, pixels = context.createImageData(512, 512);
    for (let y = 0; y < 512; y++) for (let x = 0; x < 512; x++) {
      const at = (y * 512 + x) * 4;
      // One-pixel/checker detail reveals premature raster reduction; coarser
      // stripes and colour ramps distinguish blur from a crop or phase shift.
      const value = x < 256 ? ((x + (y < 256 ? y : 0)) % 2 ? 245 : 10)
        : y < 256 ? (Math.floor(x / 4) % 2 ? 245 : 10) : (Math.floor(y / 8) % 2 ? 245 : 10);
      pixels.data[at] = value; pixels.data[at + 1] = value; pixels.data[at + 2] = x < 256 ? value : Math.floor(y / 2); pixels.data[at + 3] = 255;
    }
    context.putImageData(pixels, 0, 0);
    const base64 = source.toDataURL('image/png').split(',')[1]!;
    project.assets.push({ id: 'full-resolution', name: 'Detailed512.png', mime: 'image/png', base64 });
    const state: any = { project, node, source: `data:image/png;base64,${base64}`, view: { x: 0, y: 0, scale: 1 } };
    state.fixture = (width: number, height: number, fixture: string) => {
      project.nodes.splice(1); project.nodes.push(node);
      Object.assign(node, { x: fixture === 'distant' ? 200000 : 0, y: fixture === 'distant' ? 100000 : 0, width, height, sizeMode: 'fixed', assetId: 'full-resolution',
        contentType: 'asset', kind: 'media', parentId: project.canvases[0].rootId, collapsed: true,
        containerVariant: 'contained', grid: undefined, cell: undefined,
        style: { padding: 0, contentGap: 0, borderWidth: 0, shape: 'rectangle', radius: 0 } });
      state.selector = `[data-node-id="${node.id}"] .ms-image`; state.windowOffsets = undefined;
      if (fixture === 'gallery') {
        Object.assign(node, { kind: 'container', containerVariant: 'gallery', collapsed: false, assetId: undefined, contentType: undefined });
        project.nodes.push({ ...node, id: 'gallery-picture', kind: 'media', parentId: node.id, containerVariant: undefined,
          contentType: 'asset', assetId: 'full-resolution', title: 'Detail', collapsed: true });
        state.selector = `[data-node-id="${node.id}"] .ms-gallery-image`;
      } else if (fixture === 'window') {
        const cover: any = { ...node, id: 'scroll-owner', assetId: undefined, title: 'Window', contentType: 'text', text: 'Scroll',
          width: 60, height: 10, collapsed: false, containerVariant: 'window', childLayout: 'spatial',
          grid: { columns: 1, rows: 2, cellWidth: width, cellHeight: height, gap: 0, showHeaders: false,
            windowColumns: 1, windowRows: 1, scrollDirection: 'vertical' } };
        project.nodes.push(cover); node.parentId = cover.id; node.cell = { column: 1, row: 2 };
        const bounds = (window as any).resolutionHarness.gridCellBounds(cover, node.cell, project);
        Object.assign(node, bounds); state.windowOffsets = { [cover.id]: { x: 0, y: height } };
      }
      state.view = { x: -node.x, y: -node.y, scale: 1 }; state.draw();
    };
    state.draw = () => {
      (window as any).resolutionHarness.renderScene(document.querySelector('#scene'), project, project.entryCanvasId,
        { readonly: true, fit: false, view: state.view, windowOffsets: state.windowOffsets });
    };
    (window as any).resolutionState = state;
  }, { project, node });
}

async function imageCapture(page: Page, width: number, height: number, fixture: Fixture) {
  await page.evaluate(({ width, height, fixture }) => (window as any).resolutionState.fixture(width, height, fixture), { width, height, fixture });
  const selector = await page.evaluate(() => (window as any).resolutionState.selector), image = page.locator(selector);
  await image.evaluate(async element => { await (element as HTMLImageElement).decode(); });
  const initial = (await image.boundingBox())!;
  await page.evaluate(scale => { const state = (window as any).resolutionState;
    state.view = { x: -state.node.x * scale, y: -state.node.y * scale, scale }; state.draw(); }, 512 / initial.width);
  for (let pass = 0; pass < 4; pass++) {
    await image.evaluate(async element => { await (element as HTMLImageElement).decode(); });
    const bounds = (await image.boundingBox())!;
    await page.evaluate(bounds => { const state = (window as any).resolutionState;
      state.view.x += 40 - bounds.x; state.view.y += 80 - bounds.y; state.draw(); }, bounds);
  }
  await image.evaluate(async element => { await (element as HTMLImageElement).decode(); });
  const bounds = (await image.boundingBox())!;
  expect(bounds.width).toBeCloseTo(512, 1); expect(Math.abs(bounds.height - 512)).toBeLessThan(6);
  if (fixture === 'distant') { expect(Math.abs(bounds.x - 40)).toBeLessThan(3); expect(Math.abs(bounds.y - 80)).toBeLessThan(3); }
  else { expect(bounds.x).toBeCloseTo(40, 1); expect(bounds.y).toBeCloseTo(80, 1); }
  await expect(page.locator('.ms-resolution-image')).toBeVisible();
  await expect.poll(async () => {
    const paint = (await page.locator('.ms-resolution-image').boundingBox())!;
    return Math.abs(paint.x - bounds.x) + Math.abs(paint.y - bounds.y) + Math.abs(paint.width - bounds.width) + Math.abs(paint.height - bounds.height);
  }).toBeLessThan(.25);
  expect(await page.locator('.ms-resolution-image').evaluate(element => ({
    original: (element as HTMLImageElement).currentSrc === (window as any).resolutionState.source,
    width: (element as HTMLImageElement).naturalWidth, height: (element as HTMLImageElement).naturalHeight,
  }))).toEqual({ original: true, width: 512, height: 512 });
  const details = await image.evaluate(element => ({ naturalWidth: (element as HTMLImageElement).naturalWidth,
    naturalHeight: (element as HTMLImageElement).naturalHeight, sourceMatchesOriginal: (element as HTMLImageElement).currentSrc === (window as any).resolutionState.source,
    imageRendering: getComputedStyle(element).imageRendering, paintWidth: (element as HTMLElement).offsetWidth,
    paintHeight: (element as HTMLElement).offsetHeight, computedWidth: getComputedStyle(element).width,
    computedHeight: getComputedStyle(element).height, localStyle: (element as HTMLElement).getAttribute('style'),
    frame: element.getBoundingClientRect().toJSON(),
    nodeWidth: Number(element.closest<HTMLElement>('.ms-node')!.style.width.replace('px', '')),
    window: !!element.closest('.ms-window-content'), scale: (window as any).resolutionState.view.scale }));
  expect(details.naturalWidth).toBe(512); expect(details.naturalHeight).toBe(512); expect(details.sourceMatchesOriginal).toBe(true);
  const clip = { x: 40, y: 80, width: 512, height: 512 }, png = await page.screenshot({ clip });
  await page.evaluate(bounds => {
    const reference = new Image(); reference.id = 'reference'; reference.src = (window as any).resolutionState.source;
    Object.assign(reference.style, { left: `${bounds.x}px`, top: `${bounds.y}px`, width: `${bounds.width}px`, height: `${bounds.height}px`, objectFit: 'contain' });
    (document.querySelector('#scene') as HTMLElement).style.visibility = 'hidden';
    document.body.append(reference);
  }, bounds);
  await page.locator('#reference').evaluate(async element => { await (element as HTMLImageElement).decode(); });
  const reference = await page.screenshot({ clip });
  await page.evaluate(() => { document.querySelector('#reference')!.remove(); (document.querySelector('#scene') as HTMLElement).style.removeProperty('visibility'); });
  return { png, reference, details };
}

async function compare(page: Page, a: Buffer, b: Buffer) {
  return page.evaluate(async ({ a, b }) => {
    const read = async (source: string) => {
      const image = new Image(); image.src = `data:image/png;base64,${source}`; await image.decode();
      const canvas = document.createElement('canvas'); canvas.width = image.naturalWidth; canvas.height = image.naturalHeight;
      const context = canvas.getContext('2d')!; context.drawImage(image, 0, 0); return context.getImageData(0, 0, canvas.width, canvas.height);
    };
    const first = await read(a), second = await read(b); let absolute = 0, differences = 0, contrastA = 0, contrastB = 0, pairs = 0, samples = 0;
    for (let at = 0; at < first.data.length; at += 4) {
      const x = (at / 4) % first.width, y = Math.floor(at / 4 / first.width);
      if (x < 16 || y < 16 || x >= first.width - 16 || y >= first.height - 16) continue;
      samples++;
      for (let colour = 0; colour < 3; colour++) absolute += Math.abs(first.data[at + colour]! - second.data[at + colour]!);
      if (Math.abs(first.data[at]! - second.data[at]!) > 12) differences++;
      if (x > 4 && x < first.width / 2 - 4 && y > 4 && y < first.height / 2 - 4) {
        contrastA += Math.abs(first.data[at]! - first.data[at - 4]!); contrastB += Math.abs(second.data[at]! - second.data[at - 4]!); pairs++;
      }
    }
    return { width: first.width, height: first.height, meanAbsoluteError: absolute / (samples * 3),
      fractionDifferent: differences / samples, edgeContrastA: contrastA / pairs, edgeContrastB: contrastB / pairs };
  }, { a: a.toString('base64'), b: b.toString('base64') });
}

async function expectPaintAligned(page: Page, selector: string) {
  await expect(page.locator('.ms-resolution-image')).toBeVisible();
  await expect.poll(async () => {
    const source = (await page.locator(selector).boundingBox())!, paint = (await page.locator('.ms-resolution-image').boundingBox())!;
    return Math.abs(source.x - paint.x) + Math.abs(source.y - paint.y) + Math.abs(source.width - paint.width) + Math.abs(source.height - paint.height);
  }).toBeLessThan(.25);
}

test('camera gestures and source frame resizing keep full-source paint aligned without rebuilding the scene', async ({ page }) => {
  await page.setViewportSize({ width: 1200, height: 1050 }); await mount(page);
  await imageCapture(page, 3, 4, 'ordinary');
  const selector = await page.evaluate(() => (window as any).resolutionState.selector);
  const original = await page.locator(selector).boundingBox();
  await page.evaluate(() => {
    const state = (window as any).resolutionState;
    state.snapshot = JSON.stringify(state.project); state.originalWorld = document.querySelector('.ms-world');
    state.originalSource = document.querySelector(state.selector);
  });
  await page.getByRole('button', { name: 'Zoom out', exact: true }).click(); await expectPaintAligned(page, selector);
  expect((await page.locator(selector).boundingBox())!.width).toBeCloseTo(original!.width * .8, 1);
  await page.getByRole('button', { name: 'Zoom in', exact: true }).click(); await expectPaintAligned(page, selector);
  await page.mouse.move(800, 650); await page.mouse.wheel(0, 120);
  await expect.poll(async () => (await page.locator(selector).boundingBox())!.width).toBeLessThan(original!.width - 10);
  await expectPaintAligned(page, selector);
  const beforePan = (await page.locator(selector).boundingBox())!;
  await page.mouse.move(930, 740); await page.mouse.down(); await page.mouse.move(970, 765, { steps: 5 }); await page.mouse.up();
  await expect.poll(async () => (await page.locator(selector).boundingBox())!.x).toBeCloseTo(beforePan.x + 40, 1);
  await expectPaintAligned(page, selector);
  const beforeResize = (await page.locator(selector).boundingBox())!;
  await page.evaluate(() => {
    const state = (window as any).resolutionState, card = state.originalSource.closest('.ms-node');
    Object.assign(card.style, { width: '3.5px', height: '4.5px' });
    (window as any).resolutionHarness.fitContainedContent(card, state.project, state.node,
      { x: state.node.x, y: state.node.y, width: 3.5, height: 4.5 });
  });
  await expect.poll(async () => (await page.locator(selector).boundingBox())!.width).toBeGreaterThan(beforeResize.width + 10);
  await expectPaintAligned(page, selector);
  expect(await page.evaluate(() => {
    const state = (window as any).resolutionState;
    return { graphUnchanged: JSON.stringify(state.project) === state.snapshot,
      sameWorld: state.originalWorld === document.querySelector('.ms-world'), sameSource: state.originalSource === document.querySelector(state.selector),
      fullSource: [...document.querySelectorAll<HTMLImageElement>('.ms-image,.ms-resolution-image')].every(image => image.currentSrc === state.source && image.naturalWidth === 512) };
  })).toEqual({ graphUnchanged: true, sameWorld: true, sameSource: true, fullSource: true });
});

test('Window scroll clips image paint and Gallery paging or rerender removes stale sources', async ({ page }, info) => {
  await page.setViewportSize({ width: 1200, height: 1050 }); await mount(page);
  await imageCapture(page, 3, 4, 'window');
  const selector = await page.evaluate(() => (window as any).resolutionState.selector), pane = page.locator('.ms-window-viewport');
  const paneBounds = (await pane.boundingBox())!, initialOffset = await pane.evaluate(element => element.scrollTop);
  await page.mouse.move(paneBounds.x + paneBounds.width / 2, paneBounds.y + paneBounds.height / 2);
  await page.mouse.wheel(0, -200);
  await expect.poll(() => pane.evaluate(element => element.scrollTop)).toBeLessThan(initialOffset);
  await expectPaintAligned(page, selector);
  await expect.poll(() => page.locator('.ms-resolution-image').evaluate(element => {
    const numbers = (getComputedStyle(element).clipPath.match(/[\d.]+/g) ?? []).map(Number); return numbers[2] ?? 0;
  })).toBeGreaterThan(20);
  const clipped = await page.screenshot();
  await page.locator('.ms-image-resolution-layer').evaluate(element => (element as HTMLElement).style.visibility = 'hidden');
  const withoutPaint = await page.screenshot();
  await page.locator('.ms-image-resolution-layer').evaluate(element => (element as HTMLElement).style.removeProperty('visibility'));
  const clipping = await page.evaluate(async ({ painted, empty, pane }) => {
    const read = async (source: string) => { const image = new Image(); image.src = `data:image/png;base64,${source}`; await image.decode();
      const canvas = document.createElement('canvas'); canvas.width = image.naturalWidth; canvas.height = image.naturalHeight;
      const context = canvas.getContext('2d')!; context.drawImage(image, 0, 0); return context.getImageData(0, 0, canvas.width, canvas.height); };
    const a = await read(painted), b = await read(empty); let outside = 0, inside = 0;
    for (let at = 0; at < a.data.length; at += 4) {
      const x = (at / 4) % a.width, y = Math.floor(at / 4 / a.width), error = Math.abs(a.data[at]! - b.data[at]!);
      if (x > pane.x + 2 && x < pane.x + pane.width - 2 && y > pane.y + 2 && y < pane.y + pane.height - 2) inside += error;
      else if (x < pane.x - 2 || x > pane.x + pane.width + 2 || y < pane.y - 2 || y > pane.y + pane.height + 2) outside += error;
    }
    return { outside, inside };
  }, { painted: clipped.toString('base64'), empty: withoutPaint.toString('base64'), pane: paneBounds });
  expect(clipping.inside).toBeGreaterThan(1000); expect(clipping.outside).toBe(0);
  await info.attach('scrolled-window-clipping.png', { body: clipped, contentType: 'image/png' });
  await page.evaluate(() => {
    const state = (window as any).resolutionState;
    state.oldWindowSource = document.querySelector(state.selector); state.oldWindowPaint = document.querySelector('.ms-resolution-image');
  });
  await imageCapture(page, 3, 4, 'gallery');
  expect(await page.evaluate(() => { const state = (window as any).resolutionState;
    return { sourceConnected: state.oldWindowSource.isConnected, paintConnected: state.oldWindowPaint.isConnected }; })).toEqual({ sourceConnected: false, paintConnected: false });
  await page.evaluate(() => {
    const state = (window as any).resolutionState, original = state.project.nodes.find((node: any) => node.id === 'gallery-picture');
    const canvas = document.createElement('canvas'); canvas.width = canvas.height = 512;
    const context = canvas.getContext('2d')!; context.fillStyle = '#a426df'; context.fillRect(0, 0, 512, 512);
    const source = canvas.toDataURL('image/png'); state.secondSource = source;
    state.project.assets.push({ id: 'second-full-resolution', name: 'Second.png', mime: 'image/png', base64: source.split(',')[1] });
    state.project.nodes.push({ ...original, id: 'second-gallery-picture', title: 'Second picture', assetId: 'second-full-resolution' });
    state.draw(); state.oldGallerySource = document.querySelector(state.selector); state.oldGalleryPaint = document.querySelector('.ms-resolution-image');
    state.originalGalleryWorld = document.querySelector('.ms-world'); state.snapshot = JSON.stringify(state.project);
  });
  await expectPaintAligned(page, '.ms-gallery-image');
  await page.getByRole('button', { name: 'Next object in Detailed image', exact: true }).click();
  await expect(page.locator('[data-gallery-item-id="second-gallery-picture"]')).toBeVisible();
  await expectPaintAligned(page, '.ms-gallery-image');
  await expect(page.locator('.ms-resolution-image')).toHaveCount(1);
  expect(await page.evaluate(() => { const state = (window as any).resolutionState;
    return { oldSourceConnected: state.oldGallerySource.isConnected, oldPaintConnected: state.oldGalleryPaint.isConnected,
      replacedWorld: document.querySelector('.ms-world') !== state.originalGalleryWorld, graphUnchanged: JSON.stringify(state.project) === state.snapshot,
      replacedSource: (document.querySelector('.ms-resolution-image') as HTMLImageElement).currentSrc === state.secondSource };
  })).toEqual({ oldSourceConnected: false, oldPaintConnected: false, replacedWorld: true, graphUnchanged: true, replacedSource: true });
  await expect(page.locator('.ms-image-resolution-layer')).toHaveCount(1);
});

test('hidden ancestors restore paint and removing the final host releases its image lifecycle without another render', async ({ page }) => {
  await page.setViewportSize({ width: 1200, height: 1050 }); await mount(page);
  await page.evaluate(() => {
    const state = (window as any).resolutionState, outer = document.createElement('div'); outer.id = 'outer';
    document.querySelector('#scene')!.before(outer); outer.append(document.querySelector('#scene')!);
    const add = window.addEventListener.bind(window), remove = window.removeEventListener.bind(window);
    state.resizeListeners = new Set<EventListenerOrEventListenerObject>();
    window.addEventListener = ((type: string, callback: EventListenerOrEventListenerObject, options?: any) => {
      if (type === 'resize') state.resizeListeners.add(callback); add(type, callback, options);
    }) as typeof window.addEventListener;
    window.removeEventListener = ((type: string, callback: EventListenerOrEventListenerObject, options?: any) => {
      if (type === 'resize') state.resizeListeners.delete(callback); remove(type, callback, options);
    }) as typeof window.removeEventListener;
  });
  await imageCapture(page, 3, 4, 'gallery');
  await page.evaluate(() => {
    const state = (window as any).resolutionState;
    state.snapshot = JSON.stringify(state.project); state.retainedSource = document.querySelector(state.selector);
    state.retainedLayer = document.querySelector('.ms-image-resolution-layer'); state.retainedPaint = document.querySelector('.ms-resolution-image');
  });
  expect(await page.evaluate(() => (window as any).resolutionState.resizeListeners.size)).toBe(1);
  await page.locator('#outer').evaluate(element => (element as HTMLElement).style.visibility = 'hidden');
  await expect.poll(() => page.locator('.ms-resolution-image').evaluate(element => getComputedStyle(element).display)).toBe('none');
  await page.locator('#outer').evaluate(element => (element as HTMLElement).style.removeProperty('visibility'));
  await expectPaintAligned(page, '.ms-gallery-image');
  await page.locator('#outer').evaluate(element => (element as HTMLElement).hidden = true);
  await expect.poll(() => page.locator('.ms-resolution-image').evaluate(element => getComputedStyle(element).display)).toBe('none');
  await page.locator('#outer').evaluate(element => (element as HTMLElement).hidden = false);
  await expectPaintAligned(page, '.ms-gallery-image');
  await page.locator('#outer').evaluate(element => element.remove());
  // No follow-up scene render or synthetic resize triggers disposal: removing
  // the actual host must release the retained tree and native window listener.
  await expect.poll(() => page.evaluate(() => {
    const state = (window as any).resolutionState;
    return { layerRemoved: state.retainedLayer.parentElement === null, paintRemoved: state.retainedPaint.parentElement === null,
      sourceRestored: !state.retainedSource.classList.contains('ms-resolution-source'), resizeListeners: state.resizeListeners.size };
  })).toEqual({ layerRemoved: true, paintRemoved: true, sourceRestored: true, resizeListeners: 0 });
  expect(await page.evaluate(() => {
    const state = (window as any).resolutionState; window.dispatchEvent(new Event('resize'));
    state.retainedSource.dispatchEvent(new Event('focusin', { bubbles: true }));
    return { graphUnchanged: JSON.stringify(state.project) === state.snapshot,
      fullSource: state.retainedSource.currentSrc === state.source && state.retainedSource.naturalWidth === 512,
      paints: document.querySelectorAll('.ms-image-resolution-layer,.ms-resolution-image').length };
  })).toEqual({ graphUnchanged: true, fullSource: true, paints: 0 });
});

for (const deviceScaleFactor of [1, 2]) test(`full-resolution source sampling is relative to screen size at DPR ${deviceScaleFactor}`, async ({ browser }, info) => {
  const context = await browser.newContext({ viewport: { width: 1200, height: 1050 }, deviceScaleFactor }), page = await context.newPage();
  await mount(page);
  const captures: Record<string, Awaited<ReturnType<typeof imageCapture>>> = {};
  for (const [name, width, height, fixture] of [['large', 560, 620, 'ordinary'], ['micro', 3, 4, 'ordinary'],
    ['gallery', 3, 4, 'gallery'], ['window', 3, 4, 'window'], ['distant', 3, 4, 'distant']] as const) {
    captures[name] = await imageCapture(page, width, height, fixture);
    await info.attach(`${name}-DPR${deviceScaleFactor}.png`, { body: captures[name]!.png, contentType: 'image/png' });
    await writeFile(info.outputPath(`${name}-DPR${deviceScaleFactor}.png`), captures[name]!.png);
    await writeFile(info.outputPath(`${name}-reference-DPR${deviceScaleFactor}.png`), captures[name]!.reference);
  }
  const metrics = Object.fromEntries(await Promise.all(Object.keys(captures).map(async name =>
    [name, { ...await compare(page, captures[name]!.reference, captures[name]!.png), details: captures[name]!.details }])));
  await writeFile(info.outputPath('sampling-metrics.json'), JSON.stringify(metrics, null, 2));
  await info.attach('sampling-metrics.json', { body: JSON.stringify(metrics, null, 2), contentType: 'application/json' });
  console.log(`IMAGE SAMPLING DPR${deviceScaleFactor}: ${JSON.stringify(metrics)}`);
  for (const metric of Object.values(metrics) as any[]) {
    expect(metric.meanAbsoluteError).toBeLessThan(1);
    expect(metric.edgeContrastB / metric.edgeContrastA).toBeGreaterThan(.98);
  }
  await context.close();
});
