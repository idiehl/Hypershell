import { test, expect, type APIRequestContext, type Page, type Locator } from '@playwright/test';
import { readFile, writeFile } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';
import { addCanvas, addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import type { MeridianNode, MeridianProject, StoredModule, TextFormat, TextRun } from '../src/core/types.ts';
import { buildPublication } from '../src/publication.ts';
import { inspectorPanel, selectEditorNode } from './ui-helpers.ts';

const tile = (page: Page, id: string) => page.locator(`.ms-node[data-node-id="${id}"]`);
const stored = async (request: APIRequestContext, id: string) => await (await request.get(`/api/projects/${id}`)).json() as MeridianProject;
const styleAt = (runs: TextRun[] | undefined, offset: number): TextFormat => runs?.find(run => run.start <= offset && run.end > offset)?.style ?? {};

function fixture(formatted = false) {
  const project = newProject('Passage formatting QA');
  const add = (title: string, x: number, y: number, width = 400, height = 260) => {
    const node = addNode(project, project.entryCanvasId, 'media', title);
    Object.assign(node, { x, y, width, height, sizeMode: 'fixed', childPosition: 'manual', contentType: 'text',
      style: { shape: 'rectangle', fontFamily: 'sans', fontSize: 18, titleSize: 24, textAlign: 'center', contentSizing: 'fit' } });
    return node;
  };
  const text = add('Alpha beta gamma', 70, 70);
  text.text = 'Alpha beta gamma.\nSecond paragraph.';
  const local = addNode(project, project.entryCanvasId, 'reference', 'Linked source');
  Object.assign(local, { targetId: text.id, x: 570, y: 70, width: 390, height: 260, sizeMode: 'fixed' });
  const circle = add('Text in a circle', 70, 410, 280, 280);
  circle.text = 'Big and small passages wrap together.';
  Object.assign(circle.style!, { shape: 'circle', textRotation: 35 });
  if (formatted) {
    text.titleRuns = [{ start: 0, end: 5, style: { color: '#87e1c6', underline: true } }];
    text.textRuns = [{ start: 6, end: 10, style: { fontFamily: 'georgia', fontSize: 28, color: '#ffd18a', bold: true, italic: true, underline: true } }];
    circle.textRuns = [{ start: 0, end: 3, style: { fontSize: 36, bold: true } }, { start: 8, end: 13, style: { fontFamily: 'consolas', color: '#ffd18a' } }];
  }
  const remoteCanvas = addCanvas(project, 'Saved text references');
  const pinned = addNode(project, remoteCanvas.id, 'reference', 'Saved source');
  Object.assign(pinned, { targetId: text.id, x: 80, y: 90, width: 440, height: 280, sizeMode: 'fixed' });
  return { project: commitSnapshots(project), text, local, circle, pinned, remoteCanvas };
}

async function openFixture(page: Page, request: APIRequestContext, formatted = false) {
  const source = fixture(formatted), response = await request.post('/api/projects', { data: source.project });
  expect(response.status(), await response.text()).toBe(201); const saved = await response.json() as MeridianProject;
  await page.goto(`/?project=${saved.id}&canvas=${saved.entryCanvasId}`);
  await expect(tile(page, source.text.id)).toBeVisible(); return { ...source, saved };
}

/** Exercise browser Selection across the editor's real text nodes. Formatting
 * buttons and pickers then receive normal focus/clicks, catching lost selections. */
async function selectPassage(editor: Locator, start: number, end: number) {
  await editor.evaluate((root, offsets) => {
    (root as HTMLElement).focus();
    const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT), points: { node: Node; start: number; end: number }[] = [];
    let text: Node | null, length = 0;
    while ((text = walker.nextNode())) { const next = length + (text.textContent?.length ?? 0); points.push({ node: text, start: length, end: next }); length = next; }
    function point(offset: number): [Node, number] {
      const item = points.find(item => item.end >= offset); if (item) return [item.node, offset - item.start];
      return [root, root.childNodes.length];
    }
    const range = document.createRange(), [a, x] = point(offsets.start), [b, y] = point(offsets.end);
    range.setStart(a, x); range.setEnd(b, y);
    const selection = window.getSelection()!; selection.removeAllRanges(); selection.addRange(range);
    document.dispatchEvent(new Event('selectionchange'));
  }, { start, end });
}

