import {test,expect,type Page} from '@playwright/test';
import {build} from 'esbuild';
import {readFile,writeFile} from 'node:fs/promises';
import {pathToFileURL} from 'node:url';
import {addNode,commitSnapshots,newProject} from '../src/core/model.ts';
import {buildPublication} from '../src/publication.ts';
import type {MeridianNode,MeridianProject,NodeShape} from '../src/core/types.ts';
let script:string,css:string,viewer:string;
test.beforeAll(async()=>{
 css=(await Promise.all(['scene','grid','gallery-scene','backgrounds','window-scene','canvas-launcher','expansion-tray-scene'].map(name=>readFile(`src/${name}.css`,'utf8')))).join('\n');
 script=(await build({stdin:{contents:"import {renderScene} from './src/scene.ts';window.backgroundScene={renderScene};",resolveDir:process.cwd(),loader:'ts'},bundle:true,write:false,format:'iife',platform:'browser',target:'es2022'})).outputFiles[0]!.text;
 viewer=(await build({entryPoints:['src/viewer.ts'],bundle:true,write:false,format:'iife',platform:'browser',target:'es2022'})).outputFiles[0]!.text;
});
const card=(page:Page,id:string)=>page.locator(`.ms-node[data-node-id="${id}"]`);
function node(project:MeridianProject,title:string,x:number,y:number,width=220,height=170,shape?:NodeShape){const result=addNode(project,project.entryCanvasId,'media',title);Object.assign(result,{x,y,width,height,sizeMode:'fixed',placement:'free',contentType:'text',text:'Visible content',style:{backgroundDisplay:'hover',fill:'#405C80',border:'#D6A054',borderWidth:3,...(shape?{shape}:{}),fontSize:18,titleSize:18}});return result;}
async function mount(page:Page,project:MeridianProject,readonly=true){
 await page.emulateMedia({reducedMotion:'reduce'});await page.setContent('<style>html,body{margin:0;height:100%}#scene{height:900px;width:1450px}</style><main id="scene"></main>');await page.addStyleTag({content:css});await page.addScriptTag({content:script});
 await page.evaluate(({project,readonly})=>{const state:any={project,selections:[],resizes:[],view:{x:20,y:20,scale:1}};state.draw=()=> (window as any).backgroundScene.renderScene(document.querySelector('#scene'),state.project,state.project.entryCanvasId,{readonly,view:state.view,selections:state.selections,onSelections:(ids:string[])=>{state.selections=ids;state.draw();},onResize:(id:string,width:number,height:number,x:number,y:number)=>{state.resizes.push({id,width,height,x,y});Object.assign(state.project.nodes.find((n:any)=>n.id===id),{width,height,...(x===undefined?{}:{x,y})});state.draw();},onToggle:(id:string,field:string,value:boolean)=>{state.project.nodes.find((n:any)=>n.id===id)[field]=value;state.draw();},onViewChange:(view:any)=>state.view=view});(window as any).backgroundState=state;state.draw();},{project,readonly});
 await page.mouse.move(5,5);
}
async function rest(page:Page){await page.mouse.move(5,5);await page.evaluate(()=>{(document.activeElement as HTMLElement)?.blur();});}
const fillOpacity=(page:Page,id:string)=>card(page,id).evaluate(element=>element.dataset.shape?getComputedStyle(element,'::after').opacity:getComputedStyle(element.querySelector('.ms-node-hover-fill')!).opacity);

test('all eleven shapes keep visible content and authored borders while revealing only their paint',async({page},info)=>{
 const project=newProject('Hover shape matrix'),shapes:NodeShape[]=['rectangle','square','rounded','circle','ellipse','triangle','diamond','pentagon','hexagon','octagon','none'];
 const nodes=shapes.map((shape,index)=>node(project,shape,35+index%6*225,50+Math.floor(index/6)*240,195,190,shape));await mount(page,project);
 const before=await page.evaluate(()=>JSON.stringify((window as any).backgroundState.project));
 for(const n of nodes){const surface=card(page,n.id);await rest(page);await expect(surface.locator('.ms-node-title')).toBeVisible();await expect(surface).toHaveCSS('opacity','1');
 if(n.style!.shape==='none'){await expect(surface.locator('.ms-node-hover-fill')).toHaveCount(0);expect(await surface.evaluate(element=>getComputedStyle(element,'::after').display)).toBe('none');continue;}
 expect(await fillOpacity(page,n.id)).toBe('0');expect(await surface.evaluate(element=>getComputedStyle(element,'::before').backgroundColor)).toBe('rgba(0, 0, 0, 0)');
 const border=surface.locator('.ms-node-border path');await expect(border).toHaveCSS('fill','none');const stroke=await border.evaluate(element=>getComputedStyle(element).stroke);
 await surface.hover();await expect(surface).toHaveAttribute('data-background-active','true');expect(await fillOpacity(page,n.id)).toBe('1');expect(await border.evaluate(element=>getComputedStyle(element).stroke)).toBe(stroke);
 }
 expect(await page.evaluate(()=>JSON.stringify((window as any).backgroundState.project))).toBe(before);await page.screenshot({path:info.outputPath('hover-background-shapes.png')});
});

