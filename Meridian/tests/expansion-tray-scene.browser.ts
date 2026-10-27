import { test, expect, type Page } from '@playwright/test';
import { build } from 'esbuild';
import { readFile } from 'node:fs/promises';
import { addNode, newProject } from '../src/core/model.ts';
import type { MeridianProject } from '../src/core/types.ts';

let script: string, css: string;
test.beforeAll(async () => {
  css = (await Promise.all(['scene', 'grid', 'gallery-scene', 'window-scene', 'expansion-tray-scene'].map(name => readFile(`src/${name}.css`, 'utf8')))).join('\n');
  script = (await build({ stdin: { contents: `import {renderScene,sceneGestureActive} from './src/scene.ts';import {moveExpansionTray,resizeExpansionTray,expansionTrayBounds} from './src/core/expansion-tray.ts';window.trayHarness={renderScene,sceneGestureActive,moveExpansionTray,resizeExpansionTray,expansionTrayBounds};`,
    resolveDir: process.cwd(), loader: 'ts' }, bundle: true, write: false, format: 'iife', platform: 'browser', target: 'es2022' })).outputFiles[0]!.text;
});
const card = (page: Page, id: string) => page.locator(`.ms-node[data-node-id="${id}"]`);
const tray = (page: Page, id: string) => page.locator(`.ms-expansion-tray[data-tray-source-id="${id}"]`);
const backdrop = (page: Page, id: string) => page.locator(`.ms-expansion-tray-backdrop[data-tray-source-id="${id}"]`);
const state = (page: Page) => page.evaluate(() => structuredClone((window as any).traySceneTest.state));
const documentState = (page: Page): Promise<MeridianProject> => page.evaluate(() => structuredClone((window as any).traySceneTest.project));

async function fixture(page: Page, config: Record<string, unknown> = {}, setup?: (project: MeridianProject, ids: Record<string, string>) => void) {
  const errors: string[] = []; page.on('pageerror', error => errors.push(error.message));
  await page.setContent('<style>html,body{margin:0;height:100%}#scene{height:900px;width:1450px}</style><div id="scene"></div>');
  await page.addStyleTag({ content: css }); await page.addScriptTag({ content: script });
  const project = newProject('Expansion tray scene'), canvasId = project.entryCanvasId;
  project.canvases[0]!.snapping = { stickyEdges: false, smartAlign: false, snapToGrid: false };
  const owner = addNode(project, canvasId, 'container', 'Enclosure'); Object.assign(owner, { tag: 'Enclosure', x: 80, y: 70, width: 520, height: 260, sizeMode: 'fixed', collapsed: false, containerVariant: 'horizontal', childLayout: 'contained', childArrangement: 'row' });
  const source = addNode(project, canvasId, 'media', 'Source'); Object.assign(source, { tag: 'Original source', parentId: owner.id, x: 101, y: 110, width: 239, height: 199, sizeMode: 'fixed', collapsed: false,
    childLayout: 'spatial', containerVariant: 'contained', expansionTray: { offset: { x: 629, y: 40 }, layoutVersion: 3 } });
  const peer = addNode(project, canvasId, 'media', 'Peer'); Object.assign(peer, { parentId: owner.id, x: 340, y: 110, width: 239, height: 199, sizeMode: 'fixed', collapsed: true });
  const a = addNode(project, canvasId, 'media', 'A'); Object.assign(a, { parentId: source.id, x: 730, y: 150, width: 190, height: 120, sizeMode: 'fixed', childPosition: 'manual', childLayout: 'spatial', collapsed: false });
  const b = addNode(project, canvasId, 'media', 'B'); Object.assign(b, { parentId: source.id, x: 960, y: 150, width: 160, height: 120, sizeMode: 'fixed', childPosition: 'manual', collapsed: true });
  const grandchild = addNode(project, canvasId, 'media', 'Grandchild'); Object.assign(grandchild, { parentId: a.id, x: 730, y: 320, width: 140, height: 100, sizeMode: 'fixed', childPosition: 'manual', collapsed: true });
  const link = crypto.randomUUID(); project.links.push({ id: link, canvasId, sourceId: a.id, targetId: b.id, kind: 'visual', relation: 'generic', points: [{ x: 940, y: 110 }] });
  const ids = { owner: owner.id, source: source.id, peer: peer.id, a: a.id, b: b.id, grandchild: grandchild.id, link };
  setup?.(project, ids);
  await page.evaluate(({ project: initial, ids, config }) => {
    const { renderScene, moveExpansionTray, resizeExpansionTray } = (window as any).trayHarness;
    const harness: any = { project: initial, ids, state: { moves: [], resizes: [], selections: [], toggles: [], view: { x: 20, y: 20, scale: 1 }, ...config } };
    const render = () => renderScene(document.getElementById('scene'), harness.project, initial.entryCanvasId, {
      selections: harness.state.selections, view: harness.state.view, readonly: harness.state.readonly, galleryItems: harness.state.galleryItems,
      canMoveNodes: (ids: string[]) => !ids.some(id => harness.project.nodes.find((node: any) => node.id === id)?.positionLocked),
      onSelections(ids: string[]) {
        harness.state.selections = ids;
        if (harness.state.replaceHostOnSelect) { const next = document.createElement('div'); next.id = 'scene'; document.getElementById('scene')!.replaceWith(next); }
        render();
      },
      onTrayMove(id: string, dx: number, dy: number) { harness.state.moves.push({ id, dx, dy }); harness.project = moveExpansionTray(harness.project, id, dx, dy); render(); },
      onTrayResize(id: string, frame: any) { harness.state.resizes.push({ id, frame }); harness.project = resizeExpansionTray(harness.project, id, frame, { galleryItems: harness.state.galleryItems }); render(); },
      onToggle(id: string, field: string, value: boolean) { harness.state.toggles.push({ id, field, value }); harness.project.nodes.find((node: any) => node.id === id)[field] = value; render(); },
      onGalleryPage(id: string, item: string) { harness.state.galleryItems = { ...harness.state.galleryItems, [id]: item }; render(); },
      onViewChange(view: any) { harness.state.view = view; },
    });
    harness.render = render; (window as any).traySceneTest = harness; render();
  }, { project, ids, config });
  return { ids, errors };
}

