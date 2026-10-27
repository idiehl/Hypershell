import { test, expect, type APIRequestContext, type Page } from '@playwright/test';
import { readFile, writeFile } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';
import { addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import { reflowContainedChildren } from '../src/core/grid.ts';
import { buildPublication } from '../src/publication.ts';
import type { MeridianLink, MeridianProject } from '../src/core/types.ts';
import { inspectorPanel } from './ui-helpers.ts';

const tile = (page: Page, id: string) => page.locator(`.ms-node[data-node-id="${id}"]`);
const renderedLink = (page: Page, id: string) => page.locator(`.ms-viewport [data-link-id="${id}"]`);
const stored = async (request: APIRequestContext, id: string): Promise<MeridianProject> => (await request.get(`/api/projects/${id}`)).json();
function fixture(variant: 'contained' | 'window') {
  let project = newProject(`Embedded ${variant} relationships`); const canvasId = project.entryCanvasId;
  const parent = addNode(project, canvasId, 'container', 'Collection');
  Object.assign(parent, { x: 80, y: 100, width: 550, height: 330, childLayout: 'contained', containerVariant: variant, collapsed: false });
  if (variant === 'window') parent.grid = { columns: 2, rows: 2, cellWidth: 200, cellHeight: 130, windowColumns: 2, windowRows: 1, scrollDirection: 'vertical', gap: 0, fillOrder: 'row' };
  const children = Array.from({ length: 4 }, (_, at) => {
    const child = addNode(project, canvasId, 'text', `Object ${at + 1}`);
    Object.assign(child, { parentId: parent.id, x: 100 + at % 2 * 240, y: 160 + Math.floor(at / 2) * 110, text: `Object ${at + 1} content`, cell: { column: at % 2 + 1, row: Math.floor(at / 2) + 1 } });
    return child;
  });
  const nested = variant === 'contained' ? addNode(project, canvasId, 'container', 'Nested collection') : undefined;
  const descendant = nested ? addNode(project, canvasId, 'text', 'Nested detail') : undefined;
  if (nested && descendant) { Object.assign(nested, { parentId: parent.id, x: 100, y: 410, width: 240, height: 160 }); Object.assign(descendant, { parentId: nested.id, x: 110, y: 455, text: 'An embedded grandchild.' }); }
  const outside = addNode(project, canvasId, 'text', 'External object'); Object.assign(outside, { x: 820, y: 130, text: 'Its cross-boundary links stay visible.' });
  const spatial = addNode(project, canvasId, 'text', 'Spatial parent'); Object.assign(spatial, { x: 100, y: 760, text: 'This relationship still uses a line.', childLayout: 'spatial' });
  const spatialChild = addNode(project, canvasId, 'text', 'Spatial child'); Object.assign(spatialChild, { parentId: spatial.id, x: 530, y: 760, placement: 'free' });
  const embedded: MeridianLink[] = [], visible: MeridianLink[] = [];
  for (const [at, relation] of (['causal', 'chronological', 'mutual', 'cyclical', 'generic'] as const).entries()) embedded.push({ id: `embedded-${relation}`, canvasId, sourceId: at % 2 ? children[0]!.id : parent.id, targetId: at % 2 ? parent.id : children[0]!.id, kind: 'visual', relation, label: `Contained ${relation}`, direction: at % 2 ? 'reverse' : 'both', points: [{ x: 380, y: 105 }], sourceAnchor: { x: 0.5, y: 0 }, targetAnchor: { x: 0, y: 0.5 }, style: { color: '#88aacc', width: 2 } });
  if (descendant) embedded.push({ id: 'embedded-descendant', canvasId, sourceId: parent.id, targetId: descendant.id, kind: 'visual', relation: 'causal', label: 'Ancestor relationship', points: [{ x: 500, y: 420 }] });
  visible.push(
    { id: 'sibling-relationship', canvasId, sourceId: children[0]!.id, targetId: children[1]!.id, kind: 'visual', relation: 'mutual', label: 'Siblings still connect' },
    { id: 'external-relationship', canvasId, sourceId: parent.id, targetId: outside.id, kind: 'visual', relation: 'causal', label: 'Cross-boundary relationship' },
    { id: 'spatial-relationship', canvasId, sourceId: spatial.id, targetId: spatialChild.id, kind: 'visual', relation: 'causal', label: 'Spatial relationship' },
    { id: 'canvas-root-relationship', canvasId, sourceId: project.canvases[0]!.rootId, targetId: spatial.id, kind: 'visual', relation: 'generic', label: 'Canvas relationship' },
  );
  project.links.push(...embedded, ...visible);
  project = reflowContainedChildren(project, parent.id);
  return { project, parent, children, embedded, visible };
}
async function assertPresentation(page: Page, embedded: MeridianLink[], visible: MeridianLink[]) {
  // This includes line shafts, wide hit paths, marker endpoint hit targets and labels.
  for (const link of embedded) await expect(renderedLink(page, link.id)).toHaveCount(0);
  for (const link of visible) await expect(page.locator(`.ms-link-line[data-link-id="${link.id}"]`)).toHaveCount(1);
  await expect(page.locator('.ms-route-handle')).toHaveCount(0);
}

for (const variant of ['contained', 'window'] as const) test(`${variant} embedding omits complete link visuals in editor and offline viewer while metadata remains editable and saved`, async ({ page, request, browser }, info) => {
  test.setTimeout(90000); await page.setViewportSize({ width: 1900, height: 1100 });
  const f = fixture(variant), response = await request.post('/api/projects', { data: commitSnapshots(f.project) });
  expect(response.ok(), await response.text()).toBeTruthy(); const original: MeridianProject = await response.json(), errors: string[] = [];
  page.on('pageerror', error => errors.push(error.message)); await page.goto(`/?project=${original.id}`); await page.getByRole('button', { name: 'Fit Work', exact: true }).click();
  await assertPresentation(page, f.embedded, f.visible);
  await tile(page, f.parent.id).locator('> .ms-node-header .ms-node-title').click(); await inspectorPanel(page, 'links');
  for (const link of f.embedded) await expect(page.locator(`[data-associated-link="${link.id}"]`)).toBeVisible();
  const edited = f.embedded[0]!, row = page.locator(`[data-associated-link="${edited.id}"]`);
  await row.locator('.inspector-link-title').click();
  await expect(page.getByLabel('Connection label', { exact: true })).toHaveValue(edited.label!);
  await expect(page.getByLabel('Relationship', { exact: true })).toHaveValue(edited.relation);
  await assertPresentation(page, f.embedded, f.visible);
  const label = 'Edited conceptual relationship'; await page.getByLabel('Connection label', { exact: true }).fill(label); await page.getByLabel('Connection label', { exact: true }).press('Tab');
  await expect.poll(async () => (await stored(request, original.id)).links.find(link => link.id === edited.id)?.label).toBe(label);
  const saved = await stored(request, original.id), expectedLinks = structuredClone(original.links); expectedLinks.find(link => link.id === edited.id)!.label = label;
  expect(saved.links).toEqual(expectedLinks); expect(saved.nodes.map(node => ({ id: node.id, parentId: node.parentId }))).toEqual(original.nodes.map(node => ({ id: node.id, parentId: node.parentId })));
  await page.reload(); await expect(page.locator('.save-state')).toHaveText(`Saved · r${saved.revision}`); await assertPresentation(page, f.embedded, f.visible);
  expect((await stored(request, original.id)).links).toEqual(expectedLinks);
  // Repeated disclosure must never reintroduce the stored parent-child shafts or markers.
  await tile(page, f.parent.id).press('Alt+ArrowLeft'); for (const link of f.embedded) await expect(renderedLink(page, link.id)).toHaveCount(0);
  await tile(page, f.parent.id).press('Alt+ArrowRight'); await assertPresentation(page, f.embedded, f.visible);
  const published = info.outputPath(`embedded-${variant}.html`); await writeFile(published, buildPublication(saved, await readFile('dist/viewer.js', 'utf8'), await readFile('dist/scene.css', 'utf8')));
  const context = await browser.newContext({ offline: true, viewport: { width: 1900, height: 1100 } });
  try {
    const viewer = await context.newPage(), remoteRequests: string[] = []; viewer.on('pageerror', error => errors.push(error.message)); viewer.on('request', item => { if (/^https?:/.test(item.url())) remoteRequests.push(item.url()); });
    await viewer.goto(pathToFileURL(published).href); await assertPresentation(viewer, f.embedded, f.visible);
    const payloadBefore = await viewer.locator('#meridian-data').textContent();
    expect(JSON.parse(payloadBefore!).project.links).toEqual(expectedLinks);
    await tile(viewer, f.parent.id).locator('> .ms-node-header .ms-node-title').click();
    await viewer.getByRole('button', { name: `Select connection ${label}`, exact: true }).click();
    await expect(viewer.locator('.mv-node-context')).toContainText(label); await assertPresentation(viewer, f.embedded, f.visible);
    await viewer.getByRole('button', { name: `Inspect containing node ${f.parent.title}`, exact: true }).click();
    await viewer.getByRole('button', { name: 'Close project navigation', exact: true }).click();
    await tile(viewer, f.parent.id).press('Alt+ArrowLeft'); await tile(viewer, f.parent.id).press('Alt+ArrowRight');
    await assertPresentation(viewer, f.embedded, f.visible); expect(await viewer.locator('#meridian-data').textContent()).toBe(payloadBefore); expect(remoteRequests).toEqual([]);
    await viewer.screenshot({ path: info.outputPath(`embedded-${variant}-offline.png`), fullPage: true });
  } finally { await context.close(); }
  expect(errors).toEqual([]);
});
