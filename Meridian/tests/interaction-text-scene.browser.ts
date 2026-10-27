import { test, expect, type Page, type Locator } from '@playwright/test';
import { build } from 'esbuild';
import { readFile } from 'node:fs/promises';
import { addNode, newProject } from '../src/core/model.ts';

let script: string, css: string;
test.beforeAll(async () => {
  css = (await Promise.all(['scene', 'grid', 'gallery-scene', 'window-scene', 'expansion-tray-scene', 'canvas-launcher'].map(name => readFile(`src/${name}.css`, 'utf8')))).join('\n');
  script = (await build({ stdin: { contents: `import {renderScene} from './src/scene.ts';import {moveSelection} from './src/core/selection.ts';window.interactionScene={renderScene,moveSelection};`, resolveDir: process.cwd(), loader: 'ts' }, bundle: true, write: false, format: 'iife', platform: 'browser', target: 'es2022' })).outputFiles[0]!.text;
});
const card = (page: Page, id: string) => page.locator(`.ms-node[data-node-id="${id}"]`);
const state = (page: Page) => page.evaluate(() => structuredClone((window as any).interactionTest.state));
async function mount(page: Page, config: Record<string, unknown> = {}) {
  const project = newProject('Explicit text interaction'), canvasId = project.entryCanvasId;
  project.canvases[0]!.snapping = { stickyEdges: false, smartAlign: false, snapToGrid: false };
  const add = (title: string, x: number, y: number, width = 360, height = 190) => {
    const node = addNode(project, canvasId, 'media', title);
    Object.assign(node, { x, y, width, height, sizeMode: 'fixed', collapsed: true, childLayout: 'spatial', childPosition: 'manual', contentType: 'text', text: 'Alpha beta gamma delta. Text remains directly editable with the Text tool.', style: { shape: 'rectangle', fontFamily: 'calibri', titleSize: 24, fontSize: 22, padding: 10, textAlign: 'left' } });
    return node;
  };
  const branch = add('Branch title', 60, 70), child = add('Child title', 70, 330); child.parentId = branch.id;
  const empty = add('', 700, 70, 300, 170); empty.text = '';
  const gallery = add('Gallery', 60, 600, 390, 260); Object.assign(gallery, { kind: 'container', containerVariant: 'gallery', childLayout: 'contained', collapsed: false, text: undefined });
  const item = add('Gallery child title', 80, 640), item2 = add('Gallery child two', 80, 640); item.parentId = gallery.id; item2.parentId = gallery.id;
  const source = add('Tray source', 700, 390, 190, 170); Object.assign(source, { collapsed: false, expansionTray: { offset: { x: 410, y: 10 }, layoutVersion: 3 } });
  const trayChild = add('Tray child', 1110, 400, 220, 160); trayChild.parentId = source.id;
  const reference = add('Reference title', 700, 680, 250, 170); Object.assign(reference, { kind: 'reference', targetId: branch.id });
  const ids = { branch: branch.id, child: child.id, empty: empty.id, gallery: gallery.id, item: item.id, source: source.id, trayChild: trayChild.id, reference: reference.id };
  await page.setContent('<style>html,body{margin:0}#scene{width:1450px;height:950px}</style><main id="scene"></main>');
  await page.addStyleTag({ content: css }); await page.addScriptTag({ content: script });
  await page.evaluate(({ project, ids, config }) => {
    const api = (window as any).interactionScene;
    const h: any = { project, ids, state: { mode: 'select', selections: [], edits: [], moves: [], resizes: [], toggles: [], authorClicks: [], values: [], opens: [], links: [], routes: [], view: { x: 0, y: 0, scale: 1 }, ...config } };
    h.draw = () => api.renderScene(document.querySelector('#scene'), h.project, h.project.entryCanvasId, {
      view: h.state.view, readonly: h.state.readonly, authoringMode: h.state.mode, selections: h.state.selections,
      onTextEdit(id: string, title: boolean, surface: HTMLElement) { h.state.edits.push({ id, title }); surface.contentEditable = 'true'; surface.setAttribute('role', 'textbox'); },
      onSelections(ids: string[]) { h.state.selections = ids; h.draw(); },
      onMoveSelection(ids: string[], dx: number, dy: number) { h.state.moves.push({ ids, dx, dy }); h.project = api.moveSelection(h.project, ids, dx, dy); h.draw(); },
      onResize(id: string, width: number, height: number, x: number, y: number) { h.state.resizes.push({ id, width, height, x, y }); Object.assign(h.project.nodes.find((n: any) => n.id === id), { x, y, width, height }); h.draw(); },
      onToggle(id: string, field: string, value: boolean) { h.state.toggles.push({ id, field, value }); h.project.nodes.find((n: any) => n.id === id)[field] = value; h.draw(); },
      onAuthorClick(target: any) { h.state.authorClicks.push(target); }, onValue(id: string, value: any) { h.state.values.push({ id, value }); },
      onCanvasNodeOpen(id: string) { h.state.opens.push(id); }, onLinkSelect(id: string) { h.state.links.push(id); }, onRouteEdit(id: string) { h.state.routes.push(id); },
      onTrayMove(id: string, dx: number, dy: number) { h.state.moves.push({ id, dx, dy }); },
      onViewChange(view: any) { h.state.view = view; },
    });
    (window as any).interactionTest = h; h.draw();
  }, { project, ids, config });
  return ids;
}
async function point(surface: Locator, offset = 2) {
  return surface.evaluate((element, offset) => {
    const walker = document.createTreeWalker(element, NodeFilter.SHOW_TEXT); let node: Node | null, rest = offset;
    while ((node = walker.nextNode())) { const length = node.textContent?.length ?? 0; if (rest >= length) { rest -= length; continue; }
      const range = document.createRange(); range.setStart(node, rest); range.setEnd(node, rest + 1); const box = range.getBoundingClientRect(); return { x: box.x + 1, y: box.y + box.height / 2 }; }
    throw new Error('Missing glyph');
  }, offset);
}
async function drag(page: Page, from: { x: number; y: number }, dx: number, dy: number) { await page.mouse.move(from.x, from.y); await page.mouse.down(); await page.mouse.move(from.x + dx, from.y + dy, { steps: 7 }); await page.mouse.up(); }
async function mode(page: Page, value: string) { await page.evaluate(value => { const h = (window as any).interactionTest; h.state.mode = value; h.draw(); }, value); }