test('tray chrome surrounds the original cards, shares hover ownership and never invents graph nodes or link targets', async ({ page }, info) => {
  const { ids, errors } = await fixture(page); const before = await documentState(page);
  await expect(tray(page, ids.source)).toBeVisible(); await expect(card(page, ids.a)).toHaveCount(1); await expect(card(page, ids.b)).toHaveCount(1);
  await expect(tray(page, ids.source)).toHaveText(''); await expect(backdrop(page, ids.source)).toHaveText('');
  await expect(page.locator('.ms-expansion-tray-badge,.ms-expansion-tray-header,.ms-expansion-tray-title,.ms-expansion-tray-close')).toHaveCount(0);
  await expect(tray(page, ids.source).locator('.ms-node')).toHaveCount(0);
  await expect(page.locator(`.ms-expansion-tether[data-tray-source-id="${ids.source}"]`)).toHaveCount(1);
  await expect(page.locator('.ms-expansion-tether[data-link-id]')).toHaveCount(0);
  await card(page, ids.source).hover(); await expect(tray(page, ids.source)).toHaveClass(/ms-tray-highlighted/);
  await card(page, ids.a).hover(); await expect(tray(page, ids.source)).toHaveClass(/ms-tray-highlighted/); await expect(card(page, ids.source)).toHaveClass(/ms-tray-highlighted/);
  await card(page, ids.a).locator('.ms-node-title').click(); await expect(card(page, ids.a)).toHaveAttribute('aria-selected', 'true');
  const after = await documentState(page); expect(after).toEqual(before); expect(after.nodes.map(node => node.id)).toEqual(before.nodes.map(node => node.id));
  await page.screenshot({ path: info.outputPath('original-node-expansion-tray.png') }); expect(errors).toEqual([]);
});

