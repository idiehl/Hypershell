/**
 * Installed-editor QA against a disposable tutorial copy on the guarded 5293
 * sandbox. No requests are allowed to the user's 5240 host. Does not start,
 * stop, package, or replace an application. Node 24 --experimental-strip-types.
 */
import assert from 'node:assert/strict';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { execFileSync } from 'node:child_process';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { createHash } from 'node:crypto';
import { chromium, expect } from '@playwright/test';
import { forkProject, commitSnapshots } from '../../src/core/model.ts';

const ROOT = fileURLToPath(new URL('../../', import.meta.url));
const CYCLE = path.join(ROOT, 'output/library-tutorial-20261021');
const args = new Map();
for (let at = 2; at < process.argv.length; at += 2) {
  const key = process.argv[at]?.replace(/^--/, ''), value = process.argv[at + 1];
  assert.ok(['directory', 'origin', 'out'].includes(key) && value && !value.startsWith('--') && !args.has(key), 'Use --directory DIRECTORY [--origin http://127.0.0.1:5293] [--out DIRECTORY]');
  args.set(key, value);
}
const directory = path.resolve(args.get('directory') ?? path.join(CYCLE, 'tutorial'));
const origin = args.get('origin') ?? 'http://127.0.0.1:5293';
assert.equal(origin, 'http://127.0.0.1:5293', 'This verifier may write only to the isolated 5293 host.');
const out = path.resolve(args.get('out') ?? path.join(CYCLE, 'editor-review'));
assert.ok(out.toLowerCase().startsWith(CYCLE.toLowerCase() + path.sep), 'Keep QA output inside this cycle directory.');
const readJson = async file => JSON.parse((await readFile(file, 'utf8')).replace(/^\uFEFF/, ''));
const sha256 = value => createHash('sha256').update(typeof value === 'string' || Buffer.isBuffer(value) ? value : JSON.stringify(value)).digest('hex');
const receipt = await readJson(path.join(CYCLE, 'isolated-host.json'));
assert.equal(receipt.origin, origin);
assert.equal(path.resolve(receipt.dataRoot).toLowerCase(), path.join(CYCLE, 'isolated-data').toLowerCase());
assert.equal(path.resolve(receipt.executable).toLowerCase(), path.join(ROOT, 'output/app/Meridian.Host.exe').toLowerCase());
assert.ok(Number.isSafeInteger(receipt.processId) && receipt.processId > 0);
const hostProcess = JSON.parse(execFileSync('powershell.exe', ['-NoLogo', '-NoProfile', '-NonInteractive', '-Command', `Get-CimInstance Win32_Process -Filter 'ProcessId = ${receipt.processId}' | Select-Object ProcessId,ExecutablePath,CommandLine | ConvertTo-Json -Compress`], { encoding: 'utf8' }).trim());
assert.equal(path.resolve(hostProcess.ExecutablePath).toLowerCase(), path.resolve(receipt.executable).toLowerCase());
assert.ok(hostProcess.CommandLine.includes(origin) && hostProcess.CommandLine.toLowerCase().includes(receipt.dataRoot.toLowerCase()), 'Host command line must select the fresh isolated data directory.');
const jsonRequest = async (pathname, options) => {
  assert.ok(pathname.startsWith('/api/') || pathname === '/__meridian_status');
  const response = await fetch(origin + pathname, options);
  const text = await response.text(); assert.ok(response.ok, `${options?.method ?? 'GET'} ${pathname}: ${response.status} ${text.slice(0, 300)}`);
  return JSON.parse(text);
};
const createdReceipt = path.join(CYCLE, 'editor-review-sandbox-projects.json');
let sandbox = { projectIds: [] };
try { sandbox = await readJson(createdReceipt); } catch (error) { if (error.code !== 'ENOENT') throw error; }
const beforeCatalog = await jsonRequest('/api/projects');
assert.ok(beforeCatalog.every(item => sandbox.projectIds.includes(item.id)), 'The sandbox contains an unrecognized project; do not write to it.');
await mkdir(out, { recursive: true });
const [draft, aliases] = await Promise.all([readJson(path.join(directory, 'draft.json')), readJson(path.join(directory, 'aliases.json'))]);
const frozenDraftSha256 = sha256(draft);
const [guide, labs, coverage] = await Promise.all([
  readJson(path.join(ROOT, 'content/meridian-tutorial/guide.json')),
  readJson(path.join(ROOT, 'content/meridian-tutorial/labs.json')),
  readJson(path.join(directory, 'coverage.json')),
]);
const expectedFeatureIds = [...guide.features.map(feature => feature.id), ...labs.featureIds];
assert.equal(new Set(expectedFeatureIds).size, expectedFeatureIds.length, 'Feature registry has duplicate IDs.');
assert.deepEqual([...coverage.features.map(feature => feature.id)].sort(), [...expectedFeatureIds].sort(), 'Composed feature registry differs from the maintained guide/lab union.');
const referencedFeatureIds = [...new Set(coverage.notes.flatMap(note => note.featureIds))].sort();
assert.deepEqual(referencedFeatureIds, [...expectedFeatureIds].sort(), 'A feature is unreferenced or a lesson references an unknown feature.');
for (const [id, availability] of [['cluster-staged', 'staged-not-installed'], ['cluster.disclosure-pending', 'unavailable'], ['png-jpg-deferred', 'not-implemented'], ['online-deferred', 'deferred']]) {
  assert.equal(coverage.features.find(feature => feature.id === id)?.availability, availability, `Unavailable ${id} is presented with the wrong availability.`);
}
assert.ok(!draft.nodes.some(node => node.cluster), 'The installed build cannot demonstrate staged decorative Cluster metadata.');
const coverageAudit = {
  passed: true, guideFeatures: guide.features.length, labFeatures: labs.featureIds.length,
  registeredFeatures: coverage.features.length, distinctLessonReferences: referencedFeatureIds.length,
  lessons: coverage.lessonCount, unknownReferences: [], uncoveredFeatures: [], duplicateIds: [],
  unavailable: coverage.features.filter(feature => /staged|deferred|not-implemented|retired|legacy|unavailable/.test(feature.availability)),
  mappings: coverage.features.map(feature => ({ id: feature.id, label: feature.label, availability: feature.availability,
    lessons: coverage.notes.filter(note => note.featureIds.includes(feature.id)).map(note => ({ work: note.work, key: note.key, title: note.title })) })),
};
await writeFile(path.join(out, 'coverage-audit.json'), JSON.stringify(coverageAudit, null, 2) + '\n');
const candidate = commitSnapshots(forkProject(draft));
candidate.title += ' · isolated editor QA';
const nodeMap = new Map(draft.nodes.map((node, index) => [node.id, candidate.nodes[index].id]));
const workMap = new Map(draft.canvases.map((work, index) => [work.id, candidate.canvases[index].id]));
const nodes = Object.fromEntries(Object.entries(aliases.nodes).map(([key, id]) => [key, nodeMap.get(id)]));
const works = Object.fromEntries(Object.entries(aliases.canvases).map(([key, id]) => [key, workMap.get(id)]));
const created = await jsonRequest('/api/projects', { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(candidate) });
sandbox.projectIds.push(created.id); await writeFile(createdReceipt, JSON.stringify(sandbox, null, 2) + '\n');
const readSaved = () => jsonRequest(`/api/projects/${created.id}`);
assert.deepEqual(await readSaved(), created, 'Initial POST/readback mismatch.');
await writeFile(path.join(out, 'seeded.json'), JSON.stringify(created, null, 2) + '\n');
await writeFile(path.join(out, 'aliases.json'), JSON.stringify({ nodes, canvases: works }, null, 2) + '\n');

