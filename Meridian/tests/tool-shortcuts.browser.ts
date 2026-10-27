import { test, expect, type APIRequestContext, type Page } from '@playwright/test';
import { addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import type { MeridianProject } from '../src/core/types.ts';
import { inspectorPanel } from './ui-helpers.ts';

const tile=(page:Page,id:string)=>page.locator(`.ms-node[data-node-id="${id}"]`);
const saved=async(request:APIRequestContext,id:string):Promise<MeridianProject>=>(await request.get(`/api/projects/${id}`)).json();
async function seed(page:Page,request:APIRequestContext){
  expect(new URL((await request.get('/__meridian_status')).url()).port).not.toBe('5240');
  const project=newProject('One-shot tools and keyboard');project.canvases[0]!.snapping={stickyEdges:false,smartAlign:false,snapToGrid:false};
  const a=addNode(project,project.entryCanvasId,'media','First'),b=addNode(project,project.entryCanvasId,'media','Second');
  Object.assign(a,{x:90,y:90,width:240,height:140,sizeMode:'fixed',text:'Editable passage',contentType:'text'});
  Object.assign(b,{x:500,y:90,width:240,height:140,sizeMode:'fixed',text:'Another passage',contentType:'text'});
  const response=await request.post('/api/projects',{data:commitSnapshots(project)});expect(response.ok(),await response.text()).toBe(true);
  await page.goto(`/?project=${project.id}`);await expect(tile(page,a.id)).toBeVisible();return {project,a,b};
}
async function focusCanvas(page:Page){await page.locator('.ms-viewport').focus();}
async function choose(page:Page,name:'Node'|'Link'|'Module'){
  await focusCanvas(page);await page.keyboard.press('a');await page.getByRole('menuitem',{name,exact:true}).click();
}
async function blankPoint(page:Page){const rect=(await page.locator('.canvas-stage').boundingBox())!;return {x:rect.x+70,y:rect.y+rect.height-100};}
async function single(page:Page){await expect(page.getByRole('button',{name:'Single select',exact:true})).toHaveAttribute('aria-pressed','true');}

test('A opens a clamped cursor menu and repeated A, S and D return to Single select without sticky duplicates',async({page,request})=>{
  const {project}=await seed(page,request),stage=(await page.locator('.canvas-stage').boundingBox())!;
  const point={x:stage.x+stage.width-50,y:stage.y+stage.height-70};await page.mouse.move(point.x,point.y);await focusCanvas(page);await page.keyboard.press('a');
  const menu=page.getByRole('menu',{name:'Create an object',exact:true});await expect(menu).toBeVisible();
  expect(await menu.getByRole('menuitem').allTextContents()).toEqual(['Node','Link','Module']);
  const rect=(await menu.boundingBox())!,viewport=page.viewportSize()!;expect(rect.x).toBeGreaterThanOrEqual(8);expect(rect.y).toBeGreaterThanOrEqual(8);expect(rect.x+rect.width).toBeLessThanOrEqual(viewport.width-7);expect(rect.y+rect.height).toBeLessThanOrEqual(viewport.height-7);
  expect(Math.abs(rect.x-point.x)).toBeLessThan(rect.width+9);expect(Math.abs(rect.y-point.y)).toBeLessThan(rect.height+9);
  await page.keyboard.press('a');await expect(menu).toHaveCount(0);await single(page);
  for(const name of ['Node','Link','Module'] as const){await choose(page,name);await expect(page.locator('.workspace-create')).toHaveAttribute('aria-pressed','true');await page.keyboard.press('a');await single(page);await expect(menu).toHaveCount(0);}
  await page.keyboard.press('s');const area=page.getByRole('button',{name:'Select Area',exact:true});await expect(area).toHaveAttribute('aria-pressed','true');await expect(page.locator('.workspace-tools button')).toHaveCount(4);
  await page.keyboard.press('s');await single(page);await expect(area).toHaveCount(0);
  await page.keyboard.press('d');await expect(page.getByRole('button',{name:'Delete objects',exact:true})).toHaveAttribute('aria-pressed','true');await page.keyboard.press('d');await single(page);
  await page.keyboard.press('a');await page.mouse.click(stage.x+40,stage.y+40);await expect(menu).toHaveCount(0);await single(page);
  expect((await saved(request,project.id)).nodes).toHaveLength(3);
});

test('Node placement is one-shot after success while cancel leaves the pending tool and no authored changes',async({page,request})=>{
  const {project}=await seed(page,request),point=await blankPoint(page);await choose(page,'Node');await page.mouse.click(point.x,point.y);
  const form=page.getByRole('dialog',{name:'New Node',exact:true});await form.getByRole('button',{name:'Cancel',exact:true}).click();
  await expect(page.locator('.workspace-create')).toHaveAttribute('aria-pressed','true');expect((await saved(request,project.id)).nodes).toHaveLength(3);
  await page.mouse.click(point.x,point.y);await form.getByLabel('Type',{exact:true}).selectOption('media');await form.getByLabel('Tag',{exact:true}).fill('Created once');await form.getByRole('button',{name:'Create node',exact:true}).click();
  await expect.poll(async()=>(await saved(request,project.id)).nodes.some(node=>node.tag==='Created once')).toBe(true);await single(page);
  await page.mouse.click(point.x+350,point.y);await expect(form).toHaveCount(0);expect((await saved(request,project.id)).nodes).toHaveLength(4);
});

test('Link keeps its pending source and pivot clicks then returns to Single select only when a destination commits',async({page,request})=>{
  const {project,a,b}=await seed(page,request);await choose(page,'Link');await tile(page,a.id).click();
  await expect(page.locator('.ms-link-draft')).toHaveCount(1);await expect(page.getByRole('button',{name:'New Link',exact:true})).toHaveAttribute('aria-pressed','true');
  const rect=(await tile(page,a.id).boundingBox())!;await page.mouse.click(rect.x+rect.width+80,rect.y+rect.height+100);await expect(page.locator('.ms-link-draft')).toHaveCount(1);expect((await saved(request,project.id)).links).toHaveLength(0);
  await tile(page,b.id).click();await expect(page.locator('.ms-link-draft')).toHaveCount(0);await single(page);
  await expect.poll(async()=>(await saved(request,project.id)).links.length).toBe(2);expect((await saved(request,project.id)).nodes.filter(node=>node.kind==='junction')).toHaveLength(1);
  await tile(page,b.id).click();await expect(page.locator('.ms-link-draft')).toHaveCount(0);
  await page.keyboard.press('Escape');await choose(page,'Link');await tile(page,a.id).click();await page.keyboard.press('a');await single(page);await expect(page.locator('.ms-link-draft')).toHaveCount(0);
});

test('Delete is one-shot and Ctrl+Z / Ctrl+X undo and redo the completed canvas action',async({page,request})=>{
  const {project,a,b}=await seed(page,request);await focusCanvas(page);await page.keyboard.press('d');const point=await blankPoint(page);await page.mouse.click(point.x,point.y);
  await expect(page.getByRole('button',{name:'Delete objects',exact:true})).toHaveAttribute('aria-pressed','true');await tile(page,a.id).click();await expect(tile(page,a.id)).toHaveCount(0);await single(page);
  await tile(page,b.id).click();await expect(tile(page,b.id)).toHaveCount(1);await page.keyboard.press('Escape');await focusCanvas(page);await page.keyboard.press('Control+z');await expect(tile(page,a.id)).toHaveCount(1);
  await page.keyboard.press('Control+x');await expect(tile(page,a.id)).toHaveCount(0);await expect.poll(async()=>(await saved(request,project.id)).nodes.some(node=>node.id===a.id)).toBe(false);await expect(tile(page,b.id)).toHaveCount(1);
});

test('Module remains active for selecting the assembly and exits only after Create From Selection succeeds',async({page,request})=>{
  const {project,a,b}=await seed(page,request);await choose(page,'Module');await expect(page.getByLabel('Side panel',{exact:true})).toHaveValue('module');await expect(page.getByRole('button',{name:'Module',exact:true})).toHaveAttribute('aria-pressed','true');
  await tile(page,a.id).click();await tile(page,b.id).click();await expect(page.locator('.module-selection-row.selected')).toHaveCount(2);await expect(page.getByRole('button',{name:'Module',exact:true})).toHaveAttribute('aria-pressed','true');
  const name=`One-shot module ${project.id}`;await page.getByRole('textbox',{name:'Module name',exact:true}).fill(name);await page.getByRole('button',{name:'Create From Selection',exact:true}).click();
  await expect.poll(async()=>((await(await request.get('/api/modules')).json()) as Array<{name:string}>).some(item=>item.name===name)).toBe(true);await single(page);await expect(page.getByRole('button',{name:'New Node',exact:true})).toHaveAttribute('aria-pressed','false');
  await page.keyboard.press('Escape');await expect(page.locator('.ms-selected')).toHaveCount(0);
});

test('typing and native cut remain available in inputs and inline text; F resets the view and Escape clears selections',async({page,request})=>{
  const {project,a}=await seed(page,request);await page.getByRole('button',{name:'Zoom in',exact:true}).click();await focusCanvas(page);await page.keyboard.press('f');await expect(page.locator('.ms-zoom')).toHaveText('100%');const stage=(await page.locator('.canvas-stage').boundingBox())!,frame=(await tile(page,a.id).boundingBox())!;expect(frame.x-stage.x-a.x).toBeCloseTo(45,0);expect(frame.y-stage.y-a.y).toBeCloseTo(45,0);
  await inspectorPanel(page,'canvas');const search=page.getByRole('searchbox',{name:'Search Work nodes',exact:true});await search.fill('asdf');await search.press('Control+a');await search.press('Control+x');await expect(search).toHaveValue('');await expect(page.getByRole('menu')).toHaveCount(0);await single(page);
  const title=tile(page,a.id).locator('.ms-node-title');await page.getByRole('button',{name:'Text',exact:true}).click();await title.click();const inline=page.getByRole('textbox',{name:'Work node title',exact:true});await expect(inline).toBeVisible();await inline.press('Control+a');await page.keyboard.type('asdf text');await expect(inline).toHaveText('asdf text');await expect(page.getByRole('menu')).toHaveCount(0);await single(page);
  await inline.press('Control+a');await inline.press('Control+x');await expect(inline).toHaveText('');await page.keyboard.type('Still editable');await page.keyboard.press('Escape');await expect(page.locator('.ms-selected')).toHaveCount(0);await expect(page.locator('[contenteditable="true"]')).toHaveCount(0);
  await expect.poll(async()=>(await saved(request,project.id)).nodes.find(node=>node.id===a.id)?.title).toBe('Still editable');expect((await saved(request,project.id)).nodes).toHaveLength(3);
  await choose(page,'Node');const point=await blankPoint(page);await page.mouse.click(point.x,point.y);const form=page.getByRole('dialog',{name:'New Node',exact:true});await form.getByLabel('Tag',{exact:true}).fill('asdf');await form.getByLabel('Tag',{exact:true}).press('a');await expect(form).toBeVisible();await expect(page.getByRole('menu')).toHaveCount(0);await form.getByRole('button',{name:'Cancel',exact:true}).click();
});
