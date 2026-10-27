import { test, expect, type Page, type APIRequestContext } from '@playwright/test';
import { newProject, addNode, commitSnapshots } from '../src/core/model.ts';
import { createSmartLink, addSmartLinkMember, reflowSmartLinks, smartLinkGeometry, smartLinkMembers } from '../src/core/smart-links.ts';
import { navigationIndex } from '../src/core/navigation.ts';
import { buildPublication, readPublication } from '../src/publication.ts';
import type { MeridianNode, MeridianProject, Point } from '../src/core/types.ts';

type Axis = 'right' | 'down';
const card=(page:Page,id:string)=>page.locator(`.ms-node[data-node-id="${id}"]`);
async function saved(request:APIRequestContext,id:string):Promise<MeridianProject>{const r=await request.get(`/api/projects/${id}`);expect(r.ok(),await r.text()).toBe(true);return r.json();}
async function install(page:Page,request:APIRequestContext,p:MeridianProject){const r=await request.post('/api/projects',{data:commitSnapshots(p)});expect(r.ok(),await r.text()).toBe(true);const baseline:MeridianProject=await r.json();await page.goto(`/?project=${p.id}&canvas=${p.entryCanvasId}`);await expect(page.locator('.workspace')).toBeVisible();return baseline;}
function fixture(axis:Axis){
  const p=newProject(axis==='right'?'Centered downward hierarchy':'Centered rightward hierarchy');p.canvases[0].style={background:'#0c1c24',gridVisible:false};
  const parent=addNode(p,p.entryCanvasId,'media','Parent');Object.assign(parent,{x:axis==='right'?600:80,y:axis==='right'?80:500,width:240,height:90,sizeMode:'fixed',collapsed:false,childLayout:'spatial',childArrangement:'free',style:{fill:'#17343c',border:'#608b86',textColor:'#e9f0ed',titleSize:30,fontSize:22,textAlign:'center',radius:0,padding:10}});
  const point=axis==='right'?{x:parent.x+parent.width/2,y:parent.y+parent.height+100}:{x:parent.x+parent.width+100,y:parent.y+parent.height/2};
  const rail=createSmartLink(p,p.entryCanvasId,point,{parentId:parent.id,direction:axis,gap:40});
  const members=['Archive','Current','Future'].map((title,i)=>{const n=addNode(p,p.entryCanvasId,'media',title);Object.assign(n,{tag:title,x:point.x+60,y:point.y+60,width:[180,260,200][i],height:[72,100,82][i],sizeMode:'fixed',collapsed:true,childLayout:'spatial',childArrangement:'free',style:{fill:'#17343c',border:'#608b86',textColor:'#e9f0ed',titleSize:28,fontSize:22,textAlign:'center',radius:0,padding:10}});addSmartLinkMember(p,rail.id,n.id);return n;});
  const first=members[0],nestedPoint=axis==='right'?{x:first.x+first.width/2,y:first.y+first.height+100}:{x:first.x+first.width+100,y:first.y+first.height/2};
  const nested=createSmartLink(p,p.entryCanvasId,nestedPoint,{parentId:first.id,direction:axis,gap:36});
  const leaves=['Longer branch','Short branch','Final branch'].map((title,i)=>{const n=addNode(p,p.entryCanvasId,'media',title);Object.assign(n,{parentId:nested.id,x:nestedPoint.x+50,y:nestedPoint.y+50,width:[300,140,210][i],height:[110,60,90][i],sizeMode:'fixed',collapsed:false,childPosition:'manual',style:{fill:'#203b43',border:'#608b86',textColor:'#e9f0ed',titleSize:26,fontSize:22,textAlign:'center',radius:0,padding:8}});return n;});
  for(const [owner,handle]of[[parent,rail],[first,nested]])p.links.push({id:crypto.randomUUID(),canvasId:p.entryCanvasId,parentId:owner.id,sourceId:owner.id,targetId:handle.id,kind:'visual',relation:'generic',direction:'none',sourceAnchor:axis==='right'?{x:.5,y:1}:{x:1,y:.5},targetAnchor:{x:.5,y:.5},style:{color:'#608b86',width:1.5}});
  reflowSmartLinks(p,p.entryCanvasId);return{project:p,parent,rail,members,nested,leaves,axis};
}
function positions(p:MeridianProject,ids:string[]){return ids.map(id=>{const n=p.nodes.find(n=>n.id===id)!;return{id,x:n.x,y:n.y,width:n.width,height:n.height};});}
function centeredModel(p:MeridianProject,parentId:string,railId:string,axis:Axis){
  const nav=navigationIndex(p),parent=nav.bounds(nav.nodes.get(parentId)!),members=smartLinkMembers(p,railId).map(n=>nav.bounds(n)),start=Math.min(...members.map(b=>axis==='right'?b.x:b.y)),end=Math.max(...members.map(b=>axis==='right'?b.x+b.width:b.y+b.height)),center=axis==='right'?parent.x+parent.width/2:parent.y+parent.height/2;
  expect((start+end)/2).toBeCloseTo(center,5);expect(start).toBeLessThan(center);expect(end).toBeGreaterThan(center);
  for(const b of members)if(axis==='right')expect(b.y).toBeGreaterThan(parent.y+parent.height);else expect(b.x).toBeGreaterThan(parent.x+parent.width);
  return{start,end,center};
}
async function centeredView(page:Page,parentId:string,memberIds:string[],axis:Axis){
  await expect(card(page,parentId)).toBeVisible();for(const id of memberIds)await expect(card(page,id)).toBeVisible();
  await expect.poll(async()=>page.evaluate(({parentId,memberIds,axis})=>{const parent=document.querySelector<HTMLElement>(`.ms-node[data-node-id="${parentId}"]`)!.getBoundingClientRect(),boxes=memberIds.map(id=>document.querySelector<HTMLElement>(`.ms-node[data-node-id="${id}"]`)!.getBoundingClientRect());const a=Math.min(...boxes.map(b=>axis==='right'?b.left:b.top)),b=Math.max(...boxes.map(b=>axis==='right'?b.right:b.bottom)),center=axis==='right'?(parent.left+parent.right)/2:(parent.top+parent.bottom)/2;return Math.abs((a+b)/2-center);},{parentId,memberIds,axis})).toBeLessThan(1.5);
}
async function noOverlap(page:Page){const boxes=await page.locator('.ms-node:not(.ms-utility)').evaluateAll(nodes=>nodes.map(node=>{const b=node.getBoundingClientRect();return{id:(node as HTMLElement).dataset.nodeId!,x:b.x,y:b.y,w:b.width,h:b.height};}));const collisions:string[]=[];for(let i=0;i<boxes.length;i++)for(let j=i+1;j<boxes.length;j++){const a=boxes[i],b=boxes[j];if(Math.min(a.x+a.w,b.x+b.w)-Math.max(a.x,b.x)>.75&&Math.min(a.y+a.h,b.y+b.h)-Math.max(a.y,b.y)>.75)collisions.push(`${a.id}:${b.id}`);}expect(collisions).toEqual([]);await expect(page.locator('.ms-smart-rail.is-blocked')).toHaveCount(0);}
async function disclose(page:Page,id:string,expanded:boolean,editor=true){await card(page,id).focus();await page.keyboard.press(expanded?'Alt+ArrowRight':'Alt+ArrowLeft');await page.mouse.move(1490,30);await page.getByRole('button',{name:editor?'Reset view':'Fit Work',exact:true}).focus();}
async function insertNegative(page:Page,request:APIRequestContext,p:MeridianProject,railId:string,axis:Axis){
  const nav=navigationIndex(p),geometry=smartLinkGeometry(p,railId)!,members=geometry.memberIds.slice(0,2).map(id=>nav.bounds(nav.nodes.get(id)!));
  const point:Point=axis==='right'?{x:(members[0].x+members[0].width/2+members[1].x+members[1].width/2)/2,y:geometry.origin.y}:{x:geometry.origin.x,y:(members[0].y+members[0].height/2+members[1].y+members[1].height/2)/2};
  expect(axis==='right'?point.x:point.y).toBeLessThan(axis==='right'?geometry.origin.x:geometry.origin.y);
  await page.locator('.workspace-create').click();await page.getByRole('menuitem',{name:'Node',exact:true}).click();
  const screen=await page.locator(`.ms-smart-rail[data-smart-link-id="${railId}"]`).evaluate((element,p)=>{const q=new DOMPoint(p.x,p.y).matrixTransform((element as SVGGraphicsElement).getScreenCTM()!);return{x:q.x,y:q.y};},point);await page.mouse.click(screen.x,screen.y);
  const dialog=page.getByRole('dialog',{name:'New Node',exact:true});await expect(dialog).toBeVisible();await dialog.getByLabel('Tag',{exact:true}).fill('Inserted');await dialog.getByLabel('Title',{exact:true}).fill('Inserted');await dialog.getByRole('button',{name:'Create node',exact:true}).click();await expect(dialog).toHaveCount(0);
  await expect.poll(async()=>smartLinkMembers(await saved(request,p.id),railId).length).toBe(4);return saved(request,p.id);
}

