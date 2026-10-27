import { test, expect, type Page } from '@playwright/test';
import { build } from 'esbuild';
import { readFile } from 'node:fs/promises';
import { addNode, newProject } from '../src/core/model.ts';
import type { MeridianNode } from '../src/core/types.ts';

let script: string, css: string;
test.beforeAll(async () => {
  css = await readFile('src/scene.css', 'utf8');
  script = (await build({ stdin: { contents: `import {fitContainedContent} from './src/contained-content.ts';window.contentFitHarness={fitContainedContent};`,
    resolveDir: process.cwd(), loader: 'ts' }, bundle: true, write: false, format: 'iife', platform: 'browser', target: 'es2022' })).outputFiles[0]!.text;
});

function fixture(kind: MeridianNode['kind'] = 'canvas-link') {
  const project = newProject('Contained content'), canvas = project.entryCanvasId;
  const parent = addNode(project, canvas, 'container', 'Parent wall');
  Object.assign(parent, { collapsed: false, containerVariant: 'freeform', sizeMode: 'fixed', width: 160, height: 100 });
  const child = addNode(project, canvas, kind, 'Greek Mythology · Living atlas');
  Object.assign(child, { parentId: parent.id, width: 300, height: 160, sizeMode: 'auto', style: { titleSize: 24, fontSize: 18 } });
  return { project, parent, child };
}

async function mount(page: Page, kind: MeridianNode['kind'] = 'canvas-link', image = false) {
  const data = fixture(kind);
  await page.setContent('<style>html,body{margin:0}.world{transform-origin:0 0;margin:100px;width:500px;height:500px}.ms-node{left:0;top:0}</style><div class="meridian-scene" style="height:900px"><div class="world"><article class="ms-node ms-auto-size"><header class="ms-node-header"><span class="ms-node-title">Greek Mythology · Living atlas</span></header><div class="ms-node-body"></div></article></div></div>');
  await page.addStyleTag({ content: css }); await page.addScriptTag({ content: script });
  await page.evaluate(({ project, child, image }: any) => {
    const card = document.querySelector<HTMLElement>('.ms-node')!, body = card.querySelector<HTMLElement>('.ms-node-body')!;
    card.style.setProperty('--ms-title-size', '24px'); card.style.setProperty('--ms-font-size', '18px');
    if (image) {
      const media = document.createElement('img'); media.className = 'ms-image'; media.alt = 'Inert image fixture';
      media.src = 'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLbtAAAAABJRU5ErkJggg==';
      media.style.width = '320px'; media.style.height = '240px'; body.append(media);
    }
    const button = document.createElement('button'); button.className = 'ms-canvas-open'; button.textContent = 'Open canvas ↗'; body.append(button);
    const state: any = { project, child, clicks: 0, before: JSON.stringify(project), scales: [] };
    button.onclick = () => state.clicks++;
    state.fit = (width: number, height: number) => {
      card.style.width = `${width}px`; card.style.height = `${height}px`;
      const scale = (window as any).contentFitHarness.fitContainedContent(card, project, child, { x: 0, y: 0, width, height }); state.scales.push(scale); return scale;
    };
    (window as any).contentFitTest = state; state.fit(98, 46);
  }, { project: data.project, child: data.child, image });
  return data;
}

test('a small contained reading tile fits its frame and controls remain clickable at each camera zoom', async ({ page }) => {
  await mount(page);
  for (const zoom of [.35, 1, 2]) {
    await page.locator('.world').evaluate((world, zoom) => (world as HTMLElement).style.transform = `scale(${zoom})`, zoom);
    const card = (await page.locator('.ms-node').boundingBox())!, button = page.locator('.ms-canvas-open');
    const box = (await button.boundingBox())!;
    expect(box.x).toBeGreaterThanOrEqual(card.x); expect(box.y).toBeGreaterThanOrEqual(card.y);
    expect(box.x + box.width).toBeLessThanOrEqual(card.x + card.width + .5);
    expect(box.y + box.height).toBeLessThanOrEqual(card.y + card.height + .5);
    await button.click();
  }
  expect(await page.evaluate(() => (window as any).contentFitTest.clicks)).toBe(3);
  expect(await page.evaluate(() => JSON.stringify((window as any).contentFitTest.project) === (window as any).contentFitTest.before)).toBe(true);
  await expect(page.locator('.ms-node > .ms-node-header')).toHaveCount(1);
  await expect(page.locator('.ms-node > .ms-node-body')).toHaveCount(1);
});

