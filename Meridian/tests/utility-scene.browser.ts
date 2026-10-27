import { test, expect, type Page } from '@playwright/test';
import { build } from 'esbuild';
import { readFile } from 'node:fs/promises';
import { addNode, newProject } from '../src/core/model.ts';
import { gridGeometry, reflowContainedChildren } from '../src/core/grid.ts';
import type { MeridianProject } from '../src/core/types.ts';

let script: string, css: string;
test.beforeAll(async () => {
  css = await readFile('dist/scene.css', 'utf8');
  script = (await build({ stdin: { contents: `import {renderScene} from './src/scene.ts';window.utilityHarness={renderScene};`, resolveDir: process.cwd(), loader: 'ts' }, bundle: true, write: false, format: 'iife', platform: 'browser', target: 'es2022' })).outputFiles[0]!.text;
});

async function fixture(page: Page, draft = false) {
  const project = newProject('Utility routing'), canvasId = project.entryCanvasId;
  const a = addNode(project, canvasId, 'text', 'Start'), b = addNode(project, canvasId, 'text', 'Finish');
  Object.assign(a, { x: 90, y: 100, width: 180, height: 80, sizeMode: 'fixed', placement: 'free', childLayout: 'spatial' });
  Object.assign(b, { x: 900, y: 100, width: 180, height: 80, sizeMode: 'fixed', placement: 'free', childLayout: 'spatial' });
  const pivot = addNode(project, canvasId, 'junction', 'Pivot'), joint = addNode(project, canvasId, 'junction', 'Joint');
  Object.assign(pivot, { utilityVariant: 'pivot', x: 380, y: 150, width: 12, height: 12, placement: 'free', snapping: { snapToGrid: false, stickyEdges: false, smartAlign: false }, style: { shape: 'circle', fill: '#E67E22', border: '#203040', borderWidth: 1 } });
  Object.assign(joint, { utilityVariant: 'joint', x: 600, y: 150, width: 30, height: 6, placement: 'free', style: { shape: 'rectangle', fill: '#B678EF', borderWidth: 0 } });
  project.links.push(
    { id: 'first-leg', canvasId, sourceId: a.id, targetId: pivot.id, kind: 'visual', relation: 'mutual', direction: 'both', style: { color: '#E67E22', width: 6, lineStyle: 'dotted' } },
    { id: 'middle-leg', canvasId, sourceId: pivot.id, targetId: joint.id, kind: 'visual', relation: 'mutual', direction: 'both', style: { color: '#B678EF', width: 4, lineStyle: 'dashed' } },
    { id: 'last-leg', canvasId, sourceId: joint.id, targetId: b.id, kind: 'visual', relation: 'mutual', direction: 'both', style: { color: '#67DCC6', width: 2, lineStyle: 'solid' } },
    { id: 'bent-route', canvasId, sourceId: a.id, targetId: b.id, kind: 'visual', relation: 'generic', direction: 'none', points: [{ x: 350, y: 280 }, { x: 550, y: 280 }], style: { lineStyle: 'solid' } },
  );
  await page.setContent('<style>html,body{margin:0}#scene{width:1300px;height:850px}</style><div id="scene"></div>');
  await page.addStyleTag({ content: css }); await page.addScriptTag({ content: script });
  await page.evaluate(({ project, sourceId, draft }) => {
    const state: any = { project, selections: [], moves: [], toggles: [], authors: [], linkSelections: [], contexts: [], view: { x: 20, y: 20, scale: 1 }, mode: draft ? 'link' : 'hand', draft: draft ? { sourceId, points: [{ x: 380, y: 350 }, { x: 550, y: 350 }] } : undefined };
    const render = () => (window as any).utilityHarness.renderScene(document.getElementById('scene'), project, project.entryCanvasId, {
      view: state.view, selections: state.selections, selection: state.linkSelection, authoringMode: state.mode, linkDraft: state.draft, readonly: state.readonly,
      onViewChange(view: any) { state.view = view; },
      onSelections(ids: string[]) { state.selections = ids; state.linkSelection = undefined; render(); },
      onMoveSelection(ids: string[], dx: number, dy: number) { state.moves.push({ ids, dx, dy }); for (const id of ids) { const node = project.nodes.find(node => node.id === id)!; node.x += dx; node.y += dy; } render(); },
      onToggle(id: string, field: string, value: boolean) { state.toggles.push({ id, field, value }); },
      onAuthorClick(target: any) { state.authors.push(target); },
      onLinkSelect(id: string, point: any, segmentIndex: number) { state.linkSelections.push({ id, point, segmentIndex }); },
      onContextMenu(target: any) { state.contexts.push(target); },
    });
    (window as any).utilityTest = { state, render }; render();
  }, { project, sourceId: a.id, draft });
  return { a, b, pivot, joint, project };
}
const state = (page: Page) => page.evaluate(() => structuredClone((window as any).utilityTest.state));
const card = (page: Page, id: string) => page.locator(`.ms-node[data-node-id="${id}"]`);
const line = (page: Page, id: string) => page.locator(`.ms-link-line[data-link-id="${id}"]`);

