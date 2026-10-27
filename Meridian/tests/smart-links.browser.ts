import { test, expect, type Page, type APIRequestContext } from '@playwright/test';
import { mkdir, writeFile } from 'node:fs/promises';
import { newProject, addNode, commitSnapshots } from '../src/core/model.ts';
import { createSmartLink, addSmartLinkMember, reflowSmartLinks, smartLinkGeometry, smartLinkMembers } from '../src/core/smart-links.ts';
import { navigationIndex } from '../src/core/navigation.ts';
import { gridGeometry } from '../src/core/cells.ts';
import { buildPublication, readPublication } from '../src/publication.ts';
import type { MeridianProject, MeridianNode, Point } from '../src/core/types.ts';

const card = (page: Page, id: string) => page.locator(`.ms-node[data-node-id="${id}"]`);
async function saved(request: APIRequestContext, id: string): Promise<MeridianProject> { const response = await request.get(`/api/projects/${id}`); expect(response.ok(),await response.text()).toBe(true); return response.json(); }
async function install(page: Page, request: APIRequestContext, project: MeridianProject) {
  const response = await request.post('/api/projects', { data: commitSnapshots(project) }); expect(response.ok(),await response.text()).toBe(true);
  const baseline: MeridianProject = await response.json(); await page.goto(`/?project=${project.id}&canvas=${project.entryCanvasId}`); await expect(page.locator('.workspace')).toBeVisible(); return baseline;
}
async function createMode(page: Page) { await page.locator('.workspace-create').click(); await page.getByRole('menuitem',{name:'Node',exact:true}).click(); }
async function railPoint(page: Page, id: string, point: Point): Promise<Point> {
  return page.locator(`.ms-smart-rail[data-smart-link-id="${id}"]`).evaluate((element,p) => { const matrix=(element as SVGGraphicsElement).getScreenCTM()!;const screen=new DOMPoint(p.x,p.y).matrixTransform(matrix);return{x:screen.x,y:screen.y}; },point);
}
async function clickRail(page: Page, project: MeridianProject, id: string, position: 'tail'|'middle' = 'tail') {
  const geometry=smartLinkGeometry(project,id)!;
  let point:Point;
  if(position==='middle'&&geometry.memberIds.length>1) {
    const navigation=navigationIndex(project),a=navigation.bounds(navigation.nodes.get(geometry.memberIds[0])!),b=navigation.bounds(navigation.nodes.get(geometry.memberIds[1])!);
    point=geometry.direction==='down'||geometry.direction==='up'?{x:geometry.origin.x,y:(a.y+a.height/2+b.y+b.height/2)/2}:{x:(a.x+a.width/2+b.x+b.width/2)/2,y:geometry.origin.y};
  } else point={x:geometry.end.x+(geometry.direction==='right'?-1:geometry.direction==='left'?1:0),y:geometry.end.y+(geometry.direction==='down'?-1:geometry.direction==='up'?1:0)};
  const screen=await railPoint(page,id,point);await page.mouse.click(screen.x,screen.y);
}
async function addThroughRail(page:Page,request:APIRequestContext,projectId:string,railId:string,title:string,position:'tail'|'middle'='tail') {
  const before=await saved(request,projectId); await createMode(page); await clickRail(page,before,railId,position);
  const dialog=page.getByRole('dialog',{name:'New Node',exact:true});await expect(dialog).toBeVisible();
  const types=await dialog.getByLabel('Type',{exact:true}).locator('option').allTextContents();expect(types).toContain('Media');expect(types).toContain('Container');
  await dialog.getByLabel('Type',{exact:true}).selectOption('media');await dialog.getByLabel('Tag',{exact:true}).fill(title);await dialog.getByLabel('Title',{exact:true}).fill(title);
  await expect(dialog.getByLabel('Sizing',{exact:true})).toHaveValue('auto');await expect(dialog.getByLabel('Title font size',{exact:true})).toHaveValue('32');
  await dialog.getByRole('button',{name:'Create node',exact:true}).click();await expect(dialog).toHaveCount(0);
  await expect.poll(async()=>smartLinkMembers(await saved(request,projectId),railId).length).toBe(smartLinkMembers(before,railId).length+1);
  return saved(request,projectId);
}
async function selectProperties(page:Page,id:string){await card(page,id).click({position:{x:8,y:8}});await page.getByRole('combobox',{name:'Side panel',exact:true}).selectOption('properties');}
async function noVisibleOverlap(page:Page){
  const boxes=await page.locator('.ms-node:not(.ms-utility)').evaluateAll(nodes=>nodes.map(node=>{const b=node.getBoundingClientRect();return{id:(node as HTMLElement).dataset.nodeId!,x:b.x,y:b.y,width:b.width,height:b.height};}).filter(b=>b.width&&b.height));
  const overlaps:string[]=[];for(let a=0;a<boxes.length;a++)for(let b=a+1;b<boxes.length;b++){const x=boxes[a],y=boxes[b];if(Math.min(x.x+x.width,y.x+y.width)-Math.max(x.x,y.x)>.5&&Math.min(x.y+x.height,y.y+y.height)-Math.max(x.y,y.y)>.5)overlaps.push(`${x.id}:${y.id}`);}
  expect(overlaps).toEqual([]);return boxes;
}
async function clearRoutes(page:Page){
  return page.locator('.ms-smart-rail-line').evaluateAll(paths=>{
    const nodes=[...document.querySelectorAll<HTMLElement>('.ms-node:not(.ms-utility)')].map(node=>({id:node.dataset.nodeId!,rect:node.getBoundingClientRect()})); const hits:string[]=[];
    for(const element of paths){const path=element as SVGPathElement,matrix=path.getScreenCTM()!,length=path.getTotalLength(),count=Math.min(2000,Math.max(2,Math.ceil(length/3)));for(let i=1;i<count;i++){const p=path.getPointAtLength(length*i/count),q=new DOMPoint(p.x,p.y).matrixTransform(matrix);for(const node of nodes){const b=node.rect;if(q.x>b.left+.7&&q.x<b.right-.7&&q.y>b.top+.7&&q.y<b.bottom-.7){hits.push(node.id);break;}}}}
    return [...new Set(hits)];
  });
}

