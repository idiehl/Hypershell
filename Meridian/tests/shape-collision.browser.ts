import { test, expect, type Page } from '@playwright/test';
import { build } from 'esbuild';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { addNode, newProject } from '../src/core/model.ts';

let script: string, css: string;
test.beforeAll(async () => {
  css = await readFile(path.resolve('src/scene.css'), 'utf8');
  const bundle = await build({ stdin: { contents: `import {renderScene} from './src/scene.ts';window.geometryHarness={renderScene};`, resolveDir: process.cwd(), loader: 'ts' }, bundle: true, write: false, format: 'iife', platform: 'browser', target: 'es2022' });
  script = bundle.outputFiles[0].text;
});
const card = (page: Page, id: string) => page.locator(`.ms-node[data-node-id="${id}"]`);
async function setup(page: Page) {
  await page.setContent('<style>html,body{margin:0;height:100%}#scene{width:1300px;height:850px}</style><div id="scene"></div>');
  await page.addStyleTag({ content: css }); await page.addScriptTag({ content: script });
  const project = newProject('Backdrop geometry'), canvasId = project.entryCanvasId;
  const a = addNode(project, canvasId, 'text', 'Moving rectangle'); Object.assign(a, { sizeMode: 'fixed', x: 100, y: 100, width: 150, height: 100, text: 'Body', style: { shape: 'rectangle' } });
  const b = addNode(project, canvasId, 'text', 'Stationary rectangle'); Object.assign(b, { sizeMode: 'fixed', x: 300, y: 100, width: 150, height: 100, style: { shape: 'rectangle' } });
  const corner = addNode(project, canvasId, 'text', 'Corner tile'); Object.assign(corner, { sizeMode: 'fixed', x: 760, y: 100, width: 80, height: 36, style: { shape: 'rectangle' } });
  const diamond = addNode(project, canvasId, 'text', 'Diamond'); Object.assign(diamond, { sizeMode: 'fixed', x: 600, y: 100, width: 200, height: 200, style: { shape: 'diamond' } });
  const circle = addNode(project, canvasId, 'text', 'Circle'); Object.assign(circle, { sizeMode: 'fixed', x: 850, y: 100, width: 200, height: 100, style: { shape: 'circle' } });
  const legacy = addNode(project, canvasId, 'image', 'Legacy self links'); Object.assign(legacy, { sizeMode: 'fixed', x: 180, y: 380, width: 180, height: 180, collapsed: false });
  project.links.push(...['legacy-a', 'legacy-b'].map(id => ({ id, canvasId, sourceId: legacy.id, targetId: legacy.id, kind: 'visual' as const, relation: 'causal' as const, sourceAnchor: { x: 0, y: .55 }, targetAnchor: { x: 0, y: .55 } })));
  await page.evaluate(({ project, ids }) => {
    const state: any = { selection: null, moves: [], resizes: [], links: [], view: { x: 20, y: 20, scale: 1 } };
    const render = () => (window as any).geometryHarness.renderScene(document.getElementById('scene'), project, project.entryCanvasId, {
      selection: state.selection, view: state.view,
      onSelection(id: string) { state.selection = id; render(); },
      onMove(id: string, x: number, y: number) { state.moves.push({ id, x, y }); Object.assign(project.nodes.find(node => node.id === id)!, { x, y }); render(); },
      onResize(id: string, width: number, height: number, x: number, y: number) { state.resizes.push({ id, width, height, x, y }); Object.assign(project.nodes.find(node => node.id === id)!, { x, y, width, height }); render(); },
      onLinkSelect(id: string) { state.links.push(id); state.selection = id; render(); },
      onViewChange(view: any) { state.view = view; },
    });
    (window as any).geometryTest = { state, project, ids, render }; render();
  }, { project, ids: { a: a.id, b: b.id, diamond: diamond.id, circle: circle.id, legacy: legacy.id, corner: corner.id } });
  return { a: a.id, b: b.id, diamond: diamond.id, circle: circle.id, legacy: legacy.id, corner: corner.id };
}