for(const axis of ['right','down'] as const){
  test(`open an older one-sided ${axis} chain centered without saving or changing its saved source`,async({page,request})=>{
    const f=fixture(axis),nav=navigationIndex(f.project),shifted=new Set(f.members.flatMap(n=>[n.id,...nav.descendants(n.id).map(child=>child.id)]));
    for(const node of f.project.nodes)if(shifted.has(node.id)){if(axis==='right')node.x+=600;else node.y+=600;}
    const writes:string[]=[];page.on('request',r=>{if(!['GET','HEAD','OPTIONS'].includes(r.method()))writes.push(`${r.method()} ${new URL(r.url()).pathname}`);});
    const baseline=await install(page,request,f.project),ids=f.members.map(n=>n.id),oldNav=navigationIndex(baseline),owner=oldNav.bounds(oldNav.nodes.get(f.parent.id)!),first=oldNav.bounds(oldNav.nodes.get(ids[0])!);
    expect(axis==='right'?first.x:first.y).toBeGreaterThan(axis==='right'?owner.x+owner.width/2:owner.y+owner.height/2);
    await centeredView(page,f.parent.id,ids,axis);await noOverlap(page);
    // Automatic save coalesces committed edits after 500ms. Entry normalization
    // is presentation-only and must not enqueue such an edit or a recovery POST.
    await page.waitForTimeout(650);expect(writes).toEqual([]);expect(await saved(request,baseline.id)).toEqual(baseline);
    await page.reload();await expect(card(page,f.parent.id)).toBeVisible();await centeredView(page,f.parent.id,ids,axis);await page.waitForTimeout(650);expect(writes).toEqual([]);expect(await saved(request,baseline.id)).toEqual(baseline);
  });
  test(`center ${axis} sibling chains on their parent through nested editor expansion and collapse`,async({page,request},info)=>{
    const f=fixture(axis),baseline=await install(page,request,f.project),ids=f.members.map(n=>n.id),compact=positions(baseline,ids);centeredModel(baseline,f.parent.id,f.rail.id,axis);await centeredView(page,f.parent.id,ids,axis);await noOverlap(page);
    for(let cycle=0;cycle<3;cycle++){
      await disclose(page,f.members[0].id,true);await expect(card(page,f.leaves[0].id)).toBeVisible();await expect.poll(async()=>(await saved(request,baseline.id)).nodes.find(n=>n.id===f.members[0].id)?.collapsed).toBe(false);const opened=await saved(request,baseline.id);
      centeredModel(opened,f.parent.id,f.rail.id,axis);centeredModel(opened,f.members[0].id,f.nested.id,axis);expect(positions(opened,ids)).not.toEqual(compact);await centeredView(page,f.parent.id,ids,axis);await centeredView(page,f.members[0].id,f.leaves.map(n=>n.id),axis);await noOverlap(page);
      if(!cycle){await page.getByRole('button',{name:'Reset view',exact:true}).click();await page.mouse.move(1490,30);await page.screenshot({path:info.outputPath(`centered-${axis}-expanded.png`)});}
      await disclose(page,f.members[0].id,false);await expect(card(page,f.leaves[0].id)).toHaveCount(0);await expect.poll(async()=>positions(await saved(request,baseline.id),ids)).toEqual(compact);await centeredView(page,f.parent.id,ids,axis);
    }
    await page.reload();await expect(card(page,f.parent.id)).toBeVisible();expect(positions(await saved(request,baseline.id),ids)).toEqual(compact);await centeredView(page,f.parent.id,ids,axis);
  });
  test(`insert on the negative half of a centered ${axis} Smart Link in source order`,async({page,request})=>{
    const f=fixture(axis),baseline=await install(page,request,f.project),current=await insertNegative(page,request,baseline,f.rail.id,axis);expect(smartLinkMembers(current,f.rail.id).map(n=>n.title)).toEqual(['Archive','Inserted','Current','Future']);centeredModel(current,f.parent.id,f.rail.id,axis);await centeredView(page,f.parent.id,smartLinkMembers(current,f.rail.id).map(n=>n.id),axis);await noOverlap(page);
    await page.getByRole('button',{name:'Undo',exact:true}).click();await expect.poll(async()=>smartLinkMembers(await saved(request,baseline.id),f.rail.id).map(n=>n.id)).toEqual(f.members.map(n=>n.id));await page.getByRole('button',{name:'Redo',exact:true}).click();await expect.poll(async()=>smartLinkMembers(await saved(request,baseline.id),f.rail.id).map(n=>n.title)).toEqual(['Archive','Inserted','Current','Future']);
  });
  test(`published ${axis} child chains remain centered and compact without changing authored bytes`,async({page,request,context},info)=>{
    const f=fixture(axis),baseline=await install(page,request,f.project),js=await(await request.get('/viewer.js')).text(),css=await(await request.get('/scene.css')).text(),html=buildPublication(baseline,js,css),raw=/<script type="application\/json" id="meridian-data">([\s\S]*?)<\/script>/.exec(html)![1],imported=readPublication(html);expect(imported.nodes.filter(n=>n.smartLink).length).toBe(2);
    const viewer=await context.newPage();await viewer.route(/^https?:/,r=>r.abort());await viewer.setContent(html,{waitUntil:'domcontentloaded'});const ids=f.members.map(n=>n.id),before=await Promise.all(ids.map(id=>card(viewer,id).evaluate(e=>({left:e.style.left,top:e.style.top}))));await centeredView(viewer,f.parent.id,ids,axis);
    for(let cycle=0;cycle<3;cycle++){await disclose(viewer,f.members[0].id,true,false);await expect(card(viewer,f.leaves[0].id)).toBeVisible();await centeredView(viewer,f.parent.id,ids,axis);await centeredView(viewer,f.members[0].id,f.leaves.map(n=>n.id),axis);await noOverlap(viewer);if(!cycle)await viewer.screenshot({path:info.outputPath(`centered-${axis}-publication.png`)});await disclose(viewer,f.members[0].id,false,false);await expect(card(viewer,f.leaves[0].id)).toHaveCount(0);expect(await Promise.all(ids.map(id=>card(viewer,id).evaluate(e=>({left:e.style.left,top:e.style.top}))))).toEqual(before);}
    expect(await viewer.locator('#meridian-data').textContent()).toBe(raw);expect(await saved(request,baseline.id)).toEqual(baseline);await viewer.close();
  });
}
