import { test, expect } from '@playwright/test';
import { addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import type { MeridianProject } from '../src/core/types.ts';

test('nested Galleries respect disclosure, keep inspection with the visible object, and move as a canvas tile', async ({page,request}) => {
  const project=newProject('Nested galleries'),canvas=project.canvases[0]!;
  const outer=addNode(project,canvas.id,'container','Outer gallery');Object.assign(outer,{x:60,y:60,containerVariant:'gallery',childLayout:'contained',childArrangement:'free',collapsed:false,width:470,height:420});
  const inner=addNode(project,canvas.id,'container','Inner gallery');Object.assign(inner,{x:9000,y:9000,parentId:outer.id,containerVariant:'gallery',childLayout:'contained',childArrangement:'free',collapsed:true});
  const text=addNode(project,canvas.id,'text','An inner passage');Object.assign(text,{x:9100,y:9100,parentId:inner.id,text:'An inner passage that stays hidden until revealed.'});
  const second=addNode(project,canvas.id,'text','Second outer object');Object.assign(second,{x:9200,y:9200,parentId:outer.id,text:'The other visible object.'});
  const initial=await (await request.post('/api/projects',{data:commitSnapshots(project)})).json() as MeridianProject;
  await page.goto(`/?project=${initial.id}&canvas=${canvas.id}`);
  const outerView=page.locator(`.ms-gallery[data-gallery-id="${outer.id}"]`),innerView=page.locator(`.ms-gallery[data-gallery-id="${inner.id}"]`);
  await expect(outerView).toContainText('Inner gallery');await expect(innerView).toHaveCount(0);await expect(outerView).not.toContainText(text.text!);
  await outerView.getByRole('button',{name:'Reveal gallery: Inner gallery',exact:true}).click();
  await expect(innerView).toContainText(text.text!);await expect(page.locator('.save-state')).toHaveText(/^Saved · r\d+$/);
  await innerView.locator('.ms-gallery-item-title').click();
  await expect(page.locator('.inspector').getByLabel('Title',{exact:true})).toHaveValue(text.title);
  await outerView.getByRole('button',{name:'Next object in Outer gallery',exact:true}).click();
  await expect(page.locator('.inspector').getByLabel('Title',{exact:true})).toHaveValue(second.title);
  await outerView.getByRole('button',{name:'Previous object in Outer gallery',exact:true}).click();
  await innerView.locator('.ms-gallery-item-title').click();
  const before=await (await request.get(`/api/projects/${initial.id}`)).json() as MeridianProject;
  await page.locator('.ms-viewport').focus();await page.keyboard.press('ArrowRight');
  await expect.poll(async()=>{const saved=await (await request.get(`/api/projects/${initial.id}`)).json() as MeridianProject;return saved.nodes.find(node=>node.id===outer.id)!.x;}).toBe(before.nodes.find(node=>node.id===outer.id)!.x+5);
  const after=await (await request.get(`/api/projects/${initial.id}`)).json() as MeridianProject;
  for(const node of [outer,inner,text,second]){const a=after.nodes.find(item=>item.id===node.id)!,b=before.nodes.find(item=>item.id===node.id)!;expect(a.x-b.x).toBe(5);expect(a.y).toBe(b.y);}
  for(const node of [inner,text,second])await expect(page.locator(`.ms-node[data-node-id="${node.id}"]`)).toHaveCount(0);
});
