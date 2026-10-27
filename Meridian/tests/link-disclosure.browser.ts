import { workspaceAction } from './ui-helpers.ts';
import { test, expect, type Page } from '@playwright/test';
import { readFile, writeFile } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';
import { addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import { buildPublication } from '../src/publication.ts';

test('owned connections disclose in place and hidden endpoints never become parent proxy lines in editor and offline viewer', async ({ page, request, browser }, info) => {
  const project = newProject('Connection disclosure'), canvasId = project.entryCanvasId;
  const parent = addNode(project, canvasId, 'text', 'Genealogy');
  Object.assign(parent, { childLayout: 'spatial', collapsed: true, x: 100, y: 150, width: 260, height: 160, text: 'The children and their relationships appear together.' });
  const child = addNode(project, canvasId, 'text', 'Daughter');
  Object.assign(child, { parentId: parent.id, childLayout: 'spatial', placement: 'free', collapsed: true, x: 440, y: 140, width: 240, height: 140 });
  const grandchild = addNode(project, canvasId, 'text', 'Granddaughter');
  Object.assign(grandchild, { parentId: child.id, placement: 'free', x: 770, y: 130, width: 240, height: 140 });
  const outside = addNode(project, canvasId, 'text', 'Another branch');
  Object.assign(outside, { x: 450, y: 440, width: 240, height: 130 });
  const owner = addNode(project, canvasId, 'text', 'Reveal comparison');
  Object.assign(owner, { childLayout: 'spatial', collapsed: true, x: 100, y: 440, width: 260, height: 150, text: 'This tile owns a connection and no child nodes.' });
  project.links.push(
    { id: 'first-generation', canvasId, sourceId: parent.id, targetId: child.id, kind: 'visual', relation: 'causal' },
    { id: 'second-generation', canvasId, sourceId: child.id, targetId: grandchild.id, kind: 'visual', relation: 'causal' },
    { id: 'boundary-link', canvasId, sourceId: child.id, targetId: outside.id, kind: 'visual', relation: 'mutual' },
    { id: 'owned-comparison', canvasId, parentId: owner.id, sourceId: parent.id, targetId: outside.id, kind: 'visual', relation: 'generic' },
  );
  const card = (surface: Page, id: string) => surface.locator(`.ms-node[data-node-id="${id}"]`);
  const line = (surface: Page, id: string) => surface.locator(`.ms-link-line[data-link-id="${id}"]`);
  const errors: string[] = [];
  const exercise = async (surface: Page, viewer = false) => {
    surface.on('pageerror', error => errors.push(error.message));
    await expect(card(surface, parent.id)).toBeVisible();
    const initialCamera = await surface.locator('.ms-world').getAttribute('style'), initialUrl = surface.url();
    for (const link of project.links) await expect(line(surface, link.id)).toHaveCount(0);
    await expect(card(surface, owner.id)).toHaveAttribute('aria-expanded', 'false');
    await card(surface, owner.id).locator('.ms-node-title').click();
    await expect(line(surface, 'owned-comparison')).toHaveCount(0);
    if (!viewer) await surface.getByRole('combobox',{name:'Side panel',exact:true}).selectOption('links');
    const inspectConnection = viewer ? surface.getByRole('button',{name:'Select connection Genealogy / Another branch',exact:true}) : surface.locator('[data-associated-link="owned-comparison"] .inspector-link-title');
    await expect(inspectConnection).toBeVisible(); await inspectConnection.click();
    if(viewer){
      await expect(surface.locator('.mv-node-context')).toContainText('Contained in Reveal comparison');
      await surface.getByRole('button',{name:'Inspect containing node Reveal comparison',exact:true}).click();
    }else{
      await expect(surface.getByRole('combobox',{name:'Contained in',exact:true})).toHaveValue(owner.id);
      await surface.getByRole('button',{name:'Back to associated links',exact:true}).click();
    }
    if (viewer) await surface.getByRole('button', { name: 'Close project navigation', exact: true }).click();
    // Keyboard disclosure avoids overlay placement and still exercises the shared scene contract.
    await card(surface, owner.id).press('Alt+ArrowRight');
    await expect(line(surface, 'owned-comparison')).toHaveCount(1);
    await card(surface, owner.id).press('Alt+ArrowLeft');
    await expect(line(surface, 'owned-comparison')).toHaveCount(0);
    await card(surface, parent.id).locator('.ms-node-title').dblclick();
    await expect(line(surface, 'first-generation')).toHaveCount(1);
    await expect(line(surface, 'boundary-link')).toHaveCount(1);
    await expect(line(surface, 'second-generation')).toHaveCount(0);
    await card(surface, child.id).press('Alt+ArrowRight');
    await expect(line(surface, 'second-generation')).toHaveCount(1);
    await card(surface, parent.id).press('Alt+ArrowLeft');
    for (const link of project.links) await expect(line(surface, link.id)).toHaveCount(0);
    await expect(card(surface, child.id)).toHaveCount(0);
    await expect(card(surface, grandchild.id)).toHaveCount(0);
    expect(surface.url()).toBe(initialUrl);
    expect(await surface.locator('.ms-world').getAttribute('style')).toBe(initialCamera);
  };
  await page.setViewportSize({ width: 1900, height: 1100 });
  expect((await request.post('/api/projects', { data: commitSnapshots(project) })).ok()).toBeTruthy();
  await page.goto(`/?project=${project.id}`); await page.getByRole('button', { name: 'Fit Work', exact: true }).click();
  await exercise(page);
  await workspaceAction(page,'Save'); await expect(page.locator('.save-state')).toHaveText('Saved · r2');
  const saved = await (await request.get(`/api/projects/${project.id}`)).json();
  expect(saved.links.find((link: { id: string }) => link.id === 'owned-comparison').parentId).toBe(owner.id);
  await page.reload(); await expect(line(page, 'owned-comparison')).toHaveCount(0);
  const published = info.outputPath('owned-connections.html');
  await writeFile(published, buildPublication(project, await readFile('dist/viewer.js', 'utf8'), await readFile('dist/scene.css', 'utf8')));
  const context = await browser.newContext({ offline: true, viewport: { width: 1900, height: 1100 } });
  try {
    const viewer = await context.newPage(); await viewer.goto(pathToFileURL(published).href);
    await exercise(viewer, true);
    await viewer.screenshot({ path: info.outputPath('collapsed-relationships.png'), fullPage: true });
  } finally { await context.close(); }
  expect(errors).toEqual([]);
});

test('connection ownership is editable, contained connections toggle from properties and deleting their owner preserves endpoints', async ({page,request})=>{
  const project=newProject('Connection owner editing'),canvasId=project.entryCanvasId;
  const source=addNode(project,canvasId,'text','Source'),target=addNode(project,canvasId,'text','Target'),owner=addNode(project,canvasId,'text','Relationships');
  Object.assign(source,{x:100,y:100});Object.assign(target,{x:600,y:100});Object.assign(owner,{x:100,y:400,collapsed:true,childLayout:'spatial'});
  project.links.push({id:'editable-owner',canvasId,sourceId:source.id,targetId:target.id,parentId:owner.id,kind:'visual',relation:'generic',label:'A comparison'});
  expect((await request.post('/api/projects',{data:commitSnapshots(project)})).ok()).toBeTruthy();
  await page.goto(`/?project=${project.id}`);
  const card=(id:string)=>page.locator(`.ms-node[data-node-id="${id}"]`),line=page.locator('.ms-link-line[data-link-id="editable-owner"]');
  await card(owner.id).locator('.ms-node-title').click();
  await page.getByRole('combobox',{name:'Side panel',exact:true}).selectOption('links');
  await expect(page.locator('[data-associated-link="editable-owner"]')).toContainText('contains this link');
  await expect(line).toHaveCount(0);
  await page.locator('[data-associated-link="editable-owner"] .inspector-link-title').click();
  await page.getByRole('combobox',{name:'Contained in',exact:true}).selectOption('');await expect(line).toHaveCount(1);
  await page.getByRole('combobox',{name:'Contained in',exact:true}).selectOption(owner.id);await expect(line).toHaveCount(0);
  await page.getByRole('button',{name:'Back to associated links',exact:true}).click();
  await card(owner.id).press('Alt+ArrowRight');await expect(line).toHaveCount(1);
  await card(owner.id).click({button:'right'});await page.getByRole('menuitem',{name:'Delete',exact:true}).click();
  await expect(card(owner.id)).toHaveCount(0);await expect(line).toHaveCount(0);
  await expect(card(source.id)).toBeVisible();await expect(card(target.id)).toBeVisible();
  await workspaceAction(page,'Save');await expect(page.locator('.save-state')).toHaveText('Saved · r2');
  const saved=await(await request.get(`/api/projects/${project.id}`)).json();expect(saved.links).toEqual([]);expect(saved.nodes).toHaveLength(3);
});

test('large closed hierarchies keep discovery contextual while right-panel search reveals a precise hidden branch',async({page,request})=>{
  const project=newProject('Large closed hierarchy'),canvasId=project.entryCanvasId;
  for(let group=0;group<60;group++){
    const parent=addNode(project,canvasId,'text',`Chapter ${group}`);Object.assign(parent,{collapsed:true,childLayout:'spatial',x:100+(group%10)*300,y:100+Math.floor(group/10)*220});
    for(let detail=0;detail<96;detail++){const child=addNode(project,canvasId,'text',`Passage ${group}.${detail}`);Object.assign(child,{parentId:parent.id,collapsed:true,childLayout:'spatial',x:parent.x+300,y:parent.y+detail*140,text:group===31&&detail===27?'Unique concealed passage token':''});}
  }
  expect((await request.post('/api/projects',{data:commitSnapshots(project)})).ok()).toBeTruthy();
  await page.goto(`/?project=${project.id}`);await expect(page.locator('.sidebar, .outline')).toHaveCount(0);await expect(page.locator('.ms-node')).toHaveCount(60);
  await page.getByRole('combobox',{name:'Side panel',exact:true}).selectOption('canvas');
  const sought=project.nodes.find(node=>node.title==='Passage 31.27')!;
  await page.getByRole('searchbox',{name:'Search Work nodes'}).fill(sought.id);await expect(page.locator('[data-search-match="true"]')).toHaveCount(1);
  await page.locator(`button[data-jump-to-node="${sought.id}"]`).click();await page.getByRole('combobox',{name:'Side panel',exact:true}).selectOption('properties');await expect(page.getByLabel('GUID',{exact:true})).toHaveValue(sought.id);
  await page.getByRole('combobox',{name:'Side panel',exact:true}).selectOption('content');await expect(page.locator('.inspector').getByLabel('Title',{exact:true})).toHaveValue('Passage 31.27');
  await page.getByRole('combobox',{name:'Side panel',exact:true}).selectOption('canvas');await page.getByRole('searchbox',{name:'Search Work nodes'}).fill('');await expect(page.locator('[data-search-match="true"]')).toHaveCount(0);await expect(page.locator('.ms-node')).toHaveCount(156);
});
