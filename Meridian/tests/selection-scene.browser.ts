import { test, expect, type Page } from '@playwright/test';
import { build } from 'esbuild';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { addNode, newProject } from '../src/core/model.ts';

let sceneScript: string, sceneCss: string;
test.beforeAll(async () => {
  sceneCss = await readFile(path.resolve('src/scene.css'), 'utf8');
  const bundled = await build({
    stdin: { contents: `import {renderScene} from './src/scene.ts'; window.sceneHarness={renderScene};`, resolveDir: process.cwd(), loader: 'ts' },
    bundle: true, write: false, format: 'iife', platform: 'browser', target: 'es2022',
  });
  sceneScript = bundled.outputFiles[0].text;
});

const card = (page: Page, id: string) => page.locator(`.ms-node[data-node-id="${id}"]`);
const tileTitle = (page: Page, id: string) => card(page, id).locator('.ms-node-title');
const selection = (page: Page) => page.locator('.ms-node[aria-selected="true"]').evaluateAll(nodes => nodes.map(node => (node as HTMLElement).dataset.nodeId).sort());
const state = (page: Page) => page.evaluate(() => structuredClone((window as any).selectionTest.state));
async function fixture(page: Page, configuration: Record<string, unknown> = {}) {
  await page.setContent('<style>html,body{margin:0;height:100%}#scene{height:850px;width:1300px}</style><div id="scene"></div>');
  await page.addStyleTag({ content: sceneCss }); await page.addScriptTag({ content: sceneScript });
  const project = newProject('Scene selection contract'), canvasId = project.entryCanvasId;
  const ids: Record<string, string> = {};
  const add = (key: string, x: number, y: number, width: number, height: number, parent?: string) => {
    const node = addNode(project, canvasId, 'text', key);
    Object.assign(node, { sizeMode: 'fixed', x, y, width, height, childLayout: 'spatial', text: `${key} content` });
    if (parent) node.parentId = ids[parent]; ids[key] = node.id; return node;
  };
  add('a', 100, 100, 200, 120);
  add('a1', 120, 300, 140, 100, 'a');
  add('b', 420, 100, 180, 120);
  add('c', 700, 100, 180, 120).collapsed = true;
  add('hidden', 20, 30, 160, 100, 'c');
  add('frame', 650, 320, 380, 280).childLayout = 'contained';
  add('inside', 680, 370, 140, 100, 'frame');
  project.links.push({ id: 'a-b', canvasId, sourceId: ids.a, targetId: ids.b, kind: 'visual', relation: 'generic', points: [{ x: 350, y: 60 }] });
  await page.evaluate(({ project, ids, config }) => {
    const { renderScene } = (window as any).sceneHarness, canvasId = project.entryCanvasId;
    const state: any = { selections: [], selectionEvents: [], moves: [], legacyMoves: [], toggles: [], routeEdits: [], resizes: [], view: { x: 20, y: 20, scale: 1 }, ...config };
    const render = () => renderScene(document.getElementById('scene'), project, canvasId, {
      selections: state.selections, selection: state.linkSelection ?? state.selections.at(-1), view: state.view,
      selectionMode: state.selectionMode, regionIntersection: state.regionIntersection, readonly: state.readonly,
      onSelections(ids: string[], anchor: any) { state.selections = ids; state.selectionEvents.push({ ids, anchor }); render(); },
      onMoveSelection(ids: string[], dx: number, dy: number) { state.moves.push({ ids, dx, dy }); render(); },
      onMove(id: string, x: number, y: number) { state.legacyMoves.push({ id, x, y }); },
      onToggle(id: string, field: 'collapsed' | 'closed', value: boolean) { state.toggles.push({ id, field, value }); project.nodes.find(node => node.id === id)![field] = value; render(); },
      onViewChange(view: any) { state.view = view; },
      onResize(id: string, width: number, height: number) { state.resizes.push({ id, width, height }); render(); },
      onRouteEdit(id: string, points: any[]) { state.routeEdits.push({ id, points }); render(); },
    });
    (window as any).selectionTest = { state, ids, project, render }; render();
  }, { project, ids, config: configuration });
  return ids;
}
async function marquee(page: Page, from: [number, number], to: [number, number], shift = false) {
  if (shift) await page.keyboard.down('Shift');
  await page.mouse.move(from[0] + 20, from[1] + 20); await page.mouse.down();
  await page.mouse.move(to[0] + 20, to[1] + 20, { steps: 5 }); await page.mouse.up();
  if (shift) await page.keyboard.up('Shift');
}

