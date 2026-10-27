import { test, expect } from '@playwright/test';
import { build } from 'esbuild';
import { readFile } from 'node:fs/promises';
let script: string, css: string;
test.beforeAll(async () => {
  css = await readFile('output/document-performance-20261022/candidate/dist/scene.css', 'utf8');
  const prefix = process.env.MERIDIAN_FIT_PASS_CANDIDATE === '1' ? './output/scale-performance-20261022/candidate/' : './';
  script = (await build({ stdin: { contents: `import {renderScene} from '${prefix}src/scene.ts';import {newProject,addNode} from '${prefix}src/core/model.ts';window.rendererTest={renderScene,newProject,addNode};`, resolveDir: process.cwd(), loader: 'ts' }, bundle: true, write: false, format: 'iife', platform: 'browser', target: 'es2022' })).outputFiles[0]!.text;
});
test.beforeEach(async ({ page }) => {
  await page.route(/^https?:/, route => route.abort());
  await page.setContent('<style>html,body{margin:0}#scene{width:1100px;height:900px}</style><main id="scene"></main>'); await page.addStyleTag({ content: css }); await page.addScriptTag({ content: script });
  await page.evaluate(() => { let id = 0; if (!crypto.randomUUID) Object.defineProperty(crypto, 'randomUUID', { value: () => `00000000-0000-4000-8000-${(++id).toString(16).padStart(12, '0')}` }); });
});
test('resize edges remain ten screen pixels after zoom and interior dragging stays a move', async ({ page }) => {
  for (const [initial, target, inset, action] of [[.01, .5, 22, 'move'], [.01, .5, 5, 'resize'], [1, .1, 5, 'resize']] as const) {
    await page.evaluate(initial => {
      const api = (window as any).rendererTest, project = api.newProject('Zoom edges'), node = api.addNode(project, project.entryCanvasId, 'media', 'Zoom edge test');
      Object.assign(node, { x: 0, y: 0, width: 1000, height: 800, sizeMode: 'fixed', contentType: 'text', text: 'Ordinary text', style: { shape: 'rectangle', padding: 5 } });
      const state = { project, node, moves: 0, resizes: 0, view: { x: 100, y: 100, scale: initial } }; (window as any).rendererState = state;
      api.renderScene(document.querySelector('#scene'), project, project.entryCanvasId, { view: state.view, selection: node.id, onViewChange: (view: any) => state.view = view, onMove: () => state.moves++, onMoveSelection: () => state.moves++, onResize: () => state.resizes++ });
    }, initial);
    await page.mouse.move(100, 100); await page.mouse.wheel(0, -Math.log(target / initial) / .0013);
    await expect.poll(() => page.evaluate(() => (window as any).rendererState.view.scale)).toBeCloseTo(target, 6);
    const rect = await page.locator('.ms-node').boundingBox(); expect(rect).not.toBeNull();
    const x = rect!.x + rect!.width / 2, y = rect!.y + inset;
    await page.mouse.move(x, y); await page.mouse.down(); await page.mouse.move(x + 30, y + 25, { steps: 3 }); await page.mouse.up();
    const counts = await page.evaluate(() => ({ moves: (window as any).rendererState.moves, resizes: (window as any).rendererState.resizes }));
    expect(counts).toEqual(action === 'move' ? { moves: 1, resizes: 0 } : { moves: 0, resizes: 1 });
  }
});

