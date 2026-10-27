import { test, expect, type APIRequestContext, type Page } from '@playwright/test';
import { addCanvas, addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import { selectEditorNode } from './ui-helpers.ts';
import type { MeridianProject } from '../src/core/types.ts';

async function seed(page: Page, request: APIRequestContext) {
  const project = newProject(`Window navigation ${crypto.randomUUID().slice(0, 8)}`);
  project.canvases[0].title = 'First canvas'; project.nodes[0].title = 'First canvas';
  const second = addCanvas(project, 'Second canvas'), launcher = addNode(project, project.entryCanvasId, 'canvas-link', 'Second canvas');
  Object.assign(launcher, { x: 100, y: 100, width: 260, height: 180, targetId: second.rootId });
  const note = addNode(project, second.id, 'text', 'Second canvas note'); Object.assign(note, { x: 100, y: 100, width: 300, height: 180, text: 'Saved content' });
  const response = await request.post('/api/projects', { data: commitSnapshots(project) }); expect(response.ok()).toBeTruthy();
  const baseline = await response.json() as MeridianProject;
  await page.goto(`/?project=${project.id}&canvas=${project.entryCanvasId}`); await expect(page.locator('.workspace')).toBeVisible();
  return { project: baseline, second, launcher, note };
}

async function fileAction(page: Page, name: string) {
  await page.getByRole('button', { name: 'File', exact: true }).click();
  await page.getByRole('menuitem', { name, exact: true }).click();
}
async function openSecond(page: Page, title: string) { await page.getByRole('button', { name: `Open ${title}`, exact: true }).click(); }
async function nativeNavigation(page: Page, direction: 'back' | 'forward') {
  await page.evaluate(direction => window.dispatchEvent(new CustomEvent('meridian:navigate', { detail: { direction } })), direction);
}
async function nativeFile(page: Page, command: string) {
  await page.evaluate(command => window.dispatchEvent(new CustomEvent('meridian:file-command', { detail: { command } })), command);
}

test('browser fallback exposes a single File menu with all canvas commands and the real Meridian mark', async ({ page, request }) => {
  await seed(page, request);
  await expect(page.getByRole('button', { name: 'Workspace actions', exact: true })).toHaveCount(0);
  await expect(page.locator('.brand-mark')).not.toHaveText('M');
  await expect(page.locator('.brand-mark img')).toHaveAttribute('src', '/brand/meridian.png');
  await page.getByRole('button', { name: 'File', exact: true }).click();
  const menu = page.getByRole('menu');
  for (const name of ['Save', 'Save as', 'Create checkpoint', 'Duplicate', 'Import', 'History', 'Preview', 'Publish', 'Export Work']) {
    await expect(menu.getByRole('menuitem', { name, exact: true })).toBeVisible();
  }
  await page.keyboard.press('Escape');
});

test('native navigation commands travel through current-window canvas history without popups', async ({ page, request, context }) => {
  const f = await seed(page, request), firstUrl = page.url(), pageCount = context.pages().length;
  await openSecond(page, f.second.title); await expect(page.locator(`.ms-node[data-node-id="${f.note.id}"]`)).toHaveCount(1);
  const secondUrl = page.url(); expect(secondUrl).not.toBe(firstUrl);
  await nativeNavigation(page, 'back'); await page.waitForURL(firstUrl); await expect(page.locator(`.ms-node[data-node-id="${f.launcher.id}"]`)).toHaveCount(1);
  await nativeNavigation(page, 'forward'); await page.waitForURL(secondUrl); await expect(page.locator(`.ms-node[data-node-id="${f.note.id}"]`)).toHaveCount(1);
  expect(context.pages()).toHaveLength(pageCount);
});

test('native Back commits the focused field and waits for its in-flight save before leaving', async ({ page, request }) => {
  const f = await seed(page, request), firstUrl = page.url(); await openSecond(page, f.second.title);
  await selectEditorNode(page, f.note.id, f.note.title); const sourceUrl = page.url(), route = `/api/projects/${f.project.id}`;
  let release!: () => void, started!: () => void;
  const gate = new Promise<void>(resolve => { release = resolve; }), began = new Promise<void>(resolve => { started = resolve; });
  await page.route(`**${route}`, async intercepted => { if (intercepted.request().method() !== 'PUT') { await intercepted.continue(); return; } started(); await gate; await intercepted.continue(); });
  try {
    await page.locator('.inspector').getByLabel('Text body', { exact: true }).fill('Typed before native Back');
    await nativeNavigation(page, 'back'); await began;
    expect(page.url()).toBe(sourceUrl); await expect(page.locator('.save-state')).toHaveText('Saving…');
    release(); await page.waitForURL(firstUrl); await expect(page.locator('.workspace')).toBeVisible();
    const saved = await (await request.get(route)).json(); expect(saved.nodes.find((node: { id: string }) => node.id === f.note.id).text).toBe('Typed before native Back');
  } finally { release(); await page.unroute(`**${route}`); }
});

test('a conflicted native Back keeps the draft open and does not navigate or overwrite saved content', async ({ page, request }) => {
  const f = await seed(page, request); await openSecond(page, f.second.title); await selectEditorNode(page, f.note.id, f.note.title);
  const sourceUrl = page.url(), route = `/api/projects/${f.project.id}`;
  await page.route(`**${route}`, async intercepted => { if (intercepted.request().method() !== 'PUT') { await intercepted.continue(); return; } await intercepted.fulfill({ status: 409, contentType: 'application/json', body: JSON.stringify({ error: 'Canvas has a newer revision.' }) }); });
  try {
    await page.locator('.inspector').getByLabel('Text body', { exact: true }).fill('Retained conflict draft'); await nativeNavigation(page, 'back');
    await expect(page.locator('.save-state')).toHaveText('Save conflict · draft kept'); expect(page.url()).toBe(sourceUrl);
    await expect(page.locator('.inspector').getByLabel('Text body', { exact: true })).toHaveValue('Retained conflict draft');
    await expect(page.getByRole('dialog', { name: 'Leave this draft?', exact: true })).toHaveCount(0);
    const committed = await (await request.get(route)).json(); expect(committed.nodes.find((node: { id: string }) => node.id === f.note.id).text).toBe('Saved content');
  } finally { await page.unroute(`**${route}`); }
});

test('File checkpoint stores the selected canvas draft and Save as creates an independent named graph', async ({ page, request }) => {
  const f = await seed(page, request); await openSecond(page, f.second.title); await selectEditorNode(page, f.note.id, f.note.title);
  await page.locator('.inspector').getByLabel('Text body', { exact: true }).fill('Content before checkpoint');
  await fileAction(page, 'Create checkpoint'); const prompt = page.getByRole('dialog');
  const checkpointName = `Checkpoint ${crypto.randomUUID().slice(0, 8)}`;
  await prompt.getByLabel('Name', { exact: true }).fill(checkpointName); await prompt.getByRole('button', { name: 'Continue', exact: true }).click();
  await expect(prompt).toHaveCount(0);
  await expect.poll(async () => (await (await request.get(`/api/projects/${f.project.id}/checkpoints`)).json()).some((item: { name: string }) => item.name === checkpointName)).toBe(true);
  const checkpoints = await (await request.get(`/api/projects/${f.project.id}/checkpoints`)).json(); const checkpoint = checkpoints.find((item: { name: string }) => item.name === checkpointName); expect(checkpoint).toBeTruthy();
  const checkpointProject = await (await request.get(`/api/projects/${f.project.id}/checkpoints/${checkpoint.id}`)).json(); expect(checkpointProject.nodes.find((node: { id: string }) => node.id === f.note.id).text).toBe('Content before checkpoint');
  await fileAction(page, 'Save as'); const copying = page.getByRole('dialog'), name = `Independent canvas ${crypto.randomUUID().slice(0, 8)}`;
  await copying.getByLabel('Name', { exact: true }).fill(name); await copying.getByRole('button', { name: 'Continue', exact: true }).click();
  await expect(page.locator('.save-state')).toHaveText('Saved · r1');
  const copyId = new URL(page.url()).searchParams.get('project')!; expect(copyId).not.toBe(f.project.id);
  const copy = await (await request.get(`/api/projects/${copyId}`)).json() as MeridianProject;
  expect(copy.title).toBe(name); expect(copy.canvases).toHaveLength(f.project.canvases.length);
  expect(copy.canvases.find(canvas => canvas.id === copy.entryCanvasId)?.title).toBe(name);
  expect(copy.nodes.find(node => node.text === 'Content before checkpoint')).toBeTruthy();
  expect(copy.nodes.every(node => !f.project.nodes.some(original => original.id === node.id))).toBe(true);
});

test('native Duplicate keeps focused content and gives the copy independent canvas and node identities', async ({ page, request }) => {
  const f = await seed(page, request); await openSecond(page, f.second.title); await selectEditorNode(page, f.note.id, f.note.title);
  await page.locator('.inspector').getByLabel('Text body', { exact: true }).fill('Last typing before Duplicate');
  await nativeFile(page, 'duplicate');
  await page.waitForURL(url => !!url.searchParams.get('project') && url.searchParams.get('project') !== f.project.id);
  await expect(page.locator('.save-state')).toHaveText('Saved · r1');
  const id = new URL(page.url()).searchParams.get('project')!, copy = await (await request.get(`/api/projects/${id}`)).json() as MeridianProject;
  expect(copy.nodes.some(node => node.text === 'Last typing before Duplicate')).toBe(true);
  expect(copy.canvases).toHaveLength(f.project.canvases.length);
  expect(copy.nodes.every(node => !f.project.nodes.some(original => original.id === node.id))).toBe(true);
  expect(copy.canvases.find(canvas => canvas.id === copy.entryCanvasId)?.title).toContain(f.second.title);
});

test('native File dispatch works without a second web menu and preserves preview and export actions', async ({ page, request }) => {
  await page.addInitScript(() => { (window as Window & { __meridianNativeChrome?: boolean }).__meridianNativeChrome = true; });
  await seed(page, request); await expect(page.getByRole('button', { name: 'File', exact: true })).toHaveCount(0);
  await nativeFile(page, 'export'); await expect(page.getByRole('dialog', { name: 'Download editable Work', exact: true })).toBeVisible();
  await page.getByRole('dialog').getByRole('button', { name: 'Cancel', exact: true }).click();
  await nativeFile(page, 'preview'); await expect(page.getByRole('dialog', { name: 'Preview published Work', exact: true })).toBeVisible();
  await page.getByRole('dialog').getByRole('button', { name: 'Cancel', exact: true }).click();
  await nativeFile(page, 'history'); await expect(page.getByRole('dialog', { name: 'History & checkpoints', exact: true })).toBeVisible();
});
