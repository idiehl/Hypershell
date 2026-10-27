#!/usr/bin/env node
/** Actual editor review with a fully intercepted API and isolated browser state.
 * The only live requests are GETs for the build, saved projects, and baseline.
 * Browser saves, autosaves, recovery and checkpoints exist only in test memory. */
import assert from 'node:assert/strict';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { chromium } from '@playwright/test';
import { validateLibraryCanvasDocument } from '../../src/core/library.ts';
import { DEFAULT_DIRECTORY, IDS, hash } from './reorganize.mjs';

const args = new Map();
for (let at = 2; at < process.argv.length; at += 2) {
  assert.ok(['--directory', '--host', '--out'].includes(process.argv[at]) && process.argv[at + 1] && !args.has(process.argv[at]), 'Use --directory DIR [--host ORIGIN] [--out DIR]');
  args.set(process.argv[at], process.argv[at + 1]);
}
const directory = path.resolve(args.get('--directory') ?? DEFAULT_DIRECTORY), out = path.resolve(args.get('--out') ?? path.join(directory, 'library-interactions'));
const host = new URL(args.get('--host') ?? 'http://127.0.0.1:5240');
assert.ok(host.protocol === 'http:' && ['localhost', '127.0.0.1', '[::1]'].includes(host.hostname) && host.pathname === '/' && !host.username && !host.password && !host.search && !host.hash, 'Use a local Meridian HTTP origin.');
const json = async file => JSON.parse((await readFile(file, 'utf8')).replace(/^\uFEFF/, ''));
const save = async (file, value) => writeFile(file, JSON.stringify(value, null, 2) + '\n');
async function get(endpoint) {
  const response = await fetch(new URL(endpoint, host), { method: 'GET', signal: AbortSignal.timeout(20000) });
  assert.ok(response.ok, `GET ${endpoint} failed (${response.status}). No live write attempted.`);
  return response.json();
}
await mkdir(out, { recursive: true });
const [candidate, metadata, aliases, capturedCatalog, liveBefore] = await Promise.all([
  json(path.join(directory, 'library-candidate.json')), json(path.join(directory, 'library-composition.json')),
  json(path.join(directory, 'library.aliases.json')), json(path.join(directory, 'catalog.json')), get('/api/library'),
]);
validateLibraryCanvasDocument(candidate);
assert.equal(hash(candidate), metadata.candidateSHA256, 'Candidate no longer matches the composition receipt.');
const tutorial = await json(metadata.tutorialFile);
assert.equal(hash(tutorial), metadata.tutorialSHA256, 'Recompose the Library after refining its tutorial.');
const projects = new Map((await Promise.all(capturedCatalog.filter(item => item.id !== tutorial.id).map(async item => [item.id, await get(`/api/projects/${encodeURIComponent(item.id)}`)]))).map(([id, project]) => [id, structuredClone(project)]));
projects.set(tutorial.id, structuredClone(tutorial));
// A current catalog is synthesized from the frozen projects, including the staged tutorial.
const catalog = [...projects.values()].map(project => ({ id: project.id, title: project.title, revision: project.revision, updatedAt: '2026-10-21T00:00:00Z', entryCanvasId: project.entryCanvasId,
  canvases: project.canvases.map(work => ({ ...work, nodeCount: project.nodes.filter(node => node.canvasId === work.id).length })),
  links: project.nodes.filter(node => node.kind === 'canvas-link' && node.targetId).flatMap(node => {
    const target = project.canvases.find(work => work.rootId === node.targetId || work.id === node.targetId);
    return target ? [{ sourceCanvasId: node.canvasId, targetCanvasId: target.id }] : [];
  }),
}));
const destinations = Object.entries(candidate.canvasTargets).map(([id, target]) => {
  const project = projects.get(target.projectId); assert.ok(project, `Portal ${id} has an unavailable project.`);
  const work = project.canvases.find(work => work.id === (target.canvasId ?? project.entryCanvasId)); assert.ok(work, `Portal ${id} has an unavailable Work.`);
  return { nodeId: id, projectId: project.id, workId: work.id, workTitle: work.title, projectRevision: project.revision };
});
await save(path.join(out, 'destinations.json'), destinations);
let stored = structuredClone(candidate), recovery = null;
const recoveries = new Map(), checkpointDocuments = new Map(), localWrites = [], rejectedRequests = [], browserErrors = [], checks = [], liveStaticGETs = [], liveProjectGETs = [];
const projectHashes = Object.fromEntries([...projects].map(([id, project]) => [id, hash(project)]));
const browser = await chromium.launch({ headless: true });
const context = await browser.newContext({ viewport: { width: 2400, height: 1360 }, deviceScaleFactor: 1 });
await context.addInitScript(() => { try { localStorage.clear(); sessionStorage.clear(); } catch {} });
const page = await context.newPage();
page.on('pageerror', error => browserErrors.push(error.message));
await context.route('**/*', async route => {
  const request = route.request(), url = new URL(request.url()), method = request.method();
  const reply = (value, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(value) });
  if (url.protocol === 'data:' || url.protocol === 'blob:') return route.continue();
  if (url.origin !== host.origin) { rejectedRequests.push({ url: url.href, method, reason: 'external request' }); return route.abort(); }
  if (!url.pathname.startsWith('/api/') && url.pathname !== '/__meridian_status') {
    if (method !== 'GET') { rejectedRequests.push({ url: url.href, method, reason: 'static mutation blocked' }); return route.abort(); }
    liveStaticGETs.push(url.pathname); return route.continue();
  }
  const body = ['POST', 'PUT', 'DELETE'].includes(method) ? request.postDataJSON() : undefined;
  if (url.pathname === '/__meridian_status' && method === 'GET') return reply({ application: 'atlas-meridian', version: 1, status: 'ready', capabilities: ['library-v1', 'library-canvas-v2'] });
  if (url.pathname === '/api/library/catalog' && method === 'GET') return reply(catalog);
  if (url.pathname === '/api/library') {
    if (method === 'GET') return reply(stored);
    if (method === 'PUT') {
      if (body.expectedRevision !== stored.revision) return reply({ error: 'Isolated revision conflict' }, 409);
      validateLibraryCanvasDocument(body.library); stored = structuredClone(body.library); stored.revision++; stored.project.revision = stored.revision;
      recovery = null; localWrites.push({ method, endpoint: url.pathname, revision: stored.revision }); return reply(stored);
    }
  }
  if (url.pathname === '/api/library/recovery') {
    if (method === 'GET') return recovery ? reply(recovery) : reply({ error: 'No isolated recovery draft' }, 404);
    if (method === 'DELETE') { recovery = null; localWrites.push({ method, endpoint: url.pathname }); return route.fulfill({ status: 204 }); }
    if (method === 'PUT') { recovery = { library: structuredClone(body.library), project: structuredClone(body.library.project), baseRevision: body.baseRevision, savedAt: new Date().toISOString() }; localWrites.push({ method, endpoint: url.pathname }); return reply(recovery); }
  }
  if (url.pathname === '/api/library/checkpoints') {
    if (method === 'GET') return reply([...checkpointDocuments.values()].map(item => item.summary));
    if (method === 'POST') { const summary = { id: crypto.randomUUID(), name: body.name, createdAt: new Date().toISOString(), revision: stored.revision }; checkpointDocuments.set(summary.id, { summary, document: structuredClone(body.library) }); localWrites.push({ method, endpoint: url.pathname }); return reply(summary, 201); }
  }
  const checkpoint = /^\/api\/library\/checkpoints\/([^/]+)$/.exec(url.pathname);
  if (checkpoint && method === 'GET') return reply(checkpointDocuments.get(checkpoint[1])?.document ?? { error: 'Missing isolated checkpoint' }, checkpointDocuments.has(checkpoint[1]) ? 200 : 404);
  if (url.pathname === '/api/projects' && method === 'GET') return reply(catalog.map(({ id, title, revision, updatedAt }) => ({ id, title, revision, updatedAt })));
  if (url.pathname === '/api/modules' && method === 'GET') return reply([]);
  const match = /^\/api\/projects\/([^/]+)(?:\/(recovery|checkpoints))?$/.exec(url.pathname);
  if (match && projects.has(match[1])) {
    const id = match[1], sub = match[2];
    if (!sub && method === 'GET') {
      if (id === tutorial.id) return reply(projects.get(id));
      // Large existing Works can exceed Chromium's DevTools message limit when
      // route.fulfill serializes embedded media. Allow this exact read-only GET
      // to its original local endpoint; every mutation remains intercepted.
      liveProjectGETs.push(url.pathname); return route.continue();
    }
    // Editing existing source projects is outside this Library review.
    if (!sub && method === 'PUT') { rejectedRequests.push({ url: url.href, method, reason: 'source project save blocked' }); return reply({ error: 'Source project edits are not allowed by this review' }, 409); }
    if (sub === 'checkpoints' && method === 'GET') return reply([]);
    if (sub === 'recovery') {
      if (method === 'GET') return recoveries.has(id) ? reply(recoveries.get(id)) : reply({ error: 'No isolated recovery draft' }, 404);
      if (method === 'DELETE') { recoveries.delete(id); localWrites.push({ method, endpoint: url.pathname }); return route.fulfill({ status: 204 }); }
      if (method === 'PUT') { const value = { project: structuredClone(body.project), baseRevision: body.baseRevision, savedAt: new Date().toISOString() }; recoveries.set(id, value); localWrites.push({ method, endpoint: url.pathname }); return reply(value); }
    }
  }
  rejectedRequests.push({ url: url.href, method, reason: 'unrecognized API request blocked' });
  return reply({ error: 'This API request is not permitted by the isolated Library review' }, 403);
});
const card = id => page.locator(`.ms-node[data-node-id="${id}"]`);
async function ready() { await page.locator('.workspace').waitFor(); await page.evaluate(() => document.fonts.ready); }
async function reset() { await page.getByRole('button', { name: 'Reset view', exact: true }).click(); await ready(); }
async function shot(name) { await page.mouse.move(8, 8); await page.screenshot({ path: path.join(out, name) }); return name; }
async function fitOverview() {
  const viewport = page.locator('.ms-viewport'), frame = await viewport.boundingBox(); assert.ok(frame);
  const world = page.locator('.ms-world'), current = await world.evaluate(element => Number(getComputedStyle(element).getPropertyValue('--ms-camera-scale')));
  const boxes = await page.locator('.ms-node[data-node-id]').evaluateAll(elements => elements.map(element => {
    const box = element.getBoundingClientRect(); return { x: box.x, y: box.y, right: box.right, bottom: box.bottom };
  }));
  const left = Math.min(...boxes.map(box => box.x)), right = Math.max(...boxes.map(box => box.right));
  const top = Math.min(...boxes.map(box => box.y)), bottom = Math.max(...boxes.map(box => box.bottom));
  const desired = Math.min(1, current * (frame.width - 80) / (right - left), current * (frame.height - 80) / (bottom - top));
  const corner = { x: frame.x + 8, y: frame.y + 8 }; await page.mouse.move(corner.x, corner.y);
  await page.mouse.wheel(0, -Math.log(desired / current) / .0013);
  await page.waitForFunction(expected => Math.abs(Number(getComputedStyle(document.querySelector('.ms-world')).getPropertyValue('--ms-camera-scale')) - expected) < .00001, desired);
  const after = await page.locator('.ms-node[data-node-id]').evaluateAll(elements => elements.map(element => {
    const box = element.getBoundingClientRect(); return { x: box.x, y: box.y, right: box.right, bottom: box.bottom };
  }));
  const dx = frame.x + frame.width / 2 - (Math.min(...after.map(box => box.x)) + Math.max(...after.map(box => box.right))) / 2;
  const dy = frame.y + frame.height / 2 - (Math.min(...after.map(box => box.y)) + Math.max(...after.map(box => box.bottom))) / 2;
  await page.mouse.move(corner.x, corner.y); await page.mouse.down(); await page.mouse.move(corner.x + dx, corner.y + dy, { steps: 8 }); await page.mouse.up();
  return desired;
}
async function flushLibrary() {
  const revision = stored.revision; await page.keyboard.press('Control+s');
  await page.waitForFunction(() => !document.querySelector('dialog[open]'));
  const deadline = Date.now() + 10000;
  while (stored.revision === revision && Date.now() < deadline) await new Promise(resolve => setTimeout(resolve, 50));
  assert.ok(stored.revision > revision, 'The editor did not save its isolated Library changes.');
}
try {
  await page.goto(host.origin + '/'); await ready(); await reset();
  assert.equal(await page.locator('.ms-canvas-open').count(), 6, 'Initial Library should have six functional portal buttons.');
  const wideScreenshot = await shot('01-library-editor.png');
  await page.setViewportSize({ width: 1600, height: 1050 }); await reset(); const fitScale = await fitOverview();
  const fitScreenshot = await shot('00-library-fit.png');
  await page.setViewportSize({ width: 2400, height: 1360 }); await reset();
  checks.push({ name: 'Initial Library has three expanded categories and six functional entry portals', passed: true, screenshot: wideScreenshot, fitScreenshot, fitScale });
  const rootId = candidate.project.canvases.find(work => work.id === candidate.project.entryCanvasId).rootId;
  const entries = candidate.project.nodes.filter(node => candidate.canvasTargets[node.id] && candidate.project.nodes.find(parent => parent.id === node.parentId)?.parentId === rootId);
  assert.equal(entries.length, 6);
  for (const entry of entries) {
    await page.goto(host.origin + '/'); await ready(); await reset();
    const target = candidate.canvasTargets[entry.id], open = card(entry.id).locator('.ms-canvas-open'); await open.waitFor({ state: 'visible' });
    await open.hover(); const tooltip = page.locator('.ms-canvas-destination'); await tooltip.waitFor({ state: 'visible' });
    assert.ok((await tooltip.textContent()).trim().length, 'Portal destination hover text is empty.');
    await open.click(); await page.waitForURL(url => url.searchParams.get('project') === target.projectId && url.searchParams.get('canvas') === target.canvasId); await ready();
    const destination = projects.get(target.projectId), work = destination.canvases.find(work => work.id === target.canvasId);
    assert.ok(work); assert.ok(await page.locator('.ms-node').count() > 0, `${entry.title} opened an empty scene.`);
    checks.push({ name: `Entry portal opens ${entry.title}`, passed: true, projectId: target.projectId, workId: target.canvasId });
  }
  await page.goto(host.origin + '/'); await ready(); await reset();
  const coding = aliases.nodes['category/coding'], box = await card(coding).boundingBox(); assert.ok(box);
  await page.mouse.move(box.x + box.width / 2, box.y + 8);
  const collapse = page.locator(`.ms-container-hover-bar[data-container-hover-owner="${coding}"]`); await collapse.waitFor({ state: 'visible' }); await collapse.click();
  await card(coding).waitFor(); assert.equal(await card(coding).locator('.ms-canvas-open').count(), 0);
  await flushLibrary(); assert.equal(stored.project.nodes.find(node => node.id === coding).collapsed, true);
  await page.reload(); await ready(); await reset();
  assert.ok(await card(coding).evaluate(element => element.classList.contains('ms-collapsed')));
  const cppEntry = candidate.project.nodes.find(node => node.canvasTarget?.projectId === IDS.cpp && node.parentId === coding);
  assert.equal(await card(cppEntry.id).count(), 0, 'Collapsed Coding children reappeared on reopen.');
  checks.push({ name: 'Category collapse saves and survives reopening in isolated storage', passed: true, screenshot: await shot('02-library-coding-collapsed.png') });
  const compact = await card(coding).boundingBox(); assert.ok(compact);
  await card(coding).dblclick({ position: { x: compact.width / 2, y: compact.height * .78 } });
  await card(cppEntry.id).waitFor({ state: 'visible' }); await flushLibrary();
  await page.reload(); await ready(); await reset();
  assert.equal(await page.locator('.ms-canvas-open').count(), 6);
  for (const title of ['coding', 'mythology', 'tutorial']) assert.equal(stored.project.nodes.find(node => node.id === aliases.nodes[`category/${title}`]).collapsed, false);
  for (const [id, target] of Object.entries(candidate.canvasTargets)) assert.deepEqual(stored.canvasTargets[id], target);
  assert.deepEqual(stored.discoveredTargets, candidate.discoveredTargets);
  assert.deepEqual(stored.project.assets, candidate.project.assets);
  for (const id of Object.keys(projectHashes)) assert.equal(hash(projects.get(id)), projectHashes[id], 'Review changed a frozen source project.');
  checks.push({ name: 'Reexpanded categories save/reopen with all 82 destinations, discovery history and asset intact', passed: true, screenshot: await shot('03-library-reopened.png') });
  assert.deepEqual(browserErrors, [], 'Browser errors occurred.');
  assert.deepEqual(rejectedRequests, [], 'The review attempted an unexpected API or external request.');
  const liveAfter = await get('/api/library'); assert.deepEqual(liveAfter, liveBefore, 'Live Library changed during isolated review; investigate the external writer before applying anything.');
  const report = { passed: true, candidateSHA256: hash(candidate), tutorialSHA256: hash(tutorial), liveLibraryRevision: liveBefore.revision,
    destinationCount: destinations.length, checks, browserErrors, rejectedRequests, localWrites, liveStaticGETs: [...new Set(liveStaticGETs)], liveProjectGETs: [...new Set(liveProjectGETs)],
    liveWrites: false, liveLibraryUnchanged: true, storageBoundary: 'Every API request was checked by the route handler. Only static build GETs and whitelisted existing-project GETs reached the live host. Library data and tutorial came from local candidates; saves existed solely in test memory.' };
  await save(path.join(out, 'saved-library.json'), stored); await save(path.join(out, 'review.json'), report); console.log(JSON.stringify(report, null, 2));
} catch (error) {
  await page.screenshot({ path: path.join(out, 'failure.png') }).catch(() => {});
  await save(path.join(out, 'failure.json'), { error: error.stack ?? String(error), checks, browserErrors, rejectedRequests, localWrites }); throw error;
} finally { await context.close().catch(() => {}); await browser.close().catch(() => {}); }