test('Single Select selects and drags through visible title and passage without a caret or text selection', async ({ page }, info) => {
  const ids = await mount(page); await expect(page.locator('[data-editable-node-id]')).toHaveCount(0);
  await card(page, ids.branch).locator('.ms-node-title').click(); expect((await state(page)).selections).toEqual([ids.branch]);
  expect((await state(page)).edits).toEqual([]); await expect(page.locator('[contenteditable=true]')).toHaveCount(0);
  await drag(page, await point(card(page, ids.branch).locator('.ms-node-title'), 3), 60, 25);
  expect((await state(page)).moves).toEqual([{ ids: [ids.branch], dx: 60, dy: 25 }]);
  await drag(page, await point(card(page, ids.branch).locator('.ms-text'), 20), 45, 30);
  expect((await state(page)).moves).toHaveLength(2); expect((await state(page)).moves[1]).toEqual({ ids: [ids.branch], dx: 45, dy: 30 });
  expect((await state(page)).edits).toEqual([]); expect(await page.evaluate(() => getSelection()?.toString())).toBe('');
  await page.screenshot({ path: info.outputPath('single-select-title-body-drag.png') });
});

test('Single Select title and passage double clicks disclose the branch and preserve original child frames', async ({ page }) => {
  const ids = await mount(page), before = await page.evaluate(() => structuredClone((window as any).interactionTest.project.nodes));
  await card(page, ids.branch).locator('.ms-node-title').dblclick(); await expect(card(page, ids.child)).toHaveCount(1);
  await card(page, ids.branch).locator('.ms-text').dblclick(); await expect(card(page, ids.child)).toHaveCount(0);
  expect((await state(page)).toggles).toEqual([{ id: ids.branch, field: 'collapsed', value: false }, { id: ids.branch, field: 'collapsed', value: true }]);
  expect((await state(page)).edits).toEqual([]); expect((await state(page)).moves).toEqual([]);
  const after = await page.evaluate(() => (window as any).interactionTest.project.nodes);
  expect(after.map((n: any) => [n.id, n.x, n.y, n.width, n.height])).toEqual(before.map((n: any) => [n.id, n.x, n.y, n.width, n.height]));
});