test('fresh renderer passes preserve editing, model replacement, styles and child disclosure', async ({ page }) => {
  const result = await page.evaluate(() => {
    const api = (window as any).rendererTest, project = api.newProject('Pass changes'), parent = api.addNode(project, project.entryCanvasId, 'media', 'Branch'), child = api.addNode(project, project.entryCanvasId, 'media', 'Child');
    Object.assign(parent, { x: 40, y: 60, width: 300, height: 160, sizeMode: 'fixed', contentType: 'text', text: 'Original passage', collapsed: true, childLayout: 'spatial', style: { shape: 'rectangle', titleSize: 18 } });
    Object.assign(child, { parentId: parent.id, x: 400, y: 60, width: 250, height: 150, sizeMode: 'fixed', contentType: 'text', text: 'Child passage' });
    const host = document.querySelector('#scene'), snapshots: any[] = [], state = { edits: 0, toggles: 0 }; (window as any).rendererState = state;
    const render = (model: any, mode = 'select') => { api.renderScene(host, model, model.entryCanvasId, { authoringMode: mode, view: { x: 0, y: 0, scale: 1 }, onTextEdit: () => state.edits++, onToggle: () => state.toggles++ });
      snapshots.push([...document.querySelectorAll('.ms-node')].map((element: any) => ({ id: element.dataset.nodeId, fit: element.dataset.contentFit, text: element.textContent, styles: [...element.querySelectorAll('.ms-node-header,.ms-node-body')].map((surface: any) => Object.fromEntries([...surface.style].sort().map(property => [property, surface.style.getPropertyValue(property)]))) }))); };
    render(project); const original = structuredClone(project); parent.text = 'Changed text '.repeat(25); parent.style.titleSize = 30; render(project);
    render(original); const restored = snapshots.at(-1); render(original); const repeated = snapshots.at(-1);
    original.nodes.find((node: any) => node.id === parent.id).collapsed = false; render(original); const expandedCount = document.querySelectorAll('.ms-node').length;
    render(original, 'text'); return { first: snapshots[0], restored, repeated, expandedCount, titleEditable: document.querySelector(`[data-node-id="${parent.id}"] .ms-node-title`)?.getAttribute('data-editable-text') };
  });
  expect(result.restored).toEqual(result.first); expect(result.repeated).toEqual(result.first); expect(result.expandedCount).toBe(2); expect(result.titleEditable).toBe('title');
  await page.locator('[data-editable-text=title]').first().click(); expect(await page.evaluate(() => (window as any).rendererState.edits)).toBe(1);
});


test('camera changes keep the dedicated Work background and avoid inherited grid-color writes', async ({ page }) => {
  await page.evaluate(() => {
    const api = (window as any).rendererTest, project = api.newProject('Background camera'), node = api.addNode(project, project.entryCanvasId, 'media', 'Foreground');
    project.canvases[0].style = {backgroundPattern: 'dots', backgroundTint: '#D17A35', backgroundScale: 36, backgroundOpacity: .7, grid: '#D17A35'};
    Object.assign(node, {x: 40, y: 60, width: 300, height: 180, sizeMode: 'fixed'});
    const state = {view: {x: 70, y: 90, scale: 1}}; (window as any).rendererState = state;
    api.renderScene(document.querySelector('#scene'), project, project.entryCanvasId, {view: state.view, onViewChange: (view: any) => state.view = view});
  });
  const read = () => page.evaluate(() => {
    const viewport = document.querySelector<HTMLElement>('.ms-viewport')!, background = document.querySelector<HTMLElement>('.ms-canvas-background')!;
    return {view: {...(window as any).rendererState.view}, viewportImage: getComputedStyle(viewport).backgroundImage, pattern: background.dataset.backgroundPattern,
      image: background.style.backgroundImage, size: background.style.backgroundSize, position: background.style.backgroundPosition, opacity: Number(background.style.opacity),
      inheritedGrid: getComputedStyle(document.querySelector('.ms-world')!).getPropertyValue('--ms-grid-color').trim()};
  });
  const initial = await read(); await page.mouse.move(700, 600); await page.mouse.wheel(0, 520);
  await expect.poll(async () => (await read()).view.scale).toBeLessThan(1); const zoomed = await read();
  expect(initial.viewportImage).toBe('none'); expect(zoomed.viewportImage).toBe('none'); expect(initial.inheritedGrid).toBe(''); expect(zoomed.inheritedGrid).toBe('');
  expect(initial.pattern).toBe('dots'); expect(zoomed.pattern).toBe('dots'); expect(zoomed.image).toBe(initial.image); expect(initial.image).toContain('209, 122, 53');
  expect(zoomed.size).not.toBe(initial.size); expect(zoomed.position).not.toBe(initial.position); expect(zoomed.opacity).toBeLessThan(initial.opacity);
  await page.mouse.down(); await page.mouse.move(750, 640, {steps: 3}); await page.mouse.up(); const panned = await read();
  expect(panned.image).toBe(initial.image); expect(panned.size).toBe(zoomed.size); expect(panned.position).not.toBe(zoomed.position); expect(panned.opacity).toBe(zoomed.opacity); expect(panned.inheritedGrid).toBe('');
});