async function assertTextInside(card: Locator) {
  const layout = await card.evaluate(node => {
    const frame = node.getBoundingClientRect(), ranges: { x: number; y: number; width: number; height: number }[] = [];
    for (const block of node.querySelectorAll('.ms-node-title,.ms-text,.ms-text-preview')) {
      const range = document.createRange(); range.selectNodeContents(block);
      for (const rect of range.getClientRects()) if (rect.width > 0 && rect.height > 0) ranges.push({ x: rect.x, y: rect.y, width: rect.width, height: rect.height });
    }
    return { frame: { x: frame.x, y: frame.y, width: frame.width, height: frame.height }, ranges };
  });
  expect(layout.ranges.length).toBeGreaterThan(0);
  for (const bounds of layout.ranges) {
    expect(bounds.x).toBeGreaterThanOrEqual(layout.frame.x - 1);
    expect(bounds.y).toBeGreaterThanOrEqual(layout.frame.y - 1);
    expect(bounds.x + bounds.width).toBeLessThanOrEqual(layout.frame.x + layout.frame.width + 1);
    expect(bounds.y + bounds.height).toBeLessThanOrEqual(layout.frame.y + layout.frame.height + 1);
  }
}

async function editorFor(page: Page, node: MeridianNode) {
  await selectEditorNode(page, node.id, node.title);
  const editor = page.locator('.inspector .rich-text-editor');
  await expect(editor.getByRole('textbox', { name: 'Text body', exact: true })).toBeVisible();
  return editor;
}
async function formattedNode(request: APIRequestContext, projectId: string, nodeId: string) {
  return (await stored(request, projectId)).nodes.find(node => node.id === nodeId)!;
}
async function selectionFont(page: Page, editor: Locator, name: string) {
  await editor.getByRole('combobox', { name: 'Selection font', exact: true }).click();
  const popup = page.locator('.font-picker-popup:popover-open');
  await expect(popup).toBeVisible();
  await popup.getByRole('searchbox', { name: 'Search fonts', exact: true }).fill(name);
  await popup.getByRole('option', { name, exact: true }).click();
}

test('individual word formatting preserves the browser selection through font, size and color controls, with mixed states and partial clear', async ({ page, request }, info) => {
  const source = await openFixture(page, request), editor = await editorFor(page, source.text);
  const body = editor.getByRole('textbox', { name: 'Text body', exact: true });
  await selectPassage(body, 6, 10);
  await editor.getByRole('button', { name: 'Bold', exact: true }).click();
  await editor.getByRole('button', { name: 'Italic', exact: true }).click();
  await editor.getByRole('button', { name: 'Underline', exact: true }).click();
  await selectionFont(page, editor, 'Georgia');
  const size = editor.getByRole('spinbutton', { name: 'Selection size', exact: true });
  await size.fill('28'); await size.press('Tab');
  const color = editor.getByLabel('Selection color', { exact: true }); await color.fill('#ffd18a'); await color.press('Tab');
  const beta = { fontFamily: 'georgia', fontSize: 28, color: '#ffd18a', bold: true, italic: true, underline: true };
  await expect.poll(async () => styleAt((await formattedNode(request, source.saved.id, source.text.id)).textRuns, 6)).toEqual(beta);
  let node = await formattedNode(request, source.saved.id, source.text.id);
  expect(node.text).toBe(source.text.text); expect(node.titleRuns).toBeUndefined(); expect(styleAt(node.textRuns, 0)).toEqual({}); expect(styleAt(node.textRuns, 11)).toEqual({});
  const renderedBeta = tile(page, source.text.id).locator('.ms-text-run').filter({ hasText: /^beta$/ });
  await expect(renderedBeta).toHaveCSS('font-style', 'italic'); await expect(renderedBeta).toHaveCSS('text-decoration-line', 'underline');
  await selectPassage(body, 0, 10);
  for (const name of ['Bold', 'Italic', 'Underline']) await expect(editor.getByRole('button', { name, exact: true })).toHaveAttribute('aria-pressed', 'mixed');
  await page.screenshot({ path: info.outputPath('selected-passage-formatting-and-mixed-state.png') });
  await selectPassage(body, 3, 8); await editor.getByRole('button', { name: 'Clear formatting', exact: true }).click();
  await expect.poll(async () => styleAt((await formattedNode(request, source.saved.id, source.text.id)).textRuns, 6)).toEqual({});
  node = await formattedNode(request, source.saved.id, source.text.id); expect(styleAt(node.textRuns, 8)).toEqual(beta);
  expect(node.textRuns).toEqual([{ start: 8, end: 10, style: beta }]);
  await page.reload(); const reopened = await editorFor(page, source.text);
  await selectPassage(reopened.getByRole('textbox', { name: 'Text body', exact: true }), 8, 10);
  await expect(reopened.getByRole('combobox', { name: 'Selection font', exact: true })).toContainText('Georgia');
  await expect(reopened.getByRole('spinbutton', { name: 'Selection size', exact: true })).toHaveValue('28');
  await expect(reopened.getByRole('button', { name: 'Bold', exact: true })).toHaveAttribute('aria-pressed', 'true');
});

