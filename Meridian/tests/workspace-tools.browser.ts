import { interactionTool } from './ui-helpers.ts';
import {test,expect,type Page,type APIRequestContext} from '@playwright/test';
import {newProject,addNode,commitSnapshots} from '../src/core/model.ts';
import type {MeridianProject} from '../src/core/types.ts';

async function seed(page:Page,request:APIRequestContext,parentKind:'container'|'text'='container'){
  const project=newProject('Shared canvas tools');
  const parent=addNode(project,project.entryCanvasId,parentKind,'A collapsible parent');Object.assign(parent,{x:180,y:140,width:300,height:180,childLayout:'spatial',collapsed:true});
  const hidden=addNode(project,project.entryCanvasId,'text','Existing child');Object.assign(hidden,{parentId:parent.id,x:510,y:410,width:200,height:130,text:'Keep this content intact.'});
  const other=addNode(project,project.entryCanvasId,'text','Another tile');Object.assign(other,{x:650,y:140,width:240,height:180,text:'A separate branch.'});
  const response=await request.post('/api/projects',{data:commitSnapshots(project)});expect(response.ok()).toBeTruthy();
  await page.goto(`/?project=${project.id}&canvas=${project.entryCanvasId}`);await expect(tile(page,parent.id)).toBeVisible();return {project,parent,hidden,other};
}
const tile=(page:Page,id:string)=>page.locator(`.ms-node[data-node-id="${id}"]`);
const saved=async(request:APIRequestContext,id:string):Promise<MeridianProject>=>(await request.get(`/api/projects/${id}`)).json();
async function mode(page:Page,value:'Node'|'Link'){await page.locator('.workspace-create').click();await page.getByRole('menuitem',{name:value,exact:true}).click();}
async function emptyPoint(page:Page){const rect=(await page.locator('.ms-viewport').boundingBox())!;return {x:rect.x+60,y:rect.y+Math.min(520,rect.height-70)};}
async function textForm(page:Page,title:string){const dialog=page.getByRole('dialog',{name:'New Node',exact:true});await expect(dialog).toBeVisible();await dialog.getByLabel('Type',{exact:true}).selectOption('media');await dialog.getByLabel('Content',{exact:true}).selectOption('text');await dialog.getByLabel('Title',{exact:true}).fill(title);await dialog.getByLabel('Text body',{exact:true}).fill('Created through the shared form.');return dialog;}
async function expectRightEdgeContact(page:Page,parentId:string,childId:string){
  await expect(tile(page,parentId)).toBeVisible();await expect(tile(page,childId)).toBeVisible();
  let measured:{left:number;right:number;top:number;bottom:number;width:number;height:number}[]|null=null;
  await expect.poll(async()=>{
    measured=await page.evaluate(ids=>{
      const cards=ids.map(id=>document.querySelector<HTMLElement>(`.ms-node[data-node-id="${id}"]`));if(cards.some(card=>!card))return null;
      return cards.map(card=>{const box=card!.getBoundingClientRect();return {left:box.left,right:box.right,top:box.top,bottom:box.bottom,width:box.width,height:box.height};});
    },[parentId,childId]);
    return !!measured&&measured.every(box=>box.width>0&&box.height>0);
  }).toBe(true);
  const [parent,child]=measured!;
  expect(Math.abs(parent.right-child.left)).toBeLessThanOrEqual(1.5);
  expect(Math.min(parent.bottom,child.bottom)-Math.max(parent.top,child.top)).toBeGreaterThan(1);
}

test('Node mode marks its position, cancellation is inert, and success returns to Single select',async({page,request},info)=>{
  const f=await seed(page,request);await mode(page,'Node');const point=await emptyPoint(page);await page.mouse.click(point.x,point.y);
  await expect(page.locator('.creation-mark')).toHaveCount(1);const cancelled=await textForm(page,'Cancelled content');await cancelled.getByRole('button',{name:'Cancel',exact:true}).click();
  expect((await saved(request,f.project.id)).nodes).toHaveLength(f.project.nodes.length);await expect(page.locator('.creation-mark')).toHaveCount(0);await expect(page.getByRole('button',{name:'New Node',exact:true})).toHaveAttribute('aria-pressed','true');
  await page.mouse.click(point.x,point.y);const dialog=await textForm(page,'At my chosen position');await dialog.getByLabel('Shape',{exact:true}).selectOption('none');await page.screenshot({path:info.outputPath('shared-node-form.png')});await dialog.getByRole('button',{name:'Create node',exact:true}).click();
  await expect.poll(async()=>(await saved(request,f.project.id)).nodes.find(n=>n.title==='At my chosen position')).toMatchObject({kind:'media',contentType:'text',parentId:f.project.canvases[0].rootId,style:{shape:'none'},placement:'free'});
  await expect(page.getByRole('button',{name:'Single select',exact:true})).toHaveAttribute('aria-pressed','true');await page.mouse.click(point.x+340,point.y);await expect(page.getByRole('dialog',{name:'New Node',exact:true})).toHaveCount(0);
  await page.reload();await expect(page.locator('.ms-node').filter({hasText:'At my chosen position'})).toBeVisible();
});

