/**
 * Offline, real-viewer interaction QA. Never contacts the Meridian host or writes
 * an authored project. Node 24: --directory OUTPUT/{cpp,csharp} [--out DIRECTORY].
 */
import assert from 'node:assert/strict';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { createHash } from 'node:crypto';
import { chromium } from '@playwright/test';

const args = new Map();
for (let at = 2; at < process.argv.length; at += 2) {
  const key = process.argv[at]?.replace(/^--/, ''), value = process.argv[at + 1];
  assert.ok(['directory', 'out'].includes(key) && value && !value.startsWith('--') && !args.has(key), 'Use --directory DIRECTORY [--out DIRECTORY]'); args.set(key, value);
}
assert.ok(args.has('directory'), 'Supply --directory OUTPUT/{cpp,csharp}');
const directory = path.resolve(args.get('directory')), out = path.resolve(args.get('out') ?? path.join(directory, 'review-interactions'));
const readJson = async file => JSON.parse((await readFile(file, 'utf8')).replace(/^\uFEFF/, ''));
const [project, aliases, html] = await Promise.all([readJson(path.join(directory, 'draft.json')), readJson(path.join(directory, 'aliases.json')), readFile(path.join(directory, 'publication.html'), 'utf8')]);
const dataMatch = html.match(/<script\b[^>]*id=["']meridian-data["'][^>]*>([\s\S]*?)<\/script\s*>/i);
assert.ok(dataMatch, 'Missing publication payload');
const payload = JSON.parse(dataMatch[1]);
assert.equal(payload.project.id, project.id, 'Publication belongs to a different project');
assert.deepEqual(payload.project.nodes.map(node => node.id), project.nodes.map(node => node.id), 'Rebuild the publication after changing its draft');
const projectSha256 = createHash('sha256').update(JSON.stringify(project)).digest('hex');
await mkdir(path.join(out, 'downloads'), { recursive: true });
// Freeze the bytes under review so a concurrent recompose cannot replace them.
const subject = path.join(out, 'subject.html'); await writeFile(subject, html);
const browser = await chromium.launch({ headless: true });
const context = await browser.newContext({ viewport: { width: 1600, height: 1100 }, deviceScaleFactor: 1, acceptDownloads: true });
const page = await context.newPage(), browserErrors = [], checks = [], blockedRequests = [];
context.on('page', child => { if (child !== page) child.on('pageerror', error => browserErrors.push(error.message)); });
page.on('pageerror', error => browserErrors.push(error.message));
await context.route(/^https?:/, route => { blockedRequests.push(route.request().url()); return route.abort(); });
await page.emulateMedia({ reducedMotion: 'reduce' });
const card = id => page.locator(`.ms-node[data-node-id="${id}"]`);
const node = key => { const id = aliases.nodes[key]; assert.ok(id, `Missing alias ${key}`); return project.nodes.find(item => item.id === id); };
const children = id => project.nodes.filter(item => item.parentId === id);
const closeTo = (actual, expected, label, tolerance = .5) => assert.ok(Math.abs(actual - expected) <= tolerance, `${label}: ${actual} differs from ${expected}`);
async function ready() {
  await page.evaluate(async () => { await document.fonts.ready; await Promise.all([...document.images].map(image => image.decode().catch(() => {}))); });
}
async function chooseWork(id) {
  await page.getByLabel('Choose Work', { exact: true }).selectOption(id);
  await ready();
}
async function inspect(id, branch = false) {
  const selected = project.nodes.find(item => item.id === id); assert.ok(selected, `Missing node ${id}`);
  await chooseWork(selected.canvasId);
  const explore = page.getByRole('button', { name: 'Explore project', exact: true });
  if (await explore.getAttribute('aria-expanded') !== 'true') await explore.click();
  await page.getByRole('searchbox', { name: 'Search project', exact: true }).fill(id);
  const result = page.locator(`[data-navigation-node="${id}"]`); await result.waitFor(); await result.click();
  await page.getByRole('button', { name: 'Close project navigation', exact: true }).click();
  if (branch) await page.getByRole('button', { name: 'Fit selected branch', exact: true }).click();
  await ready();
}
async function screenshot(name) {
  await ready(); await page.screenshot({ path: path.join(out, name) }); return name;
}
async function readingScale(id, scale = 1) {
  const viewport = page.locator('.ms-viewport'), bounds = await viewport.boundingBox(); assert.ok(bounds);
  const current = await page.locator('.ms-world').evaluate(element => Number(getComputedStyle(element).getPropertyValue('--ms-camera-scale')));
  // Wheel over a Window scrolls its content. Use the clear fit margin, then pan
  // through the actual viewer gesture to center the selected tile.
  const corner = { x: bounds.x + 12, y: bounds.y + 12 };
  await page.mouse.move(corner.x, corner.y);
  await page.mouse.wheel(0, -Math.log(scale / current) / .0013);
  await page.waitForFunction(expected => {
    const value = Number(getComputedStyle(document.querySelector('.ms-world')).getPropertyValue('--ms-camera-scale'));
    return Math.abs(value - expected) < .00001;
  }, scale);
  const selected = await card(id).boundingBox(); assert.ok(selected);
  const dx = bounds.x + bounds.width / 2 - selected.x - selected.width / 2, dy = bounds.y + bounds.height / 2 - selected.y - selected.height / 2;
  await page.mouse.move(corner.x, corner.y); await page.mouse.down(); await page.mouse.move(corner.x + dx, corner.y + dy, { steps: 8 }); await page.mouse.up();
  const result = await page.locator('.ms-world').evaluate(element => Number(getComputedStyle(element).getPropertyValue('--ms-camera-scale')));
  closeTo(result, scale, 'Focused reading scale', .00001); return result;
}
async function frame(id) {
  return card(id).evaluate(element => ({ x: parseFloat(element.style.left), y: parseFloat(element.style.top), width: parseFloat(element.style.width), height: parseFloat(element.style.height) }));
}
async function downloadAsset(asset) {
  const dialog = page.locator('dialog.ms-asset-dialog'); await dialog.waitFor({ state: 'visible' });
  const dialogScreenshot = await screenshot(`attachment-${asset.name.replace(/[^a-z0-9]+/gi, '-').slice(0, 100)}.png`);
  const event = page.waitForEvent('download');
  await dialog.getByRole('button', { name: `Download ${asset.name}`, exact: true }).click();
  const download = await event, filename = download.suggestedFilename();
  await download.saveAs(path.join(out, 'downloads', filename));
  const bytes = await readFile(path.join(out, 'downloads', filename));
  assert.deepEqual(bytes, Buffer.from(asset.base64, 'base64'), `Downloaded bytes differ: ${asset.name}`);
  await dialog.getByRole('button', { name: 'Close attachment', exact: true }).click();
  return { filename, bytes: bytes.length, sha256: createHash('sha256').update(bytes).digest('hex'), dialogScreenshot };
}
async function visibleWindowItems(parentId) {
  return page.locator(`.ms-window-viewport[data-window-id="${parentId}"]`).evaluate((viewport, ids) => {
    const clip = viewport.getBoundingClientRect();
    return [...viewport.querySelectorAll('.ms-node[data-node-id]')].filter(element => ids.includes(element.dataset.nodeId)).filter(element => {
      const rect = element.getBoundingClientRect(); return rect.bottom > clip.top + 2 && rect.top < clip.bottom - 2 && rect.right > clip.left + 2 && rect.left < clip.right - 2;
    }).map(element => element.dataset.nodeId);
  }, children(parentId).map(item => item.id));
}

let failure;
try {
  await page.goto(pathToFileURL(subject).href); await ready();
  const authoredJSON = await page.locator('#meridian-data').textContent();

  // Follow a real launcher, including native history back to the collection.
  await chooseWork(aliases.canvases.overview);
  const portal = node('overview/pattern-horizontal'); await inspect(portal.id);
  const launcher = card(portal.id).locator('button.ms-canvas-open'); await launcher.hover();
  await launcher.click(); assert.equal(await page.getByLabel('Choose Work', { exact: true }).inputValue(), aliases.canvases.horizontal);
  await page.getByRole('button', { name: 'Back', exact: true }).click();
  assert.equal(await page.getByLabel('Choose Work', { exact: true }).inputValue(), aliases.canvases.overview);
  checks.push({ kind: 'portal-navigation', nodeId: portal.id, targetWorkId: aliases.canvases.horizontal, historyBackPassed: true });

  // Bounded radial panels keep their identity/frame and contain original notes.
  const family = node('radial/family-1'), firstChild = children(family.id)[0];
  await inspect(family.id); assert.equal(await card(family.id).getAttribute('aria-expanded'), 'false');
  const closed = await frame(family.id); assert.equal(await card(firstChild.id).count(), 0);
  await card(family.id).focus(); await card(family.id).press('Alt+ArrowRight');
  await card(family.id).filter({ has: page.locator('.ms-window-viewport') }).waitFor();
  assert.equal(await card(family.id).getAttribute('aria-expanded'), 'true'); assert.equal(await card(firstChild.id).count(), 1);
  const open = await frame(family.id);
  await inspect(family.id, true); await card(family.id).hover();
  const collapse = page.locator(`button.ms-container-hover-bar[data-container-hover-owner="${family.id}"]`);
  await collapse.waitFor({ state: 'visible' }); await collapse.click();
  assert.equal(await card(family.id).getAttribute('aria-expanded'), 'false'); assert.equal(await card(firstChild.id).count(), 0);
  const closedAgain = await frame(family.id);
  await card(family.id).focus(); await card(family.id).press('Alt+ArrowRight');
  assert.equal(await card(family.id).getAttribute('aria-expanded'), 'true');
  const reopened = await frame(family.id); assert.deepEqual(closedAgain, closed); assert.deepEqual(reopened, open);
  assert.equal(await card(firstChild.id).count(), 1);
  checks.push({ kind: 'disclosure', nodeId: family.id, originalChildId: firstChild.id, collapsedFrame: closed, expandedFrame: open, preservedFrames: true, hoverCollapsePassed: true, screenshot: await screenshot('radial-disclosed.png') });

  // Pointer double-click opens the ordinary topic cover's external Window.
  const stage = node('horizontal/stage-1'); await inspect(stage.id);
  const cover = await card(stage.id).boundingBox(); assert.ok(cover);
  await page.mouse.dblclick(cover.x + cover.width - 8, cover.y + cover.height - 8);
  assert.equal(await card(stage.id).getAttribute('aria-expanded'), 'true');
  const stageWindow = page.locator(`.ms-window-viewport[data-window-id="${stage.id}"]`); await stageWindow.waitFor({ state: 'visible' });
  await card(stage.id).focus(); await card(stage.id).press('Alt+ArrowLeft');
  assert.equal(await card(stage.id).getAttribute('aria-expanded'), 'false'); assert.equal(await stageWindow.count(), 0);
  checks.push({ kind: 'pointer-disclosure', nodeId: stage.id, pointerDoubleClickPassed: true, keyboardCollapsePassed: true });

  // Full catalogue browsing: Gallery pagination where authored, otherwise the
  // bounded scrolling catalogue desk. Do not invent a Gallery merely for QA.
  const galleryParent = node('sources/catalog-gallery'), galleryItems = children(galleryParent.id);
  const pages = [], galleryFrames = [];
  if (galleryParent.containerVariant === 'gallery') {
    await inspect(galleryParent.id);
    const gallery = page.locator(`[data-gallery-id="${galleryParent.id}"]`);
    for (let at = 0; at < galleryItems.length; at++) {
    const item = galleryItems[at], asset = project.assets.find(value => value.id === item.assetId); assert.ok(asset);
    const pageObject = gallery.locator('.ms-gallery-item'); assert.equal(await pageObject.getAttribute('data-gallery-item-id'), item.id);
    const size = await gallery.boundingBox(), objectSize = await pageObject.boundingBox(), navigationSize = await gallery.locator('.ms-gallery-navigation').boundingBox();
    assert.ok(size && objectSize && navigationSize);
    galleryFrames.push({ width: size.width, height: size.height });
    closeTo(objectSize.width, navigationSize.width, 'Gallery page/navigation width');
    if (at > 0) { closeTo(size.width, galleryFrames[0].width, 'Gallery width'); closeTo(size.height, galleryFrames[0].height, 'Gallery height'); }
    await pageObject.getByRole('button', { name: `Open ${asset.name}`, exact: true }).click();
    const downloaded = await downloadAsset(asset);
    pages.push({ nodeId: item.id, assetId: asset.id, title: item.title, ...downloaded });
    if (at + 1 < galleryItems.length) await gallery.locator('[data-gallery-control="next"]').click();
    }
    assert.equal(await gallery.locator('[data-gallery-control="next"]').isDisabled(), true);
    for (let at = galleryItems.length - 1; at > 0; at--) await gallery.locator('[data-gallery-control="previous"]').click();
    assert.equal(await gallery.locator('.ms-gallery-item').getAttribute('data-gallery-item-id'), galleryItems[0].id);
    assert.equal(await gallery.locator('[data-gallery-control="previous"]').isDisabled(), true);
    checks.push({ kind: 'gallery', parentId: galleryParent.id, pageCount: pages.length, nextPreviousPassed: true, constantFrame: galleryFrames, completeOriginalDownloads: pages, screenshot: await screenshot('catalog-gallery.png') });
  } else {
    assert.equal(galleryParent.containerVariant, 'window', 'Catalogs need an authored Gallery or Window');
    for (const item of galleryItems) {
      const asset = project.assets.find(value => value.id === item.assetId); assert.ok(asset);
      await inspect(item.id);
      assert.ok(await card(item.id).locator('.ms-csv-preview table').count(), `CSV table is missing: ${item.title}`);
      await card(item.id).getByRole('button', { name: `Open ${asset.name}`, exact: true }).click();
      const downloaded = await downloadAsset(asset);
      pages.push({ nodeId: item.id, assetId: asset.id, title: item.title, ...downloaded });
    }
    checks.push({ kind: 'catalog-window-downloads', parentId: galleryParent.id, completeOriginalDownloads: pages, screenshot: await screenshot('catalog-window.png') });
  }

  // Wheel over a card header scrolls the Window, rather than zooming the Work.
  for (const key of ['library/desk', 'api/desk', 'sources/reference-desk', ...(galleryParent.containerVariant === 'window' ? ['sources/catalog-gallery'] : [])]) {
    const parent = node(key); await inspect(parent.id);
    const region = page.locator(`.ms-window-viewport[data-window-id="${parent.id}"]`); await region.waitFor({ state: 'visible' });
    await region.focus(); await region.press('Home');
    const before = await visibleWindowItems(parent.id), regionBox = await region.boundingBox(); assert.ok(regionBox);
    const originalCamera = await page.locator('.ms-world').evaluate(element => getComputedStyle(element).getPropertyValue('--ms-camera-scale'));
    await page.mouse.move(regionBox.x + 6, regionBox.y + 6); await page.mouse.wheel(0, 700);
    await page.waitForFunction(id => document.querySelector(`.ms-window-viewport[data-window-id="${id}"]`)?.scrollTop > 0, parent.id);
    const after = await visibleWindowItems(parent.id), wheelTop = await region.evaluate(element => element.scrollTop);
    assert.ok(after.some(id => !before.includes(id)), `Wheel did not reveal later entries in ${key}`);
    assert.equal(await page.locator('.ms-world').evaluate(element => getComputedStyle(element).getPropertyValue('--ms-camera-scale')), originalCamera, `Window wheel changed Work zoom in ${key}`);
    await region.focus(); await region.press('End');
    const end = await visibleWindowItems(parent.id), finalChild = children(parent.id).at(-1); assert.ok(end.includes(finalChild.id), `End did not reach the final entry in ${key}`);
    checks.push({ kind: 'window-scroll', key, parentId: parent.id, initialVisibleIds: before, wheelVisibleIds: after, wheelScrollTop: wheelTop, finalVisibleIds: end, finalEntryId: finalChild.id, unchangedWorkZoom: true, screenshot: await screenshot(`window-${key.replaceAll('/', '-')}-end.png`) });
    await region.press('Home');
  }

  // A linked source remains the exact documented external destination.
  const reference = node('sources/primary-1'); await inspect(reference.id);
  const href = await card(reference.id).locator('a.ms-content-link').getAttribute('href');
  assert.equal(href, reference.hyperlinks[0].url);
  checks.push({ kind: 'primary-source-link', nodeId: reference.id, href, target: await card(reference.id).locator('a.ms-content-link').getAttribute('target') });

  // Download the complete reading packet and the portable publication itself.
  const packet = node('sources/reading-packet'), packetAsset = project.assets.find(item => item.id === packet.assetId); await inspect(packet.id);
  await card(packet.id).getByRole('button', { name: `Open ${packetAsset.name}`, exact: true }).click();
  const packetDownload = await downloadAsset(packetAsset); checks.push({ kind: 'reading-packet-download', ...packetDownload });
  const publicationEvent = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Download this published Work for viewing or importing into the Meridian editor', exact: true }).click();
  const publicationDownload = await publicationEvent, copyPath = path.join(out, 'downloads', publicationDownload.suggestedFilename()); await publicationDownload.saveAs(copyPath);
  const copiedHtml = await readFile(copyPath, 'utf8'), copiedPayload = JSON.parse(copiedHtml.match(/<script\b[^>]*id=["']meridian-data["'][^>]*>([\s\S]*?)<\/script\s*>/i)[1]);
  assert.deepEqual(copiedPayload.project, payload.project, 'Publication download changed authored graph/content');
  checks.push({ kind: 'portable-copy-download', filename: path.basename(copyPath), bytes: Buffer.byteLength(copiedHtml), originalGraphPreserved: true });
  // Exercise the complete disclosed composition through real viewer controls.
  // Independently opening one branch cannot expose interactions between trays.
  for (const [work, prefix, count] of [['horizontal', 'stage', 6], ['vertical', 'layer', 6], ['radial', 'family', 8]]) {
    await chooseWork(aliases.canvases[work]);
    await page.getByRole('button', { name: 'Reset to authored state', exact: true }).click();
    const openedIds = [];
    for (let at = 1; at <= count; at++) {
      const target = node(`${work}/${prefix}-${at}`);
      await card(target.id).focus(); await card(target.id).press('Alt+ArrowRight');
      assert.equal(await card(target.id).getAttribute('aria-expanded'), 'true');
      openedIds.push(target.id);
    }
    await page.getByRole('button', { name: 'Fit Work', exact: true }).click();
    const frames = await Promise.all(openedIds.map(async id => ({ nodeId: id, ...await frame(id) })));
    checks.push({ kind: 'all-groups-expanded', work, expandedGroups: count, frames,
      trayFrames: await page.locator('.ms-expansion-tray[data-tray-source-id]').evaluateAll(elements => elements.filter(element => !element.hidden).map(element => ({ sourceId: element.dataset.traySourceId, x: element.style.left, y: element.style.top, width: element.style.width, height: element.style.height }))),
      screenshot: await screenshot(`all-open-${work}.png`) });
  }

  // Useful reading-scale evidence, independent of an enormous overview's fit.
  for (const key of ['radial/family-1', 'vertical/layer-4/note-1', 'horizontal/stage-1/note-1']) {
    const target = node(key); await inspect(target.id, key === 'radial/family-1');
    if (key === 'radial/family-1' && await card(target.id).getAttribute('aria-expanded') === 'false') {
      await card(target.id).focus(); await card(target.id).press('Alt+ArrowRight');
    }
    const actualScale = await readingScale(target.id, 1);
    const metrics = await card(target.id).evaluate(element => ({
      title: element.querySelector(':scope > .ms-node-header .ms-node-title')?.textContent,
      titleFont: getComputedStyle(element.querySelector(':scope > .ms-node-header .ms-node-title') ?? element).fontSize,
      body: element.querySelector(':scope > .ms-node-body .ms-text')?.textContent,
      bodyFont: getComputedStyle(element.querySelector(':scope > .ms-node-body .ms-text') ?? element).fontSize,
      worldFrame: { x: element.style.left, y: element.style.top, width: element.style.width, height: element.style.height },
    }));
    checks.push({ kind: 'focused-reading', key, nodeId: target.id, requestedScale: 1, actualScale, ...metrics, screenshot: await screenshot(`reading-${key.replaceAll('/', '-')}.png`) });
  }
  assert.equal(await page.locator('#meridian-data').textContent(), authoredJSON, 'Browsing mutated the inert authored payload');
  assert.deepEqual(browserErrors, []); assert.deepEqual(blockedRequests, [], 'Offline interaction unexpectedly requested network access');
} catch (error) {
  failure = { message: String(error.message ?? error), stack: error.stack }; await screenshot('failure.png').catch(() => {});
} finally {
  await writeFile(path.join(out, 'proof.json'), JSON.stringify({ projectId: project.id, projectSha256, browser: 'Chromium', reviewedPublication: 'subject.html', passed: !failure, authoredJSONUnchanged: !failure, browserErrors, blockedRequests, checks, ...(failure ? { failure } : {}) }, null, 2) + '\n');
  await browser.close();
}
console.log(JSON.stringify({ directory: out, passed: !failure, checks: checks.length, browserErrors, ...(failure ? { failure: failure.message } : {}) }));
if (failure) process.exitCode = 1;
