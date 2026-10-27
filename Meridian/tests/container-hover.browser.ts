import { test, expect, type Page } from '@playwright/test';
import { build } from 'esbuild';
import { readFile } from 'node:fs/promises';
import { addNode, newProject } from '../src/core/model.ts';
import type { MeridianProject } from '../src/core/types.ts';

let script: string, css: string;
test.beforeAll(async () => {
  css = (await Promise.all(['scene', 'grid', 'gallery-scene', 'window-scene', 'expansion-tray-scene'].map(name => readFile(`src/${name}.css`, 'utf8')))).join('\n');
  script = (await build({ stdin: { contents: `import {renderScene,sceneGestureActive} from './src/scene.ts';import {moveExpansionTray,resizeExpansionTray} from './src/core/expansion-tray.ts';import {moveSelection} from './src/core/selection.ts';import {canMoveNodeBranches} from './src/core/position-lock.ts';window.hoverHarness={renderScene,sceneGestureActive,moveExpansionTray,resizeExpansionTray,moveSelection,canMoveNodeBranches};`, resolveDir: process.cwd(), loader: 'ts' }, bundle: true, write: false, format: 'iife', platform: 'browser', target: 'es2022' })).outputFiles[0]!.text;
});
const card = (page: Page, id: string) => page.locator(`.ms-node[data-node-id="${id}"]`);
const state = (page: Page) => page.evaluate(() => structuredClone((window as any).hoverTest.state));
const documentState = (page: Page): Promise<MeridianProject> => page.evaluate(() => structuredClone((window as any).hoverTest.project));
async function hoverOwnFrame(page: Page, id: string) {
  const box = (await card(page, id).boundingBox())!;
  await card(page, id).hover({ position: { x: box.width / 2, y: Math.min(box.y < 28 ? 45 : 15, box.height / 3) } });
}
// Hover name strips were removed by the explicit Text/Single Select contract.
// Exercise the retained movement machinery through node titles/free rims instead.
async function startHandle(page: Page, id: string, _ownFrame = false) {
  const ids = await page.evaluate(() => (window as any).hoverTest.ids);
  const element = id === ids.item
    ? page.locator(`.ms-expansion-tray-backdrop[data-tray-source-id="${ids.source}"]`)
    : card(page, id);
  const box = (await element.boundingBox())!;
  const at = id === ids.item ? { x: box.x + 4, y: box.y + box.height / 2 } : { x: box.x + box.width / 2, y: box.y + 20 };
  await page.mouse.move(at.x, at.y); await page.mouse.down(); return at;
}

