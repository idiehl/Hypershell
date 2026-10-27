import { test, expect, type Page, type Locator, type APIRequestContext } from '@playwright/test';
import { readFile, writeFile } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';
import { addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import type { MeridianProject, MeridianNode, TextRun, TextFormat } from '../src/core/types.ts';
import { buildPublication } from '../src/publication.ts';
import { interactionTool, selectEditorNode } from './ui-helpers.ts';

const card = (page: Page, id: string) => page.locator(`.ms-node[data-node-id="${id}"]`);
const body = (page: Page, id: string) => page.locator(`[data-editable-node-id="${id}"][data-editable-text="body"]`);
const heading = (page: Page, id: string) => page.locator(`[data-editable-node-id="${id}"][data-editable-text="title"]`);
const floating = (page: Page) => page.locator('.canvas-inline-toolbar');
/** Editing is explicit; Escape returns to Single Select. */
async function textTool(page: Page) { const tool = page.getByRole('button', { name: 'Text', exact: true }); if (await tool.getAttribute('aria-pressed') !== 'true') await tool.click(); }
const read = async (request: APIRequestContext, id: string) => await (await request.get(`/api/projects/${id}`)).json() as MeridianProject;
const styleAt = (runs: TextRun[] | undefined, offset: number): TextFormat => runs?.find(run => run.start <= offset && run.end > offset)?.style ?? {};

function fixture() {
  const project = newProject('Canvas passage editing');
  project.canvases[0]!.snapping = { stickyEdges: false, smartAlign: false, snapToGrid: false };
  const text = addNode(project, project.entryCanvasId, 'media', 'Alpha beta gamma');
  Object.assign(text, { x: 70, y: 65, width: 440, height: 260, sizeMode: 'fixed', contentType: 'text', childPosition: 'manual', text: 'Alpha beta gamma.\nSecond paragraph.', style: { shape: 'rectangle', fontSize: 20, titleSize: 26, contentSizing: 'fit', textAlign: 'center' } });
  const reference = addNode(project, project.entryCanvasId, 'reference', 'Reference stays read only');
  Object.assign(reference, { x: 560, y: 65, width: 340, height: 260, sizeMode: 'fixed', targetId: text.id });
  const circle = addNode(project, project.entryCanvasId, 'media', 'Rotated passage');
  Object.assign(circle, { x: 70, y: 375, width: 280, height: 280, sizeMode: 'fixed', contentType: 'text', text: 'Words follow the camera.', style: { shape: 'circle', textRotation: 35, fontSize: 20, titleSize: 22 } });
  const parent = addNode(project, project.entryCanvasId, 'container', 'A bounded collection');
  Object.assign(parent, { x: 400, y: 385, width: 270, height: 220, sizeMode: 'fixed', collapsed: true, containerVariant: 'vertical', childLayout: 'contained', childArrangement: 'column' });
  const child = addNode(project, project.entryCanvasId, 'media', 'Nested passage');
  Object.assign(child, { x: 420, y: 450, width: 220, height: 120, sizeMode: 'fixed', parentId: parent.id, contentType: 'text', text: 'A hidden descendant.' });
  const gallery = addNode(project, project.entryCanvasId, 'container', 'Gallery collection');
  Object.assign(gallery, { x: 740, y: 375, width: 340, height: 260, sizeMode: 'fixed', collapsed: false, containerVariant: 'gallery', childLayout: 'contained', childArrangement: 'free' });
  const galleryChild = addNode(project, project.entryCanvasId, 'media', 'Gallery passage');
  Object.assign(galleryChild, { x: 745, y: 420, width: 250, height: 140, sizeMode: 'fixed', parentId: gallery.id, contentType: 'text', text: 'One item at a time.' });
  return { project: commitSnapshots(project), text, reference, circle, parent, child, gallery, galleryChild };
}

async function open(page: Page, request: APIRequestContext) {
  const source = fixture(), response = await request.post('/api/projects', { data: source.project });
  expect(response.status(), await response.text()).toBe(201); const saved = await response.json() as MeridianProject;
  const errors: string[] = []; page.on('pageerror', error => errors.push(error.message));
  await page.goto(`/?project=${saved.id}`); await expect(card(page, source.text.id)).toBeVisible();
  return { ...source, saved, errors };
}
async function savedNode(request: APIRequestContext, projectId: string, nodeId: string) {
  return (await read(request, projectId)).nodes.find(node => node.id === nodeId)!;
}

/** Measure character boxes, then use real native pointer selection from the
 * inactive canvas surface. No synthetic Selection or editor activation API. */
async function selectByPointer(page: Page, surface: Locator, start: number, end: number) {
  await textTool(page);
  const points = await surface.evaluate((root, offsets) => {
    const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT), pieces: { node: Node; start: number; end: number }[] = [];
    let node: Node | null, at = 0;
    while ((node = walker.nextNode())) { const next = at + (node.textContent?.length ?? 0); pieces.push({ node, start: at, end: next }); at = next; }
    const bounds = (offset: number) => {
      const item = pieces.find(item => offset >= item.start && offset < item.end)!;
      const range = document.createRange(); range.setStart(item.node, offset - item.start); range.setEnd(item.node, offset - item.start + 1);
      const rect = range.getBoundingClientRect(); return { left: rect.left, right: rect.right, y: rect.top + rect.height / 2 };
    };
    return { a: bounds(offsets.start), b: bounds(offsets.end - 1) };
  }, { start, end });
  await page.mouse.move(points.a.left + .2, points.a.y); await page.mouse.down();
  await page.mouse.move(points.b.right - .2, points.b.y, { steps: 12 }); await page.mouse.up();
  await expect.poll(() => page.evaluate(() => document.getSelection()?.toString())).toBe((await surface.textContent())!.slice(start, end));
  await expect(surface).toHaveAttribute('contenteditable', 'true'); await expect(floating(page)).toBeVisible();
}
async function font(page: Page, name: string) {
  await floating(page).getByRole('combobox', { name: 'Selection font', exact: true }).click();
  const popup = page.locator('.font-picker-popup:popover-open');
  await popup.getByRole('searchbox', { name: 'Search fonts', exact: true }).fill(name);
  await popup.getByRole('option', { name, exact: true }).click();
}
async function assertNoSelectionBar(page: Page) {
  await expect(page.locator('.selection-bar,.selection-count')).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Download selection', exact: true })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Clear selection', exact: true })).toHaveCount(0);
}