test('place a Smart Link, add and insert normal nodes, configure/detach, undo/redo and reload',async({page,request},info)=>{
  const errors:string[]=[];page.on('pageerror',e=>errors.push(e.message));const p=newProject('Smart Link authoring');await install(page,request,p);
  await page.getByRole('button',{name:'Place Smart Link',exact:true}).click();const viewport=(await page.locator('.ms-viewport').boundingBox())!;await page.mouse.click(viewport.x+170,viewport.y+140);
  await expect(page.locator('.ms-smart-rail')).toHaveCount(1);await expect.poll(async()=> (await saved(request,p.id)).nodes.filter(n=>n.smartLink).length).toBe(1);
  let current=await saved(request,p.id);const rail=current.nodes.find(n=>n.smartLink)!;expect(rail.width).toBe(12);expect(rail.height).toBe(12);
  current=await addThroughRail(page,request,p.id,rail.id,'Documents');const first=smartLinkMembers(current,rail.id)[0];
  expect(first.kind).toBe('media');expect(first.style?.textAlign).toBe('center');expect(first.style?.textLayout).toBe('balanced');expect(first.style?.titleSize).toBe(32);expect(first.sizeMode).toBe('auto');
  current=await addThroughRail(page,request,p.id,rail.id,'Audio');current=await addThroughRail(page,request,p.id,rail.id,'Images','middle');
  expect(smartLinkMembers(current,rail.id).map(n=>n.title)).toEqual(['Documents','Images','Audio']);const inserted=smartLinkMembers(current,rail.id)[1];
  await page.getByRole('button',{name:'Undo',exact:true}).click();await expect.poll(async()=>smartLinkMembers(await saved(request,p.id),rail.id).length).toBe(2);
  await page.getByRole('button',{name:'Redo',exact:true}).click();await expect.poll(async()=>smartLinkMembers(await saved(request,p.id),rail.id).map(n=>n.id)).toEqual(smartLinkMembers(current,rail.id).map(n=>n.id));
  await page.reload();await expect(page.locator('.ms-smart-rail')).toHaveCount(1);await expect(card(page,inserted.id)).toBeVisible();
  await page.locator(`.ms-smart-rail[data-smart-link-id="${rail.id}"]`).focus();await page.keyboard.press('Enter');await page.getByRole('combobox',{name:'Side panel',exact:true}).selectOption('properties');
  await page.getByLabel('Smart Link direction',{exact:true}).selectOption('right');await expect.poll(async()=>(await saved(request,p.id)).nodes.find(n=>n.id===rail.id)?.smartLink?.direction).toBe('right');
  await page.getByLabel('Smart Link gap',{exact:true}).fill('64');await page.getByLabel('Smart Link gap',{exact:true}).press('Tab');await expect.poll(async()=>(await saved(request,p.id)).nodes.find(n=>n.id===rail.id)?.smartLink?.gap).toBe(64);
  await selectProperties(page,inserted.id);await page.getByRole('button',{name:'Detach from Smart Link',exact:true}).click();
  await expect.poll(async()=>(await saved(request,p.id)).nodes.find(n=>n.id===inserted.id)?.parentId).toBe(rail.parentId);
  await page.getByRole('button',{name:'Undo',exact:true}).click();await expect.poll(async()=>(await saved(request,p.id)).nodes.find(n=>n.id===inserted.id)?.parentId).toBe(rail.id);
  await page.getByRole('button',{name:'Reset view',exact:true}).click();await page.mouse.move(1490,30);await noVisibleOverlap(page);await page.screenshot({path:info.outputPath('smart-link-authored.png')});expect(errors).toEqual([]);
});

