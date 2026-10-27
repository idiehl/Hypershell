import { test, expect } from '@playwright/test';
import { build } from 'esbuild';
import { readFile } from 'node:fs/promises';

let script: string, css: string;
test.beforeAll(async () => {
  script = (await build({ stdin: { contents: "import {createDocumentViewer,documentActionButton} from './src/document-viewer.ts';window.readers={createDocumentViewer,documentActionButton};", resolveDir: process.cwd(), loader: 'ts' }, bundle: true, write: false, format: 'iife', platform: 'browser', target: 'es2022' })).outputFiles[0]!.text;
  css = await readFile('src/scene.css', 'utf8');
});

test('paper and pop-out document controls contain icons, remain labeled, and activate with the keyboard', async ({ page }, info) => {
  await page.route(/^https?:/, route => route.abort()); await page.setContent('<main></main>'); await page.addStyleTag({ content: css }); await page.addScriptTag({ content: script });
  await page.evaluate(() => {
    const app = (window as any).readers; (window as any).clicks = [];
    const actions = document.createElement('div'); actions.className = 'ms-document-actions';
    actions.append(app.documentActionButton('read', 'Read Catalogue.csv in this node', () => (window as any).clicks.push('read')), app.documentActionButton('reading-view', 'Open Catalogue.csv in reading view', () => (window as any).clicks.push('reading-view'))); document.querySelector('main')!.append(actions);
  });
  const buttons = page.locator('.ms-document-icon-button'); await expect(buttons).toHaveCount(2);
  for (const button of await buttons.all()) { await expect(button).toHaveText(''); await expect(button.locator('svg')).toHaveAttribute('aria-hidden', 'true'); await expect(button).toHaveAttribute('title', await button.getAttribute('aria-label') ?? ''); const box = (await button.boundingBox())!; expect(box.width).toBeGreaterThanOrEqual(28); expect(box.height).toBeGreaterThanOrEqual(28); }
  await buttons.nth(0).focus(); await page.keyboard.press('Enter'); await buttons.nth(1).focus(); await page.keyboard.press('Space'); expect(await page.evaluate(() => (window as any).clicks)).toEqual(['read', 'reading-view']);
  await page.screenshot({ path: info.outputPath('document-icon-actions.png') });
});

test('repeated reader replacement reuses decoded preparation while original download, reading state and URL cleanup remain correct', async ({ page }) => {
  await page.route(/^https?:/, route => route.abort()); await page.setContent('<main style="width:640px;height:420px"></main>'); await page.addStyleTag({ content: css }); await page.addScriptTag({ content: script });
  const source = '\uFEFFName,Description\r\n' + Array.from({ length: 965 }, (_, i) => `Type ${i},${'A useful complete description '.repeat(8)}`).join('\r\n'), asset = { id: 'catalogue', name: 'Catalogue.csv', mime: 'text/csv', base64: Buffer.from(source).toString('base64') };
  await page.evaluate(asset => {
    const decode = window.atob.bind(window), create = URL.createObjectURL.bind(URL), revoke = URL.revokeObjectURL.bind(URL), stats = { decodes: 0, created: 0, revoked: 0 }; (window as any).readerStats = stats;
    window.atob = value => { stats.decodes++; return decode(value); }; URL.createObjectURL = blob => { stats.created++; return create(blob); }; URL.revokeObjectURL = value => { stats.revoked++; return revoke(value); };
    const app = (window as any).readers; let reader: any;
    (window as any).replaceReader = () => { const state = reader?.captureState(); reader?.dispose(); reader = app.createDocumentViewer(structuredClone(asset), { state }); document.querySelector('main')!.replaceChildren(reader.element); };
    (window as any).disposeReader = () => reader.dispose(); (window as any).replaceReader();
  }, asset);
  await page.getByRole('button', { name: 'Next document page', exact: true }).click(); await expect(page.getByRole('spinbutton', { name: 'Document page', exact: true })).toHaveValue('2');
  for (let i = 0; i < 12; i++) await page.evaluate(() => (window as any).replaceReader());
  await expect(page.getByRole('spinbutton', { name: 'Document page', exact: true })).toHaveValue('2'); await expect(page.locator('tbody')).toContainText('Type 50');
  expect(await page.evaluate(() => (window as any).readerStats.decodes)).toBe(1);
  await page.getByRole('searchbox', { name: 'Find in document', exact: true }).fill('Type 964'); await expect(page.locator('tbody tr')).toHaveCount(1); await page.evaluate(() => (window as any).replaceReader()); await expect(page.locator('tbody')).toContainText('Type 964');
  const request = page.waitForEvent('download'); await page.getByRole('link', { name: 'Download original Catalogue.csv', exact: true }).click(); const download = await request; expect(await readFile((await download.path())!)).toEqual(Buffer.from(asset.base64, 'base64'));
  await page.evaluate(() => (window as any).disposeReader()); const stats = await page.evaluate(() => (window as any).readerStats); expect(stats.created).toBe(stats.revoked); expect(stats.decodes).toBe(1);
});

test('prepared Markdown keeps all pages reachable and renders authored markup inertly across redraw', async ({ page }) => {
  await page.route(/^https?:/, route => route.abort()); await page.setContent('<main style="width:640px;height:420px"></main>'); await page.addStyleTag({ content: css }); await page.addScriptTag({ content: script });
  const source = '# Guide\n\n' + Array.from({ length: 180 }, (_, i) => `## Type ${i}\n\nstd::basic_string_view: **readable** [source](https://example.com/${i}).\n\n`).join('') + '\n<script>window.unsafe=true</script>', asset = { id: 'guide', name: 'Guide.md', mime: 'application/octet-stream', base64: Buffer.from(source).toString('base64') };
  await page.evaluate(asset => { const reader = (window as any).readers.createDocumentViewer(asset); document.querySelector('main')!.append(reader.element); (window as any).guide = reader; }, asset);
  await expect(page.getByRole('heading', { name: 'Guide', exact: true })).toBeVisible(); await expect(page.locator('.mdv-prose')).toContainText('std::basic_string_view');
  const maximum = Number(await page.getByRole('spinbutton', { name: 'Document page', exact: true }).getAttribute('max')); await page.getByRole('spinbutton', { name: 'Document page', exact: true }).fill(String(maximum)); await page.getByRole('spinbutton', { name: 'Document page', exact: true }).press('Tab');
  await expect(page.locator('.mdv-prose')).toContainText('window.unsafe=true'); expect(await page.evaluate(() => (window as any).unsafe)).toBeUndefined(); await expect(page.locator('.mdv-prose script')).toHaveCount(0);
  await page.evaluate(() => (window as any).guide.dispose());
});
