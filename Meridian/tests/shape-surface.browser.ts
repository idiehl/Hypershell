import { test, expect } from '@playwright/test';
import { build } from 'esbuild';
import { readFile } from 'node:fs/promises';
import { addNode, newProject } from '../src/core/model.ts';
import type { NodeShape } from '../src/core/types.ts';

let script: string, css: string;
test.beforeAll(async () => {
  css = (await Promise.all(['src/scene.css', 'src/gallery-scene.css', 'src/window-scene.css'].map(path => readFile(path, 'utf8')))).join('\n');
  script = (await build({ stdin: { contents: `import {fitContainedContent} from './src/contained-content.ts';import {renderGalleryTile} from './src/gallery-scene.ts';import {renderScene} from './src/scene.ts';window.shapeSurfaceHarness={fitContainedContent,renderGalleryTile,renderScene};`,
    resolveDir: process.cwd(), loader: 'ts' }, bundle: true, write: false, format: 'iife', platform: 'browser', target: 'es2022' })).outputFiles[0]!.text;
});

test('selection and hit overlays retain the outer border frame as border width increases', async ({ page }) => {
  await page.setContent('<div class="meridian-scene" style="height:500px"><article class="ms-node ms-selected" style="left:30px;top:30px;width:420px;height:170px;--ms-border:#9258a6"><svg class="ms-node-outline" viewBox="0 0 420 170"><path d="M0,0H420V170H0Z"></path></svg><svg class="ms-node-hit-surface" viewBox="0 0 420 170"><path d="M0,0H420V170H0Z"></path></svg><svg class="ms-node-border" viewBox="0 0 420 170"><path d="M0,0H420V170H0Z"></path></svg></article></div>');
  await page.addStyleTag({ content: css });
  await page.addStyleTag({ content: '.ms-node{transition:none!important}' });
  const card = page.locator('.ms-node');
  for (const width of [1, 8, 24]) {
    await card.evaluate((element, width) => (element as HTMLElement).style.setProperty('--ms-border-width', `${width}px`), width);
    const frame = (await card.boundingBox())!;
    for (const selector of ['.ms-node-outline', '.ms-node-hit-surface', '.ms-node-border']) {
      const overlay = (await page.locator(selector).boundingBox())!;
      for (const coordinate of ['x', 'y', 'width', 'height'] as const) expect(overlay[coordinate]).toBeCloseTo(frame[coordinate], 5);
    }
    expect(await card.evaluate(element => getComputedStyle(element).borderLeftColor)).toBe('rgb(146, 88, 166)');
    await card.evaluate(element => element.classList.remove('ms-selected'));
    expect(await card.evaluate(element => getComputedStyle(element).borderLeftColor)).toBe('rgb(146, 88, 166)');
    await card.evaluate(element => element.classList.add('ms-selected'));
  }
});

test('default reading surfaces center text and an explicit left alignment overrides every surface', async ({ page }) => {
  await page.setContent('<div class="meridian-scene" style="height:500px"><article class="ms-node" style="width:420px;height:300px"><header class="ms-node-header"><span class="ms-node-title">Title</span></header><div class="ms-node-body"><div class="ms-text">Body</div><pre class="ms-text-preview">Document</pre><input class="ms-number-control" value="42"></div></article></div>');
  await page.addStyleTag({ content: css });
  for (const alignment of ['center', 'left']) {
    if (alignment === 'left') await page.locator('.ms-node').evaluate(element => (element as HTMLElement).style.setProperty('--ms-text-align', 'left'));
    for (const selector of ['.ms-node-header', '.ms-node-body', '.ms-text', '.ms-text-preview', '.ms-number-control']) {
      expect(await page.locator(selector).evaluate(element => getComputedStyle(element).textAlign)).toBe(alignment);
    }
  }
});

