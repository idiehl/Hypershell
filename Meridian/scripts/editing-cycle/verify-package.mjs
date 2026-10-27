/** Exercises only the staged package on 5293, with independently forked fixture data. */
import assert from 'node:assert/strict';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { chromium, expect } from '@playwright/test';
import { addNode, commitSnapshots, forkProject, newProject } from '../../src/core/model.ts';
import { validateProject } from '../../src/core/rules.ts';

const origin = 'http://127.0.0.1:5293', output = 'output/editing-qa';
const digest = bytes => createHash('sha256').update(bytes).digest('hex');
const canonical = value => Array.isArray(value) ? value.map(canonical) : value && typeof value === 'object' ? Object.fromEntries(Object.keys(value).sort().map(key => [key, canonical(value[key])])) : value;
const objectHash = value => digest(JSON.stringify(canonical(value)));
const request = async (url, method = 'GET', data) => {
  const response = await fetch(origin + url, { method, ...(data ? { headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(data) } : {}) });
  if (!response.ok) throw new Error(`${method} ${url}: ${response.status} ${(await response.text()).slice(0, 3000)}`);
  return response.json();
};
const payload = html => JSON.parse(html.match(/<script type="application\/json" id="meridian-data">([\s\S]*?)<\/script>/)[1]).project;
const protectedPaths = [
  path.resolve('output/branch-atlas/installed.json'),
  path.join(process.env.LOCALAPPDATA, 'AtlasMeridian/library.json'),
  path.join(process.env.LOCALAPPDATA, 'AtlasMeridian/projects/3d896c72-36b9-4900-adb6-a68934591233/project.json'),
];
await mkdir(output, { recursive: true });
const report = { origin, startedAt: new Date().toISOString(), capability: false, packageFiles: {}, protectedFiles: {}, browserErrors: [], externalRequests: [], checks: [] };
for (const filename of protectedPaths) report.protectedFiles[filename] = digest(await readFile(filename));
for (const filename of ['Meridian.Host.exe', 'Meridian.Host.dll', 'wwwroot/editor.js', 'wwwroot/viewer.js', 'wwwroot/app.css', 'wwwroot/scene.css']) report.packageFiles[filename] = digest(await readFile(`output/editing-20261005/app/${filename}`));
const status = await request('/__meridian_status'); assert.ok(status.capabilities.includes('selection-snippets-v1')); report.capability = true;
const browser = await chromium.launch();
let page;
try {
  page = await browser.newPage({ viewport: { width: 1800, height: 1100 }, acceptDownloads: true });
  page.setDefaultTimeout(30000);
  await page.context().tracing.start({ screenshots: true, snapshots: true, sources: true });
  page.on('pageerror', error => report.browserErrors.push(error.message));
  page.on('request', req => { if (/^https?:/.test(req.url()) && !req.url().startsWith(origin + '/')) report.externalRequests.push(req.url()); });
  const card = id => page.locator(`.ms-node[data-node-id="${id}"]`);
  const title = id => card(id).locator('.ms-node-title');
  const save = async id => {
    const before = await request(`/api/projects/${id}`);
    await page.getByRole('button', { name: 'Save', exact: true }).click();
    await expect(page.locator('.save-state')).toHaveText(`Saved · r${before.revision + 1}`);
    return request(`/api/projects/${id}`);
  };
  const download = async name => {
    await page.locator('.selection-bar').getByRole('button', { name: 'Download selection', exact: true }).click();
    const dialog = page.getByRole('dialog', { name: 'Download selection', exact: true });
    await dialog.getByLabel('Canvas name', { exact: true }).fill(name);
    const downloading = page.waitForEvent('download'); await dialog.getByRole('button', { name: 'Download snippet', exact: true }).click();
    const downloaded = await downloading, file = path.resolve(output, `${name}.meridian.html`); await downloaded.saveAs(file);
    return { file, snippet: payload(await readFile(file, 'utf8')) };
  };

  report.progress = 'Editor selection, group, undo, save and reload';
  const fixture = newProject('Packaged editing verification'), canvasId = fixture.entryCanvasId;
  fixture.canvases[0].snapping = { stickyEdges: false, smartAlign: false, snapToGrid: false };
  const a = addNode(fixture, canvasId, 'text', 'First branch'); Object.assign(a, { x: 100, y: 270, width: 200, height: 130, collapsed: true, childLayout: 'spatial', text: 'Contains a source attachment.' });
  const child = addNode(fixture, canvasId, 'document', 'Hidden source'); Object.assign(child, { parentId: a.id, x: 110, y: 470, assetId: crypto.randomUUID() });
  fixture.assets.push({ id: child.assetId, name: 'source.txt', mime: 'text/plain', base64: Buffer.from('The complete source remains with its branch.').toString('base64') });
  const b = addNode(fixture, canvasId, 'text', 'Second branch'); Object.assign(b, { x: 410, y: 270, width: 200, height: 130, text: 'Another branch.' });
  const other = addNode(fixture, canvasId, 'text', 'Unselected neighbor'); Object.assign(other, { x: 750, y: 90, width: 230, height: 130 });
  fixture.links.push({ id: crypto.randomUUID(), canvasId, sourceId: a.id, targetId: b.id, kind: 'visual', relation: 'generic', direction: 'none', points: [{ x: 355, y: 430 }] });
  const created = await request('/api/projects', 'POST', commitSnapshots(fixture));
  await page.goto(`${origin}/?project=${created.id}`);
  await title(a.id).click(); await title(b.id).click({ modifiers: ['Shift'] });
  await expect(page.locator('.selection-count')).toHaveText('2 selected');
  const initialBox = await card(a.id).boundingBox();
  await page.mouse.move(initialBox.x + 60, initialBox.y + 25); await page.mouse.down();
  await page.mouse.move(initialBox.x + 135, initialBox.y + 70, { steps: 8 }); await page.mouse.up();
  const moved = await save(created.id), movedA = moved.nodes.find(node => node.id === a.id), dx = movedA.x - a.x, dy = movedA.y - a.y;
  assert.notEqual(dx, 0); assert.equal(moved.nodes.find(node => node.id === b.id).x, b.x + dx);
  assert.equal(moved.nodes.find(node => node.id === child.id).y, child.y + dy);
  assert.equal(moved.nodes.find(node => node.id === other.id).x, other.x);
  assert.deepEqual(moved.links[0].points[0], { x: 355 + dx, y: 430 + dy });
  await page.locator('.selection-bar').getByRole('button', { name: 'Group selected nodes', exact: true }).click();
  const grouping = page.getByRole('dialog', { name: 'Group selected nodes', exact: true });
  await grouping.getByLabel('Name', { exact: true }).fill('Collected ideas'); await grouping.getByRole('button', { name: 'Continue', exact: true }).click();
  await expect(page.locator('.inspector').getByLabel('Title', { exact: true })).toHaveValue('Collected ideas');
  const grouped = await save(created.id), group = grouped.nodes.find(node => node.title === 'Collected ideas');
  assert.equal(grouped.nodes.find(node => node.id === a.id).parentId, group.id); assert.equal(grouped.nodes.find(node => node.id === b.id).parentId, group.id);
  await page.getByRole('button', { name: 'Undo', exact: true }).click(); await expect(card(group.id)).toHaveCount(0);
  await page.getByRole('button', { name: 'Redo', exact: true }).click(); await expect(card(group.id)).toBeVisible();
  await page.getByRole('button', { name: 'Fit canvas', exact: true }).click();
  await title(group.id).dblclick(); await expect(card(a.id)).toHaveCount(0); await expect(card(b.id)).toHaveCount(0);
  const saved = await save(created.id); await page.reload(); await expect(card(group.id)).toHaveAttribute('aria-expanded', 'false');
  await page.screenshot({ path: `${output}/packaged-grouped-branches.png` });
  await title(group.id).click();
  const downloaded = await download('Collected ideas snippet'), snippet = downloaded.snippet;
  assert.equal(snippet.canvases.length, 1); assert.equal(snippet.nodes.length, 5); assert.equal(snippet.links.length, 1);
  assert.equal(snippet.assets[0].base64, fixture.assets[0].base64); assert.notEqual(snippet.assets[0].id, fixture.assets[0].id);
  assert.ok(snippet.nodes.every(node => !saved.nodes.some(original => original.id === node.id)));
  assert.deepEqual(validateProject(snippet), []);
  assert.equal(objectHash(await request(`/api/projects/${created.id}`)), objectHash(saved), 'Downloading changed the saved source');
  await expect(page.locator('.save-state')).toHaveText(`Saved · r${saved.revision}`);
  report.checks.push({ fixture: 'editor', projectId: created.id, groupId: group.id, hiddenDescendantsMovedOnce: true, internalRouteMovedOnce: true, undoRedo: true, saveReload: true, snippetNodeCount: snippet.nodes.length, sourceUnchanged: true });
  const offline = await browser.newPage({ viewport: { width: 1500, height: 950 } });
  const remote = []; offline.on('request', req => { if (/^https?:/.test(req.url())) remote.push(req.url()); });
  await offline.route(/^https?:/, route => route.abort()); await offline.goto(pathToFileURL(downloaded.file).href);
  await expect(offline.locator('.mv-title')).toHaveText('Collected ideas snippet');
  const copiedGroup = snippet.nodes.find(node => node.title === 'Collected ideas');
  await offline.locator(`.ms-node[data-node-id="${copiedGroup.id}"] .ms-node-title`).dblclick();
  await expect(offline.locator('.ms-node-title', { hasText: 'First branch' })).toBeVisible();
  assert.deepEqual(remote, []); await offline.close();

  report.progress = 'Read-only original atlas fork, large selection and snippet';
  const atlas = JSON.parse(await readFile('output/branch-atlas/installed.json', 'utf8'));
  assert.equal(atlas.nodes.length, 5941); assert.equal(atlas.canvases.length, 1);
  const independent = forkProject(atlas); assert.notEqual(independent.id, atlas.id);
  assert.ok(independent.nodes.every(node => !atlas.nodes.some(original => original.id === node.id)));
  assert.deepEqual(validateProject(independent), []);
  const copied = await request('/api/projects', 'POST', independent);
  await page.goto(`${origin}/?project=${copied.id}`); await page.getByRole('button', { name: 'Fit canvas', exact: true }).click();
  const chosen = ['The genealogy', 'The Iliad', 'The Odyssey'].map(name => copied.nodes.find(node => node.title === name && node.parentId === copied.canvases[0].rootId));
  assert.ok(chosen.every(Boolean));
  for (const [index, node] of chosen.entries()) await title(node.id).click(index ? { modifiers: ['Control'] } : {});
  await expect(page.locator('.selection-count')).toHaveText('3 selected');
  await page.screenshot({ path: `${output}/atlas-selected-branches.png` });
  const atlasSnippet = await download('Mythology selected branches');
  assert.ok(atlasSnippet.snippet.nodes.length > 3000); assert.deepEqual(validateProject(atlasSnippet.snippet), []);
  assert.equal(objectHash(await request(`/api/projects/${copied.id}`)), objectHash(copied), 'Selecting or downloading changed the source atlas copy');
  await expect(page.locator('.save-state')).toHaveText('Saved · r1');
  report.checks.push({ fixture: '5941-node atlas', sourceId: atlas.id, independentProjectId: copied.id, selectedBranches: chosen.map(node => node.title), selectedBranchCount: 3, snippetNodes: atlasSnippet.snippet.nodes.length, snippetLinks: atlasSnippet.snippet.links.length, snippetAssets: atlasSnippet.snippet.assets.length, sourceCopyUnchanged: true });
  for (const filename of protectedPaths) assert.equal(digest(await readFile(filename)), report.protectedFiles[filename], `Protected file changed: ${filename}`);
  report.protectedFilesUnchanged = true; assert.deepEqual(report.browserErrors, []); assert.deepEqual(report.externalRequests, []);
  report.completedAt = new Date().toISOString(); report.passed = true; delete report.progress;
  await page.context().tracing.stop({ path: `${output}/package-trace.zip` });
} catch (error) {
  report.error = { message: String(error.message).slice(0, 12000), stack: String(error.stack).slice(0, 12000) };
  if (page) {
    await page.screenshot({ path: `${output}/package-failure.png`, fullPage: true }).catch(() => {});
    await writeFile(`${output}/package-failure.txt`, await page.locator('body').innerText().catch(() => ''), 'utf8');
    await page.context().tracing.stop({ path: `${output}/package-failure-trace.zip` }).catch(() => {});
  }
  throw new Error(report.error.message.split('Call log:')[0]);
} finally {
  await browser.close(); await writeFile(`${output}/package-verification.json`, JSON.stringify(report, null, 2) + '\n', 'utf8');
}
console.log(JSON.stringify({ passed: report.passed, capability: report.capability, checks: report.checks, protectedFilesUnchanged: report.protectedFilesUnchanged, report: `${output}/package-verification.json` }, null, 2));