test('a first pointer gesture selects canvas words and the floating font, size, color and emphasis controls preserve that passage', async ({ page, request }, info) => {
  const source = await open(page, request); await assertNoSelectionBar(page);
  await selectByPointer(page, body(page, source.text.id), 6, 10);
  await expect(card(page, source.text.id)).toHaveAttribute('aria-selected', 'true');
  for (const name of ['Bold', 'Italic', 'Underline']) await floating(page).getByRole('button', { name, exact: true }).click();
  await font(page, 'Georgia');
  const size = floating(page).getByRole('spinbutton', { name: 'Selection size', exact: true }); await size.fill('28'); await size.press('Tab');
  const color = floating(page).getByLabel('Selection color', { exact: true }); await color.fill('#ffd18a'); await color.press('Tab');
  const beta = { fontFamily: 'georgia', fontSize: 28, color: '#ffd18a', bold: true, italic: true, underline: true };
  await expect.poll(async () => styleAt((await savedNode(request, source.saved.id, source.text.id)).textRuns, 6)).toEqual(beta);
  const after = await savedNode(request, source.saved.id, source.text.id);
  expect(after.text).toBe(source.text.text); expect(styleAt(after.textRuns, 0)).toEqual({}); expect(styleAt(after.textRuns, 11)).toEqual({});
  const glyphs = card(page, source.text.id).locator('.ms-text-run').filter({ hasText: /^beta$/ });
  await expect(glyphs).toHaveCSS('font-style', 'italic'); await expect(glyphs).toHaveCSS('text-decoration-line', 'underline');
  await page.screenshot({ path: info.outputPath('native-canvas-selection-floating-toolbar.png') });
  await page.keyboard.press('Escape'); await expect(floating(page)).toHaveCount(0);
  await expect(card(page, source.text.id)).toHaveAttribute('aria-selected', 'false');
  await page.reload(); expect((await savedNode(request, source.saved.id, source.text.id)).textRuns).toEqual(after.textRuns);
  expect(source.errors).toEqual([]);
});

