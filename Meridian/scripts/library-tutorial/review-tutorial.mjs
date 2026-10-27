/** Offline review of the native Tutorial. Reads authored files, exercises a fresh
 * Chromium viewer, and writes evidence only. No host requests or live writes. */
import assert from 'node:assert/strict';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { createHash } from 'node:crypto';
import { chromium } from 'playwright';
import { evaluate } from '../../src/core/engine.ts';
import { createModule, instantiateModule } from '../../src/core/modules.ts';
import { revealChildren } from '../../src/core/reveal.ts';

const args = new Map();
for (let at = 2; at < process.argv.length; at += 2) args.set(process.argv[at].replace(/^--/, ''), process.argv[at + 1]);
const directory = path.resolve(args.get('input') ?? 'output/library-tutorial-20261021/tutorial');
const out = path.resolve(args.get('out') ?? path.join(directory, 'browser-review'));
await mkdir(out, { recursive: true });
const read = async name => JSON.parse((await readFile(path.join(directory, name), 'utf8')).replace(/^\uFEFF/, ''));
const [project, aliases, coverage] = await Promise.all(['draft.json', 'aliases.json', 'coverage.json'].map(read));
const hash = bytes => createHash('sha256').update(bytes).digest('hex');
const files = ['draft.json', 'aliases.json', 'coverage.json', 'publication.html'];
const sourceHashes = Object.fromEntries(await Promise.all(files.map(async file => [file, hash(await readFile(path.join(directory, file)))])));
const expected = coverage.expected;
assert(Array.isArray(expected), 'coverage.expected must contain the native fixture expectations.');
const nodeId = key => { const id = aliases.nodes[key]; assert(id, `Missing alias: ${key}`); return id; };
const node = key => { const result = project.nodes.find(node => node.id === nodeId(key)); assert(result, `Missing node: ${key}`); return result; };
const workId = key => { const id = aliases.canvases[key]; assert(id, `Missing Work: ${key}`); return id; };
const reports = [], browserErrors = [], networkRequests = [];
let screenshots = 0, initialPayload;
const browser = await chromium.launch({ headless: true });
const context = await browser.newContext({ viewport: { width: 1600, height: 1100 }, acceptDownloads: true, reducedMotion: 'no-preference' });
const page = await context.newPage();
page.setDefaultTimeout(12000);
page.on('pageerror', error => browserErrors.push(error.message));
await page.route(/^https?:/, route => { networkRequests.push(route.request().url()); return route.abort(); });
const card = key => page.locator(`.ms-node[data-node-id="${nodeId(key)}"]`);
const state = key => card(key).locator('.ms-state');
const keyById = id => Object.keys(aliases.nodes).find(key => aliases.nodes[key] === id);
const assertState = async (key, value) => {
  const text = await state(key).innerText();
  if (typeof value === 'number') assert(Math.abs(Number(text) - value) <= Math.max(1e-10, Math.abs(value) * 1e-10), `${key}: ${text} != ${value}`);
  else assert.equal(text, value === null ? 'Unknown' : value === true ? 'On' : value === false ? 'Off' : String(value), key);
  return text;
};
const choose = async key => { await page.getByLabel('Choose Work', { exact: true }).selectOption(workId(key)); };
const closePanel = async () => { const button = page.getByRole('button', { name: 'Close project navigation', exact: true }); if (await button.isVisible()) await button.click(); };
async function focus(key, { keepPanel = false } = {}) {
  const id = nodeId(key);
  if (await page.getByRole('button', { name: 'Explore project', exact: true }).getAttribute('aria-expanded') !== 'true')
    await page.getByRole('button', { name: 'Explore project', exact: true }).click();
  await page.getByRole('searchbox', { name: 'Search project', exact: true }).fill(id);
  await page.locator(`[data-navigation-node="${id}"]`).click();
  if (!keepPanel) await closePanel();
}
async function screenshot(name) {
  await page.evaluate(async () => { await document.fonts.ready; await Promise.all([...document.images].map(image => image.decode().catch(() => {}))); });
  const file = `${String(++screenshots).padStart(2, '0')}-${name}.png`;
  await page.screenshot({ path: path.join(out, file) }); return file;
}
async function test(name, run) {
  try { const evidence = await run(); reports.push({ ...evidence, name, passed: true }); }
  catch (error) { let capture; try { capture = await screenshot(`FAILED-${name.replace(/[^a-z0-9]/gi, '-').slice(0, 70)}`); } catch {} reports.push({ name, passed: false, error: error.stack ?? String(error), screenshot: capture }); }
}
async function numberEdit(key, value) {
  const input = card(key).locator('input[type=number]'); await input.fill(String(value)); await input.press('Enter');
}
async function toggleContents(key) {
  await focus(key, { keepPanel: true });
  await page.getByRole('button', { name: 'Toggle contents from properties', exact: true }).click();
  await closePanel();
}
async function downloadAttachment(key) {
  const original = node(key), asset = project.assets.find(asset => asset.id === original.assetId);
  assert(asset, `${key} has no original attachment.`);
  await focus(key, { keepPanel: true });
  await page.getByRole('button', { name: 'Open selected attachment', exact: true }).click();
  const dialog = page.locator('.ms-asset-dialog').last();
  assert((await dialog.innerText()).includes(asset.name));
  const waiting = page.waitForEvent('download');
  await dialog.getByRole('button', { name: `Download ${asset.name}`, exact: true }).click();
  const download = await waiting, downloaded = await readFile(await download.path());
  const originalBytes = Buffer.from(asset.base64, 'base64');
  assert.deepEqual(downloaded, originalBytes, 'Attachment bytes changed during download.');
  await dialog.getByRole('button', { name: 'Close attachment', exact: true }).click(); await closePanel();
  return { name: asset.name, bytes: downloaded.length, sha256: hash(downloaded), suggestedFilename: download.suggestedFilename() };
}