function branchFixture(){
  const p=newProject('Resources');p.canvases[0].title='Resources';p.canvases[0].style={background:'#0c1c24',gridVisible:false,backgroundPattern:'none'};
  const root=p.nodes.find(n=>n.id===p.canvases[0].rootId)!;root.title='Resources';root.tag='Resources';
  const outer=createSmartLink(p,p.entryCanvasId,{x:180,y:130},{direction:'down',gap:32});outer.tag='Resources';
  const members=['Documents','Images','Audio'].map((title,index)=>{const n=addNode(p,p.entryCanvasId,'media',title);Object.assign(n,{tag:title,x:250,y:160+index*110,width:230,height:72,sizeMode:'fixed',collapsed:true,style:{fill:'#17343c',border:'#608b86',textColor:'#e9f0ed',accent:'#b3d7c6',titleSize:30,fontSize:22,textAlign:'center',radius:0,padding:12}});addSmartLinkMember(p,outer.id,n.id);return n;});
  const inner=createSmartLink(p,p.entryCanvasId,{x:610,y:170},{parentId:members[0].id,direction:'down',gap:28});inner.tag='Documents';
  const leaves=['Markdown','Tables','PDF'].map((title,index)=>{const n=addNode(p,p.entryCanvasId,'media',title);Object.assign(n,{tag:title,parentId:inner.id,x:680,y:200+index*110,width:210,height:70,sizeMode:'fixed',collapsed:false,style:{fill:'#203b43',border:'#608b86',textColor:'#e9f0ed',titleSize:28,fontSize:22,textAlign:'center',radius:0,padding:10}});return n;});
  p.links.push({id:crypto.randomUUID(),canvasId:p.entryCanvasId,sourceId:members[0].id,targetId:inner.id,parentId:members[0].id,kind:'visual',relation:'generic',direction:'forward',label:'format'});
  reflowSmartLinks(p,p.entryCanvasId);return{project:p,outer,inner,members,leaves};
}
const geometry=(p:MeridianProject,ids:string[])=>ids.map(id=>{const n=p.nodes.find(n=>n.id===id)!;return{id,x:n.x,y:n.y,width:n.width,height:n.height};});
async function disclose(page:Page,id:string,expanded:boolean){await card(page,id).focus();await page.keyboard.press(expanded?'Alt+ArrowRight':'Alt+ArrowLeft');await page.mouse.move(1490,30);}

