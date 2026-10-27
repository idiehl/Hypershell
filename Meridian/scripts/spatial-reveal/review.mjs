/** Review staged compositions in the offline standalone viewer. No server or live writes. */
import assert from 'node:assert/strict';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';
import path from 'node:path';
import { chromium } from '@playwright/test';
import { buildPublication } from '../../src/publication.ts';

const root = 'output/spatial-reveal', manifest = JSON.parse(await readFile(`${root}/manifest.json`, 'utf8'));
const [js, css] = await Promise.all(['viewer.js', 'scene.css'].map(name => readFile(`dist/${name}`, 'utf8')));
const browser = await chromium.launch(), results = [];
try {
  for (const item of manifest) {
    const project = JSON.parse(await readFile(item.candidateFile, 'utf8'));
    const folder = `${root}/review-${item.id}`; await mkdir(folder, { recursive: true });
    const html = path.resolve(`${folder}/preview.html`); await writeFile(html, buildPublication(project, js, css), 'utf8');
    const page = await browser.newPage({ viewport: { width: 1600, height: 1100 } }), errors = [];
    page.on('pageerror', error => errors.push(error.message)); await page.route(/^https?:/, route => route.abort());
    await page.goto(pathToFileURL(html).href);
    await page.getByLabel('Choose canvas', { exact: true }).selectOption(item.reviewCanvasId);
    await page.getByRole('button', { name: 'Fit canvas', exact: true }).click();
    await page.evaluate(async () => { await document.fonts.ready; await Promise.all([...document.images].map(image => image.decode().catch(() => {}))); });
    await page.screenshot({ path: `${folder}/collapsed.png` });
    const targets = item.reviewNodes.chapter ? [item.reviewNodes.chapter] : [item.reviewNodes.relationships, item.reviewNodes.tiles];
    const checks = [];
    for (const id of targets) {
      const selector = `[data-node-id="${id}"]`, node = page.locator(selector);
      const before = await node.boundingBox();
      await node.dblclick({ position: { x: 25, y: 25 } });
      assert.equal(await page.getByLabel('Choose canvas', { exact: true }).inputValue(), item.reviewCanvasId, 'Disclosure must stay in the same canvas');
      assert.equal(await node.getAttribute('aria-expanded'), 'true');
      const after = await node.boundingBox();
      for (const key of ['x', 'y', 'width', 'height']) assert.ok(Math.abs(after[key] - before[key]) < 1, `Parent tile ${key} changed on reveal`);
      const children = project.nodes.filter(child => child.parentId === id);
      for (const child of children) assert.equal(await page.locator(`[data-node-id="${child.id}"]`).count(), 1);
      await page.evaluate(async () => { await Promise.all([...document.images].map(image => image.decode().catch(() => {}))); });
      checks.push({ nodeId: id, children: children.length, stableParentTile: true, sameCanvas: true });
    }
    await page.getByRole('button', { name: 'Fit canvas', exact: true }).click();
    await page.screenshot({ path: `${folder}/expanded.png` });
    const metrics = await page.locator('.ms-node').evaluateAll(cards => cards.map(card => {
      const title = card.querySelector('.ms-node-title'), body = card.querySelector('.ms-node-body');
      return { id: card.dataset.nodeId, title: title?.textContent, titleClipped: !!title && (title.scrollWidth > title.clientWidth + 2 || title.scrollHeight > title.clientHeight + 2), bodyClipped: [...card.querySelectorAll('.ms-text,.ms-caption')].some(element => element.scrollHeight > element.clientHeight + 2 || element.scrollWidth > element.clientWidth + 2), bodyScrollable: !!body && body.scrollHeight > body.clientHeight + 2, images: [...card.querySelectorAll('img')].map(image => ({ loaded: image.complete && image.naturalWidth > 0, width: image.naturalWidth, height: image.naturalHeight })) };
    }));
    assert.ok(metrics.every(node => node.images.every(image => image.loaded)), 'An image failed to load');
    assert.deepEqual(errors, []);
    for (const id of targets) {
      await page.locator(`[data-node-id="${id}"]`).dblclick({ position: { x: 25, y: 25 } });
      assert.equal(await page.locator(`[data-node-id="${id}"]`).getAttribute('aria-expanded'), 'false');
      for (const child of project.nodes.filter(node => node.parentId === id)) assert.equal(await page.locator(`[data-node-id="${child.id}"]`).count(), 0);
    }
    results.push({ id: item.id, canvasId: item.reviewCanvasId, folder, checks, errors, metrics }); await page.close();
  }
} finally { await browser.close(); }
await writeFile(`${root}/offline-review.json`, JSON.stringify(results, null, 2) + '\n', 'utf8');
console.log(JSON.stringify(results.map(result => ({ id: result.id, canvasId: result.canvasId, checks: result.checks, clipped: result.metrics.filter(node => node.titleClipped || node.bodyClipped).map(node => node.title), images: result.metrics.flatMap(node => node.images).length })), null, 2));