test('Text alone enables title and body carets, native passage selection, and word double clicks', async ({ page }, info) => {
  const ids = await mount(page, { mode: 'text' }), body = card(page, ids.branch).locator('.ms-text');
  const from = await point(body, 6), to = await point(body, 16); await drag(page, from, to.x - from.x, to.y - from.y);
  await expect(body).toHaveAttribute('contenteditable', 'true'); expect(await page.evaluate(() => getSelection()?.toString())).toContain('beta');
  expect((await state(page)).edits).toEqual([{ id: ids.branch, title: false }]); expect((await state(page)).moves).toEqual([]); expect((await state(page)).resizes).toEqual([]);
  const title = card(page, ids.branch).locator('.ms-node-title'); const word = await point(title, 2); await page.mouse.dblclick(word.x, word.y);
  await expect(title).toHaveAttribute('contenteditable', 'true'); expect((await page.evaluate(() => getSelection()?.toString()))?.trim()).toBe('Branch');
  expect((await state(page)).toggles).toEqual([]); expect((await state(page)).authorClicks).toEqual([]);
  await page.screenshot({ path: info.outputPath('text-tool-native-word-selection.png') });
});

test('an empty title is edited by one Text click and never by Single Select double click', async ({ page }) => {
  const ids = await mount(page); await card(page, ids.empty).dblclick(); expect((await state(page)).edits).toEqual([]); await expect(card(page, ids.empty).locator('.ms-node-title')).toHaveCount(0);
  await mode(page, 'text'); await card(page, ids.empty).click(); const title = card(page, ids.empty).locator('.ms-node-title'); await expect(title).toHaveAttribute('contenteditable', 'true');
  expect((await state(page)).edits).toEqual([{ id: ids.empty, title: true }]); await page.keyboard.type('A visible title'); await expect(title).toHaveText('A visible title');
});

test('Text keeps Gallery edits on the real child identity and mirrored reference bodies read only', async ({ page }) => {
  const ids = await mount(page, { mode: 'text' });
  await card(page, ids.gallery).locator('.ms-gallery-item-title').click(); expect((await state(page)).edits.at(-1)).toEqual({ id: ids.item, title: true });
  await card(page, ids.gallery).locator('.ms-gallery-text').click(); expect((await state(page)).edits.at(-1)).toEqual({ id: ids.item, title: false });
  const referenceBody = card(page, ids.reference).locator('.ms-text'); await expect(referenceBody).not.toHaveAttribute('data-editable-node-id'); await referenceBody.click(); expect((await state(page)).edits).toHaveLength(2);
  expect((await state(page)).moves).toEqual([]); expect((await state(page)).authorClicks).toEqual([]);
});

test('Text ignores node backdrop, tray backdrop, Work background and nontext keyboard actions', async ({ page }) => {
  const ids = await mount(page, { mode: 'text' }); const box = (await card(page, ids.branch).boundingBox())!;
  await drag(page, { x: box.x + box.width / 2, y: box.y + box.height - 8 }, 50, 20);
  const tray = page.locator(`.ms-expansion-tray-backdrop[data-tray-source-id="${ids.source}"]`); await expect(tray).toHaveCount(1);
  const trayBox = (await tray.boundingBox())!; await page.mouse.dblclick(trayBox.x + 3, trayBox.y + 3);
  await page.mouse.click(1370, 900); await card(page, ids.branch).focus(); await page.keyboard.press('ArrowRight'); await page.keyboard.press('Enter'); await page.locator('.ms-viewport').focus(); await page.keyboard.press('Enter');
  const current = await state(page); expect(current.moves).toEqual([]); expect(current.resizes).toEqual([]); expect(current.authorClicks).toEqual([]); expect(current.toggles).toEqual([]); expect(current.edits).toEqual([]);
});

test('no hover name banners remain on collapsed/expanded nodes, ancestors, trays, or the readonly renderer', async ({ page }, info) => {
  for (const readonly of [false, true]) { const ids = await mount(page, { readonly });
    for (const id of [ids.branch, ids.gallery, ids.source, ids.trayChild]) { await card(page, id).hover(); await expect(page.locator('.ms-container-hover-bar')).toHaveCount(0); }
    await card(page, ids.branch).locator('.ms-node-title').dblclick(); await card(page, ids.child).hover(); await expect(page.locator('.ms-container-hover-bar')).toHaveCount(0);
    await expect(page.locator('[contenteditable=true]')).toHaveCount(0);
  }
  await page.screenshot({ path: info.outputPath('readonly-hierarchy-without-hover-banners.png') });
});

