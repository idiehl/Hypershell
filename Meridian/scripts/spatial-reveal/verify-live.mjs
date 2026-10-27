/** Read-only verification of the installed app and committed examples. */
import assert from 'node:assert/strict';
import { readFile, writeFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { chromium, expect } from '@playwright/test';
const origin = 'http://127.0.0.1:5240', output = 'output/spatial-qa';
const sha = bytes => createHash('sha256').update(bytes).digest('hex');
const canonical = value => Array.isArray(value) ? value.map(canonical) : value && typeof value === 'object' ? Object.fromEntries(Object.keys(value).sort().map(key => [key, canonical(value[key])])) : value;
const hash = value => sha(JSON.stringify(canonical(value)));
const get = async path => { const response = await fetch(origin + path); assert.ok(response.ok, `${path}: ${response.status}`); return response.json(); };
const manifest = JSON.parse(await readFile('output/spatial-reveal/manifest.json', 'utf8'));
const report = { checkedAt: new Date().toISOString(), capability: false, staticAssets: {}, projects: [], library: {}, unchangedProtectedFiles: 0, browserErrors: [], blockedWrites: [] };
const status = await get('/__meridian_status'); assert.ok(status.capabilities.includes('spatial-children-v1')); report.capability = true;
for (const name of ['editor.js', 'viewer.js', 'app.css', 'scene.css']) {
  const response = await fetch(`${origin}/${name}`); assert.ok(response.ok);
  const digest = sha(Buffer.from(await response.arrayBuffer()));
  assert.equal(digest, sha(await readFile(`output/spatial-20261005/app/wwwroot/${name}`)));
  report.staticAssets[name] = digest;
}
for (const item of manifest) {
  const project = await get(`/api/projects/${item.id}`);
  const installed = JSON.parse(await readFile(`output/spatial-reveal/${item.id}/installed.json`, 'utf8'));
  assert.equal(hash(project), hash(installed));
  assert.equal((await fetch(`${origin}/api/projects/${item.id}/recovery`)).status, 404);
  const checkpoints = await get(`/api/projects/${item.id}/checkpoints`);
  const checkpoint = checkpoints.find(c => c.name === 'Before spatial children pass · saved'); assert.ok(checkpoint);
  const baseline = JSON.parse(await readFile(item.baselineFile, 'utf8'));
  assert.equal(hash(await get(`/api/projects/${item.id}/checkpoints/${checkpoint.id}`)), hash(baseline.saved));
  report.projects.push({ id: item.id, revision: project.revision, checkpointId: checkpoint.id, checkpointMatchesBaseline: true, readbackMatches: true, canvases: project.canvases.length, nodes: project.nodes.length });
}
const library = await get('/api/library'); assert.equal(library.revision, 10);
assert.equal(library.entries.find(entry => entry.id === 'a3d00829-1577-4dec-8591-24495b6c885d').collapsed, true);
assert.equal(library.entries.length, 299); report.library = { revision: 10, entries: 299, restoredCollapsed: true };
const protectedFiles = JSON.parse(await readFile('.cache/spatial-cycle/protected-files.json', 'utf8'));
for (const file of protectedFiles) {
  if (/\\library\.json(?:\.bak)?$/.test(file.Path) || manifest.some(item => file.Path.endsWith(`\\${item.id}\\project.json`))) continue;
  assert.equal(sha(await readFile(file.Path)).toUpperCase(), file.Hash); report.unchangedProtectedFiles++;
}
const browser = await chromium.launch();
try {
  const page = await browser.newPage({ viewport: { width: 1800, height: 1100 } });
  await page.route('**/*', route => {
    if (!['GET', 'HEAD'].includes(route.request().method())) { report.blockedWrites.push(route.request().url()); return route.abort(); }
    return route.continue();
  });
  page.on('pageerror', error => report.browserErrors.push(error.message));
  const demo = manifest.find(item => item.id.startsWith('893ce'));
  await page.goto(`${origin}/?project=${demo.id}&canvas=${demo.reviewCanvasId}`);
  await page.getByRole('button', { name: 'Fit canvas', exact: true }).click();
  const parent = page.locator(`[data-node-id="${demo.reviewNodes.tiles}"]`);
  await parent.locator('.ms-node-title').click();
  await expect(page.locator('.inspector').getByLabel('Title', { exact: true })).toHaveValue('Words that grow into a composition');
  await expect(parent).toHaveAttribute('aria-expanded', 'false');
  await expect(page.locator('.save-state')).toHaveText('Saved · r4');
  await expect(page.getByRole('button', { name: 'Select child A probable Zeus', exact: true })).toBeVisible();
  await page.screenshot({ path: `${output}/installed-spatial-children.png` });
  assert.equal(new URL(page.url()).searchParams.get('canvas'), demo.reviewCanvasId);
  assert.deepEqual(report.browserErrors, []); assert.deepEqual(report.blockedWrites, []);
  await page.close();
} finally { await browser.close(); }
report.passed = true;
await writeFile(`${output}/live-verification.json`, JSON.stringify(report, null, 2));
console.log(JSON.stringify(report));
