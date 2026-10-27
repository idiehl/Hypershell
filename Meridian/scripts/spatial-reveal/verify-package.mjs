/** Test the staged package only at port 5296. This script never targets the user's live host. */
import assert from 'node:assert/strict';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { chromium, expect } from '@playwright/test';
import { forkProject } from '../../src/core/model.ts';
import { validateProject } from '../../src/core/rules.ts';

const origin = 'http://127.0.0.1:5296', output = 'output/spatial-qa';
const digest = value => createHash('sha256').update(value).digest('hex');
const canonical = value => Array.isArray(value) ? value.map(canonical) : value && typeof value === 'object' ? Object.fromEntries(Object.keys(value).sort().map(key => [key, canonical(value[key])])) : value;
const objectHash = value => digest(JSON.stringify(canonical(value)));
const request = async (path, method = 'GET', data) => {
  const response = await fetch(origin + path, { method, ...(data ? { headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(data) } : {}) });
  if (!response.ok) throw new Error(`${method} ${path}: ${response.status} ${await response.text()}`);
  return response.json();
};
await mkdir(output, { recursive: true });
const report = { origin, startedAt: new Date().toISOString(), capability: false, packageFiles: {}, projects: [], browserErrors: [] };
for (const filename of ['Meridian.Host.exe', 'Meridian.Host.dll', 'wwwroot/editor.js', 'wwwroot/viewer.js', 'wwwroot/app.css', 'wwwroot/scene.css']) report.packageFiles[filename] = digest(await readFile(`output/spatial-20261005/app/${filename}`));
const status = await request('/__meridian_status'); assert.ok(status.capabilities.includes('spatial-children-v1')); report.capability = true;
const manifest = JSON.parse(await readFile('output/spatial-reveal/manifest.json', 'utf8'));
const browser = await chromium.launch();
try {
  for (const item of manifest) {
    const candidateBytes = await readFile(item.candidateFile), candidate = JSON.parse(candidateBytes);
    assert.equal(digest(candidateBytes), item.candidateFileSha256, 'Frozen candidate changed');
    const independent = forkProject(candidate);
    const originalIds = new Set([candidate.id, ...candidate.canvases.map(canvas => canvas.id), ...candidate.nodes.map(node => node.id), ...candidate.assets.map(asset => asset.id), ...candidate.links.map(link => link.id)]);
    assert.ok(![independent.id, ...independent.nodes.map(node => node.id), ...independent.assets.map(asset => asset.id)].some(id => originalIds.has(id)), 'Independent test copy reused original identities');
    assert.deepEqual(validateProject(independent), []);
    const created = await request('/api/projects', 'POST', independent);
    assert.equal(objectHash(created), objectHash({ ...independent, revision: 1 }), 'Created graph differs from the submitted independent copy');
    const loaded = await request(`/api/projects/${created.id}`); assert.equal(objectHash(loaded), objectHash(created), 'Reloaded graph differs from the created copy');
    const canvasId = independent.canvases[candidate.canvases.findIndex(canvas => canvas.id === item.reviewCanvasId)].id;
    const mapId = id => independent.nodes[candidate.nodes.findIndex(node => node.id === id)].id;
    const targets = item.reviewNodes.chapter ? [item.reviewNodes.chapter] : [item.reviewNodes.relationships, item.reviewNodes.tiles];
    const checks = { originalProjectId: item.id, independentProjectId: created.id, canvasId, fullApiRoundTrip: true, assets: created.assets.length, assetBytes: created.assets.reduce((sum, asset) => sum + Buffer.from(asset.base64, 'base64').length, 0), disclosures: [], attachmentDownloadVerified: false };
    const page = await browser.newPage({ viewport: { width: 1800, height: 1100 }, acceptDownloads: true });
    await page.context().tracing.start({ screenshots: true, snapshots: true, sources: true });
    page.on('pageerror', error => report.browserErrors.push(error.message));
    await page.goto(`${origin}/?project=${created.id}&canvas=${canvasId}`);
    await page.getByRole('button', { name: 'Fit canvas', exact: true }).click();
    const address = page.url(), initialCamera = await page.locator('.ms-world').evaluate(node => node.style.transform);
    const card = id => page.locator(`.ms-node[data-node-id="${id}"]`);
    for (const originalId of targets) {
      const id = mapId(originalId), parent = independent.nodes.find(node => node.id === id);
      const before = await card(id).evaluate(node => ({ x: node.style.left, y: node.style.top, width: node.style.width, height: node.style.height }));
      const children = independent.nodes.filter(node => node.parentId === id);
      report.progress = { project: item.id, nodeId: id, parentTitle: parent.title, action: 'single-click title' };
      await card(id).locator('.ms-node-title').click();
      await expect(page.locator('.inspector').getByLabel('Title', { exact: true })).toHaveValue(parent.title);
      for (const child of children) {
        await expect(page.getByRole('button', { name: `Select child ${child.title}`, exact: true })).toBeVisible();
        await expect(card(child.id)).toHaveCount(0);
      }
      await page.waitForTimeout(600);
      await card(id).locator('.ms-node-title').dblclick();
      await expect(card(id)).toHaveAttribute('aria-expanded', 'true');
      assert.equal(page.url(), address); assert.equal(await page.locator('.ms-world').evaluate(node => node.style.transform), initialCamera);
      assert.deepEqual(await card(id).evaluate(node => ({ x: node.style.left, y: node.style.top, width: node.style.width, height: node.style.height })), before);
      for (const child of children) await expect(card(child.id)).toHaveCount(1);
      checks.disclosures.push({ parentId: id, children: children.length, selectionDidNotReveal: true, sameCanvasAndCamera: true, stableParentTile: true });
    }
    await page.getByRole('button', { name: 'Fit canvas', exact: true }).click();
    await page.screenshot({ path: `${output}/package-${item.id}-expanded.png` });
    const imageNode = independent.nodes.find(node => node.canvasId === canvasId && node.kind === 'image');
    assert.ok(imageNode, 'Missing real attachment in package fixture');
    const asset = independent.assets.find(asset => asset.id === imageNode.assetId);
    await card(imageNode.id).getByRole('button', { name: `Open ${asset.name}`, exact: true }).click();
    const dialog = page.locator('dialog.document-modal'); await expect(dialog).toBeVisible();
    await expect(dialog.locator('img')).toHaveJSProperty('complete', true);
    assert.ok(await dialog.locator('img').evaluate(image => image.naturalWidth > 0));
    const downloading = page.waitForEvent('download');
    await dialog.getByRole('link', { name: 'Download original', exact: true }).click();
    const download = await downloading, filename = `${output}/package-${item.id}-${asset.name}`; await download.saveAs(filename);
    assert.equal(digest(await readFile(filename)), digest(Buffer.from(asset.base64, 'base64'))); checks.attachmentDownloadVerified = true;
    await dialog.getByRole('button', { name: 'Close', exact: true }).click();
    await page.getByRole('button', { name: 'Save', exact: true }).click(); await expect(page.locator('.save-state')).toHaveText('Saved · r2');
    const saved = await request(`/api/projects/${created.id}`);
    assert.equal(objectHash(saved.assets), objectHash(created.assets), 'Saved attachment bytes or metadata changed'); assert.equal(objectHash(saved.links), objectHash(created.links), 'Saved relationship graph changed');
    assert.equal(saved.nodes.length, created.nodes.length); assert.equal(saved.canvases.length, created.canvases.length);
    for (const originalId of targets) assert.equal(saved.nodes.find(node => node.id === mapId(originalId)).collapsed, false);
    assert.deepEqual(validateProject(saved), []);
    await page.reload();
    for (const originalId of targets) {
      await expect(card(mapId(originalId))).toHaveAttribute('aria-expanded', 'true');
      for (const child of saved.nodes.filter(node => node.parentId === mapId(originalId))) await expect(card(child.id)).toHaveCount(1);
    }
    checks.savedRevision = saved.revision; checks.expandedStateSurvivedReload = true;
    checks.snapshotValueTypes = [...new Set(Object.values(saved.snapshots).map(snapshot => typeof snapshot.value))];
    report.projects.push(checks); await page.context().tracing.stop({ path: `${output}/package-${item.id}-trace.zip` }); await page.close();
  }
  assert.deepEqual(report.browserErrors, []); report.completedAt = new Date().toISOString(); report.passed = true;
} catch (error) {
  report.error = { message: String(error.message).slice(0, 12000), stack: String(error.stack).slice(0, 12000) };
  for (const [index, context] of browser.contexts().entries()) {
    for (const [pageIndex, page] of context.pages().entries()) {
      await page.screenshot({ path: `${output}/package-failure-${index}-${pageIndex}.png`, fullPage: true }).catch(() => {});
      await writeFile(`${output}/package-failure-${index}-${pageIndex}.txt`, await page.locator('body').innerText().catch(() => ''), 'utf8');
    }
    await context.tracing.stop({ path: `${output}/package-failure-${index}-trace.zip` }).catch(() => {});
  }
  throw new Error(report.error.message.split('Call log:')[0]);
} finally {
  await browser.close(); await writeFile(`${output}/package-verification.json`, JSON.stringify(report, null, 2) + '\n', 'utf8');
}
console.log(JSON.stringify(report, null, 2));
