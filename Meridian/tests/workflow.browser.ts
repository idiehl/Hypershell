import { workspaceAction } from './ui-helpers.ts';
import { selectEditorNode } from './ui-helpers.ts';
import { test, expect } from '@playwright/test';
import { createDemoProject } from '../src/core/demo.ts';
import { newProject, addNode, commitSnapshots } from '../src/core/model.ts';
import { buildPublication, readPublication } from '../src/publication.ts';
import type { MeridianProject } from '../src/core/types.ts';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';
import path from 'node:path';
import { createServer } from 'node:http';

test('editor creates, edits, saves, exports and imports without losing project content',async({page,request},testInfo)=>{
  const errors:string[]=[];page.on('pageerror',e=>errors.push(e.message));
  const response=await request.post('/api/projects',{data:commitSnapshots(createDemoProject())});expect(response.ok()).toBeTruthy();
  const authored=await response.json() as MeridianProject,id=authored.id;
  await page.goto(`/?project=${id}&canvas=${authored.entryCanvasId}`);
  // Canvas launchers navigate the current workspace after saving its pending edits.
  async function openCanvas(name:string){
    await page.keyboard.press('Control+s');await expect(page.locator('.save-state')).toHaveText(/^Saved · r\d+$/);
    const target=authored.canvases.find(canvas=>name===`Open ${canvas.title}`)!;
    expect(target).toBeDefined();const windowCount=page.context().pages().length;
    await page.getByRole('button',{name,exact:true}).click();
    await page.waitForURL(url=>url.searchParams.get('project')===id&&url.searchParams.get('canvas')===target.id);
    await expect(page.locator('.workspace')).toBeVisible();expect(page.context().pages()).toHaveLength(windowCount);
    await expect(page.locator('.save-state')).toHaveText(/^Saved · r\d+$/);
  }
  const source=authored.nodes.find(n=>n.title==='Evidence verified')!;
  const output=authored.nodes.find(n=>n.title==='Decision')!;
  const switchNode=authored.nodes.find(n=>n.kind==='switch')!;
  await openCanvas('Open Logic lab');
  await page.getByRole('button',{name:'Fit Work',exact:true}).click();
  await expect(page.locator(`[data-node-id="${output.id}"] .ms-state`)).toHaveText('On');
  await page.getByRole('switch',{name:'Evidence connection: toggle switch'}).click();
  await expect(page.locator(`[data-node-id="${output.id}"] .ms-state`)).toHaveText('Unknown');
  await page.getByRole('button',{name:'Undo',exact:true}).click();
  await expect(page.locator(`[data-node-id="${output.id}"] .ms-state`)).toHaveText('On');
  await page.locator(`[data-node-id="${authored.nodes.find(n=>n.title==='Boolean decision')!.id}"] .ms-node-title`).dblclick();
  await expect(page.locator(`[data-node-id="${switchNode.id}"]`)).toHaveCount(0);
  await expect(page.locator(`[data-node-id="${output.id}"] .ms-state`)).toHaveText('On');
  // Closed branches keep their descendants in Relations; reveal one explicitly.
  await selectEditorNode(page,authored.nodes.find(n=>n.title==='Boolean decision')!.id,'Boolean decision');
  await page.getByLabel('Side panel',{exact:true}).selectOption('relations');
  await page.getByRole('button',{name:'Jump to Node: Evidence connection',exact:true}).click();
  await expect(page.locator(`[data-node-id="${switchNode.id}"]`)).toBeVisible();
  await page.getByRole('button',{name:'Fit Work',exact:true}).click();
  await openCanvas('Open Start here');
  await page.getByRole('button',{name:'Fit Work',exact:true}).click();
  await page.getByRole('switch',{name:'Evidence verified: toggle input'}).click();
  await openCanvas('Open Logic lab');
  await page.getByRole('button',{name:'Fit Work',exact:true}).click();
  await page.keyboard.press('Control+s');
  await expect(page.locator('.save-state')).toHaveText(/^Saved · r\d+$/);
  await expect(page.locator(`[data-node-id="${output.id}"] .ms-state`)).toHaveText('Off');
  const saved=await (await request.get(`/api/projects/${id}`)).json() as MeridianProject;
  expect(saved.nodes.find(n=>n.id===source.id)?.value).toBe(false);
  await page.reload();await expect(page.locator('.save-state')).toHaveText(`Saved · r${saved.revision}`);
  await workspaceAction(page,'History');
  await page.getByRole('button',{name:'New checkpoint',exact:true}).click();
  await page.getByLabel('Name',{exact:true}).fill('Known working arrangement');await page.getByRole('button',{name:'Continue',exact:true}).click();
  await expect(page.locator('.checkpoint-row')).toContainText('Known working arrangement');
  await page.getByRole('button',{name:'Close',exact:true}).click();
  const exportEvent=page.waitForEvent('download');await workspaceAction(page,'Export Work');await page.getByRole('button',{name:'Download Work',exact:true}).click();const exported=await exportEvent;expect(exported.suggestedFilename()).toMatch(/\.meridian$/);
  const publicationEvent=page.waitForEvent('download');await workspaceAction(page,'Publish');await page.getByRole('button',{name:'Publish HTML',exact:true}).click();const published=await publicationEvent;
  const publishedPath=testInfo.outputPath('published.html');await published.saveAs(publishedPath);const html=await readFile(publishedPath,'utf8');const reimport=readPublication(html);expect(reimport.id).not.toBe(id);expect(reimport.nodes).toHaveLength(saved.nodes.length);expect(reimport.assets).toEqual(expect.arrayContaining(saved.assets.map(a=>expect.objectContaining({base64:a.base64}))));
  await page.getByRole('button',{name:'Done',exact:true}).click();
  const windowCount=page.context().pages().length,chooserEvent=page.waitForEvent('filechooser');await workspaceAction(page,'Import');await (await chooserEvent).setFiles(publishedPath);
  await expect.poll(()=>new URL(page.url()).searchParams.get('project')).not.toBe(id);
  await expect(page.locator('.save-state')).toHaveText('Saved · r1');
  const newId=new URL(page.url()).searchParams.get('project')!;expect(newId).not.toBe(id);
  const imported=await (await request.get(`/api/projects/${newId}`)).json() as MeridianProject;
  expect(imported.title).toContain('imported');
  await expect(page.locator('[data-current-canvas]')).toContainText(imported.canvases.find(canvas=>canvas.id===imported.entryCanvasId)!.title);
  expect(page.context().pages()).toHaveLength(windowCount);
  expect(imported.nodes.every(n=>!saved.nodes.some(old=>old.id===n.id))).toBeTruthy();
  expect((await (await request.get(`/api/projects/${id}`)).json()).revision).toBe(saved.revision);
  expect(errors).toEqual([]);
  await page.screenshot({path:testInfo.outputPath('editor.png'),fullPage:true});
});