test('typing rebases formatted passages, keyboard formatting and text history stay inside the editor, and pasted HTML remains literal', async ({ page, request }, info) => {
  const source = await openFixture(page, request, true), editor = await editorFor(page, source.text);
  const body = editor.getByRole('textbox', { name: 'Text body', exact: true });
  const baseline = await formattedNode(request, source.saved.id, source.text.id);
  await selectPassage(body, 6, 10); await page.keyboard.insertText('theta');
  await expect.poll(async () => (await formattedNode(request, source.saved.id, source.text.id)).text).toBe('Alpha theta gamma.\nSecond paragraph.');
  let edited = await formattedNode(request, source.saved.id, source.text.id);
  expect(edited.textRuns).toEqual([{ start: 6, end: 11, style: source.text.textRuns![0].style }]);
  await body.press('Control+z');
  await expect.poll(async () => (await formattedNode(request, source.saved.id, source.text.id)).text).toBe(baseline.text);
  expect((await formattedNode(request, source.saved.id, source.text.id)).textRuns).toEqual(baseline.textRuns);
  await body.press('Control+Shift+z');
  await expect.poll(async () => (await formattedNode(request, source.saved.id, source.text.id)).text).toBe('Alpha theta gamma.\nSecond paragraph.');
  await selectPassage(body, 0, 5); await body.press('Control+b'); await body.press('Control+i'); await body.press('Control+u');
  await expect.poll(async () => styleAt((await formattedNode(request, source.saved.id, source.text.id)).textRuns, 0)).toMatchObject({ bold: true, italic: true, underline: true });
  await selectPassage(body, 5, 5); await body.press('Enter'); await page.keyboard.insertText('A new line');
  await expect.poll(async () => (await formattedNode(request, source.saved.id, source.text.id)).text).toBe('Alpha\nA new line theta gamma.\nSecond paragraph.');
  edited = await formattedNode(request, source.saved.id, source.text.id);
  expect(styleAt(edited.textRuns, edited.text!.indexOf('theta'))).toEqual(source.text.textRuns![0].style);
  await selectPassage(body, edited.text!.length, edited.text!.length);
  const literal = '<img src=x onerror="window.__richTextInjected=true"><script>window.__richTextInjected=true</script>';
  await body.evaluate((root, text) => {
    const transfer = new DataTransfer(); transfer.setData('text/plain', text); transfer.setData('text/html', '<strong>Unexpected pasted styling</strong>' + text);
    root.dispatchEvent(new ClipboardEvent('paste', { bubbles: true, cancelable: true, clipboardData: transfer }));
  }, literal);
  await expect.poll(async () => (await formattedNode(request, source.saved.id, source.text.id)).text?.endsWith(literal)).toBe(true);
  expect(await page.evaluate(() => (window as Window & { __richTextInjected?: boolean }).__richTextInjected)).toBeUndefined();
  expect(await body.locator('img,script,strong').count()).toBe(0); expect(await tile(page, source.text.id).locator('.ms-text img,.ms-text script').count()).toBe(0);
  await editor.getByRole('button', { name: 'Undo text edit', exact: true }).click();
  await expect.poll(async () => (await formattedNode(request, source.saved.id, source.text.id)).text?.endsWith(literal)).toBe(false);
  await editor.getByRole('button', { name: 'Redo text edit', exact: true }).click();
  await expect.poll(async () => (await formattedNode(request, source.saved.id, source.text.id)).text?.endsWith(literal)).toBe(true);
  await page.screenshot({ path: info.outputPath('plain-paste-and-text-history.png') });
  // After leaving the editor, the ordinary canvas history still works and does
  // not mistake a text-selection shortcut for a copied canvas node.
  await inspectorPanel(page, 'properties');
  await page.getByRole('button', { name: 'Undo', exact: true }).click();
  await expect.poll(async () => (await formattedNode(request, source.saved.id, source.text.id)).text?.endsWith(literal)).toBe(false);
  expect((await stored(request, source.saved.id)).nodes).toHaveLength(source.saved.nodes.length);
});