test('selected outline follows diamond/circle backdrop and no tile link or resizing chrome remains', async ({ page }) => {
  const ids = await setup(page);
  await card(page, ids.diamond).locator('.ms-node-title').click();
  const diamond = card(page, ids.diamond);
  await expect(diamond.locator('.ms-node-outline')).toBeVisible();
  await expect(diamond.locator('.ms-node-outline path')).toHaveAttribute('d', 'M100,0 L200,100 L100,200 L0,100Z');
  expect(await diamond.evaluate(node => getComputedStyle(node).outlineStyle)).toBe('none');
  await card(page, ids.circle).locator('.ms-node-title').click();
  await expect(card(page, ids.circle).locator('.ms-node-outline path')).toHaveAttribute('d', /M50,50a50,50/);
  await expect(page.locator('.ms-child-count,.ms-hidden-count,.ms-collapse,.ms-resize')).toHaveCount(0);
});

test('drag and keyboard movement stop at peer contact even when snapping is bypassed', async ({ page }) => {
  const ids = await setup(page);
  await page.keyboard.down('Alt'); await page.mouse.move(190, 180); await page.mouse.down(); await page.mouse.move(315, 180, { steps: 5 }); await page.mouse.up(); await page.keyboard.up('Alt');
  const moved = await page.evaluate(() => (window as any).geometryTest.state.moves.at(-1));
  expect(moved.id).toBe(ids.a); expect(moved.x).toBe(150); expect(moved.y).toBe(100);
  await card(page, ids.a).focus(); await page.keyboard.press('ArrowRight');
  expect(await page.evaluate(() => (window as any).geometryTest.project.nodes.find((node: any) => node.id === (window as any).geometryTest.ids.a).x)).toBe(150);
});

test('transparent polygon corners pass clicks to neighbors and empty canvas', async ({ page }) => {
  const ids = await setup(page);
  await card(page, ids.corner).locator('.ms-node-title').click();
  await expect(card(page, ids.corner)).toHaveAttribute('aria-selected', 'true');
  await page.mouse.click(640, 135); // outside the diamond, inside its DOM box
  await expect(page.locator('.ms-node[aria-selected="true"]')).toHaveCount(0);
});

test('selected real edges resize, respect collision and support origin changes', async ({ page }) => {
  const ids = await setup(page);
  await card(page, ids.a).locator('.ms-node-title').click();
  await page.mouse.move(269, 170); expect(await card(page, ids.a).evaluate(node => getComputedStyle(node).cursor)).toBe('ew-resize');
  await page.mouse.down(); await page.mouse.move(470, 170, { steps: 5 }); await page.mouse.up();
  const resize = await page.evaluate(() => (window as any).geometryTest.state.resizes.at(-1));
  expect(resize.width).toBeCloseTo(200, 5); expect(resize.x).toBe(100);
  await page.mouse.move(120, 170); await page.mouse.down(); await page.mouse.move(90, 170, { steps: 3 }); await page.mouse.up();
  const west = await page.evaluate(() => (window as any).geometryTest.state.resizes.at(-1));
  expect(west.x).toBe(70); expect(west.x + west.width).toBeCloseTo(300, 5);
});