const browser = await chromium.launch({ headless: true });
const context = await browser.newContext({ viewport: { width: 1700, height: 1100 }, acceptDownloads: true });
const page = await context.newPage(), browserErrors = [], blockedRequests = [], checks = [], revisions = [];
page.setDefaultTimeout(12000);
page.on('pageerror', error => browserErrors.push(error.message));
await context.route(/^https?:/, route => {
  if (new URL(route.request().url()).origin === origin) return route.continue();
  blockedRequests.push(route.request().url()); return route.abort();
});
await page.emulateMedia({ reducedMotion: 'reduce' });
const card = id => page.locator(`.ms-node[data-node-id="${id}"]`);
const idFor = key => { assert.ok(nodes[key], `Missing tutorial alias ${key}`); return nodes[key]; };
const savedNode = async id => (await readSaved()).nodes.find(node => node.id === id);
const inspector = () => page.locator('.inspector');
const panel = value => page.getByRole('combobox', { name: 'Side panel', exact: true }).selectOption(value);
const focusWork = () => page.locator('.ms-viewport').focus();
const expectSingle = () => expect(page.getByRole('button', { name: 'Single select', exact: true })).toHaveAttribute('aria-pressed', 'true');
async function open(work) {
  await page.goto(`${origin}/?project=${created.id}&canvas=${works[work]}`);
  await page.locator('.ms-world').waitFor(); await page.evaluate(() => document.fonts.ready);
}
async function target(id) {
  await page.keyboard.press('Escape'); await page.keyboard.press('Control+f');
  const search = page.getByRole('searchbox', { name: 'Search Work nodes', exact: true });
  await search.fill(id);
  const row = page.locator(`[data-outline-node="${id}"]`); await row.waitFor();
  await row.getByRole('button', { name: /^Jump to Node:/ }).click();
  await search.fill(''); await card(id).waitFor();
}
async function flush(label) {
  // A previous successful toast must not satisfy the next asynchronous save.
  await page.locator('.toast').evaluateAll(elements => elements.forEach(element => element.setAttribute('data-qa-old-save', 'true')));
  await page.keyboard.press('Control+s');
  await expect(page.locator('.toast:not([data-qa-old-save])')).toContainText('All changes are saved', { timeout: 15000 });
  const saved = await readSaved(); revisions.push({ label, revision: saved.revision }); return saved;
}
async function blankPoint() {
  const point = await page.locator('.ms-viewport').evaluate(viewport => {
    const r = viewport.getBoundingClientRect();
    for (let y = r.bottom - 90; y > r.top + 50; y -= 100) for (let x = r.left + 35; x < r.right - 50; x += 100) {
      const element = document.elementFromPoint(x, y);
      if (element && viewport.contains(element) && !element.closest('.ms-node,.ms-camera,.ms-container-hover-bar,.ms-gallery,.ms-expansion-tray')) return { x, y };
    }
    return null;
  });
  assert.ok(point, 'No genuinely empty point visible in the Work.'); return point;
}
async function screenshot(name) { await page.screenshot({ path: path.join(out, name) }); return name; }

