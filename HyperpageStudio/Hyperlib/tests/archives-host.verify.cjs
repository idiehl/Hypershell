// Run against an isolated, running HyperLib Host. Never targets a live library or Archives service.
// NODE_PATH may point at the bundled Playwright package; HYPERLIB_TEST_BROWSER overrides the browser executable.
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const { createHash } = require('node:crypto');
const { chromium } = require('playwright');

async function main() {
  const [originText, fixturePath, outputPath] = process.argv.slice(2);
  if (!originText || !fixturePath || !outputPath) throw new Error('Pass loopback origin, private library fixture, and evidence directory.');
  const origin = new URL(originText);
  assert.equal(origin.hostname, '127.0.0.1'); assert.equal(origin.protocol, 'http:');
  const root = path.resolve(fixturePath), output = path.resolve(outputPath);
  assert.match(root, /[\\/]output[\\/].+[\\/]library-[a-f0-9]+$/i, 'Only an explicitly isolated library fixture may be mutated.');
  await fs.mkdir(output, { recursive: true });
  let checks = 0;
  const check = (condition, description) => { assert.ok(condition, description); checks++; };
  const hash = (bytes) => createHash('sha256').update(bytes).digest('hex');
  async function request(route, method = 'GET', body, options = {}) {
    const headers = {};
    if (method !== 'GET' && options.editor !== false) headers['X-HyperLib-Client'] = 'editor';
    if (body !== undefined) headers['Content-Type'] = 'application/json';
    if (options.origin) headers.Origin = options.origin;
    const response = await fetch(new URL(route, origin), { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
    const payload = await response.json();
    if (options.status !== undefined) check(response.status === options.status, `${method} ${route}: expected ${options.status}, got ${response.status}: ${JSON.stringify(payload)}`);
    else check(response.ok, `${method} ${route}: ${response.status} ${JSON.stringify(payload)}`);
    check(!response.headers.has('Access-Control-Allow-Origin'), 'Host does not expose permissive CORS');
    return payload;
  }
  const health = await request('/api/health');
  assert.equal(path.resolve(health.root), root, 'Host must use this private fixture.');
  const contentFile = path.join(root, 'Content/library.json');
  const sourceFile = path.join(root, 'Works/animal-atlas/src/Pages/wwwroot/blocks/counter.js');
  const before = hash(await fs.readFile(contentFile));
  const packages = path.join(root, '.hyperlib/archives/packages');
  const packageNames = async () => (await fs.readdir(packages).catch(() => [])).sort();
  const originalPackages = await packageNames();
  const state = await request('/api/state');
  const branch = 'natural-world';
  const create = { rootNodeId: branch, expectedContentRevision: state.revision };
  await request('/api/archives/drafts', 'POST', create, { editor: false, status: 403 });
  await request('/api/archives/drafts', 'POST', create, { origin: 'https://foreign.invalid', status: 403 });
  await request('/api/archives/drafts', 'POST', { ...create, expectedContentRevision: state.revision - 1 }, { status: 409 });
  let draft = await request('/api/archives/drafts', 'POST', create, { origin: origin.origin });
  check(draft.selectedNodeIds.length === draft.nodes.length, 'New review selects its entire branch');
  const id = draft.id;
  const route = `/api/archives/drafts/${id}`;
  const resumed = await request(`/api/archives/drafts?rootNodeId=${branch}`);
  check(resumed.id === id && resumed.reviewHash === draft.reviewHash, 'Latest review resumes the saved identity and hash');
  await request(route, 'PUT', { expectedDraftRevision: draft.draftRevision - 1, selectedNodeIds: [] }, { status: 409 });
  await request(route, 'PUT', { expectedDraftRevision: draft.draftRevision, selectedNodeIds: ['exploring-wave'] }, { status: 400 });
  draft = await request(route, 'PUT', { expectedDraftRevision: draft.draftRevision, selectedNodeIds: ['woodland-habitat'] });
  check(draft.selectedNodeIds.includes(branch) && draft.selectedNodeIds.includes('animal-atlas') && !draft.selectedNodeIds.includes('private-page'), 'Leaf choice includes required ancestors and excludes sibling');
  check(hash(await fs.readFile(contentFile)) === before, 'Draft preparation and selection do not modify canonical content');
  const originalSource = await fs.readFile(sourceFile);
  try {
    await fs.appendFile(sourceFile, '\n// HTTP review source-change verification\n');
    draft = await request(route);
    check(draft.stale, 'Source changes mark the persisted review stale');
    draft = await request(route, 'PUT', { expectedDraftRevision: draft.draftRevision, selectedNodeIds: draft.selectedNodeIds });
    check(draft.stale, 'Selections remain editable while stale without publishing');
    await request(`${route}/refresh`, 'POST', { expectedDraftRevision: draft.draftRevision - 1, expectedContentRevision: state.revision }, { status: 409 });
    draft = await request(`${route}/refresh`, 'POST', { expectedDraftRevision: draft.draftRevision, expectedContentRevision: state.revision });
    check(!draft.stale && !draft.selectedNodeIds.includes('private-page'), 'Explicit refresh preserves exclusions and rebases source');
  } finally { await fs.writeFile(sourceFile, originalSource); }
  draft = await request(`${route}/refresh`, 'POST', { expectedDraftRevision: draft.draftRevision, expectedContentRevision: state.revision });
  const pageId = `http-review-${Date.now()}`;
  const freshBranchId = `first-review-${Date.now()}`;
  const updated = await request('/api/operations', 'POST', { expectedRevision: state.revision, description: 'Private Archives HTTP verifier', operations: [
    { kind: 'createNode', id: pageId, nodeType: 'page', parentId: 'animal-atlas', title: 'Unpublished field observation' },
    { kind: 'createNode', id: freshBranchId, nodeType: 'collection', parentId: state.libraryId, title: 'First publishing review' },
  ] });
  await request(`/api/archives/drafts?rootNodeId=${freshBranchId}`, 'GET', undefined, { status: 404 });
  draft = await request(route);
  check(draft.stale, 'New local Page invalidates previous review content revision');
  draft = await request(`${route}/refresh`, 'POST', { expectedDraftRevision: draft.draftRevision, expectedContentRevision: updated.revision });
  check(draft.nodes.some(item => item.id === pageId) && !draft.selectedNodeIds.includes(pageId), 'Refresh exposes new Pages unchecked');
  const pagesBeforeBrowser = hash(await fs.readFile(contentFile));
  const browser = await chromium.launch({ headless: true, executablePath: process.env.HYPERLIB_TEST_BROWSER || 'C:/Program Files/Google/Chrome/Application/chrome.exe' });
  const errors = [], screenshots = [];
  try {
    const page = await browser.newPage({ viewport: { width: 1440, height: 1050 }, reducedMotion: 'reduce' });
    page.on('pageerror', error => errors.push(error.message));
    // Avoid unrelated renderer builds; review DOM and all Archives HTTP requests are production code.
    await page.route('**/api/runtime/*', route => route.request().method() === 'GET'
      ? route.fulfill({ status: 503, contentType: 'application/json', body: JSON.stringify({ error: 'Renderer omitted in publishing review fixture.' }) })
      : route.continue());
    await page.addInitScript(() => {
      const listeners = [];
      window.archiveQaTraffic = [];
      window.chrome = { webview: {
        addEventListener(name, callback) { if (name === 'message') listeners.push(callback); },
        postMessage(message) {
          window.archiveQaTraffic.push(message);
          const result = message.method === 'capabilities' ? { aiAvailable: false }
            : message.method === 'archives.status' ? { canSync: true, targetTitle: 'Atlas Archives · QA connection', message: 'Ready for mock sync' }
            : { receipt: { url: 'https://archives.example/qa-field-notes', message: 'Mock sync completed. No content was uploaded.' } };
          queueMicrotask(() => listeners.forEach(callback => callback({ data: { channel: 'hyperlib', id: message.id, result } })));
        },
      } };
    });
    await page.goto(`${origin.origin}/#${freshBranchId}`);
    await page.locator('#edit-toggle').click();
    await page.locator('#archives-button').click();
    await page.locator('#archives-dialog').waitFor({ state: 'visible' });
    await page.waitForFunction(() => document.querySelector('#archives-save-status').textContent === 'Selection saved locally');
    check(await page.locator('#archives-error').textContent() === '', 'First-ever publishing review opens without a malformed empty response');
    check(await page.locator(`[data-archive-node="${freshBranchId}"]`).isChecked(), 'First-ever review starts with the chosen branch checked');
    const firstReview = await request(`/api/archives/drafts?rootNodeId=${freshBranchId}`);
    check(firstReview.rootNodeId === freshBranchId && firstReview.draftRevision === 1, 'Browser created and persisted the first local review after missing-draft response');
    check(await page.evaluate(() => !window.archiveQaTraffic.some(call => call.method === 'archives.sync')), 'Preparing the first-ever review does not publish');
    await page.locator('#archives-done').click();
    await page.goto(`${origin.origin}/?archives-review-qa=resume#${branch}`);
    await page.locator('#edit-toggle').click();
    await page.locator('#archives-button').click();
    const dialog = page.locator('#archives-dialog');
    await dialog.waitFor({ state: 'visible' });
    await page.waitForFunction(() => document.querySelector('#archives-save-status').textContent === 'Selection saved locally');
    check(await page.locator('[data-archive-node="natural-world"]').evaluate(input => input.indeterminate), 'Collection parent renders a native mixed checkbox');
    check(await page.locator('[data-archive-node="animal-atlas"]').evaluate(input => input.indeterminate), 'Work parent renders a native mixed checkbox');
    check(await page.locator('[data-archive-node="woodland-habitat"]').isChecked(), 'Selected Page is checked in actual browser');
    check(!await page.locator(`[data-archive-node="${pageId}"]`).isChecked(), 'New local Page starts unchecked in rendered review');
    check(await page.locator('#archives-sync').isEnabled(), 'Reviewed selection with a connected target enables final sync');
    check(await page.evaluate(() => !window.archiveQaTraffic.some(call => call.method === 'archives.sync')), 'Opening review never invokes remote sync');
    const wide = path.join(output, 'archives-review-wide.png'); await page.screenshot({ path: wide }); screenshots.push(wide);
    await page.locator('[data-archive-node="natural-world"]').check();
    await page.waitForFunction(() => document.querySelector('#archives-save-status').textContent === 'Selection saved locally');
    check(await page.locator('[data-archive-node]:checked').count() === draft.nodes.length, 'Clicking a mixed parent includes every descendant');
    await page.locator('[data-archive-node="natural-world"]').uncheck();
    await page.waitForFunction(() => document.querySelector('#archives-save-status').textContent === 'Selection saved locally');
    check(await page.locator('#archives-sync').isDisabled(), 'Unchecking parent clears descendants and disables empty sync');
    check(await page.locator('[data-archive-node]:checked').count() === 0, 'Parent exclusion cascades to every descendant');
    const pageChoice = page.locator('[data-archive-node="woodland-habitat"]');
    await pageChoice.focus(); await pageChoice.press('Space');
    await page.waitForFunction(() => document.querySelector('#archives-save-status').textContent === 'Selection saved locally');
    check(await page.evaluate(() => document.activeElement?.dataset.archiveNode === 'woodland-habitat'), 'Checkbox focus survives asynchronous autosave rerender');
    await page.locator('#archives-done').click();
    await page.locator('#archives-button').click();
    await page.waitForFunction(() => document.querySelector('#archives-save-status').textContent === 'Selection saved locally');
    check(await pageChoice.isChecked() && !await page.locator('[data-archive-node="private-page"]').isChecked(), 'Close and reopen resumes persisted Page choices');
    await fs.appendFile(sourceFile, '\n// Browser review source-change verification\n');
    await page.locator('#archives-done').click(); await page.locator('#archives-button').click();
    await page.waitForFunction(() => document.querySelector('#archives-save-status').textContent === 'Selection saved locally');
    check(await page.locator('#archives-stale').isVisible() && await page.locator('#archives-sync').isDisabled(), 'Source change appears as stale and blocks final sync');
    await page.locator('#archives-refresh').click();
    await page.waitForFunction(() => !document.querySelector('#archives-sync').disabled);
    check(await page.locator('#archives-stale').isHidden() && !await page.locator(`[data-archive-node="${pageId}"]`).isChecked(), 'Explicit refresh keeps new and excluded Pages unchecked');
    await page.setViewportSize({ width: 900, height: 720 });
    const compact = path.join(output, 'archives-review-compact.png'); await page.screenshot({ path: compact }); screenshots.push(compact);
    for (const selector of ['#archives-refresh', '#archives-done', '#archives-sync']) {
      const bounds = await page.locator(selector).boundingBox();
      check(bounds && bounds.x >= 0 && bounds.y >= 0 && bounds.x + bounds.width <= 900 && bounds.y + bounds.height <= 720, `${selector} remains on screen at compact size`);
    }
    await page.locator('#archives-sync').click();
    await page.locator('#archives-receipt').waitFor({ state: 'visible' });
    check((await page.locator('#archives-receipt').textContent()).includes('No content was uploaded'), 'Mock native receipt appears in review');
    const receiptBounds = await page.locator('#archives-receipt').boundingBox();
    check(receiptBounds && receiptBounds.y > 0 && receiptBounds.y + receiptBounds.height < 620, 'Receipt is visible above the compact footer without scrolling');
    const syncCalls = await page.evaluate(() => window.archiveQaTraffic.filter(call => call.method === 'archives.sync'));
    check(syncCalls.length === 1 && syncCalls[0].params.draftId === id && syncCalls[0].params.reviewHash.length === 64, 'Only explicit final action sends the exact reviewed identity to mock bridge');
    const receipt = path.join(output, 'archives-review-receipt.png'); await page.screenshot({ path: receipt }); screenshots.push(receipt);
    check(errors.length === 0, `Browser has no uncaught errors: ${errors.join('; ')}`);
  } finally {
    await browser.close();
    await fs.writeFile(sourceFile, originalSource);
  }
  check(hash(await fs.readFile(contentFile)) === pagesBeforeBrowser, 'Browser review and mock sync preserve all local Pages');
  check(JSON.stringify(await packageNames()) === JSON.stringify(originalPackages), 'Draft review endpoints never export or upload a package');
  const result = { status: 'HYPERLIB_ARCHIVES_HTTP_UI_OK', checks, fixtureRoot: root, origin: origin.origin, screenshots, errors, remotePublication: false };
  await fs.writeFile(path.join(output, 'archives-http-ui-verification.json'), JSON.stringify(result, null, 2));
  console.log(JSON.stringify(result, null, 2));
}
main().catch(error => { console.error(error); process.exitCode = 1; });
