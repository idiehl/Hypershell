// node --experimental-strip-types output/branch-atlas/genealogy-review-final.mjs [atlas.json]
// Uses the actual current standalone viewer, offline. Does not alter the supplied project.
import fs from 'node:fs/promises';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { build } from 'esbuild';
import { chromium } from '@playwright/test';
import assert from 'node:assert/strict';
import { buildPublication } from '../../src/publication.ts';
import { commitSnapshots } from '../../src/core/model.ts';
import { identifyGenealogy } from '../../scripts/branch-atlas/genealogy.mjs';
import genealogy from '../../scripts/mythology/genealogy-expanded.mjs';

const input=path.resolve(process.argv[2]??'output/branch-atlas/atlas.json');
const project=JSON.parse(await fs.readFile(input,'utf8'));
const baseline=JSON.parse(await fs.readFile('output/branch-atlas/baseline.json','utf8'));
const canonical=identifyGenealogy(baseline);
const sourceMap=new Map(project.nodes.map(node=>[node.id,node]));
const genealogyRoot=baseline.canvases.find(canvas=>canvas.title.startsWith('10 · Families')).rootId;
const inGenealogy=id=>{const seen=new Set();while(id&&!seen.has(id)){if(id===genealogyRoot)return true;seen.add(id);id=sourceMap.get(id)?.parentId;}return false;};
const js=await build({entryPoints:['src/viewer.ts'],bundle:true,format:'iife',target:['es2022'],write:false,minify:true});
const html=buildPublication(commitSnapshots(project),js.outputFiles[0].text,await fs.readFile('src/scene.css','utf8'));
const output='output/branch-atlas/genealogy-final-preview.html';
await fs.writeFile(output,html);
const browser=await chromium.launch({headless:true});
const page=await browser.newPage({viewport:{width:1680,height:1120}});
const errors=[],reviews=[];
page.on('pageerror',error=>errors.push(error.message));
await page.route(/^https?:/,route=>route.abort());
const card=id=>page.locator(`.ms-node[data-node-id="${id}"]`);
const camera=()=>page.locator('.ms-world').evaluate(element=>element.style.transform);
const rectangles=()=>page.locator('.ms-node').evaluateAll(nodes=>nodes.map(node=>({id:node.dataset.nodeId,title:node.querySelector('.ms-node-title')?.textContent,x:parseFloat(node.style.left),y:parseFloat(node.style.top),width:parseFloat(node.style.width),height:parseFloat(node.style.height)})));
const collisions=boxes=>boxes.flatMap((a,i)=>boxes.slice(i+1).filter(b=>Math.min(a.x+a.width,b.x+b.width)-Math.max(a.x,b.x)>1&&Math.min(a.y+a.height,b.y+b.height)-Math.max(a.y,b.y)>1).map(b=>[a.title,b.title]));
try{
  await page.goto(pathToFileURL(path.resolve(output)).href);
  const initialUrl=page.url(),initialCanvas=await page.getByLabel('Choose canvas',{exact:true}).inputValue();
  for(const key of ['gaia','uranus','cronus','zeus']){
    const id=canonical[key];
    if(!await page.getByRole('button',{name:'Close project navigation',exact:true}).isVisible())await page.getByRole('button',{name:'Explore project',exact:true}).click();
    await page.getByLabel('Search project',{exact:true}).fill(sourceMap.get(id).title);
    await page.locator(`[data-navigation-node="${id}"]`).click();
    await page.getByRole('button',{name:'Close project navigation',exact:true}).click();
    assert.equal(await card(id).getByRole('button',{name:/^Expand /}).count(),1,`${key} starts closed`);
    const beforeCamera=await camera();
    await card(id).dblclick({position:{x:38,y:40}});
    assert.equal(page.url(),initialUrl);assert.equal(await page.getByLabel('Choose canvas',{exact:true}).inputValue(),initialCanvas);
    assert.equal(await camera(),beforeCamera,`${key} disclosure keeps its camera`);
    const expected=genealogy.persons.filter(person=>person.parents.includes(key));
    const children=project.nodes.filter(node=>node.parentId===id);
    const visibleIds=new Set(await page.locator('.ms-node').evaluateAll(nodes=>nodes.map(node=>node.dataset.nodeId)));
    const missing=expected.filter(person=>!children.some(node=>(node.id===canonical[person.key]||node.targetId===canonical[person.key])&&visibleIds.has(node.id)));
    assert.deepEqual(missing.map(person=>person.key),[],`${key} reveals every selected child`);
    const selectedLinks=project.links.filter(link=>link.parentId===id&&link.sourceId===id);
    for(const link of selectedLinks)assert.ok(await page.locator(`g.ms-link[data-link-id="${link.id}"]`).count()>0,`Visible owned link ${link.id}`);
    const boxes=await rectangles(),overlaps=collisions(boxes.filter(box=>inGenealogy(box.id)));
    assert.deepEqual(overlaps,[],`${key} reveals genealogy tiles without overlaps`);
    const review={key,selectedChildren:expected.length,visibleOwnedLinks:selectedLinks.length,visibleNodes:boxes.length,overlaps,viewportUnchanged:true};
    reviews.push(review);
    await page.screenshot({path:`output/branch-atlas/genealogy-final-${key}-detail.png`});
    if(key==='zeus'){
      await page.getByRole('button',{name:'Fit canvas',exact:true}).click();
      await page.screenshot({path:'output/branch-atlas/genealogy-final-zeus-overview.png'});
      await card(id).getByRole('button',{name:/^Collapse /}).click();
      for(const link of selectedLinks)assert.equal(await page.locator(`g.ms-link[data-link-id="${link.id}"]`).count(),0,`Closed parent hides link ${link.id}`);
      review.recollapseHidesOwnedLinks=true;
    }
  }
  assert.deepEqual(errors,[]);
  await fs.writeFile('output/branch-atlas/genealogy-final-browser-review.json',JSON.stringify({input,errors,reviews},null,2));
  console.log(JSON.stringify({errors,reviews},null,2));
}finally{await browser.close();}
