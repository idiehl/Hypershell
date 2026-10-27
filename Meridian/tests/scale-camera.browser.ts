import { test, expect, type Page } from '@playwright/test';
import { build } from 'esbuild';
import { readFile, writeFile } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';
import { addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import { buildPublication, readPublication } from '../src/publication.ts';
import type { MeridianProject } from '../src/core/types.ts';
import { inspectorPanel, interactionTool, selectEditorNode } from './ui-helpers.ts';

let script: string, css: string;
test.beforeAll(async () => {
  css = await readFile('dist/scene.css', 'utf8');
  script = (await build({ stdin: { contents: `import {renderScene} from './src/scene.ts';window.scaleCameraHarness={renderScene};`,
    resolveDir: process.cwd(), loader: 'ts' }, bundle: true, write: false, format: 'iife', platform: 'browser', target: 'es2022' })).outputFiles[0]!.text;
});

function fixture() {
  const project = newProject('Scale camera fixture'), node = addNode(project, project.entryCanvasId, 'media', 'Detail');
  Object.assign(node, { x: 40, y: 30, width: 200, height: 120, sizeMode: 'fixed', contentType: 'text', text: 'Vector text stays editable.' });
  return { project, node };
}

async function mount(page: Page, project: MeridianProject, options: object = {}) {
  await page.setContent('<style>html,body{margin:0}#scene{width:1100px;height:800px}</style><main id="scene"></main>');
  await page.addStyleTag({ content: css }); await page.addScriptTag({ content: script });
  await page.evaluate(({ project, options }) => {
    const host = document.querySelector<HTMLElement>('#scene')!;
    const state: any = { project, before: JSON.stringify(project), selected: null, views: [] };
    state.draw = (next: object = {}) => (window as any).scaleCameraHarness.renderScene(host, project, project.entryCanvasId,
      { view: { x: 0, y: 0, scale: 1 }, onViewChange: (view: any) => { state.view = { ...view }; state.views.push({ ...view }); },
        onSelection: (id: string | null) => state.selected = id, ...options, ...next });
    (window as any).scaleCameraState = state; state.draw();
  }, { project, options });
}

async function view(page: Page): Promise<{ x: number; y: number; scale: number }> {
  return page.evaluate(() => (window as any).scaleCameraState.view);
}

test('camera buttons and cursor-anchored wheel zoom go beyond the old range without modifying the graph', async ({ page }) => {
  const { project } = fixture(); await mount(page, project, { view: { x: 60, y: 45, scale: 8 } });
  await expect(page.locator('.ms-zoom')).toHaveText('800%');
  await page.getByRole('button', { name: 'Zoom in', exact: true }).click();
  expect((await view(page)).scale).toBe(10);
  const viewport = (await page.locator('.ms-viewport').boundingBox())!, pointer = { x: 420, y: 315 }, before = await view(page);
  const worldPoint = { x: (pointer.x - before.x) / before.scale, y: (pointer.y - before.y) / before.scale };
  await page.mouse.move(viewport.x + pointer.x, viewport.y + pointer.y); await page.mouse.wheel(0, -600);
  await expect.poll(async () => (await view(page)).scale).toBeGreaterThan(10);
  const after = await view(page);
  expect((pointer.x - after.x) / after.scale).toBeCloseTo(worldPoint.x, 8);
  expect((pointer.y - after.y) / after.scale).toBeCloseTo(worldPoint.y, 8);
  await page.evaluate(() => (window as any).scaleCameraState.draw({ view: { x: 0, y: 0, scale: 1000 } }));
  await page.getByRole('button', { name: 'Zoom in', exact: true }).click(); expect((await view(page)).scale).toBe(1024);
  await page.getByRole('button', { name: 'Zoom in', exact: true }).click(); expect((await view(page)).scale).toBe(1024);
  expect(await page.evaluate(() => JSON.stringify((window as any).scaleCameraState.project) === (window as any).scaleCameraState.before)).toBe(true);
});

test('deep overview zoom shows fractional percentages and preserves the camera across repaint', async ({ page }) => {
  const { project } = fixture(); await mount(page, project, { view: { x: 20, y: 35, scale: .0005 } });
  await expect(page.locator('.ms-zoom')).toHaveText('0.05%');
  for (let at = 0; at < 12; at++) await page.getByRole('button', { name: 'Zoom out', exact: true }).click();
  expect((await view(page)).scale).toBe(.0001); await expect(page.locator('.ms-zoom')).toHaveText('0.01%');
  const current = await view(page);
  await page.evaluate(() => {
    const state = (window as any).scaleCameraState; state.draw({ view: state.view });
  });
  expect(await view(page)).toEqual(current);
  expect(await page.evaluate(() => JSON.stringify((window as any).scaleCameraState.project) === (window as any).scaleCameraState.before)).toBe(true);
});

test('Fit Work handles a large world and Jump to Node reveals a tiny vector tile at detail zoom', async ({ page }, info) => {
  const { project, node } = fixture();
  Object.assign(node, { x: 200000, y: 100000, width: 3, height: 4, title: 'Detail', text: 'Layers',
    style: { padding: 0, contentGap: 0, borderWidth: 0, fontSize: 13, titleSize: 16, shape: 'rectangle' } });
  const far = addNode(project, project.entryCanvasId, 'media', 'Overview landmark');
  Object.assign(far, { x: 900000, y: 700000, width: 80, height: 50, sizeMode: 'fixed', contentType: 'text', text: 'Distant context' });
  await mount(page, project, { fit: true });
  expect((await view(page)).scale).toBeLessThan(.01); expect((await view(page)).scale).toBeGreaterThanOrEqual(.0001);
  await page.evaluate(id => (window as any).scaleCameraState.draw({ focusNodeId: id, selection: id }), node.id);
  expect((await view(page)).scale).toBeGreaterThan(100);
  const card = page.locator(`.ms-node[data-node-id="${node.id}"]`), frame = (await card.boundingBox())!;
  expect(frame.width).toBeGreaterThan(450); expect(frame.height).toBeGreaterThan(600);
  await expect(card.locator(':scope > .ms-node-outline > path')).toHaveCount(1);
  await expect(card.locator(':scope > .ms-node-hit-surface > path')).toHaveCount(1);
  await expect(card.locator('.ms-node-title')).toHaveText('Detail'); await expect(card.locator('.ms-text')).toHaveText('Layers');
  expect(await card.locator('canvas').count()).toBe(0);
  for (const selector of ['.ms-node-title', '.ms-text']) {
    const text = (await card.locator(selector).boundingBox())!;
    expect(text.x).toBeGreaterThanOrEqual(frame.x - 1); expect(text.x + text.width).toBeLessThanOrEqual(frame.x + frame.width + 1);
    expect(text.y).toBeGreaterThanOrEqual(frame.y - 1); expect(text.y + text.height).toBeLessThanOrEqual(frame.y + frame.height + 1);
  }
  // Use the same viewport coordinates as a user. Locator auto-scroll follows
  // the far world-space offset and would scroll the deliberately clipped stage.
  await writeFile(info.outputPath('tiny-camera-before.json'), JSON.stringify(await page.evaluate(point => ({
    point, cards: [...document.querySelectorAll<HTMLElement>('.ms-node')].map(element => ({ id: element.dataset.nodeId,
      left: element.style.left, top: element.style.top, box: element.getBoundingClientRect().toJSON() })),
    hit: document.elementFromPoint(point.x, point.y)?.outerHTML.slice(0, 500),
    viewport: [...document.querySelectorAll<HTMLElement>('.ms-viewport,.ms-world,#scene')].map(element => ({ className: element.className,
      scrollLeft: element.scrollLeft, scrollTop: element.scrollTop, style: element.getAttribute('style') })),
  }), { x: frame.x + frame.width / 2, y: frame.y + frame.height / 2 }), null, 2));
  const stage = (await page.locator('.ms-viewport').boundingBox())!;
  expect(frame.x).toBeGreaterThanOrEqual(stage.x - 2); expect(frame.x + frame.width).toBeLessThanOrEqual(stage.x + stage.width + 2);
  expect(frame.y).toBeGreaterThanOrEqual(stage.y - 2); expect(frame.y + frame.height).toBeLessThanOrEqual(stage.y + stage.height + 2);
  expect(await page.evaluate(point => document.elementFromPoint(point.x, point.y)?.closest<HTMLElement>('.ms-node')?.dataset.nodeId,
    { x: frame.x + frame.width / 2, y: frame.y + frame.height / 2 })).toBe(node.id);
  await page.screenshot({ path: info.outputPath('tiny-vector-node-before-click.png') });
  await page.mouse.click(frame.x + frame.width / 2, frame.y + frame.height / 2);
  expect(await page.evaluate(() => (window as any).scaleCameraState.selected)).toBe(node.id);
  expect(await page.evaluate(() => JSON.stringify((window as any).scaleCameraState.project) === (window as any).scaleCameraState.before)).toBe(true);
  await page.screenshot({ path: info.outputPath('tiny-vector-node-detail.png') });
});

test('portable viewer retains deep and high zoom with native text and SVG silhouettes offline', async ({ browser }, info) => {
  const { project, node } = fixture();
  Object.assign(node, { width: 40, height: 30, style: { shape: 'ellipse', padding: 0, borderWidth: 0 }, text: 'Readable detail' });
  const filename = info.outputPath('scale-camera-offline.html');
  await writeFile(filename, buildPublication(project, await readFile('dist/viewer.js', 'utf8'), css));
  const context = await browser.newContext({ offline: true, viewport: { width: 1500, height: 950 } });
  try {
    const page = await context.newPage(), requests: string[] = [], errors: string[] = [];
    page.on('request', request => { if (/^https?:/.test(request.url())) requests.push(request.url()); });
    page.on('pageerror', error => errors.push(error.message)); await page.goto(pathToFileURL(filename).href);
    const payload = await page.locator('#meridian-data').textContent();
    const initial = parseFloat((await page.locator('.ms-zoom').textContent())!) / 100; expect(initial).toBeGreaterThan(3);
    for (let at = 0; at < 12; at++) await page.getByRole('button', { name: 'Zoom in', exact: true }).click();
    expect(parseFloat((await page.locator('.ms-zoom').textContent())!) / 100).toBeGreaterThan(100);
    await expect(page.locator(`.ms-node[data-node-id="${node.id}"] .ms-text`)).toHaveText(node.text!);
    await expect(page.locator(`.ms-node[data-node-id="${node.id}"] > .ms-node-outline > path`)).toHaveCount(1);
    expect(await page.locator('canvas').count()).toBe(0); expect(await page.locator('#meridian-data').textContent()).toBe(payload);
    expect(requests).toEqual([]); expect(errors).toEqual([]);
  } finally { await context.close(); }
});

test('Content sizing persists through autosave, reload and publication while the host rejects invalid enum values', async ({ page, request }, info) => {
  const { project, node } = fixture(); node.style = { contentSizing: 'fit', fontSize: 22 };
  const fixed = addNode(project, project.entryCanvasId, 'media', 'Authored typography');
  Object.assign(fixed, { x: 330, y: 40, width: 260, height: 170, sizeMode: 'fixed', contentType: 'text', text: 'Keep this typography.', style: { contentSizing: 'fixed', titleSize: 24 } });
  const created = await request.post('/api/projects', { data: commitSnapshots(project) });
  expect(created.ok(), await created.text()).toBeTruthy(); const initial: MeridianProject = await created.json();
  await page.goto(`/?project=${initial.id}`); await selectEditorNode(page, node.id, node.title); await inspectorPanel(page, 'appearance');
  const sizing = page.locator('.inspector').getByRole('combobox', { name: 'Content sizing', exact: true });
  await expect(sizing).toHaveValue('fit'); await sizing.selectOption('fixed');
  await expect.poll(async () => {
    const stored: MeridianProject = await (await request.get(`/api/projects/${initial.id}`)).json();
    return stored.nodes.find(candidate => candidate.id === node.id)!.style?.contentSizing;
  }).toBe('fixed');
  await page.reload(); await selectEditorNode(page, node.id, node.title); await inspectorPanel(page, 'appearance'); await expect(sizing).toHaveValue('fixed');
  await sizing.selectOption('fit');
  await expect.poll(async () => {
    const stored: MeridianProject = await (await request.get(`/api/projects/${initial.id}`)).json();
    return stored.nodes.find(candidate => candidate.id === node.id)!.style?.contentSizing;
  }).toBe('fit');
  const saved: MeridianProject = await (await request.get(`/api/projects/${initial.id}`)).json();
  expect(saved.nodes.find(candidate => candidate.id === fixed.id)!.style?.contentSizing).toBe('fixed');
  const html = buildPublication(saved, await readFile('dist/viewer.js', 'utf8'), css), portable = readPublication(html);
  for (const item of [node, fixed]) expect(portable.nodes.find(candidate => candidate.title === item.title)!.style?.contentSizing)
    .toBe(saved.nodes.find(candidate => candidate.id === item.id)!.style?.contentSizing);
  await writeFile(info.outputPath('content-sizing-preserved-offline.html'), html);
  for (const invalid of ['stretch', '', null, true, 42]) {
    const candidate = structuredClone(saved); (candidate.nodes.find(item => item.id === node.id)!.style as any).contentSizing = invalid;
    const rejected = await request.put(`/api/projects/${initial.id}`, { data: { project: candidate, expectedRevision: saved.revision } });
    expect(rejected.status(), `Invalid content sizing ${JSON.stringify(invalid)} is rejected`).toBe(400);
  }
  expect(await (await request.get(`/api/projects/${initial.id}`)).json()).toEqual(saved);
});

test('expanding a nested ordered container grows fixed ancestors and peers, preserves the camera, and survives reload and offline viewing', async ({ page, request, browser }, info) => {
  const project = newProject('Nested disclosure growth'), canvas = project.canvases[0]!;
  canvas.snapping = { stickyEdges: false, smartAlign: false, snapToGrid: false };
  const outer = addNode(project, canvas.id, 'container', 'Collection');
  Object.assign(outer, { x: 80, y: 70, width: 460, height: 170, sizeMode: 'fixed', collapsed: false, childLayout: 'contained', containerVariant: 'horizontal',
    grid: { columns: 2, rows: 1, cellWidth: 220, cellHeight: 120, gap: 0, showHeaders: false, fillOrder: 'row' } });
  const nested = addNode(project, canvas.id, 'container', 'Three detail layers');
  Object.assign(nested, { parentId: outer.id, width: 220, height: 120, sizeMode: 'fixed', collapsed: true, childLayout: 'contained', containerVariant: 'horizontal',
    grid: { columns: 3, rows: 1, cellWidth: 220, cellHeight: 150, gap: 0, showHeaders: false, fillOrder: 'row' }, childPosition: 'auto' });
  const peer = addNode(project, canvas.id, 'media', 'Related object');
  Object.assign(peer, { parentId: outer.id, width: 220, height: 120, sizeMode: 'fixed', contentType: 'text', text: 'The ordered peer keeps the same tile dimensions.', childPosition: 'auto' });
  const leaves = Array.from({ length: 3 }, (_, at) => {
    const leaf = addNode(project, canvas.id, 'media', `Layer ${at + 1}`);
    Object.assign(leaf, { parentId: nested.id, width: 220, height: 150, sizeMode: 'fixed', contentType: 'text', text: `Detail for layer ${at + 1}`, childPosition: 'auto' }); return leaf;
  });
  const created = await request.post('/api/projects', { data: commitSnapshots(project) });
  expect(created.ok(), await created.text()).toBeTruthy(); const initial: MeridianProject = await created.json();
  const tile = (page: Page, id: string) => page.locator(`.ms-node[data-node-id="${id}"]`);
  const dimensions = (page: Page, id: string) => tile(page, id).evaluate(element => {
    const card = element as HTMLElement; return { x: parseFloat(card.style.left), y: parseFloat(card.style.top), width: parseFloat(card.style.width), height: parseFloat(card.style.height) };
  });
  const read = async (): Promise<MeridianProject> => (await request.get(`/api/projects/${initial.id}`)).json();
  const walls = async (page: Page, parentId: string, children: string[]) => {
    const geometry = await page.evaluate(({ parentId, children }) => {
      const frame = document.querySelector<HTMLElement>(`.ms-node[data-node-id="${parentId}"]`)!, rect = frame.getBoundingClientRect();
      return { parent: { x: rect.x, y: rect.y, width: rect.width, height: rect.height }, children: children.map(id => {
        const child = document.querySelector(`.ms-node[data-node-id="${id}"]`)!.getBoundingClientRect(); return { x: child.x, y: child.y, width: child.width, height: child.height };
      }) };
    }, { parentId, children });
    for (const child of geometry.children) {
      expect(child.x).toBeGreaterThanOrEqual(geometry.parent.x - 1); expect(child.y).toBeGreaterThanOrEqual(geometry.parent.y - 1);
      expect(child.x + child.width).toBeLessThanOrEqual(geometry.parent.x + geometry.parent.width + 1);
      expect(child.y + child.height).toBeLessThanOrEqual(geometry.parent.y + geometry.parent.height + 1);
    }
  };
  await page.goto(`/?project=${initial.id}`); await interactionTool(page);
  const before = await dimensions(page, outer.id), camera = await page.locator('.ms-world').getAttribute('style');
  for (const leaf of leaves) await expect(tile(page, leaf.id)).toHaveCount(0);
  await tile(page, nested.id).locator(':scope > .ms-node-header > .ms-node-title').dblclick();
  for (const leaf of leaves) await expect(tile(page, leaf.id)).toHaveCount(1);
  expect(await page.locator('.ms-world').getAttribute('style')).toBe(camera);
  const expanded = await dimensions(page, outer.id); expect(expanded.width > before.width || expanded.height > before.height).toBe(true);
  const nestedFrame = await dimensions(page, nested.id), peerFrame = await dimensions(page, peer.id);
  expect(nestedFrame.width).toBeGreaterThan(600); expect(nestedFrame.width).toBe(peerFrame.width); expect(nestedFrame.height).toBe(peerFrame.height);
  await walls(page, outer.id, [nested.id, peer.id]); await walls(page, nested.id, leaves.map(leaf => leaf.id));
  await expect.poll(async () => (await read()).nodes.find(node => node.id === nested.id)!.collapsed).toBe(false);
  await tile(page, nested.id).locator(':scope > .ms-node-header > .ms-node-title').dblclick();
  for (const leaf of leaves) await expect(tile(page, leaf.id)).toHaveCount(0);
  expect(await dimensions(page, outer.id)).toEqual(expanded); expect(await page.locator('.ms-world').getAttribute('style')).toBe(camera);
  await tile(page, nested.id).locator(':scope > .ms-node-header > .ms-node-title').dblclick();
  for (const leaf of leaves) await expect(tile(page, leaf.id)).toHaveCount(1);
  expect(await dimensions(page, outer.id)).toEqual(expanded); expect(await page.locator('.ms-world').getAttribute('style')).toBe(camera);
  await expect(page.locator('.save-state')).toHaveText(/^Saved · r\d+$/); const saved = await read();
  expect(saved.nodes.map(node => node.id).sort()).toEqual(project.nodes.map(node => node.id).sort());
  expect(saved.nodes.find(node => node.id === outer.id)!.sizeMode).toBe('fixed');
  await page.reload(); await walls(page, outer.id, [nested.id, peer.id]); await walls(page, nested.id, leaves.map(leaf => leaf.id));
  const expected = await Promise.all([outer, nested, peer, ...leaves].map(async node => [node.id, await dimensions(page, node.id)] as const));
  await page.getByRole('button', { name: 'Fit Work', exact: true }).click(); await page.screenshot({ path: info.outputPath('nested-disclosure-growth.png') });
  const filename = info.outputPath('nested-disclosure-growth-offline.html');
  await writeFile(filename, buildPublication(saved, await readFile('dist/viewer.js', 'utf8'), css));
  const context = await browser.newContext({ offline: true, viewport: { width: 1500, height: 950 } });
  try {
    const viewer = await context.newPage(), remoteRequests: string[] = [];
    viewer.on('request', request => { if (/^https?:/.test(request.url())) remoteRequests.push(request.url()); });
    await viewer.goto(pathToFileURL(filename).href); const payload = await viewer.locator('#meridian-data').textContent();
    for (const [id, geometry] of expected) expect(await dimensions(viewer, id)).toEqual(geometry);
    await walls(viewer, outer.id, [nested.id, peer.id]); await walls(viewer, nested.id, leaves.map(leaf => leaf.id));
    expect(await viewer.locator('#meridian-data').textContent()).toBe(payload); expect(remoteRequests).toEqual([]);
    await viewer.screenshot({ path: info.outputPath('nested-disclosure-growth-offline.png') });
  } finally { await context.close(); }
});