test('dragging or holding in each author mode pans without creating, deleting, moving or disclosing objects',async({page,request})=>{
  const f=await seed(page,request);const baseline=await saved(request,f.project.id);
  for(const value of ['Node','Link','Delete'] as const){if(value==='Delete')await page.getByRole('button',{name:'Delete objects',exact:true}).click();else await mode(page,value);
    const before=await page.locator('.ms-world').getAttribute('style'),bounds=(await tile(page,f.parent.id).boundingBox())!;
    await page.mouse.move(bounds.x+80,bounds.y+40);await page.mouse.down();await page.waitForTimeout(420);await page.mouse.up();await expect(page.getByRole('dialog')).toHaveCount(0);
    await page.mouse.move(bounds.x+80,bounds.y+40);await page.mouse.down();await page.mouse.move(bounds.x+115,bounds.y+75,{steps:6});await page.mouse.up();await expect(page.getByRole('dialog')).toHaveCount(0);expect(await page.locator('.ms-world').getAttribute('style')).not.toBe(before);
    expect((await saved(request,f.project.id)).nodes).toEqual(baseline.nodes);
  }
  await interactionTool(page);await tile(page,f.parent.id).locator('.ms-node-title').click();await page.getByLabel('Side panel',{exact:true}).selectOption('content');await expect(page.locator('.inspector').getByLabel('Title',{exact:true})).toHaveValue(f.parent.title);
  await tile(page,f.parent.id).locator('.ms-node-title').dblclick();await expect(tile(page,f.hidden.id)).toHaveCount(1);
});

test('clicking a collapsed node in Node mode creates an ordinary child and preserves its existing children',async({page,request})=>{
  const f=await seed(page,request,'text');await mode(page,'Node');await tile(page,f.parent.id).locator('.ms-node-title').click();const dialog=await textForm(page,'My new child');await expect(dialog).toContainText(`Create a child of ${f.parent.title}`);await dialog.getByLabel('Behavior',{exact:true}).selectOption('sticky');await dialog.getByLabel('Parent edge',{exact:true}).selectOption('right');await dialog.getByRole('button',{name:'Create node',exact:true}).click();
  await expect.poll(async()=>(await saved(request,f.project.id)).nodes.find(n=>n.title==='My new child')).toMatchObject({parentId:f.parent.id,placement:'edge',childPosition:'manual',attachSide:'right',snapping:{stickyEdges:true}});
  const result=await saved(request,f.project.id);expect(result.nodes.find(n=>n.id===f.hidden.id)?.text).toBe(f.hidden.text);expect(result.nodes.find(n=>n.id===f.parent.id)?.collapsed).toBe(false);
  const child=result.nodes.find(n=>n.title==='My new child')!;await expectRightEdgeContact(page,f.parent.id,child.id);
  await interactionTool(page);await tile(page,f.parent.id).locator('.ms-node-title').dblclick();await expect(tile(page,f.hidden.id)).toHaveCount(0);await expect(tile(page,child.id)).toHaveCount(0);
  await expect.poll(async()=>(await saved(request,f.project.id)).nodes.find(n=>n.id===f.parent.id)?.collapsed).toBe(true);
  const collapsed=await saved(request,f.project.id);expect(collapsed.nodes.filter(n=>n.parentId===f.parent.id).map(n=>n.id).sort()).toEqual([f.hidden.id,child.id].sort());expect(collapsed.nodes.find(n=>n.id===f.hidden.id)?.text).toBe(f.hidden.text);
  await tile(page,f.parent.id).locator('.ms-node-title').dblclick();await expect(tile(page,f.hidden.id)).toBeVisible();await expect(tile(page,child.id)).toBeVisible();await expectRightEdgeContact(page,f.parent.id,child.id);
  await expect.poll(async()=>(await saved(request,f.project.id)).nodes.find(n=>n.id===f.parent.id)?.collapsed).toBe(false);await page.reload();await expect(tile(page,child.id)).toBeVisible();await expectRightEdgeContact(page,f.parent.id,child.id);
  expect((await saved(request,f.project.id)).nodes.find(n=>n.id===child.id)).toMatchObject({parentId:f.parent.id,placement:'edge',childPosition:'manual',attachSide:'right',snapping:{stickyEdges:true}});
});