test('modifier and keyboard selection survive rerender without accidentally disclosing a branch', async ({ page }) => {
  const ids = await fixture(page);
  await tileTitle(page, ids.a).click(); await tileTitle(page, ids.b).click({ modifiers: ['Control'] });
  expect(await selection(page)).toEqual([ids.a, ids.b].sort());
  await tileTitle(page, ids.a).click({ modifiers: ['Shift'] });
  expect(await selection(page)).toEqual([ids.b]);
  await tileTitle(page, ids.c).dblclick({ modifiers: ['Control'] });
  expect((await state(page)).toggles).toEqual([]); await expect(card(page, ids.hidden)).toHaveCount(0);
  await card(page, ids.a).focus(); await page.keyboard.press('Shift+Space');
  expect(await selection(page)).toEqual([ids.a, ids.b].sort());
  await tileTitle(page, ids.c).dblclick();
  expect((await state(page)).toggles).toHaveLength(1); await expect(card(page, ids.hidden)).toBeVisible();
  expect((await state(page)).selectionEvents[0].anchor).toBeDefined();
  expect((await state(page)).selectionEvents.find((entry: any) => entry.ids.includes(ids.a) && !entry.anchor)).toBeDefined();
});

test('selected branches drag as one normalized union with correct internal route preview and one commit', async ({ page }) => {
  const ids = await fixture(page);
  await tileTitle(page, ids.a).click(); await tileTitle(page, ids.a1).click({ modifiers: ['Control'] }); await tileTitle(page, ids.b).click({ modifiers: ['Control'] });
  const route = page.locator('.ms-link-line[data-link-id="a-b"]'), originalRoute = await route.getAttribute('d');
  const box = (await tileTitle(page, ids.a).boundingBox())!;
  await page.keyboard.down('Alt'); await page.mouse.move(box.x + 20, box.y + 8); await page.mouse.down();
  await page.mouse.move(box.x + 85, box.y + 53, { steps: 5 });
  for (const id of [ids.a, ids.a1, ids.b]) expect(await card(page, id).evaluate(node => (node as HTMLElement).style.transform)).toBe('translate(65px, 45px)');
  expect(await route.getAttribute('d')).toContain('L415,105');
  expect(await route.getAttribute('d')).not.toBe(originalRoute);
  await page.mouse.up(); await page.keyboard.up('Alt');
  expect((await state(page)).moves).toEqual([{ ids: [ids.a, ids.b], dx: 65, dy: 45 }]);
  expect((await state(page)).legacyMoves).toEqual([]);
  expect(await selection(page)).toEqual([ids.a, ids.a1, ids.b].sort());
  await card(page, ids.a).focus(); await page.keyboard.press('ArrowRight');
  expect((await state(page)).moves.at(-1)).toEqual({ ids: [ids.a, ids.b], dx: 5, dy: 0 });
});

test('region selection uses visible geometry, supports both policies, and shift adds without changing the camera', async ({ page }) => {
  const ids = await fixture(page, { selectionMode: 'region' });
  await marquee(page, [70, 70], [620, 250]);
  expect(await selection(page)).toEqual([ids.a, ids.b].sort());
  expect((await state(page)).view).toEqual({ x: 20, y: 20, scale: 1 });
  await marquee(page, [610, 280], [840, 500]);
  expect(await selection(page)).toEqual([ids.inside]);
  await page.evaluate(() => { const test = (window as any).selectionTest; test.state.regionIntersection = 'intersecting'; test.render(); });
  await marquee(page, [610, 280], [840, 500]);
  expect(await selection(page)).toEqual([ids.frame, ids.inside].sort());
  await page.evaluate(() => { const test = (window as any).selectionTest; test.state.selectionMode = 'pan'; test.state.regionIntersection = 'contained'; test.render(); });
  await marquee(page, [70, 70], [620, 250], true);
  expect(await selection(page)).toEqual([ids.a, ids.b, ids.frame, ids.inside].sort());
  await expect(card(page, ids.hidden)).toHaveCount(0);
  await page.mouse.click(1100, 720); expect(await selection(page)).toEqual([]);
});