test('blank tray background drag uses zoom-correct canvas deltas, previews internal links, commits once and leaves the source fixed', async ({ page }) => {
  const { ids, errors } = await fixture(page, { view: { x: 10, y: 10, scale: .75 } });
  const before = await documentState(page), source = before.nodes.find(node => node.id === ids.source)!;
  const surface = backdrop(page, ids.source), at = (await surface.boundingBox())!;
  const route = page.locator(`.ms-link-line[data-link-id="${ids.link}"]`), previous = await route.getAttribute('d');
  await page.mouse.move(at.x + 12, at.y + at.height / 2); await page.mouse.down(); await page.mouse.move(at.x + 57, at.y + at.height / 2 + 30, { steps: 5 });
  expect(await page.evaluate(() => (window as any).trayHarness.sceneGestureActive(document.getElementById('scene')))).toBe(true);
  expect(await route.getAttribute('d')).not.toBe(previous); expect(await state(page)).toMatchObject({ moves: [] });
  await page.mouse.up(); expect((await state(page)).moves).toEqual([{ id: ids.source, dx: 60, dy: 40 }]);
  const after = await documentState(page); expect(after.nodes.find(node => node.id === ids.source)!.x).toBe(source.x); expect(after.nodes.find(node => node.id === ids.source)!.y).toBe(source.y);
  for (const id of [ids.a, ids.b, ids.grandchild]) {
    const from = before.nodes.find(node => node.id === id)!, to = after.nodes.find(node => node.id === id)!;
    expect(to.x - from.x).toBe(60); expect(to.y - from.y).toBe(40); expect(to.parentId).toBe(from.parentId);
  }
  expect(after.links.find(link => link.id === ids.link)!.points).toEqual([{ x: 1000, y: 150 }]); expect(errors).toEqual([]);
});

test('Escape, blur and scene replacement cancel tray previews without changing coordinates or leaving pointer capture active', async ({ page }) => {
  const { ids, errors } = await fixture(page), original = await documentState(page);
  for (const cancellation of ['escape', 'blur', 'replace']) {
    const at = (await backdrop(page, ids.source).boundingBox())!;
    await page.mouse.move(at.x + 12, at.y + at.height / 2); await page.mouse.down(); await page.mouse.move(at.x + 62, at.y + at.height / 2 + 40, { steps: 3 });
    if (cancellation === 'escape') await page.keyboard.press('Escape');
    else if (cancellation === 'blur') await page.evaluate(() => window.dispatchEvent(new Event('blur')));
    else await page.evaluate(() => (window as any).traySceneTest.render());
    await page.mouse.up(); expect(await documentState(page)).toEqual(original); expect((await state(page)).moves).toEqual([]);
    expect(await page.evaluate(() => (window as any).trayHarness.sceneGestureActive(document.getElementById('scene')))).toBe(false);
    expect(await card(page, ids.a).evaluate(element => (element as HTMLElement).style.transform)).toBe('');
  }
  expect(errors).toEqual([]);
});

test('ancestor collapse hides trays without changing nested disclosure; readonly viewers can close a tray but cannot drag it', async ({ page }) => {
  const { ids, errors } = await fixture(page, { readonly: true }), before = await documentState(page);
  await expect(tray(page, ids.source).locator('.ms-expansion-tray-resize')).toHaveCount(0);
  await expect(backdrop(page, ids.source)).not.toHaveClass(/ms-tray-draggable/);
  await page.evaluate(({ owner }) => { const h = (window as any).traySceneTest; h.project.nodes.find((node: any) => node.id === owner).collapsed = true; h.render(); }, ids);
  await expect(tray(page, ids.source)).toHaveCount(0); await expect(card(page, ids.a)).toHaveCount(0);
  await page.evaluate(({ owner }) => { const h = (window as any).traySceneTest; h.project.nodes.find((node: any) => node.id === owner).collapsed = false; h.render(); }, ids);
  await expect(tray(page, ids.source)).toBeVisible(); await expect(card(page, ids.grandchild)).toBeVisible();
  expect((await documentState(page)).nodes.find(node => node.id === ids.a)!.collapsed).toBe(false);
  expect((await documentState(page)).nodes.find(node => node.id === ids.source)!.expansionTray).toEqual(before.nodes.find(node => node.id === ids.source)!.expansionTray);
  const at = (await backdrop(page, ids.source).boundingBox())!; await backdrop(page, ids.source).dblclick({ position: { x: 12, y: at.height / 2 } });
  await expect(tray(page, ids.source)).toHaveCount(0); await expect(card(page, ids.a)).toHaveCount(0);
  expect((await state(page)).toggles).toEqual([{ id: ids.source, field: 'collapsed', value: true }]); expect(errors).toEqual([]);
});

