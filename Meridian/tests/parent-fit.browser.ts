import { test, expect, type APIRequestContext, type Page } from '@playwright/test';
import { readFile, writeFile } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';
import { addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import type { ContainerVariant, MeridianNode, MeridianProject } from '../src/core/types.ts';
import { buildPublication } from '../src/publication.ts';
import { interactionTool, inspectorPanel, selectEditorNode } from './ui-helpers.ts';
import { DEFAULT_CHILD_INSET } from '../src/core/spacing.ts';

const tile = (page: Page, id: string) => page.locator(`.ms-node[data-node-id="${id}"]`);
const stored = async (request: APIRequestContext, id: string): Promise<MeridianProject> => (await request.get(`/api/projects/${id}`)).json();
const nodeIn = (project: MeridianProject, id: string) => project.nodes.find(node => node.id === id)!;
async function install(page: Page, request: APIRequestContext, project: MeridianProject) {
  const response = await request.post('/api/projects', { data: commitSnapshots(project) });
  expect(response.ok(), await response.text()).toBeTruthy();
  const saved: MeridianProject = await response.json();
  await page.goto(`/?project=${saved.id}`); await expect(page.locator('.save-state')).toHaveText(`Saved · r${saved.revision}`);
  await interactionTool(page); return saved;
}
async function dimensions(page: Page, id: string) {
  return tile(page, id).evaluate(element => {
    const style = (element as HTMLElement).style;
    return { x: parseFloat(style.left), y: parseFloat(style.top), width: parseFloat(style.width), height: parseFloat(style.height) };
  });
}
async function insideWall(page: Page, parent: MeridianNode, children: MeridianNode[]) {
  // Query and measure the entire visible composition in one JavaScript turn.
  // A locator's retained handle can detach during a commit/autosave repaint.
  const { box, geometry, header, measured } = await page.evaluate(({ parentId, childIds }) => {
    const card = document.querySelector<HTMLElement>(`.ms-node[data-node-id="${parentId}"]`)!;
    const coordinates = (element: Element) => { const box = element.getBoundingClientRect(); return { x: box.x, y: box.y, width: box.width, height: box.height }; };
    return { box: coordinates(card), geometry: { width: parseFloat(card.style.width) }, header: coordinates(card.querySelector(':scope > .ms-node-header')!),
      measured: childIds.map(id => coordinates(document.querySelector(`.ms-node[data-node-id="${id}"]`)!)) };
  }, { parentId: parent.id, childIds: children.map(child => child.id) });
  expect(box.width, 'The measured parent is attached and visible').toBeGreaterThan(0);
  expect(box.height, 'The measured parent is attached and visible').toBeGreaterThan(0);
  const scale = box.width / geometry.width, inset = ((parent.style?.childPadding ?? DEFAULT_CHILD_INSET) + (parent.style?.borderWidth ?? 1)) * scale;
  for (const [index, child] of children.entries()) {
    const inner = measured[index]!;
    expect(inner.x, `${child.title}: left wall`).toBeGreaterThanOrEqual(box.x + inset - 1.5);
    expect(inner.y, `${child.title}: below parent title`).toBeGreaterThanOrEqual(header.y + header.height - 1.5);
    expect(inner.x + inner.width, `${child.title}: right wall`).toBeLessThanOrEqual(box.x + box.width - inset + 1.5);
    expect(inner.y + inner.height, `${child.title}: bottom wall`).toBeLessThanOrEqual(box.y + box.height - inset + 1.5);
  }
  for (let at = 0; at < measured.length; at++) for (const other of measured.slice(at + 1)) {
    const inner = measured[at]!;
    expect(inner.x + inner.width <= other.x + 1.5 || other.x + other.width <= inner.x + 1.5 || inner.y + inner.height <= other.y + 1.5 || other.y + other.height <= inner.y + 1.5,
      'Fitted siblings do not overlap').toBe(true);
  }
}
function fixture(pattern: ContainerVariant, count = 4) {
  const project = newProject(`Parent wall · ${pattern}`), canvas = project.canvases[0]!;
  canvas.snapping = { stickyEdges: false, smartAlign: false, snapToGrid: false };
  const parent = addNode(project, canvas.id, 'container', 'Collection');
  Object.assign(parent, { x: 100, y: 100, width: 360, height: 260, sizeMode: 'fixed', collapsed: true, containerVariant: pattern, childLayout: 'contained',
    style: { shape: 'rectangle', childPadding: 9, borderWidth: 2 }, grid: { columns: 2, rows: 2, cellWidth: 440, cellHeight: 180, gap: 0, showHeaders: false, fillOrder: 'row', windowColumns: 2, windowRows: 1 } });
  const children = Array.from({ length: count }, (_, at) => {
    const child = addNode(project, canvas.id, 'media', `Item ${at + 1}`);
    Object.assign(child, { parentId: parent.id, x: -100 + at * 480, y: 470, width: 440, height: 180, sizeMode: 'fixed', text: `Object ${at + 1}`, childPosition: 'auto' });
    return child;
  });
  return { project, parent, children };
}
async function expand(page: Page, parent: MeridianNode) {
  await selectEditorNode(page, parent.id, parent.title); await inspectorPanel(page, 'relations');
  await page.getByRole('button', { name: 'Toggle contents from relations', exact: true }).click();
}

test('fixed ordered parent bounds take precedence over oversized children on expansion and reopening', async ({ page, request }, info) => {
  for (const pattern of ['horizontal', 'vertical', 'grid', 'spreadsheet'] as const) {
    const f = fixture(pattern), initialIds = f.project.nodes.map(node => node.id).sort();
    const saved = await install(page, request, f.project); await expand(page, f.parent);
    await expect(tile(page, f.children[0]!.id)).toHaveCount(1);
    const bounds = await dimensions(page, f.parent.id);
    expect(bounds.width).toBe(f.parent.width); expect(bounds.height).toBe(f.parent.height);
    await insideWall(page, f.parent, f.children);
    const sizes = await Promise.all(f.children.map(child => dimensions(page, child.id)));
    for (const size of sizes.slice(1)) { expect(size.width).toBeCloseTo(sizes[0]!.width, 2); expect(size.height).toBeCloseTo(sizes[0]!.height, 2); }
    expect(sizes[0]!.width).toBeLessThan(f.children[0]!.width);
    await expect.poll(async () => nodeIn(await stored(request, saved.id), f.parent.id).collapsed).toBe(false);
    const persisted = await stored(request, saved.id);
    expect(nodeIn(persisted, f.parent.id).width).toBe(f.parent.width); expect(nodeIn(persisted, f.parent.id).height).toBe(f.parent.height);
    expect(persisted.nodes.map(node => node.id).sort()).toEqual(initialIds);
    expect(persisted.nodes.filter(node => node.parentId === f.parent.id).map(node => node.text)).toEqual(f.children.map(node => node.text));
    await page.reload(); expect(await dimensions(page, f.parent.id)).toEqual(bounds); await insideWall(page, f.parent, f.children);
    if (pattern === 'grid') await page.screenshot({ path: info.outputPath('fixed-parent-fitted-grid.png') });
  }
});

test('numeric dimensions and dragging all child edges stop at the expanded Freeform parent wall', async ({ page, request }, info) => {
  const f = fixture('contained', 1); Object.assign(f.parent, { collapsed: false, width: 430, height: 330 });
  const child = f.children[0]!; Object.assign(child, { x: 160, y: 175, width: 150, height: 90, childPosition: 'manual' });
  const saved = await install(page, request, f.project); await selectEditorNode(page, child.id, child.title); await inspectorPanel(page, 'appearance');
  for (const label of ['Width', 'Height']) {
    const field = page.locator('.inspector').getByLabel(label, { exact: true }); await field.fill('999'); await field.press('Tab');
    await insideWall(page, f.parent, [child]);
  }
  await expect.poll(async () => nodeIn(await stored(request, saved.id), child.id).sizeMode).toBe('fixed');
  expect(nodeIn(await stored(request, saved.id), f.parent.id).width).toBe(f.parent.width);
  expect(nodeIn(await stored(request, saved.id), f.parent.id).height).toBe(f.parent.height);
  // Shrink via the inspector so each drag has expansion room before hitting its wall.
  for (const [label, value] of [['Width', '130'], ['Height', '85']]) {
    const field = page.locator('.inspector').getByLabel(label, { exact: true }); await field.fill(value); await field.press('Tab');
  }
  await page.getByRole('button', { name: 'Fit selected branch', exact: true }).click();
  // Fit now supports deep detail zoom. Keep the whole parent wall onscreen for
  // these four long drags instead of aiming through an offscreen ancestor.
  while (parseFloat((await page.locator('.ms-zoom').textContent())!) > 150)
    await page.getByRole('button', { name: 'Zoom out', exact: true }).click();
  for (const side of ['right', 'bottom', 'left', 'top']) {
    const beforeGeometry = await dimensions(page, child.id);
    const before = (await tile(page, child.id).boundingBox())!, wall = (await tile(page, f.parent.id).boundingBox())!;
    const point = { x: side === 'left' ? before.x + 3 : side === 'right' ? before.x + before.width - 3 : before.x + before.width / 2,
      y: side === 'top' ? before.y + 3 : side === 'bottom' ? before.y + before.height - 3 : before.y + before.height / 2 };
    await page.mouse.move(point.x, point.y); await page.mouse.down();
    const destination = { x: side === 'left' ? wall.x - 80 : side === 'right' ? wall.x + wall.width + 80 : point.x,
      y: side === 'top' ? wall.y - 80 : side === 'bottom' ? wall.y + wall.height + 80 : point.y };
    await page.mouse.move(destination.x, destination.y, { steps: 12 }); await insideWall(page, f.parent, [child]);
    await page.mouse.up(); await insideWall(page, f.parent, [child]);
    const afterGeometry = await dimensions(page, child.id);
    expect(side === 'left' || side === 'right' ? afterGeometry.width : afterGeometry.height, `${side} drag actually resizes the child`).toBeGreaterThan(
      (side === 'left' || side === 'right' ? beforeGeometry.width : beforeGeometry.height) + .5);
    await expect.poll(async () => nodeIn(await stored(request, saved.id), child.id).width).toBeCloseTo(afterGeometry.width, 2);
    await expect.poll(async () => nodeIn(await stored(request, saved.id), child.id).height).toBeCloseTo(afterGeometry.height, 2);
  }
  await expect(page.locator('.save-state')).toHaveText(/^Saved · r\d+$/); const current = await stored(request, saved.id);
  expect(nodeIn(current, child.id).parentId).toBe(f.parent.id); expect(nodeIn(current, f.parent.id).width).toBe(f.parent.width);
  await page.screenshot({ path: info.outputPath('manual-child-wall.png') }); await page.reload(); await insideWall(page, f.parent, [child]);
});

test('an autosave response during a captured resize preserves the preview and commits the released dimensions', async ({ page, request }, info) => {
  const f = fixture('contained', 1); Object.assign(f.parent, { collapsed: false, width: 430, height: 330 });
  const child = f.children[0]!; Object.assign(child, { x: 160, y: 175, width: 130, height: 85, childPosition: 'manual' });
  const saved = await install(page, request, f.project); await selectEditorNode(page, child.id, child.title); await inspectorPanel(page, 'appearance');
  await page.getByRole('button', { name: 'Fit selected branch', exact: true }).click();
  while (parseFloat((await page.locator('.ms-zoom').textContent())!) > 150)
    await page.getByRole('button', { name: 'Zoom out', exact: true }).click();
  let beginSave!: () => void, releaseSave!: () => void, finishSave!: () => void;
  const savingStarted = new Promise<void>(resolve => { beginSave = resolve; });
  const heldResponse = new Promise<void>(resolve => { releaseSave = resolve; });
  const savingFinished = new Promise<void>(resolve => { finishSave = resolve; });
  let intercepted = false;
  await page.route(`**/api/projects/${saved.id}`, async route => {
    if (route.request().method() !== 'PUT' || intercepted) { await route.continue(); return; }
    intercepted = true;
    const response = await route.fetch(); expect(response.ok()).toBeTruthy();
    beginSave(); await heldResponse; await route.fulfill({ response }); finishSave();
  });
  try {
    // Finish the request on the host, holding only its response so it can arrive
    // while the next gesture is in progress rather than relying on save timing.
    const width = page.locator('.inspector').getByLabel('Width', { exact: true });
    await width.fill('132'); await width.press('Tab'); await savingStarted;
    await expect(page.locator('.save-state')).toHaveText('Saving…');
    const before = await dimensions(page, child.id), box = (await tile(page, child.id).boundingBox())!;
    const point = { x: box.x + box.width - 3, y: box.y + box.height / 2 };
    await page.evaluate(childId => {
      const state = window as any; state.resizeCard = document.querySelector(`.ms-node[data-node-id="${childId}"]`);
      document.addEventListener('pointerdown', event => { state.resizePointerId = (event as PointerEvent).pointerId; }, { once: true, capture: true });
    }, child.id);
    await page.mouse.move(point.x, point.y); await page.mouse.down(); await page.mouse.move(point.x + 95, point.y, { steps: 6 });
    const preview = await dimensions(page, child.id); expect(preview.width).toBeGreaterThan(before.width + 30);
    const capturedCard = () => page.evaluate(childId => {
      const state = window as any, card = state.resizeCard as HTMLElement;
      return { same: card === document.querySelector(`.ms-node[data-node-id="${childId}"]`), connected: card.isConnected,
        captured: card.hasPointerCapture(state.resizePointerId), width: parseFloat(card.style.width), height: parseFloat(card.style.height) };
    }, child.id);
    expect(await capturedCard()).toMatchObject({ same: true, connected: true, captured: true, width: preview.width, height: preview.height });
    releaseSave(); await savingFinished; await expect(page.locator('.save-state')).toHaveText(/^Saved · r\d+$/);
    expect(await capturedCard()).toMatchObject({ same: true, connected: true, captured: true, width: preview.width, height: preview.height });
    await insideWall(page, f.parent, [child]); await page.mouse.up();
    await expect.poll(async () => nodeIn(await stored(request, saved.id), child.id).width).toBeCloseTo(preview.width, 2);
    await expect.poll(async () => nodeIn(await stored(request, saved.id), child.id).height).toBeCloseTo(preview.height, 2);
    await expect(page.locator('.save-state')).toHaveText(/^Saved · r\d+$/); await insideWall(page, f.parent, [child]);
    expect(nodeIn(await stored(request, saved.id), f.parent.id).width).toBe(f.parent.width);
    await page.screenshot({ path: info.outputPath('autosave-captured-resize.png') });
  } finally { releaseSave(); await page.mouse.up(); }
});

test('fixed non-square dimensions with a square backdrop keep previously escaped children inside the actual square edges', async ({ page, request }, info) => {
  const f = fixture('contained', 4); Object.assign(f.parent, { width: 756, height: 502, collapsed: true, style: { shape: 'square', childPadding: 5, borderWidth: 2 } });
  for (const [at, child] of f.children.entries()) Object.assign(child, { x: 30 + at % 2 * 440, y: 300 + Math.floor(at / 2) * 180, width: 440, height: 120, childPosition: 'manual' });
  const saved = await install(page, request, f.project); await expand(page, f.parent); await insideWall(page, f.parent, f.children);
  const bounds = await dimensions(page, f.parent.id), frame = (await tile(page, f.parent.id).boundingBox())!, scale = frame.width / bounds.width;
  const side = Math.min(bounds.width, bounds.height), square = { x: frame.x + (bounds.width - side) / 2 * scale, y: frame.y + (bounds.height - side) / 2 * scale, side: side * scale };
  const inset = 7 * scale;
  for (const child of f.children) {
    const box = (await tile(page, child.id).boundingBox())!;
    expect(box.x).toBeGreaterThanOrEqual(square.x + inset - 1.5); expect(box.x + box.width).toBeLessThanOrEqual(square.x + square.side - inset + 1.5);
    expect(box.y).toBeGreaterThanOrEqual(square.y + inset - 1.5); expect(box.y + box.height).toBeLessThanOrEqual(square.y + square.side - inset + 1.5);
  }
  await expect.poll(async () => nodeIn(await stored(request, saved.id), f.parent.id).collapsed).toBe(false);
  expect(nodeIn(await stored(request, saved.id), f.parent.id).width).toBe(756); expect(nodeIn(await stored(request, saved.id), f.parent.id).height).toBe(502);
  await page.screenshot({ path: info.outputPath('square-parent-actual-walls.png') });
});

test('nested fixed containers fit recursively without changing graph identities and preserve shared renderer geometry offline', async ({ page, request, browser }, info) => {
  const f = fixture('contained', 1); Object.assign(f.parent, { width: 440, height: 350 });
  const nested = f.children[0]!; Object.assign(nested, { kind: 'container', title: 'Nested', width: 720, height: 520, containerVariant: 'grid', collapsed: false, childLayout: 'contained',
    grid: { columns: 2, rows: 2, cellWidth: 540, cellHeight: 250, gap: 0, showHeaders: false, fillOrder: 'row' } });
  const leaves = Array.from({ length: 4 }, (_, at) => {
    const leaf = addNode(f.project, f.project.entryCanvasId, 'media', `Detail ${at + 1}`);
    Object.assign(leaf, { parentId: nested.id, width: 540, height: 250, sizeMode: 'fixed', text: `Nested content ${at + 1}`, childPosition: 'auto' }); return leaf;
  });
  const saved = await install(page, request, f.project); await expand(page, f.parent);
  await insideWall(page, f.parent, [nested]); await insideWall(page, nested, leaves);
  expect((await dimensions(page, f.parent.id)).width).toBe(f.parent.width); expect((await dimensions(page, f.parent.id)).height).toBe(f.parent.height);
  await expect.poll(async () => nodeIn(await stored(request, saved.id), f.parent.id).collapsed).toBe(false);
  const persisted = await stored(request, saved.id), expected = new Map(await Promise.all([f.parent, nested, ...leaves].map(async node => [node.id, await dimensions(page, node.id)] as const)));
  expect(persisted.nodes.map(node => node.id).sort()).toEqual(f.project.nodes.map(node => node.id).sort());
  const offlinePath = info.outputPath('parent-fit-offline.html');
  await writeFile(offlinePath, buildPublication(persisted, await readFile('dist/viewer.js', 'utf8'), await readFile('dist/scene.css', 'utf8')));
  const context = await browser.newContext({ offline: true, viewport: { width: 1500, height: 950 } });
  try {
    const viewer = await context.newPage(), remoteRequests: string[] = [];
    viewer.on('request', request => { if (/^https?:/.test(request.url())) remoteRequests.push(request.url()); });
    await viewer.goto(pathToFileURL(offlinePath).href); const payload = await viewer.locator('#meridian-data').textContent();
    await insideWall(viewer, f.parent, [nested]); await insideWall(viewer, nested, leaves);
    for (const [id, geometry] of expected) expect(await dimensions(viewer, id)).toEqual(geometry);
    expect(await viewer.locator('#meridian-data').textContent()).toBe(payload); expect(remoteRequests).toEqual([]);
    await viewer.screenshot({ path: info.outputPath('nested-parent-fit-offline.png') });
  } finally { await context.close(); }
});

test('parent resize refits existing ordered children while Unbounded Media retains exterior disclosure', async ({ page, request }) => {
  const f = fixture('grid'), saved = await install(page, request, f.project); await expand(page, f.parent);
  await inspectorPanel(page, 'appearance');
  for (const [label, value] of [['Width', '240'], ['Height', '200']]) { const field = page.locator('.inspector').getByLabel(label, { exact: true }); await field.fill(value); await field.press('Tab'); }
  await expect.poll(async () => nodeIn(await stored(request, saved.id), f.parent.id).width).toBe(240);
  await expect.poll(async () => nodeIn(await stored(request, saved.id), f.parent.id).height).toBe(200);
  const resized = { ...f.parent, width: 240, height: 200 }; await insideWall(page, resized, f.children);
  await inspectorPanel(page, 'properties'); await page.getByRole('combobox', { name: 'Type', exact: true }).selectOption('media');
  await expect.poll(async () => nodeIn(await stored(request, saved.id), f.parent.id).kind).toBe('media');
  const parentBox = (await tile(page, f.parent.id).boundingBox())!;
  for (const child of f.children) {
    const childBox = (await tile(page, child.id).boundingBox())!;
    expect(childBox.y).toBeGreaterThanOrEqual(parentBox.y + parentBox.height - 1.5);
  }
  expect((await dimensions(page, f.parent.id)).width).toBe(240); expect((await dimensions(page, f.parent.id)).height).toBe(200);
});

test('an expanded Media Window or Gallery keeps its exterior browsing surface inside a bounded Freeform ancestor', async ({ page, request }, info) => {
  for (const pattern of ['window', 'gallery'] as const) {
    const f = fixture('contained', 1); Object.assign(f.parent, { width: 620, height: 420, collapsed: true });
    const media = f.children[0]!; Object.assign(media, { title: `Media ${pattern}`, width: 220, height: 100, collapsed: false,
      containerVariant: pattern, childLayout: 'spatial', childPosition: 'auto', text: 'Unbounded cover remains visible',
      grid: { columns: 4, rows: 4, cellWidth: 280, cellHeight: 180, gap: 0, showHeaders: false, fillOrder: 'row', windowColumns: 3, windowRows: 2, scrollDirection: 'vertical' } });
    const objects = Array.from({ length: pattern === 'window' ? 16 : 2 }, (_, at) => {
      const object = addNode(f.project, f.project.entryCanvasId, 'media', `Object ${at + 1}`);
      Object.assign(object, { parentId: media.id, width: 600, height: 340, sizeMode: 'fixed', text: `Object ${at + 1} content`, childPosition: 'auto' });
      return object;
    });
    const saved = await install(page, request, f.project); await expand(page, f.parent); await insideWall(page, f.parent, [media]);
    const assertSurface = async (selector: string) => {
      const frame = (await tile(page, f.parent.id).boundingBox())!, geometry = await dimensions(page, f.parent.id), scale = frame.width / geometry.width;
      const inset = 11 * scale, surface = (await page.locator(selector).boundingBox())!;
      expect(surface.x).toBeGreaterThanOrEqual(frame.x + inset - 1.5); expect(surface.y).toBeGreaterThanOrEqual(frame.y + inset - 1.5);
      expect(surface.x + surface.width).toBeLessThanOrEqual(frame.x + frame.width - inset + 1.5);
      expect(surface.y + surface.height).toBeLessThanOrEqual(frame.y + frame.height - inset + 1.5);
    };
    if (pattern === 'window') {
      const selector = `[data-external-window="${media.id}"]`, viewport = page.locator(selector); await expect(viewport).toBeVisible();
      await assertSurface(selector); const camera = await page.locator('.ms-world').evaluate(element => (element as HTMLElement).style.transform);
      await viewport.evaluate(element => { element.scrollTop = element.scrollHeight; });
      await expect.poll(() => viewport.evaluate(element => element.scrollTop)).toBeGreaterThan(0); await assertSurface(selector);
      expect(await page.locator('.ms-world').evaluate(element => (element as HTMLElement).style.transform)).toBe(camera);
    } else {
      const selector = `[data-external-gallery="${media.id}"]`; await expect(tile(page, objects[0]!.id)).toBeVisible();
      await insideWall(page, f.parent, [media, objects[0]!]); await assertSurface(selector);
      const next = page.getByRole('button', { name: `Next object in ${media.title}`, exact: true }); await expect(next).toBeEnabled();
      await next.click(); await expect(tile(page, objects[1]!.id)).toBeVisible(); await expect(tile(page, objects[0]!.id)).toHaveCount(0);
      await insideWall(page, f.parent, [media, objects[1]!]); await assertSurface(selector);
      await page.getByRole('button', { name: `Previous object in ${media.title}`, exact: true }).click();
      await expect(tile(page, objects[0]!.id)).toBeVisible(); await assertSurface(selector);
    }
    await expect.poll(async () => nodeIn(await stored(request, saved.id), f.parent.id).collapsed).toBe(false);
    const persisted = await stored(request, saved.id);
    expect(persisted.nodes.map(node => node.id).sort()).toEqual(f.project.nodes.map(node => node.id).sort());
    for (const object of [media, ...objects]) { expect(nodeIn(persisted, object.id).text).toBe(object.text); expect(nodeIn(persisted, object.id).parentId).toBe(object.parentId); }
    expect(nodeIn(persisted, f.parent.id).width).toBe(620); expect(nodeIn(persisted, f.parent.id).height).toBe(420);
    await page.screenshot({ path: info.outputPath(`nested-media-${pattern}-surface-fit.png`) });
  }
});