test('visible nested branches spread neighboring members and collapse compacts without drift in the real editor',async({page,request},info)=>{
  const f=branchFixture(),baseline=await install(page,request,f.project),ids=f.members.map(n=>n.id);await noVisibleOverlap(page);await page.screenshot({path:info.outputPath('smart-link-collapsed.png')});
  const compact=geometry(baseline,ids);
  for(let repeat=0;repeat<3;repeat++){
    await disclose(page,f.members[0].id,true);await expect(card(page,f.leaves[0].id)).toBeVisible();await expect.poll(async()=>(await saved(request,baseline.id)).nodes.find(n=>n.id===f.members[0].id)?.collapsed).toBe(false);
    const opened=await saved(request,baseline.id),spread=geometry(opened,ids),rail=opened.nodes.find(n=>n.id===f.outer.id)!;
    expect(spread[0].y).toBeLessThan(compact[0].y-1);expect(spread[1].y).toBeGreaterThan(compact[1].y+1);expect(spread[2].y).toBeGreaterThan(compact[2].y+1);
    expect(spread[2].y+spread[2].height-spread[0].y).toBeGreaterThan(compact[2].y+compact[2].height-compact[0].y+80);
    expect((spread[0].y+spread[2].y+spread[2].height)/2).toBeCloseTo(rail.y+rail.height/2,5);
    await noVisibleOverlap(page);expect(await clearRoutes(page)).toEqual([]);
    if(repeat===0){await page.getByRole('button',{name:'Reset view',exact:true}).click();await page.mouse.move(1490,30);await page.screenshot({path:info.outputPath('smart-link-expanded.png')});}
    await disclose(page,f.members[0].id,false);await expect(card(page,f.leaves[0].id)).toHaveCount(0);await expect.poll(async()=>(await saved(request,baseline.id)).nodes.find(n=>n.id===f.members[0].id)?.collapsed).toBe(true);
    expect(geometry(await saved(request,baseline.id),ids)).toEqual(compact);
  }
  await page.reload();await expect(card(page,f.members[0].id)).toBeVisible();expect(geometry(await saved(request,baseline.id),ids)).toEqual(compact);
  await mkdir('output/smart-links-cycle-20261024/ui-review',{recursive:true});await writeFile('output/smart-links-cycle-20261024/ui-review/branch-fixture.json',JSON.stringify({projectId:baseline.id,memberIds:ids,railIds:[f.outer.id,f.inner.id],source:baseline},null,2));
});

test('published Smart Links disclose and compact in a runtime copy with authored payload and saved source unchanged',async({page,request,context},info)=>{
  const f=branchFixture(),baseline=await install(page,request,f.project);const sourceBefore=await saved(request,baseline.id);
  const js=await(await request.get('/viewer.js')).text(),css=await(await request.get('/scene.css')).text(),html=buildPublication(sourceBefore,js,css),raw=/<script type="application\/json" id="meridian-data">([\s\S]*?)<\/script>/.exec(html)![1],payload=JSON.parse(raw),imported=readPublication(html);
  expect(imported.nodes.filter(n=>n.smartLink).length).toBe(2);expect(payload.project.nodes.map((n:MeridianNode)=>n.id)).toEqual(sourceBefore.nodes.map(n=>n.id));
  const viewer=await context.newPage();await viewer.route(/^https?:/,route=>route.abort());await viewer.setContent(html,{waitUntil:'domcontentloaded'});await expect(viewer.locator('.ms-smart-rail')).toHaveCount(1);
  const before=await card(viewer,f.members[1].id).evaluate(e=>({left:e.style.left,top:e.style.top}));
  for(let repeat=0;repeat<2;repeat++){
    await disclose(viewer,f.members[0].id,true);await expect(card(viewer,f.leaves[0].id)).toBeVisible();await noVisibleOverlap(viewer);expect(await clearRoutes(viewer)).toEqual([]);
    expect(await card(viewer,f.members[1].id).evaluate(e=>({left:e.style.left,top:e.style.top}))).not.toEqual(before);
    await disclose(viewer,f.members[0].id,false);await expect(card(viewer,f.leaves[0].id)).toHaveCount(0);expect(await card(viewer,f.members[1].id).evaluate(e=>({left:e.style.left,top:e.style.top}))).toEqual(before);
  }
  expect(await saved(request,baseline.id)).toEqual(sourceBefore);expect(await viewer.locator('#meridian-data').textContent()).toBe(raw);await viewer.screenshot({path:info.outputPath('smart-link-publication.png')});await viewer.close();
});

