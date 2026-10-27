import { test, expect, type APIRequestContext, type Page, type Locator } from '@playwright/test';
import { readFile, writeFile } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';
import { addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import { FONT_FAMILIES } from '../src/core/fonts.ts';
import type { MeridianProject, MeridianNode } from '../src/core/types.ts';
import { buildPublication } from '../src/publication.ts';
import { inspectorPanel } from './ui-helpers.ts';

const tile = (page: Page, id: string) => page.locator(`.ms-node[data-node-id="${id}"]`);
const readProject = async (request: APIRequestContext, id: string) => await (await request.get(`/api/projects/${id}`)).json() as MeridianProject;
const png = 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/l9sAAAAASUVORK5CYII=';

function fixture(rotated = false) {
  const project = newProject('Typography and rotation QA');
  const add = (title: string, x: number, y: number, width: number, height: number) => {
    const node = addNode(project, project.entryCanvasId, 'media', title);
    Object.assign(node, { x, y, width, height, sizeMode: 'fixed', childPosition: 'manual', contentType: 'text', style: { shape: 'rectangle', fontFamily: 'sans', fontSize: 17, titleSize: 22, textAlign: 'center', contentSizing: 'fit' } });
    return node;
  };
  const text = add('Words with room to turn', 80, 80, 380, 280);
  text.text = 'Rotation keeps these words readable inside the tile.\nEvery line belongs to one scalable text block.';
  const image = add('Upright image, independent title', 560, 80, 290, 280);
  const asset = { id: crypto.randomUUID(), name: 'original-image.png', mime: 'image/png', base64: png };
  project.assets.push(asset); image.contentType = 'asset'; image.assetId = asset.id;
  const circle = add('Text inside a circle', 80, 410, 250, 250);
  circle.style!.shape = 'circle'; circle.style!.fontFamily = 'serif';
  circle.text = 'Readable text follows the available interior, even when its block turns.';
  const mono = add('Legacy monospace', 380, 430, 220, 170);
  mono.style!.fontFamily = 'mono'; mono.text = '1:1\n2:1\n3:1';
  if (rotated) {
    Object.assign(text.style!, { fontFamily: 'georgia', titleRotation: -90, textRotation: 45 });
    Object.assign(image.style!, { titleRotation: 45 });
    Object.assign(circle.style!, { titleRotation: -30, textRotation: 45 });
  }
  return { project: commitSnapshots(project), text, image, circle, mono, asset };
}

async function openFixture(page: Page, request: APIRequestContext, rotated = false) {
  const source = fixture(rotated), response = await request.post('/api/projects', { data: source.project });
  expect(response.status()).toBe(201); const saved = await response.json() as MeridianProject;
  await page.goto(`/?project=${saved.id}&canvas=${saved.entryCanvasId}`);
  await expect(tile(page, source.text.id)).toBeVisible(); return { ...source, saved };
}
async function appearance(page: Page, node: MeridianNode) {
  await tile(page, node.id).locator('.ms-node-title').click(); await inspectorPanel(page, 'appearance');
}
async function rotation(page: Page, name: 'Title' | 'Text', value: number) {
  const input = page.locator('.inspector').getByRole('spinbutton', { name: `${name} rotation (°)`, exact: true });
  await input.fill(String(value)); await input.press('Tab');
}
async function savedNode(request: APIRequestContext, projectId: string, nodeId: string) {
  return (await readProject(request, projectId)).nodes.find(node => node.id === nodeId)!;
}

async function assertTextEnvelope(card: Locator, circle = false) {
  // Range observations include actual displayed line boxes after camera and
  // content fitting; a schema-only assertion would miss clipping in the scene.
  const result = await card.evaluate((node, round) => {
    const card = node.getBoundingClientRect();
    const rect = (r: DOMRect) => ({ x: r.x, y: r.y, width: r.width, height: r.height });
    const blocks = Array.from(node.querySelectorAll('.ms-node-title,.ms-text,.ms-text-preview')).filter(item => item.textContent?.trim()).map(item => {
      const range = document.createRange(); range.selectNodeContents(item);
      const frame = item.closest('.ms-rotated-text-frame');
      return { font: getComputedStyle(item).fontFamily, rotation: item.getAttribute('data-text-rotation'),
        frame: frame ? rect(frame.getBoundingClientRect()) : undefined,
        lines: Array.from(range.getClientRects()).filter(r => r.width > 0 && r.height > 0).map(rect) };
    });
    return { card: rect(card), blocks, circle: round };
  }, circle);
  expect(result.blocks.length).toBeGreaterThan(0);
  for (const block of result.blocks) {
    expect(block.lines.length).toBeGreaterThan(0);
    for (const bounds of [...block.lines, ...block.frame ? [block.frame] : []]) {
      expect(bounds.x).toBeGreaterThanOrEqual(result.card.x - 1);
      expect(bounds.y).toBeGreaterThanOrEqual(result.card.y - 1);
      expect(bounds.x + bounds.width).toBeLessThanOrEqual(result.card.x + result.card.width + 1);
      expect(bounds.y + bounds.height).toBeLessThanOrEqual(result.card.y + result.card.height + 1);
      if (result.circle) for (const [x, y] of [[bounds.x, bounds.y], [bounds.x + bounds.width, bounds.y], [bounds.x, bounds.y + bounds.height], [bounds.x + bounds.width, bounds.y + bounds.height]]) {
        const dx = (x - result.card.x - result.card.width / 2) / (result.card.width / 2);
        const dy = (y - result.card.y - result.card.height / 2) / (result.card.height / 2);
        expect(dx * dx + dy * dy).toBeLessThanOrEqual(1.025);
      }
    }
  }
}

test('Appearance offers a large searchable local font catalog and independent text rotations that autosave, undo and reopen', async ({ page, request }, info) => {
  const errors: string[] = []; page.on('pageerror', error => errors.push(error.message));
  const source = await openFixture(page, request); await appearance(page, source.text);
  const picker = page.getByRole('combobox', { name: 'Font', exact: true });
  await picker.click(); const popup = page.locator('.font-picker-popup'); await expect(popup).toBeVisible();
  expect(await popup.getByRole('option').count()).toBeGreaterThanOrEqual(80);
  expect(await popup.getByRole('option').count()).toBe(FONT_FAMILIES.length);
  await expect(popup.getByRole('group')).toHaveCount(6);
  for (const familiar of ['Arial', 'Calibri', 'Times New Roman', 'Consolas']) await expect(popup.getByRole('option', { name: familiar, exact: true })).toBeAttached();
  await page.screenshot({ path: info.outputPath('font-catalog-preview.png') });
  const search = popup.getByRole('searchbox', { name: 'Search fonts', exact: true });
  await search.fill('Geor'); await expect(popup.getByRole('option', { name: 'Georgia', exact: true })).toBeVisible();
  expect(await popup.getByRole('option', { name: 'Georgia', exact: true }).locator('.font-picker-name').evaluate(item => getComputedStyle(item).fontFamily)).toContain('Georgia');
  await popup.getByRole('option', { name: 'Georgia', exact: true }).click();
  await expect(popup).toBeHidden();
  await expect.poll(async () => (await savedNode(request, source.saved.id, source.text.id)).style?.fontFamily).toBe('georgia');
  expect(await tile(page, source.text.id).locator('.ms-text').evaluate(item => getComputedStyle(item).fontFamily)).toContain('Georgia');
  await picker.click(); await search.fill('No font has this name'); await expect(popup.getByRole('option')).toHaveCount(0);
  await search.press('Escape'); await expect(popup).toBeHidden(); await expect(picker).toContainText('Georgia');
  await picker.focus(); await picker.press('ArrowDown'); await search.fill('Consolas'); await search.press('ArrowDown'); await search.press('Enter');
  await expect.poll(async () => (await savedNode(request, source.saved.id, source.text.id)).style?.fontFamily).toBe('consolas');
  expect(await tile(page, source.text.id).locator('.ms-text').evaluate(item => getComputedStyle(item).fontFamily)).toContain('Consolas');
  await picker.click(); await search.fill('Georgia'); await search.press('Enter');
  await expect.poll(async () => (await savedNode(request, source.saved.id, source.text.id)).style?.fontFamily).toBe('georgia');
  await rotation(page, 'Title', 45); await rotation(page, 'Text', -90);
  await expect.poll(async () => (await savedNode(request, source.saved.id, source.text.id)).style?.textRotation).toBe(-90);
  await expect(tile(page, source.text.id).locator('.ms-node-title')).toHaveAttribute('data-text-rotation', '45');
  await expect(tile(page, source.text.id).locator('.ms-text')).toHaveAttribute('data-text-rotation', '-90');
  await assertTextEnvelope(tile(page, source.text.id));
  await page.getByRole('button', { name: 'Undo', exact: true }).click();
  await expect.poll(async () => (await savedNode(request, source.saved.id, source.text.id)).style?.textRotation ?? 0).toBe(0);
  expect((await savedNode(request, source.saved.id, source.text.id)).style?.titleRotation).toBe(45);
  await page.getByRole('button', { name: 'Redo', exact: true }).click();
  await expect.poll(async () => (await savedNode(request, source.saved.id, source.text.id)).style?.textRotation).toBe(-90);
  await page.getByRole('button', { name: 'Reset title rotation', exact: true }).click();
  await page.getByRole('button', { name: 'Rotate title right', exact: true }).click();
  await expect.poll(async () => (await savedNode(request, source.saved.id, source.text.id)).style?.titleRotation).toBe(90);
  const beforeReload = await savedNode(request, source.saved.id, source.text.id);
  expect([beforeReload.width, beforeReload.height]).toEqual([source.text.width, source.text.height]);
  const effectiveFonts = await tile(page, source.text.id).evaluate(card => {
    const scale = Number((card as HTMLElement).dataset.contentFit) || 1;
    return { title: Number.parseFloat(getComputedStyle(card.querySelector('.ms-node-title')!).fontSize) * scale,
      body: Number.parseFloat(getComputedStyle(card.querySelector('.ms-text')!).fontSize) * scale };
  });
  // Ordinary fixed frames should rewrap rotated text before shrinking its
  // glyphs. Merely keeping microscopic text inside the frame is insufficient.
  expect(effectiveFonts.body).toBeGreaterThanOrEqual(10);
  expect(effectiveFonts.title).toBeGreaterThanOrEqual(13);
  await page.screenshot({ path: info.outputPath('font-picker-and-rotation.png') });
  await page.reload(); await appearance(page, source.text);
  await expect(picker).toContainText('Georgia');
  await expect(page.getByRole('spinbutton', { name: 'Title rotation (°)', exact: true })).toHaveValue('90');
  await expect(page.getByRole('spinbutton', { name: 'Text rotation (°)', exact: true })).toHaveValue('-90');
  await assertTextEnvelope(tile(page, source.text.id));

  await page.getByRole('button', { name: 'New Node', exact: true }).click();
  await page.getByRole('menuitem', { name: 'Node', exact: true }).click();
  const viewport = (await page.locator('.ms-viewport').boundingBox())!;
  await page.mouse.click(viewport.x + 950, viewport.y + 450);
  const dialog = page.getByRole('dialog', { name: 'New Node', exact: true }); await expect(dialog).toBeVisible();
  await dialog.getByLabel('Type', { exact: true }).selectOption('media');
  await dialog.getByLabel('Content', { exact: true }).selectOption('text');
  await dialog.getByLabel('Title', { exact: true }).fill('Named font from creation form');
  await dialog.getByLabel('Text body', { exact: true }).fill('This new text block starts with Calibri.');
  const creationPicker = dialog.getByRole('combobox', { name: 'Font', exact: true });
  const creationPopup = dialog.locator(`[id="${await creationPicker.getAttribute('aria-controls')}"]`);
  await creationPicker.click(); expect(await creationPopup.getByRole('option').count()).toBe(FONT_FAMILIES.length);
  const creationSearch = creationPopup.getByRole('searchbox', { name: 'Search fonts', exact: true });
  await creationSearch.fill('Calibri'); await creationPopup.getByRole('option', { name: 'Calibri', exact: true }).click();
  await expect(creationPicker).toContainText('Calibri');
  expect(await dialog.locator('.creation-preview-tile').evaluate(item => getComputedStyle(item).fontFamily)).toContain('Calibri');
  await creationPicker.click(); await creationSearch.fill('Georgia'); await creationSearch.press('Escape');
  await expect(creationPopup).toBeHidden(); await expect(dialog).toBeVisible(); await expect(creationPicker).toContainText('Calibri');
  await creationPicker.click(); await creationSearch.press('Tab');
  await expect(creationPopup).toBeHidden(); await expect(dialog.getByLabel('Content font size', { exact: true })).toBeFocused();
  await page.screenshot({ path: info.outputPath('new-node-named-font.png') });
  await dialog.getByRole('button', { name: 'Create node', exact: true }).click(); await expect(dialog).toHaveCount(0);
  await expect.poll(async () => (await readProject(request, source.saved.id)).nodes.find(node => node.title === 'Named font from creation form')?.style?.fontFamily).toBe('calibri');
  expect(errors).toEqual([]);
});

test('Rotated text fits shaped and fixed frames, leaves images upright and preserves legacy fonts in an offline publication', async ({ page, request, browser }, info) => {
  const source = await openFixture(page, request, true);
  await expect(tile(page, source.text.id).locator('.ms-node-title')).toHaveAttribute('data-text-rotation', '-90');
  await expect(tile(page, source.circle.id).locator('.ms-text')).toHaveAttribute('data-text-rotation', '45');
  await assertTextEnvelope(tile(page, source.text.id)); await assertTextEnvelope(tile(page, source.circle.id), true);
  const circleBodySize = await tile(page, source.circle.id).evaluate(card => Number.parseFloat(getComputedStyle(card.querySelector('.ms-text')!).fontSize) * (Number((card as HTMLElement).dataset.contentFit) || 1));
  expect(circleBodySize).toBeGreaterThanOrEqual(9.5);
  const image = tile(page, source.image.id).locator('.ms-image'); await expect(image).toHaveCount(1);
  expect(await image.evaluate(item => getComputedStyle(item).transform)).toBe('none');
  expect(await image.evaluate(item => !!item.closest('.ms-rotated-text-frame'))).toBe(false);
  expect(await tile(page, source.mono.id).locator('.ms-text').evaluate(item => getComputedStyle(item).fontFamily)).toContain('Consolas');
  expect(await tile(page, source.circle.id).locator('.ms-text').evaluate(item => getComputedStyle(item).fontFamily)).toContain('Georgia');
  await page.screenshot({ path: info.outputPath('rotated-text-in-fixed-shapes.png') });
  const persisted = await readProject(request, source.saved.id);
  const filename = info.outputPath('typography-offline.html');
  await writeFile(filename, buildPublication(persisted, await readFile('dist/viewer.js', 'utf8'), await readFile('dist/scene.css', 'utf8')));
  const context = await browser.newContext({ offline: true, viewport: { width: 1500, height: 950 } });
  try {
    const viewer = await context.newPage(); const errors: string[] = []; viewer.on('pageerror', error => errors.push(error.message));
    await viewer.goto(pathToFileURL(filename).href); await viewer.getByRole('button', { name: 'Fit Work', exact: true }).click();
    const payload = JSON.parse((await viewer.locator('#meridian-data').textContent())!).project as MeridianProject;
    for (const original of [source.text, source.image, source.circle, source.mono]) {
      expect(payload.nodes.find(node => node.id === original.id)?.style).toEqual(original.style);
      await expect(tile(viewer, original.id)).toBeVisible();
    }
    expect(payload.assets.find(asset => asset.id === source.asset.id)?.base64).toBe(png);
    await assertTextEnvelope(tile(viewer, source.text.id)); await assertTextEnvelope(tile(viewer, source.circle.id), true);
    expect(await tile(viewer, source.image.id).locator('.ms-image').evaluate(item => getComputedStyle(item).transform)).toBe('none');
    await expect(tile(viewer, source.text.id).locator('.ms-node-title')).toHaveAttribute('data-text-rotation', '-90');
    await expect(tile(viewer, source.circle.id).locator('.ms-text')).toHaveAttribute('data-text-rotation', '45');
    await viewer.screenshot({ path: info.outputPath('offline-typography.png') }); expect(errors).toEqual([]);
  } finally { await context.close(); }
});