test('title and expanded text editors format passages independently, and newly created nodes keep inline formatting', async ({ page, request }, info) => {
  const source = await openFixture(page, request); await editorFor(page, source.text);
  await page.getByRole('button', { name: 'Format title', exact: true }).click();
  const titleDialog = page.getByRole('dialog', { name: 'Format title', exact: true }); await expect(titleDialog).toBeVisible();
  const titleEditor = titleDialog.locator('.rich-text-editor');
  await selectPassage(titleEditor.getByRole('textbox', { name: 'Title text', exact: true }), 0, 5);
  await titleEditor.getByRole('button', { name: 'Italic', exact: true }).click();
  await titleEditor.getByRole('button', { name: 'Underline', exact: true }).click();
  await selectionFont(page, titleEditor, 'Times New Roman');
  await titleEditor.getByLabel('Selection color', { exact: true }).fill('#87e1c6');
  await expect.poll(async () => styleAt((await formattedNode(request, source.saved.id, source.text.id)).titleRuns, 0)).toMatchObject({ fontFamily: 'times-new-roman', color: '#87e1c6', italic: true, underline: true });
  expect((await formattedNode(request, source.saved.id, source.text.id)).textRuns).toBeUndefined();
  await titleDialog.getByRole('button', { name: 'Close text editor', exact: true }).click();

  await page.getByRole('button', { name: 'Open text editor', exact: true }).click();
  const textDialog = page.getByRole('dialog', { name: 'Edit text', exact: true }); await expect(textDialog).toBeVisible();
  const textEditor = textDialog.locator('.rich-text-editor');
  await selectPassage(textEditor.getByRole('textbox', { name: 'Text body', exact: true }), 18, 24);
  await textEditor.getByRole('button', { name: 'Bold', exact: true }).click();
  const size = textEditor.getByRole('spinbutton', { name: 'Selection size', exact: true }); await size.fill('32'); await size.press('Tab');
  await expect.poll(async () => styleAt((await formattedNode(request, source.saved.id, source.text.id)).textRuns, 18)).toMatchObject({ bold: true, fontSize: 32 });
  await page.screenshot({ path: info.outputPath('expanded-text-editor.png') });
  await textDialog.getByRole('button', { name: 'Close text editor', exact: true }).click();
  const beforeCreate = await stored(request, source.saved.id);
  await page.getByRole('button', { name: 'New Node', exact: true }).click(); await page.getByRole('menuitem', { name: 'Node', exact: true }).click();
  const viewport = (await page.locator('.ms-viewport').boundingBox())!; await page.mouse.click(viewport.x + 960, viewport.y + 540);
  const creation = page.getByRole('dialog', { name: 'New Node', exact: true }); await expect(creation).toBeVisible();
  await creation.getByLabel('Type', { exact: true }).selectOption('media'); await creation.getByLabel('Content', { exact: true }).selectOption('text');
  await creation.getByLabel('Title', { exact: true }).fill('Rich new node');
  const creationEditor = creation.locator('.rich-text-editor');
  const body = creationEditor.getByRole('textbox', { name: 'Text body', exact: true }); await body.fill('Plain vivid ending');
  await selectPassage(body, 6, 11);
  await creationEditor.getByRole('button', { name: 'Bold', exact: true }).click(); await creationEditor.getByRole('button', { name: 'Underline', exact: true }).click();
  await selectionFont(page, creationEditor, 'Calibri');
  await creationEditor.getByLabel('Selection color', { exact: true }).fill('#ffd18a');
  await creationEditor.getByLabel('Selection color', { exact: true }).press('Tab');
  await page.screenshot({ path: info.outputPath('new-node-with-formatted-word.png') });
  await creation.getByRole('button', { name: 'Create node', exact: true }).click();
  let after: MeridianProject;
  await expect.poll(async () => { after = await stored(request, source.saved.id); return after.nodes.length; }).toBe(beforeCreate.nodes.length + 1);
  const newNode = after!.nodes.find(node => node.title === 'Rich new node')!;
  expect(newNode.text).toBe('Plain vivid ending');
  expect(styleAt(newNode.textRuns, 6)).toMatchObject({ fontFamily: 'calibri', color: '#ffd18a', bold: true, underline: true });
  expect(styleAt(newNode.textRuns, 0)).toEqual({}); expect(styleAt(newNode.textRuns, 12)).toEqual({});
  await expect(tile(page, newNode.id).locator('.ms-text-run').filter({ hasText: /^vivid$/ })).toHaveCSS('text-decoration-line', 'underline');
  await page.reload(); expect((await formattedNode(request, source.saved.id, source.text.id)).titleRuns?.[0].style.fontFamily).toBe('times-new-roman');
});

