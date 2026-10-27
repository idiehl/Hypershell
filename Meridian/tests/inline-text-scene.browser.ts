import { test, expect, type Locator, type Page } from '@playwright/test';
import { build } from 'esbuild';
import { readFile } from 'node:fs/promises';
import { addNode, newProject } from '../src/core/model.ts';

let sceneScript: string, sceneCss: string;
test.beforeAll(async () => {
  sceneCss = (await Promise.all(['scene.css', 'grid.css', 'gallery-scene.css', 'window-scene.css', 'canvas-launcher.css']
    .map(name => readFile(`src/${name}`, 'utf8')))).join('\n');
  sceneScript = (await build({ stdin: { contents: `import {renderScene} from './src/scene.ts';window.inlineScene={renderScene};`,
    resolveDir: process.cwd(), loader: 'ts' }, bundle: true, write: false, format: 'iife', platform: 'browser', target: 'es2022' })).outputFiles[0]!.text;
});

const card = (page: Page, id: string) => page.locator(`.ms-node[data-node-id="${id}"]`);
const state = (page: Page) => page.evaluate(() => structuredClone((window as any).inlineGesture.state));
async function mount(page: Page, config: Record<string, unknown> = {}) {
  const project = newProject('Canvas text gestures'), ids: Record<string, string> = {};
  const add = (key: string, kind = 'media', x = 40, y = 40, text = 'Words remain selectable directly inside the node.') => {
    const node = addNode(project, project.entryCanvasId, kind as any, key);
    Object.assign(node, { x, y, width: 400, height: 180, sizeMode: 'fixed', contentType: 'text', text,
      style: { fontFamily: 'calibri', titleSize: 20, fontSize: 20, textAlign: 'left', shape: 'rectangle' } });
    ids[key] = node.id; return node;
  };
  const text = add('Text'); text.collapsed = true;
  const child = add('Hidden child', 'media', 60, 290); child.parentId = text.id;
  const reference = add('Reference', 'reference', 500, 40); reference.targetId = text.id;
  const attachment = add('Attachment', 'media', 960, 40); attachment.contentType = 'asset'; attachment.assetId = 'plain-document';
  project.assets.push({ id: 'plain-document', name: 'document.txt', mime: 'text/plain', base64: Buffer.from('Decoded document content stays read only.').toString('base64') });
  const gallery = add('Gallery', 'container', 40, 410); Object.assign(gallery, { containerVariant: 'gallery', childLayout: 'contained', collapsed: false, height: 240 });
  const first = add('First gallery child', 'media', 50, 450); first.parentId = gallery.id;
  const second = add('Second gallery child', 'media', 50, 450); second.parentId = gallery.id;
  await page.setContent('<style>html,body{margin:0}#scene{width:1450px;height:900px}</style><div id="scene"></div>');
  await page.addStyleTag({ content: sceneCss }); await page.addScriptTag({ content: sceneScript });
  await page.evaluate(({ project, ids, config }) => {
    const state: any = { edits: [], selections: [], moves: [], toggles: [], creates: [], modules: [], pages: [], menus: [], view: { x: 0, y: 0, scale: 1 }, ...config };
    const draw = () => (window as any).inlineScene.renderScene(document.querySelector('#scene'), project, project.entryCanvasId, {
      fit: false, view: state.view, readonly: state.readonly, authoringMode: state.mode ?? 'text', selections: state.selections,
      onTextEdit(id: string, title: boolean, surface: HTMLElement) {
        state.edits.push({ id, title }); state.selections = [id];
        surface.contentEditable = 'true'; surface.setAttribute('role', 'textbox');
        surface.setAttribute('aria-label', title ? 'Inline title' : 'Inline text');
      },
      canEditText: (id: string) => id !== state.denied,
      canMoveNodes: (ids: readonly string[]) => !ids.some(id => state.locked?.includes(id)),
      onSelections(ids: string[]) { state.selections = ids; draw(); },
      onMoveSelection(ids: string[], dx: number, dy: number) { state.moves.push({ ids, dx, dy }); },
      onMove(id: string, x: number, y: number) { state.moves.push({ id, x, y }); },
      onResize(id: string, width: number, height: number) { state.moves.push({ id, width, height }); },
      onToggle(id: string, field: string, value: boolean) { state.toggles.push({ id, field, value }); project.nodes.find(node => node.id === id)![field as 'collapsed' | 'closed'] = value; draw(); },
      onAuthorClick(target: any) { state.creates.push(target); },
      onContextMenu(target: any) { state.menus.push(target); },
      onModuleSelect(id: string) { state.modules.push(id); },
      onGalleryPage(id: string, item: string) { state.pages.push({ id, item }); },
      onViewChange(view: any) { state.view = view; },
    });
    (window as any).inlineGesture = { project, ids, state, draw }; draw();
  }, { project, ids, config });
  return ids;
}