test('direct title and body typing, newline, text undo and composition autosave; Escape exits editing and canvas copying stays separate', async ({ page, request }, info) => {
  const source = await open(page, request);
  await textTool(page); await body(page, source.text.id).click(); await body(page, source.text.id).press('Control+a');
  await page.keyboard.insertText('First line'); await page.keyboard.press('Enter'); await page.keyboard.insertText('Second line');
  await expect.poll(async () => (await savedNode(request, source.saved.id, source.text.id)).text).toBe('First line\nSecond line');
  await body(page, source.text.id).press('Control+z');
  await expect.poll(async () => (await savedNode(request, source.saved.id, source.text.id)).text).not.toBe('First line\nSecond line');
  await body(page, source.text.id).press('Control+Shift+z');
  await expect.poll(async () => (await savedNode(request, source.saved.id, source.text.id)).text).toBe('First line\nSecond line');
  await page.keyboard.press('Escape'); await textTool(page); await heading(page, source.text.id).click(); await heading(page, source.text.id).press('Control+a');
  await expect(heading(page, source.text.id)).toHaveAttribute('contenteditable', 'true');
  await page.keyboard.insertText('A new title'); await expect(heading(page, source.text.id)).toHaveText('A new title'); await page.keyboard.press('Escape');
  await expect.poll(async () => (await savedNode(request, source.saved.id, source.text.id)).title).toBe('A new title');
  await expect(card(page, source.text.id)).toHaveAttribute('aria-selected', 'false');
  await textTool(page); await heading(page, source.text.id).click(); await heading(page, source.text.id).press('End'); await page.keyboard.press('Backspace'); await page.keyboard.press('Escape');
  await expect.poll(async () => (await savedNode(request, source.saved.id, source.text.id)).title).toBe('A new titl');
  await textTool(page); await heading(page, source.text.id).click(); await heading(page, source.text.id).press('End'); await page.keyboard.insertText('e'); await page.keyboard.press('Escape');
  await expect.poll(async () => (await savedNode(request, source.saved.id, source.text.id)).title).toBe('A new title');
  // Switch immediately from a sidebar edit before its 150 ms debounce expires.
  // Canvas activation must adopt that pending value, not the stale scene text.
  await selectEditorNode(page, source.circle.id, source.circle.title);
  const sidebar = page.locator('.inspector .rich-text-editor').getByRole('textbox', { name: 'Text body', exact: true });
  await sidebar.fill('Pending sidebar passage'); await textTool(page); await body(page, source.circle.id).click(); await body(page, source.circle.id).press('Control+End');
  await page.keyboard.insertText(' + canvas'); await page.keyboard.press('Escape');
  await expect.poll(async () => (await savedNode(request, source.saved.id, source.circle.id)).text).toBe('Pending sidebar passage + canvas');
  // IME composition must not emit intermediate canvas state or interpret its
  // keystrokes as canvas commands. This exercises the native event sequence.
  await textTool(page); await body(page, source.text.id).click(); await body(page, source.text.id).press('Control+End');
  await body(page, source.text.id).evaluate(root => {
    root.dispatchEvent(new CompositionEvent('compositionstart', { bubbles: true, data: '' }));
    root.append(document.createTextNode(' 日本語'));
    root.dispatchEvent(new InputEvent('input', { bubbles: true, inputType: 'insertCompositionText', data: ' 日本語', isComposing: true }));
    root.dispatchEvent(new CompositionEvent('compositionend', { bubbles: true, data: ' 日本語' }));
  });
  await page.keyboard.press('Escape');
  await expect.poll(async () => (await savedNode(request, source.saved.id, source.text.id)).text).toBe('First line\nSecond line 日本語');
  await card(page, source.text.id).click({ position: { x: 2, y: 20 } }); await card(page, source.text.id).focus();
  await page.keyboard.press('Control+c'); await page.keyboard.press('Control+v');
  let copied: MeridianProject;
  await expect.poll(async () => { copied = await read(request, source.saved.id); return copied.nodes.length; }).toBe(source.saved.nodes.length + 1);
  const copy = copied!.nodes.find(node => !source.saved.nodes.some(old => old.id === node.id))!;
  expect(copy.id).not.toBe(source.text.id); expect(copy.text).toBe('First line\nSecond line 日本語'); expect(copy.title).toBe('A new title');
  await page.screenshot({ path: info.outputPath('direct-edit-and-independent-copy.png') });
  expect(source.errors).toEqual([]);
});

