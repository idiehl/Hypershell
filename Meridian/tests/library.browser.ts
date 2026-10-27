import { test, expect, type APIRequestContext, type Page } from '@playwright/test';
import { addCanvas, addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import { normalizeLibraryDocument, reconcileLibraryCanvas, type CanvasLibrary, type LibraryCanvasDocument, type LibraryCatalogItem } from '../src/core/library.ts';
import { CREATION_TYPES } from '../src/core/creation.ts';
import { selectEditorNode, inspectorPanel } from './ui-helpers.ts';

const tile = (page: Page, id: string) => page.locator(`#scene .ms-node[data-node-id="${id}"]`);
const geometry = async (page: Page, id: string) => tile(page, id).evaluate(element => {
  const style = (element as HTMLElement).style;
  return { x: Number.parseFloat(style.left), y: Number.parseFloat(style.top), width: Number.parseFloat(style.width), height: Number.parseFloat(style.height) };
});
const stored = async (request: APIRequestContext) => await (await request.get('/api/library')).json() as LibraryCanvasDocument;
const saved = (page: Page) => expect(page.locator('.save-state')).toHaveText(/^Saved · r\d+$/);

// Only the isolated Playwright host is used. A legacy response is simulated for
// migration rendering; core/host integration separately verifies persisted v1.
async function seed(page: Page, request: APIRequestContext, migrate = false) {
  const suffix = crypto.randomUUID().slice(0, 8), project = newProject(`Field notes ${suffix}`), chapter = addCanvas(project, `Deep reading ${suffix}`);
  const content = addNode(project, chapter.id, 'text', `Opened canvas content ${suffix}`); content.text = 'The destination remains a separate saved workspace.';
  expect((await request.post('/api/projects', { data: commitSnapshots(project) })).ok()).toBeTruthy();
  const make = (id: string, title: string, parentId: string | null, x: number, y: number, kind: 'section' | 'canvas' = 'section') => ({ id, title, parentId, x, y, kind, collapsed: false });
  const outer = make(crypto.randomUUID(), `Research ${suffix}`, null, 80, 230), inner = make(crypto.randomUUID(), `Sources ${suffix}`, outer.id, 450, 230);
  const launch = { ...make(crypto.randomUUID(), `Field notes launcher ${suffix}`, inner.id, 820, 230, 'canvas'), projectId: project.id, canvasId: project.entryCanvasId };
  const child = { ...make(crypto.randomUUID(), `Deep reading launcher ${suffix}`, launch.id, 1190, 230, 'canvas'), projectId: project.id, canvasId: chapter.id };
  const sibling = make(crypto.randomUUID(), `Independent ideas ${suffix}`, null, 80, 480);
  const current = await (await request.get('/api/library')).json();
  const priorRecovery = await request.get('/api/library/recovery');
  if (priorRecovery.ok()) { const recovery = await priorRecovery.json(); expect((await request.delete('/api/library/recovery', { data: { baseRevision: recovery.baseRevision, savedAt: recovery.savedAt } })).ok()).toBeTruthy(); }
  const source: CanvasLibrary = { schemaVersion: 1, revision: current.revision, entries: [outer, inner, launch, child, sibling] };
  const catalog = await (await request.get('/api/library/catalog')).json() as LibraryCatalogItem[];
  const document = reconcileLibraryCanvas(normalizeLibraryDocument(source, catalog), catalog);
  const note = addNode(document.project, document.project.entryCanvasId, 'text', `Hidden note ${suffix}`);
  Object.assign(note, { parentId: inner.id, x: 450, y: 480, width: 250, height: 140, placement: 'free', text: `Needle inside ordinary library content ${suffix}` });
  const response = await request.put('/api/library', { data: { library: document, expectedRevision: document.revision } });
  expect(response.ok()).toBeTruthy(); const persisted = await response.json() as LibraryCanvasDocument;
  if (migrate) {
    const legacyResponse = { ...source, revision: persisted.revision };
    await page.route('**/api/library', async route => {
      if (route.request().method() === 'GET') { await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(legacyResponse) }); await page.unroute('**/api/library'); }
      else await route.continue();
    });
  }
  await page.goto('/'); await expect(page.locator('.workspace')).toBeVisible(); await saved(page);
  return { project, chapter, content, source, outer, inner, launch, child, sibling, note };
}

async function select(page: Page, node: { id: string; title: string }) { await selectEditorNode(page, node.id, node.title); await expect(page.locator('.inspector').getByLabel('Title', { exact: true })).toHaveValue(node.title); }
async function nodeSaved(request: APIRequestContext, id: string, expected: Record<string, unknown>) { await expect.poll(async () => (await stored(request)).project.nodes.find(node => node.id === id)).toMatchObject(expected); }
async function formFromChild(page: Page, node: { id: string; title: string }) { await select(page, node); await inspectorPanel(page,'relations'); await page.getByRole('button', { name: 'Create child node', exact: true }).click(); return page.getByRole('dialog', { name: 'New Node', exact: true }); }