test('a gallery item uses its own text alignment instead of inheriting another node\'s choice', async ({ page }) => {
  const project = newProject('Shape surface gallery'), canvas = project.entryCanvasId;
  const parent = addNode(project, canvas, 'container', 'Gallery'); Object.assign(parent, { collapsed: false, containerVariant: 'gallery' });
  const child = addNode(project, canvas, 'media', 'Text object'); Object.assign(child, { parentId: parent.id, contentType: 'text', text: 'A compact text object.' });
  await page.setContent('<div class="meridian-scene" style="height:500px"><article class="ms-node" style="width:420px;height:300px;--ms-text-align:left"><div class="ms-node-body"></div></article></div>');
  await page.addStyleTag({ content: css }); await page.addScriptTag({ content: script });
  await page.evaluate(({ project, parent }) => {
    const draw = (alignment?: 'left') => {
      project.nodes.find(node => node.parentId === parent.id)!.style = alignment ? { textAlign: alignment } : undefined;
      const host = document.querySelector<HTMLElement>('.ms-node-body')!; host.replaceChildren();
      (window as any).shapeSurfaceHarness.renderGalleryTile(host, project, parent, { frameWidth: 420, localItems: new Map(), imageGesture() {} });
    };
    (window as any).drawSurfaceGallery = draw; draw();
  }, { project, parent });
  for (const selector of ['.ms-gallery-item-title', '.ms-gallery-text']) expect(await page.locator(selector).evaluate(element => getComputedStyle(element).textAlign)).toBe('center');
  await page.evaluate(() => (window as any).drawSurfaceGallery('left'));
  for (const selector of ['.ms-gallery-item-title', '.ms-gallery-text']) expect(await page.locator(selector).evaluate(element => getComputedStyle(element).textAlign)).toBe('left');
});

test('a root-level shaped reading tile fits its safe interior without changing the document', async ({ page }) => {
  const project = newProject('Root shaped text'), canvas = project.entryCanvasId;
  const node = addNode(project, canvas, 'media', 'A long title in a compact silhouette');
  Object.assign(node, { width: 420, height: 100, sizeMode: 'fixed', contentType: 'text', text: 'Reading content stays inside its selected shape.', style: { shape: 'hexagon', fontSize: 22, titleSize: 24 } });
  await page.setContent('<div class="meridian-scene" style="height:500px"><article class="ms-node" data-shape="hexagon" style="left:40px;top:40px;width:420px;height:100px;padding:22px 180px;--ms-font-size:22px;--ms-title-size:24px"><header class="ms-node-header"><span class="ms-node-title">A long title in a compact silhouette</span></header><div class="ms-node-body"><div class="ms-text">Reading content stays inside its selected shape.</div></div></article></div>');
  await page.addStyleTag({ content: css }); await page.addScriptTag({ content: script });
  const result = await page.evaluate(({ project, node }) => {
    const before = JSON.stringify(project), card = document.querySelector<HTMLElement>('.ms-node')!;
    const scale = (window as any).shapeSurfaceHarness.fitContainedContent(card, project, node, { x: 40, y: 40, width: 420, height: 100 });
    return { scale, unchanged: JSON.stringify(project) === before };
  }, { project, node });
  expect(result.scale).toBeLessThan(1); expect(result.unchanged).toBe(true);
  const card = (await page.locator('.ms-node').boundingBox())!;
  for (const selector of ['.ms-node-header', '.ms-node-body']) {
    const rect = (await page.locator(selector).boundingBox())!;
    expect(rect.x).toBeGreaterThanOrEqual(card.x + 180); expect(rect.y).toBeGreaterThanOrEqual(card.y + 22);
    expect(rect.x + rect.width).toBeLessThanOrEqual(card.x + card.width - 180 + .5);
    expect(rect.y + rect.height).toBeLessThanOrEqual(card.y + card.height - 22 + .5);
  }
  expect(await page.locator('.ms-node-title').evaluate(element => getComputedStyle(element).fontSize)).toBe('24px');
});

test('a shaped expanded workspace fits only its title and keeps the child workspace untouched', async ({ page }) => {
  const project = newProject('Structural shaped header'), canvas = project.entryCanvasId;
  const node = addNode(project, canvas, 'container', 'A long title in a compact silhouette');
  Object.assign(node, { collapsed: false, containerVariant: 'freeform', sizeMode: 'fixed', width: 420, height: 100, style: { shape: 'hexagon', titleSize: 24 } });
  await page.setContent('<div class="meridian-scene" style="height:500px"><article class="ms-node ms-node-container" data-shape="hexagon" style="width:420px;height:100px;padding:10px 180px;--ms-title-size:24px"><header class="ms-node-header"><span class="ms-node-title">A long title in a compact silhouette</span></header><div class="ms-node-body" style="width:180px"></div><div class="world-space-child" style="position:absolute;left:185px;top:40px;width:40px;height:30px"></div></article></div>');
  await page.addStyleTag({ content: css }); await page.addScriptTag({ content: script });
  const result = await page.evaluate(({ project, node }) => {
    const card = document.querySelector<HTMLElement>('.ms-node')!, body = card.querySelector<HTMLElement>('.ms-node-body')!, child = card.querySelector<HTMLElement>('.world-space-child')!;
    const before = { body: body.getAttribute('style'), child: child.getAttribute('style'), project: JSON.stringify(project) };
    const scale = (window as any).shapeSurfaceHarness.fitContainedContent(card, project, node, { x: 0, y: 0, width: 420, height: 100 });
    return { scale, before, after: { body: body.getAttribute('style'), child: child.getAttribute('style'), project: JSON.stringify(project) } };
  }, { project, node });
  expect(result.scale).toBeLessThan(1); expect(result.after).toEqual(result.before);
  const header = (await page.locator('.ms-node-header').boundingBox())!, card = (await page.locator('.ms-node').boundingBox())!;
  expect(header.x + header.width).toBeLessThanOrEqual(card.x + 240.5);
});