test('floating editing follows rotated text and zoom; references remain read only and Gallery edits affect their canonical child', async ({ page, request, browser }, info) => {
  const source = await open(page, request);
  await textTool(page); await body(page, source.circle.id).click(); await body(page, source.circle.id).press('Control+a'); await expect(floating(page)).toBeVisible();
  await floating(page).getByRole('button', { name: 'Italic', exact: true }).click();
  await expect.poll(async () => styleAt((await savedNode(request, source.saved.id, source.circle.id)).textRuns, 0)).toMatchObject({ italic: true });
  const camera = await page.locator('.ms-viewport').boundingBox();
  await page.mouse.move(camera!.x + camera!.width / 2, camera!.y + camera!.height / 2); await page.keyboard.down('Control'); await page.mouse.wheel(0, -100); await page.keyboard.up('Control');
  const bounds = await floating(page).boundingBox(); expect(bounds).not.toBeNull();
  expect(bounds!.x).toBeGreaterThanOrEqual(0); expect(bounds!.y).toBeGreaterThanOrEqual(0);
  expect(bounds!.x + bounds!.width).toBeLessThanOrEqual(1501); expect(bounds!.y + bounds!.height).toBeLessThanOrEqual(951);
  await page.screenshot({ path: info.outputPath('rotated-selection-toolbar.png') }); await page.keyboard.press('Escape');
  const referenced = card(page, source.reference.id).locator('.ms-text'); await referenced.click();
  await expect(referenced).not.toHaveAttribute('contenteditable', 'true'); await expect(floating(page)).toHaveCount(0);
  await page.keyboard.press('Escape');
  await textTool(page); await body(page, source.galleryChild.id).click(); await body(page, source.galleryChild.id).press('Control+a');
  await page.keyboard.insertText('Edited inside the gallery.'); await page.keyboard.press('Escape');
  await expect.poll(async () => (await savedNode(request, source.saved.id, source.galleryChild.id)).text).toBe('Edited inside the gallery.');
  expect((await savedNode(request, source.saved.id, source.gallery.id)).text).toBe(source.gallery.text);
  const saved = await read(request, source.saved.id), filename = info.outputPath('inline-text-offline.html');
  await writeFile(filename, buildPublication(saved, await readFile('dist/viewer.js', 'utf8'), await readFile('dist/scene.css', 'utf8')));
  const context = await browser.newContext({ offline: true });
  try {
    const viewer = await context.newPage(); await viewer.goto(pathToFileURL(filename).href);
    await expect(viewer.locator('[contenteditable="true"],.canvas-inline-toolbar')).toHaveCount(0);
    await expect(viewer.locator('.ms-gallery-text')).toHaveText('Edited inside the gallery.');
    await expect(card(viewer, source.circle.id).locator('.ms-text-run')).toHaveCSS('font-style', 'italic');
  } finally { await context.close(); }
  expect(source.errors).toEqual([]);
});

