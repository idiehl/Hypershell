import { workspaceAction } from './ui-helpers.ts';
import { selectEditorNode } from './ui-helpers.ts';
import { test, expect, type APIRequestContext, type Page } from '@playwright/test';
import { addCanvas, addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import { createDemoProject } from '../src/core/demo.ts';
import type { MeridianProject } from '../src/core/types.ts';

async function setup(page:Page,request:APIRequestContext,title:string){
  const project=newProject(title),note=addNode(project,project.entryCanvasId,'text','Working note');
  Object.assign(note,{x:100,y:100,width:320,height:220,text:'Saved original'});
  const response=await request.post('/api/projects',{data:commitSnapshots(project)});expect(response.ok()).toBeTruthy();
  const saved=await response.json() as MeridianProject,route=`/api/projects/${saved.id}`;
  await page.goto(`/?project=${saved.id}`);await selectEditorNode(page,note.id,note.title);
  return {saved,note,route,text:page.locator('.inspector').getByLabel('Text body',{exact:true})};
}
async function current(request:APIRequestContext,route:string){return (await (await request.get(route)).json()) as MeridianProject;}

test('committed changes save without pressing Save and refresh saved cross-canvas values',async({page,request})=>{
  const project=createDemoProject(),response=await request.post('/api/projects',{data:commitSnapshots(project)});
  const saved=await response.json() as MeridianProject;
  const source=saved.nodes.find(node=>node.title==='Evidence verified')!,output=saved.nodes.find(node=>node.title==='Decision')!;
  await page.goto(`/?project=${saved.id}`);
  await page.getByRole('button',{name:'Fit Work',exact:true}).click();
  await page.getByRole('switch',{name:'Evidence verified: toggle input'}).click();
  await expect(page.locator('.save-state')).toHaveText('Saved · r2');
  const result=await current(request,`/api/projects/${saved.id}`);expect(result.nodes.find(node=>node.id===source.id)?.value).toBe(false);
  expect(result.snapshots[source.id].value).toBe(false);
  const sourceUrl=page.url(),windowCount=page.context().pages().length;
  await page.getByRole('button',{name:'Open Logic lab',exact:true}).click();
  await page.waitForURL(url=>url.searchParams.get('canvas')===output.canvasId);
  await expect(page.locator(`[data-node-id="${output.id}"] .ms-state`)).toHaveText('Off');
  expect(page.context().pages()).toHaveLength(windowCount);
  await page.goBack();await page.waitForURL(sourceUrl);
  await page.goForward();await page.waitForURL(url=>url.searchParams.get('canvas')===output.canvasId);
  await expect(page.locator(`[data-node-id="${output.id}"] .ms-state`)).toHaveText('Off');
});

test('opening a canvas waits for a focused edit to finish saving before same-window navigation',async({page,request})=>{
  const project=newProject('Canvas navigation saves focused editing'),home=project.entryCanvasId;
  const note=addNode(project,home,'text','Pending navigation note');Object.assign(note,{x:60,y:80,width:300,height:180,text:'Saved original'});
  const destination=addCanvas(project,'Next canvas'),launch=addNode(project,home,'canvas-link','Continue working');
  Object.assign(launch,{x:470,y:80,width:280,height:180,targetId:destination.rootId});
  expect((await request.post('/api/projects',{data:commitSnapshots(project)})).ok()).toBeTruthy();
  await page.goto(`/?project=${project.id}&canvas=${home}`);await selectEditorNode(page,note.id,note.title);
  const route=`/api/projects/${project.id}`,text=page.locator('.inspector').getByLabel('Text body',{exact:true});
  let release!:()=>void,didCommit!:()=>void;
  const held=new Promise<void>(resolve=>{release=resolve;}),committed=new Promise<void>(resolve=>{didCommit=resolve;});
  await page.route(`**${route}`,async intercepted=>{
    if(intercepted.request().method()!=='PUT'){await intercepted.continue();return;}
    const response=await intercepted.fetch();didCommit();await held;await intercepted.fulfill({response});
  });
  try{
    await text.fill('Kept before the next canvas opens');
    const windowCount=page.context().pages().length;
    await page.getByRole('button',{name:`Open ${destination.title}`,exact:true}).click();await committed;
    expect(new URL(page.url()).searchParams.get('canvas')).toBe(home);
    await expect(page.locator('.save-state')).toHaveText('Saving…');release();
    await page.waitForURL(url=>url.searchParams.get('canvas')===destination.id);
    expect((await current(request,route)).nodes.find(node=>node.id===note.id)?.text).toBe('Kept before the next canvas opens');
    expect(page.context().pages()).toHaveLength(windowCount);
    await page.goBack();await page.waitForURL(url=>url.searchParams.get('canvas')===home);
    await selectEditorNode(page,note.id,note.title);await expect(text).toHaveValue('Kept before the next canvas opens');
  }finally{release();await page.unroute(`**${route}`);}
});

test('automatic saving leaves a focused field and its uncommitted typing intact',async({page,request})=>{
  const {note,text,route}=await setup(page,request,'Typing during automatic save');
  let release!:()=>void,didCommit!:()=>void;
  const held=new Promise<void>(resolve=>{release=resolve;}),committed=new Promise<void>(resolve=>{didCommit=resolve;});
  await page.route(`**${route}`,async intercepted=>{
    if(intercepted.request().method()!=='PUT'){await intercepted.continue();return;}
    const response=await intercepted.fetch();didCommit();await held;await intercepted.fulfill({response});
  });
  try{
    await text.fill('First committed edit');await text.press('Tab');await committed;
    await text.focus();await text.fill('Still typing this second edit');release();
    await expect(page.locator('.save-state')).toHaveText('Saved · r2');
    await expect(text).toBeFocused();await expect(text).toHaveValue('Still typing this second edit');
    expect((await current(request,route)).nodes.find(node=>node.id===note.id)?.text).toBe('First committed edit');
    await page.unroute(`**${route}`);await text.press('Tab');
    await expect.poll(async()=> (await current(request,route)).nodes.find(node=>node.id===note.id)?.text).toBe('Still typing this second edit');
    await expect(page.locator('.save-state')).toHaveText('Saved · r3');
  }finally{release();await page.unroute(`**${route}`);}
});

test('edits committed during an in-flight save are saved next using its returned revision',async({page,request})=>{
  const {note,text,route}=await setup(page,request,'Serial automatic saving');
  let release!:()=>void,didCommit!:()=>void,active=0,maxActive=0;
  const held=new Promise<void>(resolve=>{release=resolve;}),committed=new Promise<void>(resolve=>{didCommit=resolve;});
  const requests:{revision:number;text:string}[]=[];
  await page.route(`**${route}`,async intercepted=>{
    if(intercepted.request().method()!=='PUT'){await intercepted.continue();return;}
    active++;maxActive=Math.max(maxActive,active);
    const body=intercepted.request().postDataJSON();requests.push({revision:body.expectedRevision,text:body.project.nodes.find((node:{id:string})=>node.id===note.id).text});
    const response=await intercepted.fetch();
    if(requests.length===1){didCommit();await held;}
    await intercepted.fulfill({response});active--;
  });
  try{
    await text.fill('First edit');await text.press('Tab');await committed;
    await text.fill('Second edit');await text.press('Tab');await text.fill('Latest edit');await text.press('Tab');
    await expect(page.locator('.save-state')).toHaveText('Saving…');
    expect(requests).toEqual([{revision:1,text:'First edit'}]);release();
    await expect(page.locator('.save-state')).toHaveText('Saved · r3');
    expect(requests).toEqual([{revision:1,text:'First edit'},{revision:2,text:'Latest edit'}]);expect(maxActive).toBe(1);
    await page.reload();await expect(page.getByRole('dialog',{name:'Recovery draft available'})).toHaveCount(0);
    await selectEditorNode(page,note.id,note.title);await expect(text).toHaveValue('Latest edit');
  }finally{release();await page.unroute(`**${route}`);}
});

test('undo and redo each save their resulting content automatically',async({page,request})=>{
  const {note,text,route}=await setup(page,request,'Automatic undo redo');
  await text.fill('Changed automatically');await text.press('Tab');await expect(page.locator('.save-state')).toHaveText('Saved · r2');
  await page.getByRole('button',{name:'Undo',exact:true}).click();await expect(page.locator('.save-state')).toHaveText('Saved · r3');
  expect((await current(request,route)).nodes.find(node=>node.id===note.id)?.text).toBe('Saved original');
  await page.getByRole('button',{name:'Redo',exact:true}).click();await expect(page.locator('.save-state')).toHaveText('Saved · r4');
  expect((await current(request,route)).nodes.find(node=>node.id===note.id)?.text).toBe('Changed automatically');
});

test('Ctrl+S commits and saves a still-focused text field',async({page,request})=>{
  const {note,text,route}=await setup(page,request,'Keyboard save commits active text');
  await text.fill('Typed without leaving this field');await expect(text).toBeFocused();
  await page.keyboard.press('Control+s');await expect(page.locator('.save-state')).toHaveText('Saved · r2');
  expect((await current(request,route)).nodes.find(node=>node.id===note.id)?.text).toBe('Typed without leaving this field');
});

test('a save failure pauses background retry and an explicit Save retries the latest edit',async({page,request})=>{
  const {note,text,route}=await setup(page,request,'Automatic save offline retry');let attempts=0;
  await page.route(`**${route}`,async intercepted=>{
    if(intercepted.request().method()!=='PUT'){await intercepted.continue();return;}
    attempts++;await intercepted.fulfill({status:503,contentType:'application/json',body:JSON.stringify({error:'Test storage unavailable'})});
  });
  await text.fill('Unsent first edit');await text.press('Tab');await expect(page.locator('.save-state')).toHaveText('Not saved · retry');
  await text.fill('Latest unsent edit');await text.press('Tab');
  await expect.poll(async()=>{
    const response=await request.get(`${route}/recovery`);
    return response.ok()?(await response.json()).project.nodes.find((node:{id:string})=>node.id===note.id)?.text:null;
  }).toBe('Latest unsent edit');
  expect(attempts).toBe(1);expect((await current(request,route)).revision).toBe(1);
  await page.unroute(`**${route}`);await workspaceAction(page,'Save');
  await expect(page.locator('.save-state')).toHaveText('Saved · r2');
  expect((await current(request,route)).nodes.find(node=>node.id===note.id)?.text).toBe('Latest unsent edit');
});

test('two windows cannot overwrite each other and the conflicting window can restore its separate backup',async({page,context,request})=>{
  const {saved,note,text,route}=await setup(page,request,'Two automatic-save windows');
  const other=await context.newPage();await other.goto(`/?project=${saved.id}`);
  await selectEditorNode(other,note.id,note.title);
  const second=other.locator('.inspector').getByLabel('Text body',{exact:true});
  await text.fill('First window committed');await text.press('Tab');await expect(page.locator('.save-state')).toHaveText('Saved · r2');
  await second.fill('Second window draft');await second.press('Tab');
  await expect(other.locator('.save-state')).toHaveText('Save conflict · draft kept');
  expect((await current(request,route)).nodes.find(node=>node.id===note.id)?.text).toBe('First window committed');
  await second.fill('Continued second-window draft');await second.press('Tab');
  await workspaceAction(other,'Save');await expect(other.getByRole('alert')).toContainText('changed in another window');
  other.on('dialog',dialog=>void dialog.accept());await other.reload();
  await expect(other.getByRole('dialog',{name:'Recovery draft available'})).toContainText('browser backup');
  await other.getByRole('button',{name:'Restore draft',exact:true}).click();
  await expect(other.locator('.save-state')).toHaveText('Save conflict · draft kept');
  await selectEditorNode(other,note.id,note.title);await expect(second).toHaveValue('Continued second-window draft');
  expect((await current(request,route)).revision).toBe(2);
  await workspaceAction(other,'Save a copy');
  await other.getByRole('button',{name:'Continue',exact:true}).click();
  await expect(other.locator('.save-state')).toHaveText('Saved · r1');
  const copyId=new URL(other.url()).searchParams.get('project')!;expect(copyId).not.toBe(saved.id);
  expect((await current(request,`/api/projects/${copyId}`)).nodes.find(node=>node.title==='Working note')?.text).toBe('Continued second-window draft');
});

test('returning to the canvas library flushes a pending edit without a leave-draft dialog',async({page,request})=>{
  const {note,text,route}=await setup(page,request,'Navigation flushes automatic save');
  await text.fill('Saved before returning home');await text.press('Tab');
  await page.getByRole('button',{name:'Canvas library',exact:true}).click();
  await expect(page.locator('.workspace')).toBeVisible();await expect(page.getByRole('dialog',{name:'Leave this draft?'})).toHaveCount(0);
  expect((await current(request,route)).nodes.find(node=>node.id===note.id)?.text).toBe('Saved before returning home');
});
