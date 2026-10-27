import { workspaceAction, toolsAction } from './ui-helpers.ts';
import { selectEditorNode } from './ui-helpers.ts';
import { test, expect, type Page } from '@playwright/test';
import { readFile } from 'node:fs/promises';
import { addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import { buildPublication, readPublication } from '../src/publication.ts';

async function compositionTools(page:Page){await toolsAction(page,'Composition tools');}

test('appearance, labeled links and canvas themes survive saving and offline publication',async({page,request},info)=>{
  const project=newProject('Visual authoring QA');
  project.canvases[0].style={background:'#e8ddc2',grid:'#c4b89e',gridVisible:false};
  const a=addNode(project,project.entryCanvasId,'text','A larger idea'),b=addNode(project,project.entryCanvasId,'text','A related idea');
  Object.assign(a,{x:80,y:80,width:370,height:220,text:'Typography gives this idea a clear place in the reading order.'});
  Object.assign(b,{x:650,y:120,width:260,height:160,text:'The relationship has an explicit meaning.'});
  const link={id:crypto.randomUUID(),canvasId:project.entryCanvasId,sourceId:a.id,targetId:b.id,kind:'visual' as const,relation:'causal' as const};project.links.push(link);
  const created=await request.post('/api/projects',{data:commitSnapshots(project)});expect(created.ok()).toBeTruthy();
  await page.goto(`/?project=${project.id}`);
  await selectEditorNode(page,a.id,a.title);
  await page.getByLabel('Side panel',{exact:true}).selectOption('appearance');
  await page.getByRole('combobox',{name:'Font',exact:true}).click();
  await page.getByRole('option',{name:'Serif',exact:true}).click();
  await page.getByLabel('Title size',{exact:true}).fill('34');await page.getByLabel('Title size',{exact:true}).press('Tab');
  await page.getByLabel('Body size',{exact:true}).fill('17');await page.getByLabel('Body size',{exact:true}).press('Tab');
  await expect(page.locator(`[data-node-id="${a.id}"] .ms-node-title`)).toHaveCSS('font-size','34px');
  await expect(page.locator(`[data-node-id="${a.id}"] .ms-text`)).toHaveCSS('font-size','17px');
  await page.locator(`.ms-link-hit[data-link-id="${link.id}"]`).first().click();
  await page.getByLabel('Connection label',{exact:true}).fill('influences');await page.getByLabel('Connection label',{exact:true}).press('Tab');
  await expect(page.locator('.ms-link-label')).toContainText('influences');
  await workspaceAction(page,'Save');await expect(page.locator('.save-state')).toHaveText('Saved · r2');
  const saved=await(await request.get(`/api/projects/${project.id}`)).json();
  expect(saved.nodes.find((n:{id:string})=>n.id===a.id).style).toMatchObject({fontFamily:'serif',titleSize:34,fontSize:17});
  expect(saved.links[0].label).toBe('influences');
  const malformed=structuredClone(saved);malformed.nodes[1].style={fontSize:999};
  const rejected=await request.put(`/api/projects/${project.id}`,{data:{project:malformed,expectedRevision:2}});expect(rejected.status()).toBe(400);
  const html=buildPublication(saved,await readFile('dist/viewer.js','utf8'),await readFile('dist/scene.css','utf8'));
  const imported=readPublication(html);expect(imported.nodes.find(n=>n.title===a.title)?.style?.titleSize).toBe(34);expect(imported.links[0].label).toBe('influences');
  await page.route(/^https?:/,route=>route.abort());
  await page.setContent(html);await expect(page.locator('.ms-link-label')).toContainText('influences');
  await expect(page.locator(`[data-node-id="${a.id}"] .ms-node-title`)).toHaveCSS('font-size','34px');
  await page.screenshot({path:info.outputPath('styled-offline.png')});
});

test('a recipe previews in the shared viewer, applies atomically, and undoes as one change',async({page,request},info)=>{
  const project=newProject('Composition recipe QA');
  await request.post('/api/projects',{data:commitSnapshots(project)});await page.goto(`/?project=${project.id}`);
  await compositionTools(page);
  const dialog=page.getByRole('dialog',{name:'Composition tools',exact:true});
  const recipe={version:1,canvases:[{key:'here',existingId:project.entryCanvasId}],nodes:[
    {key:'cause',canvas:'here',kind:'text',title:'Cause',text:'One intentional point',width:300,height:160,style:{fontFamily:'serif',titleSize:30}},
    {key:'effect',canvas:'here',kind:'text',title:'Effect',text:'A consequence',width:220,height:130}
  ],links:[{key:'meaning',source:'cause',target:'effect',label:'leads to'}],layouts:[{kind:'row',nodes:['cause','effect'],x:60,y:80,gap:100}]};
  await dialog.getByLabel('Composition recipe',{exact:true}).fill(JSON.stringify(recipe));
  await dialog.getByRole('button',{name:'Preview recipe',exact:true}).click();
  await expect(dialog.getByRole('button',{name:'Apply to draft',exact:true})).toBeEnabled();
  await expect(page.frameLocator('iframe[title="Composition preview"]').locator('.ms-link-label')).toContainText('leads to');
  await page.screenshot({path:info.outputPath('composition-preview.png')});
  await dialog.getByRole('button',{name:'Apply to draft',exact:true}).click();
  await expect(page.locator('.ms-node')).toHaveCount(2);await expect(page.locator('.ms-link-label')).toContainText('leads to');
  await page.getByRole('button',{name:'Undo',exact:true}).click();await expect(page.locator('.ms-node')).toHaveCount(0);
  await page.getByRole('button',{name:'Redo',exact:true}).click();await expect(page.locator('.ms-node')).toHaveCount(2);
  await workspaceAction(page,'Save');await expect(page.locator('.save-state')).toHaveText('Saved · r2');
  await page.reload();await expect(page.locator('.ms-link-label')).toContainText('leads to');
});

test('invalid recipes and cancelled previews leave the saved project untouched',async({page,request})=>{
  const project=newProject('Recipe safety QA');await request.post('/api/projects',{data:commitSnapshots(project)});
  await page.goto(`/?project=${project.id}`);await compositionTools(page);
  const dialog=page.getByRole('dialog',{name:'Composition tools',exact:true});
  await dialog.getByLabel('Composition recipe',{exact:true}).fill(JSON.stringify({version:1,links:[{key:'bad',source:'missing',target:'missing'}]}));
  await dialog.getByRole('button',{name:'Preview recipe',exact:true}).click();
  await expect(dialog.getByRole('button',{name:'Apply to draft',exact:true})).toBeDisabled();
  await expect(dialog.locator('.composition-error')).not.toBeEmpty();
  await dialog.getByLabel('Composition recipe',{exact:true}).fill(JSON.stringify({version:1,title:'Temporary title'}));
  await dialog.getByRole('button',{name:'Preview recipe',exact:true}).click();await expect(dialog.getByRole('button',{name:'Apply to draft',exact:true})).toBeEnabled();
  await dialog.getByRole('button',{name:'Close',exact:true}).click();await expect(page.locator('.save-state')).toHaveText('Saved · r1');
  await expect(page.locator('[data-current-canvas]')).toContainText(project.canvases[0].title);
  expect((await (await request.get(`/api/projects/${project.id}`)).json()).title).toBe('Recipe safety QA');
  await expect(page.getByRole('button',{name:'Undo',exact:true})).toBeDisabled();
});

test('a save completing during recipe preview cannot restore stale revisions or snapshots',async({page,request})=>{
  const project=newProject('Composition save race');const node=addNode(project,project.entryCanvasId,'text','Original');
  await request.post('/api/projects',{data:commitSnapshots(project)});await page.goto(`/?project=${project.id}`);
  await selectEditorNode(page,node.id,node.title);
  await page.getByLabel('Title',{exact:true}).fill('Edited and saved');await page.getByLabel('Title',{exact:true}).press('Tab');
  let release!:()=>void,received!:()=>void;
  const gate=new Promise<void>(resolve=>{release=resolve;}),arrived=new Promise<void>(resolve=>{received=resolve;});
  await page.route(`**/api/projects/${project.id}`,async route=>{
    if(route.request().method()!=='PUT'){await route.continue();return;}
    const response=await route.fetch();received();await gate;await route.fulfill({response});
  });
  await workspaceAction(page,'Save');await arrived;
  await compositionTools(page);
  const dialog=page.getByRole('dialog',{name:'Composition tools',exact:true});
  await dialog.getByLabel('Composition recipe',{exact:true}).fill(JSON.stringify({version:1,nodes:[{key:'old',existingId:node.id,title:'Recipe from older revision'}]}));
  await dialog.getByRole('button',{name:'Preview recipe',exact:true}).click();await expect(dialog.getByRole('button',{name:'Apply to draft',exact:true})).toBeEnabled();
  release();await expect(page.locator('.save-state')).toHaveText('Saved · r2');
  await dialog.getByRole('button',{name:'Apply to draft',exact:true}).click();
  await expect(page.getByRole('alert')).toContainText('saved revision changed');
  await expect(dialog).toBeVisible();await dialog.getByRole('button',{name:'Close',exact:true}).click();
  await expect(page.locator(`[data-node-id="${node.id}"] .ms-node-title`)).toHaveText('Edited and saved');
  const saved=await(await request.get(`/api/projects/${project.id}`)).json();expect(saved.revision).toBe(2);expect(saved.snapshots[node.id].node.title).toBe('Edited and saved');
});
