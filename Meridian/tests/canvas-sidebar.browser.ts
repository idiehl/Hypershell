import { test, expect, type APIRequestContext, type Page } from '@playwright/test';
import { addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import type { MeridianProject } from '../src/core/types.ts';
import { inspectorPanel } from './ui-helpers.ts';

const row = (page: Page, id: string) => page.locator(`[data-outline-node="${id}"]`);
const details = (page: Page, id: string) => page.locator(`[data-outline-details="${id}"]`);
const more = (page: Page, id: string) => page.locator(`[data-outline-more="${id}"]`);
const target = (page: Page, id: string) => page.locator(`[data-jump-to-node="${id}"]`);
const tile = (page: Page, id: string) => page.locator(`.ms-node[data-node-id="${id}"]`);
const search = (page: Page) => page.getByRole('searchbox', { name: 'Search Work nodes', exact: true });
const stored = async (request: APIRequestContext, id: string): Promise<MeridianProject> => (await request.get(`/api/projects/${id}`)).json();
const browserErrors = new WeakMap<Page,string[]>();
test.beforeEach(async({page})=>{const errors:string[]=[];browserErrors.set(page,errors);page.on('pageerror',error=>errors.push(error.message));});
test.afterEach(async({page})=>{expect(browserErrors.get(page)??[],'The sidebar must not trigger browser exceptions.').toEqual([]);});

async function fixture(page: Page, request: APIRequestContext, many = 0) {
  const project = newProject('Canvas library'), canvas = project.canvases[0]!;
  canvas.title = 'Canvas library'; project.nodes[0]!.title = canvas.title;
  canvas.snapping = { stickyEdges:false, smartAlign:false, snapToGrid:false };
  const parent = addNode(project,canvas.id,'media','Research collection');
  Object.assign(parent,{ x:100,y:120,width:260,height:170,sizeMode:'fixed',collapsed:true,text:'A compact collection.',childLayout:'spatial',remoteLinks:[{id:'outgoing-remote',targetId:'remote_archival_record',relation:'generic',label:'Archival source'}] });
  const first = addNode(project,canvas.id,'media','Hidden account');
  Object.assign(first,{parentId:parent.id,x:1680,y:1300,width:220,height:130,sizeMode:'fixed',collapsed:true,text:'A rare hidden-passage-token for searching.'});
  const second = addNode(project,canvas.id,'media','Second account');
  Object.assign(second,{parentId:parent.id,x:1940,y:1300,width:220,height:130,sizeMode:'fixed',text:'Sibling account.'});
  const grandchild = addNode(project,canvas.id,'media','Nested observation');
  Object.assign(grandchild,{parentId:first.id,x:1680,y:1500,width:180,height:100,sizeMode:'fixed',text:'A deeper observation.'});
  const peer = addNode(project,canvas.id,'media','Independent comparison');
  Object.assign(peer,{x:520,y:120,width:240,height:140,sizeMode:'fixed',text:'A separate node.',remoteLinks:[{id:'incoming-remote',targetId:parent.id,relation:'causal',label:'Incoming comparison'}]});
  const long = addNode(project,canvas.id,'media','An exceptionally long user chosen title that must leave both action buttons visible in a narrow sidebar');
  Object.assign(long,{x:520,y:400,width:240,height:150,sizeMode:'fixed',text:'Long-title-content.'});
  project.links.push(
    {id:'incident',canvasId:canvas.id,sourceId:parent.id,targetId:peer.id,kind:'visual',relation:'mutual'},
    {id:'self',canvasId:canvas.id,sourceId:parent.id,targetId:parent.id,kind:'visual',relation:'causal',sourceAnchor:{x:0,y:.5},targetAnchor:{x:0,y:.5}},
    {id:'owned-only',canvasId:canvas.id,parentId:parent.id,sourceId:first.id,targetId:second.id,kind:'visual',relation:'chronological'},
  );
  for (let at=0;at<many;at++) { const node=addNode(project,canvas.id,'media',`Catalog entry ${String(at+1).padStart(3,'0')}`);Object.assign(node,{x:900+at*260,y:150,width:220,height:130,sizeMode:'fixed',text:'Catalog entry body.'}); }
  let deep=long;
  if(many){long.collapsed=true;for(let at=0;at<14;at++){const node=addNode(project,canvas.id,'media',`Deep branch ${at+1} with a deliberately long title`);Object.assign(node,{parentId:deep.id,x:2300,y:500,width:200,height:120,sizeMode:'fixed',collapsed:true,text:'Deeply nested content.'});deep=node;}}
  const response=await request.post('/api/projects',{data:commitSnapshots(project)});expect(response.ok(),await response.text()).toBeTruthy();
  await page.goto(`/?project=${project.id}&canvas=${canvas.id}`);await inspectorPanel(page,'canvas');await expect(row(page,parent.id)).toBeVisible();
  return {project,root:canvas.rootId,parent,first,second,grandchild,peer,long,deep};
}

test('Canvas entries default to compact title/type rows with literal target and plus icons',async({page,request},info)=>{
  const f=await fixture(page,request),before=await stored(request,f.project.id);
  const parent=row(page,f.parent.id);
  await expect(parent.locator('.inspector-tree-title')).toContainText(f.parent.title);
  await expect(parent.locator('.inspector-tree-type')).toHaveText('Media');
  await expect(more(page,f.parent.id)).toHaveAttribute('aria-expanded','false');
  await expect(details(page,f.parent.id)).toBeHidden();
  await expect(parent.getByRole('button',{name:`Delete node: ${f.parent.title}`,exact:true})).toBeHidden();
  await expect(parent.locator('.inspector-tree-disclosure')).toHaveCount(0);
  await expect(target(page,f.parent.id).locator('svg')).toHaveCount(1);
  await expect(target(page,f.parent.id)).toHaveAttribute('aria-label',`Jump to Node: ${f.parent.title}`);
  const header=await parent.locator('.inspector-tree-title').boundingBox(),type=await parent.locator('.inspector-tree-type').boundingBox();
  expect(header&&type).toBeTruthy();expect(Math.abs(type!.y+type!.height/2-(header!.y+header!.height/2))).toBeLessThan(12);
  expect((await parent.boundingBox())!.height).toBeLessThan(65);
  await page.screenshot({path:info.outputPath('compact-canvas-sidebar.png')});
  await page.locator('.inspector').screenshot({path:info.outputPath('compact-sidebar-detail.png')});
  expect(await stored(request,f.project.id)).toEqual(before);
});

test('row disclosure, More properties, and Target selection operate independently',async({page,request})=>{
  const f=await fixture(page,request),before=await stored(request,f.project.id);
  await expect(row(page,f.parent.id)).toHaveAttribute('aria-expanded','false');
  // Blank cell space is part of the disclosure target, not only its title.
  await row(page,f.parent.id).click({position:{x:2,y:8}});
  await expect(row(page,f.first.id)).toBeVisible();await expect(tile(page,f.first.id)).toHaveCount(0);
  await more(page,f.parent.id).click();await expect(details(page,f.parent.id)).toBeVisible();
  await expect(row(page,f.parent.id)).toHaveAttribute('aria-expanded','true');await expect(row(page,f.first.id)).toBeVisible();
  await more(page,f.parent.id).click();await expect(details(page,f.parent.id)).toBeHidden();
  await expect(row(page,f.first.id)).toBeVisible();
  await target(page,f.parent.id).click();await expect(tile(page,f.parent.id)).toHaveAttribute('aria-selected','true');
  await expect(row(page,f.first.id)).toBeVisible();await expect(details(page,f.parent.id)).toBeHidden();
  await row(page,f.parent.id).locator('.inspector-tree-title').click();await expect(row(page,f.first.id)).toHaveCount(0);
  await expect(tile(page,f.parent.id)).toHaveAttribute('aria-selected','true');
  await row(page,f.root).locator('.inspector-tree-title').click();await expect(page.locator('[data-outline-node]')).toHaveCount(1);
  await row(page,f.root).locator('.inspector-tree-title').click();await expect(row(page,f.parent.id)).toBeVisible();
  expect(await stored(request,f.project.id)).toEqual(before);
});

test('properties distinguish direct sub-nodes from endpoint/self/remote links and hide deletion until open',async({page,request},info)=>{
  const f=await fixture(page,request);await more(page,f.parent.id).click();
  const properties=details(page,f.parent.id);await expect(properties).toBeVisible();await expect(properties).toContainText(f.parent.id);
  await expect(properties.locator('[data-subnode-count]')).toHaveAttribute('data-subnode-count','2');
  await expect(properties.locator('[data-link-count]')).toHaveAttribute('data-link-count','4');
  await expect(properties.locator('[data-subnode-count] svg')).toHaveCount(1);await expect(properties.locator('[data-link-count] svg')).toHaveCount(1);
  const remove=properties.getByRole('button',{name:`Delete node: ${f.parent.title}`,exact:true});await expect(remove).toBeVisible();await expect(remove.locator('svg')).toHaveCount(1);
  const db=await properties.boundingBox(),rb=await remove.boundingBox();expect(rb!.x+rb!.width).toBeGreaterThan(db!.x+db!.width-20);expect(rb!.y).toBeLessThan(db!.y+22);
  await more(page,f.root).click();await expect(details(page,f.root).getByRole('button',{name:'Delete node: Canvas library',exact:true})).toBeDisabled();
  await page.screenshot({path:info.outputPath('node-properties-icons.png')});
  await row(page,f.parent.id).screenshot({path:info.outputPath('node-properties-detail.png')});
  await more(page,f.parent.id).click();await expect(remove).toBeHidden();
});

test('targeting a hidden GUID selects and centers it, while revealed Delete persists and Undo restores it',async({page,request})=>{
  const f=await fixture(page,request);
  await search(page).fill(f.first.id);await target(page,f.first.id).click();
  await expect(tile(page,f.first.id)).toHaveAttribute('aria-selected','true');
  const bounds=(await tile(page,f.first.id).boundingBox())!,viewport=(await page.locator('.ms-viewport').boundingBox())!;
  expect(Math.abs(bounds.x+bounds.width/2-(viewport.x+viewport.width/2))).toBeLessThan(8);expect(Math.abs(bounds.y+bounds.height/2-(viewport.y+viewport.height/2))).toBeLessThan(8);
  await expect.poll(async()=> (await stored(request,f.project.id)).nodes.find(n=>n.id===f.parent.id)?.collapsed).toBe(false);
  await search(page).fill(f.peer.id);await more(page,f.peer.id).click();
  await details(page,f.peer.id).getByRole('button',{name:`Delete node: ${f.peer.title}`,exact:true}).click();
  await expect.poll(async()=> (await stored(request,f.project.id)).nodes.some(n=>n.id===f.peer.id)).toBe(false);
  await page.getByRole('button',{name:'Undo',exact:true}).click();await expect.poll(async()=> (await stored(request,f.project.id)).nodes.some(n=>n.id===f.peer.id)).toBe(true);
  await page.reload();await expect(row(page,f.peer.id)).toBeVisible();await more(page,f.peer.id).click();await expect(details(page,f.peer.id)).toContainText(f.peer.id);
});

test('keyboard and search retain compact details state and keep outline disclosure separate from canvas content',async({page,request})=>{
  const f=await fixture(page,request),before=await stored(request,f.project.id);
  const header=row(page,f.parent.id).locator('.inspector-tree-title');await header.focus();await page.keyboard.press('ArrowRight');await expect(row(page,f.first.id)).toBeVisible();await expect(header).toBeFocused();
  await page.keyboard.press('ArrowDown');await expect(row(page,f.first.id).locator('.inspector-tree-title')).toBeFocused();await page.keyboard.press('ArrowLeft');await expect(header).toBeFocused();
  await header.press('Space');await expect(row(page,f.parent.id)).toHaveAttribute('aria-expanded','false');await expect(row(page,f.first.id)).toHaveCount(0);
  await header.press('Space');await expect(row(page,f.parent.id)).toHaveAttribute('aria-expanded','true');await expect(row(page,f.first.id)).toBeVisible();
  await more(page,f.parent.id).focus();await page.keyboard.press('Enter');await expect(details(page,f.parent.id)).toBeVisible();await expect(more(page,f.parent.id)).toBeFocused();
  await search(page).fill('hidden-passage-token');await expect(row(page,f.first.id)).toBeVisible();await expect(details(page,f.first.id)).toBeHidden();
  await more(page,f.first.id).click();await expect(details(page,f.first.id)).toContainText(f.first.id);await expect(details(page,f.first.id)).not.toContainText('hidden-passage-token');
  await search(page).focus();await search(page).press('ArrowDown');await expect(row(page,f.first.id).locator('.inspector-tree-title')).toBeFocused();
  await search(page).focus();await search(page).press('Escape');await expect(search(page)).toHaveValue('');await expect(search(page)).toBeFocused();
  await inspectorPanel(page,'properties');await inspectorPanel(page,'canvas');await expect(details(page,f.parent.id)).toBeVisible();
  expect(await stored(request,f.project.id)).toEqual(before);
});

test('narrow long-title entries keep target and More reachable, with no sideways overflow or scroll jump',async({page,request},info)=>{
  const f=await fixture(page,request,75);const divider=page.getByRole('separator',{name:'Resize side panel',exact:true});await divider.focus();await divider.press('Home');
  await expect(divider).toHaveAttribute('aria-valuenow','310');for(let at=0;at<3;at++)await divider.press('ArrowRight');await expect(divider).toHaveAttribute('aria-valuenow','260');
  const item=row(page,f.long.id);await item.scrollIntoViewIfNeeded();
  const titleBox=(await item.locator('.inspector-tree-title').boundingBox())!,targetBox=(await target(page,f.long.id).boundingBox())!,moreBox=(await more(page,f.long.id).boundingBox())!;
  expect(titleBox.x+titleBox.width).toBeLessThanOrEqual(targetBox.x+1);expect(targetBox.x+targetBox.width).toBeLessThanOrEqual(moreBox.x+1);
  expect(await page.locator('.inspector-panel-body').evaluate(el=>el.scrollWidth<=el.clientWidth+1)).toBe(true);
  const catalog=page.locator('[data-outline-node]').filter({hasText:'Catalog entry 050'});await catalog.scrollIntoViewIfNeeded();
  const scrollBefore=await page.locator('.inspector-panel-body').evaluate(el=>el.scrollTop);expect(scrollBefore).toBeGreaterThan(0);
  await catalog.locator('[data-outline-more]').click();const scrollAfter=await page.locator('.inspector-panel-body').evaluate(el=>el.scrollTop);expect(Math.abs(scrollAfter-scrollBefore)).toBeLessThan(3);
  await expect(catalog.locator('[data-outline-details]')).toBeVisible();
  await page.screenshot({path:info.outputPath('narrow-canvas-sidebar.png')});
  await search(page).fill(f.deep.id);const deepRow=row(page,f.deep.id);await deepRow.scrollIntoViewIfNeeded();await expect(deepRow).toHaveAttribute('aria-level','16');
  const deepBox=(await deepRow.boundingBox())!,deepTarget=(await target(page,f.deep.id).boundingBox())!,deepMore=(await more(page,f.deep.id).boundingBox())!;
  expect(deepTarget.x).toBeGreaterThanOrEqual(deepBox.x);expect(deepMore.x+deepMore.width).toBeLessThanOrEqual(deepBox.x+deepBox.width+1);
  await more(page,f.deep.id).click();expect(await page.locator('.inspector-panel-body').evaluate(el=>el.scrollWidth<=el.clientWidth+1)).toBe(true);await page.screenshot({path:info.outputPath('deep-minimum-width-sidebar.png')});
  await search(page).fill(f.long.id);await more(page,f.long.id).click();await page.screenshot({path:info.outputPath('long-title-properties.png')});
});
