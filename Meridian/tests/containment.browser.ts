import { test, expect } from '@playwright/test';
import { readFile, writeFile } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';
import { addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import { buildPublication } from '../src/publication.ts';

test('ordinary nested nodes disclose independently with keyboard focus, hidden links, undo, save and offline parity', async ({ page, request }, info) => {
  const project = newProject('Containment QA'), canvas = project.entryCanvasId;
  const parent = addNode(project, canvas, 'text', 'Research chapter');
  // This regression deliberately exercises the authored fixed-size disclosure contract.
  // Automatic fitting has separate coverage in container-sizing.browser.ts.
  Object.assign(parent, { x: 60, y: 70, width: 820, height: 600, sizeMode: 'fixed', text: 'A short summary stays readable while the chapter is collapsed.' });
  const section = addNode(project, canvas, 'text', 'Sources');
  Object.assign(section, { parentId: parent.id, x: 110, y: 240, width: 670, height: 340, sizeMode: 'fixed', text: 'Two pieces of evidence.', collapsed: true });
  const source = addNode(project, canvas, 'input', 'Hidden source');
  Object.assign(source, { parentId: section.id, x: 160, y: 360, value: true });
  const note = addNode(project, canvas, 'text', 'Hidden evidence');
  Object.assign(note, { parentId: section.id, x: 460, y: 360, text: 'A preserved original.' });
  const output = addNode(project, canvas, 'output', 'Visible result'); Object.assign(output, { x: 980, y: 100 });
  project.links.push({ id: 'containment-signal', canvasId: canvas, sourceId: source.id, targetId: output.id, kind: 'functional', relation: 'causal' });
  project.links.push({ id: 'containment-internal', canvasId: canvas, sourceId: source.id, targetId: note.id, kind: 'visual', relation: 'causal' });
  await request.post('/api/projects', { data: commitSnapshots(project) }); await page.goto(`/?project=${project.id}`);
  const card = (id: string) => page.locator(`.ms-node[data-node-id="${id}"]`);
  const parentCard = card(parent.id), sectionCard = card(section.id);
  await expect(parentCard.locator('.ms-child-count')).toHaveCount(0);
  await expect(parentCard).toHaveAttribute('aria-label', /1 child node/);
  await expect(sectionCard).toHaveCSS('width', '670px');
  await expect(card(source.id)).toHaveCount(0);
  await expect(card(output.id).locator('.ms-state')).toHaveText('On');
  await expect(page.locator('[data-link-id="containment-internal"].ms-link-line')).toHaveCount(0);
  await parentCard.focus(); await page.keyboard.press('Alt+ArrowLeft');
  await expect(parentCard).toBeFocused(); await expect(parentCard).toHaveAttribute('aria-expanded', 'false');
  await expect(parentCard).toHaveCSS('width', '820px'); await expect(parentCard).toHaveCSS('height', '600px'); await expect(sectionCard).toHaveCount(0);
  await expect(parentCard.locator('.ms-text')).toHaveText(parent.text!);
  const summary = await parentCard.locator('.ms-text').evaluate(node => ({ height: node.clientHeight, scrollHeight: node.scrollHeight }));
  expect(summary.scrollHeight).toBeLessThanOrEqual(summary.height + 1);
  await expect(page.locator('[data-link-id="containment-signal"].ms-link-line')).toHaveCount(0);
  await page.keyboard.press('Alt+ArrowRight');
  await expect(parentCard).toHaveCSS('width', '820px'); await expect(sectionCard).toBeVisible(); await expect(card(source.id)).toHaveCount(0);
  await sectionCard.focus(); await page.keyboard.press('Alt+ArrowRight');
  await expect(card(source.id)).toBeVisible(); await expect(sectionCard.locator('.ms-child-count')).toHaveCount(0);
  await expect(sectionCard).toHaveAttribute('aria-label', /2 child nodes, 1 connection/);
  await expect(page.locator('[data-link-id="containment-signal"].ms-link-line')).toHaveCount(1);
  await page.getByRole('button', { name: 'Undo', exact: true }).click(); await expect(card(source.id)).toHaveCount(0);
  await page.getByRole('button', { name: 'Redo', exact: true }).click(); await expect(card(source.id)).toBeVisible();
  // This route crosses the header: the shaft remains behind the title hit region.
  const sectionCollapse = sectionCard.locator('.ms-node-title');
  // A committed automatic save may replace this handle while the locator resolves.
  await expect.poll(() => sectionCollapse.evaluate(button => {
    if (!button.isConnected) return 'detached disclosure';
    const rect = button.getBoundingClientRect();
    const hit = document.elementFromPoint(rect.x + rect.width / 2, rect.y + rect.height / 2);
    return button.contains(hit) ? 'title' : `${hit?.tagName} ${hit?.getAttribute('class')} ${hit?.getAttribute('aria-label')}`;
  })).toBe('title');
  await sectionCard.focus(); await page.keyboard.press('Alt+ArrowLeft');
  await parentCard.focus(); await page.keyboard.press('Alt+ArrowLeft');
  await page.keyboard.press('Control+s'); await expect(page.locator('.save-state')).toHaveText(/^Saved · r\d+$/);
  const saved = await (await request.get(`/api/projects/${project.id}`)).json();
  expect(saved.nodes.find((node: { id: string }) => node.id === parent.id)).toMatchObject({ width: 820, height: 600, collapsed: true });
  expect(saved.nodes.find((node: { id: string }) => node.id === section.id)).toMatchObject({ parentId: parent.id, collapsed: true });
  await page.reload(); await expect(parentCard).toHaveCSS('width', '820px'); await expect(sectionCard).toHaveCount(0);
  const html = buildPublication(saved, await readFile('dist/viewer.js', 'utf8'), await readFile('dist/scene.css', 'utf8'));
  const publication = info.outputPath('nested-containment.html'); await writeFile(publication, html);
  await page.goto(pathToFileURL(publication).href);
  const original = await page.locator('#meridian-data').textContent();
  await expect(card(output.id).locator('.ms-state')).toHaveText('Stopped');
  await page.getByRole('button', { name: 'Run simulation', exact: true }).click();
  await expect(parentCard).toHaveCSS('width', '820px'); await expect(card(output.id).locator('.ms-state')).toHaveText('On');
  await parentCard.focus(); await page.keyboard.press('Alt+ArrowRight');
  await expect(parentCard).toBeFocused();
  await expect(sectionCard).toBeVisible(); await expect(card(source.id)).toHaveCount(0);
  await sectionCard.focus(); await page.keyboard.press('Alt+ArrowRight'); await expect(card(source.id)).toBeVisible();
  expect(await page.locator('#meridian-data').textContent()).toBe(original);
  await page.screenshot({ path: info.outputPath('nested-containment.png') });
});