test('Single Select still resizes a selected text-filled edge while Text cannot resize it', async ({ page }) => {
  const ids = await mount(page, { selections: [] }); await card(page, ids.branch).locator('.ms-node-title').click();
  let box = (await card(page, ids.branch).boundingBox())!; await drag(page, { x: box.x + box.width - 5, y: box.y + box.height / 2 }, 35, 0);
  expect((await state(page)).resizes).toHaveLength(1); expect((await state(page)).edits).toEqual([]);
  await mode(page, 'text'); box = (await card(page, ids.branch).boundingBox())!; await drag(page, { x: box.x + box.width - 5, y: box.y + box.height / 2 }, 35, 0);
  expect((await state(page)).resizes).toHaveLength(1); expect((await state(page)).moves).toEqual([]);
});

test('drag alignment guides describe the final snapped frame and clear on release, Alt, and Escape', async ({ page }, info) => {
  const ids = await mount(page);
  await page.evaluate(ids => {
    const h = (window as any).interactionTest;
    h.project.nodes = h.project.nodes.filter((n: any) => n.kind === 'canvas' || [ids.branch, ids.empty].includes(n.id));
    Object.assign(h.project.nodes.find((n: any) => n.id === ids.branch), { x: 100, y: 100, width: 200, height: 160 });
    Object.assign(h.project.nodes.find((n: any) => n.id === ids.empty), { title: 'Reference', x: 550, y: 360, width: 220, height: 180 });
    h.project.canvases[0].snapping = { stickyEdges: false, smartAlign: true, snapToGrid: false }; h.draw();
  }, ids);
  const from = await point(card(page, ids.branch).locator('.ms-node-title'), 3);
  await page.mouse.move(from.x, from.y); await page.mouse.down(); await page.mouse.move(from.x + 246, from.y + 266, { steps: 8 });
  await expect(page.locator('.ms-snap-guide[data-axis=x]')).toHaveCount(1); await expect(page.locator('.ms-snap-guide[data-axis=y]')).toHaveCount(1);
  await expect(card(page, ids.branch)).toHaveCSS('transform', 'matrix(1, 0, 0, 1, 250, 270)');
  await page.screenshot({ path: info.outputPath('final-frame-two-axis-alignment-guides.png') });
  await page.keyboard.down('Alt'); await expect(page.locator('.ms-snap-guide')).toHaveCount(0); await page.keyboard.up('Alt'); await expect(page.locator('.ms-snap-guide')).toHaveCount(2);
  await page.mouse.up(); await expect(page.locator('.ms-snap-guide')).toHaveCount(0); expect((await state(page)).moves.at(-1)).toEqual({ ids: [ids.branch], dx: 250, dy: 270 });
  const next = await point(card(page, ids.branch).locator('.ms-node-title'), 3); await page.mouse.move(next.x, next.y); await page.mouse.down(); await page.mouse.move(next.x + 1, next.y - 25, { steps: 4 });
  await page.keyboard.press('Escape'); await page.mouse.up(); await expect(page.locator('.ms-snap-guide')).toHaveCount(0); expect((await state(page)).moves).toHaveLength(1);
});

test('a spatial ancestor cannot attract its own dragged child or produce alignment guides', async ({ page }) => {
  const ids = await mount(page);
  await page.evaluate(ids => {
    const h = (window as any).interactionTest;
    h.project.nodes = h.project.nodes.filter((n: any) => n.kind === 'canvas' || [ids.branch, ids.empty].includes(n.id));
    Object.assign(h.project.nodes.find((n: any) => n.id === ids.empty), { title: 'Structural parent', x: 550, y: 600, width: 220, height: 180, collapsed: false });
    Object.assign(h.project.nodes.find((n: any) => n.id === ids.branch), { parentId: ids.empty, x: 100, y: 100, width: 200, height: 160 });
    h.project.canvases[0].snapping = { stickyEdges: false, smartAlign: true, snapToGrid: false }; h.draw();
  }, ids);
  const from = await point(card(page, ids.branch).locator('.ms-node-title'), 3);
  await page.mouse.move(from.x, from.y); await page.mouse.down(); await page.mouse.move(from.x + 448, from.y + 250, { steps: 8 });
  await expect(page.locator('.ms-snap-guide')).toHaveCount(0); await page.mouse.up();
  expect((await state(page)).moves.at(-1)).toEqual({ ids: [ids.branch], dx: 448, dy: 250 });
});