async function textPoint(surface: Locator, offset: number) {
  return surface.evaluate((surface, offset) => {
    const walk = document.createTreeWalker(surface, NodeFilter.SHOW_TEXT);
    let node: Node | null, remaining = offset;
    while ((node = walk.nextNode())) {
      const length = node.textContent?.length ?? 0;
      if (remaining >= length) { remaining -= length; continue; }
      const range = document.createRange(); range.setStart(node, remaining); range.setEnd(node, remaining + 1);
      const rect = range.getBoundingClientRect(); return { x: rect.left + 1, y: rect.top + rect.height / 2 };
    }
    throw new Error('Text offset is outside the visible passage');
  }, offset);
}

test('the first text drag selects native words without replacing DOM, moving the tile or disclosing its children', async ({ page }, info) => {
  const ids = await mount(page), body = card(page, ids.Text).locator('.ms-text');
  await body.evaluate(element => { (window as any).originalTextSurface = element; });
  const from = await textPoint(body, 0), to = await textPoint(body, 12);
  await page.mouse.move(from.x, from.y); await page.mouse.down(); await page.mouse.move(to.x, to.y, { steps: 8 }); await page.mouse.up();
  const selected = await page.evaluate(() => getSelection()?.toString()); expect(selected).toContain('Words');
  expect(await body.evaluate(element => element === (window as any).originalTextSurface)).toBe(true);
  await expect(body).toHaveAttribute('contenteditable', 'true'); await expect(card(page, ids.Text)).toHaveAttribute('aria-selected', 'true');
  const current = await state(page); expect(current.edits).toEqual([{ id: ids.Text, title: false }]); expect(current.moves).toEqual([]); expect(current.toggles).toEqual([]);
  expect(current.view).toEqual({ x: 0, y: 0, scale: 1 }); await expect(card(page, ids['Hidden child'])).toHaveCount(0);
  await page.screenshot({ path: info.outputPath('native-canvas-passage-selection.png') });
});

test('text double clicks select words while backdrop double clicks still disclose hierarchy and background drags pan', async ({ page }) => {
  const ids = await mount(page), title = card(page, ids.Text).locator('.ms-node-title');
  const word = await textPoint(title, 1); await page.mouse.dblclick(word.x, word.y);
  expect((await state(page)).toggles).toEqual([]); expect(await page.evaluate(() => getSelection()?.toString())).toBe('Text');
  await page.evaluate(() => { const h = (window as any).inlineGesture; h.state.mode = 'select'; h.draw(); });
  const box = (await card(page, ids.Text).boundingBox())!;
  await page.mouse.dblclick(box.x + box.width / 2, box.y + box.height - 12);
  expect((await state(page)).toggles).toEqual([{ id: ids.Text, field: 'collapsed', value: false }]);
  await page.mouse.move(1200, 780); await page.mouse.down(); await page.mouse.move(1240, 815, { steps: 4 }); await page.mouse.up();
  expect((await state(page)).view).toEqual({ x: 40, y: 35, scale: 1 });
});

