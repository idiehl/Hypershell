import { test, expect } from '@playwright/test';
import { readFile } from 'node:fs/promises';
import { newProject, addNode } from '../src/core/model.ts';
import { createDisclosureCluster } from '../src/core/disclosure-clusters.ts';
import { revealChildren } from '../src/core/reveal.ts';
import { buildPublication } from '../src/publication.ts';

let viewer: string, css: string;
test.beforeAll(async () => {
  [viewer, css] = await Promise.all(['dist/viewer.js', 'dist/scene.css'].map(file => readFile(file, 'utf8')));
});
function publication(shape: 'circle' | 'square') {
  const project = newProject('Cluster reading room'), work = project.entryCanvasId;
  const a = addNode(project, work, 'media', 'First source'), b = addNode(project, work, 'media', 'Second source');
  Object.assign(a, { x: 100, y: 200, width: 180, height: 110, sizeMode: 'fixed', childPosition: 'manual', style: { shape: 'rectangle' }, text: 'A readable account.' });
  Object.assign(b, { x: 400, y: 250, width: 190, height: 130, sizeMode: 'fixed', childPosition: 'manual', style: { shape: 'rectangle' }, text: 'A related account.' });
  const context = addNode(project, work, 'media', 'Explore the sources');
  Object.assign(context, { x: 850, y: 420, width: 240, height: 130, sizeMode: 'fixed', childPosition: 'manual', style: { shape: 'rectangle' }, text: 'Open the Cluster to compare the original accounts.' });
  const result = createDisclosureCluster(project, [a.id, b.id], { name: 'Sources and traditions', buttonShape: shape });
  return { html: buildPublication(revealChildren(result.project, result.rootId, false), viewer, css), clusterId: result.rootId, a, b };
}

test('standalone viewer presents an animated circular button, reveals its name, and expands original members on one click', async ({ page }, info) => {
  const errors: string[] = []; page.on('pageerror', error => errors.push(error.message));
  const fixture = publication('circle'); await page.setContent(fixture.html);
  const button = page.getByRole('button', { name: 'Expand Cluster: Sources and traditions', exact: true });
  await expect(button).toBeVisible(); await expect(button).toHaveAttribute('aria-expanded', 'false');
  await expect(page.locator(`[data-node-id="${fixture.a.id}"]`)).toHaveCount(0);
  const name = button.locator('.ms-cluster-name'); await expect(name).toBeHidden();
  await button.hover(); await expect(name).toBeVisible(); await expect(name).toHaveCSS('opacity', '1');
  expect(await button.locator('.ms-cluster-orbit').evaluate(node => getComputedStyle(node).animationName)).toBe('ms-cluster-orbit');
  const ratio = await button.evaluate(node => { const box = node.getBoundingClientRect(); return box.width / box.height; });
  expect(ratio).toBeCloseTo(1, 4);
  await page.screenshot({ path: info.outputPath('published-cluster-circle-hover.png') });
  await button.click(); await expect(button).toHaveCount(0);
  await expect(page.locator(`[data-node-id="${fixture.a.id}"]`)).toBeVisible();
  await expect(page.locator(`[data-node-id="${fixture.b.id}"]`)).toBeVisible();
  expect(errors).toEqual([]);
});

test('square button supports keyboard disclosure, ordinary double-click collapse, and reduced motion in the standalone viewer', async ({ page }, info) => {
  await page.emulateMedia({ reducedMotion: 'reduce' });
  const fixture = publication('square'); await page.setContent(fixture.html);
  const button = page.getByRole('button', { name: 'Expand Cluster: Sources and traditions', exact: true });
  await expect(button).toBeVisible(); await expect(button).toHaveAttribute('data-shape', 'square');
  expect(await button.locator('.ms-cluster-orbit').evaluate(node => getComputedStyle(node).animationName)).toBe('none');
  await button.focus(); await expect(button.locator('.ms-cluster-name')).toBeVisible();
  await button.press('Space'); await expect(button).toHaveCount(0);
  await page.locator(`[data-node-id="${fixture.a.id}"]`).hover();
  await expect(page.locator('.ms-container-hover-bar')).toHaveCount(0);
  await page.locator(`.ms-node[data-node-id="${fixture.clusterId}"]`).dblclick({ position: { x: 2, y: 20 } }); await expect(button).toBeVisible();
  await page.screenshot({ path: info.outputPath('published-cluster-square.png') });
  await button.press('Enter'); await expect(page.locator(`[data-node-id="${fixture.b.id}"]`)).toBeVisible();
  await page.locator(`[data-node-id="${fixture.a.id}"]`).hover(); await page.locator(`.ms-node[data-node-id="${fixture.clusterId}"]`).dblclick({ position: { x: 2, y: 20 } });
  await button.evaluate(node => (node as HTMLElement).click());
  await expect(page.locator(`[data-node-id="${fixture.b.id}"]`)).toBeVisible();
});