async function fixture(page: Page, config: Record<string, unknown> = {}, setup?: (project: MeridianProject, ids: Record<string, string>) => void) {
  const errors: string[] = []; page.on('pageerror', error => errors.push(error.message));
  await page.setContent('<style>html,body{margin:0;height:100%}#scene{height:900px;width:1450px}</style><div id="scene"></div>');
  await page.addStyleTag({ content: css }); await page.addScriptTag({ content: script });
  const project = newProject('Container hover scene'), canvasId = project.entryCanvasId;
  project.canvases[0]!.snapping = { stickyEdges: false, smartAlign: false, snapToGrid: false };
  const outer = addNode(project, canvasId, 'container', 'Authored canvas title'); Object.assign(outer, { tag: 'Outer Tag', x: 100, y: 100, width: 540, height: 350, sizeMode: 'fixed', collapsed: false, containerVariant: 'contained', childLayout: 'contained' });
  const inner = addNode(project, canvasId, 'container', 'Inner visible title'); Object.assign(inner, { tag: 'Inner Tag', parentId: outer.id, x: 125, y: 170, width: 260, height: 250, sizeMode: 'fixed', collapsed: false, containerVariant: 'contained', childLayout: 'contained' });
  const leaf = addNode(project, canvasId, 'media', 'Editable leaf title'); Object.assign(leaf, { tag: 'Leaf Tag', parentId: inner.id, x: 150, y: 230, width: 210, height: 160, sizeMode: 'fixed', collapsed: true });
  const source = addNode(project, canvasId, 'media', 'Source visible title'); Object.assign(source, { tag: 'Tray Source Tag', parentId: outer.id, x: 420, y: 200, width: 190, height: 170, sizeMode: 'fixed', collapsed: false, childLayout: 'spatial', expansionTray: { offset: { x: 430, y: 30 }, layoutVersion: 3 } });
  const item = addNode(project, canvasId, 'media', 'Tray child title'); Object.assign(item, { parentId: source.id, x: 850, y: 230, width: 220, height: 160, sizeMode: 'fixed', collapsed: true, childPosition: 'manual' });
  const standalone = addNode(project, canvasId, 'media', 'Plain media'); Object.assign(standalone, { tag: 'Standalone', x: 1050, y: 550, width: 180, height: 110, sizeMode: 'fixed', collapsed: false });
  const ids = { outer: outer.id, inner: inner.id, leaf: leaf.id, source: source.id, item: item.id, standalone: standalone.id, second: '', titleless: '' };
  setup?.(project, ids);
  await page.evaluate(({ project: initial, ids, config }) => {
    const api = (window as any).hoverHarness;
    const h: any = { project: initial, ids, state: { view: { x: 10, y: 20, scale: 1 }, selections: [], toggles: [], textEdits: [], moves: [], resizes: [], ...config } };
    h.render = () => api.renderScene(document.getElementById('scene'), h.project, initial.entryCanvasId, {
      view: h.state.view, selections: h.state.selections, readonly: h.state.readonly, authoringMode: h.state.authoringMode,
      canMoveNodes: (ids: string[]) => api.canMoveNodeBranches(h.project, ids),
      onSelections(ids: string[]) { h.state.selections = ids; h.render(); },
      onTextEdit(id: string, title: boolean) { h.state.textEdits.push({ id, title }); },
      onToggle(id: string, field: string, value: boolean) { h.state.toggles.push({ id, field, value }); h.project.nodes.find((node: any) => node.id === id)[field] = value; h.render(); },
      onMove(id: string, x: number, y: number) { h.state.moves.push({ id, x, y }); },
      onMoveSelection(ids: string[], dx: number, dy: number) { h.state.moves.push({ ids, dx, dy }); h.project = api.moveSelection(h.project, ids, dx, dy); h.render(); },
      onResize(id: string, width: number, height: number) { h.state.resizes.push({ id, width, height }); },
      onTrayMove(id: string, dx: number, dy: number) { h.state.moves.push({ id, dx, dy }); h.project = api.moveExpansionTray(h.project, id, dx, dy); h.render(); },
      onTrayResize(id: string, frame: any) { h.state.resizes.push({ id, frame }); h.project = api.resizeExpansionTray(h.project, id, frame); h.render(); },
      onGalleryPage(id: string, item: string) { h.state.galleryItems = { ...h.state.galleryItems, [id]: item }; h.render(); }, galleryItems: h.state.galleryItems,
      onViewChange(view: any) { h.state.view = view; },
    });
    (window as any).hoverTest = h; h.render();
  }, { project, ids, config });
  return { ids, errors };
}


const textFilledBranch = (project: MeridianProject, ids: Record<string, string>) => {
  const parent = project.nodes.find(node => node.id === ids.standalone)!;
  // Reproduce the reported hierarchy: ordinary spatial Media with six semantic
  // children and seven routing Joints, initially hidden behind a text-filled cover.
  parent.id = '211a22ef-56f6-4a34-a863-7ccacfac6eb9'; ids.standalone = parent.id;
  Object.assign(parent, { title: 'Type inspection & metaprogramming', tag: 'Type inspection & metaprogramming',
    x: 900, y: 530, width: 450, height: 260, collapsed: true, childLayout: 'spatial', childArrangement: 'free',
    text: 'Inspect types, transform them and select valid implementations. Compile-time traits describe properties; transformations form related types. These are ordinary editable instructions filling the cover.',
    style: { shape: 'rectangle', contentSizing: 'fit', fontFamily: 'sans', titleSize: 30, fontSize: 24 } });
  for (let at = 0; at < 13; at++) {
    const child = addNode(project, parent.canvasId, at < 6 ? 'media' : 'junction', at < 6 ? `Semantic family ${at + 1}` : '');
    Object.assign(child, { parentId: parent.id, x: 1500 + at * 80, y: 1000, width: at < 6 ? 180 : 12,
      height: at < 6 ? 120 : 12, sizeMode: 'fixed', collapsed: true, childPosition: 'manual', placement: 'free',
      ...(at >= 6 ? { utilityVariant: 'joint' } : {}) });
  }
};


