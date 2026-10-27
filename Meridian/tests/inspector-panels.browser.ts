import { test, expect, type Page, type APIRequestContext } from '@playwright/test';
import { newProject, addNode, addCanvas, commitSnapshots } from '../src/core/model.ts';
import { arrangeChildren } from '../src/core/grid.ts';
import type { MeridianProject } from '../src/core/types.ts';
import { readFile } from 'node:fs/promises';
import { readPublication } from '../src/publication.ts';
import { selectEditorNode, workspaceAction } from './ui-helpers.ts';

const tile = (page: Page, id: string) => page.locator(`.ms-node[data-node-id="${id}"]`);
const stored = async (request: APIRequestContext, id: string): Promise<MeridianProject> => (await request.get(`/api/projects/${id}`)).json();
async function panel(page: Page, name: string) { await page.getByRole('combobox', { name: 'Side panel', exact: true }).selectOption(name); }
async function fixture(page: Page, request: APIRequestContext, contentExamples = false) {
  const project = newProject('Inspector workspace');
  project.canvases[0]!.title = project.title; project.nodes[0]!.title = project.title;
  const parent = addNode(project, project.entryCanvasId, 'container', 'My collection');
  Object.assign(parent, { x: 120, y: 120, width: 300, height: 200, childLayout: 'spatial', collapsed: true });
  const child = addNode(project, project.entryCanvasId, 'text', 'Hidden passage');
  Object.assign(child, { parentId: parent.id, x: 1450, y: 1400, width: 200, height: 130, text: 'A passage kept intact through identity changes.' });
  const other = addNode(project, project.entryCanvasId, 'text', 'Comparison');
  Object.assign(other, { x: 620, y: 120, width: 200, height: 160, text: 'A separate branch.', style: { shape: 'hexagon' }, snapping: { snapToGrid: false } });
  const reference = addNode(project, project.entryCanvasId, 'reference', 'Passage reference');
  Object.assign(reference, { x: 620, y: 450, width: 200, height: 120, targetId: child.id });
  const elsewhereCanvas = addCanvas(project, 'Elsewhere');
  const elsewhere = addNode(project, elsewhereCanvas.id, 'text', 'Elsewhere only');
  // The unified Media node displays exactly one content item. Supporting files
  // and source hyperlinks belong to separate nodes, rather than mixed tiles.
  const documentNode = contentExamples ? addNode(project, project.entryCanvasId, 'media', 'Supporting source document') : undefined;
  const hyperlinkNode = contentExamples ? addNode(project, project.entryCanvasId, 'media', 'Source hyperlink') : undefined;
  if (documentNode) Object.assign(documentNode, { x: 920, y: 700, width: 220, height: 160 });
  if (hyperlinkNode) Object.assign(hyperlinkNode, { x: 920, y: 420, width: 220, height: 160 });
  const incident = { id: crypto.randomUUID(), canvasId: project.entryCanvasId, sourceId: parent.id, targetId: other.id, kind: 'visual' as const, relation: 'mutual' as const };
  const owned = { id: crypto.randomUUID(), canvasId: project.entryCanvasId, parentId: parent.id, sourceId: child.id, targetId: other.id, kind: 'visual' as const, relation: 'chronological' as const };
  const selfLinks = [1, 2].map(() => ({ id: crypto.randomUUID(), canvasId: project.entryCanvasId, sourceId: other.id, targetId: other.id, kind: 'visual' as const, relation: 'causal' as const, sourceAnchor: { x: 0, y: .5 }, targetAnchor: { x: 0, y: .5 } }));
  project.links.push(incident, owned, ...selfLinks);
  expect((await request.post('/api/projects', { data: commitSnapshots(project) })).ok()).toBeTruthy();
  await page.goto(`/?project=${project.id}&canvas=${project.entryCanvasId}`);
  await expect(tile(page, parent.id)).toBeVisible();
  return { project, parent, child, other, reference, elsewhere, incident, owned, selfLinks, documentNode, hyperlinkNode };
}

