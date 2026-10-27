import { test, expect, type Page } from '@playwright/test';
import { readFile, writeFile } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';
import { addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import { buildPublication } from '../src/publication.ts';

test('reading long passages, attachment previews and captions scrolls text without moving the camera, including at boundaries', async ({ page, request, browser }, info) => {
  const project = newProject('Read within the canvas'), canvasId = project.entryCanvasId;
  const passage = addNode(project, canvasId, 'text', 'A full passage');
  // These intentionally bounded tiles exercise text scrolling. Automatic tiles
  // are allowed to grow to show their complete contents instead.
  Object.assign(passage, { x: 100, y: 100, width: 280, height: 300, sizeMode: 'fixed', childLayout: 'spatial', text: 'A line of preserved epic text to read within this tile.\n'.repeat(160) });
  const attachment = addNode(project, canvasId, 'document', 'Source preview');
  Object.assign(attachment, { x: 440, y: 100, width: 280, height: 300, sizeMode: 'fixed', assetId: 'long-source' });
  project.assets.push({ id: 'long-source', name: 'Source.txt', mime: 'text/plain', base64: Buffer.from(passage.text!).toString('base64') });
  const caption = addNode(project, canvasId, 'image', 'An annotated image');
  Object.assign(caption, { x: 780, y: 100, width: 280, height: 300, sizeMode: 'fixed', assetId: 'pixel', text: 'An extended caption explaining the source and its limits.\n'.repeat(100) });
  project.assets.push({ id: 'pixel', name: 'Pixel.png', mime: 'image/png', base64: 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a2ioAAAAASUVORK5CYII=' });
  const exercise = async (surface: Page) => {
    const transform = () => surface.locator('.ms-world').getAttribute('style');
    const camera = await transform();
    for (const [id, selector] of [[passage.id, '.ms-text'], [attachment.id, '.ms-text-preview'], [caption.id, '.ms-caption']]) {
      const text = surface.locator(`.ms-node[data-node-id="${id}"] ${selector}`);
      await expect(text).toBeVisible();
      const overflow = await text.evaluate(node => node.scrollHeight - node.clientHeight); expect(overflow).toBeGreaterThan(30);
      await text.hover(); await surface.mouse.wheel(0, 180);
      await expect.poll(() => text.evaluate(node => node.scrollTop)).toBeGreaterThan(0);
      expect(await transform()).toBe(camera);
      // Firefox bounds the distance of a single native wheel gesture. Set up
      // the end position explicitly, then exercise a real wheel at the boundary.
      await text.evaluate(node => { node.scrollTop = node.scrollHeight; });
      await expect.poll(() => text.evaluate(node => node.scrollHeight - node.clientHeight - node.scrollTop)).toBeLessThanOrEqual(1);
      await surface.mouse.wheel(0, 400);
      await surface.evaluate(() => new Promise<void>(resolve => requestAnimationFrame(() => requestAnimationFrame(() => resolve()))));
      expect(await transform()).toBe(camera);
      await text.evaluate(node => { node.scrollTop = 0; });
      await expect.poll(() => text.evaluate(node => node.scrollTop)).toBe(0);
      await surface.mouse.wheel(0, -400);
      await surface.evaluate(() => new Promise<void>(resolve => requestAnimationFrame(() => requestAnimationFrame(() => resolve()))));
      expect(await transform()).toBe(camera);
    }
    const viewport = await surface.locator('.ms-viewport').boundingBox(); expect(viewport).not.toBeNull();
    await surface.mouse.move(viewport!.x + 10, viewport!.y + 10); await surface.mouse.wheel(0, 200);
    await expect.poll(transform).not.toBe(camera);
  };
  await page.setViewportSize({ width: 1900, height: 1100 });
  expect((await request.post('/api/projects', { data: commitSnapshots(project) })).ok()).toBeTruthy();
  await page.goto(`/?project=${project.id}`); await exercise(page);
  const file = info.outputPath('reading-scroll.html');
  await writeFile(file, buildPublication(project, await readFile('dist/viewer.js', 'utf8'), await readFile('dist/scene.css', 'utf8')));
  const context = await browser.newContext({ offline: true, viewport: { width: 1900, height: 1100 } });
  try { const viewer = await context.newPage(); await viewer.goto(pathToFileURL(file).href); await exercise(viewer); }
  finally { await context.close(); }
});
