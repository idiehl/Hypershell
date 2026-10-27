import assert from 'node:assert/strict';
import { readFile, writeFile, mkdir, readdir, access } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { createServer } from 'node:http';
import { fileURLToPath, pathToFileURL } from 'node:url';
import path from 'node:path';

// Use a fresh browser process and static files only. No editor host or project API is used.
const root = fileURLToPath(new URL('../../', import.meta.url));
const examples = path.join(root, 'examples/greek-mythology');
const output = path.join(root, 'output/mythology-qa');
const htmlPath = path.join(examples, 'greek-mythology.html');
const html = await readFile(htmlPath, 'utf8');
const manifest = JSON.parse(await readFile(path.join(examples, 'collection-manifest.json'), 'utf8'));
const embedHtml = await readFile(path.join(examples, 'embed-example.html'), 'utf8');
const extract = content => JSON.parse(content.match(/<script\b[^>]*\bid=["']meridian-data["'][^>]*>([\s\S]*?)<\/script>/i)?.[1] ?? 'null');
const payload = extract(html);
assert.equal(payload?.project?.id, manifest.projectId, 'Publication and manifest must describe the same build');
const project = payload.project;
const hash = data => createHash('sha256').update(data).digest('hex');
const node = key => {
  const found = project.nodes.find(n => n.id === manifest.nodeKeys[key]);
  assert.ok(found, `Manifest node ${key} exists`); return found;
};
const canvas = key => {
  const found = manifest.canvases.find(c => c.key === key);
  assert.ok(found, `Manifest canvas ${key} exists`); return found;
};
const signal = value => value === true ? 'On' : value === false ? 'Off' : 'Unknown';
await mkdir(output, { recursive: true });

// Firefox's diagnostic install must be selected before Playwright is imported.
const originalCache = process.env.PLAYWRIGHT_BROWSERS_PATH ?? path.join(process.env.LOCALAPPDATA ?? '', 'ms-playwright');
process.env.PLAYWRIGHT_BROWSERS_PATH = path.join(root, '.cache/playwright-diagnostic');
const { chromium, firefox, expect } = await import('@playwright/test');
expect.configure({ timeout: 7000 });
let chromiumExecutable = process.env.MERIDIAN_CHROMIUM;
if (!chromiumExecutable) {
  const installed = (await readdir(originalCache)).filter(name => /^chromium_headless_shell-\d+$/.test(name)).sort((a, b) => Number(b.split('-').at(-1)) - Number(a.split('-').at(-1)));
  for (const dir of installed) {
    const candidate = path.join(originalCache, dir, 'chrome-headless-shell-win64/chrome-headless-shell.exe');
    try { await access(candidate); chromiumExecutable = candidate; break; } catch { /* Try next installed version. */ }
  }
}
assert.ok(chromiumExecutable, 'An installed Chromium headless shell or MERIDIAN_CHROMIUM is required');

const report = {
  verifiedAt: new Date().toISOString(), publicationSha256: hash(html), manifestSha256: hash(await readFile(path.join(examples, 'collection-manifest.json'))),
  projectId: project.id, canvases: project.canvases.length, assets: project.assets.length, engines: [],
};
let totalFailures = 0;

for (const [engine, browserType, launchOptions] of [['chromium', chromium, { executablePath: chromiumExecutable }], ['firefox', firefox, {}]]) {
  const result = { engine, checks: [], errors: [], httpRequests: [], imageDecodes: [], downloads: [], embedded: [] };
  report.engines.push(result);
  const browser = await browserType.launch({ headless: true, ...launchOptions });
  result.version = browser.version();
  const context = await browser.newContext({ viewport: { width: 1500, height: 1050 }, offline: true, acceptDownloads: true });
  context.setDefaultTimeout(7000);
  const page = await context.newPage();
  page.on('pageerror', error => result.errors.push(error.message));
  page.on('request', request => { if (/^https?:/.test(request.url())) result.httpRequests.push(request.url()); });
  const card = key => page.locator(`[data-node-id="${node(key).id}"]`);
  const state = key => card(key).locator('.ms-state');
  const choose = async key => {
    await page.getByLabel('Choose canvas', { exact: true }).selectOption(canvas(key).id);
    await page.getByRole('button', { name: 'Fit canvas', exact: true }).click();
  };
  const run = () => page.getByRole('button', { name: 'Run simulation', exact: true }).click();
  const stop = () => page.getByRole('button', { name: 'Stop simulation', exact: true }).click();
  const reset = () => page.getByRole('button', { name: 'Reset to authored state', exact: true }).click();
  const toggle = key => card(key).getByRole('switch').click();
  async function check(name, task) {
    try { await task(); result.checks.push({ name, passed: true }); console.log(`${engine}: PASS ${name}`); }
    catch (error) {
      totalFailures++; result.checks.push({ name, passed: false, error: error.stack ?? String(error) });
      console.error(`${engine}: FAIL ${name}: ${error.message}`);
      await page.screenshot({ path: path.join(output, `viewer-${engine}-failure-${result.checks.length}.png`) }).catch(() => {});
      await page.goto(pathToFileURL(htmlPath).href).catch(() => {});
    }
  }
  try {
    await page.goto(pathToFileURL(htmlPath).href);
    await check('Standalone file starts stopped with every authored canvas available', async () => {
      await expect(page.getByRole('status')).toHaveText('Stopped');
      await expect(page.locator('.mv-canvas-select option')).toHaveCount(project.canvases.length);
      assert.equal(project.canvases.length, manifest.canvases.length);
      assert.deepEqual(await page.locator('#meridian-data').evaluate(el => JSON.parse(el.textContent)), payload);
    });
    await check('Every canvas navigates, runs, stops, and decodes its displayed raster images offline', async () => {
      for (const c of manifest.canvases) {
        await choose(c.key);
        await expect(page.getByRole('status')).toHaveText('Stopped');
        assert.ok(await page.locator('.ms-node').count(), `${c.key} has rendered nodes`);
        const images = await page.locator('.ms-image').evaluateAll(async elements => Promise.all(elements.map(async image => {
          await image.decode(); return { name: image.alt, width: image.naturalWidth, height: image.naturalHeight };
        })));
        for (const image of images) { assert.ok(image.width > 0 && image.height > 0); result.imageDecodes.push({ canvas: c.key, ...image }); }
        await run(); await expect(page.getByRole('status')).toHaveText('Running');
        assert.ok((await page.locator('.ms-state').allTextContents()).every(text => text !== 'Stopped'));
        await stop(); await expect(page.getByRole('status')).toHaveText('Stopped');
        assert.ok((await page.locator('.ms-state').allTextContents()).every(text => text === 'Stopped'));
      }
      const rasterNames = project.assets.filter(a => /^image\/(png|jpeg|gif|webp|avif|bmp)$/.test(a.mime)).map(a => a.name).sort();
      assert.deepEqual([...new Set(result.imageDecodes.map(i => i.name))].sort(), rasterNames);
    });
    await check('Seven authored gate results and the deliberately unwired output are correct', async () => {
      await reset(); await choose('lab'); await run();
      for (const [operation, expected] of Object.entries(manifest.gateDefaults)) await expect(state(`truth-${operation}-output`)).toHaveText(signal(expected));
      await expect(state('missing-input-output')).toHaveText('Unknown');
      await expect(state('voyage-output')).toHaveText('On');
    });
    await check('Exposed input, switch reopening, Stop preservation, and live reference respond through real controls', async () => {
      await reset(); await choose('lab'); await run();
      await toggle('crew-ready'); await expect(state('voyage-output')).toHaveText('Off');
      await expect(card('crew-live').locator('.ms-reference-value')).toHaveText('Off');
      await toggle('crew-ready'); await expect(state('voyage-output')).toHaveText('On');
      await toggle('voyage-switch'); await expect(state('voyage-output')).toHaveText('Unknown');
      await stop(); await expect(state('voyage-output')).toHaveText('Stopped');
      await expect(card('voyage-switch').getByRole('switch')).toHaveAttribute('aria-checked', 'false');
      await run(); await expect(state('voyage-output')).toHaveText('Unknown');
      await toggle('voyage-switch'); await expect(state('voyage-output')).toHaveText('On');
      await stop(); await toggle('crew-ready'); await expect(state('voyage-output')).toHaveText('Stopped');
      await run(); await expect(state('voyage-output')).toHaveText('Off');
    });
    await check('Cross-canvas reference stays pinned while its exposed source changes', async () => {
      await reset(); await choose('home'); await run(); await toggle('archive-reviewed');
      await expect(card('archive-reviewed').getByRole('switch')).toHaveAttribute('aria-checked', 'false');
      await choose('lab'); await expect(state('voyage-output')).toHaveText('On');
      const savedRef = project.nodes.find(n => n.canvasId === canvas('lab').id && n.kind === 'reference' && n.targetId === node('archive-reviewed').id);
      await expect(page.locator(`[data-node-id="${savedRef.id}"] .ms-reference-value`)).toHaveText('On');
      await expect(page.locator(`[data-node-id="${savedRef.id}"] .ms-reference-status`)).toContainText('Pinned');
      await choose('home'); await expect(card('archive-reviewed').getByRole('switch')).toHaveAttribute('aria-checked', 'false');
    });
    await check('Collapsed preparation keeps its sibling output alive and expansion restores children', async () => {
      await reset(); await choose('lab'); await run();
      await card('voyage-system').getByRole('button', { name: `Collapse ${node('voyage-system').title}`, exact: true }).click();
      await expect(card('crew-ready')).toHaveCount(0); await expect(state('voyage-output')).toHaveText('On');
      await card('voyage-system').getByRole('button', { name: `Expand ${node('voyage-system').title}`, exact: true }).click();
      await expect(card('crew-ready')).toHaveCount(1); await expect(state('voyage-output')).toHaveText('On');
      await page.screenshot({ path: path.join(output, `viewer-${engine}-lab.png`) });
    });
    await check('Save a copy retains the immutable authored payload and runs again offline', async () => {
      await reset(); await choose('lab'); await run(); await toggle('crew-ready'); await toggle('voyage-switch');
      await card('voyage-system').getByRole('button', { name: `Collapse ${node('voyage-system').title}`, exact: true }).click();
      const event = page.waitForEvent('download');
      await page.getByRole('button', { name: 'Download this published canvas for viewing or importing into the Meridian editor', exact: true }).click();
      const downloaded = await event; const copyPath = path.join(output, `viewer-${engine}-authored-copy.html`); await downloaded.saveAs(copyPath);
      const copyHtml = await readFile(copyPath, 'utf8'); assert.deepEqual(extract(copyHtml), payload);
      assert.equal((copyHtml.match(/<script\b[^>]*\btype=["']module["']/g) ?? []).length, 0);
      await page.goto(pathToFileURL(copyPath).href); await expect(page.getByRole('status')).toHaveText('Stopped');
      await choose('lab'); await expect(card('crew-ready').getByRole('switch')).toHaveAttribute('aria-checked', 'true');
      await expect(card('voyage-switch').getByRole('switch')).toHaveAttribute('aria-checked', 'true');
      await run(); await expect(state('voyage-output')).toHaveText('On');
      await page.goto(pathToFileURL(htmlPath).href);
    });
    await check('TXT and PDF open locally; TXT/PDF/WAV/SVG/JSON downloads retain exact bytes', async () => {
      const wanted = ['collection-reader.txt', 'mythology-companion.pdf', 'original-plucked-motif.wav', 'source-versions.svg', 'source-register.json'];
      for (const filename of wanted) {
        const asset = project.assets.find(a => a.name === filename); assert.ok(asset, filename);
        const attachment = project.nodes.find(n => n.assetId === asset.id); assert.ok(attachment);
        await choose(manifest.canvases.find(c => c.id === attachment.canvasId).key);
        await page.locator(`[data-node-id="${attachment.id}"]`).getByRole('button', { name: `Open ${filename}`, exact: true }).click();
        const dialog = page.locator('dialog.ms-asset-dialog'); await expect(dialog).toBeVisible();
        if (asset.mime === 'text/plain') {
          const text = await dialog.locator('pre').textContent();
          const authoredText = Buffer.from(asset.base64, 'base64').toString('utf8');
          assert.ok(text.length > 1000 && authoredText.startsWith(text), 'TXT preview renders an unaltered prefix of the authored text');
          result.textPreview = { filename, previewCharacters: text.length, authoredCharacters: authoredText.length, complete: text === authoredText };
        } else if (asset.mime === 'application/pdf') {
          const preview = dialog.locator('object[type="application/pdf"]'); await expect(preview).toHaveCount(1);
          const url = await preview.getAttribute('data'); assert.ok(url.startsWith('blob:'));
          const previewBytes = await page.evaluate(async url => Array.from(new Uint8Array(await (await fetch(url)).arrayBuffer())), url);
          assert.equal(hash(Buffer.from(previewBytes)), hash(Buffer.from(asset.base64, 'base64')));
          await expect(dialog.locator('.ms-preview-note')).toContainText('download');
          await page.screenshot({ path: path.join(output, `viewer-${engine}-pdf.png`) });
        } else {
          await expect(dialog.locator('object,iframe,svg,audio,script')).toHaveCount(0);
          await expect(dialog).toContainText('download to open');
        }
        const event = page.waitForEvent('download'); await dialog.getByRole('button', { name: `Download ${filename}`, exact: true }).click();
        const downloaded = await event; assert.equal(downloaded.suggestedFilename(), filename);
        const target = path.join(output, `viewer-${engine}-${filename}`); await downloaded.saveAs(target);
        const bytes = await readFile(target); assert.equal(hash(bytes), hash(Buffer.from(asset.base64, 'base64')));
        result.downloads.push({ filename, bytes: bytes.length, sha256: hash(bytes) });
        await dialog.getByRole('button', { name: 'Close attachment', exact: true }).click(); await expect(dialog).toHaveCount(0);
      }
    });
    await check('Offline run emitted no HTTP requests or JavaScript runtime errors', async () => {
      assert.deepEqual(result.httpRequests, []); assert.deepEqual(result.errors, []);
    });
  } finally { await context.close(); }

  // Serve only the generated embed example and publication on an ephemeral local port.
  const server = createServer((request, response) => {
    const url = new URL(request.url, 'http://localhost');
    const body = url.pathname.endsWith('/greek-mythology.html') ? html : url.pathname === '/' || url.pathname === '/embed-example.html' ? embedHtml : null;
    response.writeHead(body ? 200 : 404, { 'Content-Type': 'text/html;charset=utf-8' }); response.end(body ?? 'Not found');
  });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  const base = `http://127.0.0.1:${server.address().port}`;
  try {
    for (const viewport of [{ width: 1500, height: 1050 }, { width: 390, height: 844 }]) {
      const embedded = { viewport, passed: false, errors: [], externalRequests: [] }; result.embedded.push(embedded);
      const embeddedContext = await browser.newContext({ viewport });
      try {
        const page = await embeddedContext.newPage(); page.setDefaultTimeout(7000);
        page.on('pageerror', e => embedded.errors.push(e.message));
        page.on('request', req => { if (/^https?:/.test(req.url()) && !req.url().startsWith(base + '/')) embedded.externalRequests.push(req.url()); });
        await page.goto(`${base}/embed-example.html`);
        const frame = page.frameLocator('iframe[title="Greek mythology Meridian collection"]');
        await expect(frame.getByRole('status')).toHaveText('Stopped');
        await frame.getByLabel('Choose canvas', { exact: true }).selectOption(canvas('lab').id);
        for (const name of ['Run simulation', 'Stop simulation', 'Reset to authored state', 'Fit canvas']) await expect(frame.getByRole('button', { name, exact: true })).toBeInViewport();
        await frame.getByRole('button', { name: 'Run simulation', exact: true }).click(); await expect(frame.getByRole('status')).toHaveText('Running');
        await expect(frame.locator(`[data-node-id="${node('voyage-output').id}"] .ms-state`)).toHaveText('On');
        await frame.getByRole('button', { name: 'Stop simulation', exact: true }).click(); await expect(frame.getByRole('status')).toHaveText('Stopped');
        const childFrame = page.frames().find(f => f.url().endsWith('/greek-mythology.html')); assert.ok(childFrame);
        const dimensions = await childFrame.evaluate(() => ({ width: innerWidth, document: document.documentElement.scrollWidth, scene: document.querySelector('.mv-scene').getBoundingClientRect().height }));
        assert.ok(dimensions.document <= dimensions.width + 1, 'Embedded document does not overflow horizontally');
        assert.ok(dimensions.scene > 200, 'Embedded scene retains usable height');
        assert.deepEqual(embedded.errors, []); assert.deepEqual(embedded.externalRequests, []);
        await page.screenshot({ path: path.join(output, `viewer-${engine}-embed-${viewport.width}.png`), fullPage: true });
        embedded.passed = true; console.log(`${engine}: PASS iframe ${viewport.width}px`);
      } catch (error) { totalFailures++; embedded.error = error.stack ?? String(error); console.error(`${engine}: FAIL iframe ${viewport.width}px: ${error.message}`); }
      finally { await embeddedContext.close(); }
    }
  } finally { await new Promise(resolve => server.close(resolve)); await browser.close(); }
  await writeFile(path.join(output, `viewer-${engine}-verification.json`), JSON.stringify(result, null, 2));
}
report.passed = totalFailures === 0;
report.failures = totalFailures;
await writeFile(path.join(output, 'viewer-verification.json'), JSON.stringify(report, null, 2));
console.log(JSON.stringify({ passed: report.passed, failures: totalFailures, engines: report.engines.map(r => ({ engine: r.engine, checks: r.checks.length, imageDecodes: r.imageDecodes.length, downloads: r.downloads.length, embeds: r.embedded.length })) }, null, 2));
if (totalFailures) process.exitCode = 1;