test('Canvas outline disclosure is independent of canvas expansion, and GUID jump reveals and centers hidden content', async ({ page, request }) => {
  const f = await fixture(page, request); await panel(page, 'canvas');
  await expect(page.getByRole('searchbox')).toHaveCount(1);
  await page.locator(`[data-outline-node="${f.parent.id}"] .inspector-tree-title`).click();
  await expect(page.locator(`[data-outline-node="${f.child.id}"]`)).toBeVisible();
  await expect(tile(page, f.child.id)).toHaveCount(0);
  expect((await stored(request, f.project.id)).nodes.find(node => node.id === f.parent.id)?.collapsed).toBe(true);
  await page.getByRole('searchbox', { name: 'Search Work nodes' }).fill(f.elsewhere.id);
  await expect(page.locator('.inspector-canvas-tree')).toContainText('No matching nodes on this canvas.');
  await page.getByRole('searchbox', { name: 'Search Work nodes' }).fill(f.child.id);
  await page.getByRole('button', { name: 'Jump to Node: Hidden passage', exact: true }).click();
  await expect(tile(page, f.child.id)).toBeVisible();
  await expect(tile(page, f.child.id)).toHaveClass(/ms-selected/);
  const bounds = (await tile(page, f.child.id).boundingBox())!, viewport = (await page.locator('.ms-viewport').boundingBox())!;
  expect(Math.abs(bounds.x + bounds.width / 2 - (viewport.x + viewport.width / 2))).toBeLessThan(8);
  expect(Math.abs(bounds.y + bounds.height / 2 - (viewport.y + viewport.height / 2))).toBeLessThan(8);
  await expect.poll(async () => (await stored(request, f.project.id)).nodes.find(node => node.id === f.parent.id)?.collapsed).toBe(false);
});

test('Properties, Appearance and Content stay separate and search appears only in Canvas', async ({ page, request }) => {
  const f = await fixture(page, request); await tile(page, f.other.id).locator('.ms-node-title').click();
  await panel(page, 'properties');
  await expect(page.locator('.inspector').getByLabel('Type', { exact: true })).toHaveValue('media');
  await expect(page.locator('.inspector').getByLabel('GUID', { exact: true })).toHaveValue(f.other.id);
  // A partial legacy override still inherits sticky edges from the canvas.
  await expect(page.locator('.inspector').getByLabel('Alignment behavior', { exact: true })).toHaveValue('sticky');
  await expect(page.locator('.inspector').getByLabel('Title', { exact: true })).toHaveCount(0);
  await expect(page.locator('.inspector').getByLabel('Shape', { exact: true })).toHaveCount(0);
  await expect(page.getByRole('searchbox')).toHaveCount(0);
  await panel(page, 'appearance');
  await expect(page.locator('.inspector').getByLabel('Shape', { exact: true })).toHaveValue('hexagon');
  await page.locator('.inspector').getByLabel('Shape', { exact: true }).selectOption('diamond');
  await expect.poll(async () => (await stored(request, f.project.id)).nodes.find(node => node.id === f.other.id)?.style?.shape).toBe('diamond');
  await expect(page.locator('.inspector').getByLabel('Title', { exact: true })).toHaveCount(0);
  await panel(page, 'content');
  await expect(page.locator('.inspector').getByLabel('Title', { exact: true })).toHaveValue(f.other.title);
  await page.locator('.inspector').getByLabel('Title', { exact: true }).fill('A revised comparison');
  await page.locator('.inspector').getByLabel('Title', { exact: true }).press('Tab');
  await expect.poll(async () => (await stored(request, f.project.id)).nodes.find(node => node.id === f.other.id)?.title).toBe('A revised comparison');
  for (const name of ['properties', 'appearance', 'content', 'relations', 'links']) { await panel(page, name); await expect(page.getByRole('searchbox')).toHaveCount(0); }
  await expect(page.getByRole('button', { name: 'Node settings', exact: true })).toHaveCount(0);
  await expect(page.locator('.workspace-chevron')).toHaveCount(0);
  await expect(page.locator('.ms-resize').filter({ hasText: '⋱' })).toHaveCount(0);
});