test('escape rolls back active marquee, group move, camera pan, resize and route gestures', async ({ page }) => {
  const ids = await fixture(page);
  // Host-level Escape normally clears selection; an active gesture must consume
  // it first, retaining the selection while abandoning only its preview.
  await page.evaluate(() => document.addEventListener('keydown', event => {
    if (event.key === 'Escape') { const test = (window as any).selectionTest; test.state.selections = []; test.render(); }
  }));
  await tileTitle(page, ids.a).click(); await tileTitle(page, ids.b).click({ modifiers: ['Control'] });
  await page.keyboard.down('Shift'); await page.mouse.move(80, 80); await page.mouse.down(); await page.mouse.move(650, 280);
  await expect(page.locator('.ms-selection-region')).toBeVisible();
  await page.keyboard.press('Escape'); await page.mouse.up(); await page.keyboard.up('Shift');
  await expect(page.locator('.ms-selection-region')).toHaveCount(0);
  expect(await selection(page)).toEqual([ids.a, ids.b].sort());
  const box = (await tileTitle(page, ids.a).boundingBox())!;
  await page.mouse.move(box.x + 20, box.y + 8); await page.mouse.down(); await page.mouse.move(box.x + 80, box.y + 60);
  await page.keyboard.press('Escape'); await page.mouse.up();
  expect((await state(page)).moves).toEqual([]);
  expect(await selection(page)).toEqual([ids.a, ids.b].sort());
  expect(await card(page, ids.a).evaluate(node => (node as HTMLElement).style.transform)).toBe('');
  await page.mouse.move(1150, 720); await page.mouse.down(); await page.mouse.move(1200, 780);
  await page.keyboard.press('Escape'); await page.mouse.up();
  expect((await state(page)).view).toEqual({ x: 20, y: 20, scale: 1 });
  const resize = (await card(page, ids.b).boundingBox())!;
  await page.mouse.move(resize.x + resize.width, resize.y + resize.height / 2); await page.mouse.down(); await page.mouse.move(resize.x + resize.width + 75, resize.y + resize.height / 2 + 55);
  await page.keyboard.press('Escape'); await page.mouse.up(); expect((await state(page)).resizes).toEqual([]);
  expect(await card(page, ids.b).evaluate(node => (node as HTMLElement).style.width)).toBe('180px');
  await page.evaluate(() => { const test = (window as any).selectionTest; test.state.linkSelection = 'a-b'; test.render(); });
  const handle = page.locator('.ms-route-handle'); const handleBox = (await handle.boundingBox())!;
  await page.mouse.move(handleBox.x + 6, handleBox.y + 6); await page.mouse.down(); await page.mouse.move(handleBox.x + 75, handleBox.y + 50);
  await page.keyboard.press('Escape'); await page.mouse.up();
  expect((await state(page)).routeEdits).toEqual([]); await expect(handle).toHaveAttribute('cx', '350');
});

test('read-only scenes allow multi selection and regions while blocking drag mutation', async ({ page }) => {
  const ids = await fixture(page, { readonly: true });
  await tileTitle(page, ids.a).click(); await tileTitle(page, ids.b).click({ modifiers: ['Control'] });
  const box = (await tileTitle(page, ids.a).boundingBox())!;
  await page.mouse.move(box.x + 20, box.y + 8); await page.mouse.down(); await page.mouse.move(box.x + 90, box.y + 65); await page.mouse.up();
  expect((await state(page)).moves).toEqual([]); expect((await state(page)).toggles).toEqual([]);
  expect(await selection(page)).toEqual([ids.a, ids.b].sort());
  await marquee(page, [610, 280], [840, 500], true);
  expect(await selection(page)).toEqual([ids.a, ids.b, ids.inside].sort());
  await expect(page.locator('.ms-resize')).toHaveCount(0);
});
