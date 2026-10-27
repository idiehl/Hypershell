import { test, expect, type APIRequestContext, type Locator, type Page } from '@playwright/test';
import { readFile, writeFile } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';
import { build } from 'esbuild';
import { addCanvas, addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import type { MeridianProject } from '../src/core/types.ts';
import { buildPublication } from '../src/publication.ts';

async function seed(page:Page,request:APIRequestContext){
  const fixture=new URL(test.info().project.use.baseURL!);
  expect(fixture.hostname).toBe('127.0.0.1');expect(fixture.port).not.toBe('5240');
  const health=await request.get('/__meridian_status');expect(health.ok()).toBeTruthy();expect(new URL(health.url()).origin).toBe(fixture.origin);
  const project=newProject('Canvas launcher fixture'),home=project.canvases[0]!;
  home.title='The home canvas';project.nodes[0]!.title=home.title;
  const target=addCanvas(project,'A related canvas with a long title that stays in the launcher tooltip');
  const launch=addNode(project,home.id,'canvas-link','An ordinary canvas doorway');
  Object.assign(launch,{x:80,y:100,width:270,height:130,sizeMode:'fixed',targetId:target.rootId,placement:'free'});
  const reference=addNode(project,home.id,'reference','A reference to the same doorway');
  // Keep a light backdrop beside the dark launcher so both editor and published
  // screenshots exercise the doorway's edge contrast.
  Object.assign(reference,{x:410,y:100,width:270,height:130,sizeMode:'fixed',targetId:launch.id,placement:'free',style:{fill:'#FFF8E7',textColor:'#243744',border:'#AA9B7A'}});
  const gallery=addNode(project,home.id,'container','A canvas gallery');
  Object.assign(gallery,{x:750,y:100,width:320,height:280,sizeMode:'fixed',containerVariant:'gallery',childLayout:'contained',collapsed:false,placement:'free'});
  const galleryLaunch=addNode(project,home.id,'canvas-link',target.title);
  Object.assign(galleryLaunch,{parentId:gallery.id,x:760,y:145,width:280,height:100,targetId:target.rootId});
  const note=addNode(project,target.id,'media','The destination content');
  Object.assign(note,{x:100,y:120,width:240,height:100,sizeMode:'fixed',text:'The existing canvas opens in this window.',contentType:'text',placement:'free'});
  const returning=addNode(project,target.id,'canvas-link','Return home');
  Object.assign(returning,{x:410,y:120,width:220,height:100,sizeMode:'fixed',targetId:home.rootId,placement:'free'});
  // Disable alignment in every fixture Work, including the destination created
  // above, so entering it cannot add an unrelated default alignment route.
  for(const canvas of project.canvases)canvas.snapping={stickyEdges:false,smartAlign:false,snapToGrid:false};
  const response=await request.post('/api/projects',{data:commitSnapshots(project)});expect(response.ok()).toBeTruthy();
  const saved=await response.json() as MeridianProject;
  await page.goto(`/?project=${project.id}&canvas=${home.id}`);await expect(page.locator('.workspace')).toBeVisible();
  await page.getByRole('button',{name:'Reset view',exact:true}).click();
  return{project:saved,home,target,launch,reference,gallery,galleryLaunch,note,returning};
}
const launcher=(page:Page,id:string)=>page.locator(`.ms-node[data-node-id="${id}"] .ms-canvas-open`);
const sourceSceneCss=async()=> (await Promise.all(['scene.css','grid.css','gallery-scene.css','backgrounds.css','window-scene.css','canvas-launcher.css','expansion-tray-scene.css'].map(name=>readFile(`src/${name}`,'utf8')))).join('\n');
async function unchanged(request:APIRequestContext,baseline:MeridianProject){expect(await(await request.get(`/api/projects/${baseline.id}`)).json()).toEqual(baseline);}

async function verifyIcon(button:Locator,name:string){
  await expect(button).toBeVisible();await expect(button).toHaveAttribute('aria-label',name);await expect(button).not.toHaveAttribute('title');
  await expect(button).toHaveText('');await expect(button.locator('svg')).toHaveAttribute('aria-hidden','true');
  const footprint=await button.evaluate(button=>{
    const control=getComputedStyle(button),icon=getComputedStyle(button.querySelector('svg')!);
    return{width:parseFloat(control.width),height:parseFloat(control.height),iconWidth:parseFloat(icon.width),iconHeight:parseFloat(icon.height)};
  });
  expect(footprint.width).toBeGreaterThan(0);expect(footprint.height).toBeCloseTo(footprint.width,3);
  expect(footprint.iconWidth).toBeCloseTo(footprint.width,3);expect(footprint.iconHeight).toBeCloseTo(footprint.height,3);
  await expect(button).toHaveAttribute('data-portal-style','doorway');
  expect(await button.locator('[data-portal-motion="depth"]').count()).toBe(8);
  expect(await button.locator('svg defs linearGradient').count()).toBeGreaterThan(0);

}

test('ordinary, referenced and gallery canvas launchers use a vector doorway with an accessible tooltip',async({page,request},info)=>{
  const f=await seed(page,request),name=`Open ${f.target.title}`;
  await verifyIcon(launcher(page,f.launch.id),name);await verifyIcon(launcher(page,f.reference.id),name);
  const ordinary=(await launcher(page,f.launch.id).boundingBox())!,referenced=(await launcher(page,f.reference.id).boundingBox())!;
  expect(ordinary.width).toBeCloseTo(referenced.width,3);expect(ordinary.height).toBeCloseTo(referenced.height,3);
  const gallery=page.locator(`.ms-gallery[data-gallery-id="${f.gallery.id}"]`);
  await verifyIcon(gallery.locator('.ms-canvas-open'),name);
  await expect(gallery.locator('.ms-gallery-item')).toHaveCount(1);
  await page.screenshot({path:info.outputPath('canvas-launcher-normal.png')});await unchanged(request,f.project);
});

test('continuous travel accelerates smoothly on hover and keyboard focus without restarting its phase',async({page,request},info)=>{
  await page.emulateMedia({reducedMotion:'no-preference'});const f=await seed(page,request),open=launcher(page,f.launch.id);
  await page.mouse.move(10,10);await expect(open).toHaveAttribute('data-portal-rate','1.0000');
  const animation=()=>open.evaluate(button=>{const a=button.getAnimations({subtree:true}).find(a=>a.effect?.getTiming().iterations===Infinity)!;return{time:Number(a.currentTime),rate:a.playbackRate,state:a.playState};});
  const initial=await animation();expect(initial.state).toBe('running');await page.waitForTimeout(130);expect((await animation()).time).toBeGreaterThan(initial.time);
  await open.hover();await expect(open).toHaveAttribute('data-portal-active','true');
  await expect(page.locator('.ms-canvas-destination')).toHaveText(f.target.title);
  await expect.poll(async()=>Number(await open.getAttribute('data-portal-rate'))).toBe(4);
  await expect.poll(()=>open.locator('.ms-portal-approach').evaluate(element=>new DOMMatrixReadOnly(getComputedStyle(element).transform).a)).toBeCloseTo(1.18,3);
  expect((await animation()).time).toBeGreaterThan(initial.time);await page.screenshot({path:info.outputPath('canvas-portal-accelerated.png')});
  await page.mouse.move(10,10);await open.evaluate(button=>(button as HTMLButtonElement).blur());await expect.poll(async()=>Number(await open.getAttribute('data-portal-rate'))).toBe(1);
  await expect(page.locator('.ms-canvas-destination')).toHaveCount(0);
  await page.keyboard.press('Tab');await open.focus();await expect(open).toHaveAttribute('data-portal-active','true');
  await expect(page.locator('.ms-canvas-destination')).toHaveText(f.target.title);await expect.poll(async()=>Number(await open.getAttribute('data-portal-rate'))).toBe(4);
  await unchanged(request,f.project);
});

test('reduced motion keeps portal travel and approach static while retaining the destination label',async({page,request})=>{
  await page.emulateMedia({reducedMotion:'reduce'});const f=await seed(page,request),open=launcher(page,f.launch.id);
  await open.hover();await expect(open).toHaveAttribute('data-portal-paused','true');await expect(open.locator('.ms-portal-approach')).toHaveCSS('transform','none');
  const times=()=>open.evaluate(async button=>{const animations=button.getAnimations({subtree:true}).filter(a=>a.effect?.getTiming().iterations===Infinity);await Promise.all(animations.map(a=>a.ready));return animations.map(a=>a.currentTime);});
  const before=await times();await page.waitForTimeout(180);expect(await times()).toEqual(before);
  await expect(page.locator('.ms-canvas-destination')).toHaveText(f.target.title);await page.mouse.move(10,10);await open.focus();
  await expect(open.locator('.ms-portal-approach')).toHaveCSS('transform','none');await unchanged(request,f.project);
});

test('mouse and keyboard launch the same window and preserve canvas back/forward navigation',async({page,request,context})=>{
  const f=await seed(page,request),homeUrl=page.url(),pages=context.pages().length;
  await launcher(page,f.launch.id).click();await page.waitForURL(url=>url.searchParams.get('canvas')===f.target.id);
  await expect(page.locator(`.ms-node[data-node-id="${f.note.id}"]`)).toBeVisible();const targetUrl=page.url();expect(context.pages()).toHaveLength(pages);
  await page.getByRole('button',{name:'Back',exact:true}).click();await page.waitForURL(homeUrl);
  await page.getByRole('button',{name:'Forward',exact:true}).click();await page.waitForURL(targetUrl);
  await page.getByRole('button',{name:'Back',exact:true}).click();await page.waitForURL(homeUrl);
  const reference=launcher(page,f.reference.id);await reference.focus();await page.keyboard.press('Enter');await page.waitForURL(targetUrl);
  await page.getByRole('button',{name:'Back',exact:true}).click();await page.waitForURL(homeUrl);
  const gallery=page.locator(`.ms-gallery[data-gallery-id="${f.gallery.id}"] .ms-canvas-open`);await gallery.focus();await page.keyboard.press('Space');await page.waitForURL(targetUrl);
  expect(context.pages()).toHaveLength(pages);await unchanged(request,f.project);
});

test('offline publications retain the same accessible launcher in ordinary references and galleries',async({page,request,browser},info)=>{
  const f=await seed(page,request),file=info.outputPath('canvas-launcher-viewer.html');
  await writeFile(file,buildPublication(f.project,await readFile('dist/viewer.js','utf8'),await readFile('dist/scene.css','utf8')));
  const context=await browser.newContext({offline:true,viewport:{width:1500,height:950}});
  try{
    const viewer=await context.newPage();await viewer.goto(pathToFileURL(file).href);const authored=await viewer.locator('#meridian-data').textContent(),name=`Open ${f.target.title}`;
    await verifyIcon(launcher(viewer,f.launch.id),name);await verifyIcon(launcher(viewer,f.reference.id),name);
    await verifyIcon(viewer.locator(`.ms-gallery[data-gallery-id="${f.gallery.id}"] .ms-canvas-open`),name);
    await viewer.screenshot({path:info.outputPath('canvas-launcher-shared-viewer.png')});
    await launcher(viewer,f.launch.id).click();await expect(viewer.locator(`.ms-node[data-node-id="${f.note.id}"]`)).toBeVisible();
    expect(await viewer.locator('#meridian-data').textContent()).toBe(authored);
  }finally{await context.close();}
  await unchanged(request,f.project);
});

test('a scene host using onNodeOpen receives the shared icon and its original callback',async({page})=>{
  const project=newProject('Custom scene canvas opener'),node=addNode(project,project.entryCanvasId,'media','A custom doorway');
  Object.assign(node,{x:100,y:100,width:250,height:100,contentType:'text',text:'Host-provided navigation.'});
  const script=(await build({stdin:{contents:"import {renderScene} from './src/scene.ts';window.launcherHarness={renderScene};",resolveDir:process.cwd(),loader:'ts'},bundle:true,write:false,format:'iife',platform:'browser',target:'es2022'})).outputFiles[0]!.text;
  await page.setContent('<style>html,body{margin:0;height:100%}#scene{height:700px}</style><main id="scene"></main>');
  await page.addStyleTag({content:await sourceSceneCss()});await page.addScriptTag({content:script});
  await page.evaluate(({project,id})=>{
    const state={opened:[] as string[]};(window as any).launcherState=state;
    (window as any).launcherHarness.renderScene(document.querySelector('#scene'),project,project.entryCanvasId,{readonly:true,view:{x:45,y:45,scale:1},onNodeOpen:(id:string)=>state.opened.push(id),nodeOpenLabel:(node:{id:string})=>node.id===id?'Open custom canvas':undefined});
  },{project:commitSnapshots(project),id:node.id});
  const open=launcher(page,node.id);await verifyIcon(open,'Open custom canvas');await open.click();
  expect(await page.evaluate(()=>(window as any).launcherState.opened)).toEqual([node.id]);
});

test('launchers remain centered in tall, shallow, shaped and microscopic frames with hover clearance',async({page},info)=>{
  await page.emulateMedia({reducedMotion:'no-preference'});
  const project=newProject('Centered doorway frames'),canvas=project.entryCanvasId,target=addCanvas(project,'The destination');
  const frames=[
    {x:35,y:80,width:260,height:300,title:'',style:{textAlign:'left' as const}},
    {x:330,y:80,width:210,height:300,title:'A title above the doorway'},
    {x:575,y:80,width:210,height:60,title:'A shallow doorway'},
    {x:820,y:80,width:260,height:300,title:'A shaped doorway',style:{shape:'diamond' as const}},
    {x:1120,y:80,width:30,height:20,title:'A tiny doorway'}
  ];
  const nodes=frames.map(frame=>{const node=addNode(project,canvas,'canvas-link',frame.title);Object.assign(node,frame,{sizeMode:'fixed',targetId:target.rootId,placement:'free'});return node;});
  const script=(await build({stdin:{contents:"import {renderScene} from './src/scene.ts';window.launcherHarness={renderScene};",resolveDir:process.cwd(),loader:'ts'},bundle:true,write:false,format:'iife',platform:'browser',target:'es2022'})).outputFiles[0]!.text;
  await page.setContent('<style>html,body{margin:0;height:100%}#scene{height:700px}</style><main id="scene"></main>');
  await page.addStyleTag({content:await sourceSceneCss()});await page.addScriptTag({content:script});
  await page.evaluate(project=>{
    (window as any).launcherState={opened:[]};(window as any).launcherHarness.renderScene(document.querySelector('#scene'),project,project.entryCanvasId,
      {readonly:true,view:{x:0,y:0,scale:1},onCanvasOpen:(id:string)=>(window as any).launcherState.opened.push(id)});
  },commitSnapshots(project));
  for(const node of nodes){
    const card=page.locator(`.ms-node[data-node-id="${node.id}"]`),open=launcher(page,node.id),body=await card.locator(':scope > .ms-node-body').boundingBox(),box=await open.boundingBox();
    expect(body).not.toBeNull();expect(box).not.toBeNull();
    expect(box!.x+box!.width/2).toBeCloseTo(body!.x+body!.width/2,0);
    expect(box!.y+box!.height/2).toBeCloseTo(body!.y+body!.height/2,0);
    await open.hover();await expect.poll(()=>open.locator('.ms-portal-approach').evaluate(element=>new DOMMatrixReadOnly(getComputedStyle(element).transform).a)).toBeCloseTo(1.18,3);
    for(const part of ['svg']){
      const art=(await open.locator(part).boundingBox())!,frame=(await card.boundingBox())!;
      expect(art.x).toBeGreaterThanOrEqual(frame.x-.5);expect(art.y).toBeGreaterThanOrEqual(frame.y-.5);
      expect(art.x+art.width).toBeLessThanOrEqual(frame.x+frame.width+.5);expect(art.y+art.height).toBeLessThanOrEqual(frame.y+frame.height+.5);
    }
    await open.click();
  }
  expect(await page.evaluate(()=>(window as any).launcherState.opened)).toEqual(nodes.map(()=>target.id));
  await page.mouse.move(10,10);await page.screenshot({path:info.outputPath('centered-doors-at-different-node-scales.png')});
});