test('group selected existing branches into a Smart Link while retaining their identities and ordinary relationship',async({page,request})=>{
  const p=newProject('Existing branches'),a=addNode(p,p.entryCanvasId,'media','Alpha'),b=addNode(p,p.entryCanvasId,'media','Beta');
  Object.assign(a,{x:120,y:120,width:220,height:100,sizeMode:'fixed'});Object.assign(b,{x:480,y:120,width:220,height:100,sizeMode:'fixed'});
  p.links.push({id:crypto.randomUUID(),canvasId:p.entryCanvasId,sourceId:a.id,targetId:b.id,kind:'visual',relation:'mutual',direction:'none',label:'related'});
  const baseline=await install(page,request,p);await card(page,a.id).click({position:{x:10,y:10}});await card(page,b.id).click({position:{x:10,y:10},modifiers:['Shift']});
  await page.getByRole('combobox',{name:'Side panel',exact:true}).selectOption('relations');
  await expect(page.getByRole('button',{name:'Create Smart Link',exact:true})).toBeEnabled();await page.getByRole('button',{name:'Create Smart Link',exact:true}).click();
  await expect.poll(async()=>(await saved(request,p.id)).nodes.filter(n=>n.smartLink).length).toBe(1);const after=await saved(request,p.id),owner=after.nodes.find(n=>n.smartLink)!;
  expect(smartLinkMembers(after,owner.id).map(n=>n.id)).toEqual([a.id,b.id]);expect(after.links).toEqual(baseline.links);expect(after.nodes.find(n=>n.id===a.id)?.title).toBe('Alpha');expect(after.nodes.find(n=>n.id===b.id)?.title).toBe('Beta');
  await page.getByRole('button',{name:'Undo',exact:true}).click();await expect.poll(async()=>(await saved(request,p.id)).nodes.some(n=>n.smartLink)).toBe(false);expect((await saved(request,p.id)).links).toEqual(baseline.links);
});

