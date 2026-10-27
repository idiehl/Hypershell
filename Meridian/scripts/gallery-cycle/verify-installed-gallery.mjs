import assert from 'node:assert/strict';
import path from 'node:path';
import { readFile,writeFile } from 'node:fs/promises';
import { chromium,expect } from '@playwright/test';
import { galleryItems } from '../../src/core/gallery.ts';

const workspace=path.resolve(import.meta.dirname,'../..'),qa=path.join(workspace,'output/gallery-qa-20261008');
const origin='http://127.0.0.1:5240',evidence=JSON.parse(await readFile(path.join(qa,'live-zeus-gallery-applied.json'),'utf8'));
const saved=await (await fetch(`${origin}/api/projects/${evidence.projectId}`)).json(),items=galleryItems(saved,evidence.nodeId),parent=saved.nodes.find(node=>node.id===evidence.nodeId);
assert.equal(items.length,5);assert.equal(parent.title,'Zeus · Gallery');
const browser=await chromium.launch(),context=await browser.newContext({viewport:{width:1700,height:1080}}),browserErrors=[],blockedWrites=[];
context.on('page',page=>page.on('pageerror',error=>browserErrors.push(error.message)));
await context.route('**/*',async route=>{
  const request=route.request(),url=request.url();
  if(!['GET','HEAD'].includes(request.method())){blockedWrites.push({method:request.method(),url});await route.abort();}
  else if(/^https?:/i.test(url)&&new URL(url).origin!==origin)await route.abort();
  else await route.continue();
});
try{
  const page=await context.newPage();await page.goto(`${origin}/?project=${saved.id}&canvas=${parent.canvasId}`);await expect(page.locator('.workspace')).toBeVisible();
  await page.getByRole('combobox',{name:'Side panel',exact:true}).selectOption('canvas');await page.getByRole('searchbox',{name:'Search canvas nodes',exact:true}).fill(parent.id);await page.locator(`button[data-jump-to-node="${parent.id}"]`).click();
  await page.getByRole('combobox',{name:'Side panel',exact:true}).selectOption('relations');
  const gallery=page.locator(`.ms-gallery[data-gallery-id="${parent.id}"]`),tile=page.locator(`.ms-node[data-node-id="${parent.id}"]`);
  for(let index=0;index<items.length;index++){
    await expect(gallery.locator('.ms-gallery-item')).toHaveAttribute('data-gallery-item-id',items[index].id);await expect(gallery.locator('.ms-gallery-item')).toHaveCount(1);
    await expect.poll(()=>gallery.locator('img').evaluate(image=>image.complete&&image.naturalWidth>0)).toBe(true);
    for(const item of items)await expect(page.locator(`.ms-node[data-node-id="${item.id}"]`)).toHaveCount(0);
    if(index===0){await tile.screenshot({path:path.join(qa,'installed-zeus-first.png')});await page.screenshot({path:path.join(qa,'installed-zeus-relations.png')});}
    if(index===4)await tile.screenshot({path:path.join(qa,'installed-zeus-last.png')});
    if(index<4)await gallery.getByRole('button',{name:`Next object in ${parent.title}`,exact:true}).click();
  }
  await expect(gallery.getByRole('button',{name:`Next object in ${parent.title}`,exact:true})).toBeDisabled();
  assert.deepEqual(await (await fetch(`${origin}/api/projects/${saved.id}`)).json(),saved);assert.deepEqual(blockedWrites,[]);assert.deepEqual(browserErrors,[]);
  await writeFile(path.join(qa,'installed-gallery-review.json'),JSON.stringify({passed:true,projectId:saved.id,revision:saved.revision,nodeId:parent.id,objects:items.length,objectTitles:items.map(item=>item.title),savedGraphUnchanged:true,blockedWrites,browserErrors,screenshots:['installed-zeus-first.png','installed-zeus-relations.png','installed-zeus-last.png'].map(file=>path.join(qa,file))},null,2));
  console.log(JSON.stringify({passed:true,revision:saved.revision,objects:items.length,browserErrors,blockedWrites},null,2));
}finally{await context.close();await browser.close();}