test('position locks stop drag previews and keyboard nudges while leaving text editing and disclosure available', async ({ page }) => {
  const ids = await mount(page, { mode: 'select' });
  await page.evaluate(id => { const test = (window as any).inlineGesture; test.state.locked = [id]; test.state.selections = [id]; test.draw(); }, ids.Text);
  const tile = card(page, ids.Text), box = (await tile.boundingBox())!;
  await page.mouse.move(box.x + box.width / 2, box.y + box.height - 12); await page.mouse.down();
  await page.mouse.move(box.x + box.width / 2 + 60, box.y + box.height + 20, { steps: 4 });
  expect(await tile.evaluate(tile => (tile as HTMLElement).style.transform)).toBe(''); await page.mouse.up();
  expect((await state(page)).moves).toEqual([]);
  await tile.focus(); await page.keyboard.press('ArrowRight'); expect((await state(page)).moves).toEqual([]);
  await page.evaluate(() => { const h = (window as any).inlineGesture; h.state.mode = 'text'; h.draw(); });
  await tile.locator('.ms-text').click(); expect((await state(page)).edits.at(-1)).toEqual({ id: ids.Text, title: false });
  await page.evaluate(() => { const h = (window as any).inlineGesture; h.state.mode = 'select'; h.draw(); });
  await page.mouse.dblclick(box.x + box.width / 2, box.y + box.height - 12); expect((await state(page)).toggles).toHaveLength(1);
});

test('double clicking a selected resize edge discloses a branch without a phantom resize', async ({ page }) => {
  const ids = await mount(page, { mode: 'select' });
  await page.evaluate(id => { const test = (window as any).inlineGesture; test.state.selections = [id]; test.draw(); }, ids.Text);
  const tile = card(page, ids.Text), box = (await tile.boundingBox())!;
  await page.mouse.dblclick(box.x + 2, box.y + 24);
  expect((await state(page)).toggles).toEqual([{ id: ids.Text, field: 'collapsed', value: false }]);
  await page.mouse.dblclick(box.x + 2, box.y + 24);
  expect((await state(page)).toggles).toEqual([{ id: ids.Text, field: 'collapsed', value: false }, { id: ids.Text, field: 'collapsed', value: true }]);
  expect((await state(page)).moves).toEqual([]);
});

test('active text owns typing, native wheel, and Gallery arrow keys; Ctrl-wheel still adjusts the camera', async ({ page }) => {
  const ids = await mount(page), body = card(page, ids.Gallery).locator('.ms-gallery-text');
  await body.click(); await page.keyboard.press('Home'); await page.keyboard.press('ArrowRight'); await page.keyboard.type('More ');
  expect((await state(page)).pages).toEqual([]); expect((await state(page)).moves).toEqual([]); await expect(body).toContainText('More');
  await body.dispatchEvent('wheel', { deltaY: 120, bubbles: true, cancelable: true }); expect((await state(page)).view.scale).toBe(1);
  await body.dispatchEvent('wheel', { deltaY: -120, ctrlKey: true, bubbles: true, cancelable: true, clientX: 180, clientY: 500 });
  expect((await state(page)).view.scale).toBeGreaterThan(1);
});

test('tool clicks and additive node selection retain their existing gestures on text surfaces', async ({ page }) => {
  for (const mode of ['node', 'link', 'delete', 'module', 'hand']) {
    const ids = await mount(page, { mode }), title = card(page, ids.Text).locator('.ms-node-title');
    await title.click(); const current = await state(page); expect(current.edits).toEqual([]);
    if (mode === 'module') expect(current.modules).toEqual([ids.Text]);
    else if (mode !== 'hand') expect(current.creates).toHaveLength(1);
    else expect(current.selections).toEqual([ids.Text]);
  }
  const ids = await mount(page, { mode: 'select' }); await card(page, ids.Text).locator('.ms-node-title').click({ modifiers: ['Control'] });
  expect((await state(page)).edits).toEqual([]); expect((await state(page)).selections).toEqual([ids.Text]);
});