test('utility shape, color and size match selection geometry; dragging moves both joined paths and omits internal arrows', async ({ page }, info) => {
  const { pivot, joint } = await fixture(page);
  await expect(card(page, pivot.id)).toHaveAttribute('data-utility-variant', 'pivot');
  await expect(card(page, joint.id)).toHaveAttribute('data-utility-variant', 'joint');
  await expect(card(page, pivot.id).locator('.ms-node-header')).toBeHidden();
  await expect(card(page, joint.id).locator('.ms-node-body')).toBeHidden();
  expect(await card(page, pivot.id).evaluate(node => ({ width: (node as HTMLElement).style.width, height: (node as HTMLElement).style.height, fill: (node as HTMLElement).style.getPropertyValue('--ms-fill') }))).toEqual({ width: '12px', height: '12px', fill: '#E67E22' });
  expect(await card(page, joint.id).evaluate(node => ({ width: (node as HTMLElement).style.width, height: (node as HTMLElement).style.height }))).toEqual({ width: '30px', height: '6px' });
  expect(await line(page, 'first-leg').evaluate(node => ({ color: getComputedStyle(node).stroke, width: getComputedStyle(node).strokeWidth, dash: getComputedStyle(node).strokeDasharray, cap: getComputedStyle(node).strokeLinecap }))).toEqual({ color: 'rgb(230, 126, 34)', width: '6px', dash: '0px, 18px', cap: 'round' });
  expect(await line(page, 'middle-leg').evaluate(node => getComputedStyle(node).strokeDasharray)).toBe('7px, 5px');
  expect(await line(page, 'last-leg').evaluate(node => getComputedStyle(node).strokeDasharray)).toBe('none');
  await page.evaluate(() => { const fixture = (window as any).utilityTest; fixture.state.linkSelection = 'first-leg'; fixture.state.project.links[0].style.color = '#CC3366'; fixture.state.project.links[0].style.width = 9; fixture.render(); });
  expect(await line(page, 'first-leg').evaluate(node => ({ color: getComputedStyle(node).stroke, width: getComputedStyle(node).strokeWidth, glow: getComputedStyle(node).filter }))).toMatchObject({ color: 'rgb(204, 51, 102)', width: '9px' });
  expect(await line(page, 'first-leg').evaluate(node => getComputedStyle(node).filter)).toContain('drop-shadow');
  expect(await page.locator('.ms-link-hit[data-link-id="first-leg"]').evaluate(node => getComputedStyle(node).strokeWidth)).toBe('17px');
  const largeArrowId = (await line(page, 'first-leg').getAttribute('marker-start'))!.slice(5, -1);
  await expect(page.locator(`marker[id="${largeArrowId}"]`)).toHaveAttribute('markerUnits', 'userSpaceOnUse');
  await expect(page.locator(`marker[id="${largeArrowId}"]`)).toHaveAttribute('markerWidth', '28');
  const normalArrowId = (await line(page, 'last-leg').getAttribute('marker-end'))!.slice(5, -1);
  await expect(page.locator(`marker[id="${normalArrowId}"]`)).toHaveAttribute('markerWidth', '14');
  expect(await line(page, 'first-leg').getAttribute('marker-end')).toBeNull();
  expect(await line(page, 'middle-leg').getAttribute('marker-end')).toBeNull(); expect(await line(page, 'middle-leg').getAttribute('marker-start')).toBeNull();
  expect(await line(page, 'last-leg').getAttribute('marker-start')).toBeNull();
  await expect(page.locator('.ms-arrow-hit')).toHaveCount(2);
  await card(page, pivot.id).dblclick(); expect((await state(page)).toggles).toEqual([]);
  await expect(card(page, pivot.id)).toHaveAttribute('aria-selected', 'true');
  await expect(card(page, pivot.id).locator('.ms-node-outline path')).toHaveAttribute('d', 'M0,6a6,6 0 1 0 12,0a6,6 0 1 0 -12,0Z');
  const before = await line(page, 'first-leg').getAttribute('d');
  await page.mouse.move(406, 176); await page.mouse.down(); await page.mouse.move(446, 236, { steps: 5 });
  expect(await line(page, 'first-leg').getAttribute('d')).toContain('L426,216');
  expect(await line(page, 'middle-leg').getAttribute('d')).toContain('M426,216');
  await page.mouse.up(); expect((await state(page)).moves).toEqual([{ ids: [pivot.id], dx: 40, dy: 60 }]);
  expect(await line(page, 'first-leg').getAttribute('d')).not.toBe(before);
  await page.screenshot({ path: info.outputPath('utility-routing.png') });
  await page.evaluate(() => { const fixture = (window as any).utilityTest; fixture.state.readonly = true; fixture.render(); });
  await page.mouse.move(446, 236); await page.mouse.down(); await page.mouse.move(486, 276); await page.mouse.up();
  expect((await state(page)).moves).toHaveLength(1);
});