test('repeated resize previews are idempotent and enlarge the reading composition when there is room', async ({ page }) => {
  await mount(page);
  const scales = await page.evaluate(() => {
    const state = (window as any).contentFitTest;
    return [state.fit(98, 46), state.fit(98, 46), state.fit(170, 100), state.fit(98, 46), state.fit(600, 400)];
  });
  expect(scales[0]).toBe(scales[1]); expect(scales[0]).toBe(scales[3]); expect(scales[2]).toBeGreaterThan(scales[0]!);
  expect(scales[4]).toBeGreaterThan(1);
  const titleScale = await page.locator('.ms-node-header').evaluate(element => new DOMMatrixReadOnly(getComputedStyle(element).transform).a);
  // Text can shrink inside its upper zone without changing the independent
  // frame-sized doorway. Both must still grow, fit, and remain separate.
  expect(titleScale).toBeGreaterThan(1); expect(titleScale).toBeLessThanOrEqual(scales[4]!);
  const frame = (await page.locator('.ms-node').boundingBox())!, title = (await page.locator('.ms-node-header').boundingBox())!, doorway = (await page.locator('.ms-canvas-open').boundingBox())!;
  expect(title.y + title.height).toBeLessThanOrEqual(doorway.y + .5);
  expect(doorway.x).toBeGreaterThanOrEqual(frame.x - .5); expect(doorway.y).toBeGreaterThanOrEqual(frame.y - .5);
  expect(doorway.x + doorway.width).toBeLessThanOrEqual(frame.x + frame.width + .5); expect(doorway.y + doorway.height).toBeLessThanOrEqual(frame.y + frame.height + .5);
  await expect(page.locator('.ms-node')).toHaveAttribute('data-content-fit');
});

test('explicit media envelopes also fit before loading and leave authored appearance unchanged', async ({ page }) => {
  await mount(page, 'media', true);
  const card = (await page.locator('.ms-node').boundingBox())!, media = (await page.locator('.ms-image').boundingBox())!;
  expect(media.x).toBeGreaterThanOrEqual(card.x); expect(media.y).toBeGreaterThanOrEqual(card.y);
  expect(media.x + media.width).toBeLessThanOrEqual(card.x + card.width + .5);
  expect(media.y + media.height).toBeLessThanOrEqual(card.y + card.height + .5);
  expect(await page.locator('.ms-image').evaluate(element => ({ width: (element as HTMLElement).style.width, height: (element as HTMLElement).style.height })))
    .toEqual({ width: '320px', height: '240px' });
  expect(await page.locator('.ms-node-title').evaluate(element => getComputedStyle(element).fontSize)).toBe('24px');
});

test('a fixed media tile has the same fitting envelope before and after delayed image decoding', async ({ page }) => {
  await mount(page, 'media');
  const before = await page.evaluate(() => {
    const state = (window as any).contentFitTest, canvas = document.createElement('canvas'); canvas.width = 640; canvas.height = 960;
    canvas.getContext('2d')!.fillRect(0, 0, canvas.width, canvas.height);
    const data = canvas.toDataURL('image/png');
    state.project.assets.push({ id: 'portrait', name: 'Portrait.png', mime: 'image/png', base64: data.split(',')[1] });
    Object.assign(state.child, { sizeMode: 'fixed', contentType: 'asset', assetId: 'portrait', text: undefined });
    const card = document.querySelector<HTMLElement>('.ms-node')!; card.classList.remove('ms-auto-size');
    const body = card.querySelector<HTMLElement>('.ms-node-body')!, image = document.createElement('img'); image.className = 'ms-image';
    body.prepend(image); state.delayedImage = image; state.delayedSource = data;
    const scale = state.fit(98, 46), rect = image.getBoundingClientRect();
    return { scale, width: rect.width, height: rect.height };
  });
  const after = await page.evaluate(async () => {
    const state = (window as any).contentFitTest, image = state.delayedImage as HTMLImageElement;
    image.src = state.delayedSource; await image.decode();
    const rect = image.getBoundingClientRect();
    return { scale: Number(document.querySelector<HTMLElement>('.ms-node')!.dataset.contentFit), width: rect.width, height: rect.height };
  });
  expect(after).toEqual(before);
  expect(after.height / after.width).toBeCloseTo(1.5, 2);
  const card = (await page.locator('.ms-node').boundingBox())!, image = (await page.locator('.ms-image').boundingBox())!;
  expect(image.y + image.height).toBeLessThanOrEqual(card.y + card.height + .5);
});

test('ordinary exterior tiles fit content while expanded workspace bodies and utility geometry stay untouched', async ({ page }) => {
  await mount(page);
  const scales = await page.evaluate(() => {
    const state = (window as any).contentFitTest, original = { ...state.child }, variants: any[] = [
      { parentId: undefined }, { kind: 'container', collapsed: false }, { kind: 'media', containerVariant: 'gallery', collapsed: false },
      { kind: 'media', containerVariant: 'window', collapsed: false }, { kind: 'junction' }, { kind: 'canvas' },
    ];
    return variants.map(overrides => { Object.assign(state.child, original, overrides); return state.fit(36, 36); });
  });
  expect(scales.slice(0, 4).every(scale => scale > 0 && scale < 1)).toBe(true);
  expect(scales.slice(4)).toEqual([1, 1]);
  await expect(page.locator('.ms-node')).not.toHaveAttribute('data-content-fit');
});