test('backdrop dragging and disclosure still work, creation and Module tools reserve text clicks, and Escape clears canvas selections', async ({ page, request }, info) => {
  const source = await open(page, request); const before = await card(page, source.text.id).boundingBox();
  await page.mouse.move(before!.x + 2, before!.y + 20); await page.mouse.down(); await page.mouse.move(before!.x + 32, before!.y + 32, { steps: 10 }); await page.mouse.up();
  await expect.poll(async () => (await savedNode(request, source.saved.id, source.text.id)).x).not.toBe(source.text.x);
  await expect(floating(page)).toHaveCount(0);
  await textTool(page); await body(page, source.text.id).click(); await body(page, source.text.id).press('Control+End'); await page.keyboard.insertText(' Pending drag');
  await interactionTool(page);
  const worldBefore = await page.locator('.ms-world').getAttribute('style'), view = (await page.locator('.ms-viewport').boundingBox())!;
  await page.mouse.move(view.x + view.width - 65, view.y + view.height - 85); await page.mouse.down();
  await page.mouse.move(view.x + view.width - 105, view.y + view.height - 115, { steps: 8 }); await page.mouse.up();
  await expect.poll(() => page.locator('.ms-world').getAttribute('style')).not.toBe(worldBefore);
  await expect.poll(async () => (await savedNode(request, source.saved.id, source.text.id)).text?.endsWith(' Pending drag')).toBe(true);
  await expect(floating(page)).toHaveCount(0);
  await card(page, source.parent.id).dblclick({ position: { x: 2, y: 20 } }); await expect(card(page, source.child.id)).toBeVisible();
  await card(page, source.parent.id).dblclick({ position: { x: 2, y: 20 } }); await expect(card(page, source.child.id)).toHaveCount(0);
  await page.getByRole('button', { name: 'New Node', exact: true }).click(); await page.getByRole('menuitem', { name: 'Link', exact: true }).click();
  await card(page, source.text.id).locator('.ms-node-title').click(); await card(page, source.reference.id).locator('.ms-node-title').click();
  await expect.poll(async () => (await read(request, source.saved.id)).links.some(link => link.sourceId === source.text.id && link.targetId === source.reference.id)).toBe(true);
  await expect(floating(page)).toHaveCount(0); await interactionTool(page);
  await page.getByRole('button', { name: 'New Node', exact: true }).click(); await page.getByRole('menuitem', { name: 'Module', exact: true }).click();
  await card(page, source.parent.id).locator('.ms-node-title').click();
  await expect(page.locator(`[data-module-selection-node="${source.child.id}"] input[type="checkbox"]`)).toBeChecked();
  await expect(floating(page)).toHaveCount(0); await expect(page.getByRole('button', { name: 'Create From Selection', exact: true })).toBeEnabled();
  await page.keyboard.press('Escape'); await expect(page.getByRole('button', { name: 'Create From Selection', exact: true })).toBeDisabled();
  await assertNoSelectionBar(page); await page.screenshot({ path: info.outputPath('canvas-without-selection-bar.png') }); expect(source.errors).toEqual([]);
});

test('right-click position locking persists and protects drag while text remains editable; unlocking restores movement', async ({ page, request }, info) => {
  const source = await open(page, request);
  await card(page, source.text.id).click({ button: 'right', position: { x: 2, y: 20 } });
  await page.getByRole('menuitem', { name: 'Lock Position', exact: true }).click();
  await expect.poll(async () => (await savedNode(request, source.saved.id, source.text.id)).positionLocked).toBe(true);
  const locked = await savedNode(request, source.saved.id, source.text.id), bounds = await card(page, source.text.id).boundingBox();
  await page.mouse.move(bounds!.x + 2, bounds!.y + 20); await page.mouse.down(); await page.mouse.move(bounds!.x + 42, bounds!.y + 42, { steps: 10 }); await page.mouse.up();
  await card(page, source.text.id).focus(); await page.keyboard.press('ArrowRight');
  const unchanged = await savedNode(request, source.saved.id, source.text.id); expect([unchanged.x, unchanged.y]).toEqual([locked.x, locked.y]);
  await textTool(page); await body(page, source.text.id).click(); await body(page, source.text.id).press('Control+a'); await page.keyboard.insertText('A locked node can still be edited.'); await page.keyboard.press('Escape');
  await expect.poll(async () => (await savedNode(request, source.saved.id, source.text.id)).text).toBe('A locked node can still be edited.');
  await page.reload(); expect((await savedNode(request, source.saved.id, source.text.id)).positionLocked).toBe(true);
  await card(page, source.text.id).click({ button: 'right', position: { x: 2, y: 20 } }); await page.getByRole('menuitem', { name: 'Unlock Position', exact: true }).click();
  const start = await card(page, source.text.id).boundingBox();
  await page.mouse.move(start!.x + 2, start!.y + 20); await page.mouse.down(); await page.mouse.move(start!.x + 32, start!.y + 32, { steps: 10 }); await page.mouse.up();
  await expect.poll(async () => (await savedNode(request, source.saved.id, source.text.id)).x).not.toBe(locked.x);
  await page.screenshot({ path: info.outputPath('position-lock-context-menu-and-editing.png') }); expect(source.errors).toEqual([]);
});