test('draft route follows pointer without graph mutations; author clicks project onto the correct segment and drag or hold only pans', async ({ page }) => {
  const { project } = await fixture(page, true);
  await expect(page.locator('.ms-link-draft-pivot')).toHaveCount(2);
  await page.mouse.move(700, 500);
  expect(await page.locator('.ms-link-draft-line').getAttribute('d')).toContain('L680,480');
  expect((await state(page)).project).toEqual(project);
  await page.evaluate(() => { const fixture = (window as any).utilityTest; fixture.state.mode = 'node'; fixture.render(); });
  await page.mouse.click(470, 305);
  const click = (await state(page)).authors.at(-1);
  expect(click.linkId).toBe('bent-route'); expect(click.segmentIndex).toBe(1); expect(click.point).toEqual({ x: 450, y: 280 });
  await page.mouse.move(1100, 700); await page.mouse.down(); await page.mouse.move(1160, 740, { steps: 4 }); await page.mouse.up();
  expect((await state(page)).authors).toHaveLength(1);
  expect((await state(page)).view).toEqual({ x: 80, y: 60, scale: 1 });
  await page.mouse.move(1100, 700); await page.mouse.down(); await page.waitForTimeout(400); await page.mouse.up();
  expect((await state(page)).authors).toHaveLength(1);
  expect((await state(page)).project as MeridianProject).toEqual(project);
});

