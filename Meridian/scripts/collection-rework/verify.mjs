import assert from 'node:assert/strict';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';
import path from 'node:path';
import { chromium } from '@playwright/test';
import { buildPublication } from '../../src/publication.ts';
import { evaluate } from '../../src/core/engine.ts';
import { validateProject } from '../../src/core/rules.ts';

const root = 'output/collection-rework', out = `${root}/interactive-qa`;
await mkdir(out, { recursive: true });
const manifest = JSON.parse(await readFile(`${root}/manifest.json`, 'utf8'));
const projects = await Promise.all(manifest.map(async m => JSON.parse(await readFile(m.candidateFile, 'utf8'))));
const js = await readFile('dist/viewer.js', 'utf8'), css = await readFile('dist/scene.css', 'utf8');
const browser = await chromium.launch({ headless: true });
const page = await browser.newPage({ viewport: { width: 1600, height: 1100 } });
const errors = []; page.on('pageerror', e => errors.push(e.message));
await page.route(/^https?:/, route => route.abort());
const cases = [];
async function publication(p, name) {
  assert.deepEqual(validateProject(p), []);
  const file = path.resolve(out, `${name}.html`);
  await writeFile(file, buildPublication(p, js, css));
  await page.goto(pathToFileURL(file).href);
}
async function canvas(p, title) {
  const c = p.canvases.find(c => c.title === title); assert.ok(c, title);
  await page.getByLabel('Choose canvas', { exact: true }).selectOption(c.id);
  await page.getByRole('button', { name: 'Fit canvas', exact: true }).click();
  return c;
}
try {
  const first = projects.find(p => p.title === 'Meridian · First connections');
  const demo = first.canvases.find(c => c.title === 'Numbers, decisions and junctions');
  const n = title => first.nodes.find(n => n.canvasId === demo.id && n.title === title);
  const result = evaluate(first);
  assert.equal(result.values[n('Subtotal').id], 36);
  assert.equal(result.values[n('Order total').id], 40);
  assert.equal(result.values[n('Needs review?').id], false);
  await publication(first, 'first-connections'); await canvas(first, demo.title);
  await page.getByRole('button', { name: 'Run simulation', exact: true }).click();
  const state = title => page.locator(`[data-node-id="${n(title).id}"] .ms-state`);
  assert.equal(await state('Order total').textContent(), '40');
  await page.getByLabel('Unit price: value', { exact: true }).fill('15');
  await page.getByLabel('Unit price: value', { exact: true }).press('Tab');
  assert.equal(await state('Order total').textContent(), '49');
  assert.equal(await state('Needs review?').textContent(), 'On');
  await page.getByRole('button', { name: 'Collapse A small order · calculation system', exact: true }).click();
  assert.equal(await page.getByLabel('Unit price: value', { exact: true }).count(), 0);
  assert.equal(await state('Order total').textContent(), '49');
  assert.equal(await state('Needs review?').textContent(), 'On');
  await page.screenshot({ path: `${out}/numbers-collapsed.png` });
  await page.getByRole('button', { name: 'Expand A small order · calculation system', exact: true }).click();
  await page.getByRole('switch', { name: 'Order enabled: toggle switch', exact: true }).click();
  assert.equal(await state('Order total').textContent(), 'Unknown');
  assert.equal(await state('Needs review?').textContent(), 'Unknown');
  assert.equal(await state('Subtotal').textContent(), '45');
  await page.getByRole('switch', { name: 'Order enabled: toggle switch', exact: true }).click();
  assert.equal(await state('Order total').textContent(), '49');
  await page.getByRole('button', { name: 'Fit canvas', exact: true }).click();
  await page.screenshot({ path: `${out}/numbers-running.png` });
  cases.push('numeric demo evaluates, reacts, shares subtotal, survives collapse, clears dependent outputs on open, resumes');

  const expanded = projects.find(p => p.title.includes('Expanded reference atlas'));
  await publication(expanded, 'expanded-atlas');
  const welcome = await canvas(expanded, '01 · Myths in relation');
  const kinship = expanded.nodes.find(n => n.canvasId === welcome.id && n.title === '01 / Kinship');
  const kids = expanded.nodes.filter(n => n.parentId === kinship.id);
  const rome = expanded.nodes.find(n => n.canvasId === welcome.id && n.title === '03 / Rome');
  await page.getByRole('button', { name: 'Collapse 01 / Kinship', exact: true }).click();
  for (const kid of kids) assert.equal(await page.locator(`[data-node-id="${kid.id}"]`).count(), 0);
  assert.equal(await page.locator(`[data-node-id="${rome.id}"]`).count(), 1);
  await page.getByRole('button', { name: 'Expand 01 / Kinship', exact: true }).click();
  for (const kid of kids) assert.equal(await page.locator(`[data-node-id="${kid.id}"]`).count(), 1);
  await page.screenshot({ path: `${out}/welcome-expanded.png` });
  cases.push('canvas launchers independently collapse child canvases and preserve siblings');

  const book = await canvas(expanded, 'I01 · A quarrel fractures the army');
  const account = expanded.nodes.find(n => n.canvasId === book.id && n.title === 'The account');
  assert.equal(await page.locator(`[data-node-id="${account.id}"]`).count(), 0);
  await page.getByRole('button', { name: 'Expand Read the account', exact: true }).click();
  assert.equal(await page.locator(`[data-node-id="${account.id}"]`).count(), 1);
  const lens = expanded.nodes.find(n => n.canvasId === book.id && n.title === 'Interpretive lens');
  assert.equal(await page.locator(`[data-node-id="${lens.id}"]`).count(), 1);
  await page.getByRole('button', { name: 'Fit canvas', exact: true }).click();
  await page.screenshot({ path: `${out}/book-account-expanded.png` });
  cases.push('book account reveals complete source and connected readings independently from event and interpretation branches');
  await canvas(expanded, '02 · A selected divine family');
  assert.equal(await page.locator('.ms-node-junction').count(), 1);
  await page.screenshot({ path: `${out}/family-shared-junction.png` });
  await page.getByRole('button', { name: 'Collapse A selected family, with attributed relationships', exact: true }).click();
  assert.equal(await page.locator('.ms-node-junction').count(), 0);
  await page.getByRole('button', { name: 'Expand A selected family, with attributed relationships', exact: true }).click();
  assert.equal(await page.locator('.ms-node-junction').count(), 1);
  cases.push('explicit six-child visual junction remains within independently collapsible family branch');
  assert.deepEqual(errors, []);
  await writeFile(`${out}/verification.json`, JSON.stringify({ passed: true, cases, browserErrors: errors }, null, 2));
  console.log(JSON.stringify({ passed: true, cases, browserErrors: errors }, null, 2));
} finally { await browser.close(); }