test('pinned tray contents block position previews while a pinned source tile still permits moving its exterior tray', async ({ page }) => {
  const { ids, errors } = await fixture(page, {}, (project, ids) => { project.nodes.find(node => node.id === ids.a)!.positionLocked = true; });
  let at = (await backdrop(page, ids.source).boundingBox())!;
  await page.mouse.move(at.x + 12, at.y + at.height / 2); await page.mouse.down(); await page.mouse.move(at.x + 62, at.y + at.height / 2 + 40); await page.mouse.up(); expect((await state(page)).moves).toEqual([]);
  await page.evaluate(({ a, source }) => { const h = (window as any).traySceneTest; h.project.nodes.find((node: any) => node.id === a).positionLocked = false; h.project.nodes.find((node: any) => node.id === source).positionLocked = true; h.render(); }, ids);
  at = (await backdrop(page, ids.source).boundingBox())!;
  await page.mouse.move(at.x + 12, at.y + at.height / 2); await page.mouse.down(); await page.mouse.move(at.x + 62, at.y + at.height / 2 + 40); await page.mouse.up(); expect((await state(page)).moves).toEqual([{ id: ids.source, dx: 50, dy: 40 }]); expect(errors).toEqual([]);
});

test('scrolling a source out of its enclosing Window hides its detached tray and original children until the source returns', async ({ page }) => {
  const { ids, errors } = await fixture(page, {}, (project, ids) => {
    const owner = project.nodes.find(node => node.id === ids.owner)!, source = project.nodes.find(node => node.id === ids.source)!, peer = project.nodes.find(node => node.id === ids.peer)!;
    Object.assign(owner, { containerVariant: 'window', style: { childPadding: 20 }, grid: { columns: 2, rows: 1, cellWidth: 240, cellHeight: 200, windowColumns: 1, windowRows: 1, scrollDirection: 'horizontal' } });
    Object.assign(source, { cell: { column: 1, row: 1 }, width: 478 }); Object.assign(peer, { cell: { column: 2, row: 1 }, x: 579, width: 478 });
  });
  const viewport = page.locator(`.ms-window-viewport[data-window-id="${ids.owner}"]`);
  await expect(tray(page, ids.source)).toBeVisible(); await expect(card(page, ids.a)).toBeVisible();
  await viewport.evaluate(element => { element.scrollLeft = element.scrollWidth - element.clientWidth; element.dispatchEvent(new Event('scroll')); });
  await expect(tray(page, ids.source)).toBeHidden(); await expect(card(page, ids.a)).toBeHidden(); await expect(card(page, ids.grandchild)).toBeHidden();
  expect(await page.evaluate(({ a }) => getComputedStyle(document.querySelector(`.ms-node[data-node-id="${a}"]`)!).visibility, ids)).toBe('hidden');
  await page.keyboard.down('Shift'); await page.mouse.move(700, 100); await page.mouse.down(); await page.mouse.move(1300, 500, { steps: 4 }); await page.mouse.up(); await page.keyboard.up('Shift');
  expect((await state(page)).selections).toEqual([]);
  await viewport.evaluate(element => { element.scrollLeft = 0; element.dispatchEvent(new Event('scroll')); });
  await expect(tray(page, ids.source)).toBeVisible(); await expect(card(page, ids.a)).toBeVisible();
  expect((await documentState(page)).nodes.find(node => node.id === ids.source)!.collapsed).toBe(false); expect(errors).toEqual([]);
});

test('an active embedded Gallery item anchors its tray without duplicating the item, and paging hides and restores the tray', async ({ page }) => {
  const { ids, errors } = await fixture(page, {}, (project, ids) => {
    Object.assign(project.nodes.find(node => node.id === ids.owner)!, { containerVariant: 'gallery' });
  });
  await expect(card(page, ids.source)).toHaveCount(0); await expect(page.locator(`[data-gallery-item-id="${ids.source}"]`)).toHaveCount(1);
  await expect(tray(page, ids.source)).toBeVisible(); await expect(card(page, ids.a)).toBeVisible();
  await page.getByRole('button', { name: 'Next object in Enclosure', exact: true }).click();
  await expect(tray(page, ids.source)).toHaveCount(0); await expect(card(page, ids.a)).toHaveCount(0);
  await page.getByRole('button', { name: 'Previous object in Enclosure', exact: true }).click();
  await expect(tray(page, ids.source)).toBeVisible(); await expect(card(page, ids.a)).toHaveCount(1);
  expect((await documentState(page)).nodes.find(node => node.id === ids.source)!.collapsed).toBe(false); expect(errors).toEqual([]);
});