test('native fill and authored pattern or image opacity remain independent of visible media and text',async({page})=>{
 const project=newProject('Hover decoration'),pattern=node(project,'Pattern',60,70),image=node(project,'Image background',380,70);
 pattern.style={...pattern.style,backgroundPattern:'dots',backgroundOpacity:.37};image.style={...image.style,backgroundAssetId:'photo',backgroundOpacity:.62};
 project.assets.push({id:'photo',name:'Pixel.png',mime:'image/png',base64:'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a5p8AAAAASUVORK5CYII='});
 await mount(page,project);
 for(const n of [pattern,image]){const surface=card(page,n.id),paint=surface.locator('.ms-node-background[data-background-pattern]');expect(await fillOpacity(page,n.id)).toBe('0');await expect(paint).toHaveCSS('opacity','0');await expect(surface.locator('.ms-text')).toHaveText('Visible content');await surface.hover();expect(await fillOpacity(page,n.id)).toBe('1');await expect(paint).toHaveCSS('opacity',String(n.style!.backgroundOpacity));await expect(surface.locator('.ms-text')).toHaveCSS('opacity','1');await rest(page);}
});

test('flat nested children and keyboard focus reveal physical ancestors without changing hierarchy',async({page})=>{
 const project=newProject('Nested hover'),parent=node(project,'Outer',50,50,580,360),inner=node(project,'Inner',85,115,490,245),child=node(project,'Child',120,170,250,130);
 for(const n of [parent,inner])Object.assign(n,{kind:'container',containerVariant:'contained',childLayout:'contained',collapsed:false,text:undefined});inner.parentId=parent.id;child.parentId=inner.id;
 await mount(page,project);await expect(card(page,parent.id).locator('.ms-node')).toHaveCount(0);await card(page,child.id).hover();for(const n of [parent,inner,child])await expect(card(page,n.id)).toHaveAttribute('data-background-active','true');
 await rest(page);for(const n of [parent,inner,child])await expect(card(page,n.id)).toHaveAttribute('data-background-active','false');
 await card(page,child.id).focus();for(const n of [parent,inner,child])await expect(card(page,n.id)).toHaveAttribute('data-background-active','true');
});

test('Gallery pages and Window descendants reveal their own paint without hiding navigation or scrolling controls',async({page})=>{
 const project=newProject('Gallery and Window'),gallery=node(project,'Gallery',45,55,410,330),item=node(project,'Visible gallery page',60,90),windowNode=node(project,'Window',550,55,470,330),child=node(project,'Window child',580,110,180,100);
 Object.assign(gallery,{kind:'container',containerVariant:'gallery',childLayout:'contained',collapsed:false,text:undefined});item.parentId=gallery.id;item.style={...item.style,backgroundPattern:'grid',backgroundOpacity:.4};
 Object.assign(windowNode,{kind:'container',containerVariant:'window',childLayout:'contained',collapsed:false,text:undefined,grid:{columns:2,rows:2,cellWidth:180,cellHeight:120,windowColumns:1,windowRows:1}});child.parentId=windowNode.id;child.cell={column:1,row:1};
 await mount(page,project);const pageSurface=page.locator(`[data-gallery-item-id="${item.id}"]`),paint=pageSurface.locator(':scope>.ms-node-background[data-background-pattern]');
 await expect(paint).toHaveCSS('opacity','0');await pageSurface.locator('.ms-gallery-item-title').hover();await expect(pageSurface).toHaveAttribute('data-background-active','true');await expect(paint).toHaveCSS('opacity','0.4');await expect(card(page,gallery.id)).toHaveAttribute('data-background-active','true');await expect(card(page,gallery.id).locator('.ms-gallery-navigation button').first()).toBeVisible();
 await rest(page);await card(page,child.id).hover();await expect(card(page,windowNode.id)).toHaveAttribute('data-background-active','true');await expect(card(page,child.id)).toHaveAttribute('data-background-active','true');await expect(card(page,windowNode.id).locator('.ms-window-viewport')).toHaveCSS('opacity','1');
});

