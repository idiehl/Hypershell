import { test, expect, type APIRequestContext, type Page } from '@playwright/test';
import { addCanvas, addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import type { MeridianNode, MeridianProject } from '../src/core/types.ts';

const card = (page: Page, id: string) => page.locator(`.ms-node[data-node-id="${id}"]`);
const inspector = (page: Page) => page.locator('.inspector');
const search = (page: Page) => inspector(page).getByRole('searchbox', { name: 'Search Work nodes', exact: true });
const panel = async (page: Page, value: string) => inspector(page).getByRole('combobox', { name: 'Side panel', exact: true }).selectOption(value);
const camera = (page: Page) => page.locator('.ms-world').evaluate(element => (element as HTMLElement).style.transform);

function fixture() {
  const project = newProject('Navigation belongs to the canvas'), home = project.canvases[0]!;
  home.title = 'Source desk'; project.nodes[0]!.title = home.title;
  const evidence = addCanvas(project, 'Evidence canvas'), appendix = addCanvas(project, 'Nested appendix');
  const unrelated = addCanvas(project, 'Unrelated research'), orphan = addCanvas(project, 'Disconnected notes');
  for (const canvas of project.canvases) canvas.snapping = { stickyEdges: false, smartAlign: false, snapToGrid: false };
  const make = (canvasId: string, title: string, x: number, y: number, parent?: MeridianNode, kind: MeridianNode['kind'] = 'text') => {
    const node = addNode(project, canvasId, kind, title);
    Object.assign(node, { x, y, width: 300, height: 180, childLayout: 'spatial', placement: 'free' });
    if (parent) node.parentId = parent.id; return node;
  };
  const parent = make(home.id, 'Case file', 80, 90); parent.collapsed = true; parent.text = 'Inspect this closed branch without revealing its children.';
  const note = make(home.id, 'Private working note', 110, 340, parent); note.text = 'This hidden note belongs to the selected parent.';
  const direct = make(home.id, 'Direct evidence', 410, 100, parent, 'canvas-link'); direct.targetId = evidence.rootId;
  const nested = make(home.id, 'Nested section', 410, 350, parent); nested.collapsed = true;
  const nestedLaunch = make(home.id, 'Deeper appendix', 440, 590, nested, 'canvas-link'); nestedLaunch.targetId = appendix.rootId;
  const siblingLaunch = make(home.id, 'Independent research', 540, 90, undefined, 'canvas-link'); siblingLaunch.targetId = unrelated.rootId;
  const vault = make(evidence.id, 'Closed evidence shelf', 100, 170); vault.collapsed = true;
  const document = make(evidence.id, 'Complete transcript', 130, 440, vault, 'document');
  const fullText = 'An ordinary source line.\n'.repeat(420) + '\nLATE_ATTACHMENT_TOKEN: the final passage remains searchable in full.';
  const assetId = crypto.randomUUID(); document.assetId = assetId;
  project.assets.push({ id: assetId, name: 'Complete transcript.txt', mime: 'text/plain', base64: Buffer.from(fullText).toString('base64') });
  const back = make(evidence.id, 'Return to source desk', 560, 70, undefined, 'canvas-link'); back.targetId = home.rootId;
  make(orphan.id, 'Orphan detail', 100, 100).text = 'The disconnected canvas is still available through search.';
  return { project, home, evidence, appendix, unrelated, orphan, parent, note, direct, nested, nestedLaunch, siblingLaunch, vault, document, back };
}
async function read(request: APIRequestContext, id: string): Promise<MeridianProject> {
  const response = await request.get(`/api/projects/${id}`); expect(response.ok()).toBeTruthy(); return response.json();
}
async function seed(page: Page, request: APIRequestContext, project: MeridianProject) {
  const response = await request.post('/api/projects', { data: commitSnapshots(project) }); expect(response.ok()).toBeTruthy();
  const baseline = await response.json() as MeridianProject;
  await page.goto(`/?project=${baseline.id}&canvas=${baseline.entryCanvasId}`);
  await expect(page.locator('.save-state')).toHaveText(`Saved · r${baseline.revision}`); return baseline;
}

test('the canvas occupies the left edge and parent Relations open a hidden child canvas without disclosure edits', async ({ page, request }, info) => {
  const f = fixture(), baseline = await seed(page, request, f.project);
  await panel(page, 'canvas');
  await expect(page.locator('.workspace .sidebar, .workspace .canvas-list, .workspace .outline')).toHaveCount(0);
  await expect(search(page)).toBeVisible();
  const stage = await page.locator('.canvas-stage').boundingBox(), side = await inspector(page).boundingBox();
  expect(stage).not.toBeNull(); expect(side).not.toBeNull(); expect(stage!.x).toBeLessThanOrEqual(1); expect(stage!.width).toBeGreaterThan(1000);
  expect(side!.x).toBeGreaterThanOrEqual(stage!.x + stage!.width - 1);
  await page.getByRole('button', { name: 'Zoom in', exact: true }).click(); const homeCamera = await camera(page);
  await card(page, f.parent.id).locator('.ms-node-title').click(); await panel(page, 'relations');
  await expect(inspector(page).getByRole('button', { name: `Inspect descendant ${f.direct.title}`, exact: true })).toBeVisible();
  await expect(inspector(page).getByRole('button', { name: `Inspect descendant ${f.nestedLaunch.title}`, exact: true })).toBeVisible();
  await expect(inspector(page).getByRole('button', { name: `Inspect descendant ${f.siblingLaunch.title}`, exact: true })).toHaveCount(0);
  await expect(card(page, f.direct.id)).toHaveCount(0); await expect(card(page, f.parent.id)).toHaveAttribute('aria-expanded', 'false');
  await page.screenshot({ path: info.outputPath('canvas-parent-relations-wide.png') });
  await inspector(page).getByRole('button', { name: `Inspect descendant ${f.direct.title}`, exact: true }).click(); await panel(page, 'content');
  await expect(inspector(page).getByLabel('Title', { exact: true })).toHaveValue(f.direct.title);
  const direct = inspector(page).getByRole('button', { name: 'Open Work', exact: true });
  const origin=page.url(),windowCount=page.context().pages().length;await direct.click();
  await page.waitForURL(url=>url.searchParams.get('project')===f.project.id&&url.searchParams.get('canvas')===f.evidence.id);await expect(page.locator('.canvas-stage')).toBeVisible();
  const evidenceInitialCamera=await camera(page);
  await page.getByRole('button', { name: 'Zoom out', exact: true }).click(); const evidenceCamera = await camera(page);expect(evidenceCamera).not.toBe(evidenceInitialCamera);
  await page.goBack();await page.waitForURL(origin);expect(await camera(page)).toBe(homeCamera);
  await expect(card(page, f.parent.id)).toHaveAttribute('aria-expanded', 'false'); await expect(card(page, f.direct.id)).toHaveCount(0);
  await page.goForward();await page.waitForURL(url=>url.searchParams.get('canvas')===f.evidence.id);expect(await camera(page)).toBe(evidenceCamera);
  await card(page,f.back.id).getByRole('button',{name:`Open ${f.home.title}`,exact:true}).click();
  await page.waitForURL(url=>url.searchParams.get('project')===f.project.id&&url.searchParams.get('canvas')===f.home.id);await expect(page.locator('.canvas-stage')).toBeVisible();
  expect(await camera(page)).toBe(homeCamera);
  await expect(card(page, f.parent.id)).toHaveAttribute('aria-expanded', 'false'); await expect(card(page, f.direct.id)).toHaveCount(0);
  await page.goBack();await page.waitForURL(url=>url.searchParams.get('canvas')===f.evidence.id);expect(await camera(page)).toBe(evidenceCamera);
  await page.goForward();await page.waitForURL(url=>url.searchParams.get('canvas')===f.home.id);expect(await camera(page)).toBe(homeCamera);
  expect(page.context().pages()).toHaveLength(windowCount);
  expect(await read(request, f.project.id)).toEqual(baseline); await expect(page.getByRole('button', { name: 'Undo', exact: true })).toBeDisabled();
});

test('Canvas GUID search reveals a hidden document in one undo step while preserving its complete attachment', async ({ page, request }, info) => {
  const f = fixture(), baseline = await seed(page, request, f.project);
  await page.goto(`/?project=${f.project.id}&canvas=${f.evidence.id}`);
  await page.keyboard.press('Control+f'); await expect(search(page)).toBeFocused();
  await search(page).fill(f.document.id);
  const result = inspector(page).locator(`[data-outline-node="${f.document.id}"]`);
  await result.locator('[data-outline-more]').click(); await expect(result).toContainText(f.document.id); await expect(result).toContainText('Media');
  await expect(result).not.toContainText('LATE_ATTACHMENT_TOKEN');
  await expect(card(page, f.document.id)).toHaveCount(0); await result.getByRole('button', { name: `Jump to Node: ${f.document.title}`, exact: true }).click();
  expect(new URL(page.url()).searchParams.get('canvas')).toBe(f.evidence.id);
  await expect(card(page, f.document.id)).toBeVisible(); await expect(card(page, f.vault.id)).toHaveAttribute('aria-expanded', 'true');
  await page.getByRole('button', { name: 'Undo', exact: true }).click();
  await expect(card(page, f.document.id)).toHaveCount(0); await expect(page.getByRole('button', { name: 'Undo', exact: true })).toBeDisabled();
  await page.getByRole('button', { name: 'Redo', exact: true }).click(); await expect(card(page, f.document.id)).toBeVisible();
  await expect.poll(async () => (await read(request, f.project.id)).nodes.find(node => node.id === f.vault.id)?.collapsed).toBe(false);
  const saved = await read(request, f.project.id); expect(saved.assets).toEqual(baseline.assets);
  expect(saved.nodes.find(node => node.id === f.document.id)).toEqual(baseline.nodes.find(node => node.id === f.document.id));
  await page.screenshot({ path: info.outputPath('right-panel-guid-search.png') });
  await search(page).fill(''); await expect(page.locator('.outline,.search-result')).toHaveCount(0);
  await panel(page, 'content'); await expect(inspector(page).getByLabel('Title', { exact: true })).toHaveValue(f.document.title);
  await inspector(page).getByRole('button', { name: 'Open content file', exact: true }).click();
  const attachment = page.getByRole('dialog', { name: 'Complete transcript.txt', exact: true });
  await expect(attachment.locator('.document-text')).toContainText('LATE_ATTACHMENT_TOKEN'); await attachment.getByRole('button', { name: 'Close', exact: true }).click();
  await page.reload(); expect(new URL(page.url()).searchParams.get('canvas')).toBe(f.evidence.id); await expect(card(page, f.document.id)).toBeVisible();
});

test('Canvas search excludes other canvases and outline disclosure remains independent of document collapse', async ({ page, request }) => {
  const f = fixture(), baseline = await seed(page, request, f.project);
  await page.keyboard.press('Control+f'); await expect(search(page)).toBeFocused();
  await expect(inspector(page).getByRole('combobox', { name: 'Search scope', exact: true })).toHaveCount(0);
  for (const query of [f.document.id, f.orphan.title, 'LATE_ATTACHMENT_TOKEN']) { await search(page).fill(query); await expect(inspector(page).locator('.inspector-canvas-tree')).toContainText('No matching nodes on this canvas.'); }
  await search(page).fill('');
  await inspector(page).getByRole('button', { name: `Node: ${f.parent.title}`, exact: true }).click();
  await expect(inspector(page).getByRole('button', { name: `Node: ${f.note.title}`, exact: true })).toBeVisible();
  await expect(card(page, f.note.id)).toHaveCount(0);
  const parent = inspector(page).getByRole('button', { name: `Node: ${f.parent.title}`, exact: true }), note = inspector(page).getByRole('button', { name: `Node: ${f.note.title}`, exact: true });
  await parent.focus(); await page.keyboard.press('ArrowDown'); await expect(note).toBeFocused(); await page.keyboard.press('ArrowUp'); await expect(parent).toBeFocused();
  await search(page).fill(f.note.id); await inspector(page).locator(`[data-outline-node="${f.note.id}"] [data-outline-more]`).click(); await expect(inspector(page).locator(`[data-outline-node="${f.note.id}"]`)).toContainText(f.note.id);
  expect(await read(request, f.project.id)).toEqual(baseline); await expect(page.getByRole('button', { name: 'Undo', exact: true })).toBeDisabled();
});

test('compact panels open from selection and search, and child creation creates an ordinary Canvas launcher', async ({ page, request }, info) => {
  const f = fixture(); await page.setViewportSize({ width: 390, height: 844 }); const baseline = await seed(page, request, f.project);
  await expect(page.locator('.workspace .sidebar')).toHaveCount(0);
  if (await inspector(page).isVisible()) await inspector(page).getByRole('button', { name: 'Close side panel', exact: true }).click();
  await card(page, f.parent.id).locator('.ms-node-title').click(); await expect(inspector(page)).toBeVisible();
  await inspector(page).getByRole('button', { name: 'Close side panel', exact: true }).click(); await expect(inspector(page)).toBeHidden();
  await page.keyboard.press('Control+f'); await expect(inspector(page)).toBeVisible(); await expect(search(page)).toBeFocused();
  await search(page).fill(f.parent.id);
  const side = await inspector(page).boundingBox(); expect(side).not.toBeNull(); expect(side!.x).toBeGreaterThanOrEqual(0); expect(side!.x + side!.width).toBeLessThanOrEqual(391);
  expect(await inspector(page).evaluate(element => element.scrollWidth > element.clientWidth + 1)).toBe(false);
  await page.screenshot({ path: info.outputPath('right-panel-compact.png') });
  await inspector(page).getByRole('button', { name: `Jump to Node: ${f.parent.title}`, exact: true }).click(); await panel(page, 'relations');
  const address=page.url();await inspector(page).getByRole('button', { name: 'Create child node', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'New Node', exact: true }); await expect(dialog).toBeVisible();
  await dialog.getByLabel('Type',{exact:true}).selectOption('canvas');await dialog.getByLabel('Title', { exact: true }).fill('A child canvas from Relations'); await dialog.getByRole('button', { name: 'Create node', exact: true }).click();
  await expect(dialog).toHaveCount(0);
  await expect.poll(async () => (await read(request, f.project.id)).canvases.length).toBe(baseline.canvases.length + 1);
  const saved = await read(request, f.project.id), created = saved.canvases.find(canvas => canvas.title === 'A child canvas from Relations')!;
  expect(created).toBeDefined(); expect(page.url()).toBe(address);
  const launcher = saved.nodes.find(node => node.kind === 'canvas-link' && node.targetId === created.rootId)!;
  expect(launcher.parentId).toBe(f.parent.id); expect(launcher.canvasId).toBe(f.home.id);
  expect(saved.assets).toEqual(baseline.assets); for (const before of baseline.nodes) expect(saved.nodes.some(node => node.id === before.id)).toBe(true);
});
