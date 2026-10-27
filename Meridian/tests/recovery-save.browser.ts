import { workspaceAction } from './ui-helpers.ts';
import { selectEditorNode } from './ui-helpers.ts';
import { test, expect, type APIRequestContext, type Page } from '@playwright/test';
import { addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import type { MeridianProject } from '../src/core/types.ts';
import type { Recovery } from '../src/storage.ts';

async function editableNote(page: Page, request: APIRequestContext, title: string) {
  const project = newProject(title), note = addNode(project, project.entryCanvasId, 'text', 'Working note');
  Object.assign(note, { x: 100, y: 100, width: 320, height: 220, text: 'Saved original' });
  const response = await request.post('/api/projects', { data: commitSnapshots(project) }); expect(response.ok()).toBeTruthy();
  const saved = await response.json() as MeridianProject;
  await page.goto(`/?project=${saved.id}`);
  await selectEditorNode(page,note.id,note.title);
  const text = page.locator('.inspector').getByLabel('Text body', { exact: true });
  return { saved, note, text, route: `/api/projects/${saved.id}` };
}

async function recovery(request: APIRequestContext, route: string): Promise<Recovery | null> {
  const response = await request.get(`${route}/recovery`);
  if (response.status() === 404) return null;
  expect(response.ok()).toBeTruthy(); return response.json();
}

test('retrying a newer edit removes this window\'s older recovery draft and reloads the committed content directly', async ({ page, request }) => {
  const { saved, note, text, route } = await editableNote(page, request, 'Own recovery superseded by save');
  const autosaved: string[] = [];
  page.on('request', request => {
    if (request.method() === 'PUT' && new URL(request.url()).pathname === `${route}/recovery`)
      autosaved.push(request.postDataJSON().project.nodes.find((node: { id: string }) => node.id === note.id).text);
  });
  await page.route(`**${route}`,intercepted=>intercepted.request().method()==='PUT'?intercepted.fulfill({status:503,contentType:'application/json',body:JSON.stringify({error:'Pause the first commit to exercise recovery'})}):intercepted.continue());
  await text.fill('Edit A reached recovery'); await text.press('Tab');
  await expect(page.locator('.save-state')).toHaveText('Not saved · retry');
  await expect.poll(async () => (await recovery(request, route))?.project.nodes.find(node => node.id === note.id)?.text).toBe('Edit A reached recovery');
  const oldDraft = (await recovery(request, route))!; expect(oldDraft.baseRevision).toBe(1);
  await page.unroute(`**${route}`);
  await text.fill('Edit B saved immediately'); await text.press('Tab');
  await workspaceAction(page,'Save');
  await expect(page.locator('.save-state')).toHaveText('Saved · r2');
  // This must exercise a save newer than the last autosave, rather than an identical draft cleanup.
  expect(autosaved).toEqual(['Edit A reached recovery']);
  expect(await recovery(request, route)).toBeNull();
  const committed = await (await request.get(route)).json() as MeridianProject;
  expect(committed.revision).toBe(2); expect(committed.nodes.find(node => node.id === note.id)?.text).toBe('Edit B saved immediately');
  const checked = page.waitForResponse(response => new URL(response.url()).pathname === `${route}/recovery` && response.request().method() === 'GET');
  await page.reload(); expect((await checked).status()).toBe(404);
  await expect(page.getByRole('dialog', { name: 'Recovery draft available', exact: true })).toHaveCount(0);
  await expect(page.locator('.save-state')).toHaveText('Saved · r2');
  await selectEditorNode(page,note.id,note.title); await expect(text).toHaveValue('Edit B saved immediately');
  expect(new URL(page.url()).searchParams.get('project')).toBe(saved.id);
});

test('editing during a held commit and failed queued save rebases its recovery onto the acknowledged revision', async ({ page, request }) => {
  const { note, text, route } = await editableNote(page, request, 'Own recovery and in-flight save');
  await page.route(`**${route}`,intercepted=>intercepted.request().method()==='PUT'?intercepted.fulfill({status:503,contentType:'application/json',body:JSON.stringify({error:'Pause the first commit to exercise recovery'})}):intercepted.continue());
  await text.fill('Edit A reached recovery'); await text.press('Tab');
  await expect(page.locator('.save-state')).toHaveText('Not saved · retry');
  await expect.poll(async () => (await recovery(request, route))?.project.nodes.find(node => node.id === note.id)?.text).toBe('Edit A reached recovery');
  const oldDraft = (await recovery(request, route))!;
  await page.unroute(`**${route}`);
  let release!: () => void, didCommit!: () => void;
  const held = new Promise<void>(resolve => { release = resolve; }), committed = new Promise<void>(resolve => { didCommit = resolve; });
  let commits=0;
  await page.route(`**${route}`, async intercepted => {
    if (intercepted.request().method() !== 'PUT') { await intercepted.continue(); return; }
    if(++commits>1){await intercepted.fulfill({status:503,contentType:'application/json',body:JSON.stringify({error:'Queued commit unavailable'})});return;}
    const response = await intercepted.fetch(); expect(response.ok()).toBeTruthy();
    didCommit(); await held; await intercepted.fulfill({ response });
  });
  try {
    await text.fill('Edit B sent to save'); await text.press('Tab');
    await workspaceAction(page,'Save'); await committed;
    await expect(page.locator('.save-state')).toHaveText('Saving…');
    const onDisk = await (await request.get(route)).json() as MeridianProject;
    expect(onDisk.revision).toBe(2); expect(onDisk.nodes.find(node => node.id === note.id)?.text).toBe('Edit B sent to save');
    expect((await recovery(request, route))?.savedAt).toBe(oldDraft.savedAt);
    await text.fill('Edit C written while saving'); await text.press('Tab');
    await expect(text).toHaveValue('Edit C written while saving');
    release();
    await expect(page.locator('.save-state')).toHaveText('Not saved · retry');
    await expect(text).toHaveValue('Edit C written while saving');
    await expect.poll(async () => {
      const draft = await recovery(request, route);
      return draft ? { baseRevision: draft.baseRevision, revision: draft.project.revision, text: draft.project.nodes.find(node => node.id === note.id)?.text } : null;
    }).toEqual({ baseRevision: 2, revision: 2, text: 'Edit C written while saving' });
    expect((await recovery(request, route))?.savedAt).not.toBe(oldDraft.savedAt);
    expect((await (await request.get(route)).json() as MeridianProject).nodes.find(node => node.id === note.id)?.text).toBe('Edit B sent to save');
    await expect(page.getByRole('alert')).toContainText('Queued commit unavailable');
    await page.unroute(`**${route}`);
    await workspaceAction(page,'Save'); await expect(page.locator('.save-state')).toHaveText('Saved · r3');
    expect(await recovery(request, route)).toBeNull();
    const final = await (await request.get(route)).json() as MeridianProject;
    expect(final.nodes.find(node => node.id === note.id)?.text).toBe('Edit C written while saving');
    await page.reload(); await expect(page.locator('.save-state')).toHaveText('Saved · r3');
    await expect(page.getByRole('dialog', { name: 'Recovery draft available', exact: true })).toHaveCount(0);
  } finally {
    release(); await page.unroute(`**${route}`);
  }
});