test('standalone HTML works offline with transient controls and stays interactive in an iframe',async({browser},testInfo)=>{
  const project=createDemoProject();const js=await readFile('dist/viewer.js','utf8'),css=await readFile('dist/scene.css','utf8');
  const html=buildPublication(project,js,css,{includeExternalReferences:true});await mkdir(testInfo.outputDir,{recursive:true});const file=testInfo.outputPath('offline.html');await writeFile(file,html);
  const context=await browser.newContext({viewport:{width:1400,height:900},offline:true,acceptDownloads:true});
  const page=await context.newPage();const errors:string[]=[];const remote:string[]=[];
  page.on('pageerror',e=>errors.push(e.message));page.on('request',r=>{if(/^https?:/.test(r.url()))remote.push(r.url());});
  await page.goto(pathToFileURL(file).href);
  await expect(page.getByRole('status')).toHaveText('Stopped');
  const lab=project.canvases.find(c=>c.title==='Logic lab')!;const out=project.nodes.find(n=>n.title==='Decision')!;
  await page.getByLabel('Choose Work').selectOption(lab.id);
  await expect(page.locator(`[data-node-id="${out.id}"] .ms-state`)).toHaveText('Stopped');
  await page.getByRole('button',{name:'Run simulation',exact:true}).click();
  await expect(page.locator(`[data-node-id="${out.id}"] .ms-state`)).toHaveText('On');
  await page.getByRole('switch',{name:'Ready to proceed: toggle input'}).click();
  await expect(page.locator(`[data-node-id="${out.id}"] .ms-state`)).toHaveText('Off');
  await page.getByRole('button',{name:'Stop simulation',exact:true}).click();
  await expect(page.locator(`[data-node-id="${out.id}"] .ms-state`)).toHaveText('Stopped');
  await expect(page.getByRole('switch',{name:'Ready to proceed: toggle input'})).toHaveAttribute('aria-checked','false');
  await page.getByRole('switch',{name:'Ready to proceed: toggle input'}).click();
  await expect(page.locator(`[data-node-id="${out.id}"] .ms-state`)).toHaveText('Stopped');
  await page.getByRole('button',{name:'Run simulation',exact:true}).click();
  await page.locator(`[data-node-id="${project.nodes.find(n=>n.title==='Boolean decision')!.id}"] .ms-node-title`).dblclick();
  await expect(page.locator(`[data-node-id="${out.id}"] .ms-state`)).toHaveText('On');
  const copiedEvent=page.waitForEvent('download');await page.getByRole('button',{name:'Download this published canvas for viewing or importing into the Meridian editor'}).click();const copied=await copiedEvent;const copyPath=testInfo.outputPath('viewer-copy.html');await copied.saveAs(copyPath);
  await page.goto(pathToFileURL(copyPath).href);await expect(page.getByRole('status')).toHaveText('Stopped');await page.getByLabel('Choose Work').selectOption(lab.id);await expect(page.locator(`[data-node-id="${project.nodes.find(n=>n.title==='Boolean decision')!.id}"]`)).toHaveAttribute('aria-expanded','true');
  await page.screenshot({path:testInfo.outputPath('offline-viewer.png')});expect(remote).toEqual([]);expect(errors).toEqual([]);await context.close();
  const server=createServer((req,res)=>{res.setHeader('Content-Type','text/html');res.end(req.url==='/canvas.html'?html:'<!doctype html><html><head><style>body{margin:0}iframe{border:0;width:100%;height:100vh}</style></head><body><iframe title="Embedded Meridian" src="/canvas.html"></iframe></body></html>');});
  await new Promise<void>(resolve=>server.listen(0,'127.0.0.1',resolve));const port=(server.address() as {port:number}).port;
  const embedContext=await browser.newContext({viewport:{width:480,height:780}});try{const embedded=await embedContext.newPage();await embedded.goto(`http://127.0.0.1:${port}/`);const frame=embedded.frameLocator('iframe');await expect(frame.getByRole('button',{name:'Run simulation',exact:true})).toBeVisible();await frame.getByRole('button',{name:'Run simulation',exact:true}).click();await expect(frame.getByRole('status')).toHaveText('Running');await embedded.screenshot({path:testInfo.outputPath('narrow-embed.png')});}finally{await embedContext.close();await new Promise<void>(resolve=>server.close(()=>resolve()));}
});