async function frameGeometry(page: Page, id: string) {
  return tray(page, id).evaluate(element => { const style = (element as HTMLElement).style; return { x: parseFloat(style.left), y: parseFloat(style.top), width: parseFloat(style.width), height: parseFloat(style.height) }; });
}
async function cardGeometry(page: Page, id: string) {
  return card(page, id).evaluate(element => { const style = (element as HTMLElement).style; return { x: parseFloat(style.left), y: parseFloat(style.top), width: parseFloat(style.width), height: parseFloat(style.height) }; });
}

test('every tray edge and corner resizes from a screen-stable target and commits the same real-node preview exactly once', async ({ page }) => {
  for (const edge of ['n', 'ne', 'e', 'se', 's', 'sw', 'w', 'nw']) {
    await page.goto('about:blank');
    const { ids, errors } = await fixture(page, { view: { x: 10, y: 10, scale: .75 } });
    const before = await documentState(page), original = await frameGeometry(page, ids.source), source = await cardGeometry(page, ids.source);
    const handle = tray(page, ids.source).locator(`[data-tray-resize="${edge}"]`), at = (await handle.boundingBox())!;
    if (edge.length === 2) { expect(at.width).toBeCloseTo(16, 1); expect(at.height).toBeCloseTo(16, 1); }
    else expect(edge === 'n' || edge === 's' ? at.height : at.width).toBeCloseTo(12, 1);
    const dx = edge.includes('e') ? 45 : edge.includes('w') ? -30 : 0, dy = edge.includes('s') ? 30 : edge.includes('n') ? -15 : 0;
    const hit = await page.evaluate(({ x, y }) => { const element = document.elementFromPoint(x, y) as HTMLElement; return { tag: element?.tagName, className: element?.getAttribute('class'), edge: element?.dataset.trayResize }; }, { x: at.x + at.width / 2, y: at.y + at.height / 2 });
    expect(hit.edge, JSON.stringify(hit)).toBe(edge);
    await page.mouse.move(at.x + at.width / 2, at.y + at.height / 2); await page.mouse.down();
    expect(errors, edge).toEqual([]);
    await page.mouse.move(at.x + at.width / 2 + dx, at.y + at.height / 2 + dy, { steps: 4 });
    const preview = await frameGeometry(page, ids.source), previewA = await cardGeometry(page, ids.a), previewB = await cardGeometry(page, ids.b);
    expect(preview.width, edge).toBeCloseTo(original.width + Math.abs(dx) / .75, 5); expect(preview.height, edge).toBeCloseTo(original.height + Math.abs(dy) / .75, 5);
    expect((await state(page)).resizes).toEqual([]); expect(await documentState(page)).toEqual(before);
    await expect(card(page, ids.a)).toHaveCount(1); await expect(card(page, ids.source)).toHaveCount(1);
    expect(await page.evaluate(() => (window as any).trayHarness.sceneGestureActive(document.getElementById('scene')))).toBe(true);
    await page.mouse.up(); expect((await state(page)).resizes).toEqual([{ id: ids.source, frame: preview }]);
    expect(await frameGeometry(page, ids.source)).toEqual(preview); expect(await cardGeometry(page, ids.a)).toEqual(previewA); expect(await cardGeometry(page, ids.b)).toEqual(previewB);
    expect(await cardGeometry(page, ids.source)).toEqual(source); expect((await state(page)).moves).toEqual([]); expect(errors).toEqual([]);
  }
});