test('repeated characters retain the edited range, and formatted title paste and creation trimming keep correct character boundaries', async ({ page, request }) => {
  const source = await openFixture(page, request), editor = await editorFor(page, source.text);
  const body = editor.getByRole('textbox', { name: 'Text body', exact: true }); await body.fill('aaaa');
  await selectPassage(body, 0, 2); await body.press('Control+b');
  await selectPassage(body, 1, 1); await page.keyboard.insertText('a');
  await expect.poll(async () => (await formattedNode(request, source.saved.id, source.text.id)).text).toBe('aaaaa');
  expect((await formattedNode(request, source.saved.id, source.text.id)).textRuns).toEqual([{ start: 0, end: 3, style: { bold: true } }]);
  await page.getByRole('button', { name: 'Format title', exact: true }).click();
  const titleDialog = page.getByRole('dialog', { name: 'Format title', exact: true });
  const title = titleDialog.getByRole('textbox', { name: 'Title text', exact: true });
  await selectPassage(title, 0, source.text.title.length);
  await title.evaluate(root => {
    const transfer = new DataTransfer(), text = 'a\n\nb'; transfer.setData('text/plain', text);
    transfer.setData('application/x-meridian-text', JSON.stringify({ version: 1, text, runs: [{ start: 3, end: 4, style: { italic: true } }] }));
    root.dispatchEvent(new ClipboardEvent('paste', { bubbles: true, cancelable: true, clipboardData: transfer }));
  });
  await expect.poll(async () => (await formattedNode(request, source.saved.id, source.text.id)).title).toBe('a b');
  expect((await formattedNode(request, source.saved.id, source.text.id)).titleRuns).toEqual([{ start: 2, end: 3, style: { italic: true } }]);
  await titleDialog.getByRole('button', { name: 'Close text editor', exact: true }).click();

  await page.getByRole('button', { name: 'New Node', exact: true }).click(); await page.getByRole('menuitem', { name: 'Node', exact: true }).click();
  const viewport = (await page.locator('.ms-viewport').boundingBox())!; await page.mouse.click(viewport.x + 960, viewport.y + 540);
  const creation = page.getByRole('dialog', { name: 'New Node', exact: true });
  await creation.getByLabel('Type', { exact: true }).selectOption('media'); await creation.getByLabel('Title', { exact: true }).fill('  Styled  ');
  await creation.getByRole('button', { name: 'Format title', exact: true }).click();
  const newTitleDialog = page.getByRole('dialog', { name: 'Format title', exact: true });
  const newTitle = newTitleDialog.getByRole('textbox', { name: 'Title text', exact: true });
  await selectPassage(newTitle, 2, 8); await newTitle.press('Control+i');
  await newTitleDialog.getByRole('button', { name: 'Close text editor', exact: true }).click();
  await creation.getByRole('button', { name: 'Create node', exact: true }).click();
  let created: MeridianNode | undefined;
  await expect.poll(async () => { created = (await stored(request, source.saved.id)).nodes.find(node => node.title === 'Styled'); return !!created; }).toBe(true);
  expect(created!.titleRuns).toEqual([{ start: 0, end: 6, style: { italic: true } }]);
});