test('legacy library groups become ordinary enclosures preserving identifiers, ancestry and disclosure without overlapping roots', async ({ page, request }) => {
  const f = await seed(page, request, true), document = await stored(request);
  expect(document.schemaVersion).toBe(2); expect(document.project.revision).toBe(document.revision);
  for (const entry of f.source.entries) {
    const node = document.project.nodes.find(node => node.id === entry.id)!;
    expect(node).toMatchObject({ title: entry.title, collapsed: entry.collapsed,
      kind: entry.kind === 'section' ? 'container' : 'canvas-link', parentId: entry.parentId ?? document.project.canvases[0].rootId });
    if (entry.id === f.outer.id) expect(node).toMatchObject({ x: entry.x, y: entry.y });
    if (entry.kind === 'section') expect(node).toMatchObject({ containerVariant: 'contained', childLayout: 'contained', childArrangement: 'free' });
    const parent = entry.parentId && document.project.nodes.find(item => item.id === entry.parentId);
    if (parent && parent.kind === 'container') {
      const childBounds = await geometry(page, node.id), parentBounds = await geometry(page, parent.id);
      expect(childBounds.x).toBeGreaterThan(parentBounds.x); expect(childBounds.y).toBeGreaterThan(parentBounds.y);
      expect(childBounds.x + childBounds.width).toBeLessThan(parentBounds.x + parentBounds.width);
      expect(childBounds.y + childBounds.height).toBeLessThan(parentBounds.y + parentBounds.height);
    }
  }
  const outerBounds = await geometry(page, f.outer.id), siblingBounds = await geometry(page, f.sibling.id);
  expect(siblingBounds.x).toBe(f.sibling.x);
  expect(siblingBounds.y).toBeGreaterThanOrEqual(outerBounds.y + outerBounds.height);
  expect(document.canvasTargets[f.launch.id]).toEqual({ projectId: f.project.id, canvasId: f.project.entryCanvasId });
  await expect(page.locator('.library-workspace,.library-selection,.sidebar')).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'New section', exact: true })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'New subsection', exact: true })).toHaveCount(0);
  await expect(page.locator('.app-header').getByRole('button', { name: 'New canvas', exact: true })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'New Node', exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Delete objects', exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Single select', exact: true })).toHaveAttribute('aria-pressed', 'true');
});

test('home single click inspects contents and double click hides/reveals in place without changing camera or URL', async ({ page, request }) => {
  const f = await seed(page, request); await select(page, f.outer);
  await inspectorPanel(page,'relations');
  const inspector = page.locator('.inspector'), beforeUrl = page.url(), before = await stored(request), camera = await page.locator('#scene .ms-world').getAttribute('style');
  const ownGeometry = await geometry(page, f.outer.id), parent = before.project.nodes.find(node => node.id === f.outer.id)!;
  expect(parent.sizeMode).toBeUndefined(); expect(parent.collapsed).toBe(false);
  expect(ownGeometry.height).toBeGreaterThanOrEqual(parent.height);
  expect(ownGeometry.width).toBeGreaterThanOrEqual(parent.width);
  await expect(tile(page, f.inner.id)).toHaveCount(1);
  await expect(inspector.getByRole('button', { name: `Inspect descendant ${f.inner.title}`, exact: true })).toBeVisible();
  await tile(page, f.outer.id).locator('.ms-node-title').dblclick();
  await nodeSaved(request, f.outer.id, { collapsed: true }); await expect(tile(page, f.inner.id)).toHaveCount(0);
  expect(page.url()).toBe(beforeUrl); expect(await page.locator('#scene .ms-world').getAttribute('style')).toBe(camera);
  // Enclosed containers preserve a roomy composition when expanded and fit
  // their own content when collapsed, with all branches still inspectable.
  const collapsedGeometry = await geometry(page, f.outer.id);
  expect(collapsedGeometry.x).toBe(ownGeometry.x); expect(collapsedGeometry.y).toBe(ownGeometry.y);
  expect(collapsedGeometry.height).toBeGreaterThan(0); expect(collapsedGeometry.height).toBeLessThan(ownGeometry.height);
  expect(collapsedGeometry.width).toBeGreaterThan(0); expect(collapsedGeometry.width).toBeLessThanOrEqual(ownGeometry.width);
  await inspector.getByRole('button', { name: `Inspect descendant ${f.inner.title}`, exact: true }).click(); await inspectorPanel(page,'content');
  await expect(inspector.getByLabel('Title', { exact: true })).toHaveValue(f.inner.title); await expect(tile(page, f.inner.id)).toHaveCount(0);
  await tile(page, f.outer.id).locator('.ms-node-title').dblclick();
  await nodeSaved(request, f.outer.id, { collapsed: false }); await expect(tile(page, f.inner.id)).toHaveCount(1);
  expect(page.url()).toBe(beforeUrl); expect(await page.locator('#scene .ms-world').getAttribute('style')).toBe(camera);
  expect(await geometry(page, f.outer.id)).toEqual(ownGeometry);
  const after = await stored(request);
  expect(after.project.nodes.find(node => node.id === f.outer.id)).toMatchObject({ x: parent.x, y: parent.y, width: parent.width, height: parent.height });
  expect(after.project.nodes.map(node => ({ id: node.id, parentId: node.parentId, collapsed: node.collapsed }))).toEqual(before.project.nodes.map(node => ({ id: node.id, parentId: node.parentId, collapsed: node.collapsed })));
  expect(after.project.nodes.find(node => node.id === f.sibling.id)).toEqual(before.project.nodes.find(node => node.id === f.sibling.id));
});