test('tray resize fits vector text and media, previews internal routes and preserves the exact preview after commit', async ({ page }, info) => {
  const base64 = await page.evaluate(() => {
    const image = document.createElement('canvas'); image.width = 640; image.height = 360; const drawing = image.getContext('2d')!;
    drawing.fillStyle = '#214c50'; drawing.fillRect(0, 0, 640, 360); drawing.fillStyle = '#83d8bd'; drawing.beginPath(); drawing.arc(320, 180, 112, 0, Math.PI * 2); drawing.fill();
    drawing.fillStyle = '#13372e'; drawing.font = '42px sans-serif'; drawing.textAlign = 'center'; drawing.fillText('16:9', 320, 195); return image.toDataURL('image/png').split(',')[1]!;
  });
  const { ids, errors } = await fixture(page, {}, (project, ids) => {
    const a = project.nodes.find(node => node.id === ids.a)!; a.title = 'Reading detail'; a.text = 'Text and media stay readable in their fitted frames.'; a.contentType = 'text'; a.collapsed = true;
    const assetId = crypto.randomUUID(); project.assets.push({ id: assetId, name: 'Landscape aspect check', mime: 'image/png', base64 });
    const b = project.nodes.find(node => node.id === ids.b)!; b.assetId = assetId; b.contentType = 'asset';
  });
  const original = await frameGeometry(page, ids.source), handle = tray(page, ids.source).locator('[data-tray-resize="se"]'), at = (await handle.boundingBox())!;
  const route = page.locator(`.ms-link-line[data-link-id="${ids.link}"]`), previousRoute = await route.getAttribute('d');
  await page.mouse.move(at.x + 8, at.y + 8); await page.mouse.down(); await page.mouse.move(at.x + 108, at.y + 78, { steps: 4 });
  const previewRoute = await route.getAttribute('d'); expect(previewRoute).not.toBe(previousRoute);
  await expect(card(page, ids.a).locator('.ms-text')).toHaveText('Text and media stay readable in their fitted frames.');
  const textFit = await card(page, ids.a).getAttribute('data-content-fit'); expect(Number(textFit)).toBeGreaterThan(0);
  await expect(card(page, ids.b).locator('img')).toHaveCount(1);
  await expect(card(page, ids.b).locator('img')).toHaveJSProperty('naturalWidth', 640); await expect(card(page, ids.b).locator('img')).toHaveJSProperty('naturalHeight', 360);
  expect(await card(page, ids.b).locator('img').evaluate(element => getComputedStyle(element).objectFit)).toBe('contain');
  expect((await frameGeometry(page, ids.source)).width).toBeCloseTo(original.width + 100, 5);
  await page.mouse.up(); expect(await route.getAttribute('d')).toBe(previewRoute); expect(await card(page, ids.a).getAttribute('data-content-fit')).toBe(textFit);
  await page.screenshot({ path: info.outputPath('resized-tray-real-text-and-media.png') }); expect(errors).toEqual([]);
});

test('cancelled resizing restores cards, fitted text, tethers and route geometry and readonly or pinned contents expose no resizing edits', async ({ page }) => {
  const { ids, errors } = await fixture(page), original = await documentState(page), originalFrame = await frameGeometry(page, ids.source), originalCard = await cardGeometry(page, ids.a);
  const route = page.locator(`.ms-link-line[data-link-id="${ids.link}"]`), originalRoute = await route.getAttribute('d');
  for (const cancellation of ['escape', 'blur', 'replace']) {
    const at = (await tray(page, ids.source).locator('[data-tray-resize="se"]').boundingBox())!;
    await page.mouse.move(at.x + 8, at.y + 8); await page.mouse.down(); await page.mouse.move(at.x + 70, at.y + 55, { steps: 3 });
    expect(await cardGeometry(page, ids.a)).not.toEqual(originalCard);
    if (cancellation === 'escape') await page.keyboard.press('Escape'); else if (cancellation === 'blur') await page.evaluate(() => window.dispatchEvent(new Event('blur'))); else await page.evaluate(() => (window as any).traySceneTest.render());
    await page.mouse.up(); expect(await documentState(page)).toEqual(original); expect((await state(page)).resizes).toEqual([]);
    expect(await frameGeometry(page, ids.source)).toEqual(originalFrame); expect(await cardGeometry(page, ids.a)).toEqual(originalCard); expect(await route.getAttribute('d')).toBe(originalRoute);
    expect(await page.evaluate(() => (window as any).trayHarness.sceneGestureActive(document.getElementById('scene')))).toBe(false);
  }
  await page.evaluate(({ a }) => { const h = (window as any).traySceneTest; h.project.nodes.find((node: any) => node.id === a).positionLocked = true; h.render(); }, ids);
  const at = (await tray(page, ids.source).locator('[data-tray-resize="se"]').boundingBox())!;
  await page.mouse.move(at.x + 8, at.y + 8); await page.mouse.down(); await page.mouse.move(at.x + 60, at.y + 50); await page.mouse.up();
  expect((await state(page)).resizes).toEqual([]); expect(await frameGeometry(page, ids.source)).toEqual(originalFrame); expect(errors).toEqual([]);
});

