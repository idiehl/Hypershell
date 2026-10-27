import { test, expect, type Page } from '@playwright/test';
import { build } from 'esbuild';
import { readFile } from 'node:fs/promises';
import { addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import { buildPublication } from '../src/publication.ts';
import type { MeridianProject } from '../src/core/types.ts';

let sceneScript: string, viewerScript: string, css: string;
test.beforeAll(async () => {
  css = (await Promise.all(['scene', 'grid', 'gallery-scene', 'window-scene', 'expansion-tray-scene', 'canvas-launcher', 'backgrounds', 'cluster-button'].map(name => readFile(`src/${name}.css`, 'utf8')))).join('\n');
  [sceneScript, viewerScript] = await Promise.all([
    build({ stdin: { contents: `import {renderScene} from './src/scene.ts';import {revealDocumentNode,rememberDocumentNodeSize} from './src/core/document-node.ts';window.documentHarness={renderScene,revealDocumentNode,rememberDocumentNodeSize};`, resolveDir: process.cwd(), loader: 'ts' }, bundle: true, write: false, format: 'iife', platform: 'browser', target: 'es2022' }).then(result => result.outputFiles[0]!.text),
    build({ entryPoints: ['src/viewer.ts'], bundle: true, write: false, format: 'iife', platform: 'browser', target: 'es2022' }).then(result => result.outputFiles[0]!.text),
  ]);
});
const tile = (page: Page, id: string) => page.locator(`.ms-node[data-node-id="${id}"]`);
const state = (page: Page) => page.evaluate(() => structuredClone((window as any).documentTest.state));
const model = (page: Page): Promise<MeridianProject> => page.evaluate(() => structuredClone((window as any).documentTest.project));
function fixture(kind: 'csv' | 'markdown' = 'csv', width = 320, height = 200) {
  const project = newProject('Document node reading'), source = kind === 'csv'
    ? '\uFEFFName,Description,Number\r\n' + Array.from({ length: 250 }, (_, index) => `Type ${index},"A complete original description for type ${index}",${index}`).join('\r\n')
    : '# Complete guide\n\n' + Array.from({ length: 100 }, (_, index) => `## Section ${index}\n\nA **readable original** passage with [source](https://example.com/reference/${index}). ${'Long explanatory material. '.repeat(12)}\n`).join('\n');
  const asset = { id: 'original-attachment', name: kind === 'csv' ? 'Catalogue.csv' : 'Complete guide.md', mime: kind === 'csv' ? 'text/csv' : 'application/octet-stream', base64: Buffer.from(source).toString('base64') };
  project.assets.push(asset); const node = addNode(project, project.entryCanvasId, 'document', 'Read the original');
  Object.assign(node, { x: 60, y: 70, width, height, sizeMode: 'fixed', assetId: asset.id, contentType: 'asset', childPosition: 'manual', style: { shape: 'rectangle', fontFamily: 'calibri', titleSize: 20, fontSize: 16 } });
  delete node.collapsed; delete node.containerSizes;
  const other = addNode(project, project.entryCanvasId, 'media', 'Unrelated original'); Object.assign(other, { x: 1100, y: 760, width: 200, height: 110, sizeMode: 'fixed', childPosition: 'manual' });
  project.canvases[0]!.snapping = { stickyEdges: false, smartAlign: false, snapToGrid: false };
  return { project, node, other, asset, source };
}
async function mount(page: Page, f: ReturnType<typeof fixture>, config: { readonly?: boolean; fit?: boolean; solo?: boolean } = {}) {
  const errors: string[] = []; page.on('pageerror', error => errors.push(error.message));
  await page.route(/^https?:/, route => route.abort());
  if (config.solo) f.project.nodes = f.project.nodes.filter(node => node.id !== f.other.id);
  await page.setContent('<style>html,body{margin:0}#scene{width:1450px;height:950px}</style><main id="scene"></main>');
  await page.addStyleTag({ content: css }); await page.addScriptTag({ content: sceneScript });
  await page.evaluate(({ project, config }) => {
    const api = (window as any).documentHarness, h: any = { project, state: { view: { x: 0, y: 0, scale: 1 }, selections: [], toggles: [], resizes: [], moves: [], opened: [], config } };
    h.draw = () => api.renderScene(document.querySelector('#scene'), h.project, h.project.entryCanvasId, { view: h.state.view, fit: h.state.config.fit, readonly: h.state.config.readonly, selections: h.state.selections,
      onToggle(id: string, field: string, collapsed: boolean) { h.state.toggles.push({ id, field, collapsed }); h.project = api.revealDocumentNode(h.project, id, !collapsed); h.draw(); },
      onSelections(ids: string[]) { h.state.selections = ids; h.draw(); },
      onMoveSelection(ids: string[], dx: number, dy: number) { h.state.moves.push({ ids, dx, dy }); },
      onResize(id: string, width: number, height: number, x: number, y: number) { h.state.resizes.push({ id, width, height }); const node = h.project.nodes.find((n: any) => n.id === id); Object.assign(node, { width, height, x, y, sizeMode: 'fixed' }); api.rememberDocumentNodeSize(h.project, node, { authored: true }); h.draw(); },
      onAssetOpen(id: string) { h.state.opened.push(id); }, onViewChange(view: any) { h.state.view = view; },
    }); (window as any).documentTest = h; h.draw();
  }, { project: f.project, config });
  return errors;
}
async function open(page: Page, id: string) { await tile(page, id).getByRole('button', { name: /^Read .* in this node$/ }).click(); await expect(tile(page, id).locator('.mdv-viewer')).toBeVisible(); }

test('legacy documents start as compact originals and Read/Collapse preserve identities, bytes, and unrelated nodes', async ({ page }, info) => {
  const f = fixture(), errors = await mount(page, f), before = await model(page), compact = (await tile(page, f.node.id).boundingBox())!;
  await expect(tile(page, f.node.id).locator('.mdv-viewer')).toHaveCount(0); await expect(tile(page, f.node.id)).toHaveAttribute('aria-expanded', 'false'); expect((await model(page)).nodes.find(node => node.id === f.node.id)!.containerSizes).toBeUndefined();
  await open(page, f.node.id); const expanded = (await tile(page, f.node.id).boundingBox())!; expect(expanded.width).toBeGreaterThan(compact.width); expect(expanded.height).toBeGreaterThan(compact.height);
  const current = await model(page); expect(current.assets).toEqual(before.assets); expect(current.nodes.map(node => node.id)).toEqual(before.nodes.map(node => node.id)); expect(current.nodes.find(node => node.id === f.other.id)).toEqual(f.other);
  await page.screenshot({ path: info.outputPath('original-document-expanded-table.png') });
  await tile(page, f.node.id).getByRole('button', { name: 'Collapse document viewer', exact: true }).click(); await expect(tile(page, f.node.id).locator('.mdv-viewer')).toHaveCount(0);
  await expect(tile(page, f.node.id)).toHaveAttribute('aria-expanded', 'false');
  const closed = (await tile(page, f.node.id).boundingBox())!; expect(closed.width).toBeCloseTo(compact.width, 4); expect(closed.height).toBeCloseTo(compact.height, 4); expect(errors).toEqual([]);
});

test('embedded table controls page and search the original, download exact bytes, and never move the canvas', async ({ page }) => {
  const f = fixture(); await mount(page, f); await open(page, f.node.id); const reader = tile(page, f.node.id).locator('.mdv-viewer');
  await expect(reader.locator('tbody tr')).toHaveCount(50); await reader.getByRole('button', { name: 'Next document page', exact: true }).click(); await expect(reader.locator('tbody')).toContainText('Type 50');
  await reader.getByRole('searchbox', { name: 'Find in document', exact: true }).fill('Type 249'); await expect(reader.locator('tbody tr')).toHaveCount(1); await expect(reader.locator('tbody')).toContainText('Type 249');
  const camera = (await state(page)).view; await reader.getByRole('searchbox', { name: 'Find in document', exact: true }).press('Escape');
  const scroll = reader.locator('.mdv-scroll'); await scroll.hover(); await page.mouse.wheel(0, 300); await expect.poll(() => scroll.evaluate(element => element.scrollTop)).toBeGreaterThan(0); expect((await state(page)).view).toEqual(camera);
  const request = page.waitForEvent('download'); await reader.getByRole('link', { name: `Download original ${f.asset.name}`, exact: true }).click(); const download = await request; expect(download.suggestedFilename()).toBe(f.asset.name); expect(await readFile((await download.path())!)).toEqual(Buffer.from(f.asset.base64, 'base64'));
  expect((await state(page)).moves).toEqual([]); expect((await state(page)).resizes).toEqual([]); expect((await model(page)).assets).toEqual(f.project.assets);
});

test('ordinary scene redraws preserve the current document page and local search without saving view state into the node', async ({ page }) => {
  const f = fixture(); await mount(page, f); await open(page, f.node.id); const reader = tile(page, f.node.id).locator('.mdv-viewer');
  await reader.getByRole('button', { name: 'Next document page', exact: true }).click(); await expect(reader.getByRole('spinbutton', { name: 'Document page', exact: true })).toHaveValue('2');
  await reader.locator('.mdv-scroll').evaluate(element => { element.scrollTop = 180; }); await page.evaluate(() => new Promise(requestAnimationFrame));
  const before = await model(page); await page.evaluate(() => { const h = (window as any).documentTest; h.project = structuredClone(h.project); h.draw(); });
  await expect(reader.getByRole('spinbutton', { name: 'Document page', exact: true })).toHaveValue('2'); await expect(reader.locator('tbody')).toContainText('Type 50');
  await expect.poll(() => reader.locator('.mdv-scroll').evaluate(element => element.scrollTop)).toBe(180);
  await reader.getByRole('searchbox', { name: 'Find in document', exact: true }).fill('Type 249'); await expect(reader.locator('tbody tr')).toHaveCount(1);
  await page.evaluate(() => { const h = (window as any).documentTest; h.project = structuredClone(h.project); h.draw(); });
  await expect(reader.getByRole('searchbox', { name: 'Find in document', exact: true })).toHaveValue('Type 249'); await expect(reader.locator('tbody tr')).toHaveCount(1); await expect(reader.locator('tbody')).toContainText('Type 249');
  expect(await model(page)).toEqual(before);
  await reader.getByRole('button', { name: 'Collapse document viewer', exact: true }).click(); await open(page, f.node.id);
  await expect(reader.getByRole('searchbox', { name: 'Find in document', exact: true })).toHaveValue('Type 249'); await expect(reader.locator('tbody tr')).toHaveCount(1);
  const reopened = await model(page);
  await reader.getByRole('searchbox', { name: 'Find in document', exact: true }).fill(''); await expect(reader.locator('tbody tr')).toHaveCount(50);
  await reader.locator('.mdv-scroll').evaluate(element => { element.scrollTop = 120; }); await page.evaluate(() => new Promise(requestAnimationFrame));
  await page.evaluate(() => { const old = document.querySelector('#scene')!; const next = document.createElement('main'); next.id = 'scene'; old.replaceWith(next); (window as any).documentTest.draw(); });
  await expect(reader.getByRole('searchbox', { name: 'Find in document', exact: true })).toHaveValue(''); await expect(reader.locator('tbody tr')).toHaveCount(50);
  await expect.poll(() => reader.locator('.mdv-scroll').evaluate(element => element.scrollTop)).toBe(120);
  expect(await model(page)).toEqual(reopened);
});

test('document frame resize remembers independent expanded and compact dimensions', async ({ page }) => {
  const f = fixture(); await mount(page, f); await open(page, f.node.id); await tile(page, f.node.id).locator('.ms-node-title').click();
  let box = (await tile(page, f.node.id).boundingBox())!; await page.mouse.move(box.x + box.width - 1, box.y + box.height / 2); await page.mouse.down(); await page.mouse.move(box.x + box.width + 39, box.y + box.height / 2, { steps: 5 }); await page.mouse.up();
  expect((await state(page)).resizes).toHaveLength(1); const expanded = (await tile(page, f.node.id).boundingBox())!;
  await tile(page, f.node.id).getByRole('button', { name: 'Collapse document viewer', exact: true }).click(); box = (await tile(page, f.node.id).boundingBox())!;
  await page.mouse.move(box.x + box.width - 1, box.y + box.height / 2); await page.mouse.down(); await page.mouse.move(box.x + box.width + 29, box.y + box.height / 2, { steps: 5 }); await page.mouse.up();
  expect((await state(page)).resizes).toHaveLength(2); const compact = (await tile(page, f.node.id).boundingBox())!; await open(page, f.node.id); expect((await tile(page, f.node.id).boundingBox())!.width).toBeCloseTo(expanded.width, 4);
  await tile(page, f.node.id).getByRole('button', { name: 'Collapse document viewer', exact: true }).click(); expect((await tile(page, f.node.id).boundingBox())!.width).toBeCloseTo(compact.width, 4);
});

test('54000-world-unit original nodes render a logical reading frame with accessible controls at fit zoom', async ({ page }, info) => {
  const f = fixture('markdown', 54000, 28000), errors = await mount(page, f, { fit: true, solo: true }); await open(page, f.node.id);
  const card = tile(page, f.node.id), reading = card.locator('.ms-document-reading-frame'), reader = card.locator('.mdv-viewer'), box = (await card.boundingBox())!, view = (await page.locator('.ms-viewport').boundingBox())!;
  await expect(card).toHaveAttribute('data-content-fit', 'document'); await expect(reading).toHaveCSS('width', '640px'); expect(box.width).toBeLessThan(view.width); expect(box.height).toBeLessThan(view.height);
  const close = (await reader.getByRole('button', { name: 'Collapse document viewer', exact: true }).boundingBox())!; expect(close.width).toBeGreaterThan(20); expect(close.height).toBeGreaterThan(8);
  await expect(reader.getByRole('heading', { name: 'Complete guide', exact: true })).toBeVisible(); await expect(reader.locator('.mdv-prose strong').first()).toHaveText('readable original');
  await page.screenshot({ path: info.outputPath('huge-world-document-reading-frame.png') }); await reader.getByRole('button', { name: 'Open larger document reading view', exact: true }).click(); expect((await state(page)).opened).toEqual([f.asset.id]); expect(errors).toEqual([]);
});

test('very small positive document frames keep bounded finite readers after opening', async ({ page }, info) => {
  for (const [width, height] of [[32, 24], [4, 3], [.5, .25], [.25, .125]]) {
    const f = fixture('csv', width, height); await mount(page, f, { fit: true, solo: true });
    await page.evaluate(id => { const h = (window as any).documentTest; h.project = (window as any).documentHarness.revealDocumentNode(h.project, id, true); h.draw(); }, f.node.id);
    const card = tile(page, f.node.id), metrics = await card.locator('.ms-document-stage').evaluate(stage => { const frame = stage.firstElementChild as HTMLElement, bounds = stage.getBoundingClientRect(), reading = frame.getBoundingClientRect(); const card = stage.closest('.ms-node') as HTMLElement, body = stage.parentElement!; return { width: bounds.width, height: bounds.height, readingWidth: reading.width, readingHeight: reading.height, transform: frame.style.transform, cardWidth: card.getBoundingClientRect().width, cardStyle: card.style.cssText, stageStyle: getComputedStyle(stage).width, bodyStyle: getComputedStyle(body).width, frameStyle: frame.style.cssText }; });
    expect(metrics.width, JSON.stringify({ width, height, metrics })).toBeGreaterThan(0); expect(metrics.height, JSON.stringify({ width, height, metrics })).toBeGreaterThan(0);
    expect(Number.isFinite(metrics.readingWidth) && Number.isFinite(metrics.readingHeight)).toBe(true); expect(metrics.readingWidth, JSON.stringify({ width, height, metrics })).toBeLessThanOrEqual(metrics.width + 2); expect(metrics.readingHeight, JSON.stringify({ width, height, metrics })).toBeLessThanOrEqual(metrics.height + 2);
  }
  await page.screenshot({ path: info.outputPath('small-document-logical-frame.png') });
});

test('readonly publication readers open and collapse native table and Markdown nodes without changing embedded data', async ({ page }, info) => {
  await page.route(/^https?:/, route => route.abort());
  for (const kind of ['csv', 'markdown'] as const) {
    const f = fixture(kind), html = buildPublication(commitSnapshots(f.project), viewerScript, css); await page.setContent(html); const embedded = await page.locator('#meridian-data').textContent();
    await open(page, f.node.id); const reader = tile(page, f.node.id).locator('.mdv-viewer'); await expect(reader).toHaveAttribute('data-document-format', kind); await expect(page.locator('[contenteditable=true]')).toHaveCount(0);
    if (kind === 'csv') await expect(reader.locator('tbody tr')).toHaveCount(50); else await expect(reader.getByRole('heading', { name: 'Complete guide', exact: true })).toBeVisible();
    await reader.getByRole('button', { name: 'Collapse document viewer', exact: true }).click(); await expect(tile(page, f.node.id).locator('.mdv-viewer')).toHaveCount(0); expect(await page.locator('#meridian-data').textContent()).toBe(embedded);
    await page.screenshot({ path: info.outputPath(`readonly-${kind}-document.png`) });
  }
});


test('table text inherits each authored dark or light reader palette and remains readable', async ({ page }, info) => {
  for (const [name, fill, textColor] of [['dark', '#152333', '#e0eceb'], ['light', '#f6f0e5', '#17313e']] as const) {
    const f = fixture(); Object.assign(f.node.style!, { fill, textColor }); await mount(page, f); await open(page, f.node.id);
    const reader = tile(page, f.node.id).locator('.mdv-viewer');
    const colors = await reader.evaluate(element => ({ reader: getComputedStyle(element).color, cell: getComputedStyle(element.querySelector('td')!).color, header: getComputedStyle(element.querySelector('thead th:last-child')!).color }));
    expect(colors.cell).toBe(colors.reader); expect(colors.header).toBe(colors.reader);
    const luminance = (color: string) => { const rgb = color.match(/[0-9.]+/g)!.slice(0, 3).map(Number).map(value => value / 255).map(value => value <= .04045 ? value / 12.92 : ((value + .055) / 1.055) ** 2.4); return .2126 * rgb[0]! + .7152 * rgb[1]! + .0722 * rgb[2]!; };
    const base = [1, 3, 5].map(index => Number.parseInt(fill.slice(index, index + 2), 16)), background = luminance(`rgb(${base.join(',')})`), foreground = luminance(colors.cell);
    expect((Math.max(background, foreground) + .05) / (Math.min(background, foreground) + .05)).toBeGreaterThanOrEqual(4.5);
    await page.screenshot({ path: info.outputPath(`native-table-${name}-palette.png`) });
  }
});

test('compact recognized document cards show authored titles and icon controls without filename or format scaffolding', async ({ page }, info) => {
  for (const [name, mime] of [['Catalogue.csv', 'text/csv'], ['Guide.md', 'text/markdown'], ['Notes.txt', 'text/plain'], ['Reading.pdf', 'application/pdf']]) {
    const f = fixture(); Object.assign(f.asset, { name, mime }); f.node.title = 'Type Index';
    const errors = await mount(page, f), card = tile(page, f.node.id);
    await expect(card.locator('.ms-node-title')).toHaveText('Type Index');
    await expect(card.locator('.ms-file-placeholder')).toHaveCount(0);
    await expect(card.locator('.ms-document-icon-button')).toHaveCount(2);
    await expect(card).not.toContainText(name);
    await expect(card).toHaveText('Type Index');
    const read = card.getByRole('button', { name: `Read ${name} in this node`, exact: true });
    const reading = card.getByRole('button', { name: `Read ${name} in a larger view`, exact: true });
    await expect(read).toHaveText(''); await expect(reading).toHaveText('');
    await expect(read).toHaveAttribute('title', `Read ${name} in this node`);
    await reading.click(); expect((await state(page)).opened).toEqual([f.asset.id]);
    expect((await model(page)).assets).toEqual(f.project.assets); expect(errors).toEqual([]);
  }
  await page.screenshot({ path: info.outputPath('compact-document-title-and-icons.png') });
});

test('unsupported attachment cards retain their original filename fallback and open action', async ({ page }) => {
  const f = fixture(); Object.assign(f.asset, { name: 'Original.archive', mime: 'application/octet-stream' });
  await mount(page, f); const card = tile(page, f.node.id);
  await expect(card.locator('.ms-file-placeholder')).toHaveText('Document · Original.archive');
  await expect(card.locator('.ms-document-icon-button')).toHaveCount(0);
  await card.getByRole('button', { name: 'Open Original.archive', exact: true }).click();
  expect((await state(page)).opened).toEqual([f.asset.id]);
  expect((await model(page)).assets).toEqual(f.project.assets);
});