test('external Gallery navigation shows only two arrows while keeping its screen slot and accessible page status', async ({ page }) => {
  const { ids, errors } = await fixture(page, {}, (project, ids) => {
    const gallery = project.nodes.find(node => node.id === ids.standalone)!; Object.assign(gallery, { containerVariant: 'gallery', x: 750, y: 500 });
    for (const [index, text] of ['Short', 'A long authored Gallery item title that wraps over multiple lines'].entries()) { const item = addNode(project, gallery.canvasId, 'media', text); Object.assign(item, { parentId: gallery.id, x: 950, y: 500, width: 260, height: 180, sizeMode: 'fixed', collapsed: true, childPosition: 'manual' }); }
  });
  const nav = page.locator(`.ms-external-gallery-navigation[data-external-gallery="${ids.standalone}"]`), before = (await nav.boundingBox())!;
  await expect(nav.locator('button')).toHaveCount(2); await expect(nav.getByRole('button', { name: 'Previous object in Standalone' })).toBeDisabled();
  const status = nav.locator('.ms-gallery-position-sr'); await expect(status).toHaveAttribute('aria-live', 'polite');
  expect(await status.evaluate(element => getComputedStyle(element).clip)).toBe('rect(0px, 0px, 0px, 0px)');
  await nav.getByRole('button', { name: 'Next object in Standalone' }).click(); await expect(status).toHaveText('2 of 2');
  const after = (await nav.boundingBox())!; expect(after.width).toBeCloseTo(before.width, 1); expect(after.height).toBeCloseTo(before.height, 1); expect(errors).toEqual([]);
});


test('the compact five-pixel tray rim remains draggable and double-clickable while its outer edge still resizes', async ({ page }) => {
  const { ids, errors } = await fixture(page);
  const background = page.locator(`.ms-expansion-tray-backdrop[data-tray-source-id="${ids.source}"]`), frame = (await background.boundingBox())!;
  const rim = { x: frame.x + 4, y: frame.y + frame.height / 2 };
  expect(await page.evaluate(point => document.elementFromPoint(point.x, point.y)?.classList.contains('ms-expansion-tray-backdrop'), rim)).toBe(true);
  await page.mouse.move(rim.x, rim.y); await page.mouse.down(); await page.mouse.move(rim.x + 25, rim.y + 15, { steps: 3 }); await page.mouse.up();
  expect((await state(page)).moves).toEqual([{ id: ids.source, dx: 25, dy: 15 }]); expect((await state(page)).resizes).toEqual([]);
  const moved = (await background.boundingBox())!, resize = { x: moved.x - 4, y: moved.y + moved.height / 2 };
  expect(await page.evaluate(point => (document.elementFromPoint(point.x, point.y) as HTMLElement)?.dataset.trayResize, resize)).toBe('w');
  await page.mouse.move(resize.x, resize.y); await page.mouse.down(); await page.mouse.move(resize.x - 20, resize.y, { steps: 3 }); await page.mouse.up();
  expect((await state(page)).resizes).toHaveLength(1);
  const resized = (await background.boundingBox())!;
  await background.dblclick({ position: { x: 4, y: resized.height / 2 } }); await expect(page.locator('.ms-expansion-tray')).toHaveCount(0);
  expect((await state(page)).toggles).toEqual([{ id: ids.source, field: 'collapsed', value: true }]); expect(errors).toEqual([]);
});


test('a node title drags the existing multi-selection once, carrying real descendants, tray positions and internal route points', async ({ page }) => {
  const { ids, errors } = await fixture(page, { view: { x: 10, y: 20, scale: .8 } }, (project, ids) => {
    project.links.push({ id: 'owned-route', canvasId: project.entryCanvasId, sourceId: ids.leaf!, targetId: ids.item!, kind: 'visual', relation: 'generic', points: [{ x: 700, y: 480 }] });
  });
  await page.evaluate(({ outer, standalone }) => { const h = (window as any).hoverTest; h.state.selections = [outer, standalone]; h.render(); }, ids);
  const before = await documentState(page), route = page.locator('.ms-link-line[data-link-id="owned-route"]'), oldPath = await route.getAttribute('d');
  const at = await startHandle(page, ids.outer!, true); await page.mouse.move(at.x + 40, at.y + 20, { steps: 4 });
  expect(await page.evaluate(() => (window as any).hoverHarness.sceneGestureActive(document.getElementById('scene')))).toBe(true);
  expect(await route.getAttribute('d')).not.toBe(oldPath); expect((await state(page)).moves).toEqual([]);
  await page.mouse.up(); const after = await documentState(page);
  expect((await state(page)).moves).toEqual([{ ids: [ids.outer, ids.standalone], dx: 50, dy: 25 }]);
  for (const original of before.nodes.filter(node => node.kind !== 'canvas')) {
    const moved = after.nodes.find(node => node.id === original.id)!;
    expect(moved.x - original.x).toBe(50); expect(moved.y - original.y).toBe(25); expect(moved.parentId).toBe(original.parentId);
  }
  expect(after.links.find(link => link.id === 'owned-route')!.points).toEqual([{ x: 750, y: 505 }]);
  expect(after.nodes.find(node => node.id === ids.source)!.expansionTray).toEqual(before.nodes.find(node => node.id === ids.source)!.expansionTray);
  expect((await state(page)).toggles).toEqual([]); expect(errors).toEqual([]);
});