test('stale automatic saves preserve a separate window draft and never overwrite another window recovery',async({page,request})=>{
  const p=newProject('Recovery scenario');const n=addNode(p,p.entryCanvasId,'text','Draft note');n.text='Original';
  const created=await (await request.post('/api/projects',{data:commitSnapshots(p)})).json() as MeridianProject;
  const route=`/api/projects/${created.id}`;
  await page.goto(`/?project=${created.id}`);await selectEditorNode(page,n.id,n.title);
  const otherDraft=structuredClone(created);otherDraft.nodes.find(node=>node.id===n.id)!.text='A different window recovery';
  const recoveryWrite=await request.put(`${route}/recovery`,{data:{project:otherDraft,baseRevision:1,expectedSavedAt:null}});expect(recoveryWrite.ok()).toBeTruthy();
  const otherRecovery=await recoveryWrite.json();
  const remote=structuredClone(created);remote.title='Newer saved version';
  expect((await request.put(route,{data:{project:remote,expectedRevision:1}})).ok()).toBeTruthy();
  await page.getByLabel('Text body',{exact:true}).fill('A draft survives closing the window');await page.getByLabel('Text body',{exact:true}).press('Tab');
  await expect(page.locator('.save-state')).toHaveText('Save conflict · draft kept');
  expect((await (await request.get(route)).json()).title).toBe('Newer saved version');
  page.on('dialog',d=>void d.accept());await page.reload();await page.getByRole('button',{name:'Restore draft',exact:true}).click();
  await workspaceAction(page,'Save');await expect(page.getByRole('alert')).toContainText('older revision');
  await selectEditorNode(page,n.id,n.title);
  await page.getByLabel('Text body',{exact:true}).fill('Continued work on the old draft');await page.getByLabel('Text body',{exact:true}).press('Tab');
  await expect.poll(()=>page.evaluate(id=>Object.keys(localStorage).filter(key=>key.startsWith(`meridian.recovery.v1.${id}.`)).map(key=>JSON.parse(localStorage.getItem(key)!)).find(draft=>draft.project.nodes.some((node:{text?:string})=>node.text==='Continued work on the old draft'))?.baseRevision,created.id)).toBe(1);
  expect(await (await request.get(`${route}/recovery`)).json()).toEqual(otherRecovery);
  await page.reload();await page.getByRole('button',{name:'Restore draft',exact:true}).click();
  await selectEditorNode(page,n.id,n.title);await expect(page.getByLabel('Text body',{exact:true})).toHaveValue('Continued work on the old draft');
  await workspaceAction(page,'Save');await expect(page.getByRole('alert')).toContainText('older revision');
  expect((await (await request.get(route)).json()).revision).toBe(2);
});