try {
  await page.goto(pathToFileURL(path.join(directory, 'publication.html')).href);
  initialPayload = JSON.parse(await page.locator('#meridian-data').textContent());
  await test('Twelve included Works and immutable authored payload', async () => {
    assert.equal(project.canvases.length, 12);
    const options = await page.getByLabel('Choose Work', { exact: true }).locator('option').count(); assert.equal(options, 12);
    const payload = await page.locator('#meridian-data').textContent(); assert.equal(JSON.parse(payload).project.id, project.id);
    return { works: options, projectId: project.id };
  });

  await choose('logic'); await page.getByRole('button', { name: 'Run simulation', exact: true }).click();
  await test('Exposed numeric edits and open Switch propagate', async () => {
    const before = await assertState('logic/sum-output', 5);
    await numberEdit('logic/sum-a', 10); await numberEdit('logic/sum-b', -4);
    const edited = await assertState('logic/sum-output', 6);
    await card('logic/sum-switch').getByRole('switch').click(); const opened = await assertState('logic/sum-output', null);
    await card('logic/sum-switch').getByRole('switch').click(); const closed = await assertState('logic/sum-output', 6);
    await focus('logic/numeric-heading'); return { before, edited, opened, closed, screenshot: await screenshot('numeric-reading') };
  });
  await test('Boolean inputs and AND result', async () => {
    await choose('logic'); await assertState('logic/boolean-output', false); await focus('logic/boolean-b');
    await card('logic/boolean-b').getByRole('switch').click(); await assertState('logic/boolean-output', true);
    return { before: 'Off', after: 'On' };
  });
  await test('All twenty-one registry operations evaluate in the actual viewer', async () => {
    const operations = expected.filter(item => item.kind === 'operation'); assert.equal(operations.length, 21);
    const values = [];
    for (const item of operations) values.push({ operation: item.operationId, expected: item.value, displayed: await assertState(item.output, item.value) });
    return { values };
  });
  await test('Operation Window reaches its final native system', async () => {
    await focus('logic/operations-window');
    const pane = page.locator(`.ms-window-viewport[data-window-id="${nodeId('logic/operations-window')}"]`);
    await pane.focus(); await pane.press('End');
    const scroll = await pane.evaluate(element => ({ position: element.scrollTop, height: element.clientHeight, full: element.scrollHeight }));
    assert(scroll.position > 0 && scroll.position + scroll.height >= scroll.full - 2);
    const last = card('logic/operation-less-title');
    const visible = await last.evaluate(element => { const item = element.getBoundingClientRect(), frame = element.closest('.ms-window-viewport').getBoundingClientRect(); return item.bottom > frame.top && item.top < frame.bottom; });
    assert(visible); return { scroll, screenshot: await screenshot('last-operation-reading') };
  });
  await test('Division by zero becomes Unknown instead of retaining a result', async () => {
    const division = expected.find(item => item.kind === 'operation' && item.operationId === 'DIVIDE'); assert(division);
    await focus(division.inputs[1]); await numberEdit(division.inputs[1], 0);
    const actual = await assertState(division.output, null);
    await numberEdit(division.inputs[1], 3); await assertState(division.output, 2 / 3);
    return { valueWithZeroDivisor: actual };
  });
  await test('Typed routing Joint passes one numeric value to two Outputs', async () => {
    await choose('logic'); await numberEdit('logic/fan-value', 19);
    const values = await Promise.all(['logic/fan-output-a', 'logic/fan-output-b'].map(key => assertState(key, 19)));
    return { values };
  });
  await test('Native cell references follow the live named and addressed cell', async () => {
    await choose('tables');
    await numberEdit('tables/cell-quantity', 12);
    const values = await Promise.all(['tables/cell-ref-name', 'tables/cell-ref-coordinate'].map(async key => {
      const text = await card(key).locator('.ms-reference-value').innerText(); assert.equal(text, '12'); return text;
    }));
    return { values, screenshot: await screenshot('cell-references') };
  });
  await test('Reading Window reaches the sixth original record', async () => {
    await focus('tables/reading-window');
    const pane = page.locator(`.ms-window-viewport[data-window-id="${nodeId('tables/reading-window')}"]`); await pane.focus(); await pane.press('End');
    const last = await card('tables/window-row-6').evaluate(element => { const item = element.getBoundingClientRect(), frame = element.closest('.ms-window-viewport').getBoundingClientRect(); return { text: element.textContent, visible: item.bottom > frame.top && item.top < frame.bottom }; });
    assert(last.visible && last.text.includes('You reached the last row')); return { last, screenshot: await screenshot('window-final-record') };
  });
  await test('Gallery presents all three originals in one stable frame', async () => {
    await choose('media'); await focus('media/gallery-demo');
    const gallery = page.locator(`[data-gallery-id="${nodeId('media/gallery-demo')}"]`), frames = [];
    await gallery.focus(); await gallery.press('Home');
    for (let at = 0; at < 3; at++) {
      const item = gallery.locator('.ms-gallery-item');
      frames.push({ title: await item.locator('.ms-gallery-item-title').innerText(), page: await item.boundingBox(), navigation: await gallery.locator('.ms-gallery-navigation').boundingBox() });
      if (at < 2) await gallery.getByRole('button', { name: /Next object/ }).click();
    }
    assert.equal(frames[2].title, 'Third object'); assert(await gallery.getByRole('button', { name: /Next object/ }).isDisabled());
    for (const frame of frames) { assert(Math.abs(frame.page.width - frame.navigation.width) <= 1.5); assert(Math.abs(frame.page.width - frames[0].page.width) <= 1.5); assert(Math.abs(frame.page.height - frames[0].page.height) <= 1.5); }
    return { frames, screenshot: await screenshot('gallery-final') };
  });
  await test('Local reference is live while another-Work reference retains its saved observation', async () => {
    await choose('reuse');
    assert.equal(await card('reuse/reference-saved').locator('.ms-reference-value').innerText(), '7');
    await numberEdit('reuse/reference-original', 20);
    assert.equal(await card('reuse/reference-local').locator('.ms-reference-value').innerText(), '20');
    return { saved: '7', live: '20', note: 'Runtime Quantity=12 in Tables did not alter the committed snapshot.' };
  });
  await test('The authored Module is a complete running assembly', async () => {
    await choose('reuse'); await assertState('reuse/module-output', 5); await numberEdit('reuse/module-input', 9); await assertState('reuse/module-output', 10);
    await focus('reuse/module-packing'); return { before: 5, after: 10, screenshot: await screenshot('module-reading') };
  });
  await test('Independent Module insertion remaps identities without writing storage', async () => {
    const stored = createModule(project, nodeId('reuse/module-packing'), 'Tutorial add-one practice');
    const one = instantiateModule(project, stored, { canvasId: workId('reuse'), x: 4000, y: 4000 });
    const two = instantiateModule(one.project, stored, { canvasId: workId('reuse'), x: 7000, y: 4000 });
    const firstIds = new Set(Object.values(one.idMap)), secondIds = Object.values(two.idMap); assert(!secondIds.some(id => firstIds.has(id)));
    const changed = structuredClone(two.project);
    const definitionInput = stored.project.nodes.find(node => node.kind === 'number' && node.value === 4); assert(definitionInput);
    const firstInput = one.idMap[definitionInput.id], secondInput = two.idMap[definitionInput.id];
    changed.nodes.find(node => node.id === firstInput).value = 100;
    assert.equal(changed.nodes.find(node => node.id === secondInput).value, 4);
    assert.equal(project.nodes.find(node => node.id === nodeId('reuse/module-input')).value, 4);
    const definitionOutput = stored.project.nodes.find(node => node.kind === 'output'); assert(definitionOutput);
    const evaluated = evaluate(changed);
    assert.equal(evaluated.values[one.idMap[definitionOutput.id]], 101);
    assert.equal(evaluated.values[two.idMap[definitionOutput.id]], 5);
    return { independentRoots: [one.rootId, two.rootId], firstInputValue: 100, secondInputValue: 4, results: [101, 5] };
  });
  await test('Nested Expansion Tray disclosures survive closing the ancestor', async () => {
    await choose('patterns');
    await toggleContents('patterns/exterior-child'); assert.equal(await card('patterns/tray-original-1').count(), 1);
    await toggleContents('patterns/tray-original-2'); assert.equal(await card('patterns/tray-nested-original').count(), 1);
    const before = await card('patterns/tray-nested-original').evaluate(element => ({ left: element.style.left, top: element.style.top, width: element.style.width, height: element.style.height }));
    await toggleContents('patterns/disclosure-parent'); assert.equal(await card('patterns/tray-nested-original').count(), 0);
    await toggleContents('patterns/disclosure-parent'); assert.equal(await card('patterns/tray-nested-original').count(), 1);
    const after = await card('patterns/tray-nested-original').evaluate(element => ({ left: element.style.left, top: element.style.top, width: element.style.width, height: element.style.height }));
    assert.deepEqual(after, before);
    await focus('patterns/tray-original-2'); return { rememberedWorldFrame: after, screenshot: await screenshot('nested-tray-reading') };
  });
  await test('Tray identity, size and expansion-state persistence through pure disclosure', async () => {
    let candidate = revealChildren(project, nodeId('patterns/exterior-child'), true);
    candidate = revealChildren(candidate, nodeId('patterns/tray-original-2'), true);
    const before = structuredClone(candidate.nodes.find(node => node.id === nodeId('patterns/exterior-child')).expansionTray);
    const ids = candidate.nodes.map(node => node.id);
    candidate = revealChildren(candidate, nodeId('patterns/disclosure-parent'), false);
    candidate = revealChildren(candidate, nodeId('patterns/disclosure-parent'), true);
    assert.deepEqual(candidate.nodes.map(node => node.id), ids);
    assert.deepEqual(candidate.nodes.find(node => node.id === nodeId('patterns/exterior-child')).expansionTray, before);
    assert.equal(candidate.nodes.find(node => node.id === nodeId('patterns/tray-original-2')).collapsed, false);
    return { originalCount: ids.length, tray: before, nestedExpanded: true };
  });
  await test('Portal navigation retains Back and Forward history', async () => {
    await choose('navigate'); await focus('navigate/practice-portal');
    await card('navigate/practice-portal').locator('.ms-canvas-open').click(); assert.equal(await page.getByLabel('Choose Work', { exact: true }).inputValue(), workId('author'));
    await page.getByRole('button', { name: 'Back', exact: true }).click(); assert.equal(await page.getByLabel('Choose Work', { exact: true }).inputValue(), workId('navigate'));
    await page.getByRole('button', { name: 'Forward', exact: true }).click(); assert.equal(await page.getByLabel('Choose Work', { exact: true }).inputValue(), workId('author'));
    return { sequence: ['navigate', 'author', 'Back → navigate', 'Forward → author'] };
  });
  await test('Later lesson notes remain reachable in every part', async () => {
    const results = [];
    for (const [work, count] of Object.entries(coverage.byWork).filter(([work, count]) => work !== 'overview' && count > 0)) {
      await choose(work);
      const deskId = nodeId(`${work}/lesson-desk`), pane = page.locator(`.ms-window-viewport[data-window-id="${deskId}"]`);
      await focus(`${work}/lesson-desk`);
      await pane.focus(); await pane.press('End');
      await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
      // Incremental recipes preserve original document order. Lesson order is its
      // address, so a newly inserted earlier lesson can be the final stored node.
      const children = project.nodes.filter(node => node.parentId === deskId), last = children.toSorted((a, b) => (a.cell?.row ?? 0) - (b.cell?.row ?? 0)).at(-1); assert(last);
      const lastCard = page.locator(`.ms-node[data-node-id="${last.id}"]`);
      const visible = await lastCard.evaluate(element => { const box = element.getBoundingClientRect(), pane = element.closest('.ms-window-viewport').getBoundingClientRect(); return box.bottom > pane.top && box.top < pane.bottom; });
      assert(visible, `Last ${work} lesson is unreachable.`); results.push({ work, lessons: count, lastTitle: last.title });
    }
    await focus('reuse/lesson-edge-clusters'); return { results, screenshot: await screenshot('last-lesson-reading') };
  });
  await test('Show on Hover reveals only the background paint', async () => {
    await choose('appearance'); await focus('appearance/hover'); await page.mouse.move(10, 1050);
    const hover = card('appearance/hover'); await hover.blur();
    const before = await hover.getAttribute('data-background-active'); assert.notEqual(before, 'true');
    const originalText = await hover.innerText(); await hover.hover();
    await page.waitForFunction(id => document.querySelector(`.ms-node[data-node-id="${id}"]`)?.getAttribute('data-background-active') === 'true', nodeId('appearance/hover'));
    assert.equal(await hover.innerText(), originalText);
    return { before, after: await hover.getAttribute('data-background-active'), screenshot: await screenshot('hover-revealed-background') };
  });
  await test('Four colored portal styles animate and accelerate on hover', async () => {
    await choose('appearance'); const designs = [];
    for (const style of ['doorway', 'passage', 'wormhole', 'vortex']) {
      const key = `appearance/portal-${style}`; await focus(key); await page.mouse.move(10, 1050);
      const button = card(key).locator('.ms-canvas-open'); assert.equal(await button.getAttribute('data-portal-style'), style);
      const before = Number(await button.getAttribute('data-portal-rate'));
      await button.hover(); await page.waitForFunction(id => Number(document.querySelector(`.ms-node[data-node-id="${id}"] .ms-canvas-open`)?.getAttribute('data-portal-rate')) > 2, nodeId(key));
      const caption = await page.locator('.ms-canvas-destination').last().innerText(); assert.equal(caption, project.canvases.find(canvas => canvas.id === project.entryCanvasId).title);
      const animations = await button.evaluate(element => element.getAnimations({ subtree: true }).map(animation => ({ state: animation.playState, rate: animation.playbackRate })));
      assert(animations.some(animation => animation.state === 'running' && animation.rate > 2));
      designs.push({ style, tint: await button.getAttribute('data-portal-color'), before, accelerated: Number(await button.getAttribute('data-portal-rate')), caption, animationCount: animations.length });
    }
    return { designs, screenshot: await screenshot('portal-vortex-hover') };
  });
  await test('Background Gallery exposes all nine built-in surfaces', async () => {
    const candidates = project.nodes.filter(node => node.canvasId === workId('appearance') && node.containerVariant === 'gallery');
    const gallery = candidates.find(parent => project.nodes.filter(node => node.parentId === parent.id).length >= 9);
    assert(gallery, 'Nine-style background Gallery has not been composed.');
    const galleryKey = keyById(gallery.id); await focus(galleryKey);
    const surface = page.locator(`[data-gallery-id="${gallery.id}"]`); await surface.focus(); await surface.press('Home');
    const children = project.nodes.filter(node => node.parentId === gallery.id), seen = [];
    for (let at = 0; at < children.length; at++) {
      const pageItem = surface.locator('.ms-gallery-item'); const id = await pageItem.getAttribute('data-gallery-item-id'); assert.equal(id, children[at].id);
      seen.push({ title: children[at].title, pattern: children[at].style?.backgroundPattern ?? 'none', images: await pageItem.locator('.ms-node-background').count() });
      if (at + 1 < children.length) await surface.getByRole('button', { name: /Next object/ }).click();
    }
    assert.equal(seen.length, 9); assert(await surface.getByRole('button', { name: /Next object/ }).isDisabled());
    return { seen, screenshot: await screenshot('background-gallery-final') };
  });
  await test('CSV download preserves exact original bytes', async () => ({ download: await downloadAttachment('tables/sample-csv') }));
  await test('Full guide download preserves exact original bytes', async () => ({ download: await downloadAttachment('publish/guide-document') }));
  await test('Local video loads with native playback controls', async () => {
    const asset = project.assets.find(asset => /^video\//.test(asset.mime)); assert(asset, 'No video asset has been composed.');
    const videoNode = project.nodes.find(node => node.assetId === asset.id && node.canvasId === workId('media')); assert(videoNode);
    await focus(keyById(videoNode.id)); const video = page.locator(`.ms-node[data-node-id="${videoNode.id}"] video`);
    assert.equal(await video.count(), 1); await video.evaluate(element => element.load());
    await page.waitForFunction(id => { const media = document.querySelector(`.ms-node[data-node-id="${id}"] video`); return media?.readyState >= 1 || !!media?.error; }, videoNode.id);
    const metadata = await video.evaluate(element => ({ controls: element.controls, duration: element.duration, width: element.videoWidth, height: element.videoHeight, error: element.error?.message ?? null }));
    assert(metadata.controls && metadata.width > 0 && !metadata.error);
    await video.evaluate(async element => { element.muted = true; await element.play(); });
    await page.waitForFunction(id => (document.querySelector(`.ms-node[data-node-id="${id}"] video`)?.currentTime ?? 0) > .05, videoNode.id);
    const playbackTime = await video.evaluate(element => { element.pause(); return element.currentTime; });
    return { name: asset.name, metadata, playbackTime, screenshot: await screenshot('video-reading'), download: await downloadAttachment(keyById(videoNode.id)) };
  });
  await test('Local audio attachment remains downloadable without fabrication', async () => {
    const asset = project.assets.find(asset => /^audio\//.test(asset.mime)); assert(asset, 'No audio asset has been composed.');
    const audioNode = project.nodes.find(node => node.assetId === asset.id && node.canvasId === workId('media')); assert(audioNode);
    return { download: await downloadAttachment(keyById(audioNode.id)), presentation: 'Original local audio attachment; no claim of an unimplemented inline audio player.' };
  });
  await test('External hyperlink retains a safe explicit destination', async () => {
    await choose('media'); const link = card('media/external-link').locator('a');
    assert.equal(await link.getAttribute('href'), 'https://example.com/'); assert((await link.getAttribute('rel')).includes('noopener'));
    return { destination: await link.getAttribute('href'), opened: false };
  });
  await test('Remote GUID relationship retains its original identity without adding a canvas line', async () => {
    const source = node('routes/route-source'), target = node('reuse/related-source');
    const remote = source.remoteLinks?.find(link => link.targetId === target.id); assert(remote, 'Remote GUID relationship is absent.');
    assert(remote.label && remote.id && remote.relation);
    assert(!project.links.some(link => link.sourceId === source.id && link.targetId === target.id), 'A Remote GUID relationship must not fabricate a visual connection.');
    await choose('routes'); await focus('routes/route-source');
    assert.equal(await page.locator(`[data-link-id="${remote.id}"]`).count(), 0);
    return { sourceId: source.id, targetId: target.id, remoteId: remote.id, relation: remote.relation, label: remote.label, visualLine: false, networkFetch: false };
  });
  await test('Authored payload remains unchanged after temporary exploration', async () => {
    const payload = JSON.parse(await page.locator('#meridian-data').textContent());
    assert.deepEqual(payload, initialPayload); return { projectId: payload.project.id, revision: payload.project.revision, note: 'Publication intentionally carries only needed snapshots; compared with its own initial inert payload.' };
  });
} finally {
  await context.close(); await browser.close();
}

const unchanged = Object.fromEntries(await Promise.all(files.map(async file => [file, sourceHashes[file] === hash(await readFile(path.join(directory, file)))])));
const report = { generatedAt: new Date().toISOString(), directory, sourceHashes, unchanged,
  summary: { checks: reports.length, passed: reports.filter(result => result.passed).length, failed: reports.filter(result => !result.passed).length, screenshots },
  reports, browserErrors, networkRequests };
await writeFile(path.join(out, 'review.json'), JSON.stringify(report, null, 2) + '\n');
console.log(JSON.stringify({ ...report.summary, browserErrors, networkRequests, unchanged, report: path.join(out, 'review.json'), failures: reports.filter(result => !result.passed).map(result => ({ name: result.name, error: result.error.split('\n').slice(0, 3).join('\n') })) }, null, 2));
if (report.summary.failed || browserErrors.length || Object.values(unchanged).some(value => !value)) process.exitCode = 1;
