#!/usr/bin/env node
/** Read the installed Library and tutorial through the actual editor.
 * No API data is mocked. Every mutation is refused before it reaches the host. */
import assert from 'node:assert/strict';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { chromium } from '@playwright/test';
import { DEFAULT_DIRECTORY, hash } from './reorganize.mjs';

const directory = path.resolve(process.argv[2] ?? DEFAULT_DIRECTORY), out = path.join(directory, 'saved-editor-review');
const json = async file => JSON.parse((await readFile(file, 'utf8')).replace(/^\uFEFF/, ''));
const receipt = await json(path.join(directory, 'application-receipt.json'));
assert.equal(receipt.completed, true, 'The application receipt does not confirm a completed save.');
const host = new URL(receipt.origin);
assert.ok(host.protocol === 'http:' && ['127.0.0.1', 'localhost', '[::1]'].includes(host.hostname), 'Only a local Meridian host is permitted.');
async function get(endpoint) {
  const response = await fetch(new URL(endpoint, host), { method: 'GET', signal: AbortSignal.timeout(20000) });
  assert.ok(response.ok, `${endpoint} GET failed (${response.status}).`); return response.json();
}
const [library, tutorial, catalog, composition] = await Promise.all([
  get('/api/library'), get(`/api/projects/${receipt.tutorialId}`), get('/api/library/catalog'), json(path.join(directory, 'library-composition.json')),
]);
assert.equal(library.revision, receipt.librarySavedRevision);
assert.equal(tutorial.revision, receipt.tutorialRevision);
assert.equal(hash(library), receipt.savedLibrarySHA256, 'The saved Library differs from its application receipt.');
assert.equal(hash(tutorial), receipt.savedTutorialSHA256, 'The saved tutorial differs from its application receipt.');
const targets = Object.entries(library.canvasTargets).map(([nodeId, target]) => {
  const project = catalog.find(project => project.id === target.projectId); assert.ok(project, `Missing saved project for ${nodeId}.`);
  const work = project.canvases.find(work => work.id === (target.canvasId ?? project.entryCanvasId)); assert.ok(work, `Missing saved Work for ${nodeId}.`);
  return { nodeId, projectId: project.id, workId: work.id, title: work.title };
});
assert.equal(targets.length, 82); assert.equal(tutorial.canvases.length, 12); assert.equal(tutorial.nodes.length, 456);
await mkdir(out, { recursive: true });
const browser = await chromium.launch({ headless: true });
const context = await browser.newContext({ viewport: { width: 1600, height: 1050 }, deviceScaleFactor: 1 });
const page = await context.newPage(), attemptedMutations = [], blockedExternal = [], browserErrors = [], requests = [], checks = [];
page.on('pageerror', error => browserErrors.push(error.message));
await context.route('**/*', async route => {
  const request = route.request(), url = new URL(request.url()), method = request.method();
  if (['data:', 'blob:'].includes(url.protocol)) return route.continue();
  if (url.origin !== host.origin) { blockedExternal.push({ url: url.href, method }); return route.abort(); }
  if (method !== 'GET' && method !== 'HEAD') {
    attemptedMutations.push({ endpoint: url.pathname, method });
    return route.fulfill({ status: 403, contentType: 'application/json', body: JSON.stringify({ error: 'Mutation refused by read-only saved-editor verification.' }) });
  }
  requests.push({ endpoint: url.pathname, method }); return route.continue();
});
const card = id => page.locator(`.ms-node[data-node-id="${id}"]`);
async function ready() { await page.locator('.workspace').waitFor(); await page.evaluate(() => document.fonts.ready); }
async function fit() {
  const viewport = page.locator('.ms-viewport'), frame = await viewport.boundingBox(); assert.ok(frame);
  // A Window's later rows retain world coordinates outside its visible frame.
  // Its outer card owns that footprint; hidden scrolling rows must not force
  // the entire Work's overview to shrink to the logical table address space.
  const boxes = () => page.locator('.ms-node[data-node-id]').evaluateAll(elements => elements.filter(element => !element.closest('.ms-window-viewport')).map(element => { const rect = element.getBoundingClientRect(); return { x: rect.x, y: rect.y, right: rect.right, bottom: rect.bottom }; }));
  const before = await boxes(); assert.ok(before.length);
  const current = await page.locator('.ms-world').evaluate(element => Number(getComputedStyle(element).getPropertyValue('--ms-camera-scale')));
  const width = Math.max(...before.map(box => box.right)) - Math.min(...before.map(box => box.x));
  const height = Math.max(...before.map(box => box.bottom)) - Math.min(...before.map(box => box.y));
  const scale = Math.min(1, current * (frame.width - 80) / width, current * (frame.height - 80) / height);
  const corner = { x: frame.x + 8, y: frame.y + 8 }; await page.mouse.move(corner.x, corner.y); await page.mouse.wheel(0, -Math.log(scale / current) / .0013);
  await page.waitForFunction(expected => Math.abs(Number(getComputedStyle(document.querySelector('.ms-world')).getPropertyValue('--ms-camera-scale')) - expected) < .00001, scale);
  const after = await boxes(), dx = frame.x + frame.width / 2 - (Math.min(...after.map(box => box.x)) + Math.max(...after.map(box => box.right))) / 2;
  const dy = frame.y + frame.height / 2 - (Math.min(...after.map(box => box.y)) + Math.max(...after.map(box => box.bottom))) / 2;
  await page.mouse.move(corner.x, corner.y); await page.mouse.down(); await page.mouse.move(corner.x + dx, corner.y + dy, { steps: 8 }); await page.mouse.up(); return scale;
}
async function screenshot(name) { await page.mouse.move(8, 8); await page.screenshot({ path: path.join(out, name) }); return name; }
try {
  await page.goto(host.origin + '/'); await ready(); await page.getByRole('button', { name: 'Reset view', exact: true }).click();
  const libraryScale = await fit(); assert.equal(await page.locator('.ms-canvas-open').count(), 6);
  checks.push({ name: 'Actual saved Library r478 opens with three categories and six entry portals', passed: true, scale: libraryScale, screenshot: await screenshot('01-saved-library-fit.png') });
  const tutorialEntry = card(composition.tutorialEntryId).locator('.ms-canvas-open'); await tutorialEntry.waitFor({ state: 'visible' }); await tutorialEntry.click();
  await page.waitForURL(url => url.searchParams.get('project') === tutorial.id && url.searchParams.get('canvas') === tutorial.entryCanvasId); await ready();
  await page.getByRole('button', { name: 'Reset view', exact: true }).click(); const tutorialScale = await fit();
  checks.push({ name: 'Actual saved Tutorial entry portal opens the saved Start here Work', passed: true, workId: tutorial.entryCanvasId, scale: tutorialScale, screenshot: await screenshot('02-saved-tutorial-start.png') });
  const firstWork = tutorial.canvases.find(work => work.title.startsWith('01 ')); assert.ok(firstWork);
  const firstPortal = tutorial.nodes.find(node => node.canvasId === tutorial.entryCanvasId && node.kind === 'canvas-link' && [firstWork.id, firstWork.rootId].includes(node.targetId)); assert.ok(firstPortal);
  await card(firstPortal.id).locator('.ms-canvas-open').click(); await page.waitForURL(url => url.searchParams.get('project') === tutorial.id && url.searchParams.get('canvas') === firstWork.id); await ready();
  await page.getByRole('button', { name: 'Reset view', exact: true }).click(); const partScale = await fit();
  assert.ok(await page.locator('.ms-node').count() > 10, 'The saved tutorial part is unexpectedly empty.');
  checks.push({ name: 'Actual saved first tutorial part opens from the Start here menu', passed: true, workId: firstWork.id, title: firstWork.title, scale: partScale, screenshot: await screenshot('03-saved-first-part.png') });
  const [libraryAfter, tutorialAfter] = await Promise.all([get('/api/library'), get(`/api/projects/${tutorial.id}`)]);
  assert.deepEqual(libraryAfter, library, 'The saved Library changed during read-only verification.');
  assert.deepEqual(tutorialAfter, tutorial, 'The saved tutorial changed during read-only verification.');
  assert.deepEqual(browserErrors, []); assert.deepEqual(blockedExternal, []);
  const report = { passed: attemptedMutations.length === 0, checkedAt: new Date().toISOString(), origin: host.origin,
    libraryRevision: library.revision, tutorialRevision: tutorial.revision, savedLibrarySHA256: hash(library), savedTutorialSHA256: hash(tutorial),
    destinationCount: targets.length, tutorialWorkCount: tutorial.canvases.length, tutorialNodeCount: tutorial.nodes.length,
    checks, attemptedMutations, blockedExternal, browserErrors, requests, savedDataUnchanged: true, apiResponsesMocked: false, liveWrites: false };
  await writeFile(path.join(out, 'review.json'), JSON.stringify(report, null, 2) + '\n');
  await writeFile(path.join(out, 'destinations.json'), JSON.stringify(targets, null, 2) + '\n');
  console.log(JSON.stringify(report, null, 2));
  assert.deepEqual(attemptedMutations, [], 'The application attempted mutations; all were refused and logged.');
} catch (error) {
  await page.screenshot({ path: path.join(out, 'failure.png') }).catch(() => {});
  await writeFile(path.join(out, 'failure.json'), JSON.stringify({ error: error.stack ?? String(error), checks, attemptedMutations, blockedExternal, browserErrors, requests }, null, 2)); throw error;
} finally { await context.close().catch(() => {}); await browser.close().catch(() => {}); }