test('ordinary spatial children stop at parent edges while bounded Freeform content moves inside its frame', async ({ page }) => {
  await setup(page);
  const ids = await page.evaluate(() => {
    const harness = (window as any).geometryTest, project = harness.project, root = project.nodes.find((node: any) => node.kind === 'canvas');
    project.nodes = [root]; project.links = [];
    const create = (id: string, title: string, x: number, y: number, width: number, height: number, parentId = root.id, childLayout = 'spatial') => {
      // Only Container/Spreadsheet/Module own bounded child workspaces. An
      // ordinary text/Media node reveals children outside its content tile.
      project.nodes.push({ id, title, x, y, width, height, parentId, childLayout, canvasId: project.entryCanvasId,
        kind: childLayout === 'contained' ? 'container' : 'text', ...(childLayout === 'contained' ? { containerVariant: 'contained' } : {}),
        sizeMode: 'fixed', text: 'Body', collapsed: false, style: { shape: 'rectangle' }, snapping: { stickyEdges: false, smartAlign: false, snapToGrid: false } });
      return id;
    };
    const spatial = create('spatial-parent', 'Spatial parent', 100, 100, 250, 180);
    const child = create('spatial-child', 'Spatial child', 380, 140, 120, 90, spatial);
    const frame = create('enclosed-frame', 'Enclosed frame', 600, 100, 300, 300, root.id, 'contained');
    const inside = create('enclosed-child', 'Enclosed child', 640, 180, 120, 90, frame);
    harness.render(); return { spatial, child, frame, inside };
  });
  await page.keyboard.down('Alt');
  await page.mouse.move(430, 182); await page.mouse.down(); await page.mouse.move(270, 182, { steps: 5 }); await page.mouse.up(); await page.keyboard.up('Alt');
  expect(await card(page, ids.child).evaluate(node => Number.parseFloat((node as HTMLElement).style.left))).toBe(350);
  await card(page, ids.child).focus(); await page.keyboard.press('ArrowLeft');
  expect(await card(page, ids.child).evaluate(node => Number.parseFloat((node as HTMLElement).style.left))).toBe(350);
  await card(page, ids.spatial).locator('.ms-node-title').click();
  await page.mouse.move(369, 240); await page.mouse.down(); await page.mouse.move(450, 240, { steps: 5 }); await page.mouse.up();
  expect(await card(page, ids.spatial).evaluate(node => Number.parseFloat((node as HTMLElement).style.width))).toBe(250);
  await page.keyboard.down('Alt');
  await page.mouse.move(690, 222); await page.mouse.down(); await page.mouse.move(725, 252, { steps: 5 }); await page.mouse.up(); await page.keyboard.up('Alt');
  expect(await card(page, ids.inside).evaluate(node => ({ x: Number.parseFloat((node as HTMLElement).style.left), y: Number.parseFloat((node as HTMLElement).style.top) }))).toEqual({ x: 675, y: 210 });
  expect(await card(page, ids.frame).evaluate(node => ({ width: Number.parseFloat((node as HTMLElement).style.width), height: Number.parseFloat((node as HTMLElement).style.height) }))).toEqual({ width: 300, height: 300 });
});

test('legacy zero-length self links render independently selectable exterior loops and arrow targets', async ({ page }) => {
  const ids = await setup(page);
  const loops = page.locator('.ms-link-hit'); await expect(loops).toHaveCount(2);
  const paths = await loops.evaluateAll(nodes => nodes.map(node => node.getAttribute('d')));
  expect(paths[0]).toContain('C'); expect(paths[1]).toContain('C'); expect(paths[0]).not.toEqual(paths[1]);
  const outside = await loops.first().evaluate((node: SVGPathElement) => { const p = node.getPointAtLength(node.getTotalLength() / 2); return { x: p.x + 20, y: p.y + 20 }; });
  await page.mouse.click(outside.x, outside.y);
  expect(await page.evaluate(() => (window as any).geometryTest.state.links.at(-1))).toBe('legacy-a');
  await expect(page.locator('.ms-arrow-hit')).toHaveCount(2);
  const endpoint = await page.locator('.ms-arrow-hit[data-link-id="legacy-b"]').evaluate(node => ({ x: Number(node.getAttribute('cx')) + 14, y: Number(node.getAttribute('cy')) + 20 }));
  await page.mouse.click(endpoint.x, endpoint.y);
  expect(await page.evaluate(() => (window as any).geometryTest.state.links.at(-1))).toBe('legacy-b');
  await expect(card(page, ids.legacy)).toBeVisible();
});
