import { test, expect, type APIRequestContext, type Page } from '@playwright/test';
import { readFile, writeFile } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';
import { inflateRawSync } from 'node:zlib';
import { addCanvas, addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import { normalizeLibraryDocument, reconcileLibrary } from '../src/core/library.ts';
import type { MeridianProject } from '../src/core/types.ts';
import { buildPublication, readPublication } from '../src/publication.ts';
import { inspectorPanel, workspaceAction } from './ui-helpers.ts';

const authoredTitle = 'The canvas of memory';
const authoredText = 'A canvas is also an artist’s material. Keep the user’s words: canvas, Canvas, canvases.';
const saved = async (request: APIRequestContext, id: string): Promise<MeridianProject> => (await request.get(`/api/projects/${id}`)).json();
function packageManifest(bytes: Buffer): MeridianProject {
  // Read the ZIP central directory, including archives using data descriptors.
  const end = bytes.lastIndexOf(Buffer.from([0x50, 0x4b, 0x05, 0x06])); expect(end).toBeGreaterThanOrEqual(0);
  let offset = bytes.readUInt32LE(end + 16);
  for (let count = 0; count < bytes.readUInt16LE(end + 10); count++) {
    expect(bytes.readUInt32LE(offset)).toBe(0x02014b50);
    const compression = bytes.readUInt16LE(offset + 10), compressedLength = bytes.readUInt32LE(offset + 20), nameLength = bytes.readUInt16LE(offset + 28), extraLength = bytes.readUInt16LE(offset + 30), commentLength = bytes.readUInt16LE(offset + 32), local = bytes.readUInt32LE(offset + 42);
    const name = bytes.subarray(offset + 46, offset + 46 + nameLength).toString('utf8');
    if (name === 'project.json') {
      const start = local + 30 + bytes.readUInt16LE(local + 26) + bytes.readUInt16LE(local + 28), entry = bytes.subarray(start, start + compressedLength);
      expect([0, 8]).toContain(compression); return JSON.parse((compression === 8 ? inflateRawSync(entry) : entry).toString('utf8'));
    }
    offset += 46 + nameLength + extraLength + commentLength;
  }
  throw new Error('The editable Work package is missing project.json.');
}

async function seed(page: Page, request: APIRequestContext) {
  const health = await request.get('/__meridian_status');
  expect(health.ok()).toBe(true); expect(new URL(health.url()).hostname).toBe('127.0.0.1'); expect(new URL(health.url()).port).not.toBe('5240');
  const project = newProject('Legacy canvas schema · authored collection'), home = project.canvases[0]!, destination = addCanvas(project, 'My canvas destination');
  home.title = 'A canvas title chosen by its author'; project.nodes[0]!.title = home.title;
  const passage = addNode(project, home.id, 'media', authoredTitle);
  Object.assign(passage, { tag: 'canvas tag', text: authoredText, contentType: 'text', x: 80, y: 90, width: 380, height: 200, sizeMode: 'fixed', placement: 'free', style: { contentSizing: 'fixed' } });
  const doorway = addNode(project, home.id, 'canvas-link', 'An authored canvas doorway');
  Object.assign(doorway, { x: 530, y: 90, width: 300, height: 220, sizeMode: 'fixed', placement: 'free', targetId: destination.rootId });
  const destinationText = addNode(project, destination.id, 'media', 'Inside the destination'); destinationText.text = authoredText;
  for (const work of project.canvases) work.snapping = { stickyEdges: false, smartAlign: false, snapToGrid: false };
  const created = await request.post('/api/projects', { data: commitSnapshots(project) }); expect(created.ok(), await created.text()).toBe(true);
  const baseline = await created.json() as MeridianProject;
  await page.goto(`/?project=${project.id}&canvas=${home.id}`); await expect(page.locator('.workspace')).toBeVisible();
  return { project: baseline, home, destination, passage, doorway, destinationText };
}

test('editor chrome uses Work while reading legacy schema and preserving authored canvas words', async ({ page, request }, info) => {
  const f = await seed(page, request); await inspectorPanel(page, 'canvas');
  await expect(page.getByRole('combobox', { name: 'Side panel', exact: true }).locator('option:checked')).toHaveText('Work');
  await expect(page.getByRole('group', { name: 'Work tools', exact: true })).toBeVisible();
  await expect(page.getByRole('searchbox', { name: 'Search Work nodes', exact: true })).toHaveAttribute('placeholder', 'Search a GUID or Name');
  await page.getByRole('searchbox', { name: 'Search Work nodes', exact: true }).fill('canvas');
  expect(await page.getByRole('combobox', { name: 'Search scope', exact: true }).locator('option').allTextContents()).toEqual(['This Work', 'All Works']);
  await page.getByRole('searchbox', { name: 'Search Work nodes', exact: true }).fill('');
  await expect(page.locator(`.ms-node[data-node-id="${f.passage.id}"]`)).toContainText(authoredTitle);
  await expect(page.locator(`.ms-node[data-node-id="${f.passage.id}"]`)).toContainText(authoredText);
  await page.getByRole('button', { name: 'File', exact: true }).click();
  await expect(page.getByRole('menuitem', { name: 'Export Work', exact: true })).toBeVisible();
  await expect(page.getByRole('menuitem', { name: /canvas/i })).toHaveCount(0); await page.keyboard.press('Escape');
  expect(await saved(request, f.project.id)).toEqual(f.project);
  await page.screenshot({ path: info.outputPath('work-editor-preserves-author-text.png') });
});

test('creation exposes Work as a node type with Work defaults, retaining the canvas-link schema value', async ({ page, request }, info) => {
  const f = await seed(page, request); await page.locator('.ms-viewport').focus(); await page.keyboard.press('a'); await page.getByRole('menuitem', { name: 'Node', exact: true }).click();
  const stage = (await page.locator('.canvas-stage').boundingBox())!; await page.mouse.click(stage.x + 70, stage.y + stage.height - 90);
  const form = page.getByRole('dialog', { name: 'New Node', exact: true }); await expect(form).toBeVisible();
  const types = form.getByLabel('Type', { exact: true }); await expect(types.locator('option[value="canvas"]')).toHaveText('Work');
  expect(await types.locator('option').allTextContents()).not.toContain('Canvas');
  await expect(form.getByLabel('Behavior', { exact: true }).locator('option[value="inherit"]')).toHaveText('Work default');
  await types.selectOption('canvas'); await expect(form.getByLabel('Work workspace', { exact: true })).toBeVisible();
  await page.screenshot({ path: info.outputPath('create-work-node.png') }); await form.getByRole('button', { name: 'Cancel', exact: true }).click();
  expect(await saved(request, f.project.id)).toEqual(f.project);
});

test('Appearance uses Work portal controls and retains per-node colors in existing schema', async ({ page, request }, info) => {
  const f = await seed(page, request); await inspectorPanel(page, 'canvas');
  const search = page.getByRole('searchbox', { name: 'Search Work nodes', exact: true }); await search.fill(f.doorway.id);
  await page.locator(`[data-outline-node="${f.doorway.id}"] .inspector-tree-title`).click(); await inspectorPanel(page, 'appearance');
  const panel = page.locator('.appearance-panel'); if (!(await panel.evaluate(el => (el as HTMLDetailsElement).open))) await panel.locator('summary').click();
  await expect(page.getByRole('combobox', { name: 'Work portal style', exact: true })).toBeVisible();
  const picker = page.locator('input[type="color"][aria-label="Work portal color"]'); await expect(picker).toBeVisible(); await picker.fill('#af7244');
  await expect(page.getByRole('button', { name: 'Use Work color', exact: true })).toBeVisible();
  await expect.poll(async () => (await saved(request, f.project.id)).nodes.find(node => node.id === f.doorway.id)?.style?.portalColor).toBe('#af7244');
  const result = await saved(request, f.project.id); expect(result.schemaVersion).toBe(1); expect(result.entryCanvasId).toBe(f.home.id); expect(result.canvases).toEqual(f.project.canvases);
  expect(result.nodes.find(node => node.id === f.passage.id)).toEqual(f.project.nodes.find(node => node.id === f.passage.id));
  await page.screenshot({ path: info.outputPath('work-portal-appearance.png') });
});

test('editable Work download preserves legacy identifiers and exact author text', async ({ page, request }, info) => {
  const f = await seed(page, request); await workspaceAction(page, 'Export Work');
  const form = page.getByRole('dialog', { name: 'Download editable Work', exact: true }); await expect(form).toBeVisible(); await expect(form).toContainText('2 Works');
  const [download] = await Promise.all([page.waitForEvent('download'), form.getByRole('button', { name: 'Download Work', exact: true }).click()]);
  const filename = info.outputPath('authored-legacy-schema.meridian'); await download.saveAs(filename); const document = packageManifest(await readFile(filename));
  expect(document.id).toBe(f.project.id); expect(document.entryCanvasId).toBe(f.home.id); expect(document.canvases.map(work => work.id)).toEqual(f.project.canvases.map(work => work.id));
  expect(document.nodes.find(node => node.id === f.passage.id)?.text).toBe(authoredText); expect(document.nodes.find(node => node.id === f.doorway.id)?.kind).toBe('canvas-link');
  expect(await saved(request, f.project.id)).toEqual(f.project);
});

test('standalone publication uses Work chrome and navigates legacy canvas targets without editing author text', async ({ page, request, browser }, info) => {
  const f = await seed(page, request), file = info.outputPath('legacy-works-offline.html');
  const html = buildPublication(f.project, await readFile('dist/viewer.js', 'utf8'), await readFile('dist/scene.css', 'utf8')); await writeFile(file, html);
  expect(readPublication(html).nodes.find(node => node.title === authoredTitle)?.text).toBe(authoredText);
  const context = await browser.newContext({ offline: true, viewport: { width: 1500, height: 950 } });
  try {
    const reader = await context.newPage(); await reader.goto(pathToFileURL(file).href);
    await expect(reader.getByRole('combobox', { name: 'Choose Work', exact: true })).toBeVisible(); await expect(reader.getByRole('region', { name: 'Published Work', exact: true })).toBeVisible();
    await reader.getByRole('button', { name: 'Explore project', exact: true }).click();
    await expect(reader.getByRole('searchbox', { name: 'Search project', exact: true })).toHaveAttribute('placeholder', 'Search all Works and attachments…');
    await reader.getByRole('button', { name: 'Close project navigation', exact: true }).click();
    await expect(reader.getByRole('button', { name: 'Download this published Work for viewing or importing into the Meridian editor', exact: true })).toBeVisible();
    await expect(reader.locator(`.ms-node[data-node-id="${f.passage.id}"]`)).toContainText(authoredText);
    const authored = await reader.locator('#meridian-data').textContent(); await reader.locator(`.ms-node[data-node-id="${f.doorway.id}"] .ms-canvas-open`).click();
    await expect(reader.locator(`.ms-node[data-node-id="${f.destinationText.id}"]`)).toBeVisible(); expect(await reader.locator('#meridian-data').textContent()).toBe(authored);
    await reader.screenshot({ path: info.outputPath('standalone-work-reader.png') });
  } finally { await context.close(); }
  expect(await saved(request, f.project.id)).toEqual(f.project);
});

test('new library defaults use Works but legacy user-authored canvas titles are not migrated', async ({ page, request }, info) => {
  expect(reconcileLibrary({ schemaVersion: 1, revision: 0, entries: [] }, []).entries[0]!.title).toBe('My Works');
  const legacy = { schemaVersion: 1, revision: 7, entries: [{ id: 'authored-section', kind: 'section', title: 'My canvases, exactly as named', parentId: null, collapsed: false, x: 80, y: 210 }] };
  const document = normalizeLibraryDocument(legacy, []); expect(document.project.title).toBe('Work Library'); expect(document.project.nodes.find(node => node.id === 'authored-section')?.title).toBe(legacy.entries[0]!.title);
  await page.goto('/'); await expect(page.locator('.workspace')).toBeVisible(); await expect(page.getByRole('button', { name: 'Work Library', exact: true })).toBeVisible(); await inspectorPanel(page, 'canvas');
  const library = await (await request.get('/api/library')).json();
  expect(library.project.title).toBe('Work Library'); expect(library.project.canvases[0].title).toBe('Work Library');
  await page.screenshot({ path: info.outputPath('work-library-defaults.png') });
});

