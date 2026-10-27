import { workspaceAction } from './ui-helpers.ts';
import { test, expect, type Page, type APIRequestContext } from '@playwright/test';
import { addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import type { MeridianProject } from '../src/core/types.ts';

const card = (page: Page, id: string) => page.locator(`.ms-node[data-node-id="${id}"]`);
function fixture() {
  const p = newProject('Selection editing');
  p.canvases[0]!.snapping = { stickyEdges: false, smartAlign: false, snapToGrid: false };
  const a = addNode(p,p.entryCanvasId,'text','First branch'); Object.assign(a,{x:100,y:270,width:200,height:130,collapsed:true,childLayout:'spatial',text:'Owns a hidden child.'});
  const child = addNode(p,p.entryCanvasId,'number','Hidden value');Object.assign(child,{parentId:a.id,x:110,y:470,value:4});
  const b = addNode(p,p.entryCanvasId,'text','Second branch');Object.assign(b,{x:410,y:270,width:200,height:130,text:'Another branch.'});
  const other = addNode(p,p.entryCanvasId,'text','Leave in place');Object.assign(other,{x:750,y:90,width:230,height:130});
  p.links.push({id:crypto.randomUUID(),canvasId:p.entryCanvasId,sourceId:a.id,targetId:b.id,kind:'visual',relation:'generic',direction:'none',points:[{x:355,y:430}]});
  return { p:commitSnapshots(p),a,b,child,other };
}
async function read(request:APIRequestContext,id:string):Promise<MeridianProject>{return(await request.get(`/api/projects/${id}`)).json();}
async function save(page:Page,request:APIRequestContext,id:string){const p=await read(request,id);await workspaceAction(page,'Save');await expect(page.locator('.save-state')).toHaveText(`Saved · r${p.revision+1}`);return read(request,id);}

test('multi-selection moves nested branches once, keeps routes, groups with undo and saves disclosure',async({page,request},info)=>{
  const {p,a,b,child,other}=fixture();const response=await request.post('/api/projects',{data:p});expect(response.ok()).toBeTruthy();
  await page.goto(`/?project=${p.id}`);
  await card(page,a.id).click({position:{x:2,y:20}});
  await expect(card(page,a.id)).toHaveAttribute('aria-expanded','false');
  await card(page,b.id).click({modifiers:['Shift'],position:{x:2,y:20}});
  await expect(page.locator('.ms-node[aria-selected="true"]')).toHaveCount(2);
  await expect(card(page,a.id)).toHaveAttribute('aria-selected','true');
  const box=await card(page,a.id).boundingBox();expect(box).not.toBeNull();
  await page.mouse.move(box!.x+2,box!.y+20);await page.mouse.down();await page.mouse.move(box!.x+77,box!.y+47,{steps:8});await page.mouse.up();
  const moved=await save(page,request,p.id),ma=moved.nodes.find(n=>n.id===a.id)!;
  const dx=ma.x-a.x,dy=ma.y-a.y;expect(dx).not.toBe(0);
  expect(moved.nodes.find(n=>n.id===b.id)?.x).toBe(b.x+dx);expect(moved.nodes.find(n=>n.id===child.id)?.y).toBe(child.y+dy);
  expect(moved.nodes.find(n=>n.id===other.id)?.x).toBe(other.x);expect(moved.links[0]!.points![0]).toEqual({x:355+dx,y:430+dy});
  await card(page,a.id).focus();await page.keyboard.press('ArrowRight');await page.keyboard.press('ArrowRight');
  const nudged=await save(page,request,p.id);expect(nudged.nodes.find(n=>n.id===a.id)?.x).toBe(ma.x+10);expect(nudged.nodes.find(n=>n.id===b.id)?.x).toBe(b.x+dx+10);
  await card(page,a.id).click({button:'right',position:{x:2,y:20}});
  await page.getByRole('menuitem',{name:'Group selected nodes',exact:true}).click();
  const dialog=page.getByRole('dialog',{name:'Group selected nodes',exact:true});await dialog.getByLabel('Name',{exact:true}).fill('Shared ideas');await dialog.getByRole('button',{name:'Continue',exact:true}).click();
  await page.getByRole('combobox',{name:'Side panel',exact:true}).selectOption('content');await expect(page.locator('.inspector').getByLabel('Title',{exact:true})).toHaveValue('Shared ideas');
  const grouped=await save(page,request,p.id),group=grouped.nodes.find(n=>n.title==='Shared ideas')!;
  expect(grouped.nodes.find(n=>n.id===a.id)?.parentId).toBe(group.id);expect(grouped.nodes.find(n=>n.id===child.id)?.parentId).toBe(a.id);
  await page.getByRole('button',{name:'Undo',exact:true}).click();await expect(card(page,group.id)).toHaveCount(0);
  await page.getByRole('button',{name:'Redo',exact:true}).click();await expect(card(page,group.id)).toHaveCount(1);
  await page.getByRole('button',{name:'Fit Work',exact:true}).click();
  await card(page,group.id).dblclick({position:{x:2,y:20}});await expect(card(page,a.id)).toHaveCount(0);await expect(card(page,b.id)).toHaveCount(0);await expect(card(page,other.id)).toHaveCount(1);
  await save(page,request,p.id);await page.reload();await expect(card(page,group.id)).toHaveAttribute('aria-expanded','false');
  await page.screenshot({path:info.outputPath('grouped-branches.png')});
});

test('area selection transfers into Modules with hidden descendants and Escape clears selection without a selection bar',async({page,request},info)=>{
  const {p,a,b,child}=fixture();await request.post('/api/projects',{data:p});await page.goto(`/?project=${p.id}`);
  await page.getByRole('button',{name:'Select area',exact:true}).click();
  const ab=await card(page,a.id).boundingBox(),bb=await card(page,b.id).boundingBox();
  await page.mouse.move(ab!.x-10,ab!.y-12);await page.mouse.down();await page.mouse.move(bb!.x+bb!.width+10,bb!.y+bb!.height+12,{steps:6});await page.mouse.up();
  await expect(page.locator('.ms-node[aria-selected="true"]')).toHaveCount(2);
  await page.getByRole('button',{name:'New Node',exact:true}).click();await page.getByRole('menuitem',{name:'Create Module',exact:true}).click();
  await expect(page.locator(`[data-module-selection-node="${child.id}"] input[type="checkbox"]`)).toBeChecked();
  await expect(page.locator(`[data-module-selection-node="${b.id}"] input[type="checkbox"]`)).toBeChecked();
  await page.screenshot({path:info.outputPath('selection-module-tree.png')});
  await expect(page.locator('.save-state')).toHaveText('Saved · r1');
  const source=await read(request,p.id);expect(source.nodes.find(n=>n.id===child.id)?.parentId).toBe(a.id);
  await page.keyboard.press('Escape');
  await expect(page.locator('.ms-node[aria-selected="true"]')).toHaveCount(0);
  await expect(page.getByRole('button',{name:'Create From Selection',exact:true})).toBeDisabled();
  await expect(page.locator('.selection-bar')).toHaveCount(0);
});