test('nested home disclosure preserves each branch state and right-panel search reveals hidden content', async ({ page, request }) => {
  const f = await seed(page, request); await select(page, f.inner);
  await inspectorPanel(page,'relations'); await page.getByRole('button', { name: 'Toggle contents from relations', exact: true }).click(); await nodeSaved(request, f.inner.id, { collapsed: true });
  await select(page, f.outer); await tile(page, f.outer.id).locator('.ms-node-title').dblclick(); await nodeSaved(request, f.outer.id, { collapsed: true });
  await tile(page, f.outer.id).locator('.ms-node-title').dblclick(); await nodeSaved(request, f.outer.id, { collapsed: false });
  await expect(tile(page, f.inner.id)).toHaveCount(1); await expect(tile(page, f.launch.id)).toHaveCount(0);
  await page.keyboard.press('Control+f');
  const search = page.getByRole('searchbox', { name: 'Search Work nodes', exact: true }); await search.fill(f.note.id);
  const result = page.locator(`[data-outline-node="${f.note.id}"]`); await expect(result).toHaveClass(/is-hidden/); await result.getByRole('button',{name:`Jump to Node: ${f.note.title}`,exact:true}).click();
  await expect(tile(page, f.note.id)).toHaveCount(1); await nodeSaved(request, f.inner.id, { collapsed: false });
  expect(new URL(page.url()).searchParams.get('project')).toBeNull(); await search.fill(''); await inspectorPanel(page,'content');
  await expect(page.locator('.inspector').getByLabel('Title', { exact: true })).toHaveValue(f.note.title);
});

test('ordinary inspector reparents, renames and moves home branches with undo/redo and persisted geometry', async ({ page, request }) => {
  const f = await seed(page, request); await select(page, f.launch);
  await inspectorPanel(page,'relations');
  await page.locator('.inspector').getByLabel('Parent', { exact: true }).selectOption(f.sibling.id);
  await nodeSaved(request, f.launch.id, { parentId: f.sibling.id }); await nodeSaved(request, f.child.id, { parentId: f.launch.id });
  const renamed = `My authored launcher ${crypto.randomUUID().slice(0, 8)}`;
  await inspectorPanel(page,'content');
  await page.locator('.inspector').getByLabel('Title', { exact: true }).fill(renamed); await page.locator('.inspector').getByLabel('Title', { exact: true }).press('Tab');
  await nodeSaved(request, f.launch.id, { title: renamed }); await saved(page);
  const before = await stored(request), origin = before.project.nodes.find(node => node.id === f.launch.id)!, child = before.project.nodes.find(node => node.id === f.child.id)!;
  await inspectorPanel(page,'appearance');
  await page.locator('.inspector').getByLabel('X', { exact: true }).fill(String(Math.round(origin.x) + 123)); await page.locator('.inspector').getByLabel('X', { exact: true }).press('Tab');
  await nodeSaved(request, f.launch.id, { x: Math.round(origin.x) + 123 }); await saved(page);
  const moved = await stored(request), movedNode = moved.project.nodes.find(node => node.id === f.launch.id)!;
  expect(moved.project.nodes.find(node => node.id === f.child.id)!.x - child.x).toBeCloseTo(movedNode.x - origin.x, 6);
  await page.getByRole('button', { name: 'Undo', exact: true }).click(); await nodeSaved(request, f.launch.id, { x: origin.x });
  await page.getByRole('button', { name: 'Redo', exact: true }).click(); await nodeSaved(request, f.launch.id, { x: movedNode.x });
  await page.reload(); await saved(page); await select(page, { id: f.launch.id, title: renamed });
  await inspectorPanel(page,'relations');
  await expect(page.locator('.inspector').getByLabel('Parent', { exact: true })).toHaveValue(f.sibling.id);
  await inspectorPanel(page,'appearance');
  await expect(page.locator('.inspector').getByLabel('X', { exact: true })).toHaveValue(String(Math.round(movedNode.x)));
});

