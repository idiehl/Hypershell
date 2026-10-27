import { test, expect, type Locator, type Page } from '@playwright/test';
import { build } from 'esbuild';
import { readFile, writeFile } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';
import { addCanvas, addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import type { MeridianNode, MeridianProject } from '../src/core/types.ts';
import { buildPublication } from '../src/publication.ts';
import { inspectorPanel } from './ui-helpers.ts';

let script: string, viewerScript: string, css: string;
test.beforeAll(async () => {
  css = (await Promise.all(['scene.css','grid.css','gallery-scene.css','backgrounds.css','window-scene.css','canvas-launcher.css','expansion-tray-scene.css']
    .map(name => readFile(`src/${name}`, 'utf8')))).join('\n');
  const bundles = await Promise.all([
    build({ stdin: { contents: "import {renderScene} from './src/scene.ts';import {fitContainedContent} from './src/contained-content.ts';window.stableContent={renderScene,fitContainedContent};", resolveDir: process.cwd(), loader: 'ts' }, bundle: true, write: false, format: 'iife', target: 'es2022', platform: 'browser' }),
    build({ entryPoints: ['src/viewer.ts'], bundle: true, write: false, format: 'iife', target: 'es2022', platform: 'browser' })
  ]);
  script = bundles[0].outputFiles[0]!.text; viewerScript = bundles[1].outputFiles[0]!.text;
});
const card = (page: Page, id: string) => page.locator(`.ms-node[data-node-id="${id}"]`);
const launcher = (page: Page, id: string) => card(page, id).locator('.ms-canvas-open');
function fixture() {
  const project = newProject('Stable frame typography and door sizes'), target = addCanvas(project, 'Destination');
  const make = (kind: MeridianNode['kind'], title: string, index: number) => {
    const node = addNode(project, project.entryCanvasId, kind, title);
    Object.assign(node, { x: 40 + index * 320, y: 60, width: 300, height: 180, sizeMode: 'fixed', placement: 'free', collapsed: true,
      style: { shape: 'rectangle', borderWidth: 0, titleSize: 18, fontSize: 18, padding: 5 } }); return node;
  };
  const short = make('canvas-link', 'Short', 0), two = make('canvas-link', 'A second title\nwith two lines', 1), blank = make('canvas-link', '', 2);
  for (const node of [short, two, blank]) node.targetId = target.rootId;
  const reference = make('reference', 'A referenced title\nwith two lines', 3); reference.targetId = short.id;
  return { project, target, short, two, blank, reference, nodes: [short, two, blank, reference] };
}
async function mount(page: Page, project: MeridianProject) {
  await page.setContent('<style>html,body{margin:0}#scene{width:1450px;height:760px}</style><main id="scene"></main>');
  await page.addStyleTag({ content: css }); await page.addScriptTag({ content: script });
  await page.evaluate(project => {
    const state: any = { project, before: JSON.stringify(project), opened: [] };
    state.draw = (scale = 1, x = 0, y = 0) => (window as any).stableContent.renderScene(document.querySelector('#scene'), project, project.entryCanvasId,
      { readonly: true, fit: false, view: { x, y, scale }, onCanvasOpen: (id: string) => state.opened.push(id) });
    (window as any).stableState = state; state.draw();
  }, project);
}
async function effectiveFont(surface: Locator) {
  return surface.evaluate(text => {
    let scale = 1;
    for (let element: Element | null = text; element && !element.classList.contains('ms-world'); element = element.parentElement) {
      const transform = new DOMMatrixReadOnly(getComputedStyle(element).transform); scale *= Math.hypot(transform.a, transform.b);
    }
    return parseFloat(getComputedStyle(text).fontSize) * scale;
  });
}
async function centered(page: Page, id: string) {
  const frame = (await card(page, id).boundingBox())!, icon = (await launcher(page, id).boundingBox())!;
  expect(icon.x + icon.width / 2).toBeCloseTo(frame.x + frame.width / 2, 0);
  expect(icon.y + icon.height / 2).toBeCloseTo(frame.y + frame.height / 2, 0);
  return { width: icon.width, height: icon.height };
}
async function allTextInside(surface: Locator) {
  const results = await surface.evaluate(root => {
    const outer = root.getBoundingClientRect();
    return [...root.querySelectorAll('.ms-node-title,.ms-text,.ms-gallery-item-title')].flatMap(text => {
      const range = document.createRange(); range.selectNodeContents(text);
      return [...range.getClientRects()].map(rect => ({ x: rect.x - outer.x, y: rect.y - outer.y, right: rect.right - outer.x, bottom: rect.bottom - outer.y, width: outer.width, height: outer.height }));
    });
  });
  for (const rect of results) {
    expect(rect.x).toBeGreaterThanOrEqual(-.6); expect(rect.y).toBeGreaterThanOrEqual(-.6);
    expect(rect.right).toBeLessThanOrEqual(rect.width + .6); expect(rect.bottom).toBeLessThanOrEqual(rect.height + .6);
  }
}

test('equal frames retain equal authored one-line and two-line text scales and independent centered door sizes', async ({ page }, info) => {
  const f = fixture(); await mount(page, f.project);
  const sizes = [];
  for (const node of f.nodes) { sizes.push(await centered(page, node.id)); await allTextInside(card(page, node.id)); }
  for (const node of [f.short, f.two, f.reference]) expect(await effectiveFont(card(page, node.id).locator('.ms-node-title'))).toBeCloseTo(18, 4);
  for (const size of sizes) expect(size).toEqual(sizes[0]);
  const mediaOne = { ...f.short, id: crypto.randomUUID(), kind: 'media' as const, title: 'One line', x: 40, y: 300, targetId: undefined };
  const mediaTwo = { ...mediaOne, id: crypto.randomUUID(), title: 'Two lines\nwith the same font', x: 360 };
  await page.evaluate(nodes => { (window as any).stableState.project.nodes.push(...nodes); (window as any).stableState.draw(); }, [mediaOne, mediaTwo]);
  expect(await effectiveFont(card(page, mediaOne.id).locator('.ms-node-title'))).toBeCloseTo(18, 4);
  expect(await effectiveFont(card(page, mediaTwo.id).locator('.ms-node-title'))).toBeCloseTo(18, 4);
  await page.screenshot({ path: info.outputPath('equal-frame-fonts-and-centered-doors.png') });
});

test('intrinsic automatic frames preserve native fonts without changing the independent door policy', async ({ page }, info) => {
  const f = fixture();
  f.short.sizeMode = 'auto'; f.short.title = 'Short';
  const ordinary = addNode(f.project, f.project.entryCanvasId, 'media', 'Short native text');
  Object.assign(ordinary, { x: 40, y: 350, width: 300, height: 180, sizeMode: 'auto', style: { titleSize: 18, fontSize: 18, shape: 'rectangle' } });
  const longer = { ...ordinary, id: crypto.randomUUID(), x: 400, title: 'A longer ordinary native title' }; f.project.nodes.push(longer);
  await mount(page, f.project);
  for (const node of [ordinary, longer]) {
    expect(await effectiveFont(card(page, node.id).locator('.ms-node-title'))).toBeCloseTo(18, 4);
    await allTextInside(card(page, node.id));
  }
  const ownFrame = await card(page, f.short.id).evaluate(element => ({ width: parseFloat((element as HTMLElement).style.width), height: parseFloat((element as HTMLElement).style.height) }));
  const fixed = { ...f.short, id: crypto.randomUUID(), x: 40, y: 550, width: ownFrame.width, height: ownFrame.height, sizeMode: 'fixed' as const };
  await page.evaluate(node => { (window as any).stableState.project.nodes.push(node); (window as any).stableState.draw(); }, fixed);
  const nativeCube = await centered(page, f.short.id), fixedCube = await centered(page, fixed.id);
  expect(nativeCube.width).toBeCloseTo(fixedCube.width, 4); expect(nativeCube.height).toBeCloseTo(fixedCube.height, 4);
  expect(await effectiveFont(card(page, f.short.id).locator('.ms-node-title'))).toBeGreaterThan(await effectiveFont(card(page, fixed.id).locator('.ms-node-title')));
  await page.screenshot({ path: info.outputPath('intrinsic-native-fonts-and-independent-door.png') });
});

test('actual title overflow shrinks text without changing its door, and preview resizing is vector-proportional and idempotent', async ({ page }, info) => {
  const f = fixture(); f.two.title = 'A deliberately much longer title with enough words to require several lines inside the same title area'; await mount(page, f.project);
  expect(await effectiveFont(card(page, f.two.id).locator('.ms-node-title'))).toBeLessThan(18);
  expect(await centered(page, f.short.id)).toEqual(await centered(page, f.two.id));
  const baseline = await centered(page, f.short.id), source = JSON.stringify(f.project);
  for (const [factor, zoom] of [[2, 1], [.01, 100], [1, 1], [1, 1]]) {
    await page.evaluate(({ id, factor, zoom }) => {
      const state = (window as any).stableState, node = state.project.nodes.find((node: any) => node.id === id), surface = document.querySelector<HTMLElement>(`.ms-node[data-node-id="${id}"]`)!;
      Object.assign(surface.style, { width: `${300 * factor}px`, height: `${180 * factor}px` });
      (window as any).stableContent.fitContainedContent(surface, state.project, node, { x: node.x, y: node.y, width: 300 * factor, height: 180 * factor });
      document.querySelector<HTMLElement>('.ms-world')!.style.transform = `translate(${40 - parseFloat(surface.style.left) * zoom}px, ${60 - parseFloat(surface.style.top) * zoom}px) scale(${zoom})`;
    }, { id: f.short.id, factor: factor!, zoom: zoom! });
    const size = await centered(page, f.short.id);
    expect(size.width / zoom!).toBeCloseTo(baseline.width * factor!, 5);
    expect(await effectiveFont(card(page, f.short.id).locator('.ms-node-title'))).toBeCloseTo(18 * factor!, 7);
    await allTextInside(card(page, f.short.id));
  }
  expect(await page.evaluate(() => JSON.stringify((window as any).stableState.project))).toBe(source);
  await page.screenshot({ path: info.outputPath('preview-restores-the-same-text-and-door-scale.png') });
});

test('rich text and rotations fit without changing authored formatting, and fixed typography remains an opt out', async ({ page }) => {
  const f = fixture();
  f.two.title = 'Rich title'; f.two.titleRuns = [{ start: 0, end: 5, style: { bold: true } }, { start: 5, end: 10, style: { fontSize: 27, italic: true, color: '#ef9872', underline: true } }];
  f.reference.style = { ...f.reference.style, titleRotation: 30 };
  f.blank.title = 'Fixed title'; f.blank.width = 600; f.blank.height = 360; f.blank.y = 350; f.blank.x = 40; f.blank.style = { ...f.blank.style, contentSizing: 'fixed' };
  const authored = JSON.stringify(f.project); await mount(page, f.project);
  await allTextInside(card(page, f.two.id)); await allTextInside(card(page, f.reference.id));
  const runs = card(page, f.two.id).locator('.ms-node-title .ms-text-run');
  expect((await effectiveFont(runs.nth(1))) / (await effectiveFont(runs.nth(0)))).toBeCloseTo(1.5, 4);
  await expect(runs.nth(1)).toHaveCSS('font-style', 'italic');
  expect(await effectiveFont(card(page, f.blank.id).locator('.ms-node-title'))).toBeCloseTo(18, 4);
  expect(await page.evaluate(() => JSON.stringify((window as any).stableState.project))).toBe(authored);
});

test('embedded Gallery pages retain the same frame-based font and centered door across title lengths and blank titles', async ({ page }, info) => {
  const f = fixture(), gallery = addNode(f.project, f.project.entryCanvasId, 'container', '');
  Object.assign(gallery, { x: 40, y: 300, width: 360, height: 300, sizeMode: 'fixed', collapsed: false, containerVariant: 'gallery', childLayout: 'contained', style: { shape: 'rectangle', fontSize: 18, titleSize: 18, childPadding: 5, padding: 5 } });
  const items = ['One line', 'Two lines\nwith consistent type', ''].map(title => {
    const node = addNode(f.project, f.project.entryCanvasId, 'canvas-link', title);
    Object.assign(node, { parentId: gallery.id, targetId: f.target.rootId, width: 300, height: 180, sizeMode: 'fixed', style: { fontSize: 18, titleSize: 18 } }); return node;
  });
  await mount(page, f.project); const sizes = [], fonts = [];
  for (let index = 0; index < items.length; index++) {
    const scope = card(page, gallery.id), icon = scope.locator('.ms-canvas-open'), pageBox = (await scope.locator('.ms-gallery-item').boundingBox())!, box = (await icon.boundingBox())!;
    sizes.push({ width: box.width, height: box.height }); expect(box.x + box.width / 2).toBeCloseTo(pageBox.x + pageBox.width / 2, 0); expect(box.y + box.height / 2).toBeCloseTo(pageBox.y + pageBox.height / 2, 0);
    if (index < 2) fonts.push(await effectiveFont(scope.locator('.ms-gallery-item-title')));
    await allTextInside(scope); if (index < items.length - 1) await scope.getByRole('button', { name: /^Next object in / }).click();
  }
  expect(sizes[1]).toEqual(sizes[0]); expect(sizes[2]).toEqual(sizes[0]); expect(fonts[1]).toBeCloseTo(fonts[0]!, 4);
  await page.screenshot({ path: info.outputPath('stable-gallery-door-after-page-changes.png') });
});

test('offline standalone viewer uses the same stable original and referenced launcher rendering', async ({ browser }, info) => {
  const f = fixture(), file = info.outputPath('stable-frame-viewer.html');
  await writeFile(file, buildPublication(commitSnapshots(f.project), viewerScript, css));
  const context = await browser.newContext({ offline: true, viewport: { width: 1500, height: 950 } });
  try {
    const page = await context.newPage(); await page.goto(pathToFileURL(file).href);
    const boxes = await Promise.all(f.nodes.map(node => launcher(page, node.id).boundingBox()));
    for (const box of boxes) expect(box!.width).toBeCloseTo(boxes[0]!.width, 4);
    const fonts = await Promise.all([f.short, f.two, f.reference].map(node => effectiveFont(card(page, node.id).locator('.ms-node-title'))));
    expect(fonts[1]).toBeCloseTo(fonts[0]!, 4); expect(fonts[2]).toBeCloseTo(fonts[0]!, 4);
    await page.screenshot({ path: info.outputPath('stable-fonts-and-doors-in-offline-viewer.png') });
    await launcher(page, f.reference.id).click(); await expect(card(page, f.short.id)).toHaveCount(0);
  } finally { await context.close(); }
});

test('full editor saves and reloads without changing authored fonts or same-frame door rendering', async ({ page, request }) => {
  const f = fixture(), response = await request.post('/api/projects', { data: commitSnapshots(f.project) }); expect(response.ok()).toBe(true);
  await page.goto(`/?project=${f.project.id}`); await expect(page.locator('.workspace')).toBeVisible();
  await page.getByRole('button', { name: 'Fit Work', exact: true }).click();
  const before = await Promise.all(f.nodes.map(node => launcher(page, node.id).boundingBox()));
  await inspectorPanel(page, 'canvas'); const search = page.getByRole('searchbox', { name: 'Search Work nodes', exact: true }); await search.fill(f.short.id);
  await page.locator(`[data-outline-node="${f.short.id}"] .inspector-tree-title`).click(); await search.fill('');
  await inspectorPanel(page, 'properties'); const tag = page.locator('.inspector').getByLabel('Tag', { exact: true }); await tag.fill('Saved stable doorway'); await tag.press('Tab');
  await expect.poll(async () => (await (await request.get(`/api/projects/${f.project.id}`)).json() as MeridianProject).nodes.find(node => node.id === f.short.id)!.tag).toBe('Saved stable doorway');
  await page.reload(); await page.getByRole('button', { name: 'Fit Work', exact: true }).click();
  const after = await Promise.all(f.nodes.map(node => launcher(page, node.id).boundingBox()));
  for (let index = 0; index < before.length; index++) expect(after[index]!.width).toBeCloseTo(before[index]!.width, 4);
  const saved = await (await request.get(`/api/projects/${f.project.id}`)).json() as MeridianProject;
  for (const node of f.nodes) expect(saved.nodes.find(candidate => candidate.id === node.id)!.style).toEqual(node.style);
});
