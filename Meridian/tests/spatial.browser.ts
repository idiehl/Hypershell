import { workspaceAction } from './ui-helpers.ts';
import { test, expect, type Page } from '@playwright/test';
import { readFile, writeFile } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';
import { addCanvas, addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import { buildPublication, readPublication } from '../src/publication.ts';
import type { LinkDirection, LinkRelation, MeridianProject } from '../src/core/types.ts';

const card = (page: Page, id: string) => page.locator(`.ms-node[data-node-id="${id}"]`);
const title = (page: Page, id: string) => card(page, id).locator('.ms-node-title');
const camera = (page: Page) => page.locator('.ms-world').evaluate(node => (node as HTMLElement).style.transform);
const tile = (page: Page, id: string) => card(page, id).evaluate(node => {
  const style = (node as HTMLElement).style;
  return { x: parseFloat(style.left), y: parseFloat(style.top), width: parseFloat(style.width), height: parseFloat(style.height) };
});
type Box = Awaited<ReturnType<typeof tile>>;
const overlaps = (a: Box, b: Box) => a.x < b.x + b.width - .01 && a.x + a.width > b.x + .01 && a.y < b.y + b.height - .01 && a.y + a.height > b.y + .01;
const gap = (a: Box, b: Box) => Math.hypot(Math.max(0, a.x - b.x - b.width, b.x - a.x - a.width), Math.max(0, a.y - b.y - b.height, b.y - a.y - a.height));

function spatialStory() {
  const project = newProject('A story that unfolds in place'), canvas = project.entryCanvasId;
  const parent = addNode(project, canvas, 'text', 'The central account');
  Object.assign(parent, { x: 360, y: 300, width: 280, height: 220, childLayout: 'spatial', collapsed: true,
    text: 'The account remains a readable tile. '.repeat(6) + 'Its final sentence remains available in the inspector.' });
  const node = (title: string, placement: 'near' | 'edge' | 'free', x: number, y: number) => {
    const child = addNode(project, canvas, 'text', title);
    Object.assign(child, { parentId: parent.id, x, y, width: 180, height: 130, childLayout: 'spatial', placement, text: `${title}: complete supporting content.` });
    return child;
  };
  const near = node('Linked evidence', 'near', 680, 320);
  const edge = node('Adjacent illustration', 'edge', 500, 350); edge.kind = 'image'; edge.attachSide = 'left'; edge.assetId = 'spatial-pixel';
  project.assets.push({ id: edge.assetId, name: 'illustration.png', mime: 'image/png', base64: 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a2ioAAAAASUVORK5CYII=' });
  const free = node('Distant annotation', 'free', 1360, 650);
  const nested = node('Sources and variants', 'near', 680, 470); nested.collapsed = true;
  const grandchild = addNode(project, canvas, 'text', 'A hidden source');
  Object.assign(grandchild, { parentId: nested.id, x: 920, y: 470, width: 210, height: 120, placement: 'free', text: 'Nested source text is accessible before it is shown on the canvas.' });
  const blocker = addNode(project, canvas, 'text', 'Existing neighboring tile');
  Object.assign(blocker, { x: 660, y: 280, width: 220, height: 250, text: 'Revealing nearby children must not cover or move this tile.' });
  const unrelated = addNode(project, canvas, 'text', 'Unrelated account');
  Object.assign(unrelated, { x: 1050, y: 80, width: 240, height: 140, text: 'This account is independent of the revealed branch.' });
  // This fixture tests prescribed placement, not content-driven dimensions.
  for (const node of project.nodes) node.sizeMode = 'fixed';
  project.links.push({ id: 'parent-evidence', canvasId: canvas, sourceId: parent.id, targetId: near.id, kind: 'visual', relation: 'causal' });
  project.links.push({ id: 'parent-sources', canvasId: canvas, sourceId: parent.id, targetId: nested.id, kind: 'visual', relation: 'generic' });
  return { project, parent, near, edge, free, nested, grandchild, blocker, unrelated };
}

test('single selection inspects hidden children without unfolding them, while explicit reveal is separate', async ({ page, request }) => {
  await page.setViewportSize({ width: 1800, height: 1000 });
  const { project, parent, near, nested, grandchild } = spatialStory();
  expect((await request.post('/api/projects', { data: commitSnapshots(project) })).ok()).toBeTruthy();
  await page.goto(`/?project=${project.id}`); await page.getByRole('button', { name: 'Fit Work', exact: true }).click();
  const address = page.url(), initialCamera = await camera(page);
  await title(page, parent.id).click();
  await page.getByRole('combobox', { name: 'Side panel', exact: true }).selectOption('content');
  await expect(page.locator('.inspector').getByLabel('Title', { exact: true })).toHaveValue(parent.title);
  await expect(page.locator('.inspector').getByLabel('Text body', { exact: true })).toHaveValue(parent.text!);
  await page.getByRole('combobox', { name: 'Side panel', exact: true }).selectOption('relations');
  await expect(page.getByRole('button', { name: `Inspect descendant ${near.title}`, exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: `Inspect descendant ${nested.title}`, exact: true })).toBeVisible();
  await expect(card(page, near.id)).toHaveCount(0);
  expect(page.url()).toBe(address); expect(await camera(page)).toBe(initialCamera);
  await page.getByRole('button', { name: `Inspect descendant ${nested.title}`, exact: true }).click();
  await page.getByRole('combobox', { name: 'Side panel', exact: true }).selectOption('content');
  await expect(page.locator('.inspector').getByLabel('Title', { exact: true })).toHaveValue(nested.title);
  await page.getByRole('combobox', { name: 'Side panel', exact: true }).selectOption('relations');
  await expect(page.getByRole('button', { name: `Inspect descendant ${grandchild.title}`, exact: true })).toBeVisible();
  await expect(card(page, nested.id)).toHaveCount(0); await expect(card(page, grandchild.id)).toHaveCount(0);
  expect(await camera(page)).toBe(initialCamera);
  await page.getByRole('button', { name: `Inspect descendant ${grandchild.title}`, exact: true }).click();
  await page.getByRole('combobox', { name: 'Side panel', exact: true }).selectOption('content');
  await expect(page.locator('.inspector').getByLabel('Text body', { exact: true })).toHaveValue(grandchild.text!);
  await expect(card(page, grandchild.id)).toHaveCount(0);
  // Returning to the parent's property list still does not reveal anything.
  await title(page, parent.id).click();
  await expect(page.locator('.inspector').getByLabel('Title', { exact: true })).toHaveValue(parent.title);
  await expect(card(page, near.id)).toHaveCount(0);
  await page.getByRole('combobox', { name: 'Side panel', exact: true }).selectOption('relations');
  await page.getByRole('button', { name: `Jump to Node: ${near.title}`, exact: true }).click();
  await expect(card(page, near.id)).toBeVisible();
  await expect(card(page, grandchild.id)).toHaveCount(0);
  expect(page.url()).toBe(address);
});

test('double-click disclosure keeps the canvas, camera and parent fixed, places children safely, and publishes the same behavior offline', async ({ page, request, browser }, info) => {
  await page.setViewportSize({ width: 1800, height: 1000 });
  const { project, parent, near, edge, free, nested, grandchild, blocker, unrelated } = spatialStory();
  const errors: string[] = []; page.on('pageerror', error => errors.push(error.message));
  expect((await request.post('/api/projects', { data: commitSnapshots(project) })).ok()).toBeTruthy();
  await page.goto(`/?project=${project.id}`); await page.getByRole('button', { name: 'Fit Work', exact: true }).click();
  const address = page.url(), initialCamera = await camera(page), originalParent = await tile(page, parent.id), originalNeighbor = await tile(page, blocker.id), originalUnrelated = await tile(page, unrelated.id);
  const parentScreen = await card(page, parent.id).boundingBox();
  // Start unselected: the first click rebuilds the inspector, and the second must still disclose.
  await title(page, parent.id).dblclick();
  await expect(card(page, near.id)).toHaveCount(1); await expect(card(page, edge.id)).toHaveCount(1); await expect(card(page, free.id)).toHaveCount(1);
  await expect(card(page, nested.id)).toHaveCount(1); await expect(card(page, grandchild.id)).toHaveCount(0);
  expect(page.url()).toBe(address); expect(await camera(page)).toBe(initialCamera);
  expect(await tile(page, parent.id)).toEqual(originalParent); expect(await card(page, parent.id).boundingBox()).toEqual(parentScreen);
  expect(await tile(page, blocker.id)).toEqual(originalNeighbor); expect(await tile(page, unrelated.id)).toEqual(originalUnrelated);
  expect(await tile(page, free.id)).toEqual({ x: free.x, y: free.y, width: free.width, height: free.height });
  const nearby = await tile(page, near.id), attached = await tile(page, edge.id), branch = await tile(page, nested.id);
  for (const child of [nearby, attached, branch]) {
    expect(overlaps(child, originalParent)).toBe(false); expect(overlaps(child, originalNeighbor)).toBe(false); expect(overlaps(child, originalUnrelated)).toBe(false);
  }
  expect(overlaps(nearby, attached)).toBe(false); expect(overlaps(nearby, branch)).toBe(false); expect(overlaps(attached, branch)).toBe(false);
  expect(gap(nearby, originalParent)).toBeLessThanOrEqual(60);
  expect(attached.x + attached.width).toBe(originalParent.x);
  expect(Math.min(attached.y + attached.height, originalParent.y + originalParent.height) - Math.max(attached.y, originalParent.y)).toBeGreaterThan(0);
  await expect(card(page, edge.id).locator('.ms-image')).toBeVisible();
  await expect(page.locator('.ms-link-line[data-link-id="parent-evidence"]')).toHaveCount(1);
  await expect(page.locator('.ms-link-line[data-link-id="parent-sources"]')).toHaveCount(1);
  await title(page, parent.id).dblclick();
  for (const hidden of [near, edge, free, nested, grandchild]) await expect(card(page, hidden.id)).toHaveCount(0);
  expect(await camera(page)).toBe(initialCamera); expect(await tile(page, parent.id)).toEqual(originalParent);
  await title(page, parent.id).dblclick(); await expect(card(page, near.id)).toHaveCount(1); await expect(card(page, grandchild.id)).toHaveCount(0);
  expect(await tile(page, near.id)).toEqual(nearby); expect(await tile(page, edge.id)).toEqual(attached);
  await title(page, parent.id).dblclick();
  await workspaceAction(page,'Save'); await expect(page.locator('.save-state')).toHaveText('Saved · r2');
  const saved = await (await request.get(`/api/projects/${project.id}`)).json() as MeridianProject;
  expect(saved.nodes.find(node => node.id === parent.id)).toMatchObject({ collapsed: true, childLayout: 'spatial', width: parent.width, height: parent.height });
  expect(saved.nodes.find(node => node.id === nested.id)?.collapsed).toBe(true);
  expect(saved.nodes.find(node => node.id === edge.id)).toMatchObject({ placement: 'edge', attachSide: 'left', x: attached.x, y: attached.y });
  expect(saved.nodes.find(node => node.id === free.id)).toMatchObject({ placement: 'free', x: free.x, y: free.y });
  await page.reload(); await expect(card(page, near.id)).toHaveCount(0); await expect(card(page, parent.id)).toHaveCSS('width', '280px');
  await workspaceAction(page,'Publish');
  const downloading = page.waitForEvent('download'); await page.getByRole('button', { name: 'Publish HTML', exact: true }).click();
  const download = await downloading, published = info.outputPath('spatial-disclosure.html'); await download.saveAs(published);
  const portable = readPublication(await readFile(published, 'utf8'));
  expect(portable.nodes.find(node => node.title === edge.title)).toMatchObject({ placement: 'edge', attachSide: 'left' });
  expect(portable.nodes.find(node => node.title === parent.title)).toMatchObject({ childLayout: 'spatial', collapsed: true });
  expect(errors).toEqual([]);
  const context = await browser.newContext({ offline: true, viewport: { width: 1800, height: 1000 } });
  try {
    const viewer = await context.newPage(), remote: string[] = [], viewerErrors: string[] = [];
    viewer.on('request', request => { if (/^https?:/.test(request.url())) remote.push(request.url()); }); viewer.on('pageerror', error => viewerErrors.push(error.message));
    await viewer.goto(pathToFileURL(published).href);
    const authored = await viewer.locator('#meridian-data').textContent(), viewerAddress = viewer.url();
    await title(viewer, parent.id).click();
    await expect(viewer.locator('.mv-node-context')).toContainText(parent.text!);
    await expect(viewer.getByRole('button', { name: `Select child ${near.title}`, exact: true })).toBeVisible();
    await expect(card(viewer, near.id)).toHaveCount(0);
    // Close the overlay before using canvas space underneath it; disclosure must not move the camera.
    await viewer.getByRole('button', { name: 'Close project navigation', exact: true }).click();
    const viewerCamera = await camera(viewer), viewerParent = await tile(viewer, parent.id);
    await title(viewer, parent.id).dblclick();
    await expect(card(viewer, near.id)).toHaveCount(1); await expect(card(viewer, grandchild.id)).toHaveCount(0);
    expect(await camera(viewer)).toBe(viewerCamera); expect(await tile(viewer, parent.id)).toEqual(viewerParent); expect(viewer.url()).toBe(viewerAddress);
    expect(await tile(viewer, edge.id)).toEqual(attached); expect(await tile(viewer, near.id)).toEqual(nearby);
    await title(viewer, parent.id).dblclick(); await expect(card(viewer, near.id)).toHaveCount(0);
    expect(await viewer.locator('#meridian-data').textContent()).toBe(authored);
    await viewer.getByRole('button', { name: `Select child ${nested.title}`, exact: true }).click();
    await expect(viewer.locator('.mv-node-context')).toContainText(nested.text!); await expect(card(viewer, nested.id)).toHaveCount(0);
    await viewer.getByRole('button', { name: `Select child ${grandchild.title}`, exact: true }).click();
    await expect(viewer.locator('.mv-node-context')).toContainText(grandchild.text!); await expect(card(viewer, grandchild.id)).toHaveCount(0);
    expect(remote).toEqual([]); expect(viewerErrors).toEqual([]);
    await viewer.screenshot({ path: info.outputPath('spatial-offline-inspector.png'), fullPage: true });
  } finally { await context.close(); }
});

test('visual relationship arrows are explicit and canvas-node double-click reveals children without opening its destination', async ({ page, request, browser }, info) => {
  const project = newProject('Relationship directions'), canvasId = project.entryCanvasId;
  const targetCanvas = addCanvas(project, 'A separate destination');
  const launcher = addNode(project, canvasId, 'canvas-link', 'Chapter with local contents');
  Object.assign(launcher, { targetId: targetCanvas.rootId, childLayout: 'spatial', collapsed: true, x: 300, y: 80, width: 300, height: 200 });
  const contents = addNode(project, canvasId, 'text', 'Local chapter summary');
  Object.assign(contents, { parentId: launcher.id, x: 650, y: 80, width: 260, height: 160, placement: 'free', text: 'This belongs to the current visual canvas.' });
  const cases: Array<{ relation: LinkRelation; direction?: LinkDirection; start: boolean; end: boolean }> = [
    { relation: 'chronological', start: false, end: true },
    { relation: 'generic', start: false, end: false },
    { relation: 'causal', direction: 'reverse', start: true, end: false },
    { relation: 'generic', direction: 'both', start: true, end: true },
    { relation: 'causal', direction: 'none', start: false, end: false },
  ];
  for (const [index, item] of cases.entries()) {
    const source = addNode(project, canvasId, 'text', `Source ${index + 1}`), target = addNode(project, canvasId, 'text', `Target ${index + 1}`);
    Object.assign(source, { x: 160, y: 350 + index * 130, width: 160, height: 90 }); Object.assign(target, { x: 820, y: source.y, width: 160, height: 90 });
    project.links.push({ id: `direction-${index}`, canvasId, sourceId: source.id, targetId: target.id, kind: 'visual', relation: item.relation, ...(item.direction ? { direction: item.direction } : {}) });
  }
  const markers = async (surface: Page) => {
    for (const [index, item] of cases.entries()) {
      const line = surface.locator(`.ms-link-line[data-link-id="direction-${index}"]`); await expect(line).toHaveCount(1);
      expect(Boolean((await line.getAttribute('marker-start'))?.startsWith('url('))).toBe(item.start);
      expect(Boolean((await line.getAttribute('marker-end'))?.startsWith('url('))).toBe(item.end);
    }
  };
  expect((await request.post('/api/projects', { data: commitSnapshots(project) })).ok()).toBeTruthy();
  await page.goto(`/?project=${project.id}`); await page.getByRole('button', { name: 'Fit Work', exact: true }).click();
  await markers(page); const address = page.url(), initialCamera = await camera(page);
  await title(page, launcher.id).dblclick(); await expect(card(page, contents.id)).toHaveCount(1);
  expect(page.url()).toBe(address); expect(await camera(page)).toBe(initialCamera);
  await expect(card(page, launcher.id).getByRole('button', { name: `Open ${targetCanvas.title}`, exact: true })).toBeVisible();
  const published = info.outputPath('spatial-relationship-directions.html');
  await writeFile(published, buildPublication(commitSnapshots(project), await readFile('dist/viewer.js', 'utf8'), await readFile('dist/scene.css', 'utf8')));
  const context = await browser.newContext({ offline: true, viewport: { width: 1700, height: 1000 } });
  try {
    const viewer = await context.newPage(); await viewer.goto(pathToFileURL(published).href); await markers(viewer);
    await title(viewer, launcher.id).dblclick(); await expect(card(viewer, contents.id)).toHaveCount(1);
    await expect(viewer.getByRole('combobox', { name: 'Choose Work', exact: true })).toHaveValue(canvasId);
    await card(viewer, launcher.id).getByRole('button', { name: `Open ${targetCanvas.title}`, exact: true }).click();
    await expect(viewer.getByRole('combobox', { name: 'Choose Work', exact: true })).toHaveValue(targetCanvas.id);
  } finally { await context.close(); }
});

test('an attached video tile decodes with explicit playback controls and remains available in an offline publication', async ({ page, request, browser }, info) => {
  // Generate a small, silent, valid media attachment locally in the test browser.
  await page.goto('/__meridian_status');
  const base64 = await page.evaluate(async () => {
    const canvas = document.createElement('canvas'); canvas.width = 64; canvas.height = 48;
    const paint = canvas.getContext('2d')!, stream = canvas.captureStream(20);
    const recorder = new MediaRecorder(stream, { mimeType: 'video/webm;codecs=vp8' });
    const chunks: BlobPart[] = [], complete = new Promise<Blob>(resolve => { recorder.onstop = () => resolve(new Blob(chunks, { type: 'video/webm' })); });
    recorder.ondataavailable = event => { if (event.data.size) chunks.push(event.data); };
    recorder.start();
    for (let frame = 0; frame < 8; frame++) {
      paint.fillStyle = '#24495b'; paint.fillRect(0, 0, 64, 48);
      paint.fillStyle = '#78d9bf'; paint.fillRect(8 + frame * 3, 16, 12, 16);
      await new Promise(resolve => setTimeout(resolve, 50));
    }
    recorder.stop(); const blob = await complete; stream.getTracks().forEach(track => track.stop());
    const bytes = new Uint8Array(await blob.arrayBuffer()); let binary = ''; for (const byte of bytes) binary += String.fromCharCode(byte); return btoa(binary);
  });
  expect(base64.length).toBeGreaterThan(100);
  const project = newProject('A moving illustration'), canvasId = project.entryCanvasId;
  const parent = addNode(project, canvasId, 'text', 'An account with a moving illustration');
  Object.assign(parent, { x: 500, y: 300, width: 320, height: 240, childLayout: 'spatial', collapsed: true, text: 'Double-click the account to reveal the adjacent video tile.' });
  const videoNode = addNode(project, canvasId, 'document', 'Moving illustration');
  Object.assign(videoNode, { parentId: parent.id, x: 900, y: 300, width: 280, height: 240, placement: 'edge', attachSide: 'right', assetId: 'generated-video' });
  project.assets.push({ id: 'generated-video', name: 'moving-illustration.webm', mime: 'video/webm', base64 });
  expect((await request.post('/api/projects', { data: commitSnapshots(project) })).ok()).toBeTruthy();
  await page.goto(`/?project=${project.id}`);
  await title(page, parent.id).dblclick();
  const video = card(page, videoNode.id).locator('video'); await expect(video).toHaveCount(1); await expect(video).toHaveAttribute('controls', '');
  await expect.poll(() => video.evaluate(element => (element as HTMLVideoElement).readyState)).toBeGreaterThanOrEqual(1);
  expect(await video.evaluate(element => (element as HTMLVideoElement).paused)).toBe(true);
  const parentBounds = await tile(page, parent.id), videoBounds = await tile(page, videoNode.id);
  expect(videoBounds.x).toBe(parentBounds.x + parentBounds.width);
  await video.evaluate(async element => { const player = element as HTMLVideoElement; player.muted = true; await player.play(); });
  await expect.poll(() => video.evaluate(element => (element as HTMLVideoElement).currentTime)).toBeGreaterThan(0);
  await title(page, parent.id).dblclick(); await expect(card(page, videoNode.id)).toHaveCount(0);
  const published = info.outputPath('spatial-video.html');
  await writeFile(published, buildPublication(commitSnapshots(project), await readFile('dist/viewer.js', 'utf8'), await readFile('dist/scene.css', 'utf8')));
  const context = await browser.newContext({ offline: true, viewport: { width: 1600, height: 1000 } });
  try {
    const viewer = await context.newPage(); await viewer.goto(pathToFileURL(published).href);
    await title(viewer, parent.id).dblclick(); const embedded = card(viewer, videoNode.id).locator('video');
    await expect.poll(() => embedded.evaluate(element => (element as HTMLVideoElement).readyState)).toBeGreaterThanOrEqual(1);
    await embedded.evaluate(async element => { const player = element as HTMLVideoElement; player.muted = true; await player.play(); });
    await expect.poll(() => embedded.evaluate(element => (element as HTMLVideoElement).currentTime)).toBeGreaterThan(0);
    await card(viewer, videoNode.id).getByRole('button', { name: 'Open moving-illustration.webm', exact: true }).click();
    await expect(viewer.locator('.ms-asset-dialog video')).toHaveCount(1);
    await expect.poll(() => viewer.locator('.ms-asset-dialog video').evaluate(element => (element as HTMLVideoElement).readyState)).toBeGreaterThanOrEqual(1);
    await viewer.screenshot({ path: info.outputPath('spatial-video-preview.png'), fullPage: true });
  } finally { await context.close(); }
});

test('opening the viewer inspector cannot intercept the second click on a chapter beside the canvas edge', async ({ browser }, info) => {
  const project = newProject('Viewer edge disclosure'), canvasId = project.entryCanvasId;
  const parent = addNode(project, canvasId, 'text', 'Left chapter');
  Object.assign(parent, { x: 80, y: 260, width: 180, height: 160, childLayout: 'spatial', collapsed: true, text: 'A compact chapter beside the left edge.' });
  const child = addNode(project, canvasId, 'text', 'Revealed beside the chapter');
  Object.assign(child, { parentId: parent.id, x: 360, y: 260, width: 220, height: 160, placement: 'free' });
  const far = addNode(project, canvasId, 'text', 'Far-side context');
  Object.assign(far, { x: 1350, y: 260, width: 140, height: 160 });
  const published = info.outputPath('spatial-left-edge.html');
  await writeFile(published, buildPublication(commitSnapshots(project), await readFile('dist/viewer.js', 'utf8'), await readFile('dist/scene.css', 'utf8')));
  const context = await browser.newContext({ offline: true, viewport: { width: 1600, height: 950 } });
  try {
    const viewer = await context.newPage(); await viewer.goto(pathToFileURL(published).href);
    const bounds = await card(viewer, parent.id).boundingBox(); expect(bounds!.x + bounds!.width).toBeLessThan(350);
    const view = await camera(viewer);
    await title(viewer, parent.id).dblclick();
    await expect(card(viewer, child.id)).toHaveCount(1);
    expect(await camera(viewer)).toBe(view);
    await expect(viewer.locator('.mv-node-context')).toContainText(parent.text!);
    await expect(viewer.locator('.mv-navigation')).toBeVisible();
    await viewer.screenshot({ path: info.outputPath('spatial-left-edge.png'), fullPage: true });
  } finally { await context.close(); }
});