test('an unbounded ordered parent keeps placement authority and hides the unrelated Parent edge choice',async({page,request})=>{
  const project=newProject('Ordered child creation controls'),parent=addNode(project,project.entryCanvasId,'media','Ordered Media parent');
  Object.assign(parent,{x:150,y:100,width:240,height:120,collapsed:true,containerVariant:'vertical',childArrangement:'column',grid:{columns:1,rows:1,cellWidth:180,cellHeight:100,gap:0,showHeaders:false}});
  const response=await request.post('/api/projects',{data:commitSnapshots(project)});expect(response.ok()).toBeTruthy();const baseline=await response.json();
  await page.goto(`/?project=${project.id}`);await mode(page,'Node');await tile(page,parent.id).locator('.ms-node-title').click();const dialog=await textForm(page,'Ordered child');
  await dialog.getByLabel('Behavior',{exact:true}).selectOption('sticky');await expect(dialog.getByLabel('Parent edge',{exact:true})).toBeHidden();await expect(dialog).toContainText('The parent’s ordered cells control this tile’s position and dimensions.');
  await dialog.getByRole('button',{name:'Cancel',exact:true}).click();expect(await saved(request,project.id)).toEqual(baseline);
});

test('Link mode completes one path and returns to Single select after link creation and deletion',async({page,request})=>{
  const f=await seed(page,request);await mode(page,'Link');await tile(page,f.parent.id).locator('.ms-node-title').click();await expect(page.getByRole('dialog')).toHaveCount(0);await expect(page.locator('.ms-link-draft')).toHaveCount(1);
  await tile(page,f.other.id).locator('.ms-node-title').click();await expect(page.getByRole('dialog')).toHaveCount(0);await expect(page.locator('.ms-link-draft')).toHaveCount(0);
  await expect.poll(async()=>(await saved(request,f.project.id)).links.length).toBe(1);await expect(page.getByLabel('Side panel',{exact:true})).toHaveValue('links');
  await page.locator('.inspector').getByLabel('Relationship',{exact:true}).selectOption('mutual');const label=page.locator('.inspector').getByLabel('Connection label',{exact:true});await label.fill('Both perspectives');await label.press('Tab');
  await expect.poll(async()=>(await saved(request,f.project.id)).links.find(l=>l.label==='Both perspectives')).toMatchObject({sourceId:f.parent.id,targetId:f.other.id,kind:'visual',relation:'mutual'});
  await expect(page.getByRole('button',{name:'Single select',exact:true})).toHaveAttribute('aria-pressed','true');await mode(page,'Link');await tile(page,f.other.id).locator('.ms-node-title').click();await expect(page.getByRole('dialog')).toHaveCount(0);await expect(page.locator('.ms-link-draft')).toHaveCount(1);expect((await saved(request,f.project.id)).links).toHaveLength(1);await page.keyboard.press('Escape');await expect(page.locator('.ms-link-draft')).toHaveCount(0);await expect(page.getByRole('button',{name:'Single select',exact:true})).toHaveAttribute('aria-pressed','true');
  const link=(await saved(request,f.project.id)).links.find(l=>l.label==='Both perspectives')!;await page.getByRole('button',{name:'Delete objects',exact:true}).click();const point=await page.locator(`.ms-link-hit[data-link-id="${link.id}"]`).evaluate(path=>{const line=path as SVGPathElement,at=line.getPointAtLength(line.getTotalLength()*.4),screen=new DOMPoint(at.x,at.y).matrixTransform(line.getScreenCTM()!);return {x:screen.x,y:screen.y};});await page.mouse.click(point.x,point.y);await expect.poll(async()=>(await saved(request,f.project.id)).links.some(l=>l.id===link.id)).toBe(false);
  await page.getByRole('button',{name:'Undo',exact:true}).click();await expect.poll(async()=>(await saved(request,f.project.id)).links.some(l=>l.id===link.id)).toBe(true);await expect(page.getByRole('button',{name:'Delete objects',exact:true})).toHaveAttribute('aria-pressed','false');
});

test('right-click creates at the menu location and offers child creation, inspection and undoable branch deletion',async({page,request})=>{
  const f=await seed(page,request);const point=await emptyPoint(page);await page.mouse.click(point.x,point.y,{button:'right'});await expect(page.getByRole('menuitem',{name:'Create Node',exact:true})).toBeVisible();await page.getByRole('menuitem',{name:'Create Node',exact:true}).click();const dialog=await textForm(page,'From the context menu');await dialog.getByRole('button',{name:'Create node',exact:true}).click();
  await expect.poll(async()=>(await saved(request,f.project.id)).nodes.some(n=>n.title==='From the context menu')).toBe(true);await expect(page.getByRole('button',{name: 'Single select',exact:true})).toHaveAttribute('aria-pressed','true');
  await tile(page,f.parent.id).locator('.ms-node-title').click({button:'right'});await expect(page.getByRole('menuitem',{name:'Create Child Node',exact:true})).toBeVisible();await page.getByRole('menuitem',{name:'Delete',exact:true}).click();await expect.poll(async()=>(await saved(request,f.project.id)).nodes.some(n=>n.id===f.hidden.id||n.id===f.parent.id)).toBe(false);
  await page.getByRole('button',{name:'Undo',exact:true}).click();await expect.poll(async()=>(await saved(request,f.project.id)).nodes.filter(n=>[f.hidden.id,f.parent.id].includes(n.id)).length).toBe(2);
});

