import { test, expect, type APIRequestContext, type Page } from '@playwright/test';
import { readFile } from 'node:fs/promises';
import { addCanvas, addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import { emptyLibrary, normalizeLibraryDocument, reconcileLibraryCanvas, type LibraryCanvasDocument, type LibraryCatalogItem } from '../src/core/library.ts';
import { selectEditorNode } from './ui-helpers.ts';
import { resolveCanvasCollisions } from '../src/core/collision.ts';

const stored = async (request: APIRequestContext) => await (await request.get('/api/library')).json() as LibraryCanvasDocument;
const saved = (page: Page) => expect(page.locator('.save-state')).toHaveText(/^Saved · r\d+$/);
async function overflow(page: Page, name: string) { await page.getByRole('button', { name: 'File', exact: true }).click(); await page.getByRole('menuitem', { name, exact: true }).click(); }
async function select(page: Page, node: { id: string; title: string }) { await selectEditorNode(page, node.id, node.title); }
async function browserDraft(page:Page,document:LibraryCanvasDocument,nodeId:string,text:string){
  return page.evaluate(({document,nodeId,text})=>{
    const project=structuredClone(document.project);project.nodes.find(node=>node.id===nodeId)!.text=text;project.snapshots={};
    const key=`meridian.recovery.v1.${project.id}.${window.name.slice('meridian-editor:'.length)}`;
    localStorage.setItem(key,JSON.stringify({project,baseRevision:document.revision,savedAt:new Date().toISOString()}));return key;
  },{document,nodeId,text});
}

async function seed(page: Page, request: APIRequestContext) {
  const suffix = crypto.randomUUID().slice(0, 8), project = newProject(`Destination ${suffix}`), other = newProject(`Second destination ${suffix}`);
  const chapter = addCanvas(project, `External chapter ${suffix}`), content = addNode(project, chapter.id, 'text', `Destination content ${suffix}`);
  const firstContent = addNode(project, project.entryCanvasId, 'text', `First content ${suffix}`); content.text = 'Independent destination content.';
  for (const item of [project, other]) expect((await request.post('/api/projects', { data: commitSnapshots(item) })).ok()).toBeTruthy();
  const current = await (await request.get('/api/library')).json();
  const priorRecovery = await request.get('/api/library/recovery');
  if (priorRecovery.ok()) { const recovery = await priorRecovery.json(); expect((await request.delete('/api/library/recovery', { data: { baseRevision: recovery.baseRevision, savedAt: recovery.savedAt } })).ok()).toBeTruthy(); }
  const initial = { ...emptyLibrary(), revision: current.revision }, catalog = await (await request.get('/api/library/catalog')).json() as LibraryCatalogItem[];
  const document = reconcileLibraryCanvas(normalizeLibraryDocument(initial, catalog), catalog), homeId = document.project.entryCanvasId;
  const note = addNode(document.project, homeId, 'text', `Home authored content ${suffix}`); Object.assign(note, { x: 100, y: 550, text: 'Keep this authored graph intact.', style: { shape: 'none' } });
  const internal = addCanvas(document.project, `Home-owned canvas ${suffix}`), internalContent = addNode(document.project, internal.id, 'text', `Internal content ${suffix}`);
  const internalLauncher = addNode(document.project, homeId, 'canvas-link', internal.title); Object.assign(internalLauncher, { targetId: internal.rootId, x: 470, y: 550, width: 300, height: 160, childLayout: 'spatial' });
  const chapterLauncherId = Object.entries(document.canvasTargets).find(([, target]) => target.projectId === project.id && target.canvasId === chapter.id)![0];
  const homeNodes = new Map(document.project.nodes.map(node => [node.id, node]));
  for (let ancestor = homeNodes.get(chapterLauncherId)?.parentId; ancestor;) {
    const node = homeNodes.get(ancestor)!; if (node.kind !== 'canvas') node.collapsed = false; ancestor = node.parentId;
  }
  // Catalog discovery accumulates across this suite. Establish a collision-free saved
  // baseline with the initially inspected branch revealed, so selection cannot schedule
  // unrelated disclosure/layout edits during recovery tests.
  for (const canvas of document.project.canvases) document.project = resolveCanvasCollisions(document.project, canvas.id);
  const response = await request.put('/api/library', { data: { library: document, expectedRevision: document.revision } }); expect(response.ok()).toBeTruthy();
  const result = await response.json() as LibraryCanvasDocument;
  const external = (projectId: string, canvasId: string) => { const id = Object.entries(result.canvasTargets).find(([, target]) => target.projectId === projectId && target.canvasId === canvasId)![0]; return result.project.nodes.find(node => node.id === id)!; };
  const entry = external(project.id, chapter.id), first = external(project.id, project.entryCanvasId), competing = external(other.id, other.entryCanvasId);
  await page.goto('/'); await expect(page.locator('.workspace')).toBeVisible(); await saved(page); await select(page, entry); await saved(page);
  return { project, other, chapter, content, firstContent, entry, first, competing, note, internal, internalContent, internalLauncher };
}

async function openSelected(page: Page) { await page.getByLabel('Side panel', { exact: true }).selectOption('content'); await page.locator('.inspector').getByRole('button', { name: 'Open Work', exact: true }).click(); }

test('external and home-owned canvases navigate in the current window and browser history restores each canvas', async ({ page, request, context }) => {
  const f = await seed(page, request), initialUrl = page.url(), pages = context.pages().length;
  let popups = 0; page.on('popup', () => { popups++; });
  await openSelected(page);
  await expect(page.locator(`.ms-node[data-node-id="${f.content.id}"]`)).toHaveCount(1);
  expect(new URL(page.url()).searchParams.get('project')).toBe(f.project.id); expect(new URL(page.url()).searchParams.get('canvas')).toBe(f.chapter.id);
  await page.goBack(); await saved(page); expect(page.url()).toBe(initialUrl);
  await page.goForward(); await expect(page.locator(`.ms-node[data-node-id="${f.content.id}"]`)).toHaveCount(1);
  await page.goBack(); await saved(page);
  await select(page, f.internalLauncher); await openSelected(page);
  await expect(page.locator(`.ms-node[data-node-id="${f.internalContent.id}"]`)).toHaveCount(1);
  expect(new URL(page.url()).searchParams.get('libraryCanvas')).toBe(f.internal.id); expect(new URL(page.url()).searchParams.get('project')).toBeNull();
  await page.goBack(); await saved(page); expect(page.url()).toBe(initialUrl);
  await page.goForward(); await expect(page.locator(`.ms-node[data-node-id="${f.internalContent.id}"]`)).toHaveCount(1);
  expect(context.pages()).toHaveLength(pages); expect(popups).toBe(0);
});

test('slow destination loading keeps the saved home graph intact and finishes in the same window', async ({ page, request, context }) => {
  const f = await seed(page, request), original = await stored(request), pages = context.pages().length;
  let release!: () => void, started!: () => void;
  const gate = new Promise<void>(resolve => { release = resolve; }), began = new Promise<void>(resolve => { started = resolve; });
  await context.route(`**/api/projects/${f.project.id}`, async route => { started(); await gate; await route.continue(); });
  try {
    const opening = openSelected(page); await began;
    expect(context.pages()).toHaveLength(pages); expect(await stored(request)).toEqual(original);
    release(); await opening; await expect(page.locator(`.ms-node[data-node-id="${f.content.id}"]`)).toHaveCount(1);
    expect(new URL(page.url()).searchParams.get('project')).toBe(f.project.id); expect(await stored(request)).toEqual(original);
  } finally { release(); await context.unrouteAll({ behavior: 'wait' }); }
});

test('same-window canvas navigation waits for the pending home autosave commit', async ({ page, request, context }) => {
  const f = await seed(page, request), initialUrl = page.url(), pages = context.pages().length; let release!: () => void, started!: () => void;
  const gate = new Promise<void>(resolve => { release = resolve; }), began = new Promise<void>(resolve => { started = resolve; });
  await page.route('**/api/library', async route => { if (route.request().method() !== 'PUT') { await route.continue(); return; } started(); await gate; await route.continue(); });
  try {
    const title = `Saved before opening ${crypto.randomUUID().slice(0, 8)}`;
    await page.locator('.inspector').getByLabel('Title', { exact: true }).fill(title); await page.locator('.inspector').getByLabel('Title', { exact: true }).press('Tab'); await began;
    await openSelected(page);
    expect(page.url()).toBe(initialUrl); expect(context.pages()).toHaveLength(pages); await expect(page.locator('.save-state')).toHaveText('Saving…');
    release(); await expect(page.locator(`.ms-node[data-node-id="${f.content.id}"]`)).toHaveCount(1); await saved(page);
    expect((await stored(request)).project.nodes.find(node => node.id === f.entry.id)!.title).toBe(title);
  } finally { release(); await page.unroute('**/api/library'); }
});

test('a rejected pending home save cancels opening, retains editable content and stores a complete recovery draft', async ({ page, request }) => {
  const f = await seed(page, request), original = await stored(request); let release!: () => void, started!: () => void;
  const gate = new Promise<void>(resolve => { release = resolve; }), began = new Promise<void>(resolve => { started = resolve; });
  await page.route('**/api/library', async route => { if (route.request().method() !== 'PUT') { await route.continue(); return; } started(); await gate; await route.fulfill({ status: 409, contentType: 'application/json', body: JSON.stringify({ error: 'A newer home revision exists.' }) }); });
  try {
    const title = `Retained home draft ${crypto.randomUUID().slice(0, 8)}`;
    await page.locator('.inspector').getByLabel('Title', { exact: true }).fill(title); await page.locator('.inspector').getByLabel('Title', { exact: true }).press('Tab'); await began;
    const url = page.url(); await openSelected(page); expect(page.url()).toBe(url); release();
    await expect(page.locator('.save-state')).toHaveText('Save conflict · draft kept'); expect(page.url()).toBe(url);
    await expect(page.locator('.inspector').getByLabel('Title', { exact: true })).toHaveValue(title); expect(await stored(request)).toEqual(original);
    await expect.poll(async () => { const response = await request.get('/api/library/recovery'); return response.ok() ? (await response.json()).library.project.nodes.find((node: { id: string }) => node.id === f.entry.id)?.title : null; }).toBe(title);
    const recovery = await (await request.get('/api/library/recovery')).json(); expect(recovery.library.canvasTargets[f.entry.id]).toEqual(original.canvasTargets[f.entry.id]);
    await page.unroute('**/api/library'); await page.reload();
    await page.getByRole('dialog', { name: 'Recovery draft available', exact: true }).getByRole('button', { name: 'Restore draft', exact: true }).click();
    await expect.poll(async () => (await stored(request)).project.nodes.find(node => node.id === f.entry.id)?.title).toBe(title); await saved(page);
  } finally { release(); await page.unroute('**/api/library'); }
});

test('home recovery waits for the user before saving discovery and restores its full discovery history',async({page,request})=>{
  const f=await seed(page,request),baseline=await stored(request),draft=structuredClone(baseline),text='Recovered content and fresh discovery together';
  draft.project.nodes.find(node=>node.id===f.note.id)!.text=text;
  draft.project.nodes=draft.project.nodes.filter(node=>node.id!==f.competing.id);
  draft.project.links=draft.project.links.filter(link=>link.sourceId!==f.competing.id&&link.targetId!==f.competing.id&&link.parentId!==f.competing.id);
  delete draft.canvasTargets[f.competing.id];
  const recoveryResponse=await request.put('/api/library/recovery',{data:{library:draft,baseRevision:baseline.revision,expectedSavedAt:null}});expect(recoveryResponse.ok()).toBeTruthy();
  const recovery=await recoveryResponse.json(),fresh=newProject(`Discovered during recovery ${crypto.randomUUID().slice(0,8)}`);
  expect((await request.post('/api/projects',{data:commitSnapshots(fresh)})).ok()).toBeTruthy();
  await page.reload();const prompt=page.getByRole('dialog',{name:'Recovery draft available',exact:true});await expect(prompt).toBeVisible();
  // Deliberately keep the prompt open beyond the autosave idle interval.
  await page.waitForTimeout(850);
  expect((await stored(request)).revision).toBe(baseline.revision);expect(await (await request.get('/api/library/recovery')).json()).toEqual(recovery);
  await prompt.getByRole('button',{name:'Restore draft',exact:true}).click();
  await expect.poll(async()=>(await stored(request)).project.nodes.find(node=>node.id===f.note.id)?.text).toBe(text);await saved(page);
  const committed=await stored(request);expect(committed.revision).toBe(baseline.revision+1);
  expect(Object.values(committed.canvasTargets).some(target=>target.projectId===fresh.id&&target.canvasId===fresh.entryCanvasId)).toBe(true);
  expect(committed.project.nodes.some(node=>node.id===f.competing.id)).toBe(false);
  expect(committed.discoveredTargets).toContain(`${f.other.id}:${f.other.entryCanvasId}`);
  expect(committed.discoveredTargets).toContain(`${fresh.id}:${fresh.entryCanvasId}`);
  expect((await request.get('/api/library/recovery')).status()).toBe(404);
});

test('restoring this window’s home browser backup clears its acknowledged draft and preserves another host draft',async({page,request})=>{
  const f=await seed(page,request),baseline=await stored(request),hostDraft=structuredClone(baseline);
  hostDraft.project.nodes.find(node=>node.id===f.note.id)!.text='Another window’s host recovery';
  const written=await request.put('/api/library/recovery',{data:{library:hostDraft,baseRevision:baseline.revision,expectedSavedAt:null}});expect(written.ok()).toBeTruthy();const hostRecovery=await written.json();
  const text='This window’s browser backup',key=await browserDraft(page,baseline,f.note.id,text);
  await page.reload();await page.getByRole('dialog',{name:'Recovery draft available',exact:true}).getByRole('button',{name:'Restore draft',exact:true}).click();
  await expect.poll(async()=>(await stored(request)).project.nodes.find(node=>node.id===f.note.id)?.text).toBe(text);await saved(page);
  expect(await page.evaluate(key=>localStorage.getItem(key),key)).toBeNull();
  expect(await (await request.get('/api/library/recovery')).json()).toEqual(hostRecovery);
});

test('declining this window’s home browser backup discards only that backup',async({page,request})=>{
  const f=await seed(page,request),baseline=await stored(request),hostDraft=structuredClone(baseline);
  hostDraft.project.nodes.find(node=>node.id===f.note.id)!.text='Preserve this different host recovery';
  const written=await request.put('/api/library/recovery',{data:{library:hostDraft,baseRevision:baseline.revision,expectedSavedAt:null}});expect(written.ok()).toBeTruthy();const hostRecovery=await written.json();
  const key=await browserDraft(page,baseline,f.note.id,'Discard this browser draft');
  await page.reload();await page.getByRole('dialog',{name:'Recovery draft available',exact:true}).getByRole('button',{name:'Cancel',exact:true}).click();await saved(page);
  await expect.poll(()=>page.evaluate(key=>localStorage.getItem(key),key)).toBeNull();expect(await stored(request)).toEqual(baseline);
  expect(await (await request.get('/api/library/recovery')).json()).toEqual(hostRecovery);
});

test('Canvas creation uses the same node form and its launcher navigates to the home-owned canvas', async ({ page, request }) => {
  const f = await seed(page, request), initialUrl = page.url(); await select(page, f.note);
  await page.getByLabel('Side panel', { exact: true }).selectOption('relations');
  await page.getByRole('button', { name: 'Create child node', exact: true }).click(); const form = page.getByRole('dialog', { name: 'New Node', exact: true });
  await form.getByLabel('Type', { exact: true }).selectOption('canvas'); const title = `New canvas node ${crypto.randomUUID().slice(0, 8)}`;
  await form.getByLabel('Title', { exact: true }).fill(title); await form.getByLabel('Summary', { exact: true }).fill('A normal child node opens this canvas in the current workspace window.');
  await form.getByRole('button', { name: 'Create node', exact: true }).click();
  await expect.poll(async () => (await stored(request)).project.nodes.some(node => node.kind === 'canvas-link' && node.title === title)).toBe(true);
  await saved(page);
  const home = await stored(request), launcher = home.project.nodes.find(node => node.kind === 'canvas-link' && node.title === title)!;
  expect(launcher.kind).toBe('canvas-link'); expect(launcher.parentId).toBe(f.note.id); expect(home.canvasTargets[launcher.id]).toBeUndefined();
  const target = home.project.canvases.find(canvas => canvas.rootId === launcher.targetId)!; expect(target.title).toBe(title);
  await openSelected(page); await expect(page.locator('.workspace')).toBeVisible();
  expect(new URL(page.url()).searchParams.get('libraryCanvas')).toBe(target.id);
  await page.goBack(); await saved(page); expect(page.url()).toBe(initialUrl);
});

test('import and discovery retain authored home content, links, layout and internal Canvas launchers', async ({ page, request }) => {
  const f = await seed(page, request), initialUrl = page.url();
  const baseline = await stored(request), imported = newProject(`Imported canvas ${crypto.randomUUID().slice(0, 8)}`);
  const portable = await request.post('/api/export', { data: imported }); expect(portable.ok()).toBeTruthy();
  const choosing = page.waitForEvent('filechooser'); await overflow(page, 'Import'); const chooser = await choosing;
  await chooser.setFiles({ name: 'shared-home-import.meridian', mimeType: 'application/octet-stream', buffer: await portable.body() });
  await page.waitForURL(url => url.searchParams.get('project') === imported.id); await expect(page.locator('.workspace')).toBeVisible(); await saved(page);
  await page.goBack(); await saved(page); expect(page.url()).toBe(initialUrl);
  await expect.poll(async () => Object.values((await stored(request)).canvasTargets).some(target => target.projectId === imported.id)).toBe(true);
  const refreshed = await stored(request);
  for (const node of baseline.project.nodes) expect(refreshed.project.nodes.find(item => item.id === node.id)).toEqual(node);
  expect(refreshed.project.links).toEqual(baseline.project.links); expect(refreshed.project.assets).toEqual(baseline.project.assets);
  expect(refreshed.project.canvases).toEqual(baseline.project.canvases); expect(page.url()).toBe(initialUrl);
  await page.reload(); await saved(page); expect((await stored(request)).project.nodes.find(node => node.id === f.note.id)).toEqual(baseline.project.nodes.find(node => node.id === f.note.id));
});

test('canvas navigation does not require popup permission and preserves the saved home graph', async ({ page, request, context }) => {
  const f = await seed(page, request), original = await stored(request), pages = context.pages().length;
  await page.evaluate(() => { window.open = () => { throw new Error('Canvas navigation must not open another window.'); }; });
  await openSelected(page); await expect(page.locator(`.ms-node[data-node-id="${f.content.id}"]`)).toHaveCount(1);
  expect(new URL(page.url()).searchParams.get('project')).toBe(f.project.id); expect(await stored(request)).toEqual(original); expect(context.pages()).toHaveLength(pages);
});

test('two independently opened canvas windows keep separate drafts and preserve the newer saved revision on conflict', async ({ page, request, context }) => {
  const f = await seed(page, request), first = await context.newPage(), second = await context.newPage();
  try {
    await first.goto(`/?project=${f.project.id}&canvas=${f.project.entryCanvasId}`); await second.goto(`/?project=${f.project.id}&canvas=${f.chapter.id}`);
    await expect(first.locator(`.ms-node[data-node-id="${f.firstContent.id}"]`)).toHaveCount(1); await expect(second.locator(`.ms-node[data-node-id="${f.content.id}"]`)).toHaveCount(1);
    await selectEditorNode(first, f.firstContent.id, f.firstContent.title); await first.locator('.inspector').getByLabel('Title', { exact: true }).fill('Committed by first window'); await first.locator('.inspector').getByLabel('Title', { exact: true }).press('Tab'); await saved(first);
    await selectEditorNode(second, f.content.id, f.content.title); await second.locator('.inspector').getByLabel('Title', { exact: true }).fill('Retained by second window'); await second.locator('.inspector').getByLabel('Title', { exact: true }).press('Tab');
    await expect(second.locator('.save-state')).toHaveText('Save conflict · draft kept'); await expect(second.locator('.inspector').getByLabel('Title', { exact: true })).toHaveValue('Retained by second window');
    const committed = await (await request.get(`/api/projects/${f.project.id}`)).json(); expect(committed.nodes.find((node: { id: string }) => node.id === f.firstContent.id).title).toBe('Committed by first window'); expect(committed.nodes.find((node: { id: string }) => node.id === f.content.id).title).toBe(f.content.title);
    expect(new URL(page.url()).searchParams.get('project')).toBeNull(); await expect(page.locator('.workspace')).toBeVisible();
  } finally { await first.close(); await second.close(); }
});

test('a stale home window retains its draft, supports portable export, and leaves the committed graph intact', async ({ page, request, context }, info) => {
  const f = await seed(page, request), other = await context.newPage();
  try {
    await other.goto('/'); await saved(other); await select(other, f.note);
    await other.locator('.inspector').getByLabel('Title', { exact: true }).fill('Committed home note'); await other.locator('.inspector').getByLabel('Title', { exact: true }).press('Tab'); await saved(other);
    const committed = await stored(request); await select(page, f.note);
    await page.locator('.inspector').getByLabel('Title', { exact: true }).fill('Retained local home note'); await page.locator('.inspector').getByLabel('Title', { exact: true }).press('Tab');
    await expect(page.locator('.save-state')).toHaveText('Save conflict · draft kept'); expect(await stored(request)).toEqual(committed);
    await overflow(page, 'Export Work'); const exporting = page.waitForEvent('download');
    await page.getByRole('dialog').getByRole('button', { name: 'Download Work', exact: true }).click(); const download = await exporting;
    const path = info.outputPath('retained-home-draft.meridian'); await download.saveAs(path);
    const imported = await request.post('/api/import', { multipart: { file: { name: 'retained-home-draft.meridian', mimeType: 'application/octet-stream', buffer: await readFile(path) } } }); expect(imported.ok()).toBeTruthy();
    const portableDraft = await imported.json();
    expect(portableDraft.nodes.find((node: { id: string }) => node.id === f.note.id).title).toBe('Retained local home note');
    expect(portableDraft.nodes.find((node: { id: string }) => node.id === f.entry.id).canvasTarget).toEqual(committed.canvasTargets[f.entry.id]);
    expect(await stored(request)).toEqual(committed); await expect(page.locator('.inspector').getByLabel('Title', { exact: true })).toHaveValue('Retained local home note');
  } finally { await other.close(); }
});