test('formatted passages render in local and saved references, copy with new identities, survive Modules and offline viewing', async ({ page, request, browser }, info) => {
  const errors: string[] = []; page.on('pageerror', error => errors.push(error.message));
  const source = await openFixture(page, request, true);
  async function checkBeta(card: Locator) {
    const beta = card.locator('.ms-text .ms-text-run').filter({ hasText: /^beta$/ }); await expect(beta).toHaveCount(1);
    await expect(beta).toHaveCSS('font-size', '28px'); await expect(beta).toHaveCSS('font-weight', '700'); await expect(beta).toHaveCSS('font-style', 'italic');
    await expect(beta).toHaveCSS('text-decoration-line', 'underline'); await expect(beta).toHaveCSS('color', 'rgb(255, 209, 138)');
    expect(await beta.evaluate(item => getComputedStyle(item).fontFamily)).toContain('Georgia');
    await assertTextInside(card);
  }
  await checkBeta(tile(page, source.text.id)); await checkBeta(tile(page, source.local.id)); await assertTextInside(tile(page, source.circle.id));
  await page.screenshot({ path: info.outputPath('rich-passages-and-references.png') });
  await tile(page, source.text.id).click({ position: { x: 2, y: 20 } }); await tile(page, source.text.id).focus();
  await page.keyboard.press('Control+c'); await page.keyboard.press('Control+v');
  let after: MeridianProject;
  await expect.poll(async () => { after = await stored(request, source.saved.id); return after.nodes.length; }).toBe(source.saved.nodes.length + 1);
  const copy = after!.nodes.find(node => !source.saved.nodes.some(old => old.id === node.id))!;
  expect(copy.id).not.toBe(source.text.id); expect(copy.textRuns).toEqual(source.text.textRuns); expect(copy.titleRuns).toEqual(source.text.titleRuns);
  await checkBeta(tile(page, copy.id));

  await page.getByRole('button', { name: 'New Node', exact: true }).click(); await page.getByRole('menuitem', { name: 'Create Module', exact: true }).click();
  await tile(page, source.text.id).locator('.ms-node-title').click();
  const moduleName = `Rich passages ${source.saved.id.slice(0, 8)}`;
  await page.getByRole('textbox', { name: 'Module name', exact: true }).fill(moduleName);
  await page.getByRole('button', { name: 'Create From Selection', exact: true }).click();
  let catalog: { id: string; name: string }[] = [];
  await expect.poll(async () => { catalog = await (await request.get('/api/modules')).json(); return catalog.some(item => item.name === moduleName); }).toBe(true);
  const module = await (await request.get(`/api/modules/${catalog.find(item => item.name === moduleName)!.id}`)).json() as StoredModule;
  const definition = module.project.nodes.find(node => node.title === source.text.title)!;
  expect(definition.textRuns).toEqual(source.text.textRuns); expect(definition.titleRuns).toEqual(source.text.titleRuns);
  await page.locator(`.module-library-card[data-module-id="${module.id}"]`).getByRole('button', { name: `Add ${moduleName} to Canvas`, exact: true }).click();
  await expect.poll(async () => (await stored(request, source.saved.id)).nodes.some(node => node.kind === 'module')).toBe(true);
  after = await stored(request, source.saved.id);
  const inserted = after.nodes.find(node => node.title === source.text.title && node.id !== source.text.id && node.id !== copy.id)!;
  expect(inserted.id).not.toBe(definition.id); expect(inserted.textRuns).toEqual(definition.textRuns); expect(inserted.titleRuns).toEqual(definition.titleRuns);
  await checkBeta(tile(page, inserted.id));
  await page.reload(); await checkBeta(tile(page, source.text.id)); expect((await stored(request, source.saved.id)).snapshots[source.text.id].node.textRuns).toEqual(source.text.textRuns);
  await page.goto(`/?project=${source.saved.id}&canvas=${source.remoteCanvas.id}`); await checkBeta(tile(page, source.pinned.id));
  const published = { ...await stored(request, source.saved.id), entryCanvasId: source.remoteCanvas.id };
  const filename = info.outputPath('rich-text-offline.html');
  await writeFile(filename, buildPublication(published, await readFile('dist/viewer.js', 'utf8'), await readFile('dist/scene.css', 'utf8')));
  const context = await browser.newContext({ offline: true, viewport: { width: 1500, height: 950 } });
  try {
    const viewer = await context.newPage(); viewer.on('pageerror', error => errors.push(error.message)); await viewer.goto(pathToFileURL(filename).href);
    await viewer.getByRole('button', { name: 'Fit Work', exact: true }).click(); await checkBeta(tile(viewer, source.pinned.id));
    const payload = JSON.parse((await viewer.locator('#meridian-data').textContent())!).project as MeridianProject;
    expect(payload.nodes.find(node => node.id === source.text.id)?.textRuns).toEqual(source.text.textRuns);
    expect(payload.snapshots[source.text.id].node.titleRuns).toEqual(source.text.titleRuns);
    await viewer.screenshot({ path: info.outputPath('saved-reference-rich-text-offline.png') });
  } finally { await context.close(); }
  expect(errors).toEqual([]);
});




