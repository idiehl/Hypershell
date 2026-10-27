import { interactionTool } from './ui-helpers.ts';
import { test, expect, type APIRequestContext, type Locator, type Page } from '@playwright/test';
import { addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import type { MeridianNode, MeridianProject } from '../src/core/types.ts';

async function install(request: APIRequestContext, project: MeridianProject): Promise<MeridianProject> {
  const response = await request.post('/api/projects', { data: commitSnapshots(project) }); expect(response.ok(), await response.text()).toBeTruthy(); return response.json();
}
async function savedProject(request: APIRequestContext, id: string): Promise<MeridianProject> { return (await request.get(`/api/projects/${id}`)).json(); }
async function savedNode(request: APIRequestContext, id: string, title: string): Promise<MeridianNode> {
  await expect.poll(async () => (await savedProject(request, id)).nodes.some(node => node.title === title && node.kind !== 'canvas')).toBe(true);
  return (await savedProject(request, id)).nodes.find(node => node.title === title && node.kind !== 'canvas')!;
}
async function nodeMode(page: Page) { await page.getByRole('button', { name: /^New (?:Node|Link)$/ }).click(); await page.getByRole('menuitem', { name: 'Node', exact: true }).click(); }
async function linkMode(page: Page) { await page.getByRole('button', { name: /^New (?:Node|Link)$/ }).click(); await page.getByRole('menuitem', { name: 'Link', exact: true }).click(); }
async function openAt(page: Page, x = 70, y = 35): Promise<Locator> {
  const stage = page.locator('.canvas-stage'); await expect(stage).toBeVisible(); await stage.click({ position: { x, y } });
  const dialog = page.getByRole('dialog', { name: 'New Node', exact: true }); await expect(dialog).toBeVisible(); return dialog;
}
async function submit(dialog: Locator) { await dialog.getByRole('button', { name: 'Create node', exact: true }).click(); await expect(dialog).toHaveCount(0); }

test('one adaptive form exposes every built node type and container variant, traps focus, and cancels without edits', async ({ page, request }, info) => {
  const project = newProject('Adaptive node creation'), target = addNode(project, project.entryCanvasId, 'text', 'Existing reference target'); target.x = 360; target.y = 170;
  const saved = await install(request, project); await page.goto(`/?project=${saved.id}`); await nodeMode(page); const dialog = await openAt(page);
  await expect(dialog.getByLabel('Type', { exact: true })).toBeFocused();
  expect(await dialog.getByLabel('Type', { exact: true }).locator('option').allTextContents()).toEqual(['Canvas', 'Container', 'Operation', 'Media', 'Module', 'Reference', 'Number', 'Input', 'Switch', 'Output', 'Utility']);
  const controls: Record<string, string[]> = { canvas: ['Work workspace', 'Content', 'Text body'], container: ['Display Pattern', 'Content', 'Text body'], joint: ['Operation'], media: ['Display Pattern', 'Content', 'Text body'], module: ['Display Pattern'], reference: ['Referenced node', 'Cell address or name'], number: ['Value'], input: ['Value'], switch: ['Value type', 'State'], output: ['Value type'], utility: ['Utility role', 'Utility appearance'] };
  for (const [type, labels] of Object.entries(controls)) { await dialog.getByLabel('Type', { exact: true }).selectOption(type); for (const label of labels) await expect(dialog.getByLabel(label, { exact: true })).toBeVisible(); }
  await dialog.getByLabel('Type', { exact: true }).selectOption('container');
  const variants = ['contained', 'horizontal', 'vertical', 'grid', 'spreadsheet', 'window', 'gallery'];
  expect(await dialog.getByLabel('Display Pattern', { exact: true }).locator('option').allTextContents()).toEqual(['Freeform', 'Horizontal', 'Vertical', 'Grid', 'Spreadsheet', 'Window', 'Gallery']);
  expect(await dialog.getByLabel('Display Pattern', { exact: true }).locator('option').evaluateAll(options => options.map(option => (option as HTMLOptionElement).value))).toEqual(variants);
  for (const variant of variants) {
    await dialog.getByLabel('Display Pattern', { exact: true }).selectOption(variant);
    const ordered = ['horizontal', 'vertical', 'grid', 'spreadsheet', 'window'].includes(variant);
    const explicit = ['spreadsheet', 'window'].includes(variant);
    for (const label of ['Columns', 'Rows', 'Cell width', 'Cell height']) {
      if (explicit) { await expect(dialog.getByLabel(label, { exact: true })).toBeVisible(); await expect(dialog.getByLabel(label, { exact: true })).toBeEnabled(); }
      else { await expect(dialog.getByLabel(label, { exact: true })).toBeHidden(); await expect(dialog.getByLabel(label, { exact: true })).toBeDisabled(); }
    }
    for (const label of ['Fill order', 'Initial cells (CSV)']) {
      if (ordered) { await expect(dialog.getByLabel(label, { exact: true })).toBeVisible(); await expect(dialog.getByLabel(label, { exact: true })).toBeEnabled(); }
      else { await expect(dialog.getByLabel(label, { exact: true })).toBeHidden(); await expect(dialog.getByLabel(label, { exact: true })).toBeDisabled(); }
    }
    for (const label of ['Window columns', 'Window rows', 'Scroll direction']) {
      if (variant === 'window') { await expect(dialog.getByLabel(label, { exact: true })).toBeVisible(); await expect(dialog.getByLabel(label, { exact: true })).toBeEnabled(); }
      else { await expect(dialog.getByLabel(label, { exact: true })).toBeHidden(); await expect(dialog.getByLabel(label, { exact: true })).toBeDisabled(); }
    }
    if (variant === 'grid') await expect(dialog).toContainText('Ten objects use a 4:4 grid');
    if (variant === 'window') {
      await expect(dialog.getByLabel('Window columns', { exact: true })).toHaveValue('3'); await expect(dialog.getByLabel('Window rows', { exact: true })).toHaveValue('5');
      await expect(dialog.getByLabel('Scroll direction', { exact: true })).toHaveValue('vertical');
    }
    if (variant === 'gallery') await expect(dialog).toContainText(/one (?:child )?object at a time/);
  }
  for (const label of ['Inner padding', 'Content spacing', 'Text and media spacing']) await expect(dialog.getByLabel(label, { exact: true })).toHaveValue('5');
  await expect(dialog.getByLabel('Child inset', { exact: true })).toHaveValue('20');
  await expect(dialog.getByLabel('Border style', { exact: true })).toHaveValue('solid'); await expect(dialog.getByLabel('Border width', { exact: true })).toHaveValue('1');
  await expect(dialog.getByLabel('Sizing', { exact: true })).toHaveValue('auto');
  await expect(dialog.getByLabel('Width', { exact: true })).toBeHidden(); await expect(dialog.getByLabel('Height', { exact: true })).toBeHidden();
  await dialog.getByLabel('Shape', { exact: true }).selectOption('hexagon'); await dialog.getByLabel('Title', { exact: true }).fill('Uncommitted preview'); await dialog.getByLabel('Style', { exact: true }).selectOption('paper');
  await expect(dialog.locator('.creation-preview-tile')).toHaveAttribute('data-shape', 'hexagon');
  await page.screenshot({ path: info.outputPath('shared-creation-form.png') });
  await dialog.getByLabel('Type', { exact: true }).focus(); for (let index = 0; index < 24; index++) { await page.keyboard.press('Tab'); expect(await dialog.evaluate(root => root.contains(document.activeElement))).toBe(true); }
  for (let index = 0; index < 6; index++) { await page.keyboard.press('Shift+Tab'); expect(await dialog.evaluate(root => root.contains(document.activeElement))).toBe(true); }
  await page.keyboard.press('Escape'); await expect(dialog).toHaveCount(0); await expect(page.getByRole('button', { name: 'New Node', exact: true })).toHaveAttribute('aria-pressed', 'true'); await expect(page.locator('#scene .ms-viewport')).toBeFocused();
  const reopened = await openAt(page, 80, 40); await reopened.getByRole('button', { name: 'Cancel', exact: true }).click(); await expect(reopened).toHaveCount(0);
  const after = await savedProject(request, saved.id); expect(after.revision).toBe(saved.revision); expect(after.nodes).toEqual(saved.nodes); expect(after.links).toEqual(saved.links);
});

test('new child creation applies shape, font, and sticky behavior while revealing the parent in place', async ({ page, request }, info) => {
  const project = newProject('Child node creation'), parent = addNode(project, project.entryCanvasId, 'text', 'Parent passage'); parent.x = 170; parent.y = 110; parent.width = 320; parent.height = 190; parent.text = 'A passage with attached illustrations.'; parent.collapsed = true;
  const saved = await install(request, project); await page.goto(`/?project=${saved.id}`); await nodeMode(page); await page.locator(`.ms-node[data-node-id="${parent.id}"] .ms-node-title`).click();
  const dialog = page.getByRole('dialog', { name: 'New Node', exact: true }); await expect(dialog).toContainText('Create a child of Parent passage.');
  await dialog.getByLabel('Type', { exact: true }).selectOption('media'); await dialog.getByLabel('Title', { exact: true }).fill('Sticky annotation'); await dialog.getByLabel('Text body', { exact: true }).fill('This annotation belongs to the passage.');
  await dialog.getByLabel('Style', { exact: true }).selectOption('paper'); await dialog.getByLabel('Shape', { exact: true }).selectOption('hexagon');
  await expect(dialog.getByLabel('Sizing', { exact: true })).toHaveValue('auto'); await expect(dialog.getByLabel('Width', { exact: true })).toBeHidden();
  await dialog.getByLabel('Sizing', { exact: true }).selectOption('fixed'); await dialog.getByLabel('Width', { exact: true }).fill('300'); await dialog.getByLabel('Height', { exact: true }).fill('220');
  await dialog.getByLabel('Content font size', { exact: true }).fill('18'); await dialog.getByLabel('Title font size', { exact: true }).fill('30');
  await dialog.getByLabel('Border width', { exact: true }).fill('3'); await dialog.getByLabel('Border style', { exact: true }).selectOption('dotted');
  for (const [label, value] of [['Inner padding', '7'], ['Content spacing', '9'], ['Text and media spacing', '11'], ['Child inset', '13']]) await dialog.getByLabel(label!, { exact: true }).fill(value!);
  await dialog.getByLabel('Behavior', { exact: true }).selectOption('sticky'); await dialog.getByLabel('Parent edge', { exact: true }).selectOption('right'); await submit(dialog);
  const child = await savedNode(request, saved.id, 'Sticky annotation'), current = await savedProject(request, saved.id); expect(child.parentId).toBe(parent.id); expect(child.style).toMatchObject({ shape: 'hexagon', fontSize: 18, titleSize: 30, fill: '#f3e8cf', borderWidth: 3, borderStyle: 'dotted', padding: 7, contentGap: 9, textMediaGap: 11, childPadding: 13 });
  expect(child.snapping?.stickyEdges).toBe(true); expect(child.attachSide).toBe('right'); expect(current.nodes.find(node => node.id === parent.id)!.collapsed).toBe(false); expect(new URL(page.url()).searchParams.get('canvas')).toBeNull();
  expect(child.sizeMode).toBe('fixed'); expect(child.width).toBe(300); expect(child.height).toBe(220);
  await expect(page.locator(`.ms-node[data-node-id="${child.id}"]`)).toBeVisible(); await interactionTool(page); await page.screenshot({ path: info.outputPath('created-sticky-child.png') }); await page.reload();
  await expect(page.locator(`.ms-node[data-node-id="${child.id}"] .ms-text`)).toContainText('This annotation belongs');
});

test('one Media form preserves complete attachment bytes or entered text as its single content item', async ({ page, request }) => {
  const saved = await install(request, newProject('Attachment creation')); await page.goto(`/?project=${saved.id}`); await nodeMode(page);
  const image = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLbtAAAAABJRU5ErkJggg==', 'base64');
  let dialog = await openAt(page); await dialog.getByLabel('Type', { exact: true }).selectOption('media'); await dialog.getByLabel('Content', { exact: true }).selectOption('asset'); await dialog.getByLabel('Content file', { exact: true }).setInputFiles({ name: 'Illustration.png', mimeType: 'image/png', buffer: image });
  await dialog.getByLabel('Title', { exact: true }).fill('Illustration'); await expect(dialog.getByLabel('Text body', { exact: true })).toHaveCount(0); await dialog.getByLabel('Image fit', { exact: true }).selectOption('contain'); await submit(dialog);
  const media = await savedNode(request, saved.id, 'Illustration'); expect(media.kind).toBe('media'); expect(media.contentType).toBe('asset'); await expect(page.locator(`.ms-node[data-node-id="${media.id}"] img.ms-image`)).toBeVisible();
  dialog = await openAt(page, 520, 40); await dialog.getByLabel('Type', { exact: true }).selectOption('media'); await dialog.getByLabel('Content', { exact: true }).selectOption('asset'); const content = '# Sources\nHésiode — Θεογονία\nOriginal bytes remain available.\n';
  await dialog.getByLabel('Content file', { exact: true }).setInputFiles({ name: 'Sources.md', mimeType: 'text/markdown', buffer: Buffer.from(content) }); await dialog.getByLabel('Title', { exact: true }).fill('Source document'); await expect(dialog.getByLabel('Text body', { exact: true })).toHaveCount(0); await submit(dialog);
  const document = await savedNode(request, saved.id, 'Source document'); dialog = await openAt(page, 850, 40); await dialog.getByLabel('Type', { exact: true }).selectOption('media'); await dialog.getByLabel('Title', { exact: true }).fill('Written document'); await dialog.getByLabel('Text body', { exact: true }).fill('A whole document, created in the form.'); await expect(dialog.getByLabel('Content file', { exact: true })).toHaveCount(0); await submit(dialog);
  const written = await savedNode(request, saved.id, 'Written document'), current = await savedProject(request, saved.id);
  expect(Buffer.from(current.assets.find(asset => asset.id === media.assetId)!.base64, 'base64')).toEqual(image); expect(Buffer.from(current.assets.find(asset => asset.id === document.assetId)!.base64, 'base64').toString()).toBe(content);
  expect(written.text).toBe('A whole document, created in the form.'); expect(written.contentType).toBe('text'); expect(written.assetId).toBeUndefined(); expect(document.text??'').toBe(''); expect(document.kind).toBe('media'); expect(document.contentType).toBe('asset');
});

test('container spreadsheet creation makes addressable CSV child nodes and a Canvas uses the same form', async ({ page, request }) => {
  const saved = await install(request, newProject('Grid and canvas creation')); await page.goto(`/?project=${saved.id}`); await nodeMode(page);
  let dialog = await openAt(page); await dialog.getByLabel('Type', { exact: true }).selectOption('container'); await dialog.getByLabel('Display Pattern', { exact: true }).selectOption('spreadsheet'); await dialog.getByLabel('Title', { exact: true }).fill('Source spreadsheet');
  await dialog.getByLabel('Columns', { exact: true }).fill('2'); await dialog.getByLabel('Rows', { exact: true }).fill('2'); await dialog.getByLabel('Cell width', { exact: true }).fill('160'); await dialog.getByLabel('Cell height', { exact: true }).fill('80');
  await dialog.getByLabel('Initial cells (CSV)', { exact: true }).fill('Author,Work\nHesiod,Theogony'); await submit(dialog);
  const sheet = await savedNode(request, saved.id, 'Source spreadsheet'); expect(sheet.kind).toBe('spreadsheet'); expect(sheet.containerVariant).toBe('spreadsheet'); expect(sheet.grid).toMatchObject({ columns: 2, rows: 2 });
  const withSheet = await savedProject(request, saved.id), cells = withSheet.nodes.filter(node => node.parentId === sheet.id); expect(cells).toHaveLength(4); expect(cells.find(node => node.text === 'Theogony')!.cell).toEqual({ column: 2, row: 2 });
  dialog = await openAt(page, 700, 40); await dialog.getByLabel('Type', { exact: true }).selectOption('canvas'); await dialog.getByLabel('Title', { exact: true }).fill('Epic workspace'); await dialog.getByLabel('Text body', { exact: true }).fill('A dedicated canvas for the epics.'); await submit(dialog);
  const launcher = await savedNode(request, saved.id, 'Epic workspace'), current = await savedProject(request, saved.id); expect(launcher.kind).toBe('canvas-link'); expect(current.canvases).toHaveLength(2); expect(current.nodes.find(node => node.id === launcher.targetId)!.kind).toBe('canvas'); expect(current.entryCanvasId).toBe(saved.entryCanvasId);
  dialog = await openAt(page, 700, 500); await dialog.getByLabel('Type', { exact: true }).selectOption('container'); await dialog.getByLabel('Display Pattern', { exact: true }).selectOption('gallery'); await dialog.getByLabel('Title', { exact: true }).fill('Gallery container'); await expect(dialog.getByLabel('Columns', { exact: true })).toBeHidden(); await expect(dialog.getByLabel('Initial cells (CSV)', { exact: true })).toBeHidden(); await submit(dialog);
  const gallery = await savedNode(request, saved.id, 'Gallery container'); expect(gallery.containerVariant).toBe('gallery'); expect(gallery.childArrangement).toBe('free'); expect(gallery.childLayout).toBe('contained'); expect(gallery.grid).toBeUndefined(); expect((await savedProject(request, saved.id)).nodes.filter(node => node.parentId === gallery.id)).toHaveLength(0); await page.reload(); expect((await savedProject(request, saved.id)).nodes.find(node => node.id === gallery.id)!.containerVariant).toBe('gallery');
});

test('joint creation and typed link form match ports, preserve owners, and reject invalid destinations', async ({ page, request }) => {
  const project = newProject('Typed creation forms'), a = addNode(project, project.entryCanvasId, 'number', 'Numeric input'); a.value = 12; a.x = 100; a.y = 150;
  const text = addNode(project, project.entryCanvasId, 'text', 'Text destination'); text.x = 100; text.y = 400;
  const saved = await install(request, project); await page.goto(`/?project=${saved.id}`); await nodeMode(page); const dialog = await openAt(page, 500, 150);
  await dialog.getByLabel('Type', { exact: true }).selectOption('joint'); await dialog.getByLabel('Title', { exact: true }).fill('Addition joint'); await dialog.getByLabel('Operation', { exact: true }).selectOption('ADD'); await submit(dialog);
  const joint = await savedNode(request, saved.id, 'Addition joint'); expect(joint.operation).toBe('ADD'); await linkMode(page); await page.locator(`.ms-node[data-node-id="${a.id}"] .ms-node-title`).click({button:'right'}); await page.getByRole('menuitem',{name:'Create Link',exact:true}).click();
  let linkDialog = page.getByRole('dialog', { name: 'New Link', exact: true }); await expect(linkDialog.getByLabel('Source', { exact: true })).toHaveValue(a.id);
  await linkDialog.getByLabel('Type', { exact: true }).selectOption('numeric'); await linkDialog.getByLabel('Destination', { exact: true }).selectOption(text.id); await expect(linkDialog.locator('.creation-error')).toContainText('No available destination input');
  await linkDialog.getByLabel('Destination', { exact: true }).selectOption(joint.id); expect(await linkDialog.getByLabel('Destination input', { exact: true }).locator('option').allTextContents()).toEqual(['A · Number', 'B · Number']);
  await linkDialog.getByLabel('Destination input', { exact: true }).selectOption('0'); await linkDialog.getByLabel('Label', { exact: true }).fill('First operand'); await linkDialog.getByLabel('Contained in', { exact: true }).selectOption(saved.canvases[0]!.rootId); await linkDialog.getByRole('button', { name: 'Create link', exact: true }).click(); await expect(linkDialog).toHaveCount(0);
  await expect.poll(async () => (await savedProject(request, saved.id)).links.length).toBe(1); const first = (await savedProject(request, saved.id)).links[0]!; expect(first).toMatchObject({ kind: 'numeric', targetPort: 0, sourceId: a.id, targetId: joint.id, parentId: saved.canvases[0]!.rootId, label: 'First operand', direction: 'forward' });
  await page.locator(`.ms-node[data-node-id="${a.id}"] .ms-node-title`).click({button:'right'}); await page.getByRole('menuitem',{name:'Create Link',exact:true}).click(); linkDialog = page.getByRole('dialog', { name: 'New Link', exact: true }); await linkDialog.getByLabel('Type', { exact: true }).selectOption('numeric'); await linkDialog.getByLabel('Destination', { exact: true }).selectOption(joint.id);
  expect(await linkDialog.getByLabel('Destination input', { exact: true }).locator('option').allTextContents()).toEqual(['B · Number']); await linkDialog.getByRole('button', { name: 'Cancel', exact: true }).click();
});

test('a restricted parent filters creation types, container variants, and joint operations in the shared form', async ({ page, request }) => {
  const project = newProject('Creation rules'), root = project.nodes[0]!; root.rules = { nodeKinds: ['container', 'spreadsheet', 'joint', 'media'], operations: ['AND', 'OR', 'NOT'] };
  const parent = addNode(project, project.entryCanvasId, 'container', 'Restricted parent'); parent.x = 130; parent.y = 90; parent.childLayout = 'spatial'; parent.rules = { nodeKinds: ['spreadsheet', 'joint', 'media'], operations: ['OR', 'NOT'] };
  const saved = await install(request, project); await page.goto(`/?project=${saved.id}`); await nodeMode(page); await page.locator(`.ms-node[data-node-id="${parent.id}"] .ms-node-title`).click();
  const dialog = page.getByRole('dialog', { name: 'New Node', exact: true }); expect(await dialog.getByLabel('Type', { exact: true }).locator('option').allTextContents()).toEqual(['Container', 'Operation', 'Media']);
  await dialog.getByLabel('Type', { exact: true }).selectOption('container'); expect(await dialog.getByLabel('Display Pattern', { exact: true }).locator('option').allTextContents()).toEqual(['Spreadsheet']);
  await dialog.getByLabel('Type', { exact: true }).selectOption('joint'); expect(await dialog.getByLabel('Operation', { exact: true }).locator('option').evaluateAll(options => options.map(option => (option as HTMLOptionElement).value))).toEqual(['OR', 'NOT']);
  await dialog.getByLabel('Title', { exact: true }).fill('Permitted joint'); await dialog.getByLabel('Operation', { exact: true }).selectOption('NOT'); await submit(dialog);
  const joint = await savedNode(request, saved.id, 'Permitted joint'); expect(joint.parentId).toBe(parent.id); expect(joint.operation).toBe('NOT');
});