test('failed host recovery and unavailable browser backup keep the edited canvas open when leaving',async({page,request})=>{
  const p=newProject('Failed recovery scenario');const n=addNode(p,p.entryCanvasId,'text','Keep this draft');n.text='Original';
  const created=await (await request.post('/api/projects',{data:commitSnapshots(p)})).json() as MeridianProject;
  await page.goto(`/?project=${created.id}`);
  await page.evaluate(()=>{const original=Storage.prototype.setItem;Storage.prototype.setItem=function(key,value){if(key.startsWith('meridian.recovery.v1.'))throw new DOMException('Test quota exhausted','QuotaExceededError');original.call(this,key,value);};});
  await page.route(`**/api/projects/${created.id}`,route=>route.request().method()==='PUT'?route.fulfill({status:503,contentType:'application/json',body:JSON.stringify({error:'Commit unavailable for this test'})}):route.continue());
  await page.route(`**/api/projects/${created.id}/recovery`,route=>route.request().method()==='PUT'?route.fulfill({status:503,contentType:'application/json',body:JSON.stringify({error:'Storage unavailable for this test'})}):route.continue());
  await selectEditorNode(page,n.id,n.title);await page.getByLabel('Text body',{exact:true}).fill('Do not lose this edit');await page.getByLabel('Text body',{exact:true}).press('Tab');
  await expect(page.locator('.save-state')).toHaveText('Not saved · retry');
  const address=page.url();await page.getByRole('button',{name:'Canvas library',exact:true}).click();
  await expect(page.getByRole('alert')).toContainText('before navigating');await expect(page.getByLabel('Text body',{exact:true})).toHaveValue('Do not lose this edit');
  await expect(page.getByRole('dialog',{name:'Leave this draft?',exact:true})).toHaveCount(0);expect(page.url()).toBe(address);
  expect(new URL(page.url()).searchParams.get('project')).toBe(created.id);
});

