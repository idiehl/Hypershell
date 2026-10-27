/** Exercises the staged inherited-rule UI on an isolated 5293 host and fresh fixture. */
import assert from 'node:assert/strict';
import { readFile, writeFile, mkdir, readdir } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import path from 'node:path';
import { chromium, expect } from '@playwright/test';
import { addNode, commitSnapshots, newProject } from '../../src/core/model.ts';
import { validateProject } from '../../src/core/rules.ts';

const origin = 'http://127.0.0.1:5293', output = 'output/rules-qa', packageRoot = 'output/rules-20261006/app';
const digest = bytes => createHash('sha256').update(bytes).digest('hex');
const canonical = value => Array.isArray(value) ? value.map(canonical) : value && typeof value === 'object'
  ? Object.fromEntries(Object.keys(value).sort().map(key => [key, canonical(value[key])])) : value;
const objectHash = value => digest(JSON.stringify(canonical(value)));
const request = async (url, method = 'GET', data) => {
  const response = await fetch(origin + url, { method, ...(data ? { headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(data) } : {}) });
  if (!response.ok) throw new Error(`${method} ${url}: ${response.status} ${(await response.text()).slice(0, 3000)}`);
  return response.json();
};
async function protectedFiles() {
  const hashes = {};
  async function visit(directory) {
    for (const entry of await readdir(directory, { withFileTypes: true })) {
      const filename = path.join(directory, entry.name);
      if (entry.isDirectory()) await visit(filename);
      else if (entry.isFile() && entry.name !== '.host.lock') hashes[filename] = digest(await readFile(filename));
    }
  }
  await visit(path.join(process.env.LOCALAPPDATA, 'AtlasMeridian'));
  for (const relative of ['Meridian.Host.exe', 'Meridian.Host.dll', 'wwwroot/editor.js', 'wwwroot/viewer.js', 'wwwroot/app.css', 'wwwroot/scene.css']) {
    const filename = path.resolve('output/app', relative); hashes[filename] = digest(await readFile(filename));
  }
  return hashes;
}
await mkdir(output, { recursive: true });
const report = { origin, startedAt: new Date().toISOString(), packageFiles: {}, protectedFiles: await protectedFiles(), browserErrors: [], externalRequests: [], checks: [] };
for (const filename of ['Meridian.Host.exe', 'Meridian.Host.dll', 'wwwroot/editor.js', 'wwwroot/viewer.js', 'wwwroot/app.css', 'wwwroot/scene.css']) report.packageFiles[filename] = digest(await readFile(path.join(packageRoot, filename)));
const status = await request('/__meridian_status'); assert.ok(status.capabilities.includes('rule-impact-v1')); report.capability = true;