test('source fan lines are hidden inside a tray while sibling and unrelated links retain their original graph identities', async ({ page }) => {
  const { ids, errors } = await fixture(page, {}, (project, ids) => {
    for (const [id, sourceId, targetId] of [['fan-a', ids.source, ids.a], ['fan-b', ids.source, ids.b], ['unrelated', ids.peer, ids.a]]) {
      project.links.push({ id, sourceId, targetId, canvasId: project.entryCanvasId, kind: 'visual', relation: 'generic' });
    }
  });
  await expect(page.locator('[data-link-id="fan-a"],[data-link-id="fan-b"]')).toHaveCount(0);
  await expect(page.locator(`.ms-link-line[data-link-id="${ids.link}"]`)).toHaveCount(1);
  await expect(page.locator('.ms-link-line[data-link-id="unrelated"]')).toHaveCount(1);
  await expect(page.locator(`.ms-expansion-tether[data-tray-source-id="${ids.source}"]`)).toHaveCount(1);
  expect((await documentState(page)).links.map(link => link.id)).toEqual([ids.link, 'fan-a', 'fan-b', 'unrelated']); expect(errors).toEqual([]);
});

test('a fixed-size tray snaps to its enclosing frame with a highlighted shared edge, and Alt bypass changes preview and commit consistently', async ({ page }) => {
  const { ids, errors } = await fixture(page, {}, (project, ids) => { project.nodes.find(node => node.id === ids.source)!.expansionTray!.size = { width: 430, height: 342 }; });
  const original = await frameGeometry(page, ids.source), at = (await backdrop(page, ids.source).boundingBox())!;
  const unsnappedDelta = 608 - original.x;
  await page.mouse.move(at.x + 12, at.y + at.height / 2); await page.mouse.down(); await page.mouse.move(at.x + 12 + unsnappedDelta, at.y + at.height / 2, { steps: 4 });
  expect((await frameGeometry(page, ids.source)).x).toBe(600); await expect(card(page, ids.owner)).toHaveClass(/ms-tray-snap-receiver/);
  await page.keyboard.down('Alt'); expect((await frameGeometry(page, ids.source)).x).toBe(608); await expect(card(page, ids.owner)).not.toHaveClass(/ms-tray-snap-receiver/);
  await page.keyboard.up('Alt'); expect((await frameGeometry(page, ids.source)).x).toBe(600); await page.mouse.up();
  expect((await state(page)).moves).toEqual([{ id: ids.source, dx: 600 - original.x, dy: 0 }]); expect(await frameGeometry(page, ids.source)).toEqual({ ...original, x: 600 });
  await expect(card(page, ids.owner)).not.toHaveClass(/ms-tray-snap-receiver/); expect(errors).toEqual([]);
});

test('plain tray selection and consecutive blank-rim clicks survive replacement of the whole scene host while preserving source appearance', async ({ page }, info) => {
  const { ids, errors } = await fixture(page, { replaceHostOnSelect: true }, (project, ids) => {
    Object.assign(project.nodes.find(node => node.id === ids.source)!, { title: '', text: '', style: { fill: '#253b48', border: '#96bbca', borderWidth: 4, radius: 8 } });
    project.nodes.find(node => node.id === ids.owner)!.style = { fill: '#38214f', border: '#bb93d5', borderWidth: 4 };
  });
  const original = await documentState(page), oldHost = await page.locator('#scene').elementHandle(), at = (await backdrop(page, ids.source).boundingBox())!;
  await expect(tray(page, ids.source)).toHaveText(''); await expect(backdrop(page, ids.source)).toHaveText('');
  await expect(backdrop(page, ids.source)).toHaveCSS('background-color', 'rgb(37, 59, 72)');
  await expect(tray(page, ids.source)).toHaveCSS('border-width', '4px'); await expect(tray(page, ids.source)).toHaveCSS('border-color', 'rgb(150, 187, 202)'); await expect(tray(page, ids.source)).toHaveCSS('border-radius', '8px');
  await page.mouse.click(at.x + 16, at.y + at.height / 2);
  expect(await oldHost!.evaluate(element => element.isConnected)).toBe(false); await expect(card(page, ids.source)).toHaveAttribute('aria-selected', 'true'); await expect(tray(page, ids.source)).toHaveClass(/ms-selected/);
  await page.mouse.click(at.x + 16, at.y + at.height / 2);
  await expect(tray(page, ids.source)).toHaveCount(0); await expect(backdrop(page, ids.source)).toHaveCount(0); await expect(card(page, ids.a)).toHaveCount(0);
  const after = await documentState(page); expect(after.nodes.map(node => node.id)).toEqual(original.nodes.map(node => node.id));
  expect(after.nodes.find(node => node.id === ids.source)!.collapsed).toBe(true); expect(after.nodes.find(node => node.id === ids.a)!.collapsed).toBe(original.nodes.find(node => node.id === ids.a)!.collapsed);
  expect((await state(page)).toggles).toEqual([{ id: ids.source, field: 'collapsed', value: true }]); expect(errors).toEqual([]);
  await page.screenshot({ path: info.outputPath('plain-tray-source-after-collapse.png') });
});