test('a Smart Link reserves only a bounded Window viewport while forty rows scroll locally',async({page,request,context},info)=>{
  const p=newProject('Window footprint'),rail=createSmartLink(p,p.entryCanvasId,{x:120,y:100},{direction:'down',gap:48});
  const window=addNode(p,p.entryCanvasId,'container','Register');
  Object.assign(window,{x:180,y:160,width:400,height:250,sizeMode:'fixed',containerVariant:'window',collapsed:false,childLayout:'contained',childArrangement:'grid',grid:{columns:1,rows:40,cellWidth:380,cellHeight:100,windowColumns:1,windowRows:2,scrollDirection:'vertical',showHeaders:false,gap:0},style:{fill:'#17343c',border:'#608b86',textColor:'#e9f0ed',titleSize:24,fontSize:18,padding:5,radius:0}});
  addSmartLinkMember(p,rail.id,window.id);
  const surface=gridGeometry(window,p),cells:string[]=[];
  for(let row=1;row<=40;row++){const cell=addNode(p,p.entryCanvasId,'media',`Item ${row}`);Object.assign(cell,{parentId:window.id,x:surface.left,y:surface.top+(row-1)*surface.cellHeight,width:surface.cellWidth,height:surface.cellHeight,sizeMode:'fixed',cell:{column:1,row},style:{fill:'#203b43',textColor:'#e9f0ed',titleSize:22,padding:5}});cells.push(cell.id);}
  const peer=addNode(p,p.entryCanvasId,'media','Next');Object.assign(peer,{x:180,y:600,width:200,height:90,sizeMode:'fixed',style:{titleSize:28,fontSize:22,padding:5}});addSmartLinkMember(p,rail.id,peer.id);
  reflowSmartLinks(p,p.entryCanvasId);expect(peer.y-window.y).toBe(298);
  let baseline=await install(page,request,p);const pane=page.locator(`.ms-window-viewport[data-window-id="${window.id}"]`);
  // Force the real editor's edit/reflow path as well as the source fixture:
  // merely rendering precomputed coordinates could conceal a stale browser solver.
  await page.locator(`.ms-smart-rail[data-smart-link-id="${rail.id}"]`).focus();await page.keyboard.press('Enter');await page.getByRole('combobox',{name:'Side panel',exact:true}).selectOption('properties');
  for(const gap of [64,48]){await page.getByLabel('Smart Link gap',{exact:true}).fill(String(gap));await page.getByLabel('Smart Link gap',{exact:true}).press('Tab');await expect.poll(async()=>{const current=await saved(request,p.id);return current.nodes.find(n=>n.id===peer.id)!.y-current.nodes.find(n=>n.id===window.id)!.y;}).toBe(250+gap);}
  baseline=await saved(request,p.id);
  const routed=smartLinkGeometry(baseline,rail.id)!;expect(routed.blockedNodeIds??[]).toEqual([]);expect(routed.segments.some(segment=>segment.nodeId===window.id)).toBe(true);
  await expect(page.locator(`.ms-smart-rail[data-smart-link-id="${rail.id}"]`)).not.toHaveClass(/is-blocked/);await expect(page.locator('.smart-link-settings')).not.toContainText('A route is blocked');
  await expect(pane).toBeVisible();await page.mouse.move(1490,30);
  const actual=await card(page,window.id).boundingBox(),following=await card(page,peer.id).boundingBox();expect(actual).not.toBeNull();expect(following).not.toBeNull();
  const scale=actual!.height/250;expect((following!.y-actual!.y)/scale).toBeCloseTo(298,3);expect(actual!.y+actual!.height).toBeLessThan(following!.y);
  const reachesWindow=await page.locator(`.ms-smart-rail[data-smart-link-id="${rail.id}"] .ms-smart-rail-line`).evaluate((element,windowId)=>{const path=element as SVGPathElement,matrix=path.getScreenCTM()!,target=document.querySelector<HTMLElement>(`.ms-node[data-node-id="${windowId}"]`)!.getBoundingClientRect(),length=path.getTotalLength(),count=Math.min(4000,Math.ceil(length*2));for(let i=0;i<=count;i++){const at=path.getPointAtLength(length*i/count),point=new DOMPoint(at.x,at.y).matrixTransform(matrix);if(Math.hypot(point.x-target.left,point.y-(target.top+target.height/2))<1.5)return true;}return false;},window.id);expect(reachesWindow).toBe(true);
  expect(await pane.evaluate(e=>e.scrollHeight)).toBeGreaterThanOrEqual(4000);const viewportHeight=await pane.evaluate(e=>e.clientHeight);expect(viewportHeight).toBeGreaterThan(180);expect(viewportHeight).toBeLessThanOrEqual(250);
  const beforeGeometry=geometry(baseline,[window.id,peer.id,...cells]),beforeStyles=await card(page,peer.id).evaluate(e=>({left:e.style.left,top:e.style.top}));
  await pane.evaluate(e=>{e.scrollTop=2000;});await expect.poll(()=>pane.evaluate(e=>e.scrollTop)).toBe(2000);
  expect(geometry(await saved(request,p.id),[window.id,peer.id,...cells])).toEqual(beforeGeometry);expect(await saved(request,p.id)).toEqual(baseline);expect(await card(page,peer.id).evaluate(e=>({left:e.style.left,top:e.style.top}))).toEqual(beforeStyles);
  expect((await saved(request,p.id)).nodes.filter(n=>n.parentId===window.id).map(n=>n.id)).toEqual(cells);
  await page.screenshot({path:info.outputPath('smart-link-window-footprint.png')});
  const js=await(await request.get('/viewer.js')).text(),css=await(await request.get('/scene.css')).text(),html=buildPublication(baseline,js,css),viewer=await context.newPage();await viewer.route(/^https?:/,route=>route.abort());await viewer.setContent(html,{waitUntil:'domcontentloaded'});
  const published=viewer.locator(`.ms-window-viewport[data-window-id="${window.id}"]`);await expect(published).toBeVisible();const peerBefore=await card(viewer,peer.id).evaluate(e=>({left:e.style.left,top:e.style.top}));
  await published.evaluate(e=>{e.scrollTop=2500;});await expect.poll(()=>published.evaluate(e=>e.scrollTop)).toBe(2500);expect(await card(viewer,peer.id).evaluate(e=>({left:e.style.left,top:e.style.top}))).toEqual(peerBefore);expect(await saved(request,p.id)).toEqual(baseline);await viewer.close();
});
