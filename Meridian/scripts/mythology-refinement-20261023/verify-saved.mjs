import {mkdir,writeFile} from 'node:fs/promises';
import path from 'node:path';
import {chromium} from '@playwright/test';
const ids=process.argv.slice(2);
if(!ids.length)throw new Error('Pass saved project IDs to verify in the installed application.');
const root=path.resolve('output/mythology-refinement-20261023/installed-saved');await mkdir(root,{recursive:true});
const browser=await chromium.launch({headless:true});
const report={recordedAt:new Date().toISOString(),source:'Live installed Meridian application at 127.0.0.1:5240',projects:[],browserErrors:[]};
try{
 const page=await browser.newPage({viewport:{width:1760,height:1100},deviceScaleFactor:1});
 page.on('pageerror',e=>report.browserErrors.push(e.message));
 for(const id of ids){
  const response=await fetch('http://127.0.0.1:5240/api/projects/'+id);if(!response.ok)throw new Error('Saved project unavailable '+id);
  const project=await response.json();
  await page.goto('http://127.0.0.1:5240/?project='+id,{waitUntil:'domcontentloaded',timeout:120000});
  await page.locator('.ms-world').waitFor({timeout:120000});
  await page.evaluate(()=>document.fonts.ready);
  await page.locator('img').evaluateAll(images=>Promise.all(images.map(img=>img.decode().catch(()=>{}))));
  await page.evaluate(()=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve))));
  const screenshot=id+'.png';await page.screenshot({path:path.join(root,screenshot),timeout:60000});
  const metrics=await page.locator('.ms-node').evaluateAll(nodes=>nodes.map(n=>({id:n.dataset.nodeId,title:n.querySelector('.ms-node-title')?.textContent,rect:{width:n.getBoundingClientRect().width,height:n.getBoundingClientRect().height},images:[...n.querySelectorAll('img')].map(i=>({loaded:i.complete&&i.naturalWidth>0,width:i.naturalWidth,height:i.naturalHeight}))})));
  report.projects.push({id,title:project.title,revision:project.revision,works:project.canvases.length,entryWork:project.entryCanvasId,screenshot,visible:metrics.length,metrics});
  await writeFile(path.join(root,'verification.json'),JSON.stringify(report,null,2));
  console.log(JSON.stringify({id,revision:project.revision,works:project.canvases.length,visible:metrics.length,brokenImages:metrics.flatMap(m=>m.images).filter(i=>!i.loaded).length}));
 }
 if(report.browserErrors.length)throw new Error('Installed application reported browser errors.');
}finally{await browser.close();await writeFile(path.join(root,'verification.json'),JSON.stringify(report,null,2));}