test('a queued automatic save preserves another window recovery after a delayed commit response',async({page,request})=>{
  const p=newProject('Save response race');const note=addNode(p,p.entryCanvasId,'text','Concurrent note');note.text='Saved original';
  const created=await (await request.post('/api/projects',{data:commitSnapshots(p)})).json() as MeridianProject;
  const route=`/api/projects/${created.id}`;
  await page.goto(`/?project=${created.id}`);await selectEditorNode(page,note.id,note.title);
  let release!:()=>void;const held=new Promise<void>(r=>{release=r;});let committed!:()=>void;const didCommit=new Promise<void>(r=>{committed=r;});let commits=0;
  await page.route(`**${route}`,async intercepted=>{if(intercepted.request().method()!=='PUT'){await intercepted.continue();return;}const response=await intercepted.fetch();expect(response.ok()).toBeTruthy();if(++commits===1){committed();await held;}await intercepted.fulfill({response});});
  try{
    await page.getByLabel('Text body',{exact:true}).fill('First edit');await page.getByLabel('Text body',{exact:true}).press('Tab');await didCommit;
    await page.getByLabel('Text body',{exact:true}).fill('Edit while saving');await page.getByLabel('Text body',{exact:true}).press('Tab');
    const remote=await (await request.get(route)).json() as MeridianProject;remote.nodes.find(n=>n.id===note.id)!.text='Another window recovery';
    const response=await request.put(`${route}/recovery`,{data:{project:remote,baseRevision:2,expectedSavedAt:null}});expect(response.ok()).toBeTruthy();const otherRecovery=await response.json();
    release();await expect(page.locator('.save-state')).toHaveText('Saved · r3');
    expect(commits).toBe(2);expect(await (await request.get(`${route}/recovery`)).json()).toEqual(otherRecovery);
    expect((await (await request.get(route)).json()).nodes.find((n:{id:string})=>n.id===note.id).text).toBe('Edit while saving');
    await expect(page.getByLabel('Text body',{exact:true})).toHaveValue('Edit while saving');await expect(page.getByRole('alert')).toHaveCount(0);
  }finally{release();await page.unroute(`**${route}`);}
});

test('node geometry, free-boundary links and history are editable through the canvas',async({page,request})=>{
  const p=newProject('Canvas gestures');const a=addNode(p,p.entryCanvasId,'text','Source'),b=addNode(p,p.entryCanvasId,'text','Destination');Object.assign(a,{x:40,y:80,width:180,height:130,text:'Source'});Object.assign(b,{x:400,y:80,width:180,height:130,text:'Destination'});
  const saved=await (await request.post('/api/projects',{data:commitSnapshots(p)})).json();await page.goto(`/?project=${saved.id}`);
  const card=page.locator(`[data-node-id="${a.id}"]`);const rect=await card.boundingBox();expect(rect).toBeTruthy();await page.mouse.move(rect!.x+50,rect!.y+15);await page.mouse.down();await page.mouse.move(rect!.x+95,rect!.y+65,{steps:6});await page.mouse.up();
  await page.getByLabel('Side panel',{exact:true}).selectOption('appearance');
  await expect(page.getByLabel('X',{exact:true})).toHaveValue('85');await expect(page.getByLabel('Y',{exact:true})).toHaveValue('130');await page.getByRole('button',{name:'Undo',exact:true}).click();await expect(page.getByLabel('X',{exact:true})).toHaveValue('40');
  await card.locator('.ms-node-title').click({button:'right'});await page.getByRole('menuitem',{name:'Create Link',exact:true}).click();
  const linkForm=page.getByRole('dialog',{name:'New Link',exact:true});await expect(linkForm.getByLabel('Source',{exact:true})).toHaveValue(a.id);
  await linkForm.getByLabel('Destination',{exact:true}).selectOption(b.id);await linkForm.getByRole('button',{name:'Create link',exact:true}).click();
  await expect(page.locator('.ms-link-hit')).toHaveCount(1);
  await page.getByLabel('Side panel',{exact:true}).selectOption('links');
  const routePanel=page.locator('.inspector details').filter({hasText:'Attachment points and route'});
  // Free-boundary attachments remain editable at distinct heights on the chosen edges.
  for(const [axis,value] of [['Source X %','100'],['Source Y %','27'],['Destination X %','0'],['Destination Y %','54']] as const){if(await routePanel.getAttribute('open')===null)await routePanel.locator('summary').click();const field=page.locator('.inspector').getByLabel(axis,{exact:true});await field.fill(value);await field.press('Tab');}
  if(await routePanel.getAttribute('open')===null)await routePanel.locator('summary').click();
  await page.getByRole('button',{name:'+ Bend point',exact:true}).click();await expect(page.locator('.ms-route-handle')).toHaveCount(1);
  await page.keyboard.press('Control+s');await expect(page.locator('.save-state')).toHaveText(/^Saved · r\d+$/);const after=await (await request.get(`/api/projects/${saved.id}`)).json();expect(after.links).toHaveLength(1);expect(after.links[0].sourceAnchor).toEqual({x:1,y:.27});expect(after.links[0].targetAnchor).toEqual({x:0,y:.54});expect(after.links[0].points).toHaveLength(1);
});