test('a route joining form retains the acknowledged save baseline when autosave completes while it is open',async({page,request})=>{
  const project=newProject('Route form during autosave');
  const a=addNode(project,project.entryCanvasId,'text','Source'),b=addNode(project,project.entryCanvasId,'text','Destination'),joining=addNode(project,project.entryCanvasId,'text','Joining source');
  Object.assign(a,{x:80,y:100,width:180,height:100});Object.assign(b,{x:640,y:100,width:180,height:100});Object.assign(joining,{x:80,y:370,width:180,height:100});
  const linkId=crypto.randomUUID();project.links.push({id:linkId,canvasId:project.entryCanvasId,sourceId:a.id,targetId:b.id,kind:'visual',relation:'generic'});
  expect((await request.post('/api/projects',{data:commitSnapshots(project)})).ok()).toBeTruthy();
  await page.goto(`/?project=${project.id}`);await tile(page,a.id).locator('.ms-node-title').click();await page.getByLabel('Side panel',{exact:true}).selectOption('content');
  let release!:()=>void,arrived!:()=>void;const hold=new Promise<void>(r=>release=r),committed=new Promise<void>(r=>arrived=r);let writes=0;
  await page.route(`**/api/projects/${project.id}`,async route=>{if(route.request().method()!=='PUT'){await route.continue();return;}const response=await route.fetch();if(++writes===1){arrived();await hold;}await route.fulfill({response});});
  try{
    const title=page.locator('.inspector').getByLabel('Title',{exact:true});await title.fill('Edited before joining');await title.press('Tab');await committed;
    const point=await page.locator(`.ms-link-line[data-link-id="${linkId}"]`).evaluate(path=>{const line=path as SVGPathElement,p=line.getPointAtLength(line.getTotalLength()*.45),screen=new DOMPoint(p.x,p.y).matrixTransform(line.getScreenCTM()!);return{x:screen.x,y:screen.y};});
    await page.mouse.click(point.x,point.y,{button:'right'});await page.getByRole('menuitem',{name:'Create Link',exact:true}).click();
    const form=page.getByRole('dialog',{name:'New Link',exact:true});await form.getByLabel('Source',{exact:true}).selectOption(joining.id);
    release();await expect(page.locator('.save-state')).toHaveText('Saved · r2');await expect(form).toBeVisible();
    await form.getByRole('button',{name:'Create link',exact:true}).click();await expect(page.locator('.save-state')).toHaveText('Saved · r3');
    const result=await saved(request,project.id);expect(result.nodes.find(n=>n.id===a.id)?.title).toBe('Edited before joining');expect(result.nodes.filter(n=>n.kind==='junction')).toHaveLength(1);expect(result.links).toHaveLength(3);expect(result.snapshots[a.id].node.title).toBe('Edited before joining');await expect(page.getByRole('alert')).toHaveCount(0);
    // The same provisional junction may be the source of an outgoing branch.
    const outgoing=await page.locator(`.ms-link-line[data-link-id="${linkId}"]`).evaluate(path=>{const line=path as SVGPathElement,p=line.getPointAtLength(line.getTotalLength()*.5),screen=new DOMPoint(p.x,p.y).matrixTransform(line.getScreenCTM()!);return{x:screen.x,y:screen.y};});
    await page.mouse.click(outgoing.x,outgoing.y,{button:'right'});await page.getByRole('menuitem',{name:'Create Link',exact:true}).click();
    const nextForm=page.getByRole('dialog',{name:'New Link',exact:true}),prospective=await nextForm.getByLabel('Destination',{exact:true}).inputValue();
    await nextForm.getByLabel('Source',{exact:true}).selectOption(prospective);await nextForm.getByLabel('Destination',{exact:true}).selectOption(joining.id);await nextForm.getByRole('button',{name:'Create link',exact:true}).click();
    await expect(page.locator('.save-state')).toHaveText('Saved · r4');const branched=await saved(request,project.id);expect(branched.nodes.filter(n=>n.kind==='junction')).toHaveLength(2);expect(branched.links).toHaveLength(5);expect(branched.links.some(link=>link.sourceId===prospective&&link.targetId===joining.id)).toBe(true);await expect(page.getByRole('alert')).toHaveCount(0);
  }finally{release();await page.unrouteAll({behavior:'wait'});}
});