test('a Utility remains a compact draggable overlay inside a scrolled Window without consuming an ordered cell', async ({ page }) => {
  const fixtureData = await fixture(page), project = fixtureData.project;
  const owner = addNode(project, project.entryCanvasId, 'container', 'Window');
  Object.assign(owner, { x: 100, y: 380, childLayout: 'contained', containerVariant: 'window', collapsed: false,
    grid: { columns: 2, rows: 3, cellWidth: 160, cellHeight: 120, windowColumns: 2, windowRows: 2, scrollDirection: 'vertical' } });
  const tile = addNode(project, project.entryCanvasId, 'text', 'Window target');
  Object.assign(tile, { parentId: owner.id, cell: { column: 1, row: 1 } });
  const pivot = project.nodes.find(node => node.id === fixtureData.pivot.id)!;
  Object.assign(pivot, { parentId: owner.id });
  const surface = gridGeometry(owner, project);
  Object.assign(pivot, { x: surface.left + 220, y: surface.top + 150 });
  project.links.push({ id: 'window-leg', canvasId: project.entryCanvasId, parentId: owner.id, sourceId: pivot.id, targetId: tile.id, kind: 'visual', relation: 'generic', direction: 'none', style: { lineStyle: 'solid' } });
  const arranged = reflowContainedChildren(project, owner.id);
  await page.evaluate(next => {
    const fixture = (window as any).utilityTest;
    fixture.state.project.nodes.splice(0, fixture.state.project.nodes.length, ...next.nodes);
    fixture.state.project.links.splice(0, fixture.state.project.links.length, ...next.links);
    fixture.render();
  }, arranged);
  const pane = page.locator(`.ms-window-viewport[data-window-id="${owner.id}"]`);
  await expect(pane).toBeVisible();
  expect(await card(page, pivot.id).evaluate(node => ({ width: (node as HTMLElement).style.width, height: (node as HTMLElement).style.height }))).toEqual({ width: '12px', height: '12px' });
  expect((await state(page)).project.nodes.find((node: any) => node.id === pivot.id).cell).toBeUndefined();
  const unscrolledRoute = await line(page, 'window-leg').getAttribute('d');
  await pane.evaluate(node => { node.scrollTop = 60; });
  await expect.poll(() => pane.evaluate(node => node.scrollTop)).toBe(60);
  await expect(line(page, 'window-leg')).not.toHaveAttribute('d', unscrolledRoute!);
  const shaft = await line(page, 'window-leg').evaluate(node => { const path = node as SVGPathElement; const point = path.getPointAtLength(path.getTotalLength() * .3); return { x: point.x, y: point.y }; });
  const clickAt = { x: shaft.x + 20, y: shaft.y + 20 };
  const route = await line(page, 'window-leg').evaluate(node => {
    const path = node as SVGPathElement, start = path.getPointAtLength(0), end = path.getPointAtLength(path.getTotalLength());
    return { start: { x: start.x, y: start.y }, end: { x: end.x, y: end.y } };
  });
  await page.evaluate(() => {
    const fixture = (window as any).utilityTest; fixture.nativeEvents = {};
    for (const type of ['pointerdown', 'click', 'contextmenu']) document.addEventListener(type, event => {
      const mouse = event as MouseEvent; fixture.nativeEvents[type] = { x: mouse.clientX, y: mouse.clientY };
    }, true);
  });
  const deliveredPoint = async (type: 'pointerdown' | 'click' | 'contextmenu') => page.evaluate(type => {
    const fixture = (window as any).utilityTest, event = fixture.nativeEvents[type], view = fixture.state.view;
    const viewport = document.querySelector('.ms-viewport')!.getBoundingClientRect();
    return { x: (event.x - viewport.x - view.x) / view.scale, y: (event.y - viewport.y - view.y) / view.scale };
  }, type);
  const projected = (point: { x: number; y: number }) => {
    const dx = route.end.x - route.start.x, dy = route.end.y - route.start.y;
    const at = Math.max(0, Math.min(1, ((point.x - route.start.x) * dx + (point.y - route.start.y) * dy) / (dx * dx + dy * dy)));
    return { x: route.start.x + at * dx, y: route.start.y + at * dy + 60 };
  };
  await page.mouse.click(clickAt.x, clickAt.y);
  expect((await state(page)).linkSelections.at(-1)).toMatchObject({ id: 'window-leg', segmentIndex: 0 });
  // Native MouseEvent clicks can round subpixel input, while pointer events
  // retain fractions. Check the delivered coordinate and its shaft proximity.
  const clicked = await deliveredPoint('click'), selected = (await state(page)).linkSelections.at(-1).point;
  expect(selected.x).toBeCloseTo(clicked.x, 3); expect(selected.y).toBeCloseTo(clicked.y + 60, 3);
  expect(Math.hypot(selected.x - shaft.x, selected.y - shaft.y - 60)).toBeLessThan(1.5);
  await page.evaluate(() => { const fixture = (window as any).utilityTest; fixture.state.mode = 'node'; fixture.render(); });
  await page.mouse.click(clickAt.x, clickAt.y);
  expect((await state(page)).authors.at(-1)).toMatchObject({ linkId: 'window-leg', segmentIndex: 0 });
  expect((await state(page)).authors.at(-1).nodeId).toBeUndefined();
  const inserted = (await state(page)).authors.at(-1).point, expected = projected(await deliveredPoint('pointerdown'));
  expect(inserted.x).toBeCloseTo(expected.x, 3); expect(inserted.y).toBeCloseTo(expected.y, 3);
  await page.mouse.click(clickAt.x, clickAt.y, { button: 'right' });
  expect((await state(page)).contexts.at(-1)).toMatchObject({ linkId: 'window-leg', segmentIndex: 0 });
  const contextPoint = (await state(page)).contexts.at(-1).point, contextExpected = projected(await deliveredPoint('contextmenu'));
  expect(contextPoint.x).toBeCloseTo(contextExpected.x, 3); expect(contextPoint.y).toBeCloseTo(contextExpected.y, 3);
  await page.evaluate(() => { const fixture = (window as any).utilityTest; fixture.state.mode = 'link'; fixture.render(); });
  await page.mouse.click(clickAt.x, clickAt.y);
  expect((await state(page)).authors.at(-1)).toMatchObject({ linkId: 'window-leg', segmentIndex: 0 });
  await page.evaluate(() => { const fixture = (window as any).utilityTest; fixture.state.mode = 'hand'; fixture.render(); });
  const before = await line(page, 'window-leg').getAttribute('d'), box = (await card(page, pivot.id).boundingBox())!;
  await page.mouse.move(box.x + 6, box.y + 6); await page.mouse.down(); await page.mouse.move(box.x + 36, box.y + 26, { steps: 4 });
  expect(await line(page, 'window-leg').getAttribute('d')).not.toBe(before);
  await page.mouse.up();
  expect((await state(page)).moves).toEqual([{ ids: [pivot.id], dx: 30, dy: 20 }]);
  const moved = (await state(page)).project.nodes.find((node: any) => node.id === pivot.id);
  expect(moved.x).toBe(pivot.x + 30); expect(moved.y).toBe(pivot.y + 20); expect(moved.cell).toBeUndefined();
  await expect(pane).toHaveJSProperty('scrollTop', 60);
  const routeBox = await line(page, 'window-leg').boundingBox(); expect(routeBox).not.toBeNull();
});