const fixture = newProject('Packaged inherited-rule verification'), canvasId = fixture.entryCanvasId, root = fixture.nodes[0];
root.rules = { nodeKinds: ['container', 'text', 'number', 'joint', 'output'], linkKinds: ['visual', 'numeric'], operations: ['ADD', 'MULTIPLY'] };
const lab = addNode(fixture, canvasId, 'container', 'Local calculation rules');
Object.assign(lab, { x: 100, y: 160, width: 320, height: 200, childLayout: 'spatial', collapsed: true });
const nested = addNode(fixture, canvasId, 'container', 'Guarded calculation');
Object.assign(nested, { parentId: lab.id, x: 100, y: 420, width: 300, height: 180, childLayout: 'spatial', collapsed: true, rules: { operations: ['ADD'] } });
const a = addNode(fixture, canvasId, 'number', 'Hidden amount A'); Object.assign(a, { parentId: nested.id, x: 100, y: 650, value: 3 });
const b = addNode(fixture, canvasId, 'number', 'Hidden amount B'); Object.assign(b, { parentId: nested.id, x: 360, y: 650, value: 7 });
const sum = addNode(fixture, canvasId, 'joint', 'Hidden sum'); Object.assign(sum, { parentId: nested.id, x: 650, y: 650, operation: 'ADD' });
const note = addNode(fixture, canvasId, 'text', 'Editable note'); Object.assign(note, { parentId: lab.id, x: 470, y: 420, text: 'This hidden note may itself contain future typed nodes.' });
const outputNode = addNode(fixture, canvasId, 'output', 'External result'); Object.assign(outputNode, { x: 530, y: 170, width: 220, height: 140, valueType: 'number' });
const unrelated = addNode(fixture, canvasId, 'text', 'Unrelated notes'); Object.assign(unrelated, { x: 830, y: 170, width: 250, height: 140, text: 'This sibling is outside the edited branch.' });
const makeLink = (source, target, targetPort, label) => ({ id: crypto.randomUUID(), canvasId, sourceId: source.id, targetId: target.id, kind: 'numeric', relation: 'causal', targetPort, label });
const boundary = makeLink(sum, outputNode, 0, 'Result across the branch boundary');
fixture.links.push(makeLink(a, sum, 0, 'First amount'), makeLink(b, sum, 1, 'Second amount'), boundary);
assert.deepEqual(validateProject(fixture), []);
const created = await request('/api/projects', 'POST', commitSnapshots(fixture));
report.fixtureId = created.id;
const browser = await chromium.launch();
let page;
try {
  page = await browser.newPage({ viewport: { width: 1800, height: 1100 }, acceptDownloads: true });
  page.setDefaultTimeout(30000);
  await page.context().tracing.start({ screenshots: true, snapshots: true, sources: true });
  page.on('pageerror', error => report.browserErrors.push(error.message));
  page.on('request', req => { if (/^https?:/.test(req.url()) && !req.url().startsWith(origin + '/')) report.externalRequests.push(req.url()); });
  const card = id => page.locator(`.ms-node[data-node-id="${id}"]`);
  const dialog = () => page.getByRole('dialog', { name: 'Edit inherited rules', exact: true });
  const openRules = async () => {
    await card(lab.id).locator('.ms-node-title').click();
    await page.locator('.inherited-rule-settings > summary').click();
    await page.getByRole('button', { name: 'Edit inherited rules', exact: true }).click();
    await expect(dialog()).toBeVisible();
  };
  const operationChoices = () => dialog().getByRole('group', { name: 'Allowed operations', exact: true });
  const cancel = async () => { await dialog().getByRole('button', { name: 'Cancel', exact: true }).click(); await expect(dialog()).toHaveCount(0); };
  const capturePreview = async filename => { await dialog().locator('.modal-body').evaluate(body => { body.scrollTop = 0; }); await page.screenshot({ path: `${output}/${filename}` }); };
  const assertSourceUnchanged = async () => assert.equal(objectHash(await request(`/api/projects/${created.id}`)), objectHash(created), 'A preview/cancel changed the saved source');
  await page.goto(`${origin}/?project=${created.id}`);
  await expect(card(outputNode.id).locator('.ms-state')).toHaveText('10');
  await expect(card(sum.id)).toHaveCount(0);

  report.progress = 'Conflicting hidden operations remain a draft';
  await openRules();
  await expect(dialog().getByRole('button', { name: 'Apply rules', exact: true })).toBeDisabled();
  await operationChoices().getByLabel('Inherit operations from parent', { exact: true }).uncheck();
  await operationChoices().getByLabel('Add', { exact: true }).uncheck();
  await expect(dialog().getByRole('button', { name: 'Apply rules', exact: true })).toBeDisabled();
  await expect(dialog().locator(`.rule-conflicts [data-rule-node-id="${nested.id}"]`)).toContainText('cannot widen');
  await expect(dialog().locator(`.rule-conflicts [data-rule-node-id="${sum.id}"]`)).toContainText('operation is excluded');
  await expect(dialog().locator(`.rule-impact-list [data-rule-node-id="${nested.id}"]`)).toContainText('Hidden');
  await expect(dialog().locator(`.rule-impact-list [data-rule-node-id="${unrelated.id}"]`)).toHaveCount(0);
  await capturePreview('packaged-hidden-conflicts.png');
  await assertSourceUnchanged(); await cancel(); await assertSourceUnchanged();
  await expect(page.getByRole('button', { name: 'Undo', exact: true })).toBeDisabled();
  assert.equal((await fetch(`${origin}/api/projects/${created.id}/recovery`)).status, 404, 'Preview created a recovery draft');
  report.checks.push({ hiddenOperationConflict: true, nestedWideningConflict: true, unrelatedSiblingUnaffected: true, cancelUnchanged: true, noPreviewRecovery: true });

  report.progress = 'Boundary connections are included in the impact preview';
  await openRules();
  const connections = dialog().getByRole('group', { name: 'Allowed connection types', exact: true });
  await connections.getByLabel('Inherit connection types from parent', { exact: true }).uncheck();
  await connections.getByLabel('Numeric', { exact: true }).uncheck();
  await expect(dialog().getByRole('button', { name: 'Apply rules', exact: true })).toBeDisabled();
  await expect(dialog().locator(`.rule-conflicts [data-rule-link-id="${boundary.id}"]`)).toContainText('Hidden sum → External result');
  await capturePreview('packaged-boundary-conflict.png');
  await cancel(); await assertSourceUnchanged();
  report.checks.push({ boundaryConnectionId: boundary.id, boundaryConnectionConflict: true, disclosureOwnerDoesNotOverrideEndpointRules: true });

  report.progress = 'Valid named change applies once, undoes once and saves exactly';
  await openRules();
  const nodes = dialog().getByRole('group', { name: 'Allowed node types', exact: true });
  await nodes.getByLabel('Inherit node types from parent', { exact: true }).uncheck();
  await expect(nodes.getByLabel('Image', { exact: true })).toBeDisabled();
  await nodes.getByLabel('Inherit node types from parent', { exact: true }).check();
  await operationChoices().getByLabel('Inherit operations from parent', { exact: true }).uncheck();
  await operationChoices().getByLabel('Multiply', { exact: true }).uncheck();
  const affected = dialog().locator(`.rule-impact-list [data-rule-node-id="${nested.id}"]`);
  await expect(affected).toContainText('Removed: Multiply');
  await affected.getByText('Compare permissions', { exact: true }).click();
  await expect(affected).toContainText('Operations before: Add, Multiply');
  await expect(affected).toContainText('After: Add');
  await expect(dialog().locator(`.rule-impact-list [data-rule-node-id="${sum.id}"]`)).toHaveCount(0);
  await expect(dialog().getByRole('button', { name: 'Apply rules', exact: true })).toBeEnabled();
  await capturePreview('packaged-rule-preview.png');
  await dialog().getByRole('button', { name: 'Apply rules', exact: true }).click();
  await expect(dialog()).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Undo', exact: true })).toBeEnabled();
  await page.getByRole('button', { name: 'Undo', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Undo', exact: true })).toBeDisabled();
  await openRules(); await expect(operationChoices().getByLabel('Inherit operations from parent', { exact: true })).toBeChecked(); await cancel();
  await page.getByRole('button', { name: 'Redo', exact: true }).click();
  await page.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(page.locator('.save-state')).toHaveText('Saved · r2');
  const saved = await request(`/api/projects/${created.id}`);
  assert.deepEqual(saved.nodes.find(node => node.id === lab.id).rules, { operations: ['ADD'] });
  const expected = structuredClone(created); expected.nodes.find(node => node.id === lab.id).rules = { operations: ['ADD'] };
  const authoredOnly = value => { const copy = structuredClone(value); delete copy.revision; delete copy.snapshots; return copy; };
  assert.equal(objectHash(authoredOnly(saved)), objectHash(authoredOnly(expected)), 'Applying one rule modified unrelated authored content');
  await page.reload(); await openRules();
  await expect(operationChoices().getByLabel('Inherit operations from parent', { exact: true })).not.toBeChecked();
  await expect(operationChoices().getByLabel('Add', { exact: true })).toBeChecked();
  await expect(operationChoices().getByLabel('Multiply', { exact: true })).not.toBeChecked();
  await expect(dialog().getByRole('group', { name: 'Allowed node types', exact: true }).getByLabel('Inherit node types from parent', { exact: true })).toBeChecked();
  await cancel(); await expect(card(outputNode.id).locator('.ms-state')).toHaveText('10');
  assert.equal((await fetch(`${origin}/api/projects/${created.id}/recovery`)).status, 404, 'Saved rule change left a stale recovery draft');
  await page.screenshot({ path: `${output}/packaged-saved-rules.png` });
  report.checks.push({ applyOneUndo: true, redo: true, saveReload: true, exactRules: saved.nodes.find(node => node.id === lab.id).rules, inheritedChoicesLocked: true, nestedUnchangedPermissionsExcluded: true, authoredContentOtherwiseUnchanged: true, hiddenCalculationRemainsDefined: true });

  assert.deepEqual(await protectedFiles(), report.protectedFiles, 'The isolated verification changed live Meridian data or installed package files');
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
