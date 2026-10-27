import { test, expect, type APIRequestContext, type Page } from '@playwright/test';
import { addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import type { MeridianProject } from '../src/core/types.ts';
import { toolsAction } from './ui-helpers.ts';

async function seed(page:Page,request:APIRequestContext){
  // The browser runner owns this host and its isolated storage. Never seed the
  // installed 5240 host or the user's saved canvas library from a layout test.
  const health=await request.get('/__meridian_status');expect(health.ok()).toBeTruthy();
  const fixture=new URL(test.info().project.use.baseURL!);expect(fixture.hostname).toBe('127.0.0.1');expect(fixture.port).not.toBe('5240');expect(new URL(health.url()).origin).toBe(fixture.origin);
  const project=newProject(`Toolbar QA ${crypto.randomUUID().slice(0,8)}`);
  const note=addNode(project,project.entryCanvasId,'media','A toolbar fixture');
  Object.assign(note,{x:120,y:130,width:260,height:100,text:'Content stays intact.',contentType:'text',placement:'free'});
  const response=await request.post('/api/projects',{data:commitSnapshots(project)});expect(response.ok()).toBeTruthy();
  const baseline=await response.json() as MeridianProject;
  await page.goto(`/?project=${project.id}&canvas=${project.entryCanvasId}`);
  expect(new URL(page.url()).origin).toBe(fixture.origin);
  await expect(page.locator('.workspace')).toBeVisible();
  await expect(page.locator('.save-state')).toHaveCount(0);
  return baseline;
}
async function unchanged(request:APIRequestContext,baseline:MeridianProject){
  const response=await request.get(`/api/projects/${baseline.id}`);expect(response.ok()).toBeTruthy();
  expect(await response.json()).toEqual(baseline);
}
async function camera(page:Page){return page.locator('.ms-world').evaluate(element=>{const style=getComputedStyle(element),matrix=new DOMMatrixReadOnly(style.transform);return{x:matrix.e+parseFloat(style.getPropertyValue('--ms-render-origin-x'))*matrix.a,y:matrix.f+parseFloat(style.getPropertyValue('--ms-render-origin-y'))*matrix.d,scale:matrix.a};});}

test('canvas controls and reset sit left of adjacent undo/redo with one vertical divider at both widths',async({page,request},info)=>{
  const baseline=await seed(page,request);
  for(const width of [1500,760]){
    await page.setViewportSize({width,height:950});
    const canvas=page.getByRole('group',{name:'Work tools',exact:true});
    expect(await canvas.getByRole('button').evaluateAll(buttons=>buttons.map(button=>button.getAttribute('aria-label'))))
      .toEqual(['New Node','Delete objects','Single select','Reset view']);
    expect(await page.locator('.history-tools button').evaluateAll(buttons=>buttons.map(button=>button.getAttribute('aria-label')))).toEqual(['Undo','Redo']);
    const positions=await page.locator('.canvas-toolbar').evaluate(toolbar=>{
      const tools=toolbar.querySelector<HTMLElement>('.workspace-tools')!,history=toolbar.querySelector<HTMLElement>('.history-tools')!;
      const canvasBox=tools.getBoundingClientRect(),historyBox=history.getBoundingClientRect(),bar=toolbar.getBoundingClientRect();
      return {canvasLeft:canvasBox.left-bar.left,canvasRight:canvasBox.right,historyLeft:historyBox.left,canvasCenter:canvasBox.top+canvasBox.height/2,historyCenter:historyBox.top+historyBox.height/2,
        canvasBorder:getComputedStyle(tools).borderLeftWidth,historyBorder:getComputedStyle(history).borderLeftWidth,
        visibleDividers:[...toolbar.children].filter(child=>parseFloat(getComputedStyle(child).borderLeftWidth)>0).length};
    });
    expect(positions.canvasLeft).toBeLessThanOrEqual(16);expect(positions.historyLeft-positions.canvasRight).toBeGreaterThanOrEqual(4);
    expect(positions.historyLeft-positions.canvasRight).toBeLessThanOrEqual(18);expect(Math.abs(positions.historyCenter-positions.canvasCenter)).toBeLessThanOrEqual(1);
    expect(positions.canvasBorder).toBe('0px');expect(positions.historyBorder).toBe('1px');expect(positions.visibleDividers).toBe(1);
    await expect(page.getByRole('button',{name:'Work panel',exact:true})).toHaveCount(0);
    await expect(page.getByRole('button',{name:'More Work tools',exact:true})).toHaveCount(0);
    await expect(page.locator('.canvas-settings-tools')).toHaveCount(0);
    await page.screenshot({path:info.outputPath(`toolbar-${width}.png`)});
  }
  await unchanged(request,baseline);
});

test('Reset view restores the camera from the final canvas-tool position without editing content',async({page,request})=>{
  const baseline=await seed(page,request),reset=page.getByRole('group',{name:'Work tools',exact:true}).getByRole('button',{name:'Reset view',exact:true});
  await reset.click();await expect.poll(()=>camera(page)).toEqual({x:45,y:45,scale:1});
  await page.getByRole('button',{name:'Zoom in',exact:true}).click();
  await expect(page.locator('.ms-zoom')).toHaveText('125%');
  await reset.click();await expect(page.locator('.ms-zoom')).toHaveText('100%');
  await expect.poll(()=>camera(page)).toEqual({x:45,y:45,scale:1});
  await unchanged(request,baseline);
});

test('the white application File and Tools menus open the existing tools without editing the canvas',async({page,request})=>{
  const baseline=await seed(page,request),file=page.getByRole('button',{name:'File',exact:true}),tools=page.getByRole('button',{name:'Tools',exact:true});
  await expect(file).toHaveCSS('color','rgb(255, 255, 255)');await expect(tools).toHaveCSS('color','rgb(255, 255, 255)');
  await expect(page.locator('.application-menubar .application-tools')).toHaveCount(1);
  const fileBox=(await file.boundingBox())!,toolsBox=(await tools.boundingBox())!;expect(toolsBox.x).toBeGreaterThanOrEqual(fileBox.x+fileBox.width);
  await tools.click();const menu=page.getByRole('menu',{name:'Tools',exact:true});
  expect(await menu.getByRole('menuitem').allTextContents()).toEqual(['Modules','Composition tools']);await page.keyboard.press('Escape');
  await toolsAction(page,'Modules');const modules=page.getByRole('dialog',{name:'Module library',exact:true});await expect(modules).toBeVisible();
  await modules.getByRole('button',{name:'Close dialog',exact:true}).click();
  await toolsAction(page,'Composition tools');const composition=page.getByRole('dialog',{name:'Composition tools',exact:true});await expect(composition).toBeVisible();
  await expect(composition.getByLabel('Composition recipe',{exact:true})).toBeVisible();await composition.getByRole('button',{name:'Close dialog',exact:true}).click();
  await unchanged(request,baseline);
});

test('native Tools commands open both existing tools and reject unsupported commands without duplicate web menus',async({page,request})=>{
  await page.addInitScript(()=>{(window as Window&{__meridianNativeChrome?:boolean}).__meridianNativeChrome=true;});
  const baseline=await seed(page,request);await expect(page.locator('.application-menubar')).toHaveCount(0);
  const dispatch=async(command:string)=>page.evaluate(command=>window.dispatchEvent(new CustomEvent('meridian:tool-command',{detail:{command}})),command);
  for(const command of ['inspect','delete','constructor'])await dispatch(command);
  await expect(page.getByRole('dialog')).toHaveCount(0);
  for(const [command,title] of [['modules','Module library'],['composition','Composition tools']]){
    await dispatch(command);const dialog=page.getByRole('dialog',{name:title,exact:true});await expect(dialog).toBeVisible();
    await dialog.getByRole('button',{name:'Close dialog',exact:true}).click();
  }
  await unchanged(request,baseline);
});

test('the retained right arrow reopens the side panel and canvas search remains available by keyboard',async({page,request})=>{
  const baseline=await seed(page,request);
  await page.getByRole('button',{name:'Close side panel',exact:true}).click();
  const show=page.getByRole('button',{name:'Show side panel',exact:true});await expect(show).toBeVisible();
  const right=page.locator('.canvas-panel-tools');await expect(right.getByRole('button')).toHaveCount(1);
  const showBox=(await show.boundingBox())!,bar=(await page.locator('.canvas-toolbar').boundingBox())!;expect(bar.x+bar.width-(showBox.x+showBox.width)).toBeLessThanOrEqual(20);
  await show.click();await expect(page.getByRole('combobox',{name:'Side panel',exact:true})).toBeFocused();
  await page.getByRole('combobox',{name:'Side panel',exact:true}).selectOption('appearance');
  await page.keyboard.press('Control+f');await expect(page.getByRole('searchbox',{name:'Search Work nodes',exact:true})).toBeFocused();
  await expect(page.getByRole('combobox',{name:'Side panel',exact:true})).toHaveValue('canvas');
  await unchanged(request,baseline);
});