test('Gallery edits use the child GUID and mirrored/decoded content cannot edit the wrong source', async ({ page }) => {
  const ids = await mount(page), gallery = card(page, ids.Gallery);
  await gallery.locator('.ms-gallery-item-title').click(); expect((await state(page)).edits.at(-1)).toEqual({ id: ids['First gallery child'], title: true });
  await gallery.locator('.ms-gallery-text').click(); expect((await state(page)).edits.at(-1)).toEqual({ id: ids['First gallery child'], title: false });
  await expect(gallery).toHaveAttribute('aria-selected', 'true');
  const reference = card(page, ids.Reference), attachment = card(page, ids.Attachment);
  await expect(reference.locator('.ms-text')).not.toHaveAttribute('data-editable-node-id');
  await expect(attachment.locator('.ms-text-preview')).not.toHaveAttribute('data-editable-node-id');
  await reference.locator('.ms-text').click(); expect((await state(page)).edits).toHaveLength(2);
  await reference.locator('.ms-node-title').click(); expect((await state(page)).edits.at(-1)).toEqual({ id: ids.Reference, title: true });
  const readonly = await mount(page, { readonly: true }); await card(page, readonly.Text).locator('.ms-text').click(); expect((await state(page)).edits).toEqual([]);
  await expect(page.locator('[data-editable-node-id]')).toHaveCount(0);
});

test('a Gallery reference to another Gallery keeps the mirrored descendants read only', async ({ page }) => {
  const ids = await mount(page);
  await page.evaluate(ids => {
    const test = (window as any).inlineGesture, first = test.project.nodes.find((node: any) => node.id === ids['First gallery child']);
    const source = { ...structuredClone(test.project.nodes.find((node: any) => node.id === ids.Gallery)), id: 'referenced-gallery', title: 'Referenced gallery', x: 500, y: 410 };
    const entry = { ...structuredClone(first), id: 'referenced-entry', title: 'Mirror-only entry', parentId: source.id };
    test.project.nodes.push(source, entry); Object.assign(first, { kind: 'reference', targetId: source.id }); test.draw();
  }, ids);
  const outer = card(page, ids.Gallery), mirrored = outer.locator('.ms-gallery[data-gallery-id="referenced-gallery"]');
  await expect(mirrored.locator('.ms-gallery-text')).toHaveText('Words remain selectable directly inside the node.');
  await expect(mirrored.locator('[data-editable-node-id]')).toHaveCount(0);
  await mirrored.locator('.ms-gallery-text').click(); expect((await state(page)).edits).toEqual([]);
});

test('right clicking active text opens canvas node actions using the canonical Gallery child identity', async ({ page }) => {
  const ids = await mount(page), body = card(page, ids.Gallery).locator('.ms-gallery-text');
  await body.click(); await body.click({ button: 'right' });
  expect((await state(page)).menus.at(-1).nodeId).toBe(ids['First gallery child']);
  const own = card(page, ids.Text).locator('.ms-text'); await own.click(); await own.click({ button: 'right' });
  expect((await state(page)).menus.at(-1).nodeId).toBe(ids.Text);
});

test('Shift-click adds inactive text nodes to canvas selection and Text extends native passages once editing is active', async ({ page }) => {
  const ids = await mount(page, { mode: 'select' }), body = card(page, ids.Text).locator('.ms-text');
  await card(page, ids.Text).locator('.ms-node-title').click({ modifiers: ['Shift'] });
  await card(page, ids.Reference).locator('.ms-node-title').click({ modifiers: ['Shift'] });
  expect((await state(page)).edits).toEqual([]); expect((await state(page)).selections.sort()).toEqual([ids.Text, ids.Reference].sort());
  await page.evaluate(() => { const h = (window as any).inlineGesture; h.state.mode = 'text'; h.draw(); });
  // Text explicitly owns the passage gesture, including Shift extension.
  const from = await textPoint(body, 12), to = await textPoint(body, 0);
  const bounds = (await card(page, ids.Text).boundingBox())!;
  expect(from.x - bounds.x).toBeGreaterThan(10);
  await page.mouse.click(from.x, from.y); await expect(body).toHaveAttribute('contenteditable', 'true');
  await page.keyboard.down('Shift'); await page.mouse.click(to.x, to.y); await page.keyboard.up('Shift');
  expect((await state(page)).selections).toEqual([ids.Text]); expect((await state(page)).edits).toEqual([{ id: ids.Text, title: false }]);
  expect(await page.evaluate(() => getSelection()?.toString())).toContain('Words');
  expect((await state(page)).moves).toEqual([]);
});