test('a tray and its own surface reveal the tray fill without revealing the distant source cover',async({page})=>{
 const project=newProject('Physical tray hover'),source=node(project,'Source',70,70,260,170,'none'),child=node(project,'Tray child',700,160,220,160);
 Object.assign(source,{collapsed:false,childLayout:'spatial',expansionTray:{offset:{x:620,y:80},layoutVersion:3}});child.parentId=source.id;child.childPosition='manual';
 await mount(page,project);const backdrop=page.locator(`.ms-expansion-tray-backdrop[data-tray-source-id="${source.id}"]`);
 await card(page,child.id).hover();await expect(backdrop).toHaveAttribute('data-background-active','true');await expect(card(page,source.id)).toHaveAttribute('data-background-active','false');await expect(backdrop.locator('.ms-node-hover-fill')).toHaveCSS('opacity','1');
 await expect(page.locator('.ms-container-hover-bar')).toHaveCount(0);await backdrop.hover({position:{x:3,y:3}});await expect(backdrop).toHaveAttribute('data-background-active','true');await expect(card(page,source.id)).toHaveAttribute('data-background-active','false');
 await rest(page);await backdrop.focus();await expect(backdrop).toHaveAttribute('data-background-active','true');await expect(card(page,source.id)).toHaveAttribute('data-background-active','false');
});

test('background hover leaves selection, vector edge resize and display size intact',async({page})=>{
 const project=newProject('Editable hover edges'),n=node(project,'Resize me',140,140,300,180,'hexagon');await mount(page,project,false);
 await card(page,n.id).locator('.ms-node-title').click();await expect(card(page,n.id)).toHaveAttribute('aria-selected','true');await expect(card(page,n.id).locator('.ms-node-outline')).toHaveCSS('display','block');
 const box=(await card(page,n.id).boundingBox())!,shape=(await card(page,n.id).locator('.ms-node-hit-surface path').boundingBox())!;await page.mouse.move(shape.x+shape.width-5,shape.y+shape.height/2);
 const hit=await page.evaluate(({x,y})=>({hit:document.elementFromPoint(x,y)?.outerHTML.slice(0,300),state:(window as any).backgroundState.selections,cursor:document.querySelector('.ms-node[data-node-id]')?.getAttribute('data-resize-cursor')}),{x:shape.x+shape.width-5,y:shape.y+shape.height/2});expect(hit.cursor,JSON.stringify({box,shape,hit})).toBeTruthy();
 await page.mouse.down();await page.mouse.move(shape.x+shape.width+49,shape.y+shape.height/2,{steps:5});await page.mouse.up();
 expect(await page.evaluate(()=>(window as any).backgroundState.resizes.length)).toBe(1);const after=(await card(page,n.id).boundingBox())!;expect(after.width).toBeGreaterThan(box.width+30);await expect(card(page,n.id)).toHaveCSS('opacity','1');
});

test('offline readers share hover paint and no-hover touch retains visible fill without an extra tap',async({browser},info)=>{
 const project=newProject('Portable hover'),n=node(project,'Portable content',90,100,300,190),label=node(project,'Unframed',500,100,260,130);label.style={...label.style,shape:'none',backgroundPattern:'grid'};
 const file=info.outputPath('hover-background-publication.html');await writeFile(file,buildPublication(commitSnapshots(project),viewer,css));
 const desktop=await browser.newContext({offline:true,viewport:{width:1500,height:950},reducedMotion:'reduce'});
 try{const page=await desktop.newPage();await page.goto(pathToFileURL(file).href);const surface=card(page,n.id);expect(await fillOpacity(page,n.id)).toBe('0');await surface.hover();expect(await fillOpacity(page,n.id)).toBe('1');await expect(surface.locator('.ms-text')).toHaveText('Visible content');await expect(card(page,label.id).locator('.ms-node-background')).toHaveCSS('display','none');}finally{await desktop.close();}
 const touch=await browser.newContext({offline:true,viewport:{width:1500,height:950},hasTouch:true,isMobile:true,reducedMotion:'reduce'});
 try{const page=await touch.newPage();await page.goto(pathToFileURL(file).href);expect(await page.evaluate(()=>matchMedia('(any-hover:none)').matches)).toBe(true);expect(await fillOpacity(page,n.id)).toBe('1');await expect(card(page,n.id).locator('.ms-text')).toHaveText('Visible content');}finally{await touch.close();}
});