test('custom GUID editing preserves child, reference and connection identities and rejects an occupied GUID', async ({ page, request }) => {
  const f = await fixture(page, request); await tile(page, f.parent.id).locator('.ms-node-title').click(); await panel(page, 'properties');
  const guid = page.locator('.inspector').getByLabel('GUID', { exact: true });
  await guid.fill(f.other.id); await page.getByRole('button', { name: 'Apply GUID', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('already used');
  expect((await stored(request, f.project.id)).nodes.find(node => node.id === f.parent.id)?.title).toBe(f.parent.title);
  const renamed = 'my_collection_custom'; await guid.fill(renamed); await page.getByRole('button', { name: 'Apply GUID', exact: true }).click();
  await expect.poll(async () => (await stored(request, f.project.id)).nodes.some(node => node.id === renamed)).toBe(true);
  const result = await stored(request, f.project.id);
  expect(result.nodes.find(node => node.id === f.child.id)?.parentId).toBe(renamed);
  expect(result.links.find(link => link.id === f.incident.id)?.sourceId).toBe(renamed);
  expect(result.links.find(link => link.id === f.owned.id)?.parentId).toBe(renamed);
  expect(result.nodes.find(node => node.id === f.reference.id)?.targetId).toBe(f.child.id);
  await page.reload(); await tile(page, renamed).locator('.ms-node-title').click(); await panel(page, 'properties');
  await expect(page.locator('.inspector').getByLabel('GUID', { exact: true })).toHaveValue(renamed);
});

test('Links includes owned hidden connections and both legacy self links with independent inspect and delete actions', async ({ page, request }) => {
  const f = await fixture(page, request); await tile(page, f.parent.id).locator('.ms-node-title').click(); await panel(page, 'links');
  await expect(page.locator(`[data-associated-link="${f.incident.id}"]`)).toContainText('source');
  await expect(page.locator(`[data-associated-link="${f.owned.id}"]`)).toContainText('contains this link');
  await expect(page.locator(`[data-associated-link="${f.owned.id}"]`)).toContainText('Hidden');
  await expect(page.locator(`.ms-link-line[data-link-id="${f.owned.id}"]`)).toHaveCount(0);
  await tile(page, f.other.id).locator('.ms-node-title').click(); await panel(page, 'links');
  for (const link of f.selfLinks) await expect(page.locator(`[data-associated-link="${link.id}"]`)).toContainText('source · destination');
  const first = page.locator(`[data-associated-link="${f.selfLinks[0]!.id}"]`);
  await first.getByRole('button', { name: 'Delete link: Comparison → Comparison', exact: true }).click();
  await expect.poll(async () => (await stored(request, f.project.id)).links.some(link => link.id === f.selfLinks[0]!.id)).toBe(false);
  expect((await stored(request, f.project.id)).links.some(link => link.id === f.selfLinks[1]!.id)).toBe(true);
  await expect(tile(page, f.other.id)).not.toContainText('links');
});

test('remote conceptual links save and reopen without adding a visual line and can be removed independently', async ({ page, request }) => {
  const f = await fixture(page, request); await tile(page, f.other.id).locator('.ms-node-title').click(); await panel(page, 'links');
  const lines = await page.locator('.ms-link-line').count();
  await page.locator('.inspector').getByLabel('Target GUID', { exact: true }).fill('remotely_stored_source');
  await page.locator('.inspector-remote-form').getByLabel('Relationship', { exact: true }).selectOption('causal');
  await page.locator('.inspector-remote-form').getByLabel('Label', { exact: true }).fill('Remote source material');
  await page.getByRole('button', { name: 'Add remote link', exact: true }).click();
  await expect.poll(async () => (await stored(request, f.project.id)).nodes.find(node => node.id === f.other.id)?.remoteLinks?.length).toBe(1);
  expect(await page.locator('.ms-link-line').count()).toBe(lines);
  await expect(page.locator('.inspector-remote-row')).toContainText('External GUID');
  await expect(page.locator('.inspector-remote-row')).toContainText('remotely_stored_source');
  await page.reload(); await tile(page, f.other.id).locator('.ms-node-title').click(); await panel(page, 'links');
  await expect(page.locator('.inspector-remote-row')).toContainText('Remote source material');
  await page.locator('.inspector-remote-row').getByRole('button', { name: 'Remove remote link to remotely_stored_source', exact: true }).click();
  await expect.poll(async () => (await stored(request, f.project.id)).nodes.find(node => node.id === f.other.id)?.remoteLinks).toBeUndefined();
  expect((await stored(request, f.project.id)).links).toHaveLength(f.project.links.length);
});

test('canvas root alignment edits the canvas default and ordinary nodes can override or inherit it', async ({ page, request }) => {
  const f = await fixture(page, request); await panel(page, 'canvas');
  await page.locator(`[data-outline-node="${f.project.canvases[0]!.rootId}"]`).getByRole('button', { name: 'Jump to Node: Inspector workspace', exact: true }).click();
  await panel(page, 'properties');
  await page.locator('.inspector').getByLabel('Alignment behavior', { exact: true }).selectOption('free');
  await expect.poll(async () => (await stored(request, f.project.id)).canvases[0]!.snapping?.stickyEdges).toBe(false);
  await tile(page, f.other.id).locator('.ms-node-title').click(); await panel(page, 'properties');
  await page.locator('.inspector').getByLabel('Alignment behavior', { exact: true }).selectOption('sticky');
  await expect.poll(async () => (await stored(request, f.project.id)).nodes.find(node => node.id === f.other.id)?.snapping?.stickyEdges).toBe(true);
  await page.locator('.inspector').getByLabel('Alignment behavior', { exact: true }).selectOption('inherit');
  await expect.poll(async () => (await stored(request, f.project.id)).nodes.find(node => node.id === f.other.id)?.snapping).toBeUndefined();
});

test('Content uses one item per Media node and preserves separate image, document and hyperlink nodes in publication', async ({ page, request }, info) => {
  const f = await fixture(page, request, true); await selectEditorNode(page, f.other.id, f.other.title); await panel(page, 'properties');
  await page.locator('.inspector').getByLabel('Type', { exact: true }).selectOption('media'); await panel(page, 'content');
  await page.locator('.inspector').getByLabel('Content', { exact: true }).selectOption('asset');
  const image = 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLbtAAAAABJRU5ErkJggg==';
  await page.locator('.inspector').getByLabel('Content file', { exact: true }).setInputFiles({ name: 'Illustration.png', mimeType: 'image/png', buffer: Buffer.from(image, 'base64') });
  await expect(page.locator('.inspector').getByRole('button', { name: 'Open content file', exact: true })).toHaveText('Illustration.png');
  await expect(page.locator('.inspector').getByLabel('Text body', { exact: true })).toHaveCount(0);
  await expect(page.locator('.inspector').getByLabel('Add attachment', { exact: true })).toHaveCount(0);
  const document = 'Supporting source material, preserved in its own document node.\n';
  await selectEditorNode(page, f.documentNode!.id, f.documentNode!.title);
  await page.locator('.inspector').getByLabel('Content', { exact: true }).selectOption('asset');
  await page.locator('.inspector').getByLabel('Content file', { exact: true }).setInputFiles({ name: 'Source notes.txt', mimeType: 'text/plain', buffer: Buffer.from(document) });
  await expect(page.locator('.inspector').getByRole('button', { name: 'Open content file', exact: true })).toHaveText('Source notes.txt');
  await selectEditorNode(page, f.hyperlinkNode!.id, f.hyperlinkNode!.title);
  await page.locator('.inspector').getByLabel('Content', { exact: true }).selectOption('link');
  await page.locator('.inspector').getByLabel('Hyperlink URL', { exact: true }).fill('https://example.org/source');
  await page.locator('.inspector').getByLabel('Hyperlink label', { exact: true }).fill('Source record');
  await page.getByRole('button', { name: 'Apply hyperlink', exact: true }).click();
  await expect.poll(async () => (await stored(request, f.project.id)).nodes.find(node => node.id === f.hyperlinkNode!.id)?.hyperlinks).toEqual([{ url: 'https://example.org/source', label: 'Source record' }]);
  const before = await stored(request, f.project.id), node = before.nodes.find(node => node.id === f.other.id)!, documentItem = before.nodes.find(node => node.id === f.documentNode!.id)!, hyperlinkItem = before.nodes.find(node => node.id === f.hyperlinkNode!.id)!;
  expect(node.kind).toBe('media'); expect(node.contentType).toBe('asset'); expect(node.assetId).toBeTruthy();
  expect(node.text).toBeUndefined(); expect(node.attachments).toBeUndefined(); expect(node.hyperlinks).toBeUndefined();
  expect(before.assets.find(asset => asset.id === node.assetId)?.base64).toBe(image);
  expect(documentItem.contentType).toBe('asset'); expect(documentItem.text).toBeUndefined(); expect(documentItem.attachments).toBeUndefined(); expect(documentItem.hyperlinks).toBeUndefined();
  expect(Buffer.from(before.assets.find(asset => asset.id === documentItem.assetId)!.base64, 'base64').toString()).toBe(document);
  expect(hyperlinkItem.contentType).toBe('link'); expect(hyperlinkItem.assetId).toBeUndefined(); expect(hyperlinkItem.text).toBeUndefined(); expect(hyperlinkItem.attachments).toBeUndefined();
  await page.reload(); await selectEditorNode(page, f.other.id, f.other.title);
  await expect(page.locator('.inspector-content-preview')).toBeVisible();
  await page.screenshot({ path: info.outputPath('content-media-attachments.png') });
  await selectEditorNode(page, f.hyperlinkNode!.id, f.hyperlinkNode!.title);
  await expect(tile(page, f.hyperlinkNode!.id).getByRole('link', { name: 'Source record', exact: true })).toHaveAttribute('href', 'https://example.org/source');
  await workspaceAction(page, 'Preview'); await page.getByRole('button', { name: 'Open preview', exact: true }).click();
  const preview = page.getByRole('dialog', { name: 'Published preview', exact: true });
  await expect(preview).toBeVisible(); await expect(preview.locator('iframe')).toBeVisible();
  await preview.getByRole('button', { name: 'Close', exact: true }).click();
  const downloading = page.waitForEvent('download'); await workspaceAction(page, 'Publish'); await page.getByRole('button', { name: 'Publish HTML', exact: true }).click();
  const download = await downloading, file = info.outputPath('attachments-publication.html'); await download.saveAs(file);
  const copy = readPublication(await readFile(file, 'utf8')), copied = copy.nodes.find(node => node.title === f.other.title)!;
  const copiedDocument = copy.nodes.find(node => node.title === f.documentNode!.title)!, copiedHyperlink = copy.nodes.find(node => node.title === f.hyperlinkNode!.title)!;
  expect(copied.kind).toBe('media'); expect(copied.contentType).toBe('asset'); expect(copied.attachments).toBeUndefined(); expect(copied.hyperlinks).toBeUndefined(); expect(copied.text).toBeUndefined();
  expect(copy.assets.find(asset => asset.id === copied.assetId)?.base64).toBe(image);
  expect(copiedDocument.contentType).toBe('asset'); expect(Buffer.from(copy.assets.find(asset => asset.id === copiedDocument.assetId)!.base64, 'base64').toString()).toBe(document);
  expect(copiedHyperlink.contentType).toBe('link'); expect(copiedHyperlink.hyperlinks).toEqual(hyperlinkItem.hyperlinks); expect(copiedHyperlink.assetId).toBeUndefined(); expect(copiedHyperlink.text).toBeUndefined();
  expect(copy.nodes.find(node => node.title === f.child.title)?.text).toBe(f.child.text);
});

test('the desktop side panel closes and its draggable divider remembers width with keyboard and reset controls', async ({ page, request }, info) => {
  await fixture(page, request); const divider = page.getByRole('separator', { name: 'Resize side panel', exact: true });
  await expect(divider).toBeVisible(); const initial = Number(await divider.getAttribute('aria-valuenow'));
  const rect = (await divider.boundingBox())!;
  await page.mouse.move(rect.x + rect.width / 2, rect.y + 220); await page.mouse.down();
  await page.mouse.move(rect.x - 85, rect.y + 220, { steps: 5 }); await page.mouse.up();
  await expect.poll(async () => Number(await page.getByRole('separator', { name: 'Resize side panel', exact: true }).getAttribute('aria-valuenow'))).toBeGreaterThan(initial + 70);
  const width = Number(await divider.getAttribute('aria-valuenow'));
  await page.reload(); await expect(page.getByRole('separator', { name: 'Resize side panel', exact: true })).toHaveAttribute('aria-valuenow', String(width));
  const restored = page.getByRole('separator', { name: 'Resize side panel', exact: true });
  await restored.focus(); await page.keyboard.press('ArrowLeft'); await expect(restored).toHaveAttribute('aria-valuenow', String(Math.min(520, width + 20)));
  await page.keyboard.press('Home'); await expect(restored).toHaveAttribute('aria-valuenow', '310');
  await page.getByRole('button', { name: 'Close side panel', exact: true }).click(); await expect(page.locator('.inspector')).toBeHidden();
  expect((await page.locator('.canvas-stage').boundingBox())!.width).toBeGreaterThan(1450);
  await page.getByRole('button', { name: 'Show side panel', exact: true }).click(); await expect(page.locator('.inspector')).toBeVisible();
  await expect(page.getByRole('separator', { name: 'Resize side panel', exact: true })).toHaveAttribute('aria-valuenow', '310');
  await page.screenshot({ path: info.outputPath('canvas-hierarchy-panel.png') });
});

test('applying a custom GUID after autosave acknowledges a focused field preserves the newest revision and snapshots', async ({ page, request }) => {
  const f = await fixture(page, request); await tile(page, f.other.id).locator('.ms-node-title').click(); await panel(page, 'content');
  let release!: () => void, arrived!: () => void, finished!: () => void, writes = 0;
  const hold = new Promise<void>(resolve => { release = resolve; }), received = new Promise<void>(resolve => { arrived = resolve; });
  const completed = new Promise<void>(resolve => { finished = resolve; });
  await page.route(`**/api/projects/${f.project.id}`, async route => {
    if (route.request().method() !== 'PUT') { await route.continue(); return; }
    const response = await route.fetch();
    if (++writes === 1) { arrived(); await hold; await route.fulfill({ response }); finished(); }
    else await route.fulfill({ response });
  });
  try {
    const title = page.locator('.inspector').getByLabel('Title', { exact: true });
    await title.fill('Comparison saved before renaming'); await title.press('Tab'); await received;
    await panel(page, 'properties'); const guid = page.locator('.inspector').getByLabel('GUID', { exact: true });
    const renamed = 'comparison_after_acknowledged_save'; await guid.fill(renamed);
    release(); await expect.poll(async () => (await stored(request, f.project.id)).revision).toBe(2);
    // The save acknowledgment deliberately leaves the focused editor mounted.
    await expect(guid).toHaveValue(renamed); await expect(guid).toBeFocused();
    await page.getByRole('button', { name: 'Apply GUID', exact: true }).click();
    await expect.poll(async () => (await stored(request, f.project.id)).revision).toBe(3);
    const result = await stored(request, f.project.id), node = result.nodes.find(node => node.id === renamed)!;
    expect(node.title).toBe('Comparison saved before renaming'); expect(result.revision).toBe(3);
    expect(result.snapshots[renamed]?.node.title).toBe(node.title); expect(result.snapshots[f.other.id]).toBeUndefined();
    expect(result.links.find(link => link.id === f.incident.id)?.targetId).toBe(renamed);
    for (const link of f.selfLinks) { const saved = result.links.find(item => item.id === link.id)!; expect(saved.sourceId).toBe(renamed); expect(saved.targetId).toBe(renamed); }
    expect(result.nodes.find(item => item.id === f.child.id)?.text).toBe(f.child.text);
    await expect(page.getByRole('alert').filter({ hasText: /conflict/i })).toHaveCount(0);
  } finally { release(); if (writes) await completed; await page.unroute(`**/api/projects/${f.project.id}`); }
});

test('Relations applies child appearance, behavior and cell sizing explicitly with undo while preserving descendant content', async ({ page, request }) => {
  let project = newProject('Child editing workspace');
  const parent = addNode(project, project.entryCanvasId, 'container', 'Editable children');
  // This test asks children to adopt deliberately authored 220×160 cells.
  // Spreadsheet keeps explicit cell pixels while Grid derives dimensions from contents.
  Object.assign(parent, { x: 120, y: 120, width: 530, height: 280, sizeMode: 'fixed', childLayout: 'contained', containerVariant: 'spreadsheet', childArrangement: 'grid', grid: { columns: 2, rows: 1, cellWidth: 220, cellHeight: 160, gap: 0 } });
  const first = addNode(project, project.entryCanvasId, 'text', 'First child');
  Object.assign(first, { parentId: parent.id, width: 180, height: 120, collapsed: true, childLayout: 'spatial', text: 'Keep the first child content.', style: { fill: '#123A37', fontSize: 14, titleSize: 18 } });
  const second = addNode(project, project.entryCanvasId, 'text', 'Second child');
  Object.assign(second, { parentId: parent.id, width: 160, height: 100, text: 'Keep the second child content.', style: { shape: 'ellipse', textColor: '#D2F3E9', fontSize: 12 } });
  const descendant = addNode(project, project.entryCanvasId, 'text', 'Grandchild');
  Object.assign(descendant, { parentId: first.id, x: 1450, y: 1400, width: 130, height: 90, text: 'Deeper content remains untouched.', style: { shape: 'circle', fontSize: 11 }, snapping: { stickyEdges: true, snapToGrid: false, smartAlign: false } });
  project = arrangeChildren(project, parent.id, 'grid').project;
  const firstStyle = structuredClone(project.nodes.find(node => node.id === first.id)!.style);
  const secondStyle = structuredClone(project.nodes.find(node => node.id === second.id)!.style);
  expect(firstStyle).toEqual({ ...first.style, shape: 'rectangle' });
  expect(secondStyle).toEqual({ ...second.style, shape: 'rectangle' });
  expect((await request.post('/api/projects', { data: commitSnapshots(project) })).ok()).toBeTruthy();
  await page.goto(`/?project=${project.id}&canvas=${project.entryCanvasId}`);
  await tile(page, parent.id).locator('.ms-node-title').click(); await panel(page, 'relations');
  const tools = page.locator('.child-editing-tools'); await expect(tools).not.toHaveAttribute('open', '');
  await tools.locator('summary').click();
  await tools.getByLabel('Child shape', { exact: true }).selectOption('hexagon');
  await tools.getByLabel('Child title size', { exact: true }).fill('20');
  const before = await stored(request, project.id);
  expect(before.nodes.find(node => node.id === first.id)?.style).toEqual(firstStyle);
  expect(before.nodes.find(node => node.id === second.id)?.style).toEqual(secondStyle);
  await tools.getByRole('button', { name: 'Apply child appearance', exact: true }).click();
  await expect.poll(async () => (await stored(request, project.id)).nodes.find(node => node.id === second.id)?.style?.shape).toBe('hexagon');
  let result = await stored(request, project.id);
  for (const child of [first, second]) { const saved = result.nodes.find(node => node.id === child.id)!; expect(saved.style?.shape).toBe('hexagon'); expect(saved.style?.titleSize).toBe(20); expect(saved.text).toBe(child.text); }
  expect(result.nodes.find(node => node.id === first.id)?.style?.fill).toBe(first.style!.fill);
  expect(result.nodes.find(node => node.id === second.id)?.style?.fontSize).toBe(second.style!.fontSize);
  expect(result.nodes.find(node => node.id === descendant.id)?.style).toEqual(descendant.style);
  await page.getByRole('button', { name: 'Undo', exact: true }).click();
  await expect.poll(async () => (await stored(request, project.id)).nodes.find(node => node.id === second.id)?.style).toEqual(secondStyle);
  expect((await stored(request, project.id)).nodes.find(node => node.id === first.id)?.style).toEqual(firstStyle);
  await tools.getByLabel('Child alignment', { exact: true }).selectOption('free');
  await tools.getByRole('button', { name: 'Apply child alignment', exact: true }).click();
  await expect.poll(async () => (await stored(request, project.id)).nodes.find(node => node.id === first.id)?.snapping).toEqual({ stickyEdges: false, smartAlign: false, snapToGrid: false });
  await expect(tools.getByRole('button', { name: 'Fit child tiles to cells', exact: true })).toHaveCount(0);
  await tools.getByLabel('Child width', { exact: true }).fill('260');
  await tools.getByLabel('Child height', { exact: true }).fill('180');
  await tools.getByRole('button', { name: 'Apply child dimensions', exact: true }).click();
  // The fixed 530px parent has 5px child padding and a 1px border on
  // both sides. Its 518px interior is the wall: two cells fit at 259px,
  // while the requested 260px cell size remains authored grid metadata.
  await expect.poll(async () => (await stored(request, project.id)).nodes.find(node => node.id === second.id)?.width).toBe(259);
  result = await stored(request, project.id);
  expect(result.nodes.find(node => node.id === parent.id)?.grid).toMatchObject({ cellWidth: 260, cellHeight: 180 });
  const savedParent = result.nodes.find(node => node.id === parent.id)!;
  expect([savedParent.width, savedParent.height, savedParent.sizeMode]).toEqual([530, 280, 'fixed']);
  for (const child of [first, second]) { const saved = result.nodes.find(node => node.id === child.id)!; expect(saved.width).toBe(259); expect(saved.height).toBe(180); expect(saved.sizeMode).toBe('fixed'); expect(saved.text).toBe(child.text); expect(saved.parentId).toBe(parent.id); expect(saved.x).toBeGreaterThanOrEqual(savedParent.x + 6); expect(saved.x + saved.width).toBeLessThanOrEqual(savedParent.x + savedParent.width - 6); expect(saved.y + saved.height).toBeLessThanOrEqual(savedParent.y + savedParent.height - 6); }
  const savedDescendant = result.nodes.find(node => node.id === descendant.id)!;
  expect(savedDescendant.width).toBe(descendant.width); expect(savedDescendant.height).toBe(descendant.height);
  expect(savedDescendant.style).toEqual(descendant.style); expect(savedDescendant.snapping).toEqual(descendant.snapping); expect(savedDescendant.text).toBe(descendant.text);
  await page.getByRole('button', { name: 'Undo', exact: true }).click();
  await expect.poll(async () => (await stored(request, project.id)).nodes.find(node => node.id === second.id)?.width).toBe(220);
  result = await stored(request, project.id);
  expect(result.nodes.find(node => node.id === parent.id)?.grid).toMatchObject({ cellWidth: 220, cellHeight: 160 });
  for (const child of [first, second]) { const saved = result.nodes.find(node => node.id === child.id)!; expect(saved.width).toBe(220); expect(saved.height).toBe(160); expect(saved.style).toEqual(child.id === first.id ? firstStyle : secondStyle); }
  await page.reload(); await tile(page, parent.id).locator('.ms-node-title').click(); await panel(page, 'relations');
  await expect(page.locator('.child-editing-tools')).not.toHaveAttribute('open', '');
  expect((await stored(request, project.id)).nodes.find(node => node.id === second.id)?.width).toBe(220);
});
