import { test, expect, type APIRequestContext, type Page } from '@playwright/test';
import { addNode, newProject } from '../src/core/model.ts';
import type { MeridianProject } from '../src/core/types.ts';

const card=(page:Page,id:string)=>page.locator(`.ms-node[data-node-id="${id}"]`);
const read=async(request:APIRequestContext,id:string)=>await(await request.get(`/api/projects/${id}`)).json() as MeridianProject;
async function committed(page:Page,request:APIRequestContext,id:string){
  // The quiet header no longer displays a persistent save/revision label. A
  // manual save creates a fresh confirmation only after its flush completes.
  const previous=await page.$('.toast');await page.keyboard.press('Control+s');
  await page.waitForFunction(prior=>{const current=document.querySelector('.toast');return current!==prior&&/^All changes are saved · r\d+\.$/.test(current?.textContent??'');},previous);
  await previous?.dispose();return read(request,id);
}
async function drag(page:Page,id:string,dx:number,dy:number){
  const bounds=await card(page,id).boundingBox();expect(bounds).not.toBeNull();
  // Titles and passages are native text-selection surfaces; selected node
  // edges resize. Use a real interior backdrop gap for object movement.
  const point=await card(page,id).evaluate(root=>{
    const frame=root.getBoundingClientRect();
    for(let y=frame.top+9;y<frame.bottom-9;y+=1)for(const x of [frame.left+frame.width/2,frame.left+10,frame.right-10]){
      const hit=document.elementFromPoint(x,y);
      if(hit?.closest('.ms-node')===root&&!hit.closest('[data-editable-node-id],button,input,textarea,select,a'))return{x,y};
    }
  });
  expect(point,'A node must expose a draggable backdrop outside its editable text and resize edges').toBeDefined();
  const {x,y}=point!;
  await page.mouse.move(x,y);await page.mouse.down();await page.mouse.move(x+dx,y+dy,{steps:12});await page.mouse.up();
}

test('snapping commits a line-free relationship, edge locking moves rigidly, resize stops at peers and unlock restores independent movement',async({page,request},info)=>{
  const project=newProject('Cluster editor QA'),a=addNode(project,project.entryCanvasId,'media','Cluster A'),b=addNode(project,project.entryCanvasId,'media','Cluster B');
  for(const node of [a,b])Object.assign(node,{x:node===a?100:500,y:100,width:200,height:110,sizeMode:'fixed',contentType:'text',text:'An independent node',style:{shape:'rectangle',padding:5}});
  const response=await request.post('/api/projects',{data:project});expect(response.status()).toBe(201);const created=await response.json() as MeridianProject;
  const errors:string[]=[];page.on('pageerror',error=>errors.push(error.message));
  await page.goto(`/?project=${created.id}`);await expect(card(page,b.id)).toBeVisible();
  await drag(page,b.id,-200,0);
  const snapped=await committed(page,request,created.id),contact=snapped.links.find(link=>link.snap);
  expect(contact).toBeDefined();expect(contact!.snap).toEqual({locked:false});expect(contact!.direction).toBe('none');
  expect(snapped.nodes.find(node=>node.id===b.id)!.x).toBeCloseTo(300,5);
  await expect(page.locator(`[data-link-id="${contact!.id}"]`)).toHaveCount(0);
  const first=await card(page,a.id).boundingBox();await page.mouse.move(first!.x+first!.width,first!.y+first!.height*.5);
  const lock=page.getByRole('button',{name:'Lock nodes together',exact:true});await expect(lock).toBeVisible();
  await page.screenshot({path:info.outputPath('snapped-edge-lock.png')});await lock.click();
  const locked=await committed(page,request,created.id);expect(locked.links.find(link=>link.id===contact!.id)!.snap).toEqual({locked:true});
  await drag(page,a.id,60,130);
  const moved=await committed(page,request,created.id),at=(p:MeridianProject,id:string)=>p.nodes.find(node=>node.id===id)!;
  expect(at(moved,a.id).x-at(locked,a.id).x).toBeCloseTo(at(moved,b.id).x-at(locked,b.id).x,5);
  expect(at(moved,a.id).y-at(locked,a.id).y).toBeCloseTo(at(moved,b.id).y-at(locked,b.id).y,5);
  expect(at(moved,a.id).x).toBeCloseTo(160,5);expect(at(moved,b.id).x-at(moved,a.id).x).toBe(200);
  await card(page,a.id).locator('.ms-node-title').click();await page.getByRole('combobox',{name:'Side panel',exact:true}).selectOption('appearance');
  await page.getByLabel('Width',{exact:true}).fill('350');await page.getByLabel('Width',{exact:true}).press('Tab');
  const limited=await committed(page,request,created.id);expect(at(limited,a.id).width).toBeCloseTo(200,4);expect(at(limited,b.id).x).toBe(at(moved,b.id).x);
  await page.getByRole('combobox',{name:'Side panel',exact:true}).selectOption('links');
  await page.locator(`[data-associated-link="${contact!.id}"] .inspector-link-title`).click();
  await expect(page.getByRole('button',{name:'Unlock edge',exact:true})).toBeVisible();await page.getByRole('button',{name:'Unlock edge',exact:true}).click();
  const unlocked=await committed(page,request,created.id);expect(unlocked.links.find(link=>link.id===contact!.id)!.snap).toEqual({locked:false});
  await drag(page,b.id,100,0);const apart=await committed(page,request,created.id);
  expect(at(apart,a.id).x).toBe(at(unlocked,a.id).x);expect(at(apart,b.id).x).toBeCloseTo(at(unlocked,b.id).x+100,5);expect(apart.links.some(link=>link.id===contact!.id)).toBe(false);
  await page.getByRole('button',{name:'Undo',exact:true}).click();const undo=await committed(page,request,created.id);expect(undo.links.find(link=>link.id===contact!.id)!.snap).toEqual({locked:false});
  await page.getByRole('button',{name:'Undo',exact:true}).click();const relocked=await committed(page,request,created.id);expect(relocked.links.find(link=>link.id===contact!.id)!.snap).toEqual({locked:true});
  await page.reload();await expect(card(page,b.id)).toBeVisible();await drag(page,b.id,0,0);await card(page,b.id).focus();await page.keyboard.press('ArrowRight');
  const nudged=await committed(page,request,created.id);expect(at(nudged,a.id).x-at(relocked,a.id).x).toBe(5);expect(at(nudged,b.id).x-at(relocked,b.id).x).toBe(5);
  expect(errors).toEqual([]);await page.screenshot({path:info.outputPath('cluster-reloaded.png')});
});