test('wide authored frames preserve each regular silhouette and keep actual reading surfaces inside it', async ({ page }) => {
  const project = newProject('Regular backdrop reading surfaces'), node = addNode(project, project.entryCanvasId, 'media', 'A full title inside the selected silhouette');
  Object.assign(node, { x: 40, y: 40, width: 560, height: 140, sizeMode: 'fixed', contentType: 'text', text: 'Content remains within the backdrop after the shape changes.', style: { shape: 'hexagon', titleSize: 20, fontSize: 18 } });
  await page.setContent('<style>html,body{margin:0}#scene{width:1000px;height:600px}</style><div id="scene"></div>');
  await page.addStyleTag({ content: css }); await page.addScriptTag({ content: script });
  const shapes: Array<{ shape: NodeShape; ratio: number }> = [
    { shape: 'square', ratio: 1 }, { shape: 'circle', ratio: 1 }, { shape: 'triangle', ratio: 2 / Math.sqrt(3) },
    { shape: 'diamond', ratio: 1 }, { shape: 'pentagon', ratio: 2 * Math.sin(Math.PI * 2 / 5) / (1 + Math.cos(Math.PI / 5)) },
    { shape: 'hexagon', ratio: 2 / Math.sqrt(3) }, { shape: 'octagon', ratio: 1 },
  ];
  for (const { shape, ratio } of shapes) {
    const observed = await page.evaluate(({ project, id, shape }) => {
      const node = project.nodes.find(node => node.id === id)!; node.style!.shape = shape;
      const before = JSON.stringify(project);
      (window as any).shapeSurfaceHarness.renderScene(document.getElementById('scene'), project, project.entryCanvasId, { selection: id, view: { x: 0, y: 0, scale: 1 } });
      const card = document.querySelector<HTMLElement>(`[data-node-id="${id}"]`)!, frame = card.getBoundingClientRect();
      const path = card.querySelector<SVGGeometryElement>('.ms-node-hit-surface path')!, svg = path.ownerSVGElement!, physical = path.getBBox();
      const cornersInside = [...card.querySelectorAll<HTMLElement>(':scope > .ms-node-header,:scope > .ms-node-body')].every(surface => {
        const rect = surface.getBoundingClientRect();
        return [[rect.left, rect.top], [rect.right, rect.top], [rect.right, rect.bottom], [rect.left, rect.bottom]].every(([x, y]) => {
          const point = svg.createSVGPoint(); point.x = x - frame.left; point.y = y - frame.top;
          return path.isPointInFill(point);
        });
      });
      const style = getComputedStyle(card, '::before');
      return { ratio: physical.width / physical.height, cornersInside, unchanged: JSON.stringify(project) === before,
        paintWidth: frame.width - Number.parseFloat(style.left) - Number.parseFloat(style.right),
        paintHeight: frame.height - Number.parseFloat(style.top) - Number.parseFloat(style.bottom),
        physicalWidth: physical.width, physicalHeight: physical.height };
    }, { project, id: node.id, shape });
    expect(observed.ratio, shape).toBeCloseTo(ratio, 5); expect(observed.cornersInside, shape).toBe(true); expect(observed.unchanged, shape).toBe(true);
    expect(observed.paintWidth, shape).toBeCloseTo(observed.physicalWidth, 2); expect(observed.paintHeight, shape).toBeCloseTo(observed.physicalHeight, 2);
  }
  await page.screenshot({ path: test.info().outputPath('regular-backdrop-content.png') });
});