test('a tray rim uses the existing snap and Alt preview while keeping its pinned source cover fixed', async ({ page }) => {
  const { ids, errors } = await fixture(page, {}, (project, ids) => { project.nodes.find(node => node.id === ids.source)!.positionLocked = true; });
  const before = await documentState(page), frame = page.locator(`.ms-expansion-tray[data-tray-source-id="${ids.source}"]`);
  const sourceBefore = before.nodes.find(node => node.id === ids.source)!, oldFrame = (await frame.boundingBox())!, owner = (await card(page, ids.outer!).boundingBox())!;
  const at = await startHandle(page, ids.item!), unsnapped = owner.x + owner.width + 6;
  await page.mouse.move(at.x + unsnapped - oldFrame.x, at.y, { steps: 5 });
  expect((await frame.boundingBox())!.x).toBeCloseTo(owner.x + owner.width, 2);
  await page.keyboard.down('Alt'); expect((await frame.boundingBox())!.x).toBeCloseTo(unsnapped, 2);
  await page.keyboard.up('Alt'); await page.mouse.up();
  const after = await documentState(page), delta = owner.x + owner.width - oldFrame.x;
  expect((await state(page)).moves).toEqual([{ id: ids.source, dx: delta, dy: 0 }]); expect((await state(page)).toggles).toEqual([]);
  const sourceAfter = after.nodes.find(node => node.id === ids.source)!;
  expect(sourceAfter.x).toBe(sourceBefore.x); expect(sourceAfter.y).toBe(sourceBefore.y);
  expect(after.nodes.find(node => node.id === ids.item)!.x - before.nodes.find(node => node.id === ids.item)!.x).toBe(delta); expect(errors).toEqual([]);
});


test('node and tray drags cancel on Escape, blur and scene replacement and never collapse after returning to their start', async ({ page }) => {
  const { ids, errors } = await fixture(page), before = await documentState(page);
  for (const hoverId of [ids.source!, ids.item!]) for (const cancellation of ['escape', 'blur', 'render', 'return']) {
    const at = await startHandle(page, hoverId); await page.mouse.move(at.x + 35, at.y + 20, { steps: 3 });
    if (cancellation === 'escape') await page.keyboard.press('Escape');
    else if (cancellation === 'blur') await page.evaluate(() => window.dispatchEvent(new Event('blur')));
    else if (cancellation === 'render') await page.evaluate(() => (window as any).hoverTest.render());
    else await page.mouse.move(at.x, at.y, { steps: 3 });
    await page.mouse.up(); expect(await documentState(page)).toEqual(before); expect((await state(page)).moves).toEqual([]); expect((await state(page)).toggles).toEqual([]);
    expect(await page.evaluate(() => (window as any).hoverHarness.sceneGestureActive(document.getElementById('scene')))).toBe(false);
  }
  expect(errors).toEqual([]);
});


test('a collapsed spatial Media title drags its entire original branch once without opening or editing it', async ({ page }) => {
  const { ids, errors } = await fixture(page, {}, textFilledBranch);
  const before = await documentState(page), at = await startHandle(page, ids.standalone);
  await page.mouse.move(at.x + 32, at.y + 20, { steps: 4 }); await page.mouse.up();
  const after = await documentState(page);
  expect((await state(page)).moves).toEqual([{ ids: [ids.standalone], dx: 32, dy: 20 }]);
  expect((await state(page)).toggles).toEqual([]); expect((await state(page)).textEdits).toEqual([]);
  for (const old of before.nodes.filter(node => node.id === ids.standalone || node.parentId === ids.standalone)) {
    const current = after.nodes.find(node => node.id === old.id)!;
    expect(current.x - old.x).toBe(32); expect(current.y - old.y).toBe(20); expect(current.collapsed).toBe(old.collapsed);
  }
  expect(errors).toEqual([]);
});

