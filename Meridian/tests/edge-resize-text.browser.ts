import { test, expect, type Page, type Locator } from '@playwright/test';
import { build } from 'esbuild';
import { readFile } from 'node:fs/promises';
import { addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import type { MeridianProject } from '../src/core/types.ts';

let sceneScript: string, sceneCss: string;
test.beforeAll(async () => {
  sceneCss = (await Promise.all(['scene.css', 'grid.css', 'gallery-scene.css', 'window-scene.css', 'canvas-launcher.css']
    .map(name => readFile(`src/${name}`, 'utf8')))).join('\n');
  sceneScript = (await build({ stdin: { contents: `import {renderScene} from './src/scene.ts';window.edgeScene={renderScene};`,
    resolveDir: process.cwd(), loader: 'ts' }, bundle: true, write: false, format: 'iife', platform: 'browser', target: 'es2022' })).outputFiles[0]!.text;
});

const tile = (page: Page) => page.locator('.ms-node[data-node-id]');
const state = (page: Page) => page.evaluate(() => structuredClone((window as any).edgeGesture.state));
async function mount(page: Page, config: Record<string, unknown> = {}) {
  const project = newProject('Text-filled node edge resize'), node = addNode(project, project.entryCanvasId, 'media', '');
  const scale = typeof config.scale === 'number' ? config.scale : 1;
  Object.assign(node, { x: 80 / scale, y: 80 / scale, width: 400 / scale, height: 200 / scale, sizeMode: 'fixed',
    contentType: 'text', text: 'Alpha beta gamma delta epsilon.\nWords remain selectable directly on this canvas.\nResize the frame by dragging its edges.',
    style: { shape: 'rectangle', padding: 0, borderWidth: 0, fontSize: 30, contentSizing: 'fit', textAlign: 'left' } });
  await page.setContent('<style>html,body{margin:0}#scene{width:1000px;height:700px}</style><div id="scene"></div>');
  await page.addStyleTag({ content: sceneCss }); await page.addScriptTag({ content: sceneScript });
  await page.evaluate(({ project, nodeId, config }) => {
    const state: any = { edits: [], textPointers: 0, resizes: [], moves: [], creates: [], selected: [nodeId], scale: 1, ...config };
    const draw = () => (window as any).edgeScene.renderScene(document.querySelector('#scene'), project, project.entryCanvasId, {
      fit: false, view: { x: 0, y: 0, scale: state.scale }, readonly: state.readonly, connecting: state.connecting,
      authoringMode: state.mode ?? 'select', selections: state.selected,
      onTextEdit(id: string, title: boolean, surface: HTMLElement) {
        state.edits.push({ id, title }); surface.contentEditable = 'true';
        if (!surface.dataset.countPointer) {
          surface.dataset.countPointer = 'true';
          surface.addEventListener('pointerdown', () => { state.textPointers++; });
        }
      },
      canMoveNodes: () => !state.locked,
      onSelections(ids: string[]) { state.selected = ids; draw(); },
      onResize(id: string, width: number, height: number, x: number, y: number) {
        state.resizes.push({ width, height, x, y }); Object.assign(project.nodes.find(node => node.id === id)!, { width, height, x, y }); draw();
      },
      onMoveSelection(ids: string[], dx: number, dy: number) { state.moves.push({ ids, dx, dy }); },
      onAuthorClick(target: any) { state.creates.push(target); },
    });
    (window as any).edgeGesture = { project, state, draw }; draw();
  }, { project, nodeId: node.id, config });
  return node;
}

async function drag(page: Page, x: number, y: number, dx: number, dy: number) {
  await page.mouse.move(x, y); await page.mouse.down(); await page.mouse.move(x + dx, y + dy, { steps: 8 }); await page.mouse.up();
}
async function textPoint(surface: Locator, offset: number) {
  return surface.evaluate((element, offset) => {
    const walk = document.createTreeWalker(element, NodeFilter.SHOW_TEXT); let node: Node | null, remaining = offset;
    while ((node = walk.nextNode())) {
      const length = node.textContent?.length ?? 0;
      if (remaining >= length) { remaining -= length; continue; }
      const range = document.createRange(); range.setStart(node, remaining); range.setEnd(node, remaining + 1);
      const rect = range.getBoundingClientRect(); return { x: rect.left + .5, y: rect.top + rect.height / 2 };
    }
    throw new Error('Text offset not found');
  }, offset);
}

test('the 10 screen-pixel edge target resizes full-bleed text at low, normal, and high camera scales', async ({ page }) => {
  for (const scale of [.125, 1, 8]) {
    const node = await mount(page, { scale }), box = (await tile(page).boundingBox())!;
    const textBox = (await tile(page).locator('.ms-text').boundingBox())!;
    const x = box.x + 8, y = textBox.y + Math.min(textBox.height / 2, box.height / 2);
    expect(await page.evaluate(({ x, y }) => !!document.elementFromPoint(x, y)?.closest('.ms-text'), { x, y }), JSON.stringify({ scale, box, textBox, x, y })).toBe(true);
    await page.mouse.move(x, y); await expect(tile(page)).toHaveAttribute('data-resize-cursor', 'ew-resize');
    expect(await page.evaluate(({ x, y }) => getComputedStyle(document.elementFromPoint(x, y)!).cursor, { x, y })).toBe('ew-resize');
    await drag(page, x, y, -40, 0);
    const current = await state(page); expect(current.edits).toEqual([]); expect(current.moves).toEqual([]);
    expect(current.resizes).toHaveLength(1); expect(current.resizes[0].width).toBeCloseTo(node.width + 40 / scale, 4);
    expect(current.resizes[0].height).toBeCloseTo(node.height, 4);
  }
});

test('Text keeps native passage selection and switching to Single Select restores edge resize', async ({ page }, info) => {
  await mount(page, { mode: 'text' }); const body = tile(page).locator('.ms-text');
  const from = await textPoint(body, 6), to = await textPoint(body, 15);
  await drag(page, from.x, from.y, to.x - from.x, to.y - from.y);
  await expect(body).toHaveAttribute('contenteditable', 'true');
  expect(await page.evaluate(() => getSelection()?.toString())).toContain('beta');
  const before = await state(page); await page.evaluate(() => { const h = (window as any).edgeGesture; h.state.mode = 'select'; h.draw(); });
  const box = (await tile(page).boundingBox())!;
  await drag(page, box.x + box.width - 8, box.y + box.height / 2, 50, 0);
  const after = await state(page); expect(after.resizes).toHaveLength(1); expect(after.edits).toEqual(before.edits);
  expect(after.textPointers).toBe(before.textPointers); expect(after.moves).toEqual([]);
  await page.screenshot({ path: info.outputPath('text-edge-resize-preserves-native-editing.png') });
});

test('viewer, creation, area selection, modifier selection and position locks preserve their own gestures', async ({ page }) => {
  for (const config of [{ readonly: true }, { mode: 'node' }, { mode: 'link' }, { mode: 'delete' }, { mode: 'module' }, { mode: 'hand' }, { connecting: true }]) {
    await mount(page, config); const box = (await tile(page).boundingBox())!;
    await drag(page, box.x + box.width - 8, box.y + box.height / 2, 30, 0);
    expect((await state(page)).resizes).toEqual([]);
  }
  await mount(page); let box = (await tile(page).boundingBox())!;
  await page.keyboard.down('Control'); try { await drag(page, box.x + box.width - 8, box.y + box.height / 2, 30, 0); }
  finally { await page.keyboard.up('Control'); }
  expect((await state(page)).resizes).toEqual([]);
  await mount(page, { locked: true }); box = (await tile(page).boundingBox())!;
  await drag(page, box.x + 8, box.y + box.height / 2, -30, 0); expect((await state(page)).resizes).toEqual([]);
  await drag(page, box.x + box.width - 8, box.y + box.height / 2, 30, 0);
  const current = await state(page); expect(current.resizes).toHaveLength(1); expect(current.resizes[0].x).toBeCloseTo(box.x, 4);
});

test('Text retains an editable interior in a tiny selected node without resize targets', async ({ page }) => {
  await mount(page, { mode: 'text' });
  await page.evaluate(() => {
    const fixture = (window as any).edgeGesture, node = fixture.project.nodes.find((node: any) => fixture.state.selected.includes(node.id));
    Object.assign(node, { width: 32, height: 24, text: 'Alpha\nbeta\ngamma\ndelta\nepsilon\nzeta\neta\ntheta', style: { ...node.style, fontSize: 20, textAlign: 'center' } }); fixture.draw();
  });
  const box = (await tile(page).boundingBox())!;
  const target = await tile(page).locator('.ms-text').evaluate(element => {
    const box = element.getBoundingClientRect(); return { x: box.x + box.width / 2, y: box.y + box.height / 2, box: { width: box.width, height: box.height } };
  });
  expect(target.x).toBeGreaterThan(box.x + 6); expect(target.y).toBeGreaterThan(box.y + 6);
  await page.mouse.click(target.x, target.y);
  expect((await state(page)).edits).toHaveLength(1); expect((await state(page)).resizes).toEqual([]);
});

test('editor: resizing an active passage flushes newly typed text and saves both text and frame across reload', async ({ page, request }, info) => {
  const project = newProject('Pending inline passage and frame'), node = addNode(project, project.entryCanvasId, 'media', '');
  Object.assign(node, { x: 100, y: 100, width: 420, height: 220, sizeMode: 'fixed', contentType: 'text',
    text: 'Alpha beta gamma delta.\nThe frame and passage are independently editable.',
    style: { shape: 'rectangle', padding: 0, borderWidth: 0, fontSize: 24, contentSizing: 'fit', textAlign: 'left' } });
  project.canvases[0]!.snapping = { stickyEdges: false, smartAlign: false, snapToGrid: false };
  const response = await request.post('/api/projects', { data: commitSnapshots(project) });
  expect(response.status(), await response.text()).toBe(201);
  const errors: string[] = []; page.on('pageerror', error => errors.push(error.message));
  await page.goto(`/?project=${project.id}`);
  const card = page.locator(`.ms-node[data-node-id="${node.id}"]`), body = card.locator('.ms-text'); await expect(card).toBeVisible();
  await page.getByRole('button', { name: 'Text', exact: true }).click();
  const inside = await textPoint(body, 8); await page.mouse.click(inside.x, inside.y);
  await expect(body).toHaveAttribute('contenteditable', 'true'); await page.keyboard.press('Control+End');
  const suffix = ' Newly typed text survives edge resize.'; await page.keyboard.insertText(suffix);
  await page.getByRole('button', { name: /^(?:Single select|Select Area)$/ }).click(); await page.getByRole('menuitem', { name: 'Single select', exact: true }).click();
  const before = (await card.boundingBox())!, scale = before.width / node.width;
  const edge = { x: before.x + 8, y: before.y + before.height / 2 };
  expect(await page.evaluate(({ x, y }) => !!document.elementFromPoint(x, y)?.closest('.ms-text'), edge)).toBe(true);
  await drag(page, edge.x, edge.y, -56, 0);
  const read = async () => (await (await request.get(`/api/projects/${project.id}`)).json() as MeridianProject).nodes.find(item => item.id === node.id)!;
  await expect.poll(async () => (await read()).text).toBe(node.text + suffix);
  await expect.poll(async () => (await read()).width).toBeCloseTo(node.width + 56 / scale, 2);
  expect((await read()).height).toBeCloseTo(node.height, 2);
  await page.reload(); await expect(card).toBeVisible(); await expect(card.locator('.ms-text')).toContainText(suffix.trim());
  expect((await read()).width).toBeCloseTo(node.width + 56 / scale, 2); expect(errors).toEqual([]);
  await page.screenshot({ path: info.outputPath('saved-native-passage-and-resized-frame.png') });
});