test('tray corners are rectangular by default and honor explicit rounded Appearance without rounding a rectangle', async ({ page }, info) => {
  for (const [style, radius] of [[undefined, 0], [{ shape: 'rectangle', radius: 24 }, 0], [{ shape: 'rounded' }, 20], [{ shape: 'rounded', radius: 12 }, 12], [{ radius: 8 }, 8]] as const) {
    const { ids, errors } = await fixture(page, {}, (project, ids) => { project.nodes.find(node => node.id === ids.source)!.style = style ? { ...style } : undefined; });
    const original = await documentState(page);
    await expect(tray(page, ids.source)).toHaveCSS('border-top-left-radius', `${radius}px`);
    await expect(backdrop(page, ids.source)).toHaveCSS('border-top-left-radius', `${radius}px`);
    expect(await documentState(page)).toEqual(original); expect(errors).toEqual([]);
  }
  await page.screenshot({ path: info.outputPath('optional-rounded-tray-corners.png') });
});

test('the solid source tether paints above opaque enclosing fills and borders and below ordinary child cards', async ({ page }, info) => {
  const { ids, errors } = await fixture(page, {}, (project, ids) => { project.nodes.find(node => node.id === ids.owner)!.style = { fill: '#352047', border: '#b19bc8', borderWidth: 6 }; });
  const stack = await page.evaluate(ids => {
    const tether = document.querySelector(`.ms-expansion-tether[data-tray-source-id="${ids.source}"]`)!;
    const owner = document.querySelector(`.ms-node[data-node-id="${ids.owner}"]`)!, child = document.querySelector(`.ms-node[data-node-id="${ids.peer}"]`)!;
    return { owner: Number.parseFloat(getComputedStyle(owner).zIndex) || 0, tether: Number.parseFloat(getComputedStyle(tether.parentElement!).zIndex) || 0, child: Number.parseFloat(getComputedStyle(child).zIndex) || 0, dash: getComputedStyle(tether).strokeDasharray, d: tether.getAttribute('d') };
  }, ids);
  expect(stack.tether).toBeGreaterThan(stack.owner); expect(stack.tether).toBeLessThan(stack.child); expect(stack.dash).toBe('none');
  const source = await cardGeometry(page, ids.source), start = /^M([^,]+),([^ ]+)/.exec(stack.d!)!;
  expect(Number(start[2])).toBeCloseTo(source.y + source.height, 5);
  await page.screenshot({ path: info.outputPath('plain-tray-foreground-tether.png') }); expect(errors).toEqual([]);
});

test('a tray blank background never steals selection from an expanded shaped child or its nested contents', async ({ page }) => {
  const { ids, errors } = await fixture(page, {}, (project, ids) => { project.nodes.find(node => node.id === ids.a)!.style = { shape: 'rectangle', fill: '#24535b' }; });
  const bounds = (await card(page, ids.a).boundingBox())!;
  await page.mouse.click(bounds.x + bounds.width / 2, bounds.y + bounds.height - 18);
  await expect(card(page, ids.a)).toHaveAttribute('aria-selected', 'true'); await expect(card(page, ids.source)).toHaveAttribute('aria-selected', 'false');
  await card(page, ids.grandchild).locator('.ms-node-title').click(); await expect(card(page, ids.grandchild)).toHaveAttribute('aria-selected', 'true'); expect(errors).toEqual([]);
});