let failure;
try {
  await open('author');
  const blankId = idFor('author/blank'), originalBlank = await savedNode(blankId);
  assert.equal(originalBlank.title, ''); assert.equal(originalBlank.text, '');
  await target(blankId); await panel('properties');
  const tag = inspector().getByLabel('Tag', { exact: true });
  const changedTag = originalBlank.tag + ' · verified independently';
  await tag.fill(changedTag); await tag.press('Tab'); await flush('Tag edit');
  assert.equal((await savedNode(blankId)).tag, changedTag);
  assert.equal((await savedNode(blankId)).title, '');
  assert.equal((await card(blankId).innerText()).trim(), '', 'Blank Media should not display its Tag or filler text.');
  await panel('content'); const title = inspector().getByLabel('Title', { exact: true });
  await title.fill('A temporary visible title'); await title.press('Tab'); await flush('Visible title edit');
  assert.equal((await savedNode(blankId)).tag, changedTag);
  await title.fill(''); await title.press('Tab'); await flush('Blank title restored');
  assert.equal((await card(blankId).innerText()).trim(), '');
  checks.push({ kind: 'tag-title-independence', nodeId: blankId, tag: changedTag, blankInteriorHasNoFiller: true });

  const editableId = idFor('author/editable'), originalEditable = await savedNode(editableId);
  await target(editableId); const heading = card(editableId).locator('.ms-node-title'); await heading.click();
  const inline = page.getByRole('textbox', { name: 'Work node title', exact: true });
  await expect(inline).toHaveAttribute('contenteditable', 'true');
  await inline.press('Control+a'); await page.keyboard.insertText('Edited directly in the isolated Work'); await page.keyboard.press('Escape');
  await flush('Direct text editing');
  assert.equal((await savedNode(editableId)).title, 'Edited directly in the isolated Work');
  assert.equal((await savedNode(editableId)).tag, originalEditable.tag);
  checks.push({ kind: 'inline-text-edit', nodeId: editableId, tagUnchanged: true });

  // Resize by a real edge gesture, not by synthetic mutation or a property edit.
  await target(editableId); await focusWork();
  const beforeResize = await savedNode(editableId), bounds = await card(editableId).boundingBox(); assert.ok(bounds);
  await page.mouse.move(bounds.x + bounds.width - 1, bounds.y + bounds.height / 2);
  const edgeCursor = await card(editableId).getAttribute('data-resize-cursor');
  assert.equal(edgeCursor, 'ew-resize', 'Text-filled node edge must advertise a resize target.');
  await page.mouse.down(); await page.mouse.move(bounds.x + bounds.width + 30, bounds.y + bounds.height / 2, { steps: 8 }); await page.mouse.up();
  await flush('Edge resize'); const afterResize = await savedNode(editableId);
  assert.ok(afterResize.width > beforeResize.width + 1, 'Edge drag did not increase node width.');
  assert.equal(afterResize.title, 'Edited directly in the isolated Work'); assert.equal(afterResize.tag, originalEditable.tag);
  assert.equal(await page.locator('[contenteditable="true"]').count(), 0, 'Edge drag entered inline text editing.');
  checks.push({ kind: 'edge-resize-priority', nodeId: editableId, widthBefore: beforeResize.width, widthAfter: afterResize.width, edgeCursor, screenshot: await screenshot('author-edited-resized.png') });

  // Toggle every keyboard mode, then create and delete exactly one disposable node.
  await focusWork(); const point = await blankPoint(); await page.mouse.move(point.x, point.y); await page.keyboard.press('a');
  const createMenu = page.getByRole('menu', { name: 'Create an object', exact: true });
  await expect(createMenu).toBeVisible(); assert.deepEqual(await createMenu.getByRole('menuitem').allTextContents(), ['Node', 'Link', 'Module']);
  await page.keyboard.press('a'); await expect(createMenu).toHaveCount(0); await expectSingle();
  await page.keyboard.press('s'); await expect(page.getByRole('button', { name: 'Select Area', exact: true })).toHaveAttribute('aria-pressed', 'true');
  await page.keyboard.press('s'); await expectSingle();
  await page.keyboard.press('d'); await expect(page.getByRole('button', { name: 'Delete objects', exact: true })).toHaveAttribute('aria-pressed', 'true');
  await page.keyboard.press('d'); await expectSingle();
  await page.keyboard.press('a'); await createMenu.getByRole('menuitem', { name: 'Node', exact: true }).click();
  await page.mouse.click(point.x, point.y);
  const form = page.getByRole('dialog', { name: 'New Node', exact: true }); await expect(form).toBeVisible();
  await form.getByLabel('Type', { exact: true }).selectOption('media');
  const availableTypes = await form.getByLabel('Type', { exact: true }).locator('option').evaluateAll(options => options.map(option => option.value));
  assert.ok(!availableTypes.includes('block'), 'Retired Block unexpectedly remains in installed creation choices.');
  await form.getByLabel('Tag', { exact: true }).fill('Isolated QA disposable node');
  await form.getByLabel('Title', { exact: true }).fill('Delete me once');
  await form.getByRole('button', { name: 'Create node', exact: true }).click();
  await expect(form).toHaveCount(0); await expectSingle();
  const withDisposable = await flush('One-shot creation');
  const disposable = withDisposable.nodes.find(node => node.tag === 'Isolated QA disposable node'); assert.ok(disposable);
  assert.equal(withDisposable.nodes.length, created.nodes.length + 1);
  await target(disposable.id); await focusWork(); await page.keyboard.press('d');
  const disposableBox = await card(disposable.id).boundingBox(); assert.ok(disposableBox);
  await page.mouse.click(disposableBox.x + disposableBox.width / 2, disposableBox.y + disposableBox.height / 2);
  await expect(card(disposable.id)).toHaveCount(0); await expectSingle();
  const afterDelete = await flush('One-shot deletion'); assert.equal(afterDelete.nodes.length, created.nodes.length);
  await focusWork(); await page.keyboard.press('Control+z'); await expect(card(disposable.id)).toHaveCount(1);
  await page.keyboard.press('Control+x'); await expect(card(disposable.id)).toHaveCount(0);
  await page.keyboard.press('Escape'); await expect(page.locator('.ms-node.ms-selected')).toHaveCount(0); await expectSingle(); await flush('Undo redo and Escape');
  checks.push({ kind: 'one-shot-tools-and-shortcuts', createMenu: ['Node', 'Link', 'Module'], repeatedA: true, repeatedS: true, repeatedD: true, nodeCreation: true, deletion: true, undoRedo: true, escapeCleared: true, retiredBlockAbsent: true });

  await open('navigate'); const portalId = idFor('navigate/practice-portal'); await target(portalId);
  const launcher = card(portalId).locator('.ms-canvas-open'); await launcher.hover();
  await launcher.click(); await expect(page).toHaveURL(new RegExp(`canvas=${works.author}`));
  await page.goBack(); await expect(page).toHaveURL(new RegExp(`canvas=${works.navigate}`));
  checks.push({ kind: 'editor-portal-navigation', nodeId: portalId, destination: works.author, sameWindow: true, historyBack: true });

  const parentId = idFor('navigate/practice-parent'), childId = idFor('navigate/practice-child');
  await target(parentId); await card(parentId).focus(); await card(parentId).press('Alt+ArrowRight');
  await expect(card(parentId)).toHaveAttribute('aria-expanded', 'true');
  await card(childId).focus(); await card(childId).press('Alt+ArrowRight');
  await expect(card(childId)).toHaveAttribute('aria-expanded', 'true');
  const expanded = await flush('Expanded nested states');
  assert.equal(expanded.nodes.find(node => node.id === parentId).collapsed, false);
  assert.equal(expanded.nodes.find(node => node.id === childId).collapsed, false);
  const graphBeforeReload = sha256(expanded);
  await page.reload(); await target(parentId);
  await expect(card(parentId)).toHaveAttribute('aria-expanded', 'true'); await expect(card(childId)).toHaveAttribute('aria-expanded', 'true');
  const reopened = await readSaved(); assert.equal(sha256(reopened), graphBeforeReload, 'Reopening changed the saved graph.');
  assert.deepEqual(reopened.assets, created.assets, 'Editor exercises changed an original attachment.');
  assert.deepEqual(reopened.nodes.map(node => node.id), created.nodes.map(node => node.id), 'Editing created/deleted identities outside the disposable node.');
  checks.push({ kind: 'save-reopen', parentId, childId, expandedStatesPreserved: true, exactGraphReadback: true, originalAssetsPreserved: reopened.assets.length, screenshot: await screenshot('nested-saved-reopened.png') });

  // Verify the tutorial's concrete playback boundary against the installed UI.
  const videoId = idFor('media/video'), audioId = idFor('media/audio');
  await open('media'); await target(videoId);
  const video = card(videoId).locator('video.ms-video'); await expect(video).toBeVisible();
  await expect.poll(() => video.evaluate(element => ({ ready: element.readyState >= 1, failed: !!element.error }))).toEqual({ ready: true, failed: false });
  await video.evaluate(async element => { element.muted = true; await element.play(); });
  await expect.poll(() => video.evaluate(element => element.currentTime)).toBeGreaterThan(.05);
  const videoProof = await video.evaluate(element => {
    const playing = !element.paused, elapsed = element.currentTime;
    const durationKnown = Number.isFinite(element.duration); element.pause();
    return { duration: durationKnown ? element.duration : null, durationKnown, elapsed, width: element.videoWidth, height: element.videoHeight, playing };
  });
  // A MediaRecorder WebM may initially expose an unknown/stream duration. Actual
  // decoded dimensions and advancing playback establish usable media instead.
  assert.ok(videoProof.width > 0 && videoProof.height > 0 && videoProof.elapsed > .05 && videoProof.playing, `The included WebM failed decoded playback: ${JSON.stringify(videoProof)}`);
  await target(audioId); assert.equal(await card(audioId).locator('audio').count(), 0, 'Refresh the tutorial audio capability claim if an audio player ships.');
  const audioNode = reopened.nodes.find(node => node.id === audioId), audioAsset = reopened.assets.find(asset => asset.id === audioNode.assetId);
  assert.equal(audioAsset.mime, 'audio/wav'); assert.ok(Buffer.from(audioAsset.base64, 'base64').length > 44);
  checks.push({ kind: 'native-video-and-audio-boundary', videoId, ...videoProof, audioId, audioMime: audioAsset.mime, audioBytes: Buffer.from(audioAsset.base64, 'base64').length, inlineAudioAbsentAsDocumented: true });

  const final = await readSaved();
  assert.ok(final.revision > created.revision, 'Editor actions did not advance the saved revision.');
  checks.push({ kind: 'revision-checked-persistence', initialRevision: created.revision, finalRevision: final.revision, delta: final.revision - created.revision, exactInitialReadback: true, originalProjectId: draft.id, isolatedCopyId: created.id });
  assert.equal(sha256(await readJson(path.join(directory, 'draft.json'))), frozenDraftSha256, 'The authoring draft changed during this run; rerun against the final draft.');
  assert.deepEqual(browserErrors, [], 'Browser script errors occurred.');
  await writeFile(path.join(out, 'final.json'), JSON.stringify(final, null, 2) + '\n');
} catch (error) {
  failure = error; await screenshot('failure.png').catch(() => {});
} finally {
  const report = { passed: !failure, inputDirectory: directory, inputProjectId: draft.id, inputSha256: frozenDraftSha256,
    inputCounts: { works: draft.canvases.length, nodes: draft.nodes.length, assets: draft.assets.length },
    coverageAudit: { passed: coverageAudit.passed, guideFeatures: coverageAudit.guideFeatures, labFeatures: coverageAudit.labFeatures, registeredFeatures: coverageAudit.registeredFeatures, lessons: coverageAudit.lessons },
    origin, hostProcessId: receipt.processId, dataRoot: receipt.dataRoot, isolatedProjectId: created.id, checks, revisions, browserErrors, blockedRequests, failure: failure ? { message: failure.message, stack: failure.stack } : null, finishedUtc: new Date().toISOString() };
  await writeFile(path.join(out, 'report.json'), JSON.stringify(report, null, 2) + '\n'); await browser.close();
  console.log(JSON.stringify({ passed: report.passed, checks: checks.length, isolatedProjectId: created.id, report: path.join(out, 'report.json'), failure: failure?.message }));
}
if (failure) throw failure;