test('home and saved canvas share every creation type, variants, shapes and behavior fields', async ({ page, request, context }) => {
  const f = await seed(page, request), homeForm = await formFromChild(page, f.sibling);
  const choices = await homeForm.getByLabel('Type', { exact: true }).locator('option').evaluateAll(options => options.map(option => ({ value: (option as HTMLOptionElement).value, label: option.textContent })));
  expect(choices).toEqual(CREATION_TYPES.map(type => ({ value: type.value, label: type.label })));
  await homeForm.getByLabel('Type', { exact: true }).selectOption('container');
  expect(await homeForm.getByLabel('Display Pattern', { exact: true }).locator('option').allTextContents()).toEqual(['Freeform', 'Horizontal', 'Vertical', 'Grid', 'Spreadsheet', 'Window', 'Gallery']);
  await homeForm.getByLabel('Display Pattern', { exact: true }).selectOption('spreadsheet'); await expect(homeForm.getByLabel(/^Initial cells \(CSV\)/)).toBeVisible();
  await homeForm.getByLabel('Shape', { exact: true }).selectOption('none'); await expect(homeForm.locator('.creation-preview-tile')).toHaveAttribute('data-shape', 'none');
  expect(await homeForm.getByLabel('Behavior', { exact: true }).locator('option').allTextContents()).toEqual(['Work default', 'Free', 'Align to edges', 'Sticky']);
  await homeForm.getByRole('button', { name: 'Cancel', exact: true }).click();
  const editor = await context.newPage();
  try {
    await editor.goto(`/?project=${f.project.id}&canvas=${f.project.entryCanvasId}`); await expect(editor.locator('.workspace')).toBeVisible();
    await inspectorPanel(editor,'relations'); await editor.getByRole('button', { name: 'Create child node', exact: true }).click();
    const editorForm = editor.getByRole('dialog', { name: 'New Node', exact: true });
    expect(await editorForm.getByLabel('Type', { exact: true }).locator('option').evaluateAll(options => options.map(option => ({ value: (option as HTMLOptionElement).value, label: option.textContent })))).toEqual(choices);
    await editorForm.getByRole('button', { name: 'Cancel', exact: true }).click();
  } finally { await editor.close(); }
});

test('deleting a home Canvas launcher removes its branch and discovery does not recreate it after reload', async ({ page, request }) => {
  const f = await seed(page, request), originalSource = await (await request.get(`/api/projects/${f.project.id}`)).json();
  await select(page, f.launch); await inspectorPanel(page,'canvas'); await page.getByRole('searchbox',{name:'Search Work nodes',exact:true}).fill(f.launch.id); await page.locator(`[data-outline-node="${f.launch.id}"]`).getByRole('button',{name:`Delete node: ${f.launch.title}`,exact:true}).click();
  await expect.poll(async () => (await stored(request)).project.nodes.some(node => node.id === f.launch.id)).toBe(false); await saved(page);
  expect((await stored(request)).project.nodes.some(node => node.id === f.child.id)).toBe(false);
  await page.reload(); await saved(page);
  const after = await stored(request); expect(after.project.nodes.some(node => node.id === f.launch.id)).toBe(false);
  expect(Object.values(after.canvasTargets).some(target => target.projectId === f.project.id)).toBe(false);
  expect(await (await request.get(`/api/projects/${f.project.id}`)).json()).toEqual(originalSource);
});

test('shared home workspace remains usable on a compact viewport without document overflow', async ({ page, request }, info) => {
  const f = await seed(page, request); await page.setViewportSize({ width: 390, height: 844 }); await page.reload(); await saved(page);
  await expect(page.getByRole('button', { name: 'New Node', exact: true })).toBeVisible(); await select(page, f.child);
  await expect(page.locator('.inspector')).toHaveClass(/is-open/); await expect(page.getByRole('button', { name: 'Open Work', exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Close side panel', exact: true }).click();
  const dimensions = await page.evaluate(() => ({ width: innerWidth, height: innerHeight, scrollWidth: document.documentElement.scrollWidth, scrollHeight: document.documentElement.scrollHeight }));
  expect(dimensions.scrollWidth).toBeLessThanOrEqual(dimensions.width + 1); expect(dimensions.scrollHeight).toBeLessThanOrEqual(dimensions.height + 1);
  await page.screenshot({ path: info.outputPath('shared-home-compact.png'), fullPage: true });
});
